using Llampec.Platform;

namespace Llampec.Actions.Touch;

/// <summary>
/// Opens Windows' virtual touchpad after the panel has hidden. The sub-page shows or hides its own taskbar
/// button. The chevron is highlighted while that button is shown.
/// </summary>
public sealed class TouchpadAction : QuickActionBase
{
    private const string IconGlyph = "\uEFA5"; // Touchpad, as in the taskbar
    private readonly bool _available;
    private readonly Action _open;
    private readonly Func<bool?> _readIcon;
    private readonly TaskbarIconOption[] _options;
    private bool _visible;
    private string? _message;

    public TouchpadAction() : this(TouchInput.TouchpadAvailable, TouchInput.OpenTouchpad,
        TouchInput.ReadTouchpadIcon, TouchInput.WriteTouchpadIcon) { }

    public TouchpadAction(bool available, Action open, Func<bool?> readIcon, Func<bool, bool> writeIcon)
    {
        ArgumentNullException.ThrowIfNull(writeIcon);
        _available = available;
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _readIcon = readIcon ?? throw new ArgumentNullException(nameof(readIcon));
        _options =
        [
            new("touchpad-icon", "Show icon in taskbar", IconGlyph, () => _visible,
                () => writeIcon(_readIcon() != true), OnIconChanged),
        ];
    }

    public override string Id => "touchpad";
    public override string Title => "Virtual touchpad";
    public override string Glyph => IconGlyph;
    public override ActionKind Kind => ActionKind.ButtonWithSubpage;
    public override IReadOnlyList<IQuickAction> SubActions => _options;

    public override void Refresh()
    {
        // One registry read; the switch shows this value instead of reading it again.
        _visible = _readIcon() == true;
        IsAvailable = _available;
        State = _visible ? ActionState.On : ActionState.Off;
        Subtitle = _message ?? (!_available ? "Virtual touchpad unavailable" : _visible ? "Icon in taskbar" : null);
        foreach (var option in _options) option.Refresh();
    }

    protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _message = null;
        try
        {
            _open();
            Diagnostics.Log.Info("Virtual touchpad opened.");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"Virtual touchpad failed: {ex.Message}");
            _message = "Could not open the virtual touchpad.";
        }
        return Task.CompletedTask;
    }

    private void OnIconChanged(bool confirmed)
    {
        _message = confirmed ? null : "Could not change the taskbar icon.";
        Refresh();
    }
}
