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
    private readonly AppUpdates updates = new();
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
    }
    public void StopUpdateChecks() { updateTimer.Stop(); updateCancellation?.Cancel(); }
    private void SetUpdateBusy(bool busy)
    {
        updateBusy = busy;
        CheckUpdateButton.IsEnabled = AutomaticUpdates.IsEnabled = BetaUpdates.IsEnabled = !busy;
        InstallUpdateButton.IsEnabled = !busy && availableUpdate is not null;
        CancelUpdateButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    public async Task CheckForUpdatesAsync()
    {
        if (updateBusy || controller.IsClosing) return;
        updateCancellation = new CancellationTokenSource(); SetUpdateBusy(true); UpdateStateText.Text = "正在检查更新…";
        try
        {
            ShowAvailableUpdate(await updates.CheckAsync(BuildInfo.Version, controller.Settings.IncludeBetaUpdates, BuildInfo.Edition, updateCancellation.Token));
        }
        catch (OperationCanceledException) { UpdateStateText.Text = updateCancellation.IsCancellationRequested ? "已取消检查。" : "检查超时，请稍后重试或打开下载页。"; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { UpdateStateText.Text = "暂时无法检查更新，请稍后重试或打开下载页。"; }
        finally { SetUpdateBusy(false); updateCancellation.Dispose(); updateCancellation = null; }
    }
    internal void ShowAvailableUpdate(UpdateRelease? release)
    {
        availableUpdate = release;
        AvailableVersionText.Text = release is null ? "" : "可更新到 " + release.Version;
        UpdateNotes.Text = release?.Notes ?? "";
        UpdateNotice.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateNoticeText.Text = release is null ? "" : "发现新版本 " + release.Version + " · 查看更新";
        UpdateStateText.Text = release is null ? "暂无可用更新 · " + DateTime.Now.ToString("HH:mm") + " 已检查" : "有新版本，可查看说明后更新。";
        InstallUpdateButton.IsEnabled = !updateBusy && release is not null;
        if (release is not null) Motion.Reveal(UpdateNotice);
    }
    private async void CheckUpdateClick(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();
    private void CancelUpdateClick(object sender, RoutedEventArgs e) => updateCancellation?.Cancel();
    private void ShowUpdatesClick(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = UpdatesTab;
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
        if (!updateChecksStarted) return;
        if (controller.Settings.AutomaticallyCheckUpdates) updateTimer.Start(); else updateTimer.Stop();
        if (sender == BetaUpdates) { ShowAvailableUpdate(null); UpdateStateText.Text = "更新范围已更改，请检查更新。"; }
        if (sender != AnimateUi && (controller.Settings.AutomaticallyCheckUpdates || sender == BetaUpdates)) _ = CheckForUpdatesAsync();
    }
    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is not { } release || updateBusy) return;
        updateCancellation = new CancellationTokenSource(); SetUpdateBusy(true); UpdateProgress.Value = 0; UpdateProgress.Visibility = Visibility.Visible;
        UpdateStateText.Text = "正在下载更新…";
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnshanOverlay", "Updates", Guid.NewGuid().ToString("N"));
            string installer = await updates.DownloadAsync(release, directory, new Progress<int>(value => { UpdateProgress.Value = value; UpdateStateText.Text = $"正在下载更新 · {value}%"; }), updateCancellation.Token);
            updateCancellation.Token.ThrowIfCancellationRequested();
            controller.PrepareForUpdate();
            var command = AppUpdates.InstallerCommand(installer, release, AppContext.BaseDirectory, Environment.ProcessId);
            using var process = Process.Start(command) ?? throw new InvalidOperationException("Updater did not start");
            UpdateStateText.Text = "已保存场景，正在退出并安装更新…"; controller.Close();
        }
        catch (OperationCanceledException) { UpdateStateText.Text = "已取消下载，当前版本可继续使用。"; }
        catch (InvalidDataException) { UpdateStateText.Text = "安装包校验未通过，请重试下载或打开下载页。"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { UpdateStateText.Text = "更新尚未开始，请检查网络、保存位置权限后重试，或打开下载页。"; }
        finally { SetUpdateBusy(false); UpdateProgress.Visibility = Visibility.Collapsed; updateCancellation?.Dispose(); updateCancellation = null; }
    }
    public void ShowUpdateResult(int exitCode, string targetVersion)
    {
        MainTabs.SelectedItem = UpdatesTab;
        UpdateStateText.Text = exitCode == 0 && BuildInfo.Version == targetVersion ? "更新完成 · 当前版本 " + BuildInfo.Version : "更新未完成 · 当前仍为 " + BuildInfo.Version + "，可重试或打开下载页。";
    }
    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || e.Source != MainTabs) return;
        Dispatcher.BeginInvoke(new Action(() => { if (MainTabs.SelectedContent is FrameworkElement content) Motion.Reveal(content); }), DispatcherPriority.Loaded);
    }
}
