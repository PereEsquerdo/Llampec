using Llampec.Platform;

namespace Llampec.Actions.Touch;

/// <summary>
/// Shows or hides the touch keyboard after the panel has hidden, so it serves the app underneath.
/// The sub-page chooses when Windows shows the keyboard's own taskbar button. The chevron is
/// highlighted while that button is always shown.
/// </summary>
public sealed class TouchKeyboardAction : QuickActionBase
{
    private const string IconGlyph = "\uE765"; // KeyboardClassic, as in the taskbar
    private readonly bool _available;
    private readonly Func<CancellationToken, Task> _toggle;
    private readonly Func<KeyboardIconMode?> _readIcon;
    private readonly TaskbarIconOption[] _options;
    private KeyboardIconMode? _mode;
    private string? _message;

    public TouchKeyboardAction() : this(TouchInput.KeyboardAvailable, TouchInput.ToggleKeyboardAsync,
        TouchInput.ReadKeyboardIcon, TouchInput.WriteKeyboardIcon) { }

    public TouchKeyboardAction(bool available, Func<CancellationToken, Task> toggle,
        Func<KeyboardIconMode?> readIcon, Func<KeyboardIconMode, bool> writeIcon)
    {
        ArgumentNullException.ThrowIfNull(writeIcon);
        _available = available;
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _readIcon = readIcon ?? throw new ArgumentNullException(nameof(readIcon));
        _options =
        [
            Option(KeyboardIconMode.Always, "always", "Always"),
            Option(KeyboardIconMode.WithoutKeyboard, "without-keyboard", "When no keyboard is attached"),
            Option(KeyboardIconMode.Never, "never", "Never"),
        ];

        TaskbarIconOption Option(KeyboardIconMode mode, string id, string title) => new($"touch-keyboard-icon-{id}", title, IconGlyph,
            () => _mode == mode, () => writeIcon(mode), OnIconChanged);
    }

    public override string Id => "touch-keyboard";
    public override string Title => "Touch keyboard";
    public override string Glyph => IconGlyph;
    public override ActionKind Kind => ActionKind.ButtonWithSubpage;
    public override IReadOnlyList<IQuickAction> SubActions => _options;
    public override bool SubActionsAreExclusive => true;

    public override void Refresh()
    {
        // One registry read; the options show this value instead of reading it again.
        _mode = _readIcon();
        IsAvailable = _available;
        State = _mode == KeyboardIconMode.Always ? ActionState.On : ActionState.Off;
        Subtitle = _message ?? (!_available ? "Touch keyboard unavailable" : _mode switch
        {
            KeyboardIconMode.Always => "Icon in taskbar",
            KeyboardIconMode.WithoutKeyboard => "Icon without keyboard",
            _ => null,
        });
        foreach (var option in _options) option.Refresh();
    }

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        _message = null;
        try
        {
            await _toggle(cancellationToken).ConfigureAwait(false);
            Diagnostics.Log.Info("Touch keyboard toggled.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Diagnostics.Log.Warn($"Touch keyboard failed: {ex.Message}");
            _message = "Could not open the touch keyboard.";
        }
    }

    private void OnIconChanged(bool confirmed)
    {
        _message = confirmed ? null : "Could not change the taskbar icon.";
        Refresh();
    }
}
