using System;
using System.Runtime.InteropServices;

namespace VRMonitorSwitch.Display
{
    /// <summary>
    /// Raw user32 display API surface used by <see cref="DisplayManager"/>.
    /// All struct layouts match the Windows SDK (wingdi.h) exactly; sizes are asserted by the
    /// scratch test harness: DISPLAYCONFIG_PATH_INFO = 72, DISPLAYCONFIG_MODE_INFO = 64,
    /// DISPLAYCONFIG_TARGET_DEVICE_NAME = 420, DISPLAYCONFIG_SOURCE_DEVICE_NAME = 84, DEVMODE = 220.
    /// </summary>
    internal static class DisplayNative
    {
        // ------------------------------------------------------------------------------------
        // EnumDisplayDevices / DISPLAY_DEVICE
        // ------------------------------------------------------------------------------------

        public const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        public const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;          // same bit as ATTACHED_TO_DESKTOP (SDK alias)
        public const uint DISPLAY_DEVICE_MULTI_DRIVER = 0x00000002;
        public const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
        public const uint DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008;
        public const uint DISPLAY_DEVICE_VGA_COMPATIBLE = 0x00000010;
        public const uint DISPLAY_DEVICE_REMOVABLE = 0x00000020;
        public const uint DISPLAY_DEVICE_DISCONNECT = 0x02000000;
        public const uint DISPLAY_DEVICE_REMOTE = 0x04000000;
        public const uint DISPLAY_DEVICE_MODESPRUNED = 0x08000000;

        /// <summary>EnumDisplayDevices flag: return the device interface name in DeviceID.</summary>
        public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public uint cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;

            public static DISPLAY_DEVICE Create()
            {
                var dd = new DISPLAY_DEVICE();
                dd.cb = (uint)Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                return dd;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumDisplayDevicesW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        // ------------------------------------------------------------------------------------
        // EnumDisplaySettingsEx / ChangeDisplaySettingsEx / DEVMODE
        // ------------------------------------------------------------------------------------

        public const int ENUM_CURRENT_SETTINGS = -1;
        public const int ENUM_REGISTRY_SETTINGS = -2;

        public const uint EDS_RAWMODE = 0x00000002;
        public const uint EDS_ROTATEDMODE = 0x00000004;

        // DEVMODE.dmFields
        public const uint DM_POSITION = 0x00000020;
        public const uint DM_DISPLAYORIENTATION = 0x00000080;
        public const uint DM_DISPLAYFIXEDOUTPUT = 0x20000000;
        public const uint DM_BITSPERPEL = 0x00040000;
        public const uint DM_PELSWIDTH = 0x00080000;
        public const uint DM_PELSHEIGHT = 0x00100000;
        public const uint DM_DISPLAYFLAGS = 0x00200000;
        public const uint DM_DISPLAYFREQUENCY = 0x00400000;

        // DEVMODE.dmDisplayOrientation
        public const uint DMDO_DEFAULT = 0;
        public const uint DMDO_90 = 1;
        public const uint DMDO_180 = 2;
        public const uint DMDO_270 = 3;

        // DEVMODE.dmDisplayFixedOutput
        public const uint DMDFO_DEFAULT = 0;
        public const uint DMDFO_STRETCH = 1;
        public const uint DMDFO_CENTER = 2;

        // ChangeDisplaySettingsEx flags
        public const uint CDS_UPDATEREGISTRY = 0x00000001;
        public const uint CDS_TEST = 0x00000002;
        public const uint CDS_FULLSCREEN = 0x00000004;
        public const uint CDS_GLOBAL = 0x00000008;
        public const uint CDS_SET_PRIMARY = 0x00000010;
        public const uint CDS_VIDEOPARAMETERS = 0x00000020;
        public const uint CDS_ENABLE_UNSAFE_MODES = 0x00000100;
        public const uint CDS_DISABLE_UNSAFE_MODES = 0x00000200;
        public const uint CDS_RESET = 0x40000000;
        public const uint CDS_RESET_EX = 0x20000000;
        public const uint CDS_NORESET = 0x10000000;

        // ChangeDisplaySettingsEx results
        public const int DISP_CHANGE_SUCCESSFUL = 0;
        public const int DISP_CHANGE_RESTART = 1;
        public const int DISP_CHANGE_FAILED = -1;
        public const int DISP_CHANGE_BADMODE = -2;
        public const int DISP_CHANGE_NOTUPDATED = -3;
        public const int DISP_CHANGE_BADFLAGS = -4;
        public const int DISP_CHANGE_BADPARAM = -5;
        public const int DISP_CHANGE_BADDUALVIEW = -6;

        public static string DispChangeToString(int result)
        {
            switch (result)
            {
                case DISP_CHANGE_SUCCESSFUL: return "DISP_CHANGE_SUCCESSFUL";
                case DISP_CHANGE_RESTART: return "DISP_CHANGE_RESTART";
                case DISP_CHANGE_FAILED: return "DISP_CHANGE_FAILED";
                case DISP_CHANGE_BADMODE: return "DISP_CHANGE_BADMODE";
                case DISP_CHANGE_NOTUPDATED: return "DISP_CHANGE_NOTUPDATED";
                case DISP_CHANGE_BADFLAGS: return "DISP_CHANGE_BADFLAGS";
                case DISP_CHANGE_BADPARAM: return "DISP_CHANGE_BADPARAM";
                case DISP_CHANGE_BADDUALVIEW: return "DISP_CHANGE_BADDUALVIEW";
                default: return "DISP_CHANGE_" + result.ToString();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL
        {
            public int x;
            public int y;

            public POINTL(int x, int y) { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECTL
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        /// <summary>
        /// DEVMODEW (220 bytes). The 16-byte union after dmDriverExtra/dmFields is expressed with the
        /// display-device variant (dmPosition, dmDisplayOrientation, dmDisplayFixedOutput); the printer
        /// variant (dmOrientation ... dmPrintQuality, eight shorts) has the same size.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;

            // union { struct { POINTL dmPosition; DWORD dmDisplayOrientation; DWORD dmDisplayFixedOutput; };
            //         struct { short dmOrientation ... short dmPrintQuality; } }
            public POINTL dmPosition;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;

            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;     // union with dmNup
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;

            public static DEVMODE Create()
            {
                var dm = new DEVMODE();
                dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
                dm.dmDriverExtra = 0;
                return dm;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumDisplaySettingsExW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ChangeDisplaySettingsExW")]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        /// <summary>Overload used to pass a NULL DEVMODE (commit pending changes: ChangeDisplaySettingsEx(NULL, NULL, NULL, 0, NULL)).</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ChangeDisplaySettingsExW")]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        // ------------------------------------------------------------------------------------
        // CCD (Connecting and Configuring Displays) API
        // ------------------------------------------------------------------------------------

        // QueryDisplayConfig flags
        public const uint QDC_ALL_PATHS = 0x00000001;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        public const uint QDC_DATABASE_CURRENT = 0x00000004;
        public const uint QDC_VIRTUAL_MODE_AWARE = 0x00000010;
        public const uint QDC_INCLUDE_HMD = 0x00000020;

        // SetDisplayConfig flags
        public const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
        public const uint SDC_TOPOLOGY_CLONE = 0x00000002;
        public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        public const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
        public const uint SDC_TOPOLOGY_SUPPLIED = 0x00000010;
        public const uint SDC_USE_DATABASE_CURRENT = SDC_TOPOLOGY_INTERNAL | SDC_TOPOLOGY_CLONE | SDC_TOPOLOGY_EXTEND | SDC_TOPOLOGY_EXTERNAL;
        public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        public const uint SDC_VALIDATE = 0x00000040;
        public const uint SDC_APPLY = 0x00000080;
        public const uint SDC_NO_OPTIMIZATION = 0x00000100;
        public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        public const uint SDC_ALLOW_CHANGES = 0x00000400;
        public const uint SDC_PATH_PERSIST_IF_REQUIRED = 0x00000800;
        public const uint SDC_FORCE_MODE_ENUMERATION = 0x00001000;
        public const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000;
        public const uint SDC_VIRTUAL_MODE_AWARE = 0x00008000;

        // DISPLAYCONFIG_PATH_INFO.flags
        public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
        public const uint DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008;

        public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;
        public const uint DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID = 0xFFFF;
        public const uint DISPLAYCONFIG_PATH_TARGET_MODE_IDX_INVALID = 0xFFFF;
        public const uint DISPLAYCONFIG_PATH_CLONE_GROUP_INVALID = 0xFFFF;

        // DISPLAYCONFIG_PATH_SOURCE_INFO.statusFlags
        public const uint DISPLAYCONFIG_SOURCE_IN_USE = 0x00000001;

        // DISPLAYCONFIG_PATH_TARGET_INFO.statusFlags
        public const uint DISPLAYCONFIG_TARGET_IN_USE = 0x00000001;
        public const uint DISPLAYCONFIG_TARGET_FORCIBLE = 0x00000002;
        public const uint DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_BOOT = 0x00000004;
        public const uint DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_PATH = 0x00000008;
        public const uint DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_SYSTEM = 0x00000010;
        public const uint DISPLAYCONFIG_TARGET_IS_HMD = 0x00000020;

        // DISPLAYCONFIG_TARGET_DEVICE_NAME.flags (bitfield packed in a DWORD)
        public const uint DISPLAYCONFIG_TARGET_DEVICE_NAME_FRIENDLY_NAME_FROM_EDID = 0x00000001;
        public const uint DISPLAYCONFIG_TARGET_DEVICE_NAME_FRIENDLY_NAME_FORCED = 0x00000002;
        public const uint DISPLAYCONFIG_TARGET_DEVICE_NAME_EDID_IDS_VALID = 0x00000004;

        // Win32 error codes returned by the CCD functions
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_GEN_FAILURE = 31;
        public const int ERROR_NOT_SUPPORTED = 50;
        public const int ERROR_INVALID_PARAMETER = 87;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_BAD_CONFIGURATION = 1610;

        public static string Win32ErrorToString(int code)
        {
            switch (code)
            {
                case ERROR_SUCCESS: return "ERROR_SUCCESS";
                case ERROR_ACCESS_DENIED: return "ERROR_ACCESS_DENIED";
                case ERROR_GEN_FAILURE: return "ERROR_GEN_FAILURE";
                case ERROR_NOT_SUPPORTED: return "ERROR_NOT_SUPPORTED";
                case ERROR_INVALID_PARAMETER: return "ERROR_INVALID_PARAMETER";
                case ERROR_INSUFFICIENT_BUFFER: return "ERROR_INSUFFICIENT_BUFFER";
                case ERROR_BAD_CONFIGURATION: return "ERROR_BAD_CONFIGURATION";
                default: return "Win32 error " + code.ToString();
            }
        }

        public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
        {
            OTHER = 0xFFFFFFFF,
            HD15 = 0,
            SVIDEO = 1,
            COMPOSITE_VIDEO = 2,
            COMPONENT_VIDEO = 3,
            DVI = 4,
            HDMI = 5,
            LVDS = 6,
            D_JPN = 8,
            SDI = 9,
            DISPLAYPORT_EXTERNAL = 10,
            DISPLAYPORT_EMBEDDED = 11,
            UDI_EXTERNAL = 12,
            UDI_EMBEDDED = 13,
            SDTVDONGLE = 14,
            MIRACAST = 15,
            INDIRECT_WIRED = 16,
            INDIRECT_VIRTUAL = 17,
            DISPLAYPORT_USB_TUNNEL = 18,
            INTERNAL = 0x80000000
        }

        /// <summary>True for the laptop's built-in panel connectors (INTERNAL, DISPLAYPORT_EMBEDDED, UDI_EMBEDDED).</summary>
        public static bool IsInternalOutputTechnology(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech)
        {
            return tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INTERNAL
                || tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EMBEDDED
                || tech == DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.UDI_EMBEDDED;
        }

        public enum DISPLAYCONFIG_ROTATION : uint
        {
            IDENTITY = 1,
            ROTATE90 = 2,
            ROTATE180 = 3,
            ROTATE270 = 4
        }

        public enum DISPLAYCONFIG_SCALING : uint
        {
            IDENTITY = 1,
            CENTERED = 2,
            STRETCHED = 3,
            ASPECTRATIOCENTEREDMAX = 4,
            CUSTOM = 5,
            PREFERRED = 128
        }

        public enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
        {
            UNSPECIFIED = 0,
            PROGRESSIVE = 1,
            INTERLACED = 2,
            INTERLACED_UPPERFIELDFIRST = INTERLACED,
            INTERLACED_LOWERFIELDFIRST = 3
        }

        public enum DISPLAYCONFIG_PIXELFORMAT : uint
        {
            PIXELFORMAT_8BPP = 1,
            PIXELFORMAT_16BPP = 2,
            PIXELFORMAT_24BPP = 3,
            PIXELFORMAT_32BPP = 4,
            PIXELFORMAT_NONGDI = 5
        }

        public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
        {
            SOURCE = 1,
            TARGET = 2,
            DESKTOP_IMAGE = 3
        }

        public enum DISPLAYCONFIG_TOPOLOGY_ID : uint
        {
            INTERNAL = 0x00000001,
            CLONE = 0x00000002,
            EXTEND = 0x00000004,
            EXTERNAL = 0x00000008
        }

        public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
        {
            GET_SOURCE_NAME = 1,
            GET_TARGET_NAME = 2,
            GET_TARGET_PREFERRED_MODE = 3,
            GET_ADAPTER_NAME = 4,
            SET_TARGET_PERSISTENCE = 5,
            GET_TARGET_BASE_TYPE = 6,
            GET_SUPPORT_VIRTUAL_RESOLUTION = 7,
            SET_SUPPORT_VIRTUAL_RESOLUTION = 8,
            GET_ADVANCED_COLOR_INFO = 9,
            SET_ADVANCED_COLOR_STATE = 10,
            GET_SDR_WHITE_LEVEL = 11
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;

            public long ToInt64()
            {
                return ((long)HighPart << 32) | LowPart;
            }

            public bool Equals(LUID other)
            {
                return LowPart == other.LowPart && HighPart == other.HighPart;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;

            public double ToDouble()
            {
                return Denominator == 0 ? 0.0 : (double)Numerator / Denominator;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_2DREGION
        {
            public uint cx;
            public uint cy;
        }

        /// <summary>20 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            /// <summary>
            /// Union: modeInfoIdx (UINT32) or { cloneGroupId:16; sourceModeInfoIdx:16 } when
            /// DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE is set. We never request virtual-mode-aware queries.
            /// </summary>
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        /// <summary>48 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            /// <summary>Union: modeInfoIdx (UINT32) or { desktopModeInfoIdx:16; targetModeInfoIdx:16 }.</summary>
            public uint modeInfoIdx;
            public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
            public DISPLAYCONFIG_ROTATION rotation;
            public DISPLAYCONFIG_SCALING scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)]
            public bool targetAvailable;
            public uint statusFlags;
        }

        /// <summary>72 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;

            public bool IsActive
            {
                get { return (flags & DISPLAYCONFIG_PATH_ACTIVE) != 0; }
            }
        }

        /// <summary>48 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            /// <summary>Union: videoStandard (UINT32) or AdditionalSignalInfo bitfield.</summary>
            public uint videoStandard;
            public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
        }

        /// <summary>48 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_TARGET_MODE
        {
            public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
        }

        /// <summary>20 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width;
            public uint height;
            public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
            public POINTL position;
        }

        /// <summary>40 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
        {
            public POINTL PathSourceSize;
            public RECTL DesktopImageRegion;
            public RECTL DesktopImageClip;
        }

        /// <summary>
        /// 64 bytes. infoType (4) + id (4) + adapterId (8) + union at offset 16 of
        /// DISPLAYCONFIG_TARGET_MODE (48) / DISPLAYCONFIG_SOURCE_MODE (20) / DISPLAYCONFIG_DESKTOP_IMAGE_INFO (40).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)]
            public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
            [FieldOffset(4)]
            public uint id;
            [FieldOffset(8)]
            public LUID adapterId;
            [FieldOffset(16)]
            public DISPLAYCONFIG_TARGET_MODE targetMode;
            [FieldOffset(16)]
            public DISPLAYCONFIG_SOURCE_MODE sourceMode;
            [FieldOffset(16)]
            public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
        }

        /// <summary>20 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        /// <summary>420 bytes.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            /// <summary>DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS bitfield packed in a DWORD.</summary>
            public uint flags;
            public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;

            public static DISPLAYCONFIG_TARGET_DEVICE_NAME Create(LUID adapterId, uint targetId)
            {
                var s = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                s.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME;
                s.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                s.header.adapterId = adapterId;
                s.header.id = targetId;
                return s;
            }
        }

        /// <summary>84 bytes.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;

            public static DISPLAYCONFIG_SOURCE_DEVICE_NAME Create(LUID adapterId, uint sourceId)
            {
                var s = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                s.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_SOURCE_NAME;
                s.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                s.header.adapterId = adapterId;
                s.header.id = sourceId;
                return s;
            }
        }

        [DllImport("user32.dll", SetLastError = false)]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern int SetDisplayConfig(
            uint numPathArrayElements,
            [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            uint flags);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);
    }
}
