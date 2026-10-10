using Llampec.Actions.Fullscreen;
using Llampec.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Llampec.Flyout;

/// <summary>Disposable status and shortcut controls for the application-owned fullscreen service.</summary>
public sealed class FullscreenView : StackPanel, IDisposable
{
    private readonly FullscreenService _service;
    private readonly TextBlock _status = Note("");
    private readonly TextBlock _target = Note("");
    private readonly TextBlock _monitor = Note("");
    private readonly TextBlock _windowError = Note("");
    private readonly Button _toggle = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    private bool _disposed;

    public FullscreenView(FullscreenService service)
    {
        _service = service;
        Spacing = 10;
        Children.Add(_status);
        Children.Add(_target);
        Children.Add(_monitor);
        _toggle.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _toggle.Click += (_, _) => _service.ToggleTarget();
        Children.Add(_toggle);
        Children.Add(_windowError);
        Children.Add(Note("Hover at the bottom edge or swipe up to show the taskbar. Switching to another window restores the previous window."));

        Children.Add(new ShortcutEditor(FullscreenSettings.DefaultHotkey,
            "Enters or exits full screen for the active window. Leave empty to disable.",
            () => App.Current.Settings.Fullscreen.Hotkey, App.Current.TrySetFullscreenHotkey, App.Current.FullscreenHotkeyError));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_windowError, AutomationLiveSetting.Polite);

        _service.Changed += OnChanged;
        _service.Refresh();
        Update();
    }

    private void OnChanged(object? sender, EventArgs e) => OnUi(DispatcherQueue, Update);

    private void Update()
    {
        if (_disposed) return;
        bool active = _service.IsActive;
        _status.Text = UiText.Get(active ? "Full screen is on" : "Full screen is off");
        _target.Text = active ? _service.ActiveTitle
            : _service.HasTarget ? _service.TargetTitle : UiText.Get("No eligible window is active.");
        _monitor.Text = active ? UiText.Format("Active on {0}", _service.ActiveMonitor) : "";
        _monitor.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = UiText.Get(active ? "Exit full screen" : "Enter full screen");
        _toggle.IsEnabled = active || _service.HasTarget;
        AutomationProperties.SetName(_toggle, (string)_toggle.Content);
        AutomationProperties.SetHelpText(_toggle, active ? $"{_target.Text}. {_monitor.Text}" : _target.Text);
        SetMessage(_windowError, _service.Error);
    }

    public void Dispose()
    {
        _disposed = true;
        _service.Changed -= OnChanged;
    }
}
