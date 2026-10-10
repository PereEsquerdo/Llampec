using Llampec.Diagnostics;
using Llampec.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Llampec.Flyout;

/// <summary>Small helpers shared by the panel's pages (imported statically by the app project).</summary>
internal static class PanelControls
{
    /// <summary>The translation of an English UI string.</summary>
    public static string T(string key) => UiText.Get(key);

    public static TextBlock Note(string key, double opacity = 1) => new()
    {
        Text = T(key), FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = opacity,
    };

    public static TextBlock Section(string key, double topMargin = 0) => new()
    {
        Text = T(key), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, topMargin, 0, 0),
    };

    /// <summary>Shows a translated message, or hides the line when there is none.</summary>
    public static void SetMessage(TextBlock control, string? key)
    {
        control.Text = string.IsNullOrEmpty(key) ? "" : T(key);
        control.Visibility = string.IsNullOrEmpty(key) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Opens a Windows Settings page; false (and logged) when Windows refused or failed.</summary>
    public static async Task<bool> OpenSettingsAsync(string uri)
    {
        try
        {
            if (await Windows.System.Launcher.LaunchUriAsync(new Uri(uri))) return true;
            Log.Warn($"Windows did not open {uri}.");
        }
        catch (Exception error) { Log.Warn($"Could not open {uri}: {error.Message}"); }
        return false;
    }

    /// <summary>Runs <paramref name="action"/> now on the UI thread, or queues it there from another thread.</summary>
    public static void OnUi(DispatcherQueue dispatcher, Action action)
    {
        if (dispatcher.HasThreadAccess) action();
        else dispatcher.TryEnqueue(() => action());
    }
}
