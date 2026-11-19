param([switch]$SkipDesktop)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $dotnetExe = if (Test-Path -LiteralPath '.tools/dotnet/dotnet.exe') { Join-Path $repoRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
    & $dotnetExe build Dino2.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $dotnetExe run --project tests/Dino2.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Physics/storage checks failed' }
    & $dotnetExe run --project tests/Dino2.FourStroke.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw '4T physical/numerical checks failed' }
    if (!$SkipDesktop) {
        & $dotnetExe run --project tests/Dino2.Desktop.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Desktop checks failed' }
    }
    New-Item -ItemType Directory -Force artifacts/verification | Out-Null
    & $dotnetExe run --project src/Dino2.Cli -c Release --no-build -- examples/otto-400cc.json artifacts/verification/otto-400cc.csv
    if ($LASTEXITCODE -ne 0) { throw 'Reference reproduction failed' }
    & $dotnetExe run --project src/Dino2.Cli -c Release --no-build -- --4t examples/4t-400cc.json artifacts/verification/4t-baseline.csv
    if ($LASTEXITCODE -ne 0) { throw '4T CLI reproduction failed' }
}
finally { Pop-Location }
