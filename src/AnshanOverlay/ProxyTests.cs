using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

internal static class ProxyTests
{
    public static async Task RunAsync(Action<bool, string> verify, string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { verify(condition, name); passed.Add(name); }
        foreach (string address in new[] { "http://127.0.0.1:7890", "https://proxy.example:443", "socks5://localhost:1080", "http://[::1]:7890", "  http://127.0.0.1:7890  " })
            Check(NetworkProxy.TryParseAddress(address, out _), "accept proxy address " + address.Trim());
        foreach (string address in new[] { "", "127.0.0.1:7890", "ftp://host:123", "socks5://host", "http://host:0", "http://host:65536", "http://user:secret@host:80", "http://host:80/path", "http://host:80?token=x", "http://host:80#x", "http://ho st:80", "http://host\\path:80" })
            Check(!NetworkProxy.TryParseAddress(address, out _), "reject invalid proxy address " + address);
        var defaults = new Preferences();
        using (var system = NetworkProxy.CreateHandler(defaults))
            Check(defaults.ProxyMode == NetworkProxyMode.System && system.UseProxy && system.Proxy is null, "old preferences keep the system proxy behavior");
        using (var direct = NetworkProxy.CreateHandler(new() { ProxyMode = NetworkProxyMode.Direct, ProxyAddress = "invalid" }))
            Check(!direct.UseProxy, "direct mode ignores stored manual address");
        using (var manual = NetworkProxy.CreateHandler(new() { ProxyMode = NetworkProxyMode.Manual, ProxyAddress = "socks5://127.0.0.1:1080" }, false))
            Check(manual.UseProxy && manual.Proxy!.GetProxy(new Uri("https://example.org"))!.Scheme == "socks5" && !manual.AllowAutoRedirect && manual.ServerCertificateCustomValidationCallback is null, "manual proxy preserves hero redirect restrictions and normal TLS verification");

        string settingsFile = Path.Combine(output, "proxy-settings.json");
        File.WriteAllText(settingsFile, "{\"panelWidth\":555}");
        var loaded = Preferences.Load(out var warning, settingsFile);
        Check(loaded.ProxyMode == NetworkProxyMode.System && loaded.PanelWidth == 555 && warning is null, "old settings migrate without losing window preferences");
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(new Preferences { ProxyMode = NetworkProxyMode.Manual, ProxyAddress = "not a proxy", PanelWidth = 555 }, Assets.Json));
        loaded = Preferences.Load(out warning, settingsFile);
        Check(loaded.ProxyMode == NetworkProxyMode.System && loaded.PanelWidth == 555 && warning is not null, "corrupt proxy setting recovers with a warning and preserves other preferences");

        var controller = new Controller(test: true); var panel = controller.Panel;
        try
        {
            string scene = controller.Store.Serialize();
            Check(panel.ProxyModeChoice.SelectedIndex == 0 && !panel.ProxyAddressBox.IsEnabled, "proxy UI starts with system mode and disabled manual field");
            panel.ProxyModeChoice.SelectedIndex = 2;
            Check(panel.ProxyAddressBox.IsEnabled && controller.Settings.ProxyMode == NetworkProxyMode.System, "draft mode changes do not affect network settings before saving");
            panel.ProxyAddressBox.Text = "wrong"; panel.SaveProxyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.Settings.ProxyMode == NetworkProxyMode.System && panel.ProxyStateText.Text.Contains("请输入"), "invalid address is rejected visibly without replacing saved settings");
            panel.ProxyAddressBox.Text = "http://127.0.0.1:7890"; panel.SaveProxyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.Settings.ProxyMode == NetworkProxyMode.Manual && controller.Settings.ProxyAddress == "http://127.0.0.1:7890/" && panel.ProxyStateText.Text.Contains("已保存"), "save button applies and normalizes the manual proxy");
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(controller.Settings, Assets.Json));
            loaded = Preferences.Load(out warning, settingsFile);
            Check(loaded.ProxyMode == NetworkProxyMode.Manual && loaded.ProxyAddress == controller.Settings.ProxyAddress && warning is null, "manual proxy survives saving and reloading preferences");
            panel.ProxyModeChoice.SelectedIndex = 1; panel.SaveProxyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.Settings.ProxyMode == NetworkProxyMode.Direct && !panel.ProxyAddressBox.IsEnabled && controller.Settings.ProxyAddress.Contains(":7890"), "disabling proxy retains the last valid address for reuse");
            Check(controller.Store.Serialize() == scene && !controller.Store.CanUndo, "proxy editing does not modify scene or undo history");

            await using var proxy = new LocalProxy();
            controller.ApplyProxyPreferences(NetworkProxyMode.Manual, proxy.Address);
            await panel.CheckForUpdatesAsync();
            Check(proxy.Requests.Any(r => r == "CONNECT api.github.com:443 HTTP/1.1") && panel.UpdateStateText.Text.Contains("代理设置"), "real update UI sends HTTPS CONNECT through manual HTTP proxy and reports failure");
            var beforeHeroes = controller.HeroData;
            await panel.CheckHeroDataAsync();
            Check(proxy.Requests.Any(r => r.Contains("overwatch.blizzard.com:443")) && proxy.Requests.Any(r => r.Contains("ld5.res.netease.com:443")), "both official hero sources use the configured HTTP proxy");
            Check(ReferenceEquals(controller.HeroData, beforeHeroes) && panel.HeroDataStateText.Text.Contains("继续使用已有英雄数据"), "proxy failure keeps existing hero data usable");

            using (var client = NetworkProxy.CreateClient(controller.Settings))
            {
                var release = new UpdateRelease("0.2.0", "", new Uri(AppUpdates.ReleasesPage), "proxy-fixture.bin", new Uri("http://download.invalid/installer"), LocalProxy.Payload.Length, Convert.ToHexString(SHA256.HashData(LocalProxy.Payload)));
                string file = await new AppUpdates(client).DownloadAsync(release, Path.Combine(output, "proxy-download"), null, CancellationToken.None);
                Check(File.ReadAllBytes(file).SequenceEqual(LocalProxy.Payload) && proxy.Requests.Any(r => r == "GET http://download.invalid/installer HTTP/1.1"), "installer bytes travel through the configured proxy and pass SHA256 verification");
                bool failed = false;
                try { await new AppUpdates(client).DownloadAsync(release with { Download = new Uri("https://github.com/installer") }, Path.Combine(output, "proxy-failed-download"), null, CancellationToken.None); }
                catch (HttpRequestException) { failed = true; }
                Check(failed && proxy.Requests.Any(r => r.Contains("github.com:443")) && !Directory.EnumerateFiles(Path.Combine(output, "proxy-failed-download")).Any(), "HTTPS installer uses the same proxy and leaves no partial file on failure");
                using var response = await client.GetAsync("http://download.invalid/redirect");
                Check(response.IsSuccessStatusCode && proxy.Requests.Any(r => r.Contains("/redirected")), "installer redirects continue through the manual proxy");
            }
            using (var client = NetworkProxy.CreateClient(controller.Settings, heroData: true))
            using (var response = await client.GetAsync("http://download.invalid/redirect"))
                Check(response.StatusCode == HttpStatusCode.Found, "hero requests still reject automatic redirects");

            int previousRequests = proxy.Requests.Count;
            await using var socks = new LocalProxy(socks: true);
            controller.ApplyProxyPreferences(NetworkProxyMode.Manual, socks.Address);
            await panel.CheckForUpdatesAsync();
            Check(socks.Requests.Contains("SOCKS5 api.github.com:443") && proxy.Requests.Count == previousRequests, "saving a SOCKS5 proxy reroutes the next update check without restarting");
            controller.ApplyProxyPreferences(NetworkProxyMode.Direct, "");
            using (var client = NetworkProxy.CreateClient(controller.Settings))
                Check((await client.GetByteArrayAsync(proxy.Address + "/direct")).SequenceEqual(LocalProxy.Payload) && proxy.Requests.Last() == "GET /direct HTTP/1.1", "switching to direct mode actually bypasses the stored SOCKS proxy");

            controller.ApplyProxyPreferences(NetworkProxyMode.Manual, "http://127.0.0.1:7890");
            panel.ProxyModeChoice.SelectedIndex = 2; panel.ProxyAddressBox.Text = controller.Settings.ProxyAddress;
            panel.SaveProxyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            controller.Management.Pages.SelectedItem = panel.UpdatesTab;
            var content = (FrameworkElement)controller.Management.Content; controller.Management.Content = null;
            foreach (int width in new[] { 360, 500 })
            {
                Render(content, width, 760, Path.Combine(output, "proxy-settings-" + width + ".png"));
                var settingsScroll = (ScrollViewer)panel.UpdatesTab.Content;
                settingsScroll.ScrollToVerticalOffset(panel.ProxySettingsPanel.TranslatePoint(new Point(), (FrameworkElement)settingsScroll.Content).Y);
                Render(content, width, 760, Path.Combine(output, "proxy-settings-" + width + ".png"));
                foreach (FrameworkElement field in new FrameworkElement[] { panel.ProxyModeChoice, panel.ProxyAddressBox, panel.SaveProxyButton, panel.ProxyStateText })
                {
                    var bounds = field.TransformToAncestor(content).TransformBounds(new Rect(field.RenderSize));
                    Check(field.ActualWidth > 0 && bounds.Left >= 0 && bounds.Right <= width && bounds.Bottom <= 760, "proxy control fits settings panel at " + width + ": " + field.Name);
                }
            }
            controller.Management.Content = content;
        }
        finally { controller.Close(); }
        File.WriteAllText(Path.Combine(output, "proxy-checks.json"), JsonSerializer.Serialize(passed, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Render(FrameworkElement content, int width, int height, string path)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
    }

    private sealed class LocalProxy : IAsyncDisposable
    {
        internal static readonly byte[] Payload = Encoding.UTF8.GetBytes("isolated proxy download fixture");
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        private readonly Task serving;
        internal ConcurrentQueue<string> Requests { get; } = new();
        internal string Address { get; }
        internal LocalProxy(bool socks = false)
        {
            listener.Start(); Address = $"{(socks ? "socks5" : "http")}://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            serving = ServeAsync(socks);
        }
        private async Task ServeAsync(bool socks)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    using var socket = await listener.AcceptTcpClientAsync(cancellation.Token);
                    using var stream = socket.GetStream(); var token = cancellation.Token;
                    if (socks)
                    {
                        byte[] header = new byte[2]; await stream.ReadExactlyAsync(header, token);
                        byte[] methods = new byte[header[1]]; await stream.ReadExactlyAsync(methods, token);
                        await stream.WriteAsync(new byte[] { 5, 0 }, token);
                        byte[] command = new byte[4]; await stream.ReadExactlyAsync(command, token);
                        byte[] length = new byte[1]; await stream.ReadExactlyAsync(length, token);
                        byte[] host = new byte[length[0]]; await stream.ReadExactlyAsync(host, token);
                        byte[] port = new byte[2]; await stream.ReadExactlyAsync(port, token);
                        Requests.Enqueue($"SOCKS5 {Encoding.UTF8.GetString(host)}:{port[0] * 256 + port[1]}");
                        await stream.WriteAsync(new byte[] { 5, 5, 0, 1, 0, 0, 0, 0, 0, 0 }, token);
                    }
                    else
                    {
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                        string first = (await reader.ReadLineAsync(token))!;
                        while (await reader.ReadLineAsync(token) is { Length: > 0 }) { }
                        Requests.Enqueue(first);
                        string headers = first.StartsWith("CONNECT ") ? "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0" :
                            first.Contains("/redirect ") ? "HTTP/1.1 302 Found\r\nLocation: http://download.invalid/redirected\r\nContent-Length: 0" :
                            "HTTP/1.1 200 OK\r\nContent-Length: " + Payload.Length;
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\nConnection: close\r\n\r\n"), token);
                        if (headers.Contains("200 OK")) await stream.WriteAsync(Payload, token);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (SocketException) when (cancellation.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync() { cancellation.Cancel(); listener.Stop(); try { await serving; } finally { cancellation.Dispose(); } }
    }
}
