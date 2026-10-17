using System.Text.Json;
using System.Text.Json.Serialization;
using Llampec.Platform;
using Llampec.Settings;

namespace Llampec.Actions.AgentMode;

/// <summary>What agent mode changed, saved before the changes so a later start can undo them after a crash.</summary>
/// <param name="HideTaskbar">Auto-hide was turned on for the covers and must be turned off again.</param>
public sealed record AgentModeRecovery(int Version, ProjectionTopology? Topology, PowerMode? Mode, IReadOnlyList<string> Monitors,
    bool HideTaskbar = false);

/// <summary>The Windows features agent mode combines. Screen methods are called on the UI thread.</summary>
public interface IAgentModePlatform
{
    IDisposable KeepAwake(bool keepDisplayOn);
    IReadOnlyList<ActiveDisplay> Displays();
    ProjectionTopology? Topology();
    void SetTopology(ProjectionTopology topology);
    /// <summary>Null when Windows offers no power modes (another plan, or the overlay functions are missing).</summary>
    PowerMode? Mode();
    void SetMode(PowerMode mode);
    Task<IReadOnlyList<MonitorStatus>> MonitorsAsync();
    Task<IReadOnlyList<DdcMonitor>> SetMonitorPowerAsync(IEnumerable<DdcMonitor> monitors, bool on);
    IAgentScreen CreateScreen();
    /// <summary>Whether the taskbar hides automatically; null with the tablet taskbar, which ignores it.</summary>
    bool? TaskbarAutoHide();
    void SetTaskbarAutoHide(bool hide);
    AgentModeRecovery? LoadRecovery();
    void SaveRecovery(AgentModeRecovery recovery);
    void ClearRecovery();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentModeRecovery))]
internal sealed partial class AgentModeRecoveryJsonContext : JsonSerializerContext;

public sealed class AgentModePlatform(ExternalMonitorService monitors) : IAgentModePlatform
{
    private static string RecoveryPath => Path.Combine(SettingsStore.Directory, "agent-mode-recovery.json");

    public IDisposable KeepAwake(bool keepDisplayOn) => CaffeineRequest.Create(keepDisplayOn, "Agentic work");
    public IReadOnlyList<ActiveDisplay> Displays() => ActiveDisplays.Read();
    public ProjectionTopology? Topology() => ProjectionModes.TryGetCurrent();
    public void SetTopology(ProjectionTopology topology) => ProjectionModes.Set(topology);

    public PowerMode? Mode()
    {
        var power = PowerOptions.Read();
        return power.ModeSupported ? power.Mode : null;
    }

    public void SetMode(PowerMode mode) => PowerOptions.SetMode(mode);

    public async Task<IReadOnlyList<MonitorStatus>> MonitorsAsync()
    {
        await monitors.RefreshAsync().ConfigureAwait(false);
        return monitors.Monitors;
    }

    public Task<IReadOnlyList<DdcMonitor>> SetMonitorPowerAsync(IEnumerable<DdcMonitor> targets, bool on) => monitors.SetPowerAsync(targets, on);
    /// <summary>A minute without movement: long enough not to move the pointer while an agent is mid-task.</summary>
    public IAgentScreen CreateScreen() => new AgentScreen(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));

    public bool? TaskbarAutoHide() => TabletTaskbar.ReadMode() == TaskbarMode.Normal
        ? (Actions.Taskbar.TaskbarAutoHideAction.GetState() & Interop.Shell32.ABS_AUTOHIDE) != 0 : null;

    public void SetTaskbarAutoHide(bool hide)
    {
        uint state = Actions.Taskbar.TaskbarAutoHideAction.GetState();
        Actions.Taskbar.TaskbarAutoHideAction.SetState(hide ? state | Interop.Shell32.ABS_AUTOHIDE : state & ~Interop.Shell32.ABS_AUTOHIDE);
    }

    public AgentModeRecovery? LoadRecovery()
    {
        if (!File.Exists(RecoveryPath)) return null;
        using var stream = File.OpenRead(RecoveryPath);
        var recovery = JsonSerializer.Deserialize(stream, AgentModeRecoveryJsonContext.Default.AgentModeRecovery);
        return recovery is { Version: 1, Monitors: not null } ? recovery : null;
    }

    public void SaveRecovery(AgentModeRecovery recovery)
    {
        Directory.CreateDirectory(SettingsStore.Directory);
        string temporary = RecoveryPath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            JsonSerializer.Serialize(stream, recovery, AgentModeRecoveryJsonContext.Default.AgentModeRecovery);
        File.Move(temporary, RecoveryPath, overwrite: true);
    }

    public void ClearRecovery() => File.Delete(RecoveryPath);
}
