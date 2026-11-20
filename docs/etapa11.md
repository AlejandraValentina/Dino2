# Etapa 11 · Recuperación científica de Dino2

**Veredicto · CONDITIONAL / RESTRICTED.** Los extremos de presión, las fuentes de pared y la conservación de resultados por régimen tienen verificación numérica. El caso 2T a 8000 RPM sale del dominio termoquímico al refinar la malla. Parte de la geometría 4T con escape reflectante alcanza el mismo límite. El dominio documentado permite experimentos numéricos y comparaciones locales. La exactitud frente a motores reales sigue sin determinarse.

## Línea de base y causa del defecto

Se preservó `02c6715da633127ee48e1be61386cab1686e0076` en `recovery/stage10-02c6715`. La recuperación se desarrolló en `stage11/recuperacion-cientifica`. La auditoría original, sus programas y los archivos de prueba se conservaron en `E:/dino2-audit`. El SHA-256 de `INFORME-AUDITORIA-DINO2.md` es `333188B93314D0E3D40D829C63C2260F62B0A31B2AC83C91B998AFB0E478A719`.

El extremo histórico suministraba un estado de remanso como lado exterior de un problema de Riemann. Para perturbaciones pequeñas alrededor de un estado común, ese cierre deja salir la onda con reflexión casi nula. Un extremo ideal a presión fija debe producir una onda de presión de signo opuesto. En cambio, una terminación rígida produce el mismo signo. El balance de energía cierra con cualquiera de los tres y por sí solo no identifica el error.

| Prueba independiente | Observado | Referencia |
| --- | ---: | --- |
| Extremo adaptado histórico, pulso débil | +0,00008 | Aproximadamente 0 |
| Extremo a presión, pulso débil | −0,95786 | −1 en el límite lineal sin disipación |
| Extremo rígido, pulso débil | +0,95765 | +1 en el mismo límite |
| Extremo a presión, pulso de 20 kPa | −0,77100 | No lineal. Los extremos rígido y de presión se observan con el mismo esquema, malla y distancia recorrida. |
| Periodo fundamental cerrado–abierto | 11,520483 ms | 11,521112 ms con 4L/a. Diferencia del 0,0055 %. |

El ensayo E1 histórico también se reprodujo ejecutando el programa de la auditoría contra el binario preservado. Generó reflejos de magnitud aproximada 0,000–0,024 para el extremo histórico. La caja 0D de un colector es un caso distinto. Su presión evoluciona con el balance conservativo y puede transmitir ondas entre ramales y reflejarlas ante cambios de área. La prueba histórica de escalón de área y pulso transmitido sigue siendo pertinente. El colector compacto mezcla instantáneamente su contenido y no resuelve pérdidas direccionales ni la geometría espacial interior.

## Formulación aplicada

Con la coordenada positiva hacia el tubo, la salida subsónica se cierra con presión estática exterior y composición local. Para una rarefacción se usa `u* = u + 2a/(γ−1) [(p*/p)^((γ−1)/(2γ)) − 1]`, con la rama de Rankine–Hugoniot en compresión y un límite sónico cuando la onda de expansión alcanza Mach 1. Una descarga ya supersónica exporta el estado interior. Para entrada se emplean composición y remanso exteriores. Se impone entalpía total exterior en el flujo de energía y se obtiene el estado de cara igualando la característica interior y `h₀ ≈ cp T* + u*²/2` con γ congelado en el contorno. La energía total advectada incluye la referencia química. La aproximación local de γ no sustituye las propiedades dependientes de temperatura del interior. Es un extremo ideal sin impedancia de radiación, longitud acústica añadida, vena contracta ni descarga externa tridimensional. [Poinsot y Lele, contornos característicos](https://ntrs.nasa.gov/citations/19920065566).

Cada tramo admite una pared opcional. Para diámetro local D, densidad ρ y velocidad u, el flujo de momento recibe `−fD ρu|u|/(2D)` en Pa/m. En régimen laminar se usa el límite `−32μu/D²`. En turbulento se usa el factor de Darcy de Haaland, con transición continua de 2300 a 4000 en Reynolds. Sutherland aproxima μ con aire. El aporte de energía por volumen es `4 Nu k (Tpared−Tgas)/D²` en W/m³, con `k=μcp/Pr`, Pr=0,71, Nu=3,66 laminar plenamente desarrollado y Gnielinski para tubo liso turbulento. Una pared estacionaria adiabática disipa movimiento en energía interna y no extrae energía total. El integrador limita el paso explícito por la relajación de pared y registra el calor firmado. La pared usa temperatura fija, rugosidad uniforme, hipótesis de flujo interno desarrollado y correlaciones cuasiestacionarias. Se omiten la radiación, la inercia de pared y el incremento térmico por rugosidad. La prueba de caída laminar contra Hagen–Poiseuille difiere aproximadamente 0,6 %. Los ensayos térmicos comprueban signo, equilibrio uniforme y balance. Falta contraste cuantitativo con escape pulsante caliente. [Haaland](https://doi.org/10.1115/1.3240948) · [Jones y Brown, mediciones de transferencia térmica](https://ntrs.nasa.gov/api/citations/19820005954/downloads/19820005954.pdf).

Se mantiene Wiebe con duración prescrita en segundos o, explícitamente, en grados de cigüeñal. Para el modo en segundos, `Δθ=6·RPM·Δt`. Con grados fijos el tiempo efectivo cae con las RPM. El ejemplo 2T de 1,1 ms y su alternativa de 33° coinciden a 5000 RPM. La ley 4T condicionada conserva su duración propia. A 9000 RPM se reprodujo una fracción residual de 0,26576 frente al límite de 0,20. En la misma curva el resultado de 7500 RPM se conserva. Una extrapolación de presión o temperatura se marca por régimen cuando el cálculo es posible. El modo 2T rechaza equivalencia φ>1 para los modelos de gasolina/aire actuales. Cambiar el modelo de productos ricos requiere comprobaciones con Cantera y datos de combustión. [Metghalchi y Keck](https://www.sciencedirect.com/science/article/pii/0010218082901274).

## Motores completos y resultados antes/después

Los cambios de contorno modifican el instante y signo de la presión que regresa a válvulas y lumbreras. El cierre de balances se mantiene, mientras cambian llenado, masa atrapada y condiciones de combustión. Cada fila compara la misma geometría y malla salvo donde se indica.

| Caso | Histórico | Presión corregida | Presión y pared |
| --- | ---: | ---: | ---: |
| 2T 4000 RPM, par al eje (N·m) | 2,926015 | 6,571248 | 6,940426 |
| 2T 5000 RPM, par al eje (N·m) | 3,948674 | 8,716516 | 8,774600 |
| 2T 6000 RPM, par al eje (N·m) | 2,592013 | 6,807866 | 7,240501 |
| 2T 5000 RPM, pico (K) | 2371,0 | 2648,2 | 2649,1 |
| 2T 5000 RPM, masa fresca atrapada (mg) | 41,4724 | 71,7740 | 71,9789 |
| 4T bicilíndrico 4000 RPM, par al freno (N·m) | 39,597048 | 41,261449 | — |
| 2T multicilíndrico 5000 RPM, par al eje (N·m) | 7,247319 | 9,687672 | — |

A 5000 RPM en el 2T mono la potencia al eje cambia de 2,068 a 4,564 kW. La fracción de masa fresca retenida cae de 0,56724 a 0,43424 aunque sube la masa total fresca atrapada. En 4T con admisión a presión y escape histórico, la VE a 3000 RPM para longitudes 0,15/0,35/0,70/1,40 m fue 0,90209/0,92345/1,07144/0,91281, frente a 0,86052/0,86224/0,86506/0,86998 en el histórico. El mayor llenado de la cuadrícula cambia de 1,40 a 0,70 m. A 4500 RPM prefiere 0,35 m y a 6000 RPM prefiere 0,15 m con el extremo de presión. Son máximos en la malla base de un experimento de sensibilidad con escape adaptado. Requieren refinamiento y un cierre físico de escape para usarlos como clasificación de un estudio de motor.

Con ambos extremos a presión, paredes de 320/500 K y escape 0,85 m, `examples/4t-recovery-wall.json` devuelve 32,02770/28,06671/14,13276 N·m a 3000/4500/6000 RPM y VE 0,91244/0,90765/0,67722. El máximo de presión a 4500 RPM es 5,022 MPa y el máximo de temperatura 2583 K. Con tubos de escape de 0,25 o 0,40 m se cruza la cota de 200 K antes de cerrar ciclo. Con 0,65 m y las mismas paredes se completan 3000 y 6000 RPM, con fallo a 4500. En los multicilíndricos corregidos el cierre de energía en los puntos ensayados fue 2,73×10⁻¹² J (4T) y 4,32×10⁻¹³ J (2T). El comportamiento periódico de un caso utilizable no rehabilita geometrías vecinas.

En el 2T corregido, escalar todas las longitudes de la cámara por 0,8 o 1,25 con paso espacial comparable invierte la preferencia entre 4000 y 6000 RPM. El par es 5,684/8,680 N·m a 4000 y 8,110/7,463 N·m a 6000. El barrido sigue siendo sensible a la extracción fresca prescrita. A 5000 RPM, usar factores 0,75/1/1,25 produce 8,916/8,717/8,546 N·m. Es una dependencia de calibración del transporte y la mezcla en cilindro. La revisión experimental necesita series de eficiencia de barrido y gas residual, presión dinámica en puerto, geometrías y estados de láminas. [Torregrosa y colaboradores](https://doi.org/10.3390/app7050480).

En el contraste A/B de avance +3° a 5000 RPM, la diferencia de par en ×1/×2/×4 fue −0,068097/−0,071556/−0,069580 N·m con contorno histórico, clasificada `Inferior`. Con ambos extremos a presión fue −0,129970/−0,128803/−0,127629 N·m, clasificada `DiscretizationSensitive` porque el último cambio entre mallas aumentó de 0,001167 a 0,001174 N·m. El signo se mantuvo, pero el clasificador exige reducción de la diferencia entre refinamientos. Se conservó el criterio anterior para que el efecto del cambio físico fuese trazable. Los contrastes tardaron 141 s y 85 s respectivamente.

## Resolución espacial, temporal y coste

| 2T Mono, MUSCL y presión | ×1 (N·m) | ×2 (N·m) | ×4 (N·m) | ×8 (N·m) |
| --- | ---: | ---: | ---: | ---: |
| 6000 RPM | 6,807866 | 6,796313 | 6,782798 | 6,787558 |
| 8000 RPM | 6,039204 | Fuera de dominio | Fuera de dominio | Fuera de dominio |

A 6000 RPM la amplitud entre ×2, ×4 y ×8 es 0,013515 N·m (0,20 %). La secuencia cambia de sentido y no permite atribuir un orden asintótico. Reducir CFL de 0,45 a 0,20 en ×1 arroja 6,812076 N·m. Exigir residual periódico de 10⁻⁶ da 6,807984 N·m. A 8000 RPM ambas comprobaciones sobre ×1 cambian el par poco, mientras ×2/×4/×8 salen de 200–6000 K. La pérdida de margen térmico se reproduce en una celda de escape próxima al cilindro, antes del final del ciclo durante el refinamiento. Reducir el paso no elimina el cruce. Aún no se aisló la contribución relativa de la expansión local, el acoplamiento puerto–primera celda y la reconstrucción de especies cerca de ese límite. Las curvas absolutas 2T indican ausencia de contraste de malla general. El resultado ×1 a 8000 queda limitado a diagnóstico y no respalda sintonía.

En la serie E7 preservada, el modelo histórico pasó de 2,5920 a 2,7305 N·m (×1 a ×4) a 6000 RPM y de 1,2242 a 1,3715 N·m a 8000 RPM. Corresponden a variaciones de 5,3 % y 12,0 %. La base histórica del primer punto se reprodujo en el código de recuperación conservando el extremo adaptado. La serie con presión corregida aplica refinamiento equivalente de malla, paso angular y tolerancia periódica. El cambio de nivel entre series proviene del contorno. La variación dentro de cada serie expresa dependencia numérica.

Windows x64, Intel i5-10400 de 6 núcleos y 12 hilos, .NET 10. Los tiempos corresponden a una campaña de procesos en la misma máquina, con hasta dos o tres casos concurrentes e incluyen JIT y carga del sistema. Son tiempos de pared por simulación y los bytes son asignaciones del hilo registradas por GC, sin equivalencia directa al RSS. A 5000 RPM el 2T histórico tardó 3,94 s con 51 ciclos y 43.359 pasos, el de presión 2,20 s con 28 ciclos y 25.412 pasos y con pared 2,47 s con 25 ciclos y 21.996 pasos. El trabajo por paso cambia con las nuevas fuentes y los tiempos agregados también dependen de la periodicidad. En el 2T a 6000 RPM, ×1/×2/×4/×8 tardaron 2,36/8,54/27,14/128,72 s y asignaron aproximadamente 52/91/168/332 kB. ×1 necesitó 21.159 pasos y ×8 252.436. Los multicilíndricos históricos y corregidos tardaron 4,44/3,50 s (4T) y 8,51/5,66 s (2T) en los puntos citados. La carga dominante medida por evaluaciones y pasos es el avance de celdas bajo CFL. Estos datos no aíslan el coste de cada función ni miden escalabilidad con el número de cilindros. Se conserva la inversión y el control de paso existentes.

El control de escalado 2T histórico en la regresión final, con escapes y admisiones independientes a 5000 RPM, tardó 6,26/9,76/13,62 s para dos/tres/cuatro cilindros y registró 7,80/12,18/16,37 millones de evaluaciones. Las asignaciones fueron 0,54/0,74/0,95 MB. Los colectores compartidos cambian el número de ciclos. Estos resultados caracterizan el caso histórico controlado. La campaña con extremos nuevos compara un bicilíndrico representativo de cada familia.

## Registro de los veinte hallazgos

| ID | Disposición | Evidencia e impacto pendiente |
| --- | --- | --- |
| F-01 | Corregido con dominio restringido | Reflexión, resonancia, inversión de flujo, entalpía y motores completos. Extremos ideales. Parte del escape 4T sale del dominio. |
| F-02 | Implementado con alcance restringido | Caída laminar y balance térmico. Temperaturas y rugosidades declaradas. Falta calibración pulsante. |
| F-03 | Parcial | 6000 RPM con dispersión cuantificada. 8000 RPM sin solución refinada en el caso ensayado. |
| F-04 | Corregido como opción explícita | Duración prescrita en grados o segundos. Sensibilidad distinta a RPM. Requiere CA10/50/90 medidos para decidir la ley de un motor. |
| F-05 | Restringido | Se rechaza el régimen 2T con φ>1. Conservación y reparto de productos ricos siguen pendientes. |
| F-06 | Corregido | Fracción residual fuera de 0–0,2 aislada por RPM. Extrapolaciones evaluables identificadas. |
| F-07 | Parcial | Sensibilidades a cámara y extracción cuantificadas. Siguen pendientes transfer 1D, láminas dinámicas y calibración de puerto/barrido. |
| F-08 | Corregido para archivos 2T evaluados | Hash v2, compatibilidad dimensional y vínculo de configuración. Se rechaza el archivo original manipulado. SHA-256 de contenido permite comprobar integridad, sin autenticación del autor. |
| F-09 | Implementado para nuevas curvas | Identidad del modelo, revisión numérica, revisión de build, estado del árbol, fecha UTC y esquema. Las curvas históricas conservan procedencia incompleta. |
| F-10 | Delimitado | Mezcla compacta conservativa y geometría compartida. Pérdidas direccionales y diferencias de geometría por cilindro quedan pendientes. |
| F-11 | Actualizado | Ejemplos de recuperación con MUSCL. Los proyectos previos conservan su esquema. La malla necesaria depende de frecuencia y geometría. |
| F-12 | Abierto | La comparación histórica con equilibrio ampliado encontró 40–80 K de diferencia. Cambiar radicales excede esta recuperación y requiere referencia UV independiente. |
| F-13 | Abierto | Leyes de alzada y Cd simplificados. Requieren trazas de leva y banco de flujo para predicción específica. |
| F-14 | Parcial | 4T separa fallos y puntos válidos, estudios excluyen curvas incompletas del ordenamiento. Se conservan definiciones de periodicidad existentes. |
| F-15 | Mejorado | Ensayos independientes de onda, frecuencia y caída de presión. La regresión histórica adaptada conserva su alcance. Queda automatizar el contraste con Cantera. |
| F-16 | Parcial | Cierre exterior y pared compartidos. Queda evaluar integración común de ciclo sin alterar las regresiones. |
| F-17 | Preservado | Base local y rama de recuperación trazables. `main` conserva `7e3e2bb`. |
| F-18 | Parcial | Estado por régimen, límites, datos de build e hipótesis de pared visibles. Quedan revisiones de unidades y traducción de mensajes. |
| F-19 | Documentado | Los ejemplos representan entradas sintéticas. Par negativo, dispersión y falta de periodicidad son resultados diagnósticos. |
| F-20 | Parcial | Trabajo con artefactos aislados. Se leen formatos anteriores para recuperar proyectos. Los sobres 2T nuevos exigen el sello. |

## Reproducción

Con .NET 10 y PowerShell en la raíz, `pwsh -File scripts/verify.ps1` ejecuta las suites físicas, de persistencia y Desktop. `dotnet run --project tests/Dino2.FourStroke.Tests -c Release -- --recovery` ejecuta la verificación independiente de contorno, pared y registros por RPM. El mismo programa admite `--campaign engines`, `--campaign tuning`, `--campaign geometry`, `--campaign mesh`, `--campaign fine`, `--campaign cause` y `--campaign comparison`. El grupo `fine` tarda varios minutos. La CLI acepta `--4t examples/4t-recovery-wall.json salida.csv` o `--2t examples/2t-recovery-pressure.json salida.csv ángulo.csv`. Para ejecución autónoma, `pwsh -File scripts/publish.ps1` prepara la distribución Windows.

## Verificación de entrega

La solución compiló en Release sin advertencias. Pasaron los 13 grupos base, 18 grupos 4T, 15 termoquímicos y 7 de estudios, además de las suites 2T y multicilíndricas. Las pruebas Desktop se ejecutaron por separado para evitar actividad de ventanas simultánea con el lanzador de regresión. Los artefactos incluyen capturas y exportaciones. El portátil Windows autocontenido usa .NET 10.0.15. Su build procede de `4ef697b` con árbol limpio. Los commits posteriores contienen documentación y ajustes de pruebas, y preservan los binarios verificados.

Desde el ejecutable se verificaron la referencia Otto, las 11 RPM del 4T con extremos y pared, y el flujo 2T de simulación, estudio, aplicación como B, persistencia y exportación. Se reprodujo el rechazo del archivo manipulado original, se exportó una curva parcial con su RPM inadmisible y se comprobó la cancelación. El portable multicilíndrico 2T también calculó, guardó y reabrió A/B, exportó sus resultados, ejecutó un estudio y conservó A al cancelar. El refinamiento A/B de presión de la etapa se ejecutó en la campaña numérica y se conserva por separado de las verificaciones de interfaz.

El flujo multicilíndrico 4T del portable reprodujo el contraste histórico de tres mallas a 4000 RPM. B conserva su clasificación superior con la formulación adaptada de ese proyecto. Esa clasificación sirve como evidencia de regresión de software y del método de comparación. Su significado físico depende del contorno.

La siguiente investigación debe separar expansión física, reconstrucción y unión lumbrera–conducto en los casos inferiores a 200 K. Los datos de presión sincronizada a cigüeñal, presión pulsante en conducto y mediciones independientes de par permiten después identificar el dominio experimental. El conjunto de calibración debe fijar combustión, flujo y pared antes de evaluar RPM o variantes reservadas. La recuperación presenta verificación matemática, numérica y de software. La calibración y la validación experimental requieren evidencia adicional.
