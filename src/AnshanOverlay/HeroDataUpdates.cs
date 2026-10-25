using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnshanOverlay;

public sealed record HeroDataSnapshot(HeroManifest Manifest, IReadOnlyDictionary<string, byte[]> Portraits)
{
    private static readonly Lazy<HeroDataSnapshot> embedded = new(() =>
    {
        var manifest = Assets.LoadHeroes();
        return new(manifest, manifest.Heroes.ToDictionary(h => h.Id, h => Assets.Read($"Assets/Heroes/{h.Id}.png")));
    });
    public static HeroDataSnapshot BuiltIn => embedded.Value;
}

public sealed record HeroDataUpdate(HeroDataSnapshot Data, int Added, int Updated)
{
    public bool HasChanges => Added + Updated > 0;
}

public sealed class HeroDataUpdates
{
    public const string GlobalSource = "https://overwatch.blizzard.com/en-us/heroes/";
    public const string MainlandSource = "https://ld5.res.netease.com/pc/zt/20241115153332/static/heroConfigs.json";
    public static string CachePath => Path.Combine(Path.GetDirectoryName(Preferences.FilePath)!, "hero-data.zip");
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
    private readonly HttpClient client;
    private readonly string cachePath;
    private const int MaxPortraitBytes = 8 * 1024 * 1024;
    private sealed record CacheFile(int Format, HeroManifest Manifest);
    public HeroDataUpdates(HttpClient? client = null, string? cachePath = null) { this.client = client ?? SharedClient; this.cachePath = cachePath ?? CachePath; }
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 64 } && Match(id, "^[a-z0-9]+(?:-[a-z0-9]+)*$").Success;
    private static string Attribute(string text, string name) => WebUtility.HtmlDecode(Match(text, "(?:^|\\s)" + name + "\\s*=\\s*\"([^\"]+)\"").Groups[1].Value);
    private static bool ValidPortraitUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "d15f34w2p8l1cc.cloudfront.net" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.AbsolutePath.StartsWith("/overwatch/", StringComparison.Ordinal) && url.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    private static bool ValidHero(Hero hero) => ValidId(hero.Id) && hero.Name is { Length: > 0 and <= 120 } && hero.EnglishName is { Length: > 0 and <= 120 } && hero.Aliases is { Length: <= 1024 } && hero.Role is "tank" or "damage" or "support" && hero.Image == hero.Id + ".png" && ValidPortraitUrl(hero.SourceUrl);

    internal static List<Hero> ParseOfficial(string html, string mainlandJson, HeroManifest current)
    {
        using var cn = JsonDocument.Parse(mainlandJson);
        var names = new Dictionary<string, string>();
        foreach (var entry in cn.RootElement.GetProperty("heroConfigs").EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            if (id == "jetpackcat") id = "jetpack-cat";
            string name = entry.GetProperty("name").GetString()!;
            if (id is null || !ValidId(id) || name is not { Length: > 0 and <= 120 } || !names.TryAdd(id, name)) throw new InvalidDataException("Invalid mainland hero list");
        }
        var known = current.Heroes.ToDictionary(h => h.Id);
        var heroes = new List<Hero>(); var ids = new HashSet<string>();
        foreach (Match card in Regex.Matches(html, "<a\\b(?=[^>]*\\bclass=\"[^\"]*\\bhero-card\\b)[^>]*>.*?</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(2)))
        {
            string id = Attribute(card.Value, "id"), role = Attribute(card.Value, "data-role");
            string english = WebUtility.HtmlDecode(Match(card.Value, "<h2[^>]*>([^<]+)</h2>").Groups[1].Value).Trim();
            string source = Attribute(Match(card.Value, "<blz-image\\b[^>]*>").Value, "src");
            if (!names.TryGetValue(id, out var name)) throw new InvalidDataException("Official hero lists do not agree");
            var hero = new Hero(id, name, english, role, known.TryGetValue(id, out var old) ? old.Aliases : "", id + ".png", source);
            if (!ValidHero(hero) || !ids.Add(id)) throw new InvalidDataException("Invalid global hero list");
            heroes.Add(hero);
        }
        if (heroes.Count < 40 || heroes.Count > 256 || !ids.SetEquals(names.Keys)) throw new InvalidDataException("Official hero lists are incomplete or inconsistent");
        return heroes;
    }

    public async Task<HeroDataUpdate> UpdateAsync(HeroDataSnapshot current, IProgress<string>? progress, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        progress?.Report("正在核对官方英雄名单…");
        var global = DownloadAsync(GlobalSource, 12 * 1024 * 1024, token);
        var mainland = DownloadAsync(MainlandSource, 2 * 1024 * 1024, token);
        await Task.WhenAll(global, mainland);
        var official = ParseOfficial(System.Text.Encoding.UTF8.GetString(global.Result), System.Text.Encoding.UTF8.GetString(mainland.Result), current.Manifest);
        var heroes = current.Manifest.Heroes.ToDictionary(h => h.Id);
        var portraits = current.Portraits.ToDictionary(pair => pair.Key, pair => pair.Value);
        int added = 0, updated = 0, processed = 0;
        foreach (var entry in official)
        {
            token.ThrowIfCancellationRequested(); processed++;
            heroes.TryGetValue(entry.Id, out var old);
            if (old is not null && old.Name == entry.Name && old.EnglishName == entry.EnglishName && old.Role == entry.Role && old.SourceUrl == entry.SourceUrl) continue;
            var next = entry;
            if (old is null || old.SourceUrl != entry.SourceUrl)
            {
                progress?.Report($"正在更新头像 · {entry.Name}（{processed}/{official.Count}）…");
                var original = await DownloadAsync(entry.SourceUrl, MaxPortraitBytes, token);
                string filename = Path.GetFileNameWithoutExtension(new Uri(entry.SourceUrl).AbsolutePath);
                if (Match(filename, "^[0-9a-fA-F]{64}$").Success && !Convert.ToHexString(SHA256.HashData(original)).Equals(filename, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Official portrait checksum mismatch");
                portraits[entry.Id] = CirclePortrait(original);
                next = next with { Sha256 = Convert.ToHexString(SHA256.HashData(portraits[entry.Id])).ToLowerInvariant() };
            }
            else next = next with { Sha256 = old.Sha256 };
            heroes[entry.Id] = next;
            if (old is null) added++; else updated++;
        }
        if (added + updated == 0)
        {
            if (File.Exists(cachePath)) { _ = LoadCache(out var warning); if (warning is not null) SaveCache(current, token); }
            return new(current, 0, 0);
        }
        // Keep previously known IDs so saved scenes and undo history remain valid.
        var ordered = official.Select(h => heroes[h.Id]).Concat(current.Manifest.Heroes.Where(h => !official.Any(o => o.Id == h.Id))).ToList();
        if (ordered.Count > 256) throw new InvalidDataException("Hero list exceeds the supported size");
        var result = new HeroDataSnapshot(new HeroManifest(GlobalSource, DateTime.UtcNow, ordered.Count, ordered), portraits);
        progress?.Report("正在保存英雄资料…");
        SaveCache(result, token);
        return new(result, added, updated);
    }

    private async Task<byte[]> DownloadAsync(string url, int limit, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("AnshanOverlay/" + BuildInfo.Version);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Hero data response is too large");
        using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[32768];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + read > limit) throw new InvalidDataException("Hero data response is too large"); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        return output.ToArray();
    }

    private static void ValidatePng(byte[] bytes, bool cached)
    {
        if (bytes.Length < 24 || bytes.Length > MaxPortraitBytes || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Invalid hero portrait");
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (cached ? width != 256 || height != 256 : width < 16 || height < 16 || width > 4096 || height > 4096) throw new InvalidDataException("Invalid portrait dimensions");
    }
    internal static byte[] CirclePortrait(byte[] bytes)
    {
        ValidatePng(bytes, false);
        using var stream = new MemoryStream(bytes);
        var source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        double scale = 512d / Math.Min(source.PixelWidth, source.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new EllipseGeometry(new Point(256, 256), 255, 255));
            dc.DrawImage(source, new Rect((512 - source.PixelWidth * scale) / 2, (512 - source.PixelHeight * scale) / 2, source.PixelWidth * scale, source.PixelHeight * scale));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(.5, .5))));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }

    private void SaveCache(HeroDataSnapshot data, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        string temporary = cachePath + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var manifest = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(manifest, new CacheFile(1, data.Manifest), Assets.Json);
                foreach (var hero in data.Manifest.Heroes)
                { token.ThrowIfCancellationRequested(); using var image = zip.CreateEntry(hero.Image, CompressionLevel.NoCompression).Open(); image.Write(data.Portraits[hero.Id]); }
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, cachePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public HeroDataSnapshot LoadCache(out string? warning)
    {
        warning = null; var builtIn = HeroDataSnapshot.BuiltIn;
        if (!File.Exists(cachePath)) return builtIn;
        try
        {
            if (new FileInfo(cachePath).Length > 64 * 1024 * 1024) throw new InvalidDataException("Hero cache is too large");
            using var zip = ZipFile.OpenRead(cachePath);
            var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing hero manifest");
            if (entry.Length > 1024 * 1024) throw new InvalidDataException("Hero manifest is too large");
            using var stream = entry.Open(); var cached = JsonSerializer.Deserialize<CacheFile>(stream, Assets.Json);
            if (cached is not { Format: 1, Manifest: { Heroes: not null } } || cached.Manifest.Count != cached.Manifest.Heroes.Count || cached.Manifest.Count < 40 || cached.Manifest.Count > 256 || cached.Manifest.Heroes.Any(h => h is null || !ValidHero(h)) || cached.Manifest.Heroes.Select(h => h.Id).Distinct().Count() != cached.Manifest.Count || zip.Entries.Count != cached.Manifest.Count + 1 || zip.Entries.Sum(e => e.Length) > 64 * 1024 * 1024) throw new InvalidDataException("Invalid hero manifest");
            var portraits = new Dictionary<string, byte[]>();
            foreach (var hero in cached.Manifest.Heroes)
            {
                var portrait = zip.GetEntry(hero.Image) ?? throw new InvalidDataException("Missing hero portrait");
                if (portrait.Length > MaxPortraitBytes) throw new InvalidDataException("Hero portrait is too large");
                using var input = portrait.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); var png = bytes.ToArray(); ValidatePng(png, true);
                if (!Convert.ToHexString(SHA256.HashData(png)).Equals(hero.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Hero cache checksum mismatch");
                using var imageStream = new MemoryStream(png); _ = BitmapFrame.Create(imageStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                portraits.Add(hero.Id, png);
            }
            var heroes = builtIn.Manifest.Heroes.ToDictionary(h => h.Id);
            var mergedImages = builtIn.Portraits.ToDictionary(p => p.Key, p => p.Value);
            foreach (var hero in cached.Manifest.Heroes)
                if (!heroes.ContainsKey(hero.Id) || cached.Manifest.RetrievedAt >= builtIn.Manifest.RetrievedAt) { heroes[hero.Id] = hero; mergedImages[hero.Id] = portraits[hero.Id]; }
            return new(new HeroManifest(GlobalSource, new[] { cached.Manifest.RetrievedAt, builtIn.Manifest.RetrievedAt }.Max(), heroes.Count, heroes.Values.ToList()), mergedImages);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or ArgumentException or FileFormatException)
        { warning = "本地英雄数据无法读取，已使用内置资料；可重新检查更新。"; return builtIn; }
    }
}
