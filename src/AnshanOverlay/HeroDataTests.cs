using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

internal static class HeroDataTests
{
    private static string Cards(IEnumerable<Hero> heroes) => string.Join("\n", heroes.Select(h => $"<a class=\"hero-card\" id=\"{h.Id}\" data-role=\"{h.Role}\"><blz-image src=\"{WebUtility.HtmlEncode(h.SourceUrl)}\"></blz-image><h2>{WebUtility.HtmlEncode(h.EnglishName)}</h2></a>"));
    private static string Names(IEnumerable<Hero> heroes) => JsonSerializer.Serialize(new { heroConfigs = heroes.Select(h => new { id = h.Id == "jetpack-cat" ? "jetpackcat" : h.Id, name = h.Name }) });
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request, cancellationToken);
    }
    private static HttpClient Client(string cards, string names, byte[] portrait, Action<string>? requested = null) => new(new Handler((request, token) =>
    {
        token.ThrowIfCancellationRequested(); string url = request.RequestUri!.AbsoluteUri; requested?.Invoke(url);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = url == HeroDataUpdates.GlobalSource ? new StringContent(cards) : url == HeroDataUpdates.MainlandSource ? new StringContent(names) : new ByteArrayContent(portrait) });
    }));
    private static async Task<bool> Rejected(Func<Task> run)
    {
        try { await run(); return false; }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or OperationCanceledException or IOException or UnauthorizedAccessException or HttpRequestException) { return true; }
    }
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        string folder = Path.Combine(output, "hero-data"); Directory.CreateDirectory(folder);
        string cache = Path.Combine(folder, "hero-data.zip");
        if (File.Exists(cache)) File.Delete(cache);
        var builtIn = HeroDataSnapshot.BuiltIn;
        var heroes = builtIn.Manifest.Heroes;
        var service = new HeroDataUpdates(cachePath: cache);
        check(ReferenceEquals(service.LoadCache(out var missingWarning), builtIn) && missingWarning is null, "missing hero cache uses all embedded heroes offline");
        var requests = new List<string>();
        using var unchangedClient = Client(Cards(heroes), Names(heroes), Array.Empty<byte>(), requests.Add);
        var unchangedService = new HeroDataUpdates(unchangedClient, cache);
        var unchanged = await unchangedService.UpdateAsync(builtIn, null, CancellationToken.None);
        check(!unchanged.HasChanges && ReferenceEquals(unchanged.Data, builtIn) && requests.Count == 2 && !File.Exists(cache), "unchanged official data does not redownload portraits or rewrite the cache");

        var renamed = heroes.Select(h => h.Id == "ana" ? h with { Name = "安娜（更新）", Role = "tank" } : h).ToList();
        requests.Clear();
        using var renamedClient = Client(Cards(renamed), Names(renamed), Array.Empty<byte>(), requests.Add);
        var metadata = await new HeroDataUpdates(renamedClient, cache).UpdateAsync(builtIn, null, CancellationToken.None);
        check(metadata.Added == 0 && metadata.Updated == 1 && requests.Count == 2 && metadata.Data.Manifest.Heroes.Single(h => h.Id == "ana").Aliases == heroes.Single(h => h.Id == "ana").Aliases, "name and role changes update independently while preserving aliases and reusing portraits");
        var reloaded = service.LoadCache(out var warning);
        check(warning is null && reloaded.Manifest.Heroes.Single(h => h.Id == "ana").Name == "安娜（更新）" && reloaded.Manifest.Heroes.Single(h => h.Id == "ana").Role == "tank", "hero metadata changes survive offline cache reload");

        byte[] original = builtIn.Portraits["ana"];
        string source = "https://d15f34w2p8l1cc.cloudfront.net/overwatch/" + Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() + ".png";
        var future = new Hero("future-hero", "未来英雄", "Future Hero", "support", "", "future-hero.png", source);
        var expanded = renamed.Append(future).ToList();
        requests.Clear();
        using var expandedClient = Client(Cards(expanded), Names(expanded), original, requests.Add);
        var added = await new HeroDataUpdates(expandedClient, cache).UpdateAsync(metadata.Data, null, CancellationToken.None);
        check(added.Added == 1 && added.Updated == 0 && requests.Count == 3 && added.Data.Manifest.Count == heroes.Count + 1, "a newly announced hero needs only its new portrait and does not require a software version change");
        var cached = service.LoadCache(out warning);
        check(warning is null && cached.Manifest.Heroes.Any(h => h.Id == future.Id) && cached.Portraits[future.Id].SequenceEqual(added.Data.Portraits[future.Id]), "new hero and portrait survive restart through the independent cache");
        using (var imageStream = new MemoryStream(cached.Portraits[future.Id]))
        {
            var image = BitmapFrame.Create(imageStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            check(image.PixelWidth == 256 && image.PixelHeight == 256 && pixel[3] == 0, "downloaded portraits have the expected size and transparent circular corners");
        }
        using var removedClient = Client(Cards(renamed), Names(renamed), Array.Empty<byte>());
        var retained = await new HeroDataUpdates(removedClient, cache).UpdateAsync(cached, null, CancellationToken.None);
        check(retained.Data.Manifest.Heroes.Any(h => h.Id == future.Id), "temporary removal from an official list never removes IDs needed by saved scenes");
        var futureScene = new Scene { Items = new() { new SceneItem { HeroId = future.Id, X = 200, Y = 200 } } };
        check(SceneStore.Parse(JsonSerializer.Serialize(futureScene, Assets.Json), retained.Data.Manifest.Heroes).Items.Single().HeroId == future.Id, "saved scenes containing downloaded heroes remain loadable");

        var saved = File.ReadAllBytes(cache);
        foreach (var invalid in new[] {
            (Cards(heroes.Take(5)), Names(heroes.Take(5))),
            (Cards(heroes), Names(heroes.Skip(1))),
            (Cards(heroes.Append(heroes[0])), Names(heroes)),
            (Cards(heroes.Select(h => h.Id == "ana" ? h with { Role = "healer" } : h)), Names(heroes)),
            (Cards(heroes.Select(h => h.Id == "ana" ? h with { SourceUrl = "https://example.invalid/portrait.png" } : h)), Names(heroes)),
            (Cards(heroes), "{not json") })
        {
            using var invalidClient = Client(invalid.Item1, invalid.Item2, original);
            check(await Rejected(() => new HeroDataUpdates(invalidClient, cache).UpdateAsync(cached, null, CancellationToken.None)) && File.ReadAllBytes(cache).SequenceEqual(saved), "incomplete inconsistent duplicated or invalid official data preserves the last complete cache");
        }
        using (var corruptPortrait = Client(Cards(expanded), Names(expanded), Encoding.UTF8.GetBytes("not a PNG")))
            check(await Rejected(() => new HeroDataUpdates(corruptPortrait, cache).UpdateAsync(metadata.Data, null, CancellationToken.None)) && File.ReadAllBytes(cache).SequenceEqual(saved), "a failed portrait checksum leaves the previous cache intact");
        using (var cancelled = new CancellationTokenSource())
        using (var cancelledClient = Client(Cards(expanded), Names(expanded), original, url => { if (url == source) cancelled.Cancel(); }))
            check(await Rejected(() => new HeroDataUpdates(cancelledClient, cache).UpdateAsync(metadata.Data, null, cancelled.Token)) && File.ReadAllBytes(cache).SequenceEqual(saved), "cancelling during a portrait download preserves the existing cache");
        using (var oversized = new HttpClient(new Handler((request, token) =>
        {
            var content = new ByteArrayContent(Array.Empty<byte>()); content.Headers.ContentLength = 100 * 1024 * 1024;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        })))
            check(await Rejected(() => new HeroDataUpdates(oversized, cache).UpdateAsync(cached, null, CancellationToken.None)) && File.ReadAllBytes(cache).SequenceEqual(saved), "oversized downloads are rejected before reading and never replace working data");
        string unwritable = Path.Combine(folder, "directory-instead-of-file"); Directory.CreateDirectory(unwritable);
        using (var blockedClient = Client(Cards(expanded), Names(expanded), original))
            check(await Rejected(() => new HeroDataUpdates(blockedClient, unwritable).UpdateAsync(metadata.Data, null, CancellationToken.None)) && !File.Exists(unwritable + ".tmp"), "failed cache replacement cleans its staging file without applying a partial update");

        File.WriteAllText(cache, "broken ZIP");
        check(ReferenceEquals(service.LoadCache(out warning), builtIn) && warning is not null, "a corrupt cache falls back to usable embedded data with a warning");
        await unchangedService.UpdateAsync(builtIn, null, CancellationToken.None);
        check(service.LoadCache(out warning).Manifest.Count == heroes.Count && warning is null, "a successful official check repairs an unreadable cache even when no hero has changed");
        File.WriteAllBytes(cache, saved);
        using (var zip = ZipFile.Open(cache, ZipArchiveMode.Update))
        {
            zip.GetEntry("future-hero.png")!.Delete(); using var entry = zip.CreateEntry("future-hero.png").Open(); entry.Write(original);
        }
        check(ReferenceEquals(service.LoadCache(out warning), builtIn) && warning is not null, "a cached PNG with the wrong checksum cannot enter the active hero catalog");
        File.WriteAllBytes(cache, saved);
        using (var zip = ZipFile.Open(cache, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
            entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(json.Replace("\"id\":\"future-hero\"", "\"id\":null"));
        }
        check(ReferenceEquals(service.LoadCache(out warning), builtIn) && warning is not null, "null cached hero identifiers recover without a startup exception");
        File.WriteAllBytes(cache, saved);

        var controller = new Controller(test: true);
        try
        {
            controller.Store.Edit(_ => futureScene with { Items = new() { new SceneItem { HeroId = "ana", X = 200, Y = 200 } } });
            controller.Editor.SelectedId = controller.Store.Current.Items[0].Id;
            controller.Panel.BatchMode.IsChecked = true; controller.Panel.ToggleBatchHero(heroes.Single(h => h.Id == "ana"));
            string sceneBefore = controller.Store.Serialize(); string? selectionBefore = controller.Editor.SelectedId;
            await controller.Server.StartAsync(0); controller.Server.Publish(sceneBefore);
            using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{controller.Server.Port}/ws"), timeout.Token);
            async Task<string> Receive()
            {
                using var memory = new MemoryStream(); var bytes = new byte[16384]; WebSocketReceiveResult response;
                do { response = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), timeout.Token); memory.Write(bytes, 0, response.Count); } while (!response.EndOfMessage);
                return Encoding.UTF8.GetString(memory.ToArray());
            }
            _ = await Receive(); await controller.Panel.CheckHeroDataAsync(new HeroDataUpdates(expandedClient, cache));
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            var applied = controller.HeroData;
            check(controller.Panel.HeroDataStateText.Text.StartsWith("已更新：") && controller.Panel.CheckHeroDataButton.IsEnabled && controller.Panel.CancelHeroDataButton.Visibility == Visibility.Collapsed, "successful manual updates finish with a stable completion message and enabled controls");
            check(controller.Store.Serialize() == sceneBefore && controller.Editor.SelectedId == selectionBefore && controller.Store.CanUndo, "live hero updates preserve the current scene selection and undo history");
            check(controller.Panel.BatchHint.Text.Contains("安娜（更新）") && controller.Panel.AddHeroesButton.IsEnabled, "live hero metadata updates preserve pending batch choices and refresh their names");
            controller.Panel.SearchBox.Text = "未来英雄";
            check(controller.Panel.HeroList.Items.Cast<Hero>().Single().Id == future.Id && controller.Overlay.Board.Heroes.Any(h => h.Id == future.Id), "new heroes appear immediately in the searchable library and drawing catalog");
            check(Assets.Portrait(future.Id).IsFrozen && Assets.Portrait(future.Id).PixelWidth == 256, "live drawing uses the downloaded immutable portrait");
            using var state = JsonDocument.Parse(await Receive());
            check(state.RootElement.GetProperty("heroCatalog").GetProperty(future.Id).GetString() == "未来英雄" && state.RootElement.GetProperty("heroRevision").GetString()!.Length > 0 && state.RootElement.GetProperty("items")[0].GetProperty("heroId").GetString() == "ana", "OBS receives the updated catalog and portrait revision alongside the unchanged scene");
            using var http = new HttpClient(); string root = $"http://127.0.0.1:{controller.Server.Port}";
            check((await http.GetByteArrayAsync(root + "/heroes/future-hero.png")).SequenceEqual(added.Data.Portraits[future.Id]) && (await http.GetStringAsync(root + "/catalog")).Contains("future-hero"), "OBS serves new portraits and names from the same live catalog");
            controller.Server.Publish(sceneBefore);
            check((await Receive()).Contains("heroRevision"), "subsequent scene changes retain the updated OBS catalog revision");
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);

            using var offline = new HttpClient(new Handler((request, token) => throw new HttpRequestException("offline")));
            await controller.Panel.CheckHeroDataAsync(new HeroDataUpdates(offline, cache));
            check(controller.Panel.HeroDataStateText.Text.Contains("继续使用已有英雄数据") && controller.Panel.CheckHeroDataButton.IsEnabled && ReferenceEquals(controller.HeroData, applied) && controller.Store.Serialize() == sceneBefore, "an offline manual update explains the failure and keeps the active data and scene usable");
            controller.Settings.AutomaticallyCheckUpdates = false;
            controller.Panel.AutomaticHeroUpdates.IsChecked = false;
            check(!controller.Settings.AutomaticallyUpdateHeroes && !controller.Settings.AutomaticallyCheckUpdates && !JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(controller.Settings, Assets.Json), Assets.Json)!.AutomaticallyUpdateHeroes, "hero auto-update preference is saved independently of software updates");
            controller.Panel.AutomaticHeroUpdates.IsChecked = true;
            check(controller.Settings.AutomaticallyUpdateHeroes && !controller.Settings.AutomaticallyCheckUpdates && JsonSerializer.Deserialize<Preferences>("{}", Assets.Json)!.AutomaticallyUpdateHeroes, "enabling hero updates leaves software updates unchanged and old settings gain the startup default");
            controller.Store.Undo(); check(controller.Store.Current.Items.Count == 0, "hero data changes do not insert entries into scene undo history");
        }
        finally { controller.Close(); Assets.UseHeroData(builtIn); }
    }

    public static async Task RunLiveAsync(Action<bool, string> check, string output)
    {
        var builtIn = HeroDataSnapshot.BuiltIn;
        // Force one real portrait refresh to verify the official CDN and PNG pipeline too.
        var stale = builtIn with { Manifest = builtIn.Manifest with { Heroes = builtIn.Manifest.Heroes.Select(h => h.Id == "ana" ? h with { SourceUrl = "" } : h).ToList() } };
        string cache = Path.Combine(output, "official-live-hero-data.zip");
        var service = new HeroDataUpdates(cachePath: cache);
        var update = await service.UpdateAsync(stale, null, CancellationToken.None);
        check(update.Updated >= 1 && update.Data.Manifest.Count >= builtIn.Manifest.Count, "real official lists and portrait CDN complete an independent data update");
        var loaded = service.LoadCache(out var warning);
        check(warning is null && loaded.Manifest.Count == update.Data.Manifest.Count && loaded.Portraits["ana"].Length > 1000, "data downloaded from official sources can be reloaded offline");
        File.WriteAllText(Path.Combine(output, "official-hero-update-result.json"), JsonSerializer.Serialize(new { globalSource = HeroDataUpdates.GlobalSource, mainlandSource = HeroDataUpdates.MainlandSource, count = loaded.Manifest.Count, added = update.Added, updated = update.Updated, cached = warning is null, utc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
