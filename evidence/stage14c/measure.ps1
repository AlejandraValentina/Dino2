param(
    [Parameter(Mandatory)][string]$Revision,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$FourStrokeResult,
    [string]$Python = 'python',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destination) { throw 'Usá un directorio de evidencia nuevo.' }
New-Item -ItemType Directory -Path $destination | Out-Null
git -C $root rev-parse $Revision | Set-Content "$destination/commit.txt"
git -C $root archive --format=zip --output="$destination/source.zip" $Revision
if ($LASTEXITCODE -ne 0) { throw "No se pudo archivar $Revision." }
Expand-Archive -LiteralPath "$destination/source.zip" -DestinationPath "$destination/source"
& $Python "$PSScriptRoot/instrument.py" "$destination/source"
if ($LASTEXITCODE -ne 0) { throw 'Falló la instrumentación.' }
New-Item -ItemType Directory -Path "$destination/source/measure" | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/Measure.cs" -Destination "$destination/source/measure"
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../src/Dino2.Core/Dino2.Core.csproj" />
  </ItemGroup>
</Project>
'@ | Set-Content "$destination/source/measure/Measure.csproj"
& $Dotnet build "$destination/source/measure" -c Release -o "$destination/bin" > "$destination/build.log"
if ($LASTEXITCODE -ne 0) { throw "Falló la compilación. Ver $destination/build.log" }
'case,cpu_ms,wall_ms,calls,alpha_le_1e12,choked_near,max_wave_residual_m_s,domain,numerical' | Set-Content "$destination/summary.csv"
& $Dotnet "$destination/bin/Measure.dll" "$destination/results" ([IO.Path]::GetFullPath($FourStrokeResult)) |
    Add-Content "$destination/summary.csv"
if ($LASTEXITCODE -ne 0) { throw 'Falló la campaña instrumentada.' }
