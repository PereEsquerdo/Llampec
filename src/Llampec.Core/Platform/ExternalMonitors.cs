using Llampec.Interop;

namespace Llampec.Platform;

/// <summary>MCCS VCP codes Llampec reads and writes over DDC/CI.</summary>
public enum MonitorFeature : byte
{
    Brightness = 0x10,
    Contrast = 0x12,
    Volume = 0x62,
    /// <summary>Display power mode: 1 on, 4 off (the monitor's DPM standby).</summary>
    Power = 0xD6,
}

public readonly record struct FeatureValue(uint Current, uint Maximum)
{
    public int Percent => Maximum == 0 ? 0 : (int)Math.Round(100.0 * Math.Min(Current, Maximum) / Maximum);
}

/// <summary>One physical monitor behind a desktop display. Valid until the display configuration changes.</summary>
public sealed record DdcMonitor(string DeviceName, int Index, string Name, nint Display)
{
    public string Key => $"{DeviceName}#{Index}";
}

/// <param name="PowerState">The raw VCP D6 value: 1 on, 4 standby (also when it finds no signal), 5 switched off.</param>
public sealed record MonitorStatus(DdcMonitor Monitor, bool? PoweredOn, FeatureValue? Brightness,
    FeatureValue? Contrast = null, FeatureValue? Volume = null, uint? PowerState = null);

/// <summary>Blocking DDC/CI calls; each takes tens to hundreds of milliseconds.</summary>
public interface IMonitorControlBackend
{
    /// <summary>The physical monitors of every external desktop display, without probing them.</summary>
    IReadOnlyList<DdcMonitor> Enumerate();
    FeatureValue? Read(DdcMonitor monitor, MonitorFeature feature);
    bool Write(DdcMonitor monitor, MonitorFeature feature, uint value);
}

/// <summary>
/// The Monitor Configuration API. Built-in panels have no DDC/CI channel and are skipped: Windows
/// already controls their brightness. Handles are opened per call and released at once, so a
/// display change never leaves a stale handle behind.
/// </summary>
public sealed class NativeMonitorControlBackend : IMonitorControlBackend
{
    private const int Attempts = 3;

    public IReadOnlyList<DdcMonitor> Enumerate()
    {
        var monitors = new List<DdcMonitor>();
        foreach (var display in ActiveDisplays.Read())
        {
            if (display.Internal || !Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(display.Monitor, out uint count)) continue;
            for (int index = 0; index < count; index++)
                monitors.Add(new(display.DeviceName, index, count > 1 ? $"{display.Name} {index + 1}" : display.Name, display.Monitor));
        }
        return monitors;
    }

    public FeatureValue? Read(DdcMonitor monitor, MonitorFeature feature) => WithHandle(monitor, handle =>
    {
        // A monitor occasionally misses one exchange, for example just after waking.
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (Dxva2.GetVCPFeatureAndVCPFeatureReply(handle, (byte)feature, out _, out uint current, out uint maximum))
                return new FeatureValue(current, maximum);
            if (attempt + 1 < Attempts) Thread.Sleep(50);
        }
        return (FeatureValue?)null;
    });

    public bool Write(DdcMonitor monitor, MonitorFeature feature, uint value) => WithHandle(monitor, handle =>
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (Dxva2.SetVCPFeature(handle, (byte)feature, value)) return true;
            if (attempt + 1 < Attempts) Thread.Sleep(50);
        }
        Diagnostics.Log.Warn($"DDC/CI write 0x{(byte)feature:X2} to {monitor.Name} failed: 0x{Marshal.GetLastPInvokeError():X}");
        return false;
    });

    private static T? WithHandle<T>(DdcMonitor monitor, Func<nint, T> operation)
    {
        if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Display, out uint count) || monitor.Index >= count) return default;
        var physical = new Dxva2.PHYSICAL_MONITOR[count];
        if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(monitor.Display, count, physical)) return default;
        try { return operation(physical[monitor.Index].hPhysicalMonitor); }
        finally { Dxva2.DestroyPhysicalMonitors(count, physical); }
    }
}

/// <summary>
/// Reads and writes external monitors on demand, one DDC/CI exchange at a time. There is no polling:
/// state is read when the panel or the monitor page opens. Slider changes are coalesced so that only
/// the latest value for each control is sent once the bus is free.
/// </summary>
public sealed class ExternalMonitorService : IDisposable
{
    public const uint PowerOn = 1;
    public const uint PowerOff = 4;

    private readonly IMonitorControlBackend _backend;
    private readonly SemaphoreSlim _bus = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<(string Key, MonitorFeature Feature), (DdcMonitor Monitor, uint Value)> _pending = [];
    private IReadOnlyList<MonitorStatus> _monitors = [];
    private readonly HashSet<string> _standbyByLlampec = new(StringComparer.Ordinal);
    private Task? _refresh;
    private bool _draining;
    private bool _disposed;

    public ExternalMonitorService(IMonitorControlBackend? backend = null) => _backend = backend ?? new NativeMonitorControlBackend();

    /// <summary>The monitors that answered DDC/CI at the last refresh, with the values last read or written.</summary>
    public IReadOnlyList<MonitorStatus> Monitors { get { lock (_gate) return _monitors; } }

    /// <summary>
    /// True when Llampec put this monitor into standby and has not woken it since, so a standby
    /// reading is intended rather than a monitor that failed to wake. Keyed by name, which survives
    /// display changes.
    /// </summary>
    public bool IsStandbyByLlampec(DdcMonitor monitor) { lock (_gate) return _standbyByLlampec.Contains(monitor.Name); }

    /// <summary>True once a refresh has completed.</summary>
    public bool HasRead { get; private set; }

    /// <summary>Raised from a thread-pool thread after a refresh or an accepted change.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Re-reads power and brightness, plus contrast and volume when <paramref name="details"/> is set.
    /// A refresh already running is shared unless details were requested and it lacks them.
    /// </summary>
    public Task RefreshAsync(bool details = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_refresh is { IsCompleted: false } running && !details) return running;
            var previous = _refresh ?? Task.CompletedTask;
            return _refresh = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    await RunOnBusAsync(() => ReadAll(details)).ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { }
                catch (Exception error) { Diagnostics.Log.Warn($"Monitors could not be read: {error.Message}"); }
            });
        }
    }

    private void ReadAll(bool details)
    {
        var known = Monitors.ToDictionary(status => status.Monitor.Key);
        var result = new List<MonitorStatus>();
        foreach (var monitor in _backend.Enumerate())
        {
            var power = _backend.Read(monitor, MonitorFeature.Power);
            var brightness = _backend.Read(monitor, MonitorFeature.Brightness);
            known.TryGetValue(monitor.Key, out var old);
            if (power is null && brightness is null)
            {
                // A monitor that answered before can miss a whole exchange just after waking; keep it.
                // Otherwise it has no DDC/CI, or DDC/CI is disabled in its own menu.
                if (old is not null) result.Add(old with { Monitor = monitor });
                continue;
            }
            result.Add(new(monitor, power is { } p ? p.Current == PowerOn : null, brightness,
                details ? _backend.Read(monitor, MonitorFeature.Contrast) : old?.Contrast,
                details ? _backend.Read(monitor, MonitorFeature.Volume) : old?.Volume, power?.Current));
        }
        lock (_gate) { _monitors = result.AsReadOnly(); HasRead = true; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Queues a value; a newer value for the same control replaces one not yet sent.</summary>
    public void Set(DdcMonitor monitor, MonitorFeature feature, uint value)
    {
        bool start;
        lock (_gate)
        {
            if (_disposed) return;
            _pending[(monitor.Key, feature)] = (monitor, value);
            Update(monitor, feature, value);
            start = !_draining;
            _draining = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (start) _ = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            (DdcMonitor Monitor, MonitorFeature Feature, uint Value) next;
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0) { _draining = false; return; }
                var entry = _pending.First();
                _pending.Remove(entry.Key);
                next = (entry.Value.Monitor, entry.Key.Feature, entry.Value.Value);
            }
            try { await RunOnBusAsync(() => _backend.Write(next.Monitor, next.Feature, next.Value)).ConfigureAwait(false); }
            catch (Exception error) when (error is not ObjectDisposedException)
            { Diagnostics.Log.Warn($"Monitor setting failed: {error.Message}"); }
            catch (ObjectDisposedException) { return; }
        }
    }

    /// <summary>Turns each monitor on or off. Returns the monitors that accepted it.</summary>
    public async Task<IReadOnlyList<DdcMonitor>> SetPowerAsync(IEnumerable<DdcMonitor> monitors, bool on)
    {
        var accepted = new List<DdcMonitor>();
        foreach (var monitor in monitors.ToArray())
        {
            if (await RunOnBusAsync(() => _backend.Write(monitor, MonitorFeature.Power, on ? PowerOn : PowerOff)).ConfigureAwait(false))
            {
                accepted.Add(monitor);
                lock (_gate)
                {
                    Update(monitor, MonitorFeature.Power, on ? PowerOn : PowerOff);
                    if (on) _standbyByLlampec.Remove(monitor.Name); else _standbyByLlampec.Add(monitor.Name);
                }
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return accepted;
    }

    private void Update(DdcMonitor monitor, MonitorFeature feature, uint value)
    {
        _monitors = _monitors.Select(status => status.Monitor.Key != monitor.Key ? status : feature switch
        {
            MonitorFeature.Power => status with { PoweredOn = value == PowerOn, PowerState = value },
            MonitorFeature.Brightness => status with { Brightness = With(status.Brightness, value) },
            MonitorFeature.Contrast => status with { Contrast = With(status.Contrast, value) },
            MonitorFeature.Volume => status with { Volume = With(status.Volume, value) },
            _ => status,
        }).ToArray();

        static FeatureValue? With(FeatureValue? old, uint value) => old is { } known ? known with { Current = Math.Min(value, known.Maximum) } : null;
    }

    private async Task<T> RunOnBusAsync<T>(Func<T> operation)
    {
        await _bus.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return operation();
        }
        finally { _bus.Release(); }
    }

    private Task RunOnBusAsync(Action operation) => RunOnBusAsync(() => { operation(); return true; });

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _pending.Clear(); }
    }
}
