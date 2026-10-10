using Llampec.Platform;

namespace Llampec.Actions.Projection;

/// <summary>
/// One of the four fixed display topologies (see <see cref="ProjectionTopology"/>), a sub-tile of
/// <see cref="ProjectionAction"/>. It shows the topology its parent read last, so opening the panel
/// queries Windows once rather than once per mode. Tapping it always applies its own topology -- it
/// behaves like a radio button, not a plain on/off switch.
/// </summary>
public sealed class ProjectionModeAction : QuickActionBase
{
    private readonly string _title;
    private readonly Func<ProjectionTopology?> _current;
    private readonly Action _applied;

    /// <param name="current">The parent's last reading; null when it cannot be read.</param>
    /// <param name="applied">Asks the parent to read the new topology.</param>
    public ProjectionModeAction(ProjectionTopology topology, string title, Func<ProjectionTopology?> current, Action applied)
    {
        Topology = topology;
        _title = title;
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _applied = applied ?? throw new ArgumentNullException(nameof(applied));
    }

    public ProjectionTopology Topology { get; }

    public override string Id => $"projection.{Topology}";
    public override string Title => _title;
    public override string Glyph => ""; // TVMonitor
    public override ActionKind Kind => ActionKind.Toggle;

    public override void Refresh()
    {
        var current = _current();
        IsAvailable = current is not null;
        State = current == Topology ? ActionState.On : ActionState.Off;
    }

    protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        try { ProjectionModes.Set(Topology); }
        finally { _applied(); }
        return Task.CompletedTask;
    }
}
