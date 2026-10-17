# Using Llampec

## Install

Download the file for your architecture (ARM64 or x64) from
[Releases](https://github.com/PereEsquerdo/Llampec/releases):

- **Installer** (`Llampec-<version>-win-<architecture>-setup.exe`): installs for the
  current user only, without administrator rights, in `%LOCALAPPDATA%\Programs\Llampec`,
  with a Start menu shortcut and an optional start with Windows. It warns when the
  .NET 10 Runtime is missing and offers its download page. A newer installer upgrades
  in place; uninstall from **Settings > Apps**. Your settings in `%LOCALAPPDATA%\Llampec`
  are kept.
- **Portable archive** (`.zip`): extract it to a permanent folder and keep the whole
  folder together; `Llampec.exe` alone is not enough.

Both include the Windows App SDK files. Requirements: Windows 11 24H2 (build 26100)
or later, ARM64 or x64, and the .NET 10 Runtime for the same architecture. The files
are not code-signed yet, so SmartScreen may ask for confirmation
(**More info > Run anyway**).

## Opening and closing

Open Llampec from the Start menu, click its notification-area icon, or press
**Ctrl+Alt+Space**. Click outside the panel or press **Escape** to close it.
Right-click the icon for the menu; the gear opens Settings. `Llampec.exe --background`
starts it silently.

Buttons with an arrow have options: the button does the quick thing, the arrow opens
the rest. A title or status that doesn't fit scrolls while you hover over it or focus
it with the keyboard; the tooltip always shows all of it.

## Arranging the panel

Select the pencil. Each group appears as a block and buttons look as they do in the
panel, but they don't run. Drag a button by its face or a category by its heading; an
empty slot shows where it will land, and holding it at the top or bottom edge scrolls.
**×** removes a button and unloads its module completely, **+ Add module** brings one
back, **⋯** renames, moves or deletes a category, and **+** at the top creates one.
On the keyboard, **Enter** opens a button's options and **Delete** removes it.
**Save** applies the changes; **Cancel** discards them. The panel has 1–6 columns and
adapts to the monitor. See [panel layout](PANEL_LAYOUT.md).

## Windows

**Always on Top** pins the window you were using before opening Llampec, or the
active window with **Ctrl+Alt+T**. Its options list your windows, pin or unpin any of
them, adjust the border and opacity, and change the shortcut. Windows that another
app keeps on top are listed too and can be unpinned; **Restore all** clears every
window kept on top. Exiting Llampec only removes its own pins. Pinning an elevated
app such as Task Manager needs Llampec to run as administrator too; the error offers
**Restart as administrator**. See [Always on Top](ALWAYS_ON_TOP.md).

**Full screen** (**Ctrl+Alt+F**) fills the active window's monitor while keeping the
app's own controls; move to the bottom edge to show the taskbar. Selecting another
window restores it. See [full screen](FULLSCREEN.md).

**Auto-hide taskbar** and **Touch taskbar** switch those Windows settings; the touch
taskbar applies in tablet posture.

## Displays

- **HDR** switches all compatible displays or each one separately. The list of
  monitors is read at startup; restart Llampec after connecting a new one.
- **External monitor** controls brightness, contrast and volume of monitors that
  support DDC/CI, and taps them into their own standby without Windows noticing. If a
  monitor stays black after sleep, **Recover monitor** brings it back, and Llampec
  tries that on its own when the screen turns on. See [external monitor](MONITOR.md).
- **Turn off displays** turns off every screen (Windows enters standby, so programs
  pause and it may ask for sign-in). Its options turn off just the built-in screen or
  just one monitor, which doesn't sleep or lock the PC.
- **Multiple displays** chooses PC screen only, Duplicate, Extend or Second screen only.
- **Rotation lock** locks rotation and sets each display's orientation, even when
  Windows pauses automatic rotation for a keyboard or dock. See [tablet controls](TABLET.md).

## System

**Dark mode** switches the Windows theme. Its options schedule it at fixed hours or
from sunrise to sunset, using coordinates you enter or an explicit
**Use current location**. Llampec must be running for the schedule; it doesn't wake
the PC. A manual change lasts until the next scheduled one. See
[theme scheduling](THEME_SCHEDULE.md).

**Caffeine mode** keeps the PC awake indefinitely, for 1–3 hours or a custom time,
optionally with the screen on. Closing the panel keeps it going; sleep, expiry or
exiting ends it. Windows may limit it on battery. See [caffeine](CAFFEINE.md).

**Power** cycles the power mode; its options choose the plan, lid and power-button
actions and show battery health. See [power](POWER.md).

**Camera** lights up while a camera is in use and names the app. **Screenshot** hides
the panel and opens the Windows capture tool (**Win+Shift+S**); Windows handles the
rest. **Touch keyboard** and **Virtual touchpad** open those Windows tools for the app
underneath. See [camera](CAMERA.md), [screenshot](SCREENSHOT.md) and
[touch input](TOUCH_INPUT.md).

## Logitech MX mouse

Open the **MX mouse** options, find and select your mouse, turn the module on, assign
shortcuts to its buttons and press **Apply**. DPI and wheel settings are optional.
What's available depends on the mouse. Turning the module off or exiting restores the
settings Llampec changed. Tested with an MX Master 3S over Bluetooth. See
[Logitech setup and recovery](LOGITECH.md).

## Agentic work

Keeps the PC working for agents and remote access while the screens are dark: the
built-in screen turns off if another one is connected, external monitors go to
standby, anything still lit is covered with black (agents and remote tools still see
the desktop), and Windows neither sleeps nor locks. Any key, click, touch or mouse
movement on the PC restores everything; input from agents and remote tools doesn't.
Each part can be turned off in its options. See [agentic work](AGENT_MODE.md).

## Privacy

Preferences are stored in `%LOCALAPPDATA%\Llampec\settings.json`. Llampec has no
telemetry and makes no network requests of its own. Location is requested only
through the location button (Windows location providers may use the network), and
saved coordinates are used for offline sunrise and sunset calculations.

## Languages

English, Spanish and Catalan/Valencian, following Windows or chosen in Settings. See
[settings and languages](SETTINGS.md).
