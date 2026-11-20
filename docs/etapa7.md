# Etapa 7 · Consolidación científica del 2T

**CLOSED dentro del dominio numérico verificado.**

La etapa 6 quedó integrada en **94f20a5**. El árbol del merge coincide con el de **79c0597** y conserva sus cuatro commits. Se ejecutaron las regresiones antes y después de integrar, y se verificó un portable Windows de la base. La consolidación posterior vive en **stage7/consolidacion-2t**.

## Dominio analizado

Se estudió el ejemplo sintético de 124,8 cm³ con admisión por láminas, cárter y cilindro 0D y escape de expansión 1D. El modelo térmico y de combustión se mantiene trazable al [modelo de etapa 6](modelo-2t.md). Se identifican por separado sus parámetros supuestos, los errores de malla y la evidencia experimental.

| Subsistema | Supuesto y alcance |
|---|---|
| Láminas | Fuerza cuasiestática de presión contra rigidez y precarga, con tope y contacto unilateral. Representa restricción y pulsos de presión, con flujo a cárter. Su validez dinámica depende del cociente entre la escala temporal del pulso y la frecuencia del pétalo. Quedan fuera la inercia, el flutter y el reflujo por cierre tardío. |
| Cárter y lumbreras | Cárter adiabático de volumen variable. El instante de apertura, altura efectiva y garganta se calculan por cinemática. Quedan fuera pérdidas térmicas del cárter y detalles del chorro. |
| Transfers | Tobera compresible reversible de garganta equivalente. Conserva especies y entalpía entre los volúmenes. La longitud, orientación e inercia de los transfers requieren un modelo y mediciones adicionales. |
| Barrido | Inventarios de carga etiquetada y antigua, con temperatura y presión comunes. El cociente de extracción permite estudiar segregación en la salida. Esta formulación no estima la dirección espacial del chorro ni capas a distintas temperaturas. |
| Combustión y calor | Wiebe con fase, duración y conversión prescritas. Equilibrio UV restringido mientras las lumbreras están cerradas. Hohenberg con temperatura y factor estimados. La estabilidad física de una combustión real cerca del ralentí queda fuera de la prueba de periodicidad determinista. |
| Escape y uniones | Euler cuasi-1D conservativo, HLLC/HLL y MUSCL o primer orden, con secciones cónicas continuas. Contornos por depósito y acoplamiento de tobera con característica linealizada. Se omiten calor, viscosidad y ondas multidimensionales. La temperatura modifica la propagación y la impedancia. |
| Potencia y par | Integrales de presión-volumen de cilindro y cárter. La estimación de cigüeñal resta FMEP dependiente de velocidad media y presión pico. El par de banco y las pérdidas mecánicas requieren medición independiente. |

Para láminas se consideró el oscilador unilateral de primer modo, excitado por la presión de ambos lados. La evidencia disponible sí confirma que puede importar a alta velocidad, pero mover masa, rigidez y amortiguamiento sin alzada medida haría indeterminados los resultados. Se conserva el cierre estático y se mide cómo el conducto modifica su excitación. Los trabajos de Benson y Sher muestran que desplazamiento, mezcla y cortocircuito pueden suceder juntos. Los parámetros adicionales de estas familias necesitan fracciones medidas o datos de puertos y chorros. Se conserva el cierre de dos inventarios y se verifica su límite homogeneizado. [SAE 972738](https://doi.org/10.4271/972738) · [Benson, 1977](https://doi.org/10.1016/0020-7403(77)90056-X) · [Sher y Harari, 1991](https://doi.org/10.1243/PIME_PROC_1991_205_019_02).

## Defectos corregidos y trazabilidad

- El limitador MUSCL previo podía arrastrar presión y velocidad al limitar la pendiente del marcador pasivo y admitía extrapolación superior a uno en una cara. El 2T limita el marcador por separado en [0,1]. Limita caras, con la misma densidad pasiva conservada en la celda. Una prueba manufacturada verifica que variar solo el marcador deja idénticas las presiones reconstruidas y que las caras respetan el intervalo. El operador 4T usa su política previa.
- El integrador admite la fracción de paso diminuta al aterrizar en un evento cinemático si ese es el paso restante. Se conserva el límite para reintentos internos y se rechazan pasos no finitos. Un punto cuyo encendido invade el intervalo abierto queda fuera de dominio en ese régimen. El resto del barrido se calcula y sus resultados se conservan.
- Una fila con falta de periodicidad registra ciclos, residuo final y anterior, variable dominante, pasos aceptados y rechazados. La potencia y el par quedan ausentes si el régimen falla. La interfaz muestra estos campos y el CSV y JSON los conservan.
- Una revisión numérica identifica curvas y evidencia de contraste. Los proyectos previos son recuperables. Verificar una comparación de curvas históricas actualiza ambas mallas base para evitar construir una estimación de error con operadores distintos.

## Periodicidad, malla y condiciones iniciales

En etapa 6, 16 de los 19 puntos de 1000 a 10 000 RPM alcanzaron periodicidad con máximo de 100 ciclos. A 8500, 9000 y 9500 RPM, la misma versión convergió en 102, 108 y 103 ciclos con un máximo de 200 y tolerancia inalterada de 5 × 10⁻⁵. En etapa 7, esos tres puntos conservan esos conteos con máximo de 160. Tras 100 ciclos, la energía de las celdas 1, 6 y 1 del escape domina, respectivamente. Los residuos son 5,44 × 10⁻⁵, 8,43 × 10⁻⁵ y 5,72 × 10⁻⁵. Ese estado sigue asentándose al ampliar el número de ciclos. El dominio comprobado corresponde a esta configuración y malla, con un contraste localizado a 5000 RPM.

El ejemplo ampliado publica 20 puntos de **500 a 10 000 RPM en incrementos de 500 RPM**, con 36–108 ciclos. Conserva 12 y 32 celdas de admisión y escape, CFL 0,44, paso angular máximo 0,65° y el mismo umbral. El máximo absoluto del balance fue 1,36 × 10⁻¹⁸ kg y 1,24 × 10⁻¹² J. La prueba automatizada verifica también las siete especies, los elementos, el marcador, los límites geométricos y el caso motorizado. Los balances cuantifican conservación y el refinamiento cuantifica sensibilidad de la solución.

En la continuación se transfirió únicamente el estado conservativo de un régimen ya periódico a la misma fase de apertura de escape, con idéntica geometría y malla. A 7500–10 000 RPM, los arranques independientes emplearon 594 ciclos y 334 967 pasos. La continuación ascendente requirió 417 ciclos y 235 720 pasos. La descendente requirió 418 ciclos y 235 780 pasos. Las diferencias máximas de potencia respecto al arranque independiente fueron 0,73 W al subir RPM y 0,30 W al bajarlas. Las aproximaciones a la tolerancia dependen del estado inicial. Los arranques independientes siguen como referencia en Desktop y CLI. La continuación queda disponible en la campaña diagnóstica.

## Barrido y respuesta de la geometría

El caso de depósito isotérmico de masa fija verifica el límite de mezcla perfecta: con sólo entrada fresca, la fracción fresca es uno menos el exponencial negativo de la masa entregada normalizada. También se verifica que extraer más fracción fresca reduce lo que queda atrapado. En el motor se informa entrega bruta, retorno por transfers, expulsión bruta, retorno por escape y pérdida neta al escape. El residual de cierre usa carga atrapada menos entrega neta más pérdida neta. La eficiencia histórica de atrapamiento conserva su denominador de entrega por transfer más retorno de escape. Además se expone fresca atrapada dividida por entrega bruta de transfer. Esta última, multiplicada por el delivery ratio, reproduce la eficiencia de carga.

El experimento de geometrías ejecutó 13 configuraciones a 4000, 5000 y 7500 RPM. Se probaron colector, difusor, sección central, cono, stinger, longitud de admisión, distribución de escape y transfer, compresión, avance, temperatura del depósito de cola y extracción de fresca. Los cambios de longitud mantuvieron aproximadamente el paso de malla. Las cifras siguientes son a 5000 RPM en malla base.

| Configuración | Cigüeñal [W] | Entrega bruta [mg] | Fresca atrapada [mg] | Pérdida neta al escape [mg] | Presión mínima en boca [kPa abs] |
|---|---:|---:|---:|---:|---:|
| Base | 2067,52 | 68,99 | 41,47 | 20,46 | 110,77 |
| Colector 190 mm | 2162,86 | 73,95 | 42,80 | 24,61 | 100,27 |
| Difusor 290 mm | 2107,14 | 69,41 | 42,00 | 20,11 | 107,85 |
| Centro 150 mm | 2070,68 | 68,46 | 41,48 | 19,46 | 110,01 |
| Cono 220 mm | 2073,53 | 68,63 | 41,53 | 19,95 | 110,40 |
| Stinger 19 mm | 2184,86 | 71,77 | 42,89 | 21,67 | 108,38 |
| Admisión 160 mm | 2066,98 | 68,98 | 41,46 | 20,46 | 110,76 |
| Techo de escape 30 mm | 2020,14 | 70,54 | 41,41 | 21,79 | 108,76 |
| Techo de transfers 42 mm | 1974,32 | 66,99 | 40,11 | 19,16 | 111,49 |
| Depósito de cola 330 K | 2159,98 | 71,38 | 42,60 | 21,55 | 108,90 |
| Extracción 0,75 | 2149,62 | 68,84 | 43,14 | 18,63 | 111,52 |

A 5000 RPM, el colector más largo bajó la presión mínima en la lumbrera y aumentó tanto la entrega como la pérdida. El máximo de presión posterior al PMI pasó de 251,6° a 180,1°, cerca del inicio de la ventana. Ese máximo describe el estado combinado en la boca y puede corresponder a un pico distinto. A 7500 RPM el mismo colector redujo el par en 0,098 N·m en la malla base, frente a +0,182 N·m a 5000 RPM. Elevar el techo de escape de 31 a 30 mm aumentó la entrega total, aunque cayó la masa fresca atrapada. La pérdida neta al escape aumenta de 20,46 a 21,79 mg. El balance distingue ese término del retorno por transfers y de la masa realmente retenida. La perturbación del depósito de cola afecta el estado de la boca y el atrapamiento. La sensibilidad a la hipótesis de barrido supera varias diferencias geométricas en malla base. Cada modificación debe contrastarse con refinamiento antes de clasificar su ganancia.

## Resolución A/B del difusor a 5000 RPM

| Contraste | Δ par B − A [N·m] | Δ potencia [W] |
|---|---:|---:|
| Malla base | +0,075676 | +39,62 |
| Malla base, mitad de paso y CFL | +0,078648 | +41,18 |
| Malla base, tolerancia periódica cinco veces menor | +0,075764 | +39,67 |
| Malla ×2 y paso refinado | +0,015077 | +7,89 |
| Malla ×4 y paso refinado | +0,025308 | +13,25 |
| Malla base, extracción relativa de fresca 0,75 | +0,021734 | +11,38 |

La banda empírica queda en **0,121198 N·m**. La diferencia se clasifica **numéricamente indistinguible**. La sensibilidad temporal aislada fue ~0,0030 N·m y la periódica ~0,00009 N·m. La espacial domina el contraste en este caso. El cambio de cierre de barrido por sí solo movió Δ par en ~0,054 N·m. El sentido físico de una ganancia pequeña queda sujeto a la resolución y a la identificación del barrido.
## Perfil y rendimiento

Se instrumentaron intervalos exclusivos de reconstrucción y decodificación 1D, balances 1D, acoplamientos y cierre termodinámico de los dos volúmenes. El perfil inicial a 5000 RPM asignó aproximadamente el 79 % del tiempo a preparar el estado de los conductos. El cálculo de cada frente reconstruido y la inversión de energía repiten las propiedades de las especies. El coste de uniones fue del orden del 6 %. Esa medición orientó la optimización a preparar las primitivas.

En **c9c6d08** se precalculan coeficientes de los polinomios integrados NASA y se omiten contribuciones de especies con masa exactamente cero en los bucles más frecuentes. Se mantienen las ecuaciones, composiciones, mallas y tolerancias. La optimización se comparó con **6c85488**, que ya tiene el mismo limitador y diagnóstico. Las cuatro repeticiones por binario se ejecutaron en Release, Windows x64 con Intel Core i5-10400 y .NET 10.0.15. La tabla usa las medianas de tres repeticiones después del primer arranque.

| Métrica | Antes | Después |
|---|---:|---:|
| Tiempo por régimen | 3412,3 ms | 2966,1 ms |
| Preparación de conductos | 2684,6 ms | 2247,9 ms |
| Tiempo medio por ciclo | 66,9 ms | 58,2 ms |
| Evaluaciones del operador | 87 279 | 87 279 |
| Pasos acumulados | 43 359 | 43 359 |

El tiempo total bajó **13,1 %**. La diferencia de potencia fue inferior a 6 × 10⁻¹² W. Las asignaciones de la ejecución caliente fueron aproximadamente 54 kB en ambos binarios. La campaña de variantes reutiliza la ejecución independiente por caso y su paralelismo acotado. La campaña de continuación midió la reducción de trabajo sin incorporar esa dependencia de recorrido al uso normal.
## Aceptación

La campaña cierra la verificación numérica del ejemplo y sus casos de modificación declarados. El caso sintético no tiene calibración ni validación experimental. El portable ejecutó el recorrido de configuración, curva, estudio, aplicación de B, contraste, guardado, recuperación, exportación y cancelación. También ejecutó un barrido con un régimen inadmisible y verificó que el exportador deja vacíos el par y la potencia de ese punto. Windows cargó el runtime desde el portable.
## Reproducción

Ejecutá **scripts/repro-stage7.ps1** para generar los CSV de RPM, continuación, geometría, resolución y perfil. En **2t-125cc-partial-domain.json**, un punto periódico a 5000 RPM convive con un punto fuera de dominio a 10 000 RPM por la fase de combustión. Ejecutá **scripts/verify.ps1** para las pruebas y **scripts/smoke-two-stroke.ps1 -VerifyResolution -VerifyDomain** sobre el portable publicado. El informe de [datos experimentales y protocolo](datos-experimentales-2t.md) registra los datos disponibles, restricciones y criterios de validación reservada.
