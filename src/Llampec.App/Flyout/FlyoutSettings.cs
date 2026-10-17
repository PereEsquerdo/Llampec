using System.Reflection;
using Llampec.Platform;
using Llampec.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Llampec.Flyout;

public sealed partial class FlyoutWindow
{
    private enum UtilityPage { None, Settings, Editor }
    private UtilityPage _utilityPage;



    private void UpdateLanguage()
    {
        Root.Language = UiText.Language;
        AutomationProperties.SetName(BackButton, T("Back"));
        AutomationProperties.SetName(MenuButton, T("Settings"));
        AutomationProperties.SetName(EditButton, T("Edit panel"));
        AutomationProperties.SetName(FooterBrand, T("About Llampec"));
        ToolTipService.SetToolTip(FooterBrand, T("About Llampec"));
        SaveEditorButton.Content = T("Save");
        CancelEditorButton.Content = T("Cancel");
    }

    private void ReleaseSubpage()
    {
        ReleasePanelEditor();
        foreach (var unsubscribe in _subUnsubscribe) unsubscribe();
        _subUnsubscribe.Clear();
        _subpageView?.Dispose();
        _subpageView = null;
        Subpage.Children.Clear();
    }

    private void RebuildTiles(int? columns = null)
    {
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();
        Tiles.Children.Clear(); Tiles.ColumnDefinitions.Clear(); Tiles.RowDefinitions.Clear();
        BuildTiles(columns);
    }

    private void ShowMainPage()
    {
        _utilityPage = UtilityPage.None;
        if (_model.Subpage is not null) _model.CloseSubpage();
        else ApplyPage();
    }

    private void BeginUtilityPage(UtilityPage page, string title)
    {
        _model.CloseSubpage();
        ReleaseSubpage();
        _utilityPage = page;
        EditButton.Visibility = Visibility.Collapsed;
        PageHeader.Visibility = BackButton.Visibility = Subpage.Visibility = Visibility.Visible;
        Tiles.Visibility = Visibility.Collapsed;
        Heading.Text = T(title);
        ErrorBar.IsOpen = false;
    }

    private void GoBack()
    {
        if (_utilityPage == UtilityPage.Editor) ShowSettings();
        else if (_utilityPage == UtilityPage.Settings) ShowMainPage();
        else _model.CloseSubpage();
    }


    private void ShowSettings()
    {
        BeginUtilityPage(UtilityPage.Settings, "Settings");
        var language = new ComboBox { Header = T("Language"), HorizontalAlignment = HorizontalAlignment.Stretch };
        string?[] tags = [null, "es", "ca", "en"];
        language.Items.Add(T("Use Windows language"));
        language.Items.Add("Español (ESP)"); language.Items.Add("Català / Valencià (CAT/VAL)"); language.Items.Add("English (ENG)");
        language.SelectedIndex = App.Current.Settings.Language is { } savedLanguage
            ? Array.IndexOf(tags, UiText.ResolveLanguage(savedLanguage)) : 0;
        bool changingLanguage = false;
        language.SelectionChanged += (_, _) =>
        {
            if (changingLanguage) return;
            int index = language.SelectedIndex;
            if (index < 0) return;
            var settings = App.Current.Settings;
            string? previous = settings.Language;
            settings.Language = tags[index];
            if (!SettingsStore.Save(settings))
            {
                settings.Language = previous;
                changingLanguage = true;
                language.SelectedIndex = Math.Max(0, Array.IndexOf(tags, previous));
                changingLanguage = false;
                ErrorBar.Message = T("Could not save settings. Check access to the Llampec settings folder.");
                ErrorBar.IsOpen = true;
                return;
            }
            UiText.SetLanguage(settings.Language);
            UpdateLanguage(); RebuildTiles();
            // Recreate this small page immediately, including its accessible names.
            ShowSettings();
        };
        Subpage.Children.Add(language);

        var startup = new StartupRegistration(Environment.ProcessPath!);
        var start = new ToggleSwitch { Header = T("Start with Windows"), OnContent = T("On"), OffContent = T("Off"), Margin = new Thickness(0, 12, 0, 0) };
        try { start.IsOn = startup.IsRegistered; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        { start.IsEnabled = false; ErrorBar.Message = T("Could not update startup registration."); ErrorBar.IsOpen = true; }
        bool updating = false;
        start.Toggled += (_, _) =>
        {
            if (updating) return;
            try { startup.SetRegistered(start.IsOn); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
            {
                updating = true; start.IsOn = !start.IsOn; updating = false;
                ErrorBar.Message = T("Could not update startup registration.") + " " + ex.Message; ErrorBar.IsOpen = true;
            }
        };
        Subpage.Children.Add(start);
        Subpage.Children.Add(Note("Opens in the notification area when you sign in.", 0.75));
        Subpage.Children.Add(Note("Windows can also disable startup. After moving Llampec, open it once to update its startup location.", 0.75));
        var windowsStartup = new HyperlinkButton { Content = T("Windows startup apps"), Padding = new Thickness(0) };
        windowsStartup.Click += async (_, _) =>
        {
            if (await OpenSettingsAsync("ms-settings:startupapps")) return;
            ErrorBar.Message = T("Could not open Windows Settings.");
            ErrorBar.IsOpen = true;
        };
        Subpage.Children.Add(windowsStartup);
        var reorder = new Button { Content = T("Buttons and categories"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) };
        reorder.Click += (_, _) => ShowPanelEditor();
        Subpage.Children.Add(reorder);
        var about = new Button { Content = T("About Llampec"), HorizontalAlignment = HorizontalAlignment.Stretch };
        about.Click += (_, _) => ShowAbout();
        Subpage.Children.Add(about);
        Reposition();
    }

    private void OnFooterBrand(object sender, RoutedEventArgs e) => ShowAbout();

    /// <summary>The width of an About bullet's emoji column; its sub-items are indented by the same amount.</summary>
    private const double AboutBulletIndent = 30;

    /// <summary>An emoji followed by bold text, or a bold link.</summary>
    private static Grid AboutBullet(string emoji, FrameworkElement text)
    {
        var row = new Grid { ColumnSpacing = 0 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AboutBulletIndent) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = emoji, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        if (text is TextBlock block) { block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; block.TextWrapping = TextWrapping.Wrap; }
        else if (text is Control control) control.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

private async void ShowAbout()
    {
        var assembly = typeof(App).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "";
        string copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? "Copyright (c) 2026 Pere Esquerdo Ramis";
        Hold(true);
        try
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = $"{T("Version")} {version}\n{copyright}\n{T("Open-source software · MIT License")}",
                TextWrapping = TextWrapping.Wrap,
            });
            // Three bullets, each a colour emoji and bold text. The dedication is the same in every language.
            content.Children.Add(AboutBullet("\U0001FAF6", new TextBlock { Text = "Dedicat a Lara i a Miquel" }));
            content.Children.Add(AboutBullet("\U0001F4BB", new HyperlinkButton
            {
                // The repository's page also leads to its releases.
                Content = T("Code and updates on GitHub"), NavigateUri = new Uri("https://github.com/PereEsquerdo/Llampec"),
                Padding = new Thickness(0),
            }));
            var thanks = new StackPanel { Spacing = 4, Margin = new Thickness(AboutBulletIndent, 0, 0, 0) };
            void Credit(string note, string link, string uri)
            {
                thanks.Children.Add(Note(note, 0.75));
                thanks.Children.Add(new HyperlinkButton { Content = link, NavigateUri = new Uri(uri), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 6) });
            }
            Credit("Always on Top inspired by Microsoft PowerToys.", "Microsoft PowerToys · MIT", "https://github.com/microsoft/PowerToys");
            Credit("Logitech HID++ feature code adapted from Mouser.", "Mouser · MIT", "https://github.com/TomBadash/Mouser");
            Credit("Logo typeface: Outfit.", "Outfit · OFL", "https://github.com/Outfitio/Outfit-Fonts");
            var acknowledgements = new StackPanel { Spacing = 8 };
            acknowledgements.Children.Add(AboutBullet("\U0001F91D", new TextBlock { Text = T("Acknowledgements") }));
            acknowledgements.Children.Add(thanks);
            content.Children.Add(acknowledgements);
            // The logo as the title; the dialog keeps its accessible name.
            var logo = new ContentControl
            {
                ContentTemplate = (DataTemplate)Root.Resources["BrandLogoTemplate"], Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left, IsTabStop = false, Margin = new Thickness(0, 4, 0, 4),
            };
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
                Title = logo, Content = content, CloseButtonText = T("Close") };
            AutomationProperties.SetName(dialog, T("About Llampec"));
            await dialog.ShowAsync();
        }
        finally { Hold(false); }
    }
}
