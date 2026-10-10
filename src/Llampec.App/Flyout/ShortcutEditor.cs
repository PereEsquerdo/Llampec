using Llampec.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Llampec.Flyout;

/// <summary>A module's global shortcut: a heading, the text box, a note, Apply and the outcome.</summary>
internal sealed class ShortcutEditor : StackPanel
{
    /// <summary>Reserves and saves a shortcut, returning false with an error when it cannot.</summary>
    public delegate bool TrySet(string text, out string? error);

    private readonly TextBox _shortcut;
    private readonly TextBlock _status = Note("");
    private readonly TrySet _trySet;
    private readonly Func<string> _saved;

    /// <param name="saved">The normalized shortcut as saved, shown after applying.</param>
    /// <param name="error">A registration error from startup, if any.</param>
    public ShortcutEditor(string placeholder, string note, Func<string> saved, TrySet trySet, string? error)
    {
        Spacing = 10;
        Margin = new Thickness(0, 6, 0, 0);
        _saved = saved;
        _trySet = trySet;
        _shortcut = new TextBox { PlaceholderText = placeholder, Text = saved() };
        AutomationProperties.SetName(_shortcut, T("Shortcut"));
        _shortcut.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            Apply();
            e.Handled = true;
        };
        var apply = new Button { Content = T("Apply"), HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Click += (_, _) => Apply();
        Children.Add(Section("Shortcut"));
        Children.Add(_shortcut);
        Children.Add(Note(note));
        Children.Add(apply);
        Children.Add(_status);
        SetMessage(_status, error);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
    }

    private void Apply()
    {
        string text = _shortcut.Text.Trim();
        if (text.Length != 0 && !Hotkey.TryParse(text, out _))
        {
            SetMessage(_status, "Enter a valid shortcut or leave it empty to disable it.");
            return;
        }
        if (!_trySet(text, out string? error))
        {
            SetMessage(_status, error ?? "Could not save settings.");
            return;
        }
        _shortcut.Text = _saved();
        SetMessage(_status, text.Length == 0 ? "Shortcut disabled." : "Shortcut saved.");
    }
}
