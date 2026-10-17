namespace Llampec.Settings;

public sealed class MonitorSettings
{
    /// <summary>
    /// When Windows turns the screen back on, check once whether a DDC/CI monitor stayed in no-signal
    /// standby and bring it back (see <see cref="Platform.MonitorRecovery"/>).
    /// </summary>
    public bool RecoverAfterWake { get; set; } = true;

    public MonitorSettings Clone() => (MonitorSettings)MemberwiseClone();
}
