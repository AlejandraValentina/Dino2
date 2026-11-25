# Consolidación de Etapa 15B

Fecha de corte: 2026-11-25. Estado del checkout original inspeccionado antes de organizar: `ui/mahapps-shell` en `78df3e03d8955438d541430b06c18e12a4772ada`, con padre `af937878ac8d3e4ed9258bae4cb3b6fc7b7fbd4c`. El reflog mostró que Opus había confirmado sus cambios en `78df3e0`; el trabajo científico de Codex seguía como modificaciones y archivos nuevos basados en 15A. Por lo tanto, no se volvió a confirmar ni a reescribir el commit de Opus.

## Preservación y procedencia

Antes de crear los worktrees se guardó una recuperación completa en `E:\dino2-stage15b1-recovery-20261125`: bundle Git SHA-256 `A1C2D7B4503E3EA186F503CBAD76701DB9059B364D4A721CD50747B0319ED81E`, parche del árbol tracked, copia de archivos no rastreados, estado/reflog y copias de los artefactos ignorados. El registro de las ramas y el reflog está en ese directorio. El diff residual se guardó después de su consolidación en `stash@{0}` (`952b711d4f1f81f12f4ad1c541c1f4e92102556c`), dejando el checkout original limpio sobre `ui/mahapps-shell`. No se ejecutaron `reset --hard`, `clean` ni se reescribieron referencias históricas.

| Desarrollo | Rama | Commit | Worktree | Contenido |
|---|---|---|---|---|
| Opus Desktop | `stage15b/desktop` (también `ui/mahapps-shell`) | `78df3e03d8955438d541430b06c18e12a4772ada` | `E:\dino2-desktop` | MahApps shell, navegación, `ScientificPlot`, `FieldValidation`, selectores 4T, manual/README y pruebas Desktop |
| Codex científico | `stage15b/science` | `492a194` | `E:\dino2-science` | mapas Cd, CSV/metadatos, persistencia, integración de mapa 4T 0D, adaptación a `ports-v4`, modelo de lámina experimental y pruebas/evidencias |
| Integración local | `stage15b/integration` | base `df4b873`; adaptador Cd `51da42c`; Desktop mapas `d8ca006`; cierre/documentación en HEAD | `E:\dino2-integration` | base Opus más cherry-pick científico, adaptadores 2T y controles Desktop de mapas |

El commit científico no modifica los archivos de shell/manual de Opus. El commit Desktop toca 27 archivos, todos en Desktop, pruebas Desktop, README y manual. Las capturas originales permanecen en `E:\dino2-desktop\artifacts`; la copia probatoria está en `evidence/stage15b1`. Las pruebas Desktop dejaron 116 líneas `PASS` (log incluido). Cada worktree produce sus propios `bin/obj`, artefactos y logs. La SDK 10.0.404 usada para compilar vive en `E:\dino2\.tools\dotnet` y se invocó por PATH; no se copió dentro de los worktrees.

## Inventario científico recuperado

Incluye `DischargeMap.cs`, `DischargeMapFile.cs`, campos de mapa forward/reverse en válvulas y lumbreras, igualdad/hash estructural y serialización; adaptadores 4T 0D y 4T 1D `ports-v4`, transfer agregado 2T 0D, escape 2T 1D y válvulas de lámina en frontera de admisión. El Desktop integrado puede importar, consultar, limpiar y persistir mapas. También conserva la lámina modal experimental, pruebas y `evidence/stage15b` (incluida la falla de periodicidad). La copia de recuperación preserva cualquier archivo no rastreado e ignorado que no se incorporó al commit.

## Resultado de verificación

- Desktop en `E:\dino2-desktop`: compilación Release correcta, 0 warnings/0 errores; 116/116 checks (`evidence/stage15b1/desktop-tests.log`).
- Científico en `E:\dino2-science`: `scripts/verify.ps1` finalizó con código 0; log íntegro en `evidence/stage15b1/science-regression.log`. Incluye build sin warnings, 13/13 generales, 25/25 grupos 4T, 15/15 reactivos, 7/7 estudios, campaña 2T, redes multicilíndricas, 4T Desktop, 116 Desktop checks y reproducciones CLI 4T/2T.
- El grupo 4T añadido verifica el flujo con mapa Cd variable dentro de `ports-v4`, flujo directo e inverso, evaluación con presión de cara aceptada y rechazo de consulta fuera de dominio. Los grupos 2T verifican mapa de escape 1D en ambas direcciones, mapa cuasiestático/dinámico de lámina, conservación de flujo aceptado, residual y trazas. Desktop verifica importación, puntos, convenciones, limpiar a constante y persistencia/reapertura 4T/2T. El benchmark existente del sweep 4T (~0.53 s para 11 RPM) corresponde al modelo de referencia, no al costo de mapa en una campaña completa.
- La rama integrada se creó desde `78df3e0` y se le aplicó `492a194` por cherry-pick (`df4b873`); los adaptadores 2T y controles Desktop posteriores se preservan en `51da42c` y `d8ca006`. La regresión completa sobre estos cambios terminó con código 0; log `evidence/stage15b1/integration-final-regression-2.log`, junto a las salidas CLI en `evidence/stage15b1/integration-cli`. El manifiesto SHA-256 se regeneró y verificó sobre 80 archivos.

## Diferencias y limitaciones

No hubo conflictos al combinar los commits. La integración del mapa con `ports-v4` sólo se acepta para metadatos explícitos `compatibleModel=ports-v4-equivalent-capacity-v1`, `ReferenceArea=curtain_area` y `PressureConvention=upstream_stagnation_to_downstream_static`. Se usa una búsqueda de área de capacidad equivalente para igualar el flujo ideal del Cd con el flujo de la interfaz en el estado mixto aceptado; no se multiplica Cd sobre el área ya efectiva de `ports-v4`. El mapa se vuelve a evaluar durante la búsqueda y el resultado final se recalcula/certifica por el camino normal. La búsqueda puede rechazar por falta de capacidad, dirección no cubierta o presión fuera de grilla.

Siguen pendientes matrices generativas reservadas y colección completa de descargas variables para todas las direcciones/régimen; también campañas de motor completo comparando las variantes Cd y medición del costo map-on. La evidencia usa casos sintéticos y analíticos; no equivale al banco exhaustivo de 112 casos para cada contrato y no constituye validación experimental. Esas limitaciones justifican `CONDITIONAL / RESTRICTED`.

La lámina dinámica conserva su estado `EXPERIMENTAL / NOT VERIFIED IN COMPLETE ENGINE`: 5000 RPM, no periódica tras 200 ciclos, residuo `3.422e-3`; permanece su evidencia de 15B y el balance gas-estructura sigue pendiente. No se comenzaron conductos de transferencia 1D ni los trabajos de 15B.2/15B.3.

## Recuperar y reproducir

```powershell
git worktree list
Set-Location E:\dino2-desktop
$env:PATH = 'E:\dino2\.tools\dotnet;' + $env:PATH
 dotnet build Dino2.slnx -c Release
 dotnet run --project tests/Dino2.Desktop.Tests -c Release --no-build

Set-Location E:\dino2-science
$env:PATH = 'E:\dino2\.tools\dotnet;' + $env:PATH
.\scripts\verify.ps1

Set-Location E:\dino2-integration
git log --oneline --decorate -4
```


