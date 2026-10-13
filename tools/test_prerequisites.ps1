$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src/AnshanOverlay/AnshanOverlay.csproj') -Raw)).Project.PropertyGroup.Version
$installer = Join-Path $projectRoot "dist/AnshanOverlay-$version-Lite-Setup-x64.exe"
$qa = Join-Path $projectRoot "artifacts/qa/prerequisites-$version"
$registryKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{B67B9F75-6EC5-4BBA-87B8-A9F722DF7281}_is1'
if (Test-Path -LiteralPath $registryKey) { throw 'An existing installation is registered. Do not replace it with a verification install.' }
New-Item -ItemType Directory -Path $qa -Force | Out-Null

# Isolated native-architecture hosts simulate runtime inventories without changing installed .NET.
$source = Join-Path $qa 'RuntimeInventory.cs'
@'
using System;
using System.IO;
class RuntimeInventory {
    static void Main() {
        Console.Write(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes.txt")));
    }
}
'@ | Set-Content -LiteralPath $source -Encoding utf8
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
foreach ($architecture in @('x64', 'x86')) {
    & $compiler /nologo "/platform:$architecture" "/out:$(Join-Path $qa "$architecture.exe")" $source
    if ($LASTEXITCODE -ne 0) { throw 'Runtime inventory fixture compilation failed.' }
}
$core = 'Microsoft.NETCore.App 8.0.1 [fixture]'
$desktop = 'Microsoft.WindowsDesktop.App 8.0.22 [fixture]'
$aspnet = 'Microsoft.AspNetCore.App 8.0.1 [fixture]'
$cases = @(
    @{ name = 'no-host'; arch = ''; lines = @(); desktopMissing = $true; aspnetMissing = $true },
    @{ name = 'only-net10'; arch = 'x64'; lines = @('Microsoft.NETCore.App 10.0.11 [fixture]', 'Microsoft.WindowsDesktop.App 10.0.11 [fixture]', 'Microsoft.AspNetCore.App 10.0.11 [fixture]'); desktopMissing = $true; aspnetMissing = $true },
    @{ name = 'desktop-only'; arch = 'x64'; lines = @($core, $desktop); desktopMissing = $false; aspnetMissing = $true },
    @{ name = 'aspnet-only'; arch = 'x64'; lines = @($core, $aspnet); desktopMissing = $true; aspnetMissing = $false },
    @{ name = 'preview-only'; arch = 'x64'; lines = @('Microsoft.NETCore.App 8.0.0-rc.1 [fixture]', 'Microsoft.WindowsDesktop.App 8.0.0-rc.1 [fixture]', 'Microsoft.AspNetCore.App 8.0.0-rc.1 [fixture]'); desktopMissing = $true; aspnetMissing = $true },
    @{ name = 'x86-only'; arch = 'x86'; lines = @($core, $desktop, $aspnet); desktopMissing = $true; aspnetMissing = $true }
)
$userFiles = @("$env:LOCALAPPDATA/AnshanOverlay/autosave.json", "$env:LOCALAPPDATA/AnshanOverlay/settings.json")
function Fingerprint([string]$path) { if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'missing' } }
$before = @{}; foreach ($path in $userFiles) { $before[$path] = Fingerprint $path }
$previousRoot = $env:DOTNET_ROOT_X64
$results = @()
try {
    foreach ($case in $cases) {
        $directory = Join-Path $qa $case.name
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $installDir = Join-Path $directory 'installed'
        if (Test-Path -LiteralPath $installDir) { throw 'Verification directory already exists. Inspect it before retrying.' }
        if ($case.arch) { Copy-Item -LiteralPath (Join-Path $qa "$($case.arch).exe") -Destination (Join-Path $directory 'dotnet.exe') -Force }
        $case.lines | Set-Content -LiteralPath (Join-Path $directory 'runtimes.txt') -Encoding ascii
        $env:DOTNET_ROOT_X64 = $directory
        $log = Join-Path $directory 'install.log'
        $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/SP-', '/LANG=chinesesimplified', ('/DIR="' + $installDir + '"'), ('/LOG="' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 7) { throw "Expected prerequisite failure (7): $($case.name), received $($process.ExitCode)" }
        $message = Get-Content -LiteralPath $log -Raw
        foreach ($component in @(@{ name = '.NET Desktop Runtime 8 (x64)'; missing = $case.desktopMissing }, @{ name = 'ASP.NET Core Runtime 8 (x64)'; missing = $case.aspnetMissing })) {
            if ($message.Contains($component.name) -ne $component.missing) { throw "Incorrect missing-component message: $($case.name), $($component.name)" }
        }
        if (!$message.Contains('https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0')) { throw 'Official download URL is missing.' }
        if ((Test-Path -LiteralPath $installDir) -or (Test-Path -LiteralPath $registryKey)) { throw 'Prerequisite failure wrote application files or registration.' }
        $results += [ordered]@{ scenario = $case.name; exitCode = $process.ExitCode; message = 'matched'; installation = 'blocked' }
    }
} finally {
    $env:DOTNET_ROOT_X64 = $previousRoot
}
foreach ($path in $userFiles) { if ((Fingerprint $path) -ne $before[$path]) { throw 'User data changed during prerequisite verification.' } }
[ordered]@{ version = $version; cases = $results; userData = 'unchanged' } | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $qa 'prerequisites-result.json')
