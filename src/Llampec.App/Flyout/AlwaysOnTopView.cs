using Llampec.Actions.AlwaysOnTop;
using Llampec.Platform;
using Llampec.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Llampec.Flyout;

/// <summary>Disposable controls for the application-owned pinning service.</summary>
public sealed class AlwaysOnTopView : StackPanel, IDisposable
{
    private readonly AlwaysOnTopService _service;
    private readonly StackPanel _windows = new() { Spacing = 4 };
    private readonly Dictionary<nint, WindowRow> _rows = [];
    private readonly TextBlock _count = Note("");
    private readonly TextBlock _windowError = Note("");
    private readonly Button _restartAdministrator = new()
    {
        Content = UiText.Get("Restart as administrator"),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _empty = Note("No windows available.");
    private readonly Button _unpinAll = new() { Content = UiText.Get("Unpin all") };
    private readonly Button _restoreAll = new() { Content = UiText.Get("Restore all") };
    private readonly AppearanceEditor _appearance;
    private readonly TextBlock _appearanceError = Note("");
    private readonly StackPanel _perWindow = new() { Spacing = 8 };
    private readonly TextBlock _perWindowEmpty = Note("Pin a window to adjust it separately.");
    private readonly Dictionary<nint, WindowAppearanceRow> _appearanceRows = [];

    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _disposed;
    private string? _restartError;

    public AlwaysOnTopView(AlwaysOnTopService service, Action<bool>? holdOpen = null)
    {
        _service = service;
        Spacing = 10;
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(150);
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += OnRefreshTick;
        Children.Add(Section("Choose a window"));
        Children.Add(_count);
        var windowScroll = new ScrollViewer
        {
            Content = _windows, MaxHeight = 208,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        AutomationProperties.SetName(windowScroll, UiText.Get("Choose a window"));
        Children.Add(windowScroll);
        Children.Add(_empty);
        Children.Add(_windowError);
        _restartAdministrator.Click += (_, _) =>
        {
            _restartError = null;
            holdOpen?.Invoke(true);
            try
            {
                if (!App.Current.TryRestartAsAdministrator(out string? error))
                {
                    _restartError = error;
                    SetMessage(_windowError, error);
                }
            }
            finally { holdOpen?.Invoke(false); }
        };
        Children.Add(_restartAdministrator);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var refresh = new Button
        {
            Content = ActionIcons.Glyph("\uE72C", 16),
        };
        AutomationProperties.SetName(refresh, UiText.Get("Refresh windows"));
        ToolTipService.SetToolTip(refresh, UiText.Get("Refresh windows"));
        refresh.Click += (_, _) => RefreshWindows();
        _unpinAll.Click += (_, _) => { _restartError = null; _service.UnpinAll(); UpdateWindows(); };
        _restoreAll.Click += (_, _) => { _restartError = null; _service.RestoreAll(); UpdateWindows(); };
        string restoreHelp = UiText.Get("Removes Always on Top from every app window, including those pinned by other apps.");
        ToolTipService.SetToolTip(_restoreAll, restoreHelp);
        AutomationProperties.SetHelpText(_restoreAll, restoreHelp);
        buttons.Children.Add(refresh);
        buttons.Children.Add(_unpinAll);
        buttons.Children.Add(_restoreAll);
        Children.Add(buttons);

        Children.Add(Section("Appearance", topMargin: 6));
        var preferences = App.Current.Settings.AlwaysOnTop;
        _appearance = new AppearanceEditor(DispatcherQueue);
        _appearance.Show(preferences.Appearance);
        _appearance.Changed += (_, _) => SaveAppearance();
        Children.Add(_appearance);
        Children.Add(_appearanceError);
        // Rarely needed, so it stays folded below the global appearance.
        _perWindow.Children.Add(_perWindowEmpty);
        var perWindow = new Expander
        {
            Header = UiText.Get("Per-window appearance"), Content = _perWindow,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(perWindow, UiText.Get("Per-window appearance"));
        Children.Add(perWindow);
        Children.Add(new ShortcutEditor(AlwaysOnTopSettings.DefaultHotkey,
            "Pins or unpins the active window. Leave empty to disable.",
            () => App.Current.Settings.AlwaysOnTop.Hotkey, App.Current.TrySetAlwaysOnTopHotkey, App.Current.AlwaysOnTopHotkeyError));
        SetMessage(_appearanceError, null);
        AutomationProperties.SetLiveSetting(_windowError, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_appearanceError, AutomationLiveSetting.Polite);

        _service.Changed += OnChanged;
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        if (_disposed) return;
        _restartError = null;
        _service.Refresh();
        _refreshTimer.Stop();
        UpdateWindows();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        OnUi(DispatcherQueue, ScheduleRefresh);
    }

    private void ScheduleRefresh()
    {
        if (_disposed) return;
        // Coalesce native events. Existing controls are retained when titles or pin states change.
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTick(DispatcherQueueTimer sender, object args) => UpdateWindows();

    private void UpdateWindows()
    {
        if (_disposed) return;
        _refreshTimer.Stop();
        // Pinned windows first, then by title, as the service orders them.
        var available = _service.GetWindows();
        var present = available.Select(window => window.Handle).ToHashSet();
        foreach (var handle in _rows.Keys.Where(handle => !present.Contains(handle)).ToArray())
        {
            _windows.Children.Remove(_rows[handle].Button);
            _rows.Remove(handle);
        }

        for (int index = 0; index < available.Count; index++)
        {
            var window = available[index];
            if (!_rows.TryGetValue(window.Handle, out var row))
            {
                row = new WindowRow();
                nint handle = window.Handle;
                row.Button.Click += (_, _) => { _restartError = null; _service.Toggle(handle); UpdateWindows(); };
                _rows.Add(handle, row);
            }
            row.Title.Text = window.Title;
            row.Button.IsChecked = window.IsPinned && !window.PinnedByOtherApp;
            row.Pin.Opacity = window.IsPinned ? 1 : .35;
            row.Detail.Visibility = window.PinnedByOtherApp ? Visibility.Visible : Visibility.Collapsed;
            string operation = UiText.Format(window.IsPinned ? "Unpin: {0}" : "Pin: {0}", window.Title);
            AutomationProperties.SetName(row.Button, operation);
            ToolTipService.SetToolTip(row.Button, operation);
            Place(_windows, row.Button, index);
        }

        _count.Text = _service.OtherAppPinCount == 0 ? UiText.Format("{0} pinned", _service.PinnedCount)
            : UiText.Format("{0} pinned · {1} by other apps", _service.PinnedCount, _service.OtherAppPinCount);
        _unpinAll.IsEnabled = _service.PinnedCount > 0;
        _restoreAll.IsEnabled = _service.PinnedCount + _service.OtherAppPinCount > 0;
        _empty.Visibility = available.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_service.Error != AdministratorRestart.PermissionError) _restartError = null;
        SetMessage(_windowError, _restartError ?? _service.Error);
        _restartAdministrator.Visibility = _service.Error == AdministratorRestart.PermissionError
            && !AdministratorRestart.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        UpdateAppearanceRows();
    }

    /// <summary>Moves a retained control only when its position changed, keeping focus and any drag.</summary>
    private static void Place(Panel panel, Control control, int index)
    {
        int oldIndex = panel.Children.IndexOf(control);
        if (oldIndex == index) return;
        var focus = control.FocusState;
        if (oldIndex >= 0) panel.Children.RemoveAt(oldIndex);
        panel.Children.Insert(index, control);
        if (focus != FocusState.Unfocused) control.Focus(focus);
    }

    private void SaveAppearance()
    {
        if (_disposed) return;
        var settings = App.Current.Settings;
        var previous = settings.AlwaysOnTop;
        var value = _appearance.Value;
        settings.AlwaysOnTop = new AlwaysOnTopSettings
        {
            ShowBorder = value.ShowBorder, BorderThickness = value.BorderThickness,
            BorderColor = value.BorderColor is { } rgb ? AlwaysOnTopSettings.FormatColor(rgb) : null,
            Opacity = value.Opacity, Hotkey = previous.Hotkey,
        };
        if (!SettingsStore.Save(settings))
        {
            settings.AlwaysOnTop = previous;
            _appearance.Show(previous.Appearance);
            SetMessage(_appearanceError, "Could not save settings.");
            return;
        }
        SetMessage(_appearanceError, null);
        // An appearance error reaches the view through the service's Changed event.
        _service.ApplyAppearance();
    }

    /// <summary>One entry per pinned window, retained while the window stays pinned.</summary>
    private void UpdateAppearanceRows()
    {
        var pinned = _service.GetPinnedAppearances();
        var present = pinned.Select(window => window.Handle).ToHashSet();
        foreach (var handle in _appearanceRows.Keys.Where(handle => !present.Contains(handle)).ToArray())
        {
            _perWindow.Children.Remove(_appearanceRows[handle].Expander);
            _appearanceRows[handle].Editor.Detach();
            _appearanceRows.Remove(handle);
        }
        if (pinned.Count == 0)
        {
            if (!_perWindow.Children.Contains(_perWindowEmpty)) _perWindow.Children.Add(_perWindowEmpty);
            return;
        }
        _perWindow.Children.Remove(_perWindowEmpty);
        for (int index = 0; index < pinned.Count; index++)
        {
            var window = pinned[index];
            if (!_appearanceRows.TryGetValue(window.Handle, out var row))
            {
                row = new WindowAppearanceRow(DispatcherQueue);
                nint handle = window.Handle;
                row.UseGlobal.Toggled += (_, _) =>
                {
                    if (row.Updating) return;
                    if (!row.UseGlobal.IsOn) row.Editor.Show(App.Current.Settings.AlwaysOnTop.Appearance);
                    row.UpdateVisibility();
                    _service.SetWindowAppearance(handle, row.UseGlobal.IsOn ? null : row.Editor.Value);
                };
                row.Editor.Changed += (_, _) =>
                {
                    if (!_disposed && !row.UseGlobal.IsOn) _service.SetWindowAppearance(handle, row.Editor.Value);
                };
                _appearanceRows.Add(handle, row);
                row.Show(window.Appearance);
            }
            // A window unpinned and pinned again starts from the global appearance.
            else if (window.Appearance is null != row.UseGlobal.IsOn) row.Show(window.Appearance);
            row.SetTitle(window.Title);
            Place(_perWindow, row.Expander, index);
        }
    }
    public void Dispose()
    {
        // Closing the panel mid-drag still applies the last choice.
        if (!_disposed)
        {
            _appearance.Flush();
            foreach (var row in _appearanceRows.Values) row.Editor.Flush();
        }
        _disposed = true;
        _service.Changed -= OnChanged;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        _appearance.Detach();
        foreach (var row in _appearanceRows.Values) row.Editor.Detach();
    }

    /// <summary>A pinned window's folded entry: follow the global appearance, or use its own.</summary>
    private sealed class WindowAppearanceRow
    {
        private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis };

        public WindowAppearanceRow(DispatcherQueue dispatcher)
        {
            Editor = new AppearanceEditor(dispatcher);
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(UseGlobal);
            content.Children.Add(Editor);
            Expander = new Expander
            {
                Header = _title, Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(UseGlobal, UiText.Get("Use global appearance"));
        }

        public Expander Expander { get; }
        public AppearanceEditor Editor { get; }
        public ToggleSwitch UseGlobal { get; } = new()
        {
            Header = UiText.Get("Use global appearance"), OnContent = UiText.Get("On"), OffContent = UiText.Get("Off"),
        };
        public bool Updating { get; private set; }

        /// <summary>Shows the window's own appearance, or the global one it follows, without reporting it.</summary>
        public void Show(WindowAppearance? own)
        {
            Editor.Show(own ?? App.Current.Settings.AlwaysOnTop.Appearance);
            Updating = true;
            UseGlobal.IsOn = own is null;
            Updating = false;
            UpdateVisibility();
        }

        public void SetTitle(string title)
        {
            _title.Text = title;
            AutomationProperties.SetName(Expander, title);
        }

        public void UpdateVisibility() => Editor.Visibility = UseGlobal.IsOn ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class WindowRow
    {
        public ToggleButton Button { get; } = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40, Padding = new Thickness(10, 6, 10, 6),
        };
        public TextBlock Title { get; } = new()
        {
            FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        public TextBlock Detail { get; } = new()
        {
            Text = UiText.Get("Pinned by another app"), FontSize = 11, Opacity = .75,
            TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed,
        };
        public FontIcon Pin { get; } = new()
        {
            Glyph = "\uE718", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };

        public WindowRow()
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(Pin, 1);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Title);
            text.Children.Add(Detail);
            content.Children.Add(text);
            content.Children.Add(Pin);
            Button.Content = content;
        }
    }
}
