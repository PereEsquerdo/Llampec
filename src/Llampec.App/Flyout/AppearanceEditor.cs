using Llampec.Actions.AlwaysOnTop;
using Llampec.Platform;
using Llampec.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Llampec.Flyout;

/// <summary>
/// Border, thickness, color and opacity of pinned windows, used for the global appearance and for
/// each window's own. It only reports changes; the owner decides where they are saved or applied.
/// </summary>
internal sealed class AppearanceEditor : StackPanel
{
    private readonly ToggleSwitch _border = new()
    {
        Header = UiText.Get("Show border"), OnContent = UiText.Get("On"), OffContent = UiText.Get("Off"),
    };
    private readonly NumberBox _thickness = new()
    {
        Header = UiText.Get("Border thickness"), Minimum = AlwaysOnTopSettings.MinimumBorderThickness,
        Maximum = AlwaysOnTopSettings.MaximumBorderThickness, SmallChange = 1, LargeChange = 1,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly ComboBox _color = new()
    {
        Header = UiText.Get("Border color"), HorizontalAlignment = HorizontalAlignment.Stretch,
        Items = { UiText.Get("Windows accent color"), UiText.Get("Custom color") },
    };
    private readonly ColorPicker _picker = new()
    {
        IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorChannelTextInputVisible = false,
        IsHexInputVisible = true, IsColorPreviewVisible = false, ColorSpectrumShape = ColorSpectrumShape.Box,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly Slider _opacity = new()
    {
        Minimum = AlwaysOnTopSettings.MinimumOpacity, Maximum = AlwaysOnTopSettings.MaximumOpacity,
        StepFrequency = 5, SmallChange = 5, LargeChange = 10, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly DispatcherQueueTimer _settle;
    private int _validThickness = AlwaysOnTopSettings.DefaultBorderThickness;
    private bool _updating;

    public AppearanceEditor(DispatcherQueue dispatcher)
    {
        Spacing = 10;
        // Dragging the spectrum or the slider raises many changes; report once they settle.
        _settle = dispatcher.CreateTimer();
        _settle.Interval = TimeSpan.FromMilliseconds(250);
        _settle.IsRepeating = false;
        _settle.Tick += OnSettled;

        AutomationProperties.SetName(_border, UiText.Get("Show border"));
        AutomationProperties.SetName(_thickness, UiText.Get("Border thickness"));
        AutomationProperties.SetHelpText(_thickness, UiText.Get("Enter a whole number from 1 to 8."));
        AutomationProperties.SetName(_color, UiText.Get("Border color"));
        AutomationProperties.SetName(_picker, UiText.Get("Custom color"));
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);

        _border.Toggled += (_, _) =>
        {
            if (_updating) return;
            // A cleared number field must not prevent switching the border off.
            if (!IsValidThickness(_thickness.Value)) Update(() => _thickness.Value = _validThickness);
            SetError(null);
            Report();
        };
        _thickness.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            if (!IsValidThickness(_thickness.Value))
            {
                SetError("Enter a whole number from 1 to 8.");
                return;
            }
            _validThickness = (int)_thickness.Value;
            SetError(null);
            Report();
        };
        _color.SelectionChanged += (_, _) => { if (!_updating) Report(); };
        _picker.ColorChanged += (_, _) => { if (!_updating) ReportWhenSettled(); };
        _opacity.ValueChanged += (_, _) =>
        {
            UpdateOpacityHeader();
            if (!_updating) ReportWhenSettled();
        };

        Children.Add(_border);
        Children.Add(_thickness);
        Children.Add(_color);
        Children.Add(_picker);
        Children.Add(_opacity);
        Children.Add(_error);
    }

    /// <summary>Raised on the UI thread after a valid change.</summary>
    public event EventHandler? Changed;

    public WindowAppearance Value => new(_border.IsOn, _validThickness, CustomColor ? PickerRgb : null, (int)_opacity.Value);

    /// <summary>Shows a value without reporting it. The picker starts from the accent color until a custom one exists.</summary>
    public void Show(WindowAppearance value)
    {
        _settle.Stop();
        Update(() =>
        {
            _border.IsOn = value.ShowBorder;
            _validThickness = value.BorderThickness;
            _thickness.Value = value.BorderThickness;
            _color.SelectedIndex = value.BorderColor is null ? 0 : 1;
            uint rgb = value.BorderColor ?? SystemTheme.GetAccentColorRgb();
            _picker.Color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            _opacity.Value = value.Opacity;
        });
        SetError(null);
    }

    /// <summary>Reports a change still waiting for the drag to settle, e.g. when the panel closes.</summary>
    public void Flush()
    {
        if (!_settle.IsRunning) return;
        _settle.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Detach()
    {
        _settle.Stop();
        _settle.Tick -= OnSettled;
    }

    private bool CustomColor => _color.SelectedIndex == 1;

    private uint PickerRgb => ((uint)_picker.Color.R << 16) | ((uint)_picker.Color.G << 8) | _picker.Color.B;

    private static bool IsValidThickness(double value) => double.IsFinite(value) && value == Math.Truncate(value)
        && value is >= AlwaysOnTopSettings.MinimumBorderThickness and <= AlwaysOnTopSettings.MaximumBorderThickness;

    private void Update(Action change)
    {
        _updating = true;
        try { change(); }
        finally { _updating = false; }
        UpdateControls();
    }

    private void Report()
    {
        _settle.Stop();
        UpdateControls();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReportWhenSettled()
    {
        _settle.Stop();
        _settle.Start();
    }

    private void OnSettled(DispatcherQueueTimer sender, object args) => Changed?.Invoke(this, EventArgs.Empty);

    private void UpdateControls()
    {
        _thickness.IsEnabled = _color.IsEnabled = _picker.IsEnabled = _border.IsOn;
        _picker.Visibility = CustomColor ? Visibility.Visible : Visibility.Collapsed;
        UpdateOpacityHeader();
    }

    private void UpdateOpacityHeader()
    {
        string header = UiText.Format("Opacity: {0}%", (int)_opacity.Value);
        _opacity.Header = header;
        AutomationProperties.SetName(_opacity, header);
    }

    private void SetError(string? key)
    {
        _error.Text = key is null ? "" : UiText.Get(key);
        _error.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
