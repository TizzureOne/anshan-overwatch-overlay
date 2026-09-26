$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src/AnshanOverlay/AnshanOverlay.csproj'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$exe = Join-Path $projectRoot 'src/AnshanOverlay/bin/Release/net8.0-windows/AnshanOverlay.exe'
$outputPath = Join-Path $projectRoot 'artifacts/qa'
$process = Start-Process -FilePath $exe -ArgumentList @('--self-test', '--output', ('"' + $outputPath + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path (Split-Path $exe) 'self-test-error.txt') -ErrorAction SilentlyContinue
    throw "Self-test failed: $($process.ExitCode)"
}
Get-Content -LiteralPath (Join-Path $outputPath 'self-test-result.json')
