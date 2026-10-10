using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Llampec.Core.Tests")]
[assembly: InternalsVisibleTo("Llampec.AlwaysOnTop.Smoke")]

namespace Llampec.Actions.AlwaysOnTop;

internal readonly record struct WindowIdentity(nint Handle, uint ProcessId, uint ThreadId);
internal sealed record WindowSnapshot(WindowIdentity Identity, string Title, bool IsEligible, bool IsTopmost);
internal readonly record struct ForegroundWindowResolution(WindowSnapshot? Target, bool IsOwnWindow, string? Reason = null);
internal readonly record struct WindowChange(nint Handle, bool Foreground, bool PositionOnly = false);
/// <summary>A window's layered state before Llampec first changed its opacity.</summary>
internal sealed record OpacityRestore(bool WasLayered, uint ColorKey, byte Alpha, uint Flags);

/// <summary>The ownership marker is also checked, since Windows can reuse a handle in the same thread.</summary>
internal interface IAlwaysOnTopWindowSystem : IDisposable
{
    event Action<WindowChange>? WindowChanged;
    bool IsTrackingForeground { get; }
    /// <summary>
    /// Foreground changes are always reported. Other window events are reported for every
    /// window while <paramref name="all"/> (the panel shows them), otherwise only for the
    /// processes of <paramref name="pinned"/> windows (plus window destruction), so an idle
    /// Llampec is not woken by every window, move and caret in the session.
    /// </summary>
    void SetWindowTracking(bool all, IReadOnlyCollection<WindowIdentity> pinned) { }
    /// <summary>A cheap check that a window still has this identity and is topmost.</summary>
    bool IsTopmost(WindowIdentity identity) => Read(identity.Handle) is { IsTopmost: true } window && window.Identity == identity;
    string? LastError => null;
    nint ForegroundWindow { get; }
    ForegroundWindowResolution ResolveForeground(nint handle);
    WindowSnapshot? Read(nint handle);
    IReadOnlyList<WindowSnapshot> Enumerate();
    bool TryClaim(WindowIdentity identity);
    bool HasClaim(WindowIdentity identity);
    void ReleaseClaim(WindowIdentity identity);
    bool TrySetTopmost(WindowIdentity identity, bool topmost);
    /// <summary>
    /// Applies a claimed window's alpha. The first call records the original layered state in
    /// <paramref name="original"/>; pass it back unchanged until it is restored.
    /// </summary>
    bool TrySetOpacity(WindowIdentity identity, byte alpha, ref OpacityRestore? original) => false;
    /// <summary>Restores the recorded state. A closed or recycled window needs nothing.</summary>
    bool TryRestoreOpacity(WindowIdentity identity, OpacityRestore original) => true;
}
