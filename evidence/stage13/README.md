# Evidencia de etapa 13

El [informe técnico](../../docs/etapa13.md) documenta ecuaciones, resultados y alcance. `results/` conserva el banco medido y los resúmenes extraídos de las campañas. El ZIP de evidencia conserva los JSON con las series angulares, el registro de consola, los archivos exportados por Desktop, la reproducción de etapa 12 y los hashes.

`reproduce.ps1` y `Campaign.cs` permiten repetir el diagnóstico 4T, el A/B a dos longitudes y los controles de malla 2T. El modo `mono` usa cuatro combinaciones de válvulas entre `ports-v1` y `ports-v2`, usando el código histórico de `90c7aad` en un directorio de diagnóstico. Las columnas del arreglo `throat` son ángulo, válvula (`1` admisión, `2` escape), flujo hacia cilindro, presión de garganta, temperatura de garganta, Mach, presión mezclada y velocidad axial del conducto, todas en SI salvo ángulo en grados. El campo `mask` del diagnóstico identifica las válvulas con la revisión nueva. Los sellos del solver instrumentado corresponden al núcleo compilado, y `mask` identifica las ecuaciones usadas para cada experimento.

Los ensayos `bench`, `thermal` y `micro` se ejecutan con `Dino2.FourStroke.Tests`. `scripts/check-stage13-aperture.py` implementa la referencia independiente del banco ideal. `summarize.py` lee los datos crudos de las campañas, calcula estadísticas y realiza el análisis armónico a partir de las trazas. Para repetir la comprobación de archivos históricos, extraé las entradas `windows/ui-mono2/engine-project.json`, `windows/ui-multi4/motor.json` y `windows/ui-multi2/motor.json` del ZIP de etapa 12 preservando las rutas, y ejecutá:

```powershell
dotnet run --project tests/Dino2.Desktop.Tests -c Release -- --historical-stage13 RUTA_DE_EXTRACCIÓN
```

Las columnas `elapsed_ms` son tiempos de pared observados en campañas concurrentes. Los resúmenes de la etapa son resultados numéricos para las geometrías publicadas.
