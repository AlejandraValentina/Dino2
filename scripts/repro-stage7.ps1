param([string]$Destination = 'artifacts/stage7/reproduction')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $sdk = if (Test-Path '.tools/dotnet/dotnet.exe') { Join-Path $root '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
    New-Item -ItemType Directory -Force $Destination | Out-Null
    & $sdk build Dino2.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la campaña.' }
    $harness = 'tests/Dino2.TwoStroke.Tests/bin/Release/net10.0/Dino2.TwoStroke.Tests.dll'
    & $sdk src/Dino2.Cli/bin/Release/net10.0/Dino2.Cli.dll --2t examples/2t-125cc-wide-rpm.json (Join-Path $Destination 'range.csv')
    if ($LASTEXITCODE -ne 0) { throw 'El barrido tiene puntos fallidos. Consultá range.csv.' }
    foreach ($name in @('continuation', 'geometry', 'resolution')) {
        & $sdk $harness --stage7 $name > (Join-Path $Destination "$name.csv")
        if ($LASTEXITCODE -ne 0) { throw "Falló la campaña $name." }
    }
    & $sdk $harness --stage7-profile > (Join-Path $Destination 'profile.csv')
    if ($LASTEXITCODE -ne 0) { throw 'Falló el perfil.' }
}
finally { Pop-Location }
