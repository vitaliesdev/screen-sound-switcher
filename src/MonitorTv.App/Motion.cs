using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonitorTv.App;

/// <summary>
/// The app's motion: new content settles into place, a few elements in a short cascade.
/// Curves are Emil Kowalski's: a strong ease-out for things arriving and an ease-in-out for things already on screen
/// that move. Everything stays under about 300 ms and is skipped when animations are off, so the state change
/// still happens, only without the movement.
/// </summary>
internal static class Motion
{
    // cubic-bezier(0.23, 1, 0.32, 1): fast start, long soft tail, so the end of the move is gentle instead of a snap
    private static readonly KeySpline EaseOut = new(0.23, 1, 0.32, 1);

    // cubic-bezier(0.77, 0, 0.175, 1): accelerates and decelerates, for something that travels across the screen
    private static readonly KeySpline EaseInOut = new(0.77, 0, 0.175, 1);

    /// <summary>Set by the development snapshot commands so a still image is taken of the finished state.</summary>
    public static bool Disabled { get; set; }

    /// <summary>"system" (follow Windows), "on" or "off"; chosen by the user in the footer menu.</summary>
    public static string Mode { get; set; } = "system";

    public static bool Enabled => !Disabled && Mode switch
    {
        "on" => true,
        "off" => false,
        _ => SystemParameters.ClientAreaAnimation,
    };

    private static DoubleAnimationUsingKeyFrames Curve(double from, double to, int durationMs, KeySpline spline,
        int delayMs = 0, FillBehavior fill = FillBehavior.Stop)
    {
        var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = fill };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs)), spline));
        return animation;
    }

    /// <summary>
    /// Eases an element into place from a little below. <paramref name="fromOpacity"/> is 0 for things that appear
    /// (a notice) and higher for content that is only being refreshed, so it never blanks out and flashes back.
    /// </summary>
    public static void Reveal(FrameworkElement element, double fromY = 6, int delayMs = 0, double fromOpacity = 0)
    {
        if (!Enabled) return;

        if (element.RenderTransform is not TranslateTransform { IsFrozen: false } translate)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        // starts at the dimmed value, and the animation takes over after the delay; the resting values return when it ends
        element.Opacity = fromOpacity;
        var fade = Curve(fromOpacity, 1, 280, EaseOut, delayMs, FillBehavior.HoldEnd);
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);

        translate.BeginAnimation(TranslateTransform.YProperty, Curve(fromY, 0, 300, EaseOut, delayMs));
    }

    /// <summary>Refreshes the visible children of a panel one after another so the eye follows the change from top to bottom.</summary>
    public static void Cascade(Panel panel, int stepMs = 24, int maxDelayMs = 144)
    {
        if (!Enabled) return;
        var index = 0;
        foreach (FrameworkElement child in panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible))
            Reveal(child, 6, Math.Min(index++ * stepMs, maxDelayMs), fromOpacity: 0.35);
    }

    /// <summary>A gentle settle for the thing whose identity just changed (the profile's icon tile): no overshoot, no bounce.</summary>
    public static void Pop(FrameworkElement element)
    {
        if (!Enabled) return;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is not ScaleTransform { IsFrozen: false } scale)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Curve(0.9, 1, 280, EaseOut));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Curve(0.9, 1, 280, EaseOut));
    }

    public static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.RegisterAttached(
        "AnimatedOffset", typeof(double), typeof(Motion),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    public static double GetAnimatedOffset(DependencyObject d) => (double)d.GetValue(AnimatedOffsetProperty);

    public static void SetAnimatedOffset(DependencyObject d, double value) => d.SetValue(AnimatedOffsetProperty, value);

    /// <summary>Brings a scrolled pane back to its top. It is already on screen and moving, so it eases in and out.</summary>
    public static void ScrollToTop(ScrollViewer scroll)
    {
        var from = scroll.VerticalOffset;
        if (!Enabled || from < 24)
        {
            scroll.ScrollToVerticalOffset(0);
            return;
        }

        scroll.BeginAnimation(AnimatedOffsetProperty, Curve(from, 0, 300, EaseInOut));
    }
}
