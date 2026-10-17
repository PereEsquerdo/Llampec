using Llampec.Diagnostics;
using Llampec.Settings;

namespace Llampec.Actions.AlwaysOnTop;

/// <param name="PinnedByOtherApp">Kept on top by another application: shown so it can be undone, never claimed.</param>
public sealed record PinnableWindow(nint Handle, string Title, bool IsPinned, bool PinnedByOtherApp = false);

/// <summary>
/// Session-owned pins, independent of disposable panel controls. Construct and use on the UI thread:
/// out-of-context WinEvents are delivered to the thread that registered the hooks.
/// </summary>
public sealed class AlwaysOnTopService : IDisposable
{
    private sealed class Pin(WindowIdentity identity, string title)
    {
        public WindowIdentity Identity { get; } = identity;
        public string Title { get; set; } = title;
        public IAlwaysOnTopBorder? Border { get; set; }
        /// <summary>This window's own appearance; null follows the global settings.</summary>
        public WindowAppearance? Appearance { get; set; }
        /// <summary>The original layered state while Llampec has changed the window's opacity.</summary>
        public OpacityRestore? Opacity;
    }

    // Appearance errors clear themselves on the next successful appearance change.
    internal const string BorderError = "Could not show the window border.";
    internal const string TransparencyError = "This window does not support transparency.";

    private readonly Func<AlwaysOnTopSettings> _preferences;
    private readonly Func<nint, IAlwaysOnTopBorder> _borderFactory;
    private readonly IAlwaysOnTopWindowSystem _windows;
    private readonly SynchronizationContext? _context;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<nint, Pin> _pins = [];
    private Dictionary<nint, WindowIdentity> _selectorIdentities = [];
    private Dictionary<nint, WindowIdentity> _availableIdentities = [];
    private IReadOnlyList<PinnableWindow> _availableWindows = [];
    private WindowIdentity? _lastExternal;
    private WindowIdentity? _target;
    private bool _targetFrozen, _mutating, _refreshing, _refreshQueued, _disposed, _notifying, _notifyAgain;
    private (WindowIdentity? Target, string Title, bool Pinned, string? Error)? _publishedState;
    private IReadOnlyList<PinnableWindow> _publishedWindows = [];

    public event EventHandler? Changed;
    public event EventHandler? SelectionRequested;
    public nint TargetWindow => _target?.Handle ?? 0;
    public int PinnedCount => _pins.Count;
    public string? Error { get; private set; }
    public bool HasTarget => _target is not null;
    public bool IsTargetPinned => _target is { } target && _pins.TryGetValue(target.Handle, out var pin) && pin.Identity == target;
    /// <summary>The target is kept on top by another application; toggling it removes that.</summary>
    public bool IsTargetPinnedByOtherApp { get; private set; }
    /// <summary>Application windows kept on top by other applications, as of the last refresh.</summary>
    public int OtherAppPinCount { get; private set; }
    public string TargetTitle { get; private set; } = string.Empty;

    public AlwaysOnTopService(Func<AlwaysOnTopSettings> preferences, Func<nint, IAlwaysOnTopBorder>? borderFactory = null)
        : this(preferences, new NativeAlwaysOnTopWindowSystem(), borderFactory ?? (hwnd => new NativeWindowBorder(hwnd)), SynchronizationContext.Current)
    {
    }

    internal AlwaysOnTopService(Func<AlwaysOnTopSettings> preferences, IAlwaysOnTopWindowSystem windows,
        Func<nint, IAlwaysOnTopBorder> borderFactory, SynchronizationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(borderFactory);
        _preferences = preferences;
        _windows = windows;
        _borderFactory = borderFactory;
        _context = context;
        _windows.WindowChanged += OnWindowChanged;
        if (!_windows.IsTrackingForeground) Error = "Window tracking is unavailable. Choose a window from the list.";
        Refresh();
    }

    /// <summary>Call immediately before showing the panel, before Llampec can take focus.</summary>
    public void CaptureTarget()
    {
        if (_disposed) return;
        RememberForeground();
        _target = _lastExternal;
        _targetFrozen = true;
        Refresh();
    }

    public void ReleaseTarget()
    {
        if (_disposed) return;
        _targetFrozen = false;
        Refresh();
    }

    public IReadOnlyList<PinnableWindow> GetWindows()
    {
        Refresh();
        // Refresh notifications can arrive while existing rows are still displayed. Update the
        // action identities only when the UI actually requests a replacement row snapshot.
        _selectorIdentities = _availableIdentities;
        return _availableWindows;
    }

    public void ToggleTarget()
    {
        if (_disposed) return;
        Refresh();
        if (_target is { } identity) ToggleIdentity(identity);
        else SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A shortcut resolves the actual foreground application, including its owned dialogs. When
    /// Llampec itself has focus, it acts on the panel's external target without pinning the panel.
    /// </summary>
    public void ToggleForeground()
    {
        if (_disposed) return;
        nint foreground = _windows.ForegroundWindow;
        var resolution = _windows.ResolveForeground(foreground);
        WindowSnapshot? current = resolution.Target;
        if (current is null && resolution.IsOwnWindow)
        {
            var remembered = _targetFrozen ? _target : _lastExternal;
            current = remembered is { } identity ? ReadMatching(identity) : null;
        }
        if (current is { IsEligible: true })
        {
            Log.Info($"Always on Top shortcut: foreground=0x{foreground:X}, target=0x{current.Identity.Handle:X}, ownForeground={resolution.IsOwnWindow}.");
            ToggleIdentity(current.Identity);
        }
        else
        {
            // A shortcut does not implicitly navigate to settings. The main tile retains the
            // selector fallback; the host may show this shortcut error without taking focus.
            Error = "No eligible window is active.";
            Log.Warn($"Always on Top shortcut rejected: foreground=0x{foreground:X}, ownForeground={resolution.IsOwnWindow}, reason={resolution.Reason ?? "no remembered target"}.");
            Refresh();
        }
    }

    public void Toggle(nint handle)
    {
        if (_disposed) return;
        // Use the identity from the selector the user saw; a recycled handle must not select a new app.
        if (_selectorIdentities.TryGetValue(handle, out var identity)) ToggleIdentity(identity);
        else
        {
            Error = "This window is no longer available.";
            Refresh();
        }
    }

    private void ToggleIdentity(WindowIdentity identity)
    {
        if (_mutating) return;
        _mutating = true;
        try
        {
            Error = null;
            PrunePins();
            if (_pins.TryGetValue(identity.Handle, out var pin) && pin.Identity == identity)
            {
                if (!Unpin(pin)) Error = _windows.LastError ?? "Could not unpin this window.";
                return;
            }

            var window = ReadMatching(identity);
            if (window is not { IsEligible: true })
            {
                Error = "This window is no longer available.";
                return;
            }
            if (window.IsTopmost)
            {
                // Another application pinned it, possibly without a way to undo it. The user asked
                // for this window explicitly, so clear it without claiming it as Llampec's own pin.
                if (!ClearOtherAppPin(identity)) Error ??= "Could not unpin this window.";
                return;
            }
            if (!_windows.TryClaim(identity))
            {
                Error = _windows.LastError ?? "Could not pin this window.";
                return;
            }

            bool succeeded = _windows.TrySetTopmost(identity, true);
            var after = ReadMatching(identity);
            if (after is { IsTopmost: true } && _windows.HasClaim(identity))
            {
                // Even a partially failed native operation must remain tracked for cleanup.
                var newPin = new Pin(identity, after.Title);
                _pins.Add(identity.Handle, newPin);
                UpdateAppearance(newPin);
            }
            else _windows.ReleaseClaim(identity);
            if (!succeeded || after is not { IsTopmost: true }) Error = _windows.LastError ?? "Could not pin this window.";
        }
        finally
        {
            _mutating = false;
            Refresh();
        }
    }

    public void UnpinAll()
    {
        if (_disposed || _mutating) return;
        _mutating = true;
        try
        {
            Error = null;
            foreach (var pin in _pins.Values.ToArray())
                if (!Unpin(pin)) Error = _windows.LastError ?? "Could not unpin this window.";
        }
        finally { _mutating = false; Refresh(); }
    }

    /// <summary>
    /// Unpins Llampec's windows and also clears "always on top" from every application window that
    /// another application pinned (the topmost z-order band, WS_EX_TOPMOST). Shell surfaces and
    /// Llampec's own windows are not application windows here and are left alone. Only an explicit
    /// user request does this: module removal and exit restore Llampec's own pins only.
    /// </summary>
    public void RestoreAll()
    {
        if (_disposed || _mutating) return;
        _mutating = true;
        try
        {
            Error = null;
            foreach (var pin in _pins.Values.ToArray())
                if (!Unpin(pin)) Error = _windows.LastError ?? "Could not unpin this window.";
            foreach (var window in _windows.Enumerate())
                if (window is { IsEligible: true, IsTopmost: true } && !_pins.ContainsKey(window.Identity.Handle)
                    && !ClearOtherAppPin(window.Identity)) Error ??= "Could not unpin this window.";
        }
        finally { _mutating = false; Refresh(); }
    }

    private bool ClearOtherAppPin(WindowIdentity identity)
    {
        bool cleared = _windows.TryClearTopmost(identity);
        if (cleared) Log.Info($"Always on Top: cleared another application's pin on 0x{identity.Handle:X}.");
        else
        {
            Error = _windows.LastError ?? (ReadMatching(identity) is { IsTopmost: true }
                ? "The application keeps this window on top again. Change it in that application." : null);
            Log.Warn($"Always on Top: could not clear another application's pin on 0x{identity.Handle:X}: {Error}");
        }
        return cleared;
    }

    /// <summary>
    /// Restore session-owned pins before unloading the module. A failed restoration keeps its
    /// claim, border and service alive so the host can offer recovery instead of abandoning it.
    /// </summary>
    public bool TryDeactivate()
    {
        if (_mutating) return false;
        UnpinAll();
        return _pins.Count == 0;
    }

    private bool Unpin(Pin pin)
    {
        var current = ReadMatching(pin.Identity);
        if (current is null || !_windows.HasClaim(pin.Identity) || !current.IsTopmost)
        {
            ForgetPin(pin);
            return true;
        }

        // Keep the pin (and its record of the original state) until the window is opaque again.
        if (!RestoreOpacity(pin)) return false;
        bool succeeded = _windows.TrySetTopmost(pin.Identity, false);
        var after = ReadMatching(pin.Identity);
        if (after is { IsTopmost: true } && _windows.HasClaim(pin.Identity)) return false;
        ForgetPin(pin);
        return succeeded || after is null;
    }

    public void ApplyAppearance()
    {
        if (_disposed || _mutating) return;
        _mutating = true;
        try
        {
            ClearAppearanceError();
            PrunePins();
            foreach (var pin in _pins.Values) UpdateAppearance(pin);
        }
        finally { _mutating = false; Refresh(); }
    }

    /// <summary>Pinned windows by title, with any appearance of their own, as of the last refresh.</summary>
    public IReadOnlyList<PinnedWindowAppearance> GetPinnedAppearances()
    {
        return _pins.Values.OrderBy(pin => pin.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(pin => new PinnedWindowAppearance(pin.Identity.Handle, pin.Title, pin.Appearance)).ToArray();
    }

    /// <summary>Gives one pinned window its own appearance, or null to follow the global settings again.</summary>
    public void SetWindowAppearance(nint handle, WindowAppearance? appearance)
    {
        if (_disposed || _mutating || !_pins.TryGetValue(handle, out var pin)) return;
        _mutating = true;
        try
        {
            ClearAppearanceError();
            pin.Appearance = appearance;
            if (ReadMatching(pin.Identity) is not null) UpdateAppearance(pin);
        }
        finally { _mutating = false; Refresh(); }
    }

    private void ClearAppearanceError()
    {
        if (Error is BorderError or TransparencyError) Error = null;
    }

    private void UpdateAppearance(Pin pin)
    {
        var appearance = pin.Appearance ?? _preferences().Appearance;
        UpdateOpacity(pin, appearance.Alpha);
        if (!appearance.ShowBorder)
        {
            DisposeBorder(pin);
            return;
        }
        try
        {
            pin.Border ??= _borderFactory(pin.Identity.Handle);
            pin.Border.Update(appearance.BorderThickness, appearance.BorderColor);
        }
        catch (Exception ex)
        {
            DisposeBorder(pin);
            Error = BorderError;
            Log.Warn($"Always on Top border failed: {ex.Message}");
        }
    }

    private void UpdateOpacity(Pin pin, byte alpha)
    {
        if (alpha == 255)
        {
            RestoreOpacity(pin);
            return;
        }
        if (!_windows.TrySetOpacity(pin.Identity, alpha, ref pin.Opacity))
        {
            Error = _windows.LastError ?? TransparencyError;
            Log.Warn($"Always on Top opacity failed for 0x{pin.Identity.Handle:X}: {Error}");
        }
    }

    private bool RestoreOpacity(Pin pin)
    {
        if (pin.Opacity is not { } original) return true;
        if (_windows.TryRestoreOpacity(pin.Identity, original))
        {
            pin.Opacity = null;
            return true;
        }
        Log.Warn($"Could not restore the opacity of 0x{pin.Identity.Handle:X}: {_windows.LastError}");
        return false;
    }

    private static void DisposeBorder(Pin pin)
    {
        var border = pin.Border;
        pin.Border = null;
        try { border?.Dispose(); }
        catch (Exception ex) { Log.Warn($"Always on Top border cleanup failed: {ex.Message}"); }
    }

    private void PrunePins()
    {
        foreach (var pin in _pins.Values.ToArray())
        {
            var current = ReadMatching(pin.Identity);
            if (current is null || !current.IsTopmost || !_windows.HasClaim(pin.Identity)) ForgetPin(pin);
            else pin.Title = current.Title;
        }
    }

    private void ForgetPin(Pin pin)
    {
        _pins.Remove(pin.Identity.Handle);
        // An unpinned window must look as it did before; a closed one needs nothing.
        RestoreOpacity(pin);
        DisposeBorder(pin);
        // The backend also verifies both identity and the unique marker before removing it.
        _windows.ReleaseClaim(pin.Identity);
    }

    private WindowSnapshot? ReadMatching(WindowIdentity identity)
    {
        var current = _windows.Read(identity.Handle);
        return current?.Identity == identity ? current : null;
    }

    private void RememberForeground()
    {
        var current = _windows.ResolveForeground(_windows.ForegroundWindow).Target;
        if (current is { IsEligible: true }) _lastExternal = current.Identity;
        else if (!_windows.IsTrackingForeground || (_lastExternal is { } last && ReadMatching(last) is not { IsEligible: true })) _lastExternal = null;
    }

    public void Refresh()
    {
        if (_disposed || _mutating || _refreshing) return;
        _refreshing = true;
        try
        {
            PrunePins();
            RememberForeground();
            if (!_targetFrozen) _target = _lastExternal;
            var targetWindow = _target is { } target ? ReadMatching(target) : null;
            if (targetWindow is not { IsEligible: true }) _target = null;
            TargetTitle = _target is null ? string.Empty : targetWindow!.Title;
            IsTargetPinnedByOtherApp = _target is not null && targetWindow!.IsTopmost && !IsTargetPinned;

            var identities = new Dictionary<nint, WindowIdentity>();
            var available = new List<PinnableWindow>();
            foreach (var pin in _pins.Values)
            {
                identities[pin.Identity.Handle] = pin.Identity;
                available.Add(new(pin.Identity.Handle, pin.Title, true));
            }
            int otherApps = 0;
            foreach (var window in _windows.Enumerate())
            {
                if (!window.IsEligible || identities.ContainsKey(window.Identity.Handle)) continue;
                identities[window.Identity.Handle] = window.Identity;
                available.Add(new(window.Identity.Handle, window.Title, window.IsTopmost, window.IsTopmost));
                if (window.IsTopmost) otherApps++;
            }
            OtherAppPinCount = otherApps;
            _availableIdentities = identities;
            // Llampec's pins, then other applications' pins, then the rest, each by title.
            _availableWindows = available.OrderByDescending(w => w.IsPinned && !w.PinnedByOtherApp).ThenByDescending(w => w.PinnedByOtherApp)
                .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally { _refreshing = false; }
        PublishIfChanged();
    }

    private void OnWindowChanged(WindowChange change)
    {
        if (_disposed) return;
        if (_context is not null && Environment.CurrentManagedThreadId != _ownerThread)
        {
            _context.Post(_ => HandleWindowChanged(change), null);
            return;
        }
        HandleWindowChanged(change);
    }

    private void HandleWindowChanged(WindowChange change)
    {
        if (_disposed) return;
        // While the panel is hidden nobody sees the selector: remember the last external
        // app and follow only pinned windows. Opening the panel refreshes everything.
        if (!_targetFrozen)
        {
            if (change.Foreground) { RememberForeground(); return; }
            if (!_pins.ContainsKey(change.Handle)) return;
        }
        // Moving an unrelated window should not rebuild the selector for each animation frame.
        if (change.PositionOnly)
        {
            if (_pins.TryGetValue(change.Handle, out var pin))
            {
                if (_windows.IsTopmost(pin.Identity) && _windows.HasClaim(pin.Identity)) return;
            }
            else if (_target is not { } target || target.Handle != change.Handle
                || ReadMatching(target) is { IsEligible: true }) return;
            // A location event can also report an externally cleared TOPMOST style or a closed
            // target. Only those state changes need a complete refresh; the border follows motion.
        }
        QueueRefresh();
    }

    /// <summary>A burst of window events (titles, reorders) needs one enumeration, not one each.</summary>
    private void QueueRefresh()
    {
        if (_context is null) { Refresh(); return; }
        if (_refreshQueued) return;
        _refreshQueued = true;
        _context.Post(_ => { _refreshQueued = false; Refresh(); }, null);
    }

    /// <summary>Window events matter only while the panel shows them or a pin must be followed.</summary>
    private void UpdateWindowTracking()
    {
        if (_disposed) return;
        // Hooks belong to the owner thread's message loop.
        if (_context is not null && Environment.CurrentManagedThreadId != _ownerThread)
        { _context.Post(_ => UpdateWindowTracking(), null); return; }
        _windows.SetWindowTracking(_targetFrozen, _pins.Values.Select(pin => pin.Identity).ToArray());
    }

    private void PublishIfChanged()
    {
        UpdateWindowTracking();
        var state = (_target, TargetTitle, IsTargetPinned || IsTargetPinnedByOtherApp, Error);
        if (_publishedState == state && _publishedWindows.SequenceEqual(_availableWindows)) return;
        _publishedState = state;
        _publishedWindows = _availableWindows;
        if (_notifying) { _notifyAgain = true; return; }
        _notifying = true;
        try
        {
            do
            {
                _notifyAgain = false;
                Changed?.Invoke(this, EventArgs.Empty);
            } while (_notifyAgain && !_disposed);
        }
        finally { _notifying = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _windows.WindowChanged -= OnWindowChanged;
        _mutating = true;
        try
        {
            foreach (var pin in _pins.Values.ToArray())
            {
                if (!Unpin(pin))
                {
                    Log.Warn("Could not restore an Always on Top window during shutdown.");
                    ForgetPin(pin);
                }
            }
        }
        finally
        {
            _disposed = true;
            _mutating = false;
            _windows.Dispose();
            _target = null;
            TargetTitle = string.Empty;
            _availableWindows = [];
            _selectorIdentities.Clear();
            _availableIdentities.Clear();
        }
    }
}
