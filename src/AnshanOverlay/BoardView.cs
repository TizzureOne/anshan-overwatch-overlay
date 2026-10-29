using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace AnshanOverlay;

public sealed class BoardView : FrameworkElement
{
    public static readonly DependencyProperty ModeOpacityProperty = Progress("ModeOpacity", .5);
    public static readonly DependencyProperty VisibilityProgressProperty = Progress("VisibilityProgress", 1);
    public static readonly DependencyProperty HeroProgressProperty = Progress("HeroProgress", 1);
    public static readonly DependencyProperty SelectionProgressProperty = Progress("SelectionProgress", 1);
    public static readonly DependencyProperty ClickProgressProperty = Progress("ClickProgress", 1);
    private static DependencyProperty Progress(string name, double value) => DependencyProperty.Register(name, typeof(double), typeof(BoardView), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));
    private Scene scene = new();
    private IReadOnlySet<string> selectedIds = new HashSet<string>();
    private HashSet<string> enteringHeroes = new();
    private bool editing;
    private double passThroughOpacity = .5;
    private Point clickPosition;
    public SelectionAppearance HeroSelection { get; set; } = new();
    internal string? EditingTextId { get; set; }
    public Scene Scene
    {
        get => scene;
        set
        {
            if (Motion.CanAnimate && IsLoaded && DrawEditorChrome && value.Visible)
            {
                var added = value.Items.Where(i => i.Kind == "hero").Select(i => i.Id).Except(scene.Items.Select(i => i.Id)).ToHashSet();
                if (added.Count > 0) { enteringHeroes = added; Motion.Start(this, HeroProgressProperty, 0); }
            }
            if (!Motion.CanAnimate) { enteringHeroes.Clear(); BeginAnimation(HeroProgressProperty, null); BeginAnimation(SelectionProgressProperty, null); }
            scene = value; Motion.To(this, VisibilityProgressProperty, value.Visible ? 1 : 0);
        }
    }
    public IReadOnlySet<string> SelectedIds
    {
        get => selectedIds;
        set
        {
            if (selectedIds.SetEquals(value)) return;
            selectedIds = new HashSet<string>(value);
            if (value.Count > 0) Motion.Start(this, SelectionProgressProperty, 0);
        }
    }
    public Rect? SelectionBox { get; set; }
    public string? DraggedHeroId { get; set; }
    public bool Editing
    {
        get => editing;
        set
        {
            editing = value;
            if (!value) { BeginAnimation(ClickProgressProperty, null); SetValue(ClickProgressProperty, 1d); }
            Motion.To(this, ModeOpacityProperty, value ? 1 : PassThroughOpacity);
        }
    }
    public bool DrawEditorChrome { get; set; } = true;
    public double PassThroughOpacity { get => passThroughOpacity; set { passThroughOpacity = value; Motion.To(this, ModeOpacityProperty, Editing ? 1 : value); } }
    public string ModeHint { get; set; } = "";
    public Point? EraserPosition { get; set; }
    public double EraserRadius { get; set; }
    public IReadOnlyList<Hero> Heroes { get; set; } = Array.Empty<Hero>();
    public BoardView() { Focusable = true; ClipToBounds = true; }
    public void ShowClick(Point point)
    {
        if (!Editing || !DrawEditorChrome || !Scene.Visible || !Motion.CanAnimate) return;
        clickPosition = point;
        Motion.Start(this, ClickProgressProperty, 0, 420);
    }
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
        double sceneOpacity = DrawEditorChrome ? (double)GetValue(VisibilityProgressProperty) : Scene.Visible ? 1 : 0;
        if (sceneOpacity <= 0) return;
        double modeOpacity = DrawEditorChrome ? (double)GetValue(ModeOpacityProperty) : 1;
        double selectionProgress = (double)GetValue(SelectionProgressProperty);
        foreach (var item in Scene.Items)
        {
            if (DrawEditorChrome && item.Id == EditingTextId) continue;
            if (item.IsHeroTrail) { DrawHeroTrail(dc, item, sceneOpacity); continue; }
            bool entering = DrawEditorChrome && item.Kind == "hero" && enteringHeroes.Contains(item.Id);
            double progress = entering ? (double)GetValue(HeroProgressProperty) : 1;
            dc.PushOpacity(item.Opacity * (item.Kind == "hero" ? modeOpacity : 1) * sceneOpacity * (.45 + .55 * progress) * (DrawEditorChrome && Editing && item.Kind == "hero" && item.Id == DraggedHeroId ? .55 : 1));
            if (entering) dc.PushTransform(new ScaleTransform(.88 + .12 * progress, .88 + .12 * progress, item.X, item.Y));
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
            else if (Annotation.IsLabel(item))
            {
                var bounds = Annotation.Bounds(item); var text = Annotation.Format(item);
                dc.DrawRoundedRectangle(item.Kind == "number" ? color : Brush("#15202C"), new Pen(color, 2), bounds, item.Kind == "number" ? bounds.Height / 2 : 5, item.Kind == "number" ? bounds.Height / 2 : 5);
                text.SetForegroundBrush(item.Kind == "number" ? Brush("#15202C") : color);
                dc.DrawText(text, new Point(bounds.X + (bounds.Width - text.WidthIncludingTrailingWhitespace) / 2, bounds.Y + (bounds.Height - text.Height) / 2));
            }
            else if (item.Kind is "circle" or "rectangle" or "path")
            {
                var geometry = Editor.Outline(item);
                dc.DrawGeometry(null, Stroke(item, color, item.Width), geometry);
            }
            else if (item.Kind == "line") DrawLine(dc, item, color);
            if (item.Erasures.Count > 0) dc.Pop();
            if (entering) dc.Pop();
            dc.Pop();
            if (entering && progress < 1)
            {
                dc.PushOpacity((1 - progress) * .8);
                dc.DrawEllipse(null, new Pen(Brush("#FFD166"), 2), item.Start, item.Size / 2 + 5 + progress * 12, item.Size / 2 + 5 + progress * 12);
                dc.Pop();
            }
            if (DrawEditorChrome && Editing && SelectedIds.Contains(item.Id))
            {
                if (item.Kind == "hero")
                    DrawHeroSelection(dc, item, selectionProgress);
                else if (SelectedIds.Count == 1 && !Annotation.IsLabel(item))
                    foreach (var p in new[] { item.Start, item.End }) dc.DrawEllipse(Brushes.White, new Pen(color, 2), p, 6, 6);
                else
                {
                    var bounds = Editor.SelectionBounds(item);
                    if (!bounds.IsEmpty) { bounds.Inflate(5, 5); dc.DrawRectangle(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash }, bounds); }
                }
            }
        }
        if (DrawEditorChrome && Editing && Motion.CanAnimate && (double)GetValue(ClickProgressProperty) is var click && click < 1)
        {
            double radius = 8 + click * 28, fade = (1 - click) * (1 - click);
            var glow = new RadialGradientBrush(Color.FromArgb(42, 125, 226, 209), Colors.Transparent);
            dc.PushOpacity(fade);
            dc.DrawEllipse(glow, null, clickPosition, radius + 8, radius + 8);
            dc.DrawEllipse(null, new Pen(Brush("#997DE2D1"), 1.5), clickPosition, radius, radius);
            dc.DrawEllipse(Brush("#CCFFFFFF"), null, clickPosition, 2, 2);
            dc.Pop();
        }
        if (DrawEditorChrome && Editing && EraserPosition is { } cursor)
            dc.DrawEllipse(null, new Pen(Brush("#FF9C27"), 1.5) { DashStyle = DashStyles.Dot }, cursor, EraserRadius, EraserRadius);
        if (DrawEditorChrome && Editing && SelectionBox is { } box)
            dc.DrawRectangle(Brush("#22FF9C27"), new Pen(Brush("#FF9C27"), 1.5) { DashStyle = DashStyles.Dash }, box);
        if (DrawEditorChrome && Editing && ModeHint.Length > 0)
        {
            var hint = new FormattedText(ModeHint, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            hint.MaxTextWidth = Math.Max(100, Scene.Width - 56);
            var bounds = new Rect(16, Math.Max(12, Scene.Height - hint.Height - 32), hint.Width + 24, hint.Height + 16);
            dc.DrawRoundedRectangle(Brush("#E6223344"), new Pen(Brush("#FF9C27"), 1), bounds, 5, 5);
            dc.DrawText(hint, new Point(bounds.X + 12, bounds.Y + 8));
        }
    }
    private void DrawHeroSelection(DrawingContext dc, SceneItem item, double progress)
    {
        var style = HeroSelection;
        if (style.Shape == HeroSelectionShape.None || style.Opacity <= 0) return;
        double radius = item.Size / 2 + style.Gap + 3 * (1 - progress);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            if (style.Shape == HeroSelectionShape.Corners)
                foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
                {
                    var corner = item.Start + new Vector(x * radius, y * radius);
                    double length = Math.Min(style.Length, radius);
                    path.BeginFigure(corner - new Vector(x * length, 0), false, false);
                    path.LineTo(corner, true, false); path.LineTo(corner - new Vector(0, y * length), true, false);
                }
            else if (style.Shape == HeroSelectionShape.Crosshair)
                foreach (var direction in new[] { new Vector(-1, 0), new Vector(1, 0), new Vector(0, -1), new Vector(0, 1) })
                {
                    path.BeginFigure(item.Start + direction * radius, false, false);
                    path.LineTo(item.Start + direction * (radius + style.Length), true, false);
                }
        }
        geometry.Freeze();
        void Draw(string color, double width)
        {
            var pen = new Pen(Brush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (style.Shape == HeroSelectionShape.Ring) dc.DrawEllipse(null, pen, item.Start, radius, radius);
            else dc.DrawGeometry(null, pen, geometry);
        }
        dc.PushOpacity(style.Opacity);
        if (style.Outline) Draw("#15202C", style.Thickness + 2);
        Draw(style.Color, style.Thickness); dc.Pop();
    }
    private static void DrawHeroTrail(DrawingContext dc, SceneItem item, double opacity)
    {
        // Brush alpha avoids a separate offscreen opacity surface for every trail/band.
        var color = (Color)ColorConverter.ConvertFromString(item.Color);
        void Draw(IReadOnlyList<InkPoint> points, double width, double alpha, bool dashed)
        {
            if (alpha <= 0) return;
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), color.R, color.G, color.B)); brush.Freeze();
            var pen = new Pen(brush, width) { LineJoin = PenLineJoin.Round };
            if (dashed) { pen.DashStyle = new DashStyle(new[] { 3d, 2d }, item.TrailDashOffset / width); pen.DashCap = pen.StartLineCap = pen.EndLineCap = PenLineCap.Round; }
            pen.Freeze(); var geometry = Editor.Path(points); geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
        }
        if (item.TrailBands is { } bands)
            foreach (var band in bands) Draw(band.Points, band.Width, opacity * item.Opacity * band.Opacity, false);
        else Draw(item.Points, item.Width, opacity * item.Opacity, item.Dashed);
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
