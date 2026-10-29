using System;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnshanOverlay;

internal sealed class BrandCelebration : Window
{
    internal CelebrationView Visual { get; } = new();
    private int generation;
    private readonly Window anchor;
    private bool closed;

    internal BrandCelebration(Window owner)
    {
        anchor = owner; Width = 520; Height = 360; Title = "安山守望 · 英雄集结";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        IsHitTestVisible = false; Focusable = false; Content = Visual;
        SourceInitialized += (_, _) => Native.SetPassThrough(new WindowInteropHelper(this).Handle, true);
        Closed += (_, _) => { closed = true; Visual.BeginAnimation(CelebrationView.ProgressProperty, null); };
        owner.Closed += (_, _) => { if (!closed) Close(); };
    }
    internal void Play()
    {
        if (closed || !Motion.CanAnimate || !anchor.IsVisible) return;
        Owner = anchor;
        Left = anchor.Left + anchor.ActualWidth / 2 - Width / 2;
        Top = anchor.Top + anchor.ActualHeight / 2 - 150;
        Show(); WindowLayout.KeepVisible(this);
        int current = ++generation;
        Visual.BeginAnimation(CelebrationView.ProgressProperty, null);
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(2.8));
        animation.Completed += (_, _) => { if (current == generation) Hide(); };
        Visual.BeginAnimation(CelebrationView.ProgressProperty, animation);
    }
}

internal sealed class CelebrationView : FrameworkElement
{
    internal static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double), typeof(CelebrationView), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    private readonly Brush[] colors = { BoardView.Brush("#FFD166"), BoardView.Brush("#7DE2D1"), BoardView.Brush("#FFFFFF"), BoardView.Brush("#F48CAA"), BoardView.Brush("#94BFFF") };
    protected override void OnRender(DrawingContext dc)
    {
        double p = (double)GetValue(ProgressProperty);
        if (p >= 1 || !Motion.CanAnimate) return;
        var center = new Point(ActualWidth / 2, 150);
        double burst = 1 - Math.Pow(1 - Math.Min(1, p / .5), 3);
        double opacity = Math.Min(1, p / .06) * Math.Min(1, (1 - p) / .2);
        dc.PushOpacity(opacity);
        dc.PushOpacity((1 - p) * .5);
        dc.DrawEllipse(null, new Pen(colors[0], 3), center, 40 + 105 * burst, 40 + 105 * burst);
        dc.DrawEllipse(null, new Pen(colors[1], 1.5), center, 30 + 80 * burst, 30 + 80 * burst);
        dc.Pop();
        for (int i = 0; i < 48; i++)
        {
            double angle = i * 2.3999632297, distance = (65 + i % 9 * 14) * burst;
            var position = center + new Vector(Math.Cos(angle) * distance, Math.Sin(angle) * distance * .65 + p * p * 60);
            dc.PushTransform(new RotateTransform(i * 37 + p * (i % 2 == 0 ? 280 : -280), position.X, position.Y));
            if (i % 4 == 0)
            {
                var pen = new Pen(colors[i % colors.Length], 1.8);
                dc.DrawLine(pen, position - new Vector(5, 0), position + new Vector(5, 0));
                dc.DrawLine(pen, position - new Vector(0, 5), position + new Vector(0, 5));
            }
            else dc.DrawRoundedRectangle(colors[i % colors.Length], null, new Rect(position.X - 2, position.Y - 5, 4, 10), 1.5, 1.5);
            dc.Pop();
        }
        double badge = .65 + .35 * Math.Min(1, p / .12) + .12 * Math.Sin(Math.Min(1, p / .3) * Math.PI);
        dc.PushTransform(new ScaleTransform(badge, badge, center.X, center.Y));
        dc.DrawEllipse(BoardView.Brush("#E61A2A40"), new Pen(colors[0], 2), center, 40, 40);
        dc.PushTransform(new RotateTransform(360 * (1 - Math.Pow(1 - Math.Min(1, p / .3), 3)), center.X, center.Y));
        dc.DrawImage(Assets.AppIcon, new Rect(center.X - 29, center.Y - 29, 58, 58)); dc.Pop(); dc.Pop();
        var banner = new Rect(center.X - 146, 214, 292, 76);
        dc.DrawRoundedRectangle(BoardView.Brush("#F01A2A40"), new Pen(colors[0], 1.5), banner, 14, 14);
        Text("英雄集结", 28, 221, colors[0], true);
        Text("安山守望 · 开讲！", 13, 261, colors[2], false);
        dc.Pop();
        void Text(string value, double size, double y, Brush color, bool bold)
        {
            var text = new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(center.X - text.Width / 2, y));
        }
    }
}
