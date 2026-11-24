# Reproducción de la etapa 14C

Se requiere .NET 10. El portable y su automatización requieren Windows x64. Las referencias en Python usan NumPy y Cantera 3.2. Los destinos de evidencia deben ser nuevos para conservar corridas anteriores. La [nota científica](../../docs/etapa14c.md) documenta las unidades, criterios y restricciones.

```powershell
$out = 'E:/dino2-stage14c-replay'
New-Item -ItemType Directory -Path $out | Out-Null
dotnet build Dino2.slnx -c Release
$test = 'tests/Dino2.FourStroke.Tests/bin/Release/net10.0/Dino2.FourStroke.Tests.dll'
dotnet $test --stage14b
foreach ($seed in 140301, 104729, 196613) {
    dotnet $test --stage14c $seed
    if ($LASTEXITCODE -ne 0) { throw "Falló la semilla $seed" }
}
dotnet $test --stage13 bench "$out/bench"
dotnet $test --stage13 thermal "$out/thermal"
foreach ($mode in 'checks','networks','four','four-strict','mesh','fine','temporal') {
    dotnet $test --stage12 $mode > "$out/$mode.log"
    if ($LASTEXITCODE -ne 0) { throw "Falló $mode" }
}
```

Los bancos CSV deben contener 112 y 24 filas con estado `ok`. La campaña de geometría guarda curvas y trazas en JSON, incluidos los dos plenos de cada par A/B.

```powershell
pwsh -File evidence/stage13/reproduce.ps1 -Destination "$out/campaign" -Build -Mode mono
foreach ($mode in 'network','network-fine','network-time','four-seconds','two','two-pairs','two-small') {
    pwsh -File evidence/stage13/reproduce.ps1 -Destination "$out/campaign" -Mode $mode
    if ($LASTEXITCODE -ne 0) { throw "Falló $mode" }
}
```

Para reproducir impacto y diagnóstico, `$config` es el `stage14b-4t-default-result.json` de la evidencia Windows de 14B. La sonda construye una copia privada de cada núcleo con `git archive`. Sus contadores incluyen llamadas de muestreo y evaluación, además de los rechazos del solver. `ChokedNear` significa entrada con cuello sónico y α≤10⁻⁶. `WaveErrorMax` en ese conjunto es una medición independiente de compatibilidad en m/s. El arnés se ejecuta por separado del perfil sin instrumentación.

```powershell
pwsh -File evidence/stage14c/measure.ps1 -Revision 90693b2 -Destination "$out/old" -FourStrokeResult $config
pwsh -File evidence/stage14c/measure.ps1 -Revision HEAD -Destination "$out/new" -FourStrokeResult $config
python evidence/stage14c/compare.py "$out/old/results" "$out/new/results" "$out/impact.csv"
```

El paquete de evidencia conserva los resultados de la sonda independiente de 14B enlazada al nuevo núcleo. `reference/closure_reference.py` resuelve en presión con aritmética decimal. `reference/local_audit.py` evalúa Hugoniot, rarefacción e invariantes a partir de la tabla de propiedades. `reference/cantera_audit.py` utiliza GRI-Mech. `reference/moving_reference.py` obtiene la trayectoria del frente sin la malla de producción. La referencia histórica permanece preservada en `E:/dino2-audit/c14b`.

Desde una copia del paquete de evidencia, completá `reference/source` con `src` del commit que quieras comprobar. `reference/probe/Audit.csproj` apunta a ese árbol. Ejecutá cada modo con destino nuevo. La sonda de predictor de 14B espera estudiar un RHS y se detiene con el rechazo legítimo actual. Las comprobaciones permanentes de 14C cubren ambos esquemas y ambos extremos.

```powershell
git archive --output "$out/source.zip" 4deeb3075c43a2f95aa3e2361473974754d389c5 src
Expand-Archive "$out/source.zip" "$out/reference/source" -Force
dotnet build "$out/reference/probe/Audit.csproj" -c Release
$probe = "$out/reference/probe/bin/Release/net10.0/Dino2.FourStroke.Tests.dll"
New-Item -ItemType Directory "$out/reference/replay" | Out-Null
foreach ($mode in 'stress','reversal','limits','tube-domain','closure','historical','false-reject','moving-shock','B8') {
    dotnet $probe $mode > "$out/reference/replay/$mode.csv" 2> "$out/reference/replay/$mode.err"
    if ($LASTEXITCODE -ne 0) { throw "Falló $mode" }
}
```

`check-free-shock.py` verifica con la ecuación área–Mach por qué la boca parcial del ensayo móvil mantiene el estado incidente. Para repetir el contraste pequeño construido, copiá `SmallComparison.cs` como `Program.cs` de un proyecto de consola `net10.0` con referencia a `Dino2.Core.dll` extraída del portable. La variación constante de pérdidas produce un desplazamiento analítico de 0,00005 N·m y sirve para comprobar un B−A no nulo por debajo de la banda.

```powershell
python evidence/stage14c/check-free-shock.py "$out/reference/results/moving-shock.csv"
python evidence/stage14b/compare_plenum.py "$out/campaign" "$out/plenum-paired.csv"
```

```powershell
pwsh -File scripts/publish.ps1 -Destination "$out/portable" -Archive "$out/portable.zip"
Expand-Archive "$out/portable.zip" "$out/extracted"
pwsh -File scripts/smoke-publish.ps1 -Application "$out/extracted/Dino2.Desktop.exe"
```

Para entregar, se verifican también los recorridos `smoke-studies`, `smoke-two-stroke`, `smoke-multicylinder` y `smoke-multicylinder-2t` sobre el mismo ZIP extraído. El manifiesto de evidencia distingue los perfiles preliminares, el código instrumentado y la compilación limpia distribuible.
