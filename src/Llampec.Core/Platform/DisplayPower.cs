namespace Llampec.Platform;

/// <summary>One row of "turn off one display": the built-in panel or a DDC/CI monitor.</summary>
/// <param name="CanToggle">False when turning it off would leave no display lit, so nothing could turn it back on.</param>
public sealed record DisplayPowerRow(string Name, bool Internal, bool On, bool CanToggle, DdcMonitor? Monitor);

/// <summary>
/// Turning off a single display without standby or lock: the built-in panel leaves the desktop
/// (Second screen only) and a DDC/CI monitor enters its own standby. At least one display stays lit.
/// </summary>
public static class DisplayPower
{
    public static IReadOnlyList<DisplayPowerRow> Rows(IReadOnlyList<ActiveDisplay> displays, ProjectionTopology? topology,
        IReadOnlyList<MonitorStatus> monitors)
    {
        var standby = monitors.Where(m => m.PoweredOn == false).Select(m => m.Monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lit = displays.Where(d => !standby.Contains(d.DeviceName)).ToArray();
        var rows = new List<DisplayPowerRow>();
        var builtIn = displays.FirstOrDefault(d => d.Internal);
        if (builtIn is not null)
            rows.Add(new(builtIn.Name, true, true, lit.Any(d => d != builtIn), null));
        else if (topology == ProjectionTopology.External && displays.Count > 0)
            rows.Add(new("Built-in display", true, false, true, null)); // off through Second screen only
        foreach (var status in monitors)
        {
            bool on = status.PoweredOn != false;
            bool othersLit = lit.Any(d => !string.Equals(d.DeviceName, status.Monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            rows.Add(new(status.Monitor.Name, false, on, !on || othersLit, status.Monitor));
        }
        return rows;
    }

    /// <summary>True when some active display stays lit after putting <paramref name="monitors"/> into standby.</summary>
    public static bool LeavesALitDisplay(IReadOnlyList<ActiveDisplay> displays, IEnumerable<DdcMonitor> monitors, IReadOnlyList<MonitorStatus> known)
    {
        var dark = known.Where(m => m.PoweredOn == false).Select(m => m.Monitor.DeviceName)
            .Concat(monitors.Select(m => m.DeviceName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return displays.Any(d => !dark.Contains(d.DeviceName));
    }
}
