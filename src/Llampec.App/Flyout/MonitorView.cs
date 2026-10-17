using System.Globalization;
using Llampec.Platform;
using Llampec.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Llampec.Flyout;

/// <summary>
/// Power, brightness, contrast and volume of each DDC/CI monitor. The values are read once when the
/// page opens; the controls are then built and send changes without reading them back.
/// </summary>
public sealed class MonitorView : StackPanel, IDisposable
{
    private readonly ExternalMonitorService _service;
    private readonly StackPanel _monitors = new() { Spacing = 16 };
    private readonly TextBlock _status = Note("Reading monitor settings…");
    private readonly Dictionary<string, ToggleSwitch> _power = [];
    private string? _builtFor;
    private bool _updating;
    private bool _disposed;

    public MonitorView(ExternalMonitorService service)
    {
        _service = service;
        Spacing = 12;
        Children.Add(_status);
        Children.Add(_monitors);
        Children.Add(Note("Uses DDC/CI. If a monitor does not appear, turn on DDC/CI in its own menu. "
            + "Standby keeps the monitor in the desktop: windows do not move and Windows does not lock.", .75));
        Children.Add(Note("Windows Quick Settings controls the built-in display's brightness.", .75));
        Children.Add(RecoverySection());
        _service.Changed += OnChanged;
        _ = LoadAsync();
    }

    private StackPanel RecoverySection()
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(Section("Monitor without picture", 8));
        section.Children.Add(Note("If Windows lists the monitor but it stays dark, for example after sleep, "
            + "Llampec wakes it and rebuilds the connection. The screens may flicker for a moment.", .75));
        var result = Note("");
        PanelControls.SetMessage(result, null);
        var recover = new Button { Content = T("Recover monitor") };
        recover.Click += async (_, _) =>
        {
            recover.IsEnabled = false;
            PanelControls.SetMessage(result, "Recovering…");
            var outcome = await App.Current.MonitorRecovery.RecoverAsync(force: true);
            if (_disposed) return;
            PanelControls.SetMessage(result, outcome switch
            {
                MonitorRecoveryResult.Failed => "The monitor did not come back. Try unplugging and reconnecting it.",
                _ => "Done. If it is still dark, try unplugging and reconnecting it.",
            });
            recover.IsEnabled = true;
            await LoadAsync();
        };
        section.Children.Add(recover);
        section.Children.Add(result);
        var automatic = new ToggleSwitch
        {
            Header = T("Recover automatically when the screen turns on"), OnContent = T("On"), OffContent = T("Off"),
            IsOn = App.Current.Settings.Monitor.RecoverAfterWake,
        };
        automatic.Toggled += (_, _) =>
        {
            var settings = App.Current.Settings;
            var previous = settings.Monitor;
            var next = previous.Clone();
            next.RecoverAfterWake = automatic.IsOn;
            settings.Monitor = next;
            if (SettingsStore.Save(settings)) return;
            settings.Monitor = previous;
            PanelControls.SetMessage(result, "Could not save settings.");
        };
        section.Children.Add(automatic);
        section.Children.Add(Note("Checks once, a few seconds after Windows turns the screen on, whether a monitor stayed without a signal.", .75));
        return section;
    }

    private async Task LoadAsync()
    {
        await _service.RefreshAsync(details: true);
        OnUi(DispatcherQueue, Update);
    }

    private void OnChanged(object? sender, EventArgs e) => OnUi(DispatcherQueue, Update);

    private void Update()
    {
        if (_disposed || !_service.HasRead) return;
        var monitors = _service.Monitors;
        // Rebuild only when the monitor list changes, never under a slider the user is dragging.
        string key = string.Join("|", monitors.Select(m => $"{m.Monitor.Key}:{m.Contrast is not null}:{m.Volume is not null}"));
        if (key != _builtFor) Build(monitors, key);
        _updating = true;
        foreach (var status in monitors)
            if (_power.TryGetValue(status.Monitor.Key, out var toggle)) toggle.IsOn = status.PoweredOn != false;
        _updating = false;
    }

    private void Build(IReadOnlyList<MonitorStatus> monitors, string key)
    {
        _builtFor = key;
        _monitors.Children.Clear();
        _power.Clear();
        PanelControls.SetMessage(_status, monitors.Count == 0 ? "No monitor answered over DDC/CI." : null);
        foreach (var status in monitors)
        {
            var monitor = status.Monitor;
            var group = new StackPanel { Spacing = 4 };
            group.Children.Add(Section(monitor.Name));
            var power = new ToggleSwitch { Header = T("Monitor on"), OnContent = T("On"), OffContent = T("Standby"), IsOn = status.PoweredOn != false };
            power.Toggled += async (_, _) =>
            {
                if (_updating) return;
                power.IsEnabled = false;
                await _service.SetPowerAsync([monitor], power.IsOn);
                if (power.IsOn) await App.Current.MonitorRecovery.RecoverAsync(force: false, justWoken: true);
                if (!_disposed) power.IsEnabled = true;
            };
            _power[monitor.Key] = power;
            group.Children.Add(power);
            AddSlider(group, monitor, MonitorFeature.Brightness, "Brightness", status.Brightness);
            AddSlider(group, monitor, MonitorFeature.Contrast, "Contrast", status.Contrast);
            AddSlider(group, monitor, MonitorFeature.Volume, "Volume", status.Volume);
            _monitors.Children.Add(group);
        }
    }

    private void AddSlider(StackPanel group, DdcMonitor monitor, MonitorFeature feature, string title, FeatureValue? value)
    {
        if (value is not { Maximum: > 0 } known) return;
        var slider = new Slider
        {
            Header = T(title), Minimum = 0, Maximum = known.Maximum, StepFrequency = 1, SmallChange = 1,
            LargeChange = Math.Max(1, known.Maximum / 10), Value = Math.Min(known.Current, known.Maximum),
            TickPlacement = TickPlacement.None, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(slider, $"{monitor.Name}: {T(title)}");
        slider.ThumbToolTipValueConverter = new PercentConverter(known.Maximum);
        slider.ValueChanged += (_, args) => _service.Set(monitor, feature, (uint)Math.Round(args.NewValue));
        group.Children.Add(slider);
    }

    private sealed class PercentConverter(uint maximum) : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is double current ? UiText.Format("{0}%", ((int)Math.Round(100 * current / maximum)).ToString(CultureInfo.CurrentCulture)) : "";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        _disposed = true;
        _service.Changed -= OnChanged;
    }
}
