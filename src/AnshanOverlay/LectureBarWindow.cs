using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace AnshanOverlay;

public sealed class LectureBarWindow : Window
{
    private readonly Controller controller;
    private readonly Grid layout = new();
    private readonly StackPanel identity = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal };
    private readonly Button mode;
    private readonly Button heroes;
    private readonly Button visible;
    private readonly Button fold;
    private readonly Button more;
    private bool narrow;
    private readonly TextBlock brandName = new() { Text = "安山守望", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer brandTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private long lastBrandClick;
    private int brandClicks;
    private readonly BrandCelebration celebration;
    public Image AppMark { get; } = new() { Source = Assets.AppIcon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 7, 0) };
    public TextBlock ModeStateText { get; } = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };

    public LectureBarWindow(Controller controller)
    {
        this.controller = controller;
        celebration = new BrandCelebration(this);
        Title = "安山守望 · 讲解浮条"; Icon = Assets.AppIcon;
        Width = Math.Min(controller.Settings.LectureBarWidth, SystemParameters.WorkArea.Width - 24);
        Height = 54; MinWidth = 340; MinHeight = MaxHeight = 54;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Topmost = true;
        Background = Brushes.White; Foreground = BoardView.Brush("#27374A");
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 12; UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(7, 0, 7, 0), GlassFrameThickness = new Thickness(0) });
        WindowLayout.Restore(this, controller.Settings.LectureBarPosition);
        WindowGestures.EnableBlankDrag(this);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(AppMark);
        brand.Children.Add(brandName);
        var logo = Button(brand, "安山守望 · 展开工具", ActivateBrand);
        logo.Name = "LectureLogoButton"; logo.Padding = new Thickness(3);
        identity.Children.Add(logo);
        mode = Button(ModeStateText, "切换战术模式与桌面模式", controller.TogglePassThrough); mode.Name = "LectureModeButton"; mode.Margin = new Thickness(8, 0, 1, 0); identity.Children.Add(mode);
        heroes = Button("英雄", "打开或收起英雄库", controller.ToggleHeroLibrary); heroes.Name = "LectureHeroesButton";
        visible = Button("战术板", "显示 / 隐藏战术板", controller.ToggleVisible); visible.Name = "LectureVisibleButton";
        fold = Button("工具", "打开 / 关闭绘图工具", () => controller.SetLectureToolsFolded(!controller.LectureToolsFolded)); fold.Name = "LectureFoldButton";
        more = Button("···", "场景、设置与更多操作", () => { var menu = CreateMoreMenu(); menu.PlacementTarget = more!; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; });
        more.FontSize = 18; more.Width = 30;
        actions.Children.Add(heroes); actions.Children.Add(fold); actions.Children.Add(visible); actions.Children.Add(more);
        layout.Margin = new Thickness(9, 5, 7, 5);
        layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        identity.VerticalAlignment = actions.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(identity); layout.Children.Add(actions); Grid.SetColumn(actions, 1);
        Content = new Border { Background = Brushes.White, BorderBrush = BoardView.Brush("#CDD0D4"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = layout };
        ((FrameworkElement)Content).SizeChanged += (_, e) => ConfigureLayout(e.NewSize.Width);
        SizeChanged += (_, e) => ConfigureLayout(e.NewSize.Width);
        Closing += (_, e) => { if (!controller.IsClosing) { e.Cancel = true; Dispatcher.BeginInvoke(new Action(controller.Close)); } };
        PreviewKeyDown += (_, e) => { if (e.Key != Key.Tab) controller.HandleEditorKey(e); };
        controller.Changed += Refresh;
        controller.Panel.IsVisibleChanged += (_, _) => Refresh();
        controller.Toolbar.IsVisibleChanged += (_, _) => Refresh();
        brandTimer.Tick += (_, _) => { brandTimer.Stop(); Motion.Text(brandName, "安山守望"); };
        Closed += (_, _) => brandTimer.Stop();
        ConfigureLayout(Width); Refresh();
    }
    internal void ActivateBrand()
    {
        controller.SetLectureToolsFolded(false);
        long now = Environment.TickCount64;
        brandClicks = now - lastBrandClick <= 800 ? brandClicks + 1 : 1; lastBrandClick = now;
        bool surprise = brandClicks >= 5;
        if (surprise) { brandClicks = 0; Motion.Text(brandName, "英雄集结"); brandTimer.Stop(); brandTimer.Start(); celebration.Play(); }
        AppMark.RenderTransformOrigin = new Point(.5, .5);
        var rotation = new RotateTransform(); AppMark.RenderTransform = rotation;
        if (Motion.CanAnimate && AppMark.IsLoaded)
        {
            var turn = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(surprise ? 600 : 280), FillBehavior = FillBehavior.Stop };
            turn.KeyFrames.Add(new EasingDoubleKeyFrame(surprise ? -720 : -10, KeyTime.FromPercent(0)));
            turn.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
            rotation.BeginAnimation(RotateTransform.AngleProperty, turn);
        }
    }
    private Button Button(object content, string hint, Action action)
    {
        var button = new Button { Content = content, ToolTip = hint, Style = (Style)FindResource("QuietButton"), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(1, 0, 1, 0) };
        System.Windows.Automation.AutomationProperties.SetName(button, hint);
        button.Click += (_, _) => action(); return button;
    }
    private void ConfigureLayout(double width)
    {
        narrow = width < 490;
        double height = narrow ? 94 : 54;
        MinHeight = 0; MaxHeight = double.PositiveInfinity; Height = height; MinHeight = MaxHeight = height;
        Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 0 : 1);
        Grid.SetColumnSpan(identity, narrow ? 2 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1);
        layout.RowDefinitions[1].Height = narrow ? new GridLength(40) : new GridLength(0);
        actions.HorizontalAlignment = narrow ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
    public void Refresh()
    {
        Motion.Text(ModeStateText, controller.ModeState);
        mode.ToolTip = ModeStateText.ToolTip = (controller.PassThrough ? "进入战术模式" : "返回桌面模式") + " · " + controller.ModeShortcut;
        Highlight(mode, !controller.PassThrough);
        Highlight(heroes, controller.HeroLibraryOpen);
        Highlight(fold, !controller.LectureToolsFolded);
        Highlight(visible, controller.Store.Current.Visible);
        bool update = controller.Panel.UpdateNotice.Visibility == Visibility.Visible;
        more.Content = update ? "更新" : "···"; more.FontSize = update ? 12 : 18; more.Width = update ? 40 : 30;
        more.Foreground = BoardView.Brush(update ? "#A65300" : "#27374A");
        more.ToolTip = update ? "有新版 · 场景、设置与更多操作" : "场景、设置与更多操作";
    }
    private static void Highlight(Button button, bool active)
    {
        Motion.Paint(button, BackgroundProperty, active ? "#FFF0DF" : "#00FFFFFF");
        Motion.Paint(button, ForegroundProperty, active ? "#A65300" : "#27374A");
        System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "已开启" : "已关闭");
    }
    internal ContextMenu CreateMoreMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool? selected = null)
        {
            var item = new MenuItem { Header = label, IsCheckable = selected.HasValue, IsChecked = selected == true };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("场景与教学屏幕", () => controller.ShowPanelPage(1));
        Add("OBS / 帮助", () => controller.ShowPanelPage(2));
        Add("设置与更新", () => controller.ShowPanelPage(3));
        menu.Items.Add(new Separator());
        Add("英雄名称", () => controller.SetHeroNames(!controller.Store.Current.ShowHeroNames), controller.Store.Current.ShowHeroNames);
        Add("画布置顶", controller.ToggleTopmost, controller.AlwaysOnTop);
        Add("自定义快捷键…", controller.ShowShortcuts);
        menu.Items.Add(new Separator());
        Add("退出程序", controller.Close);
        return menu;
    }
}
