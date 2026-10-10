namespace Llampec.Actions.Touch;

/// <summary>One taskbar-icon choice in a touch tool's sub-page; the parent re-reads all choices afterwards.</summary>
public sealed class TaskbarIconOption : QuickActionBase
{
    private readonly Func<bool> _isSelected;
    private readonly Func<bool> _select;
    private readonly Action<bool> _completed;

    /// <param name="select">Applies the choice and returns whether the read-back confirmed it.</param>
    /// <param name="completed">Receives that confirmation.</param>
    public TaskbarIconOption(string id, string title, string glyph, Func<bool> isSelected, Func<bool> select, Action<bool> completed)
    {
        Id = id;
        Title = title;
        Glyph = glyph;
        _isSelected = isSelected ?? throw new ArgumentNullException(nameof(isSelected));
        _select = select ?? throw new ArgumentNullException(nameof(select));
        _completed = completed ?? throw new ArgumentNullException(nameof(completed));
    }

    public override string Id { get; }
    public override string Title { get; }
    public override string Glyph { get; }
    public override ActionKind Kind => ActionKind.Toggle;

    public override void Refresh() => State = _isSelected() ? ActionState.On : ActionState.Off;

    protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _completed(_select());
        return Task.CompletedTask;
    }
}
