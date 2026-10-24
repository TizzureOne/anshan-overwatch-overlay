using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnshanOverlay;

public static class Motion
{
    private sealed record ResizeState(double Height, double Minimum);
    private static readonly DependencyProperty ResizeStateProperty = DependencyProperty.RegisterAttached("ResizeState", typeof(ResizeState), typeof(Motion));
    public static bool Enabled { get; set; } = true;
    public static bool CanAnimate => Enabled && SystemParameters.ClientAreaAnimation;
    private static DoubleAnimation Transition(double from, double to, int milliseconds, int delay = 0) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    {
        BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.Stop,
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };
    public static void To(FrameworkElement element, DependencyProperty property, double value, int milliseconds = 160)
    {
        double from = (double)element.GetValue(property);
        if (CanAnimate && (double)element.GetAnimationBaseValue(property) == value) return;
        element.BeginAnimation(property, null); element.SetValue(property, value);
        if (CanAnimate && element.IsLoaded && from != value) element.BeginAnimation(property, Transition(from, value, milliseconds));
    }
    public static void Fade(FrameworkElement element, double opacity, int milliseconds = 120)
    {
        To(element, UIElement.OpacityProperty, opacity, milliseconds);
    }
    public static void Start(FrameworkElement element, DependencyProperty property, double from, int milliseconds = 180)
    {
        element.BeginAnimation(property, null);
        if (CanAnimate && element.IsLoaded) element.BeginAnimation(property, Transition(from, (double)element.GetAnimationBaseValue(property), milliseconds));
    }
    public static void Reveal(FrameworkElement element, int delay = 0, double offset = 6)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (element.RenderTransform is TranslateTransform old) { old.BeginAnimation(TranslateTransform.YProperty, null); old.Y = 0; }
        if (!CanAnimate || !element.IsLoaded) return;
        element.BeginAnimation(UIElement.OpacityProperty, Transition(element.Opacity * .45, element.Opacity, 180, delay));
        if (offset == 0) return;
        var translation = new TranslateTransform(); element.RenderTransform = translation;
        translation.BeginAnimation(TranslateTransform.YProperty, Transition(offset, 0, 180, delay));
    }
    public static void Pulse(FrameworkElement element)
    {
        var scale = ScaleFor(element);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        if (!CanAnimate || !element.IsLoaded) return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Transition(.96, 1, 160));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Transition(.96, 1, 160));
    }
    public static void Paint(FrameworkElement element, DependencyProperty property, string color)
    {
        var target = (Color)ColorConverter.ConvertFromString(color);
        var previous = element.GetValue(property) as SolidColorBrush;
        if (previous is not null && (Color)previous.GetAnimationBaseValue(SolidColorBrush.ColorProperty) == target && (CanAnimate || !previous.HasAnimatedProperties)) return;
        var brush = new SolidColorBrush(target); element.SetCurrentValue(property, brush);
        if (CanAnimate && element.IsLoaded && previous is not null)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous.Color, target, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.Stop });
    }
    public static void Text(TextBlock element, string text)
    {
        if (element.Text == text) return;
        element.Text = text; Reveal(element, offset: 0);
    }
    public static void Resize(Window window, double height, double minHeight)
    {
        double from = window.Height;
        window.ClearValue(ResizeStateProperty);
        window.BeginAnimation(FrameworkElement.HeightProperty, null);
        window.MinHeight = Math.Min(window.MinHeight, minHeight); window.Height = height;
        if (!CanAnimate) { window.MinHeight = minHeight; return; }
        var state = new ResizeState(height, minHeight); window.SetValue(ResizeStateProperty, state);
        var animation = Transition(from, height, 200);
        animation.Completed += (_, _) => { if (ReferenceEquals(window.GetValue(ResizeStateProperty), state)) FinishResize(window); };
        window.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }
    public static double Height(Window window) => (window.GetValue(ResizeStateProperty) as ResizeState)?.Height ?? window.Height;
    public static void FinishResize(Window window)
    {
        if (window.GetValue(ResizeStateProperty) is not ResizeState state) return;
        window.ClearValue(ResizeStateProperty); window.BeginAnimation(FrameworkElement.HeightProperty, null);
        // Native WM_SIZE messages can replace the animated property's base value.
        // Commit the intended size explicitly instead of retaining an in-between frame.
        window.MinHeight = Math.Min(window.MinHeight, state.Minimum); window.Height = state.Height; window.MinHeight = state.Minimum;
    }
    public static void ButtonFeedback(object sender, bool pressed)
    {
        if (sender is not Button button || button.Template.FindName("Bd", button) is not FrameworkElement face) return;
        var scale = ScaleFor(face);
        double target = pressed ? .97 : button.IsMouseOver ? 1.015 : 1;
        double x = scale.ScaleX, y = scale.ScaleY;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = target;
        if (CanAnimate && button.IsLoaded)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Transition(x, target, pressed ? 70 : 120));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Transition(y, target, pressed ? 70 : 120));
        }
    }
    private static ScaleTransform ScaleFor(FrameworkElement element)
    {
        var scale = element.RenderTransform as ScaleTransform;
        if (scale is null || scale.IsFrozen) { scale = scale?.CloneCurrentValue() ?? new ScaleTransform(); element.RenderTransform = scale; }
        element.RenderTransformOrigin = new Point(.5, .5); return scale;
    }
}
