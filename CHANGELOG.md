# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Recovery of a laptop screen left off by a killed session: a banner with **Encender pantalla de la laptop**
  when the built-in display is connected but inactive, and `--restore` performs the same recovery when no
  instance is running.
- `--diagnose` prints the driver (minor) version when the driver is `Ok`.
- Per-monitor DPI rescaling (`App.config` opt-in `DpiAwareness=PerMonitorV2`).

### Fixed

- Restore waits for the virtual monitors to leave the display topology before re-applying the snapshot and never
  saves a layout to the Windows connectivity database while a Parsec target is still connected (the saved
  "Parsec inactive" entry made the next profile come up with inactive monitors).
- If Windows turns the laptop screen off by itself when the virtual monitors appear (persisted entry) while the
  user chose to keep it on, it is turned back on.
- Installer: `VersionInfoVersion` accepts prerelease/CI versions; driver presence is detected through the
  `HardwareID` of `Enum\ROOT\DISPLAY\*` (the driver was re-installed on every upgrade); silent installs download
  the driver; the downloaded driver is kept in `{app}\redist`; the download-failure prompt shows the real reason;
  the setup refuses Windows on ARM (`x64os`).
- UI: the balloon warns when the laptop screen could not be turned off; tray double-click without an active
  session shows the window instead of exiting; a cancelled Windows shutdown no longer leaves the app without a
  tray icon; Escape closes an open resolution list instead of going back a step; clicking the "Resolución"
  caption opens the list instead of applying; CLI apply errors show the window before the dialog; driver status
  texts are localized; "Falta el driver" is only used when it is really missing; the driver download has timeouts.
- An unhandled UI exception no longer tears down the active VR session; a second instance can bring the first
  one to the foreground; an inaccessible single-instance mutex (elevated instance) is treated as "already
  running"; a cancelled driver IOCTL is waited for at most 2 s instead of forever.
- Mode fallback prefers the requested refresh rate instead of the fastest one; Spanish wording
  ("huella SHA-256", "Ultrapanorámico"); `SHA256SUMS.txt` lists only the two release assets.

## [0.1.0] - 2026-10-16

### Added

- Two-step wizard (laptop screen question, then profile choice) with a dark, DPI-aware WinForms UI
  in Spanish (default) and English.
- Three virtual-monitor profiles backed by the Parsec Virtual Display Driver:
  **Dual** (2 x 1920x1080), **Ultrawide** (1 x 5120x1440, 32:9) and **Single** (1 x 1920x1080),
  each with a list of alternative resolutions.
- Optional switch-off of the laptop's built-in screen for the duration of the VR session, with the
  answer remembered on request.
- Automatic restore of the previous display layout on exit, on tray "Restore", on Windows sign-out
  and on process exit.
- Resident tray icon that keeps the driver heartbeat alive while a profile is active
  (Show / Restore and exit / Change profile / Exit).
- Command-line switches: `--profile`, `--mode`, `--laptop`, `--diagnose`, `--restore`, `--lang`.
- Settings file `%APPDATA%\VRMonitorSwitch\settings.json` (last profile and resolution, extra modes,
  keep-alive interval, language).
- Log file `%LOCALAPPDATA%\VRMonitorSwitch\logs\vrmonitorswitch.log` with 2 MB rotation.
- Inno Setup installer (`VRMonitorSwitch-Setup-x.y.z.exe`) that installs the Parsec VDD 0.45 driver
  when it is missing (bundled, or downloaded and SHA-256 verified) and registers five extra
  resolution presets (5120x1440, 3840x1080, 3840x1600, 2560x1600, 1920x1200 at 60 Hz).
- Portable zip, local build script (`scripts/build.ps1`) and GitHub Actions workflow that publishes
  both artifacts on `v*` tags.

[Unreleased]: https://github.com/Tecneu/VRMonitorSwitch/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Tecneu/VRMonitorSwitch/releases/tag/v0.1.0
