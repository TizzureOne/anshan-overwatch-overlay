using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;

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
            var controller = new Controller(); MainWindow = controller.Panel; await controller.StartAsync();
            int resultIndex = Array.IndexOf(e.Args, "--update-result"), targetIndex = Array.IndexOf(e.Args, "--update-target");
            if (resultIndex >= 0 && resultIndex + 1 < e.Args.Length && targetIndex >= 0 && targetIndex + 1 < e.Args.Length && int.TryParse(e.Args[resultIndex + 1], out int result))
                controller.Panel.ShowUpdateResult(result, e.Args[targetIndex + 1]);
        }
        catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "安山守望"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
    private void ButtonHover(object sender, MouseEventArgs e) => Motion.ButtonFeedback(sender, false);
    private void ButtonPress(object sender, MouseButtonEventArgs e) => Motion.ButtonFeedback(sender, true);
}
