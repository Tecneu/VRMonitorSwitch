using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using VRMonitorSwitch.Core;

namespace VRMonitorSwitch.Vdd
{
    /// <summary>
    /// Driver status as reported by the Configuration Manager. Mirrors <c>DeviceStatus</c>
    /// from nomi-san's <c>parsec-vdd.h</c>, in the same order.
    /// </summary>
    public enum VddStatus
    {
        /// <summary>Driver loaded and started — ready to use.</summary>
        Ok,
        /// <summary>SetupAPI could not enumerate the device class.</summary>
        Inaccessible,
        /// <summary>Device present but neither started nor flagged with a problem.</summary>
        Unknown,
        /// <summary>Device has a problem code we do not map.</summary>
        UnknownProblem,
        /// <summary>Device disabled (CM_PROB_DISABLED / CM_PROB_HARDWARE_DISABLED).</summary>
        Disabled,
        /// <summary>Driver failed after start (CM_PROB_FAILED_POST_START).</summary>
        DriverError,
        /// <summary>Driver installed but a reboot is pending (CM_PROB_NEED_RESTART).</summary>
        RestartRequired,
        /// <summary>Driver service disabled (CM_PROB_DISABLED_SERVICE).</summary>
        DisabledService,
        /// <summary>No device with hardware id <c>Root\Parsec\VDA</c> is present.</summary>
        NotInstalled
    }

    /// <summary>
    /// Client for the Parsec Virtual Display Driver (Parsec VDD). Faithful C# port of the
    /// public domain header <c>core/parsec-vdd.h</c> from https://github.com/nomi-san/parsec-vdd.
    /// <para>
    /// Lifetime: <see cref="Open"/> acquires the device handle, reads the driver version and
    /// starts a keep-alive thread that sends <c>IOCTL_UPDATE</c> every
    /// <see cref="KeepAliveIntervalMs"/> ms (the driver unplugs every display after ~1 s of
    /// silence). <see cref="Dispose"/> removes every display added by this instance, stops the
    /// keep-alive thread and closes the handle. All IOCTLs are serialized through one lock.
    /// </para>
    /// </summary>
    public sealed class ParsecVdd : IDisposable
    {
        public const string DriverDownloadUrl = "https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe";
        public const string DriverSha256 = "E23332448FDAF5AA017CB308DB5EF6855FAC526A7DED05D80C039404126D5362";
        public const string AdapterName = "Parsec Virtual Display Adapter";
        public const string MonitorName = "ParsecVDA";
        /// <summary>The driver supports 16 per adapter; the header caps at 8 to avoid plugging lag.</summary>
        public const int MaxDisplays = 8;

        /// <summary>Device interface GUID used to open the adapter handle.</summary>
        public static readonly Guid AdapterGuid = new Guid("00b41627-04c4-429e-a26e-0265cf50c8fa");
        /// <summary>Setup class GUID (Display) used to query the device status.</summary>
        public static readonly Guid ClassGuid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
        /// <summary>Hardware id of the root-enumerated Parsec adapter.</summary>
        public const string HardwareId = @"Root\Parsec\VDA";
        /// <summary>EDID manufacturer/product id of the virtual monitor (visible in the registry).</summary>
        public const string DisplayId = "PSCCDD0";

        // IOCTL codes (CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800 + n, METHOD_BUFFERED, access))
        private const uint IoctlAdd = 0x0022e004;
        private const uint IoctlRemove = 0x0022a008;
        private const uint IoctlUpdate = 0x0022a00c;
        private const uint IoctlVersion = 0x0022e010;

        private const int InputBufferSize = 32;
        private const uint IoTimeoutMs = 5000;
        /// <summary>Bounded wait for a cancelled request to drain after a timeout; never wait forever while holding the lock.</summary>
        private const uint IoCancelTimeoutMs = 2000;
        private const int IoctlFailed = -1;
        private const int KeepAliveJoinTimeoutMs = 3000;

        private readonly object _ioLock = new object();
        private readonly List<int> _displays = new List<int>();

        private SafeFileHandle _handle;
        private Thread _keepAliveThread;
        private ManualResetEvent _stopKeepAlive;
        private int _keepAliveIntervalMs = 50;
        private int _version;
        private int _disposed; // 0 = alive, 1 = disposed (Interlocked)

        // ------------------------------------------------------------------
        // Static status query
        // ------------------------------------------------------------------

        /// <summary>
        /// Port of <c>QueryDeviceStatus(&amp;VDD_CLASS_GUID, VDD_HARDWARE_ID)</c>. Never throws.
        /// Returns <see cref="VddStatus.NotInstalled"/> when no present device in the Display
        /// class carries the hardware id <c>Root\Parsec\VDA</c>.
        /// </summary>
        public static VddStatus QueryStatus()
        {
            VddStatus status = VddStatus.Inaccessible;
            IntPtr devInfo = VddNative.INVALID_HANDLE_VALUE;

            try
            {
                Guid classGuid = ClassGuid;
                devInfo = VddNative.SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, VddNative.DIGCF_PRESENT);
                if (devInfo == VddNative.INVALID_HANDLE_VALUE || devInfo == IntPtr.Zero)
                {
                    Log.Warn("ParsecVdd.QueryStatus: SetupDiGetClassDevs failed, error " + Marshal.GetLastWin32Error());
                    return VddStatus.Inaccessible;
                }

                bool found = false;
                for (uint index = 0; !found; index++)
                {
                    var devInfoData = VddNative.SP_DEVINFO_DATA.Create();
                    if (!VddNative.SetupDiEnumDeviceInfo(devInfo, index, ref devInfoData))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != VddNative.ERROR_NO_MORE_ITEMS)
                            Log.Warn("ParsecVdd.QueryStatus: SetupDiEnumDeviceInfo failed at index " + index + ", error " + err);
                        break;
                    }

                    string[] hardwareIds = ReadHardwareIds(devInfo, ref devInfoData);
                    if (hardwareIds == null || !ContainsHardwareId(hardwareIds))
                        continue;

                    found = true;
                    uint devStatus, devProblem;
                    int cr = VddNative.CM_Get_DevNode_Status(out devStatus, out devProblem, devInfoData.DevInst, 0);
                    if (cr != VddNative.CR_SUCCESS)
                    {
                        Log.Warn("ParsecVdd.QueryStatus: CM_Get_DevNode_Status returned CONFIGRET " + cr);
                        status = VddStatus.NotInstalled;
                        break;
                    }

                    status = MapDevNodeStatus(devStatus, devProblem);
                    Log.Info(string.Format("ParsecVdd.QueryStatus: devnode status 0x{0:X8}, problem {1} -> {2}", devStatus, devProblem, status));
                }

                if (!found)
                    status = VddStatus.NotInstalled;
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd.QueryStatus failed", ex);
                status = VddStatus.Inaccessible;
            }
            finally
            {
                if (devInfo != VddNative.INVALID_HANDLE_VALUE && devInfo != IntPtr.Zero)
                    VddNative.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return status;
        }

        /// <summary>True when <see cref="QueryStatus"/> is anything other than <see cref="VddStatus.NotInstalled"/>.</summary>
        public static bool IsInstalled
        {
            get { return QueryStatus() != VddStatus.NotInstalled; }
        }

        private static VddStatus MapDevNodeStatus(uint devStatus, uint devProblem)
        {
            if ((devStatus & (VddNative.DN_DRIVER_LOADED | VddNative.DN_STARTED)) != 0)
                return VddStatus.Ok;

            if ((devStatus & VddNative.DN_HAS_PROBLEM) != 0)
            {
                switch (devProblem)
                {
                    case VddNative.CM_PROB_NEED_RESTART:
                        return VddStatus.RestartRequired;
                    case VddNative.CM_PROB_DISABLED:
                    case VddNative.CM_PROB_HARDWARE_DISABLED:
                        return VddStatus.Disabled;
                    case VddNative.CM_PROB_DISABLED_SERVICE:
                        return VddStatus.DisabledService;
                    case VddNative.CM_PROB_FAILED_POST_START:
                        return VddStatus.DriverError;
                    default:
                        return VddStatus.UnknownProblem;
                }
            }

            return VddStatus.Unknown;
        }

        /// <summary>
        /// Reads SPDRP_HARDWAREID (REG_SZ or REG_MULTI_SZ, UTF-16) and splits it into its
        /// individual ids. Returns null when the property is missing or not a string type.
        /// </summary>
        private static string[] ReadHardwareIds(IntPtr devInfo, ref VddNative.SP_DEVINFO_DATA devInfoData)
        {
            uint regType, required;
            VddNative.SetupDiGetDeviceRegistryProperty(devInfo, ref devInfoData, VddNative.SPDRP_HARDWAREID,
                out regType, null, 0, out required);
            if (required == 0)
                return null;

            byte[] buffer = new byte[required];
            if (!VddNative.SetupDiGetDeviceRegistryProperty(devInfo, ref devInfoData, VddNative.SPDRP_HARDWAREID,
                    out regType, buffer, (uint)buffer.Length, out required))
                return null;

            if (regType != VddNative.REG_SZ && regType != VddNative.REG_MULTI_SZ)
                return null;

            int byteCount = Math.Min((int)required, buffer.Length);
            string block = Encoding.Unicode.GetString(buffer, 0, byteCount);
            return block.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool ContainsHardwareId(string[] ids)
        {
            // Hardware ids are case-insensitive in PnP; the header uses lstrcmpA (case-sensitive),
            // which is a strict subset of this check.
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.Equals(ids[i], HardwareId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Instance state
        // ------------------------------------------------------------------

        /// <summary>True between a successful <see cref="Open"/> and <see cref="Dispose"/>.</summary>
        public bool IsOpen
        {
            get
            {
                SafeFileHandle h = _handle;
                return _disposed == 0 && h != null && !h.IsInvalid && !h.IsClosed;
            }
        }

        /// <summary>Driver minor version (IOCTL_VERSION). Valid after <see cref="Open"/>; -1 if the query failed.</summary>
        public int Version
        {
            get { return _version; }
        }

        /// <summary>Snapshot of the display indices added by this instance, in insertion order.</summary>
        public IReadOnlyList<int> Displays
        {
            get
            {
                lock (_ioLock)
                {
                    return _displays.ToArray();
                }
            }
        }

        /// <summary>
        /// Interval between IOCTL_UPDATE pings. Default 50 ms; the driver requires less than 100 ms.
        /// Values are clamped to [10, 95]. Takes effect on the next keep-alive iteration.
        /// </summary>
        public int KeepAliveIntervalMs
        {
            get { return _keepAliveIntervalMs; }
            set
            {
                int clamped = Math.Max(10, Math.Min(95, value));
                if (clamped != value)
                    Log.Warn("ParsecVdd.KeepAliveIntervalMs " + value + " out of range, clamped to " + clamped);
                _keepAliveIntervalMs = clamped;
            }
        }

        // ------------------------------------------------------------------
        // Open / IOCTL plumbing
        // ------------------------------------------------------------------

        /// <summary>
        /// Opens the adapter device handle, reads the driver version and starts the keep-alive
        /// thread. Calling it on an already open instance is a logged no-op.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The instance was disposed.</exception>
        /// <exception cref="InvalidOperationException">The device interface could not be opened (localized message).</exception>
        public void Open()
        {
            ThrowIfDisposed();

            lock (_ioLock)
            {
                if (IsOpen)
                {
                    Log.Warn("ParsecVdd.Open called while already open; ignored");
                    return;
                }

                SafeFileHandle handle = OpenDeviceHandle();
                if (handle == null)
                {
                    VddStatus status = QueryStatus();
                    Log.Error("ParsecVdd.Open: could not open the Parsec VDD device interface (driver status: " + status + ")");
                    throw new InvalidOperationException(Strings.Format("vdd.err.openFailed", Strings.DriverStatus(status)));
                }

                _handle = handle;

                int version = IoControl(IoctlVersion, null);
                if (version == IoctlFailed)
                    Log.Warn("ParsecVdd.Open: IOCTL_VERSION failed; continuing without version information");
                _version = version;

                _stopKeepAlive = new ManualResetEvent(false);
                _keepAliveThread = new Thread(KeepAliveLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = "ParsecVdd keep-alive"
                };
                _keepAliveThread.Start();

                Log.Info("ParsecVdd.Open: device opened, driver minor version " + version + ", keep-alive every " + _keepAliveIntervalMs + " ms");
            }
        }

        /// <summary>
        /// Port of <c>OpenDeviceHandle(&amp;VDD_ADAPTER_GUID)</c>: enumerates the device
        /// interfaces of the adapter GUID and opens the first one that <c>CreateFile</c> accepts.
        /// Returns null on failure (never throws).
        /// </summary>
        private static SafeFileHandle OpenDeviceHandle()
        {
            IntPtr devInfo = VddNative.INVALID_HANDLE_VALUE;
            try
            {
                Guid interfaceGuid = AdapterGuid;
                devInfo = VddNative.SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero,
                    VddNative.DIGCF_PRESENT | VddNative.DIGCF_DEVICEINTERFACE);
                if (devInfo == VddNative.INVALID_HANDLE_VALUE || devInfo == IntPtr.Zero)
                {
                    Log.Warn("ParsecVdd.OpenDeviceHandle: SetupDiGetClassDevs failed, error " + Marshal.GetLastWin32Error());
                    return null;
                }

                for (uint i = 0; ; i++)
                {
                    var ifData = VddNative.SP_DEVICE_INTERFACE_DATA.Create();
                    if (!VddNative.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref interfaceGuid, i, ref ifData))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != VddNative.ERROR_NO_MORE_ITEMS)
                            Log.Warn("ParsecVdd.OpenDeviceHandle: SetupDiEnumDeviceInterfaces failed, error " + err);
                        else if (i == 0)
                            Log.Warn("ParsecVdd.OpenDeviceHandle: no device interface found for the Parsec adapter GUID");
                        break;
                    }

                    string devicePath = GetDeviceInterfacePath(devInfo, ref ifData);
                    if (string.IsNullOrEmpty(devicePath))
                        continue;

                    SafeFileHandle handle = VddNative.CreateFile(devicePath,
                        VddNative.GENERIC_READ | VddNative.GENERIC_WRITE,
                        VddNative.FILE_SHARE_READ | VddNative.FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        VddNative.OPEN_EXISTING,
                        VddNative.FILE_ATTRIBUTE_NORMAL | VddNative.FILE_FLAG_NO_BUFFERING |
                        VddNative.FILE_FLAG_OVERLAPPED | VddNative.FILE_FLAG_WRITE_THROUGH,
                        IntPtr.Zero);

                    if (handle != null && !handle.IsInvalid)
                    {
                        Log.Info("ParsecVdd.OpenDeviceHandle: opened " + devicePath);
                        return handle;
                    }

                    int createErr = Marshal.GetLastWin32Error();
                    Log.Warn("ParsecVdd.OpenDeviceHandle: CreateFile failed for " + devicePath + ", error " + createErr);
                    if (handle != null)
                        handle.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd.OpenDeviceHandle failed", ex);
            }
            finally
            {
                if (devInfo != VddNative.INVALID_HANDLE_VALUE && devInfo != IntPtr.Zero)
                    VddNative.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return null;
        }

        /// <summary>
        /// Two-step SetupDiGetDeviceInterfaceDetailW into an unmanaged buffer, honoring the
        /// cbSize quirk (8 on x64 / 6 on x86). Returns null on failure.
        /// </summary>
        private static string GetDeviceInterfacePath(IntPtr devInfo, ref VddNative.SP_DEVICE_INTERFACE_DATA ifData)
        {
            uint required;
            VddNative.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out required, IntPtr.Zero);
            int err = Marshal.GetLastWin32Error();
            if (required == 0)
            {
                Log.Warn("ParsecVdd.GetDeviceInterfacePath: size query failed, error " + err);
                return null;
            }

            IntPtr detail = IntPtr.Zero;
            try
            {
                detail = Marshal.AllocHGlobal((int)required);
                ZeroMemory(detail, (int)required);
                Marshal.WriteInt32(detail, 0, (int)VddNative.DeviceInterfaceDetailCbSize);

                if (!VddNative.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detail, required, out required, IntPtr.Zero))
                {
                    Log.Warn("ParsecVdd.GetDeviceInterfacePath: SetupDiGetDeviceInterfaceDetail failed, error " + Marshal.GetLastWin32Error());
                    return null;
                }

                return Marshal.PtrToStringUni(IntPtr.Add(detail, VddNative.DeviceInterfaceDetailPathOffset));
            }
            finally
            {
                if (detail != IntPtr.Zero)
                    Marshal.FreeHGlobal(detail);
            }
        }

        /// <summary>
        /// Port of <c>VddIoControl</c>: 32-byte zeroed input (payload copied at offset 0), one
        /// DWORD output, overlapped with a manual-reset event and a 5 s
        /// <c>GetOverlappedResultEx</c> timeout. Returns the output DWORD as int, or -1 on failure.
        /// Must be called with <see cref="_ioLock"/> held.
        /// </summary>
        private int IoControl(uint code, byte[] payload)
        {
            SafeFileHandle handle = _handle;
            if (handle == null || handle.IsInvalid || handle.IsClosed)
                return IoctlFailed;

            int overlappedSize = Marshal.SizeOf(typeof(VddNative.OVERLAPPED));
            // Layout: [0..32) input, [32..36) output DWORD, [40..40+overlappedSize) OVERLAPPED.
            const int inOffset = 0;
            const int outOffset = InputBufferSize;
            int ovOffset = InputBufferSize + 8;
            int total = ovOffset + overlappedSize;

            IntPtr mem = IntPtr.Zero;
            IntPtr hEvent = IntPtr.Zero;
            try
            {
                mem = Marshal.AllocHGlobal(total);
                ZeroMemory(mem, total);

                if (payload != null && payload.Length > 0)
                    Marshal.Copy(payload, 0, mem, Math.Min(payload.Length, InputBufferSize));

                hEvent = VddNative.CreateEvent(IntPtr.Zero, true, false, null);
                if (hEvent == IntPtr.Zero)
                {
                    Log.Error("ParsecVdd.IoControl: CreateEvent failed, error " + Marshal.GetLastWin32Error());
                    return IoctlFailed;
                }

                IntPtr ov = IntPtr.Add(mem, ovOffset);
                var overlapped = new VddNative.OVERLAPPED { hEvent = hEvent };
                Marshal.StructureToPtr(overlapped, ov, false);

                uint bytesReturned;
                bool ok = VddNative.DeviceIoControl(handle, code, IntPtr.Add(mem, inOffset), InputBufferSize,
                    IntPtr.Add(mem, outOffset), sizeof(uint), out bytesReturned, ov);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != VddNative.ERROR_IO_PENDING)
                    {
                        Log.Warn(string.Format("ParsecVdd.IoControl 0x{0:X8}: DeviceIoControl failed, error {1}", code, err));
                        return IoctlFailed;
                    }
                }

                uint transferred;
                if (!VddNative.GetOverlappedResultEx(handle, ov, out transferred, IoTimeoutMs, false))
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Warn(string.Format("ParsecVdd.IoControl 0x{0:X8}: GetOverlappedResultEx failed, error {1}{2}",
                        code, err, err == VddNative.WAIT_TIMEOUT ? " (timeout)" : string.Empty));

                    // The request may still be in flight and owns our unmanaged buffers; cancel it
                    // and wait (bounded) for completion before the memory is released.
                    VddNative.CancelIoEx(handle, ov);
                    uint ignored;
                    if (!VddNative.GetOverlappedResultEx(handle, ov, out ignored, IoCancelTimeoutMs, false))
                    {
                        int cancelErr = Marshal.GetLastWin32Error();
                        if (cancelErr == VddNative.WAIT_TIMEOUT)
                        {
                            // The driver did not complete the cancelled request. Waiting forever here would deadlock the
                            // keep-alive thread, RemoveAll/Dispose and every Restore behind _ioLock; freeing the buffer
                            // could let the kernel write into reused memory. Deliberately leak the 64-byte buffer and
                            // the event instead (ownership stays with the pending IRP).
                            Log.Error(string.Format("ParsecVdd.IoControl 0x{0:X8}: cancelled request did not complete within {1} ms; leaking its {2}-byte buffer and event",
                                code, IoCancelTimeoutMs, total));
                            mem = IntPtr.Zero;
                            hEvent = IntPtr.Zero;
                        }
                        else if (cancelErr != VddNative.ERROR_OPERATION_ABORTED)
                        {
                            Log.Warn(string.Format("ParsecVdd.IoControl 0x{0:X8}: GetOverlappedResultEx after cancel failed, error {1}", code, cancelErr));
                        }
                    }
                    return IoctlFailed;
                }

                return Marshal.ReadInt32(mem, outOffset);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("ParsecVdd.IoControl 0x{0:X8} failed", code), ex);
                return IoctlFailed;
            }
            finally
            {
                if (hEvent != IntPtr.Zero)
                    VddNative.CloseHandle(hEvent);
                if (mem != IntPtr.Zero)
                    Marshal.FreeHGlobal(mem);
            }
        }

        private static void ZeroMemory(IntPtr ptr, int count)
        {
            for (int i = 0; i < count; i++)
                Marshal.WriteByte(ptr, i, 0);
        }

        /// <summary>IOCTL_UPDATE. Must be called with <see cref="_ioLock"/> held.</summary>
        private bool Update()
        {
            return IoControl(IoctlUpdate, null) != IoctlFailed;
        }

        // ------------------------------------------------------------------
        // Displays
        // ------------------------------------------------------------------

        /// <summary>
        /// Plugs a new virtual display and returns its index (port of <c>VddAddDisplay</c>).
        /// </summary>
        /// <exception cref="InvalidOperationException">Not open, <see cref="MaxDisplays"/> reached, or the driver returned -1.</exception>
        public int AddDisplay()
        {
            ThrowIfDisposed();
            lock (_ioLock)
            {
                EnsureOpen();

                if (_displays.Count >= MaxDisplays)
                {
                    Log.Error("ParsecVdd.AddDisplay: limit of " + MaxDisplays + " displays reached");
                    throw new InvalidOperationException(Strings.Format("vdd.err.maxDisplays", MaxDisplays));
                }

                int index = IoControl(IoctlAdd, null);
                Update();

                if (index == IoctlFailed)
                {
                    Log.Error("ParsecVdd.AddDisplay: IOCTL_ADD returned -1");
                    throw new InvalidOperationException(Strings.Get("vdd.err.addFailed"));
                }

                _displays.Add(index);
                Log.Info("ParsecVdd.AddDisplay: added display index " + index + " (" + _displays.Count + " active)");
                return index;
            }
        }

        /// <summary>
        /// Unplugs the display with the given index (port of <c>VddRemoveDisplay</c>; index sent
        /// as 16-bit big-endian). Removing an index this instance does not track is allowed and logged.
        /// </summary>
        public void RemoveDisplay(int index)
        {
            ThrowIfDisposed();
            lock (_ioLock)
            {
                EnsureOpen();
                RemoveDisplayCore(index);
            }
        }

        /// <summary>Must be called with <see cref="_ioLock"/> held and the handle open.</summary>
        private void RemoveDisplayCore(int index)
        {
            if (index < 0 || index > 0xFFFF)
                throw new ArgumentOutOfRangeException("index", index, "Display index must fit in 16 bits.");

            byte[] payload = { (byte)((index >> 8) & 0xFF), (byte)(index & 0xFF) };
            int result = IoControl(IoctlRemove, payload);
            Update();

            bool tracked = _displays.Remove(index);
            if (result == IoctlFailed)
                Log.Warn("ParsecVdd.RemoveDisplay: IOCTL_REMOVE for index " + index + " reported failure (display untracked anyway)");
            else
                Log.Info("ParsecVdd.RemoveDisplay: removed display index " + index + (tracked ? string.Empty : " (was not tracked by this instance)"));
        }

        /// <summary>
        /// Removes every display added by this instance, highest index first (Windows 10
        /// connectivity-cache quirk, nomi-san issue #23). Failures are logged, never thrown.
        /// </summary>
        public void RemoveAll()
        {
            lock (_ioLock)
            {
                if (_displays.Count == 0)
                    return;

                if (!IsOpenUnsafe())
                {
                    Log.Warn("ParsecVdd.RemoveAll: handle is not open; clearing " + _displays.Count + " tracked display(s) without IOCTLs");
                    _displays.Clear();
                    return;
                }

                int[] indices = _displays.ToArray();
                Array.Sort(indices);
                Array.Reverse(indices);

                foreach (int index in indices)
                {
                    try
                    {
                        RemoveDisplayCore(index);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ParsecVdd.RemoveAll: failed to remove display " + index, ex);
                        _displays.Remove(index);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Keep-alive
        // ------------------------------------------------------------------

        private void KeepAliveLoop()
        {
            ManualResetEvent stop = _stopKeepAlive;
            bool lastFailed = false;
            int consecutiveFailures = 0;

            Log.Info("ParsecVdd keep-alive thread started");
            try
            {
                while (!stop.WaitOne(_keepAliveIntervalMs))
                {
                    bool ok;
                    lock (_ioLock)
                    {
                        if (!IsOpenUnsafe())
                            break;
                        ok = Update();
                    }

                    if (!ok)
                    {
                        consecutiveFailures++;
                        if (!lastFailed || consecutiveFailures % 100 == 0)
                            Log.Warn("ParsecVdd keep-alive: IOCTL_UPDATE failed (" + consecutiveFailures + " consecutive)");
                        lastFailed = true;
                    }
                    else if (lastFailed)
                    {
                        Log.Info("ParsecVdd keep-alive: IOCTL_UPDATE recovered after " + consecutiveFailures + " failure(s)");
                        lastFailed = false;
                        consecutiveFailures = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd keep-alive thread crashed", ex);
            }
            Log.Info("ParsecVdd keep-alive thread stopped");
        }

        // ------------------------------------------------------------------
        // Dispose
        // ------------------------------------------------------------------

        /// <summary>
        /// RemoveAll + stop keep-alive + CloseHandle. Idempotent and safe to call from any thread
        /// (joins the keep-alive thread with a timeout; never joins itself).
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                RemoveAll();
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd.Dispose: RemoveAll failed", ex);
            }

            Thread keepAlive;
            ManualResetEvent stop;
            lock (_ioLock)
            {
                keepAlive = _keepAliveThread;
                stop = _stopKeepAlive;
                _keepAliveThread = null;
            }

            try
            {
                if (stop != null)
                    stop.Set();

                if (keepAlive != null && keepAlive != Thread.CurrentThread && keepAlive.IsAlive)
                {
                    if (!keepAlive.Join(KeepAliveJoinTimeoutMs))
                        Log.Warn("ParsecVdd.Dispose: keep-alive thread did not stop within " + KeepAliveJoinTimeoutMs + " ms");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ParsecVdd.Dispose: stopping keep-alive failed", ex);
            }

            lock (_ioLock)
            {
                try
                {
                    if (_handle != null)
                    {
                        _handle.Dispose();
                        Log.Info("ParsecVdd.Dispose: device handle closed");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ParsecVdd.Dispose: CloseHandle failed", ex);
                }
                finally
                {
                    _handle = null;
                }

                if (stop != null)
                {
                    try { stop.Dispose(); } catch (Exception ex) { Log.Error("ParsecVdd.Dispose: event cleanup failed", ex); }
                    _stopKeepAlive = null;
                }
            }
        }

        // ------------------------------------------------------------------
        // Guards
        // ------------------------------------------------------------------

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(ParsecVdd), Strings.Get("vdd.err.disposed"));
        }

        /// <summary>Open check without the disposed flag; caller holds <see cref="_ioLock"/>.</summary>
        private bool IsOpenUnsafe()
        {
            SafeFileHandle h = _handle;
            return h != null && !h.IsInvalid && !h.IsClosed;
        }

        private void EnsureOpen()
        {
            if (!IsOpenUnsafe())
            {
                Log.Error("ParsecVdd: operation requested before Open()");
                throw new InvalidOperationException(Strings.Get("vdd.err.notOpen"));
            }
        }
    }
}
