# Etapa 14C · Contrato numérico de las interfaces 0D–1D

## Resumen ejecutivo

`ports-v4` corrige la reacción espuria cerca del cierre, comprueba el residuo de onda antes de publicar el flujo, unifica el dominio térmico de las etapas del integrador y evita construir una pared auxiliar cuando la descarga supersónica puede evacuarse libremente. En simulaciones 2T ordinarias, la versión anterior alcanzó el régimen de apertura que provocaba el defecto. El contraste instrumentado muestra diferencias de hasta 106 kPa en trazas locales de escape y hasta 3,02 W en potencia al cigüeñal. Son resultados de simulación con las mismas configuraciones y calibraciones.

## 1. Referencia y alcance

La rama `stage14c/contrato-de-interfaces` parte de `90693b299372d40f6311b94683acf8c5dcbab0a1`, preservado como `recovery/stage14b-90693b2`. Se reejecutaron los mínimos de la contraauditoría antes de modificar producción y se comprobaron su manifiesto y los ZIP de referencia. Las salidas de esa repetición se conservaron en `E:/dino2-stage14c/baseline`. La revisión científica nueva es `ports-v4`.

La intervención se limita al cálculo de fronteras físicas de presión, válvulas, lumbreras, láminas cuasiestáticas, bocas, uniones con volúmenes compactos y estados del conducto que alimentan los flujos. Continúan disponibles los cierres históricos de referencia con sus propias aproximaciones, sin certificación de onda exacta de `ports-v4`. El intervalo termoquímico sigue siendo 200–6000 K. No cambia la calibración de combustión, fricción, geometría ni coeficientes de descarga.

## 2. Reproducción y causas raíz

| Caso | `ports-v3`, reproducido | Intervención |
| --- | --- | --- |
| A14B-01 | Tubo quieto a 100 kPa y 600 K, tanque a 400 kPa y 600 K, α=10⁻¹⁵. La cara publicó 2207,174 Pa con el caudal crítico correcto | La velocidad de búsqueda se acota por continuidad más la presión de pared. Se verifica compatibilidad de onda al publicar |
| A14B-02, frío | Aire en tubo a 220 K, Mach −0,8, tanque a 60 kPa y 380 K, α=0,37. Garganta y cara eran admisibles, la rarefacción del tubo requería T<200 K | La onda receptora usa inversión acotada y forma parte de la aceptación |
| A14B-02, caliente | Aire en tubo a 5100 K, Mach 0,6, tanque a 400 kPa y 380 K, α=0,37. El estado postchoque necesario supera 6000 K y se publicaba un flujo con cara fría | El máximo de presión del Hugoniot se deduce a T=6000 K y la raíz se comprueba dentro del dominio |
| A14B-03 | El predictor de una especie a 199 o 6001 K llegó a calcular RHS con primer orden y MUSCL. El validador del estado aceptado sí lo rechazó | La decodificación y las caras reconstruidas comparten el dominio. Prueba con gas de una especie y mezcla |
| A14B-04 | Aire a 5800 K, 100 kPa y Mach 1,6, tanque a 60 kPa y boca plena. La pared auxiliar fallaba antes de decidir el régimen de descarga | Se demuestra primero si el flujo incidente puede salir por una garganta admisible sin recibir información del receptor |

En A14B-01, la anchura inicial de la búsqueda en velocidad era del orden de la velocidad del sonido. Su raíz disminuye proporcionalmente a α. Tras 42 divisiones, el último intervalo era demasiado grande para representar la raíz y el algoritmo publicaba su punto medio. Con caudal fijado por la garganta, la relación p=jRT/u traslada ese error a la presión. A 10⁻¹⁵, el caudal permanece alrededor de 6,6003×10⁻¹³ kg/(m²·s), pero la presión antigua distaba casi 98 kPa de la reacción de cierre.

## 3. Contrato de construcción y aceptación

Se usa la coordenada local que apunta al conducto. La composición se conserva en la onda y se transporta la del donante a través del contacto. Presión y velocidad coinciden en el contacto subcrítico, aunque los dos gases tengan temperaturas y entropías diferentes. Las caras ideales y las uniones físicas de área plena reutilizan el mismo cierre y la reflexión de los componentes impares del vector conservativo. En una boca exterior se conserva la política de salida supersónica libre del contorno abierto. En una unión compacta puede entrar un choque si la contrapresión lo exige.

| Rama | Incógnita y ecuación | Comprobación al publicar |
| --- | --- | --- |
| Pared o equilibrio | Presión sobre la onda admisible con velocidad cero | Residuo de velocidad de pared, reacción pA finita, masa y energía nulas |
| Entrada con escalón | Velocidad de garganta, isentrópica del depósito, continuidad y transmisión de momento en hombro, onda del conducto | Velocidad de contacto, igual caudal, entalpía total común, momento del escalón, entropía admisible |
| Entrada estrangulada | Caudal crítico dado, velocidad media en intervalo proporcional al caudal, onda del conducto | Igual caudal y entalpía, residuo de onda, garganta sónica |
| Chorro sónico en cara | Caudal crítico, capacidad de la cara y condición de salida sónica | Igual caudal y entalpía y desigualdad característica en el límite sónico. La reacción de hombro y chorro se obtiene del vector axial, sin imponer la ecuación de escalón a una rama subexpandida |
| Descarga por contracción | Presión de cara a partir de la onda y garganta isentrópica con la contrapresión o bloqueo | Residuo local de masa y de velocidad en la onda, Mach de garganta, entalpía y entropía comunes |
| Descarga supersónica libre | Traza incidente y garganta sobre la rama supersónica de igual remanso | Capacidad y sentido de propagación, masa, energía y entropía. Esta rama no impone un contacto aguas arriba |

El flujo publicado tiene tres partes. Primero se construye una solución provisional y se comprueba su régimen, estados y residuos. Después se obtiene el caudal único de garganta multiplicado por el área. Finalmente se publica la masa de cada especie, la energía y el momento. Para el eje del conducto, el momento es pA más el producto del flujo másico por la velocidad local. En el escalón se comprueba la relación de momento de la expansión compacta. La entalpía de entrada usa la referencia química del donante y la de salida usa la entalpía total de la cara en el marco del conducto.

Un estado térmicamente fuera de dominio se señala con `ModelDomainException`. Un estado no físico, un residual incompatible, una raíz mal condicionada o agotada se distingue en el diagnóstico numérico. Las configuraciones contrarias al modelo de volumen en reposo se rechazan como argumentos incompatibles. Los integradores pueden reducir el paso ante un estado provisional inadmisible. Si la reducción no recupera una evaluación válida, el régimen queda rechazado con su causa y la curva conserva los demás RPM. El vector de frontera conserva su contenido previo si falla la comprobación local.

## 4. Escalas, precisión y selección de ramas

En entrada estrangulada, j es el caudal por área total determinado por el cuello. La continuidad da p=jRT/u y la energía implica T≤T₀. La rama entrante compatible requiere p≥p de pared o su extremo térmico admisible. Así, la velocidad superior puede limitarse por **jRT₀/p de pared**, además del valor sónico. Al tender α a cero, el intervalo se reduce con el caudal. La búsqueda combina Newton con bisección protegida. Las pendientes provienen de la isentrópica, la conservación del escalón y la característica. Una propuesta fuera del intervalo pierde el paso de Newton y vuelve a bisecar. Se acepta por residuo físico, no por haber agotado el lazo.

La velocidad de onda se controla con una parte relativa a la velocidad local y un piso de representación derivado de ULP de presión convertida mediante la impedancia, más ULP de la velocidad incidente. El criterio típico de entrada usa 2×10⁻¹¹ de la velocidad local. El de descarga combina 2×10⁻⁹ con la condición de representación y comprueba la continuidad por separado. Energía, entropía y momento usan sus propias escalas. Se preservan los incrementos pequeños de presión inversa en una variable de exceso sobre contrapresión, con funciones estables para logaritmo y exponencial. La aproximación tangente cerca de pared usa la pendiente de esa misma onda en el nivel de redondeo.

El caso original recupera 100 kPa con diferencia de orden subnanopascal. Para incidencia no nula se usa su presión de pared, que depende del signo y la magnitud de la velocidad. Las pruebas incluyen distintos Mach incidentes y áreas desde cero hasta 10⁻²⁴, con el caudal crítico y la presión calculados de manera independiente. Esto corrige la parametrización defectuosa, sin área mínima ni recorte de presión.

Para una descarga libre, primero se busca una garganta supersónica admisible y se comprueba la presión que puede imponerse aguas abajo. Se verifican la boca plena caliente a 5800 K y una abertura parcial α=0,99 en la que la presión del receptor supera la del tubo pero queda por debajo de la presión de garganta. Una onda auxiliar de pared queda fuera de esas decisiones. Cuando se necesita un choque, su velocidad distingue entre salida libre, choque estacionario y choque incidente. La ecuación de Hugoniot se resuelve dentro del intervalo termoquímico. [NASA, propiedades del gas térmicamente perfecto](https://ntrs.nasa.gov/citations/19950005582) [NASA, relaciones de choque normal](https://www.grc.nasa.gov/www/BGH/normal.html)

## 5. Admisibilidad y revisión transversal

La onda se evalúa con las propiedades de su composición. La presión límite caliente se obtiene resolviendo Hugoniot a 6000 K y la fría a partir de la isentrópica a 200 K. Un extremo puede acotar una raíz sin convertirse en una solución de contacto. Las inversiones de temperatura, compresión y estado sónico usan iterados acotados. El residuo en un límite permite informar que la solución excede el dominio. Las propiedades de mezcla se calculan dentro del intervalo con la tabla de dos tramos, integrando cada tramo con sus coeficientes.

En MUSCL se limitan las pendientes primitivas antes de reconstruir y se conserva el estado conservativo central. Para una especie, la presión y temperatura de la celda se reconstruyen sólo tras comprobar su admisibilidad. Para varias, se invierte la energía de mezcla con un intervalo acotado. El corrector conserva la comprobación de energía por especie en la celda. El acoplamiento de pleno y ramal comparte la validación de frontera. Las láminas conservan su ley de alzada cuasiestática. El bloqueo unidireccional se decide antes de construir un flujo inverso incompatible. La aproximación de presión en la ley de alzada queda documentada como parte de ese modelo. Los intercambios 0D–0D emplean sus relaciones algebraicas de tobera y el transporte del donante.

## 6. Verificación generativa y referencias

`--stage14c` combina pruebas deterministas con geometría analítica y un muestreo de 2048 casos por semilla. Se varían áreas logarítmicas, presiones, composición, temperatura, velocidad incidente, estados sub/supersónicos, diferencias de presión cercanas a ULP y aberturas próximas a cero y uno. Una transformación metamórfica escala las dos áreas y verifica que la presión se conserve. Las infracciones muestran semilla y parámetros. Una prueba de rechazo comprueba que el flujo provisional no llegó al destino.

La semilla de desarrollo es 140301. Las semillas reservadas se ejecutan sobre la revisión candidata sin participar en la elección de tolerancias. Se repite la matriz independiente de la contraauditoría, con referencia decimal de entrada estrangulada, referencia de choque móvil de gas perfecto y contraste de Cantera para la termoquímica variable. Los diagnósticos del arnés distinguen rechazo de dominio y residuo numérico. Una comparación de Cantera es una referencia de propiedades y de implementación, no evidencia de una medición de motor.

## 7. Evidencia, rendimiento y procedencia

La instrumentación de campañas se compila a partir de copias privadas de dos commits. Registra área mínima, ramas estranguladas cerca del cierre, iteraciones, residuos, rechazos, picos de momento y energía, trazas angulares y resultados periódicos. Los tiempos de CPU y pared se miden por separado. El perfil de producción a 5000 RPM separa preparación de conducto y costo de las interfaces. La optimización de las raíces conserva comprobaciones físicas en Release.

La [guía de reproducción](../evidence/stage14c/README.md) identifica los ensayos y la forma de repetirlos. Los resultados, scripts externos, perfiles, trazas, hash del portable y registros de compilación se conservan en `E:/dino2-stage14c`. El commit exacto de compilación se registra en el manifiesto y en la salida del ejecutable. Los formatos históricos se leen con sus propias etiquetas. La selección y la comparación actual conservan la revisión numérica de cada curva.

## 8. Condiciones físicas pendientes

La inertancia local y la radiación exterior quedan diferidas. La formulación requiere una abertura compacta y cuasiestacionaria para el fenómeno estudiado. Los ensayos numéricos de ondas en tubo y colector no validan la fase de una abertura real para todos los armónicos de motor. La validación experimental requiere geometría, presión pulsante, levantamiento y descarga, barrido y curvas al eje con condiciones de ensayo reproducibles.

## 9. Resultados de las pruebas locales

| Campaña | Casos | Aceptados | Fuera de 200–6000 K |
| --- | ---: | ---: | ---: |
| Propiedades, semilla 140301 | 2048 | 1889 | 159 |
| Reserva, semilla 104729 | 2048 | 1902 | 146 |
| Reserva, semilla 196613 | 2048 | 1890 | 158 |
| Matriz anterior de presión, área y Mach | 21 504 | 21 257 | 247 |
| Reversión débil | 1080 | 1080 | 0 |
| Límites de abertura y temperatura | 242 | 105 | 137 |
| Onda del lado del tubo | 1728 | 1277 | 451 |
| Límite de cierre | 270 | 270 | 0 |

Las dos familias son independientes. Dentro de ellas hubo 1298 rechazos térmicos explícitos y cero agotamientos publicados o rechazos numéricos imprevistos. La reconstrucción independiente de las ondas no encontró temperaturas inadmisibles que hubiesen influido en un flujo aceptado. La garganta cubrió 201 a 5904,90 K y la cara 201 a 6000 K en la matriz independiente. El contraste de Cantera cubrió choques de aire y gases de escape. En 25 puntos el máximo contraste de temperatura fue 7,74 K y el mayor contraste relativo de caudal fue 2,87 %, junto a un choque cercano al estancamiento. Ambas referencias usan tablas de propiedades distintas. La comparación directa de la cuadratura de producción de tres puntos con 48 puntos mostró hasta 0,00210 m/s de diferencia en una rarefacción extensa. Ese valor caracteriza el error de cuadratura del caso y es distinto del residuo de convergencia contra la característica que se resuelve en producción.

Para la abertura α=10⁻¹⁵, la referencia decimal da 100000,000000000324 Pa frente a 100000,00000000032 Pa calculados. También se mantienen las correcciones de 14B. Entre α=1−10⁻⁹ y α=1 la diferencia de entalpía total es 5,31×10⁻⁵ J/kg en el choque caliente histórico. En reversión débil, el caudal de 1,524094×10⁻¹⁵ kg/(m²·s) proviene de la garganta y coincide con el que se publica. La reapertura de resultados antiguos no permite mezclar sus contrastes con la revisión vigente.

En el choque móvil de boca plena, la discrepancia máxima de posición descendió de 4,62 mm con 128 celdas a 2,31 mm con 256. El error máximo de presión postchoque pasó de 0,244 % a 0,191 %. Para α=0,6, la referencia independiente obtiene Mach 1,12631 y 354,61 kPa en la garganta, por encima de los 300 kPa del receptor. Los seis puntos espacio-tiempo conservan el estado incidente y están libres de un frente que entra al tubo. La tabla de choque forzado conserva la rama hipotética de compresión como control negativo. En B8, las 2160 muestras dan diferencias máximas frente a 14B de 0,00237 Pa en el nodo, 0,0137 Pa en el ramal y 0,0157 Pa en el tronco. Los residuos máximos de balance son 7,0×10⁻¹⁸ kg y 1,37×10⁻¹² J.

Las pruebas de desarrollo separaron estados de ensayo que debían construirse con conservación de energía de verdaderos falsos rechazos. El caso caliente de abertura parcial α=0,99 con presión receptora mayor que la presión estática del tubo destapó una precondición demasiado restrictiva durante el desarrollo. La condición corregida usa la presión de la garganta supersónica. Las semillas de reserva se corrieron después de esa corrección.

## 10. Impacto con configuración de motor idéntica

| Modelo y RPM | Cierres estrangulados medidos en `ports-v3` | Δ par al eje (N·m) | Δ potencia al eje (W) | Máx. Δ presión de escape (Pa) |
| --- | ---: | ---: | ---: | ---: |
| 2T 4000 | 130 | −0,001299 | −0,544 | 97 605 |
| 2T 5000 | 106 | +0,001270 | +0,665 | 96 470 |
| 2T 8000 | 119 | −0,000765 | −0,641 | 97 655 |
| 2T 9500 | 127 | −0,003029 | −3,014 | 105 574 |
| 4T 1000 | 50 | −1,17×10⁻⁸ | −1,22×10⁻⁶ | 0,0092 |
| 4T 5000 | 0 | −5,54×10⁻⁶ | −0,00290 | 1,84 |
| 4T 6000 | 22 | −2,93×10⁻⁶ | −0,00184 | 4,48 |

Las trazas se compararon a un grado de cigüeñal sobre las señales completas. Los valores máximos son diferencias locales de traza que incluyen la presión de cara. El defecto se observa directamente también como un residuo de velocidad de hasta 1364 m/s en 2T frente a 3,50×10⁻¹³ m/s en la subpoblación de aperturas estranguladas pequeñas de 14C. Se alcanzó α=4,80×10⁻¹⁶ durante ciclos ordinarios 2T. En el caso 2T a 9500 RPM, el trabajo de cilindro cambió −0,0211 J, el de cárter +0,00180 J y la masa atrapada −0,0188 mg. En los cuatro 2T, la mayor diferencia en pico de presión del cilindro fue 712 Pa, y en el conjunto 4T fue 6,78 Pa. El cambio máximo de trabajo de bombeo 4T medido fue 1,04×10⁻⁵ J. Los diez pares instrumentados conservaron periodicidad y no registraron rechazos locales térmicos ni numéricos.

El conteo se hace sobre cada llamada, incluidas las de diagnóstico. La captación de bocas en 14C casi duplica ese conteo comparado con el instrumento en la frontera anterior. Las correcciones de rama y la adopción de la misma termoquímica en las bocas también cambian resultados. Por ello, el contraste completo mide el impacto conjunto. Los mínimos y B8 aíslan la compatibilidad de onda, la reacción de pared y las reflexiones, sin atribuir toda diferencia de un motor a una sola corrección.

## 11. Regresión de motor, comparación y resolución

Los 112 casos estacionarios y 24 controles térmicos y de mezcla conservaron su estado `ok`. La campaña 4T usa las mismas referencias, ciclos máximos, combustión prescrita en grados y en segundos, y escapes entre 0,25 y 0,85 m. La auditoría 4T en Windows completó los 11 RPM con CFL 0,20, paso máximo 0,15° y tolerancia 10⁻⁶. El caso instrumentado de referencia a 4500 RPM dio 30,0113017 N·m. Los informes de malla y tiempo distinguen el cambio de frontera del cambio de discretización y del criterio periódico.

La campaña 2T conserva geometría, mallas de admisión y escape separables, transferencia, atrapamiento y trabajo de cárter. Se cubrieron ×1, ×2, ×4 y ×8 en ocho regímenes entre 4000 y 10000 RPM, incluidos 8000 y 9500 RPM. A 9500 RPM, el par pasó por 6,55934, 6,39358, 6,40960 y 6,40124 N·m. Las diferencias de malla de esta secuencia son mayores que el cambio de revisión instrumentado a ese régimen. En la prueba temporal ×2 a 8000 y 9500 RPM, el residuo periódico es 4,82×10⁻⁷ y 5,86×10⁻⁷ respectivamente. Las regresiones de redes cubren 2, 3 y 4 cilindros, casos aislados, colectores compartidos y fases asimétricas. Las salidas de redes, banco B8 y pruebas de tubos conservan balances, reflexión, transmisión y retorno desde la boca.

Se contrasta directamente B−A a igual discretización entre pares, y se reporta la banda a partir de los cambios de B−A entre resoluciones. El signo cambiante, una diferencia no estabilizada o la falta de un estado periódico impiden emitir una conclusión de ventaja. La banda sólo describe la sensibilidad observada en los niveles comprobados. El par 2T a 9000 RPM con +40 mm de difusor sigue clasificado como sensible. A 9500 RPM, el par histórico con +10 µm de diámetro de barriga y +5 µm de longitud de difusor produce −0,0000911, −0,0001253 y −0,0000988 N·m. El último valor excede la banda de 0,0000685 N·m y la clasificación pasa a inferior dentro de esta resolución. En un control construido, se añade a B una reducción constante de pérdidas de 2,517 Pa que aporta analíticamente +0,0000500 N·m en todos los niveles y deja intacta la dinámica gaseosa. Los tres valores pasan a −0,0000411, −0,0000753 y −0,0000488 N·m. El último queda dentro de la misma banda y el contraste ejecutado lo clasifica como indistinguible. La reducción se usa sólo en el caso de comprobación del clasificador, con el caso de referencia preservado.

| Pleno 2,4 → 3,6 L a 4000 RPM | B−A ×1 (W) | B−A ×2 (W) | B−A ×4 (W) | B−A ×4 con control temporal (W) |
| --- | ---: | ---: | ---: | ---: |
| Tronco 0,38 m | −603,82 | −620,05 | −622,50 | −622,80 |
| Tronco 0,55 m | −467,28 | −470,15 | −469,99 | −470,42 |

Las conclusiones históricas se mantienen en estos dos casos. Las diferencias de fase de orden 2 en ×4 fueron −25,31° y −69,46°. El control temporal cambia las diferencias de potencia en menos de 0,44 W. En el segundo tronco, la secuencia no es monótona pero el signo se mantiene y el último cambio disminuye. Los pares conservan la condición de convergencia que se usó en cada malla.

## 12. Perfil de cálculo

En tres repeticiones calientes 2T a 5000 RPM, la mediana de CPU fue 3,766 s en 14B y 3,594 s en 14C. La mediana de pared fue 4,740 y 4,245 s bajo carga de otras campañas, por lo que es orientativa. La interfaz pasó de 1,015 a 0,776 s y `Prepare` del conducto de 3,025 a 2,762 s, inclusivos de sus subrutinas. Los 27 ciclos necesitaron 49 906 y 49 916 evaluaciones en cada revisión. Las asignaciones medidas tienen la misma mediana de 54 992 bytes. El perfil instrumentado separa iteraciones y número de llamadas, y conserva el mismo costo del flujo de producción. En la red 4T de caso común, 2, 3 y 4 cilindros requirieron aproximadamente 7,0, 7,5 y 12,2 s, con 1,40, 1,94 y 2,53 MB asignados. También cambió el número de ciclos para alcanzar periodicidad. Estas cifras no miden un escalado paralelo puro y no separan `cp` e invariantes como temporizadores independientes.

## 13. Matriz de hallazgos y dominio

| Hallazgo | Estado en 14C | Evidencia |
| --- | --- | --- |
| A14B-01, presión y momento espurios de cierre | Corregido | Referencia decimal, varios Mach y áreas, trazas de motor |
| A14B-02, onda requerida extrapolada | Corregido | Límites frío y caliente, barridos, comparación independiente |
| A14B-03, predictor de una especie | Corregido | Operador espacial, primer orden y MUSCL, mezcla y gas de una especie |
| A14B-04, pared auxiliar fuera de rango | Corregido | Boca plena y parcial, contrapresión, choque móvil y referencia de área crítica |
| C13-01, caudal débil | Conservado | 1080 puntos y mínimo de reversión |
| C13-02, salto termoquímico | Conservado | Choque caliente y límite de área plena |
| C13-03, extrapolación de garganta y cara | Conservado | Rechazos térmicos de los dos mínimos históricos |
| C13-04, fase de una abertura real | Diferido | Cierre de abertura compacta y cuasiestacionaria |

El cierre físico requiere estados con presión y densidad positivas, composición admisible, solución de interfaz en 200–6000 K, depósito en reposo y abertura compacta en relación con las escalas de onda. Las campañas ejercitan modos de fallo y escalas de área, temperatura, presión y Mach, pero una geometría o régimen fuera de esos ensayos conserva su propio requisito de refinamiento y periodicidad. La propagación corresponde al modelo 1D con sus cierres de pérdidas, combustión, barrido y transferencia de calor. La exactitud experimental, las láminas dinámicas, la inertancia local y la radiación de boca siguen abiertas.

| Criterios de aceptación | Evidencia |
| --- | --- |
| 1–6 · Presión, pared, momento, residuo, agotamiento y coherencia entre planos | Referencia decimal, `Interface14CChecks`, matriz de invariantes y mínimos A14B-01/02 |
| 7–9 · Dominio, integrador y gas de una especie | `Decode`, inversiones térmicas acotadas, predictor de ambas discretizaciones y pruebas de mezcla |
| 10–12 · Ramas, pared auxiliar y contrato | Choques de contrapresión, Mach, área–Mach y validación conservativa antes de publicar |
| 13–15 · Propiedades, reserva e impacto | Tres semillas, verificador independiente y diez comparaciones instrumentadas |
| 16–17 · Entalpía y caudal residual | `--stage14b`, banco histórico y límite de reversión débil |
| 18–20 · Rango 4T, campañas 2T y redes | Windows 11/11, refinamientos ×1–×8 y redes de dos a cuatro cilindros |
| 21–22 · Contraste, identidad e incompatibilidad | Plenos pareados, caso indistinguible y recuperación de archivos históricos |
| 23–25 · Portable, reproducción y alcance | Recorridos nativos, manifiestos, scripts y condiciones físicas de esta sección |

## 14. Verificación Desktop y procedencia del binario

La solución Release compiló con cero errores y advertencias. Pasaron las pruebas de Otto y almacenamiento, cuatro tiempos, termoquímica, estudios, dos tiempos y redes. La suite WPF conserva los resultados numéricos de las revisiones anteriores, impide compararlos como vigentes y recupera los puntos válidos cuando otro RPM sale del dominio.

El distribuible autocontenido `Dino2-stage14c-win-x64.zip` se construyó en un árbol limpio en `4deeb3075c43a2f95aa3e2361473974754d389c5`. Se comprobó que WPF cargó `coreclr.dll` del propio directorio extraído. El SHA-256 del ZIP es `599a82add0e94e56a647dd44b739e8d44661fa49035ced40a8cd424a35535813`. La medición instrumentada se compila aparte y conserva sus propios sellos. El cambio de automatización del selector de archivos y esta documentación son posteriores a la compilación y se verifican sobre el mismo binario.

| Recorrido sobre el portable extraído | Resultado |
| --- | --- |
| 4T monocilíndrico y Otto | Dos curvas de 11/11 RPM, incluida la variante 1D física, y cálculo Otto independiente |
| Estudios 4T | Cuatro variantes, contraste, guardado, reapertura, CSV y aplicación de B |
| 2T monocilíndrico | Curva, salvaguarda de A, estudios, refinamientos pareados, exportaciones, recuperación de puntos ante rechazo de dominio y cancelación |
| 4T multicilíndrico | Curvas, contraste inferior de B a 4000 RPM, estudio de tres variantes, guardado y reapertura, CSV de curvas y cilindros, aplicación de B y cancelación |
| 2T multicilíndrico | Curvas, contraste superior de B a 5000 RPM, estudio de tres variantes, archivo nativo, CSV de curvas y cilindros, aplicación de B y cancelación |

La primera corrida nativa multicilíndrica 2T se detuvo al automatizar el segundo selector de archivos. Se hizo esperar a la confirmación del texto y al cierre real del selector. La repetición recorrió todos los pasos con el mismo ZIP y quedó registrada junto al primer intento. Las reaperturas archivadas y el contraste bloqueado se comprobaron en la suite WPF. Los tiempos de contraste nativo de 128,98 s en 4T y 69,11 s en 2T corresponden a sus respectivas configuraciones y niveles de refinamiento.

## 15. Veredicto y etapa 15

**NUMERICALLY VERIFIED / EXPERIMENTALLY UNVALIDATED.** Se cierra la recuperación numérica de `ports-v4` en el dominio que se describe en la sección 13. La repetición de casos conocidos, las semillas de reserva, las referencias independientes y las campañas completas no identificaron defectos estructurales bloqueantes en ese dominio. El cierre permite usar esta versión como base numérica de la etapa 15. Esa etapa requerirá alcance propio, sus regresiones y evidencia experimental para cualquier afirmación nueva de precisión de motor. La rama actual conserva la recuperación de 14B y no incorpora trabajo de etapa 15.
