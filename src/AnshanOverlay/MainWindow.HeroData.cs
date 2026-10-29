using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AnshanOverlay;

public partial class MainWindow
{
    private bool heroDataBusy;
    private CancellationTokenSource? heroDataCancellation;
    internal void RefreshHeroCatalog()
    {
        var ids = batchHeroes.Select(h => h.Id).ToArray(); batchHeroes.Clear();
        batchHeroes.AddRange(ids.Select(id => controller.Heroes.Heroes.FirstOrDefault(h => h.Id == id)).OfType<Hero>());
        HeroDataSummary.Text = $"{controller.Heroes.Count} 位英雄 · 资料日期 {controller.Heroes.RetrievedAt.ToLocalTime():yyyy-MM-dd}";
        SourceInfo.Text = $"英雄资料：Blizzard / 国服官方 · {controller.Heroes.RetrievedAt.ToLocalTime():yyyy-MM-dd}\n© Blizzard Entertainment。非官方教学工具。";
        FilterHeroes(); UpdateBatchControls(); RefreshHeroButtons();
    }
    internal async Task CheckHeroDataAsync(HeroDataUpdates? service = null)
    {
        if (heroDataBusy || controller.IsClosing) return;
        heroDataBusy = true; heroDataCancellation = new CancellationTokenSource();
        CheckHeroDataButton.IsEnabled = AutomaticHeroUpdates.IsEnabled = false;
        CancelHeroDataButton.Visibility = Visibility.Visible;
        HeroDataStateText.Text = "正在核对官方英雄名单…";
        try
        {
            using var client = service is null ? NetworkProxy.CreateClient(controller.Settings, heroData: true) : null;
            var progress = new Progress<string>(value => { if (heroDataBusy) HeroDataStateText.Text = value; });
            var result = await (service ?? new HeroDataUpdates(client)).UpdateAsync(controller.HeroData, progress, heroDataCancellation.Token);
            if (controller.IsClosing) return;
            if (result.HasChanges) controller.ApplyHeroData(result.Data);
            HeroDataStateText.Text = result.HasChanges ? $"已更新：新增 {result.Added} 位、调整 {result.Updated} 位。英雄库、画布和 OBS 已同步。" : $"英雄数据已是最新 · {DateTime.Now:MM-dd HH:mm} 已核对官方资料。";
        }
        catch (OperationCanceledException)
        { HeroDataStateText.Text = heroDataCancellation.IsCancellationRequested ? "已取消更新，继续使用已有英雄数据。" : "检查超时，继续使用已有英雄数据，可稍后重试。"; }
        catch (HttpRequestException) { HeroDataStateText.Text = "暂时无法连接官方资料，请检查网络或代理设置；继续使用已有英雄数据。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or NotSupportedException or ArgumentException or FileFormatException or RegexMatchTimeoutException)
        { HeroDataStateText.Text = "英雄资料未能完整校验或保存，继续使用已有数据，可稍后重试。"; }
        finally
        {
            heroDataBusy = false; heroDataCancellation.Dispose(); heroDataCancellation = null;
            CheckHeroDataButton.IsEnabled = AutomaticHeroUpdates.IsEnabled = true;
            CancelHeroDataButton.Visibility = Visibility.Collapsed;
        }
    }
    private async void CheckHeroDataClick(object sender, RoutedEventArgs e) => await CheckHeroDataAsync();
    private void CancelHeroDataClick(object sender, RoutedEventArgs e) => heroDataCancellation?.Cancel();
    private void HeroUpdatePreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (controller.SetAutomaticHeroUpdates(AutomaticHeroUpdates.IsChecked == true) is { } error)
        {
            ready = false; AutomaticHeroUpdates.IsChecked = controller.Settings.AutomaticallyUpdateHeroes; ready = true;
            HeroDataStateText.Text = error; return;
        }
        if (updateChecksStarted && controller.Settings.AutomaticallyUpdateHeroes) _ = CheckHeroDataAsync();
    }
}
