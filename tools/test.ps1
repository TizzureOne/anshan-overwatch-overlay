param([switch]$TemporaryInkOnly, [switch]$ProxyOnly, [switch]$HeroTrailsOnly, [switch]$LectureBarOnly, [switch]$ComfortOnly, [switch]$ExperienceOnly, [switch]$PopupOnly, [switch]$TextOnly, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src/AnshanOverlay/AnshanOverlay.csproj'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$exe = Join-Path $projectRoot 'src/AnshanOverlay/bin/Release/net8.0-windows/AnshanOverlay.exe'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts/qa' }
$testArgs = @('--self-test', '--output', ('"' + $outputPath + '"'))
if ($TemporaryInkOnly) { $testArgs += '--temporary-ink-only' }
if ($ProxyOnly) { $testArgs += '--proxy-only' }
if ($HeroTrailsOnly) { $testArgs += '--hero-trails-only' }
if ($LectureBarOnly) { $testArgs += '--lecture-bar-only' }
if ($ComfortOnly) { $testArgs += '--comfort-only' }
if ($ExperienceOnly) { $testArgs += '--experience-only' }
if ($PopupOnly) { $testArgs += '--popup-only' }
if ($TextOnly) { $testArgs += '--text-only' }
$process = Start-Process -FilePath $exe -ArgumentList $testArgs -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path (Split-Path $exe) 'self-test-error.txt') -ErrorAction SilentlyContinue
    throw "Self-test failed: $($process.ExitCode)"
}
Get-Content -LiteralPath (Join-Path $outputPath 'self-test-result.json')
