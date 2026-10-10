using Llampec.Platform;

namespace Llampec.Actions.Projection;

/// <summary>
/// "Multiple displays": the Win+P menu (Settings &gt; System &gt; Display's "Multiple displays" section) as a
/// tile. The sub-page lists the four fixed topologies Windows itself offers (PC screen only / Duplicate /
/// Extend / Second screen only); tapping the tile body cycles to the next one, the same as pressing the
/// physical Win+P key repeatedly without opening its own flyout. Built on <see cref="ProjectionModes"/>,
/// which calls the exact same SetDisplayConfig/QueryDisplayConfig the OS menu does -- no attempt to
/// reimplement topology switching per-monitor.
/// </summary>
public sealed class ProjectionAction : QuickActionBase
{
    private readonly List<ProjectionModeAction> _modes;
    private ProjectionTopology? _current;

    public ProjectionAction()
    {
        // A mode's tap re-reads the topology for every sibling (radio-button semantics: exactly one is On).
        ProjectionModeAction Mode(ProjectionTopology topology, string title) => new(topology, title, () => _current, Refresh);
        _modes =
        [
            Mode(ProjectionTopology.Internal, "PC screen only"),
            Mode(ProjectionTopology.Clone, "Duplicate"),
            Mode(ProjectionTopology.Extend, "Extend"),
            Mode(ProjectionTopology.External, "Second screen only"),
        ];
        Refresh();
    }

    public override string Id => "projection";
    public override string Title => "Multiple displays";
    public override string Glyph => ""; // Project
    public override ActionKind Kind => ActionKind.ToggleWithSubpage;
    public override IReadOnlyList<IQuickAction> SubActions => _modes;
    public override bool SubActionsAreExclusive => true;

    public override void Refresh()
    {
        _current = ProjectionModes.TryGetCurrent();
        foreach (var mode in _modes) mode.Refresh();
        var active = _modes.Find(m => m.State == ActionState.On);
        IsAvailable = _current is not null;
        State = active is not null ? ActionState.On : ActionState.Off;
        Subtitle = _current is null ? "Not currently available" : active?.Title;
    }

    protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        int index = _modes.FindIndex(m => m.State == ActionState.On);
        return _modes[(index + 1) % _modes.Count].ExecuteAsync(cancellationToken);
    }
}
