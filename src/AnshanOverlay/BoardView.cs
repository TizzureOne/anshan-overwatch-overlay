using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace AnshanOverlay;

public sealed class BoardView : FrameworkElement
{
    public Scene Scene { get; set; } = new();
    public IReadOnlySet<string> SelectedIds { get; set; } = new HashSet<string>();
    public Rect? SelectionBox { get; set; }
    public string? DraggedHeroId { get; set; }
    public bool Editing { get; set; }
    public bool DrawEditorChrome { get; set; } = true;
    public Point? EraserPosition { get; set; }
    public double EraserRadius { get; set; }
    public IReadOnlyList<Hero> Heroes { get; set; } = Array.Empty<Hero>();
    public BoardView() { Focusable = true; ClipToBounds = true; }
    public static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b;
    }
    protected override void OnRender(DrawingContext dc)
    {
        // Non-zero alpha is intentional: layered windows pass fully transparent
        // pixels through even when WPF's logical hit test would hit the canvas.
        if (Editing && DrawEditorChrome)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (!Scene.Visible) return;
        foreach (var item in Scene.Items)
        {
            dc.PushOpacity(item.Opacity * (DrawEditorChrome && Editing && item.Kind == "hero" && item.Id == DraggedHeroId ? .55 : 1));
            if (item.Erasures.Count > 0) dc.PushClip(Geometry.Combine(new RectangleGeometry(new Rect(0, 0, Scene.Width, Scene.Height)), Editor.ErasedArea(item), GeometryCombineMode.Exclude, null));
            var color = Brush(item.Kind == "hero" ? item.HeroBorder : item.Color);
            if (item.Kind == "hero")
            {
                var radius = item.Size / 2;
                dc.DrawEllipse(Brush("#15202C"), null, item.Start, radius, radius);
                dc.DrawImage(Assets.Portrait(item.HeroId), new Rect(item.X - radius, item.Y - radius, item.Size, item.Size));
                dc.DrawEllipse(null, new Pen(color, 3), item.Start, radius, radius);
                if (Scene.ShowHeroNames)
                {
                    var text = new FormattedText(Heroes.FirstOrDefault(h => h.Id == item.HeroId)?.Name ?? item.HeroId,
                        CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"),
                        Math.Clamp(item.Size * 0.19, 12, 24), Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    var rect = new Rect(item.X - text.Width / 2 - 7, item.Y + radius + 5, text.Width + 14, text.Height + 4);
                    dc.DrawRoundedRectangle(Brush("#15202C"), null, rect, 4, 4);
                    dc.DrawText(text, new Point(rect.X + 7, rect.Y + 2));
                }
            }
            else if (item.Kind is "circle" or "rectangle" or "path")
            {
                var geometry = Editor.Outline(item);
                dc.DrawGeometry(null, Stroke(item, color, item.Width), geometry);
            }
            else if (item.Kind == "line") DrawLine(dc, item, color);
            if (item.Erasures.Count > 0) dc.Pop();
            dc.Pop();
            if (DrawEditorChrome && Editing && SelectedIds.Contains(item.Id))
            {
                if (item.Kind == "hero")
                    dc.DrawEllipse(null, new Pen(Brushes.White, 2) { DashStyle = DashStyles.Dash }, item.Start, item.Size / 2 + 9, item.Size / 2 + 9);
                else if (SelectedIds.Count == 1)
                    foreach (var p in new[] { item.Start, item.End }) dc.DrawEllipse(Brushes.White, new Pen(color, 2), p, 6, 6);
                else
                {
                    var bounds = Editor.SelectionBounds(item);
                    if (!bounds.IsEmpty) { bounds.Inflate(5, 5); dc.DrawRectangle(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash }, bounds); }
                }
            }
        }
        if (DrawEditorChrome && Editing && EraserPosition is { } cursor)
            dc.DrawEllipse(null, new Pen(Brush("#FF9C27"), 1.5) { DashStyle = DashStyles.Dot }, cursor, EraserRadius, EraserRadius);
        if (DrawEditorChrome && Editing && SelectionBox is { } box)
            dc.DrawRectangle(Brush("#22FF9C27"), new Pen(Brush("#FF9C27"), 1.5) { DashStyle = DashStyles.Dash }, box);
    }
    private static Pen Stroke(SceneItem item, Brush brush, double width)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (item.Dashed) pen.DashStyle = new DashStyle(new[] { item.Width * 3 / width, item.Width * 2 / width }, 0);
        return pen;
    }
    private static void DrawLine(DrawingContext dc, SceneItem item, Brush color)
    {
        var start = item.Start; var end = item.End; var delta = end - start;
        if (delta.Length < 1) return;
        double length = delta.Length; delta.Normalize();
        double head = Math.Min(length * 0.7, Math.Max(16, item.Width * 3.5));
        var shaftEnd = item.Arrow ? end - delta * head * 0.75 : end;
        dc.DrawLine(Stroke(item, color, item.Width), start, shaftEnd);
        if (item.Arrow)
        {
            var normal = new Vector(-delta.Y, delta.X);
            var path = new StreamGeometry();
            using (var g = path.Open())
            {
                g.BeginFigure(end, true, true);
                g.LineTo(end - delta * head + normal * head * 0.48, true, false);
                g.LineTo(end - delta * head - normal * head * 0.48, true, false);
            }
            path.Freeze(); dc.DrawGeometry(color, null, path);
        }
    }
}
