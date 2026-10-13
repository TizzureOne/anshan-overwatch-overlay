using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

public sealed record Hero(string Id, string Name, string EnglishName, string Role, string Aliases, string Image)
{
    [JsonIgnore] public BitmapSource Portrait => Assets.Portrait(Id);
    [JsonIgnore] public string RoleName => Role switch { "tank" => "重装", "damage" => "输出", _ => "支援" };
    public bool Matches(string query) => $"{Name} {EnglishName} {Id} {Aliases}".Contains(query, StringComparison.OrdinalIgnoreCase);
}
public sealed record HeroManifest(string Source, DateTime RetrievedAt, int Count, List<Hero> Heroes);

public static class Assets
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private static readonly Dictionary<string, BitmapSource> images = new();
    public static byte[] Read(string path)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AnshanOverlay." + path.Replace('/', '.'))
            ?? throw new FileNotFoundException("Embedded resource missing: " + path);
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
    public static BitmapSource AppIcon { get; } = LoadIcon();
    private static BitmapSource LoadIcon()
    {
        using var stream = new MemoryStream(Read("Assets/app.png"));
        var icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); icon.Freeze(); return icon;
    }
    public static HeroManifest LoadHeroes() => JsonSerializer.Deserialize<HeroManifest>(Read("Assets/heroes.json"), Json)!;
    public static BitmapSource Portrait(string id)
    {
        if (images.TryGetValue(id, out var cached)) return cached;
        using var stream = new MemoryStream(Read($"Assets/Heroes/{id}.png"));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); images[id] = bitmap; return bitmap;
    }
}

public sealed record SceneItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "hero";
    public string HeroId { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public double Size { get; init; } = 80;
    public string Color { get; init; } = "#4ED3C0";
    public double Width { get; init; } = 5;
    public double Opacity { get; init; } = 1;
    public List<EraseMark> Erasures { get; init; } = new();
    public bool Dashed { get; init; }
    public bool Arrow { get; init; }
    public string Team { get; init; } = "ally";
    public List<InkPoint> Points { get; init; } = new();
    [JsonIgnore] public string HeroBorder => Team == "enemy" ? "#F05265" : "#39A9FF";
    [JsonIgnore] public Point Start => new(X, Y);
    [JsonIgnore] public Point End => new(X2, Y2);
}
public sealed record InkPoint(double X, double Y);
public sealed record EraseMark(double X, double Y, double X2, double Y2, double Radius);
public sealed record Scene
{
    public int Version { get; init; } = 2;
    public double Width { get; init; } = 1920;
    public double Height { get; init; } = 1080;
    public bool Visible { get; init; } = true;
    public bool ShowHeroNames { get; init; }
    public bool DrawingDashed { get; init; }
    public List<SceneItem> Items { get; init; } = new();
}

public sealed class SceneStore
{
    public Scene Current { get; private set; } = new();
    private readonly List<Scene> undo = new();
    private readonly Stack<Scene> redo = new();
    public event Action? Changed;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Preview(Scene scene) { Current = scene; Changed?.Invoke(); }
    public void Commit(Scene before)
    {
        if (JsonSerializer.Serialize(before, Assets.Json) == JsonSerializer.Serialize(Current, Assets.Json)) return;
        undo.Add(before); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); Changed?.Invoke();
    }
    public void Edit(Func<Scene, Scene> change) { var before = Current; Preview(change(Current)); Commit(before); }
    public void Undo() { if (!CanUndo) return; redo.Push(Current); var s = undo[^1]; undo.RemoveAt(undo.Count - 1); Preview(s); }
    public void Redo() { if (!CanRedo) return; undo.Add(Current); Preview(redo.Pop()); }
    public void Replace(SceneItem item) => Preview(Current with { Items = Current.Items.Select(x => x.Id == item.Id ? item : x).ToList() });
    public void Remove(string? id) => Edit(s => s with { Items = s.Items.Where(x => x.Id != id).ToList() });
    public void Clear(bool linesOnly) => Edit(s => s with { Items = linesOnly ? s.Items.Where(x => x.Kind == "hero").ToList() : new() });
    public string Serialize() => JsonSerializer.Serialize(Current, Assets.Json);
    public static Scene Parse(string json, IReadOnlyCollection<Hero> heroes)
    {
        var s = JsonSerializer.Deserialize<Scene>(json, Assets.Json) ?? throw new InvalidDataException("Empty scene");
        bool Finite(double n) => double.IsFinite(n) && Math.Abs(n) <= 100000;
        if ((s.Version != 1 && s.Version != 2) || !Finite(s.Width) || !Finite(s.Height) || s.Width < 320 || s.Height < 200 || s.Width > 16000 || s.Height > 16000 || s.Items is null || s.Items.Count > 2000)
            throw new InvalidDataException("Invalid scene dimensions, version or item count");
        var ids = new HashSet<string>();
        foreach (var i in s.Items)
            if (i is null || string.IsNullOrEmpty(i.Id) || !ids.Add(i.Id) || (i.Kind != "hero" && i.Kind != "line" && i.Kind != "path" && i.Kind != "circle" && i.Kind != "rectangle") ||
                (i.Kind == "hero" && !heroes.Any(h => h.Id == i.HeroId)) || i.Color is null || !Regex.IsMatch(i.Color, "^#[0-9a-fA-F]{6}$") ||
                !double.IsFinite(i.Opacity) || i.Opacity < 0 || i.Opacity > 1 || i.Erasures is null || i.Erasures.Count > 20000 || i.Erasures.Any(e => e is null || !new[] { e.X, e.Y, e.X2, e.Y2, e.Radius }.All(Finite) || e.Radius <= 0 || e.Radius > 1000) ||
                (i.Team != "ally" && i.Team != "enemy") || i.Points is null || i.Points.Count > 20000 || i.Points.Any(p => p is null || !Finite(p.X) || !Finite(p.Y)) ||
                !new[] { i.X, i.Y, i.X2, i.Y2, i.Size, i.Width }.All(Finite) || i.Size < 24 || i.Size > 400 || i.Width < 1 || i.Width > 30)
                throw new InvalidDataException("Invalid scene item");
        using var legacy = JsonDocument.Parse(json);
        if (!legacy.RootElement.TryGetProperty("showHeroNames", out _) && legacy.RootElement.TryGetProperty("items", out _))
            s = s with { ShowHeroNames = legacy.RootElement.GetProperty("items").EnumerateArray().Any(i => i.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.True) };
        return s with { Version = 2 };
    }
    public void Resize(double width, double height)
    {
        // A display change is not an undoable drawing action. Transform history
        // too, so undo cannot switch the canvas back to the previous monitor size.
        for (int n = 0; n < undo.Count; n++) undo[n] = Scale(undo[n], width, height);
        var scaledRedo = redo.Reverse().Select(s => Scale(s, width, height)).ToArray();
        redo.Clear(); foreach (var s in scaledRedo) redo.Push(s);
        Preview(Scale(Current, width, height));
    }
    public static Scene Scale(Scene s, double width, double height)
    {
        double scale = Math.Min(width / s.Width, height / s.Height);
        double ox = (width - s.Width * scale) / 2, oy = (height - s.Height * scale) / 2;
        return s with { Width = width, Height = height, Items = s.Items.Select(i => i with {
            X = i.X * scale + ox, Y = i.Y * scale + oy, X2 = i.X2 * scale + ox, Y2 = i.Y2 * scale + oy,
            Size = Math.Clamp(i.Size * scale, 24, 400), Width = Math.Clamp(i.Width * scale, 1, 30),
            Points = i.Points.Select(p => new InkPoint(p.X * scale + ox, p.Y * scale + oy)).ToList(),
            Erasures = i.Erasures.Select(e => new EraseMark(e.X * scale + ox, e.Y * scale + oy, e.X2 * scale + ox, e.Y2 * scale + oy, Math.Min(1000, e.Radius * scale))).ToList() }).ToList() };
    }
}

public enum EditorTool { Move, Hero, Brush, Line, Arrow, Circle, Rectangle, Eraser }
public sealed class ToolSettings
{
    public string Color { get; set; } = "#FFD166";
    public double Width { get; set; } = 5;
    public double Size { get; set; } = 80;
    public string Team { get; set; } = "ally";
}

public sealed class Editor
{
    public readonly SceneStore Store;
    public EditorTool Tool { get; set; }
    public Hero? PendingHero { get; set; }
    public Dictionary<EditorTool, ToolSettings> Settings { get; } = Enum.GetValues<EditorTool>().ToDictionary(t => t, _ => new ToolSettings());
    public ToolSettings Options => Settings[Tool];
    public string Color { get => Options.Color; set => Options.Color = value; }
    public double Width { get => Options.Width; set => Options.Width = value; }
    public bool Dashed { get => Store.Current.DrawingDashed; set => Store.Edit(s => s with { DrawingDashed = value }); }
    public double Size { get => Settings[EditorTool.Hero].Size; set => Settings[EditorTool.Hero].Size = value; }
    public string Team { get => Settings[EditorTool.Hero].Team; set => Settings[EditorTool.Hero].Team = value; }
    private readonly HashSet<string> selection = new();
    public IReadOnlySet<string> SelectedIds => selection;
    public string? SelectedId
    {
        get => selection.Count == 1 ? selection.First() : null;
        set { selection.Clear(); if (value is not null) selection.Add(value); }
    }
    public SceneItem[] SelectedItems => Store.Current.Items.Where(i => selection.Contains(i.Id)).ToArray();
    public void Select(IEnumerable<string> ids) { selection.Clear(); selection.UnionWith(ids); }
    public Rect? SelectionBox { get; private set; }
    public bool IsDragging => before is not null;
    public bool IsMoving => before is not null && original is not null && (!selecting || !extendSelection && selectionMoved);
    private Scene? before;
    private SceneItem? original;
    private Point origin;
    private int endpoint;
    private HashSet<string>? selectionBefore;
    private bool selecting;
    private bool extendSelection;
    private bool selectionMoved;
    private bool erasing;
    private Point lastErase;
    public double EraserSize { get; set; } = 28;
    public bool EraseWhole { get; set; }
    public Editor(SceneStore store)
    {
        Store = store;
        Store.Changed += () => selection.IntersectWith(Store.Current.Items.Select(i => i.Id));
    }
    public SceneItem? Selected => Store.Current.Items.FirstOrDefault(i => i.Id == SelectedId);
    public void Down(Point point, bool extend = false)
    {
        Cancel();
        point = Clamp(point); origin = point;
        before = Store.Current; endpoint = 0;
        if (Tool == EditorTool.Move)
        {
            selecting = true; extendSelection = extend; selectionBefore = new(selection);
            original = Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => Hit(i, point));
            if (original is null) { if (!extend) SelectedId = null; return; }
            if (extend)
            {
                if (!selection.Add(original.Id)) selection.Remove(original.Id);
                return;
            }
            if (!selection.Contains(original.Id)) SelectedId = original.Id;
            if (selection.Count == 1 && original.Kind == "line" && original.Erasures.Count == 0)
                endpoint = (point - original.Start).Length < 14 ? 1 : (point - original.End).Length < 14 ? 2 : 0;
            return;
        }
        // Heroes remain directly draggable with every active tool.
        original = Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => i.Kind == "hero" && Hit(i, point));
        if (original is not null) { SelectedId = original.Id; return; }
        SelectedId = null;
        if (Tool == EditorTool.Eraser) { erasing = true; lastErase = point; Erase(point); return; }
        if (Tool == EditorTool.Hero && PendingHero is not null)
        {
            before = null;
            var item = new SceneItem { HeroId = PendingHero.Id, X = point.X, Y = point.Y, Size = Size, Team = Team };
            Store.Edit(s => s with { Visible = true, Items = s.Items.Append(item).ToList() });
            SelectedId = item.Id; return;
        }
        before = Store.Current; endpoint = 0;
        if (Tool is EditorTool.Line or EditorTool.Arrow or EditorTool.Circle or EditorTool.Rectangle or EditorTool.Brush)
        {
            original = new SceneItem { Kind = Tool == EditorTool.Circle ? "circle" : Tool == EditorTool.Rectangle ? "rectangle" : Tool == EditorTool.Brush ? "path" : "line", X = point.X, Y = point.Y, X2 = point.X, Y2 = point.Y, Color = Color, Width = Width, Dashed = Dashed, Arrow = Tool == EditorTool.Arrow,
                Points = Tool == EditorTool.Brush ? new() { new(point.X, point.Y) } : new() };
            SelectedId = original.Id;
            Store.Preview(Store.Current with { Items = Store.Current.Items.Append(original).ToList() });
            endpoint = 2;
        }
        else
        {
            original = Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => Hit(i, point));
            SelectedId = original?.Id;
            if (original is null) { before = null; return; }
            if (original.Kind == "line" && original.Erasures.Count == 0)
                endpoint = (point - original.Start).Length < 14 ? 1 : (point - original.End).Length < 14 ? 2 : 0;
        }
    }
    public void Move(Point point, bool snap)
    {
        if (before is null) return;
        point = Clamp(point);
        if (selecting)
        {
            var distance = point - origin;
            if (!selectionMoved && Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            selectionMoved = true;
            if (original is null || extendSelection)
            {
                SelectionBox = new Rect(origin, point);
                selection.Clear();
                if (extendSelection) selection.UnionWith(selectionBefore!);
                foreach (var item in before.Items)
                {
                    var bounds = SelectionBounds(item);
                    if (!bounds.IsEmpty && SelectionBox.Value.Contains(bounds)) selection.Add(item.Id);
                }
                return;
            }
        }
        if (erasing) { Erase(point); return; }
        if (original is null) return;
        SceneItem next;
        if (endpoint == 2 && original.Kind == "path")
        {
            var live = Selected!;
            var last = live.Points[^1];
            if (new Vector(point.X - last.X, point.Y - last.Y).Length < 2 || live.Points.Count >= 20000) return;
            Store.Replace(live with { X2 = point.X, Y2 = point.Y, Points = live.Points.Append(new InkPoint(point.X, point.Y)).ToList() });
            return;
        }
        if (endpoint == 2 && original.Kind is "circle" or "rectangle")
        {
            var d = point - origin;
            if (snap) { double side = Math.Min(Math.Abs(d.X), Math.Abs(d.Y)); d = new Vector(Math.Sign(d.X) * side, Math.Sign(d.Y) * side); }
            Store.Replace(original with { X2 = origin.X + d.X, Y2 = origin.Y + d.Y });
            return;
        }
        if (endpoint != 0)
        {
            var anchor = endpoint == 1 ? original.End : original.Start;
            if (snap)
            {
                Vector v = point - anchor; double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * Math.PI / 4;
                point = Clamp(anchor + new Vector(Math.Cos(angle), Math.Sin(angle)) * v.Length);
            }
            next = endpoint == 1 ? original with { X = point.X, Y = point.Y } : original with { X2 = point.X, Y2 = point.Y };
        }
        else
        {
            var moving = selecting ? before.Items.Where(i => selection.Contains(i.Id)).ToArray() : new[] { original };
            var bounds = Rect.Empty;
            foreach (var item in moving) bounds.Union(MovementBounds(item));
            var delta = point - origin;
            delta.X = Math.Clamp(delta.X, -Math.Max(0, bounds.Left), Math.Max(0, Store.Current.Width - bounds.Right));
            delta.Y = Math.Clamp(delta.Y, -Math.Max(0, bounds.Top), Math.Max(0, Store.Current.Height - bounds.Bottom));
            var ids = moving.Select(i => i.Id).ToHashSet();
            Store.Preview(before with { Items = before.Items.Select(i => ids.Contains(i.Id) ? Translate(i, delta) : i).ToList() });
            return;
        }
        Store.Replace(next);
    }
    public void Up()
    {
        if (before is null) return;
        var saved = before; before = null;
        if (selecting && !selectionMoved && !extendSelection && original is not null) SelectedId = original.Id;
        SelectionBox = null;
        if ((!selecting || endpoint != 0) && !erasing && Selected is { } i && ((i.Kind == "line" && (i.End - i.Start).Length < 4) || (i.Kind is "circle" or "rectangle" && (Math.Abs(i.X2 - i.X) < 3 || Math.Abs(i.Y2 - i.Y) < 3)) || (i.Kind == "path" && i.Points.Count < 2))) Store.Preview(saved);
        else Store.Commit(saved);
        original = null; erasing = selecting = selectionMoved = false; selectionBefore = null;
    }
    public void Cancel()
    {
        if (selectionBefore is not null) { selection.Clear(); selection.UnionWith(selectionBefore); }
        SelectionBox = null; original = null; erasing = selecting = selectionMoved = false; selectionBefore = null;
        if (before is not null) { var s = before; before = null; Store.Preview(s); }
    }
    public void ResizeSelected(int direction)
    {
        if (Selected is not { Kind: "hero" } item) return;
        Store.Edit(s => s with { Items = s.Items.Select(i => i.Id == item.Id ? i with { Size = Math.Clamp(i.Size + direction * 8, 24, 400) } : i).ToList() });
    }
    public void Duplicate()
    {
        Cancel();
        var copies = SelectedItems.Select(i => Translate(i, new Vector(24, 24)) with { Id = Guid.NewGuid().ToString("N") }).ToList();
        if (copies.Count == 0) return;
        selection.Clear(); selection.UnionWith(copies.Select(i => i.Id));
        Store.Edit(s => s with { Items = s.Items.Concat(copies).ToList() });
    }
    public void Delete()
    {
        Cancel(); var ids = new HashSet<string>(selection);
        Store.Edit(s => s with { Items = s.Items.Where(i => !ids.Contains(i.Id)).ToList() });
    }
    private static SceneItem Translate(SceneItem item, Vector delta) => item with {
        X = item.X + delta.X, Y = item.Y + delta.Y, X2 = item.X2 + delta.X, Y2 = item.Y2 + delta.Y,
        Points = item.Points.Select(p => new InkPoint(p.X + delta.X, p.Y + delta.Y)).ToList(),
        Erasures = item.Erasures.Select(e => e with { X = e.X + delta.X, Y = e.Y + delta.Y, X2 = e.X2 + delta.X, Y2 = e.Y2 + delta.Y }).ToList() };
    private static Rect MovementBounds(SceneItem item)
    {
        if (item.Kind == "hero") return new Rect(item.X - item.Size / 2, item.Y - item.Size / 2, item.Size, item.Size);
        if (item.Kind == "path" && item.Points.Count > 0)
            return new Rect(new Point(item.Points.Min(p => p.X), item.Points.Min(p => p.Y)), new Point(item.Points.Max(p => p.X), item.Points.Max(p => p.Y)));
        return new Rect(item.Start, item.End);
    }
    public static Rect SelectionBounds(SceneItem item)
    {
        if (item.Kind == "hero") { var bounds = MovementBounds(item); bounds.Inflate(1.5, 1.5); return bounds; }
        return Ink(item).Bounds;
    }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Store.Current.Width), Math.Clamp(p.Y, 0, Store.Current.Height));
    private void Erase(Point point)
    {
        Geometry sweep = (point - lastErase).Length < 0.01
            ? new EllipseGeometry(point, EraserSize / 2, EraserSize / 2)
            : new LineGeometry(lastErase, point).GetWidenedPathGeometry(new Pen(Brushes.Black, EraserSize) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var items = new List<SceneItem>(); bool changed = false;
        foreach (var item in Store.Current.Items)
        {
            if (item.Kind == "hero") { items.Add(item); continue; }
            var ink = Ink(item);
            if (ink.FillContainsWithDetail(sweep) == IntersectionDetail.Empty) { items.Add(item); continue; }
            if (!EraseWhole && item.Erasures.Count >= 20000) { items.Add(item); continue; }
            changed = true;
            if (!EraseWhole) items.Add(item with { Erasures = item.Erasures.Append(new EraseMark(lastErase.X, lastErase.Y, point.X, point.Y, EraserSize / 2)).ToList() });
        }
        if (changed) Store.Preview(Store.Current with { Items = items });
        lastErase = point;
    }
    private static Geometry Ink(SceneItem item)
    {
        var outline = Outline(item);
        Geometry ink = outline.GetWidenedPathGeometry(new Pen(Brushes.Black, item.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
        if (item.Arrow) ink = Geometry.Combine(ink, outline, GeometryCombineMode.Union, null);
        if (item.Erasures.Count > 0) ink = Geometry.Combine(ink, ErasedArea(item), GeometryCombineMode.Exclude, null);
        return ink;
    }
    public static Geometry ErasedArea(SceneItem item)
    {
        var area = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var e in item.Erasures)
        {
            var a = new Point(e.X, e.Y); var b = new Point(e.X2, e.Y2);
            area.Children.Add((a - b).Length < .01 ? new EllipseGeometry(a, e.Radius, e.Radius) :
                new LineGeometry(a, b).GetWidenedPathGeometry(new Pen(Brushes.Black, e.Radius * 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }));
        }
        return area;
    }
    public static Geometry Outline(SceneItem i)
    {
        if (i.Kind == "circle") return new EllipseGeometry(new Rect(i.Start, i.End));
        if (i.Kind == "rectangle") return new RectangleGeometry(new Rect(i.Start, i.End));
        if (i.Kind == "path")
        {
            if (i.Points.Count < 2) return Geometry.Empty;
            var path = new StreamGeometry();
            using var g = path.Open();
            g.BeginFigure(new Point(i.Points[0].X, i.Points[0].Y), false, false);
            g.PolyLineTo(i.Points.Skip(1).Select(p => new Point(p.X, p.Y)).ToArray(), true, false);
            return path;
        }
        if (i.Arrow && (i.End - i.Start).Length > 1)
        {
            var direction = i.End - i.Start; double head = Math.Min(direction.Length * .7, Math.Max(16, i.Width * 3.5)); direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            var path = new StreamGeometry();
            using var g = path.Open();
            g.BeginFigure(i.Start, false, false); g.LineTo(i.End, true, false);
            g.BeginFigure(i.End, true, true);
            g.LineTo(i.End - direction * head + normal * head * .48, true, false);
            g.LineTo(i.End - direction * head - normal * head * .48, true, false);
            return path;
        }
        return new LineGeometry(i.Start, i.End);
    }
    public static bool Hit(SceneItem i, Point point)
    {
        if (i.Kind == "hero") return (point - i.Start).Length <= i.Size / 2 + 5;
        if (i.Erasures.Count > 0 && ErasedArea(i).FillContains(point)) return false;
        var outline = Outline(i);
        return (i.Arrow && outline.FillContains(point)) || outline.StrokeContains(new Pen(Brushes.Black, Math.Max(20, i.Width + 10)), point);
    }
}
