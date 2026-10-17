using Llampec.Platform;
using Llampec.Settings;

namespace Llampec.Actions.AgentMode;

/// <summary>
/// "Agentic work": keeps the PC working and reachable for local agents and remote tools while the
/// screens are dark. It combines existing Windows features and undoes each of them on exit:
/// <list type="number">
/// <item>A power request (as caffeine), with the display kept on for Windows by default, so the session
/// neither enters standby nor locks;</item>
/// <item>Second screen only, which switches the built-in panel off while another display remains;</item>
/// <item>DDC/CI standby for external monitors, which Windows still treats as part of the desktop;</item>
/// <item>black covers, excluded from capture, on any display still lit (an OLED's black pixels are off);</item>
/// <item>optionally, the best power efficiency mode.</item>
/// </list>
/// A key, click, touch or deliberate mouse movement on this PC ends the mode; input that agents and
/// remote tools synthesize does not. There is no timer or polling while it is active or inactive.
/// </summary>
public sealed class AgentModeAction : QuickActionBase, IDisposable
{
    /// <summary>Lets the panel finish hiding and the hand leave the mouse, as for Turn off display.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    private readonly Func<AgentModeSettings> _settings;
    private readonly IAgentModePlatform _platform;
    private readonly SynchronizationContext? _ui;
    private readonly TimeSpan _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Session? _session;
    private string? _error;
    private bool _disposed;

    private sealed class Session(IDisposable request, AgentModeSettings settings)
    {
        public IDisposable Request { get; } = request;
        public AgentModeSettings Settings { get; } = settings;
        public ProjectionTopology? PreviousTopology { get; set; }
        public PowerMode? PreviousMode { get; set; }
        public List<DdcMonitor> MonitorsOff { get; } = [];
        public IAgentScreen? Screen { get; set; }
    }

    public AgentModeAction(Func<AgentModeSettings> settings, IAgentModePlatform platform, TimeSpan? delay = null)
    {
        _settings = settings;
        _platform = platform;
        _delay = delay ?? Delay;
        _ui = SynchronizationContext.Current;
        Refresh();
    }

    public override string Id => "agent-mode";
    public override string Title => "Agentic work";
    public override string Glyph => ""; // Robot
    public override ActionKind Kind => ActionKind.ButtonWithSubpage;
    public bool IsActive => _session is not null;
    public string? Error => _error;

    /// <summary>Raised on the UI thread when the mode ends, whatever ended it.</summary>
    public event EventHandler? Ended;

    public override void Refresh()
    {
        State = _session is null ? ActionState.Off : ActionState.On;
        Subtitle = _error ?? (_session is null ? null : "Active");
    }

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        if (_session is not null) { await StopAsync("tile").ConfigureAwait(false); return; }
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    public async Task<bool> StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null) return true;
            var settings = _settings().Clone();
            IDisposable request;
            try { request = _platform.KeepAwake(settings.KeepUnlocked); }
            catch (Exception error)
            {
                Diagnostics.Log.Warn($"Agent mode power request failed: {error.Message}");
                _error = "Could not activate agentic work.";
                return false;
            }
            _error = null;
            var session = _session = new Session(request, settings); // A failure below restores what was done.
            var displays = _platform.Displays();
            bool builtInOff = settings.TurnOffBuiltInDisplay && displays.Any(d => d.Internal) && displays.Any(d => !d.Internal);
            PowerMode? mode = settings.BestEfficiency ? Try(_platform.Mode) : null;
            ProjectionTopology? topology = builtInOff ? Try(_platform.Topology) : null;
            var monitors = settings.MonitorStandby
                ? (await _platform.MonitorsAsync().ConfigureAwait(false)).Where(m => m.PoweredOn == true).ToArray() : [];
            // Saved before anything changes: a crash then leaves enough to restore at the next start.
            Try(() => _platform.SaveRecovery(new(1,
                topology is { } t && t != ProjectionTopology.External ? t : null,
                mode is { } m && m != PowerMode.BestEfficiency ? m : null,
                [.. monitors.Select(s => s.Monitor.Name)])));

            if (mode is { } previousMode && previousMode != PowerMode.BestEfficiency
                && Try(() => _platform.SetMode(PowerMode.BestEfficiency)))
                session.PreviousMode = previousMode;
            if (builtInOff && topology != ProjectionTopology.External
                && Try(() => _platform.SetTopology(ProjectionTopology.External)))
                session.PreviousTopology = topology ?? ProjectionTopology.Extend;
            if (monitors.Length > 0)
            {
                // Display handles change with the topology, so read them again after it.
                var current = session.PreviousTopology is null ? monitors
                    : (await _platform.MonitorsAsync().ConfigureAwait(false)).Where(m => m.PoweredOn == true).ToArray();
                session.MonitorsOff.AddRange(await _platform.SetMonitorPowerAsync(current.Select(s => s.Monitor), on: false).ConfigureAwait(false));
            }

            await OnUiAsync(() =>
            {
                var screen = _platform.CreateScreen();
                session.Screen = screen;
                screen.PhysicalInput += OnPhysicalInput;
                screen.DisplayTurnedOn += OnDisplayTurnedOn;
                CoverAndPark(session);
            }).ConfigureAwait(false);
            Diagnostics.Log.Info($"Agent mode started: builtInOff={session.PreviousTopology is not null}, "
                + $"standby={session.MonitorsOff.Count}, unlocked={settings.KeepUnlocked}, efficiency={session.PreviousMode is not null}");
            return true;
        }
        catch (Exception error) when (error is not ObjectDisposedException)
        {
            Diagnostics.Log.Error("Agent mode could not start", error);
            _error = "Could not activate agentic work.";
            await RestoreAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            _gate.Release();
            Refresh();
        }
    }

    /// <summary>Restores everything the mode changed. Safe to call when inactive.</summary>
    public async Task StopAsync(string reason)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is null) return;
            Diagnostics.Log.Info($"Agent mode ending: {reason}");
            await RestoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            Refresh();
        }
        if (_ui is null) Ended?.Invoke(this, EventArgs.Empty);
        else _ui.Post(_ => Ended?.Invoke(this, EventArgs.Empty), null);
    }

    /// <summary>The desktop changed while active, for example a monitor was unplugged and Windows lit the built-in panel.</summary>
    public void OnDisplayChanged()
    {
        if (_session is { Screen: not null } session && session.Settings.CoverDisplays) Try(() => CoverAndPark(session));
    }

    /// <summary>Undoes a session that ended with the process, for example after a crash or a forced shutdown.</summary>
    public async Task RecoverAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is not null) return;
            AgentModeRecovery? recovery = null;
            if (!Try(() => recovery = _platform.LoadRecovery()) || recovery is null) return;
            Diagnostics.Log.Info("Restoring display and power settings from an interrupted agent mode.");
            if (recovery.Topology is { } topology && Try(_platform.Topology) == ProjectionTopology.External)
                Try(() => _platform.SetTopology(topology));
            if (recovery.Mode is { } mode && Try(_platform.Mode) == PowerMode.BestEfficiency)
                Try(() => _platform.SetMode(mode));
            if (recovery.Monitors.Count > 0) await WakeAsync(recovery.Monitors).ConfigureAwait(false);
            Try(_platform.ClearRecovery);
        }
        finally { _gate.Release(); }
    }

    private async Task RestoreAsync()
    {
        var session = _session;
        _session = null;
        if (session is null) return;
        await OnUiAsync(() =>
        {
            if (session.Screen is not { } screen) return;
            screen.PhysicalInput -= OnPhysicalInput;
            screen.DisplayTurnedOn -= OnDisplayTurnedOn;
            screen.Dispose();
        }).ConfigureAwait(false);
        // The built-in panel first: it is the quickest to come back.
        if (session.PreviousTopology is { } topology && Try(_platform.Topology) == ProjectionTopology.External)
            Try(() => _platform.SetTopology(topology));
        if (session.PreviousMode is { } mode && Try(_platform.Mode) == PowerMode.BestEfficiency)
            Try(() => _platform.SetMode(mode));
        if (session.MonitorsOff.Count > 0) await WakeAsync([.. session.MonitorsOff.Select(m => m.Name)]).ConfigureAwait(false);
        Try(session.Request.Dispose);
        Try(_platform.ClearRecovery);
    }

    /// <summary>
    /// Wakes the named monitors through fresh handles: the topology may have changed since they slept.
    /// Waking a monitor that is already on is harmless.
    /// </summary>
    private async Task WakeAsync(IReadOnlyCollection<string> names)
    {
        try
        {
            var monitors = await _platform.MonitorsAsync().ConfigureAwait(false);
            var targets = monitors.Where(m => names.Contains(m.Monitor.Name)).Select(m => m.Monitor).ToArray();
            await _platform.SetMonitorPowerAsync(targets, on: true).ConfigureAwait(false);
        }
        catch (Exception error) { Diagnostics.Log.Warn($"Monitors could not be woken: {error.Message}"); }
    }

    private void CoverAndPark(Session session)
    {
        var dark = session.MonitorsOff.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var displays = _platform.Displays();
        var lit = displays.Where(d => !dark.Contains(d.DeviceName)).ToArray();
        if (session.Settings.CoverDisplays) session.Screen?.Cover(lit);
        // A still pointer would mark an OLED; leave it on a monitor in standby, or in a covered corner.
        if (displays.FirstOrDefault(d => dark.Contains(d.DeviceName)) is { } standby)
            _platform.ParkCursor((standby.Bounds.Left + standby.Bounds.Right) / 2, (standby.Bounds.Top + standby.Bounds.Bottom) / 2);
        else if (session.Settings.CoverDisplays && lit.Length > 0)
            _platform.ParkCursor(lit[0].Bounds.Right - 1, lit[0].Bounds.Bottom - 1);
    }

    private void OnPhysicalInput(object? sender, EventArgs e) => _ = StopAsync("local input");
    private void OnDisplayTurnedOn(object? sender, EventArgs e) => _ = StopAsync("Windows turned the display on");

    private Task OnUiAsync(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) { action(); return Task.CompletedTask; }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try { action(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
        }, null);
        return done.Task;
    }

    private static bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception error) { Diagnostics.Log.Warn($"Agent mode step failed: {error.Message}"); return false; }
    }

    private static T? Try<T>(Func<T?> read) where T : struct
    {
        try { return read(); }
        catch (Exception error) { Diagnostics.Log.Warn($"Agent mode could not read a setting: {error.Message}"); return null; }
    }

    /// <summary>Restores synchronously enough for application exit; the DDC wake-up is awaited briefly.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        try { StopAsync("exit").Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception error) { Diagnostics.Log.Warn($"Agent mode did not stop cleanly: {error.Message}"); }
        _disposed = true;
    }
}
