namespace Llampec.Settings;

/// <summary>What agent mode changes while it is active. Nothing here survives the session itself.</summary>
public sealed class AgentModeSettings
{
    /// <summary>Removes the built-in display from the desktop while another display remains (Second screen only).</summary>
    public bool TurnOffBuiltInDisplay { get; set; } = true;

    /// <summary>Puts DDC/CI monitors into their own standby while Windows keeps them in the desktop.</summary>
    public bool MonitorStandby { get; set; } = true;

    /// <summary>Covers displays that remain lit with black, excluded from screen capture.</summary>
    public bool CoverDisplays { get; set; } = true;

    /// <summary>
    /// Keeps the display on for Windows, so the session neither enters standby nor locks. Off lets
    /// Windows turn the screens off after its timeout: lower consumption, but Windows locks the session.
    /// </summary>
    public bool KeepUnlocked { get; set; } = true;

    /// <summary>Selects the best power efficiency mode while active and restores the previous mode afterwards.</summary>
    public bool BestEfficiency { get; set; }

    public AgentModeSettings Clone() => (AgentModeSettings)MemberwiseClone();
}
