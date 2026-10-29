using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

internal static class ComfortTests
{
    internal static async Task RunAsync(Action<bool, string> check, string output)
    {
        await ProbeAsync(check, output);
        var controller = new Controller(test: true);
        try
        {
            var bar = controller.LectureBar;
            var barContent = (FrameworkElement)bar.Content; bar.Content = null;
            var frame = Frame(barContent, bar);
            Button Toggle(string name) => Descendants<Button>(frame).Single(b => b.Name == name);
            controller.ShowPanelPage(0); controller.SetLectureToolsFolded(true);
            check(controller.HeroLibraryOpen && controller.Store.Current.Visible, "closing tools preserves hero library and tactical board");
            controller.SetLectureToolsFolded(false); controller.Panel.ClosePanel();
            check(!controller.HeroLibraryOpen && !controller.LectureToolsFolded, "closing library preserves tools");
            controller.ShowPanelPage(0); controller.ShowPanelPage(3);
            check(controller.HeroLibraryOpen && controller.Panel.MainTabs.Items.Count == 1 && controller.Management.Pages.Items.Count == 3 && Window.GetWindow(controller.Panel.ProxyModeChoice) == controller.Management, "settings controls belong to an independent window while the hero library stays open");
            controller.Panel.SearchBox.Text = "安娜";
            foreach (int width in new[] { 340, 420, 489, 490, 500, 680 })
            {
                int height = width < 490 ? 94 : 54;
                Render(frame, width, height, Path.Combine(output, $"comfort-bar-{width}.png"));
                CheckButtons(frame, width, height, check, "lecture bar " + width);
                check(Toggle("LectureFoldButton").Content.Equals("工具") && Toggle("LectureVisibleButton").Content.Equals("战术板"), "toggle labels stay stable at " + width);
                check(AutomationProperties.GetItemStatus(Toggle("LectureHeroesButton")) == "已开启", "hero toggle highlights its independent open state");
            }
            Toggle("LectureHeroesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!controller.HeroLibraryOpen && !controller.LectureToolsFolded, "hero toggle closes only the library");
            Toggle("LectureHeroesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.HeroLibraryOpen && controller.Panel.SearchBox.Text == "安娜", "library reopen retains search");
            controller.SetPassThrough(true); Toggle("LectureModeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!controller.PassThrough && bar.ModeStateText.Text == "战术模式", "mode text itself enters tactical mode");
            Toggle("LectureModeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.PassThrough && bar.ModeStateText.Text == "桌面模式", "mode text itself returns to desktop mode");
            frame.Content = null; bar.Content = barContent;

            var panel = controller.Panel;
            var libraryContent = (FrameworkElement)panel.Content; panel.Content = null; frame = Frame(libraryContent, panel);
            Render(frame, 340, 600, Path.Combine(output, "comfort-library.png"));
            var heroButton = Descendants<Button>(panel.HeroList).Single(b => b.Tag is Hero h && h.Id == "ana");
            var before = controller.Store.Serialize(); int count = controller.Store.Current.Items.Count;
            heroButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.PreviewMouseDoubleClickEvent });
            var added = controller.Editor.Selected!;
            check(controller.Store.Current.Items.Count == count + 1 && added.HeroId == "ana" && added.X == controller.Store.Current.Width / 2 && added.Y == controller.Store.Current.Height / 2 && !controller.PassThrough, "real hero-card double-click adds exactly one centered hero and enters tactical mode");
            controller.Undo(); check(controller.Store.Serialize() == before, "double-click addition is one undo operation");
            frame.Content = null; panel.Content = libraryContent;

            var toolbar = controller.Toolbar; var toolsContent = (FrameworkElement)toolbar.Content; toolbar.Content = null; frame = Frame(toolsContent, toolbar);
            foreach (var size in new[] { new Size(300, 300), new Size(340, 300), new Size(640, 180), new Size(680, 180), new Size(685, 189) })
            {
                foreach (var tool in new[] { EditorTool.Move, EditorTool.Brush, EditorTool.Text })
                {
                    controller.SetTool(tool);
                    Render(frame, (int)size.Width, (int)size.Height, Path.Combine(output, $"comfort-tools-{tool}-{size.Width}.png"));
                    CheckButtons(frame, size.Width, size.Height, check, "tools " + tool + " " + size.Width);
                }
            }
            foreach (double scale in new[] { 1.25, 1.5, 2 })
            {
                Render(frame, 685, 189, Path.Combine(output, $"comfort-tools-scale-{scale:0.##}.png"), scale);
                CheckButtons(frame, 685, 189, check, "scaled tools " + scale);
            }
            frame.Content = null; toolbar.Content = toolsContent;
            var managementContent = (FrameworkElement)controller.Management.Content; controller.Management.Content = null; frame = Frame(managementContent, controller.Management);
            controller.Management.Pages.SelectedIndex = 2;
            foreach (int width in new[] { 360, 500 }) Render(frame, width, 740, Path.Combine(output, $"comfort-settings-{width}.png"));
            frame.Content = null; controller.Management.Content = managementContent;

            string beforeFps = controller.Store.Serialize();
            controller.Panel.HighFrameRate.IsChecked = true;
            check(controller.Settings.HighFrameRate && Math.Abs(controller.Editor.Trails.RefreshMilliseconds - 1000d / 60) < .1, "high-frame-rate switch immediately requests a sixty-hertz trajectory cadence");
            controller.Panel.HighFrameRate.IsChecked = false;
            check(!controller.Settings.HighFrameRate && controller.Editor.Trails.RefreshMilliseconds > 33 && controller.Store.Serialize() == beforeFps, "standard cadence restores without changing scene or history");

            var menu = controller.Overlay.CreateBoardMenu();
            menu.Measure(new Size(menu.Width, double.PositiveInfinity));
            int menuHeight = (int)Math.Ceiling(menu.DesiredSize.Height);
            Render(menu, (int)menu.Width, menuHeight, Path.Combine(output, "comfort-board-menu.png"));
            foreach (var item in menu.Items.OfType<MenuItem>())
                check(new Rect(0, 0, menu.Width, menuHeight).Contains(new Rect(item.TranslatePoint(new Point(), menu), item.RenderSize)), "blank-board menu item stays inside popup: " + item.Header);
            controller.SetPassThrough(false);
            menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "退出战术模式")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(controller.PassThrough && !controller.IsClosing && controller.Store.Serialize() == beforeFps, "blank-board exit returns to desktop without exiting app or altering scene");

            var hero = new SceneItem { HeroId = "ana", X = 160, Y = 90, Size = 60 };
            foreach (string background in new[] { "#FFFFFF", "#263746" })
            {
                var board = new BoardView { Scene = new Scene { Width = 320, Height = 180, Items = new() { hero } }, Editing = true, SelectedIds = new HashSet<string> { hero.Id } };
                var visual = new Border { Background = BoardView.Brush(background), Child = board };
                var pixels = Render(visual, 320, 180, Path.Combine(output, "comfort-selection-" + background[1..] + ".png"));
                int accent = Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] > 160 && pixels[i * 4 + 1] > 185 && pixels[i * 4 + 2] < 160);
                check(accent > 80, "selected hero has visible default corner marks on " + background);
            }
        }
        finally { controller.Close(); }
    }
    private static ContentControl Frame(FrameworkElement content, Window window) => new() { Content = content, FontFamily = window.FontFamily, FontSize = window.FontSize, Foreground = window.Foreground, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private static void CheckButtons(FrameworkElement root, double width, double height, Action<bool, string> check, string label)
    {
        foreach (var button in Descendants<Button>(root).Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0))
        {
            var bounds = new Rect(button.TranslatePoint(new Point(), root), button.RenderSize);
            check(new Rect(-1, -1, width + 2, height + 2).Contains(bounds), label + " button stays inside window: " + button.ToolTip);
            if (button.Content is FrameworkElement content && content.ActualWidth > 0)
            {
                var inner = new Rect(content.TranslatePoint(new Point(), button), content.RenderSize);
                check(new Rect(-1, -1, button.ActualWidth + 2, button.ActualHeight + 2).Contains(inner), label + " button content is not clipped: " + button.ToolTip);
            }
        }
    }
    internal static Task ProbeAsync(Action<bool, string> check, string output)
    {
        var results = new Dictionary<string, bool>();
        var controller = new Controller(test: true);
        try
        {
            var toolbar = controller.Toolbar;
            var content = (FrameworkElement)toolbar.Content; toolbar.Content = null;
            var frame = new ContentControl { Content = content, FontFamily = toolbar.FontFamily, FontSize = toolbar.FontSize, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            Render(frame, 685, 189, Path.Combine(output, "toolbar-content-probe.png"));
            var fold = Descendants<Button>(frame).Single(b => Equals(b.ToolTip, "折叠工具栏"));
            var bounds = new Rect(fold.TranslatePoint(new Point(), frame), fold.RenderSize);
            results["fold button fits entirely inside its header"] = new Rect(0, 0, 685, 30).Contains(bounds);
            var icon = (FrameworkElement)fold.Content;
            var iconBounds = new Rect(icon.TranslatePoint(new Point(), fold), icon.RenderSize);
            results["fold icon has inner padding on every side"] = new Rect(3, 3, fold.ActualWidth - 6, fold.ActualHeight - 6).Contains(iconBounds);
            frame.Content = null; toolbar.Content = content;
            controller.Settings.CloseBehavior = PanelCloseBehavior.Exit;
            controller.Panel.ClosePanelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            results["hero library X never exits the application despite legacy exit preference"] = !controller.IsClosing;
        }
        finally { controller.Close(); }
        long time = 0;
        using var trails = new HeroTrails(() => time);
        trails.Configure(true, HeroTrailStyle.Dashed, 2);
        var hero = new SceneItem { HeroId = "ana", X = 40, Y = 90, Size = 60 };
        var moved = hero with { X = 73 }; trails.Move(hero, moved);
        time = 500; trails.Move(moved, moved with { X = 186 });
        time = 1000; trails.Move(moved with { X = 186 }, moved with { X = 300 }); trails.Complete();
        byte[] Draw(string name) => Render(new BoardView { Scene = new Scene { Width = 400, Height = 200, Items = trails.Items.ToList() }, DrawEditorChrome = false }, 400, 200, Path.Combine(output, name));
        var beforeScene = trails.Items;
        var before = Draw("dash-before-erase.png");
        time = 2100; trails.Expire(); var after = Draw("dash-after-erase.png");
        File.WriteAllText(Path.Combine(output, "trail-erasure-fixtures.json"), JsonSerializer.Serialize(new { before = new Scene { Width = 400, Height = 200, Items = beforeScene.ToList() }, after = new Scene { Width = 400, Height = 200, Items = trails.Items.ToList() } }, Assets.Json));
        results["unexpired dashes stay at identical pixels while older trajectory is erased"] = Enumerable.Range(210, 70).All(x => Enumerable.Range(86, 9).All(y => before[(y * 400 + x) * 4 + 3] == after[(y * 400 + x) * 4 + 3]));
        File.WriteAllText(Path.Combine(output, "comfort-probe.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var result in results) check(result.Value, result.Key);
        return Task.CompletedTask;
    }
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n); if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static byte[] Render(FrameworkElement element, int width, int height, string path, double scale = 1)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        int pixelsWide = (int)Math.Ceiling(width * scale), pixelsHigh = (int)Math.Ceiling(height * scale);
        var bitmap = new RenderTargetBitmap(pixelsWide, pixelsHigh, 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
        var pixels = new byte[pixelsWide * pixelsHigh * 4]; bitmap.CopyPixels(pixels, pixelsWide * 4, 0); return pixels;
    }
}
