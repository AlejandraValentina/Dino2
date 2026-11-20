# Etapa 10 — Integración y multicilíndrico 2T

**CLOSED dentro del dominio numérico declarado para cárteres separados y colectores compactos.**

## Integración de etapa 9

La integración local de etapa 9 es **7e3e2bb**, con padres **a4ef998** y **f813d12**. Se verificaron ramas, worktrees, historial y referencias del remoto. El árbol estaba limpio y el árbol del merge coincide con el cierre de etapa 9. Se comprobó el SHA-256 del portable y se repitieron regresiones y pruebas Windows de Otto, 4T, MUSCL y 2T, incluida recuperación, exportación, estudio, dominio parcial y cancelación. La etapa 10 reside en **stage10/multicilindrico-2t**. [Evidencia de etapa 9](etapa9.md).

## Funcionalidad y arquitectura

Dino2 Desktop incorpora **Multicilíndrico 2T**. Configura dos, tres o cuatro cilindros en línea, cárteres separados, fases, geometría común y redes con admisión y escape conmutables por separado. Incluye curvas, presiones, barrido, atrapamiento, par indicado de gases, A/B, estudios, archivos y exportación. El formato JSON 1 se identifica por familia y modelo. Los estudios guardan curvas y balances por RPM. Para obtener trazas completas se aplica y simula la variante. [Manual](manual-desktop.md).

El motor conserva las ecuaciones de etapa 7. Reutiliza **FiringPhases**, **DuctMesh**, **GasEquation**, **DuctBoundary** y **ManifoldVolume** con 4T. Los cierres de cárter, láminas, lumbreras, barrido y reacciones corresponden al 2T. La expansión compartida va después de la unión y tiene primarios propios. Cada volumen y conducto evoluciona en el mismo paso RK2. [Modelo](modelo-multicilindrico-2t.md).

La prueba de Windows abrió el ejecutable desde el ZIP extraído y comprobó que **coreclr.dll** se cargó desde ese directorio. Ejecutó cálculo, edición de la unión, contraste, CSV, estudio, guardado, reapertura, aplicación como B y cancelación. El recorrido detectó y permitió corregir una pérdida de desfases al repoblar los editores 2T y 4T mientras estaban bloqueados. Las regresiones de ambos módulos abren el archivo con el editor bloqueado y verifican la configuración recuperada.

La última ejecución del portable guardó y reabrió un proyecto con A/B, el contraste de tres mallas y las tres filas del estudio. El contraste nativo tardó 129,74 s. La solución Release compiló sin advertencias y la regresión completa pasó con los modelos históricos y ambos módulos multicilíndricos.

## Resultados y verificación

| RPM | 2 cilindros kW / N·m | 3 cilindros kW / N·m | 4 cilindros kW / N·m |
|---:|---:|---:|---:|
| 4000 | 3,487 / 8,324 | 4,740 / 11,316 | 5,159 / 12,316 |
| 5000 | 3,795 / 7,247 | 4,998 / 9,546 | 5,143 / 9,822 |

- **Conservación y periodicidad.** Las seis soluciones fueron periódicas en 40–53 ciclos, con residuales globales de 1,01 × 10⁻⁴ a 1,44 × 10⁻⁴ frente a 2 × 10⁻⁴ de tolerancia. El máximo absoluto de masa quedó por debajo de 8 × 10⁻¹⁸ kg y el de energía por debajo de 2 × 10⁻¹² J. Se comprobaron especies, elementos, marcador y balances de interfaz. El control de tres ciclos verifica que agotar el límite conserva diagnóstico y omite rendimiento.
- **Límite aislado.** A 5000 RPM, el monocilíndrico 2T de referencia produjo 2067,521 W. El solver conjunto aislado dio 4134,837, 6202,316 y 8269,698 W para dos, tres y cuatro cilindros. La diferencia máxima respecto de sumar el monocilíndrico fue inferior a 0,005 %. Al añadir restricciones de paso en los eventos de otros cilindros cambia levemente la discretización. Se contrastan masa atrapada, trabajo, picos de presión y fase. La integración trapezoidal del par de gases muestreado reproduce el trabajo indicado dentro de 0,8 %.
- **Problemas independientes.** La suite 2T retiene las soluciones cinemáticas, flujo y umbral de láminas, barrido de mezcla perfecta, transporte pasivo y onda reflejada. La prueba de unión de tres tubos de etapa 9 cubre pulso transmitido, retorno, reversión, conservación de especies y energía, y refinamientos de espacio y tiempo. La red 2T mide flujos de interfaz y sus residuos de inventario. [Verificación 2T](etapa7.md) · [Prueba de unión](etapa9.md).
- **Refinamiento temporal separado.** En el bicilíndrico a 5000 RPM y malla de 8/16/8 celdas para ramal de admisión, expansión y primario, reducir el paso máximo de 0,65° a 0,20° pasó de 3789,52 a 3773,31 W, una variación de −0,43 %. El dato caracteriza esa malla y ese régimen.
- **Refinamiento pareado A/B.** A 5000 RPM, cambiar el volumen de unión de escape de 0,55 a 0,75 L dio diferencias de par +0,00689, +0,00797 y +0,00524 N·m en ×1, ×2 y ×4. La banda empírica fue 0,00546 N·m. El contraste clasifica el caso como **sensible a discretización**. La campaña de tres mallas tardó 126,7 s.
- **Estudio factorial.** Dos longitudes de admisión entre 140 y 160 mm y dos avances de 19 y 20° a 5000 RPM. Se conservaron cinco filas incluyendo la base y cuatro configuraciones únicas. Uno y dos trabajadores reprodujeron la misma potencia en cada variante. Los tiempos fueron 27,87 y 22,49 s.

### Sensibilidades en el bicilíndrico a 5000 RPM

Diferencias respecto del ejemplo con ambas redes compartidas, a malla de referencia. El contraste de tres mallas corresponde al cambio de volumen anterior.

| Modificación | Δ potencia al cigüeñal (W) | Δ fresca suministrada (mg) | Δ fresca atrapada (mg) |
|---|---:|---:|---:|
| Admisión independiente | +482,23 | +12,264 | +5,938 |
| Escape independiente | +642,64 | +29,922 | +8,845 |
| Ramal de admisión 160 mm | +3,54 | +0,107 | +0,049 |
| Cárter mínimo 190 cm³ | −90,28 | −3,158 | −1,412 |
| Techo de transfer 44 mm con altura de 10 mm | −2,07 | +1,102 | +0,319 |
| Techo de escape 32 mm | +160,72 | −0,531 | +1,371 |
| Primario de escape 260 mm | −5,37 | −0,122 | −0,064 |
| Fase del segundo cilindro a 170° | −3,40 | −0,012 | −0,038 |

Los dos primeros cambios alteran topología y condición acústica, con la misma geometría numérica de expansión aplicada por rama cuando el escape es independiente. Al aumentar la distancia entre PMS y techo se acorta el tiempo abierto del puerto. El cambio de techo de escape reduce el suministro y retiene más fresca. La discretización aún puede dominar cambios pequeños.

## Rendimiento y reproducibilidad

Las evaluaciones por celda en las dos etapas RK2 dominan el costo. A 5000 RPM se registraron 8,59, 9,81 y 11,62 millones con dos, tres y cuatro cilindros. Se reutilizan los buffers y se registra el ciclo al intentar confirmar periodicidad. Los estudios y contrastes omiten guardar trazas angulares. El paralelismo se aplica entre variantes completas. La interfaz estima el costo antes de ejecutar el estudio.

Medianas de tres ejecuciones a 5000 RPM con trazas, .NET 10.0.15 y Windows x64 en un Intel Core i5-10400 de 12 procesadores lógicos. El monocilíndrico usa la misma plantilla geométrica de la prueba con un escape independiente. Se registró el pico de memoria de cada proceso de consola. No se cambió el criterio periódico para medir.

| Cilindros | Mediana (s) | Asignación administrada (MiB) | Memoria pico (MiB) |
|---:|---:|---:|---:|
| 1 | 3,629 | 0,161 | 26,5 |
| 2 | 7,455 | 0,507 | 26,9 |
| 3 | 10,278 | 0,680 | 28,1 |
| 4 | 10,658 | 0,853 | 27,8 |

~~~powershell
.\scripts\verify.ps1
dotnet run --project tests/Dino2.TwoStroke.Tests -c Release -- --stage10 --probe
dotnet run --project tests/Dino2.TwoStroke.Tests -c Release -- --stage10 --resolution
dotnet run --project tests/Dino2.TwoStroke.Tests -c Release -- --stage10 --sensitivity
1..4 | ForEach-Object { dotnet run --project tests/Dino2.TwoStroke.Tests -c Release -- --stage10 --benchmark $_ }
dotnet run --project src/Dino2.Cli -c Release -- --multi-2t examples/multi-2t-inline2.json artifacts/multi2t.csv artifacts/multi2t-cycle.csv
pwsh -File scripts/smoke-multicylinder-2t.ps1 -Application artifacts/stage10-win-x64/Dino2.Desktop.exe -Destination artifacts/stage10-native-run
~~~

## Alcance de la evidencia

El dominio verificado es el de los ejemplos de cárteres separados y colectores compactos a 4000 y 5000 RPM. Las topologías mixtas se comprobaron en el bicilíndrico a 5000 RPM. Fuera de ese dominio se requiere comprobar periodicidad y resolución en el caso concreto. La evidencia es numérica. Continúan pendientes ensayos independientes y la calibración del motor y de pérdidas en uniones. [Fuentes y metodología experimental](modelo-multicilindrico-2t.md).
