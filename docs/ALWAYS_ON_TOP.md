# Always on Top - agreed design

Status: phase 1 implemented, with fixes in 0.2.0-alpha.4. Phase 2 adds custom border
colors, window transparency and per-window appearance (2026-10-10).
Decision recorded: 2026-09-18.

## Phase 1: interaction

- Add a split quick-action tile named "Always on Top" ("Siempre encima" in Spanish).
- The main button pins or unpins the last eligible external window used before
  opening Llampec. Remember foreground changes, excluding Llampec and shell
  surfaces, so opening the notification area does not lose the intended target.
- Identify the target and the operation in the tile subtitle or tooltip. Keep
  that target stable while the panel is being used.
- A configurable global shortcut pins or unpins the foreground window without
  opening the panel. The default is Ctrl+Alt+T. Changing it reserves the new
  combination before releasing the old one; conflicts or failed settings saves
  retain the working shortcut. An empty value disables the shortcut.
- Support multiple pinned windows. The tile state refers to its target; show a
  separate pinned-window count rather than implying that a click unpins all.
- The arrow opens a window selector with pinned windows first, individual
  pin/unpin controls, and an explicit "Unpin all" action for Llampec's pins.
- When the main tile has no eligible target, offer the selector. The shortcut
  resolves the active application's root/owned dialog, or the captured target
  when Llampec has focus. A successful shortcut does not open the panel; a failed
  operation opens its error details.
- Keep pinning state outside disposable panel controls. Restore the changes
  owned by Llampec on normal exit; do not restore pinned windows across sessions.

## Phase 1: Appearance

The feature subpage must include an **Appearance** section (**Apariencia** in
Spanish) from the first implementation. The user explicitly requested:

- A toggle to show or hide the border around pinned windows.
- A control to adjust border thickness.

Disable the thickness control while the border is off, retaining its chosen
value. Use the Windows accent color initially. Save the appearance preferences
and apply changes to windows already pinned by Llampec. Thickness ranges from
1 to 8 logical pixels (default 3), scaled to the target monitor. High contrast
uses the system highlight color.

## Implementation and lifetime

`AlwaysOnTopService` owns the foreground target, current pins and border lifetimes.
It is separate from `AlwaysOnTopView`, which is disposed when the panel closes.
The `AlwaysOnTopAction` tile reflects its target; the separate count reflects all
session pins. The selector keeps existing row controls and coalesces change events.

The native backend always observes foreground changes, which remember the last
external application when the tray icon takes focus. Window lifecycle events
(create/destroy/show/hide/reorder, state, location, name and cloaking, in three
narrow ranges) are hooked session-wide only while the panel is shown. With the
panel hidden and a pin, they are hooked only for the pinned windows' processes, plus
window destruction session-wide: a process-scoped hook ends with its process and
can miss the destruction of a window whose app exits on close (the native smoke
test caught this). Moves, carets and title changes elsewhere therefore no longer
wake Llampec while a window stays pinned. Bursts of window events with the panel
open are coalesced into one window enumeration. A pinned window's own moves are
checked with an identity and TOPMOST read, not a full snapshot. Every owned
pin has a process/thread identity and a Llampec-specific native ownership marker;
unpinning checks both. Windows already topmost before a request are not claimed.
Closing a target removes its record. A failed pin or unpin is reported in the UI.
Applications running with greater privileges may reject changes. Access denied
is reported with an explicit **Restart as administrator** button; only a user
click invokes the standard UAC prompt. Cancellation keeps the current instance.
An approved restart lets normal cleanup finish before the replacement acquires
the single-instance mutex. Activate the target and retry the shortcut afterwards.
Window groups that already contain visible topmost floating palettes are declined,
because Windows can propagate topmost changes between an owner and its windows.

`NativeWindowBorder` draws a hollow, nonactivating, mouse-transparent native
window. Event hooks update its geometry when the target moves, resizes, minimizes
or changes desktop visibility. It uses DWM visible frame bounds and scales the
border per monitor. Both the inner and outer contour follow the standard Windows
11 corner policy (8 DIP normal, 4 DIP small). For floating windows, the cutout
overlaps the native frame by `DWMWA_VISIBLE_FRAME_BORDER_THICKNESS`, already in
physical pixels; the outer contour and corner radius move inward by that amount
without changing the selected stroke width. This covers the native frame strip
that otherwise looks like a gap. An unavailable or zero thickness adds no inset.
Maximized/snapped windows use a square inner perimeter. DWM exposes requested
policy, not a precise rendered
radius; custom regions/layered shapes use a conservative square fallback. No polling
timer runs while idle. Border creation failures leave the pin usable and report
the missing outline. Normal application exit attempts to restore owned windows;
forced termination cannot run cleanup.

## Verification

The regular core test suite covers target capture, shell transitions, multiple
pins, ownership, recycled handles, failures, settings and shortcut transactions.
Run the opt-in native [smoke test](../tests/Llampec.AlwaysOnTop.Smoke/README.md)
for real Win32 pin/border checks using only its own synthetic windows. It does
not exercise the WinUI controls or replace a visual inspection of the panel.

## Border color (phase 2, 2026-10-10)

The user reported that the border did not look like the Windows accent color. It
used `DwmGetColorizationColor`, a darker frame shade (`#483223` against the accent
`#8B6F5C` on the Surface). The default now reads the documented
`UISettings.GetColorValue(UIColorType.Accent)`, the accent apps use, as PowerToys
does; DWM colorization is only a fallback. Accent changes reach the border through
the setting/colorization messages it already handles. High contrast still uses the
system highlight color, also over a custom color.

The Appearance section adds **Border color** (**Color del borde**, **Color de la
vora**): *Windows accent color* or *Custom color*. Custom shows an inline WinUI
`ColorPicker` (spectrum, brightness slider and hex input, no alpha) that starts
from the accent color. Changes repaint pinned borders and are saved 250 ms after
they settle, or when the panel closes. `AlwaysOnTopSettings.BorderColor` stores
`#RRGGBB`; null or invalid text means the accent color.

Validated on the Surface (ARM64): a pinned test window's border measured `#8B6F5C`
with the accent choice and `#00C060` after entering that hex value; the setting
was saved, and choosing the accent again restored it. The native smoke test
passed its 75 checks.

## Transparency and per-window appearance (phase 2, 2026-10-10)

The global Appearance section adds **Opacity** (30–100 %, default 100 %), saved
with the other appearance settings. Below it, a folded **Per-window appearance**
expander (**Apariencia por ventana**, **Aparença per finestra**) lists the windows
pinned at that moment. Each entry starts with **Use global appearance** on; turning
it off shows the same border, thickness, color and opacity controls for that window
alone, starting from the global values. The user asked for this split because
per-window changes are rarely needed. A window's own appearance belongs to its pin:
it is not saved and ends when the window is unpinned or closed, like the pin itself.
Global changes do not affect windows with their own appearance.

`AppearanceEditor` provides the controls for both uses and only reports changes;
the view saves the global value or passes a window's value to
`AlwaysOnTopService.SetWindowAppearance`. Spectrum and slider drags are reported
250 ms after they settle, and a pending change is applied when the panel closes.

Opacity uses the documented layered-window API on the target: `WS_EX_LAYERED` and
`SetLayeredWindowAttributes(LWA_ALPHA)`. At 100 % nothing is changed. The first
change records the window's original state: not layered, or layered with its
color key, alpha and flags. A window drawn with `UpdateLayeredWindow` has no
readable attributes and is declined with *This window does not support
transparency.*; its pin and border stay. The original state is restored when the
opacity returns to 100 %, on unpin, when another actor clears the pin, when the
module is removed and on normal exit. A closed or recycled window needs nothing.
Forced termination cannot restore it; the window stays translucent until it is
closed and reopened. Elevated windows reject the change like a pin. Some
GPU-composited or Store apps may ignore or partially apply layered alpha; this has
not been checked app by app.

The native smoke test now covers global and per-window opacity, clearing a
window's own appearance, restoration at 100 %, on unpin and on disposal, using
windows of another process (83 checks). Core tests cover the same rules with an
injected backend. The WinUI controls still need a visual check.

## Windows pinned by other applications (2026-10-17)

The user reported that Claude desktop, while controlling the PC, sometimes keeps its
window on top with no way to undo it; Llampec only answered that another application
already kept it on top. Such windows are now listed (after Llampec's own pins) with
**Pinned by another app**, and the count reads *N pinned · M by other apps*.

- Choosing one, from its row, the main button or the shortcut, removes "always on
  top" with the documented `SetWindowPos(HWND_NOTOPMOST)`. It is never claimed: it
  gets no marker, border or opacity, and becomes an ordinary window that Llampec can
  pin later. The tile shows it as on, with *Unpin: …*.
- **Restore all** (**Restaurar todo**, **Restaura-ho tot**) next to **Unpin all**
  unpins Llampec's windows and clears every other application window kept on top.
  Shell surfaces, tool and no-activate windows, and Llampec's own windows are not
  eligible, so the taskbar, flyouts and overlays are left alone.
- Only an explicit request does this. **Unpin all**, module removal and exit still
  restore Llampec's own pins only, as before.
- An application that pins its window again at once is reported: *The application
  keeps this window on top again. Change it in that application.* Elevated windows
  refuse it as they refuse a pin.

The native smoke test pins a helper window from the test process, as another
application would, and checks the listing, the single clear without a claim, and
Restore all together with a Llampec pin (89 checks).

## Phase 2: further appearance options

Custom border colors, transparency and per-window appearance are implemented
(above). Border opacity remains a possible future option; extend the existing
Appearance section when adding more. These options are recorded as future work, not as part
of the initial implementation. Transparency must preserve and restore each
window's original state when unpinned or on normal application exit.

## PowerToys attribution

Use [PowerToys Always on Top](https://github.com/microsoft/PowerToys/tree/main/src/modules/alwaysontop)
as a reference and adapt useful parts to Llampec's existing architecture. Credit
Microsoft PowerToys in acknowledgements. Retain the applicable copyright and
[MIT license](https://github.com/microsoft/PowerToys/blob/main/LICENSE) notices
for any reused or adapted code.
