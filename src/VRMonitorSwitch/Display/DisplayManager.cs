using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using VRMonitorSwitch.Core;
using static VRMonitorSwitch.Display.DisplayNative;

namespace VRMonitorSwitch.Display
{
    /// <summary>One display target known to Windows (active or merely connected).</summary>
    public sealed class DisplayInfo
    {
        /// <summary>GDI device name used by ChangeDisplaySettingsEx, e.g. @"\\.\DISPLAY3". May be empty for inactive targets.</summary>
        public string DeviceName { get; set; }
        /// <summary>Adapter description, e.g. "AMD Radeon(TM) 890M Graphics" / "Parsec Virtual Display Adapter".</summary>
        public string AdapterString { get; set; }
        /// <summary>Friendly monitor name from DISPLAYCONFIG_TARGET_DEVICE_NAME (may be empty).</summary>
        public string MonitorName { get; set; }
        public string MonitorDevicePath { get; set; }
        public bool IsActive { get; set; }
        public bool IsPrimary { get; set; }
        /// <summary>outputTechnology INTERNAL (0x80000000), DISPLAYPORT_EMBEDDED (11) or UDI_EMBEDDED (13).</summary>
        public bool IsInternal { get; set; }
        /// <summary>AdapterString == ParsecVdd.AdapterName || MonitorName == "ParsecVDA" || device path contains "PSCCDD0".</summary>
        public bool IsParsecVirtual { get; set; }
        public Rectangle Bounds { get; set; }
        /// <summary>Current GDI mode; null if inactive.</summary>
        public DisplayMode CurrentMode { get; set; }
        /// <summary>LUID packed (HighPart &lt;&lt; 32 | LowPart).</summary>
        public long AdapterIdValue { get; set; }
        public uint TargetId { get; set; }
        public uint SourceId { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} [{1}] monitor='{2}' active={3} primary={4} internal={5} parsec={6} bounds={7} mode={8} adapter=0x{9:X} target={10} source={11} path='{12}'",
                DeviceName, AdapterString, MonitorName, IsActive, IsPrimary, IsInternal, IsParsecVirtual,
                Bounds, CurrentMode == null ? "-" : CurrentMode.ToString(), AdapterIdValue, TargetId, SourceId, MonitorDevicePath);
        }
    }

    /// <summary>Opaque copy of the active DISPLAYCONFIG paths/modes, used to restore the pre-session layout.</summary>
    public sealed class DisplayConfigSnapshot
    {
        internal DISPLAYCONFIG_PATH_INFO[] Paths { get; private set; }
        internal DISPLAYCONFIG_MODE_INFO[] Modes { get; private set; }

        public DateTime TakenAt { get; private set; }
        public int PathCount { get { return Paths == null ? 0 : Paths.Length; } }

        internal DisplayConfigSnapshot(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
        {
            Paths = paths ?? new DISPLAYCONFIG_PATH_INFO[0];
            Modes = modes ?? new DISPLAYCONFIG_MODE_INFO[0];
            TakenAt = DateTime.Now;
        }
    }

    public enum TopologyKind { Internal, Clone, Extend, External }

    /// <summary>
    /// Win32 display layer: enumeration (GDI + CCD), mode changes, arrangement, snapshot/restore and
    /// enabling/disabling the laptop's built-in panel. All methods are synchronous and thread-agnostic.
    /// </summary>
    public static class DisplayManager
    {
        private const string ParsecMonitorName = "ParsecVDA";
        private const string ParsecEdidId = "PSCCDD0";

        private const uint ApplyFlagsWithDb = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE;
        private const uint ApplyFlagsNoDb = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;
        private const uint ValidateFlags = SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES;

        // ------------------------------------------------------------------------------------
        // Enumeration
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Lists every display target. With <paramref name="includeInactive"/> the connected-but-inactive
        /// targets are reported too (IsActive=false, CurrentMode=null). Targets whose
        /// targetAvailable flag is false are never reported.
        /// </summary>
        public static List<DisplayInfo> GetDisplays(bool includeInactive)
        {
            DISPLAYCONFIG_PATH_INFO[] paths;
            DISPLAYCONFIG_MODE_INFO[] modes;
            QueryDisplayConfigChecked(includeInactive ? QDC_ALL_PATHS : QDC_ONLY_ACTIVE_PATHS, out paths, out modes);

            Dictionary<string, GdiAdapter> gdiAdapters = EnumerateGdiAdapters();

            // One DisplayInfo per (adapterId, targetId). With QDC_ALL_PATHS every source x target
            // combination is listed; active paths come first, and we prefer an active path over an inactive one.
            var byTarget = new Dictionary<string, DisplayInfo>(StringComparer.Ordinal);
            var order = new List<string>();

            for (int i = 0; i < paths.Length; i++)
            {
                DISPLAYCONFIG_PATH_INFO path = paths[i];
                if (!path.targetInfo.targetAvailable)
                    continue;

                bool active = path.IsActive;
                string key = TargetKey(path.targetInfo.adapterId, path.targetInfo.id);

                DisplayInfo existing;
                if (byTarget.TryGetValue(key, out existing) && (existing.IsActive || !active))
                    continue;

                DisplayInfo info;
                try
                {
                    info = BuildDisplayInfo(path, modes, gdiAdapters);
                }
                catch (Exception ex)
                {
                    Log.Error("DisplayManager.GetDisplays: failed to describe path " + i + " (target " + key + ")", ex);
                    continue;
                }

                if (existing == null)
                    order.Add(key);
                byTarget[key] = info;
            }

            var result = new List<DisplayInfo>(order.Count);
            foreach (string key in order)
            {
                DisplayInfo info = byTarget[key];
                if (!includeInactive && !info.IsActive)
                    continue;
                result.Add(info);
            }

            result = result
                .OrderByDescending(d => d.IsActive)
                .ThenBy(d => d.Bounds.X)
                .ThenBy(d => d.Bounds.Y)
                .ThenBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log.Info("DisplayManager.GetDisplays(includeInactive=" + includeInactive + "): " + result.Count + " display(s)");
            foreach (DisplayInfo d in result)
                Log.Info("  " + d);
            return result;
        }

        /// <summary>Returns the built-in panel (active preferred) or null on a desktop PC.</summary>
        public static DisplayInfo FindInternal(List<DisplayInfo> displays)
        {
            if (displays == null)
                return null;
            return displays.FirstOrDefault(d => d.IsInternal && d.IsActive)
                ?? displays.FirstOrDefault(d => d.IsInternal);
        }

        /// <summary>Polls every 250 ms until at least <paramref name="expectedCount"/> active Parsec displays exist.</summary>
        public static List<DisplayInfo> WaitForParsecDisplays(int expectedCount, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            int lastCount = -1;
            int lastInactive = -1;
            bool extendTried = false;
            while (true)
            {
                List<DisplayInfo> all;
                try
                {
                    all = GetDisplays(true);
                }
                catch (Exception ex)
                {
                    Log.Warn("DisplayManager.WaitForParsecDisplays: enumeration failed, retrying: " + ex.Message);
                    all = new List<DisplayInfo>();
                }

                List<DisplayInfo> parsec = all.Where(d => d.IsParsecVirtual && d.IsActive).ToList();
                int inactive = all.Count(d => d.IsParsecVirtual && !d.IsActive);

                if (parsec.Count != lastCount || inactive != lastInactive)
                {
                    Log.Info("DisplayManager.WaitForParsecDisplays: " + parsec.Count + "/" + expectedCount +
                             " active Parsec display(s)" + (inactive > 0 ? ", " + inactive + " connected but inactive" : string.Empty));
                    lastCount = parsec.Count;
                    lastInactive = inactive;
                }

                if (parsec.Count >= expectedCount)
                    return parsec.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();

                // Windows (or a saved connectivity entry) may leave the new targets connected but inactive, e.g. when
                // another virtual display such as Virtual Desktop's own monitor is present. The "extend" topology
                // activates every available target, so try it once as soon as an inactive Parsec target shows up.
                if (inactive > 0 && !extendTried)
                {
                    extendTried = true;
                    Log.Warn("DisplayManager.WaitForParsecDisplays: " + inactive + " Parsec target(s) are connected but inactive; applying the Extend topology.");
                    try
                    {
                        SetTopology(TopologyKind.Extend);
                        Thread.Sleep(1000);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("DisplayManager.WaitForParsecDisplays: SetTopology(Extend) failed: " + ex.Message);
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    foreach (DisplayInfo d in all)
                        Log.Warn("  At timeout: " + d);
                    var tex = new TimeoutException(Msg("err.display.parsecTimeout",
                        parsec.Count + "/" + expectedCount + " after " + (int)timeout.TotalMilliseconds + " ms" +
                        (inactive > 0 ? ", " + inactive + " inactive" : string.Empty)));
                    Log.Error("DisplayManager.WaitForParsecDisplays timed out", tex);
                    throw tex;
                }

                Thread.Sleep(250);
            }
        }

        /// <summary>All 32-bpp modes reported by EnumDisplaySettingsEx for the device, distinct, sorted descending.</summary>
        public static List<DisplayMode> GetSupportedModes(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                throw new ArgumentNullException("deviceName");

            var set = new HashSet<string>(StringComparer.Ordinal);
            var modes = new List<DisplayMode>();
            int index = 0;
            while (true)
            {
                DEVMODE dm = DEVMODE.Create();
                if (!EnumDisplaySettingsEx(deviceName, index, ref dm, 0))
                    break;
                index++;

                if (dm.dmBitsPerPel != 32 || dm.dmPelsWidth == 0 || dm.dmPelsHeight == 0)
                    continue;

                int hz = (int)dm.dmDisplayFrequency;
                if (hz <= 1) // 0/1 = "hardware default"
                    continue;

                string key = dm.dmPelsWidth + "x" + dm.dmPelsHeight + "@" + hz;
                if (set.Add(key))
                    modes.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, hz));
            }

            modes = modes
                .OrderByDescending(m => m.Width)
                .ThenByDescending(m => m.Height)
                .ThenByDescending(m => m.Hz)
                .ToList();

            Log.Info("DisplayManager.GetSupportedModes(" + deviceName + "): " + modes.Count + " mode(s) from " + index + " entries");
            return modes;
        }

        // ------------------------------------------------------------------------------------
        // Mode / arrangement
        // ------------------------------------------------------------------------------------

        /// <summary>ChangeDisplaySettingsEx(CDS_UPDATEREGISTRY) with the requested size and refresh rate; throws with the DISP_CHANGE_* text.</summary>
        public static void SetMode(string deviceName, DisplayMode mode)
        {
            if (string.IsNullOrEmpty(deviceName))
                throw new ArgumentNullException("deviceName");
            if (mode == null)
                throw new ArgumentNullException("mode");

            DEVMODE dm = DEVMODE.Create();
            dm.dmPelsWidth = (uint)mode.Width;
            dm.dmPelsHeight = (uint)mode.Height;
            dm.dmBitsPerPel = 32;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
            if (mode.Hz > 0)
            {
                dm.dmDisplayFrequency = (uint)mode.Hz;
                dm.dmFields |= DM_DISPLAYFREQUENCY;
            }

            Log.Info("DisplayManager.SetMode(" + deviceName + ", " + mode + ")");
            int result = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (result != DISP_CHANGE_SUCCESSFUL)
            {
                var ex = new InvalidOperationException(Msg("err.display.modeChange",
                    deviceName + " -> " + mode + ": " + DispChangeToString(result)));
                Log.Error("DisplayManager.SetMode failed", ex);
                throw ex;
            }
            Log.Info("DisplayManager.SetMode: " + DispChangeToString(result));
        }

        /// <summary>
        /// Places the listed devices side by side (left to right, top aligned) with the primary at (0,0),
        /// then appends every other attached display to the far right, and commits the change.
        /// </summary>
        public static void ArrangeHorizontally(IList<string> deviceNamesLeftToRight, string primaryDeviceName)
        {
            if (deviceNamesLeftToRight == null || deviceNamesLeftToRight.Count == 0)
                throw new ArgumentException("deviceNamesLeftToRight must contain at least one device", "deviceNamesLeftToRight");

            var ordered = new List<string>();
            foreach (string name in deviceNamesLeftToRight)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!ordered.Contains(name, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(name);
            }

            // Every other attached display goes to the right, in its current left-to-right order.
            var others = new List<KeyValuePair<string, int>>();
            foreach (GdiAdapter adapter in EnumerateGdiAdapters().Values)
            {
                if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                    continue;
                if (ordered.Contains(adapter.DeviceName, StringComparer.OrdinalIgnoreCase))
                    continue;
                DEVMODE cur;
                int x = TryGetCurrentDevMode(adapter.DeviceName, out cur) ? cur.dmPosition.x : int.MaxValue;
                others.Add(new KeyValuePair<string, int>(adapter.DeviceName, x));
            }
            foreach (var kv in others.OrderBy(k => k.Value))
                ordered.Add(kv.Key);

            if (string.IsNullOrEmpty(primaryDeviceName) || !ordered.Contains(primaryDeviceName, StringComparer.OrdinalIgnoreCase))
            {
                Log.Warn("DisplayManager.ArrangeHorizontally: primary '" + primaryDeviceName + "' not in list, using '" + ordered[0] + "'");
                primaryDeviceName = ordered[0];
            }

            // Compute x offsets from current widths, then shift so that the primary sits at (0,0).
            var widths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in ordered)
            {
                DEVMODE cur;
                int width = 0;
                if (TryGetCurrentDevMode(name, out cur))
                    width = (int)cur.dmPelsWidth;
                if (width <= 0)
                {
                    DEVMODE reg = DEVMODE.Create();
                    if (EnumDisplaySettingsEx(name, ENUM_REGISTRY_SETTINGS, ref reg, 0))
                        width = (int)reg.dmPelsWidth;
                }
                if (width <= 0)
                {
                    Log.Warn("DisplayManager.ArrangeHorizontally: unknown width for " + name + ", assuming 1920");
                    width = 1920;
                }
                widths[name] = width;
            }

            int primaryX = 0;
            int cursor = 0;
            var xs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in ordered)
            {
                xs[name] = cursor;
                if (string.Equals(name, primaryDeviceName, StringComparison.OrdinalIgnoreCase))
                    primaryX = cursor;
                cursor += widths[name];
            }

            Log.Info("DisplayManager.ArrangeHorizontally: " + string.Join(", ", ordered.Select(n => n + "@" + (xs[n] - primaryX))) + " primary=" + primaryDeviceName);

            // Primary first (CDS_SET_PRIMARY must be applied at 0,0 before the others reference it).
            var applyOrder = new List<string>();
            applyOrder.Add(primaryDeviceName);
            applyOrder.AddRange(ordered.Where(n => !string.Equals(n, primaryDeviceName, StringComparison.OrdinalIgnoreCase)));

            foreach (string name in applyOrder)
            {
                bool isPrimary = string.Equals(name, primaryDeviceName, StringComparison.OrdinalIgnoreCase);
                DEVMODE dm = DEVMODE.Create();
                dm.dmFields = DM_POSITION;
                dm.dmPosition = new POINTL(xs[name] - primaryX, 0);

                uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
                if (isPrimary)
                    flags |= CDS_SET_PRIMARY;

                int result = ChangeDisplaySettingsEx(name, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
                if (result != DISP_CHANGE_SUCCESSFUL)
                {
                    var ex = new InvalidOperationException(Msg("err.display.arrange",
                        name + " @ (" + dm.dmPosition.x + ",0)" + (isPrimary ? " primary" : "") + ": " + DispChangeToString(result)));
                    Log.Error("DisplayManager.ArrangeHorizontally failed", ex);
                    throw ex;
                }
            }

            int commit = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            if (commit != DISP_CHANGE_SUCCESSFUL)
            {
                var ex = new InvalidOperationException(Msg("err.display.arrange", "commit: " + DispChangeToString(commit)));
                Log.Error("DisplayManager.ArrangeHorizontally commit failed", ex);
                throw ex;
            }
            Log.Info("DisplayManager.ArrangeHorizontally: committed");
        }

        // ------------------------------------------------------------------------------------
        // Snapshot / restore / topology
        // ------------------------------------------------------------------------------------

        /// <summary>QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS) captured for a later <see cref="Restore"/>.</summary>
        public static DisplayConfigSnapshot Snapshot()
        {
            DISPLAYCONFIG_PATH_INFO[] paths;
            DISPLAYCONFIG_MODE_INFO[] modes;
            QueryDisplayConfigChecked(QDC_ONLY_ACTIVE_PATHS, out paths, out modes);
            var snap = new DisplayConfigSnapshot(paths, modes);
            Log.Info("DisplayManager.Snapshot: " + paths.Length + " active path(s), " + modes.Length + " mode(s)");
            return snap;
        }

        /// <summary>
        /// Re-applies a snapshot. When no Parsec target is connected any more it tries
        /// SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_ALLOW_CHANGES|SDC_SAVE_TO_DATABASE first; then (or directly,
        /// while Parsec targets are still enumerated) the same without SAVE_TO_DATABASE, and as a last resort
        /// <see cref="SetTopology"/>(Extend). The database is never written while a Parsec target is connected:
        /// SDC_USE_SUPPLIED_DISPLAY_CONFIG deactivates every target missing from the array, and persisting that for
        /// the {physical + Parsec} set would make Windows bring the virtual displays up inactive on the next session.
        /// </summary>
        public static void Restore(DisplayConfigSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");

            Log.Info("DisplayManager.Restore: " + snapshot.PathCount + " path(s) taken at " + snapshot.TakenAt.ToString("s"));

            // Copy so a failed attempt cannot mutate the caller's snapshot.
            var paths = (DISPLAYCONFIG_PATH_INFO[])snapshot.Paths.Clone();
            var modes = (DISPLAYCONFIG_MODE_INFO[])snapshot.Modes.Clone();

            int rc;
            if (AnyParsecTargetConnected())
            {
                Log.Warn("DisplayManager.Restore: Parsec target(s) still connected; applying without SDC_SAVE_TO_DATABASE");
            }
            else
            {
                rc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlagsWithDb);
                if (rc == ERROR_SUCCESS)
                {
                    Log.Info("DisplayManager.Restore: applied (saved to database)");
                    return;
                }
                Log.Warn("DisplayManager.Restore: SetDisplayConfig with SAVE_TO_DATABASE failed: " + Describe(rc) + "; retrying without database save");
            }

            rc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlagsNoDb);
            if (rc == ERROR_SUCCESS)
            {
                Log.Info("DisplayManager.Restore: applied (not saved to database)");
                return;
            }
            Log.Warn("DisplayManager.Restore: SetDisplayConfig without SAVE_TO_DATABASE failed: " + Describe(rc) + "; falling back to SetTopology(Extend)");

            SetTopology(TopologyKind.Extend);
        }

        /// <summary>True when at least one Parsec target is connected (active or not). On enumeration failure assumes true (safer: no database write).</summary>
        private static bool AnyParsecTargetConnected()
        {
            try
            {
                return GetDisplays(true).Any(d => d.IsParsecVirtual);
            }
            catch (Exception ex)
            {
                Log.Warn("DisplayManager.AnyParsecTargetConnected: enumeration failed, assuming a Parsec target is present: " + ex.Message);
                return true;
            }
        }

        /// <summary>SetDisplayConfig(NULL, NULL, SDC_APPLY | SDC_TOPOLOGY_*).</summary>
        public static void SetTopology(TopologyKind kind)
        {
            uint flag;
            switch (kind)
            {
                case TopologyKind.Internal: flag = SDC_TOPOLOGY_INTERNAL; break;
                case TopologyKind.Clone: flag = SDC_TOPOLOGY_CLONE; break;
                case TopologyKind.Extend: flag = SDC_TOPOLOGY_EXTEND; break;
                case TopologyKind.External: flag = SDC_TOPOLOGY_EXTERNAL; break;
                default: throw new ArgumentOutOfRangeException("kind");
            }

            Log.Info("DisplayManager.SetTopology(" + kind + ")");
            int rc = SetDisplayConfig(0, null, 0, null, SDC_APPLY | flag);
            if (rc != ERROR_SUCCESS)
            {
                var ex = new InvalidOperationException(Msg("err.display.setConfigFailed", "SetTopology(" + kind + "): " + Describe(rc)));
                Log.Error("DisplayManager.SetTopology failed", ex);
                throw ex;
            }
        }

        // ------------------------------------------------------------------------------------
        // Internal display on/off
        // ------------------------------------------------------------------------------------

        /// <summary>Turns the laptop's built-in panel off (false) or on (true). See ARCHITECTURE.md for the algorithm.</summary>
        public static void SetInternalDisplayEnabled(bool enabled)
        {
            Log.Info("DisplayManager.SetInternalDisplayEnabled(" + enabled + ")");

            DISPLAYCONFIG_PATH_INFO[] paths;
            DISPLAYCONFIG_MODE_INFO[] modes;
            QueryDisplayConfigChecked(QDC_ALL_PATHS, out paths, out modes);

            // Step 2: locate the internal/embedded target(s).
            var internalPathIdx = new List<int>();
            var techCache = new Dictionary<string, DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                if (!paths[i].targetInfo.targetAvailable)
                    continue;
                string key = TargetKey(paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
                DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech;
                if (!techCache.TryGetValue(key, out tech))
                {
                    tech = paths[i].targetInfo.outputTechnology;
                    DISPLAYCONFIG_TARGET_DEVICE_NAME tn;
                    if (TryGetTargetName(paths[i].targetInfo.adapterId, paths[i].targetInfo.id, out tn))
                        tech = tn.outputTechnology;
                    techCache[key] = tech;
                }
                if (IsInternalOutputTechnology(tech))
                    internalPathIdx.Add(i);
            }

            if (internalPathIdx.Count == 0)
            {
                var ex = new InvalidOperationException(Msg("err.internalNotFound", null));
                Log.Error("DisplayManager.SetInternalDisplayEnabled: no internal display", ex);
                throw ex;
            }

            if (!enabled)
                DisableInternal(paths, modes, internalPathIdx);
            else
                EnableInternal(paths, modes, internalPathIdx);
        }

        private static void DisableInternal(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, List<int> internalPathIdx)
        {
            var internalSet = new HashSet<int>(internalPathIdx);

            // Step 3: at least one other active, available path must remain.
            int otherActive = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                if (internalSet.Contains(i))
                    continue;
                if (paths[i].IsActive && paths[i].targetInfo.targetAvailable)
                    otherActive++;
            }
            if (otherActive == 0)
            {
                var ex = new InvalidOperationException(Msg("err.display.noOtherDisplay", null));
                Log.Error("DisplayManager.SetInternalDisplayEnabled(false): refusing, no other active display", ex);
                throw ex;
            }

            bool anyInternalActive = internalPathIdx.Any(i => paths[i].IsActive);
            if (!anyInternalActive)
            {
                Log.Info("DisplayManager.SetInternalDisplayEnabled(false): internal display is already inactive");
                return;
            }

            // Was the internal display primary (source at 0,0)?
            bool internalWasPrimary = false;
            foreach (int i in internalPathIdx)
            {
                if (!paths[i].IsActive)
                    continue;
                int sIdx = (int)paths[i].sourceInfo.modeInfoIdx;
                if (paths[i].sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && sIdx >= 0 && sIdx < modes.Length
                    && modes[sIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE
                    && modes[sIdx].sourceMode.position.x == 0 && modes[sIdx].sourceMode.position.y == 0)
                {
                    internalWasPrimary = true;
                }
            }

            // Deactivate the internal path(s).
            foreach (int i in internalPathIdx)
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }

            // Shift the remaining active sources so the leftmost one lands on (0,0).
            if (internalWasPrimary)
            {
                var remainingSourceModes = new HashSet<int>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!paths[i].IsActive)
                        continue;
                    uint idx = paths[i].sourceInfo.modeInfoIdx;
                    if (idx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && idx < (uint)modes.Length
                        && modes[(int)idx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE)
                        remainingSourceModes.Add((int)idx);
                }

                if (remainingSourceModes.Count > 0)
                {
                    int dx = int.MaxValue, dy = 0;
                    foreach (int idx in remainingSourceModes)
                    {
                        POINTL p = modes[idx].sourceMode.position;
                        if (p.x < dx || (p.x == dx && p.y < dy))
                        {
                            dx = p.x;
                            dy = p.y;
                        }
                    }
                    if (dx != 0 || dy != 0)
                    {
                        Log.Info("DisplayManager.SetInternalDisplayEnabled(false): internal was primary, shifting remaining sources by (" + (-dx) + "," + (-dy) + ")");
                        foreach (int idx in remainingSourceModes)
                        {
                            modes[idx].sourceMode.position.x -= dx;
                            modes[idx].sourceMode.position.y -= dy;
                        }
                    }
                }
            }

            int rc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlagsWithDb);
            if (rc == ERROR_SUCCESS)
            {
                Log.Info("DisplayManager.SetInternalDisplayEnabled(false): applied");
                return;
            }
            Log.Warn("DisplayManager.SetInternalDisplayEnabled(false): SetDisplayConfig failed: " + Describe(rc));

            // Fallback chain: validate (for the log), apply without DB save, then the EXTERNAL topology.
            int vrc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ValidateFlags);
            Log.Warn("DisplayManager.SetInternalDisplayEnabled(false): SDC_VALIDATE -> " + Describe(vrc));

            rc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlagsNoDb);
            if (rc == ERROR_SUCCESS)
            {
                Log.Info("DisplayManager.SetInternalDisplayEnabled(false): applied without database save");
                return;
            }
            Log.Warn("DisplayManager.SetInternalDisplayEnabled(false): retry without SAVE_TO_DATABASE failed: " + Describe(rc) + "; falling back to SetTopology(External)");

            SetTopology(TopologyKind.External);
        }

        private static void EnableInternal(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes, List<int> internalPathIdx)
        {
            if (internalPathIdx.Any(i => paths[i].IsActive))
            {
                Log.Info("DisplayManager.SetInternalDisplayEnabled(true): internal display is already active");
                return;
            }

            // Sources already driving an active target on the same adapter must not be reused (that would clone).
            var busySources = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].IsActive)
                    busySources.Add(TargetKey(paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id));
            }

            int chosen = -1;
            foreach (int i in internalPathIdx)
            {
                if (!busySources.Contains(TargetKey(paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id)))
                {
                    chosen = i;
                    break;
                }
            }
            if (chosen < 0)
            {
                chosen = internalPathIdx[0];
                Log.Warn("DisplayManager.SetInternalDisplayEnabled(true): every candidate source is in use, using path " + chosen);
            }

            Log.Info("DisplayManager.SetInternalDisplayEnabled(true): activating path " + chosen
                + " (source " + paths[chosen].sourceInfo.id + ", target " + paths[chosen].targetInfo.id + ")");

            paths[chosen].flags |= DISPLAYCONFIG_PATH_ACTIVE;
            paths[chosen].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            paths[chosen].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;

            int rc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlagsWithDb);
            if (rc == ERROR_SUCCESS)
            {
                Log.Info("DisplayManager.SetInternalDisplayEnabled(true): applied");
                return;
            }
            Log.Warn("DisplayManager.SetInternalDisplayEnabled(true): SetDisplayConfig failed: " + Describe(rc));

            int vrc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ValidateFlags);
            Log.Warn("DisplayManager.SetInternalDisplayEnabled(true): SDC_VALIDATE -> " + Describe(vrc));

            rc = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlagsNoDb);
            if (rc == ERROR_SUCCESS)
            {
                Log.Info("DisplayManager.SetInternalDisplayEnabled(true): applied without database save");
                return;
            }
            Log.Warn("DisplayManager.SetInternalDisplayEnabled(true): retry without SAVE_TO_DATABASE failed: " + Describe(rc) + "; falling back to SetTopology(Extend)");

            SetTopology(TopologyKind.Extend);
        }

        // ------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------

        private sealed class GdiAdapter
        {
            public string DeviceName;
            public string DeviceString;
            public uint StateFlags;
            public string MonitorString;   // child monitor DeviceString (fallback for friendly name)
            public string MonitorDeviceId; // child monitor interface path (fallback for MonitorDevicePath)
        }

        /// <summary>EnumDisplayDevices for every adapter (\\.\DISPLAYn) plus its first child monitor.</summary>
        private static Dictionary<string, GdiAdapter> EnumerateGdiAdapters()
        {
            var map = new Dictionary<string, GdiAdapter>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; ; i++)
            {
                DISPLAY_DEVICE dd = DISPLAY_DEVICE.Create();
                if (!EnumDisplayDevices(null, i, ref dd, 0))
                    break;
                if (string.IsNullOrEmpty(dd.DeviceName))
                    continue;
                if ((dd.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0)
                    continue;

                var a = new GdiAdapter();
                a.DeviceName = dd.DeviceName;
                a.DeviceString = dd.DeviceString ?? string.Empty;
                a.StateFlags = dd.StateFlags;

                DISPLAY_DEVICE mon = DISPLAY_DEVICE.Create();
                if (EnumDisplayDevices(dd.DeviceName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    a.MonitorString = mon.DeviceString ?? string.Empty;
                    a.MonitorDeviceId = mon.DeviceID ?? string.Empty;
                }
                else
                {
                    a.MonitorString = string.Empty;
                    a.MonitorDeviceId = string.Empty;
                }
                map[a.DeviceName] = a;
            }
            return map;
        }

        private static DisplayInfo BuildDisplayInfo(DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes, Dictionary<string, GdiAdapter> gdi)
        {
            var info = new DisplayInfo();
            info.IsActive = path.IsActive;
            info.AdapterIdValue = path.targetInfo.adapterId.ToInt64();
            info.TargetId = path.targetInfo.id;
            info.SourceId = path.sourceInfo.id;
            info.DeviceName = string.Empty;
            info.AdapterString = string.Empty;
            info.MonitorName = string.Empty;
            info.MonitorDevicePath = string.Empty;

            // Source -> GDI device name.
            DISPLAYCONFIG_SOURCE_DEVICE_NAME sn;
            if (TryGetSourceName(path.sourceInfo.adapterId, path.sourceInfo.id, out sn))
                info.DeviceName = sn.viewGdiDeviceName ?? string.Empty;

            // Target -> friendly name / device path / output technology.
            DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech = path.targetInfo.outputTechnology;
            DISPLAYCONFIG_TARGET_DEVICE_NAME tn;
            if (TryGetTargetName(path.targetInfo.adapterId, path.targetInfo.id, out tn))
            {
                info.MonitorName = tn.monitorFriendlyDeviceName ?? string.Empty;
                info.MonitorDevicePath = tn.monitorDevicePath ?? string.Empty;
                tech = tn.outputTechnology;
            }
            info.IsInternal = IsInternalOutputTechnology(tech);

            GdiAdapter adapter = null;
            if (!string.IsNullOrEmpty(info.DeviceName))
                gdi.TryGetValue(info.DeviceName, out adapter);

            if (adapter != null)
            {
                info.AdapterString = adapter.DeviceString;
                if (info.IsActive)
                    info.IsPrimary = (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                if (string.IsNullOrEmpty(info.MonitorDevicePath))
                    info.MonitorDevicePath = adapter.MonitorDeviceId;
                if (string.IsNullOrEmpty(info.MonitorName) && info.IsActive)
                    info.MonitorName = adapter.MonitorString;
            }

            // Current mode and bounds: GDI first (matches SetMode/GetSupportedModes), CCD source mode as fallback.
            if (info.IsActive)
            {
                int w = 0, h = 0, hz = 0, x = 0, y = 0;

                DEVMODE cur;
                if (!string.IsNullOrEmpty(info.DeviceName) && TryGetCurrentDevMode(info.DeviceName, out cur))
                {
                    w = (int)cur.dmPelsWidth;
                    h = (int)cur.dmPelsHeight;
                    hz = (int)cur.dmDisplayFrequency;
                    x = cur.dmPosition.x;
                    y = cur.dmPosition.y;
                }

                uint sIdx = path.sourceInfo.modeInfoIdx;
                if ((w == 0 || h == 0) && sIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && sIdx < (uint)modes.Length
                    && modes[(int)sIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE)
                {
                    DISPLAYCONFIG_SOURCE_MODE sm = modes[(int)sIdx].sourceMode;
                    w = (int)sm.width;
                    h = (int)sm.height;
                    x = sm.position.x;
                    y = sm.position.y;
                }

                if (hz <= 1)
                {
                    double rate = path.targetInfo.refreshRate.ToDouble();
                    uint tIdx = path.targetInfo.modeInfoIdx;
                    if (rate <= 0 && tIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && tIdx < (uint)modes.Length
                        && modes[(int)tIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.TARGET)
                        rate = modes[(int)tIdx].targetMode.targetVideoSignalInfo.vSyncFreq.ToDouble();
                    hz = (int)Math.Round(rate);
                }

                info.Bounds = new Rectangle(x, y, w, h);
                info.CurrentMode = (w > 0 && h > 0) ? new DisplayMode(w, h, hz) : null;
                if (adapter == null && x == 0 && y == 0)
                    info.IsPrimary = true;
            }

            info.IsParsecVirtual =
                string.Equals(info.AdapterString, VRMonitorSwitch.Vdd.ParsecVdd.AdapterName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(info.MonitorName, ParsecMonitorName, StringComparison.OrdinalIgnoreCase)
                || (info.MonitorDevicePath != null && info.MonitorDevicePath.IndexOf(ParsecEdidId, StringComparison.OrdinalIgnoreCase) >= 0);

            return info;
        }

        private static bool TryGetCurrentDevMode(string deviceName, out DEVMODE dm)
        {
            dm = DEVMODE.Create();
            return EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, ref dm, 0);
        }

        private static bool TryGetSourceName(LUID adapterId, uint sourceId, out DISPLAYCONFIG_SOURCE_DEVICE_NAME name)
        {
            name = DISPLAYCONFIG_SOURCE_DEVICE_NAME.Create(adapterId, sourceId);
            int rc = DisplayConfigGetDeviceInfo(ref name);
            if (rc != ERROR_SUCCESS)
            {
                Log.Warn("DisplayConfigGetDeviceInfo(GET_SOURCE_NAME, adapter=0x" + adapterId.ToInt64().ToString("X") + ", source=" + sourceId + ") -> " + Describe(rc));
                return false;
            }
            return true;
        }

        private static bool TryGetTargetName(LUID adapterId, uint targetId, out DISPLAYCONFIG_TARGET_DEVICE_NAME name)
        {
            name = DISPLAYCONFIG_TARGET_DEVICE_NAME.Create(adapterId, targetId);
            int rc = DisplayConfigGetDeviceInfo(ref name);
            if (rc != ERROR_SUCCESS)
            {
                Log.Warn("DisplayConfigGetDeviceInfo(GET_TARGET_NAME, adapter=0x" + adapterId.ToInt64().ToString("X") + ", target=" + targetId + ") -> " + Describe(rc));
                return false;
            }
            return true;
        }

        /// <summary>GetDisplayConfigBufferSizes + QueryDisplayConfig, retried when the configuration changes between the two calls.</summary>
        private static void QueryDisplayConfigChecked(uint flags, out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
        {
            int rc = ERROR_GEN_FAILURE;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint pathCount, modeCount;
                rc = GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount);
                if (rc != ERROR_SUCCESS)
                    break;

                var p = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var m = new DISPLAYCONFIG_MODE_INFO[modeCount];
                rc = QueryDisplayConfig(flags, ref pathCount, p, ref modeCount, m, IntPtr.Zero);
                if (rc == ERROR_INSUFFICIENT_BUFFER)
                {
                    Log.Warn("QueryDisplayConfig: configuration changed during query, retrying");
                    continue;
                }
                if (rc != ERROR_SUCCESS)
                    break;

                if (pathCount != (uint)p.Length)
                    Array.Resize(ref p, (int)pathCount);
                if (modeCount != (uint)m.Length)
                    Array.Resize(ref m, (int)modeCount);
                paths = p;
                modes = m;
                return;
            }

            var ex = new InvalidOperationException(Msg("err.display.queryFailed", "QueryDisplayConfig(0x" + flags.ToString("X") + "): " + Describe(rc)));
            Log.Error("DisplayManager: QueryDisplayConfig failed", ex);
            throw ex;
        }

        private static string TargetKey(LUID adapterId, uint id)
        {
            return adapterId.ToInt64().ToString("X") + ":" + id.ToString();
        }

        private static string Describe(int win32Code)
        {
            string text;
            try
            {
                text = new Win32Exception(win32Code).Message;
            }
            catch (Exception)
            {
                text = string.Empty;
            }
            return Win32ErrorToString(win32Code) + " (" + win32Code + (string.IsNullOrEmpty(text) ? "" : ": " + text) + ")";
        }

        /// <summary>Localized message plus an optional technical detail in parentheses.</summary>
        private static string Msg(string key, string detail)
        {
            string text = Strings.Get(key);
            if (string.IsNullOrEmpty(detail))
                return text;
            return text + " (" + detail + ")";
        }
    }
}
