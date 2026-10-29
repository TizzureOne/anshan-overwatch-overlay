using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnshanOverlay;

internal static class ExperienceTests
{
    internal static async Task RunAsync(Action<bool, string> check, string output)
    {
        await LayoutProbeAsync(check, output);
        var controller = new Controller(test: true);
        try
        {
            var management = controller.Management;
            management.Pages.SelectedIndex = 2;
            var content = (FrameworkElement)management.Content; management.Content = null;
            var frame = new ContentControl { Content = content, FontFamily = management.FontFamily, FontSize = management.FontSize, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            ComfortTests.Render(frame, 360, 740, Path.Combine(output, "selection-settings-360.png"));
            var editor = ComfortTests.Descendants<SelectionAppearancePanel>(frame).Single();
            var shape = ComfortTests.Descendants<ComboBox>(editor).Single();
            var color = ComfortTests.Descendants<TextBox>(editor).Single();
            var apply = ComfortTests.Descendants<Button>(editor).Single(b => b.Name == "ApplySelectionStyle");
            string scene = controller.Store.Serialize(); var original = controller.Settings.HeroSelection;
            shape.SelectedIndex = (int)HeroSelectionShape.Ring; color.Text = "#E680A0";
            check(controller.Settings.HeroSelection == original, "selection editor previews a draft until Apply");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Settings.HeroSelection.Shape == HeroSelectionShape.Ring && controller.Overlay.Board.HeroSelection.Color == "#E680A0", "Apply updates real board selection appearance");
            check(controller.Store.Serialize() == scene, "selection customization does not enter scene, undo or OBS data");
            var chosen = controller.Settings.HeroSelection;
            color.Text = "wrong"; check(!apply.IsEnabled && controller.Settings.HeroSelection == chosen, "invalid color cannot replace applied style");
            ComfortTests.Descendants<Button>(editor).Single(b => b.Name == "ResetSelectionStyle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Settings.HeroSelection == new SelectionAppearance() && apply.IsEnabled, "reset restores the complete default style");
            ComfortTests.Render(frame, 500, 740, Path.Combine(output, "selection-settings-500.png"));
            frame.Content = null; management.Content = content;

            string settingsPath = Path.Combine(output, "selection-settings.json");
            controller.Settings.HeroSelection = chosen;
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(controller.Settings, Assets.Json));
            var loaded = Preferences.Load(out var warning, settingsPath);
            check(warning is null && loaded.HeroSelection == chosen, "selection appearance survives a settings roundtrip");
            File.WriteAllText(settingsPath, "{\"heroSelection\":{\"shape\":99,\"color\":\"bad\",\"thickness\":-4,\"gap\":999,\"length\":0,\"opacity\":2}}");
            loaded = Preferences.Load(out warning, settingsPath);
            check(loaded.HeroSelection.Shape == HeroSelectionShape.Corners && loaded.HeroSelection.Color == "#7DE2D1" && loaded.HeroSelection.Thickness == 1 && loaded.HeroSelection.Gap == 24 && loaded.HeroSelection.Length == 4 && loaded.HeroSelection.Opacity == 1, "invalid saved appearance values are normalized");

            bool names = controller.Store.Current.ShowHeroNames;
            controller.Overlay.CreateBoardMenu().Items.OfType<MenuItem>().Single(i => Equals(i.Header, names ? "关闭英雄名显示" : "开启英雄名显示")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(controller.Store.Current.ShowHeroNames != names && controller.Store.Serialize().Contains("\"showHeroNames\":" + (!names ? "true" : "false")), "blank-board name toggle changes the shared scene for OBS");
            controller.Undo(); check(controller.Store.Current.ShowHeroNames == names, "hero-name menu change is undoable");
            var menu = controller.Overlay.CreateBoardMenu(); menu.Measure(new Size(menu.Width, double.PositiveInfinity));
            ComfortTests.Render(menu, (int)menu.Width, (int)Math.Ceiling(menu.DesiredSize.Height), Path.Combine(output, "board-menu.png"));

            controller.ShowPanelPage(0); controller.SetLectureToolsFolded(true);
            for (int n = 0; n < 5; n++) controller.LectureBar.ActivateBrand();
            var barContent = (FrameworkElement)controller.LectureBar.Content; controller.LectureBar.Content = null;
            var barFrame = new ContentControl { Content = barContent, FontFamily = controller.LectureBar.FontFamily, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            ComfortTests.Render(barFrame, 340, 94, Path.Combine(output, "brand-surprise.png"));
            check(ComfortTests.Descendants<TextBlock>(barFrame).Any(t => t.Text == "英雄集结") && controller.HeroLibraryOpen && !controller.LectureToolsFolded && controller.Store.Serialize() == scene, "brand easter egg preserves the scene and still restores only tools");
            barFrame.Content = null; controller.LectureBar.Content = barContent;

            var hero = new SceneItem { HeroId = "ana", X = 160, Y = 90, Size = 60 };
            var board = new BoardView { Scene = new Scene { Width = 320, Height = 180, ShowHeroNames = false, Items = new() { hero } }, Editing = true, SelectedIds = new HashSet<string> { hero.Id } };
            var pictures = new List<byte[]>();
            foreach (HeroSelectionShape style in Enum.GetValues<HeroSelectionShape>())
            {
                board.HeroSelection = new() { Shape = style }; board.InvalidateVisual();
                pictures.Add(ComfortTests.Render(board, 320, 180, Path.Combine(output, "selection-" + style + ".png")));
            }
            check(!pictures[0].SequenceEqual(pictures[1]) && !pictures[1].SequenceEqual(pictures[2]) && !pictures[2].SequenceEqual(pictures[3]), "selection shapes produce distinct real rendering");
            board.SelectedIds = new HashSet<string>(); board.InvalidateVisual();
            check(pictures[3].SequenceEqual(ComfortTests.Render(board, 320, 180, Path.Combine(output, "selection-clean.png"))), "hidden selection leaves the hero image unchanged");

            board = controller.Overlay.Board;
            var drawingRoot = (Panel)VisualTreeHelper.GetParent(board); drawingRoot.Children.Remove(board);
            using (var host = new HwndSource(new HwndSourceParameters("Anshan isolated click feedback") { Width = 400, Height = 240, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = Native.ExNoActivate | Native.ExToolWindow }) { RootVisual = board })
            {
                controller.Store.Preview(new Scene { Width = 400, Height = 240 }); controller.SetTool(EditorTool.Move); controller.SetPassThrough(false);
                Motion.Enabled = true; await Task.Delay(30);
                bool animate = Motion.CanAnimate;
                string beforeClick = controller.Store.Serialize();
                ComfortTests.Render(board, 400, 240, Path.Combine(output, "click-before.png"));
                board.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                board.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                for (int n = 0; animate && (double)board.GetValue(BoardView.ClickProgressProperty) == 1 && n < 20; n++)
                { await Dispatcher.Yield(DispatcherPriority.Render); await Task.Delay(15); }
                check(!animate || (double)board.GetValue(BoardView.ClickProgressProperty) < 1, "actual board pointer-down starts local click feedback");
                board.ShowClick(new Point(200, 100)); await Task.Delay(45);
                ComfortTests.Render(board, 400, 240, Path.Combine(output, "click-early.png"));
                board.DrawEditorChrome = false; board.InvalidateVisual();
                var obsDuring = ComfortTests.Render(board, 400, 240, Path.Combine(output, "click-obs-during.png"));
                board.DrawEditorChrome = true; board.InvalidateVisual();
                await Task.Delay(180); ComfortTests.Render(board, 400, 240, Path.Combine(output, "click-fading.png"));
                await Task.Delay(260); ComfortTests.Render(board, 400, 240, Path.Combine(output, "click-settled.png"));
                check((double)board.GetValue(BoardView.ClickProgressProperty) == 1 && controller.Store.Serialize() == beforeClick, "click feedback ends cleanly without changing scene or undo state");
                board.DrawEditorChrome = false; board.InvalidateVisual();
                check(obsDuring.SequenceEqual(ComfortTests.Render(board, 400, 240, Path.Combine(output, "click-obs-after.png"))), "click feedback never enters OBS rendering");
                board.DrawEditorChrome = true; board.ShowClick(new Point(200, 100)); controller.SetPassThrough(true);
                check((double)board.GetValue(BoardView.ClickProgressProperty) == 1, "returning to desktop clears an active click effect");
                Motion.Enabled = false; controller.SetPassThrough(false); board.ShowClick(new Point(200, 100));
                check((double)board.GetValue(BoardView.ClickProgressProperty) == 1, "animation opt-out suppresses click effects");
                host.RootVisual = null;
            }
            drawingRoot.Children.Insert(0, board);
        }
        finally { Motion.Enabled = false; controller.Close(); }
    }
    internal static Task LayoutProbeAsync(Action<bool, string> check, string output)
    {
        var results = new List<object>();
        bool fits = true;
        var controller = new Controller(test: true);
        try
        {
            var hero = new SceneItem { HeroId = "ana", X = 400, Y = 300, Size = 80 };
            controller.Store.Preview(new Scene { Width = 1000, Height = 640, Items = new() { hero } });
            controller.SetTool(EditorTool.Move); controller.Editor.SelectedId = hero.Id; controller.Notify();
            var toolbar = controller.Toolbar;
            var content = (FrameworkElement)toolbar.Content; toolbar.Content = null;
            var frame = new ContentControl { Content = content, FontFamily = toolbar.FontFamily, FontSize = toolbar.FontSize, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            foreach (var size in new[] { new Size(645, 162), new Size(482, 168), new Size(300, 150), new Size(680, 150) })
            {
                toolbar.Width = size.Width; toolbar.Height = size.Height;
                string path = Path.Combine(output, $"resize-{size.Width}.png");
                ComfortTests.Render(frame, (int)size.Width, (int)Math.Max(toolbar.Height, toolbar.MinHeight), path);
                ComfortTests.Render(frame, (int)size.Width, (int)Math.Max(toolbar.Height, toolbar.MinHeight), path);
                var scroll = ComfortTests.Descendants<ScrollViewer>(frame).First(s => s.Content is WrapPanel);
                bool visible = scroll.ScrollableHeight < 1 && scroll.ViewportHeight > 30;
                fits &= visible;
                results.Add(new { requestedWidth = size.Width, requestedHeight = size.Height, height = Math.Max(toolbar.Height, toolbar.MinHeight), scroll.ViewportHeight, scroll.ScrollableHeight, fits = visible });
            }
            frame.Content = null; toolbar.Content = content;
        }
        finally { controller.Close(); }
        File.WriteAllText(Path.Combine(output, "resize-probe.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        check(fits, "resizing between the reported toolbar sizes keeps every selected-hero parameter visible");
        return Task.CompletedTask;
    }
}
