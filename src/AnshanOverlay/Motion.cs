using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnshanOverlay;

public static class Motion
{
    public static bool Enabled { get; set; } = true;
    public static bool CanAnimate => Enabled && SystemParameters.ClientAreaAnimation;
    public static void Fade(FrameworkElement element, double opacity, int milliseconds = 120)
    {
        double from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = opacity;
        if (CanAnimate) element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, opacity, TimeSpan.FromMilliseconds(milliseconds)) { FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
    public static void Reveal(FrameworkElement element)
    {
        if (!CanAnimate || !element.IsLoaded) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        var offset = new TranslateTransform(); element.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    public static void ButtonFeedback(object sender, bool pressed)
    {
        if (sender is Button button) Fade(button, pressed ? .8 : button.IsMouseOver ? .94 : 1, pressed ? 70 : 120);
    }
}
