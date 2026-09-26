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
            Board.Focus(); controller.Editor.Down(e.GetPosition(Board));
            if (controller.Editor.IsDragging) Board.CaptureMouse();
            Refresh(); controller.Notify(); e.Handled = true;
        };
        Board.MouseMove += (_, e) =>
        {
            Board.EraserPosition = !controller.PassThrough && controller.Editor.Tool == EditorTool.Eraser ? e.GetPosition(Board) : null;
            Board.EraserRadius = controller.Editor.EraserSize / 2;
            controller.Editor.Move(e.GetPosition(Board), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            Board.InvalidateVisual();
        };
        Board.MouseLeave += (_, _) => { Board.EraserPosition = null; Board.InvalidateVisual(); };
        Board.MouseLeftButtonUp += (_, e) => { controller.Editor.Up(); Board.ReleaseMouseCapture(); controller.Notify(); e.Handled = true; };
        Board.LostMouseCapture += (_, _) => { controller.Editor.Cancel(); Refresh(); };
        Board.MouseRightButtonDown += (_, e) =>
        {
            controller.Editor.Cancel(); Board.ReleaseMouseCapture();
            var point = e.GetPosition(Board);
            var hit = controller.Store.Current.Items.AsEnumerable().Reverse().FirstOrDefault(i => Editor.Hit(i, point));
            if (hit is { Kind: "hero" })
            {
                controller.Editor.SelectedId = hit.Id; Refresh(); controller.Notify();
                var menu = CreateHeroMenu(hit); menu.PlacementTarget = Board; menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true;
            }
            else { controller.Editor.PendingHero = null; controller.SetTool(EditorTool.Move); }
            e.Handled = true;
        };
        Board.MouseWheel += (_, e) => { controller.Editor.ResizeSelected(Math.Sign(e.Delta)); e.Handled = true; };
        PreviewKeyDown += (_, e) => controller.HandleEditorKey(e);
        SourceInitialized += (_, _) => Native.SetPassThrough(new WindowInteropHelper(this).Handle, controller.PassThrough);
        Activated += (_, _) => controller.RaisePanel();
        Closed += (_, _) => { if (!controller.IsClosing) controller.Close(); };
    }
    public void Refresh()
    {
        Board.Scene = controller.Store.Current; Board.Width = Board.Scene.Width; Board.Height = Board.Scene.Height;
        Board.Selection = controller.Editor.SelectedId; Board.Editing = !controller.PassThrough;
        Board.DraggedHeroId = controller.Editor.IsDragging && controller.Editor.Selected?.Kind == "hero" ? controller.Editor.SelectedId : null;
        if (controller.Editor.Tool != EditorTool.Eraser || controller.PassThrough) Board.EraserPosition = null;
        Board.Cursor = controller.Editor.Tool is EditorTool.Hero or EditorTool.Move ? Cursors.Arrow : controller.Editor.Tool == EditorTool.Eraser ? Cursors.Cross : Cursors.Pen;
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
        Native.SetPassThrough(new WindowInteropHelper(this).Handle, enabled);
        Refresh();
    }
    public ContextMenu CreateHeroMenu(SceneItem hero)
    {
        var menu = new ContextMenu { Background = BoardView.Brush("#F4F6F9"), Foreground = BoardView.Brush("#27374A"), BorderBrush = BoardView.Brush("#C5CFDB"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Microsoft YaHei UI") };
        menu.Items.Add(new MenuItem { Header = controller.Heroes.Heroes.First(h => h.Id == hero.HeroId).Name, IsEnabled = false });
        menu.Items.Add(new Separator());
        var team = new MenuItem { Header = "阵营" };
        foreach (var (id, name, color) in new[] { ("ally", "友方 · 蓝色", "#39A9FF"), ("enemy", "敌方 · 红色", "#F05265") })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = hero.Team == id, Foreground = BoardView.Brush(color) };
            item.Click += (_, _) => controller.UpdateHero(hero.Id, h => h with { Team = id }); team.Items.Add(item);
        }
        menu.Items.Add(team);
        var sizes = new MenuItem { Header = $"尺寸 · {hero.Size:0} px" };
        foreach (double size in new double[] { 40, 60, 80, 100, 120, 160 })
        {
            var item = new MenuItem { Header = $"{size:0} px", IsCheckable = true, IsChecked = Math.Abs(hero.Size - size) < 0.1 };
            item.Click += (_, _) => controller.UpdateHero(hero.Id, h => h with { Size = size }); sizes.Items.Add(item);
        }
        menu.Items.Add(sizes);
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "删除", Foreground = BoardView.Brush("#F05265") };
        delete.Click += (_, _) => controller.Store.Remove(hero.Id); menu.Items.Add(delete);
        return menu;
    }
}
