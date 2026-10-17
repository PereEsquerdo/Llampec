# Changelog

## Unreleased

- Agentic work hides the taskbar automatically while it covers a display, and shows
  it again on exit: the taskbar, also topmost, stayed lit above the cover on a
  single OLED display. If the taskbar has focus, which keeps it showing, focus moves to
  the topmost app. The pointer is hidden on a small window of its own, so no app shows
  hover effects or tooltips under it, and returns there after a minute without mouse
  movement, when an agent has left it elsewhere.
- The Windows App SDK components follow an official release (2.5.1): CI checks that
  they match it, and a weekly workflow reports a newer release.
- Maintenance: CI actions off the deprecated Node.js 20 (`checkout@v7`,
  `setup-dotnet@v6`, `upload-artifact@v7`), tests on xUnit v3 with
  Microsoft.Testing.Platform, Windows App SDK WinUI 2.3.9 and Foundation 2.3.12,
  and Dependabot for weekly action and NuGet updates.
- A **logo**: the app's bolt with the name in Outfit, horizontal and stacked, for
  light and dark backgrounds.
- A shorter **README** in English, with the project's history, panel screenshots,
  acknowledgements and the dedication. Usage details moved to
  [using Llampec](docs/USING.md), build and validation notes to
  [development](docs/DEVELOPMENT.md).
- About: the logo as its title, then three bold bullets with an emoji each: the
  dedication *Dedicat a Lara i a Miquel* (🫶), **Code and updates on GitHub** (💻)
  instead of "Source code", and acknowledgements (🤝) indented below, now also
  crediting the Outfit typeface of the logo.
- The panel footer shows the logo instead of the plain name; clicking it opens
  About. Both use one vector template, drawn like an icon, with no image or font.
- Always on Top: windows **pinned by another application** (for example by an
  agent controlling the PC) are listed and can be unpinned from their row, the
  button or the shortcut, without Llampec claiming them. **Restore all** clears
  Llampec's pins and those of other applications; shell surfaces are left alone.
  See [Always on Top](docs/ALWAYS_ON_TOP.md#windows-pinned-by-other-applications-2026-10-17).
- Tighter panel: one-line titles and subtitles that scroll while hovered or
  focused when they do not fit, a pinned-count badge instead of a third line, and
  smaller gaps. A row went from about 122 to 98 DIP (Quick Settings: about 96).
  See [panel layout](docs/PANEL_LAYOUT.md#tile-captions-and-spacing-2026-10-17).
- Add an **External monitor** module for DDC/CI monitors: brightness, contrast and
  volume sliders per monitor, and a tap that puts them into their own standby while
  Windows keeps them in the desktop (no standby, no lock, no moved windows). State is
  read on the thread pool when the panel or its options open; there is no polling.
  See [external monitor](docs/MONITOR.md).
- External monitor: **recover a monitor without picture**, for example one behind a
  USB4 dock that stays dark after sleep while Windows still lists it. Llampec wakes
  it over DDC/CI and, while it looks for a signal, switches to PC screen only and
  back, which made Windows enumerate it again. Available on demand and, by default,
  as one check 8 s after Windows turns the screen on. See
  [external monitor](docs/MONITOR.md#monitor-without-picture-after-sleep).
- **Turn off display** becomes **Turn off displays** and gains options to turn off
  just the built-in panel or just one DDC/CI monitor, which neither suspends nor
  locks the PC. Neither these options nor the External monitor tap can leave every
  display dark. The button itself still turns off all displays (Modern Standby).
- Add an **Agentic work** module that keeps the PC working for local agents and
  remote access with the screens dark: Second screen only for the built-in panel
  when another display is on, DDC/CI standby for external monitors, black
  capture-excluded covers for displays that stay lit, a power request that keeps the
  session unlocked (optional) and, optionally, best power efficiency. A physical key,
  click, touch or mouse movement restores everything; input injected by agents and
  remote tools does not. An interrupted session is restored at the next start.
  Measured on a Surface with an external monitor: 8.2 → 6.0 W. See
  [agentic work](docs/AGENT_MODE.md).

## 0.2.0-alpha.9 — 2026-10-11

- Add a per-user **installer** for ARM64 and x64 (Inno Setup): no administrator
  rights, Start menu shortcut, optional start with Windows, .NET 10 Runtime check,
  in-place upgrades that first close Llampec normally, and uninstallation that keeps
  settings. The portable archives remain available.

- Add a **Power** module. Tapping it cycles the power mode for the current power
  source. Its options choose the power plan (when Windows offers more than one) and
  the lid and power-button actions on battery and plugged in. They also show charge
  rate and battery capacity compared with its design capacity. State is read when
  the panel opens.
- Add an **MX mouse** module. The Logitech settings move from Settings into this
  button's options. The switch turns them on or off and the caption shows the battery
  the mouse reports (HID++ 0x1004/0x1000), read on connection and then updated by
  the mouse's own events. Removing the button restores the mouse and unloads the
  service; if restoration fails, the button stays.
- Add a **Camera** module that lights up while a camera is capturing and names the
  camera and app, using Windows' documented sensor activity monitor only while the
  panel is visible. Pressing it opens Windows camera settings.
- Add **Touch keyboard** and **Virtual touchpad** modules. Pressing them hides the
  panel and opens the tool for the app underneath (the keyboard toggles). Their
  options show or hide the tool's own taskbar icon, applied immediately; the
  keyboard can show it only when no keyboard is attached. See
  [touch input](docs/TOUCH_INPUT.md).
- Always on Top: the border now uses the real Windows accent color (it used DWM's
  darker frame shade), and Appearance adds **Border color** with an inline color
  picker for a custom color.
- Always on Top: **Opacity** (30–100 %) for pinned windows, restored on unpin and
  normal exit, and a folded **Per-window appearance** section to give any pinned
  window its own border, color and opacity for as long as it stays pinned.
- Choosing a sub-page option no longer leaves its tile disabled with a busy bar
  until the panel reopens.
- Code review before the beta:
  - Fixes: a display-configuration failure (for example in a Remote Desktop session)
    could stop Llampec at startup through the HDR or Multiple displays tile; both now
    show as unavailable. Opacity changes skip a hung window instead of waiting on it,
    and a window whose opacity cannot be restored stays pinned until it can be. The
    panel's dismissal holds are counted, so one page can no longer release another's,
    and a rotation started from its page keeps the panel open. Dark mode broadcasts
    the change off the UI thread. An elevated restart no longer fails while the old
    instance is exiting.
  - MX mouse: a receiver reporting its mouse as unreachable retries instead of
    failing, and a mouse that reconnects behind an open HID path is reconfigured
    (HID++ 0x1D4B). A switched-off module ignores unrelated device changes. A receiver
    whose mouse is off is no longer retried every 15 seconds after the first three
    attempts; opening the panel tries again.
  - Efficiency: with a window pinned and the panel closed, Always on Top hooks only
    the pinned processes (plus window destruction) instead of every window, move and
    caret. Fullscreen finds taskbars directly instead of enumerating every window
    four times a second, and classifies the foreground once per tick. Multiple
    displays reads the topology once per refresh instead of eight times; the touch
    tiles read their registry value once. Borders recolor only on accent or contrast
    changes. Power, tile and per-window updates are coalesced. Hidden taskbar tiles
    are no longer refreshed on every setting broadcast. Logging formats nothing when
    the log file is off.
  - Clean-up: one display-query helper, one shell-window list, one key-name table
    (the panel and mouse shortcuts now accept the same keys), shared panel helpers
    and shortcut editor, a single sub-page view, and removal of the stale
    `ActionCatalog`, `OsVersion`, unused view-model commands and 15 unused
    translations. CI now also builds the native smoke test and the Logitech probe.
    The superseded fullscreen prototypes in `tools/` are removed.
- x64 validation (Intel, Windows 11 build 26200, commit `85af722`): full Core suite
  (498 passed, 1 opt-in skipped), opt-in native fullscreen test, 75 Always on Top
  native checks, fullscreen self-test and the x64 Release package all passed.
  Logitech hardware, touch editing, tablet controls and touch input remain
  unchecked on x64.
- Power options show whether energy saver is on and the battery level that turns it
  on, with a link to its Windows settings.
- New modules are enabled for existing settings and appear under No category.
  Remove any of them with the panel editor.
- Efficiency review before the first beta:
  - Always on Top and Fullscreen read a window's process name with one limited
    query instead of `Process.GetProcessById(...).ProcessName`, which snapshots every
    process (7.3 ms and 6 KB per call with 305 processes, against about 1 µs). This
    removes roughly 20–25 ms of work from every window switch and, with 11 open
    windows, an estimated 150–250 ms of UI-thread work from each panel open/close.
  - Hidden and idle, Fullscreen only remembers the last app on window switches
    instead of refreshing its whole state.
  - The Power tile reads only status, plan and mode; the options page reads the rest.
  - Reference only the Windows App SDK WinUI and Foundation packages: the published
    ARM64 build drops from 156 MB to 116 MB (x64: 108 MB), without AI, ML, Search or
    Widgets binaries.
  - Publish ReadyToRun with non-concurrent GC and without dynamic PGO. ARM64 Release
    startup went from 1.43 to 1.00 s and the first panel preparation from 207 to
    165 ms; idle memory after use dropped from 7.4 to 6.9 MB.
  - The "Panel prepared" diagnostic now includes target-window capture.

## 0.2.0-alpha.8 — 2026-10-06

- Simplify the panel editor to look like the finished panel: each group is a block,
  buttons keep their real face and caption, and a small circular × at the top-left
  corner disables a module. Drag a button by its face or a category by its heading,
  without drag handles; the item floats under the pointer while an empty slot and
  animated reflow show where it will land, as in Windows Quick Settings. Escape
  restores the previous position. Keyboard users open a button's options with
  Enter/Space/Shift+F10 and disable it with Delete.
- Reuse editor views during a session and fill shared menus when opened, instead of
  rebuilding the editor and its per-item menus after every change. The panel keeps
  its size during a drag and repositions once on release.
- Dropped editor items glide into their slot, and holding an item at the top or
  bottom edge keeps scrolling. Both timers exist only during a drag.
- Reduce idle CPU: Always on Top and Fullscreen hook window lifecycle events only
  while the panel is shown, a pin exists or a fullscreen session is active, using
  narrow event ranges instead of all object events (which included every cursor
  and caret move). With the panel hidden, Always on Top processes only pinned
  windows' events. With a moving mouse and a window changing title 20 times per
  second, the hidden ARM64 Debug instance went from about 1 s to 0 ms of CPU per
  20 s, and from 0.77 s to 0.17 s while a window is pinned.
- An absent Logitech mouse (missing HID path) backs off to 5-minute retries after
  2/5/15 s, since Windows device notifications report its return; a present but
  silent device keeps 15-second retries.
- Fix panel hit testing: the shadow's Z elevation moved to a non-interactive layer,
  since a Z translation on the interactive surface displaced clickable areas from
  the drawn controls by several DIP across the whole panel.

- Add a visual panel editor from the main-panel pencil, with a fixed column/category
  header and Save/Cancel footer. Choose 1–6 columns (default 3), create or rename
  categories, and move icons within/between groups or reorder categories through
  drag handles and keyboard menus. Empty groups remain drop destinations; deleting
  a category appends its icons to the end of No category in visual order.
- Disable and re-enable tile modules through the editor, using a static metadata
  catalogue for inactive choices. Explicit DisabledModules releases actions,
  services and shortcuts on Save; legacy hidden buttons retain their services.
  Keep module configuration and add enabled icons at the end of No category,
  without recreating previous pin, fullscreen or caffeine sessions.
- Prepare restoration before module unloading; failed recovery keeps the module
  enabled and retains ownership. Drain pending theme work, release caffeine power
  requests, and keep shared tray/events and the independent Logitech service alive.
- Reserve room for expanded primary/secondary taskbars when positioning the panel,
  including conventional auto-hide and collapsed touch taskbars.
- Retain only unfinished editor draft data when hidden and rebuild disposable
  controls on reopening. Cancel discards the draft; Escape cancels an active drag
  or hides the panel. Use local pointer capture on drag handles with an 8 DIP
  threshold, geometric destinations and event-driven edge scrolling in one bounded
  viewport, without a system drag payload or new idle timer.
- Use validated task-button geometry when Explorer's tray host includes transparent
  padding, removing the excess panel-to-taskbar gap. Missing or invalid shell
  geometry keeps the conservative placement fallback.
- Credit Pere Esquerdo Ramis in copyright, author metadata and About, with a clear
  open-source/MIT label and a source-code link. GitHub publication remains deferred.
- Coalesce panel layout work and avoid process/title reads on every active fullscreen
  tick. Layout controls remain disposable and add no idle polling.
- Keep fullscreen mouse-edge detection associated with its auto-hidden taskbar
  when vertically stacked monitors put the hidden bar's HWND on the next screen.
- Editor validation: 482 Core tests, including the opt-in native fullscreen smoke
  test, and final ARM64/x64 Debug builds passed with no warnings or errors. GUI
  checks covered responsive columns, category and cross-group icon moves, empty
  category drops, creation, Cancel, and actual module unloading/re-enabling.
  Physical touchscreen dragging remains untested. A local hidden ARM64 run with
  all eleven modules enabled averaged 10.18 MiB private working set; the workload
  and measurement limits are recorded in the panel notes.

- Add Fullscreen: toggle the last active app from Llampec or with configurable
  Ctrl+Alt+F, retaining its own controls. Show the active window and monitor;
  restore the original state on toggle, normal exit or selection of another window.
- Support fullscreen on all monitors and mouse-edge taskbar reveal where Windows
  provides a taskbar, preserving the user's auto-hide and touch preferences.
- Compensate measured window and client-area margins to cover the monitor,
  including Chromium's frame margins. Visual hardware validation remains ongoing.

## 0.2.0-alpha.7 — 2026-10-02

- Add a Touch taskbar toggle for Windows' touch-optimization preference, with
  verified read-back, external-change refresh and explicit unavailable states.
- Add Rotation lock with the internal display selected by default, a dynamic
  display selector and four visual screen/AB orientation choices in a 2-by-2 grid.
  Orientation is applied and saved immediately, without a confirmation countdown.
  Failed operations retain conditional recovery across panel closure and respect
  newer external display changes.
- Keep AB lettering parallel to the screen's short edge in portrait cards, with
  flipped choices upside down. Report failures when opening Windows display settings.
- Allow manual orientation while Windows pauses automatic rotation for an attached
  keyboard, dock or multiple-monitor configuration; the automatic-rotation lock
  control continues to follow Windows' availability restrictions.
- Disable automatic taskbar hiding while the tablet-optimized taskbar is inferred
  active, showing the reason without changing the saved auto-hide preference.
  Refresh availability on posture/settings changes, resume and Explorer restart;
  recheck before applying a change. Unknown configurations have a separate status.
- Keep the panel on its opening monitor during rotation and reserve the
  shell-reported touch taskbar bounds so its expanded state cannot cover the footer.
- Refine the screenshot icon to four separated dashes, round the display-power
  icon, and standardize Fluent glyphs, optical alignment and navigation sizes.
  Scale the complete 24-unit vector geometry to 20 DIP so the monitor edge, stand
  and screenshot plus are no longer clipped.
- Defer the global touch-mode experiment to a future version at the user's request.
  It is separate from the touch-taskbar preference and does not block this release.
- Save orientation with the normal apply-and-save operation: on this Surface,
  the no-reset variant reported success but its saved-mode read-back stayed stale.
  Failure recovery restores separately saved and active modes when they differ.
- Validation: 386 hardware-safe Core tests passed, including 82 rotation cases;
  ARM64 and x64 builds completed without errors or warnings. Computer Use verified
  the final portrait lettering in light mode and the Windows display-settings link.
  Real manual rotation with an attached keyboard applied and saved portrait, then
  restored landscape without changing Windows' rotation flags. Earlier checks
  covered light/dark icon colors and persistence beyond 15 seconds and a normal
  app exit/relaunch. Keyboard transitions, sensor reliability, external-display
  and suspend/resume coverage remain pending.

## 0.2.0-alpha.6 — 2026-09-21

- Screenshot now uses a dotted selection frame with rounded corners and a plus
  sign. The vector icon follows the button foreground in light, dark and
  high-contrast themes. Screenshot behavior is unchanged.
- Updated documentation and portable ARM64/x64 packages.

## 0.2.0-alpha.5 — 2026-09-21

- Screenshot quick action: waits for the panel to finish hiding, then sends
  Win+Shift+S to open Windows' native capture selector. No capture library,
  background worker or image storage is added.
- Optional Logitech MX settings: per-device button shortcuts, sensor DPI, native
  wheel mode/SmartShift and independent scroll direction. Includes multiple button
  assignments, background operation, device/power recovery and an original-state
  journal for restoration after interrupted sessions. Selected HID++ feature code
  is attributed to Mouser under MIT.
- MX Master 3S over Bluetooth on Windows ARM64: the user confirmed thumb-button
  shortcuts in the application and after a real suspend/resume cycle. Receiver
  connections and other models remain outside the current hardware validation.
- Windows startup follows the current portable location after a successful manual
  launch when startup was already registered. Stale commands no longer appear as
  enabled in Llampec; Windows' separate startup-disable choice is preserved.
- A background launch no longer opens the panel of an already running instance.
- Validation: 244 hardware-safe Core tests and six additional read-only
  action-catalog integration tests passed. ARM64 and x64 builds succeeded;
  physical Logitech checks currently cover MX Master 3S Bluetooth on ARM64.

## 0.2.0-alpha.4 — 2026-09-18

- Always on Top outlines overlap the native Windows frame border instead of
  leaving that strip visible as a gap. Placement uses DWM's physical border
  thickness and adjusts the corner cutout while retaining the chosen stroke width.

## 0.2.0-alpha.3 — 2026-09-18

- Rounded Always on Top borders follow the standard Windows 11 corner policy,
  including small corners; maximized and snapped windows keep a square perimeter.
- Ctrl+Alt+T resolves ordinary owned dialogs and the captured external target when
  Llampec has focus. Hidden topmost tooltips no longer block pinning.
- Permission failures explain why an elevated window cannot be pinned and offer
  an explicit Restart as administrator action through the standard UAC prompt.
- Regression tests cover shortcut targeting, native errors and rounded border masks.

## 0.2.0-alpha.2 — 2026-09-18

- Always on Top: last-active-window tile, configurable global shortcut (Ctrl+Alt+T),
  window selector, multiple pins and explicit Unpin all.
- Appearance section with optional accent-colored border and adjustable thickness;
  event-driven tracking of movement, DPI, minimization and desktop visibility.
- Transactional shortcut changes, per-session pin ownership and normal-exit cleanup.
- Spanish, Catalan/Valencian and English UI, and Microsoft PowerToys acknowledgements.
- Window transparency and further appearance customization remain planned for phase 2.

## 0.2.0-alpha.1 — 2026-09-17

First functional alpha baseline for Windows 11 24H2+, available for ARM64 and x64.

- WinUI 3 panel with desktop acrylic, unified compositor animations, light/dark
  appearance and support for the Windows animation preference.
- Notification-area access, global hotkey, single-instance activation and silent startup.
- HDR controls per display, display power, projection modes and taskbar auto-hide.
- Windows light/dark mode with fixed-hour or offline sunrise/sunset scheduling,
  current state and countdown to the next transition.
- Caffeine sessions with configurable duration and optional display-on requests.
- Spanish, Catalan/Valencian and English; per-user startup and button reordering.
- Local preferences, on-demand location and delayed release of hidden UI resources.
- Core tests, local diagnostics and verified portable packages for both architectures.
