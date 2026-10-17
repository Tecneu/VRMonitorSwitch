# VR Monitor Switch — Architecture & module contracts

Single-click Windows tool that prepares a set of **virtual monitors** for a VR headset
(Virtual Desktop, Meta Quest Link, Steam Link, Immersed…) and optionally **turns the laptop's
built-in screen off** while the VR session lasts. Everything is restored when the session ends.

* Language/runtime: **C# 7.3+ / .NET Framework 4.8**, WinForms, single `VRMonitorSwitch.exe`,
  no NuGet dependencies at runtime (4.8 ships with Windows 10 1903+ and Windows 11).
* Runs **as the user (asInvoker)**. No UAC prompt at runtime. The only privileged step is
  installing the display driver, done once by the installer.
* Virtual monitors are provided by the **Parsec Virtual Display Driver (Parsec VDD)**
  `parsec-vdd-0.45.0.0.exe` (NSIS, silent flag `/S`, SHA-256
  `E23332448FDAF5AA017CB308DB5EF6855FAC526A7DED05D80C039404126D5362`, signed by
  "Parsec Cloud, Inc."). Download URL: `https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe`
  (fallback 0.41: `https://builds.parsec.app/vdd/parsec-vdd-0.41.0.0.exe`).
* The driver is controlled through `DeviceIoControl` exactly as documented by
  https://github.com/nomi-san/parsec-vdd (`core/parsec-vdd.h`). Key facts:
  * Device interface GUID `{00b41627-04c4-429e-a26e-0265cf50c8fa}`; class GUID (Display)
    `{4d36e968-e325-11ce-bfc1-08002be10318}`; hardware id `Root\Parsec\VDA`;
    adapter name `Parsec Virtual Display Adapter`; monitor name `ParsecVDA`; EDID id `PSCCDD0`.
  * Open with `SetupDiGetClassDevs(&guid, NULL, NULL, DIGCF_PRESENT|DIGCF_DEVICEINTERFACE)` →
    `SetupDiEnumDeviceInterfaces` → `SetupDiGetDeviceInterfaceDetail` → `CreateFile(path,
    GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, OPEN_EXISTING,
    FILE_ATTRIBUTE_NORMAL|FILE_FLAG_NO_BUFFERING|FILE_FLAG_OVERLAPPED|FILE_FLAG_WRITE_THROUGH)`.
  * IOCTLs: `ADD=0x0022e004`, `REMOVE=0x0022a008`, `UPDATE=0x0022a00c`, `VERSION=0x0022e010`.
    Input buffer is always 32 zeroed bytes (payload copied at offset 0), output is one DWORD,
    overlapped I/O with a 5 s `GetOverlappedResultEx` timeout. `ADD` returns the display index.
    `REMOVE` takes the index as **16-bit big-endian** in the first two input bytes.
  * **Keep-alive**: `UPDATE` must be sent at least every 100 ms (we use 50 ms) or the driver
    unplugs every display after ~1 s. This is why the app stays resident (tray) during a session.
  * Up to 16 displays per adapter; we never use more than 3.
  * Remove displays **right-to-left** (highest index first) to avoid the Windows 10 connectivity
    cache quirk (nomi-san issue #23).
  * Preset modes include 1920x1080, 2560x1440, 3840x2160, 2560x1080, 3440x1440 (24/30/60/144/240 Hz).
    Extra modes (max 5) are read from `HKLM\SOFTWARE\Parsec\vdd\<0..4>` with DWORD values
    `width`, `height`, `hz` **when a display is added**. The installer writes:
    `0: 5120x1440@60`, `1: 3840x1080@60`, `2: 3840x1600@60`, `3: 2560x1600@60`, `4: 1920x1200@60`.

## Folder layout

```
VRMonitorSwitch/
├─ src/VRMonitorSwitch/            # the app (SDK-style csproj, net48, WinForms)
│  ├─ Program.cs                   # entry point, single instance, CLI switches
│  ├─ Core/                        # profiles, settings, localization, session orchestration
│  ├─ Vdd/                         # Parsec VDD client (SetupAPI + DeviceIoControl)
│  ├─ Display/                     # Win32 display APIs (modes, arrangement, enable/disable)
│  └─ UI/                          # WinForms: wizard window + tray
├─ installer/VRMonitorSwitch.iss   # Inno Setup 6 script (admin; installs driver + registry presets)
├─ scripts/build.ps1               # local build: dotnet publish + ISCC → dist/
├─ .github/workflows/build.yml     # CI build on push; release assets on tag v*
├─ assets/icon.ico
└─ docs/ARCHITECTURE.md            # this file
```

## Public contracts (must match exactly — files are written by different people)

### `Core/Profiles.cs` — namespace `VRMonitorSwitch.Core`

```csharp
public enum ProfileKind { DualStandard = 0, UltraWide = 1, SingleStandard = 2 }

public sealed class DisplayMode : IEquatable<DisplayMode>
{
    public int Width { get; set; }  public int Height { get; set; }  public int Hz { get; set; }
    public DisplayMode() {}  public DisplayMode(int w, int h, int hz) {...}
    public string AspectLabel { get; }       // "16:9", "21:9", "32:9", "16:10", "24:10"
    public override string ToString();       // "5120 × 1440 @ 60 Hz (32:9)"
    public static DisplayMode Parse(string s);   // "5120x1440@60" (hz optional → 60)
    public string ToKey();                       // "5120x1440@60"
    // Equals/GetHashCode by value
}

public sealed class Profile
{
    public ProfileKind Kind { get; }
    public string Id { get; }               // "dual", "ultrawide", "single"
    public int MonitorCount { get; }        // 2, 1, 1
    public DisplayMode DefaultMode { get; } // 1920x1080@60, 5120x1440@60, 1920x1080@60
    public IReadOnlyList<DisplayMode> Modes { get; }   // DefaultMode first, then alternatives
}

public static class Profiles
{
    public static IReadOnlyList<Profile> All { get; }
    public static Profile Get(ProfileKind kind);
    public static Profile FindById(string id);   // null if unknown
}
```

Alternatives: Dual → 1920x1080, 2560x1440, 1600x900, 1280x720 (all 60 Hz).
UltraWide → 5120x1440, 3840x1080, 3840x1600, 3440x1440, 2560x1080 (60 Hz).
Single → 1920x1080, 2560x1440, 3840x2160, 1920x1200, 2560x1600 (60 Hz).
Also expose 72 and 90 Hz variants only via `AppSettings.ExtraModes` (power users), not in the default list.

### `Core/AppSettings.cs`

```csharp
public sealed class AppSettings
{
    public string LastProfileId { get; set; }                       // "dual" | "ultrawide" | "single"
    public Dictionary<string,string> LastModeByProfile { get; set; } // profileId → "WxH@Hz"
    public bool AskLaptopScreenEveryTime { get; set; } = true;
    public bool LaptopScreenOffDefault { get; set; } = true;        // used when not asking / preselected answer
    public List<string> ExtraModes { get; set; }                     // "WxH@Hz" appended to every profile list
    public int KeepAliveIntervalMs { get; set; } = 50;
    public int DisplayAppearTimeoutMs { get; set; } = 15000;
    public string Language { get; set; }                             // null = auto, "es", "en"
    public static string FilePath { get; }     // %APPDATA%\VRMonitorSwitch\settings.json
    public static AppSettings Load();          // never throws; returns defaults on any error
    public void Save();                        // never throws (swallow + Log)
}
```

Serialize with `System.Web.Script.Serialization.JavaScriptSerializer` (in `System.Web.Extensions`,
already referenced) or `DataContractJsonSerializer`; **no NuGet**.

### `Core/Log.cs`

```csharp
public static class Log
{
    public static string FilePath { get; }        // %LOCALAPPDATA%\VRMonitorSwitch\logs\vrmonitorswitch.log
    public static void Info(string msg);  public static void Warn(string msg);
    public static void Error(string msg, Exception ex = null);
    public static event Action<string> LineWritten;   // UI can show the live log
}
```
Thread-safe, rotates at 2 MB (keep one `.1` backup). Never throws.

### `Core/Strings.cs` — localization (Spanish default, English fallback)

```csharp
public static class Strings
{
    public static string Culture { get; }           // "es" or "en" resolved at startup
    public static void Init(string forcedLanguage); // null → CurrentUICulture.TwoLetterISOLanguageName; anything not "es" → "en"
    public static string Get(string key);           // returns key itself if missing
    public static string Format(string key, params object[] args);
}
```
Keys used by the UI (both languages must exist): `app.title`, `step.laptop.title`, `step.laptop.question`,
`step.laptop.off`, `step.laptop.on`, `step.laptop.hint`, `step.profile.title`, `profile.dual.name`,
`profile.dual.desc`, `profile.ultrawide.name`, `profile.ultrawide.desc`, `profile.single.name`,
`profile.single.desc`, `btn.apply`, `btn.back`, `btn.cancel`, `btn.restore`, `btn.exit`, `btn.installDriver`,
`status.idle`, `status.applying`, `status.active`, `status.restoring`, `status.error`, `driver.missing.title`,
`driver.missing.text`, `tray.show`, `tray.restore`, `tray.change`, `tray.exit`, `tray.active`,
`msg.applied`, `msg.restored`, `msg.confirmExit`, `err.generic`, `err.noDisplays`, `err.internalNotFound`,
`label.resolution`, `label.monitors`, `label.remember`.

### `Vdd/ParsecVdd.cs` — namespace `VRMonitorSwitch.Vdd`

```csharp
public enum VddStatus { Ok, Inaccessible, Unknown, UnknownProblem, Disabled, DriverError, RestartRequired, DisabledService, NotInstalled }

public sealed class ParsecVdd : IDisposable
{
    public const string DriverDownloadUrl = "https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe";
    public const string DriverSha256 = "E23332448FDAF5AA017CB308DB5EF6855FAC526A7DED05D80C039404126D5362";
    public const string AdapterName = "Parsec Virtual Display Adapter";
    public const string MonitorName = "ParsecVDA";
    public const int MaxDisplays = 8;

    public static VddStatus QueryStatus();            // port of QueryDeviceStatus() from parsec-vdd.h
    public static bool IsInstalled { get; }           // QueryStatus() != NotInstalled

    public bool IsOpen { get; }
    public int Version { get; }                       // minor version, valid after Open()
    public IReadOnlyList<int> Displays { get; }       // indices currently added by this instance
    public int KeepAliveIntervalMs { get; set; }      // default 50

    public void Open();                               // throws InvalidOperationException with a localized-friendly message
    public int AddDisplay();                          // returns index; throws on failure (-1)
    public void RemoveDisplay(int index);
    public void RemoveAll();                          // highest index first
    public void Dispose();                            // RemoveAll + stop keep-alive + CloseHandle
}
```
Implementation notes: keep-alive runs on a dedicated background thread (`IsBackground = true`,
`Priority = AboveNormal`) started in `Open()`; `DeviceIoControl` must be called with an
`OVERLAPPED` + manual-reset event and `GetOverlappedResultEx(…, 5000, false)` just like the header,
with every IOCTL serialized through one `lock` (the keep-alive thread and callers share the handle).
Zero the 32-byte input buffer every call. Use `SafeFileHandle` from `CreateFile`.

### `Display/DisplayManager.cs` — namespace `VRMonitorSwitch.Display`

```csharp
public sealed class DisplayInfo
{
    public string DeviceName { get; set; }      // @"\\.\DISPLAY3" (GDI name used by ChangeDisplaySettingsEx)
    public string AdapterString { get; set; }   // e.g. "AMD Radeon(TM) 890M Graphics" / "Parsec Virtual Display Adapter"
    public string MonitorName { get; set; }     // friendly name from DISPLAYCONFIG_TARGET_DEVICE_NAME (may be empty)
    public string MonitorDevicePath { get; set; }
    public bool IsActive { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsInternal { get; set; }        // outputTechnology INTERNAL (0x80000000), DISPLAYPORT_EMBEDDED (11) or UDI_EMBEDDED (13) — never 9 (that is SDI)
    public bool IsParsecVirtual { get; set; }   // AdapterString == ParsecVdd.AdapterName || MonitorName == "ParsecVDA" || device path contains "PSCCDD0"
    public System.Drawing.Rectangle Bounds { get; set; }
    public VRMonitorSwitch.Core.DisplayMode CurrentMode { get; set; }   // null if inactive
    public long AdapterIdValue { get; set; }    // LUID packed (HighPart<<32 | LowPart)
    public uint TargetId { get; set; }
    public uint SourceId { get; set; }
}

public sealed class DisplayConfigSnapshot   // opaque; holds the DISPLAYCONFIG_PATH_INFO[] / MODE_INFO[] of active paths
{
    public DateTime TakenAt { get; }
    public int PathCount { get; }
}

public static class DisplayManager
{
    public static List<DisplayInfo> GetDisplays(bool includeInactive);
    public static DisplayInfo FindInternal(List<DisplayInfo> displays);          // null if none (desktop PC)
    public static List<DisplayInfo> WaitForParsecDisplays(int expectedCount, TimeSpan timeout);
                                                                                // polls every 250 ms until ≥ expectedCount active Parsec displays, throws TimeoutException
    public static List<VRMonitorSwitch.Core.DisplayMode> GetSupportedModes(string deviceName);   // EnumDisplaySettingsEx, 32bpp, distinct, sorted desc
    public static void SetMode(string deviceName, VRMonitorSwitch.Core.DisplayMode mode);        // ChangeDisplaySettingsEx(CDS_UPDATEREGISTRY); throws with DISP_CHANGE_* text
    public static void ArrangeHorizontally(IList<string> deviceNamesLeftToRight, string primaryDeviceName);
                                                                                // sets positions with CDS_UPDATEREGISTRY|CDS_NORESET (+CDS_SET_PRIMARY at 0,0), then ChangeDisplaySettingsEx(null, …) to commit
    public static DisplayConfigSnapshot Snapshot();                              // QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)
    public static void Restore(DisplayConfigSnapshot snapshot);                  // If no Parsec target is connected: SetDisplayConfig(paths, modes, SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_ALLOW_CHANGES|SDC_SAVE_TO_DATABASE), on failure retry without SAVE_TO_DATABASE. While a Parsec target is still connected: only the no-database variant (persisting "Parsec inactive" for that set would break the next Apply). Last resort SetTopology(TopologyKind.Extend)
    public static void SetInternalDisplayEnabled(bool enabled);                  // see algorithm below
    public static void SetTopology(TopologyKind kind);                           // SDC_APPLY | SDC_TOPOLOGY_*
}

public enum TopologyKind { Internal, Clone, Extend, External }
```

**Disable/enable the internal display (`SetInternalDisplayEnabled`)**
1. `QueryDisplayConfig(QDC_ALL_PATHS)` → paths + modes.
2. Find path(s) whose target `outputTechnology` is internal/embedded (via `DisplayConfigGetDeviceInfo`
   `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME`, same adapterId/targetId) — if none, throw
   `InvalidOperationException` ("no internal display").
3. To **disable**: require at least one other *active* path whose target is available
   (`targetInfo.targetAvailable`), otherwise throw (never leave the user with zero screens).
   Clear `DISPLAYCONFIG_PATH_ACTIVE` on the internal path(s); set their `sourceInfo.modeInfoIdx`
   and `targetInfo.modeInfoIdx` to `DISPLAYCONFIG_PATH_MODE_IDX_INVALID (0xFFFFFFFF)`.
   If the internal display was primary, the remaining active source whose position is leftmost
   gets moved to (0,0) by shifting all remaining source mode positions.
   Call `SetDisplayConfig(paths, modes, SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_ALLOW_CHANGES|SDC_SAVE_TO_DATABASE)`.
   On `ERROR_INVALID_PARAMETER`/`ERROR_BAD_CONFIGURATION` retry with `SDC_VALIDATE` first to log, then
   retry with `SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_ALLOW_CHANGES` (no DB save),
   then as a last resort `SetTopology(TopologyKind.External)`.
4. To **enable**: set `DISPLAYCONFIG_PATH_ACTIVE` on the first internal path that has
   `targetAvailable`, leave mode indexes INVALID (let Windows pick the preferred mode), apply with
   the same flags; fall back to `SetTopology(TopologyKind.Extend)`.
5. All P/Invoke structs must be the **Windows SDK layouts** (`DISPLAYCONFIG_PATH_INFO` = 72 bytes,
   `DISPLAYCONFIG_MODE_INFO` = 64 bytes, `DISPLAYCONFIG_TARGET_DEVICE_NAME` = 420 bytes,
   `DISPLAYCONFIG_SOURCE_DEVICE_NAME` = 84 bytes). Use `[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]`
   with explicit union handling (`DISPLAYCONFIG_MODE_INFO` union → `[FieldOffset]` explicit layout).

### `Core/SessionController.cs`

```csharp
public enum SessionState { Idle, Applying, Active, Restoring, Faulted }

public enum RecoveryResult { AlreadyOn, Restored, NoInternalDisplay }

public sealed class SessionController : IDisposable
{
    public SessionController(AppSettings settings);
    public SessionState State { get; }
    public Profile ActiveProfile { get; }  public DisplayMode ActiveMode { get; }  public bool LaptopScreenOff { get; }
    public event Action<string> Progress;             // human readable step messages (localized)
    public event Action<SessionState> StateChanged;   // raised on any transition (may come from a worker thread)

    public void Apply(Profile profile, DisplayMode mode, bool turnOffLaptopScreen);  // synchronous; call from a worker thread
    public void Restore();                                                            // idempotent; never throws (logs)
    public void Dispose();                                                            // Restore()

    public static RecoveryResult RecoverInternalDisplay();  // state-independent: if the built-in display is connected but
                                                            // inactive, SetInternalDisplayEnabled(true) (fallback SetTopology(Extend)); throws when both fail
}
```

**Apply algorithm** (every step logged and reported through `Progress`):
1. `ParsecVdd.QueryStatus()` must be `Ok` (else throw `DriverNotReadyException : Exception` carrying the status).
2. `snapshot = DisplayManager.Snapshot()`; remember the internal display (may be null on a desktop PC →
   `turnOffLaptopScreen` is ignored with a warning) and the number of Parsec targets already connected
   (`GetDisplays(true)`), which the restore wait below treats as foreign.
3. `vdd.Open()`; add `profile.MonitorCount` displays, 400 ms apart; `WaitForParsecDisplays(count, timeout)`.
   If the laptop screen is to be kept on, re-query `GetDisplays(true)`: when Windows re-applied a persisted
   "internal inactive" entry on the hot-plug, call `SetInternalDisplayEnabled(true)` (logged, non-fatal).
4. For each new Parsec display: `SetMode(device, mode)` — if it fails, try `GetSupportedModes` and pick the
   closest ≤ requested (same aspect ratio preferred, then the refresh rate closest to the request without
   exceeding it; when nothing fits, the smallest mode with the same refresh-rate preference); log the substitution.
5. `ArrangeHorizontally(parsecDevicesLeftToRight, primary: first parsec device)` so the virtual monitors
   sit at (0,0) and to the right, and the laptop screen (if kept) goes to the far right.
6. If `turnOffLaptopScreen && internal != null`: `SetInternalDisplayEnabled(false)`. **Non-fatal**: on failure the
   session stays Active with the screen on, `LaptopScreenOff` stays false and the UI shows a warning instead of
   the plain success balloon.
7. State = Active. If **anything else throws after step 3**, run `Restore()` and rethrow.

**Restore algorithm**: State = Restoring → if the internal display was turned off, `SetInternalDisplayEnabled(true)`
(and if that throws, `SetTopology(Extend)`) → `vdd.RemoveAll()` → `vdd.Dispose()` → **wait for the Parsec targets to
leave the CCD topology** (poll `GetDisplays(true)` every 250 ms, bounded to 5 s, until no more Parsec targets than
before the session remain; the REMOVE IOCTLs return long before the IddCx monitor departure is processed) → try
`DisplayManager.Restore(snapshot)` (ignore failures, the virtual targets are gone so Windows keeps the pre-session
layout) → State = Idle.
Also hook `SystemEvents.SessionEnding` and `AppDomain.CurrentDomain.ProcessExit` in `Program.cs` to call `Restore()`.
`Application.ThreadException` only logs and shows a dialog: the process keeps running, so a benign UI exception must
not tear down the VR session.

**Recovery without session state** (`RecoverInternalDisplay`): a fresh process has no snapshot and no `_internalOff`,
so `Restore()` cannot help when the laptop screen was left off by a killed session. The GUI shows a banner with
**Encender pantalla de la laptop** whenever the built-in display is connected but inactive and no session is running;
`--restore` with no running instance performs the same recovery. Stale virtual displays are not touched (the driver
unplugs them ~1 s after their keep-alive stops).

### `Program.cs` CLI (all optional; no switches → GUI)

```
VRMonitorSwitch.exe                         GUI wizard
VRMonitorSwitch.exe --profile dual|ultrawide|single [--mode 5120x1440@60] [--laptop off|on]
                                            apply immediately (still shows the tray; GUI hidden)
VRMonitorSwitch.exe --diagnose              print displays, driver status, modes to stdout (AttachConsole) and exit
VRMonitorSwitch.exe --restore               tell a running instance to restore & exit; with no instance running,
                                            SessionController.RecoverInternalDisplay() (turn the laptop screen back on)
VRMonitorSwitch.exe --lang es|en
```
Single instance via named mutex `Local\VRMonitorSwitch.SingleInstance`; a second launch without
`--restore` just brings the running window to front (WM_COPYDATA or a named event — keep it simple:
named `EventWaitHandle` "Local\VRMonitorSwitch.Show" and "Local\VRMonitorSwitch.Restore"). The second instance
calls `AllowSetForegroundWindow(primaryPid)` before signalling so the primary may actually take the foreground.
`UnauthorizedAccessException`/`IOException` from the mutex constructor mean "exists but not openable" (e.g. an
elevated instance) and are treated as *secondary instance*, never as primary.

### `UI/MainForm.cs`

One window, **two steps in the same form** (panel swap), dark flat theme, large click targets,
Segoe UI, DPI-aware (AutoScaleMode.Dpi **plus** `App.config` →
`<System.Windows.Forms.ApplicationConfigurationSection><add key="DpiAwareness" value="PerMonitorV2"/>`: on
.NET Framework 4.7+ the manifest alone makes the OS treat the window as PerMonitorV2 while WinForms ignores
WM_DPICHANGED and reports `DeviceDpi` = 96; the config opt-in enables the rescaling), min 720×520:

1. **Step 1 – laptop screen**: title + question *"¿Quieres apagar la pantalla de la laptop mientras usas el visor?"*
   with two big buttons **"Sí, apagarla"** / **"No, dejarla encendida"**. A checkbox
   *"Recordar mi respuesta y no volver a preguntar"* (writes `AskLaptopScreenEveryTime=false`).
   If the machine has no internal display, skip this step automatically.
2. **Step 2 – profile**: three big cards (Buttons with icon-like glyph drawn in code: two rectangles / one wide
   rectangle / one rectangle), name + description + a `ComboBox` with that profile's resolutions
   (preselect last used). Clicking a card applies immediately (no extra Apply button needed, but keep a
   **Volver** link). While applying show a progress label + marquee `ProgressBar`, disable the cards.
3. On success: hide to tray with balloon *"Perfil X activo. Haz doble clic para restaurar."*
   Tray menu: **Mostrar**, **Restaurar y salir**, **Cambiar perfil**, **Salir**.
   Double-click tray → Restore and exit while Active; in any other state it shows the window. Closing the
   window (X) while Active → minimize to tray (not exit).
4. If the driver is missing/not ready: banner with **Instalar driver** button → `ShellExecute(runas)` the
   bundled installer (`<appdir>\redist\parsec-vdd-0.45.0.0.exe /S`) if present, else download to %TEMP%
   (bounded timeouts) with SHA-256 check, then run it elevated, then re-check status. Title
   `driver.missing.title` only for `NotInstalled`; every other status uses `driver.notReady.title`.
5. If the built-in display is connected but inactive and no session is running: banner with
   **Encender pantalla de la laptop** → `SessionController.RecoverInternalDisplay()` on a worker thread.
6. All text via `Strings.Get`.

### Installer (`installer/VRMonitorSwitch.iss`)
* `AppId={{B6A6B1F6-3E0C-4A9D-9C5E-6F2B1D7F0A11}`, `PrivilegesRequired=admin`, `ArchitecturesAllowed=x64os` and
  `ArchitecturesInstallIn64BitMode=x64os` (not `x64compatible`: the Parsec VDD is an x64 kernel driver and cannot load
  on Windows on ARM; needs Inno Setup 6.3+), `MinVersion=10.0.19041`, `DefaultDirName={autopf}\VR Monitor Switch`,
  Spanish + English languages, `WizardStyle=modern`, `Compression=lzma2/ultra64`, `SolidCompression=yes`,
  `OutputBaseFilename=VRMonitorSwitch-Setup-{#AppVersion}`. `VersionInfoVersion` must be numeric, so the script derives
  `AppVersionNumeric` (semver without the prerelease suffix + `.0`) in ISPP and keeps the full semver in
  `VersionInfoProductTextVersion` (CI builds pass `0.0.0-ci`).
* Files: `..\dist\app\*` → `{app}`; `redist\parsec-vdd-0.45.0.0.exe` → `{app}\redist` (flag `external skipifsourcedoesntexist`).
* `[Registry]`: `HKLM\SOFTWARE\Parsec\vdd\0..4` with the five DWORD triplets above (`uninsdeletekey`).
* `[Code]` (`ssPostInstall`): driver install `{app}\redist\parsec-vdd-0.45.0.0.exe /S` **only if** `ParsecVdd` is not
  installed. The Parsec setup creates a root-enumerated *Display*-class device (`nefcon --create-device-node`,
  `DICD_GENERATE_ID`), so the devnode is `HKLM\SYSTEM\CurrentControlSet\Enum\ROOT\DISPLAY\000x` and `Root\Parsec\VDA`
  is its `HardwareID` REG_MULTI_SZ value, not a key path: enumerate `Enum\ROOT\DISPLAY\*` and compare `HardwareID`
  (same as `ParsecVdd.QueryStatus`); `Enum\Root\Parsec` is kept only as a legacy fallback. If the redist is missing,
  download it (download page in interactive mode, `DownloadTemporaryFile` from `PrepareToInstall` in silent mode),
  verify SHA-256 and keep the verified copy in `{app}\redist`.
* Tasks: desktop icon (checked by default), start-menu entry, optional "start automatically" **not** included.
* `[UninstallRun]` optional driver removal is **not** done (other apps may use Parsec VDD); say so in README.

### CI (`.github/workflows/build.yml`)
* `on: push` (branches main, tags `v*`), `pull_request`, `workflow_dispatch`.
* `windows-latest`, `actions/setup-dotnet@v4` (8.0.x), `dotnet build -c Release`, `dotnet publish -c Release -o dist/app`.
* Download the Parsec VDD exe into `installer/redist/` and verify the SHA-256 (fail the build on mismatch).
* `choco install innosetup -y --no-progress` then `ISCC installer\VRMonitorSwitch.iss /DAppVersion=<tag without v>`.
* Zip `dist/app` as `VRMonitorSwitch-<ver>-portable.zip`.
* On tags: `softprops/action-gh-release@v2` with both artifacts + auto release notes. Always upload artifacts.

### Local build (`scripts/build.ps1`)
`-Version 0.1.0` → `dotnet publish` → `dist/app`, download redist if absent (verify hash), `ISCC` → `dist/VRMonitorSwitch-Setup-0.1.0.exe`, zip portable.
Uses `dotnet` from PATH or `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`; ISCC from `${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe`.
