# Touch keyboard and virtual touchpad

Two buttons open Windows' own touch input tools on demand, for apps and websites
that are hard to use with fingers in tablet posture. Their taskbar buttons can
then stay hidden, or be shown from Llampec while they are needed for a while.

| Tile | Spanish | Catalan/Valencian | Press | Options (arrow) |
| --- | --- | --- | --- | --- |
| **Touch keyboard** | Teclado táctil | Teclat tàctil | Shows or hides the touch keyboard | Taskbar icon: **Always**, **When no keyboard is attached**, **Never** |
| **Virtual touchpad** | Panel táctil virtual | Ratolí tàctil virtual | Opens the virtual touchpad | **Show icon in taskbar** on/off |

Both buttons first hide the panel, like Screenshot, so the tool serves the app
underneath. **Touch keyboard** toggles what is visible when it is pressed. With a
keyboard attached the keyboard stays visible while the panel opens, so a second
press hides it. In tablet posture, Windows already hides the touch keyboard when
the panel takes focus, so a second press shows it again; its own X closes it. The
virtual touchpad has no toggle: a second press leaves it open, and its own X
closes it.
The arrow is highlighted while the taskbar icon is always shown; the caption says
*Icon in taskbar* or, for the keyboard's automatic choice, *Icon without keyboard*.

The icon choices are the same ones Windows offers under **Settings >
Personalization > Taskbar > System tray icons**. Only the keyboard has the
automatic choice; Windows does not offer one for the touchpad. Both modules are
new tiles with a sub-page, using `ActionKind.ButtonWithSubpage`.

## Implementation

- **Taskbar icons.** The choices are the current user's existing Settings values
  `HKCU\Software\Microsoft\TabletTip\1.7\TipbandDesiredVisibility`
  (0 never, 1 always, 2 without keyboard) and
  `HKCU\Software\Microsoft\Touchpad\TouchpadDesiredVisibility` (0 hidden, 1 shown).
  They are not a public settings API. Explorer watches both keys: a write changes
  the notification area immediately, without `WM_SETTINGCHANGE` or restarting
  Explorer. Llampec writes only into the existing key, verifies the value by
  read-back, and shows *Could not change the taskbar icon.* otherwise. Unknown or
  missing values select no choice; they are never guessed.
- **Virtual touchpad.** Windows registers the `ms-virtualtouchpad:` protocol
  (handled by `LaunchWinApp.exe`), launched through the shell like
  `ms-settings:`. The tile is unavailable when the protocol is not registered.
- **Touch keyboard.** The documented `IInputPaneInterop::GetForWindow` +
  `InputPane.TryShow` returned success from a desktop window without a focused
  edit field but left the keyboard hidden. Llampec therefore uses the shell's
  undocumented `ITipInvocation::Toggle` (`CLSID_UIHostNoLaunch`), the interface
  behind the taskbar's keyboard button. Its server belongs to `TabTip.exe`; if it
  is not running, Llampec starts it and retries for up to two seconds. A failure
  leaves the keyboard unchanged and shows *Could not open the touch keyboard.*
  This is a second exception to the documented-API rule, alongside the power
  overlay functions. The user accepted it on 2026-10-10 after testing `TryShow`
  (see Validation).

No polling or background work: values are read when the panel opens and after
each choice. Nothing runs while the panel is closed.

## Validation

On the Surface (ARM64, Windows 26H1 build 28000), with the Type Cover attached
(laptop posture):

- Writing each registry value directly changed the notification area within
  1.5 s, observed through UI Automation: keyboard 1 → shown, 0 → hidden, 2 →
  hidden with the keyboard attached; touchpad 0 → hidden, 1 → shown.
- `InputPane.TryShow` returned `true` and showed nothing. `ITipInvocation::Toggle`
  showed the docked touch keyboard and hid it on the second call (screenshots).
- `ms-virtualtouchpad:` opened the virtual touchpad; a second launch kept it open.
- In the Debug app, pressing **Teclat tàctil** hid the panel and showed the
  keyboard; pressing it again hid it. **Ratolí tàctil virtual** hid the panel and
  opened the touchpad. Choosing **Sempre** wrote 1 and showed the tray icon;
  the touchpad switch wrote 0 and 1 with matching tray changes. The user's
  original values (keyboard 2, touchpad 1) were restored.

The user then ran a Windows PowerShell 5.1 test window calling
`IInputPaneInterop::GetForWindow` + `TryShow`/`TryHide` on the same Surface:

- Keyboard attached: `TryShow` showed nothing.
- Keyboard detached: the first `TryShow` showed the keyboard and hid it again;
  the second left it on screen. `InputPane.Visible` still reported `false`.

In tablet posture (`SM_CONVERTIBLESLATEMODE` 0, touch taskbar collapsed), the
Llampec button showed the keyboard on the first press. Opening the panel again
hid it (focus moved to the panel), and the next press showed it again. The
collapsed tablet taskbar hides most notification icons, so the automatic icon
choice was not observed in tablet posture. x64 hardware remains to be checked.
