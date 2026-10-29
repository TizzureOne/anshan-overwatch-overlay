using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AnshanOverlay;

public partial class MainWindow
{
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(12) };
    private CancellationTokenSource? updateCancellation;
    private UpdateRelease? availableUpdate;
    private bool updateBusy;
    private bool updateChecksStarted;

    public void StartUpdateChecks()
    {
        if (updateChecksStarted) return; updateChecksStarted = true;
        updateTimer.Tick += async (_, _) => { if (controller.Settings.AutomaticallyCheckUpdates) await CheckForUpdatesAsync(); };
        if (controller.Settings.AutomaticallyCheckUpdates) { updateTimer.Start(); _ = CheckForUpdatesAsync(); }
        if (controller.Settings.AutomaticallyUpdateHeroes) _ = CheckHeroDataAsync();
    }
    public void StopUpdateChecks() { updateTimer.Stop(); updateCancellation?.Cancel(); heroDataCancellation?.Cancel(); }
    private void SetUpdateBusy(bool busy)
    {
        updateBusy = busy;
        CheckUpdateButton.IsEnabled = AutomaticUpdates.IsEnabled = BetaUpdates.IsEnabled = !busy;
        InstallUpdateButton.IsEnabled = !busy && availableUpdate is not null;
        CancelUpdateButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        UpdateProgress.Value = 0; UpdateProgress.IsIndeterminate = busy && Motion.CanAnimate;
        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) { Motion.Reveal(UpdateProgress, offset: 0); Motion.Reveal(CancelUpdateButton, offset: 0); }
    }
    public async Task CheckForUpdatesAsync()
    {
        if (updateBusy || controller.IsClosing) return;
        updateCancellation = new CancellationTokenSource(); SetUpdateBusy(true); Motion.Text(UpdateStateText, "正在检查更新…");
        try
        {
            using var client = NetworkProxy.CreateClient(controller.Settings);
            var updates = new AppUpdates(client);
            ShowAvailableUpdate(await updates.CheckAsync(BuildInfo.Version, controller.Settings.IncludeBetaUpdates, BuildInfo.Edition, updateCancellation.Token));
        }
        catch (OperationCanceledException) { Motion.Text(UpdateStateText, updateCancellation.IsCancellationRequested ? "已取消检查。" : "检查超时，请稍后重试或打开下载页。"); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { Motion.Text(UpdateStateText, "暂时无法检查更新，请检查网络或代理设置后重试，也可打开下载页。"); }
        finally { SetUpdateBusy(false); updateCancellation.Dispose(); updateCancellation = null; }
    }
    internal void ShowAvailableUpdate(UpdateRelease? release)
    {
        availableUpdate = release;
        AvailableVersionText.Text = release is null ? "" : "可更新到 " + release.Version;
        UpdateNotes.Text = release?.Notes ?? "";
        UpdateNotice.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateNoticeText.Text = release is null ? "" : "发现新版本 " + release.Version + " · 查看更新";
        Motion.Text(UpdateStateText, release is null ? "暂无可用更新 · " + DateTime.Now.ToString("HH:mm") + " 已检查" : "有新版本，可查看说明后更新。");
        InstallUpdateButton.IsEnabled = !updateBusy && release is not null;
        if (release is not null) Motion.Reveal(UpdateNotice);
        controller.LectureBar?.Refresh();
    }
    private async void CheckUpdateClick(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();
    private void CancelUpdateClick(object sender, RoutedEventArgs e) => updateCancellation?.Cancel();
    private void ShowUpdatesClick(object sender, RoutedEventArgs e) => controller.ShowPanelPage(3);
    private void OpenReleasesClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(availableUpdate?.Page.AbsoluteUri ?? AppUpdates.ReleasesPage) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { UpdateStateText.Text = "无法打开浏览器，请手动访问 GitHub 的安山守望发布页。"; }
    }
    private void UpdatePreferencesChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        string? error = controller.ApplyUpdatePreferences(AutomaticUpdates.IsChecked == true, BetaUpdates.IsChecked == true, AnimateUi.IsChecked == true);
        if (error is not null)
        {
            ready = false;
            AutomaticUpdates.IsChecked = controller.Settings.AutomaticallyCheckUpdates;
            BetaUpdates.IsChecked = controller.Settings.IncludeBetaUpdates;
            AnimateUi.IsChecked = controller.Settings.AnimateInterface;
            ready = true; UpdateStateText.Text = error; return;
        }
        if (!Motion.CanAnimate) { UpdateProgress.IsIndeterminate = false; UpdateProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null); }
        if (!updateChecksStarted) return;
        if (controller.Settings.AutomaticallyCheckUpdates) updateTimer.Start(); else updateTimer.Stop();
        if (sender == BetaUpdates) { ShowAvailableUpdate(null); UpdateStateText.Text = "更新范围已更改，请检查更新。"; }
        if (sender != AnimateUi && (controller.Settings.AutomaticallyCheckUpdates || sender == BetaUpdates)) _ = CheckForUpdatesAsync();
    }
    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is not { } release || updateBusy) return;
        updateCancellation = new CancellationTokenSource(); SetUpdateBusy(true); UpdateProgress.IsIndeterminate = false;
        Motion.Text(UpdateStateText, "正在下载更新…");
        try
        {
            using var client = NetworkProxy.CreateClient(controller.Settings);
            var updates = new AppUpdates(client);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnshanOverlay", "Updates", Guid.NewGuid().ToString("N"));
            string installer = await updates.DownloadAsync(release, directory, new Progress<int>(value => { Motion.To(UpdateProgress, System.Windows.Controls.Primitives.RangeBase.ValueProperty, value, 120); UpdateStateText.Text = $"正在下载更新 · {value}%"; }), updateCancellation.Token);
            updateCancellation.Token.ThrowIfCancellationRequested();
            controller.PrepareForUpdate();
            var command = AppUpdates.InstallerCommand(installer, release, AppContext.BaseDirectory, Environment.ProcessId);
            using var process = Process.Start(command) ?? throw new InvalidOperationException("Updater did not start");
            Motion.Text(UpdateStateText, "已保存场景，正在退出并安装更新…"); controller.Close();
        }
        catch (OperationCanceledException) { Motion.Text(UpdateStateText, "已取消下载，当前版本可继续使用。"); }
        catch (InvalidDataException) { Motion.Text(UpdateStateText, "安装包校验未通过，请重试下载或打开下载页。"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { Motion.Text(UpdateStateText, "更新尚未开始，请检查网络、代理设置或保存位置权限后重试，也可打开下载页。"); }
        finally { SetUpdateBusy(false); UpdateProgress.Visibility = Visibility.Collapsed; updateCancellation?.Dispose(); updateCancellation = null; }
    }
    public void ShowUpdateResult(int exitCode, string targetVersion)
    {
        controller.ShowPanelPage(3);
        Motion.Text(UpdateStateText, exitCode == 0 && BuildInfo.Version == targetVersion ? "更新完成 · 当前版本 " + BuildInfo.Version : "更新未完成 · 当前仍为 " + BuildInfo.Version + "，可重试或打开下载页。");
    }
    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || e.Source != MainTabs) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MainTabs.SelectedContent is FrameworkElement content) Motion.Reveal(content, offset: 0);
            if (MainTabs.SelectedItem is TabItem tab && tab.Template.FindName("Indicator", tab) is FrameworkElement indicator) Motion.Start(indicator, WidthProperty, 6, 150);
        }), DispatcherPriority.Loaded);
    }
}
