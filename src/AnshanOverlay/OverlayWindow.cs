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
            { controller.PlaceHero(hero, e.GetPosition(Board)); e.Effects = DragDropEffects.Copy; }
            e.Handled = true;
        };
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = Board };
        Board.MouseLeftButtonDown += (_, e) =>
        {
            if (controller.PassThrough) return;
            Board.Focus(); controller.Editor.Down(e.GetPosition(Board), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            if (controller.Editor.IsDragging) Board.CaptureMouse();
            Refresh(); controller.Notify(); e.Handled = true;
        };
        Board.MouseMove += (_, e) =>
        {
            Board.EraserPosition = !controller.PassThrough && controller.Editor.Tool == EditorTool.Eraser ? e.GetPosition(Board) : null;
            Board.EraserRadius = controller.Editor.EraserSize / 2;
            controller.Editor.Move(e.GetPosition(Board), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            Refresh();
            if (controller.Editor.SelectionBox is not null) controller.Notify();
        };
        Board.MouseLeave += (_, _) => { Board.EraserPosition = null; Board.InvalidateVisual(); };
        Board.MouseLeftButtonUp += (_, e) => { controller.Editor.Up(); Board.ReleaseMouseCapture(); Refresh(); controller.Notify(); e.Handled = true; };
        Board.LostMouseCapture += (_, _) => { controller.Editor.Cancel(); Refresh(); };
        Board.MouseRightButtonDown += (_, e) =>
        {
            controller.Editor.Cancel(); Board.ReleaseMouseCapture();
            var point = e.GetPosition(Board);
            var hit = controller.Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => Editor.Hit(i, point));
            if (hit is { Kind: "hero" })
            {
                if (heroMenu is not null) heroMenu.IsOpen = false;
                heroMenu = CreateHeroMenu(hit); heroMenu.PlacementTarget = Board; heroMenu.Placement = PlacementMode.MousePoint; heroMenu.IsOpen = true;
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
        Board.Scene = controller.Store.Current; Board.Width = Board.Scene.Width; Board.Height = Board.Scene.Height;
        Board.SelectedIds = controller.Editor.SelectedIds; Board.SelectionBox = controller.Editor.SelectionBox; Board.Editing = !controller.PassThrough;
        Board.ModeHint = controller.ModeState + " · " + controller.ModeShortcut;
        Board.PassThroughOpacity = controller.Settings.PassThroughOpacityPercent / 100d;
        Board.DraggedHeroId = controller.Editor.IsMoving && controller.Editor.Selected?.Kind == "hero" ? controller.Editor.SelectedId : null;
        if (controller.Editor.Tool != EditorTool.Eraser || controller.PassThrough) Board.EraserPosition = null;
        Board.Cursor = controller.Editor.Tool == EditorTool.Move ? Cursors.Arrow : controller.Editor.Tool == EditorTool.Eraser ? Cursors.Cross : Cursors.Pen;
        Board.InvalidateVisual();
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
}
