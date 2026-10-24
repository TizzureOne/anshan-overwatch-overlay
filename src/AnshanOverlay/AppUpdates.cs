using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AnshanOverlay;

public sealed record UpdateRelease(string Version, string Notes, Uri Page, string Filename, Uri Download, long Size, string Sha256);

public sealed record ReleaseVersion(Version Core, string[] Prerelease) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion? Parse(string text)
    {
        text = text.TrimStart('v').Split('+')[0];
        var parts = text.Split('-', 2);
        if (parts[0].Split('.').Length != 3 || !Version.TryParse(parts[0], out var core)) return null;
        var suffix = parts.Length == 2 ? parts[1].Split('.') : Array.Empty<string>();
        if (suffix.Any(s => !Regex.IsMatch(s, "^[0-9A-Za-z-]+$"))) return null;
        return new(core, suffix);
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int result = Core.CompareTo(other.Core);
        if (result != 0) return result;
        if (Prerelease.Length == 0 || other.Prerelease.Length == 0) return (Prerelease.Length == 0 ? 1 : 0).CompareTo(other.Prerelease.Length == 0 ? 1 : 0);
        for (int n = 0; n < Math.Min(Prerelease.Length, other.Prerelease.Length); n++)
        {
            string left = Prerelease[n], right = other.Prerelease[n];
            bool ln = left.All(char.IsAsciiDigit), rn = right.All(char.IsAsciiDigit);
            if (ln && rn) { result = left.Length.CompareTo(right.Length); if (result == 0) result = string.CompareOrdinal(left, right); }
            else if (ln != rn) result = ln ? -1 : 1;
            else result = string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }
}

public sealed class AppUpdates
{
    public const string Repository = "TizzureOne/anshan-overwatch-overlay";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient client;
    public AppUpdates(HttpClient? client = null) => this.client = client ?? SharedClient;

    public static UpdateRelease? SelectRelease(string json, string currentVersion, bool includeBeta, string edition)
    {
        var current = ReleaseVersion.Parse(currentVersion) ?? throw new InvalidDataException("Invalid current version");
        UpdateRelease? chosen = null; ReleaseVersion? highest = null;
        using var document = JsonDocument.Parse(json);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString()!;
            var version = ReleaseVersion.Parse(tag);
            if (version is null || (!includeBeta && (release.GetProperty("prerelease").GetBoolean() || version.Prerelease.Length > 0)) || version.CompareTo(current) <= 0 || highest is not null && version.CompareTo(highest) <= 0) continue;
            string number = tag.TrimStart('v');
            string filename = $"AnshanOverlay-{number}{(edition == "Lite" ? "-Lite" : "")}-Setup-x64.exe";
            var expected = new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(filename)}");
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != filename || !Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) || url != expected) continue;
                if (!asset.TryGetProperty("digest", out var digest) || digest.GetString() is not { } hash || !Regex.IsMatch(hash, "^sha256:[0-9a-fA-F]{64}$")) continue;
                long size = asset.GetProperty("size").GetInt64(); if (size <= 0) continue;
                chosen = new(number, release.GetProperty("body").GetString() ?? "", new Uri($"{ReleasesPage}/tag/{Uri.EscapeDataString(tag)}"), filename, url, size, hash[7..].ToLowerInvariant());
                highest = version; break;
            }
        }
        return chosen;
    }

    public async Task<UpdateRelease?> CheckAsync(string currentVersion, bool includeBeta, string edition, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("AnshanOverlay/" + BuildInfo.Version);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
        return SelectRelease(await response.Content.ReadAsStringAsync(timeout.Token), currentVersion, includeBeta, edition);
    }

    public async Task<string> DownloadAsync(UpdateRelease release, string directory, IProgress<int>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, release.Filename + ".download"), destination = Path.Combine(directory, release.Filename);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Download); request.Headers.UserAgent.ParseAdd("AnshanOverlay/" + BuildInfo.Version);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidDataException("Installer length mismatch");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[65536]; long received = 0; int lastPercent = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) != 0)
                {
                    received += read; if (received > release.Size) throw new InvalidDataException("Installer exceeds expected size");
                    hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    int percent = (int)(received * 100 / release.Size);
                    if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
                }
                if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer checksum mismatch");
            }
            cancellation.ThrowIfCancellationRequested(); File.Move(partial, destination, true); return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static ProcessStartInfo InstallerCommand(string installer, UpdateRelease release, string installDirectory, int parentId)
    {
        string script = Path.Combine(Path.GetDirectoryName(installer)!, "install-update.ps1");
        File.WriteAllText(script, InstallerScript, new UTF8Encoding(true));
        var command = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Installer", installer, "-InstallDirectory", Path.TrimEndingDirectorySeparator(installDirectory), "-ParentId", parentId.ToString(), "-TargetVersion", release.Version, "-Sha256", release.Sha256 }) command.ArgumentList.Add(argument);
        return command;
    }
    public const string InstallerScript = """
param([string]$Installer, [string]$InstallDirectory, [int]$ParentId, [string]$TargetVersion, [string]$Sha256)
$ErrorActionPreference = 'Stop'
$log = Join-Path ([IO.Path]::GetDirectoryName($Installer)) 'install.log'
$exitCode = -1
try {
    $parent = Get-Process -Id $ParentId -ErrorAction SilentlyContinue
    if ($parent) { $parent.WaitForExit() }
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Installer)
    try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
    if ($actualHash -ine $Sha256) { throw 'Installer checksum mismatch' }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/LANG=chinesesimplified', ('/DIR="' + $InstallDirectory + '"'), ('/LOG="' + $log + '"'))
    $setup = Start-Process -FilePath $Installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    $exitCode = $setup.ExitCode
} catch { $_.Exception.Message | Add-Content -LiteralPath $log -Encoding UTF8 }
$exe = Join-Path $InstallDirectory 'AnshanOverlay.exe'
if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -WorkingDirectory $InstallDirectory -ArgumentList @('--update-result', $exitCode.ToString(), '--update-target', $TargetVersion)
}
if ($exitCode -eq 0) {
    Remove-Item -LiteralPath $Installer -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $PSCommandPath -ErrorAction SilentlyContinue
}
""";
}
