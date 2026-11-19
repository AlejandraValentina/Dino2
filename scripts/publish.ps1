param(
    [string]$Destination = 'artifacts/win-x64',
    [string]$Archive = 'artifacts/Dino2-win-x64.zip'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $dotnetExe = if (Test-Path -LiteralPath '.tools/dotnet/dotnet.exe') { Join-Path $repoRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
    & $dotnetExe publish src/Dino2.Desktop -c Release -r win-x64 --self-contained true -o $Destination -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $examples = Join-Path $Destination 'examples'
    $docs = Join-Path $Destination 'docs'
    New-Item -ItemType Directory -Force $examples, $docs | Out-Null
    Copy-Item -Path examples/*.json -Destination $examples
    Copy-Item -LiteralPath README.md -Destination (Join-Path $Destination 'README.md')
    Copy-Item -Path docs/*.md -Destination $docs
    if (Test-Path -LiteralPath docs/figures) {
        $figures = Join-Path $docs 'figures'
        New-Item -ItemType Directory -Force $figures | Out-Null
        Copy-Item -Path docs/figures/* -Destination $figures
    }
    Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath $Archive -Force
    Write-Output "Distribucion: $Archive. Ejecutar Dino2.Desktop.exe tras extraer todo el ZIP."
}
finally { Pop-Location }
