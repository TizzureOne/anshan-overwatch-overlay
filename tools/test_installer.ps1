param([string]$Installer, [switch]$Lite)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src/AnshanOverlay/AnshanOverlay.csproj') -Raw)).Project.PropertyGroup.Version
$suffix = if ($Lite) { '-Lite' } else { '' }
if (!$Installer) { $Installer = Join-Path $projectRoot "dist/AnshanOverlay-$version$suffix-Setup-x64.exe" }
$qa = Join-Path $projectRoot "artifacts/qa/installer-$version$suffix"
$installDir = Join-Path $qa 'installed'
$registryKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{B67B9F75-6EC5-4BBA-87B8-A9F722DF7281}_is1'
if (Test-Path -LiteralPath $registryKey) { throw 'An existing installation is registered. Do not replace it with a verification install.' }
if (Test-Path -LiteralPath $installDir) { throw 'Verification directory already exists. Inspect it before retrying.' }
New-Item -ItemType Directory -Path $qa -Force | Out-Null
$userFiles = @("$env:LOCALAPPDATA/AnshanOverlay/autosave.json", "$env:LOCALAPPDATA/AnshanOverlay/settings.json")
function Fingerprint([string]$path) { if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'missing' } }
$before = @{}; foreach ($path in $userFiles) { $before[$path] = Fingerprint $path }
$process = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/SP-', ('/DIR="' + $installDir + '"'), ('/LOG="' + (Join-Path $qa 'install.log') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode). See install.log." }
try {
    $exe = Join-Path $installDir 'AnshanOverlay.exe'
    $published = Join-Path $projectRoot "dist/AnshanOverlay-$version$suffix-win-x64/AnshanOverlay.exe"
    if ((Fingerprint $exe) -ne (Fingerprint $published)) { throw 'Installed executable hash does not match publish output.' }
    $registered = Get-ItemProperty -LiteralPath $registryKey
    if ($registered.DisplayVersion -ne $version) { throw 'Installed version registration is incorrect.' }
    if (!$Lite -and !(Test-Path -LiteralPath (Join-Path $installDir 'licenses'))) { throw 'Runtime licenses are missing.' }
    foreach ($file in Get-ChildItem -LiteralPath (Split-Path $published) -File -Recurse) {
        $installedFile = Join-Path $installDir ([IO.Path]::GetRelativePath((Split-Path $published), $file.FullName))
        if ((Fingerprint $installedFile) -ne (Fingerprint $file.FullName)) { throw "Installed file hash mismatch: $($file.Name)" }
    }
    $test = Start-Process -FilePath $exe -ArgumentList @('--self-test', '--output', ('"' + (Join-Path $qa 'self-test') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Installed executable self-test failed: $($test.ExitCode)" }
} finally {
    # Only uninstall this new verification instance, inside the checked workspace path.
    $resolvedInstall = [IO.Path]::GetFullPath($installDir)
    $expectedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/qa')) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedInstall.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe verification uninstall path.' }
    $uninstaller = Join-Path $resolvedInstall 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $qa 'uninstall.log') + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Verification uninstall failed: $($uninstall.ExitCode)" }
    }
}
if ((Test-Path -LiteralPath (Join-Path $installDir 'AnshanOverlay.exe')) -or (Test-Path -LiteralPath $registryKey)) { throw 'Uninstall left the application or registration behind.' }
foreach ($path in $userFiles) { if ((Fingerprint $path) -ne $before[$path]) { throw 'User data changed during verification.' } }
$results = [ordered]@{ version = $version; variant = $(if ($Lite) { 'Lite' } else { 'Full' }); install = 'passed'; binaryHash = 'matched'; uninstall = 'passed'; userData = 'unchanged'; selfTest = (Get-Content -LiteralPath (Join-Path $qa 'self-test/self-test-result.json') -Raw | ConvertFrom-Json) }
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $qa 'installer-result.json') -Encoding utf8
$results | ConvertTo-Json -Depth 4
