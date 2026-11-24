param(
    [Parameter(Mandatory)][string]$Destination,
    [ValidateSet('mono','network','network-fine','two','two-fine','four-seconds','network-time','two-pairs')][string]$Mode = 'mono',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$output = [IO.Path]::GetFullPath($Destination)
$work = Join-Path $output 'solver-diagnostico'
$bin = Join-Path $output 'bin'
$dotnet = if (Test-Path "$repo/.tools/dotnet/dotnet.exe") { "$repo/.tools/dotnet/dotnet.exe" } else { 'dotnet' }
if ($Build -or !(Test-Path "$bin/Campaign.dll")) {
    New-Item -ItemType Directory -Force $work, "$work/campaign" | Out-Null
    $files = git -C $repo ls-files src/Dino2.Core
    if ($LASTEXITCODE -ne 0) { throw 'No se localizaron los archivos del núcleo.' }
    foreach ($file in $files) {
        $target = Join-Path $work $file
        New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath "$repo/$file" -Destination $target
    }
    Copy-Item -LiteralPath "$repo/Directory.Build.props" -Destination $work
    Copy-Item -LiteralPath "$PSScriptRoot/Campaign.cs", "$PSScriptRoot/Campaign.csproj" -Destination "$work/campaign"
    $old = git -C $repo show 90c7aad97f2ee8451b0dc3d44df65c020fd020d7:src/Dino2.Core/GasDynamics/PortBoundary.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falta el cierre original ports-v1.' }
    $old.Replace('class PortBoundary','class PortBoundaryV1') | Set-Content -Encoding utf8 "$work/src/Dino2.Core/GasDynamics/PortBoundaryV1.cs"
    $coupling = "$work/src/Dino2.Core/GasDynamics/DuctCoupling.cs"
    $body = Get-Content -Raw $coupling
    $call = 'return PortBoundary.Flux(gas, area, opening, cylinder, chamber, last, inside, result);'
    $sample = 'double p = ValveFlux(end, angle, edge, edgeState, cylinder, chamber, flux);'
    if (!$body.Contains($call) -or !$body.Contains($sample)) { throw 'La instrumentación requiere el código de etapa 13.' }
    $body = $body.Replace($call, @'
{
            int bit = ReferenceEquals(end, inlet) ? 1 : 2;
            if ((ResearchSwitches.Mask & bit) == 0)
                return PortBoundaryV1.Flux(gas, area, opening, cylinder, chamber, last, inside, result);
            double portPressure = PortBoundary.Flux(gas, area, opening, cylinder, chamber, last, inside, result, out var trace);
            ResearchSwitches.LastTrace = trace;
            return portPressure;
        }
'@)
    $body = $body.Replace($sample, @'
ResearchSwitches.LastTrace = null;
            double p = ValveFlux(end, angle, edge, edgeState, cylinder, chamber, flux);
            if (ResearchSwitches.LastTrace is { } throat)
                ResearchSwitches.Trace?.Add([angle, ReferenceEquals(end, inlet) ? 1 : 2, MassFlux(flux), throat.ThroatPressurePa,
                    throat.ThroatTemperatureK, throat.ThroatMach, throat.MixedPressurePa, throat.MixedVelocityMps]);
'@)
    Set-Content -Encoding utf8 $coupling $body
    @'
namespace Dino2.Core.GasDynamics;
public static class ResearchSwitches
{
    [System.ThreadStatic] public static int Mask;
    [System.ThreadStatic] internal static PortTrace? LastTrace;
    [System.ThreadStatic] public static System.Collections.Generic.List<double[]>? Trace;
}
'@ | Set-Content -Encoding utf8 "$work/src/Dino2.Core/GasDynamics/ResearchSwitches.cs"
    & $dotnet build "$work/campaign" -c Release -o $bin
    if ($LASTEXITCODE -ne 0) { throw 'No se compiló el diagnóstico.' }
}
$env:DINO2_STAGE13_COMMIT = git -C $repo rev-parse HEAD
& $dotnet "$bin/Campaign.dll" $Mode (Join-Path $output $Mode)
if ($LASTEXITCODE -ne 0) { throw 'La campaña de etapa 13 falló.' }
