namespace Llampec.Platform;

public enum MonitorRecoveryResult
{
    /// <summary>Every DDC/CI monitor was already on.</summary>
    NothingToDo,
    /// <summary>A DDC/CI wake-up was enough.</summary>
    Woken,
    /// <summary>The monitor came back after Windows rebuilt the display connection.</summary>
    Reconnected,
    /// <summary>The monitor still reports standby, or the connection could not be rebuilt.</summary>
    Failed,
}

/// <summary>
/// Brings back an external monitor that Windows still lists as active but that shows nothing, for
/// example after Modern Standby when a USB4/Thunderbolt dock does not restore its DisplayPort tunnel.
/// The monitor then answers DDC/CI but reports standby (VCP D6 = 4): it wakes, finds no signal and
/// sleeps again. Measured on a Surface with a Philips 329M1RV behind a USB4 hub on 2026-10-16:
/// waking it, reapplying its mode or changing refresh rate did not help, and neither did cycling the
/// topology while it slept. Waking it over DDC/CI and, while it looked for a signal, switching to
/// PC screen only and back made the USB4 connection manager enumerate the monitor again.
/// <para>
/// Steps, escalating: wake (D6 = 1) and check; then wake again, PC screen only, back to the previous
/// topology (retried, since the first attempt can fail while the internal topology settles) and check.
/// Monitors switched off with their own button (D6 = 5) and monitors Llampec put into standby are
/// left alone.
/// </para>
/// </summary>
public sealed class MonitorRecovery(ExternalMonitorService monitors, Func<ProjectionTopology?>? topology = null,
    Action<ProjectionTopology>? setTopology = null, Func<TimeSpan, Task>? delay = null)
{
    public const uint NoSignalStandby = 4;
    private readonly Func<ProjectionTopology?> _topology = topology ?? ProjectionModes.TryGetCurrent;
    private readonly Action<ProjectionTopology> _setTopology = setTopology ?? ProjectionModes.Set;
    private readonly Func<TimeSpan, Task> _delay = delay ?? (time => Task.Delay(time));
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>Time a monitor gets to find the signal before it is checked again.</summary>
    public static readonly TimeSpan SignalWait = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Monitors in no-signal standby that nobody asked to sleep. Reading this never changes anything.
    /// </summary>
    public async Task<IReadOnlyList<DdcMonitor>> FindAsleepAsync()
    {
        await monitors.RefreshAsync().ConfigureAwait(false);
        return monitors.Monitors
            .Where(m => m.PowerState == NoSignalStandby && !monitors.IsStandbyByLlampec(m.Monitor))
            .Select(m => m.Monitor).ToArray();
    }

    /// <param name="force">
    /// Rebuild the connection even when no DDC/CI monitor reports standby: the user can see the screen
    /// is dark, and some monitors give no DDC/CI answer in that state.
    /// </param>
    /// <param name="justWoken">
    /// The monitor was just woken: a monitor without a signal reports "on" for about two seconds before
    /// sleeping again, so wait before checking.
    /// </param>
    public async Task<MonitorRecoveryResult> RecoverAsync(bool force, bool justWoken = false)
    {
        if (!await _running.WaitAsync(0).ConfigureAwait(false)) return MonitorRecoveryResult.Failed;
        try
        {
            if (justWoken) await _delay(SignalWait).ConfigureAwait(false);
            var asleep = await FindAsleepAsync().ConfigureAwait(false);
            if (asleep.Count == 0 && !force) return MonitorRecoveryResult.NothingToDo;
            Diagnostics.Log.Info($"Monitor recovery: {asleep.Count} monitor(s) in no-signal standby, force={force}.");
            if (asleep.Count > 0)
            {
                await monitors.SetPowerAsync(asleep, on: true).ConfigureAwait(false);
                await _delay(SignalWait).ConfigureAwait(false);
                asleep = await FindAsleepAsync().ConfigureAwait(false);
                if (asleep.Count == 0 && !force) return MonitorRecoveryResult.Woken;
            }

            if (_topology() is not { } previous || previous == ProjectionTopology.Internal)
                return asleep.Count == 0 ? MonitorRecoveryResult.Woken : MonitorRecoveryResult.Failed;
            // The monitor must be awake when the signal returns, or it misses it and sleeps again.
            if (asleep.Count > 0) await monitors.SetPowerAsync(asleep, on: true).ConfigureAwait(false);
            _setTopology(ProjectionTopology.Internal);
            await _delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            bool restored = false;
            for (int attempt = 0; attempt < 4 && !restored; attempt++)
            {
                try { _setTopology(previous); }
                catch (InvalidOperationException error) { Diagnostics.Log.Warn($"Monitor recovery: {error.Message}"); }
                restored = _topology() == previous;
                if (!restored) await _delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            if (!restored) { Diagnostics.Log.Warn("Monitor recovery could not restore the topology."); return MonitorRecoveryResult.Failed; }
            await _delay(SignalWait).ConfigureAwait(false);
            var still = await FindAsleepAsync().ConfigureAwait(false);
            if (still.Count > 0) await monitors.SetPowerAsync(still, on: true).ConfigureAwait(false);
            Diagnostics.Log.Info($"Monitor recovery finished: {still.Count} monitor(s) still in standby.");
            return still.Count == 0 ? MonitorRecoveryResult.Reconnected : MonitorRecoveryResult.Failed;
        }
        finally { _running.Release(); }
    }
}
