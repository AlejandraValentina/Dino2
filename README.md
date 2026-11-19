# Dino2

Aplicación Windows para simular un motor monocilíndrico **4T con un modelo físico 0D**, C# / WPF / .NET 10. Calcula curvas de potencia y par indicados y estimados al cigüeñal, BMEP, llenado, estados del ciclo, bombeo y pérdidas. Permite conservar un original A, editar una variante B y comparar sus curvas por RPM.

**Etapa 3.** El modelo 4T de iso-octano usa equilibrio UV reducido de CO₂/CO y H₂O/H₂ durante combustión y expansión cerrada. Desde apertura de escape transporta las siete especies con composición congelada. Se conservan Otto, pseudoaire v1 y productos congelados v2. El [informe de etapa 3](docs/informe-etapa3.md) presenta comparaciones con Cantera, incertidumbre por especies y tiempos químicos omitidos, y alcance de la evidencia experimental.

Los ejemplos son sintéticos. La duración de combustión y los parámetros de pérdidas requieren identificación del motor. La verificación termoquímica y numérica se completó sin calibración experimental propia.

## Ejecutar en Windows

Abrir `artifacts/win-x64/Dino2.Desktop.exe`, o extraer **todo** `artifacts/Dino2-win-x64.zip` y abrir el ejecutable. La distribución incluye el runtime; no hace falta instalar .NET para usarla. Los artefactos generados no se incluyen en Git.

1. El ejemplo v3 se carga al iniciar y también se puede abrir desde `examples/4t-equilibrium.json`. **Modelo físico** ofrece pseudoaire v1, productos congelados v2 y equilibrio UV reducido v3. v2 y v3 permiten Wiebe prescrita o escalada. `examples/4t-isooctane.json` conserva el ejemplo v2. `examples/4t-400cc.json` conserva la referencia anterior.
2. Editar **Geometría**, **Válvulas**, **Combustión**, **Comb. v2**, **Contornos**, **Fricción** y **RPM / cálculo**. **Evidencia** registra fuente, datos y rango para parámetros estimados o declarados calibrados; no certifica validación independiente. En v2 el PCI se calcula a partir del combustible y la conversión deja explícitamente combustible sin quemar. Las presiones son absolutas. 0° es PMS de intercambio; 360° es PMS de combustión. Se admite coma o punto decimal sin separadores de miles.
3. Pulsar **Simular**. Se calcula cada régimen hasta periodicidad; la interfaz sigue respondiendo y permite **Cancelar**. Una falta de convergencia se informa, sin publicar una curva parcial.
4. Examinar **Curvas**, **Resultados**, **Ciclo** (selector RPM, presión angular y p–V) y **Diagnósticos** (balances, ciclos, pasos y tiempos). Las líneas discontinuas son indicadas y las continuas estimadas al cigüeñal.
5. Pulsar **Conservar como A**, modificar por ejemplo la alzada de admisión de 8 a 4 mm y volver a **Simular**. A permanece intacto. **A / B** muestra B−A para RPM comunes exactas, sin interpolación. Una diferencia simulada no demuestra ganancia mecánica. Si se comparan modelos diferentes, la aplicación lo advierte.
6. **Guardar proyecto…** conserva ambas configuraciones. Al abrir se regeneran ambas curvas con **Simular**. La edición invalida B y mantiene A; se avisa antes de descartar cambios sin guardar.
7. **Exportar CSV…** exporta curvas, traza del RPM seleccionado y, si existe A, original y comparación. Incluye entradas SI, modelo y diagnósticos. Líneas iniciales `#` son metadatos; separador coma, decimal punto, `NA` para magnitudes indefinidas.

**Otto de referencia** abre el ciclo ideal independiente de etapa 0. Sus ecuaciones, JSON y pruebas se conservan. No es un motor 4T predictivo ni comparte parámetros implícitos con el nuevo modelo.

## Compilar desde código

Requisitos: Windows x64 y [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). No se necesitan paquetes NuGet externos, Node, Python ni Visual Studio para construir y ejecutar la aplicación o las pruebas C#.

```powershell
dotnet build Dino2.slnx -c Release
dotnet run --project src/Dino2.Desktop -c Release
```

Este workspace tiene un SDK local ignorado por Git: si el global sigue en versión 9, sustituir `dotnet` por `.\.tools\dotnet\dotnet.exe`. Los scripts detectan el SDK local automáticamente. `global.json` requiere la familia 10.0.

## Verificar y reproducir

```powershell
.\scripts\verify.ps1
# Solo en Windows sin sesión interactiva; omite pruebas WPF:
.\scripts\verify.ps1 -SkipDesktop
```

Los ejecutores de consola devuelven código no cero ante fallos. **`dotnet test` no ejecuta estas pruebas**. El script compila y ejecuta 13 grupos originales, 15 grupos 4T de referencia, 13 grupos termoquímicos y 25 comprobaciones de ventana; genera estudios de discretización/periodicidad, sensibilidades, benchmarks, CSV y capturas. Los tiempos varían con la carga del equipo.

CLI 4T y reproducción del Otto:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- --4t examples/4t-400cc.json resultados.csv traza.csv
dotnet run --project src/Dino2.Cli -c Release -- --4t examples/4t-equilibrium.json reactivo.csv reactivo-traza.csv
dotnet run --project src/Dino2.Cli -c Release -- examples/otto-400cc.json otto.csv
```

La traza de CLI corresponde al primer régimen; la UI permite seleccionar otro para exportar. Si el proyecto contiene original, CLI genera también `resultados.comparison.csv`. Para crear un JSON 4T ilustrativo:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- --example-4t motor.json
dotnet run --project src/Dino2.Cli -c Release -- --example-equilibrium motor-reactivo.json
```

Comprobaciones independientes opcionales con Python 3, solo biblioteca estándar:

```powershell
python scripts/reference.py
python scripts/reference-4t.py
```

La segunda requiere los artefactos de `verify.ps1` y reconstruye los balances de la traza por otra implementación. Las figuras del informe se regeneran con `python scripts/plot-4t.py`; ese paso opcional necesita matplotlib (3.9.3 usado en esta entrega).

Verificación termoquímica y evaluación de equilibrio independientes, opcionales, con Cantera 3.2.0:

```powershell
python -m venv .tools/science
.\.tools\science\Scripts\python.exe -m pip install cantera==3.2.0
.\.tools\science\Scripts\python.exe scripts/check-stage3-cantera.py
python scripts/plot-stage3.py   # opcional; requiere matplotlib
```

Cantera no es una dependencia de la aplicación. Las comparaciones de equilibrio son verificaciones numéricas. Resultados reproducibles en `artifacts/stage3` y `artifacts/stage2`.

## Distribución y prueba del ejecutable

```powershell
.\scripts\publish.ps1
.\scripts\smoke-publish.ps1
```

Publish produce carpeta y ZIP Windows x64 con runtime, ejemplos y documentación. La primera ejecución puede necesitar red para descargar los paquetes oficiales del runtime. Sin instalador ni firma en esta etapa. La prueba publicada usa Windows UI Automation para abrir el 4T, calcular, abrir y calcular el Otto independiente, comprobar el runtime incluido y cerrar las ventanas. Requiere sesión gráfica.

## Documentación

- [Informe de etapa 3: equilibrio UV, comparación y evidencia experimental](docs/informe-etapa3.md).
- [Informe de etapa 2: resultados científicos y limitaciones](docs/informe-etapa2.md).
- [Modelo termoquímico y combustión condicionada](docs/modelo-termoquimico.md).
- [Datos experimentales investigados y protocolo de calibración/validación](docs/validacion-experimental.md).
- [Informe técnico final y resultados](docs/informe-4t.md).
- [Modelo físico 4T: ecuaciones, unidades, referencias y limitaciones](docs/modelo-4t.md).
- [Arquitectura 4T y separación de responsabilidades](docs/arquitectura-4t.md).
- [Modelo Otto independiente](docs/modelo-otto.md).
- [Elección tecnológica y arquitectura histórica de etapa 0](docs/arquitectura.md).
- [Verificación histórica de etapa 0](docs/verificacion.md).

La evidencia experimental prioritaria son trazas compatibles de presión del cilindro, contornos y par, con puntos de operación reservados para evaluación independiente.
