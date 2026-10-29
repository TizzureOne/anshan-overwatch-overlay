using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AnshanOverlay;

public partial class MainWindow : Window
{
    private readonly Controller controller;
    private bool ready;
    private string role = "tank";
    private Point heroDragOrigin;
    private bool draggedHero;
    private readonly List<Hero> batchHeroes = new();
    private double expandedHeight;
    private HeroDragPreviewWindow? dragPreview;
    public bool IsFolded { get; private set; }
    public const double FoldedHeight = 84;
    public double ExpandedHeight => IsFolded ? expandedHeight : (WindowState == WindowState.Normal ? Motion.Height(this) : RestoreBounds.Height);
    public MainWindow(Controller controller)
    {
        this.controller = controller; InitializeComponent();
        Title = "安山守望 · 英雄库"; VersionText.Text = BuildInfo.Label;
        CurrentVersionText.Text = "当前版本 " + BuildInfo.Version + " · " + BuildInfo.EditionName;
        AutomaticUpdates.IsChecked = controller.Settings.AutomaticallyCheckUpdates; BetaUpdates.IsChecked = controller.Settings.IncludeBetaUpdates; AnimateUi.IsChecked = controller.Settings.AnimateInterface;
        AutomaticHeroUpdates.IsChecked = controller.Settings.AutomaticallyUpdateHeroes;
        HighFrameRate.IsChecked = controller.Settings.HighFrameRate;
        SelectionSettingsHost.Children.Add(new SelectionAppearancePanel(controller));
        ProxyModeChoice.SelectedIndex = (int)controller.Settings.ProxyMode;
        ProxyAddressBox.Text = controller.Settings.ProxyAddress;
        ProxyAddressBox.IsEnabled = controller.Settings.ProxyMode == NetworkProxyMode.Manual;
        HeroDataStateText.Text = controller.HeroDataWarning ?? "尚未检查英雄数据。";
        PassThroughOpacity.Value = controller.Settings.PassThroughOpacityPercent;
        Loaded += (_, _) => Motion.Reveal(ExpandedPanel);
        IsVisibleChanged += (_, _) => { if (IsVisible) Motion.Reveal((FrameworkElement)Content, offset: 0); };
        Width = Math.Min(controller.Settings.PanelWidth, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(controller.Settings.PanelHeight, SystemParameters.WorkArea.Height - 24);
        WindowLayout.Restore(this, controller.Settings.PanelPosition);
        Icon = Assets.AppIcon; AppMark.Source = Assets.AppIcon;
        WindowGestures.EnableBlankDrag(this);
        ScreenBox.ItemsSource = controller.Screens; ScreenBox.SelectedItem = controller.Screen;
        Closing += (_, e) =>
        {
            if (controller.IsClosing) return;
            e.Cancel = true;
            // Finish the native close event before hiding or closing this window again.
            Dispatcher.BeginInvoke(new Action(ClosePanel));
        };
        Closed += (_, _) => dragPreview?.Close();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            { SetFolded(false); MainTabs.SelectedIndex = 0; SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBox or ComboBox || e.Key == Key.Tab) return;
            controller.HandleEditorKey(e);
        };
        controller.Changed += Refresh;
        ready = true; RefreshHeroCatalog(); Refresh();
    }
    public void Refresh()
    {
        if (!ready) return;
        Motion.Text(ModeStateText, "● " + controller.ModeState);
        ModeStateText.Foreground = BoardView.Brush(controller.PassThrough ? "#1482D4" : "#A55B00");
        Motion.Text(FoldedModeText, controller.ModeState + " · " + controller.BindingText("edit"));
        FoldedModeText.Foreground = ModeStateText.Foreground;
        ModeStateText.ToolTip = FoldedModeText.ToolTip = controller.ModeShortcut;
        ShortcutHelp.Text = string.Join("\n", Preferences.Actions.Select(a => $"{controller.BindingText(a.Id)}    {a.Label}"));
        TopmostLabel.Text = controller.AlwaysOnTop ? "画布置顶" : "画布未置顶";
        VisibleLabel.Text = controller.Store.Current.Visible ? "标注可见" : "标注已隐藏";
        TopmostButton.ToolTip = controller.AlwaysOnTop ? "点击取消画布置顶" : "点击将画布置顶";
        VisibleButton.ToolTip = controller.Store.Current.Visible ? "点击隐藏标注" : "点击显示标注";
        Motion.Text(StatusText, controller.Status);
        StatusText.ToolTip = controller.Status;
        HotkeyWarningText.Text = controller.HotkeyWarning;
        PassThroughOpacity.Value = controller.Settings.PassThroughOpacityPercent;
        PassThroughOpacityText.Text = controller.Settings.PassThroughOpacityPercent + "%";
        ClosePanelButton.ToolTip = "关闭英雄库 · 战术板和工具继续运行";
        Motion.Text(ObjectCount, $"{controller.Store.Current.Items.Count(i => i.Kind == "hero")} 个英雄  /  {controller.Store.Current.Items.Count(i => i.Kind != "hero")} 个标注");
        UndoButton.IsEnabled = controller.Store.CanUndo; RedoButton.IsEnabled = controller.Store.CanRedo;
        ObsUrl.Text = controller.Server.Port == 0 ? "OBS 服务未启动" : controller.Server.Url;
        ObsSize.Text = $"宽度 {controller.Store.Current.Width:0}   /   高度 {controller.Store.Current.Height:0}";
        BlueTeam.IsChecked = controller.Editor.Team == "ally"; RedTeam.IsChecked = controller.Editor.Team == "enemy";
        RefreshHeroButtons();
    }
    private void FilterHeroes()
    {
        if (!ready) return;
        string query = SearchBox.Text.Trim();
        var heroes = controller.Heroes.Heroes.Where(h => (role == "all" || h.Role == role) && h.Matches(query)).ToList();
        HeroList.ItemsSource = heroes;
        SearchHint.Text = query.Length == 0 ? $"双击居中添加 · 拖出自由放置 · {heroes.Count} 位英雄" : $"找到 {heroes.Count} 位英雄 · 双击或拖出添加";
        EmptySearch.Visibility = heroes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HeroScroll.ScrollToTop();
    }
    public void SetFolded(bool folded)
    {
        if (IsFolded == folded) return;
        WindowState = WindowState.Normal;
        if (folded) expandedHeight = Motion.Height(this);
        IsFolded = folded;
        ExpandedPanel.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        FoldedPanel.Visibility = folded ? Visibility.Visible : Visibility.Collapsed;
        Motion.Resize(this, folded ? FoldedHeight : expandedHeight, folded ? FoldedHeight : 420);
        ResizeMode = folded ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Motion.Reveal(folded ? FoldedPanel : ExpandedPanel);
    }
    public void ClosePanel() { if (controller.IsClosing) return; dragPreview?.End(); controller.HeroLibraryOpen = false; Motion.HideWindow(this); controller.LectureBar?.Refresh(); }
    internal TabItem[] DetachManagementPages()
    {
        var pages = MainTabs.Items.Cast<TabItem>().Skip(1).ToArray();
        foreach (var page in pages) MainTabs.Items.Remove(page);
        return pages;
    }
    private void HighFrameRateChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (controller.SetHighFrameRate(HighFrameRate.IsChecked == true) is { } error) { HighFrameRate.IsChecked = controller.Settings.HighFrameRate; controller.SetStatus(error); }
    }
    private void FoldClick(object s, RoutedEventArgs e) => SetFolded(true);
    private void ExpandClick(object s, RoutedEventArgs e) => SetFolded(false);
    private void MinimizeClick(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object s, RoutedEventArgs e) => ClosePanel();
    internal ContextMenu CreateMoreMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("自定义快捷键…", controller.ShowShortcuts);
        Add("设置与更新", () => controller.ShowPanelPage(3));
        menu.Items.Add(new Separator());
        Add("退出程序", controller.Close);
        return menu;
    }
    private void MoreClick(object s, RoutedEventArgs e)
    {
        var menu = CreateMoreMenu(); menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true;
    }
    private void ModeClick(object s, RoutedEventArgs e) => controller.TogglePassThrough();
    private void PassThroughOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        int percent = (int)Math.Round(PassThroughOpacity.Value);
        PassThroughOpacityText.Text = percent + "%";
        if (percent != controller.Settings.PassThroughOpacityPercent && controller.SetPassThroughOpacity(percent) is { } error) controller.SetStatus(error);
    }
    private void TopmostClick(object s, RoutedEventArgs e) => controller.ToggleTopmost();
    private void VisibleClick(object s, RoutedEventArgs e) => controller.ToggleVisible();
    private void SearchChanged(object s, TextChangedEventArgs e)
    {
        if (!ready) return;
        if (SearchBox.Text.Trim().Length > 0 && AllRole.IsChecked != true) { AllRole.IsChecked = true; return; }
        FilterHeroes();
    }
    private void RoleChanged(object s, RoutedEventArgs e) { if (s is RadioButton button && button.IsChecked == true) { role = (string)button.Tag; FilterHeroes(); } }
    private void HeroClick(object s, RoutedEventArgs e)
    {
        if (draggedHero) { draggedHero = false; return; }
        if (BatchMode.IsChecked == true) ToggleBatchHero((Hero)((Button)s).Tag);
    }
    private void HeroTeamChanged(object s, RoutedEventArgs e)
    {
        if (ready && s is RadioButton button && button.IsChecked == true) controller.SetHeroTeam((string)button.Tag);
    }
    private void BatchModeChanged(object s, RoutedEventArgs e)
    {
        if (!ready) return;
        batchHeroes.Clear(); controller.Editor.Cancel();
        UpdateBatchControls(); RefreshHeroButtons();
    }
    internal void ToggleBatchHero(Hero hero)
    {
        if (batchHeroes.Any(h => h.Id == hero.Id)) batchHeroes.RemoveAll(h => h.Id == hero.Id);
        else batchHeroes.Add(hero);
        UpdateBatchControls(); RefreshHeroButtons();
    }
    private void UpdateBatchControls()
    {
        bool reveal = BatchActions.Visibility != Visibility.Visible && BatchMode.IsChecked == true;
        BatchActions.Visibility = BatchMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (reveal) Motion.Reveal(BatchActions);
        Motion.Text(BatchHint, batchHeroes.Count == 0 ? "点选英雄，可跨职责选择" : $"已选 {batchHeroes.Count} 位：" + string.Join("、", batchHeroes.Select(h => h.Name)));
        BatchHint.ToolTip = BatchHint.Text;
        AddHeroesButton.Content = batchHeroes.Count == 0 ? "添加英雄" : $"添加 {batchHeroes.Count} 位英雄";
        AddHeroesButton.IsEnabled = batchHeroes.Count > 0;
    }
    private void HeroButtonLoaded(object s, RoutedEventArgs e)
    {
        var button = (Button)s; RefreshHeroButton(button);
        Motion.Reveal(button, Math.Min(HeroList.Items.IndexOf(button.Tag), 5) * 12);
    }
    private void RefreshHeroButton(Button button)
    {
        string id = ((Hero)button.Tag).Id;
        bool selected = BatchMode.IsChecked == true && batchHeroes.Any(h => h.Id == id);
        Motion.Paint(button, BackgroundProperty, selected ? "#FFF0DF" : "#00FFFFFF");
        Motion.Paint(button, BorderBrushProperty, selected ? "#BE7729" : "#00FFFFFF");
    }
    private void RefreshHeroButtons()
    {
        for (int n = 0; n < HeroList.Items.Count; n++)
            if (HeroList.ItemContainerGenerator.ContainerFromIndex(n) is ContentPresenter presenter && presenter.ContentTemplate?.FindName("HeroButton", presenter) is Button button)
                RefreshHeroButton(button);
    }
    private void AddHeroesClick(object s, RoutedEventArgs e)
    {
        if (controller.AddHeroes(batchHeroes.ToArray())) batchHeroes.Clear();
        UpdateBatchControls(); RefreshHeroButtons();
    }
    private void ClearHeroSelectionClick(object s, RoutedEventArgs e)
    {
        batchHeroes.Clear(); UpdateBatchControls(); RefreshHeroButtons();
    }
    private void HeroDragStart(object s, MouseButtonEventArgs e)
    {
        heroDragOrigin = e.GetPosition(this); draggedHero = false;
    }
    private void HeroDoubleClick(object s, MouseButtonEventArgs e)
    {
        if (s is not Button { Tag: Hero hero }) return;
        draggedHero = true; controller.AddHeroAtCenter(hero); e.Handled = true;
    }
    public void CancelHeroPreview() => dragPreview?.End();
    public void FinishHeroDrag(bool wasPassThrough)
    {
        CancelHeroPreview();
        if (wasPassThrough) controller.SetPassThrough(true);
    }
    private void HeroDragMove(object s, MouseEventArgs e)
    {
        if (draggedHero || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - heroDragOrigin;
        if (System.Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && System.Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var hero = (Hero)((Button)s).Tag; draggedHero = true; bool wasPassThrough = controller.PassThrough;
        controller.SetPassThrough(false);
        try
        {
            dragPreview ??= new HeroDragPreviewWindow();
            dragPreview.Begin(hero.Id, controller.Editor.Size, controller.Editor.Team);
            DragDrop.DoDragDrop((Button)s, new DataObject("AnshanHero", hero.Id), DragDropEffects.Copy);
        }
        finally { FinishHeroDrag(wasPassThrough); e.Handled = true; }
    }
    private void ShortcutsClick(object s, RoutedEventArgs e) => controller.ShowShortcuts();
    private void UndoClick(object s, RoutedEventArgs e) => controller.Undo();
    private void RedoClick(object s, RoutedEventArgs e) => controller.Redo();
    private void DuplicateClick(object s, RoutedEventArgs e) => controller.Duplicate();
    private void DeleteClick(object s, RoutedEventArgs e) => controller.Delete();
    private void ClearLinesClick(object s, RoutedEventArgs e) => controller.Clear(true);
    private void ClearAllClick(object s, RoutedEventArgs e) => controller.Clear(false);
    private void ScreenChanged(object s, SelectionChangedEventArgs e) { if (ready && ScreenBox.SelectedItem is DisplayInfo screen) controller.ChangeScreen(screen); }
    private void CopyObsClick(object s, RoutedEventArgs e)
    {
        if (controller.Server.Port == 0) { controller.SetStatus("OBS 服务未启动，请重新启动工具。"); return; }
        try { Clipboard.SetText(controller.Server.Url); controller.SetStatus("已复制 OBS 浏览器源地址。"); }
        catch (System.Runtime.InteropServices.COMException) { controller.SetStatus("剪贴板正忙，请稍后再试或手动复制地址。"); }
    }
    private void SaveClick(object s, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "安山场景 (*.json)|*.json", FileName = "教学场景.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { controller.Editor.Cancel(); controller.WriteScene(dialog.FileName); controller.SetStatus("场景已保存。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { controller.SetStatus("保存失败：" + ex.Message); }
    }
    private void LoadClick(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "安山场景 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        LoadSceneFile(dialog.FileName);
    }
    internal void LoadSceneFile(string path)
    {
        try { controller.LoadScene(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { controller.SetStatus("加载失败：" + ex.Message); }
    }
}
