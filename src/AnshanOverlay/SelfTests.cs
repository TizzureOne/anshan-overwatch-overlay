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
        var manifest = Assets.LoadHeroes();
        Check(manifest.Count == manifest.Heroes.Count && manifest.Count >= 53, "complete official roster");
        Check(manifest.Heroes.Select(h => h.Id).Distinct().Count() == manifest.Count, "unique hero IDs");
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
        var settings = new Preferences(); Check(Preferences.Validate(settings.Shortcuts) is null, "default shortcuts unique");
        settings.Shortcuts["brush"] = new Shortcut(Key.F8, ModifierKeys.Control);
        Check(Preferences.Validate(settings.Shortcuts) is null, "custom shortcut accepted");
        settings.Shortcuts["line"] = settings.Shortcuts["brush"];
        Check(Preferences.Validate(settings.Shortcuts) is not null, "duplicate shortcut rejected");
        settings.Shortcuts["line"] = new Shortcut(Key.None);
        Check(Preferences.Validate(settings.Shortcuts) is null, "shortcut can be unassigned");
        var restoredSettings = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings, Assets.Json), Assets.Json)!;
        Check(restoredSettings.Shortcuts["brush"].Key == Key.F8, "shortcut settings roundtrip");
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
        controller.PlaceHero(manifest.Heroes[0], new Point(420, 260));
        Check(controller.Store.Current.Items[0].X == 420 && controller.Store.Current.Items.Count == 1, "drag drop places exactly one hero");
        controller.Clear(false); Check(controller.Store.Current.Items.Count == 0, "clear canvas includes heroes");
        controller.Undo(); Check(controller.Store.Current.Items.Count == 1, "clear canvas is undoable");
        controller.Store.Preview(new Scene { Items = new() { markedLine } }); controller.Editor.SelectedId = markedLine.Id;
        controller.BeginPropertyEdit(); controller.UpdateSelected(i => i with { Width = 12 }); controller.UpdateSelected(i => i with { Width = 17, Dashed = true, Opacity = .3 }); controller.EndPropertyEdit();
        Check(controller.Editor.Selected!.Width == 17 && controller.Editor.Selected.Opacity == .3 && controller.Editor.Selected.Dashed, "move selection properties apply to selected item");
        controller.Undo(); Check(controller.Editor.Selected!.Width == 10 && !controller.Editor.Selected.Dashed, "one undo reverts entire property slider gesture");
        Check(controller.ApplyShortcuts(settings.Shortcuts) is null && controller.BindingText("brush") == "Ctrl+F8", "custom shortcuts applied and hints refreshed");
        var heroMenu = controller.Overlay.CreateHeroMenu(store.Current.Items[0]);
        Check(heroMenu.Items.OfType<System.Windows.Controls.MenuItem>().Count() == 4, "hero context menu exposes name team size delete without per-hero label");
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
        Check(controller.Panel.ModeButton.ActualHeight < 60 && controller.Panel.SearchBox.ActualHeight < 50, "armor decorations do not inflate layout");
        var tabs = FindChild<System.Windows.Controls.TabControl>(panelContent)!;
        tabs.SelectedIndex = 1;
        Render(panelPreview, 474, 900, Path.Combine(output, "settings-panel.png"));
        tabs.SelectedIndex = 2;
        Render(panelPreview, 474, 900, Path.Combine(output, "obs-panel.png"));
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
        controller.Overlay.Topmost = false;
        Check((Native.GetWindowLong(hwnd, -20) & 8) == 0, "native topmost off");
        controller.Overlay.Topmost = true;
        Check((Native.GetWindowLong(hwnd, -20) & 8) != 0, "native topmost on");
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
        File.WriteAllText(Path.Combine(output, "self-test-result.json"), JsonSerializer.Serialize(new { passed = count, heroes = manifest.Count, serverPort = server.Port, utc = DateTime.UtcNow, renderTier = RenderCapability.Tier >> 16 }, new JsonSerializerOptions { WriteIndented = true }));
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
