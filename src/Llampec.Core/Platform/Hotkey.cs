using Llampec.Interop;

namespace Llampec.Platform;

/// <summary>A global hotkey such as "Ctrl+Alt+Space", parsed from / formatted to its settings string.</summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint mods = 0;
        uint vk = 0;
        foreach (string rawPart in text.Split('+', StringSplitOptions.TrimEntries))
        {
            uint modifier = rawPart.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => User32.MOD_CONTROL,
                "ALT" => User32.MOD_ALT,
                "SHIFT" => User32.MOD_SHIFT,
                "WIN" or "WINDOWS" => User32.MOD_WIN,
                _ => 0,
            };
            if (modifier != 0)
            {
                if ((mods & modifier) != 0) return false;
                mods |= modifier;
            }
            else if (vk != 0 || !KeyNames.TryParse(rawPart, out ushort key)) return false;
            else vk = key;
        }

        if (vk == 0 || mods == 0)
        {
            return false; // a global hotkey without modifiers would steal ordinary typing
        }

        hotkey = new Hotkey(mods, vk);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & User32.MOD_WIN) != 0) parts.Add("Win");
        if ((Modifiers & User32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & User32.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & User32.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyNames.Format(VirtualKey));
        return string.Join('+', parts);
    }
}
