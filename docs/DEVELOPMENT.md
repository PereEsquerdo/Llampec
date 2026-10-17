# Developing Llampec

Llampec is a .NET 10 WinUI 3 app for Windows 11 24H2+, built for ARM64 and x64. The
logic lives in `Llampec.Core`, independent of the UI; `Llampec.App` is the panel. See
[architecture](ARCHITECTURE.md) and [adding an action](ADDING_AN_ACTION.md).

## Build and run

Development requires the .NET 10 SDK and the Windows build tools required by the
Windows App SDK. Dependencies are pinned in the projects and restored from NuGet.

```powershell
dotnet build Llampec.sln
dotnet run --project src/Llampec.App
```

`tools/run.ps1` closes a running development build normally, rebuilds and starts it.
Run it from PowerShell 7 (`pwsh`): Windows PowerShell 5.1 on ARM64 reports an x64
system and would build the x64 app.

## Packages

```powershell
./tools/package.ps1 -RuntimeIdentifier win-arm64
./tools/package.ps1 -RuntimeIdentifier win-x64
```

The script publishes Release, verifies the executable and the required WinUI PRI/XBF
resources, includes the README, license, documentation and dependency notices, and
creates `publish/Llampec-<version>-<architecture>.zip`. With Inno Setup 6 installed
(`winget install JRSoftware.InnoSetup`) it also builds the per-user installer
`publish/Llampec-<version>-win-<architecture>-setup.exe` from `installer/Llampec.iss`.
It refuses to reuse an existing output folder.

For a development publish without packaging:

```powershell
dotnet publish src/Llampec.App -c Release -r win-arm64 --self-contained false -o publish/win-arm64
./tools/test-publish.ps1 -PublishDirectory publish/win-arm64
```

## Tests

The tests CI runs, without touching the active display setup:

```powershell
dotnet test tests/Llampec.Core.Tests -c Release --filter "FullyQualifiedName!~SystemActionTests&FullyQualifiedName!~HdrDisplaysTests&FullyQualifiedName!~ProjectionModesTests"
```

The excluded integration tests inspect the real Windows session, and
`ProjectionModesTests` reapplies the current display topology; run them explicitly on
suitable hardware. The opt-in native smoke test for Always on Top uses its own
synthetic windows:

```powershell
dotnet run --project tests/Llampec.AlwaysOnTop.Smoke -c Release
```

Hardware-changing tests stay opt-in. Features that talk to real devices (DDC/CI
monitors, Logitech mice, rotation, agentic work) are also checked by hand; each
feature's document records what was measured and what is still open.

## Validation records

- **2026-10-17, ARM64 (Surface, build 28000):** 549 Core tests passed (1 opt-in
  skipped); the Always on Top smoke test passed 89 native checks. ARM64 and x64 Debug
  builds without errors. External monitor, agentic work and monitor recovery were
  checked on a Philips 329M1RV behind a USB4 hub; see [external monitor](MONITOR.md)
  and [agentic work](AGENT_MODE.md) for the measurements and open items.
- **2026-10-10, x64 (Intel Core i7-1065G7, build 26200, commit `85af722`):** the full
  Core suite passed (498 passed, 1 opt-in skipped), the native fullscreen test and
  the Always on Top smoke test (75 checks) passed, and the x64 Release package passed
  the resource check. In the background the published build idled at about 5 MB
  private working set, later about 40 MB, probably after opening the panel. Not yet
  covered on x64: the Logitech module with real hardware, touchscreen dragging in the
  editor, tablet controls and the touch keyboard/touchpad buttons.
- **2026-10-06, ARM64:** 476 tests passed with the CI filter (486 in the full run);
  ARM64 and x64 Release packages built without warnings. Mouse and keyboard checks
  covered the panel editor, Always on Top with on-demand window tracking and Logitech
  reconnection. Physical touchscreen dragging in the editor is still untested.

## Diagnostics

`--diagnostics` writes a local log for that session. `--theme=dark` or `--theme=light`
previews a panel theme on a fresh launch without changing the saved preference.

## Feature documents

- [Always on Top](ALWAYS_ON_TOP.md) · [Full screen](FULLSCREEN.md) · [Panel layout](PANEL_LAYOUT.md)
- [External monitor](MONITOR.md) · [Agentic work](AGENT_MODE.md) · [Tablet controls and orientation](TABLET.md)
- [Theme scheduling](THEME_SCHEDULE.md) · [Caffeine mode](CAFFEINE.md) · [Power](POWER.md)
- [Camera](CAMERA.md) · [Screenshot](SCREENSHOT.md) · [Touch keyboard and virtual touchpad](TOUCH_INPUT.md)
- [Logitech MX settings and service](LOGITECH.md) · [Logitech MX diagnostic probe](LOGITECH_PROTOTYPE.md)
- [Settings and languages](SETTINGS.md) · [Acknowledgements](ACKNOWLEDGEMENTS.md)
- Research, parked: [rotation animation](ROTATION_ANIMATION_RESEARCH.md), [global touch mode](GLOBAL_TOUCH_MODE_RESEARCH.md), [tablet research](TABLET_RESEARCH.md)

## Brand assets

`docs/images` holds the logo, horizontal and stacked, for light and dark backgrounds:
the app icon's bolt (`#FFC400`) and the name in Outfit SemiBold, converted to
outlines so the files need no font. Outfit is under the
[SIL Open Font License](third-party/Outfit.OFL.txt). The panel screenshots show
English and the light and dark themes over a neutral backdrop.
