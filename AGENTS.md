# Project decisions

Read [the agreed Always on Top design](docs/ALWAYS_ON_TOP.md) before working on
that feature. Phase 1 is implemented, with fixes in 0.2.0-alpha.4; phase 2 is in progress.

The user confirmed on 2026-09-18 that phase 1 must include an Appearance section
with a border on/off toggle and a border-thickness control. Window transparency
and further appearance customization belong to phase 2. On 2026-10-10 the user
started phase 2: the border uses the real accent color or a custom one, global
opacity, and a folded per-window appearance section for pinned windows.

On 2026-10-02 the user reaffirmed Llampec's scope: simple, lightweight access
to existing Windows settings, without deep operating-system changes. Native
rotation-animation and global touch-posture research are parked unless a simple,
supported approach fits that scope; their earlier prototype proposals are not
the current implementation plan.

For future touch discussions, read the touchscreen-gesture research in
[Tablet controls](docs/TABLET.md#touchscreen-gestures-research).
Do not confuse configurable touchpad gestures with touchscreen gestures.
The user confirmed removing Widgets, explaining the likely missing destination
of the left-edge swipe. Research and quick-action suggestions do not select
the next feature to implement.

On 2026-10-05 the user requested full-name attribution: **Pere Esquerdo Ramis**
in copyright, author metadata and the app's About page. Identify Llampec as
open-source under MIT and link its source at https://github.com/PereEsquerdo/Llampec.
This attribution was published with 0.2.0-alpha.8 on 2026-10-06; keep it in
later releases.

On 2026-10-06 the user described Llampec's direction as becoming what Windows 11
Quick Settings should have been, with extras. Planned future areas: power options,
network settings, wireless projection, webcam handling, and launchers for the
volume and brightness panels. These are roadmap items, not a selected next task.

On 2026-10-07 the user approved one exception to documented APIs: the power mode
uses the undocumented `powrprof.dll` overlay functions that Windows Settings uses,
because a failure only disables the control. Night light was rejected for lacking
an API. Llampec should add what Windows lacks or does poorly, not duplicate the
native Quick Settings. The same day added Power, MX mouse (battery only; Easy-Switch
declined) and a minimal Camera activity button that opens Windows camera settings.

On 2026-10-10 the user added Touch keyboard and Virtual touchpad buttons with
taskbar-icon options. After testing the documented `InputPane.TryShow` (nothing
with the keyboard attached, unreliable when detached), the user accepted a second
undocumented exception: the shell's `ITipInvocation` for the touch keyboard. See
[touch input](docs/TOUCH_INPUT.md).

On 2026-10-16 the user asked, before the beta, for turning off a single display
without locking, DDC/CI brightness for external monitors and an agentic-work mode
that keeps the PC working and reachable with dark screens. Implemented as the
External monitor and Agentic work modules; see [agentic work](docs/AGENT_MODE.md)
for what is measured and what still needs hands-on validation.

On 2026-10-17, before making the repository public, the user asked for all GitHub
documentation in English and a short README written in a natural, first-person
voice, with the details in [using](docs/USING.md) and [development](docs/DEVELOPMENT.md).
Project history for the README: the first Llampec was an internal, text-only tool
the user wrote in 2016 for a small development and design company, covering gaps in
Windows of that time (pin windows, caffeine, monitor off, full screen, dark mode once
Windows added it to Settings). The logo is the app icon's bolt beside or above the
name in Outfit SemiBold (`docs/images`). The dedication is always exactly
**Dedicat a Lara i a Miquel 🫶**, in Catalan in every language, in the README and
the About page.

Keep the original lightweight design: no recurring work for disposable layout
controls, no idle polling for window features and no speculative memory trimming.
The user considers the observed roughly 18 MB idle physical memory acceptable;
verify resource changes under comparable states instead of treating this as a
cross-device memory guarantee.
