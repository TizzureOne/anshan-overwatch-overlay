using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace AnshanOverlay;

public sealed class ManagementWindow : Window
{
    public TabControl Pages { get; } = new() { Background = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(4) };
    public ManagementWindow(Controller controller, IEnumerable<TabItem> pages)
    {
        Title = "安山守望 · 设置与场景"; Icon = Assets.AppIcon;
        Width = 500; Height = Math.Min(740, SystemParameters.WorkArea.Height - 24); MinWidth = 360; MinHeight = 420;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Topmost = true;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; Background = Brushes.White; UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(7), GlassFrameThickness = new Thickness(0) });
        var root = new DockPanel();
        var header = new DockPanel { Height = 42, Background = BoardView.Brush("#F7F7F8"), LastChildFill = true };
        var close = new Button { Content = "×", FontSize = 20, Width = 32, MinHeight = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 4, 8, 4), Style = (Style)FindResource("QuietButton"), ToolTip = "关闭设置窗口" };
        close.Click += (_, _) => Motion.HideWindow(this); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        title.Children.Add(new Image { Source = Assets.AppIcon, Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) });
        title.Children.Add(new TextBlock { Text = "设置与场景", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        foreach (var page in pages) Pages.Items.Add(page);
        Pages.SelectionChanged += (_, e) => { if (e.Source == Pages && Pages.SelectedContent is FrameworkElement content) Motion.Reveal(content); };
        root.Children.Add(Pages); Content = new Border { Child = root, Background = Brushes.White, BorderBrush = BoardView.Brush("#CDD0D4"), BorderThickness = new Thickness(1) };
        WindowGestures.EnableBlankDrag(this);
        Closing += (_, e) => { if (!controller.IsClosing) { e.Cancel = true; Dispatcher.BeginInvoke(new Action(() => { if (!controller.IsClosing) Motion.HideWindow(this); })); } };
        PreviewKeyDown += (_, e) => { if (Keyboard.FocusedElement is not TextBoxBase and not ComboBox and not RangeBase && e.Key != Key.Tab) controller.HandleEditorKey(e); };
    }
}
