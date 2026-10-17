# External monitor

The **External monitor** button (**Monitor externo** in Spanish, **Monitor extern**
in Catalan/Valencian) controls monitors connected by cable through DDC/CI, which
Windows Quick Settings does not do. Its caption shows the brightness of the first
monitor that is on.

- **Tap:** puts every DDC/CI monitor into its own standby (VCP `D6` = 4), or wakes
  them (`D6` = 1). Windows keeps the monitor in the desktop, so windows do not
  move, the PC does not enter standby and the session does not lock. This is the
  way to turn off *only* the external monitor; **Multiple displays → Second screen
  only** turns off only the built-in panel.
- **Last lit display:** standby is refused, with a note, when no other display would
  stay lit (for example with the built-in panel already off): Windows would not know
  the screens are dark and nothing visible could wake them. **Turn off displays**
  and **Agentic work** cover that case.
- **Turn off displays → options** lists the built-in panel and each DDC/CI monitor
  with the same rule (`Platform/DisplayPower`). The built-in panel returns to the
  topology it had before.
- **Options:** for each monitor, a power switch and sliders for brightness (`10`),
  contrast (`12`) and volume (`62`, only when the monitor has speakers and answers it).
  Changes apply while dragging; only the latest value is sent once the bus is free.
- Built-in panels have no DDC/CI channel. Their brightness stays in Windows Quick
  Settings.

## Monitor without picture after sleep

Sometimes, after Modern Standby, Windows still lists the external monitor as active
(Extend) but it stays dark. Diagnosed on 2026-10-16 with the Philips behind a USB4
hub: after the 13:35 resume the USB4 connection manager logged no "monitor
enumerated" event (it did after earlier resumes), the monitor answered DDC/CI and
reported `D6` = 4, and a DDC/CI wake-up lasted about two seconds before it slept
again for lack of signal. The DisplayPort tunnel had not come back.

| Attempt | Result |
| --- | --- |
| `D6` = 1 | awake ~2 s, then standby again |
| Same mode again (`CDS_RESET`), alone or right after a wake-up | no effect (9 ms: no real modeset) |
| 50 Hz and back to 60 Hz | no effect |
| Wake, then input HDMI 1 (`60` = `0x11`) | no effect |
| PC screen only → Extend while the monitor slept | no effect |
| **Wake, then PC screen only → Extend within the two seconds** | **recovered**: USB4 event at once, `D6` = 1 stable |

`Platform/MonitorRecovery` repeats that sequence, escalating: wake and check after
4 s; if it sleeps again, wake, PC screen only, back to the previous topology
(retried: the first attempt can fail with error 31 while the change settles), and
check again.

- **Recover monitor** in the External monitor options runs it on demand, even
  when no monitor answers DDC/CI.
- **Recover automatically when the screen turns on** (on by default): `SystemEvents`
  receives `GUID_CONSOLE_DISPLAY_STATE`; 8 s after Windows turns the screen back
  on, one DDC/CI read checks for a monitor in no-signal standby, and recovers it
  only then. A screen-off cancels the pending check. Nothing else runs.
- The tile shows **No signal** in that state, and tapping it recovers instead of
  only waking. Waking a monitor from the options also checks it afterwards.
- Left alone: monitors that Llampec put into standby (tracked by name until woken),
  monitors switched off with their own button (`D6` = 5), and everything while
  agentic work is active.

It cannot replace a physical reconnection in every case; the result says so.

## Implementation

- `Platform/ActiveDisplays` lists desktop displays with CCD (friendly name, built-in
  output technology) and GDI (monitor handle, bounds).
- `Platform/ExternalMonitors` uses the documented Monitor Configuration API
  (`dxva2.dll`: `GetPhysicalMonitorsFromHMONITOR`, `GetVCPFeatureAndVCPFeatureReply`,
  `SetVCPFeature`). Physical-monitor handles are opened for each exchange and
  released at once. Reads and writes retry up to three times, 50 ms apart.
- `ExternalMonitorService` runs one DDC/CI exchange at a time on the thread pool,
  never on the UI thread. It reads power and brightness when the panel opens, plus
  contrast and volume when the options page opens. There is no polling or timer.
  A monitor that answered before and misses a whole exchange (for example just after
  waking) is kept rather than removed from the list.
- The capabilities string is not used: the tested monitor does not return a valid
  one, although each VCP code works. A monitor is listed when it answers power or
  brightness.

## Validation

Philips PHL 329M1RV on a Surface (ARM64, Qualcomm Adreno X2-85, build 28000),
connected by USB-C DisplayPort through an HDMI adapter, 2026-10-16:

| Exchange | Result |
| --- | --- |
| Capabilities string | fails (`0xC026258B`) |
| Read one VCP code | ~185 ms |
| Read power + brightness / with contrast and volume | ~370 ms / ~780 ms |
| Write brightness 50 → 45 → 50 | applied and read back |
| `D6` = 4 for 6 s, then `D6` = 1 | monitor kept in the desktop; answers DDC/CI while in standby; on again in ~1 s |

Checked by hand on 2026-10-17: the panel goes dark and comes back with Llampec. The
monitor's own power button does not undo DDC/CI standby: it switches the monitor off
and on, and it stays without a signal. Wake it from Llampec (the tile, its options or
any key in agentic work); **Recover monitor** also does it. Still to check: how long
it stays in DDC/CI standby.
