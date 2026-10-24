using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnshanOverlay;

// A small cached visual moves in physical screen pixels, including across DPI boundaries.
// No scene edits, bitmap decoding, or full-canvas redraws are needed for cursor movement.
public sealed class HeroDragPreviewWindow : Window
{
    public const int FrameIntervalMs = 16;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(FrameIntervalMs) };
    private readonly HeroDragVisual preview = new();
    private IntPtr handle;
    private int side;
    private Native.CursorPoint? lastCursor;
    public bool IsTracking => timer.IsEnabled;

    public HeroDragPreviewWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        IsHitTestVisible = false; Focusable = false; Content = preview;
        SourceInitialized += (_, _) => { handle = new WindowInteropHelper(this).Handle; Native.SetPassThrough(handle, true); };
        timer.Tick += (_, _) => FollowCursor();
        Closed += (_, _) => timer.Stop();
    }
    public void Begin(string heroId, double size, string team)
    {
        preview.Configure(heroId, team);
        side = (int)Math.Ceiling(size + 8); Width = side; Height = side;
        new WindowInteropHelper(this).EnsureHandle();
        lastCursor = null; FollowCursor(); Show();
        Native.SetPassThrough(handle, true);
        lastCursor = null; FollowCursor(); timer.Start();
    }
    public void End() { timer.Stop(); Hide(); lastCursor = null; }
    private void FollowCursor()
    {
        if (!Native.GetCursorPos(out var cursor) || (lastCursor is { } last && last.X == cursor.X && last.Y == cursor.Y)) return;
        lastCursor = cursor;
        Native.SetWindowPos(handle, new IntPtr(-1), cursor.X - side / 2, cursor.Y - side / 2, side, side, 0x10);
    }
}

public sealed class HeroDragVisual : FrameworkElement
{
    private BitmapSource? portrait;
    private Pen border = new(BoardView.Brush("#39A9FF"), 3);
    private static readonly Pen Crosshair = new(Brushes.White, 1);
    public void Configure(string heroId, string team)
    {
        portrait = Assets.Portrait(heroId);
        border = new Pen(BoardView.Brush(team == "enemy" ? "#F05265" : "#39A9FF"), 3); border.Freeze();
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (portrait is null || ActualWidth < 8 || ActualHeight < 8) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 4;
        dc.PushOpacity(.55);
        dc.DrawEllipse(BoardView.Brush("#15202C"), null, center, radius, radius);
        dc.DrawImage(portrait, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        dc.DrawEllipse(null, border, center, radius, radius); dc.Pop();
        dc.DrawLine(Crosshair, center - new Vector(4, 0), center + new Vector(4, 0));
        dc.DrawLine(Crosshair, center - new Vector(0, 4), center + new Vector(0, 4));
    }
}
