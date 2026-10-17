using Llampec.Actions;
using Llampec.Actions.AgentMode;
using Llampec.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Llampec.Flyout;

/// <summary>What agentic work changes. Each switch is saved at once and applies from the next activation.</summary>
public sealed class AgentModeView : StackPanel, IDisposable
{
    private readonly AgentModeAction _action;
    private readonly TextBlock _status = Note("");
    private readonly Button _toggle = new();
    private readonly Action _run;
    private bool _disposed;

    /// <param name="run">The tile's own action, which hides the panel before starting.</param>
    public AgentModeView(AgentModeAction action, Action run)
    {
        _action = action;
        _run = run;
        Spacing = 12;
        Children.Add(Note("Keeps the PC working for agents and remote access with the screens dark. "
            + "To exit, press a key, click, touch the screen or move the mouse on this PC. "
            + "Input from agents and remote tools does not exit."));
        Children.Add(_status);
        var settings = App.Current.Settings.AgentMode;
        Add("Turn off the built-in display", "When another display is on. Its windows move to that display until you exit.",
            settings.TurnOffBuiltInDisplay, (s, on) => s.TurnOffBuiltInDisplay = on);
        Add("Put external monitors in standby", "Uses DDC/CI. Windows keeps them in the desktop.",
            settings.MonitorStandby, (s, on) => s.MonitorStandby = on);
        Add("Cover displays that stay on", "With black, which turns OLED pixels off. Agents and remote tools still see the desktop.",
            settings.CoverDisplays, (s, on) => s.CoverDisplays = on);
        Add("Keep the session unlocked", "Windows keeps the screen on for itself, so it neither sleeps nor locks. "
            + "Off: Windows turns the screens off after its usual time and locks the session; agents keep working with lower consumption.",
            settings.KeepUnlocked, (s, on) => s.KeepUnlocked = on);
        Add("Best power efficiency", "Switches the power mode while active and restores it on exit.",
            settings.BestEfficiency, (s, on) => s.BestEfficiency = on);
        _toggle.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _toggle.Click += (_, _) => _run();
        Children.Add(_toggle);
        _action.Changed += OnChanged;
        Update();
    }

    private void Add(string title, string description, bool value, Action<AgentModeSettings, bool> write)
    {
        var toggle = new ToggleSwitch { Header = T(title), OnContent = T("On"), OffContent = T("Off"), IsOn = value };
        toggle.Toggled += (_, _) =>
        {
            var settings = App.Current.Settings;
            var previous = settings.AgentMode;
            var next = previous.Clone();
            write(next, toggle.IsOn);
            settings.AgentMode = next;
            if (SettingsStore.Save(settings)) { Update(); return; }
            settings.AgentMode = previous;
            PanelControls.SetMessage(_status, "Could not save settings.");
        };
        var group = new StackPanel { Spacing = 0 };
        group.Children.Add(toggle);
        group.Children.Add(Note(description, .75));
        Children.Add(group);
    }

    private void OnChanged(object? sender, EventArgs e) => OnUi(DispatcherQueue, Update);

    private void Update()
    {
        if (_disposed) return;
        bool active = _action.State == ActionState.On;
        PanelControls.SetMessage(_status, _action.Error ?? (active ? "Active. Changes apply the next time you start it." : null));
        _toggle.Content = T(active ? "Exit agentic work" : "Start agentic work");
        _toggle.IsEnabled = !_action.IsBusy;
    }

    public void Dispose() { _disposed = true; _action.Changed -= OnChanged; }
}
