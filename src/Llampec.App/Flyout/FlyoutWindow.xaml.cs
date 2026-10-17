using System.ComponentModel;
using Llampec.Actions;
using Llampec.Interop;
using Llampec.Settings;
using Llampec.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using System.Diagnostics;
using Llampec.Diagnostics;
using Microsoft.UI.Dispatching;

using Windows.UI.ViewManagement;

namespace Llampec.Flyout;

/// <summary>A lightweight window shell. Tile controls and backdrop are rebuilt after idle release.</summary>
public sealed partial class FlyoutWindow : Window, IDisposable
{
    private readonly FlyoutViewModel _model;
    private readonly List<Action> _unsubscribe = [];
    private readonly List<Action> _subUnsubscribe = [];
    /// <summary>Open dialogs and operations that must keep the panel open when it loses focus.</summary>
    private int _holds;
    private bool _disposed;
    private readonly nint _hwnd;
    private IDisposable? _subpageView;
    private int _observedPinCount;
    private bool _observedFullscreenActive;
    private bool _closing;
    private readonly PanelMotion _motion;
    private readonly PanelAcrylicBackdrop _panelBackdrop;
    private readonly DispatcherQueueTimer _idleTimer;
    private TaskCompletionSource? _panelHidden;
    private int _showVersion;
    private int _idleStage;
    private bool _transitioning;
    private bool _placementPending;
    private bool _repositioning;
    private bool _sizeRepositionQueued;
    private int _builtTileColumns;
    private bool _showMenuAfterOpen;
    private float _slideDistance;
    private User32.POINT _anchor;
    private nint _anchorMonitor;
    private bool _hasAnchor;
    private readonly UISettings _uiSettings = new();

    public FlyoutWindow(FlyoutViewModel model)
    {
        InitializeComponent();
        _model = model;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _motion = new PanelMotion(Surface, DispatcherQueue);
        _idleTimer = DispatcherQueue.CreateTimer();
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += OnIdle;
        AppWindow.IsShownInSwitchers = false;
        var presenter = OverlappedPresenter.Create();
        // The visible frame and material belong to Surface so they can move as
        // one compositor visual. The HWND only provides a stationary viewport.
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        Dwm.SetInt(_hwnd, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, Dwm.DWMWCP_DONOTROUND);
        SystemBackdrop = new TransparentPanelBackdrop(_hwnd);
        _panelBackdrop = new PanelAcrylicBackdrop(Stage);
        PanelMaterial.SystemBackdrop = _panelBackdrop;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Llampec.ico"));
        Root.ActualThemeChanged += OnActualThemeChanged;
        ApplyTheme();
        UpdateLanguage();
        Root.Loaded += (_, _) => Reposition();
        Tiles.SizeChanged += OnPageSizeChanged;
        Subpage.SizeChanged += OnPageSizeChanged;
        model.RunWithPanelHiddenRequested += RunWithPanelHiddenAsync;
        model.PropertyChanged += OnModelChanged;
        Activated += (_, e) =>
        {
            // A mouse-down on our tray icon deactivates us BEFORE NIN_SELECT arrives.
            // Let that callback toggle once, rather than hiding here and reopening there.
            // A display reconfiguration also deactivates the panel; keep it while rotation runs,
            // whether started from the tile or from its page.
            if (e.WindowActivationState == WindowActivationState.Deactivated && !IsHeld
                && App.Current.Rotation?.IsBusy != true)
            {
                bool trayPress = App.Current.IsPointerPressOnTrayIcon();
                Log.Info($"Panel deactivated: trayPress={trayPress}");
                if (!trayPress) HidePanel();
            }
        };
        AppWindow.Closing += (_, e) => { e.Cancel = true; HidePanel(); };
    }

    private bool IsHeld => _holds > 0 || _savingEditor || _editorDragging;

    /// <summary>Paired calls from a dialog or long operation; holds from different owners add up.</summary>
    private void Hold(bool hold) => _holds = Math.Max(0, _holds + (hold ? 1 : -1));

    public void ApplyTheme()
    {
        Stage.RequestedTheme = (App.Current.PreviewTheme ?? App.Current.Settings.Theme) switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateFrameTheme();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateFrameTheme();
        if (!AppWindow.IsVisible) ScheduleIdleRelease();
    }

    private void UpdateFrameTheme()
    {
        // XAML theme does not automatically update the non-client DWM outline.
        // WinUI draws the same theme-aware stroke as its native flyouts. Suppress
        // the extra DWM outline, which otherwise leaves a bright edge in dark mode.
        Dwm.SetInt(_hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, Root.ActualTheme == ElementTheme.Dark ? 1 : 0);
        Dwm.SetUInt(_hwnd, Dwm.DWMWA_BORDER_COLOR, Dwm.DWMWA_COLOR_NONE);
    }

    private static FontIcon Icon(string glyph, double size = 20) => ActionIcons.Glyph(glyph, size);

    private void BuildTiles(int? columns = null)
    {
        _builtTileColumns = columns ?? EffectiveTileColumns();
        Tiles.ColumnDefinitions.Add(new ColumnDefinition());
        var groups = _model.GetTileGroups(App.Current.Settings);
        bool hasCategories = groups.Any(group => group.CategoryId is not null);
        int row = 0;
        foreach (var group in groups)
        {
            if (group.CategoryId is not null || hasCategories)
            {
                var heading = new TextBlock
                {
                    Text = group.CategoryId is null ? T("No category") : group.Name, FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                };
                Tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(heading, row++);
                Tiles.Children.Add(heading);
            }
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            for (int column = 0; column < _builtTileColumns; column++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            int rows = (group.Tiles.Count + _builtTileColumns - 1) / _builtTileColumns;
            for (int tileRow = 0; tileRow < rows; tileRow++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int index = 0; index < group.Tiles.Count; index++)
            {
                var control = BuildTile(group.Tiles[index]);
                Grid.SetColumn(control, index % _builtTileColumns);
                Grid.SetRow(control, index / _builtTileColumns);
                grid.Children.Add(control);
            }
            Tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(grid, row++);
            Tiles.Children.Add(grid);
        }
    }

    private FrameworkElement BuildTile(TileViewModel tile)
    {
        var stack = new StackPanel { Spacing = 6 };
        var face = new Grid();
        face.ColumnDefinitions.Add(new ColumnDefinition());
        if (tile.HasSubpage) face.ColumnDefinitions.Add(new ColumnDefinition());
        var glyph = ActionIcons.Tile(tile.Id, tile.Glyph, tile.GlyphBadge);
        ButtonBase button = tile.IsButton ? new Button() : new ToggleButton();
        button.Content = glyph;
        button.Height = 48;
        button.Padding = new Thickness(0);
        button.CornerRadius = tile.HasSubpage ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(6);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(button, tile.Title);
        button.Click += (_, _) => tile.Toggle();
        face.Children.Add(button);
        Button? more = null;
        if (tile.HasSubpage)
        {
            more = new Button
            {
                Content = Icon("\uE76C", 16), Padding = new Thickness(0), Height = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(0, 6, 6, 0), BorderThickness = new Thickness(0, 1, 1, 1),
            };
            AutomationProperties.SetName(more, UiText.Format("{0} options", tile.Title));
            more.Click += (_, _) =>
            {
                _model.OpenSubpage(tile);
            };
            Grid.SetColumn(more, 1);
            face.Children.Add(more);
        }
        // One line each, as in Windows Quick Settings; a line that does not fit scrolls while the
        // tile is hovered or focused. The tooltip and accessible help carry the full text.
        var title = new MarqueeText(_uiSettings);
        title.SetText(tile.Title);
        ToolTipService.SetToolTip(button, tile.Title);
        var subtitle = new MarqueeText(_uiSettings, (Style)Root.Resources["TileStatusStyle"]);
        var caption = new StackPanel();
        caption.Children.Add(title);
        caption.Children.Add(subtitle);
        void Scroll(bool on) { if (on) { title.Start(); subtitle.Start(); } else { title.Stop(); subtitle.Stop(); } }
        // The buttons mark pointer events handled for their own visual states.
        stack.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => Scroll(true)), handledEventsToo: true);
        stack.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => Scroll(false)), handledEventsToo: true);
        stack.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => Scroll(false)), handledEventsToo: true);
        button.GotFocus += (_, _) => { if (button.FocusState == FocusState.Keyboard) Scroll(true); };
        button.LostFocus += (_, _) => Scroll(false);
        // A count badge rather than a third caption line keeps the row height of other tiles.
        var pinnedCount = new TextBlock
        {
            FontSize = 10, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Root.Resources["TileBadgeTextStyle"],
        };
        var pinnedBadge = new Border
        {
            Child = pinnedCount, MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(4, 0, 4, 0),
            Style = (Style)Root.Resources["TileBadgeStyle"],
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        if (tile.Id == "always-on-top") face.Children.Add(pinnedBadge);
        var busy = new ProgressBar
        {
            Height = 2, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 6, 2), IsHitTestVisible = false,
        };
        Grid.SetColumnSpan(busy, 2);
        face.Children.Add(busy);
        stack.Children.Add(face);
        stack.Children.Add(caption);
        void Update()
        {
            // Each property is computed; read them once per update.
            string? text = tile.Subtitle;
            bool busyNow = tile.IsBusy;
            button.IsEnabled = tile.IsAvailable && !busyNow;
            if (button is ToggleButton toggle) toggle.IsChecked = tile.IsOn || tile.IsMixed;
            if (more is not null)
                more.Style = tile.IsOn || tile.IsMixed ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
            subtitle.SetText(string.IsNullOrEmpty(text) ? null : tile.CompactSubtitle);
            ToolTipService.SetToolTip(subtitle, text);
            ToolTipService.SetToolTip(button, string.IsNullOrEmpty(text) ? tile.Title : $"{tile.Title}\n{text}");
            busy.Visibility = busyNow ? Visibility.Visible : Visibility.Collapsed;
            busy.IsIndeterminate = busyNow;
            AutomationProperties.SetHelpText(button, tile.IsMixed ? T("Some displays are on") : text ?? "");
            if (tile.Id == "always-on-top")
            {
                int count = App.Current.AlwaysOnTop?.PinnedCount ?? 0;
                string pinned = UiText.Format("{0} pinned", count);
                pinnedCount.Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
                pinnedBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ToolTipService.SetToolTip(button, string.IsNullOrEmpty(text) ? $"{tile.Title}\n{pinned}" : $"{tile.Title}\n{text}\n{pinned}");
                AutomationProperties.SetHelpText(button, $"{text}. {pinned}");
            }
        }
        Observe(tile, Update, _unsubscribe);
        return stack;
    }

    private int EffectiveTileColumns()
    {
        if (!_hasAnchor) return App.Current.Settings.TileColumns;
        var (work, scale) = PlacementArea();
        return EffectiveTileColumns(work, scale);
    }

    private int EffectiveTileColumns(User32.RECT work, double scale)
    {
        double widthDip = work.Width / scale - 24;
        int available = Math.Max(1, (int)Math.Floor(widthDip / 120));
        int requested = _utilityPage == UtilityPage.Editor && _layoutDraft is not null
            ? _layoutDraft.Columns : App.Current.Settings.TileColumns;
        return Math.Min(requested, available);
    }

    private static void Observe(TileViewModel tile, Action update, List<Action> subscriptions)
    {
        PropertyChangedEventHandler handler = (_, _) => update();
        tile.PropertyChanged += handler;
        subscriptions.Add(() => tile.PropertyChanged -= handler);
        update();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyoutViewModel.Subpage)) ApplyPage();
    }

    /// <summary>Shows the main grid or the open tile's page, building that page's controls.</summary>
    private void ApplyPage()
    {
        _utilityPage = UtilityPage.None;
        ReleaseSubpage();
        bool open = _model.Subpage is not null;
        PageHeader.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Tiles.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        Subpage.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        EditButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        Heading.Text = _model.Subpage?.Title ?? "Llampec";
        if (_model.Subpage is { } page)
        {
            if (CreateSubpageView(page) is { } view)
            {
                _subpageView = (IDisposable)view;
                Subpage.Children.Add(view);
            }            if (page.Id == "touch-keyboard")
                Subpage.Children.Add(new TextBlock { Text = T("Taskbar icon"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var tile in page.SubTiles)
            {
                bool updating = false;
                if (tile.IsRadioItem)
                {
                    var radio = new RadioButton { Content = tile.Title, GroupName = page.Id, MinHeight = 44 };
                    radio.Click += (_, _) => { if (!updating) tile.Toggle(); };
                    Subpage.Children.Add(radio);
                    Observe(tile, () =>
                    {
                        updating = true;
                        radio.IsChecked = tile.IsOn;
                        radio.IsEnabled = tile.IsAvailable && !tile.IsBusy;
                        updating = false;
                    }, _subUnsubscribe);
                }
                else
                {
                    var toggle = new ToggleSwitch { Header = tile.Title, OnContent = T("On"), OffContent = T("Off"), HorizontalAlignment = HorizontalAlignment.Stretch };
                    toggle.Toggled += (_, _) => { if (!updating) tile.Toggle(); };
                    Subpage.Children.Add(toggle);
                    Observe(tile, () =>
                    {
                        updating = true;
                        toggle.IsOn = tile.IsOn;
                        toggle.IsEnabled = tile.IsAvailable && !tile.IsBusy;
                        updating = false;
                    }, _subUnsubscribe);
                }
            }
            BackButton.Focus(FocusState.Programmatic);
        }
        if (AppWindow.IsVisible) Reposition();
    }

    /// <summary>The page's own controls, or null when the generic switches suffice. Each is IDisposable.</summary>
    private FrameworkElement? CreateSubpageView(TileViewModel page)
    {
        if (page.Action is Llampec.Actions.Caffeine.CaffeineAction caffeine) return new CaffeineView(caffeine);
        if (page.Action is Llampec.Actions.Power.PowerAction power) return new PowerView(power);
        if (page.Action is Llampec.Actions.Monitor.MonitorAction monitor) return new MonitorView(monitor.Monitors);
        if (page.Action is Llampec.Actions.DisplayOff.DisplayOffAction displayOff)
            return new DisplayOffView(displayOff, App.Current.ExternalMonitors, page.Toggle);
        if (page.Action is Llampec.Actions.AgentMode.AgentModeAction agentMode) return new AgentModeView(agentMode, page.Toggle);
        return page.Id switch
        {
            "theme" => new ThemeScheduleView(Hold),
            "always-on-top" when App.Current.AlwaysOnTop is { } pins => new AlwaysOnTopView(pins, Hold),
            "fullscreen" when App.Current.Fullscreen is { } fullscreen => new FullscreenView(fullscreen),
            "rotation" when App.Current.Rotation is { } rotation => new RotationView(rotation, Hold),
            "mouse" => new LogitechMouseView(App.Current.LogitechMouse),
            _ => null,
        };
    }

    private void Reposition() => Reposition(allowHidden: false);

    private void Reposition(bool allowHidden)
    {
        // A transition owns its fixed viewport until completion.
        if (_disposed || _repositioning || !_hasAnchor || (!allowHidden && !AppWindow.IsVisible)) return;
        if (_transitioning) { _placementPending = true; return; }
        // A drag preview reflows groups; resizing the bottom-anchored panel would
        // move the content under the pointer and make the destination oscillate.
        if (_panelEditor?.DragInProgress == true) return;
        _repositioning = true;
        try
        {
            _placementPending = false;
            // Keep the opening monitor even if the pointer moves during layout/animation.
            var (work, scale) = PlacementArea();
            int margin = (int)Math.Round(12 * scale);
            bool mainPage = _utilityPage == UtilityPage.None && !_model.IsSubpageOpen;
            bool editing = _utilityPage == UtilityPage.Editor && _layoutDraft is not null;
            int columns = EffectiveTileColumns(work, scale);
            if (editing) _panelEditor?.SetColumns(columns);
            if (mainPage && Tiles.Children.Count > 0 && _builtTileColumns != columns) RebuildTiles(columns);
            int desiredWidthDip = mainPage || editing ? Math.Max(180, columns * 120) : 360;
            int visibleWidth = Math.Min((int)Math.Round(desiredWidthDip * scale), Math.Max(1, work.Width - 2 * margin));
            int width = visibleWidth + 2 * margin;
            // Size from the actual wrapped captions/subtitles, rather than a fixed row estimate.
            // Keep the footer visible; only scroll when the monitor's work area requires it.
            Root.Measure(new Windows.Foundation.Size(visibleWidth / scale, double.PositiveInfinity));
            double heightDip = Root.DesiredSize.Height;
            int height = Math.Min((int)Math.Ceiling(heightDip * scale) + 2 * margin, work.Height);
            // Measure the viewport at the height we will actually show. A final
            // infinite-height measure leaves long pages without a bounded scroll
            // viewport when the monitor caps the native window's height.
            double shownHeightDip = Math.Max(1, (height - 2 * margin) / scale);
            Root.Measure(new Windows.Foundation.Size(visibleWidth / scale, shownHeightDip));
            var bounds = new RectInt32(work.Right - width, work.Bottom - height, width, height);
            _slideDistance = (float)(height / scale);
            if (AppWindow.Size.Width != width || AppWindow.Size.Height != height
                || AppWindow.Position.X != bounds.X || AppWindow.Position.Y != bounds.Y)
                AppWindow.MoveAndResize(bounds);
        }
        finally { _repositioning = false; }
    }

    private void CapturePlacement()
    {
        User32.GetCursorPos(out _anchor);
        _hasAnchor = true;
        _anchorMonitor = User32.MonitorFromPoint(_anchor, User32.MONITOR_DEFAULTTONEAREST);
        var (work, scale) = PlacementArea();
        // Adopt the target DPI before measuring. The entire host remains within
        // this work area throughout animation, even with vertically stacked screens.
        if (User32.GetDpiForWindow(_hwnd) != (uint)Math.Round(96 * scale))
            AppWindow.Move(new PointInt32(work.Left, work.Top));
        Log.Info($"Panel placement: dpi={User32.GetDpiForWindow(_hwnd)}; scale={scale}; transparent host");
    }

    private (User32.RECT WorkArea, double Scale) PlacementArea()
    {
        // Rotation can move the original cursor point onto a different display.
        // Keep the opening monitor while it exists, then fall back to the nearest.
        var info = new User32.MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<User32.MONITORINFOEXW>() };
        if (_anchorMonitor == 0 || !User32.GetMonitorInfo(_anchorMonitor, ref info))
        {
            _anchorMonitor = User32.MonitorFromPoint(_anchor, User32.MONITOR_DEFAULTTONEAREST);
            if (!User32.GetMonitorInfo(_anchorMonitor, ref info)) return User32.GetMonitorWorkAreaAt(_anchor);
        }
        _anchor.X = Math.Clamp(_anchor.X, info.rcMonitor.Left, Math.Max(info.rcMonitor.Left, info.rcMonitor.Right - 1));
        _anchor.Y = Math.Clamp(_anchor.Y, info.rcMonitor.Top, Math.Max(info.rcMonitor.Top, info.rcMonitor.Bottom - 1));
        double scale = User32.GetDpiForMonitor(_anchorMonitor, 0, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        var work = Llampec.Platform.PanelPlacement.ReserveTaskbarArea(
            _anchorMonitor, info.rcMonitor, info.rcWork, scale);
        return (work, scale);
    }

    public void OnDisplayEnvironmentChanged()
    {
        if (!_disposed && AppWindow.IsVisible) Reposition();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed || !AppWindow.IsVisible || _sizeRepositionQueued) return;
        _sizeRepositionQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _sizeRepositionQueued = false;
            if (!_disposed) Reposition();
        })) _sizeRepositionQueued = false;
    }

    public void Toggle()
    {
        if (_closing) { ShowPanel(); return; }
        if (AppWindow.IsVisible)
        {
            HidePanel();
        }
        else ShowPanel();
    }

    public void ShowPanel()
    {
        if (_disposed) return;
        ++_showVersion;
        // Repeated show requests must not snap an entry animation to its endpoint.
        if (AppWindow.IsVisible && !_closing)
        {
            Activate();
            User32.SetForegroundWindow(_hwnd);
            return;
        }
        bool fresh = !AppWindow.IsVisible;
        _panelHidden?.TrySetCanceled();
        _panelHidden = null;
        long started = Stopwatch.GetTimestamp();
        App.Current.AlwaysOnTop?.CaptureTarget();
        App.Current.Fullscreen?.CaptureTarget();
        _idleTimer.Stop();
        _closing = false;
        if (fresh)
        {
            _motion.Cancel();
            _transitioning = false;
            CapturePlacement();
            ApplyTheme();
            if (Tiles.Children.Count == 0 && _layoutDraft is null) BuildTiles();
            _panelBackdrop.Resume();
            App.Current.SetPanelVisible(true);
            if (_layoutDraft is not null) ShowPanelEditor(); else ShowMainPage();
            try { _model.RefreshAll(); }
            catch (Exception ex)
            {
                ErrorBar.Message = ex.Message;
                ErrorBar.IsOpen = true;
            }
            App.Current.Scheduler?.SetStatusVisible(_layoutDraft is null);
            Reposition(allowHidden: true);
        }
        bool useMotion = _uiSettings.AnimationsEnabled;
        _transitioning = true;
        _motion.PrepareOpening(_slideDistance, fresh);
        AppWindow.Show();
        Activate();
        User32.SetForegroundWindow(_hwnd);
        Log.Info($"Panel prepared: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms; animations={useMotion}; host={AppWindow.Position.X},{AppWindow.Position.Y},{AppWindow.Size.Width},{AppWindow.Size.Height}");
        _motion.Start(opening: true, useMotion, fresh, _slideDistance, () =>
        {
            _transitioning = false;
            if (_placementPending) Reposition();
            if (_showMenuAfterOpen)
            {
                _showMenuAfterOpen = false;
                ShowMenu();
            }
        });
    }

    public void HidePanel()
    {
        if (_disposed || _savingEditor || !AppWindow.IsVisible || _closing) return;
        _showMenuAfterOpen = false;
        _closing = true;
        _transitioning = true;
        _motion.Start(opening: false, _uiSettings.AnimationsEnabled, fresh: false, _slideDistance, FinishHide);
    }

    private void FinishHide()
    {
        AppWindow.Hide();
        App.Current.AlwaysOnTop?.ReleaseTarget();
        App.Current.Fullscreen?.ReleaseTarget();
        App.Current.Scheduler?.SetStatusVisible(false);
        App.Current.SetPanelVisible(false);
        _closing = false;
        _transitioning = false;
        ShowMainPage();
        ScheduleIdleRelease();
        _panelHidden?.TrySetResult();
        _panelHidden = null;
    }

    public void ScheduleIdleRelease()
    {
        if (_disposed || AppWindow.IsVisible) return;
        _idleStage = 0;
        _idleTimer.Stop();
        _idleTimer.Interval = TimeSpan.FromSeconds(2);
        _idleTimer.Start();
    }

    private void OnIdle(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || AppWindow.IsVisible || _model.Tiles.Any(t => t.IsBusy)) return;
        if (_idleStage++ == 0)
        {
            foreach (var unsubscribe in _unsubscribe) unsubscribe();
            _unsubscribe.Clear();
            Tiles.Children.Clear(); Tiles.ColumnDefinitions.Clear(); Tiles.RowDefinitions.Clear();
            // Keep the element's external backdrop link attached to its host.
            // Releasing it during hidden-window idle can destroy WinUI input-site
            // resources from finalization; the native surface lives until Dispose.
            _panelBackdrop.Suspend();
            ErrorBar.IsOpen = false;
            // Once per idle transition, never periodically. Collect the discarded control
            // tree, then give WinUI/finalizers time to release native references before trimming.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
            _idleTimer.Interval = TimeSpan.FromMilliseconds(500);
            _idleTimer.Start();
            return;
        }
        Log.Info("Idle: tile controls/acrylic controller released; native surface retained; returning resident pages to Windows.");
        if (!Kernel32.EmptyWorkingSet(-1)) Log.Warn("Could not trim the idle working set.");
    }

    private async Task RunWithPanelHiddenAsync(Func<Task> action)
    {
        if (_disposed) return;
        int version = _showVersion;
        if (AppWindow.IsVisible)
        {
            var completion = _panelHidden ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            HidePanel();
            await completion.Task;
        }
        // Check and invoke on the UI thread without another continuation between
        // them: reopening the panel cancels a capture even after the hide completed.
        if (!_disposed && !AppWindow.IsVisible && version == _showVersion) await action();
    }
    private void OnStagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface).Position;
        if (point.X < 0 || point.Y < 0 || point.X > Surface.ActualWidth || point.Y > Surface.ActualHeight)
            HidePanel();
    }
    private void OnBack(object sender, RoutedEventArgs e) => GoBack();
    private void OnMenu(object sender, RoutedEventArgs e) => ShowSettings();
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (_utilityPage == UtilityPage.Editor)
        {
            if (_panelEditor?.DragInProgress == true)
            { _panelEditor.CancelDrag(); e.Handled = true; return; }
            if (IsHeld) return;
            HidePanel(); e.Handled = true; return;
        }
        if (IsHeld) return;
        if (_utilityPage != UtilityPage.None || _model.IsSubpageOpen) GoBack(); else HidePanel();
        e.Handled = true;
    }

    public void ShowTrayMenu()
    {
        ShowPanel();
        // Anchor the menu after the panel surface reaches its final position.
        if (_transitioning) _showMenuAfterOpen = true;
        else DispatcherQueue.TryEnqueue(ShowMenu);
    }

    public void OpenAlwaysOnTop()
    {
        if (_disposed) return;
        ShowPanel();
        if (_model.Tiles.FirstOrDefault(tile => tile.Id == "always-on-top") is { } tile)
            _model.OpenSubpage(tile);
    }

    public void OnAlwaysOnTopChanged()
    {
        int count = App.Current.AlwaysOnTop?.PinnedCount ?? 0;
        bool added = count > _observedPinCount;
        _observedPinCount = count;
        if (added && !_disposed) KeepAboveNewTopmost();
    }

    /// <summary>
    /// HWND_TOPMOST raises a newly pinned or fullscreen target even with NOACTIVATE. Keep the
    /// active panel usable if that target covers it; never raise a background panel.
    /// </summary>
    private void KeepAboveNewTopmost()
    {
        if (!_closing && AppWindow.IsVisible && User32.GetForegroundWindow() == _hwnd)
            User32.SetWindowPos(_hwnd, -1 /* HWND_TOPMOST */, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | 0x0200 /* NOOWNERZORDER */);
    }

    public void OpenFullscreen()
    {
        if (_disposed) return;
        ShowPanel();
        if (_model.Tiles.FirstOrDefault(tile => tile.Id == "fullscreen") is { } tile)
            _model.OpenSubpage(tile);
    }

    public void OnFullscreenChanged()
    {
        if (_disposed) return;
        bool active = App.Current.Fullscreen?.IsActive == true;
        bool entered = active && !_observedFullscreenActive;
        _observedFullscreenActive = active;
        if (entered) KeepAboveNewTopmost();
        if (!_closing && AppWindow.IsVisible) Reposition();
    }

    private void ShowMenu()
    {
        if (_disposed || !AppWindow.IsVisible || _closing) return;
        if (_transitioning) { _showMenuAfterOpen = true; return; }
        var menu = new MenuFlyout();
        var settings = new MenuFlyoutItem { Text = T("Settings") };
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);
        var about = new MenuFlyoutItem { Text = T("About Llampec") };
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);
        menu.Items.Add(new MenuFlyoutSeparator());
        var exit = new MenuFlyoutItem { Text = T("Exit") };
        exit.Click += (_, _) => App.Current.ExitApplication();
        menu.Items.Add(exit);
        menu.ShowAt(MenuButton);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleasePanelEditor();
        _layoutDraft = null;
        _panelHidden?.TrySetCanceled();
        _panelHidden = null;
        AppWindow.Hide();
        _motion.Dispose();
        _idleTimer.Stop();
        _idleTimer.Tick -= OnIdle;
        ReleaseSubpage();
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _model.PropertyChanged -= OnModelChanged;
        _model.RunWithPanelHiddenRequested -= RunWithPanelHiddenAsync;
        Root.ActualThemeChanged -= OnActualThemeChanged;
        Tiles.SizeChanged -= OnPageSizeChanged;
        Subpage.SizeChanged -= OnPageSizeChanged;
        _panelBackdrop.Dispose();
    }
}
