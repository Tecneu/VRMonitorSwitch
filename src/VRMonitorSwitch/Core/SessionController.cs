using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VRMonitorSwitch.Display;
using VRMonitorSwitch.Vdd;

namespace VRMonitorSwitch.Core
{
    /// <summary>Lifecycle of a VR monitor session.</summary>
    public enum SessionState
    {
        /// <summary>Nothing applied; the real displays are untouched.</summary>
        Idle,
        /// <summary><see cref="SessionController.Apply"/> is running.</summary>
        Applying,
        /// <summary>Virtual monitors are up and kept alive.</summary>
        Active,
        /// <summary><see cref="SessionController.Restore"/> is running.</summary>
        Restoring,
        /// <summary>The last Apply failed. Everything was rolled back; a new Apply or Restore is allowed.</summary>
        Faulted
    }

    /// <summary>Outcome of <see cref="SessionController.RecoverInternalDisplay"/>.</summary>
    public enum RecoveryResult
    {
        /// <summary>The built-in display was already active; nothing was changed.</summary>
        AlreadyOn,
        /// <summary>The built-in display was connected but inactive and has been turned back on.</summary>
        Restored,
        /// <summary>This machine has no built-in display (desktop PC).</summary>
        NoInternalDisplay
    }

    /// <summary>Thrown by <see cref="SessionController.Apply"/> when the Parsec VDD is not <see cref="VddStatus.Ok"/>.</summary>
    public sealed class DriverNotReadyException : Exception
    {
        public VddStatus Status { get; }

        public DriverNotReadyException(VddStatus status)
            : base(Strings.Format("err.driverNotReady", Strings.DriverStatus(status)))
        {
            Status = status;
        }

        public DriverNotReadyException(VddStatus status, string message)
            : base(message)
        {
            Status = status;
        }
    }

    /// <summary>
    /// Orchestrates one session: snapshot → create virtual displays → set modes → arrange → (turn laptop
    /// screen off) and the exact reverse on <see cref="Restore"/>. Thread-safe; <see cref="Apply"/> is
    /// synchronous and meant to run on a worker thread. Events may be raised from that thread.
    /// </summary>
    public sealed class SessionController : IDisposable
    {
        private const int AddDisplayDelayMs = 400;
        /// <summary>How long RestoreCore waits for the removed Parsec targets to disappear from the CCD topology.</summary>
        private const int ParsecDepartTimeoutMs = 5000;
        private const int ParsecDepartPollMs = 250;

        private readonly AppSettings _settings;
        private readonly object _gate = new object();

        private volatile SessionState _state = SessionState.Idle;
        private ParsecVdd _vdd;
        private DisplayConfigSnapshot _snapshot;
        private DisplayInfo _internal;
        private bool _internalOff;
        private bool _disposed;
        /// <summary>Parsec targets (active or not) that existed before this session added its own.</summary>
        private int _parsecTargetsBefore;

        public SessionController(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
        }

        public SessionState State
        {
            get { return _state; }
        }

        public Profile ActiveProfile { get; private set; }

        public DisplayMode ActiveMode { get; private set; }

        /// <summary>True while the laptop's built-in display is turned off by this controller.</summary>
        public bool LaptopScreenOff
        {
            get { return _internalOff; }
        }

        /// <summary>Human readable, localized step messages. May be raised from a worker thread.</summary>
        public event Action<string> Progress;

        /// <summary>Raised on every state transition. May be raised from a worker thread.</summary>
        public event Action<SessionState> StateChanged;

        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Applies <paramref name="profile"/> at <paramref name="mode"/>. Synchronous: call from a worker thread.
        /// Throws <see cref="DriverNotReadyException"/> when the driver is not usable, or any other exception
        /// after rolling back everything that was changed.
        /// </summary>
        public void Apply(Profile profile, DisplayMode mode, bool turnOffLaptopScreen)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (mode == null) mode = profile.DefaultMode;
            if (_disposed) throw new ObjectDisposedException(nameof(SessionController));

            lock (_gate)
            {
                SessionState current = _state;
                if (current == SessionState.Applying || current == SessionState.Restoring)
                {
                    throw new InvalidOperationException(Strings.Get("session.busy"));
                }

                if (current == SessionState.Active)
                {
                    Report("session.alreadyActive");
                    RestoreCore(SessionState.Idle);
                }

                Log.Info("Apply: profile=" + profile.Id + " mode=" + mode.ToKey() + " laptopOff=" + turnOffLaptopScreen);
                SetState(SessionState.Applying);

                bool vddOpened = false;
                try
                {
                    // 1. Driver must be healthy.
                    Report("session.step.checkDriver");
                    VddStatus status = ParsecVdd.QueryStatus();
                    Log.Info("Driver status: " + status);
                    if (status != VddStatus.Ok)
                    {
                        throw new DriverNotReadyException(status);
                    }

                    // 2. Snapshot the current layout and locate the laptop screen.
                    Report("session.step.snapshot");
                    _snapshot = DisplayManager.Snapshot();
                    Log.Info("Snapshot taken: " + _snapshot.PathCount + " active path(s)");

                    List<DisplayInfo> before = DisplayManager.GetDisplays(false);
                    foreach (DisplayInfo d in before)
                    {
                        Log.Info("  Before: " + Describe(d));
                    }

                    _internal = DisplayManager.FindInternal(before);
                    if (_internal == null)
                    {
                        if (turnOffLaptopScreen)
                        {
                            Log.Warn("No internal display found; ignoring the request to turn the laptop screen off.");
                            Report("session.step.noInternal");
                        }
                        turnOffLaptopScreen = false;
                    }
                    else
                    {
                        Log.Info("Internal display: " + _internal.DeviceName);
                    }

                    var preExistingParsec = new HashSet<string>(
                        before.Where(d => d.IsParsecVirtual && !string.IsNullOrEmpty(d.DeviceName)).Select(d => d.DeviceName),
                        StringComparer.OrdinalIgnoreCase);
                    if (preExistingParsec.Count > 0)
                    {
                        Log.Warn("There are already " + preExistingParsec.Count + " active Parsec display(s) not owned by this session: " + string.Join(", ", preExistingParsec));
                    }
                    _parsecTargetsBefore = CountParsecTargets();
                    if (_parsecTargetsBefore > 0)
                    {
                        Log.Warn(_parsecTargetsBefore + " Parsec target(s) are connected before this session starts; they will not be waited for on restore.");
                    }

                    // 3. Open the driver and add the displays.
                    Report("session.step.openDriver");
                    _vdd = new ParsecVdd();
                    _vdd.KeepAliveIntervalMs = _settings.KeepAliveIntervalMs;
                    _vdd.Open();
                    vddOpened = true;
                    Log.Info("Parsec VDD opened, driver minor version " + _vdd.Version + ", keep-alive " + _vdd.KeepAliveIntervalMs + " ms");

                    for (int i = 0; i < profile.MonitorCount; i++)
                    {
                        if (i > 0)
                        {
                            Thread.Sleep(AddDisplayDelayMs);
                        }
                        Report("session.step.addDisplay", i + 1, profile.MonitorCount);
                        int index = _vdd.AddDisplay();
                        Log.Info("Added virtual display, driver index " + index);
                    }

                    Report("session.step.waitDisplays");
                    int expected = preExistingParsec.Count + profile.MonitorCount;
                    var timeout = TimeSpan.FromMilliseconds(_settings.DisplayAppearTimeoutMs);
                    List<DisplayInfo> parsecAll;
                    try
                    {
                        parsecAll = DisplayManager.WaitForParsecDisplays(expected, timeout);
                    }
                    catch (TimeoutException ex)
                    {
                        throw new TimeoutException(Strings.Get("err.noDisplays"), ex);
                    }

                    // New monitors come up cloned onto the primary screen by default; make sure they are extended.
                    parsecAll = EnsureExtendedTopology(parsecAll, expected, timeout);
                    _internal = DisplayManager.FindInternal(DisplayManager.GetDisplays(false)) ?? _internal;
                    before = DisplayManager.GetDisplays(false).Where(d => d.IsActive && !d.IsParsecVirtual).ToList();

                    List<DisplayInfo> newParsec = parsecAll
                        .Where(d => d.IsParsecVirtual && d.IsActive && !string.IsNullOrEmpty(d.DeviceName) && !preExistingParsec.Contains(d.DeviceName))
                        .OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (newParsec.Count < profile.MonitorCount)
                    {
                        Log.Warn("Expected " + profile.MonitorCount + " new Parsec display(s) but identified " + newParsec.Count + "; falling back to all active Parsec displays.");
                        newParsec = parsecAll
                            .Where(d => d.IsParsecVirtual && d.IsActive && !string.IsNullOrEmpty(d.DeviceName))
                            .OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    if (newParsec.Count == 0)
                    {
                        throw new InvalidOperationException(Strings.Get("err.noDisplays"));
                    }

                    if (newParsec.Count > profile.MonitorCount)
                    {
                        newParsec = newParsec.Take(profile.MonitorCount).ToList();
                    }

                    Report("session.step.displaysReady", newParsec.Count);
                    foreach (DisplayInfo d in newParsec)
                    {
                        Log.Info("  New Parsec display: " + Describe(d));
                    }

                    // The CCD database may hold an "internal inactive" entry for this exact set of targets (saved by
                    // an earlier session that turned the panel off). Windows can re-apply it on the hot-plug, which
                    // would leave the laptop screen dark against the user's choice. Put it back on when that happens.
                    if (_internal != null && !turnOffLaptopScreen)
                    {
                        EnsureInternalDisplayStillOn();
                    }

                    // 4. Resolution on each virtual display.
                    foreach (DisplayInfo d in newParsec)
                    {
                        if (before.Any(p => string.Equals(p.DeviceName, d.DeviceName, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new InvalidOperationException(Strings.Get("err.display.cloneStuck"));
                        }
                    }
                    DisplayMode effective = null;
                    foreach (DisplayInfo d in newParsec)
                    {
                        DisplayMode applied = ApplyMode(d.DeviceName, mode);
                        if (effective == null && applied != null)
                        {
                            effective = applied;
                        }
                    }

                    // 5. Virtual monitors at (0,0) and to the right; physical screens after them.
                    Report("session.step.arrange");
                    List<string> order = newParsec.Select(d => d.DeviceName).ToList();
                    foreach (DisplayInfo d in before.Where(d => d.IsActive && !string.IsNullOrEmpty(d.DeviceName)))
                    {
                        if (!order.Contains(d.DeviceName, StringComparer.OrdinalIgnoreCase))
                        {
                            order.Add(d.DeviceName);
                        }
                    }
                    Log.Info("Arranging left to right: " + string.Join(" | ", order) + " (primary " + order[0] + ")");
                    DisplayManager.ArrangeHorizontally(order, order[0]);

                    // 6. Laptop screen off.
                    if (turnOffLaptopScreen && _internal != null)
                    {
                        Report("session.step.laptopOff");
                        try
                        {
                            DisplayManager.SetInternalDisplayEnabled(false);
                            _internalOff = true;
                            Log.Info("Internal display turned off.");
                        }
                        catch (Exception ex)
                        {
                            // Not fatal: the virtual monitors work either way. Keep the session, warn the user.
                            Log.Error("Could not turn the internal display off; keeping it on.", ex);
                            Report("session.step.laptopOffFailed");
                            _internalOff = false;
                        }
                    }

                    // 7. Done.
                    ActiveProfile = profile;
                    ActiveMode = effective ?? mode;
                    SetState(SessionState.Active);
                    Report("session.step.active", profile.Name);
                    Log.Info("Session active: " + profile.Id + " @ " + ActiveMode.ToKey() + (_internalOff ? " (laptop screen off)" : string.Empty));
                }
                catch (Exception ex)
                {
                    Log.Error("Apply failed" + (vddOpened ? "; rolling back." : "."), ex);
                    Report("session.step.failed", ex.Message);

                    if (vddOpened)
                    {
                        Report("session.step.cleanup");
                        RestoreCore(SessionState.Faulted);
                    }
                    else
                    {
                        _snapshot = null;
                        _internal = null;
                        DisposeVddQuietly();
                        SetState(SessionState.Faulted);
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// Undoes everything <see cref="Apply"/> did. Idempotent and never throws (failures are logged).
        /// Safe to call from shutdown handlers and from any thread.
        /// </summary>
        public void Restore()
        {
            try
            {
                lock (_gate)
                {
                    if (_state == SessionState.Restoring)
                    {
                        return;
                    }

                    bool nothingToDo = _vdd == null && !_internalOff && _snapshot == null;
                    if (nothingToDo)
                    {
                        if (_state != SessionState.Idle)
                        {
                            SetState(SessionState.Idle);
                        }
                        return;
                    }

                    Log.Info("Restore requested (state " + _state + ").");
                    RestoreCore(SessionState.Idle);
                }
            }
            catch (Exception ex)
            {
                // RestoreCore already guards every step; this is belt and braces.
                Log.Error("Restore: unexpected failure.", ex);
                try { SetState(SessionState.Idle); } catch { }
            }
        }

        /// <summary><see cref="Restore"/>.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Restore();
            _disposed = true;
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>Must be called inside <see cref="_gate"/>. Ends in <paramref name="finalState"/>.</summary>
        private void RestoreCore(SessionState finalState)
        {
            SetState(SessionState.Restoring);
            Report("session.step.restoring");

            // a) Laptop screen back on first, so the user is never left staring at nothing.
            if (_internalOff)
            {
                Report("session.step.laptopOn");
                try
                {
                    DisplayManager.SetInternalDisplayEnabled(true);
                    Log.Info("Internal display turned back on.");
                }
                catch (Exception ex)
                {
                    Log.Error("SetInternalDisplayEnabled(true) failed; trying SetTopology(Extend).", ex);
                    try
                    {
                        DisplayManager.SetTopology(TopologyKind.Extend);
                        Log.Info("SetTopology(Extend) succeeded.");
                    }
                    catch (Exception ex2)
                    {
                        Log.Error("SetTopology(Extend) failed too.", ex2);
                    }
                }
                _internalOff = false;
            }

            // b) Remove the virtual displays and release the driver (stops keep-alive).
            if (_vdd != null)
            {
                Report("session.step.removeDisplays");
                try
                {
                    _vdd.RemoveAll();
                    Log.Info("Virtual displays removed.");
                }
                catch (Exception ex)
                {
                    Log.Error("ParsecVdd.RemoveAll failed.", ex);
                }
                DisposeVddQuietly();
            }

            // c) Put the physical displays back where they were. The REMOVE IOCTLs return long before the IddCx
            //    monitor departure reaches the CCD topology, so wait for the virtual targets to vanish first:
            //    applying the snapshot while they are still enumerated would persist "Parsec targets inactive"
            //    in the connectivity database and break the next Apply. Failures afterwards are harmless: with the
            //    virtual targets gone Windows already fell back to the pre-session layout.
            if (_snapshot != null)
            {
                Report("session.step.restoreLayout");
                WaitForParsecTargetsToDepart();
                try
                {
                    DisplayManager.Restore(_snapshot);
                    Log.Info("Display layout restored from snapshot (" + _snapshot.TakenAt.ToString("HH:mm:ss") + ").");
                }
                catch (Exception ex)
                {
                    Log.Warn("DisplayManager.Restore(snapshot) failed (ignored): " + ex.Message);
                }
                _snapshot = null;
            }

            _internal = null;
            _parsecTargetsBefore = 0;
            ActiveProfile = null;
            ActiveMode = null;

            SetState(finalState);
            if (finalState == SessionState.Idle)
            {
                Report("session.step.restored");
            }
            Log.Info("Restore finished → " + finalState);
        }

        /// <summary>
        /// Windows attaches a brand-new monitor as a clone of the primary display by default. In that topology the
        /// virtual target shares its source (and GDI device name) with the physical screen, so a mode change would
        /// hit the laptop panel. Detects that situation and forces the "extend" topology so every target gets its
        /// own source. Returns the refreshed list of active Parsec displays.
        /// </summary>
        private List<DisplayInfo> EnsureExtendedTopology(List<DisplayInfo> parsecAll, int expected, TimeSpan timeout)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                List<DisplayInfo> active;
                try
                {
                    active = DisplayManager.GetDisplays(false).Where(d => d.IsActive).ToList();
                }
                catch (Exception ex)
                {
                    Log.Warn("EnsureExtendedTopology: enumeration failed, assuming extended: " + ex.Message);
                    return parsecAll;
                }

                var physicalNames = new HashSet<string>(
                    active.Where(d => !d.IsParsecVirtual && !string.IsNullOrEmpty(d.DeviceName)).Select(d => d.DeviceName),
                    StringComparer.OrdinalIgnoreCase);
                var physicalSources = new HashSet<string>(
                    active.Where(d => !d.IsParsecVirtual).Select(d => d.AdapterIdValue + ":" + d.SourceId));
                List<DisplayInfo> parsec = active.Where(d => d.IsParsecVirtual).ToList();

                bool clonedWithPhysical = parsec.Any(d =>
                    physicalNames.Contains(d.DeviceName ?? string.Empty) ||
                    physicalSources.Contains(d.AdapterIdValue + ":" + d.SourceId));
                int distinctParsecSources = parsec.Select(d => d.AdapterIdValue + ":" + d.SourceId).Distinct().Count();
                bool clonedTogether = distinctParsecSources < parsec.Count;

                if (!clonedWithPhysical && !clonedTogether)
                {
                    return parsecAll;
                }

                if (attempt == 1)
                {
                    throw new InvalidOperationException(Strings.Get("err.display.cloneStuck"));
                }

                Log.Warn("Windows attached the virtual display(s) in clone mode (" +
                         (clonedWithPhysical ? "with a physical screen" : "with each other") + "); switching the topology to Extend.");
                Report("session.step.extend");
                DisplayManager.SetTopology(TopologyKind.Extend);
                Thread.Sleep(1000);
                parsecAll = DisplayManager.WaitForParsecDisplays(expected, timeout);
            }
            return parsecAll;
        }

        private void DisposeVddQuietly()
        {
            ParsecVdd vdd = _vdd;
            _vdd = null;
            if (vdd == null)
            {
                return;
            }
            try
            {
                vdd.Dispose();
                Log.Info("Parsec VDD handle closed.");
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd.Dispose failed.", ex);
            }
        }

        /// <summary>Number of Parsec targets currently connected (active or inactive); -1 when enumeration fails.</summary>
        private static int CountParsecTargets()
        {
            try
            {
                return DisplayManager.GetDisplays(true).Count(d => d.IsParsecVirtual);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not count Parsec targets: " + ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// Polls the CCD topology every <see cref="ParsecDepartPollMs"/> ms (bounded by
        /// <see cref="ParsecDepartTimeoutMs"/>) until no more Parsec targets than
        /// <see cref="_parsecTargetsBefore"/> remain connected. Never throws.
        /// </summary>
        private void WaitForParsecTargetsToDepart()
        {
            int allowed = Math.Max(0, _parsecTargetsBefore);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ParsecDepartTimeoutMs);
            DateTime started = DateTime.UtcNow;
            int lastCount = int.MinValue;
            while (true)
            {
                int count = CountParsecTargets();
                if (count != lastCount)
                {
                    Log.Info("Waiting for Parsec targets to depart: " + count + " connected (allowed " + allowed + ")");
                    lastCount = count;
                }

                if (count >= 0 && count <= allowed)
                {
                    Log.Info("Parsec targets departed after " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms.");
                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Log.Warn("Parsec targets still connected after " + ParsecDepartTimeoutMs + " ms (" + count + "); restoring the snapshot anyway.");
                    return;
                }

                Thread.Sleep(ParsecDepartPollMs);
            }
        }

        /// <summary>
        /// Called after the virtual displays appeared when the user chose to keep the laptop screen on:
        /// re-enables the built-in panel if Windows turned it off on the hot-plug (persisted database entry).
        /// Never throws.
        /// </summary>
        private void EnsureInternalDisplayStillOn()
        {
            try
            {
                List<DisplayInfo> now = DisplayManager.GetDisplays(true);
                DisplayInfo internalNow = DisplayManager.FindInternal(now);
                if (internalNow == null)
                {
                    Log.Warn("Internal display is no longer enumerated after adding the virtual displays.");
                    return;
                }
                if (internalNow.IsActive)
                {
                    return;
                }

                Log.Warn("Windows turned the internal display off when the virtual displays appeared; turning it back on.");
                Report("session.step.laptopOn");
                DisplayManager.SetInternalDisplayEnabled(true);
                Log.Info("Internal display re-enabled.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not re-enable the internal display after adding the virtual displays.", ex);
            }
        }

        /// <summary>
        /// Recovery that does not depend on in-process state: if the built-in display is connected but inactive
        /// (for example after the process was killed with the screen off), turns it back on with
        /// <see cref="DisplayManager.SetInternalDisplayEnabled"/>, falling back to
        /// <see cref="DisplayManager.SetTopology"/>(Extend). Throws when both fail. Stale virtual displays are not
        /// touched: the driver unplugs them by itself once their keep-alive stops.
        /// </summary>
        public static RecoveryResult RecoverInternalDisplay()
        {
            Log.Info("RecoverInternalDisplay: checking the built-in display.");
            List<DisplayInfo> displays = DisplayManager.GetDisplays(true);
            DisplayInfo internalDisplay = DisplayManager.FindInternal(displays);
            if (internalDisplay == null)
            {
                Log.Info("RecoverInternalDisplay: this machine has no built-in display.");
                return RecoveryResult.NoInternalDisplay;
            }
            if (internalDisplay.IsActive)
            {
                Log.Info("RecoverInternalDisplay: the built-in display is already active (" + Describe(internalDisplay) + ").");
                return RecoveryResult.AlreadyOn;
            }

            Log.Warn("RecoverInternalDisplay: the built-in display is connected but inactive; turning it on.");
            try
            {
                DisplayManager.SetInternalDisplayEnabled(true);
            }
            catch (Exception ex)
            {
                Log.Error("RecoverInternalDisplay: SetInternalDisplayEnabled(true) failed; trying SetTopology(Extend).", ex);
                DisplayManager.SetTopology(TopologyKind.Extend);
            }

            DisplayInfo after = DisplayManager.FindInternal(DisplayManager.GetDisplays(true));
            if (after != null && after.IsActive)
            {
                Log.Info("RecoverInternalDisplay: built-in display is active again (" + Describe(after) + ").");
                return RecoveryResult.Restored;
            }

            var failure = new InvalidOperationException(Strings.Get("recover.failed"));
            Log.Error("RecoverInternalDisplay: the built-in display is still inactive after the change.", failure);
            throw failure;
        }

        /// <summary>
        /// Sets <paramref name="requested"/> on <paramref name="deviceName"/>; on failure picks the closest supported
        /// mode that is not larger than the request (same aspect ratio preferred). Returns the mode that ended up
        /// applied, or null when nothing could be changed.
        /// </summary>
        private DisplayMode ApplyMode(string deviceName, DisplayMode requested)
        {
            Report("session.step.setMode", deviceName, requested.ToString());
            try
            {
                DisplayManager.SetMode(deviceName, requested);
                Log.Info("SetMode " + deviceName + " → " + requested.ToKey() + " OK");
                return requested;
            }
            catch (Exception ex)
            {
                Log.Warn("SetMode " + deviceName + " → " + requested.ToKey() + " failed: " + ex.Message);
            }

            List<DisplayMode> supported;
            try
            {
                supported = DisplayManager.GetSupportedModes(deviceName) ?? new List<DisplayMode>();
            }
            catch (Exception ex)
            {
                Log.Error("GetSupportedModes(" + deviceName + ") failed.", ex);
                Report("session.step.modeFailed", deviceName);
                return null;
            }

            Log.Info("Supported modes on " + deviceName + ": " + string.Join(", ", supported.Select(m => m.ToKey())));
            DisplayMode fallback = PickClosest(supported, requested);
            if (fallback == null)
            {
                Log.Warn("No usable fallback mode for " + deviceName + "; keeping the current mode.");
                Report("session.step.modeFailed", deviceName);
                return null;
            }

            Report("session.step.modeSubstituted", deviceName, requested.ToString(), fallback.ToString());
            try
            {
                DisplayManager.SetMode(deviceName, fallback);
                Log.Info("SetMode " + deviceName + " → " + fallback.ToKey() + " OK (substituted for " + requested.ToKey() + ")");
                return fallback;
            }
            catch (Exception ex)
            {
                Log.Error("Fallback SetMode " + deviceName + " → " + fallback.ToKey() + " failed; keeping the current mode.", ex);
                Report("session.step.modeFailed", deviceName);
                return null;
            }
        }

        /// <summary>
        /// Closest mode ≤ requested (by area), same aspect label preferred, then refresh rate closest to the request
        /// without exceeding it. If nothing fits, the smallest supported mode overall with the same refresh-rate
        /// preference (never the fastest one: a 240 Hz virtual desktop only costs encoder time).
        /// </summary>
        internal static DisplayMode PickClosest(IEnumerable<DisplayMode> supported, DisplayMode requested)
        {
            if (supported == null || requested == null)
            {
                return null;
            }

            List<DisplayMode> all = supported.Where(m => m != null && m.Width > 0 && m.Height > 0).Distinct().ToList();
            if (all.Count == 0)
            {
                return null;
            }

            List<DisplayMode> notLarger = all.Where(m => m.Width <= requested.Width && m.Height <= requested.Height).ToList();
            if (notLarger.Count == 0)
            {
                return all
                    .OrderBy(m => m.Area)
                    .ThenByDescending(m => m.Hz <= requested.Hz ? 1 : 0)
                    .ThenBy(m => Math.Abs(m.Hz - requested.Hz))
                    .First();
            }

            string aspect = requested.AspectLabel;
            return notLarger
                .OrderByDescending(m => m.AspectLabel == aspect ? 1 : 0)
                .ThenByDescending(m => m.Area)
                .ThenByDescending(m => m.Hz <= requested.Hz ? 1 : 0)
                .ThenBy(m => Math.Abs(m.Hz - requested.Hz))
                .First();
        }

        private static string Describe(DisplayInfo d)
        {
            if (d == null)
            {
                return "<null>";
            }
            return d.DeviceName + " [" +
                   (d.IsActive ? "active" : "inactive") +
                   (d.IsPrimary ? ", primary" : string.Empty) +
                   (d.IsInternal ? ", internal" : string.Empty) +
                   (d.IsParsecVirtual ? ", parsec" : string.Empty) + "] " +
                   (d.MonitorName ?? string.Empty) + " / " + (d.AdapterString ?? string.Empty) +
                   (d.CurrentMode != null ? " " + d.CurrentMode.ToKey() : string.Empty) +
                   " @" + d.Bounds.X + "," + d.Bounds.Y;
        }

        private void SetState(SessionState state)
        {
            if (_state == state)
            {
                return;
            }
            _state = state;
            Log.Info("State → " + state);

            Action<SessionState> handler = StateChanged;
            if (handler != null)
            {
                try
                {
                    handler(state);
                }
                catch (Exception ex)
                {
                    Log.Error("StateChanged handler threw.", ex);
                }
            }
        }

        private void Report(string key, params object[] args)
        {
            string text = args == null || args.Length == 0 ? Strings.Get(key) : Strings.Format(key, args);
            Log.Info("Step: " + text);

            Action<string> handler = Progress;
            if (handler != null)
            {
                try
                {
                    handler(text);
                }
                catch (Exception ex)
                {
                    Log.Error("Progress handler threw.", ex);
                }
            }
        }
    }
}
