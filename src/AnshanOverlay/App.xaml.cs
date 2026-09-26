using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

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
        try { var controller = new Controller(); MainWindow = controller.Panel; await controller.StartAsync(); }
        catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "安山守望"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
}
