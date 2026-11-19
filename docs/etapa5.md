# Dino2 · Etapa 5 · Estudios paramétricos

**CLOSED para la búsqueda discreta y la verificación numérica descritas.** El portable ejecuta barridos de uno a tres parámetros sobre un motor 4T existente. Compara las curvas de la base y las variantes, ordena las configuraciones elegibles por un objetivo explícito y permite contrastar la resolución antes de aplicar una selección como B. Los modelos conservan su dominio físico anterior. La etapa no aporta validación experimental de preparaciones.

## Integración de etapa 4

`main` avanzó con el merge **`4bb75cf`**. Sus padres son `72f4fe0` y `988d2ae`. La revisión encontró el árbol limpio, cuatro commits de consolidación y una relación de ancestro directo. La comprobación del contenido integrado dio igualdad exacta con `stage4/consolidacion`.

Antes y después del merge pasaron 45 grupos de prueba y 34 comprobaciones WPF. Se reconstruyó el ejemplo A/B MUSCL de cinco regímenes, y el portable de `main` ejecutó Otto, 4T con depósitos y 4T MUSCL de once regímenes con su runtime. La rama `stage5/estudios-parametricos` nació de ese commit después de completar las verificaciones. [Cierre científico de etapa 4](cierre-etapa4.md).

## Estrategia de búsqueda

Se enumera un producto cartesiano finito de uno, dos o tres ejes definidos por el usuario. La misma grilla RPM se prueba para cada variante. Esto incluye pares de longitud y régimen, o de dos parámetros físicos a régimen fijo. Un factorial completo detecta interacciones dentro de los niveles ensayados y permite reproducir cada punto. Un diseño fraccional reduce costo pero puede confundir interacciones. La evolución diferencial explora espacios continuos con evaluaciones estocásticas y no aporta una verificación directa de una diferencia pequeña cerca del ruido del solver. La etapa elige la grilla finita por su trazabilidad, costo anunciado y adecuación a un número pequeño de parámetros. El rango propuesto queda bajo control del usuario. [NIST, diseño factorial completo](https://itl.nist.gov/div898/handbook/pri/section3/pri333.htm) · [SciPy, evolución diferencial](https://docs.scipy.org/doc/scipy/reference/generated/scipy.optimize.differential_evolution.html).

Se admiten avance, compresión, alzada y apertura/cierre de admisión y escape, presión absoluta y temperatura de ambos depósitos. Longitudes y diámetros terminales aparecen solo en modelos que tienen conductos. Las dimensiones de conductos y alzadas se ingresan en mm, y las presiones en kPa absolutos. El solver mantiene sus límites y validaciones de eventos. La leva sigue siendo la representación geométrica de la etapa 4. No se verifica interferencia mecánica pistón/válvula. Una combinación fuera de dominio figura con su diagnóstico sin detener a las demás.

La base y todas las variantes usan copias inmutables. Cuando cambia una longitud, se escala y redondea el número de celdas para aproximar Δx de la base, con el mínimo de ocho celdas del solver. El máximo físico admitido es 256. Se muestra la malla aplicada de cada caso seleccionado. Una geometría que exige más de 256 celdas queda inadmisible. El modelo original, la formulación de combustión y los demás parámetros permanecen vinculados a la configuración de cada caso.

## Objetivos

Los indicadores se evalúan al cigüeñal. Cada extremo de una banda debe coincidir con un régimen calculado. Las RPM de la grilla llegan al fin indicado. Un punto único es válido para los objetivos de banda y reporta el valor de ese régimen.

| Objetivo | Definición |
|---|---|
| Potencia máxima | Mayor potencia entre las RPM calculadas en la banda |
| Par máximo | Mayor par entre las RPM calculadas en la banda |
| Potencia media / par medio | Integral trapezoidal en RPM dividida por el ancho de banda, con ponderación uniforme por RPM |
| Compromiso baja/alta | Maximiza el par medio en alta si el par medio en baja cae como máximo los N·m permitidos respecto de la base |
| Consumo específico mínimo | 3,6×10⁹ por el cociente del flujo másico integrado en RPM y la potencia integrada en RPM, en g/kWh |

El consumo usa masa neta de combustible por ciclo y frecuencia RPM/120. Se habilita en los modelos termoquímicos con balance de combustible y requiere flujo neto y potencia positivos en toda la banda. Un punto sin potencia útil deja el objetivo indefinido. El cociente de integrales pondera energía útil del barrido y no equivale a un ciclo de conducción. Las bandas de baja y alta son separadas para el objetivo con restricción. Se pueden ejecutar estudios distintos para objetivos distintos; el ranking depende del criterio elegido.

## Resolución de diferencias

El primer orden de la tabla es el ranking de las muestras convergidas y elegibles en la resolución de entrada. Un contraste posterior compara una variante con la base o con otra variante, sobre la banda completa del objetivo. Usa los casos ya simulados y dos niveles nuevos de resolución. Los modelos 1D multiplican celdas por 2 y 4 y reducen el paso angular máximo en la misma proporción. Al afinar Δx se reduce el paso permitido por CFL. En todos los modelos se estrechan tolerancias hasta los límites del solver, y se mantiene la condición periódica. Las restricciones se comprueban de nuevo en cada nivel. Si dos variantes se comparan con el objetivo de compromiso, la base también se refina para evaluar sus márgenes.

La ventaja es el objetivo de la variante menos el de referencia, con signo invertido para minimizar consumo. La banda empírica es dos veces el mayor cambio de ventaja entre niveles consecutivos, criterio heredado de la etapa 4. Signo persistente, contracción de la variación y ventaja fina fuera de esa banda permiten indicar superior o inferior **respecto de la referencia verificada**. Una diferencia final dentro de la banda con tendencia estable queda numéricamente indistinguible. Cambios de signo, crecimiento de variación o sensibilidad de la restricción producen una comparación sensible. Si falla una corrida, excede el presupuesto o falta periodicidad, el contraste queda sin evidencia para declarar superioridad. El resultado guarda las tres evaluaciones del objetivo, la clasificación, el costo y la referencia. La banda no es intervalo de confianza, estimador general del error ni una afirmación de convergencia asintótica. [NASA Glenn, comprobación de convergencia espacial](https://www.grc.nasa.gov/www/wind/valid/tutorial/spatconv.html).

El alcance de la recomendación es la configuración muestreada y la referencia contrastada. El usuario puede contrastar candidatos cercanos entre sí. El estudio no busca máximo continuo entre muestras ni refina automáticamente a todos los rivales. Las diferencias de la gráfica tienen símbolos en las RPM que se ejecutaron. Las líneas solo unen muestras. El mapa 2D usa celdas medidas, y los huecos señalan configuraciones sin puntuación.

## Arquitectura y costo

`Dino2.Core/Studies` prepara la grilla, evalúa el objetivo, coordina el barrido y ejecuta contrastes. Usa `EngineSimulation` sin alterar ecuaciones. Las excepciones tipadas de dominio y periodicidad distinguen condiciones inadmisibles, no convergencia y fallos aritméticos. `Dino2.Storage/StudyFiles.cs` guarda la base, grilla, resultados, identidad de modelo, diagnóstico y contrastes en un esquema separado del proyecto 4T. La pestaña **Estudios** reúne el plan, el ranking, curvas, diagnósticos por RPM y sensibilidad. La configuración seleccionada puede aplicarse como B sin tocar A.

El costo máximo inicial es 96 variantes y 240 puntos RPM únicos con depósitos, o 120 con conductos. Se aplica también para 1D una cota de trabajo relativo de 240 unidades, contando celdas, inverso del menor Δx, duración del ciclo proporcional a 1/RPM y factor de CFL. Se normaliza por 6400 celdas por metro a 3000 RPM y CFL 0,45. Es una cota operativa y no un tiempo previsto. El contraste permite hasta 72 puntos RPM adicionales y 500 unidades 1D. Las validaciones de tamaño ocurren antes del cálculo. En estado inicial identificado como único, la base se ejecuta primero y las configuraciones exactamente iguales reutilizan su resultado. Para cada estudio hay entre uno y cuatro trabajadores. Los contrastes usan el paralelismo del estudio dentro de cada nivel, con no más de tres curvas independientes. Los resultados se ordenan por identificador para que la llegada de resultados no cambie la grilla ni el ranking.

El solver puede mantener una traza completa por trabajador durante una corrida. El estudio retiene solo los puntos RPM con balances, potencia, trabajo, par, VE, combustible y estado, y libera las trazas de los ciclos. La cancelación conserva los casos terminados y una corrida sin ciclo convergido no deja curva parcial. Se puede guardar y recuperar un estudio parcial para examinarlo o exportarlo. Ejecutar otro estudio vuelve a generar su grilla. No se incluye reanudación automática desde un archivo parcial. Los contrastes ya realizados del mismo par se reutilizan en la sesión.

## Evidencia de resultados y rendimiento

En este equipo Windows x64, .NET 10 Release y 12 procesadores lógicos, las pruebas produjeron exactamente las mismas curvas y puntajes al variar trabajadores. Los tiempos son ejecuciones observadas y dependen de la carga. El conjunto de resultados conserva tiempo por curva, bytes asignados durante cada ejecución y máximo de memoria residente acumulado del proceso. Este último incluye estudios previos de la misma corrida de benchmark.

| Estudio | Puntos RPM únicos | 1 trabajador | 2 trabajadores | 4 trabajadores | Memoria asignada por barrido |
|---|---:|---:|---:|---:|---:|
| Avance 5–35° APMS, v3, 2000–4000 RPM | 21 | 3,40 s | 1,91 s | 1,40 s | 22,7 MB |
| Longitud × diámetro de admisión, MUSCL, 4000–5000 RPM | 8 | 14,11 s | 10,12 s | 6,98 s | 9,6 MB |

En secuencial, cada curva de tres RPM del caso v3 estuvo en torno a 0,45 s tras la primera. Las curvas MUSCL de dos RPM rondaron 3,2–3,9 s. El máximo residente acumulado de la sesión terminó en unos 73 MB. El perfil de etapa 4 identificó el costo de reconstrucción; el benchmark de etapa 5 muestra que dicho costo y el refinamiento del solver dominan el tiempo de las grillas. El contraste de la mejor variante muestreada contra su base pasó de 6,4 a 3,2 s en el caso v3 y de 142 a 78 s en MUSCL al procesar pares en paralelo. Las diferencias por nivel se mantuvieron. Las dos fases se midieron en ejecuciones distintas y el tiempo no es garantía de rendimiento.

El avance de **15° APMS** lideró la muestra del ejemplo v3 para potencia media. Los tres niveles del contraste contra la base de 20° dieron **+54,16 / +54,14 / +54,14 W**, una banda observada de unos 0,047 W. En MUSCL, **490 mm de longitud y 50 mm de diámetro en depósito** lideraron la grilla para par medio. La ventaja frente a 350 mm y 42 mm fue **+1,303 / +1,300 / +1,299 N·m**, con banda de unos 0,0047 N·m. Los mejores casos y los contrastes corresponden al motor sintético, el objetivo y las RPM elegidas.

## Uso y verificación

1. Abrí un proyecto 4T y entrá a **Estudios**. Capturá el motor actual o el A conservado.
2. En **Plan**, seleccioná los parámetros, valores inicial/final/incremento, RPM y objetivo. El costo aparece antes de pulsar **Ejecutar estudio**.
3. En **Resultados**, ordená la tabla, inspeccioná estados y elegí un caso. **Curvas** y **Puntos** muestran las RPM ejecutadas y diagnósticos. El mapa de **Sensibilidad** permite escoger una celda calculada.
4. Para verificar una diferencia, elegí base o rival y pulsá **Contrastar resolución**. Esperá las dos mallas nuevas o cancelá sin descartar la grilla.
5. Guardá el estudio o exportalo a CSV. **Aplicar selección como B** carga su configuración exacta. Pulsá **Simular** en el 4T para regenerar el ciclo y recuperar la comparación manual con A.

`examples/study-spark-v3.dino-study.json` y `examples/study-ducts-muscl.dino-study.json` se abren desde **Abrir estudio…** e incluyen grillas completas y un contraste. El CSV identifica las filas pendientes y fallidas, y registra el plan y la resolución en metadatos.

```powershell
.\scripts\verify.ps1
dotnet run --project tests/Dino2.Studies.Tests -c Release -- --benchmark
.\scripts\publish.ps1 -Destination artifacts/stage5-win-x64 -Archive artifacts/Dino2-stage5-win-x64.zip
.\scripts\smoke-publish.ps1 -Application artifacts/stage5-win-x64/Dino2.Desktop.exe
.\scripts\smoke-studies.ps1 -Application artifacts/stage5-win-x64/Dino2.Desktop.exe
```

La suite suma 52 grupos de prueba de código y 43 comprobaciones WPF. La referencia independiente usa fórmulas analíticas para las ponderaciones, el consumo, las restricciones y el máximo conocido de una superficie con interacción. Las pruebas físicas revisan independencia, deduplicación, media y ranking, malla comparable, fallos, persistencia y clasificación de resolución. El portable se prueba en Windows con el runtime incluido, guardado, reapertura, exportación y aplicación como B. Los artefactos y el informe permiten distinguir esa verificación funcional y numérica de la validación experimental, que sigue pendiente.
