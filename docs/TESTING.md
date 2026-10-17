# Manual test checklist (new machine)

Run through this list on every machine before trusting a release. Each line is a pass/fail item.
Keep `%LOCALAPPDATA%\VRMonitorSwitch\logs\vrmonitorswitch.log` open in a text editor that
auto-reloads; every step below writes to it.

Tested build: `VRMonitorSwitch-Setup-_____.exe`   Machine: __________   Date: ________

## 1. Installation

- [ ] Windows 10 2004+ / 11 x64 with no Parsec VDD installed (no subkey of `HKLM\SYSTEM\CurrentControlSet\Enum\ROOT\DISPLAY`
      has `HardwareID` = `Root\Parsec\VDA`; `--diagnose` reports `NotInstalled`).
- [ ] Run the setup. Exactly one UAC prompt. Wizard shows in the Windows UI language (Spanish or English).
- [ ] The "Installing the Parsec Virtual Display Driver..." status appears; Windows may show a driver trust dialog.
- [ ] After setup: `HKLM\SOFTWARE\Parsec\vdd\0..4` exist with `width/height/hz` DWORDs
      (5120x1440, 3840x1080, 3840x1600, 2560x1600, 1920x1200, all 60).
- [ ] Desktop shortcut and Start Menu entry exist. `{app}\redist\parsec-vdd-0.45.0.0.exe` exists.
- [ ] Setup compiled **without** the redist (CI artifact without `installer\redist`): a download page appears
      before installing, with the SHA-256 verified; disconnecting the network shows the "continue without driver?" prompt.
- [ ] Device Manager > Display adapters shows "Parsec Virtual Display Adapter" with no warning icon.
      If it shows a warning, reboot and check again (RestartRequired). Details > *Device instance path* is
      `ROOT\DISPLAY\000x` and *Hardware Ids* contains `Root\Parsec\VDA`.
- [ ] Run the setup a second time (upgrade): the setup log says the driver is already installed and the
      Parsec installer is **not** re-run; no "Windows may ask for a restart" message.
- [ ] Silent install (`/VERYSILENT`) of a setup compiled **without** the redist, with network: the driver is
      downloaded and installed anyway; `{app}\redist\parsec-vdd-0.45.0.0.exe` exists afterwards.

## 2. Driver status / diagnostics

- [ ] `VRMonitorSwitch.exe --diagnose` prints: driver status `Ok`, the driver (minor) version, every display with
      device name, adapter, internal/external, active, bounds and current mode, and the supported modes.
- [ ] Window scaling: drag the window between the laptop panel (150-200 %) and a 100 % external monitor; the
      window and the cards rescale (no huge/tiny window, no clipped card text).
- [ ] With the driver disabled in Device Manager, the app shows the "driver missing" banner with
      **Instalar driver**; re-enable the device and the banner goes away after the re-check.

## 3. Profiles (laptop screen kept ON)

Answer **No, dejarla encendida** on step 1, then for each profile:

| Profile   | Expected                                                        | Pass |
|-----------|-----------------------------------------------------------------|------|
| Dual      | 2 new monitors 1920x1080@60, side by side, first one is primary at (0,0); laptop screen at the far right | [ ] |
| Ultrawide | 1 new monitor 5120x1440@60 (32:9) primary; laptop screen to its right | [ ] |
| Single    | 1 new monitor 1920x1080@60 primary; laptop screen to its right  | [ ] |

- [ ] Changing the resolution in the combo box before clicking the card applies that mode
      (try 3840x1080 on Ultrawide and 2560x1440 on Single).
- [ ] If a mode is refused, the log shows the substituted closest mode and the session still completes.
- [ ] After apply: window hides, balloon "Perfil X activo..." appears, tray icon present.
- [ ] Windows **Settings > Display** shows the same layout the app reported.
- [ ] Headset test: in Virtual Desktop (or Quest Link / Steam Link) switch monitors; every virtual monitor
      streams and the mouse moves across them in the expected order.

## 4. Laptop screen OFF

Answer **Si, apagarla** on step 1 and pick **Dual**:

- [ ] Built-in screen turns off; the two virtual monitors remain active (Settings > Display lists only them).
- [ ] The app never leaves zero active displays (try on a desktop PC with no internal screen: step 1 is skipped
      and the option is ignored with a warning in the log).
- [ ] Check **Recordar mi respuesta**: next launch skips step 1; `settings.json` has
      `AskLaptopScreenEveryTime=false` and `LaptopScreenOffDefault=true`.
- [ ] Delete `settings.json`: step 1 is asked again.

## 5. Restore

- [ ] Tray > **Restaurar y salir**: laptop screen comes back, virtual monitors disappear, previous layout
      (positions, primary, resolution) is restored, process exits.
- [ ] Tray double-click does the same.
- [ ] Closing the window with **X** while active only minimizes to tray (session stays active).
- [ ] Tray > **Cambiar perfil**: window shows step 2 again; choosing another profile restores first, then applies.
- [ ] `VRMonitorSwitch.exe --restore` from a second console restores the running instance and exits it.
- [ ] Launching a second instance while one is running just brings the window to front.
- [ ] Tray double-click while **no** session is active shows the window (it does not exit the app).
- [ ] Restore → Apply back to back (tray **Cambiar perfil**, pick another profile): the log shows
      "Parsec targets departed after N ms" before "Display layout restored", and the second profile's virtual
      monitors come up **active** (no `err.noDisplays`).
- [ ] Kill the process from Task Manager while a profile is active: within ~1-2 s the driver drops the virtual
      monitors by itself (keep-alive stops). If the laptop screen stayed off, verify each of these brings it back:
      (a) run the app again: a banner "La pantalla de la laptop está apagada" appears with the button
      **Encender pantalla de la laptop**; (b) `VRMonitorSwitch.exe --restore` with no instance running prints
      "La pantalla de la laptop se volvió a encender." (exit code 0); (c) Win+P → "PC screen only".

## 6. CLI

- [ ] `VRMonitorSwitch.exe --profile ultrawide --mode 3840x1080@60 --laptop off` applies without the wizard
      and leaves the tray icon.
- [ ] `--profile single --laptop on` works without `--mode` (profile default mode is used).
- [ ] `--lang en` switches every string to English; `--lang es` back to Spanish.
- [ ] Unknown profile id shows the generic error (localized) and exits with a non-zero code.

## 7. Reboot / sign-out behaviour

- [ ] Sign out while a profile is active (laptop screen off): on sign-in the laptop screen is on and no
      virtual monitors remain (SessionEnding hook restored them).
- [ ] Reboot while a profile is active: after boot the layout is the pre-session one. The app is **not**
      auto-started (no startup entry is created).
- [ ] After reboot, `--diagnose` still reports driver status `Ok`.

## 8. Uninstall

- [ ] Uninstall from Apps & features while the app is running: the uninstaller asks to close it.
- [ ] After uninstall: app folder, shortcuts and `HKLM\SOFTWARE\Parsec\vdd\0..4` are gone.
- [ ] The Parsec Virtual Display Adapter is **still** in Device Manager (driver is intentionally kept).
- [ ] `%APPDATA%\VRMonitorSwitch\settings.json` and the logs remain (user data is not deleted).
- [ ] Reinstall over the previous version (upgrade path): no errors, settings preserved.
