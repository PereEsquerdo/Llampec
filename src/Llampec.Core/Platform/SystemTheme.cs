using Llampec.Interop;
using Microsoft.Win32;

namespace Llampec.Platform;

/// <summary>Reads the current Windows theme (registry) and accent colour (UISettings, with a DWM fallback).</summary>
public static class SystemTheme
{
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsUseLightThemeValue = "AppsUseLightTheme";
    public const string SystemUsesLightThemeValue = "SystemUsesLightTheme";

    /// <summary>True when apps should use the light theme (the value the native Quick Settings panel follows).</summary>
    public static bool IsAppsLightTheme() => IsLight(AppsUseLightThemeValue);

    public static bool IsSystemLightTheme() => IsLight(SystemUsesLightThemeValue);

    /// <summary>A missing or unreadable value means light, the Windows default.</summary>
    private static bool IsLight(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(valueName) is not int v || v != 0;
    }

    /// <summary>
    /// Sets both the "apps" and "system" colour mode to light or dark -- the same two registry values
    /// Settings &gt; Personalization &gt; Colors' Light/Dark switch writes ("Custom" mode, where the two
    /// differ, is not exposed here) -- then broadcasts WM_SETTINGCHANGE the same way Settings does after an
    /// edit there, so Explorer and every theme-aware app (Llampec's own panel included, through
    /// <see cref="SystemEvents"/>) repaints immediately instead of waiting for the next unrelated broadcast.
    /// </summary>
    public static void SetLightTheme(bool light)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: true)
            ?? throw new InvalidOperationException($@"Registry key HKCU\{PersonalizeKey} not found");

        int value = light ? 1 : 0;
        key.SetValue(AppsUseLightThemeValue, value, RegistryValueKind.DWord);
        key.SetValue(SystemUsesLightThemeValue, value, RegistryValueKind.DWord);

        // SendMessageTimeout, not SendMessage: a hung top-level window elsewhere on the desktop can't
        // block this call forever, and nothing here needs the reply.
        User32.SendMessageTimeout(User32.HWND_BROADCAST, User32.WM_SETTINGCHANGE, 0,
            "ImmersiveColorSet", User32.SMTO_ABORTIFHUNG, 3000, out _);
    }

    /// <summary>
    /// The Windows accent color as 0xRRGGBB: the documented UISettings Accent value that apps use
    /// (SystemAccentColor). DWM's colorization and the DWM AccentColor registry value are darker
    /// shades meant for window frames. Falls back to DWM colorization, then Windows' default blue.
    /// </summary>
    public static uint GetAccentColorRgb()
    {
        try
        {
            var accent = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return ((uint)accent.R << 16) | ((uint)accent.G << 8) | accent.B;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeInitializationException)
        {
            Diagnostics.Log.Warn($"Could not read the accent color: {ex.Message}");
        }
        return Dwm.DwmGetColorizationColor(out uint argb, out _) == 0 ? argb & 0xFFFFFF : 0x0078D4;
    }
}
