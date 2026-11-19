# Etapa 6 · Motor 2T de láminas con escape de expansión

**Veredicto CLOSED dentro del dominio numérico declarado.**
La etapa 5 se integró en `main` con el merge `3a41404`. Se verificaron las regresiones de Otto y 4T, la integración WPF de estudios, el contraste por refinamiento y el portable derivado de ese commit. El motor 2T reside en `stage6/motor-2t`. El [modelo 2T](modelo-2t.md) documenta las ecuaciones, unidades, interfaces, hipótesis y procedimientos de interpretación.

## Recorrido Windows

1. Extraé todo el ZIP Windows x64 y ejecutá `Dino2.Desktop.exe`. Desde el 4T, elegí **Motor 2T**.
2. Abrí `examples/2t-125cc.json` o usá **Ejemplo 2T**. El selector de la izquierda organiza la geometría, RPM, combustión, cárter, láminas, transfers, escape y resolución. Las presiones son absolutas, las distancias geométricas se muestran en mm y los volúmenes en cm³. El archivo JSON usa SI.
3. Pulsá **Simular 2T**. Se informa el estado por RPM y se conservan los puntos ya procesados al cancelar. **Resultados** muestra rendimiento y diagnósticos. **Ciclo y flujos** permite seleccionar el régimen, examinar presiones, temperaturas, caudal, composición residual y exportar la traza.
4. **Conservar como A** fija el original. Modificá un parámetro y simulá B. En **A / B**, el contraste usa la RPM elegida y tres resoluciones. Para el caso reproducible, abrí `examples/2t-125cc-escape-ab.json`, pulsá **Simular 2T**, seleccioná 5000 RPM y verificá malla. El proyecto guarda ambas curvas y el último contraste. **Exportar CSV 2T…** genera la curva B, las diferencias a RPM comunes y, cuando existe, un CSV adicional con el contraste de resolución.
5. En **Estudios 2T**, seleccioná el motor base y uno o dos parámetros. Introducí inicio, fin y paso en las unidades indicadas. Los extremos de la banda deben coincidir con RPM de la grilla del motor base. Con banda vacía se usa toda la grilla. Se admiten potencia o par máximos, medias ponderadas por RPM, par de alta con restricción en baja y BSFC. **Previsualizar costo 2T** informa las evaluaciones distintas. Las tablas conservan el orden preliminar de objetivos, los fallos y la evidencia de cada contraste completado.
6. Seleccioná una fila para ver sus curvas y diagnósticos por RPM. **Verificar candidato 2T** contrasta con la base del estudio. **Aplicar como B** copia la variante conservando A. Volvé a simular B para recuperar la traza angular. Los propios botones del estudio permiten guardar, recuperar y exportar.

## Verificación numérica

La batería nueva verifica la cinemática en PMS y PMI, el volumen mínimo del cárter, la distribución y el área de lumbreras, la ley cuasiestática de láminas y el límite sin combustión. También verifica la solución hidrostática y el volumen geométrico del escape segmentado. Un pulso débil en una expansión de 40 a 80 mm produce una rarefacción de vuelta al origen. El coeficiente analítico para un salto abrupto es −0,60. En la transición de 30 mm simulada se obtuvo −0,454 con 96 celdas y −0,529 con 192. Ambos regresos se midieron alrededor de 1,8 ms, compatibles con el trayecto de ida y vuelta a velocidad acústica local. Las pruebas de Sod, reflexión rígida y convergencia del solver 1D compartido se mantienen en la batería anterior.

En el ejemplo encendido, los residuos del balance global están del orden de $10^{-18}$ kg y $10^{-12}$ J. Las pruebas comprueban cada especie contra las extensiones de reacción y C, H, O y N por separado. También comprueban el marcador de procedencia, la identidad de masa fresca al cierre, el límite motorizado con potencia negativa al cigüeñal, la determinación de BSFC por un ciclo por vuelta, la reproducibilidad con y sin traza y la ausencia de resultado periódico cuando se limitan los ciclos a tres. La integración trapezoidal independiente de la traza B a 4000 RPM dio 32,605 J en el cilindro y −3,04172 J en el cárter. Las integrales del solver fueron 32,608 J y −3,04181 J. Los límites automatizados son 2 × 10⁻¹⁴ kg para masa, especies, elementos y marcador y 10⁻⁸ J para energía. Son límites de regresión para errores de balance, distintos de la sensibilidad de la solución al discretizar.

### Curva base sintética

Motor de 124,8 cm³, compresión geométrica 11, cárter mínimo 180 cm³, admisión 100 kPa y 320 K, escape 100 kPa y 300 K, avance 19°, mezcla estequiométrica y Wiebe de 1,10 ms. Mallas de 12 y 32 celdas, CFL 0,44, máximo angular 0,65° y tolerancia periódica 5 × 10⁻⁵.

| RPM | Indicada neta [kW] | Cigüeñal estimada [kW] | Par [N·m] | Fresca atrapada [mg] | Ciclos | Error periódico |
|---:|---:|---:|---:|---:|---:|---:|
| 4000 | 1,974 | 1,227 | 2,930 | 34,50 | 67 | 3,88e−5 |
| 5000 | 3,117 | 2,069 | 3,951 | 41,45 | 51 | 2,99e−5 |
| 6000 | 2,966 | 1,629 | 2,592 | 33,17 | 67 | 4,50e−5 |

A 5000 RPM, la entrega bruta por transfers fue 68,96 mg. Volvieron 7,06 mg hacia el cárter y 4,12 mg desde el escape, y salieron 24,57 mg frescos hacia el escape. Al cierre quedaron 41,45 mg frescos y 28,74 mg de carga vieja. Se obtuvieron delivery ratio 0,486, eficiencia de atrapamiento 0,567, eficiencia de barrido 0,591 y eficiencia de carga 0,292. El pico térmico calculado fue 2371 K. La combustión congelada elevó el resultado a 2401 K y la potencia estimada de 2068,66 a 2083,93 W con el resto de entradas fijo. Este ejercicio mide el efecto del cierre termoquímico en el modelo.

### Modificaciones y resolución a 5000 RPM

El estudio de un eje prueba difusores de 250 y 290 mm contra la base de 270 mm. A 290 mm, se calculan 41,96 mg atrapados frente a 41,45 mg en A, con un aumento preliminar de 38,90 W. Al resolver las ondas con más celdas, los dos resultados se acercan. El contraste conserva tamaños de celda parecidos ajustando las celdas de escape de 32 a 33 en la variante. No se modifica el exponente Wiebe ni la fricción para obtener este resultado.

| Refinamiento | A [N·m] | B [N·m] | B − A [N·m] |
|---:|---:|---:|---:|
| ×1 | 3,95084 | 4,02514 | +0,07430 |
| ×2 | 4,05586 | 4,07008 | +0,01422 |
| ×4 | 4,05293 | 4,07901 | +0,02608 |

La banda observada es 0,12015 N·m. **Numéricamente indistinguible** en este contraste. A malla base y mitad de paso angular y CFL, la potencia de referencia A pasó de 2068,66 a 2068,05 W. El esquema espacial de primer orden dio 1879,80 W y 38,94 mg atrapados con la misma geometría. El efecto dominante del ejemplo es la resolución espacial y de la onda. El estudio de dos ejes, difusor de 250 o 290 mm y avance de 19° o 21°, resolvió sus cinco casos, incluida la base. A 290 mm, el avance de 19° dio 2107,56 W y el de 21° dio 2092,15 W en la malla base. Se trata de un orden preliminar del objetivo.

El tiempo de asentamiento se verificó por separado. Al exigir tolerancia periódica de 1 × 10⁻⁵ con la misma malla, A necesitó 64 ciclos en vez de 51. La potencia cambió −0,020 W y la masa fresca atrapada, −0,00025 mg. Esto acota el efecto del residuo entre ciclos para este caso.

A continuación se varía una sola entrada de A a 5000 RPM. Los puntos alcanzaron periodicidad y conservaron los balances. Los cambios se calcularon en la malla base y carecen de un veredicto de resolubilidad individual.

| Entrada cambiada | Δ cigüeñal [W] | Δ fresca atrapada [mg] |
|---|---:|---:|
| Cárter mínimo de 180 a 160 cm³ | +114,84 | +1,81 |
| Lámina de 850 a 700 N/m | +29,79 | +0,38 |
| Techo de escape de 31 a 30 mm | −46,80 | −0,08 |
| Garganta de transfers de 800 a 450 mm² | −4,79 | −0,06 |
| Compresión geométrica de 11 a 11,5 | +44,33 | +0,13 |
| Extracción relativa fresca de 1 a 0,75 | +82,27 | +1,67 |

El cárter más pequeño elevó el delivery ratio de 0,486 a 0,509. El techo de escape más alto adelantó la apertura, reduciendo el tramo de expansión cerrado. La sensibilidad al cierre de barrido es similar a la de varios cambios mecánicos. Medir la separación entre carga fresca y residual es importante para atribuir diferencias de par a una modificación real.
## Rendimiento

Windows x64, Intel Core i5-10400, 6 núcleos y 12 hilos lógicos, runtime .NET 10.0.15, Release. Cada cifra corresponde a una ejecución. La concurrencia mantiene resultados idénticos en los campos comparados. La memoria indicada es el conjunto de trabajo del proceso de consola, aproximada y dependiente del runtime.

| Carga | Tiempo |
|---|---:|
| Curva de 4000, 5000 y 6000 RPM secuencial | 15,19 s |
| Ejecuciones de esas RPM en paralelo, 3 trabajadores | 6,18 s |
| Base y dos difusores, 1 trabajador | 12,24 s |
| Base y dos difusores, 2 trabajadores | 8,07 s |
| Base y dos difusores, 4 trabajadores | 7,87 s |
| Base y factorial de dos ejes, 2 trabajadores | 12,07 s |
| Refinamiento selectivo ×2 y ×4 del par candidato/base | 161,80 s |

A 5000 RPM, A tomó 3,91 s en secuencia, ~77 ms por ciclo y 43 361 pasos aceptados, sin pasos rechazados en esa ejecución. Las dos variantes usaron 46 809 y 47 860 pasos. Durante la batería de barridos, el proceso llegó a aproximadamente 30 MB de conjunto de trabajo. Al doblar celdas crecen tanto el costo por evaluación de caras como el número de evaluaciones por CFL. El mayor costo observado fue el refinamiento, con reconstrucción y conversiones termoquímicas por celda y paso. Se usan búferes por simulación y se evita guardar trazas por variante. La curva interactiva estándar se mantiene secuencial con progreso por RPM. La ejecución paralela por variantes, el reuso de duplicados y el refinamiento selectivo ya están habilitados. El ensayo de RPM en paralelo mide el beneficio potencial adicional sin introducir dependencia de estados convergidos entre puntos.

## Evidencia experimental y alcance

- **Referencia de motor y láminas.** Krieger y colaboradores describen un modelo de motor encendido con cárter y lámina y comparaciones experimentales. La ficha SAE no proporciona el conjunto de entradas y trazas. El trabajo abierto de Booy incluye oscilogramas a 2500 y 4500 RPM y potencia al freno de un motor derivado de un fuera de borda, con y sin soplador. El caso publicado usa escape atmosférico y no proporciona insumos suficientes para reproducir con esta configuración la geometría, el escape y la alzada de láminas. [SAE 690135](https://saemobilus.sae.org/papers/simulation-a-crankcase-scavenged-two-stroke-si-engine-comparisons-experimental-data-690135) · [Booy, actas WSC, 1969](https://informs-sim.org/wsc69papers/1969_0044.pdf).
- **Escape de expansión.** Ortenzi y Bossaglia publican hardware, simulación 0D–1D, puntos medidos y curvas normalizadas de kart. La declaración de disponibilidad restringe las series de datos. Las curvas permiten contraste cualitativo y no permiten identificar y evaluar nuestro modelo con condiciones idénticas. [Energies 16(13), 4947](https://www.mdpi.com/1996-1073/16/13/4947).
- **Barrido.** Foudray y Ghandhi miden gases atrapados en un 2T de inyección directa. El cambio de estrategia de combustible impide trasladar directamente los resultados de cortocircuito de combustible a la admisión premezclada de Dino2. [SAE 2003-32-0081](https://saemobilus.sae.org/papers/scavenging-measurements-a-direct-injection-two-stroke-engine-2003-32-0081).

El ejemplo no ha sido calibrado. Faltan mediciones sincronizadas de presión angular en cilindro, cárter y boca de escape, geometrías completas, alzada de lámina, condiciones de depósito, AFR, consumo y par. Para identificar parámetros se requieren ensayos de descarga o de alzada, motorizados para pérdidas y un subconjunto encendido para estimar fase de combustión, transferencia térmica y barrido. La evaluación independiente debe usar RPM y al menos una geometría de escape reservadas, con errores de presión angular, fase, llenado y par reportados junto con incertidumbre instrumental y numérica. Aquí no se atribuye error experimental ni precisión absoluta al ejemplo. La evidencia numérica de motor se concentra en la geometría sintética y en 4000–6000 RPM, con contraste explícito de escape a 5000 RPM. Las entradas editables abarcan más casos y pueden exigir otra verificación de convergencia y periodicidad.

## Reproducción

```powershell
.\scripts\verify.ps1
dotnet run --project src/Dino2.Cli -c Release -- --2t examples/2t-125cc.json artifacts/2t-base.csv artifacts/2t-trace.csv
dotnet run --project src/Dino2.Cli -c Release -- --2t examples/2t-125cc-escape-ab.json artifacts/2t-ab.csv
dotnet run --project tests/Dino2.TwoStroke.Tests -c Release -- --benchmark
dotnet run --project tests/Dino2.TwoStroke.Tests -c Release -- --sensitivity
.\scripts\publish.ps1 -Destination artifacts/stage6-win-x64 -Archive artifacts/Dino2-stage6-win-x64.zip
.\scripts\smoke-two-stroke.ps1 -VerifyResolution
```

El portable superó la comprobación con UI Automation en Windows, incluido el refinamiento A/B, la recuperación y exportación de su evidencia, el guardado y reapertura de estudios y la cancelación. Windows cargó coreclr.dll desde la carpeta del portable. Los artefactos se generan fuera de Git. La ejecución CLI guarda un CSV de cada punto, genera la comparación al encontrar A y devuelve código no cero si falta un régimen periódico en la curva actual.
