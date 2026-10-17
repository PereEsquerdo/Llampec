using Llampec.Actions;
using Llampec.Actions.AgentMode;
using Llampec.Actions.Monitor;
using Llampec.Interop;
using Llampec.Platform;
using Llampec.Settings;
using Xunit;

namespace Llampec.Tests;

public sealed class AgentModeTests
{
    internal sealed class FakeMonitors : IMonitorControlBackend
    {
        public List<DdcMonitor> Monitors = [];
        public Dictionary<(string, MonitorFeature), uint> Values = [];
        public List<(string Name, MonitorFeature Feature, uint Value)> Writes = [];
        public bool FailReads;
        public TaskCompletionSource? WriteGate;

        public FakeMonitors Add(string name, uint brightness = 50, bool on = true, bool volume = true)
        {
            var monitor = new DdcMonitor($@"\\.\DISPLAY{Monitors.Count + 2}", 0, name, Monitors.Count + 2);
            Monitors.Add(monitor);
            Values[(monitor.Key, MonitorFeature.Brightness)] = brightness;
            Values[(monitor.Key, MonitorFeature.Contrast)] = 50;
            Values[(monitor.Key, MonitorFeature.Power)] = on ? 1u : 4u;
            if (volume) Values[(monitor.Key, MonitorFeature.Volume)] = 30;
            return this;
        }

        public IReadOnlyList<DdcMonitor> Enumerate() => Monitors.ToArray();
        public FeatureValue? Read(DdcMonitor monitor, MonitorFeature feature) =>
            !FailReads && Values.TryGetValue((monitor.Key, feature), out uint value)
                ? new(value, feature == MonitorFeature.Power ? 4u : 100u) : null;
        public bool Write(DdcMonitor monitor, MonitorFeature feature, uint value)
        {
            WriteGate?.Task.Wait();
            lock (Writes) Writes.Add((monitor.Name, feature, value));
            Values[(monitor.Key, feature)] = value;
            return true;
        }
    }

    internal sealed class FakeScreen : IAgentScreen
    {
        public List<string> Covered = [];
        public bool Disposed;
        public event EventHandler? PhysicalInput;
        public event EventHandler? DisplayTurnedOn;
        public void Cover(IReadOnlyList<ActiveDisplay> displays) => Covered = [.. displays.Select(d => d.DeviceName)];
        public void Input() => PhysicalInput?.Invoke(this, EventArgs.Empty);
        public void Wake() => DisplayTurnedOn?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Disposed = true;
    }

    internal sealed class FakePlatform : IAgentModePlatform
    {
        public readonly FakeMonitors Ddc;
        public readonly ExternalMonitorService Service;
        public readonly List<string> Log = [];
        public List<ActiveDisplay> Active = [];
        public ProjectionTopology? Current = ProjectionTopology.Extend;
        public PowerMode? Power = PowerMode.Balanced;
        public bool? KeptDisplayOn;
        public bool RequestReleased;
        public FakeScreen? Screen;
        public AgentModeRecovery? Recovery;
        public (int X, int Y)? Cursor;
        public bool FailTopology;

        public FakePlatform(FakeMonitors? ddc = null)
        {
            Ddc = ddc ?? new FakeMonitors();
            Service = new ExternalMonitorService(Ddc);
        }

        public FakePlatform WithDisplays(bool builtIn, params string[] external)
        {
            Active.Clear();
            if (builtIn) Active.Add(Display(@"\\.\DISPLAY1", "Built-in display", true, 0));
            for (int i = 0; i < external.Length; i++)
            {
                Active.Add(Display($@"\\.\DISPLAY{i + 2}", external[i], false, (i + 1) * 3000));
                if (Ddc.Monitors.All(m => m.Name != external[i])) Ddc.Add(external[i]);
            }
            return this;
        }

        private static ActiveDisplay Display(string device, string name, bool builtIn, int left) =>
            new(device, name, builtIn, left + 1, new User32.RECT { Left = left, Top = 0, Right = left + 2000, Bottom = 1000 });

        private sealed class Request(FakePlatform owner) : IDisposable
        {
            public void Dispose() { owner.RequestReleased = true; owner.Log.Add("release"); }
        }

        public IDisposable KeepAwake(bool keepDisplayOn) { KeptDisplayOn = keepDisplayOn; Log.Add("awake"); return new Request(this); }
        public IReadOnlyList<ActiveDisplay> Displays() => Active.ToArray();
        public ProjectionTopology? Topology() => Current;
        public void SetTopology(ProjectionTopology topology)
        {
            if (FailTopology) throw new InvalidOperationException("SetDisplayConfig failed");
            Log.Add($"topology {topology}");
            Current = topology;
            if (topology == ProjectionTopology.External) Active.RemoveAll(d => d.Internal);
            else if (Active.All(d => !d.Internal)) Active.Insert(0, Display(@"\\.\DISPLAY1", "Built-in display", true, 0));
        }
        public PowerMode? Mode() => Power;
        public void SetMode(PowerMode mode) { Log.Add($"mode {mode}"); Power = mode; }
        public async Task<IReadOnlyList<MonitorStatus>> MonitorsAsync() { await Service.RefreshAsync(); return Service.Monitors; }
        public async Task<IReadOnlyList<DdcMonitor>> SetMonitorPowerAsync(IEnumerable<DdcMonitor> monitors, bool on)
        {
            var result = await Service.SetPowerAsync(monitors, on);
            foreach (var monitor in result) Log.Add($"{monitor.Name} {(on ? "on" : "off")}");
            return result;
        }
        public IAgentScreen CreateScreen() { Log.Add("screen"); return Screen = new FakeScreen(); }
        public void ParkCursor(int x, int y) => Cursor = (x, y);
        public AgentModeRecovery? LoadRecovery() => Recovery;
        public void SaveRecovery(AgentModeRecovery recovery) => Recovery = recovery;
        public void ClearRecovery() => Recovery = null;
    }

    private static AgentModeAction Mode(FakePlatform platform, AgentModeSettings? settings = null) =>
        new(() => settings ?? new AgentModeSettings(), platform, TimeSpan.Zero);

    [Fact]
    public async Task DeskSetupTurnsOffBuiltInPanelAndMonitorAndKeepsTheSessionUnlocked()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true, "PHL 329M1RV");
        using var mode = Mode(platform);

        Assert.True(await mode.StartAsync());

        Assert.True(mode.IsActive);
        Assert.Equal(ActionState.On, mode.State);
        Assert.True(platform.KeptDisplayOn);
        Assert.Equal(ProjectionTopology.External, platform.Current);
        Assert.Equal(4u, platform.Ddc.Values[(platform.Ddc.Monitors[0].Key, MonitorFeature.Power)]);
        // Every lit display is dark by other means: nothing to cover; the pointer rests on the sleeping monitor.
        Assert.Empty(platform.Screen!.Covered);
        Assert.Equal((4000, 500), platform.Cursor);
        Assert.Equal(["awake", "topology External", "PHL 329M1RV off", "screen"], platform.Log);
        Assert.Equal(ProjectionTopology.Extend, platform.Recovery!.Topology);
        Assert.Equal(["PHL 329M1RV"], platform.Recovery.Monitors);
    }

    [Fact]
    public async Task PhysicalInputRestoresEverythingInOrder()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true, "PHL 329M1RV");
        using var mode = Mode(platform, new() { BestEfficiency = true });
        await mode.StartAsync();
        var ended = new TaskCompletionSource();
        mode.Ended += (_, _) => ended.TrySetResult();
        platform.Log.Clear();

        platform.Screen!.Input();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(mode.IsActive);
        Assert.True(platform.Screen.Disposed);
        Assert.Equal(["topology Extend", "mode Balanced", "PHL 329M1RV on", "release"], platform.Log);
        Assert.Equal(1u, platform.Ddc.Values[(platform.Ddc.Monitors[0].Key, MonitorFeature.Power)]);
        Assert.Null(platform.Recovery);
        Assert.Equal(ActionState.Off, mode.State);
    }

    [Fact]
    public async Task SingleBuiltInDisplayIsCoveredInsteadOfRemoved()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true);
        using var mode = Mode(platform);

        await mode.StartAsync();

        Assert.Equal(ProjectionTopology.Extend, platform.Current);
        Assert.Equal([@"\\.\DISPLAY1"], platform.Screen!.Covered);
        Assert.Equal((1999, 999), platform.Cursor); // bottom-right pixel: the pointer is almost entirely off screen
        await mode.StopAsync("test");
        Assert.True(platform.RequestReleased);
    }

    [Fact]
    public async Task LockableVariantLetsWindowsTurnOffTheDisplayAndEndsWhenItReturns()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true);
        using var mode = Mode(platform, new() { KeepUnlocked = false });
        await mode.StartAsync();
        Assert.False(platform.KeptDisplayOn);

        var ended = new TaskCompletionSource();
        mode.Ended += (_, _) => ended.TrySetResult();
        platform.Screen!.Wake();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(mode.IsActive);
    }

    [Fact]
    public async Task UserChangesDuringTheSessionAreNotOverwritten()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true, "PHL 329M1RV");
        using var mode = Mode(platform, new() { BestEfficiency = true });
        await mode.StartAsync();
        platform.Current = ProjectionTopology.Clone; // chosen with Win+P meanwhile
        platform.Power = PowerMode.BestPerformance;
        platform.Log.Clear();

        await mode.StopAsync("test");

        Assert.Equal(ProjectionTopology.Clone, platform.Current);
        Assert.Equal(PowerMode.BestPerformance, platform.Power);
        Assert.Equal(["PHL 329M1RV on", "release"], platform.Log);
    }

    [Fact]
    public async Task OptionsLeaveTheirFeatureUntouched()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true, "PHL 329M1RV");
        using var mode = Mode(platform, new() { TurnOffBuiltInDisplay = false, MonitorStandby = false, CoverDisplays = false });

        await mode.StartAsync();

        Assert.Equal(["awake", "screen"], platform.Log);
        Assert.Empty(platform.Screen!.Covered);
        Assert.Null(platform.Cursor);
    }

    [Fact]
    public async Task MonitorAlreadyInStandbyIsNotWokenOnExit()
    {
        var ddc = new FakeMonitors().Add("PHL 329M1RV", on: false);
        var platform = new FakePlatform(ddc).WithDisplays(builtIn: true, "PHL 329M1RV");
        using var mode = Mode(platform);
        await mode.StartAsync();
        await mode.StopAsync("test");
        Assert.DoesNotContain(ddc.Writes, write => write.Feature == MonitorFeature.Power);
    }

    [Fact]
    public async Task UnplugWhileActiveCoversTheBuiltInPanelWindowsReenabled()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true, "PHL 329M1RV");
        using var mode = Mode(platform);
        await mode.StartAsync();

        platform.Active.Clear();
        platform.Active.Add(new(@"\\.\DISPLAY1", "Built-in display", true, 1, new User32.RECT { Right = 2000, Bottom = 1000 }));
        mode.OnDisplayChanged();

        Assert.Equal([@"\\.\DISPLAY1"], platform.Screen!.Covered);
    }

    [Fact]
    public async Task FailedTopologyChangeStillDarkensWhatItCan()
    {
        var platform = new FakePlatform().WithDisplays(builtIn: true, "PHL 329M1RV");
        platform.FailTopology = true;
        using var mode = Mode(platform);

        Assert.True(await mode.StartAsync());

        Assert.Equal(ProjectionTopology.Extend, platform.Current);
        Assert.Equal([@"\\.\DISPLAY1"], platform.Screen!.Covered);
        Assert.Equal(ProjectionTopology.Extend, platform.Recovery!.Topology); // restored only if Windows shows External
        await mode.StopAsync("test");
        Assert.DoesNotContain("topology Extend", platform.Log);
    }

    [Fact]
    public async Task RecoveryUndoesAnInterruptedSession()
    {
        var ddc = new FakeMonitors().Add("PHL 329M1RV", on: false);
        var platform = new FakePlatform(ddc) { Current = ProjectionTopology.External, Power = PowerMode.BestEfficiency };
        platform.WithDisplays(builtIn: false, "PHL 329M1RV");
        platform.Recovery = new(1, ProjectionTopology.Extend, PowerMode.Balanced, ["PHL 329M1RV"]);
        using var mode = Mode(platform);

        await mode.RecoverAsync();

        Assert.Equal(ProjectionTopology.Extend, platform.Current);
        Assert.Equal(PowerMode.Balanced, platform.Power);
        Assert.Equal(1u, ddc.Values[(ddc.Monitors[0].Key, MonitorFeature.Power)]);
        Assert.Null(platform.Recovery);
    }

    [Fact]
    public async Task FailedPowerRequestDoesNotStart()
    {
        var platform = new ThrowingPlatform();
        using var mode = new AgentModeAction(() => new(), platform, TimeSpan.Zero);
        Assert.False(await mode.StartAsync());
        Assert.False(mode.IsActive);
        Assert.Equal("Could not activate agentic work.", mode.Subtitle);
    }

    private sealed class ThrowingPlatform : IAgentModePlatform
    {
        public IDisposable KeepAwake(bool keepDisplayOn) => throw new System.ComponentModel.Win32Exception(5);
        public IReadOnlyList<ActiveDisplay> Displays() => [];
        public ProjectionTopology? Topology() => null;
        public void SetTopology(ProjectionTopology topology) => throw new NotSupportedException();
        public PowerMode? Mode() => null;
        public void SetMode(PowerMode mode) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorStatus>> MonitorsAsync() => Task.FromResult<IReadOnlyList<MonitorStatus>>([]);
        public Task<IReadOnlyList<DdcMonitor>> SetMonitorPowerAsync(IEnumerable<DdcMonitor> monitors, bool on) => Task.FromResult<IReadOnlyList<DdcMonitor>>([]);
        public IAgentScreen CreateScreen() => throw new NotSupportedException();
        public void ParkCursor(int x, int y) { }
        public AgentModeRecovery? LoadRecovery() => null;
        public void SaveRecovery(AgentModeRecovery recovery) { }
        public void ClearRecovery() { }
    }
}

public sealed class ExternalMonitorTests
{
    [Fact]
    public async Task RefreshListsOnlyMonitorsThatAnswerAndReadsDetailsOnRequest()
    {
        var ddc = new AgentModeTests.FakeMonitors().Add("PHL 329M1RV", brightness: 40);
        ddc.Monitors.Add(new(@"\\.\DISPLAY9", 0, "No DDC", 9));
        using var service = new ExternalMonitorService(ddc);

        await service.RefreshAsync();
        var status = Assert.Single(service.Monitors);
        Assert.Equal(40u, status.Brightness!.Value.Current);
        Assert.True(status.PoweredOn);
        Assert.Null(status.Volume);

        await service.RefreshAsync(details: true);
        Assert.Equal(30u, service.Monitors[0].Volume!.Value.Current);
        Assert.Equal(50u, service.Monitors[0].Contrast!.Value.Current);
    }

    [Fact]
    public async Task MonitorThatMissesOneExchangeIsKept()
    {
        var ddc = new AgentModeTests.FakeMonitors().Add("PHL 329M1RV");
        using var service = new ExternalMonitorService(ddc);
        await service.RefreshAsync();
        ddc.FailReads = true;
        await service.RefreshAsync();
        Assert.Single(service.Monitors);
    }

    [Fact]
    public async Task SliderValuesAreCoalescedWhileTheBusIsBusy()
    {
        var ddc = new AgentModeTests.FakeMonitors().Add("PHL 329M1RV");
        using var service = new ExternalMonitorService(ddc);
        await service.RefreshAsync();
        var monitor = service.Monitors[0].Monitor;
        ddc.WriteGate = new TaskCompletionSource();

        service.Set(monitor, MonitorFeature.Brightness, 10);
        await Task.Delay(50); // the first write is now blocked on the bus
        for (uint value = 11; value <= 30; value++) service.Set(monitor, MonitorFeature.Brightness, value);
        Assert.Equal(30u, service.Monitors[0].Brightness!.Value.Current); // shown at once
        ddc.WriteGate.SetResult();
        for (int i = 0; i < 100 && ddc.Values[(monitor.Key, MonitorFeature.Brightness)] != 30; i++) await Task.Delay(10);

        lock (ddc.Writes) Assert.Equal([10u, 30u], ddc.Writes.Select(write => write.Value));
    }

    [Fact]
    public async Task TileTogglesStandbyAndShowsBrightness()
    {
        var ddc = new AgentModeTests.FakeMonitors().Add("PHL 329M1RV", brightness: 50);
        using var service = new ExternalMonitorService(ddc);
        using var action = new MonitorAction(service, () => [BuiltIn, External(@"\\.\DISPLAY2", "PHL 329M1RV")]);
        await service.RefreshAsync();
        Assert.Equal(ActionState.On, action.State);
        Assert.Equal(UiText.Format("Brightness {0} %", 50), action.Subtitle);

        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(4u, ddc.Values[(ddc.Monitors[0].Key, MonitorFeature.Power)]);
        Assert.Equal(ActionState.Off, action.State);

        await action.ExecuteAsync(CancellationToken.None);
        Assert.Equal(1u, ddc.Values[(ddc.Monitors[0].Key, MonitorFeature.Power)]);
    }

    [Fact]
    public async Task NoMonitorMakesTheTileUnavailable()
    {
        using var service = new ExternalMonitorService(new AgentModeTests.FakeMonitors());
        using var action = new MonitorAction(service);
        Assert.True(action.IsAvailable); // not read yet
        await service.RefreshAsync();
        Assert.False(action.IsAvailable);
        Assert.Equal("No DDC/CI monitor", action.Subtitle);
    }

    private static readonly ActiveDisplay BuiltIn = new(@"\\.\DISPLAY1", "Built-in display", true, 1, new User32.RECT { Right = 2000, Bottom = 1000 });
    private static ActiveDisplay External(string device, string name) => new(device, name, false, 2, new User32.RECT { Left = 2000, Right = 4000, Bottom = 1000 });

    [Fact]
    public async Task TileRefusesStandbyWhenNoOtherDisplayWouldStayLit()
    {
        var ddc = new AgentModeTests.FakeMonitors().Add("PHL 329M1RV");
        using var service = new ExternalMonitorService(ddc);
        // The built-in panel is already off through Second screen only.
        using var action = new MonitorAction(service, () => [External(@"\\.\DISPLAY2", "PHL 329M1RV")]);

        await action.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1u, ddc.Values[(ddc.Monitors[0].Key, MonitorFeature.Power)]);
        Assert.Equal("Another display must stay on", action.Subtitle); // kept after the tap's own refresh
        action.Refresh(); // next panel opening
        Assert.NotEqual("Another display must stay on", action.Subtitle);
    }

    [Fact]
    public void SingleDisplayRowsKeepOneDisplayLit()
    {
        var philips = new DdcMonitor(@"\\.\DISPLAY2", 0, "PHL 329M1RV", 2);
        var both = new[] { BuiltIn, External(@"\\.\DISPLAY2", "PHL 329M1RV") };

        var rows = DisplayPower.Rows(both, ProjectionTopology.Extend, [new(philips, true, null)]);
        Assert.Equal([("Built-in display", true, true), ("PHL 329M1RV", true, true)], rows.Select(r => (r.Name, r.On, r.CanToggle)));

        // Philips in standby: the built-in panel is the only lit display and cannot be turned off.
        rows = DisplayPower.Rows(both, ProjectionTopology.Extend, [new(philips, false, null)]);
        Assert.Equal([("Built-in display", true, false), ("PHL 329M1RV", false, true)], rows.Select(r => (r.Name, r.On, r.CanToggle)));

        // Built-in panel off through Second screen only: it can come back; the Philips must stay on.
        rows = DisplayPower.Rows([both[1]], ProjectionTopology.External, [new(philips, true, null)]);
        Assert.Equal([("Built-in display", false, true), ("PHL 329M1RV", true, false)], rows.Select(r => (r.Name, r.On, r.CanToggle)));
        Assert.True(rows[0].Internal);
        Assert.Same(philips, rows[1].Monitor);
    }

    [Fact]
    public void MonitorWithoutDdcStillCountsAsLit()
    {
        var philips = new DdcMonitor(@"\\.\DISPLAY2", 0, "PHL 329M1RV", 2);
        var displays = new[] { External(@"\\.\DISPLAY2", "PHL 329M1RV"), External(@"\\.\DISPLAY3", "Old TV") };
        Assert.True(DisplayPower.LeavesALitDisplay(displays, [philips], [new(philips, true, null)]));
        Assert.False(DisplayPower.LeavesALitDisplay([displays[0]], [philips], [new(philips, true, null)]));
    }
}
