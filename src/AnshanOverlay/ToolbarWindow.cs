using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace AnshanOverlay;

public sealed class ToolbarWindow : Window
{
    private readonly Controller controller;
    private readonly WrapPanel parameters = new();
    private readonly Dictionary<EditorTool, Button> tools = new();
    private readonly Dictionary<string, TextBlock> shortcutLabels = new();
    private readonly CheckBox names = new() { Content = "英雄名称", ToolTip = "统一显示或隐藏所有英雄名称，桌面与 OBS 同步" };
    private readonly CheckBox dashed = new() { Content = "虚线", ToolTip = "对接下来绘制的画笔、直线、箭头、椭圆、矩形生效" };
    private readonly Button mode;
    private readonly Button pin;
    private readonly Button visibility;
    private string shownState = "";
    private bool changingParameter;
    private bool adjusting;
    private readonly ScrollViewer toolContent;
    private readonly Button foldButton;
    private double expandedHeight;
    public bool IsFolded { get; private set; }
    public double ExpandedHeight => IsFolded ? expandedHeight : Height;
    public ToolbarWindow(Controller controller)
    {
        this.controller = controller;
        Title = "安山守望 · 工具栏"; Width = controller.Settings.ToolbarWidth; Height = controller.Settings.ToolbarHeight; MinWidth = 300; MinHeight = 150;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; ShowInTaskbar = false;
        Background = BoardView.Brush("#F4F6F9"); Foreground = BoardView.Brush("#27374A");
        FontFamily = new FontFamily("Microsoft YaHei UI"); UseLayoutRounding = true;
        Icon = Assets.AppIcon;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(8), GlassFrameThickness = new Thickness(0) });
        WindowGestures.EnableBlankDrag(this);
        var root = new StackPanel { Margin = new Thickness(10, 0, 10, 10) };
        var shell = new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); shell.RowDefinitions.Add(new RowDefinition());
        var windowActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 6, 10, 6) };
        Button WindowAction(string label, Action action)
        {
            var button = new Button { Content = label, FontSize = 11, Height = 24, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(4, 0, 0, 0) };
            button.Click += (_, _) => action(); windowActions.Children.Add(button); return button;
        }
        foldButton = WindowAction("折叠", () => SetFolded(!IsFolded));
        WindowAction("关闭", Hide).ToolTip = "隐藏工具栏；可从托盘或面板快捷键恢复";
        WindowAction("退出程序", controller.Close);
        shell.Children.Add(windowActions);
        toolContent = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(toolContent, 1); shell.Children.Add(toolContent);
        Content = new Border { Background = Background, Child = shell, BorderBrush = BoardView.Brush("#FF9C27"), BorderThickness = new Thickness(0, 3, 0, 0) };
        var row = new WrapPanel();
        var grip = new TextBlock { Text = "⠿", Width = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = BoardView.Brush("#67768A"), Cursor = Cursors.SizeAll, ToolTip = "空白处可拖动；Alt+拖动可从任意位置移动" };
        grip.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }; row.Children.Add(grip);
        var definitions = new[] {
            (EditorTool.Move,"移动","M 8,1 L 8,17 M 1,9 L 15,9 M 5,4 L 8,1 L 11,4 M 5,14 L 8,17 L 11,14 M 4,6 L 1,9 L 4,12 M 12,6 L 15,9 L 12,12"),
            (EditorTool.Brush,"画笔","M 3,12 L 12,2 L 16,6 L 7,15 Z M 3,12 C 4,19 1,18 0,18"),
            (EditorTool.Line,"直线","M 1,16 L 15,2"),
            (EditorTool.Arrow,"箭头","M 1,16 L 15,2 M 6,2 L 15,2 L 15,11"),
            (EditorTool.Circle,"椭圆","M 1,9 A 8,5 0 1 1 1,9.01"),
            (EditorTool.Rectangle,"矩形","M 1,3 L 17,3 L 17,15 L 1,15 Z"),
            (EditorTool.Eraser,"橡皮擦","M 1,11 L 10,2 L 17,9 L 9,17 L 6,17 Z M 5,7 L 12,14 M 7,17 L 18,17") };
        foreach (var (tool, label, geometry) in definitions)
        {
            var content = new StackPanel(); content.Children.Add(CreateIcon(geometry));
            content.Children.Add(new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
            content.Children.Add(ShortcutLabel(tool.ToString().ToLowerInvariant()));
            var button = new Button { Content = content, MinWidth = 42, Height = 63, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(0, 0, 3, 4), ToolTip = label };
            button.Click += (_, _) => controller.SetTool(tool);
            tools[tool] = button; row.Children.Add(button);
        }
        var clearContent = new StackPanel(); clearContent.Children.Add(CreateIcon("M 2,4 L 16,4 M 6,1 L 12,1 M 4,4 L 5,17 L 13,17 L 14,4 M 8,7 L 8,14 M 11,7 L 11,14"));
        clearContent.Children.Add(new TextBlock { Text = "清空", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
        clearContent.Children.Add(ShortcutLabel("clear"));
        var clear = new Button { Content = clearContent, MinWidth = 44, Height = 63, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(0, 0, 8, 4), ToolTip = "清空画布（可撤销）" };
        clear.Click += (_, _) => controller.Clear(false); row.Children.Add(clear);
        Button Utility(string text, string tip, Action action)
        {
            var b = new Button { Content = text, ToolTip = tip, Width = 25, Height = 32, FontSize = 15, Padding = new Thickness(0), Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => action(); row.Children.Add(b); return b;
        }
        Utility("↶", "撤销", controller.Undo); Utility("↷", "重做", controller.Redo);
        pin = Utility("⌖", "画布置顶", controller.ToggleTopmost);
        visibility = Utility("◉", "显示／隐藏标注", controller.ToggleVisible);
        mode = Utility("↗", "编辑／鼠标穿透", controller.TogglePassThrough);
        Utility("⚙", "自定义快捷键", controller.ShowShortcuts);
        root.Children.Add(row);
        var global = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(17, 7, 0, 6) };
        names.FontSize = dashed.FontSize = 11; global.Children.Add(names); global.Children.Add(dashed);
        names.Click += (_, _) => controller.SetHeroNames(names.IsChecked == true);
        dashed.Click += (_, _) => controller.SetDashed(dashed.IsChecked == true);
        root.Children.Add(global);
        root.Children.Add(new Border { BorderBrush = BoardView.Brush("#C5CFDB"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(17, 9, 0, 0), Child = parameters });
        controller.Changed += Refresh;
        Closing += (_, e) => { if (!controller.IsClosing) { e.Cancel = true; Hide(); } };
        PreviewKeyDown += (_, e) => { if (Keyboard.FocusedElement is not System.Windows.Controls.Primitives.RangeBase) controller.HandleEditorKey(e); };
        Refresh();
    }
    private static Path CreateIcon(string data) => new() { Data = Geometry.Parse(data), Stroke = BoardView.Brush("#27374A"), StrokeThickness = 1.5, Width = 17, Height = 17, Stretch = Stretch.Uniform };
    public void SetFolded(bool folded)
    {
        if (IsFolded == folded) return;
        if (folded) expandedHeight = Height;
        IsFolded = folded; toolContent.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        foldButton.Content = folded ? "展开" : "折叠";
        MinHeight = folded ? 44 : 150; Height = folded ? 44 : expandedHeight;
        ResizeMode = folded ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    }
    private TextBlock ShortcutLabel(string action)
    {
        var label = new TextBlock { FontSize = 10, Foreground = BoardView.Brush("#536477"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.NoWrap };
        shortcutLabels[action] = label; return label;
    }
    public void Refresh()
    {
        foreach (var (action, label) in shortcutLabels) label.Text = controller.BindingText(action);
        names.IsChecked = controller.Store.Current.ShowHeroNames; dashed.IsChecked = controller.Editor.Dashed;
        mode.Content = controller.PassThrough ? "↗" : "✎";
        mode.ToolTip = (controller.PassThrough ? "穿透中 · 点击进入编辑" : "编辑中 · 点击开启鼠标穿透") + " · " + controller.BindingText("edit");
        mode.Background = BoardView.Brush(controller.PassThrough ? "#E5EAF0" : "#FF9C27");
        pin.Background = BoardView.Brush(controller.AlwaysOnTop ? "#FFD18E" : "#E5EAF0");
        visibility.Opacity = controller.Store.Current.Visible ? 1 : .45;
        foreach (var (tool, button) in tools)
        {
            button.Background = BoardView.Brush(tool == controller.Editor.Tool ? "#FF9C27" : "#E5EAF0");
            button.ToolTip = controller.BindingText(tool.ToString().ToLowerInvariant());
        }
        var selected = controller.Editor.Selected;
        string state = controller.Editor.Tool + (controller.Editor.Tool == EditorTool.Move ? $"/{selected?.Id}/{selected?.Width}/{selected?.Opacity}/{selected?.Dashed}/{selected?.Size}" : "");
        if (shownState == state || changingParameter || adjusting) return;
        shownState = state; BuildParameters();
    }
    private void BuildParameters()
    {
        parameters.Children.Clear(); var editor = controller.Editor;
        if (editor.Tool == EditorTool.Move)
        {
            if (editor.Selected is not { } selected) { parameters.Children.Add(new TextBlock { Text = "点击选中标注，拖动移动；选中后可编辑样式。", FontSize = 11 }); return; }
            if (selected.Kind == "hero") Slider("尺寸", selected.Size, 24, 400, v => controller.UpdateSelected(i => i with { Size = v }), true);
            else
            {
                Slider("线宽", selected.Width, 1, 30, v => controller.UpdateSelected(i => i with { Width = v }), true);
                var lineStyle = new CheckBox { Content = "选中项虚线", IsChecked = selected.Dashed, FontSize = 11 };
                lineStyle.Click += (_, _) => Change(() => controller.UpdateSelected(i => i with { Dashed = lineStyle.IsChecked == true }));
                parameters.Children.Add(lineStyle);
            }
            Slider("不透明度", selected.Opacity * 100, 0, 100, v => controller.UpdateSelected(i => i with { Opacity = v / 100 }), true, "%");
        }
        else if (editor.Tool == EditorTool.Hero)
        {
            foreach (var (team, name, color) in new[] { ("ally", "友方", "#1482D4"), ("enemy", "敌方", "#D52E48") })
            {
                var b = new RadioButton { Content = name, GroupName = "NewHeroTeam", Foreground = BoardView.Brush(color), IsChecked = editor.Team == team, FontSize = 11, Margin = new Thickness(0, 2, 12, 0) };
                b.Checked += (_, _) => editor.Team = team; parameters.Children.Add(b);
            }
            Slider("尺寸", editor.Size, 40, 180, v => editor.Size = v);
        }
        else if (editor.Tool == EditorTool.Eraser)
        {
            Slider("范围", editor.EraserSize, 8, 80, v => editor.EraserSize = v);
            var whole = new CheckBox { Content = "整项擦除", IsChecked = editor.EraseWhole, ToolTip = "不勾选时仅擦掉经过的局部", FontSize = 11, Margin = new Thickness(12, 2, 0, 0) };
            whole.Click += (_, _) => editor.EraseWhole = whole.IsChecked == true; parameters.Children.Add(whole);
        }
        else
        {
            var profile = editor.Options;
            foreach (var color in new[] { "#FFD166", "#39A9FF", "#F05265", "#FFFFFF", "#65D6B0", "#C69AFF" })
            {
                var button = new Button { Width = 21, Height = 21, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), Background = BoardView.Brush(color), ToolTip = color,
                    BorderBrush = BoardView.Brush(profile.Color == color ? "#27374A" : "#C5CFDB"), BorderThickness = new Thickness(2) };
                button.Click += (_, _) => { profile.Color = color; foreach (Button peer in parameters.Children.OfType<Button>()) peer.BorderBrush = BoardView.Brush(ReferenceEquals(peer, button) ? "#27374A" : "#C5CFDB"); }; parameters.Children.Add(button);
            }
            Slider("线宽", profile.Width, 2, 16, v => profile.Width = v);
        }
    }
    private void Change(Action action)
    {
        changingParameter = true;
        try { action(); } finally { changingParameter = false; }
    }
    private void Slider(string name, double value, double min, double max, Action<double> update, bool selection = false, string unit = "px")
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 8) };
        var text = new TextBlock { Text = $"{name} {value:0} {unit}", Width = 105, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        group.Children.Add(text); var slider = new Slider { Width = 110, Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { text.Text = $"{name} {e.NewValue:0} {unit}"; Change(() => update(e.NewValue)); };
        if (selection)
        {
            slider.PreviewMouseLeftButtonDown += (_, _) => { adjusting = true; controller.BeginPropertyEdit(); };
            slider.PreviewMouseLeftButtonUp += (_, _) => { adjusting = false; controller.EndPropertyEdit(); };
            slider.LostMouseCapture += (_, _) => { adjusting = false; controller.EndPropertyEdit(); };
        }
        group.Children.Add(slider); parameters.Children.Add(group);
    }
}
