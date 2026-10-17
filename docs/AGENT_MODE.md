# Agentic work

The **Agentic work** button (**Trabajo agéntico**, **Treball agèntic**) keeps the PC
working and reachable for local agents and remote tools with the screens dark: no
OLED wear, lower consumption, and by default no standby or lock. It combines
existing Windows features and undoes each of them on exit. It was requested on
2026-10-16, before the beta.

## Behaviour

Pressing the button hides the panel and, after 0.5 s, applies the options that are on:

| Option (default) | What it does | Undone on exit |
| --- | --- | --- |
| Turn off the built-in display (on) | **Second screen only**, when another display is active. The panel is fully off. | Previous topology, unless Win+P changed it meanwhile |
| Put external monitors in standby (on) | DDC/CI `D6` = 4. Windows keeps them in the desktop. | `D6` = 1 for the monitors it turned off |
| Cover displays that stay on (on) | Black, click-through window, excluded from capture | Removed |
| Keep the session unlocked (on) | Power request with the display required: Windows neither sleeps nor locks | Request released |
| Best power efficiency (off) | Power mode | Previous mode, unless changed meanwhile |

The system-required power request is always taken, as in Caffeine.

**Exit:** any key, mouse button, wheel, deliberate mouse movement (150 counts), touch,
pen or touchpad contact on this PC. Input that agents or remote-desktop tools inject
with `SendInput` has no source device and does not exit. Agent mode also ends when
Windows turns the display back on after turning it off (the unlocked option off),
on suspend, on application exit, and when its button is removed. Pressing the button
again while active also ends it.

With **Keep the session unlocked** off, Windows turns the screens off after its own
timeout and locks the session according to its sign-in setting. Agents keep working
(see [Caffeine and Modern Standby](CAFFEINE.md#modern-standby-turn-off-display-and-local-agents)),
with the lowest consumption. On return, the first input wakes Windows' display and
Llampec restores everything at the sign-in screen.

## Design notes

- **Covers.** On OLED, black pixels emit no light, so a cover stops wear without
  removing the display. `WDA_EXCLUDEFROMCAPTURE` keeps it out of screen capture, so
  computer-use agents and remote tools see and click the desktop beneath
  (`WS_EX_LAYERED | WS_EX_TRANSPARENT`). Covers are re-raised on each foreground
  change. Shell flyouts and notifications can still appear briefly above them.
- **Pointer.** A still pointer marks an OLED. It is parked on a monitor in standby,
  or in the bottom-right pixel of a covered display.
- **Unplugging.** If the external monitor disappears, Windows lights the built-in
  panel again; agent mode covers it.
- **Recovery.** What will change is saved in `agent-mode-recovery.json` before
  any change. If Llampec ends without restoring (crash, forced shutdown), the next
  start restores the topology, power mode and monitors, then deletes the file.
- **Cost.** Nothing runs while inactive. While active: one hidden window with raw
  input (`RIDEV_INPUTSINK`), a display-state notification and a foreground WinEvent
  hook. No timer or polling.
- Everything uses documented APIs; power mode reuses the approved `powrprof`
  overlay exception described in [Power](POWER.md).

## Measurements

Surface (ARM64, build 28000), plugged in, with a Philips PHL 329M1RV, 2026-10-16,
Windows Energy Meter counters (`system` rail, 15–20 s averages; noise about ±0.5 W):

| State | System rail |
| --- | --- |
| Both displays on (built-in panel already at 0 % brightness) | 8.2–11.0 W |
| Black cover over the built-in OLED | −0.2 to −0.3 W |
| Philips in DDC standby | no change on the Surface (the monitor's own mains consumption drops) |
| Agentic work (built-in off, Philips in standby) | **6.0 W** (from 8.2 W just before) |

End-to-end on the same hardware: the topology switched to Second screen only, the
Philips entered standby, an injected click did not end the mode, and application exit
restored Extend, `D6` = 1 and all ten visible windows to their previous positions,
leaving no recovery file. A cover was confirmed excluded from capture (average
luminance 70.6 vs 71.1 without it) and injected keyboard and mouse input produced
no physical-input events.

Not yet verified by hand: exit by physical key, click, touch and touchpad; the
locking variant (display off by timeout, then wake at the sign-in screen); a single
built-in display covered for a long run; consumption on battery.

## Remote access on Windows 11 Home

Windows 11 Home cannot host Remote Desktop. With agent mode active:

- **Claude Code Remote Control** or SSH (the OpenSSH Server optional feature, for
  example over Tailscale) only need the process running and the network. The lock
  does not matter, so **Keep the session unlocked** can be turned off for the lowest
  consumption.
- **Desktop viewers** (Chrome Remote Desktop, RustDesk and similar) show the console
  session. With the session unlocked they see the desktop beneath the covers and
  their input does not end the mode.
