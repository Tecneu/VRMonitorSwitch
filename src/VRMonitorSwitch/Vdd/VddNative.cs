using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VRMonitorSwitch.Vdd
{
    /// <summary>
    /// Raw Win32 declarations used by <see cref="ParsecVdd"/>: SetupAPI device enumeration,
    /// Configuration Manager status queries and overlapped <c>DeviceIoControl</c>.
    /// Only the Unicode ("W") entry points are used; strings read from the registry
    /// property buffers are therefore UTF-16 multi-sz blocks.
    /// </summary>
    internal static class VddNative
    {
        // ------------------------------------------------------------------
        // Constants
        // ------------------------------------------------------------------

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // SetupDiGetClassDevs flags
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        // SetupDiGetDeviceRegistryProperty property codes
        public const uint SPDRP_HARDWAREID = 0x00000001;

        // Registry value types
        public const uint REG_SZ = 1;
        public const uint REG_MULTI_SZ = 7;

        // Configuration Manager return codes
        public const int CR_SUCCESS = 0;

        // DevNode status flags (cfg.h)
        public const uint DN_DRIVER_LOADED = 0x00000002;
        public const uint DN_STARTED = 0x00000008;
        public const uint DN_HAS_PROBLEM = 0x00000400;

        // DevNode problem codes (cfg.h)
        public const uint CM_PROB_NEED_RESTART = 14;
        public const uint CM_PROB_DISABLED = 22;
        public const uint CM_PROB_HARDWARE_DISABLED = 29;
        public const uint CM_PROB_DISABLED_SERVICE = 32;
        public const uint CM_PROB_FAILED_POST_START = 43;

        // CreateFile
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

        // Win32 error codes
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_NO_MORE_ITEMS = 259;
        public const int ERROR_IO_PENDING = 997;
        public const int ERROR_OPERATION_ABORTED = 995;
        public const int WAIT_TIMEOUT = 258;

        // ------------------------------------------------------------------
        // Structures
        // ------------------------------------------------------------------

        /// <summary>SP_DEVINFO_DATA — 32 bytes on x64, 28 bytes on x86.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;

            public static SP_DEVINFO_DATA Create()
            {
                var d = new SP_DEVINFO_DATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                return d;
            }
        }

        /// <summary>SP_DEVICE_INTERFACE_DATA — 32 bytes on x64, 28 bytes on x86.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;

            public static SP_DEVICE_INTERFACE_DATA Create()
            {
                var d = new SP_DEVICE_INTERFACE_DATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                return d;
            }
        }

        /// <summary>
        /// SP_DEVICE_INTERFACE_DETAIL_DATA_W is a variable-length structure
        /// (<c>DWORD cbSize; WCHAR DevicePath[ANYSIZE_ARRAY];</c>). It is handled as a raw
        /// unmanaged buffer. The cbSize quirk: it must be the size of the *fixed* part of the
        /// struct as the C compiler sees it — 8 on x64 (4 + 2, padded to pointer alignment)
        /// and 6 on x86 — not the size of the allocated buffer.
        /// </summary>
        public static uint DeviceInterfaceDetailCbSize
        {
            get { return IntPtr.Size == 8 ? 8u : 6u; }
        }

        /// <summary>Byte offset of <c>DevicePath</c> inside SP_DEVICE_INTERFACE_DETAIL_DATA_W.</summary>
        public const int DeviceInterfaceDetailPathOffset = 4;

        /// <summary>OVERLAPPED — 32 bytes on x64, 20 bytes on x86.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        // ------------------------------------------------------------------
        // setupapi.dll
        // ------------------------------------------------------------------

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ------------------------------------------------------------------
        // cfgmgr32.dll
        // ------------------------------------------------------------------

        /// <summary>Returns a CONFIGRET value (CR_SUCCESS == 0).</summary>
        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_DevNode_Status(
            out uint status,
            out uint problemNumber,
            uint devInst,
            uint flags);

        // ------------------------------------------------------------------
        // kernel32.dll
        // ------------------------------------------------------------------

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        /// <summary>
        /// Overlapped variant: all buffers are raw unmanaged pointers so they remain valid
        /// until the I/O completes (METHOD_BUFFERED copies the output back at completion time).
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            IntPtr inBuffer,
            uint inBufferSize,
            IntPtr outBuffer,
            uint outBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetOverlappedResultEx(
            SafeFileHandle file,
            IntPtr overlapped,
            out uint numberOfBytesTransferred,
            uint milliseconds,
            [MarshalAs(UnmanagedType.Bool)] bool alertable);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetOverlappedResult(
            SafeFileHandle file,
            IntPtr overlapped,
            out uint numberOfBytesTransferred,
            [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

        [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateEvent(
            IntPtr eventAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool manualReset,
            [MarshalAs(UnmanagedType.Bool)] bool initialState,
            string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
