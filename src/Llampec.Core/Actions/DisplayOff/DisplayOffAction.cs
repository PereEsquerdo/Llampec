using Llampec.Interop;
using Llampec.Platform;

namespace Llampec.Actions.DisplayOff;

/// <summary>
/// "Turn off displays": puts every monitor into power-off mode with WM_SYSCOMMAND / SC_MONITORPOWER.
/// The message is posted to Llampec's own message window (DefWindowProc does the work), so nothing is
/// broadcast to other applications. Any key press or mouse movement wakes the displays again. On Modern
/// Standby devices Windows treats this as entering standby: programs pause and it may ask for sign-in.
/// <para>
/// The sub-page turns off a single display instead, which neither enters standby nor locks because
/// another display stays on: the built-in panel through Second screen only, a DDC/CI monitor through
/// its own standby (see <see cref="DisplayPower"/>).
/// </para>
/// </summary>
public sealed class DisplayOffAction(SystemEvents systemEvents) : QuickActionBase
{
    /// <summary>
    /// Time for the panel to hide and the user's hand to leave the mouse. Without it the click that
    /// pressed the tile, or the panel's fade-out, wakes the display straight away.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    private ProjectionTopology? _beforeBuiltInOff;

    public override string Id => "display-off";
    public override string Title => "Turn off displays";
    public override string Glyph => ""; // TVMonitor
    // The WinUI view draws a single monitor/power symbol with the switch inside the screen.
    // Overlaying two font glyphs at this size obscures both silhouettes.
    public override ActionKind Kind => ActionKind.ButtonWithSubpage;

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        TurnOff(systemEvents.Handle);
    }

    /// <summary>Removes the built-in panel from the desktop (Second screen only), remembering the topology to return to.</summary>
    public void TurnOffBuiltIn()
    {
        var current = ProjectionModes.TryGetCurrent();
        ProjectionModes.Set(ProjectionTopology.External);
        if (current is { } topology && topology != ProjectionTopology.External) _beforeBuiltInOff = topology;
    }

    /// <summary>Returns to the topology before <see cref="TurnOffBuiltIn"/>, or Extend.</summary>
    public void TurnOnBuiltIn()
    {
        ProjectionModes.Set(_beforeBuiltInOff ?? ProjectionTopology.Extend);
        _beforeBuiltInOff = null;
    }

    /// <summary>Sends SC_MONITORPOWER / MONITOR_OFF to <paramref name="hWnd"/>.</summary>
    public static void TurnOff(nint hWnd)
    {
        // PostMessage is thread-safe (we are on the thread pool) and DefWindowProc handles the command.
        if (!User32.PostMessage(hWnd, User32.WM_SYSCOMMAND, (nuint)User32.SC_MONITORPOWER, User32.MONITOR_OFF))
        {
            throw new InvalidOperationException($"PostMessage(SC_MONITORPOWER) failed: {Marshal.GetLastPInvokeError()}");
        }
    }
}
