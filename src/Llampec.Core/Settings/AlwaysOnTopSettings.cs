namespace Llampec.Settings;

/// <summary>Appearance and shortcut preferences; pinned windows belong only to the current session.</summary>
public sealed class AlwaysOnTopSettings
{
    public const int MinimumBorderThickness = 1;
    public const int MaximumBorderThickness = 8;
    public const int DefaultBorderThickness = 3;
    public const string DefaultHotkey = "Ctrl+Alt+T";
    public const int MinimumOpacity = 30;
    public const int MaximumOpacity = 100;

    private int _borderThickness = DefaultBorderThickness;
    private int _opacity = MaximumOpacity;
    private string _hotkey = DefaultHotkey;
    private string? _borderColor;

    public bool ShowBorder { get; set; } = true;

    /// <summary>"#RRGGBB", or null for the Windows accent color. Invalid text falls back to the accent color.</summary>
    public string? BorderColor
    {
        get => _borderColor;
        set => _borderColor = TryParseColor(value, out uint rgb) ? FormatColor(rgb) : null;
    }

    /// <summary>The custom color as 0xRRGGBB, or null for the Windows accent color.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public uint? BorderRgb => TryParseColor(_borderColor, out uint rgb) ? rgb : null;

    /// <summary>Opacity of pinned windows in percent; 100 leaves them unchanged.</summary>
    public int Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, MinimumOpacity, MaximumOpacity);
    }

    /// <summary>The global appearance, used by every pinned window without its own.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Actions.AlwaysOnTop.WindowAppearance Appearance => new(ShowBorder, BorderThickness, BorderRgb, Opacity);

    public static bool TryParseColor(string? text, out uint rgb)
    {
        rgb = 0;
        string value = text?.Trim() ?? "";
        if (value.StartsWith('#')) value = value[1..];
        return value.Length == 6 && uint.TryParse(value, System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture, out rgb);
    }

    public static string FormatColor(uint rgb) => $"#{rgb & 0xFFFFFF:X6}";

    /// <summary>Logical pixels, scaled separately for each window's monitor.</summary>
    public int BorderThickness
    {
        get => _borderThickness;
        set => _borderThickness = Math.Clamp(value, MinimumBorderThickness, MaximumBorderThickness);
    }

    /// <summary>An empty value disables the shortcut.</summary>
    public string Hotkey
    {
        get => _hotkey;
        set => _hotkey = value is not null && string.IsNullOrWhiteSpace(value) ? string.Empty
            : Platform.Hotkey.TryParse(value, out var parsed) ? parsed.ToString() : DefaultHotkey;
    }
}
