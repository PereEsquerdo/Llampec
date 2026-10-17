using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Llampec.Flyout;

/// <summary>
/// One centred line that ends in an ellipsis when it does not fit. While its tile is hovered or has
/// keyboard focus, text that does not fit scrolls once to its end, holds it for a second and returns, then rests. The scroll is
/// a render-transform animation, so it runs on the compositor; nothing runs otherwise, and Windows'
/// animation setting turns it off (the tooltip still shows the whole text). The line sits in a
/// <see cref="Canvas"/> because a layout slot would clip the part that scrolls into view.
/// </summary>
internal sealed class MarqueeText : Grid
{
    private const double PixelsPerSecond = 40;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1);
    private readonly TranslateTransform _shift = new();
    private readonly Canvas _canvas = new();
    private readonly Windows.UI.ViewManagement.UISettings _settings;
    private Storyboard? _story;
    private double _natural;

    public MarqueeText(Windows.UI.ViewManagement.UISettings settings, Style? style = null)
    {
        _settings = settings;
        Text = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, RenderTransform = _shift,
        };
        if (style is not null) Text.Style = style;
        _canvas.Children.Add(Text);
        Children.Add(_canvas);
        SizeChanged += (_, args) =>
        {
            Clip = new RectangleGeometry { Rect = new(0, 0, args.NewSize.Width, args.NewSize.Height) };
            if (_story is null) Rest();
        };
    }

    public TextBlock Text { get; }

    /// <summary>Sets the line and its height; an empty value collapses it.</summary>
    public void SetText(string? value)
    {
        if (Text.Text == (value ?? "") && _natural > 0) return;
        Stop();
        Text.Text = value ?? "";
        Text.ClearValue(WidthProperty);
        Text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _natural = Text.DesiredSize.Width;
        Height = Text.DesiredSize.Height;
        Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        Rest();
    }

    /// <summary>Centred when it fits; otherwise the slot's width with an ellipsis.</summary>
    private void Rest()
    {
        double width = ActualWidth;
        if (width <= 0) return;
        bool fits = _natural <= width + .5;
        Text.TextTrimming = TextTrimming.CharacterEllipsis;
        Text.Width = fits ? _natural : width;
        Canvas.SetLeft(Text, fits ? Math.Floor((width - _natural) / 2) : 0);
    }

    public void Start()
    {
        Stop();
        double overflow = _natural - ActualWidth;
        if (!_settings.AnimationsEnabled || ActualWidth <= 0 || overflow <= 1) return;
        // The whole line, from its start, while it moves.
        Text.TextTrimming = TextTrimming.None;
        Text.Width = _natural;
        Canvas.SetLeft(Text, 0);
        // Rest, scroll to the end, hold it long enough to read, scroll back.
        var travel = TimeSpan.FromSeconds(Math.Max(1, overflow / PixelsPerSecond));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = Pause, Value = 0 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = Pause + travel, Value = -overflow, EasingFunction = ease });
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = Pause + travel + Hold, Value = -overflow });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = Pause + travel + Hold + travel, Value = 0, EasingFunction = ease });
        Storyboard.SetTarget(animation, _shift);
        Storyboard.SetTargetProperty(animation, "X");
        var story = _story = new Storyboard();
        story.Children.Add(animation);
        story.Completed += (_, _) => { if (_story == story) Stop(); };
        story.Begin();
    }

    public void Stop()
    {
        _story?.Stop();
        _story = null;
        _shift.X = 0;
        Rest();
    }
}
