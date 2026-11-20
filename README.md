# Dino2

Aplicación Windows en C# / WPF / .NET 10 para motores monocilíndricos **4T con conductos 1D opcionales** y **2T de láminas con escape de expansión 1D**. Calcula curvas de potencia y par indicados y estimados al cigüeñal, BMEP, llenado, estados del ciclo, bombeo y pérdidas. Permite conservar un original A, editar una variante B y comparar sus curvas por RPM.

**Cierre de etapa 4.** Admisión y escape con propagación y reflexión de presión, longitud y diámetros terminales editables, y balances separados en conductos y cilindro. La comparación A/B incluye llenado, par, potencia y contraste de resolución en tres mallas. Los depósitos 0D y las referencias anteriores siguen disponibles. El [informe de cierre](docs/cierre-etapa4.md) cuantifica la sensibilidad a malla y paso, y delimita las hipótesis del modelo.

**Etapa 5.** La pestaña **Estudios** ejecuta grillas de uno a tres parámetros, ordena resultados por objetivos explícitos y permite contrastar la resolución de un candidato antes de aplicarlo como B. Abrí los ejemplos de estudio desde su propio botón **Abrir estudio…**. [Informe y guía de etapa 5](docs/etapa5.md).

**Etapa 6.** Desde la ventana principal, elegí **Motor 2T**. La configuración, el cálculo y los estudios específicos se organizan en una ventana independiente. [Modelo 2T](docs/modelo-2t.md) · [Informe y guía de etapa 6](docs/etapa6.md).

**Etapa 7.** El ejemplo **2t-125cc-wide-rpm.json** amplía la verificación a 20 regímenes entre 500 y 10 000 RPM. Los diagnósticos identifican el componente que limita la periodicidad y distinguen resultados de revisiones numéricas distintas. [Consolidación y evidencia](docs/etapa7.md).

## Ejecutar en Windows

Abrir `artifacts/stage7-win-x64/Dino2.Desktop.exe`, o extraer **todo** `artifacts/Dino2-stage7-win-x64.zip` y abrir el ejecutable. La distribución incluye el runtime; no hace falta instalar .NET para usarla. Los artefactos generados no se incluyen en Git.

Para 2T, abrí "examples/2t-125cc.json" desde **Abrir proyecto 2T…** y pulsá **Simular 2T**. El ejemplo A/B de escape está en "examples/2t-125cc-escape-ab.json". La [guía de etapa 6](docs/etapa6.md) describe el estudio, el contraste y las exportaciones.

### Recorrido 4T

1. El ejemplo con depósitos v3 se carga al iniciar. Para simular conductos, seleccioná **v4 · Conductos MUSCL · Wiebe prescrita** o abrí `examples/4t-ducts.json`. El proyecto `examples/4t-ducts-muscl-ab.json` contiene ambas geometrías con pasos axiales comparables. Los ejemplos anteriores conservan el esquema de primer orden. El editor **Conductos** permite ajustar longitud, diámetros, número de celdas y CFL.
2. Editar **Geometría**, **Válvulas**, **Combustión**, **Comb. v2**, **Contornos**, **Fricción** y **RPM / cálculo**. **Evidencia** registra fuente, datos y rango para parámetros estimados o declarados calibrados; no certifica validación independiente. En v2 el PCI se calcula a partir del combustible y la conversión deja explícitamente combustible sin quemar. Las presiones son absolutas. 0° es PMS de intercambio; 360° es PMS de combustión. Se admite coma o punto decimal sin separadores de miles.
3. Pulsar **Simular**. Se calcula cada régimen hasta periodicidad; la interfaz sigue respondiendo y permite **Cancelar**. Una falta de convergencia se informa, sin publicar una curva parcial.
4. Examinar **Curvas**, **Resultados**, **Ciclo** (selector RPM, presión angular y p–V), **Conductos** (presiones próximas a ambas válvulas en v4) y **Diagnósticos** (balances, ciclos, pasos y tiempos). Las líneas discontinuas son indicadas y las continuas estimadas al cigüeñal.
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

Los ejecutores de consola devuelven código no cero ante fallos. **`dotnet test` no ejecuta estas pruebas**. El script compila y ejecuta 52 grupos históricos de física, métodos numéricos y archivos, 43 comprobaciones históricas de ventana y una batería 2T; genera estudios de discretización/periodicidad, sensibilidades, benchmarks, CSV y capturas. Los tiempos varían con la carga del equipo.

CLI 4T y reproducción del Otto:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- --4t examples/4t-400cc.json resultados.csv traza.csv
dotnet run --project src/Dino2.Cli -c Release -- --4t examples/4t-ducts-muscl-ab.json conductos.csv traza.csv
dotnet run --project src/Dino2.Cli -c Release -- examples/otto-400cc.json otto.csv
```

La traza de CLI corresponde al primer régimen; la UI permite seleccionar otro para exportar. Si el proyecto contiene original, CLI genera también `resultados.comparison.csv`. Para crear un JSON 4T ilustrativo:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- --example-4t motor.json
dotnet run --project src/Dino2.Cli -c Release -- --example-ducts motor-conductos.json
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
.\scripts\publish.ps1 -Destination artifacts/stage7-win-x64 -Archive artifacts/Dino2-stage7-win-x64.zip
.\scripts\smoke-publish.ps1 -Application artifacts/stage7-win-x64/Dino2.Desktop.exe
.\scripts\smoke-studies.ps1 -Application artifacts/stage7-win-x64/Dino2.Desktop.exe
.\scripts\smoke-two-stroke.ps1 -Application artifacts/stage7-win-x64/Dino2.Desktop.exe
```

Publish produce carpeta y ZIP Windows x64 con runtime, ejemplos y documentación. La primera ejecución puede necesitar red para descargar los paquetes oficiales del runtime. Sin instalador ni firma en esta etapa. La prueba publicada usa Windows UI Automation para abrir el 4T y calcular con depósitos y conductos, abrir y calcular el Otto independiente, comprobar el runtime incluido y cerrar las ventanas. Requiere sesión gráfica.

## Documentación

- [Estudios paramétricos, resolución y rendimiento](docs/etapa5.md).
- [Cierre de etapa 4: causas, mallas y aceptación](docs/cierre-etapa4.md).
- [Informe inicial de etapa 4 y verificación de conductos 1D](docs/informe-etapa4.md).
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
