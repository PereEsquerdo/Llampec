# Llampec

A compact, open-source Windows 11 quick-settings panel by
[Pere Esquerdo Ramis](https://github.com/PereEsquerdo).

Llampec is growing into what the Windows 11 Quick Settings panel should have
been — with a few extra vitamins: the controls you actually reach for, arranged
your way, one click from the notification area. It stays simple and lightweight:
it gives quick access to existing Windows settings without deep system changes,
and does no work in the background while it waits.

**0.2.0-alpha.8** adds Fullscreen, a fully customizable panel and lower idle
CPU use:

- **Edit the panel like Quick Settings.** Choose 1–6 columns, create categories,
  and drag buttons or whole categories into place with a live preview. Remove a
  button with its corner ×, or add it back with **+ Add module**. Removed modules
  are fully unloaded and use no resources.
- **Fullscreen** for any app window, with a shortcut and mouse-edge taskbar reveal.
- The panel leaves room for an expanded or auto-hidden taskbar.
- While idle, Llampec no longer reacts to other windows or mouse movement: with
  ordinary desktop activity its background CPU use dropped from about 1 s per
  20 s to none in local measurements.

Llampec lives in the notification area and provides eighteen configurable quick actions:

- **HDR:** switch all compatible displays or control each display separately.
- **Turn off displays:** its options turn off just one display — the built-in panel
  (Second screen only) or a DDC/CI monitor (its own standby) — without standby or
  lock, always keeping one display lit. The button turns off all displays; on Modern Standby devices this enters a
  short standby: programs pause and Windows may ask for sign-in. To keep a local
  agent working, use Caffeine and let the screen turn off by itself; see
  [Caffeine and Modern Standby](docs/CAFFEINE.md#modern-standby-turn-off-display-and-local-agents),
  or **Agentic work**.
- **External monitor:** brightness, contrast and volume of DDC/CI monitors, which
  Windows Quick Settings does not control. Tapping puts them into their own standby
  without removing them from the desktop. See [external monitor](docs/MONITOR.md).
- **Agentic work:** keep the PC working for agents and remote access with the
  screens dark: the built-in panel off when another display is on, external monitors
  in standby, black covers that agents and remote tools see through, and no standby
  or lock. Any key, click or touch on the PC restores everything; input from agents
  and remote tools does not. See [agentic work](docs/AGENT_MODE.md).
- **Light/dark mode:** change the Windows theme, with optional fixed-hour or
  sunrise/sunset scheduling and a countdown to the next change.
- **Multiple displays:** choose PC screen only, Duplicate, Extend or Second screen only.
- **Taskbar:** toggle automatic hiding.
- **Touch taskbar:** enable or disable touch optimization in tablet posture.
  Conventional auto-hide is disabled while the touch taskbar is active.
- **Rotation lock:** toggle the internal display's rotation lock; choose a display
  and apply an orientation immediately from four visual screen/AB choices.
  Manual orientation also works when Windows pauses automatic rotation for a
  keyboard, dock or multiple-monitor configuration.
- **Caffeine:** prevent automatic sleep for a chosen duration, optionally keeping
  the display on.
- **Always on Top:** pin the last active window, use a configurable shortcut, or
  choose windows from a list. An optional border has adjustable thickness and uses the Windows accent color
  or a custom color. Pinned windows can be made translucent; a folded section
  adjusts each pinned window separately.
- **Screenshot:** close the panel, then open the native Windows capture selector
  with **Win+Shift+S**.
- **Fullscreen:** press **Ctrl+Alt+F** or its Llampec button to fill the active
  window's monitor while keeping the app's own controls. The options show the
  active app and monitor and allow changing the shortcut. Selecting another app
  window restores the original state. Mouse-edge reveal uses the taskbar available
  on that monitor; Windows handles the native touch gesture.
  See [fullscreen behavior and validation](docs/FULLSCREEN.md).
- **Power:** tap to cycle the power mode (best efficiency, balanced, best performance)
  for the current power source. Its options also choose the power plan when Windows
  offers more than one, set the lid and power-button actions on battery and plugged
  in, and show battery charge rate and capacity. See [power](docs/POWER.md).
- **MX mouse:** turn the Logitech MX mouse settings on or off and see the battery
  the mouse reports. Its options hold the button, DPI and wheel configuration.
- **Camera:** lights up while a camera is capturing and names the camera and the app
  using it. Pressing it opens Windows camera settings. See [camera](docs/CAMERA.md).
- **Touch keyboard** and **Virtual touchpad:** open Windows' touch input tools when
  an app is hard to use with fingers. Their options show or hide each tool's own
  taskbar icon; the keyboard can also show it only when no keyboard is attached.
  See [touch input](docs/TOUCH_INPUT.md).

The pencil at the bottom of the panel (or **Settings → Buttons and categories**)
opens the visual editor. The panel uses **1–6 columns** (default **3**) and adapts
its width to the monitor. See [panel layout](docs/PANEL_LAYOUT.md).

The **MX mouse** button's options configure a Logitech MX mouse: assign keyboard
shortcuts to supported mouse buttons (including the thumb button), adjust sensor DPI
and configure native wheel mode, SmartShift and scroll direction. It keeps working
with the panel closed and reconnects to the selected mouse. Removing the button
restores the mouse and unloads the service. See
[Logitech setup and recovery](docs/LOGITECH.md) for scope and hardware coverage.

The panel uses WinUI 3, desktop acrylic and compositor animations that respect the
Windows animation setting. Settings include Spanish, Catalan/Valencian and English,
per-user startup, and button reordering.

## Requirements

- Windows 11 24H2 (build 26100) or later.
- ARM64 or x64.
- .NET 10 Runtime matching the application architecture.

## Install

Download from [Releases](https://github.com/PereEsquerdo/Llampec/releases) the file for
your architecture (ARM64 or x64):

- **Installer** (`Llampec-<version>-win-<architecture>-setup.exe`): installs for the
  current user only, without administrator rights, in `%LOCALAPPDATA%\Programs\Llampec`,
  with a Start menu shortcut and an optional start with Windows. It warns when the
  .NET 10 Runtime is missing and offers its download page. Running a newer installer
  upgrades in place; uninstall from Windows Settings > Apps. Settings in
  `%LOCALAPPDATA%\Llampec` are kept.
- **Portable archive** (`.zip`): extract it to a permanent folder and keep the whole
  folder together; copying only `Llampec.exe` is insufficient.

Both include the Windows App SDK dependencies. The files are not code-signed yet, so
Windows SmartScreen may ask for confirmation (**More info > Run anyway**).

## Use

Open Llampec from the Start menu (or `Llampec.exe`), click its notification-area icon, or press **Ctrl+Alt+Space**.
Click outside the panel or press **Escape** to close it. Right-click the icon for
the menu; the gear opens Settings. Use `Llampec.exe --background` to start silently.

To customize the panel, select the pencil. Each group appears as a block and
buttons look as they do in the panel, but they do not run. Drag a button by its
face, or a category by its heading; an empty slot shows where it will land, and
holding it at the top or bottom edge scrolls. **×** removes a button,
**+ Add module** restores one, **⋯** renames, moves or deletes a category, and
**+** at the top creates one. On the keyboard, **Enter** opens a button's options
and **Delete** removes it. **Save** applies the changes; **Cancel** discards them.

Each split button has an arrow for its options. For light/dark scheduling, choose
fixed hours or sunrise/sunset and select **Apply**. Solar scheduling accepts manual
coordinates or an explicit **Use current location** request. Llampec must remain
running for scheduled changes; it does not wake the computer. A manual theme change
lasts until the next scheduled transition.

**Screenshot** waits until Llampec's panel is hidden before sending **Win+Shift+S**,
so its panel is not part of the selection. Windows handles the capture selector,
clipboard and subsequent editing or saving. Llampec does not store or upload the
image. See [screenshot behavior](docs/SCREENSHOT.md).

For Logitech MX, open the **MX mouse** button's options (›), find and select the mouse,
enable the module, then assign shortcuts and press **Apply**. DPI and wheel settings
are optional. The selected device's advertised capabilities determine what is
available; other mice retain their behavior. Disabling the module or exiting normally
restores the settings Llampec changed. [Setup and recovery](docs/LOGITECH.md) explains
connection recovery and the hardware combinations tested.

Caffeine supports indefinite sessions, 1/2/3-hour presets and custom durations of
1–1440 minutes. Closing the panel keeps the session active. Expiry, manual sleep or
exiting the application ends it; sessions are not restored on restart. Windows
power policy may limit power requests, particularly on battery and Modern Standby.

Always on Top uses **Ctrl+Alt+T** by default to pin or unpin the active window. The
tile acts on the window used before opening Llampec; its subtitle identifies that
target, and a separate count shows all windows pinned by Llampec. Its arrow opens
the window selector, **Unpin all**, **Appearance** (border on/off and thickness),
and shortcut preferences. Existing topmost windows owned by other applications
are not taken over. Normal exit removes Llampec's pins; closing the panel does not.
The shortcut works directly, including with ordinary application dialogs. The
border follows standard Windows 11 rounded corners and uses a square inner border
for maximized or snapped windows. Applications with custom window shapes may differ.
To pin an elevated application (such as Task Manager), Llampec needs administrator
permissions too. A permissions error offers **Restart as administrator**, using the
normal Windows UAC prompt. After restarting, activate the target and press the
shortcut again. Cancelling UAC leaves the current Llampec instance running.

Preferences are stored in `%LOCALAPPDATA%\Llampec\settings.json`. Llampec has no
telemetry or application-owned network requests. Location is requested only through
the location button; Windows location providers may use the network. Saved
coordinates are used for offline solar calculations.

## Roadmap

Future versions plan to bring more everyday controls into the same panel:

- **Power options:** power mode and energy-saving choices.
- **Network settings:** quick access to network connections and their options.
- **Wireless projection:** connect to wireless displays.
- **Webcams:** manage the available cameras.
- **Volume and brightness:** launchers for the Windows volume and brightness panels.

Each addition follows the same principle: quick access to existing Windows settings,
no deep system changes and no background cost while idle. Plans may change as each
feature is researched.

## Build and run

Development requires the .NET 10 SDK and the Windows build tools required by
Windows App SDK. Dependencies are pinned in the project and restored from NuGet.

```powershell
dotnet build Llampec.sln
dotnet run --project src/Llampec.App
```

The default build uses the SDK's native architecture. Create a distributable package
for either target:

```powershell
./tools/package.ps1 -RuntimeIdentifier win-arm64
./tools/package.ps1 -RuntimeIdentifier win-x64
```

The script publishes Release, verifies the executable and required WinUI PRI/XBF
resources, includes the README, license and dependency notices, and creates
`publish/Llampec-<version>-<architecture>.zip`. With Inno Setup 6 installed
(`winget install JRSoftware.InnoSetup`) it also builds the per-user installer
`publish/Llampec-<version>-win-<architecture>-setup.exe` from `installer/Llampec.iss`.
It refuses to reuse an existing output folder.

For a development publish without packaging:

```powershell
dotnet publish src/Llampec.App -c Release -r win-arm64 --self-contained false -o publish/win-arm64
./tools/test-publish.ps1 -PublishDirectory publish/win-arm64
```

Use `win-x64` instead for x64.

## Verification and development

Run the tests used by CI without accessing the active display setup:

```powershell
dotnet test tests/Llampec.Core.Tests -c Release --filter "FullyQualifiedName!~SystemActionTests&FullyQualifiedName!~HdrDisplaysTests&FullyQualifiedName!~ProjectionModesTests"
```

The excluded integration tests inspect the real Windows session, and
`ProjectionModesTests` reapplies the current display topology. Run those tests
explicitly on suitable hardware.

Validation on 2026-10-06: 476 tests passed with that filter (486 in the full local
run, plus the opt-in native fullscreen smoke test); ARM64 and x64 Release packages
built without errors or warnings and passed the resource check. Mouse and keyboard
checks on the Surface ARM64 covered the panel editor (drag preview, category moves,
edge scrolling, ×, + Add module, Escape and Cancel), Always on Top pinning and
release with the new on-demand window tracking, and Logitech reconnection.
Physical touchscreen dragging in the editor is still untested. Earlier releases
verified tablet controls and orientation on the same device. Hardware limits and
the separate investigation of global tablet posture are recorded in
[tablet controls](docs/TABLET.md). The global touch-mode experiment is deferred to
a future version and is outside this release's scope.

x64 validation on 2026-10-10 (commit `85af722`, Intel Core i7-1065G7, Windows 11
build 26200): the full Core suite passed (498 passed, 1 opt-in skipped) and the
opt-in native fullscreen test passed. The Always on Top smoke test passed its
75 native checks, and the fullscreen self-test passed normal/maximized fullscreen
and rollback. The x64 Release package built and passed the resource check.
Started in the background, the published build idled at about 5 MB private
working set until a later rise to about 40 MB, probably from opening the panel
(unconfirmed). On x64, the Logitech module with real hardware, touchscreen dragging
in the editor, tablet controls and the touch keyboard/touchpad buttons are not
yet covered.

HDR's per-monitor list is captured at startup. Restart Llampec after connecting a
new monitor to add it to that list; unplugged monitors are disabled. ARM64 and x64
packages are built separately. Current Logitech hardware coverage is MX Master 3S
over Bluetooth on Windows ARM64; receiver connections and other MX models need
their own checks.

`--diagnostics` enables a local log for the session. `--theme=dark` or `--theme=light`
previews a panel theme on a fresh launch without changing the saved preference.
See [architecture and diagnostic options](docs/ARCHITECTURE.md) for details.

Developer documentation:

- [Adding an action](docs/ADDING_AN_ACTION.md)
- [Theme scheduling](docs/THEME_SCHEDULE.md)
- [Caffeine mode](docs/CAFFEINE.md)
- [External monitor](docs/MONITOR.md)
- [Agentic work](docs/AGENT_MODE.md)
- [Settings and languages](docs/SETTINGS.md)
- [Always on Top](docs/ALWAYS_ON_TOP.md)
- [Screenshot](docs/SCREENSHOT.md)
- [Power](docs/POWER.md)
- [Camera](docs/CAMERA.md)
- [Touch keyboard and virtual touchpad](docs/TOUCH_INPUT.md)
- [Tablet controls and orientation](docs/TABLET.md)
- [Future rotation-animation research](docs/ROTATION_ANIMATION_RESEARCH.md)
- [Future global touch-mode research](docs/GLOBAL_TOUCH_MODE_RESEARCH.md)
- [Logitech MX diagnostic probe](docs/LOGITECH_PROTOTYPE.md)
- [Logitech MX settings and service](docs/LOGITECH.md)
- [Acknowledgements](docs/ACKNOWLEDGEMENTS.md)

## Credits

Llampec is designed, developed and maintained by **Pere Esquerdo Ramis**
([@PereEsquerdo](https://github.com/PereEsquerdo)). Third-party components and references are
credited in [Acknowledgements](docs/ACKNOWLEDGEMENTS.md).

## License

[MIT](LICENSE) · Copyright © 2026 Pere Esquerdo Ramis.
[Source code on GitHub](https://github.com/PereEsquerdo/Llampec).
