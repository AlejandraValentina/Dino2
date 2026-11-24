# Reproducción científica de etapa 14B

Se requiere Windows x64 para las pruebas WPF y .NET 10 para todas las pruebas. La referencia externa usa Cantera 3.2. Usar rutas nuevas para conservar los resultados anteriores.

```powershell
$out = 'E:/dino2-stage14b-replay'
New-Item -ItemType Directory -Force $out | Out-Null
dotnet build Dino2.slnx -c Release
$test = 'tests/Dino2.FourStroke.Tests/bin/Release/net10.0/Dino2.FourStroke.Tests.dll'
dotnet $test --stage14b
dotnet $test --stage13 bench "$out/bench"
dotnet $test --stage13 thermal "$out/thermal"
dotnet $test --stage13 micro "$out/micro"
foreach ($p in "$out/bench/aperture.csv", "$out/thermal/thermal.csv") {
    if (Import-Csv $p | Where-Object status -ne 'ok') { throw "Caso fallido en $p" }
}
foreach ($mode in 'checks','mesh','fine','temporal','four','four-strict','networks') {
    dotnet $test --stage12 $mode | Tee-Object "$out/$mode.log"
    if ($LASTEXITCODE -ne 0) { throw "Falló $mode" }
}

pwsh -File evidence/stage13/reproduce.ps1 -Destination "$out/campaign" -Build -Mode mono
foreach ($mode in 'network','network-fine','network-time','four-seconds','two','two-pairs','two-small') {
    pwsh -File evidence/stage13/reproduce.ps1 -Destination "$out/campaign" -Mode $mode
    if ($LASTEXITCODE -ne 0) { throw "Falló $mode" }
}
dotnet run --project tests/Dino2.Desktop.Tests -c Release -- "$out/desktop"
pwsh -File scripts/publish.ps1 -Destination "$out/portable" -Archive "$out/portable.zip"
Expand-Archive "$out/portable.zip" "$out/extracted"
pwsh -File scripts/smoke-publish.ps1 -Application "$out/extracted/Dino2.Desktop.exe"
```

La campaña instrumentada guarda estados angulares, configuración, balances y diagnósticos, sin insertar instrumentación en el binario de distribución. En `mono-3.json` ambos puertos usan la formulación vigente. `mono-0.json` usa la etapa 12 en ambos puertos como control. Para repetir `ports-v2` usar `recovery/stage13-7a080e7` en otro árbol y el banco de esa revisión. Para la reapertura histórica ejecutar el binario de pruebas Desktop con `--historical-stage13` y la raíz de los datos extraídos de etapa 12 o 13.

`check_cantera.py` recibe el CSV `edge.csv` de los ensayos independientes y escribe el contraste del choque caliente. El programa reproduce el estado poschoque a contrapresión prescrita con propiedades de GRI-Mech. Usa una tolerancia relativa de 0,1 % entre tablas y compara el límite de área dentro de Dino2 en unidades absolutas. Las pruebas del núcleo miden balances de Rankine–Hugoniot dentro de un único conjunto de propiedades.

```powershell
# $evidence es la raíz del paquete de evidencia extraído.
$core = (Resolve-Path 'src/Dino2.Core/Dino2.Core.csproj').Path
dotnet build "$evidence/source/Probe.csproj" -c Release -p:Dino2CoreProject=$core -o "$out/probe"
$probe = "$out/probe/Dino2.FourStroke.Tests.dll"
foreach ($mode in 'edge','shock','transient','b8','acoustic-balanced') {
    dotnet $probe $mode > "$out/$mode.csv" 2> "$out/$mode.err"
    if ($LASTEXITCODE -ne 0) { throw "Falló $mode" }
}
python -m pip install cantera==3.2.0
python evidence/stage14b/check_cantera.py "$out/edge.csv" "$out/cantera-hot-shock.csv"
python evidence/stage14b/compare_plenum.py "$out/campaign" "$out/plenum-ab.csv"
dotnet $probe paired-time "$out/campaign/two-pairs/pair-5000-1E-07.json" "$out/campaign/two-pairs/pair-9000-0.04.json"
```

Las campañas tienen niveles de concurrencia distintos. Usar el piloto de perfil en el paquete de evidencia para comparar CPU y pared en dos revisiones con la misma carga y el mismo arnés. No comparar tiempos de campañas ejecutadas con diferente concurrencia.
