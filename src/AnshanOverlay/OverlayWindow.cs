using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Linq;
using System.Windows.Controls.Primitives;

namespace AnshanOverlay;

public sealed class OverlayWindow : Window
{
    public BoardView Board { get; }
    internal InlineTextEditor TextEditor { get; }
    private readonly Grid drawingRoot = new();
    private readonly Controller controller;
    private ContextMenu? heroMenu;
    public OverlayWindow(Controller controller)
    {
        this.controller = controller;
        Title = "安山守望 · 透明画布";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Board = new BoardView { Heroes = controller.Heroes.Heroes, AllowDrop = true };
        Board.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("AnshanHero") ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Board.Drop += (_, e) => {
            if (e.Data.GetData("AnshanHero") is string id && controller.Heroes.Heroes.FirstOrDefault(h => h.Id == id) is { } hero)
            { e.Effects = controller.PlaceHero(hero, e.GetPosition(Board)) ? DragDropEffects.Copy : DragDropEffects.None; }
            e.Handled = true;
        };
        TextEditor = new InlineTextEditor(controller);
        drawingRoot.Children.Add(Board); drawingRoot.Children.Add(TextEditor);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = drawingRoot };
        Board.MouseLeftButtonDown += (_, e) =>
        {
            if (controller.PassThrough) return;
            Board.ShowClick(e.GetPosition(Board));
            if (HandleTextClick(e.GetPosition(Board), e.ClickCount)) { e.Handled = true; return; }
            Board.Focus(); controller.Editor.Down(e.GetPosition(Board), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            if (controller.Editor.IsDragging) Board.CaptureMouse();
            else if (controller.Store.Current.Items.Count >= SceneStore.MaxItems) controller.ReportItemLimit();
            Refresh(); controller.Notify(); e.Handled = true;
        };
        Board.MouseMove += (_, e) =>
        {
            Board.EraserPosition = !controller.PassThrough && controller.Editor.Tool == EditorTool.Eraser ? e.GetPosition(Board) : null;
            Board.EraserRadius = controller.Editor.EraserSize / 2;
            var previous = controller.Store.Current;
            controller.Editor.Move(e.GetPosition(Board), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            if (ReferenceEquals(previous, controller.Store.Current)) Refresh();
            if (controller.Editor.SelectionBox is not null) controller.Notify();
        };
        Board.MouseLeave += (_, _) => { Board.EraserPosition = null; Board.InvalidateVisual(); };
        Board.MouseLeftButtonUp += (_, e) => { controller.Editor.Up(); Board.ReleaseMouseCapture(); Refresh(); controller.Notify(); e.Handled = true; };
        Board.LostMouseCapture += (_, _) => { controller.Editor.Cancel(); Refresh(); };
        Board.MouseRightButtonDown += (_, e) =>
        {
            if (controller.PassThrough) return;
            if (!TextEditor.Commit()) { e.Handled = true; return; }
            Board.ShowClick(e.GetPosition(Board));
            controller.Editor.Cancel(); Board.ReleaseMouseCapture();
            var point = e.GetPosition(Board);
            var hit = controller.Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => Editor.Hit(i, point));
            if (hit is { Kind: "hero" })
            {
                if (heroMenu is not null) heroMenu.IsOpen = false;
                heroMenu = CreateHeroMenu(hit); heroMenu.PlacementTarget = Board; heroMenu.Placement = PlacementMode.MousePoint; heroMenu.IsOpen = true;
            }
            else if (hit is null)
            {
                if (heroMenu is not null) heroMenu.IsOpen = false;
                heroMenu = CreateBoardMenu(); heroMenu.PlacementTarget = Board; heroMenu.Placement = PlacementMode.MousePoint; heroMenu.IsOpen = true;
            }
            else controller.SetTool(EditorTool.Move);
            e.Handled = true;
        };
        Board.MouseWheel += (_, e) => { controller.Editor.ResizeSelected(Math.Sign(e.Delta)); e.Handled = true; };
        PreviewKeyDown += (_, e) => controller.HandleEditorKey(e);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Keep the full-monitor overlay from suppressing the auto-hide taskbar.
            Native.SetProp(handle, "NonRudeHWND", new IntPtr(1));
            Native.SetPassThrough(handle, controller.PassThrough);
        };
        Closing += (_, _) => Native.RemoveProp(new WindowInteropHelper(this).Handle, "NonRudeHWND");
        Activated += (_, _) => controller.RaisePanel();
        Closed += (_, _) => { if (!controller.IsClosing) controller.Close(); };
    }
    public void Refresh()
    {
        Board.Scene = controller.DisplayScene; Board.Width = Board.Scene.Width; Board.Height = Board.Scene.Height;
        drawingRoot.Width = TextEditor.Width = Board.Width; drawingRoot.Height = TextEditor.Height = Board.Height;
        Board.SelectedIds = controller.Editor.SelectedIds; Board.SelectionBox = controller.Editor.SelectionBox; Board.Editing = !controller.PassThrough;
        Board.ModeHint = controller.ModeState + " · " + controller.ModeShortcut;
        Board.PassThroughOpacity = controller.Settings.PassThroughOpacityPercent / 100d;
        Board.HeroSelection = controller.Settings.HeroSelection;
        Board.DraggedHeroId = controller.Editor.IsMoving && controller.Editor.Selected?.Kind == "hero" ? controller.Editor.SelectedId : null;
        if (controller.Editor.Tool != EditorTool.Eraser || controller.PassThrough) Board.EraserPosition = null;
        Board.Cursor = controller.Editor.Tool == EditorTool.Move ? Cursors.Arrow : controller.Editor.Tool == EditorTool.Text ? Cursors.IBeam : controller.Editor.Tool == EditorTool.Eraser ? Cursors.Cross : Cursors.Pen;
        Board.InvalidateVisual();
    }
    internal bool HandleTextClick(Point point, int clickCount)
    {
        if (TextEditor.IsEditing) { TextEditor.Commit(); return true; }
        var hit = controller.Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => Editor.Hit(i, point));
        if (hit?.Kind == "text" && (clickCount == 2 || controller.Editor.Tool == EditorTool.Text))
        { TextEditor.Begin(point, hit); return true; }
        if (controller.Editor.Tool == EditorTool.Text && hit?.Kind != "hero")
        { TextEditor.Begin(point); return true; }
        return false;
    }
    public void Place(DisplayInfo screen)
    {
        var handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(handle, IntPtr.Zero, screen.X, screen.Y, screen.Width, screen.Height, 0x4 | 0x10);
    }
    public void SetPassThrough(bool enabled)
    {
        controller.Editor.Cancel(); Board.ReleaseMouseCapture();
        if (enabled && heroMenu is not null) { heroMenu.IsOpen = false; heroMenu = null; }
        Native.SetPassThrough(new WindowInteropHelper(this).Handle, enabled);
        Refresh();
    }
    public ContextMenu CreateHeroMenu(SceneItem hero)
    {
        if (!controller.Editor.SelectedIds.Contains(hero.Id)) controller.Editor.SelectedId = hero.Id;
        var heroes = controller.Editor.SelectedItems.Where(i => i.Kind == "hero").ToArray();
        var ids = heroes.Select(i => i.Id).ToHashSet();
        bool multiple = heroes.Length > 1;
        Refresh(); controller.Notify();
        var panel = new FrameworkElementFactory(typeof(WrapPanel)); panel.SetValue(WidthProperty, 238d);
        var menu = new ContextMenu { Width = 264, FontFamily = new FontFamily("Microsoft YaHei UI"), ItemsPanel = new ItemsPanelTemplate(panel) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new Image { Source = Assets.Portrait(hero.HeroId), Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0) });
        var caption = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        caption.Children.Add(new TextBlock { Text = multiple ? $"已选 {heroes.Length} 位英雄" : controller.Heroes.Heroes.First(h => h.Id == hero.HeroId).Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = BoardView.Brush("#27374A") });
        caption.Children.Add(new TextBlock { Text = multiple ? "批量调整英雄标注" : "英雄标注", FontSize = 10, Foreground = BoardView.Brush("#67768A"), Margin = new Thickness(0, 2, 0, 0) }); title.Children.Add(caption);
        void Section(object header) => menu.Items.Add(new MenuItem { Header = header, Width = 238, Style = (Style)FindResource("MenuSection") });
        void Divider() => menu.Items.Add(new Separator { Width = 234, Height = 1, Margin = new Thickness(2, 5, 2, 5), Background = BoardView.Brush("#E4E8EE") });
        Section(title); Divider();
        Section(new TextBlock { Text = "移动轨迹 · 所有英雄", FontSize = 10, Foreground = BoardView.Brush("#67768A") });
        AddTrailControls(menu); Divider();
        Section(new TextBlock { Text = "阵营", FontSize = 10, Foreground = BoardView.Brush("#67768A") });
        // Keep choices in one popup so screen-edge placement never requires entering a flipped submenu.
        foreach (var (id, name, color) in new[] { ("ally", "友方 · 蓝色", "#39A9FF"), ("enemy", "敌方 · 红色", "#F05265") })
        {
            var item = new MenuItem { Header = name, Width = 115, IsCheckable = true, IsChecked = heroes.All(h => h.Team == id), Foreground = BoardView.Brush(color), Padding = new Thickness(7, 7, 7, 7) };
            item.Click += (_, _) => controller.UpdateHeroes(ids, h => h with { Team = id }); menu.Items.Add(item);
        }
        Divider();
        Section(new TextBlock { Text = heroes.All(h => h.Size == hero.Size) ? $"尺寸 · 当前 {hero.Size:0} px" : "尺寸 · 多种", FontSize = 10, Foreground = BoardView.Brush("#67768A") });
        foreach (double size in new double[] { 40, 60, 80, 100, 120, 160 })
        {
            var item = new MenuItem { Header = $"{size:0} px", Width = 75, FontSize = 11, Padding = new Thickness(6, 7, 6, 7), IsCheckable = true, IsChecked = heroes.All(h => Math.Abs(h.Size - size) < 0.1) };
            item.Click += (_, _) => controller.UpdateHeroes(ids, h => h with { Size = size }); menu.Items.Add(item);
        }
        Divider();
        var delete = new MenuItem { Header = multiple ? $"删除 {heroes.Length} 位英雄" : "删除", Width = 234, Foreground = BoardView.Brush("#C64254") };
        delete.Click += (_, _) => { controller.Editor.Cancel(); controller.Store.Edit(s => s with { Items = s.Items.Where(i => !ids.Contains(i.Id)).ToList() }); }; menu.Items.Add(delete);
        return menu;
    }
    internal ContextMenu CreateBoardMenu()
    {
        var menu = new ContextMenu { Width = 232, FontFamily = new FontFamily("Microsoft YaHei UI") };
        void Add(string label, string symbol, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, Icon = new TextBlock { Text = symbol, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center }, IsEnabled = enabled, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(2, 1, 2, 1) };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("退出战术模式", "↗", () => controller.SetPassThrough(true));
        menu.Items.Add(new Separator());
        Add("打开英雄库", "＋", () => controller.ShowPanelPage(0));
        Add(controller.Store.Current.ShowHeroNames ? "关闭英雄名显示" : "开启英雄名显示", "名", () => controller.SetHeroNames(!controller.Store.Current.ShowHeroNames));
        Add(controller.LectureToolsFolded ? "打开绘图工具" : "关闭绘图工具", "✎", () => controller.SetLectureToolsFolded(!controller.LectureToolsFolded));
        Add("移动与选择", "↔", () => controller.SetTool(EditorTool.Move));
        Add("画笔", "╱", () => controller.SetTool(EditorTool.Brush));
        Add("箭头", "↗", () => controller.SetTool(EditorTool.Arrow));
        menu.Items.Add(new Separator());
        Add("撤销", "↶", controller.Undo, controller.Store.CanUndo || controller.Editor.Temporary.CanUndo);
        Add("清除笔迹 · 保留英雄", "⌫", () => controller.Clear(true), controller.Store.Current.Items.Any(i => i.Kind != "hero") || controller.Editor.Temporary.Items.Length > 0 || controller.Editor.Trails.Items.Length > 0);
        return menu;
    }
    internal ContextMenu CreateTrailMenu()
    {
        var panel = new FrameworkElementFactory(typeof(WrapPanel)); panel.SetValue(WidthProperty, 238d);
        var menu = new ContextMenu { Width = 264, FontFamily = new FontFamily("Microsoft YaHei UI"), ItemsPanel = new ItemsPanelTemplate(panel) };
        menu.Items.Add(new MenuItem { Header = "移动轨迹 · 所有英雄", Width = 238, Style = (Style)FindResource("MenuSection") });
        AddTrailControls(menu); return menu;
    }
    private void AddTrailControls(ContextMenu menu)
    {
        var choices = new System.Collections.Generic.List<MenuItem>();
        var duration = new Slider { Minimum = 1, Maximum = 10, Value = controller.Settings.HeroTrailSeconds, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(duration, "移动轨迹保留秒数");
        var label = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        bool refreshing = false;
        void Sync()
        {
            refreshing = true;
            choices[0].IsChecked = !controller.Settings.HeroTrailsEnabled;
            choices[1].IsChecked = controller.Settings.HeroTrailsEnabled && controller.Settings.HeroTrailStyle == HeroTrailStyle.Dashed;
            choices[2].IsChecked = controller.Settings.HeroTrailsEnabled && controller.Settings.HeroTrailStyle == HeroTrailStyle.Tail;
            duration.IsEnabled = controller.Settings.HeroTrailsEnabled;
            duration.Value = controller.Settings.HeroTrailSeconds; label.Text = $"保留 {controller.Settings.HeroTrailSeconds} 秒";
            refreshing = false;
        }
        void Apply(bool enabled, HeroTrailStyle style, int seconds)
        {
            if (controller.SetHeroTrails(enabled, style, seconds) is { } error) controller.SetStatus(error);
            Sync();
        }
        foreach (var (name, tag, enabled, style) in new[] {
            ("关闭", "trail-off", false, controller.Settings.HeroTrailStyle),
            ("虚线", "trail-dashed", true, HeroTrailStyle.Dashed),
            ("拖尾", "trail-tail", true, HeroTrailStyle.Tail) })
        {
            var item = new MenuItem { Header = name, Tag = tag, Width = 75, FontSize = 12, IsCheckable = true, StaysOpenOnClick = true, Padding = new Thickness(6, 7, 6, 7) };
            item.Click += (_, _) => Apply(enabled, style, controller.Settings.HeroTrailSeconds);
            choices.Add(item); menu.Items.Add(item);
        }
        var row = new Grid { Width = 218, Height = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(label); Grid.SetColumn(duration, 1); row.Children.Add(duration);
        menu.Items.Add(new MenuItem { Header = row, Tag = "trail-duration", Width = 234, StaysOpenOnClick = true, Padding = new Thickness(7, 3, 7, 3) });
        duration.ValueChanged += (_, _) => { if (!refreshing) Apply(controller.Settings.HeroTrailsEnabled, controller.Settings.HeroTrailStyle, (int)Math.Round(duration.Value)); };
        Sync();
    }
}
