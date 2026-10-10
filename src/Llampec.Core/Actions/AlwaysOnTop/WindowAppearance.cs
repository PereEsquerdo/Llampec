using Llampec.Settings;

namespace Llampec.Actions.AlwaysOnTop;

/// <summary>
/// How one pinned window looks. The global settings supply it unless the window has its own,
/// which lasts only as long as the pin.
/// </summary>
/// <param name="BorderColor">0xRRGGBB, or null for the Windows accent color.</param>
/// <param name="Opacity">Percent; 100 leaves the window opaque and unchanged.</param>
public sealed record WindowAppearance(bool ShowBorder, int BorderThickness, uint? BorderColor, int Opacity)
{
    public int BorderThickness { get; init; } = Math.Clamp(BorderThickness,
        AlwaysOnTopSettings.MinimumBorderThickness, AlwaysOnTopSettings.MaximumBorderThickness);
    public int Opacity { get; init; } = Math.Clamp(Opacity, AlwaysOnTopSettings.MinimumOpacity, AlwaysOnTopSettings.MaximumOpacity);
    public uint? BorderColor { get; init; } = BorderColor & 0xFFFFFF;

    /// <summary>Layered-window alpha, 0–255.</summary>
    public byte Alpha => (byte)Math.Round(Opacity * 255 / 100.0);
}

/// <summary>A pinned window and its own appearance, if it has one.</summary>
public sealed record PinnedWindowAppearance(nint Handle, string Title, WindowAppearance? Appearance);
