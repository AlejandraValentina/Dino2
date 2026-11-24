# Etapa 14B · Coherencia de la interfaz 0D–1D

## 1. Resumen ejecutivo, alcance y referencia

La revisión científica `ports-v3` unifica las propiedades del gas en la garganta y la cara del conducto. Corrige el salto térmico a apertura plena, la publicación de flujos fuera del dominio térmico y la fuga numérica al invertir el caudal. El ejemplo 4T predeterminado da 11/11 RPM con la tolerancia periódica original. El cierre local sigue siendo cuasiestacionario.

La referencia auditada es `7a080e70196388f83c8d49172df46d75f1de7d53`, preservada en `recovery/stage13-7a080e7`. La rama de trabajo es `stage14b/coherencia-de-interfaz`. Se repitieron los programas de la contraauditoría independiente de etapa 13 antes de intervenir. El banco, los perfiles y las trazas de esta revisión se conservan en `E:/dino2-stage14b`. El identificador exacto del árbol limpio usado para compilar el portable está en `portable-commit.txt` del paquete de evidencia y en los sellos de las curvas exportadas.

El alcance comprende el acoplamiento compacto de válvulas, lumbreras y láminas a los conductos. Las bocas exteriores ideales y el cierre de pleno compacto conservan sus propias aproximaciones. El caso 4T usa química reducida; el 2T de referencia usa pseudoaire y un modelo de extracción fresca. Los ejemplos son sintéticos y esta etapa aporta verificación numérica.

## 2. Reproducción y tratamiento de los hallazgos

| ID | Reproducción en etapa 13 | Resultado de etapa 14B |
| --- | --- | --- |
| C13-01 | Con aire en reposo, Δp de −10⁻⁶ Pa y abertura 10⁻¹², el flujo era 6,1883×10⁻⁶ kg/(m²·s) con garganta de caudal cero | ±1,524×10⁻¹⁵ kg/(m²·s) con el signo correspondiente a cada lado de la inversión. La diferencia garganta–cara en ambos extremos probados fue del orden de 2×10⁻³¹ kg/(m²·s) |
| C13-02 | A 1800 K, Mach 2 y 700 kPa de contrapresión, la entalpía total cambiaba 77,94 kJ/kg entre α=0,999999999 y 1 | La diferencia es 0,000053 J/kg. La variación de masa es 7,44×10⁻⁸ kg/(m²·s) y la de momento axial es 2,82×10⁻⁵ Pa |
| C13-03 | Un donante a 225 K produjo una garganta a 189,03 K. Otro a 5000 K produjo una cara a 8585,73 K | El caso frío rechaza la garganta a 188,88 K y el caliente la garganta a 8669,53 K a α=0,3. El diagnóstico procede antes de publicar el flujo |
| C13-04 | La excitación armónica equilibrada presenta resistencia con flujo medio y parte reactiva numéricamente nula | Se reproduce y delimita. La energía cinética local del chorro se transporta, pero el modelo carece de almacenamiento local de momento |
| C13-05 | La campaña imprimía `AcceptedSteps` como `ms=` | La campaña etiqueta ciclos, pasos aceptados y rechazados, pared en ms y concurrencia máxima. El perfil informa CPU y pared por separado |
| C13-06 | 9/11 RPM. A 4000 y 5000 RPM, residuos 3,86×10⁻⁵ y 1,15×10⁻⁴ después de 60 ciclos | 11/11 RPM con CFL 0,20, máximo 0,15° y tolerancia 10⁻⁶. Residuos respectivos 1,97×10⁻⁷ y 4,44×10⁻⁸ |

Los mínimos de C13-01 a C13-03 se ejecutan con `--stage14b` y en la regresión 4T. El control acústico queda en el arnés independiente. El control de RPM y balances es obligatorio en WPF y el smoke del portable exige también 11/11. La evidencia conserva las condiciones y diagnósticos originales.

## 3. Formulación termoquímica y admisibilidad

Se usa un gas ideal calóricamente imperfecto de composición fija durante cada salto. Se agregan coeficientes NASA en masa y se evalúan (p=\rho RT) y (a^2=RTc_p/(c_p-R)). Los cambios de (h) se integran exactamente por cuadratura gaussiana para el polinomio de cuarto grado en cada tramo. El cambio de entropía es la integral de (c_p/T) menos (R\ln(p_2/p_1)). La integral cruza los 1000 K sin reiniciar el origen. La referencia química se conserva a través de la entalpía del donante. El gas de una especie conserva su rama caloríficamente perfecta.

La rarefacción usa (du=dp/(\rho a)) con la misma isentrópica. Se resuelve el Hugoniot (h_2-h_1=(p_2-p_1)(v_1+v_2)/2) a presión dada y se obtiene la masa relativa al frente por el balance de momento. La compresión físicamente admisible tiene producción positiva de entropía. En la tobera ideal se conserva (h_0=h+u^2/2) y se limita el caudal por la garganta sónica. Para volumen→conducto, la separación compacta de etapa 13 satisface (p_m+j u_m=p_t+j u_t) en la expansión modelada y conserva (h_0). El gamma local sólo inicializa la raíz de momento para mezclas. Para chorro subexpandido queda una reacción de pared y región de mezcla que no resuelve el campo del chorro. Para conducto→volumen, la contracción usa el remanso de la cara y el mismo (j=\alpha\rho_t u_t). El choque estacionario decide si la descarga supersónica se sostiene. El transporte de ambos lados usa el mismo vector conservativo. [NACA 1135, flujo compresible](https://www.nasa.gov/wp-content/uploads/2023/03/equations-tables-charts-compressibleflow-report-1135.pdf) [NASA Glenn, choque normal](https://www.grc.nasa.gov/www/BGH/normal.html) [NASA CEA, termoquímica de especies](https://www.nasa.gov/glenn/research/chemical-equilibrium-with-applications/)

La raíz inversa mide el exceso de presión sobre el receptor en lugar de resolver sobre un intervalo de presión acústica absoluto. Cerca de la inversión conserva incrementos de presión inferiores a la resolución de la presión total y verifica directamente el residuo de caudal, con criterio relativo (2\times10^{-9}) o un límite de redondeo derivado de 32 ULP de presión dividido por la velocidad de sonido si ya se agotó el intervalo representable. La velocidad publicada de la cara se obtiene de la continuidad con la garganta. En cierre exacto, se conserva la reacción de presión de la pared y los flujos de masa y energía son cero.

Los donantes se comprueban en presión, densidad, velocidad, energía, composición y temperatura. La garganta y el plano medio derivan del estado termodinámico común y deben dar presión positiva, (a^2>0) y magnitudes finitas. Se comprueba 200–6000 K en los estados de transporte antes de publicar los flujos. Los candidatos internos de la raíz pueden evaluarse fuera de la ventana matemática durante la búsqueda. La pared cerrada sólo transmite presión. Los receptores y las etapas de integración se verifican al decodificar el estado conservativo. Un estado fuera de 200–6000 K puede admitir un intento con paso menor, seguido de un fallo de dominio si persiste. Un estado no físico se diagnostica como error numérico. Se conservan los demás RPM de la curva.

Cantera 3.2 con GRI-Mech da 3151,543 K, 91,529 m/s y 68,9147 kg/(m²·s) para el choque caliente. Dino2 da 3150,177 K, 91,452 m/s y 68,8868 kg/(m²·s), con discrepancias inferiores al 0,1 % entre tablas. Cantera da un incremento de entropía de aproximadamente 249 J/(kg·K). Las pruebas dentro de Dino2 comparan el balance conservativo en el marco del choque para aire y productos, de 300 a 3300 K, y contrastan la rarefacción isentrópica y las aperturas parciales y plenas. Esta comparación verifica la implementación del modelo de gas ideal, con independencia de la química de equilibrio de un motor real.

## 4. Control temporal 4T y referencia a 4500 RPM

Los dos fallos históricos persistían al subir el máximo a 150 ciclos. La selección MUSCL nueva usa CFL 0,20 y reduce el máximo angular de fábrica a 0,15°. El CFL resuelve el transporte acústico de la malla y el máximo angular resuelve el movimiento del pistón, la ley de apertura y las fuentes entre etapas. El ejemplo con combustión prescrita en segundos cambia la duración angular con las RPM. El resultado demuestra sensibilidad al control temporal, sin aislar una única fuente de la oscilación periódica. No se cambió la tolerancia ni se introdujo un reintento oculto. Una configuración guardada retiene sus controles. Desktop muestra los efectivos y la prueba comprueba la lista completa, periodicidad, balances y ausencia de fallos.

Con `ports-v3` y los controles históricos, 4000 RPM sí alcanza el criterio en 52 ciclos, frente a 9 con los controles nuevos. 5000 RPM alcanza el criterio en 8 ciclos con ambos controles. Las diferencias de par entre controles son 0,00014 y 0,00025 N·m. El refinamiento espacial representativo con los controles antiguos cambia el par aproximadamente 0,0131 y 0,00068 N·m, respectivamente. Se eligieron los nuevos controles para reproducir con margen la periodicidad del ejemplo completo, sin depender de una política de reintentos ni afirmar que sean necesarios en cada punto.

| RPM | Par al eje, N·m | Ciclos | Residuo periódico |
| ---: | ---: | ---: | ---: |
| 1000 | 29,8886 | 9 | 7,11×10⁻⁸ |
| 1500 | 32,2884 | 7 | 9,88×10⁻⁹ |
| 2000 | 32,8990 | 7 | 1,94×10⁻⁸ |
| 2500 | 32,8689 | 7 | 2,66×10⁻⁸ |
| 3000 | 32,1137 | 9 | 2,99×10⁻⁸ |
| 3500 | 32,2124 | 7 | 4,31×10⁻⁷ |
| 4000 | 31,9820 | 9 | 1,97×10⁻⁷ |
| 4500 | 29,1242 | 7 | 3,29×10⁻⁸ |
| 5000 | 24,8125 | 8 | 4,44×10⁻⁸ |
| 5500 | 20,0403 | 10 | 4,94×10⁻⁸ |
| 6000 | 15,1118 | 12 | 4,59×10⁻⁸ |

En el control científico de 4500 RPM con escape de 0,85 m y Wiebe de 40°, el par pasa de 29,9834 a 30,0113 N·m (+0,093 %), la masa fresca al cierre de admisión de 433,68 a 433,90 mg, la VE de ≈0,9326 a 0,93303, el trabajo bruto de 500,96 a 501,22 J, el bombeo de −42,28 a −42,18 J y la fricción de 81,89 a 81,90 J. La fracción residual pasa de 4,77 a 4,756 %. CA50 permanece en 366,678°. La diferencia de fricción surge del estado calculado con los mismos coeficientes. La curva Desktop anterior usa otra duración del pulso y su punto de 4500 RPM pertenece a otro ensayo.

## 5. Resolución 2T y clasificación A/B

En el ejemplo de recuperación, par al eje en N·m con refinamiento simultáneo en ambos conductos y controles de paso y periodicidad explicitados en `--stage12`.

| RPM | ×1 | ×2 | ×4 | ×8 | ∣×8−×4∣ |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 4000 | 7,34690 | 7,05017 | 6,98304 | 6,96870 | 0,01434 |
| 5000 | 9,67157 | 9,69350 | 9,71682 | 9,67185 | 0,04497 |
| 6000 | 7,15582 | 7,18933 | 7,18338 | 7,17428 | 0,00910 |
| 7000 | 6,91660 | 6,73557 | 6,72626 | 6,70815 | 0,01811 |
| 8000 | 6,39292 | 6,14758 | 6,11274 | 6,10928 | 0,00347 |
| 9000 | 6,11856 | 5,95709 | 5,93447 | 5,87839 | 0,05608 |
| 9500 | 6,56237 | 6,39723 | 6,41009 | 6,40140 | 0,00869 |
| 10000 | 6,95646 | 6,89628 | 6,95244 | 6,95898 | 0,00654 |

×1 es exploratoria. Contra ×8 difiere aproximadamente 5,43 % a 4000 RPM, 4,64 % a 8000 y 4,09 % a 9000. Las secuencias de 5000, 9500 y 10000 RPM son no monótonas. El último salto es sensibilidad observada y no una estimación asintótica del error. A 9000 RPM, de ×1 a ×8 se reduce la masa fresca atrapada de 63,91 a 62,56 mg y el trabajo de cilindro de 61,71 a 60,15 J. El trabajo del cárter cambia de −6,049 a −6,014 J. En la misma secuencia el pico de presión cae de 2,581 a 2,551 MPa. A 9500 RPM todos los niveles alcanzan periodicidad en este ejemplo. El contraste temporal fijo de ×2 da 6,14540 N·m a 8000 y 6,39576 N·m a 9500 frente a 6,14758 y 6,39723 N·m con el control de referencia. La sensibilidad temporal observada aquí es menor que la espacial.

En un A/B con condiciones compatibles, el contraste compara directamente (D_h=B_h-A_h) en ×1, ×2 y ×4 emparejados. La banda observada es (2\max(|D_2-D_1|,|D_4-D_2|)). Es un criterio conservador empírico de clasificación del software. Un signo cambiante, una variación creciente o un margen numérico insuficiente impiden atribuir superioridad. Una diferencia dentro de banda con signo estable se muestra como indistinguible. Un punto fallido queda sin contraste. La banda no tiene interpretación probabilística ni representa la variabilidad de un banco de ensayo.

En el ejemplo 2T a 9000 RPM, alargar el difusor 40 mm y ajustar el número de celdas en B da diferencias de +1,02856, +0,98755 y +1,06646 N·m. El último salto crece y Dino2 lo clasifica como sensible a discretización. Manteniendo ×4 y endureciendo CFL, paso y tolerancia, D pasa a +1,06883 N·m. El cambio temporal de 0,00238 N·m es menor que el último salto de la serie emparejada. Una modificación de 10 µm da efectos sensibles a 4000 y 7000 RPM. El control de 0,1 µm a 5000 RPM da D de 5,806×10⁻⁷ N·m en ×4 y 5,674×10⁻⁷ N·m al refinar el tiempo. La diferencia de este control es reproducible en el modelo, a una escala sin interpretación de taller. Las pruebas de clasificación del núcleo incluyen también una diferencia de signo estable dentro de banda y una diferencia nula.

Refinar sólo escape de ×1 a ×2 cambia el par 0,29623 N·m a 4000 RPM y 0,16346 N·m a 9000. Refinar sólo admisión lo cambia 0,00203 y 0,00255 N·m. La diferencia del escape en esta geometría domina. Las trazas de presión de lumbreras, caudal, trabajo de cilindro/cárter, fase angular, barrido y atrapamiento quedan en los JSON instrumentados.

## 6. Banco científico y contraste del pleno

Los 112 casos de descarga ideales y los 24 de mezclas completaron su integración. El caudal estacionario de los 112 casos difiere como máximo 0,128 % de la referencia algebraica independiente. Se repitieron la inversión alrededor de ±10⁻⁶ Pa, los máximos de flujo, los estados límite y los pulsos de apertura. El banco de choques móviles contrasta posición y estado posterior en 128 y 256 celdas. A abertura plena, el desvío máximo de posición fue 4,63 mm en los instantes ensayados. B8 registra presión de pleno, reflexión y transmisión en ambas ramas con dos volúmenes. Sus desbalances máximos registrados fueron 1,99×10⁻¹⁷ kg y 1,49×10⁻¹² J. La campaña de redes aisladas y compartidas cubre 2, 3 y 4 cilindros en 4T y 2T, con variantes asimétricas y los balances conservativos por cilindro y conjunto.

La matriz 4T de 0,25/0,40/0,65/0,85 m se repitió a 3000, 4500 y 6000 RPM en grados y en segundos. La duración en segundos equivale a 40° a 4500 RPM y el contraste usa periodicidad de 10⁻⁶. El rango de par entre longitudes para los tres regímenes con esa duración temporal fue 31,7075–32,6470, 29,7373–30,0113 y 17,0413 N·m con dispersión menor a 0,0001 N·m en el último caso. Los archivos conservan cada geometría, sus trazas y los controles de cada ejecución.

El A/B del pleno de 2,4 a 3,6 L a 4000 RPM mantiene el signo previo. Los contrastes de potencia al eje se calculan como B−A en la misma resolución.

| Tronco | Diferencia ×1, W | Diferencia ×2, W | Diferencia ×4, W | Etapa 13 ×4, W |
| ---: | ---: | ---: | ---: | ---: |
| 0,38 m | −603,82 | −620,05 | −622,50 | −622,27 |
| 0,55 m | −467,27 | −470,15 | −469,98 | −468,58 |

Las bandas de comparación emparejada son aproximadamente 32,46 y 5,75 W. Para cada longitud, el cambio B−A del experimento temporal más fino fue −622,80 y −470,42 W, diferencias de 0,30 y 0,43 W respecto de ×4. Las variaciones de par a ×4 son −1,486 y −1,122 N·m. En cada cilindro, la masa fresca cae aproximadamente 6,95 y 12,43 mg y la masa neta de admisión cae 7,41 y 13,26 mg, según el tronco. El trabajo neto cae 9,57 y 7,52 J por cilindro. El bombeo cambia −0,84 y +6,73 J, respectivamente. A fase de cigüeñal fija, el coeficiente de Fourier de orden 2 del pleno cambia −25,31° y −69,46° de fase y +4,20 y −7,77 kPa de amplitud. Son resultados del modelo sintético sometidos a contraste de discretización.

## 7. Dominio acústico defendible

La malla 1D propaga y refleja ondas y el pleno compacto almacena masa y energía. El modelo de abertura conserva la caída cuasiestacionaria y la reacción axial, con respuesta instantánea. El arnés armónico, corrigiendo el estado medio antes de excitar, reproduce parte imaginaria despreciable tanto con sesgo de flujo como sin él. En un orificio circular de espesor despreciable y sin sesgo, la referencia inercial tiene (Z/(\rho c)=i(\omega/c)A/K), con (K=2r_o). La abertura puede ser geométricamente compacta (kD\ll1) y aun así su reactancia importar por (A/K).

El dominio defendible exige (|\omega A/(cK)|\ll1) frente a la escala de impedancia del conducto, o reactancia despreciable frente a la resistencia en el caso medido con sesgo, y cambios de abertura lentos comparados con la relajación del chorro. Estas son condiciones de escala, no un límite de RPM universal. Con un ducto de 40 mm, α=0,3 y aire a 300 K, el orificio ideal da |ωA/(cK)|≈0,083 a 80 Hz y ≈0,83 a 800 Hz. La fase del espectro motor y la geometría real de válvulas impiden extrapolar la referencia de orificio a un (K) único. Se difiere la inertancia local junto con la identificación de espesores, conductividad acústica, sesgo, armónicos y pérdidas con datos de ensayo. [Brandão y Schnitzer, impedancia de orificios cilíndricos](https://doi.org/10.1017/jfm.2020.187)

Como escala de comparación, la frecuencia de encendido es 37,5 Hz en el 4T mono a 4500 RPM, 66,7 Hz en el 4T de dos cilindros espaciados a 4000 RPM y 158,3 Hz en el 2T mono a 9500 RPM. En el orificio ideal anterior los cocientes correspondientes son 0,039, 0,069 y 0,164. El tercer armónico del último caso ya da ≈0,49. Por tanto, una frecuencia fundamental baja no basta para verificar la fase de los pulsos al variar geometría o apertura en el motor.

## 8. Rendimiento y Desktop

En el perfil 2T físico a 5000 RPM se ejecutaron cuatro repeticiones en serie por revisión y se descartó la primera. La mediana de CPU fue 1,95 s en etapa 13 y 2,52 s en etapa 14B (+29 %), con 1,95 y 2,50 s de pared. El tiempo instrumentado en la interfaz pasa de 0,235 a 0,750 s y explica la mayor parte del aumento. La preparación de conductos sigue siendo el tramo de mayor costo, alrededor de 1,35–1,39 s. Se mantuvieron 27 ciclos, unas 49 900 evaluaciones y cerca de 55 kB asignados por la hebra tras la primera ejecución. El refinamiento ×8 final consumió aproximadamente 2–4,9 min de pared por punto bajo concurrencia. Se conservaron los perfiles iniciales con más carga concurrente para identificar el efecto de la contención. Los perfiles no separan el costo de cada operación interna de las raíces.

Se conservaron identidades de archivo `ports-v1` y `ports-v2` para visualizar los resultados históricos. Un contraste actual requiere revisiones compatibles. Desktop conserva A, marca RPM incompletas, restaura curvas históricas y exige recalcular con la revisión actual para comparar cuando corresponde. El paquete extraído carga su propio `coreclr.dll` 10.0.15. Los recorridos cubren Otto, el 4T reactivo, MUSCL con 11/11, el 2T y ambos multicilíndricos, con estudio, contraste, recuperación, exportación, aplicación de B, cancelación y protección de A. El 2T nativo preserva un punto periódico en una curva parcial y rechaza el archivo manipulado de auditoría.

El ensayo A/B multicilíndrico 2T reveló que el guardado esperaba el identificador de modelo en el campo de revisión numérica. La corrección vincula el contraste con las dos curvas que lo originaron y comprueba que sus revisiones coincidan. Las curvas históricas compatibles conservan su procedencia. El smoke verifica la clasificación mediante los tres refinamientos exportados y luego guarda y reabre el proyecto. A 5000 RPM, en su geometría sintética, la diferencia de ×4 es +1,05552 N·m con banda observada de 0,21330 N·m.

## 9. Registro histórico

Las fuentes de reproducción de los hallazgos anteriores son las auditorías originales, [etapa 11](etapa11.md), [etapa 12](etapa12.md) y [etapa 13](etapa13.md). La severidad corresponde a la auditoría que abrió el hallazgo. Los estados anteriores se resumen a partir de etapa 12 y la evidencia posterior.

| ID | Causa y estado anterior | Intervención o contraste de etapa 14B | Riesgo residual, dominio y datos externos | Estado final |
| --- | --- | --- | --- | --- |
| C13-01 · P2 | Residuo de raíz amplificado cerca del equilibrio | Reproducción exacta, matriz áreas/sentidos, verificación de masa local y límite sónico | Cierre compacto, sin medición de Cd | CORRECTED |
| C13-02 · P1 | Inconsistencia gamma congelado y (h(T)) NASA | Energía y momento comunes, Rankine–Hugoniot, Cantera y límite α→1 | Composición congelada, sin cinética de choque | CORRECTED |
| C13-03 · P2 | La cara podía salirse del dominio antes de publicar flujo | Rechazo probado de ambos mínimos de auditoría y reintento acotado | Ventana 200–6000 K en estados de transporte | CORRECTED |
| C13-04 · P2 | Abertura sin reactancia, abierto | Prueba armónica equilibrada y límite de escala | Se necesita impedancia con geometría y sesgo medidos | DEFERRED WITH JUSTIFICATION |
| C13-05 · P3 | Pasos impresos en ms | Tiempo medido y columnas separadas | Pared depende de concurrencia | CORRECTED |
| C13-06 · P2 | Ciclo periódico sensible a controles del ejemplo | 11/11 y balances en WPF y portable | Otros motores pueden requerir distinta resolución | CORRECTED |
| 14B-01 · P2 | Guardado de contraste 2T multicilíndrico con identidad incorrecta | Contraste de tres niveles, guardado y reapertura verificados | Los archivos históricos conservan el sello disponible | CORRECTED |
| NF-01 · P0 | Pleno 0D sin dinámica, corregido en compacto | B8, redes y transmisión | Pérdidas de unión y resonancias internas requieren ensayo | VERIFIED WITH RESTRICTIONS |
| NF-02 · P1 | Acoplamiento de abertura, antes corregido con alcance limitado | Continuidad local, estados térmicos y choques | Mezcla compacta con Cd efectivo | CORRECTED |
| NF-03 · P1 | Boca anterior absorbente, corregida para proyectos nuevos | Modos históricos y exterior de presión en regresión | Radiación física requiere ensayo | VERIFIED WITH RESTRICTIONS |
| NF-04 · P1 | Modo legado seleccionable, mitigado | Procedencia y reapertura preservadas | Comparaciones entre revisiones se inhiben | MITIGATED |
| NF-05 · P2 | Amortiguamiento pulsante, pendiente | Cierre de pared preservado, contraste temporal sin recalibración | Necesita presión y pérdidas pulsantes medidas | OPEN |
| NF-06 · P2 | Etapa correctora 2T, corregida | Diagnóstico antes de aceptar, balances multicilíndricos | Dominios de estado y periodicidad restringidos | CORRECTED |
| NF-07 · P2 | Integridad de persistencia, parcial | Compatibilidad y pruebas de archivos manipulados | Un hash comprueba integridad y la autenticidad requiere otro mecanismo | MITIGATED |
| NF-08 · P2 | Química pobre de pseudoaire, restringida | Comprobación de dominio preservada | Química rica requiere balance y referencia independientes | VERIFIED WITH RESTRICTIONS |
| NF-09 · P3 | Boca sin corrección de extremo, pendiente | Regresión de boca ideal | Radiación y longitud efectiva requieren medición | DEFERRED WITH JUSTIFICATION |
| NF-10 · P3 | Verificación incompleta, mejorada | Matriz de casos, Cantera, transitorio y Desktop | Sigue faltando banco experimental | MITIGATED |
| F-01 · P0 | Reflexión exterior, corregida con alcance | B8 y referencia rígida/de presión | Exterior ideal | VERIFIED WITH RESTRICTIONS |
| F-02 · P1 | Pared y fricción pulsante, parcial | Balances repetidos con pared original | Requiere serie dinámica | OPEN |
| F-03 · P1 | Malla fina 2T, parcial | ×1–×8 y 9500 RPM, sensibilidad no monótona | Sin estimación asintótica general | MITIGATED |
| F-04 · P1 | Fase de combustión con RPM, opción explícita | Prescripción por grados y segundos | CA10/50/90 de motor real para calibrar | VERIFIED WITH RESTRICTIONS |
| F-05 · P1 | Química rica, restringida | Restricción preservada | Dominio de equivalencia y especies reducidas | VERIFIED WITH RESTRICTIONS |
| F-06 · P1 | Un fallo descartaba la curva, corregido | Pruebas de curva parcial y comportamientos de dominio | La curva puede ser parcial | CORRECTED |
| F-07 · P1 | Lámina, transfer y barrido 2T, pendiente | Preservados y comparación de masa atrapada | Requiere presión, lámina y barrido medidos | OPEN |
| F-08 · P1 | Integridad accidental y plausibilidad limitadas | Exportación y reapertura, prueba de manipulación | Autenticidad fuera del alcance | MITIGATED |
| F-09 · P1 | Procedencia, parcial | Sello `ports-v3` y commit limpio de compilación | Los archivos viejos conservan lo que registraron | MITIGATED |
| F-10 · P2 | Unión y fase compartidas, mejoradas | Dos a cuatro cilindros y configuraciones asimétricas | Complejidad geométrica del pleno requiere ensayo | VERIFIED WITH RESTRICTIONS |
| F-11 · P2 | MUSCL y malla exploratoria, parcial | Sensibilidad cuantificada y controles Desktop | ×1 no basta en general para comparación 2T | MITIGATED |
| F-12 · P2 | Sin radicales en química reducida, abierto | Se verificó composición fija en el salto | Equilibrio UV ampliado requiere referencia independiente | DEFERRED WITH JUSTIFICATION |
| F-13 · P2 | Alzada y Cd simplificados, abierto | Coeficientes físicos de referencia conservados | Perfil de leva y mapa Cd deben medirse | OPEN |
| F-14 · P2 | Aceptación de estado y periodicidad, mejoradas | Balances, 11 RPM y rechazo antes del flujo | Periodicidad sólo en dominio ensayado | VERIFIED WITH RESTRICTIONS |
| F-15 · P2 | Referencias externas parciales | Cantera 3.2 y programas independientes | Verificación numérica, ensayo de válvula pendiente | MITIGATED |
| F-16 · P2 | Interfaces compartidas, mejoradas | Un cierre con reutilización mono/multi, 2T y 4T | Integradores siguen separados | VERIFIED WITH RESTRICTIONS |
| F-17 · P2 | Trazabilidad local preservada | Referencia de recuperación y rama propia | Dependencia de historial Git local | VERIFIED WITH RESTRICTIONS |
| F-18 · P2 | Hipótesis y estado visibles, parciales | Controles efectivos y revisión de interfaz visibles | Diagnóstico depende de alcance del ensayo | MITIGATED |
| F-19 · P3 | Ejemplos sintéticos documentados | Resultados anteriores y posteriores sin ajustes de Cd | Sin validación de predicción de motor real | VERIFIED WITH RESTRICTIONS |
| F-20 · P3 | Reproducción aislada, preservada | Campañas y manifestación de procedencia | Dependencias externas para reproducir Cantera y UI | VERIFIED WITH RESTRICTIONS |

## 10. Entrega y condiciones para etapa 15

**Veredicto — NUMERICALLY VERIFIED / EXPERIMENTALLY UNVALIDATED.** La recuperación numérica queda cerrada dentro del dominio ensayado de la interfaz compacta. El dominio se acota por la composición congelada en saltos, propiedades de 200–6000 K, el régimen cuasiestacionario del puerto y la periodicidad comprobada para cada caso. La etapa conserva la validación experimental como pendiente.

El SHA-256 del ZIP Windows x64 autocontenido y el de sus binarios se conservan en `portable-hashes.txt` del paquete de evidencia. La [guía de reproducción](../evidence/stage14b/README.md) identifica bancos, configuraciones y pasos de compilación y verificación. Las carpetas externas preservan trazas, CSV, perfiles, logs y hashes.

La etapa 15 puede investigar inertancia reactiva, pérdidas pulsantes de pared, radiación, geometría y pérdidas de uniones, dinámica de láminas y barrido, Cd en banco y química ampliada. Para atribuir precisión predictiva se necesitan datos independientes calibrados de presión dinámica, alzada, flujo y potencia, además de condiciones de ensayo reproducibles. La siguiente contraauditoría debe verificar los tres mínimos locales, los controles efectivos del ejemplo 4T y los contrastes emparejados antes de incorporar esos cambios.

### Respuestas de cierre

1. Sí. El salto de 77,94 kJ/kg se reduce a 0,000053 J/kg en el caso histórico.
2. Sí. Las ondas de interfaz, el choque, la garganta y el plano medio usan las mismas propiedades del donante de composición fija.
3. Sí, para los saltos y estados ensayados. En choque móvil la relación de energía se verifica en el marco del frente y la cara transporta su entalpía total en el marco del tubo.
4. Sí. El choque compresivo aumenta entropía y las ramas de tobera y rarefacción ideal siguen su isentrópica.
5. Sí. La garganta y la cara se validan antes de publicar el flujo. Los estados que exceden 200–6000 K generan diagnóstico de dominio.
6. Sí. Apertura y salto de presión tendiendo a cero producen un caudal tendiendo a cero. Cierre exacto retiene sólo la reacción de presión.
7. Sí. Garganta y cara comparten el caudal conservativo; el arnés independiente lo reconstruye de estados primitivos.
8. Sí. WPF y el portable extraído completan los 11 RPM de 1000 a 6000.
9. Los controles verificados de fábrica son CFL 0,20, paso angular de 0,15° y tolerancia periódica de 10⁻⁶. A 4000 RPM reducen 52 ciclos a 9 con la revisión nueva. Se conserva la elección de cada proyecto previamente guardado.
10. En el ejemplo 2T, el salto ×4–×8 fue de aproximadamente 0,0035 a 0,0561 N·m y el control temporal de ×2 a 8000 y 9500 RPM cambió el par 0,00218 y 0,00147 N·m. Estas diferencias observadas no acotan el error respecto de la solución exacta.
11. Empareja resoluciones de A y B, contrasta directamente la diferencia de cada nivel y exige estabilidad frente a la banda observada. Las diferencias sensibles e indistinguibles se muestran como tales.
12. El régimen que permite omitir almacenamiento local de momento por escala de impedancia y tiempo de relajación. Su justificación depende de geometría, frecuencias y flujo medio.
13. Los bancos y ejemplos verificados conservaron sus regresiones. Los valores de par cambian ligeramente por la formulación termoquímica revisada.
14. CPU mediana de 2,52 s frente a 1,95 s en la referencia 2T perfilada, aproximadamente +29 %. La preparación de conductos sigue dominando y el aumento se concentra en la interfaz.
15. Sí, en los ensayos indicados y en el dominio térmico y cuasiestacionario declarado. El veredicto es **NUMERICALLY VERIFIED / EXPERIMENTALLY UNVALIDATED**.
16. Inertancia, radiación y pérdidas pulsantes, geometría real de uniones y aperturas, experimentos de Cd y barrido, láminas y química ampliada con validación independiente.
