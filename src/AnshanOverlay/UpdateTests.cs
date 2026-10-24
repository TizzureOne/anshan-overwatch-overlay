using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnshanOverlay;

internal static class UpdateTests
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        byte[] payload = Enumerable.Range(0, 90000).Select(n => (byte)(n % 251)).ToArray();
        string digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        string Release(string tag, bool beta = false, bool draft = false, bool lite = true, string? hash = null, string? url = null) => JsonSerializer.Serialize(new
        {
            tag_name = tag, prerelease = beta, draft, body = "修复与更新说明",
            assets = (lite ? new[] { "", "-Lite" } : new[] { "" }).Select(suffix => new
            {
                name = $"AnshanOverlay-{tag.TrimStart('v')}{suffix}-Setup-x64.exe", size = payload.Length,
                digest = "sha256:" + (hash ?? digest),
                browser_download_url = url ?? $"https://github.com/{AppUpdates.Repository}/releases/download/{tag}/AnshanOverlay-{tag.TrimStart('v')}{suffix}-Setup-x64.exe"
            }).ToArray()
        });
        string Mixed(params string[] releases) => "[" + string.Join(",", releases) + "]";
        string available = Mixed(Release("v0.1.9"), Release("v0.1.10"), Release("v0.2.0-beta.2", true), Release("v0.2.0-beta.10", true), Release("v9.0.0", false, true));
        check(AppUpdates.SelectRelease(available, "0.1.8", false, "Full")?.Version == "0.1.10", "stable updates compare numeric versions and exclude beta and draft releases");
        var beta = AppUpdates.SelectRelease(available, "0.1.8", true, "Lite");
        check(beta?.Version == "0.2.0-beta.10" && beta.Filename.Contains("-Lite-"), "beta updates compare prerelease numbers and preserve Lite edition");
        check(AppUpdates.SelectRelease(Mixed(Release("v0.2.0")), "0.2.0-beta.10", false, "Full")?.Version == "0.2.0", "final release upgrades a beta with the same numeric version");
        check(AppUpdates.SelectRelease(available, "1.0.0", true, "Full") is null, "updater never offers a downgrade");
        check(AppUpdates.SelectRelease(Mixed(Release("v0.1.4")), "0.1.4+build123", true, "Full") is null, "build metadata does not create a false update");
        check(AppUpdates.SelectRelease(Mixed(Release("v0.2.0", true)), "0.1.4", false, "Full") is null, "release prerelease flag also respects the stable channel");
        check(AppUpdates.SelectRelease(Mixed(Release("v0.2.0", lite: false)), "0.1.4", true, "Lite") is null, "Lite update does not silently switch to a Full installer");
        check(AppUpdates.SelectRelease(Mixed(Release("v0.2.0", hash: "missing")), "0.1.4", true, "Full") is null, "update with no valid SHA256 is not installable");
        check(AppUpdates.SelectRelease(Mixed(Release("v0.2.0", url: "https://example.invalid/installer.exe")), "0.1.4", true, "Full") is null, "installer download is restricted to this repository's exact release asset");
        check(ReleaseVersion.Parse("invalid") is null && ReleaseVersion.Parse("0.1.4-") is null, "invalid release tags are rejected");
        check(ReleaseVersion.Parse("1.0.0-alpha.1")!.CompareTo(ReleaseVersion.Parse("1.0.0-beta")) < 0, "prerelease identifiers follow semantic version precedence");
        string single = Mixed(Release("v0.2.0"));
        var release = AppUpdates.SelectRelease(single, "0.1.4-beta", false, "Full")!;
        using var apiClient = new HttpClient(new StubHandler((request, _) =>
        {
            check(request.RequestUri?.AbsoluteUri == $"https://api.github.com/repos/{AppUpdates.Repository}/releases?per_page=100" && request.Headers.UserAgent.Count > 0, "update check uses the real public release API with a user agent");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(single) });
        }));
        check((await new AppUpdates(apiClient).CheckAsync("0.1.4-beta", false, "Full"))?.Version == "0.2.0", "HTTP update check returns the selected release");
        using var downloadClient = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));
        var service = new AppUpdates(downloadClient);
        string directory = Path.Combine(output, "updates", Guid.NewGuid().ToString("N"));
        var progress = new List<int>();
        string downloaded = await service.DownloadAsync(release, Path.Combine(directory, "valid"), new ProgressSink(progress.Add), CancellationToken.None);
        check(File.ReadAllBytes(downloaded).SequenceEqual(payload) && progress.Last() == 100, "installer download reports progress and matches the expected bytes");
        bool rejected = false;
        string corrupt = Path.Combine(directory, "corrupt");
        try { await service.DownloadAsync(release with { Sha256 = new string('0', 64) }, corrupt, null, CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && Directory.GetFiles(corrupt).Length == 0, "checksum mismatch removes the partial installer and never makes an executable available");
        rejected = false; string truncated = Path.Combine(directory, "truncated");
        try { await service.DownloadAsync(release with { Size = release.Size + 1 }, truncated, null, CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && Directory.GetFiles(truncated).Length == 0, "incorrect installer length is rejected before installation");
        using var cancellation = new CancellationTokenSource();
        using var cancelledClient = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CancellingStream(payload, cancellation)) })));
        string cancelled = Path.Combine(directory, "cancelled"); rejected = false;
        try { await new AppUpdates(cancelledClient).DownloadAsync(release, cancelled, null, cancellation.Token); }
        catch (OperationCanceledException) { rejected = true; }
        check(rejected && Directory.GetFiles(cancelled).Length == 0, "cancelling an in-progress download removes partial files");
        using var failureClient = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        rejected = false;
        try { await new AppUpdates(failureClient).CheckAsync("0.1.4-beta", true, "Full"); }
        catch (HttpRequestException) { rejected = true; }
        check(rejected, "network failure is not mistaken for no available updates");
        await CheckInstallerHandoff(check, directory);
    }

    private static async Task CheckInstallerHandoff(Action<bool, string> check, string directory)
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "Fixture.cs"), fixture = Path.Combine(directory, "Fixture.exe");
        File.WriteAllText(source, """
using System;
using System.IO;
using System.Threading;
class Fixture {
    static int Main(string[] args) {
        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (args.Length > 0 && args[0] == "--hold") { Thread.Sleep(1500); File.WriteAllText(exe + ".finished", "done"); return 0; }
        File.WriteAllLines(exe + ".args", args);
        int code;
        return Path.GetFileName(exe) == "Setup.exe" && int.TryParse(Environment.GetEnvironmentVariable("ANSHAN_QA_SETUP_EXIT"), out code) ? code : 0;
    }
}
""");
        var compiler = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework/v4.0.30319/csc.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "/nologo", "/target:winexe", "/out:" + fixture, source }) compiler.ArgumentList.Add(argument);
        using (var compile = Process.Start(compiler)!) { await compile.WaitForExitAsync(); check(compile.ExitCode == 0, "isolated updater process fixtures compile"); }
        foreach (int exitCode in new[] { 0, 5, -1 })
        {
            string trial = Path.Combine(directory, "handoff " + exitCode + " 'quoted'"), appDirectory = Path.Combine(trial, "installed app");
            Directory.CreateDirectory(appDirectory);
            string installer = Path.Combine(trial, "Setup.exe"), app = Path.Combine(appDirectory, "AnshanOverlay.exe"), parent = Path.Combine(trial, "Parent.exe");
            File.Copy(fixture, installer); File.Copy(fixture, app); File.Copy(fixture, parent);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))).ToLowerInvariant();
            var release = new UpdateRelease("0.2.0-beta", "", new Uri(AppUpdates.ReleasesPage), "Setup.exe", new Uri(AppUpdates.ReleasesPage), new FileInfo(installer).Length, exitCode == -1 ? new string('0', 64) : hash);
            var parentStart = new ProcessStartInfo(parent) { UseShellExecute = false, CreateNoWindow = true }; parentStart.ArgumentList.Add("--hold");
            using var old = Process.Start(parentStart)!;
            var command = AppUpdates.InstallerCommand(installer, release, appDirectory + Path.DirectorySeparatorChar, old.Id);
            command.Environment["ANSHAN_QA_SETUP_EXIT"] = exitCode.ToString();
            check(command.CreateNoWindow && command.ArgumentList.Contains(appDirectory) && command.ArgumentList.Contains(installer), "updater handoff preserves paths with spaces and quotes");
            using var helper = Process.Start(command)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await helper.WaitForExitAsync(timeout.Token);
            for (int attempt = 0; attempt < 30 && !File.Exists(app + ".args"); attempt++) await Task.Delay(50);
            check(helper.ExitCode == 0 && File.Exists(parent + ".finished"), "updater waits for the old process to exit before continuing");
            check(File.ReadAllLines(app + ".args").SequenceEqual(new[] { "--update-result", exitCode.ToString(), "--update-target", release.Version }), "updater restarts the application with the actual installer result " + exitCode);
            if (exitCode != -1)
                check(File.ReadAllLines(installer + ".args").Any(a => a.StartsWith("/DIR=") && a[5..].Trim('"') == appDirectory), "installer receives the correct upgrade directory " + exitCode);
            else check(!File.Exists(installer + ".args"), "a changed cached installer is never executed");
            check(File.Exists(installer) == (exitCode != 0), "successful updates remove the downloaded installer while failed updates retain it " + exitCode);
        }
    }
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request, cancellationToken);
    }
    private sealed class ProgressSink(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var read = base.ReadAsync(buffer, cancellationToken); cancellation.Cancel(); return read; }
    }
}
