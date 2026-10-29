using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AnshanOverlay;

public partial class App : Application
{
    private Mutex? singleInstance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            try { await SelfTests.RunAsync(e.Args); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        singleInstance = new Mutex(true, "Local\\AnshanOverlay.Desktop", out bool first);
        if (!first) { MessageBox.Show("工具已在运行，请从系统托盘打开，或按 Ctrl+Alt+P。", "安山守望"); Shutdown(); return; }
        try
        {
            var controller = new Controller(); MainWindow = controller.LectureBar; await controller.StartAsync();
            int resultIndex = Array.IndexOf(e.Args, "--update-result"), targetIndex = Array.IndexOf(e.Args, "--update-target");
            if (resultIndex >= 0 && resultIndex + 1 < e.Args.Length && targetIndex >= 0 && targetIndex + 1 < e.Args.Length && int.TryParse(e.Args[resultIndex + 1], out int result))
                controller.Panel.ShowUpdateResult(result, e.Args[targetIndex + 1]);
        }
        catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "安山守望"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
    private void ButtonHover(object sender, MouseEventArgs e) => Motion.ButtonFeedback(sender, false);
    private void ButtonPress(object sender, MouseButtonEventArgs e) => Motion.ButtonFeedback(sender, true);
    private void ButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Template.FindName("Bd", button) is FrameworkElement face) Motion.Pulse(face);
    }
    private void ToggleFeedback(object sender, RoutedEventArgs e) { if (sender is FrameworkElement element) Motion.Pulse(element); }
    private void InputFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Control input) Motion.Paint(input, Control.BorderBrushProperty, input.IsKeyboardFocusWithin ? "#FF9C27" : "#C5CFDB");
    }
    private void PopupFeedback(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement popup) return;
        Motion.Reveal(popup, offset: 4);
        if (popup is ItemsControl items) popup.Dispatcher.BeginInvoke(new Action(() => Motion.Cascade(items)), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void MenuHighlight(object sender, MouseEventArgs e)
    {
        if (sender is MenuItem item && item.Template.FindName("Row", item) is Border row)
            Motion.Paint(row, Border.BackgroundProperty, item.IsChecked ? "#FFF0DE" : item.IsMouseOver ? "#EEF1F6" : "#00FFFFFF");
    }
    private void ComboOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox box && box.Template.FindName("PART_Popup", box) is Popup { Child: FrameworkElement content }) Motion.Reveal(content, offset: 3);
    }
    private void ComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox box) { box.DropDownOpened -= ComboOpened; box.DropDownOpened += ComboOpened; }
    }
}
