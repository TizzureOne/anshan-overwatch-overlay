using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnshanOverlay;

public sealed class SelectionAppearancePanel : StackPanel
{
    private readonly Controller controller;
    private SelectionAppearance draft;
    private readonly BoardView preview;
    private readonly ComboBox shape;
    private readonly TextBox color;
    private readonly CheckBox outline;
    private readonly Button apply;
    private readonly TextBlock status = new() { FontSize = 11, Foreground = BoardView.Brush("#67768A"), Margin = new Thickness(0, 5, 0, 0) };
    private readonly List<(Slider Slider, Func<SelectionAppearance, double> Read)> sliders = new();
    private bool syncing;

    public SelectionAppearancePanel(Controller controller)
    {
        this.controller = controller; draft = controller.Settings.HeroSelection;
        Children.Add(new TextBlock { Text = "英雄选中样式", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var hero = new SceneItem { HeroId = "ana", X = 75, Y = 64, Size = 58 };
        var second = hero with { Id = Guid.NewGuid().ToString("N"), X = 225 };
        preview = new BoardView { Width = 300, Height = 128, Editing = true, HeroSelection = draft, Scene = new Scene { Width = 300, Height = 128, ShowHeroNames = false, Items = new() { hero, second } }, SelectedIds = new HashSet<string> { hero.Id, second.Id } };
        var background = new Grid { Width = 300, Height = 128 };
        background.ColumnDefinitions.Add(new ColumnDefinition()); background.ColumnDefinitions.Add(new ColumnDefinition());
        background.Children.Add(new Border { Background = BoardView.Brush("#F0F3F6") });
        var dark = new Border { Background = BoardView.Brush("#263746") }; Grid.SetColumn(dark, 1); background.Children.Add(dark);
        Grid.SetColumnSpan(preview, 2); background.Children.Add(preview);
        Children.Add(new Border { BorderBrush = BoardView.Brush("#D8DEE8"), BorderThickness = new Thickness(1), Child = new Viewbox { Stretch = Stretch.Uniform, Child = background }, Margin = new Thickness(0, 0, 0, 8) });
        shape = new ComboBox { Name = "SelectionShapeChoice", ItemsSource = new[] { "四角", "圆环", "准心", "隐藏选中标记" }, SelectedIndex = (int)draft.Shape, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(shape, "英雄选中形状"); Children.Add(shape);
        var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
        color = new TextBox { Name = "SelectionColorBox", Text = draft.Color, Width = 86, MaxLength = 7, Padding = new Thickness(5, 4, 5, 4), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        colors.Children.Add(new TextBlock { Text = "颜色", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontSize = 12 });
        foreach (string hex in new[] { "#7DE2D1", "#FFFFFF", "#39A9FF", "#FFD166", "#F05265", "#C69AFF" })
        {
            var swatch = new Button { Width = 23, Height = 25, MinHeight = 25, Padding = new Thickness(3), Margin = new Thickness(0, 0, 3, 0), Style = (Style)FindResource("QuietButton"), ToolTip = hex, Content = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(4), Background = BoardView.Brush(hex), BorderBrush = BoardView.Brush("#AAB5C4"), BorderThickness = new Thickness(1) } };
            swatch.Click += (_, _) => { draft = draft with { Color = hex }; color.Text = hex; UpdatePreview(); }; colors.Children.Add(swatch);
        }
        System.Windows.Automation.AutomationProperties.SetName(color, "选中颜色，十六进制"); colors.Children.Add(color); Children.Add(colors);
        AddSlider("粗细", 1, 6, p => p.Thickness, (p, v) => p with { Thickness = v }, "px");
        AddSlider("间距", 0, 24, p => p.Gap, (p, v) => p with { Gap = v }, "px");
        AddSlider("线段长度", 4, 24, p => p.Length, (p, v) => p with { Length = v }, "px");
        AddSlider("不透明度", 0, 100, p => p.Opacity * 100, (p, v) => p with { Opacity = v / 100 }, "%");
        outline = new CheckBox { Name = "SelectionOutlineChoice", Content = "深色衬边", IsChecked = draft.Outline, Margin = new Thickness(0, 4, 0, 8) }; Children.Add(outline);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        apply = new Button { Name = "ApplySelectionStyle", Content = "应用样式", Margin = new Thickness(0, 0, 8, 0) };
        apply.Click += (_, _) => Apply(); actions.Children.Add(apply);
        var reset = new Button { Name = "ResetSelectionStyle", Content = "恢复默认" };
        reset.Click += (_, _) => { draft = new(); Sync(); Apply(); }; actions.Children.Add(reset); Children.Add(actions); Children.Add(status);
        Children.Add(new TextBlock { Text = "拖动滑块查看亮、暗背景预览，点击“应用样式”保存。圆环不使用线段长度；样式不改变阵营颜色，也不显示在 OBS 中。", FontSize = 11, Foreground = BoardView.Brush("#67768A"), Margin = new Thickness(0, 6, 0, 0) });
        shape.SelectionChanged += (_, _) => { if (!syncing && shape.SelectedIndex >= 0) { draft = draft with { Shape = (HeroSelectionShape)shape.SelectedIndex }; UpdatePreview(); } };
        color.TextChanged += (_, _) =>
        {
            if (syncing) return;
            apply.IsEnabled = SelectionAppearance.ValidColor(color.Text);
            if (apply.IsEnabled) { draft = draft with { Color = color.Text.ToUpperInvariant() }; UpdatePreview(); }
            else status.Text = "颜色请填写 #RRGGBB，例如 #7DE2D1。";
        };
        outline.Click += (_, _) => { draft = draft with { Outline = outline.IsChecked == true }; UpdatePreview(); };
        UpdatePreview();
    }
    private void AddSlider(string label, double min, double max, Func<SelectionAppearance, double> read, Func<SelectionAppearance, double, SelectionAppearance> change, string unit)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = $"{read(draft):0} {unit}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(value, 2); row.Children.Add(value);
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true, Value = read(draft) }; Grid.SetColumn(slider, 1); row.Children.Add(slider);
        System.Windows.Automation.AutomationProperties.SetName(slider, "选中" + label);
        slider.ValueChanged += (_, e) => { value.Text = $"{e.NewValue:0} {unit}"; if (!syncing) { draft = change(draft, e.NewValue); UpdatePreview(); } };
        sliders.Add((slider, read)); Children.Add(row);
    }
    private void UpdatePreview()
    {
        preview.HeroSelection = draft; preview.InvalidateVisual(); status.Text = "预览 · 应用后对所有选中英雄生效";
        if (sliders.Count > 2) sliders[2].Slider.IsEnabled = draft.Shape is HeroSelectionShape.Corners or HeroSelectionShape.Crosshair;
    }
    private void Sync()
    {
        syncing = true;
        shape.SelectedIndex = (int)draft.Shape; color.Text = draft.Color; outline.IsChecked = draft.Outline;
        foreach (var pair in sliders) pair.Slider.Value = pair.Read(draft);
        syncing = false; apply.IsEnabled = true; UpdatePreview();
    }
    private void Apply()
    {
        if (controller.SetSelectionAppearance(draft) is { } error) { status.Text = error; return; }
        draft = controller.Settings.HeroSelection; Sync(); status.Text = "已应用并保存";
    }
}
