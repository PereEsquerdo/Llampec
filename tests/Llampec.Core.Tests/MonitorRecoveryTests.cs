using Llampec.Actions;
using Llampec.Actions.Monitor;
using Llampec.Platform;
using Xunit;

namespace Llampec.Tests;

/// <summary>
/// A monitor behind a dock whose video tunnel did not come back after standby: it answers DDC/CI,
/// sleeps again after any wake-up, and only gets a signal when the topology is rebuilt while it is awake.
/// </summary>
public sealed class MonitorRecoveryTests
{
    private sealed class DockedMonitor : IMonitorControlBackend
    {
        public readonly DdcMonitor Monitor = new(@"\.\DISPLAY2", 0, "PHL 329M1RV", 2);
        public bool Signal;
        public bool Awake;
        public uint Power = 4;
        public bool AnswersDdc = true;
        public int Wakes;
        public IReadOnlyList<DdcMonitor> Enumerate() => [Monitor];
        public FeatureValue? Read(DdcMonitor monitor, MonitorFeature feature) => !AnswersDdc ? null : feature switch
        {
            MonitorFeature.Power => new(Signal ? 1u : Power, 4),
            MonitorFeature.Brightness => new(50, 100),
            _ => null,
        };
        public bool Write(DdcMonitor monitor, MonitorFeature feature, uint value)
        {
            if (feature != MonitorFeature.Power) return true;
            Awake = value == 1;
            if (Awake) Wakes++;
            if (!Signal && value != 1) Power = value;
            return true;
        }
    }

    private sealed class Topology(DockedMonitor monitor)
    {
        public ProjectionTopology Current = ProjectionTopology.Extend;
        public List<ProjectionTopology> Changes = [];
        public int FailNext;
        public void Set(ProjectionTopology topology)
        {
            if (topology != ProjectionTopology.Internal && FailNext-- > 0) throw new InvalidOperationException("SetDisplayConfig failed: 31");
            Changes.Add(topology);
            Current = topology;
            // The dock rebuilds the tunnel; the monitor sees it only if it is awake.
            if (topology != ProjectionTopology.Internal && monitor.Awake) monitor.Signal = true;
        }
    }

    private static (MonitorRecovery Recovery, ExternalMonitorService Service, DockedMonitor Monitor, Topology Topology) Create()
    {
        var monitor = new DockedMonitor();
        var service = new ExternalMonitorService(monitor);
        var topology = new Topology(monitor);
        // Measured: without a signal the monitor stays awake about two seconds, then sleeps again.
        var recovery = new MonitorRecovery(service, () => topology.Current, topology.Set,
            time => { if (!monitor.Signal && time >= TimeSpan.FromSeconds(2)) monitor.Awake = false; return Task.CompletedTask; });
        return (recovery, service, monitor, topology);
    }

    [Fact]
    public async Task RebuildsTheConnectionWhileTheMonitorIsAwake()
    {
        var (recovery, service, monitor, topology) = Create();
        Assert.Equal(MonitorRecoveryResult.Reconnected, await recovery.RecoverAsync(force: false));
        Assert.True(monitor.Signal);
        Assert.Equal([ProjectionTopology.Internal, ProjectionTopology.Extend], topology.Changes);
        Assert.Equal(ProjectionTopology.Extend, topology.Current);
        service.Dispose();
    }

    [Fact]
    public async Task RetriesTheTopologyThatFailsWhileTheInternalOneSettles()
    {
        var (recovery, _, monitor, topology) = Create();
        topology.FailNext = 1; // as measured: the first SetDisplayConfig back returned 31
        Assert.Equal(MonitorRecoveryResult.Reconnected, await recovery.RecoverAsync(force: false));
        Assert.Equal(ProjectionTopology.Extend, topology.Current);
        Assert.True(monitor.Signal);
    }

    [Fact]
    public async Task HealthyMonitorIsLeftAlone()
    {
        var (recovery, _, monitor, topology) = Create();
        monitor.Signal = true;
        Assert.Equal(MonitorRecoveryResult.NothingToDo, await recovery.RecoverAsync(force: false));
        Assert.Empty(topology.Changes);
        Assert.Equal(0, monitor.Wakes);
    }

    [Fact]
    public async Task MonitorSwitchedOffWithItsButtonIsLeftAlone()
    {
        var (recovery, _, monitor, topology) = Create();
        monitor.Power = 5;
        Assert.Equal(MonitorRecoveryResult.NothingToDo, await recovery.RecoverAsync(force: false));
        Assert.Empty(topology.Changes);
    }

    [Fact]
    public async Task StandbyRequestedThroughLlampecIsNotUndone()
    {
        var (recovery, service, monitor, topology) = Create();
        monitor.Signal = true;
        await service.RefreshAsync();
        await service.SetPowerAsync([monitor.Monitor], on: false);
        monitor.Signal = false; // the monitor sleeps and reports standby, as asked
        Assert.Equal(MonitorRecoveryResult.NothingToDo, await recovery.RecoverAsync(force: false));
        Assert.Empty(topology.Changes);
    }

    [Fact]
    public async Task ManualRecoveryRebuildsEvenWithoutADdcAnswer()
    {
        var (recovery, _, monitor, topology) = Create();
        monitor.AnswersDdc = false;
        await recovery.RecoverAsync(force: true);
        Assert.Equal([ProjectionTopology.Internal, ProjectionTopology.Extend], topology.Changes);
    }

    [Fact]
    public async Task NoSignalTileTapRunsTheRecovery()
    {
        var (recovery, service, monitor, _) = Create();
        using var action = new MonitorAction(service, () => [], recovery);
        await service.RefreshAsync();
        Assert.Equal("No signal", action.Subtitle);
        await action.ExecuteAsync(CancellationToken.None);
        Assert.True(monitor.Signal);
        Assert.Equal(ActionState.On, action.State);
    }
}
