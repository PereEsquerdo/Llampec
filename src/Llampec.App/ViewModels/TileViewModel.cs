using Microsoft.UI.Dispatching;
using Llampec.Actions;
using Llampec.Settings;

namespace Llampec.ViewModels;

/// <summary>Presents one <see cref="IQuickAction"/> as a tile. Marshals action events to the UI thread.</summary>
public sealed class TileViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<Func<Task>, Task>? _runWithPanelHidden;
    private bool _executing;
    private bool _refreshing;
    private bool _disposed;

    public TileViewModel(IQuickAction action, DispatcherQueue dispatcher, Func<Func<Task>, Task>? runWithPanelHidden, bool isRadioItem = false)
    {
        Action = action;
        _dispatcher = dispatcher;
        _runWithPanelHidden = runWithPanelHidden;
        IsRadioItem = isRadioItem;

        SubTiles = action.SubActions.Select(a => new TileViewModel(a, dispatcher, null, action.SubActionsAreExclusive)).ToList();
        // IsBusy includes the sub-tiles. A child can finish after the parent's last change
        // notification, so re-read the parent whenever a child changes.
        foreach (var sub in SubTiles) sub.PropertyChanged += OnSubTileChanged;
        action.Changed += OnActionChanged;
    }

    public IQuickAction Action { get; }

    /// <summary>True when this tile is one of a parent's <see cref="IQuickAction.SubActionsAreExclusive"/>
    /// sub-actions -- the sub-page renders it as a radio-button row instead of a toggle switch.</summary>
    public bool IsRadioItem { get; }

    public string Id => Action.Id;
    public string Title => UiText.Get(Action.Title);
    public string? Subtitle
    {
        get
        {
            if (Id == "theme") return ThemeScheduleStatus.Format(!IsOn, App.Current.Scheduler?.Plan, DateTimeOffset.Now, UiText.Language);
            if (Action.Subtitle is not { } text) return null;
            string[] parts = text.Split(" of ", StringSplitOptions.None);
            return parts.Length == 2 && int.TryParse(parts[0], out int on) && int.TryParse(parts[1], out int total)
                ? UiText.Format("{0} of {1}", on, total) : UiText.Get(text);
        }
    }
    /// <summary>Fits the narrow grid; full target/time remains in Subtitle for tooltips and accessibility.</summary>
    public string? CompactSubtitle => Id == "theme"
        ? ThemeScheduleStatus.Format(!IsOn, App.Current.Scheduler?.Plan, DateTimeOffset.Now, UiText.Language, compact: true)
        : Subtitle;
    public string Glyph => Action.Glyph;
    public string? GlyphBadge => Action.GlyphBadge;
    public bool IsAvailable => Action.IsAvailable;
    public bool IsBusy => _executing || Action.IsBusy || SubTiles.Any(tile => tile.IsBusy);
    public bool IsOn => Action.State == ActionState.On;
    public bool IsMixed => Action.State == ActionState.Mixed;
    public bool IsButton => Action.Kind is ActionKind.Button or ActionKind.ButtonWithSubpage;
    public bool HasSubpage => Action.Kind is ActionKind.ToggleWithSubpage or ActionKind.ButtonWithSubpage;

    public IReadOnlyList<TileViewModel> SubTiles { get; }

    /// <summary>Runs the action (a button press, switch or radio choice) unless it is busy or unavailable.</summary>
    public void Toggle()
    {
        if (_disposed || IsBusy || !IsAvailable) return;
        _ = ExecuteAndRefreshThemeAsync();
    }

    private async Task ExecuteAndRefreshThemeAsync()
    {
        _executing = true;
        RaiseAll();
        try
        {
            // A screenshot must see the desktop after the closing animation and
            // HWND hide, rather than capture the panel while it is still moving.
            if (IsButton && _runWithPanelHidden is not null)
                await _runWithPanelHidden(() => Action.ExecuteAsync(CancellationToken.None));
            else await Action.ExecuteAsync(CancellationToken.None);
            App.Current.RefreshTheme();
        }
        catch (OperationCanceledException) { } // Closing was reversed, or the app exited.
        catch (Exception error) { Llampec.Diagnostics.Log.Warn($"Action '{Id}' could not complete: {error.Message}"); }
        finally
        {
            _executing = false;
            RaiseAll();
        }
    }

    public void Refresh()
    {
        if (_disposed) return;
        // Each changed property would otherwise notify the tile; notify once at the end.
        _refreshing = true;
        try
        {
            Action.Refresh();
            foreach (var sub in SubTiles) sub.Refresh();
        }
        finally { _refreshing = false; }
        // Computed text (for example a scheduled countdown) can change independently
        // of the action's state and therefore without an Action.Changed notification.
        RaiseAll();
    }

    private void OnActionChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.HasThreadAccess)
        {
            RaiseAll();
        }
        else
        {
            _dispatcher.TryEnqueue(RaiseAll);
        }
    }

    private void OnSubTileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseAll();

    private void RaiseAll()
    {
        if (_disposed || _refreshing) return;
        // Cheap and simple: the tile re-reads every bound property.
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Action.Changed -= OnActionChanged;
        foreach (var child in SubTiles)
        {
            child.PropertyChanged -= OnSubTileChanged;
            child.Dispose();
        }
    }
}
