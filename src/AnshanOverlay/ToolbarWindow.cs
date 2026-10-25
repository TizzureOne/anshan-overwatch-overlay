using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace AnshanOverlay;

public sealed class ToolbarWindow : Window
{
    private readonly Controller controller;
    private readonly WrapPanel parameters = new();
    private readonly Grid toolStrip = new();
    private readonly Dictionary<EditorTool, Button> tools = new();
    private readonly CheckBox names = new() { Content = "英雄名称", FontSize = 12, ToolTip = "统一显示或隐藏所有英雄名称，桌面与 OBS 同步" };
    private readonly CheckBox dashed = new() { Content = "虚线", FontSize = 12, ToolTip = "对接下来绘制的线条和图形生效" };
    private readonly Button mode;
    private readonly Button undo;
    private readonly Button redo;
    private readonly Button shape;
    private readonly Button more;
    private readonly TextBlock modeLabel = new() { FontSize = 12 };
    private readonly TextBlock hint = new() { FontSize = 11, Foreground = BoardView.Brush("#67768A"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock count = new() { FontSize = 11, Foreground = BoardView.Brush("#67768A"), TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(12, 0, 0, 0) };
    public TextBlock ModeStateText { get; } = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    private readonly Grid toolContent;
    private readonly Border footer;
    private readonly Button foldButton;
    private EditorTool lastShape = EditorTool.Arrow;
    private string shownState = "";
    private bool compact;
    private bool changingParameter;
    private bool adjusting;
    private double expandedHeight;
    public bool IsFolded { get; private set; }
    public double ExpandedHeight => IsFolded ? expandedHeight : Motion.Height(this);
    private static readonly (EditorTool Tool, string Label, string Geometry)[] Definitions = {
        (EditorTool.Move,"移动","M 8,1 L 8,17 M 1,9 L 15,9 M 5,4 L 8,1 L 11,4 M 5,14 L 8,17 L 11,14 M 4,6 L 1,9 L 4,12 M 12,6 L 15,9 L 12,12"),
        (EditorTool.Brush,"画笔","M 3,12 L 12,2 L 16,6 L 7,15 Z M 3,12 C 4,19 1,18 0,18"),
        (EditorTool.Line,"直线","M 1,16 L 15,2"),
        (EditorTool.Arrow,"箭头","M 1,16 L 15,2 M 6,2 L 15,2 L 15,11"),
        (EditorTool.Circle,"椭圆","M 1,9 A 8,5 0 1 1 1,9.01"),
        (EditorTool.Rectangle,"矩形","M 1,3 L 17,3 L 17,15 L 1,15 Z"),
        (EditorTool.Eraser,"橡皮","M 1,11 L 10,2 L 17,9 L 9,17 L 6,17 Z M 5,7 L 12,14 M 7,17 L 18,17") };

    public ToolbarWindow(Controller controller)
    {
        this.controller = controller;
        Title = "安山守望 · 工具栏"; Width = controller.Settings.ToolbarWidth; Height = controller.Settings.ToolbarHeight; MinWidth = 300; MinHeight = 150;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; ShowInTaskbar = false;
        WindowLayout.Restore(this, controller.Settings.ToolbarPosition);
        Background = Brushes.White; Foreground = BoardView.Brush("#27374A"); FontSize = 13;
        FontFamily = new FontFamily("Microsoft YaHei UI"); UseLayoutRounding = true; Icon = Assets.AppIcon;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(8), GlassFrameThickness = new Thickness(0) });
        WindowGestures.EnableBlankDrag(this);
        var shell = new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); shell.RowDefinitions.Add(new RowDefinition()); shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(8, 0, 6, 0) };
        foldButton = ActionButton(CreateIcon("M 2,11 L 8,5 L 14,11"), "折叠工具栏", () => SetFolded(!IsFolded)); foldButton.Width = 28;
        DockPanel.SetDock(foldButton, Dock.Right); header.Children.Add(foldButton);
        mode = ActionButton(modeLabel, "切换画布编辑与鼠标穿透", controller.TogglePassThrough); mode.Margin = new Thickness(4, 0, 2, 0); mode.BorderBrush = BoardView.Brush("#D2D5D9");
        DockPanel.SetDock(mode, Dock.Right); header.Children.Add(mode);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "⠿", FontSize = 14, Margin = new Thickness(0, 0, 6, 0), Foreground = BoardView.Brush("#67768A"), Cursor = Cursors.SizeAll });
        title.Children.Add(new TextBlock { Text = "标注工具", FontSize = 12, Foreground = BoardView.Brush("#67768A"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(ModeStateText); header.Children.Add(title);
        shell.Children.Add(new Border { Background = BoardView.Brush("#F7F7F8"), BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(0, 0, 0, 1), Child = header });
        toolContent = new Grid(); toolContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); toolContent.RowDefinitions.Add(new RowDefinition());
        var commandRow = new Grid { Margin = new Thickness(8, 7, 8, 5) }; commandRow.ColumnDefinitions.Add(new ColumnDefinition()); commandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var (tool, label, geometry) in Definitions)
        {
            var button = ActionButton(ToolFace(label, geometry), label, () => controller.SetTool(tool));
            button.Height = 52; button.Padding = new Thickness(4, 4, 4, 4); button.Tag = tool;
            tools[tool] = button;
        }
        shape = ActionButton(ToolFace("箭头 ▾", Definitions.Single(d => d.Tool == lastShape).Geometry), "选择形状", () => OpenMenu(CreateShapeMenu(), shape!));
        shape.Height = 52; shape.Padding = new Thickness(3); shape.Name = "ShapeButton";
        commandRow.Children.Add(toolStrip);
        var utilities = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        undo = ActionButton(CreateIcon("M 6,4 L 1,9 L 6,14 M 1,9 L 10,9 C 19,9 19,18 10,18"), "撤销", controller.Undo);
        redo = ActionButton(CreateIcon("M 12,4 L 17,9 L 12,14 M 17,9 L 8,9 C -1,9 -1,18 8,18"), "重做", controller.Redo);
        more = ActionButton(CreateIcon("M 2,9 L 3,9 M 8,9 L 9,9 M 14,9 L 15,9"), "更多操作", () => OpenMenu(CreateMoreMenu(), more!));
        foreach (var button in new[] { undo, redo, more }) { button.Width = 28; button.Padding = new Thickness(5); utilities.Children.Add(button); }
        var utilityBorder = new Border { BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(5, 6, 0, 6), Padding = new Thickness(6, 0, 0, 0), Child = utilities };
        Grid.SetColumn(utilityBorder, 1); commandRow.Children.Add(utilityBorder); toolContent.Children.Add(commandRow);
        names.Click += (_, _) => controller.SetHeroNames(names.IsChecked == true);
        dashed.Click += (_, _) => controller.SetDashed(dashed.IsChecked == true);
        var parameterScroll = new ScrollViewer { Content = parameters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 5, 8, 0) };
        var parameterBorder = new Border { BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(0, 1, 0, 0), Child = parameterScroll };
        Grid.SetRow(parameterBorder, 1); toolContent.Children.Add(parameterBorder); Grid.SetRow(toolContent, 1); shell.Children.Add(toolContent);
        var footerContent = new DockPanel(); DockPanel.SetDock(count, Dock.Right); footerContent.Children.Add(count); footerContent.Children.Add(hint);
        footer = new Border { Background = BoardView.Brush("#F7F7F8"), BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 5, 10, 5), Child = footerContent };
        Grid.SetRow(footer, 2); shell.Children.Add(footer);
        Content = new Border { Background = Background, Child = shell, BorderBrush = BoardView.Brush("#CDD0D4"), BorderThickness = new Thickness(1) };
        shell.SizeChanged += (_, e) => ConfigureLayout(e.NewSize.Width);
        Loaded += (_, _) => Motion.Reveal(toolContent);
        IsVisibleChanged += (_, _) => { if (IsVisible) Motion.Reveal((FrameworkElement)Content, offset: 0); };
        controller.Changed += Refresh;
        Closing += (_, e) => { if (!controller.IsClosing) { e.Cancel = true; Hide(); } };
        PreviewKeyDown += (_, e) => { if (Keyboard.FocusedElement is not RangeBase) controller.HandleEditorKey(e); };
        ConfigureLayout(Width); Refresh();
    }
    private static Path CreateIcon(string data) => new() { Data = Geometry.Parse(data), Stroke = BoardView.Brush("#42484F"), StrokeThickness = 1.4, Width = 16, Height = 16, Stretch = Stretch.Uniform };
    private static StackPanel ToolFace(string label, string geometry)
    {
        var content = new StackPanel(); content.Children.Add(CreateIcon(geometry));
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.NoWrap }); return content;
    }
    private Button ActionButton(object content, string label, Action action)
    {
        var button = new Button { Content = content, Style = (Style)FindResource("QuietButton"), ToolTip = label, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action(); return button;
    }
    private void ConfigureLayout(double width)
    {
        bool narrow = width < 540;
        if (toolStrip.Children.Count > 0 && compact == narrow) return;
        compact = narrow; toolStrip.Children.Clear(); toolStrip.ColumnDefinitions.Clear();
        var visibleTools = compact ? new[] { tools[EditorTool.Move], tools[EditorTool.Brush], shape, tools[EditorTool.Eraser] } : Definitions.Select(d => tools[d.Tool]).ToArray();
        foreach (var button in visibleTools)
        { Grid.SetColumn(button, toolStrip.ColumnDefinitions.Count); toolStrip.ColumnDefinitions.Add(new ColumnDefinition()); toolStrip.Children.Add(button); }
        count.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        UpdateMode(); BuildParameters();
    }
    private void UpdateMode()
    {
        modeLabel.Text = (controller.PassThrough ? "开始标注" : "返回游戏") + (compact ? "" : "  " + controller.BindingText("edit"));
        Motion.Text(ModeStateText, controller.PassThrough ? "● 穿透中" : "● 正在标注");
        ModeStateText.Foreground = BoardView.Brush(controller.PassThrough ? "#1482D4" : "#A55B00");
        mode.ToolTip = ModeStateText.ToolTip = controller.ModeState + " · " + controller.ModeShortcut;
    }
    internal ContextMenu CreateShapeMenu()
    {
        var menu = new ContextMenu();
        foreach (var (tool, label, geometry) in Definitions.Where(d => IsShape(d.Tool)))
        {
            var item = new MenuItem { Header = label + "    " + controller.BindingText(tool.ToString().ToLowerInvariant()), Tag = tool, IsCheckable = true, IsChecked = controller.Editor.Tool == tool };
            item.Click += (_, _) => controller.SetTool(tool); menu.Items.Add(item);
        }
        return menu;
    }
    internal ContextMenu CreateMoreMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool? selected = null)
        {
            var item = new MenuItem { Header = label, IsCheckable = selected.HasValue, IsChecked = selected == true };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("标注可见", controller.ToggleVisible, controller.Store.Current.Visible);
        Add("画布置顶", controller.ToggleTopmost, controller.AlwaysOnTop);
        Add("英雄名称", () => controller.SetHeroNames(!controller.Store.Current.ShowHeroNames), controller.Store.Current.ShowHeroNames);
        menu.Items.Add(new Separator());
        Add("清除标注", () => controller.Clear(true));
        Add("清空全部    " + controller.BindingText("clear"), () => controller.Clear(false));
        menu.Items.Add(new Separator());
        Add("自定义快捷键…", controller.ShowShortcuts); Add("隐藏工具栏", Hide); Add("退出程序", controller.Close);
        return menu;
    }
    private static void OpenMenu(ContextMenu menu, Button target) { menu.PlacementTarget = target; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; }
    private static bool IsShape(EditorTool tool) => tool is EditorTool.Line or EditorTool.Arrow or EditorTool.Circle or EditorTool.Rectangle;
    public void SetFolded(bool folded)
    {
        if (IsFolded == folded) return;
        if (folded) expandedHeight = Motion.Height(this);
        IsFolded = folded; toolContent.Visibility = footer.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        foldButton.Content = CreateIcon(folded ? "M 2,5 L 8,11 L 14,5" : "M 2,11 L 8,5 L 14,11");
        foldButton.ToolTip = folded ? "展开工具栏" : "折叠工具栏";
        Motion.Resize(this, folded ? 44 : expandedHeight, folded ? 44 : 150);
        ResizeMode = folded ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        if (!folded) Motion.Reveal(toolContent);
    }
    public void Refresh()
    {
        UpdateMode();
        names.IsChecked = controller.Store.Current.ShowHeroNames; dashed.IsChecked = controller.Editor.Dashed;
        undo.IsEnabled = controller.Store.CanUndo; redo.IsEnabled = controller.Store.CanRedo;
        undo.ToolTip = "撤销 · " + controller.BindingText("undo"); redo.ToolTip = "重做 · " + controller.BindingText("redo");
        var current = controller.Editor.Tool;
        if (IsShape(current)) lastShape = current;
        var shapeInfo = Definitions.Single(d => d.Tool == lastShape);
        if (shape.Tag is not EditorTool shownShape || shownShape != lastShape)
        { shape.Content = ToolFace(shapeInfo.Label + " ▾", shapeInfo.Geometry); shape.Tag = lastShape; }
        shape.ToolTip = shapeInfo.Label + " · " + controller.BindingText(lastShape.ToString().ToLowerInvariant()) + " · 点击选择形状";
        Motion.Paint(shape, BackgroundProperty, IsShape(current) ? "#FFF0DF" : "#00FFFFFF");
        foreach (var (tool, button) in tools)
        {
            Motion.Paint(button, BackgroundProperty, tool == current ? "#FFF0DF" : "#00FFFFFF");
            button.BorderBrush = BoardView.Brush(tool == current ? "#E6B77D" : "#00FFFFFF");
            button.ToolTip = Definitions.Single(d => d.Tool == tool).Label + " · " + controller.BindingText(tool.ToString().ToLowerInvariant());
        }
        string action = current switch { EditorTool.Move => "拖动移动 · 空白处框选", EditorTool.Brush => "拖动画笔 · 连续绘制", EditorTool.Line => "直线 · Shift 约束方向", EditorTool.Arrow => "箭头 · 从起点拖到终点", EditorTool.Circle => "椭圆 · Shift 绘制正圆", EditorTool.Rectangle => "矩形 · Shift 绘制正方形", _ => "橡皮 · 拖动擦除" };
        hint.Text = action; hint.ToolTip = action + " · " + controller.BindingText(current.ToString().ToLowerInvariant());
        count.Text = $"{controller.Store.Current.Items.Count(i => i.Kind == "hero")} 个英雄 · {controller.Store.Current.Items.Count(i => i.Kind != "hero")} 个标注";
        var selected = controller.Editor.Selected;
        string state = current + (current == EditorTool.Move ? $"/{controller.Editor.SelectedIds.Count}/{selected?.Id}/{selected?.Width}/{selected?.Opacity}/{selected?.Dashed}/{selected?.Size}" : "");
        if (shownState == state || changingParameter || adjusting) return;
        shownState = state; BuildParameters(); Motion.Reveal(parameters);
    }
    private void BuildParameters()
    {
        // Release these shared controls from the previous drawing row before reusing them.
        if (names.Parent is Panel nameParent) nameParent.Children.Remove(names);
        if (dashed.Parent is Panel dashParent) dashParent.Children.Remove(dashed);
        parameters.Children.Clear(); var editor = controller.Editor;
        if (editor.Tool == EditorTool.Move)
        {
            if (editor.Selected is not { } selected)
            {
                parameters.Children.Add(new TextBlock { Text = editor.SelectedIds.Count > 1
                    ? $"已选 {editor.SelectedIds.Count} 项 · 拖动整体移动\nShift+点击加选/取消 · {controller.BindingText("delete")} 删除 · {controller.BindingText("duplicate")} 复制"
                    : "点击选择头像或标注；从空白处拖动框选。\nShift+点击加选或取消。", FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 245, Margin = new Thickness(0, 4, 0, 4) });
                return;
            }
            if (selected.Kind == "hero") Slider("头像尺寸", selected.Size, 24, 400, v => controller.UpdateSelected(i => i with { Size = v }), true);
            else
            {
                Slider("粗细", selected.Width, 1, 30, v => controller.UpdateSelected(i => i with { Width = v }), true);
                var lineStyle = new CheckBox { Content = "虚线", IsChecked = selected.Dashed, FontSize = 12 };
                lineStyle.Click += (_, _) => Change(() => controller.UpdateSelected(i => i with { Dashed = lineStyle.IsChecked == true })); parameters.Children.Add(lineStyle);
            }
            Slider("不透明度", selected.Opacity * 100, 0, 100, v => controller.UpdateSelected(i => i with { Opacity = v / 100 }), true, "%");
        }
        else if (editor.Tool == EditorTool.Eraser)
        {
            Slider("范围", editor.EraserSize, 8, 80, v => editor.EraserSize = v);
            var whole = new CheckBox { Content = "整项擦除", IsChecked = editor.EraseWhole, ToolTip = "不勾选时仅擦掉经过的局部", FontSize = 12 };
            whole.Click += (_, _) => editor.EraseWhole = whole.IsChecked == true; parameters.Children.Add(whole);
        }
        else
        {
            var profile = editor.Options;
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4), Height = 28 };
            colors.Children.Add(new TextBlock { Text = "颜色", FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var (color, label) in new[] { ("#FFD166", "黄色"), ("#39A9FF", "蓝色"), ("#F05265", "红色"), ("#FFFFFF", "白色"), ("#65D6B0", "绿色"), ("#C69AFF", "紫色") })
            {
                var swatch = new Ellipse { Fill = BoardView.Brush(color), Stroke = BoardView.Brush("#C5C9CE"), StrokeThickness = 1, Width = 19, Height = 19 };
                var button = new Button { Content = swatch, Style = (Style)FindResource("QuietButton"), Width = 26, Height = 26, MinHeight = 26, Padding = new Thickness(2), Margin = new Thickness(0, 0, 1, 0), ToolTip = label,
                    BorderBrush = BoardView.Brush(profile.Color == color ? "#42484F" : "#00FFFFFF"), Tag = color };
                System.Windows.Automation.AutomationProperties.SetName(button, label);
                button.Click += (_, _) => { profile.Color = color; foreach (Button peer in colors.Children.OfType<Button>()) Motion.Paint(peer, BorderBrushProperty, ReferenceEquals(peer, button) ? "#42484F" : "#00FFFFFF"); }; colors.Children.Add(button);
            }
            parameters.Children.Add(colors);
            Slider("粗细", profile.Width, 2, 16, v => profile.Width = v);
            var toggles = new StackPanel { Orientation = Orientation.Horizontal, Height = 28 }; toggles.Children.Add(dashed); toggles.Children.Add(names); parameters.Children.Add(toggles);
        }
    }
    private void Change(Action action) { changingParameter = true; try { action(); } finally { changingParameter = false; } }
    private void Slider(string name, double value, double min, double max, Action<double> update, bool selection = false, string unit = "px")
    {
        var group = new Grid { Width = compact ? 248 : 208, Height = 28, Margin = new Thickness(0, 0, 10, 4) };
        group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(name.Length > 2 ? 60 : 34) }); group.ColumnDefinitions.Add(new ColumnDefinition()); group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        group.Children.Add(new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var valueText = new TextBlock { Text = $"{value:0} {unit}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        Grid.SetColumn(valueText, 2); group.Children.Add(valueText);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        System.Windows.Automation.AutomationProperties.SetName(slider, name);
        slider.ValueChanged += (_, e) => { valueText.Text = $"{e.NewValue:0} {unit}"; Change(() => update(e.NewValue)); };
        if (selection)
        {
            slider.PreviewMouseLeftButtonDown += (_, _) => { adjusting = true; controller.BeginPropertyEdit(); };
            slider.PreviewMouseLeftButtonUp += (_, _) => { adjusting = false; controller.EndPropertyEdit(); };
            slider.LostMouseCapture += (_, _) => { adjusting = false; controller.EndPropertyEdit(); };
        }
        Grid.SetColumn(slider, 1); group.Children.Add(slider); parameters.Children.Add(group);
    }
}
