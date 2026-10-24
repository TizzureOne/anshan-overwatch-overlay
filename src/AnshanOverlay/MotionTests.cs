using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnshanOverlay;

internal static class MotionTests
{
    public static async Task RunAsync(Controller controller, Action<bool, string> check, string output)
    {
        var panel = controller.Panel; var toolbar = controller.Toolbar; var board = controller.Overlay.Board;
        var panelContent = (FrameworkElement)panel.Content; var toolbarContent = (FrameworkElement)toolbar.Content;
        var canvasContent = (FrameworkElement)controller.Overlay.Content;
        panelContent.Width = panelContent.Height = toolbarContent.Width = toolbarContent.Height = double.NaN;
        panel.Content = toolbar.Content = controller.Overlay.Content = null;
        var panelFrame = new Border { Child = panelContent, Width = 474, VerticalAlignment = VerticalAlignment.Top };
        var toolbarFrame = new Border { Child = toolbarContent, Width = 660, VerticalAlignment = VerticalAlignment.Top };
        var canvasFrame = new Border { Child = canvasContent, Background = BoardView.Brush("#263746"), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 260, 0, 0), Height = 410, VerticalAlignment = VerticalAlignment.Top };
        var menuFrame = new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 335, 12, 0) };
        var right = new Grid { Margin = new Thickness(490, 16, 0, 0) }; right.Children.Add(toolbarFrame); right.Children.Add(canvasFrame); right.Children.Add(menuFrame);
        var root = new Grid { Width = 1160, Height = 920, Background = BoardView.Brush("#E8EDF4") }; root.Children.Add(panelFrame); root.Children.Add(right);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display); TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        panelFrame.HorizontalAlignment = HorizontalAlignment.Left; panelFrame.Margin = new Thickness(8, 8, 0, 0);
        // This HWND is never shown and receives no desktop input. Its visual tree
        // supplies real WPF Loaded events and animation clocks for isolated journeys.
        using var source = new HwndSource(new HwndSourceParameters("Anshan isolated motion journey") {
            Width = 1160, Height = 920, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = Native.ExNoActivate | Native.ExToolWindow
        }) { RootVisual = root };
        string frames = Path.Combine(output, "motion-journey"); Directory.CreateDirectory(frames);
        int frame = 0;
        void Layout()
        {
            panelFrame.Height = panel.Height; toolbarFrame.Height = toolbar.Height;
            root.Measure(new Size(1160, 920)); root.Arrange(new Rect(0, 0, 1160, 920)); root.UpdateLayout();
        }
        async Task Capture(string stage)
        {
            for (int n = 0; n < 6; n++)
            {
                await Dispatcher.Yield(DispatcherPriority.Render); Layout();
                Save(root, 1160, 920, Path.Combine(frames, $"{frame++:000}-{stage}.png"));
                await Task.Delay(45);
            }
        }
        async Task Settle(Func<bool> finished)
        {
            // Hidden presentation targets can be throttled. Observe completion
            // instead of assuming a wall-clock delay means a frame was presented.
            for (int n = 0; n < 80 && !finished(); n++) { await Task.Delay(25); await Dispatcher.Yield(DispatcherPriority.Render); }
        }
        async Task ClickHero(string id)
        {
            var hero = controller.Heroes.Heroes.Single(h => h.Id == id);
            Descendants<RadioButton>(panelContent).First(r => Equals(r.Tag, hero.Role)).IsChecked = true;
            panel.SearchBox.Text = hero.Name; Layout(); await Dispatcher.Yield(DispatcherPriority.Loaded); Layout();
            var button = Descendants<Button>(panel.HeroList).Single(b => b.Tag is Hero h && h.Id == id);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        try
        {
            panel.MainTabs.SelectedIndex = 0; panel.BatchMode.IsChecked = false; panel.SearchBox.Text = "";
            controller.Store.Preview(new Scene { Width = 1280, Height = 720 }); controller.Editor.SelectedId = null; controller.SetPassThrough(true);
            await Dispatcher.Yield(DispatcherPriority.Loaded); Layout();
            check(panelContent.IsLoaded && board.IsLoaded, "isolated journey runs loaded WPF visuals without showing native windows");
            Motion.Enabled = true;
            bool animated = Motion.CanAnimate;
            string beforeHeroClick = controller.Store.Serialize();
            await ClickHero("ana");
            controller.Editor.Down(new Point(240, 220)); controller.Editor.Up(); controller.Overlay.Refresh();
            check(controller.Store.Serialize() == beforeHeroClick && controller.PassThrough, "hero card and canvas clicks cannot place a hero or leave mouse pass-through");
            await Capture("search-before-drag");
            controller.SetPassThrough(false); controller.PlaceHero(controller.Heroes.Heroes.Single(h => h.Id == "ana"), new Point(240, 220)); controller.Panel.FinishHeroDrag(true);
            check(controller.Store.Current.Items.Single().HeroId == "ana" && controller.PassThrough, "hero drop creates the intended hero and restores mouse pass-through during its entrance");
            check(!animated || board.HasAnimatedProperties, "hero entrance uses the live WPF animation clock");
            await Capture("hero-entrance");
            panel.BatchMode.IsChecked = true; panel.RedTeam.IsChecked = true;
            await ClickHero("ana"); await ClickHero("genji");
            check(panel.BatchHint.Text.StartsWith("已选 2 位"), "animated filtering and role switching preserve the pending lineup");
            panel.AddHeroesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Store.Current.Items.Count == 3 && controller.Store.Current.Items.Count(i => i.Team == "enemy") == 2, "animated batch action adds exactly the selected red team");
            await Capture("batch-add");
            var arrow = Descendants<Button>(toolbarContent).Single(b => b.Content is StackPanel stack && stack.Children.OfType<TextBlock>().Any(t => t.Text == "箭头"));
            arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(controller.Editor.Tool == EditorTool.Arrow, "toolbar click changes the tool immediately during its visual transition");
            controller.Editor.Down(new Point(120, 400)); controller.Editor.Move(new Point(660, 480), false);
            check(controller.Store.Current.Items[^1].End == new Point(660, 480), "drawing geometry follows the pointer immediately without animated position lag");
            controller.Editor.Up(); await Capture("tool-and-draw");
            controller.SetTool(EditorTool.Move); controller.Editor.SelectedId = controller.Store.Current.Items[0].Id; controller.Overlay.Refresh(); controller.Notify(); Layout();
            var size = Descendants<Slider>(toolbarContent).First(); size.Value = 96;
            check(controller.Store.Current.Items[0].Size == 96, "property slider changes the selected hero immediately during parameter transitions");
            await Capture("selection-properties");
            check((double)board.GetValue(BoardView.ModeOpacityProperty) == 1 && (double)board.GetValue(BoardView.VisibilityProgressProperty) == 1, "editing transitions settle at original scene opacity instead of the dependency property default");
            var menu = controller.Overlay.CreateHeroMenu(controller.Store.Current.Items[0]);
            using var menuSource = new HwndSource(new HwndSourceParameters("Anshan isolated menu") { Width = 264, Height = 320, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = Native.ExNoActivate | Native.ExToolWindow }) { RootVisual = menu };
            menu.Measure(new Size(264, 320)); menu.Arrange(new Rect(0, 0, 264, menu.DesiredSize.Height)); menu.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            menuFrame.Child = new System.Windows.Shapes.Rectangle { Width = 264, Height = menu.ActualHeight, Fill = new VisualBrush(menu) }; Layout();
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            check(menu.Items.OfType<MenuItem>().All(i => !i.HasItems), "polished menu retains directly accessible commands without submenus");
            check(menu.ActualHeight < 320 && menu.ActualWidth == 264, "polished menu stays compact after real layout and styling");
            await Capture("context-menu");
            menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "120 px")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(controller.Store.Current.Items[0].Size == 120, "compact size command changes its original target hero"); menuFrame.Child = null;
            string sceneBeforeMode = controller.Store.Serialize();
            controller.SetPassThrough(true);
            check((Native.GetWindowLong(new WindowInteropHelper(controller.Overlay).Handle, -20) & Native.ExTransparent) != 0, "mouse pass-through takes effect immediately while visual opacity transitions");
            await Capture("pass-through");
            check(Math.Abs((double)board.GetValue(BoardView.ModeOpacityProperty) - .5) < .001, "mode animation settles at the configured opacity");
            controller.SetPassThrough(false); controller.SetPassThrough(true); await Task.Delay(230);
            await Settle(() => (double)board.GetValue(BoardView.ModeOpacityProperty) == .5);
            check(controller.Store.Serialize() == sceneBeforeMode && Math.Abs((double)board.GetValue(BoardView.ModeOpacityProperty) - .5) < .001, "rapid mode reversals settle correctly without changing scene data");
            controller.ToggleVisible(); await Capture("hide-annotations"); controller.ToggleVisible(); await Capture("show-annotations");
            check(controller.Store.Serialize() == sceneBeforeMode, "visibility transitions leave the final scene exactly unchanged after a roundtrip");
            panel.MainTabs.SelectedIndex = 1; await Capture("settings-page");
            double expanded = panel.ExpandedHeight; panel.SetFolded(true); await Task.Delay(45);
            check(!animated || panel.Height > MainWindow.FoldedHeight && panel.Height < expanded, "panel folding passes through intermediate window sizes");
            panel.SetFolded(false);
            if (animated)
            {
                var panelHandle = new WindowInteropHelper(panel).Handle;
                Native.GetWindowRect(panelHandle, out var rectangle);
                Native.SetWindowPos(panelHandle, IntPtr.Zero, 0, 0, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top - 5, 0x2 | 0x4 | 0x10);
            }
            check(panel.ExpandedHeight == expanded, "native in-between size messages cannot overwrite the persisted expanded height");
            panel.SetFolded(true); panel.SetFolded(false); await Capture("rapid-fold-restore");
            await Settle(() => panel.Height == expanded && panel.MinHeight == 420);
            check(panel.Height == expanded && panel.ExpandedHeight == expanded && panel.MinHeight == 420, $"rapid fold reversals restore the intended full size and minimum height (actual={panel.Height:R}, saved={panel.ExpandedHeight:R}, target={expanded:R}, minimum={panel.MinHeight:R})");
            double toolbarExpanded = toolbar.ExpandedHeight; toolbar.SetFolded(true); toolbar.SetFolded(false); await Settle(() => toolbar.Height == toolbarExpanded && toolbar.MinHeight == 150);
            check(toolbar.Height == toolbarExpanded && toolbar.MinHeight == 150, "toolbar fold reversals preserve its expanded size");
            check(Native.GetWindow(new WindowInteropHelper(panel).Handle, 4) == new WindowInteropHelper(controller.Overlay).Handle, "window animation preserves the panel ownership that prevents canvas obstruction");
            board.DrawEditorChrome = false; Save(board, 1280, 720, Path.Combine(frames, "obs-before.png"));
            board.BeginAnimation(BoardView.ModeOpacityProperty, null); board.SetCurrentValue(BoardView.ModeOpacityProperty, .1);
            board.SetCurrentValue(BoardView.HeroProgressProperty, .2); board.SetCurrentValue(BoardView.SelectionProgressProperty, .3);
            Save(board, 1280, 720, Path.Combine(frames, "obs-during.png"));
            check(File.ReadAllBytes(Path.Combine(frames, "obs-before.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(frames, "obs-during.png"))), "all desktop motion values leave clean OBS rendering pixel-identical");
            board.DrawEditorChrome = true; board.SetCurrentValue(BoardView.HeroProgressProperty, 1d); board.SetCurrentValue(BoardView.SelectionProgressProperty, 1d);
            Motion.Enabled = false; controller.Overlay.Refresh(); controller.SetPassThrough(false); controller.SetPassThrough(true);
            check((double)board.GetValue(BoardView.ModeOpacityProperty) == .5, "disabling motion immediately settles an interrupted canvas transition");
            panel.SetFolded(true); panel.SetFolded(false);
            check(panel.Height == expanded && panel.MinHeight == 420, "motion opt-out makes window resizing immediate with final dimensions intact");
            File.WriteAllText(Path.Combine(frames, "journey.json"), System.Text.Json.JsonSerializer.Serialize(new { frames = frame, activeAnimations = animated, desktopInput = false, nativeWindowsShown = false }));
        }
        finally
        {
            Motion.Enabled = false; board.DrawEditorChrome = true; menuFrame.Child = null;
            source.RootVisual = null; panelFrame.Child = toolbarFrame.Child = canvasFrame.Child = null;
            panel.Content = panelContent; toolbar.Content = toolbarContent; controller.Overlay.Content = canvasContent;
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        {
            var child = VisualTreeHelper.GetChild(root, n); if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Save(FrameworkElement element, int width, int height, string path)
    {
        element.InvalidateVisual(); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
