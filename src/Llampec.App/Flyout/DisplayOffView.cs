using Llampec.Actions.DisplayOff;
using Llampec.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Llampec.Flyout;

/// <summary>
/// Turns off a single display, which neither suspends nor locks the PC, and offers the main button's
/// "turn off all". Displays are read when the page opens and after each change.
/// </summary>
public sealed class DisplayOffView : StackPanel, IDisposable
{
    private readonly DisplayOffAction _action;
    private readonly ExternalMonitorService _monitors;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _status = Note("Reading displays…");
    private bool _busy;
    private bool _disposed;

    /// <param name="turnOffAll">The tile's own action, which hides the panel before turning every display off.</param>
    public DisplayOffView(DisplayOffAction action, ExternalMonitorService monitors, Action turnOffAll)
    {
        _action = action;
        _monitors = monitors;
        Spacing = 12;
        Children.Add(Section("Turn off one display"));
        Children.Add(Note("The other display stays on, so the PC neither sleeps nor locks.", .75));
        Children.Add(_status);
        Children.Add(_rows);
        var all = new Button { Content = T("Turn off all displays"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        all.Click += (_, _) => turnOffAll();
        Children.Add(all);
        Children.Add(Note("Windows enters standby: programs pause and it may ask you to sign in. "
            + "To keep agents working with the screens dark, use Agentic work.", .75));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        await _monitors.RefreshAsync();
        var displays = ActiveDisplays.Read();
        var topology = ProjectionModes.TryGetCurrent();
        OnUi(DispatcherQueue, () => Build(DisplayPower.Rows(displays, topology, _monitors.Monitors)));
    }

    private void Build(IReadOnlyList<DisplayPowerRow> rows)
    {
        if (_disposed) return;
        _busy = false;
        _rows.Children.Clear();
        PanelControls.SetMessage(_status, rows.Count < 2 ? "Connect another display to turn off just one." : null);
        if (rows.Count < 2) return;
        foreach (var row in rows)
        {
            var toggle = new ToggleSwitch
            {
                Header = T(row.Name), IsOn = row.On, IsEnabled = row.CanToggle,
                OnContent = T("On"), OffContent = T(row.Internal ? "Off" : "Standby"),
            };
            toggle.Toggled += async (_, _) =>
            {
                if (_busy || toggle.IsOn == row.On) return;
                _busy = true;
                foreach (var other in _rows.Children.OfType<ToggleSwitch>()) other.IsEnabled = false;
                bool on = toggle.IsOn;
                try
                {
                    if (row.Monitor is { } monitor)
                    {
                        await _monitors.SetPowerAsync([monitor], on);
                        // A monitor that stays without a signal needs the connection rebuilt.
                        if (on) await App.Current.MonitorRecovery.RecoverAsync(force: false, justWoken: true);
                    }
                    else await Task.Run(on ? _action.TurnOnBuiltIn : _action.TurnOffBuiltIn);
                }
                catch (Exception error) { Diagnostics.Log.Warn($"Single display power change failed: {error.Message}"); }
                if (!_disposed) await LoadAsync();
            };
            _rows.Children.Add(toggle);
            if (row.On && !row.CanToggle) _rows.Children.Add(Note("Another display must stay on", .75));
        }
    }

    public void Dispose() => _disposed = true;
}
