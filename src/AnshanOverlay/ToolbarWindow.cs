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
    private readonly CheckBox dashed = new() { Content = "虚线", FontSize = 12, ToolTip = "对接下来绘制的线条和图形生效" };
    private readonly Button undo;
    private readonly Button redo;
    private readonly Button shape;
    private readonly Button more;
    private readonly TextBlock hint = new() { FontSize = 11, Foreground = BoardView.Brush("#67768A"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock count = new() { FontSize = 11, Foreground = BoardView.Brush("#67768A"), TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Grid toolContent;
    private readonly Grid commandRow;
    private readonly Border footer;
    private readonly Button foldButton;
    private EditorTool lastShape = EditorTool.Arrow;
    private string shownState = "";
    private bool compact;
    private bool changingParameter;
    private bool adjusting;
    private Button? trailButton;
    private ContextMenu? activeMenu;
    private double expandedHeight;
    private double layoutWidth;
    private double expandedMinimum = 150;
    public bool IsFolded { get; private set; }
    public double ExpandedHeight => IsFolded ? expandedHeight : Motion.Height(this);
    private static readonly (EditorTool Tool, string Label, string Geometry)[] Definitions = {
        (EditorTool.Move,"移动","M 8,1 L 8,17 M 1,9 L 15,9 M 5,4 L 8,1 L 11,4 M 5,14 L 8,17 L 11,14 M 4,6 L 1,9 L 4,12 M 12,6 L 15,9 L 12,12"),
        (EditorTool.Brush,"画笔","M 3,12 L 12,2 L 16,6 L 7,15 Z M 3,12 C 4,19 1,18 0,18"),
        (EditorTool.Line,"直线","M 1,16 L 15,2"),
        (EditorTool.Arrow,"箭头","M 1,16 L 15,2 M 6,2 L 15,2 L 15,11"),
        (EditorTool.Circle,"椭圆","M 1,9 A 8,5 0 1 1 1,9.01"),
        (EditorTool.Rectangle,"矩形","M 1,3 L 17,3 L 17,15 L 1,15 Z"),
        (EditorTool.Text,"文字","M 1,3 L 17,3 M 9,3 L 9,17 M 5,17 L 13,17"),
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
        var shell = new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); shell.RowDefinitions.Add(new RowDefinition()); shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(8, 0, 6, 0) };
        foldButton = ActionButton(CreateIcon("M 2,11 L 8,5 L 14,11"), "折叠工具栏", () => SetFolded(!IsFolded));
        foldButton.Name = "ToolbarFoldButton"; foldButton.Width = 28; foldButton.MinHeight = foldButton.Height = 26; foldButton.Padding = new Thickness(4); foldButton.Margin = new Thickness(0, 1, 2, 1);
        DockPanel.SetDock(foldButton, Dock.Right); header.Children.Add(foldButton);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "⠿", FontSize = 14, Margin = new Thickness(0, 0, 6, 0), Foreground = BoardView.Brush("#67768A"), Cursor = Cursors.SizeAll });
        title.Children.Add(new TextBlock { Text = "工具", FontSize = 12, Foreground = BoardView.Brush("#67768A"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(title);
        shell.Children.Add(new Border { Background = BoardView.Brush("#F7F7F8"), BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(0, 0, 0, 1), Child = header });
        toolContent = new Grid(); toolContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); toolContent.RowDefinitions.Add(new RowDefinition());
        commandRow = new Grid { Margin = new Thickness(8, 7, 8, 5) }; commandRow.ColumnDefinitions.Add(new ColumnDefinition()); commandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var (tool, label, geometry) in Definitions)
        {
            var button = ActionButton(ToolFace(label, geometry), label, () => controller.SetTool(tool));
            button.Height = 44; button.Padding = new Thickness(4, 2, 4, 2); button.Tag = tool;
            tools[tool] = button;
        }
        shape = ActionButton(ToolFace("箭头 ▾", Definitions.Single(d => d.Tool == lastShape).Geometry), "选择形状", () => OpenMenu(CreateShapeMenu(), shape!));
        shape.Height = 44; shape.Padding = new Thickness(3); shape.Name = "ShapeButton";
        commandRow.Children.Add(toolStrip);
        var utilities = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        undo = ActionButton(CreateIcon("M 6,4 L 1,9 L 6,14 M 1,9 L 10,9 C 19,9 19,18 10,18"), "撤销", controller.Undo);
        redo = ActionButton(CreateIcon("M 12,4 L 17,9 L 12,14 M 17,9 L 8,9 C -1,9 -1,18 8,18"), "重做", controller.Redo);
        more = ActionButton(CreateIcon("M 2,9 L 3,9 M 8,9 L 9,9 M 14,9 L 15,9"), "更多操作", () => OpenMenu(CreateMoreMenu(), more!));
        foreach (var button in new[] { undo, redo, more }) { button.Width = 28; button.Padding = new Thickness(5); utilities.Children.Add(button); }
        var clear = ActionButton("清笔迹", "清除标注 · 保留英雄 · 可撤销", () => controller.Clear(true));
        clear.Name = "ClearInkButton"; clear.Padding = new Thickness(5); utilities.Children.Insert(2, clear);
        var utilityBorder = new Border { BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(5, 6, 0, 6), Padding = new Thickness(6, 0, 0, 0), Child = utilities };
        Grid.SetColumn(utilityBorder, 1); commandRow.Children.Add(utilityBorder); toolContent.Children.Add(commandRow);
        dashed.Click += (_, _) => controller.SetDashed(dashed.IsChecked == true);
        var parameterScroll = new ScrollViewer { Content = parameters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 2, 8, 0) };
        var parameterBorder = new Border { BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(0, 1, 0, 0), Child = parameterScroll };
        Grid.SetRow(parameterBorder, 1); toolContent.Children.Add(parameterBorder); Grid.SetRow(toolContent, 1); shell.Children.Add(toolContent);
        var footerContent = new DockPanel(); DockPanel.SetDock(count, Dock.Right); footerContent.Children.Add(count); footerContent.Children.Add(hint);
        footer = new Border { Background = BoardView.Brush("#F7F7F8"), BorderBrush = BoardView.Brush("#E3E4E6"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 5, 10, 5), Child = footerContent };
        Grid.SetRow(footer, 2); shell.Children.Add(footer);
        Content = new Border { Background = Background, Child = shell, BorderBrush = BoardView.Brush("#CDD0D4"), BorderThickness = new Thickness(1) };
        shell.SizeChanged += (_, e) => ConfigureLayout(e.NewSize.Width);
        Loaded += (_, _) => Motion.Reveal(toolContent);
        IsVisibleChanged += (_, _) => { if (IsVisible) Motion.Reveal((FrameworkElement)Content, offset: 0); else CloseMenu(); };
        LocationChanged += (_, _) => CloseMenu();
        SizeChanged += (_, e) => { if (e.WidthChanged) CloseMenu(); };
        Closed += (_, _) => CloseMenu();
        controller.Changed += Refresh;
        Closing += (_, e) => { if (!controller.IsClosing) { e.Cancel = true; Dispatcher.BeginInvoke(new Action(() => { if (!controller.IsClosing) controller.SetLectureToolsFolded(true); })); } };
        PreviewKeyDown += (_, e) => { if (e.OriginalSource is not TextBoxBase && Keyboard.FocusedElement is not RangeBase and not TextBoxBase and not ComboBox && e.Key != Key.Tab) controller.HandleEditorKey(e); };
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
        layoutWidth = Math.Max(298, width);
        bool narrow = width < 640;
        if (toolStrip.Children.Count > 0 && compact == narrow) { UpdateMinimumHeight(); return; }
        compact = narrow; toolStrip.Children.Clear(); toolStrip.ColumnDefinitions.Clear(); toolStrip.RowDefinitions.Clear();
        var visibleTools = compact ? new[] { tools[EditorTool.Move], tools[EditorTool.Brush], shape, tools[EditorTool.Eraser], tools[EditorTool.Text] } : Definitions.Select(d => tools[d.Tool]).ToArray();
        int columns = compact ? 3 : visibleTools.Length;
        for (int n = 0; n < columns; n++) toolStrip.ColumnDefinitions.Add(new ColumnDefinition());
        for (int n = 0; n < (compact ? 2 : 1); n++) toolStrip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int n = 0; n < visibleTools.Length; n++)
        { Grid.SetColumn(visibleTools[n], n % columns); Grid.SetRow(visibleTools[n], n / columns); toolStrip.Children.Add(visibleTools[n]); }
        count.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BuildParameters(); UpdateMinimumHeight();
    }
    private void UpdateMinimumHeight()
    {
        parameters.Measure(new Size(Math.Max(1, layoutWidth - 20), double.PositiveInfinity));
        footer.Measure(new Size(layoutWidth, double.PositiveInfinity));
        // Width changes arrive during arrange, when the command grid may still report its old row height.
        double commandsHeight = tools[EditorTool.Move].Height * (compact ? 2 : 1) + commandRow.Margin.Top + commandRow.Margin.Bottom;
        double minimum = Math.Max(150, Math.Ceiling(28 + commandsHeight + parameters.DesiredSize.Height + footer.DesiredSize.Height + 7));
        if (Math.Abs(minimum - expandedMinimum) < 1) return;
        expandedMinimum = minimum;
        if (IsFolded) { expandedHeight = Math.Max(expandedHeight, minimum); return; }
        Motion.FinishResize(this);
        MinHeight = minimum;
        if (Height < minimum)
        {
            Height = minimum;
            if (IsVisible) Dispatcher.BeginInvoke(new Action(() => WindowLayout.KeepVisible(this)));
        }
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
        Add("文字    " + controller.BindingText("text"), () => controller.SetTool(EditorTool.Text));
        menu.Items.Add(new Separator());
        Add("清除标注", () => controller.Clear(true));
        Add("清空全部    " + controller.BindingText("clear"), () => controller.Clear(false));
        menu.Items.Add(new Separator());
        Add("自定义快捷键…", controller.ShowShortcuts); Add("隐藏工具栏", () => controller.SetLectureToolsFolded(true)); Add("退出程序", controller.Close);
        return menu;
    }
    private void OpenMenu(ContextMenu menu, Button target)
    {
        CloseMenu(); activeMenu = menu;
        menu.PlacementTarget = target; menu.Placement = PlacementMode.Bottom;
        menu.Closed += (_, _) => { if (ReferenceEquals(activeMenu, menu)) activeMenu = null; };
        menu.IsOpen = true;
    }
    private void CloseMenu() { var menu = activeMenu; activeMenu = null; if (menu is not null) menu.IsOpen = false; }
    private static bool IsShape(EditorTool tool) => tool is EditorTool.Line or EditorTool.Arrow or EditorTool.Circle or EditorTool.Rectangle;
    public void SetFolded(bool folded)
    {
        if (IsFolded == folded) return;
        CloseMenu();
        if (folded) expandedHeight = Motion.Height(this);
        IsFolded = folded; toolContent.Visibility = footer.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        foldButton.Content = CreateIcon(folded ? "M 2,5 L 8,11 L 14,5" : "M 2,11 L 8,5 L 14,11");
        foldButton.ToolTip = folded ? "展开工具栏" : "折叠工具栏";
        Motion.Resize(this, folded ? 44 : Math.Max(expandedHeight, expandedMinimum), folded ? 44 : expandedMinimum);
        ResizeMode = folded ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        if (!folded) Motion.Reveal(toolContent);
        controller.LectureBar?.Refresh();
    }
    public void Refresh()
    {
        dashed.IsChecked = controller.Editor.Dashed;
        undo.IsEnabled = controller.Store.CanUndo || controller.Editor.Temporary.CanUndo; redo.IsEnabled = controller.Store.CanRedo || controller.Editor.Temporary.CanRedo;
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
        string action = current switch { EditorTool.Move => "拖动移动 · 空白处框选", EditorTool.Brush => controller.Editor.TemporaryBrush ? "临时画笔 · 松手 5 秒后消失" : "拖动画笔 · 连续绘制", EditorTool.Line => "直线 · Shift 约束方向", EditorTool.Arrow => "箭头 · 从起点拖到终点", EditorTool.Circle => "椭圆 · Shift 绘制正圆", EditorTool.Rectangle => "矩形 · Shift 绘制正方形", EditorTool.Text => "点击战术板输入 · 双击文字继续编辑", _ => "橡皮 · 拖动擦除" };
        Motion.Text(hint, action); hint.ToolTip = action + " · " + controller.BindingText(current.ToString().ToLowerInvariant());
        count.Text = $"{controller.Store.Current.Items.Count(i => i.Kind == "hero")} 个英雄 · {controller.Store.Current.Items.Count(i => i.Kind != "hero")} 个标注";
        if (trailButton is not null && !Equals(trailButton.Content, TrailCaption()))
        { trailButton.Content = TrailCaption(); UpdateMinimumHeight(); }
        string state = ParameterState();
        if (shownState == state || changingParameter || adjusting) return;
        shownState = state; BuildParameters(); UpdateMinimumHeight(); Motion.Reveal(parameters);
    }
    private void BuildParameters()
    {
        CloseMenu(); trailButton = null;
        // Release these shared controls from the previous drawing row before reusing them.
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
            if (selected.Kind == "hero")
            {
                Slider("头像尺寸", selected.Size, 24, 400, v => controller.UpdateSelected(i => i with { Size = v }), true);
                var team = new ComboBox { Width = 82, Height = 28, FontSize = 12, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 10, 4), ItemsSource = new[] { "蓝方", "红方" }, SelectedIndex = selected.Team == "ally" ? 0 : 1 };
                System.Windows.Automation.AutomationProperties.SetName(team, "选中英雄阵营");
                team.SelectionChanged += (_, _) => Change(() => controller.UpdateSelected(i => i with { Team = team.SelectedIndex == 0 ? "ally" : "enemy" }));
                parameters.Children.Add(team);
                trailButton = ActionButton(TrailCaption(), "调整所有英雄的移动轨迹", () => OpenMenu(controller.Overlay.CreateTrailMenu(), trailButton!));
                trailButton.Name = "HeroTrailButton"; trailButton.MinHeight = trailButton.Height = 28; trailButton.Padding = new Thickness(6, 2, 6, 2); trailButton.Margin = new Thickness(0, 0, 8, 4); parameters.Children.Add(trailButton);
            }
            else if (Annotation.IsLabel(selected))
            {
                if (selected.Kind == "number") LabelInput(selected);
                else parameters.Children.Add(ActionButton("编辑文字", "在文字原位置继续编辑", () => controller.Overlay.TextEditor.Begin(selected.Start, selected)));
                Slider("字号", selected.FontSize, 12, 96, v => controller.UpdateSelected(i => Annotation.Fit(i with { FontSize = v }, controller.Store.Current)), true);
            }
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
            var whole = new CheckBox { Content = "整项擦除", IsChecked = editor.EraseWhole, ToolTip = "不勾选时仅擦掉笔迹局部；文字标注始终整项擦除", FontSize = 12 };
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
                button.Click += (_, _) => { profile.Color = color; if (editor.Tool == EditorTool.Text) controller.Overlay.TextEditor.UpdateStyle(); foreach (Button peer in colors.Children.OfType<Button>()) Motion.Paint(peer, BorderBrushProperty, ReferenceEquals(peer, button) ? "#42484F" : "#00FFFFFF"); }; colors.Children.Add(button);
            }
            parameters.Children.Add(colors);
            if (editor.Tool == EditorTool.Text) Slider("字号", profile.FontSize, 12, 96, v => { profile.FontSize = v; controller.Overlay.TextEditor.UpdateStyle(); });
            else
            {
                Slider("粗细", profile.Width, 2, 16, v => profile.Width = v);
                var toggles = new StackPanel { Orientation = Orientation.Horizontal, Height = 28 }; toggles.Children.Add(dashed);
                if (editor.Tool == EditorTool.Brush)
                {
                    toggles.Children.Add(new TextBlock { Text = "保留", FontSize = 12, Margin = new Thickness(10, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
                    var duration = new ComboBox { Name = "BrushDurationChoice", Width = 82, FontSize = 12, Padding = new Thickness(5, 2, 5, 2), ItemsSource = new[] { "永久", "5 秒" }, SelectedIndex = editor.TemporaryBrush ? 1 : 0, ToolTip = "选择 5 秒时，松手后自动消失；桌面与 OBS 同步" };
                    System.Windows.Automation.AutomationProperties.SetName(duration, "笔迹保留时间");
                    duration.SelectionChanged += (_, _) => Change(() => controller.SetTemporaryBrush(duration.SelectedIndex == 1)); toggles.Children.Add(duration);
                }
                parameters.Children.Add(toggles);
            }
        }
    }
    private void LabelInput(SceneItem selected)
    {
        var row = new DockPanel { Width = 248, Margin = new Thickness(0, 0, 10, 4) };
        var input = new TextBox { Text = selected.Text, MaxLength = 4, MinWidth = 80, FontSize = 12, ToolTip = "编号：1–9999" };
        System.Windows.Automation.AutomationProperties.SetName(input, "编号内容");
        var apply = ActionButton("应用", "应用编号修改", () =>
        {
            string value = input.Text.Trim();
            if (!int.TryParse(value, out int number) || number < 1 || number > 9999)
            { controller.SetStatus("编号请输入 1–9999。"); return; }
            controller.UpdateSelected(i => Annotation.Fit(i with { Text = value }, controller.Store.Current));
        });
        apply.Margin = new Thickness(4, 0, 0, 0); DockPanel.SetDock(apply, Dock.Right); row.Children.Add(apply);
        row.Children.Add(input); parameters.Children.Add(row);
    }
    private string TrailCaption() => "所有英雄轨迹 · " + (!controller.Settings.HeroTrailsEnabled ? "关闭" : (controller.Settings.HeroTrailStyle == HeroTrailStyle.Dashed ? "虚线" : "拖尾") + $" {controller.Settings.HeroTrailSeconds}秒") + " ▾";
    private string ParameterState()
    {
        var current = controller.Editor.Tool; var selected = controller.Editor.Selected;
        return current + (current == EditorTool.Move ? $"/{controller.Editor.SelectedIds.Count}/{selected?.Id}/{selected?.Width}/{selected?.Opacity}/{selected?.Dashed}/{selected?.Size}/{selected?.Text}/{selected?.FontSize}/{selected?.Team}" : current == EditorTool.Brush ? $"/{controller.Editor.TemporaryBrush}" : current == EditorTool.Text ? $"/{controller.Overlay.TextEditor.ItemId}/{controller.Editor.Options.FontSize}/{controller.Editor.Options.Color}" : "");
    }
    private void Change(Action action)
    {
        changingParameter = true;
        try { action(); }
        finally { shownState = ParameterState(); changingParameter = false; }
    }
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
