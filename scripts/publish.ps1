$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $dotnetExe = if (Test-Path -LiteralPath '.tools/dotnet/dotnet.exe') { Join-Path $repoRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
    & $dotnetExe publish src/Dino2.Desktop -c Release -r win-x64 --self-contained true -o artifacts/win-x64 -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    New-Item -ItemType Directory -Force artifacts/win-x64/examples, artifacts/win-x64/docs | Out-Null
    Copy-Item -LiteralPath examples/otto-400cc.json -Destination artifacts/win-x64/examples/otto-400cc.json
    Copy-Item -LiteralPath README.md -Destination artifacts/win-x64/README.md
    Copy-Item -Path docs/*.md -Destination artifacts/win-x64/docs
    Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/Dino2-win-x64.zip -Force
    Write-Output 'Distribucion: artifacts/Dino2-win-x64.zip; ejecutar Dino2.Desktop.exe tras extraer todo el ZIP.'
}
finally { Pop-Location }
