param([string]$RuntimeVersion = '8.0.31', [string]$CompilerPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src/AnshanOverlay/AnshanOverlay.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$destination = Join-Path $projectRoot "dist/AnshanOverlay-$version-win-x64"
if (!$CompilerPath) {
    $CompilerPath = @(
        (Join-Path $projectRoot 'artifacts/tools/InnoSetup/ISCC.exe'),
        "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
        "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup 6.7+ is required. Supply -CompilerPath with the path to ISCC.exe.' }
dotnet publish $project -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $destination '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
$cacheRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$licenses = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.aspnetcore.app.runtime.win-x64')) {
    foreach ($name in @('LICENSE.TXT', 'LICENSE', 'THIRD-PARTY-NOTICES.TXT')) {
        $source = Join-Path $cacheRoot "$package/$RuntimeVersion/$name"
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $licenses "$package-$name") -Force }
    }
}
& $CompilerPath "/DAppVersion=$version" "/DPublishDir=$destination" (Join-Path $PSScriptRoot 'installer/AnshanOverlay.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$installer = Join-Path $projectRoot "dist/AnshanOverlay-$version-Setup-x64.exe"
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $installer
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $projectRoot "dist/AnshanOverlay-$version-SHA256.txt") -Encoding ascii
Get-Item -LiteralPath $installer
$hash
