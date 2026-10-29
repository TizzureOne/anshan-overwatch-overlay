using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnshanOverlay;

internal sealed class InlineTextEditor : Canvas
{
    private readonly Controller controller;
    private readonly StackPanel panel = new();
    private readonly Border frame;
    private SceneItem? original;
    private SceneItem? draft;
    private EditorTool returnTool;
    private bool syncing;
    internal TextBox Input { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = Annotation.MaxLength,
        Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(1), BorderBrush = BoardView.Brush("#55C6E8"),
        Background = BoardView.Brush("#E815202C"), CaretBrush = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    internal bool IsEditing => draft is not null;
    internal string? ItemId => draft?.Id;

    internal InlineTextEditor(Controller controller)
    {
        this.controller = controller; Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(Input, "在战术板输入文字");
        panel.Children.Add(Input);
        var actions = new DockPanel { Background = BoardView.Brush("#F015202C"), LastChildFill = true };
        void Action(string text, string hint, Action action)
        {
            var button = new Button { Content = text, ToolTip = hint, FontSize = 12, MinHeight = 24, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(1), Focusable = false };
            button.Click += (_, e) => { action(); e.Handled = true; }; DockPanel.SetDock(button, Dock.Right); actions.Children.Add(button);
        }
        Action("×", "放弃本次修改 · Esc", Cancel);
        Action("✓", "完成文字 · Ctrl+Enter", () => Commit());
        actions.Children.Add(new TextBlock { Text = "Enter 换行 · 点击空白完成", FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(actions);
        frame = new Border { Child = panel, CornerRadius = new CornerRadius(4) }; Children.Add(frame);
        Input.TextChanged += (_, _) => { if (!syncing) Reflow(); };
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Enter && Annotation.Normalize(Input.Text).Count(c => c == '\n') >= Annotation.MaxLines - 1)
            { controller.SetStatus($"每段文字最多 {Annotation.MaxLines} 行。"); e.Handled = true; }
        };
        DataObject.AddPastingHandler(Input, (_, e) =>
        {
            if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted) return;
            string combined = Input.Text.Remove(Input.SelectionStart, Input.SelectionLength).Insert(Input.SelectionStart, pasted);
            if (!string.IsNullOrWhiteSpace(combined) && !Annotation.ValidText(Annotation.Normalize(combined)))
            { e.CancelCommand(); controller.SetStatus($"每段文字最多 {Annotation.MaxLength} 字、{Annotation.MaxLines} 行。"); }
        });
    }
    internal void Begin(Point point, SceneItem? item = null)
    {
        if (item is not null && item.Kind != "text") return;
        if (!Commit()) return;
        if (item is null && controller.Store.Current.Items.Count >= SceneStore.MaxItems) { controller.ReportItemLimit(); return; }
        controller.Editor.Cancel(); controller.Overlay.Board.ReleaseMouseCapture();
        var options = controller.Editor.Settings[EditorTool.Text];
        if (item is not null) { options.Color = item.Color; options.FontSize = item.FontSize; }
        returnTool = controller.Editor.Tool; controller.SetTool(EditorTool.Text);
        original = item;
        var scene = controller.Store.Current;
        double x = Math.Clamp(point.X, 0, Math.Max(0, scene.Width - 240));
        double width = Math.Min(Math.Max(480, item is null ? 0 : Annotation.Bounds(item).Width), scene.Width - x);
        draft = item ?? new SceneItem { Kind = "text", X = x, Y = point.Y, Color = options.Color, FontSize = options.FontSize };
        draft = draft with { TextWidth = item?.TextWidth > 0 ? item.TextWidth : width - 20 };
        controller.Editor.SelectedId = item?.Id;
        syncing = true; Input.IsUndoEnabled = false; Input.Text = item?.Text ?? ""; Input.IsUndoEnabled = true; syncing = false;
        Visibility = Visibility.Visible; controller.Overlay.Board.EditingTextId = item?.Id;
        UpdateStyle(); controller.Overlay.Board.InvalidateVisual(); controller.Notify();
        if (controller.Overlay.IsVisible)
        {
            controller.Overlay.Activate(); Input.Focus(); Input.CaretIndex = Input.Text.Length;
            Dispatcher.BeginInvoke(() => { if (IsEditing && IsVisible) Input.Focus(); }, DispatcherPriority.Input);
        }
    }
    internal void UpdateStyle()
    {
        if (draft is null) return;
        var options = controller.Editor.Settings[EditorTool.Text];
        draft = draft with { FontSize = options.FontSize, Color = options.Color };
        Input.FontSize = draft.FontSize; Input.Foreground = BoardView.Brush(draft.Color); Reflow();
    }
    private void Reflow()
    {
        if (draft is null) return;
        var scene = controller.Store.Current;
        var layout = Annotation.Metrics(draft with { Text = Annotation.Normalize(Input.Text) });
        double width = Math.Min(scene.Width, Math.Max(240, Math.Min(draft.TextWidth + 20, layout.Width + 4)));
        Input.Width = width; Input.MaxHeight = Math.Max(40, scene.Height - 40);
        Input.Height = Math.Min(Input.MaxHeight, Math.Max(48, layout.Height + 8));
        frame.Measure(new Size(width, scene.Height));
        SetLeft(frame, Math.Clamp(draft.X, 0, Math.Max(0, scene.Width - width)));
        SetTop(frame, Math.Clamp(draft.Y, 0, Math.Max(0, scene.Height - frame.DesiredSize.Height)));
    }
    internal bool Commit()
    {
        if (draft is null) return true;
        string text = Annotation.Normalize(Input.Text);
        bool empty = string.IsNullOrWhiteSpace(text);
        if (!empty && !Annotation.ValidText(text)) { controller.SetStatus($"每段文字最多 {Annotation.MaxLength} 字、{Annotation.MaxLines} 行。"); return false; }
        if (!empty && original is null && controller.Store.Current.Items.Count >= SceneStore.MaxItems) { controller.ReportItemLimit(); return false; }
        if (original is not null && text == original.Text && draft.Color == original.Color && draft.FontSize == original.FontSize) { End(true); return true; }
        var saved = Annotation.Fit(draft with { Text = text, X = original?.X ?? GetLeft(frame), Y = original?.Y ?? GetTop(frame) }, controller.Store.Current);
        if (!empty && !new Rect(0, 0, controller.Store.Current.Width, controller.Store.Current.Height).Contains(Annotation.Bounds(saved)))
        { controller.SetStatus("文字超出战术板，请减少内容或字号。"); return false; }
        bool replace = original is not null; string id = draft.Id;
        End(true);
        if (empty) { if (replace) controller.Store.Edit(s => s with { Items = s.Items.Where(i => i.Id != id).ToList() }); }
        else controller.Store.Edit(s => s with { Items = replace ? s.Items.Select(i => i.Id == id ? saved : i).ToList() : s.Items.Append(saved).ToList() });
        controller.Editor.SelectedId = empty ? null : id; controller.Overlay.Refresh(); controller.Notify(); return true;
    }
    internal void Cancel() { if (draft is not null) End(false); }
    private void End(bool completed)
    {
        controller.Editor.Tool = completed ? EditorTool.Move : returnTool;
        draft = original = null; Visibility = Visibility.Collapsed;
        controller.Overlay.Board.EditingTextId = null; controller.Overlay.Board.InvalidateVisual();
        syncing = true; Input.Clear(); syncing = false;
        controller.Notify();
    }
}
