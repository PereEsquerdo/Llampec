namespace Llampec.Actions.AlwaysOnTop;

/// <summary>A nonactivating, click-through indicator owned by one pin.</summary>
public interface IAlwaysOnTopBorder : IDisposable
{
    /// <param name="color">0xRRGGBB, or null for the Windows accent color.</param>
    void Update(int thickness, uint? color);
}
