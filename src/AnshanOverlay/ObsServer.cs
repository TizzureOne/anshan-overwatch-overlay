using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AnshanOverlay;

public sealed class ObsServer : IAsyncDisposable
{
    private WebApplication? app;
    private string latest = "{}";
    private readonly ConcurrentDictionary<Guid, Channel<string>> clients = new();
    private readonly object snapshotLock = new();
    private HeroManifest heroes;
    private string heroRevision = Guid.NewGuid().ToString("N");
    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/overlay";
    public int ClientCount => clients.Count;
    public ObsServer(HeroManifest heroes) => this.heroes = heroes;
    public async Task StartAsync(int preferredPort = 17865)
    {
        for (int offset = 0; offset < 10; offset++)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            int port = preferredPort == 0 ? 0 : preferredPort + offset;
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));
            var candidate = builder.Build();
            candidate.Use(async (context, next) =>
            {
                // Only the local OBS page may connect; no external sites or LAN clients.
                string origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && origin != $"http://127.0.0.1:{Port}") { context.Response.StatusCode = 403; return; }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; connect-src 'self' ws://127.0.0.1:*";
                await next(context);
            });
            candidate.UseWebSockets();
            candidate.MapGet("/overlay", () => Results.Bytes(Assets.Read("Web/overlay.html"), "text/html; charset=utf-8"));
            candidate.MapGet("/overlay.js", () => Results.Bytes(Assets.Read("Web/overlay.js"), "text/javascript; charset=utf-8"));
            candidate.MapGet("/heroes/{id}.png", (string id) => heroes.Heroes.Any(h => h.Id == id)
                ? Results.Bytes(Assets.ReadPortrait(id), "image/png") : Results.NotFound());
            candidate.MapGet("/state", () => Results.Text(Volatile.Read(ref latest), "application/json"));
            candidate.MapGet("/health", () => Results.Json(new { status = "ok", heroes = heroes.Count }));
            candidate.MapGet("/catalog", () => Results.Json(heroes.Heroes.ToDictionary(h => h.Id, h => h.Name)));
            candidate.Map("/ws", Stream);
            try
            {
                await candidate.StartAsync(); app = candidate;
                Port = new Uri(candidate.Urls.Single()).Port; return;
            }
            catch (IOException) when (preferredPort != 0 && offset < 9) { await candidate.DisposeAsync(); }
            catch { await candidate.DisposeAsync(); throw; }
        }
    }
    public void Publish(string json)
    {
        lock (snapshotLock)
        {
            var state = JsonNode.Parse(json)!.AsObject();
            state["heroRevision"] = heroRevision;
            state["heroCatalog"] = JsonSerializer.SerializeToNode(heroes.Heroes.ToDictionary(h => h.Id, h => h.Name));
            json = state.ToJsonString();
            Volatile.Write(ref latest, json);
            foreach (var channel in clients.Values) channel.Writer.TryWrite(json);
        }
    }
    public void UpdateHeroes(HeroManifest updated)
    {
        lock (snapshotLock)
        {
            Volatile.Write(ref heroes, updated); heroRevision = Guid.NewGuid().ToString("N");
            Publish(latest);
        }
    }
    private async Task Stream(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        lock (snapshotLock)
        {
            clients[id] = channel;
            channel.Writer.TryWrite(Volatile.Read(ref latest));
        }
        async Task Receive()
        {
            var buffer = new byte[1024];
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    var message = await socket.ReceiveAsync(buffer.AsMemory(), cancel.Token);
                    if (message.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
            finally { cancel.Cancel(); }
        }
        var receive = Receive();
        try
        {
            await foreach (var json in channel.Reader.ReadAllAsync(cancel.Token))
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancel.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        finally
        {
            clients.TryRemove(id, out _); cancel.Cancel(); await receive;
            if (socket.State == WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (app is null) return;
        foreach (var channel in clients.Values) channel.Writer.TryComplete();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await app.StopAsync(timeout.Token); await app.DisposeAsync();
    }
}
