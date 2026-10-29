using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnshanOverlay;

internal static class LectureBarTests
{
    internal static async Task RunAsync(Action<bool, string> check, string output)
    {
        string legacy = Path.Combine(output, "lecture-legacy-settings.json");
        File.WriteAllText(legacy, "{\"panelWidth\":474,\"panelHeight\":900,\"toolbarHeight\":220,\"passThroughOpacityPercent\":37}");
        var migrated = Preferences.Load(out var warning, legacy);
        check(warning is null && migrated.PanelWidth == 360 && migrated.PanelHeight == 620 && migrated.ToolbarHeight == 180 && migrated.PassThroughOpacityPercent == 37, "legacy default layout migrates while preserving scene preferences");
        migrated.LectureBarPosition = new(-800, 100); migrated.LectureToolsFolded = true; migrated.LectureBarWidth = 640;
        File.WriteAllText(legacy, JsonSerializer.Serialize(migrated, Assets.Json));
        var restored = Preferences.Load(out warning, legacy);
        check(restored.LectureBarPosition == migrated.LectureBarPosition && restored.LectureBarWidth == 640 && restored.LectureToolsFolded, "lecture position width and folded state roundtrip");
        File.WriteAllText(legacy, "{\"panelWidth\":550,\"panelHeight\":710,\"toolbarHeight\":260}");
        var custom = Preferences.Load(out warning, legacy);
        check(custom.PanelWidth == 550 && custom.PanelHeight == 710 && custom.ToolbarHeight == 260, "migration retains custom panel and toolbar sizes");

        var controller = new Controller(test: true);
        try
        {
            var bar = controller.LectureBar; var toolbar = controller.Toolbar;
            var barContent = (FrameworkElement)bar.Content; bar.Content = null;
            var barPreview = Frame(barContent, bar);
            var toolContent = (FrameworkElement)toolbar.Content; toolbar.Content = null;
            var toolPreview = Frame(toolContent, toolbar);
            var scene = new Scene { Width = 1000, Height = 640, Items = new() {
                new SceneItem { HeroId = "ana", X = 230, Y = 330, Size = 64 },
                new SceneItem { HeroId = "winston", X = 500, Y = 240, Size = 64 },
                new SceneItem { Kind = "line", X = 270, Y = 330, X2 = 470, Y2 = 260, Arrow = true, Color = "#39A9FF", Width = 6 }
            } };
            controller.Store.Preview(scene); string before = controller.Store.Serialize();
            Button BarButton(string name) => Descendants<Button>(barPreview).Single(b => b.Name == name);
            foreach (var size in new[] { new Size(680, 54), new Size(340, 94) })
            {
                foreach (bool folded in new[] { false, true })
                {
                    controller.SetLectureToolsFolded(folded);
                    Render(barPreview, (int)size.Width, (int)size.Height, Path.Combine(output, $"lecture-bar-{size.Width:0}-{(folded ? "folded" : "expanded")}.png"));
                    var area = new Rect(new Point(), size);
                    check(bar.AppMark.Source == Assets.AppIcon && bar.AppMark.Visibility == Visibility.Visible && bar.AppMark.ActualWidth == 28 && area.Contains(new Rect(bar.AppMark.TranslatePoint(new Point(), barPreview), bar.AppMark.RenderSize)), "original logo remains visible and unclipped at " + size.Width + " folded=" + folded);
                    check(Descendants<Button>(barPreview).All(b => area.Contains(new Rect(b.TranslatePoint(new Point(), barPreview), b.RenderSize))), "all lecture actions fit at " + size.Width + " folded=" + folded);
                    check(Equals(BarButton("LectureFoldButton").Content, "工具"), "tools toggle keeps a stable label");
                }
            }
            BarButton("LectureLogoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!controller.LectureToolsFolded && !toolbar.IsFolded, "logo restores drawing tools");
            BarButton("LectureFoldButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.LectureToolsFolded && !toolbar.IsVisible, "fold action hides drawing window");
            BarButton("LectureLogoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Store.Serialize() == before && !controller.Store.CanUndo, "folding and restoring controls never mutate annotations or undo history");
            controller.Panel.SearchBox.Text = "安娜"; controller.ShowPanelPage(3);
            BarButton("LectureHeroesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Panel.MainTabs.SelectedIndex == 0 && controller.Panel.SearchBox.Text == "安娜", "hero entry restores the library and preserves its search");
            bar.CreateMoreMenu().Items.OfType<MenuItem>().Single(i => Equals(i.Header, "OBS / 帮助")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(controller.Management.Pages.SelectedIndex == 1, "OBS setup remains reachable from the lecture menu");
            bar.CreateMoreMenu().Items.OfType<MenuItem>().Single(i => Equals(i.Header, "设置与更新")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(controller.Management.Pages.SelectedIndex == 2, "settings remain reachable from the lecture menu");
            controller.SetPassThrough(false);
            check(bar.ModeStateText.Text.Contains("战术模式"), "lecture status reflects edit mode");
            controller.SetPassThrough(true);
            check(bar.ModeStateText.Text.Contains("桌面模式") && (Native.GetWindowLong(new WindowInteropHelper(controller.Overlay).Handle, -20) & Native.ExTransparent) != 0, "lecture status agrees with native click-through");
            BarButton("LectureVisibleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!controller.Store.Current.Visible && controller.PassThrough, "hide action also restores mouse access to the game");
            controller.Undo();

            controller.SetTool(EditorTool.Brush);
            Render(toolPreview, 680, 180, Path.Combine(output, "lecture-drawing-tools.png"));
            check(!Descendants<CheckBox>(toolPreview).Any(c => Equals(c.Content, "英雄名称")), "brush parameters contain no global hero-name setting");
            check(Descendants<ComboBox>(toolPreview).Single(c => c.Name == "BrushDurationChoice").Items.Count == 2, "brush offers permanent and five-second retention");
            Descendants<Button>(toolPreview).Single(b => b.Name == "ClearInkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Store.Current.Items.Count == 2 && controller.Store.Current.Items.All(i => i.Kind == "hero"), "direct clear-ink action preserves heroes");
            controller.Undo(); check(controller.Store.Serialize() == before, "clear ink reverses in one undo");
            controller.SetTool(EditorTool.Move); controller.Editor.SelectedId = scene.Items[0].Id; controller.Notify();
            foreach (var size in new[] { new Size(680, 180), new Size(340, 300), new Size(300, 300) })
            {
                Render(toolPreview, (int)size.Width, (int)size.Height, Path.Combine(output, $"lecture-hero-tools-{size.Width:0}.png"));
                check(Descendants<ScrollViewer>(toolPreview).All(v => v.ScrollableHeight < 1 && v.ScrollableWidth < 1), "hero properties fit without scrolling at " + size.Width);
            }
            var team = Descendants<ComboBox>(toolPreview).Single(c => AutomationProperties.GetName(c) == "选中英雄阵营");
            team.SelectedIndex = 1;
            check(controller.Store.Current.Items[0].Team == "enemy" && controller.Store.Current.Items[1].Team == "ally", "inline team edit changes only the selected hero");
            controller.Undo();
            var trail = controller.Overlay.CreateTrailMenu();
            trail.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "trail-tail")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(controller.Settings.HeroTrailsEnabled && controller.Settings.HeroTrailStyle == HeroTrailStyle.Tail && controller.Store.Serialize() == before, "global trail entry applies the existing preference without changing the scene");
            controller.SetHeroTrails(false, HeroTrailStyle.Dashed, 3);
            controller.SetTool(EditorTool.Brush); controller.SetPassThrough(false);
            var board = new BoardView { Scene = scene, Heroes = controller.Heroes.Heroes, DrawEditorChrome = false };
            var root = new Canvas { Background = BoardView.Brush("#263746"), Width = 1000, Height = 640 };
            board.Width = 1000; board.Height = 640; root.Children.Add(board);
            barPreview.Width = 680; barPreview.Height = 54; Canvas.SetLeft(barPreview, 20); Canvas.SetTop(barPreview, 20); root.Children.Add(barPreview);
            toolPreview.Width = 680; toolPreview.Height = 180; Canvas.SetLeft(toolPreview, 160); Canvas.SetTop(toolPreview, 440); root.Children.Add(toolPreview);
            Render(root, 1000, 640, Path.Combine(output, "lecture-expanded.png"));
            controller.SetLectureToolsFolded(true); toolPreview.Visibility = Visibility.Collapsed;
            Render(root, 1000, 640, Path.Combine(output, "lecture-folded.png"));
            root.Children.Clear(); barPreview.Content = toolPreview.Content = null; bar.Content = barContent; toolbar.Content = toolContent;
            var overlayHandle = new WindowInteropHelper(controller.Overlay).Handle;
            var barHandle = new WindowInteropHelper(bar).EnsureHandle();
            var foreground = Native.GetForegroundWindow();
            controller.ToggleTopmost(); controller.RaisePanel();
            check((Native.GetWindowLong(overlayHandle, -20) & 8) == 0 && (Native.GetWindowLong(barHandle, -20) & 8) != 0, "unpinning canvas keeps lecture bar topmost");
            controller.ToggleTopmost();
            check(Native.GetWindow(barHandle, 4) == overlayHandle && IsAbove(barHandle, overlayHandle), "native ownership keeps the logo bar above the full-screen canvas");
            check(!bar.IsVisible && !controller.Panel.IsVisible && !toolbar.IsVisible && !controller.Overlay.IsVisible && Native.GetForegroundWindow() == foreground, "isolated checks do not show windows or take desktop focus");
        }
        finally { controller.Close(); }
        var closeController = new Controller(test: true);
        var closeHandle = new WindowInteropHelper(closeController.LectureBar).EnsureHandle();
        closeController.LectureBar.Close();
        await Dispatcher.Yield(DispatcherPriority.Background);
        check(closeController.IsClosing && !Native.IsWindow(closeHandle), "native lecture-bar close exits after the closing event without recursive window closure");
    }
    private static ContentControl Frame(FrameworkElement content, Window window) => new() { Content = content, FontFamily = window.FontFamily, FontSize = window.FontSize, Foreground = window.Foreground, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private static bool IsAbove(IntPtr above, IntPtr below)
    {
        for (var current = Native.GetWindow(below, 3); current != IntPtr.Zero; current = Native.GetWindow(current, 3)) if (current == above) return true;
        return false;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n); if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}

