# Dino2

Aplicación Windows para simular un motor monocilíndrico **4T con un modelo físico 0D**, C# / WPF / .NET 10. Calcula curvas de potencia y par indicados y estimados al cigüeñal, BMEP, llenado, estados del ciclo, bombeo y pérdidas. Permite conservar un original A, editar una variante B y comparar sus curvas por RPM.

**Predicciones aproximadas, sin validación experimental de Dino2.** El gas es pseudoaire con cp(T), la combustión es prescrita y las pérdidas empíricas no están calibradas para el motor del ejemplo. No se calcula detonación ni viabilidad mecánica. El ejemplo es sintético. El [informe técnico](docs/informe-4t.md) describe resultados, precisión numérica, límites y evidencia.

## Ejecutar en Windows

Abrir `artifacts/win-x64/Dino2.Desktop.exe`, o extraer **todo** `artifacts/Dino2-win-x64.zip` y abrir el ejecutable. La distribución incluye el runtime; no hace falta instalar .NET para usarla. Los artefactos generados no se incluyen en Git.

1. El ejemplo 4T se carga al iniciar. También se puede abrir `examples/4t-400cc.json` con **Abrir…**.
2. Editar los grupos **Geometría**, **Válvulas**, **Combustión**, **Contornos**, **Fricción** y **RPM / cálculo**. Las presiones son absolutas. 0° es PMS de intercambio; el PMS de combustión corresponde a 360°. Las etiquetas indican unidades; se admite coma o punto decimal sin separadores de miles.
3. Pulsar **Simular**. Se calcula cada régimen hasta periodicidad; la interfaz sigue respondiendo y permite **Cancelar**. Una falta de convergencia se informa, sin publicar una curva parcial.
4. Examinar **Curvas**, **Resultados**, **Ciclo** (selector RPM, presión angular y p–V) y **Diagnósticos** (balances, ciclos, pasos y tiempos). Las líneas discontinuas son indicadas y las continuas estimadas al cigüeñal.
5. Pulsar **Conservar como A**, modificar por ejemplo la alzada de admisión de 8 a 4 mm y volver a **Simular**. A permanece intacto. La pestaña **A / B** muestra B−A: positivo gana, negativo pierde. Solo se comparan RPM comunes exactas; no se interpola.
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

Los ejecutores de consola devuelven código no cero ante fallos. **`dotnet test` no ejecuta estas pruebas**. El script compila y ejecuta 13 grupos originales, 15 grupos 4T y 20 comprobaciones de ventana; genera estudios de discretización/periodicidad, benchmarks, CSV y capturas. La ejecución mide rendimiento y por eso puede variar con la carga del equipo.

CLI 4T y reproducción del Otto:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- --4t examples/4t-400cc.json resultados.csv traza.csv
dotnet run --project src/Dino2.Cli -c Release -- examples/otto-400cc.json otto.csv
```

La traza de CLI corresponde al primer régimen; la UI permite seleccionar otro para exportar. Si el proyecto contiene original, CLI genera también `resultados.comparison.csv`. Para crear un JSON 4T ilustrativo:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- --example-4t motor.json
```

Comprobaciones independientes opcionales con Python 3, solo biblioteca estándar:

```powershell
python scripts/reference.py
python scripts/reference-4t.py
```

La segunda requiere los artefactos de `verify.ps1` y reconstruye los balances de la traza por otra implementación. Las figuras del informe se regeneran con `python scripts/plot-4t.py`; ese paso opcional necesita matplotlib (3.9.3 usado en esta entrega).

## Distribución y prueba del ejecutable

```powershell
.\scripts\publish.ps1
.\scripts\smoke-publish.ps1
```

Publish produce carpeta y ZIP Windows x64 con runtime, ejemplos y documentación. La primera ejecución puede necesitar red para descargar los paquetes oficiales del runtime. Sin instalador ni firma en esta etapa. La prueba publicada usa Windows UI Automation para abrir el 4T, calcular, abrir y calcular el Otto independiente, comprobar el runtime incluido y cerrar las ventanas. Requiere sesión gráfica.

## Documentación

- [Informe técnico final y resultados](docs/informe-4t.md).
- [Modelo físico 4T: ecuaciones, unidades, referencias y limitaciones](docs/modelo-4t.md).
- [Arquitectura 4T y separación de responsabilidades](docs/arquitectura-4t.md).
- [Modelo Otto independiente](docs/modelo-otto.md).
- [Elección tecnológica y arquitectura histórica de etapa 0](docs/arquitectura.md).
- [Verificación histórica de etapa 0](docs/verificacion.md).

El siguiente trabajo científico prioritario es contrastar un motor con datos experimentales completos y mejorar composición/productos y combustión, conservando trazabilidad. No se incorporaron 2T ni optimización compleja. El repositorio del Dino anterior no fue modificado. No se hace push sin autorización.
