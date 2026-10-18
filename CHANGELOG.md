# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-10-18

First public release. Verified on an ASUS Zenbook S 16 (Windows 11 25H2, AMD Radeon 890M) with a
Meta Quest 3 and Virtual Desktop: single, ultrawide (laptop screen off) and dual profiles, plus restore.

### Added

- Two-step wizard (laptop screen question, then profile choice) with a dark, DPI-aware WinForms UI
  in Spanish (default) and English.
- Three virtual-monitor profiles backed by the Parsec Virtual Display Driver:
  **Dual** (2 x 1920x1080), **Ultrawide** (1 x 5120x1440, 32:9) and **Single** (1 x 1920x1080),
  each with a list of alternative resolutions.
- Optional switch-off of the laptop's built-in screen for the duration of the VR session, with the
  answer remembered on request.
- Automatic restore of the previous display layout on exit, on tray "Restore", on Windows sign-out
  and on process exit; recovery banner and `--restore` for a laptop screen left off by a killed session.
- Windows attaches a brand-new monitor as a clone of the primary screen; the app switches to the
  extended topology before touching any display mode, so the laptop panel keeps its own resolution.
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
