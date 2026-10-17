using Llampec.Platform;

namespace Llampec.Actions.Monitor;

/// <summary>
/// "External monitor": brightness, contrast, volume and power of DDC/CI monitors, which Windows Quick
/// Settings does not control. Tapping puts the external monitors into their own standby, or wakes them:
/// Windows keeps the display in the desktop, so nothing moves, the PC does not enter standby and the
/// session is not locked. The tile reads power and brightness in the background when the panel opens.
/// Standby is refused when no other display would stay lit: Windows would not know the screens are
/// dark, and nothing visible could wake them.
/// </summary>
public sealed class MonitorAction : QuickActionBase, IDisposable
{
    private readonly ExternalMonitorService _monitors;
    private readonly Func<IReadOnlyList<ActiveDisplay>> _displays;
    private string? _message;
    private bool _messageIsNew; // survives the refresh that follows the tap, not the next panel opening

    private readonly MonitorRecovery? _recovery;

    /// <param name="displays">The desktop displays; defaults to <see cref="ActiveDisplays.Read"/>.</param>
    /// <param name="recovery">Used when tapping a monitor that stayed without a signal, which a wake-up alone does not fix.</param>
    public MonitorAction(ExternalMonitorService monitors, Func<IReadOnlyList<ActiveDisplay>>? displays = null, MonitorRecovery? recovery = null)
    {
        _monitors = monitors;
        _displays = displays ?? ActiveDisplays.Read;
        _recovery = recovery;
        _monitors.Changed += OnMonitorsChanged;
        Update();
    }

    private void OnMonitorsChanged(object? sender, EventArgs e) => Update();
    public void Dispose() => _monitors.Changed -= OnMonitorsChanged;

    public ExternalMonitorService Monitors => _monitors;
    public override string Id => "monitor";
    public override string Title => "External monitor";
    public override string Glyph => ""; // Brightness
    public override ActionKind Kind => ActionKind.ToggleWithSubpage;

    public override void Refresh()
    {
        if (_messageIsNew) _messageIsNew = false; else _message = null;
        Update();
        _ = _monitors.RefreshAsync();
    }

    private void Update()
    {
        var monitors = _monitors.Monitors;
        IsAvailable = !_monitors.HasRead || monitors.Count > 0;
        bool on = monitors.Any(m => m.PoweredOn != false);
        State = monitors.Count > 0 && on ? ActionState.On : ActionState.Off;
        Subtitle = _message ?? (!_monitors.HasRead ? null
            : monitors.Count == 0 ? "No DDC/CI monitor"
            : monitors.Any(NoSignal) ? "No signal"
            : !on ? "Standby"
            : monitors.FirstOrDefault(m => m.PoweredOn != false)?.Brightness is { } brightness
                ? Settings.UiText.Format("Brightness {0} %", brightness.Percent) : "On");
    }

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        await _monitors.RefreshAsync().ConfigureAwait(false);
        var monitors = _monitors.Monitors;
        bool turnOn = monitors.All(m => m.PoweredOn == false);
        if (turnOn && _recovery is not null && monitors.Any(NoSignal))
        {
            Diagnostics.Log.Info($"Monitor tap: recovery {await _recovery.RecoverAsync(force: false).ConfigureAwait(false)}");
            return;
        }
        if (!turnOn && !DisplayPower.LeavesALitDisplay(_displays(), monitors.Select(m => m.Monitor), monitors))
        {
            Diagnostics.Log.Info("Monitor standby refused: no other display would stay on.");
            _message = "Another display must stay on";
            _messageIsNew = true;
            return;
        }
        await _monitors.SetPowerAsync(monitors.Select(m => m.Monitor), turnOn).ConfigureAwait(false);
    }

    /// <summary>In standby that nobody asked for: usually a monitor that found no signal after sleep.</summary>
    private bool NoSignal(MonitorStatus status) =>
        status.PowerState == MonitorRecovery.NoSignalStandby && !_monitors.IsStandbyByLlampec(status.Monitor);
}
