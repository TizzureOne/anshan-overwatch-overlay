using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

public static class SelfTests
{
    public static async Task RunAsync(string[] args)
    {
        string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "qa");
        Directory.CreateDirectory(output);
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; }
        await UpdateTests.RunAsync(Check, output);
        var manifest = Assets.LoadHeroes();
        var opacityCases = new (SceneItem Item, int X, int Y)[] {
            (new() { HeroId = "ana", X = 50, Y = 50, Size = 40, Opacity = .8 }, 50, 40),
            (new() { Kind = "line", X = 100, Y = 40, X2 = 160, Y2 = 40, Width = 8, Opacity = .6 }, 125, 40),
            (new() { Kind = "line", X = 190, Y = 40, X2 = 265, Y2 = 40, Width = 8, Arrow = true, Opacity = .7 }, 220, 40),
            (new() { Kind = "circle", X = 25, Y = 100, X2 = 75, Y2 = 150, Width = 8, Opacity = .9 }, 50, 100),
            (new() { Kind = "rectangle", X = 105, Y = 100, X2 = 165, Y2 = 150, Width = 8, Opacity = .5 }, 130, 100),
            (new() { Kind = "path", Points = new() { new(200, 120), new(230, 120), new(260, 150) }, Width = 8, Opacity = .4 }, 215, 120) };
        var opacityScene = new Scene { Width = 300, Height = 180, ShowHeroNames = true, Items = opacityCases.Select(c => c.Item).ToList() };
        string originalOpacityScene = JsonSerializer.Serialize(opacityScene, Assets.Json);
        var opacityBoard = new BoardView { Scene = opacityScene, Heroes = manifest.Heroes };
        foreach (double factor in new[] { 0d, .5, 1d })
        {
            opacityBoard.PassThroughOpacity = factor; opacityBoard.InvalidateVisual();
            string image = Path.Combine(output, $"pass-through-opacity-{factor * 100:0}.png"); Render(opacityBoard, 300, 180, image);
            foreach (var sample in opacityCases)
                Check(Math.Abs(PixelAlpha(image, sample.X, sample.Y) - sample.Item.Opacity * factor * 255) <= 2, $"pass-through opacity multiplies the original opacity for {sample.Item.Kind} arrow={sample.Item.Arrow} at {factor}");
            Check(Math.Abs(PixelAlpha(image, 50, 77) - .8 * factor * 255) <= 2, "hero name fades with the rest of its avatar at " + factor);
        }
        opacityBoard.PassThroughOpacity = 0; opacityBoard.DrawEditorChrome = false; opacityBoard.InvalidateVisual();
        string cleanOpacityFile = Path.Combine(output, "pass-through-opacity-obs.png"); Render(opacityBoard, 300, 180, cleanOpacityFile);
        foreach (var sample in opacityCases)
            Check(Math.Abs(PixelAlpha(cleanOpacityFile, sample.X, sample.Y) - sample.Item.Opacity * 255) <= 2, "clean output preserves original opacity for " + sample.Item.Kind);
        opacityBoard.DrawEditorChrome = true; opacityBoard.Editing = true; opacityBoard.InvalidateVisual();
        string editOpacityFile = Path.Combine(output, "pass-through-opacity-edit.png"); Render(opacityBoard, 300, 180, editOpacityFile);
        foreach (var sample in opacityCases)
            Check(Math.Abs(PixelAlpha(editOpacityFile, sample.X, sample.Y) - sample.Item.Opacity * 255) <= 2, "editing restores original opacity for " + sample.Item.Kind);
        Check(JsonSerializer.Serialize(opacityScene, Assets.Json) == originalOpacityScene, "mode opacity never changes the stored scene or individual object opacity");
        Check(manifest.Count == manifest.Heroes.Count && manifest.Count >= 54, "complete official roster");
        Check(manifest.Heroes.Select(h => h.Id).Distinct().Count() == manifest.Count, "unique hero IDs");
        var doctrine = manifest.Heroes.Single(h => h.Id == "doctrine");
        Check(doctrine.Name == "血律" && doctrine.Role == "support" && doctrine.Matches("xuelv") && doctrine.Matches("Doctrine"), "Doctrine is available by verified Chinese name and search aliases");
        var sombra = manifest.Heroes.Single(h => h.Id == "sombra");
        Check(sombra.Role == "support" && sombra.Matches("黑影") && sombra.Matches("hy"), "Sombra is classified as support and remains searchable");
        using var document = JsonDocument.Parse(Assets.Read("Assets/heroes.json"));
        foreach (var hero in document.RootElement.GetProperty("heroes").EnumerateArray())
        {
            var id = hero.GetProperty("id").GetString()!;
            var data = Assets.Read($"Assets/Heroes/{id}.png");
            Check(Convert.ToHexString(SHA256.HashData(data)).Equals(hero.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "asset hash " + id);
            var image = Assets.Portrait(id); Check(image.PixelWidth == 256 && image.PixelHeight == 256, "asset dimensions " + id);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); var pixels = new byte[256 * 256 * 4]; converted.CopyPixels(pixels, 256 * 4, 0);
            Check(pixels[3] == 0 && pixels[(256 * 256 - 1) * 4 + 3] == 0, "prebaked transparent corners " + id);
        }
        var store = new SceneStore(); var editor = new Editor(store) { PendingHero = manifest.Heroes.First(h => h.Id == "ana"), Tool = EditorTool.Hero };
        editor.Down(new Point(350, 300)); Check(store.Current.Items.Count == 1, "hero placement");
        editor.Down(new Point(350, 300)); editor.Move(new Point(500, 400), false); editor.Up();
        Check(store.Current.Items[0].X == 500, "drag moves hero"); store.Undo(); Check(store.Current.Items[0].X == 350, "one undo reverses whole drag");
        store.Redo(); Check(store.Current.Items[0].X == 500, "redo drag");
        editor.Down(new Point(500, 400)); editor.Move(new Point(600, 500), false); editor.Cancel(); Check(store.Current.Items[0].X == 500, "cancel restores drag");
        editor.Tool = EditorTool.Arrow; editor.Dashed = true; editor.Color = "#FF6577";
        editor.Down(new Point(600, 400)); editor.Move(new Point(1000, 600), true); editor.Up();
        Check(store.Current.Items.Count == 2 && store.Current.Items[1].Arrow && store.Current.Items[1].Dashed, "dashed arrow");
        var line = store.Current.Items[1]; Check(Math.Abs(line.X2 - line.X - (line.Y2 - line.Y)) < 0.01, "45-degree snap");
        editor.Tool = EditorTool.Hero; editor.PendingHero = null; editor.Down(line.End); editor.Move(new Point(1100, 620), false); editor.Up();
        Check(store.Current.Items[1].X2 == 1100, "line endpoint editing");
        store.Clear(true); Check(store.Current.Items.Count == 1, "clear lines preserves heroes"); store.Undo();
        string json = store.Serialize(); Check(SceneStore.Parse(json, manifest.Heroes).Items.Count == 2, "save load roundtrip");
        bool rejected = false; try { SceneStore.Parse(json.Replace("#FF6577", "invalid"), manifest.Heroes); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "reject corrupt scene");
        var scaled = SceneStore.Scale(store.Current, 3840, 2160); Check(scaled.Items[0].X == store.Current.Items[0].X * 2, "4K coordinate scaling");
        var resizedHistory = new SceneStore();
        resizedHistory.Edit(s => s with { Items = new() { new SceneItem { HeroId = "ana", X = 100, Y = 100 } } });
        resizedHistory.Resize(3840, 2160); resizedHistory.Undo();
        Check(resizedHistory.Current.Width == 3840 && resizedHistory.Current.Items.Count == 0, "undo after monitor change preserves viewport");
        resizedHistory.Redo(); Check(resizedHistory.Current.Items[0].X == 200, "redo scales with monitor");
        var portrait2 = manifest.Heroes.First(h => h.Id == "genji");
        store.Edit(s => s with { Items = s.Items.Append(new SceneItem { HeroId = portrait2.Id, X = 1150, Y = 620, Size = 112, Team = "enemy" }).ToList() });
        Check(store.Current.Items[0].HeroBorder == "#39A9FF" && store.Current.Items[^1].HeroBorder == "#F05265", "hero borders restricted to faction colors");
        var newStore = new SceneStore(); var newEditor = new Editor(newStore);
        newEditor.Tool = EditorTool.Brush; newEditor.Width = 9; newEditor.Color = "#FFFFFF";
        newEditor.Tool = EditorTool.Arrow; newEditor.Width = 3;
        newEditor.Tool = EditorTool.Brush; Check(newEditor.Width == 9 && newEditor.Color == "#FFFFFF", "per-tool parameters persist independently");
        newEditor.Down(new Point(100, 100)); newEditor.Move(new Point(120, 160), false); newEditor.Move(new Point(200, 140), false); newEditor.Up();
        Check(newStore.Current.Items.Single().Kind == "path" && newStore.Current.Items[0].Points.Count == 3, "freehand stroke records points");
        newStore.Undo(); Check(newStore.Current.Items.Count == 0, "one undo removes entire brush stroke"); newStore.Redo();
        newEditor.Tool = EditorTool.Circle; newEditor.Down(new Point(300, 300)); newEditor.Move(new Point(480, 450), false); newEditor.Up();
        Check(newStore.Current.Items[1].Kind == "circle" && newStore.Current.Items[1].X2 == 480 && newStore.Current.Items[1].Y2 == 450, "ellipse has independent axes");
        Check(Editor.Hit(newStore.Current.Items[1], new Point(480, 375)) && !Editor.Hit(newStore.Current.Items[1], new Point(375, 375)), "circle hit testing uses outline");
        newEditor.Tool = EditorTool.Line; newEditor.Dashed = true; newEditor.Down(new Point(20, 20)); newEditor.Move(new Point(80, 80), false); newEditor.Up();
        Check(newStore.Current.Items[^1].Dashed, "global dashed mode applies to line");
        Check(SceneStore.Parse(newStore.Serialize(), manifest.Heroes).Items.Count == 3, "new shapes survive scene roundtrip");
        var resizedPath = SceneStore.Scale(newStore.Current, 3840, 2160).Items[0]; Check(resizedPath.Points[1].X == 240, "brush points scale with display");
        newEditor.Tool = EditorTool.Hero; newEditor.Down(new Point(120, 160)); newEditor.Move(new Point(120, 1080), false); newEditor.Up();
        Check(newStore.Current.Items[0].Points.Max(p => p.Y) == 1080, "dragging brush stroke clamps all points within viewport");
        newStore.Undo();
        var modes = new[] { EditorTool.Move, EditorTool.Hero, EditorTool.Brush, EditorTool.Line, EditorTool.Arrow, EditorTool.Circle, EditorTool.Rectangle, EditorTool.Eraser };
        var interaction = new SceneStore(); var action = new Editor(interaction) { PendingHero = manifest.Heroes[0] };
        foreach (var tool in modes)
        {
            interaction.Preview(new Scene { Items = new() { new SceneItem { HeroId = "ana", X = 500, Y = 300 } } });
            action.Tool = tool; action.Down(new Point(500, 300)); action.Move(new Point(620, 400), false); action.Up();
            Check(interaction.Current.Items.Count == 1 && interaction.Current.Items[0].X == 620 && action.Tool == tool, "hero drag takes priority and preserves tool " + tool);
        }
        foreach (bool dash in new[] { true, false })
        foreach (var tool in modes.Where(t => t != EditorTool.Move && t != EditorTool.Hero && t != EditorTool.Eraser))
        {
            interaction.Preview(new Scene()); action.Dashed = dash; action.Tool = tool;
            action.Down(new Point(500, 400)); action.Move(new Point(300, 300), false); action.Up();
            var shape = interaction.Current.Items.Single();
            Check(shape.Dashed == dash, "global dash shared by " + tool + " " + dash);
            if (tool is EditorTool.Circle or EditorTool.Rectangle)
                Check(shape.X2 == 300 && shape.Y2 == 300 && Editor.Hit(shape, new Point(300, 350)) && !Editor.Hit(shape, new Point(400, 350)), "reverse drag and outline hit " + tool);
            var parsed = SceneStore.Parse(interaction.Serialize(), manifest.Heroes);
            Check(parsed.DrawingDashed == dash && parsed.Items.Single().Kind == shape.Kind, "shape and global dash survive save " + tool);
        }
        foreach (var tool in new[] { EditorTool.Circle, EditorTool.Rectangle })
        {
            interaction.Preview(new Scene()); action.Tool = tool; action.Down(new Point(500, 400)); action.Move(new Point(700, 500), true); action.Up();
            Check(interaction.Current.Items[0].X2 == 600 && interaction.Current.Items[0].Y2 == 500, "shift constrains aspect " + tool);
            interaction.Preview(new Scene()); action.Down(new Point(500, 400)); action.Move(new Point(700, 401), false); action.Up();
            Check(interaction.Current.Items.Count == 0, "discard flat shape " + tool);
        }
        var eraseItems = new System.Collections.Generic.List<SceneItem> {
            new() { HeroId = "ana", X = 450, Y = 300 },
            new() { Kind = "line", X = 250, Y = 250, X2 = 250, Y2 = 350 },
            new() { Kind = "circle", X = 300, Y = 200, X2 = 600, Y2 = 400 },
            new() { Kind = "rectangle", X = 650, Y = 200, X2 = 750, Y2 = 400 },
            new() { Kind = "path", X = 800, Y = 250, X2 = 800, Y2 = 350, Points = new() { new(800, 250), new(800, 350) } } };
        interaction.Preview(new Scene { Items = eraseItems }); action.Tool = EditorTool.Eraser; action.EraseWhole = true;
        action.Down(new Point(200, 300)); action.Move(new Point(850, 300), false); action.Up();
        Check(interaction.Current.Items.Count == 1 && interaction.Current.Items[0].Kind == "hero", "eraser sweep hits all crossed shapes and protects heroes");
        interaction.Undo(); Check(interaction.Current.Items.Count == 5, "one undo restores full eraser gesture");
        interaction.Redo(); Check(interaction.Current.Items.Count == 1, "redo eraser gesture"); interaction.Undo();
        action.Down(new Point(250, 300)); action.Move(new Point(850, 300), false); action.Cancel();
        Check(interaction.Current.Items.Count == 5, "cancel eraser restores removed marks");
        foreach (string kind in new[] { "circle", "rectangle" })
        {
            interaction.Preview(new Scene { Items = new() { new SceneItem { Kind = kind, X = 100, Y = 100, X2 = 400, Y2 = 300 } } });
            action.Down(new Point(250, 200)); action.Up();
            Check(interaction.Current.Items.Count == 1, "eraser ignores empty interior " + kind);
        }
        interaction.Preview(new Scene { Items = new() { new SceneItem { Kind = "line", Arrow = true, Width = 16, X = 100, Y = 100, X2 = 300, Y2 = 100 } } });
        action.EraserSize = 8; action.Down(new Point(247, 125)); action.Up();
        Check(interaction.Current.Items.Count == 0, "eraser catches arrowhead away from shaft");
        interaction.Preview(new Scene { ShowHeroNames = true, DrawingDashed = true, Items = eraseItems });
        var namesRoundtrip = SceneStore.Parse(interaction.Serialize(), manifest.Heroes);
        Check(namesRoundtrip.ShowHeroNames && namesRoundtrip.DrawingDashed, "global switches saved and restored");
        var legacyNames = "{\"items\":[{\"heroId\":\"ana\",\"label\":true}]}";
        Check(SceneStore.Parse(legacyNames, manifest.Heroes).ShowHeroNames, "legacy individual names migrate to global switch");
        var partialStore = new SceneStore(); var partial = new Editor(partialStore) { Tool = EditorTool.Eraser, EraserSize = 40 };
        var markedLine = new SceneItem { Kind = "line", X = 100, Y = 100, X2 = 300, Y2 = 100, Width = 10, Opacity = .5, Color = "#F05265" };
        partialStore.Preview(new Scene { Width = 400, Height = 200, Items = new() { markedLine } });
        partial.Down(new Point(200, 100)); partial.Up();
        var cutLine = partialStore.Current.Items.Single();
        Check(cutLine.Erasures.Count == 1 && !Editor.Hit(cutLine, new Point(200, 100)) && Editor.Hit(cutLine, new Point(150, 100)), "partial erase removes hit area and preserves other ink");
        partialStore.Undo(); Check(partialStore.Current.Items[0].Erasures.Count == 0, "partial erase undo"); partialStore.Redo();
        Check(SceneStore.Parse(partialStore.Serialize(), manifest.Heroes).Items[0].Opacity == .5, "opacity and partial erase roundtrip");
        var inkPreview = new BoardView { Scene = partialStore.Current, Width = 400, Height = 200, DrawEditorChrome = false };
        string inkFile = Path.Combine(output, "partial-erase.png"); Render(inkPreview, 400, 200, inkFile);
        using (var pixelsFile = File.OpenRead(inkFile))
        {
            var image = BitmapFrame.Create(pixelsFile, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); var pixels = new byte[400 * 200 * 4]; bgra.CopyPixels(pixels, 400 * 4, 0);
            Check(pixels[(100 * 400 + 200) * 4 + 3] == 0, "WPF erased pixels are transparent");
            Check(Math.Abs(pixels[(100 * 400 + 150) * 4 + 3] - 128) <= 1, "WPF selected opacity renders correctly");
            Check(pixels[(108 * 400 + 150) * 4 + 3] == 0, "WPF line has no outer black stroke");
        }
        partial.Tool = EditorTool.Move; partial.Down(new Point(150, 100)); partial.Move(new Point(200, 150), false); partial.Up();
        var movedInk = partialStore.Current.Items[0];
        Check(movedInk.X == 150 && movedInk.Erasures[0].X == 250 && !Editor.Hit(movedInk, new Point(250, 150)), "move tool moves ink and erased area together");
        var scaledCut = SceneStore.Scale(partialStore.Current, 800, 400).Items[0];
        Check(scaledCut.Erasures[0].X == 500 && scaledCut.Erasures[0].Radius == 40, "partial erasure scales with monitor");
        partial.Duplicate(); Check(partialStore.Current.Items[1].Erasures[0].X == 274, "duplicate preserves translated erased area");
        foreach (var item in eraseItems.Where(i => i.Kind != "hero"))
        {
            partialStore.Preview(new Scene { Items = new() { item } }); partial.Tool = EditorTool.Move;
            var hitPoint = item.Kind == "circle" ? new Point(item.X, (item.Y + item.Y2) / 2) : item.Start;
            partial.Down(hitPoint); partial.Move(hitPoint + new Vector(40, 30), false); partial.Up();
            Check(partialStore.Current.Items[0] != item, "move tool edits shape " + item.Kind);
        }
        var selectionStore = new SceneStore(); var selectionEditor = new Editor(selectionStore) { Tool = EditorTool.Move };
        var firstHero = new SceneItem { HeroId = "ana", X = 120, Y = 120, Size = 60 };
        var secondHero = new SceneItem { HeroId = "genji", X = 260, Y = 120, Size = 60 };
        var selectionLine = cutLine with { X = 100, Y = 230, X2 = 300, Y2 = 230, Erasures = new() { new(200, 230, 200, 230, 12) } };
        var crossingArrow = new SceneItem { Kind = "line", Arrow = true, X = 20, Y = 310, X2 = 750, Y2 = 310, Width = 10 };
        var selectionScene = new Scene { Width = 800, Height = 600, Items = new() { firstHero, secondHero, selectionLine, crossingArrow } };
        selectionStore.Preview(selectionScene);
        selectionEditor.Down(firstHero.Start); selectionEditor.Up();
        Check(selectionEditor.SelectedId == firstHero.Id, "plain click selects one object");
        selectionEditor.Down(secondHero.Start, true); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.SetEquals(new[] { firstHero.Id, secondHero.Id }) && selectionEditor.Selected is null, "shift click adds without a primary single selection");
        selectionEditor.Down(firstHero.Start, true); selectionEditor.Up();
        Check(selectionEditor.SelectedId == secondHero.Id, "shift click removes an already selected object");
        selectionEditor.Down(new Point(140, 230), true); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.Count == 2, "heroes and partially erased ink can be selected together");
        selectionEditor.Down(secondHero.Start); selectionEditor.Up();
        Check(selectionEditor.SelectedId == secondHero.Id, "plain click on a selected member collapses a group on release");
        selectionEditor.Down(secondHero.Start); selectionEditor.Move(secondHero.Start + new Vector(1, 1), false); selectionEditor.Up();
        Check(selectionStore.Current == selectionScene, "minor pointer jitter does not move a clicked object");
        selectionEditor.Down(new Point(700, 550)); selectionEditor.Move(new Point(701, 551), false); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.Count == 0 && selectionEditor.SelectionBox is null, "blank click and minor pointer jitter clear selection without a marquee");
        selectionEditor.Down(new Point(60, 60)); selectionEditor.Move(new Point(330, 270), false);
        Check(selectionEditor.SelectionBox is not null && selectionEditor.SelectedIds.Count == 3, "marquee previews fully enclosed mixed objects");
        selectionEditor.Up();
        Check(selectionEditor.SelectionBox is null && selectionEditor.SelectedIds.Count == 3, "marquee commits selection and removes its preview");
        selectionEditor.Down(new Point(330, 270)); selectionEditor.Move(new Point(60, 60), false); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.Count == 3, "reverse marquee uses the same full containment rule");
        selectionEditor.Down(new Point(170, 80)); selectionEditor.Move(new Point(290, 170), false); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.Count == 0, "marquee excludes a partially enclosed hero including its border");
        selectionEditor.Down(new Point(0, 0)); selectionEditor.Move(new Point(400, 340), false); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.Count == 3 && !selectionEditor.SelectedIds.Contains(crossingArrow.Id), "marquee excludes an arrow that only crosses the box");
        selectionEditor.Down(secondHero.Start); selectionEditor.Up();
        selectionEditor.Down(new Point(80, 80), true); selectionEditor.Move(new Point(330, 270), false);
        Check(selectionEditor.SelectedIds.Count == 3, "shift marquee appends to the original selection");
        selectionEditor.Move(new Point(170, 170), false); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.SetEquals(new[] { firstHero.Id, secondHero.Id }), "shrinking an additive marquee keeps only its baseline and current hits");
        selectionEditor.Down(new Point(600, 500), true); selectionEditor.Move(new Point(700, 550), false); selectionEditor.Up();
        Check(selectionEditor.SelectedIds.Count == 2, "empty additive marquee preserves previous selection");
        selectionEditor.Down(new Point(600, 500)); selectionEditor.Move(new Point(700, 550), false); selectionEditor.Cancel();
        Check(selectionEditor.SelectedIds.Count == 2 && selectionEditor.SelectionBox is null, "cancel marquee restores the previous selection");
        selectionEditor.Down(secondHero.Start); selectionEditor.Up();
        selectionEditor.Down(firstHero.Start, true); selectionEditor.Move(new Point(330, 270), false); selectionEditor.Up();
        Check(selectionEditor.SelectedId == secondHero.Id && selectionStore.Current == selectionScene, "shift drag can begin over an object without moving it");
        Check(!selectionStore.CanUndo && !selectionStore.CanRedo, "selection-only gestures do not create scene undo entries");
        selectionEditor.Down(new Point(60, 60)); selectionEditor.Move(new Point(330, 270), false); selectionEditor.Up();
        selectionEditor.Down(firstHero.Start); selectionEditor.Move(new Point(160, 150), false); selectionEditor.Up();
        Check(selectionStore.Current.Items.Take(3).Zip(selectionScene.Items, (moved, old) => moved.X == old.X + 40 && moved.Y == old.Y + 30).All(ok => ok), "dragging a selected hero translates the whole mixed selection");
        Check(selectionStore.Current.Items[2].Erasures[0].X == 240 && selectionStore.Current.Items[3] == crossingArrow, "group movement translates erasures and leaves unselected objects unchanged");
        selectionStore.Undo();
        Check(selectionStore.Current == selectionScene && !selectionStore.CanUndo, "one undo restores the entire group move");
        selectionEditor.Down(firstHero.Start, true); selectionEditor.Up(); selectionEditor.Down(firstHero.Start, true); selectionEditor.Up();
        Check(selectionStore.CanRedo, "selection changes preserve scene redo history");
        selectionStore.Redo(); Check(selectionStore.Current.Items[0].X == 160, "redo restores the group move"); selectionStore.Undo();
        selectionEditor.Down(firstHero.Start); selectionEditor.Move(new Point(800, 600), false); selectionEditor.Up();
        Check(selectionStore.Current.Items[0].X == 620 && selectionStore.Current.Items[1].X == 760 && selectionStore.Current.Items[2].X2 == 800, "group movement clamps as a unit and preserves relative spacing");
        selectionStore.Undo();
        selectionEditor.Down(firstHero.Start); selectionEditor.Move(new Point(180, 180), false); selectionEditor.Cancel();
        Check(selectionStore.Current == selectionScene && selectionEditor.SelectedIds.Count == 3, "cancel group move restores geometry and selection");
        selectionEditor.ResizeSelected(1);
        Check(selectionStore.Current == selectionScene, "mouse wheel does not resize an arbitrary member of a multi-selection");
        selectionEditor.Duplicate();
        var copies = selectionEditor.SelectedItems;
        Check(copies.Length == 3 && selectionStore.Current.Items.Count == 7 && copies.All(i => selectionScene.Items.All(old => old.Id != i.Id)), "duplicate copies and selects every selected object with new IDs");
        Check(copies[0].X == 144 && copies[1].X == 284 && copies[2].Erasures[0].X == 224, "group copies preserve their offsets and erased areas");
        selectionEditor.Delete();
        Check(selectionStore.Current.Items.SequenceEqual(selectionScene.Items) && selectionEditor.SelectedIds.Count == 0, "delete removes the complete selection and clears stale IDs");
        selectionStore.Undo(); Check(selectionStore.Current.Items.Count == 7, "one undo restores a deleted selection");
        selectionStore.Undo(); Check(selectionStore.Current.Items.SequenceEqual(selectionScene.Items), "one undo removes the whole duplicated selection");
        var wideArrow = new SceneItem { Kind = "line", Arrow = true, X = 150, Y = 100, X2 = 300, Y2 = 100, Width = 20 };
        var boundsStore = new SceneStore(); var boundsEditor = new Editor(boundsStore) { Tool = EditorTool.Move };
        boundsStore.Preview(new Scene { Items = new() { wideArrow, firstHero with { X = 100, Y = 300 } } });
        boundsEditor.Down(new Point(100, 80)); boundsEditor.Move(new Point(330, 120), false); boundsEditor.Up();
        Check(boundsEditor.SelectedIds.Count == 0, "full containment includes the arrowhead instead of only its endpoints");
        boundsEditor.Down(new Point(50, 40)); boundsEditor.Move(new Point(350, 350), false); boundsEditor.Up();
        boundsEditor.Down(wideArrow.End); boundsEditor.Move(wideArrow.End + new Vector(40, 20), false); boundsEditor.Up();
        Check(boundsStore.Current.Items[0].X == 190 && boundsStore.Current.Items[0].X2 == 340 && boundsStore.Current.Items[1].X == 140, "multi-selection moves arrows from their endpoints without stretching them");
        boundsStore.Preview(new Scene { Items = new() { wideArrow } });
        boundsEditor.Down(wideArrow.End); boundsEditor.Move(wideArrow.Start, false); boundsEditor.Up();
        Check(boundsStore.Current.Items[0] == wideArrow, "single endpoint editing cannot collapse a line to zero length");
        foreach (var item in eraseItems.Where(i => i.Kind is "circle" or "rectangle" or "path"))
        {
            boundsStore.Preview(new Scene { Items = new() { item } });
            var box = Editor.SelectionBounds(item); box.Inflate(20, 20);
            boundsEditor.Down(box.TopLeft); boundsEditor.Move(box.BottomRight, false); boundsEditor.Up();
            Check(boundsEditor.SelectedId == item.Id, "marquee encloses the full geometry of " + item.Kind);
        }
        foreach (var tool in new[] { EditorTool.Brush, EditorTool.Line, EditorTool.Arrow, EditorTool.Circle, EditorTool.Rectangle })
        {
            boundsStore.Preview(new Scene()); boundsEditor.Tool = tool;
            boundsEditor.Down(new Point(100, 100), true); boundsEditor.Move(new Point(200, 150), true); boundsEditor.Up();
            Check(boundsStore.Current.Items.Count == 1 && boundsEditor.SelectionBox is null, "shift drawing remains drawing instead of marquee selection " + tool);
        }
        var selectionView = new BoardView { Scene = selectionScene, Width = 800, Height = 600, Heroes = manifest.Heroes, Editing = true,
            SelectedIds = selectionScene.Items.Take(3).Select(i => i.Id).ToHashSet(), SelectionBox = new Rect(new Point(60, 60), new Point(330, 270)) };
        string selectionFile = Path.Combine(output, "selection-chrome.png"); Render(selectionView, 800, 600, selectionFile);
        Check(PixelAlpha(selectionFile, 159, 120) > 200 && PixelAlpha(selectionFile, 70, 70) > 20, "selected hero outlines and translucent marquee render during editing");
        selectionView.DrawEditorChrome = false; selectionView.InvalidateVisual();
        string cleanSelectionFile = Path.Combine(output, "selection-clean.png"); Render(selectionView, 800, 600, cleanSelectionFile);
        Check(PixelAlpha(cleanSelectionFile, 159, 120) == 0 && PixelAlpha(cleanSelectionFile, 70, 70) == 0, "selection outlines and marquee stay out of clean output");
        selectionView.DrawEditorChrome = true; selectionView.InvalidateVisual();
        var selectionBackground = new System.Windows.Controls.Grid { Background = BoardView.Brush("#263746") };
        selectionBackground.Children.Add(selectionView); Render(selectionBackground, 800, 600, Path.Combine(output, "multi-selection-preview.png"));
        var settings = new Preferences(); Check(Preferences.Validate(settings.Shortcuts) is null, "default shortcuts unique");
        settings.Shortcuts["brush"] = new Shortcut(Key.F8, ModifierKeys.Control);
        Check(Preferences.Validate(settings.Shortcuts) is null, "custom shortcut accepted");
        settings.Shortcuts["line"] = settings.Shortcuts["brush"];
        Check(Preferences.Validate(settings.Shortcuts) is not null, "duplicate shortcut rejected");
        settings.Shortcuts["line"] = new Shortcut(Key.None);
        Check(Preferences.Validate(settings.Shortcuts) is null, "shortcut can be unassigned");
        var restoredSettings = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings, Assets.Json), Assets.Json)!;
        Check(restoredSettings.Shortcuts["brush"].Key == Key.F8, "shortcut settings roundtrip");
        var layoutWindow = new Window { Left = 140, Top = 90, Width = 474, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var windowSettings = new Preferences { PanelPosition = WindowLayout.Capture(layoutWindow), ToolbarPosition = new(-920, 210), PassThroughOpacityPercent = 37 };
        string windowSettingsFile = Path.Combine(output, "window-settings.json"); File.WriteAllText(windowSettingsFile, JsonSerializer.Serialize(windowSettings, Assets.Json));
        var restoredWindowSettings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(windowSettingsFile), Assets.Json)!;
        layoutWindow.Left = 0; layoutWindow.Top = 0; WindowLayout.Restore(layoutWindow, restoredWindowSettings.PanelPosition);
        Check(layoutWindow.WindowStartupLocation == WindowStartupLocation.Manual && layoutWindow.Left == 140 && layoutWindow.Top == 90, "saved panel position overrides the old centered startup location");
        WindowLayout.Restore(layoutWindow, restoredWindowSettings.ToolbarPosition);
        Check(layoutWindow.Left == -920 && layoutWindow.Top == 210 && restoredWindowSettings.PassThroughOpacityPercent == 37, "negative secondary-monitor coordinates and opacity survive settings serialization");
        WindowLayout.Restore(layoutWindow, new(double.NaN, 10));
        Check(layoutWindow.Left == -920 && layoutWindow.Top == 210, "invalid saved coordinates do not overwrite a valid window position");
        Check(WindowLayout.FitPosition(new Rect(140, 90, 474, 800), new Rect(0, 0, 1920, 1040)) == new Point(140, 90), "visible saved positions remain unchanged");
        Check(WindowLayout.FitPosition(new Rect(2400, 1300, 474, 800), new Rect(0, 0, 1920, 1040)) == new Point(1446, 240), "removed monitors restore windows inside the current work area");
        Check(WindowLayout.FitPosition(new Rect(-1800, -200, 680, 220), new Rect(-1920, -1080, 1920, 1080)) == new Point(-1800, -220), "work area clamping supports monitors above and to the left");
        Check(WindowLayout.FitPosition(new Rect(-500, -500, 2000, 1200), new Rect(0, 0, 1280, 720)) == new Point(0, 0), "oversized restored windows retain a reachable top-left corner");
        layoutWindow.Close();
        Check(Assets.AppIcon.PixelWidth == 256 && Assets.Read("Assets/app.ico")[2] == 1, "embedded application icons available");
        using (var extracted = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!))
        {
            using var bitmap = extracted!.ToBitmap(); int orange = 0;
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            { var c = bitmap.GetPixel(x, y); if (c.R > 220 && c.G > 90 && c.G < 200 && c.B < 100) orange++; }
            Check(orange > 5, "executable contains original orange application icon");
        }
        store.Edit(s => s with { ShowHeroNames = true });
        store.Edit(s => s with { Items = s.Items.Concat(newStore.Current.Items).ToList() });
        var view = new BoardView { Scene = store.Current, Width = 1920, Height = 1080, Heroes = manifest.Heroes, DrawEditorChrome = false };
        Render(view, 1920, 1080, Path.Combine(output, "overlay-transparent.png"));
        var background = new System.Windows.Controls.Grid { Background = BoardView.Brush("#263746"), Width = 1920, Height = 1080 };
        background.Children.Add(view); Render(background, 1920, 1080, Path.Combine(output, "overlay-preview.png"));
        var controller = new Controller(test: true);
        string beforeOpacitySettings = controller.Store.Serialize(); bool undoBeforeOpacity = controller.Store.CanUndo;
        foreach (int percent in new[] { 0, 25, 50, 100 })
        {
            controller.Panel.PassThroughOpacity.Value = percent;
            Check(controller.Settings.PassThroughOpacityPercent == percent && controller.Panel.PassThroughOpacityText.Text == percent + "%" && controller.Overlay.Board.PassThroughOpacity == percent / 100d, "settings slider immediately updates all desktop annotations at " + percent);
        }
        controller.SetPassThroughOpacity(-10); Check(controller.Settings.PassThroughOpacityPercent == 0, "pass-through opacity clamps below zero");
        controller.SetPassThroughOpacity(110); Check(controller.Settings.PassThroughOpacityPercent == 100, "pass-through opacity clamps above one hundred");
        controller.SetPassThroughOpacity(50);
        Check(controller.Store.Serialize() == beforeOpacitySettings && controller.Store.CanUndo == undoBeforeOpacity, "opacity preference changes leave the scene and undo history intact");
        var overlayHandle = new WindowInteropHelper(controller.Overlay).EnsureHandle();
        Check(!controller.Overlay.IsVisible && Native.GetProp(overlayHandle, "NonRudeHWND") == new IntPtr(1), "overlay opts out of fullscreen taskbar suppression before it is shown");
        string beforePlacement = controller.Store.Serialize();
        controller.Overlay.Place(controller.Screen);
        Check(Native.GetProp(overlayHandle, "NonRudeHWND") == new IntPtr(1) && !controller.Overlay.IsVisible && controller.Store.Serialize() == beforePlacement, "screen placement preserves taskbar compatibility and full scene dimensions without showing a test window");
        Check(controller.Panel.VersionText.Text == BuildInfo.Label && controller.Panel.Title.Contains(BuildInfo.Version) && controller.Panel.CurrentVersionText.Text.Contains(BuildInfo.Version), "all visible version labels read the built application version");
        Check(BuildInfo.Edition is "Full" or "Lite" && BuildInfo.Version.StartsWith(typeof(BuildInfo).Assembly.GetName().Version!.ToString(3)), "version and installer edition are embedded in the built assembly");
        var legacyPreferences = JsonSerializer.Deserialize<Preferences>("{}", Assets.Json)!;
        Check(legacyPreferences.AutomaticallyCheckUpdates && legacyPreferences.IncludeBetaUpdates == BuildInfo.IsBeta && legacyPreferences.AnimateInterface && legacyPreferences.PassThroughOpacityPercent == 50 && legacyPreferences.PanelPosition is null && legacyPreferences.ToolbarPosition is null, "old settings get defaults for updates opacity and missing window positions");
        controller.ApplyUpdatePreferences(false, false, false);
        var updatePreferences = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(controller.Settings, Assets.Json), Assets.Json)!;
        Check(!updatePreferences.AutomaticallyCheckUpdates && !updatePreferences.IncludeBetaUpdates && !updatePreferences.AnimateInterface && !Motion.CanAnimate, "update and motion preferences roundtrip without touching user settings");
        controller.ApplyUpdatePreferences(true, BuildInfo.IsBeta, true);
        controller.SetPassThrough(true); controller.SetPassThrough(false);
        controller.PlaceHero(manifest.Heroes[0], new Point(340, 260));
        controller.Panel.FinishHeroDrag(true);
        Check(controller.PassThrough && controller.Store.Current.Items.Count == 1 && !controller.Overlay.Board.Editing, "successful hero drag restores the original click-through mode and keeps the dropped hero");
        Check((Native.GetWindowLong(new WindowInteropHelper(controller.Overlay).Handle, -20) & Native.ExTransparent) != 0, "successful drag also restores the native mouse pass-through flag");
        controller.SetPassThrough(false); controller.Panel.FinishHeroDrag(false);
        Check(!controller.PassThrough && controller.Panel.ModeStateText.Text.Contains("编辑中") && controller.Toolbar.ModeStateText.Text.Contains("编辑中"), "dragging from edit mode preserves editing and both panels show its actual state");
        controller.SetPassThrough(true);
        Check(controller.Panel.ModeStateText.Text.Contains("鼠标穿透") && controller.Panel.FoldedModeText.Text.Contains(controller.BindingText("edit")) && controller.Overlay.Board.ModeHint.Contains(controller.BindingText("edit")), "mode and the current shortcut remain visible in expanded folded and canvas status");
        var modeShortcuts = controller.Settings.Shortcuts.ToDictionary(p => p.Key, p => p.Value);
        modeShortcuts["edit"] = new Shortcut(Key.F9, ModifierKeys.Control | ModifierKeys.Alt); controller.ApplyShortcuts(modeShortcuts);
        Check(controller.Panel.ModeText.Text.Contains("Ctrl+Alt+F9") && controller.Panel.FoldedModeText.Text.Contains("Ctrl+Alt+F9") && controller.Overlay.Board.ModeHint.Contains("Ctrl+Alt+F9"), "mode indicators follow the customized shortcut instead of a hardcoded default");
        modeShortcuts["edit"] = new Shortcut(Key.E, ModifierKeys.Control | ModifierKeys.Alt); controller.ApplyShortcuts(modeShortcuts);
        controller.Panel.ShowUpdateResult(0, BuildInfo.Version);
        Check(controller.Panel.UpdateStateText.Text.StartsWith("更新完成"), "update completion reports the actually running target version");
        controller.Panel.ShowUpdateResult(0, "99.0.0");
        Check(controller.Panel.UpdateStateText.Text.StartsWith("更新未完成"), "installer success alone cannot claim a version that did not start");
        controller.Panel.ShowUpdateResult(5, BuildInfo.Version);
        Check(controller.Panel.UpdateStateText.Text.StartsWith("更新未完成"), "installer failures remain visible in the relaunched app");
        controller.Panel.ShowAvailableUpdate(null); controller.Panel.MainTabs.SelectedIndex = 0;
        controller.Store.Preview(new Scene());
        controller.Panel.RedTeam.IsChecked = true;
        Check(controller.Editor.Team == "enemy", "hero library team selector sets the next hero faction");
        controller.PlaceHero(manifest.Heroes[0], new Point(420, 260));
        Check(controller.Store.Current.Items[0].X == 420 && controller.Store.Current.Items.Count == 1, "drag drop places exactly one hero");
        Check(controller.Store.Current.Items[0].Team == "enemy", "single hero drag drop uses the chosen red faction");
        controller.Clear(false); Check(controller.Store.Current.Items.Count == 0, "clear canvas includes heroes");
        controller.Undo(); Check(controller.Store.Current.Items.Count == 1, "clear canvas is undoable");
        controller.Store.Preview(new Scene { Items = new() { markedLine } }); controller.Editor.SelectedId = markedLine.Id;
        controller.BeginPropertyEdit(); controller.UpdateSelected(i => i with { Width = 12 }); controller.UpdateSelected(i => i with { Width = 17, Dashed = true, Opacity = .3 }); controller.EndPropertyEdit();
        Check(controller.Editor.Selected!.Width == 17 && controller.Editor.Selected.Opacity == .3 && controller.Editor.Selected.Dashed, "move selection properties apply to selected item");
        controller.Undo(); Check(controller.Editor.Selected!.Width == 10 && !controller.Editor.Selected.Dashed, "one undo reverts entire property slider gesture");
        controller.ChooseHero(doctrine); controller.Editor.Down(new Point(650, 260)); controller.Editor.Up();
        Check(controller.Store.Current.Items[^1].HeroId == "doctrine" && controller.Store.Current.Items[^1].Team == "enemy", "single click placement uses the chosen faction for Doctrine");
        controller.Undo();
        string beforeBatch = controller.Store.Serialize();
        var lineup = new[] { "orisa", "genji", "ana", "sombra", "doctrine" }.Select(id => manifest.Heroes.Single(h => h.Id == id)).ToArray();
        controller.Panel.BatchMode.IsChecked = true;
        foreach (var hero in lineup) controller.Panel.ToggleBatchHero(hero);
        controller.Panel.SearchBox.Text = "not-a-hero";
        Check(controller.Panel.HeroList.Items.Count == 0 && controller.Panel.BatchHint.Text.StartsWith("已选 5 位") && controller.Panel.AddHeroesButton.IsEnabled, "hero search preserves the complete pending lineup");
        controller.Panel.SupportRole.IsChecked = true; controller.Panel.SearchBox.Text = "血律";
        Check(controller.Panel.HeroList.Items.Cast<Hero>().Single().Id == "doctrine" && controller.Panel.BatchHint.Text.StartsWith("已选 5 位"), "role switching preserves the lineup and Doctrine appears under support");
        controller.Panel.SearchBox.Text = "黑影";
        Check(controller.Panel.HeroList.Items.Cast<Hero>().Single().Id == "sombra" && controller.Panel.BatchHint.Text.Contains("黑影"), "Sombra is available in support search and batch selection");
        controller.Panel.ToggleBatchHero(doctrine); Check(controller.Panel.BatchHint.Text.StartsWith("已选 4 位"), "clicking a pending hero again removes it");
        controller.Panel.ToggleBatchHero(doctrine);
        controller.Panel.AddHeroesButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var redLineup = controller.Editor.SelectedItems;
        Check(redLineup.Length == 5 && redLineup.All(i => i.Team == "enemy") && redLineup.Select(i => i.HeroId).SequenceEqual(lineup.Select(h => h.Id)), "add button creates the chosen red lineup in selection order");
        Check(redLineup.Select(i => i.X).Distinct().Count() == 5 && redLineup.Select(i => i.Y).Distinct().Count() == 1 && controller.Editor.Tool == EditorTool.Move, "five heroes are arranged in one row and selected for immediate group movement");
        Check(!controller.Panel.AddHeroesButton.IsEnabled && controller.Panel.BatchMode.IsChecked == true, "successful batch clears pending heroes and leaves batch mode ready for the other team");
        controller.Undo(); Check(controller.Store.Serialize() == beforeBatch, "one undo removes the entire added lineup");
        controller.Redo(); Check(controller.Store.Current.Items.Count == 6, "redo restores the complete lineup");
        controller.Panel.BlueTeam.IsChecked = true; controller.AddHeroes(lineup);
        Check(controller.Editor.SelectedItems.All(i => i.Team == "ally") && controller.Editor.SelectedItems[0].Y < redLineup[0].Y, "blue lineup uses the selected faction and starts separately from red");
        var batchRoundtrip = SceneStore.Parse(controller.Store.Serialize(), manifest.Heroes);
        Check(batchRoundtrip.Items.Count(i => i.HeroId == "doctrine") == 2 && batchRoundtrip.Items.Count(i => i.Team == "enemy") == 5, "both lineups and the new hero survive scene save and load");
        controller.Store.Preview(new Scene { Width = 320, Height = 200 }); controller.AddHeroes(manifest.Heroes);
        var canvas = new Rect(0, 0, 320, 200);
        Check(controller.Editor.SelectedItems.Length == manifest.Count && controller.Editor.SelectedItems.All(i => i.Size >= 24 && canvas.Contains(Editor.SelectionBounds(i))), "large batches fit inside a small canvas without invalid avatar sizes");
        controller.Panel.BatchMode.IsChecked = false; controller.Panel.SearchBox.Text = "";
        controller.Panel.TankRole.IsChecked = true;
        controller.Store.Preview(new Scene { Items = new() { markedLine } }); controller.Editor.SelectedId = markedLine.Id;
        Check(controller.ApplyShortcuts(settings.Shortcuts) is null && controller.BindingText("brush") == "Ctrl+F8", "custom shortcuts applied and hints refreshed");
        var heroMenu = controller.Overlay.CreateHeroMenu(store.Current.Items[0]);
        Check(heroMenu.Items.OfType<System.Windows.Controls.MenuItem>().All(i => !i.HasItems), "hero menu keeps all actions in one popup at screen edges");
        heroMenu.Measure(new Size(220, double.PositiveInfinity));
        int heroMenuHeight = (int)Math.Ceiling(heroMenu.DesiredSize.Height);
        Check(heroMenuHeight < 320, "hero menu fits on a small display without a secondary popup");
        Render(heroMenu, 220, heroMenuHeight, Path.Combine(output, "hero-context-menu.png"));
        var beforeMenu = controller.Store.Current;
        var beforeMenuSelection = controller.Editor.SelectedId;
        foreach (var position in new[] { new Point(1880, 60), new Point(40, 60), new Point(960, 1040) })
        {
            var menuHero = new SceneItem { HeroId = "lifeweaver", X = position.X, Y = position.Y, Team = "ally", Size = 80 };
            var untouchedHero = new SceneItem { HeroId = "doomfist", X = 500, Y = 300, Team = "ally", Size = 80 };
            controller.Store.Preview(new Scene { Items = new() { menuHero, untouchedHero } });
            controller.Editor.SelectedId = menuHero.Id;
            string beforeMenuAction = controller.Store.Serialize();
            var directMenu = controller.Overlay.CreateHeroMenu(menuHero);
            var redTeamItem = directMenu.Items.OfType<System.Windows.Controls.MenuItem>().SingleOrDefault(i => Equals(i.Header, "敌方 · 红色"));
            Check(redTeamItem is { IsEnabled: true }, "hero faction is directly clickable without entering an edge-flipped submenu");
            redTeamItem!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(controller.Store.Current.Items[0] == menuHero with { Team = "enemy" } && controller.Store.Current.Items[1] == untouchedHero, "direct faction action changes only its target hero");
            var blueTeamItem = controller.Overlay.CreateHeroMenu(controller.Store.Current.Items[0]).Items.OfType<System.Windows.Controls.MenuItem>().Single(i => Equals(i.Header, "友方 · 蓝色"));
            Check(!blueTeamItem.IsChecked, "reopening the hero menu reflects the changed faction");
            blueTeamItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(controller.Store.Serialize() == beforeMenuAction, "direct blue faction action restores the target without changing other heroes");
            controller.Undo(); Check(controller.Store.Current.Items[0].Team == "enemy", "one undo reverses only the latest faction action");
            controller.Undo(); Check(controller.Store.Serialize() == beforeMenuAction, "one undo restores the faction menu action");
            foreach (double menuSize in new double[] { 40, 60, 80, 100, 120, 160 })
            {
                directMenu = controller.Overlay.CreateHeroMenu(controller.Store.Current.Items[0]);
                var sizeItem = directMenu.Items.OfType<System.Windows.Controls.MenuItem>().SingleOrDefault(i => Equals(i.Header, $"{menuSize:0} px"));
                Check(sizeItem is { IsEnabled: true }, "hero size is directly clickable without entering an edge-flipped submenu " + menuSize);
                sizeItem!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                Check(controller.Store.Current.Items[0] == menuHero with { Size = menuSize } && controller.Store.Current.Items[1] == untouchedHero, "direct size action preserves faction position and other heroes " + menuSize);
            }
            var resizedMenu = controller.Overlay.CreateHeroMenu(controller.Store.Current.Items[0]);
            Check(resizedMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(i => Equals(i.Header, "160 px")).IsChecked, "reopening hero menu reflects the current size");
            controller.Undo(); Check(controller.Store.Current.Items[0].Size == 120, "one undo reverses just the most recent size action");
            var deleteHero = controller.Overlay.CreateHeroMenu(controller.Store.Current.Items[0]).Items.OfType<System.Windows.Controls.MenuItem>().Single(i => Equals(i.Header, "删除"));
            deleteHero.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(controller.Store.Current.Items.Count == 1 && controller.Store.Current.Items[0] == untouchedHero, "hero menu delete removes only its target");
            controller.Undo(); Check(controller.Store.Current.Items[0].Id == menuHero.Id && controller.Store.Current.Items[0].Size == 120, "undo restores a hero removed through its menu");
        }
        controller.Store.Preview(beforeMenu); controller.Editor.SelectedId = beforeMenuSelection;
        controller.Editor.Tool = EditorTool.Rectangle; controller.Toolbar.Refresh();
        var toolbarContent = (FrameworkElement)controller.Toolbar.Content; controller.Toolbar.Content = null;
        var toolbarPreview = new System.Windows.Controls.Border { Background = controller.Toolbar.Background, Child = toolbarContent };
        Render(toolbarPreview, 680, 180, Path.Combine(output, "toolbar-preview.png"));
        controller.Editor.Tool = EditorTool.Move; controller.Toolbar.Refresh();
        Render(toolbarPreview, 680, 220, Path.Combine(output, "selection-toolbar.png"));
        Render(toolbarPreview, 340, 300, Path.Combine(output, "toolbar-narrow.png"));
        var toolbarTexts = Descendants(toolbarPreview).OfType<System.Windows.Controls.TextBlock>().ToList();
        Check(!toolbarTexts.Any(t => t.Text == "英雄"), "toolbar no longer contains hero tool button");
        Check(toolbarTexts.Any(t => t.Text == "Ctrl+F8") && toolbarTexts.Any(t => t.Text == "未设置"), "tool buttons display customized and cleared shortcuts");
        Check(toolbarTexts.Any(t => t.Text == "Ctrl+Shift+Delete" && t.ActualWidth > 70), "clear tool displays full long shortcut");
        controller.Store.Preview(selectionScene); controller.Editor.Down(new Point(60, 60)); controller.Editor.Move(new Point(330, 270), false); controller.Editor.Up(); controller.Toolbar.Refresh();
        Render(toolbarPreview, 340, 300, Path.Combine(output, "multi-selection-toolbar.png"));
        var multiHint = Descendants(toolbarPreview).OfType<System.Windows.Controls.TextBlock>().FirstOrDefault(t => t.Text.StartsWith("已选 3 项"));
        Check(multiHint is not null && multiHint.ActualWidth <= 245 && !Descendants(toolbarPreview).OfType<System.Windows.Controls.Slider>().Any(), "multi-selection toolbar shows a wrapped count and no arbitrary single-item properties");
        settings.Shortcuts["brush"] = new Shortcut(Key.B); settings.Shortcuts["line"] = new Shortcut(Key.L);
        controller.ApplyShortcuts(settings.Shortcuts);
        Check(toolbarTexts.Any(t => t.Text == "B") && !toolbarTexts.Any(t => t.Text == "Ctrl+F8"), "shortcut labels update without switching tools");
        controller.Editor.Tool = EditorTool.Rectangle; controller.Toolbar.Refresh();
        Render(toolbarPreview, 680, 230, Path.Combine(output, "toolbar-shortcuts.png"));
        var toolbarWindowActions = Descendants(toolbarPreview).OfType<System.Windows.Controls.Button>().Where(b => b.Content is string).Select(b => (string)b.Content).ToList();
        Check(new[] { "折叠", "关闭", "退出程序" }.All(toolbarWindowActions.Contains), "toolbar exposes fold close and exit actions");
        double toolbarHeight = controller.Toolbar.Height;
        controller.Toolbar.SetFolded(true);
        Render(toolbarPreview, 340, 44, Path.Combine(output, "toolbar-folded.png"));
        Check(controller.Toolbar.Height == 44 && controller.Toolbar.ExpandedHeight == toolbarHeight && controller.Toolbar.IsFolded, "toolbar folds without losing expanded height");
        controller.Toolbar.SetFolded(false);
        Check(controller.Toolbar.Height == toolbarHeight && controller.Toolbar.ResizeMode == ResizeMode.CanResizeWithGrip, "toolbar restores expanded height and resize mode");
        Check(controller.Toolbar.ResizeMode == ResizeMode.CanResizeWithGrip && controller.Panel.MinWidth == 340, "windows support resizing");
        new WindowInteropHelper(controller.Panel).EnsureHandle();
        var shortcutsWindow = new ShortcutWindow(controller);
        var shortcutsContent = (FrameworkElement)shortcutsWindow.Content; shortcutsWindow.Content = null;
        Render(new System.Windows.Controls.Border { Background = controller.Panel.Background, Child = shortcutsContent }, 480, 680, Path.Combine(output, "shortcuts-panel.png"));
        var panelContent = (FrameworkElement)controller.Panel.Content;
        controller.Panel.Content = null;
        var panelControl = new System.Windows.Controls.ContentControl {
            Content = panelContent, Foreground = controller.Panel.Foreground,
            FontFamily = controller.Panel.FontFamily, FontSize = controller.Panel.FontSize, Width = 474, Height = 900,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var panelPreview = new System.Windows.Controls.Border { Background = controller.Panel.Background, Child = panelControl };
        System.Windows.Media.TextOptions.SetTextRenderingMode(panelPreview, TextRenderingMode.Grayscale);
        Render(panelPreview, 474, 900, Path.Combine(output, "control-panel.png"));
        controller.Panel.BatchMode.IsChecked = true;
        foreach (var hero in lineup) controller.Panel.ToggleBatchHero(hero);
        Render(panelPreview, 474, 900, Path.Combine(output, "batch-hero-panel.png"));
        panelControl.Width = 340; panelControl.Height = 600;
        Render(panelPreview, 340, 600, Path.Combine(output, "batch-hero-panel-narrow.png"));
        var batchBounds = new Rect(controller.Panel.BatchActions.TranslatePoint(new Point(), panelControl), controller.Panel.BatchActions.RenderSize);
        Check(controller.Panel.AddHeroesButton.ActualWidth > 120 && new Rect(0, 0, panelControl.Width, panelControl.Height).Contains(batchBounds), "batch controls remain usable and fully visible in the narrow hero panel");
        controller.Panel.BatchMode.IsChecked = false; panelControl.Width = 474; panelControl.Height = 900;
        Render(panelPreview, 474, 900, Path.Combine(output, "control-panel.png"));
        Check(controller.Panel.ModeButton.ActualHeight < 60 && controller.Panel.SearchBox.ActualHeight < 50, "armor decorations do not inflate layout");
        var tabs = FindChild<System.Windows.Controls.TabControl>(panelContent)!;
        tabs.SelectedIndex = 1;
        Render(panelPreview, 474, 900, Path.Combine(output, "settings-panel.png"));
        tabs.SelectedIndex = 2;
        Render(panelPreview, 474, 900, Path.Combine(output, "obs-panel.png"));
        var exampleUpdate = new UpdateRelease("0.2.0-beta", "修复英雄拖放后画布挡住鼠标的问题。\n新增编辑 / 穿透状态提示。\n改善按钮反馈和面板切换。", new Uri(AppUpdates.ReleasesPage), "Setup.exe", new Uri(AppUpdates.ReleasesPage), 1, new string('0', 64));
        controller.Panel.ShowAvailableUpdate(exampleUpdate); tabs.SelectedIndex = 3;
        Check(controller.Panel.UpdateNotice.Visibility == Visibility.Visible && controller.Panel.InstallUpdateButton.IsEnabled && controller.Panel.UpdateNotes.Text == exampleUpdate.Notes, "available update shows a notice notes and an install action");
        Render(panelPreview, 474, 900, Path.Combine(output, "updates-panel.png"));
        panelControl.Width = 340; panelControl.Height = 600;
        Render(panelPreview, 340, 600, Path.Combine(output, "updates-panel-narrow.png"));
        controller.Panel.ShowAvailableUpdate(null);
        Check(controller.Panel.UpdateNotice.Visibility == Visibility.Collapsed && !controller.Panel.InstallUpdateButton.IsEnabled && controller.Panel.UpdateNotes.Text == "", "no update clears old release information and disables install");
        tabs.SelectedIndex = 0; panelControl.Width = 340; panelControl.Height = 600;
        Render(panelPreview, 340, 600, Path.Combine(output, "panel-narrow.png"));
        var windowButtons = controller.Panel.WindowActions.Children.OfType<System.Windows.Controls.Button>().ToList();
        var expandedPositions = windowButtons.Select(b => b.TranslatePoint(new Point(), panelContent)).ToArray();
        Check(windowButtons.Count == 3 && windowButtons.All(b => b.Content is System.Windows.Shapes.Path), "window actions use minimize close and power vector icons");
        Check(WindowGestures.IsControl((System.Windows.Controls.TabItem)tabs.Items[0]) && WindowGestures.IsControl(controller.Panel.SearchBox), "blank drag excludes tabs and search field");
        Check(!WindowGestures.IsControl(controller.Panel.ExpandedPanel), "panel blank area allows dragging");
        Check(!WindowGestures.IsControl(controller.Panel.SearchHint), "hero list hint allows blank dragging");
        controller.Panel.Top = 120; controller.Panel.Left = 160;
        double expandedHeight = controller.Panel.Height, expandedWidth = controller.Panel.Width;
        controller.Panel.SetFolded(true);
        Check(controller.Panel.Height == MainWindow.FoldedHeight && controller.Panel.Top == 120 && controller.Panel.Left == 160 && controller.Panel.Width == expandedWidth, "fold shrinks upward without moving window anchor");
        Check(controller.Panel.ExpandedHeight == expandedHeight && controller.Panel.ExpandedPanel.Visibility == Visibility.Collapsed, "fold preserves saved expanded size and hides content");
        panelControl.Width = 340; panelControl.Height = MainWindow.FoldedHeight;
        Render(panelPreview, 340, (int)MainWindow.FoldedHeight, Path.Combine(output, "panel-folded.png"));
        var foldedState = new Rect(controller.Panel.FoldedModeText.TranslatePoint(new Point(), panelContent), controller.Panel.FoldedModeText.RenderSize);
        var foldedVersion = new Rect(controller.Panel.VersionText.TranslatePoint(new Point(), panelContent), controller.Panel.VersionText.RenderSize);
        Check(!foldedState.IntersectsWith(foldedVersion) && foldedState.Bottom <= MainWindow.FoldedHeight, "folded mode status remains visible below the version label");
        Check(windowButtons.Select(b => b.TranslatePoint(new Point(), panelContent)).SequenceEqual(expandedPositions), "folded window action positions exactly match expanded positions");
        var expandPosition = controller.Panel.ExpandButton.TranslatePoint(new Point(), panelContent);
        var exitPosition = controller.Panel.ExitButton.TranslatePoint(new Point(), panelContent);
        Check(expandPosition.Y > exitPosition.Y + controller.Panel.ExitButton.ActualHeight && Math.Abs(expandPosition.X + controller.Panel.ExpandButton.ActualWidth - exitPosition.X - controller.Panel.ExitButton.ActualWidth) < 1, "expand action is on next row right aligned with window actions");
        controller.Panel.SetFolded(true); controller.Panel.SetFolded(false);
        Check(controller.Panel.Height == expandedHeight && controller.Panel.ResizeMode == ResizeMode.CanResizeWithGrip && controller.Panel.Top == 120, "expand restores dimensions and resizing after repeated fold");
        string beforePanelClose = controller.Store.Serialize(); controller.Panel.Close();
        Check(!controller.IsClosing && Native.IsWindow(new WindowInteropHelper(controller.Panel).Handle) && controller.Store.Serialize() == beforePanelClose, "closing panel keeps program scene and native hotkey window alive");
        var ghost = new HeroDragPreviewWindow(); var ghostHandle = new WindowInteropHelper(ghost).EnsureHandle();
        Check(!ghost.IsTracking && !ghost.ShowActivated && !ghost.ShowInTaskbar && !ghost.IsHitTestVisible, "drag preview idle has no timer or activation");
        Check((Native.GetWindowLong(ghostHandle, -20) & (Native.ExTransparent | Native.ExNoActivate)) == (Native.ExTransparent | Native.ExNoActivate), "drag preview native window passes mouse and never activates");
        ghost.End(); ghost.Close();
        var ghostVisual = new HeroDragVisual(); ghostVisual.Configure("ana", "ally");
        string ghostFile = Path.Combine(output, "hero-drag-preview.png"); Render(ghostVisual, 88, 88, ghostFile);
        Check(PixelAlpha(ghostFile, 44, 30) is >= 139 and <= 141 && PixelAlpha(ghostFile, 0, 0) == 0, "drag preview uses translucent portrait and transparent corners");
        Check(ReferenceEquals(Assets.Portrait("ana"), Assets.Portrait("ana")) && Assets.Portrait("ana").IsFrozen, "drag portrait uses shared immutable cached bitmap");
        var ghostHero = new SceneItem { HeroId = "ana", X = 60, Y = 60, Size = 80 };
        var ghostBoard = new BoardView { Scene = new Scene { Width = 120, Height = 120, Items = new() { ghostHero } }, Editing = true, DraggedHeroId = ghostHero.Id };
        string ghostBoardFile = Path.Combine(output, "hero-canvas-drag.png"); Render(ghostBoard, 120, 120, ghostBoardFile);
        Check(PixelAlpha(ghostBoardFile, 60, 50) is >= 139 and <= 142 && ghostHero.Opacity == 1, "existing hero drag is translucent without modifying scene opacity");
        ghostBoard.DrawEditorChrome = false; ghostBoard.InvalidateVisual(); Render(ghostBoard, 120, 120, ghostBoardFile);
        Check(PixelAlpha(ghostBoardFile, 60, 50) == 255, "drag chrome cannot change clean output opacity");
        var modeBoard = new BoardView { Scene = new Scene { Width = 800, Height = 600 }, Editing = true, ModeHint = controller.ModeState + " · " + controller.ModeShortcut };
        string modeFile = Path.Combine(output, "canvas-mode-status.png"); Render(modeBoard, 800, 600, modeFile);
        Check(PixelAlpha(modeFile, 20, 570) > 0, "editing canvas displays its mouse-interception status");
        modeBoard.DrawEditorChrome = false; modeBoard.InvalidateVisual(); Render(modeBoard, 800, 600, modeFile);
        Check(PixelAlpha(modeFile, 20, 570) == 0, "mouse mode status stays out of the clean OBS output");
        modeBoard.DrawEditorChrome = true; modeBoard.InvalidateVisual(); Render(modeBoard, 800, 600, modeFile);
        File.WriteAllText(Path.Combine(output, "sample-scene.json"), store.Serialize());
        // Native style checks use an unshown HWND: no desktop input or screenshots.
        var hwnd = new WindowInteropHelper(controller.Overlay).EnsureHandle();
        var panelHandle = new WindowInteropHelper(controller.Panel).Handle;
        uint testModifiers = 0x4000 | 1 | 2 | 4; uint testKey = (uint)KeyInterop.VirtualKeyFromKey(Key.F24);
        bool registered = Native.RegisterHotKey(hwnd, 900, testModifiers, testKey);
        try
        {
            Check(registered, "custom global hotkey registers on native window");
            bool duplicateBinding = Native.RegisterHotKey(panelHandle, 901, testModifiers, testKey);
            if (duplicateBinding) Native.UnregisterHotKey(panelHandle, 901);
            Check(!duplicateBinding, "native conflicting hotkey is detected");
        }
        finally { if (registered) Native.UnregisterHotKey(hwnd, 900); }
        Native.SetPassThrough(hwnd, true);
        Check((Native.GetWindowLong(hwnd, -20) & Native.ExTransparent) != 0, "native click-through enabled");
        Check((Native.GetWindowLong(hwnd, -20) & Native.ExNoActivate) != 0, "pass-through does not activate overlay");
        Native.SetPassThrough(hwnd, false);
        Check((Native.GetWindowLong(hwnd, -20) & Native.ExTransparent) == 0, "native click-through disabled");
        Check((Native.GetWindowLong(hwnd, -20) & Native.ExNoActivate) == 0, "edit mode can receive keyboard focus");
        Check(Native.GetProp(hwnd, "NonRudeHWND") == new IntPtr(1), "taskbar compatibility survives edit and mouse pass-through changes");
        controller.SetPassThrough(false); controller.Editor.Tool = EditorTool.Move;
        controller.Editor.Down(new Point(600, 500)); controller.Editor.Move(new Point(700, 550), false); controller.Overlay.Refresh();
        Check(controller.Overlay.Board.SelectionBox is not null, "overlay receives live marquee geometry");
        controller.CancelOrDeselect();
        Check(!controller.PassThrough && controller.Editor.SelectedIds.Count == 3 && !controller.Editor.IsDragging && controller.Overlay.Board.SelectionBox is null, "first escape cancels marquee and restores the previous selection");
        controller.CancelOrDeselect();
        Check(!controller.PassThrough && controller.Editor.SelectedIds.Count == 0, "next escape clears selection while staying in edit mode");
        controller.CancelOrDeselect();
        Check(controller.PassThrough && (Native.GetWindowLong(hwnd, -20) & Native.ExTransparent) != 0, "escape with no selection restores native click-through");
        controller.SetPassThrough(false); controller.Editor.Tool = EditorTool.Move;
        controller.Editor.Down(new Point(60, 60)); controller.Editor.Move(new Point(330, 270), false); controller.SetPassThrough(true);
        Check(controller.Overlay.Board.SelectionBox is null && controller.Editor.SelectedIds.Count == 0 && !controller.Editor.IsDragging, "direct click-through toggle cancels an unfinished marquee");
        controller.Overlay.Topmost = false;
        Check((Native.GetWindowLong(hwnd, -20) & 8) == 0, "native topmost off");
        controller.Overlay.Topmost = true;
        Check((Native.GetWindowLong(hwnd, -20) & 8) != 0, "native topmost on");
        Check(Native.GetProp(hwnd, "NonRudeHWND") == new IntPtr(1), "taskbar compatibility survives switching window topmost state");
        var toolbarHandle = new WindowInteropHelper(controller.Toolbar).EnsureHandle();
        Native.SetWindowPos(panelHandle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        Native.SetWindowPos(toolbarHandle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        controller.ToggleTopmost(); controller.ToggleTopmost();
        Check(IsAbove(panelHandle, hwnd) && IsAbove(toolbarHandle, hwnd), "re-enabling canvas topmost keeps both controls above its mouse-intercepting surface");
        Check(Native.GetWindow(panelHandle, 4) == hwnd && Native.GetWindow(toolbarHandle, 4) == hwnd, "native ownership enforces control windows above the canvas");
        var foregroundBeforeOrder = Native.GetForegroundWindow();
        string sceneBeforeOrder = controller.Store.Serialize();
        controller.ToggleTopmost();
        Check((Native.GetWindowLong(hwnd, -20) & 8) == 0 && (Native.GetWindowLong(panelHandle, -20) & 8) != 0 && (Native.GetWindowLong(toolbarHandle, -20) & 8) != 0, "unpinning the canvas preserves independently topmost control windows");
        controller.RaisePanel();
        Check((Native.GetWindowLong(hwnd, -20) & 8) == 0 && !controller.Panel.IsVisible && !controller.Toolbar.IsVisible, "raising controls neither repins the canvas nor shows hidden panels");
        controller.ToggleTopmost();
        Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        Check(IsAbove(panelHandle, hwnd) && IsAbove(toolbarHandle, hwnd), "native canvas raising cannot cover its owned controls without any activation callback");
        controller.SetPassThrough(false); controller.Panel.FinishHeroDrag(true); controller.SetPassThrough(false);
        controller.Overlay.Place(controller.Screen);
        Check(IsAbove(panelHandle, hwnd) && IsAbove(toolbarHandle, hwnd) && (Native.GetWindowLong(hwnd, -20) & Native.ExTransparent) == 0, "controls stay above the mouse-intercepting canvas after mode changes drag cleanup and screen placement");
        var shortcutDialog = new ShortcutWindow(controller);
        var shortcutHandle = new WindowInteropHelper(shortcutDialog).EnsureHandle();
        Native.SetWindowPos(shortcutHandle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        controller.RaisePanel();
        Check(IsAbove(shortcutHandle, panelHandle) && Native.GetWindow(shortcutHandle, 4) == panelHandle, "control raising leaves its shortcut dialog above the main panel");
        shortcutDialog.Close();
        Check(Native.GetForegroundWindow() == foregroundBeforeOrder && controller.Store.Serialize() == sceneBeforeOrder, "window ordering changes preserve desktop focus and the saved scene");
        Check(Native.GetProp(hwnd, "NonRudeHWND") == new IntPtr(1), "owned control ordering preserves auto-hide taskbar compatibility");
        await using var server = new ObsServer(manifest); await server.StartAsync(0); server.Publish(store.Serialize());
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        string root = $"http://127.0.0.1:{server.Port}";
        await using var occupied = new ObsServer(manifest);
        await occupied.StartAsync(server.Port);
        Check(occupied.Port != server.Port, "OBS port conflict fallback");
        Check((await http.GetStringAsync(root + "/health")).Contains("ok"), "OBS HTTP health");
        Check((await http.GetStringAsync(root + "/overlay")).Contains("overlay.js"), "OBS page served");
        Check((await http.GetByteArrayAsync(root + "/heroes/ana.png")).Length > 1000, "embedded portrait over HTTP");
        Check((await http.GetAsync(root + "/heroes/not-a-hero.png")).StatusCode == System.Net.HttpStatusCode.NotFound, "unknown asset 404");
        using var forbidden = new HttpRequestMessage(HttpMethod.Get, root + "/state"); forbidden.Headers.Add("Origin", "https://example.org");
        Check((await http.SendAsync(forbidden)).StatusCode == System.Net.HttpStatusCode.Forbidden, "foreign origin rejected");
        using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/ws"), timeout.Token);
        async Task<string> Read()
        {
            using var memory = new MemoryStream(); var buffer = new byte[65536]; WebSocketReceiveResult result;
            do { result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token); memory.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
            return Encoding.UTF8.GetString(memory.ToArray());
        }
        Check((await Read()) == store.Serialize(), "OBS initial full snapshot");
        store.Edit(s => s with { Visible = false }); server.Publish(store.Serialize());
        Check((await Read()).Contains("\"visible\":false"), "OBS visibility synchronizes");
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
        using var reconnect = new ClientWebSocket(); await reconnect.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/ws"), timeout.Token);
        var rb = new byte[65536]; var rr = await reconnect.ReceiveAsync(new ArraySegment<byte>(rb), timeout.Token);
        Check(Encoding.UTF8.GetString(rb, 0, rr.Count).Contains("\"visible\":false"), "OBS reconnect restores latest state");
        await reconnect.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
        File.WriteAllText(Path.Combine(output, "self-test-result.json"), JsonSerializer.Serialize(new { version = BuildInfo.Version, edition = BuildInfo.Edition, passed = count, heroes = manifest.Count, serverPort = server.Port, utc = DateTime.UtcNow, renderTier = RenderCapability.Tier >> 16 }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static bool IsAbove(IntPtr window, IntPtr other)
    {
        for (var previous = Native.GetWindow(other, 3); previous != IntPtr.Zero; previous = Native.GetWindow(previous, 3))
            if (previous == window) return true;
        return false;
    }
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n);
            if (child is T value) return value;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static byte PixelAlpha(string path, int x, int y)
    {
        using var file = File.OpenRead(path);
        var bitmap = BitmapFrame.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; bgra.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel[3];
    }
}
