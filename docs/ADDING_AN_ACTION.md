# Adding an action

Llampec uses a WinUI 3 frontend and a UI-independent Core project.
There is no plugin discovery or DI container.

1. Create a class under `src/Llampec.Core/Actions/<Name>/` deriving from `QuickActionBase`.
2. Implement stable `Id`, `Title`, `Glyph`, `Kind`, `Refresh()` and `ExecuteCoreAsync()`.
3. Describe it in `ModuleCatalog.All` (catalog order is the default display order) and
   create it in `App.EnableModule`; release any service it owns in `RemoveModuleAsync`.
   `ModuleCatalogTests` checks that the descriptor matches the action.
4. Add a view to `FlyoutWindow.CreateSubpageView` if its sub-page needs more than the
   generic switches or radio buttons.
5. Test both system themes, keyboard navigation and multiple display scales.

## Contract

- Re-read actual system state in `Refresh`; do not assume a successful change.
- `ActionKind.Button` awaits completed panel dismissal before execution. Pass the
  operation to the existing `RunWithPanelHiddenRequested` callback; the window
  checks visibility and reopening before invoking it. Do not estimate animation
  completion with a fixed delay. Interrupted dismissal or reopening must cancel
  the pending action.
  `ScreenshotAction` uses this flow before emitting Win+Shift+S. Display power
  additionally retains its short delay so the triggering input does not immediately
  wake the monitor.
- `ToggleWithSubpage` exposes independent child switches. Set
  `SubActionsAreExclusive` for a radio-button list such as projection.
  `ButtonWithSubpage` combines a button (run with the panel hidden) with the same
  sub-page; `State` On highlights its arrow, as for the touch keyboard.
- `Subtitle` describes secondary state; `GlyphBadge` adds a small secondary glyph.
- Use documented Win32 APIs through `Interop`, or WinRT APIs through the Windows SDK.
- Observe each API's threading, permissions and package-identity requirements.
  In particular, location access must originate from a foreground user gesture.
- Reuse `Platform.KeyboardShortcut` for a complete keyboard chord. Its held-key
  guard and partial-emission cleanup are shared with Logitech button assignments;
  it does not bypass Windows input-integrity restrictions.
- Do not introduce telemetry. Justify new dependencies and their maintenance cost.

The current view models snapshot child actions at construction. Dynamic hardware
discovery and child-list reconciliation remain a separate follow-up.
Some existing tests access real hardware; keep hardware-changing tests opt-in.
