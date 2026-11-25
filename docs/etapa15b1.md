# Dino2 — Etapa 15B.1: consolidación e integración científica de mapas Cd

## Veredicto

**CONDITIONAL / RESTRICTED.** Se recuperaron y separaron los desarrollos de Opus y Codex; los mapas siguen integrados sólo en las formulaciones verificadas que se describen abajo. La etapa no queda lista para declarar sus veinte criterios: falta llevar la integración al escape 1D 2T y a láminas cuando corresponda, una interfaz Desktop de mapas, campañas comparativas completas y referencias independientes más amplias. La dinámica de láminas permanece experimental. No se inicia 15B.2, 15B.3 ni transfers 1D.

## Repositorio y preservación

El checkout original estaba en `ui/mahapps-shell` (`78df3e03`), donde las mejoras de Opus ya estaban confirmadas; las modificaciones científicas de Codex estaban sin confirmar. La recuperación íntegra, incluido bundle Git, árbol tracked, archivos nuevos y artefactos ignorados, está en `E:\dino2-stage15b1-recovery-20261125`. Bundle SHA-256: `A1C2D7B4503E3EA186F503CBAD76701DB9059B364D4A721CD50747B0319ED81E`.

- Opus: `78df3e03d8955438d541430b06c18e12a4772ada`, rama `stage15b/desktop`, worktree `E:\dino2-desktop`.
- Codex: `492a194`, rama `stage15b/science`, worktree `E:\dino2-science`.
- Integración local: rama `stage15b/integration`, worktree `E:\dino2-integration`, base Opus más cherry-pick del commit científico (`df4b873`); sin push ni cambios en `main`.

El inventario, procedencia, captura del estado inicial y pasos de reproducción están en [consolidacion-stage15b.md](consolidacion-stage15b.md). Las capturas Desktop están en `evidence/stage15b1/ui-after` y las salidas generadas en `evidence/stage15b1/desktop-verification`.

## Definición y acoplamiento Cd

El dato declara `Cd = mdot_real / mdot_ideal`, área de referencia, dirección, unidades, origen, hash, convención de presión y modelo compatible. El `Cd` no es `Cc`: un mapa de caudal entre estaciones remotas no identifica por sí solo la vena contracta, la presión de garganta ni la recuperación de presión. Ese tipo de dato se rechaza para una interfaz cuya convención no declare explícitamente. La interpolación mantiene recuperación de nodos, bilinealidad, grilla rectangular completa, valores no negativos y rechazo fuera de dominio.

Convenciones implementadas y separadas por modelo:

| Camino | Contrato declarado | Estado |
|---|---|---|
| 4T 0D | `four-stroke-0d-isentropic-nozzle`; área de cortina; remanso aguas arriba / estática aguas abajo | Integrado antes en 15B; compatibilidad de persistencia cubierta por pruebas existentes |
| 4T 1D `ports-v4` | `ports-v4-equivalent-capacity-v1`; `curtain_area`; misma convención de presión | Integración nueva: búsqueda implícita de área de capacidad equivalente |
| Transfer agregado 2T 0D | `two-stroke-0d-isentropic-nozzle`; `port_open_area`; misma convención de presión | Integración de geometría/mapa en modelo de tobera agregada |
| Escape 1D 2T y láminas | Sin adaptador aceptado en esta etapa | No declarar compatible ni aplicar silenciosamente un Cd de otro modelo |

Para el adaptador 4T 1D, `PortBoundary.MappedFlux` prueba áreas de apertura y para cada una resuelve la interfaz normal de `ports-v4`; consulta el Cd con la presión mixta/estado donante de esa solución y compara caudal másico con la referencia ideal. Una bisección busca la capacidad equivalente. Se vuelve a evaluar el flujo por `ports-v4` con el área resuelta y se registra Cd, residuo e iteraciones. Esto evita multiplicar el Cd sobre el caudal/área efectivos de una solución que ya contiene contracción, expansión y recuperación de presión. No convierte de forma única un mapa medido entre tomas remotas: requiere exactamente la convención compatible indicada.

La dirección selecciona mapas forward/reverse independientes o uno explícitamente bidireccional. Falta de dirección, metadatos incompatibles, cobertura ausente, apertura positiva a presión fuera de la grilla o capacidad insuficiente se informan como error; no hay extrapolación ni fallback constante oculto. Abertura geométrica cero sigue el cierre normal de la interfaz. El solver falla si el residuo de masa no converge dentro de la tolerancia ya fijada; las pruebas cubren los casos sintéticos descritos, pero no toda la envolvente enumerada en el plan.

## Pruebas y resultados

1. Desktop de Opus: compilación Release, 0 warnings/errores y **116/116** checks. Log: `evidence/stage15b1/desktop-tests.log`.
2. Build científico Release: 0 warnings/errores.
3. 4T: **25/25** grupos; incluye el grupo nuevo de integración de Cd con presión de estado aceptado, flujo directo/inverso, masa y fuera de dominio. Masa ideal independiente con expresión isentrópica de gas perfecto y balance conservativo inspeccionado por la interfaz. No se usó este adaptador como banco experimental.
4. Resto de `scripts/verify.ps1`: 13/13 base, 15/15 termoquímica reactiva, 7/7 estudios, pruebas 2T, redes multicilíndricas, Desktop y reproducciones CLI completas; proceso terminó con código 0.
5. Log íntegro y trazas de consola: `evidence/stage15b1/science-regression.log`. CSV/mapa sintético anterior: `evidence/stage15b/cd-map-synthetic.csv`. Reproducción: desde el worktree deseado, poner `E:\dino2\.tools\dotnet` al frente de `PATH` y ejecutar `scripts/verify.ps1`.

La prueba no constituye una regresión exhaustiva de 112 descargas variables, choque caliente/móvil, sensibilidad de todas las mezclas ni semillas reservadas con cada variante de mapa. La regresión base 14C de interfaces se preserva en el conjunto actual de pruebas y pasó, pero esta adaptación aún necesita una suite específica más amplia. Las campañas 4T 11/11 RPM y 2T existentes pasan sin mapas; no se atribuye al mapa ningún cambio de rendimiento completo. No se ajustaron combustión ni fricción. No se midió todavía el costo de una campaña con mapa `ports-v4`; el benchmark 4T existente (11 RPM, ~0.53 s mediana) es baseline sin afirmación de costo diferencial.

## Persistencia, Desktop y lámina

CSV y metadatos tienen SHA-256, carga/validación y pruebas de roundtrip del modelo. No existe aún flujo Desktop para escoger/importar/visualizar el mapa, ver su dominio o alternar a constante; por eso el criterio Desktop de mapas no pasa. La igualdad estructural/hash y reapertura del estado 4T/2T existente pasan en las suites, pero no hay una campaña de exportación/reapertura Desktop de un proyecto que contenga mapas nuevos en todos los modelos.

La lámina modal conserva el fallo de periodicidad de 5000 RPM: residuo `3.422e-3` luego de 200 ciclos, dominante en energía de celda 26 del ducto de escape. Tampoco se ha cerrado el trabajo presión–estructura en el balance energético. Evidencia original: `evidence/stage15b/reed-motor-periodicity.txt`. No se revisa ni se reconstruye en esta etapa.

## Respuestas expresas

1. **¿Se recuperaron íntegramente ambos trabajos?** Sí, con bundle, archivos, capturas y artefactos preservados; las referencias históricas no se reescribieron.
2. **¿Qué commits/ramas?** Opus `78df3e03` (`stage15b/desktop`); Codex `492a194` (`stage15b/science`); integración local `stage15b/integration` desde ambos.
3. **¿Qué worktrees?** `E:\dino2-desktop`, `E:\dino2-science`, e integración `E:\dino2-integration`.
4. **¿Desktop pasó?** Sí, 116/116; compiló sin warnings/errores.
5. **¿Cómo se integró Cd con `ports-v4`?** Con área de capacidad implícita que iguala el caudal ideal del mapa con el flujo aceptado por `ports-v4` en la presión mixta correspondiente; el flujo final se recalcula con el cierre certificado.
6. **¿Convenciones compatibles?** Sólo modelos/áreas/presiones explícitos: 4T 0D (`four-stroke-0d-isentropic-nozzle`), 4T 1D (`ports-v4-equivalent-capacity-v1`, `curtain_area`), y transfer agregado 2T 0D (`two-stroke-0d-isentropic-nozzle`, `port_open_area`). No son intercambiables.
7. **¿Cómo se evita duplicar pérdidas?** El Cd prescribe capacidad másica equivalente; no se multiplica sobre el resultado de masa efectivo previo de `ports-v4`.
8. **¿Qué pasa fuera de cobertura?** Error explícito de dominio/dirección/capacidad; no extrapola ni cae a Cd constante.
9. **¿Se verificó masa, momento y energía?** Sí para la certificación nativa de `ports-v4` en cada solución aceptada; masa Cd se compara con referencia ideal independiente y residuo queda registrado. La suite nueva no cubre de forma independiente todas las magnitudes de toda la matriz de estados, así que el criterio general queda parcialmente verificado.
10. **¿Qué referencias independientes?** Tobera isentrópica analítica de gas perfecto para el caudal Cd, pruebas preexistentes de régimen/masa/ondas y conservaciones 14C, junto a checks de nodos/interpolación/CSV. No hay datos experimentales.
11. **¿Se preservó robustez 14C?** Regresión completa existente, incluido `ports-v4` y casos generativos base, pasó; falta extender la matriz a mapas variables y combinaciones térmicas nuevas.
12. **¿Cambios en motores completos?** No se reportan diferencias causales con mapa: las campañas completas ejecutadas fueron referencia sin mapas; no hay motor completo map-vs-constante aceptado.
13. **¿Costo computacional?** Se añade una búsqueda de hasta 48 iteraciones, cada una resolviendo la interfaz y evaluando el mapa. Falta medir sobre campañas map-on; la cifra baseline 11 RPM no mide esa sobrecarga.
14. **¿Guardado, reapertura y exportación?** Persistence/CSV y pruebas generales pasan; falta comprobar el ciclo completo Desktop de proyectos con mapas para cada contrato.
15. **¿Está preparada 15B.2?** El repositorio sí está organizado, pero el subsistema Cd aún no satisface el alcance completo; veredicto científico **CONDITIONAL / RESTRICTED**, no READY. La lámina conserva fallo conocido y no se tocó.

## Próximos límites de alcance

Cerrar integración y pruebas 2T 1D/láminas aplicables, suite generativa independiente de cobertura y capacidad, campañas comparativas completas con cambios sintéticos claramente etiquetados, costo map-on, y flujo Desktop de mapa con persistencia/exportación; repetir regresión sobre el commit final. La dinámica de lámina sigue pendiente según 15B.2. Transfers 1D siguen fuera de esta solicitud.

