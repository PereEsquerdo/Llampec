using System.Globalization;

namespace Llampec.Platform;

/// <summary>
/// Names of the non-modifier keys accepted in shortcut text, shared by the global hotkeys and the
/// mouse-button shortcuts so both accept the same keys. Letters, digits and F1–F24 are named by
/// themselves; matching ignores case.
/// </summary>
internal static class KeyNames
{
    private static readonly (string Name, ushort Key)[] Named =
    [
        ("Space", 0x20), ("Tab", 0x09), ("Enter", 0x0D), ("Esc", 0x1B), ("Backspace", 0x08),
        ("Home", 0x24), ("End", 0x23), ("Insert", 0x2D), ("Delete", 0x2E), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
        ("VolumeUp", 0xAF), ("VolumeDown", 0xAE), ("VolumeMute", 0xAD),
        ("MediaPlayPause", 0xB3), ("MediaNext", 0xB0), ("MediaPrevious", 0xB1),
    ];

    private static readonly Dictionary<string, ushort> Keys = BuildKeys();

    private static Dictionary<string, ushort> BuildKeys()
    {
        var keys = Named.ToDictionary(entry => entry.Name, entry => entry.Key, StringComparer.OrdinalIgnoreCase);
        keys["Return"] = 0x0D; keys["Escape"] = 0x1B; keys["Ins"] = 0x2D; keys["Del"] = 0x2E;
        return keys;
    }

    public static bool TryParse(string name, out ushort key)
    {
        if (Keys.TryGetValue(name, out key)) return true;
        if (name.Length == 1 && (char.IsAsciiLetter(name[0]) || char.IsAsciiDigit(name[0])))
        {
            key = char.ToUpperInvariant(name[0]); // VK codes for 0-9 and A-Z equal their ASCII values
            return true;
        }
        if (name.Length is 2 or 3 && name[0] is 'F' or 'f'
            && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + f - 1);
            return true;
        }
        key = 0;
        return false;
    }

    public static string Format(uint key)
    {
        foreach (var (name, value) in Named)
            if (value == key) return name;
        return key switch
        {
            >= 0x70 and <= 0x87 => "F" + (key - 0x70 + 1).ToString(CultureInfo.InvariantCulture),
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
            _ => "0x" + key.ToString("X", CultureInfo.InvariantCulture),
        };
    }
}
