# Consolidación de Etapa 15B

Fecha de corte: 2026-11-25. Estado del checkout original inspeccionado antes de organizar: `ui/mahapps-shell` en `78df3e03d8955438d541430b06c18e12a4772ada`, con padre `af937878ac8d3e4ed9258bae4cb3b6fc7b7fbd4c`. El reflog mostró que Opus había confirmado sus cambios en `78df3e0`; el trabajo científico de Codex seguía como modificaciones y archivos nuevos basados en 15A. Por lo tanto, no se volvió a confirmar ni a reescribir el commit de Opus.

## Preservación y procedencia

Antes de crear los worktrees se guardó una recuperación completa en `E:\dino2-stage15b1-recovery-20261125`: bundle Git SHA-256 `A1C2D7B4503E3EA186F503CBAD76701DB9059B364D4A721CD50747B0319ED81E`, parche del árbol tracked, copia de archivos no rastreados, estado/reflog y copias de los artefactos ignorados. El registro de las ramas y el reflog está en ese directorio. No se ejecutaron `reset --hard`, `clean` ni se reescribieron referencias históricas.

| Desarrollo | Rama | Commit | Worktree | Contenido |
|---|---|---|---|---|
| Opus Desktop | `stage15b/desktop` (también `ui/mahapps-shell`) | `78df3e03d8955438d541430b06c18e12a4772ada` | `E:\dino2-desktop` | MahApps shell, navegación, `ScientificPlot`, `FieldValidation`, selectores 4T, manual/README y pruebas Desktop |
| Codex científico | `stage15b/science` | `492a194` | `E:\dino2-science` | mapas Cd, CSV/metadatos, persistencia, integración de mapa 4T 0D, adaptación a `ports-v4`, modelo de lámina experimental y pruebas/evidencias |
| Integración local | `stage15b/integration` | `df4b873` más el commit de documentación de consolidación | `E:\dino2-integration` | base Opus más cherry-pick reproducible del commit científico y documentación de cierre |

El commit científico no modifica los archivos de shell/manual de Opus. El commit Desktop toca 27 archivos, todos en Desktop, pruebas Desktop, README y manual. Las capturas originales permanecen en `E:\dino2-desktop\artifacts`; la copia probatoria está en `evidence/stage15b1`. Las pruebas Desktop dejaron 116 líneas `PASS` (log incluido). Cada worktree produce sus propios `bin/obj`, artefactos y logs. La SDK 10.0.404 usada para compilar vive en `E:\dino2\.tools\dotnet` y se invocó por PATH; no se copió dentro de los worktrees.

## Inventario científico recuperado

Incluye `DischargeMap.cs`, `DischargeMapFile.cs`, campos de mapa forward/reverse en válvulas y lumbreras, igualdad/hash estructural y serialización, integración previa 4T 0D, integración adicional 4T 1D en `PortBoundary`/`DuctCoupling`, integración agregada 2T 0D, la lámina modal experimental, pruebas de integración y `evidence/stage15b` (incluida la falla de periodicidad). La copia de recuperación preserva también cualquier archivo no rastreado e ignorado que no se incorporó al commit.

## Resultado de verificación

- Desktop en `E:\dino2-desktop`: compilación Release correcta, 0 warnings/0 errores; 116/116 checks (`evidence/stage15b1/desktop-tests.log`).
- Científico en `E:\dino2-science`: `scripts/verify.ps1` finalizó con código 0; log íntegro en `evidence/stage15b1/science-regression.log`. Incluye build sin warnings, 13/13 generales, 25/25 grupos 4T, 15/15 reactivos, 7/7 estudios, campaña 2T, redes multicilíndricas, 4T Desktop, 116 Desktop checks y reproducciones CLI 4T/2T.
- El grupo 4T añadido verifica el flujo con mapa Cd variable dentro de `ports-v4`, flujo directo e inverso, evaluación con presión de cara aceptada y rechazo de consulta fuera de dominio. El benchmark existente del sweep 4T midió mediana cercana a 0.53 s para 11 RPM, pero ese número corresponde al modelo de referencia, no al costo de mapa `ports-v4` en una campaña completa.
- La rama integrada se creó desde `78df3e0` y se le aplicó `492a194` por cherry-pick (`df4b873`). La regresión completa registrada pertenece al mismo contenido de código científico, antes del commit sólo documental de consolidación.

## Diferencias y limitaciones

No hubo conflictos al combinar los commits. La integración del mapa con `ports-v4` sólo se acepta para metadatos explícitos `compatibleModel=ports-v4-equivalent-capacity-v1`, `ReferenceArea=curtain_area` y `PressureConvention=upstream_stagnation_to_downstream_static`. Se usa una búsqueda de área de capacidad equivalente para igualar el flujo ideal del Cd con el flujo de la interfaz en el estado mixto aceptado; no se multiplica Cd sobre el área ya efectiva de `ports-v4`. El mapa se vuelve a evaluar durante la búsqueda y el resultado final se recalcula/certifica por el camino normal. La búsqueda puede rechazar por falta de capacidad, dirección no cubierta o presión fuera de grilla.

No están implementados ni aceptados en este cierre: mapa en el escape 1D 2T; mapas en lámina cuasiestática/dinámica; controles/visualización/importación de mapas en Desktop; matrices generativas reservadas, colección completa de casos de descarga variables, ni campañas de motor completo comparando las cinco variantes Cd. La evidencia independiente se concentra en los casos de interfaz sintéticos agregados en `Interface15BChecks`; no equivale al banco de referencia de 112 casos para todos los mapas/direcciones/régimen. Tampoco hay una validación experimental. Esas limitaciones justifican `CONDITIONAL / RESTRICTED`.

La lámina dinámica conserva su estado `EXPERIMENTAL / NOT VERIFIED IN COMPLETE ENGINE`: 5000 RPM, no periódica tras 200 ciclos, residuo `3.422e-3`; permanece su evidencia de 15B y el balance gas-estructura sigue pendiente. No se comenzó transfers 1D, 15B.2 ni 15B.3.

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
