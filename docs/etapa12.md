# Etapa 12 · Acoplamiento conservativo 0D–1D

**Veredicto · CONDITIONAL / RESTRICTED.** La apertura parcial y la unión al colector dinámico superan contrastes independientes, y las configuraciones antes fallidas de la serie 2T vuelven a producir soluciones periódicas al refinar. La verificación cubre las geometrías y regímenes documentados. El cierre compacto no resuelve el campo dentro del chorro o del colector. El error de discretización varía con el régimen y sigue pendiente la validación con mediciones de motores.

## Base reproducida

La rama `stage12/acoplamiento-conservativo` parte de `d8970e9ff65a2688c6087c36660e89a666d60adb`, preservada como `recovery/stage11-d8970e9`. `main` permanece en `7e3e2bb` y la referencia local de `origin/main` en `9e0a6b1`. Para repetir los experimentos se copiaron los programas y las fuentes instrumentadas de `E:/dino2-audit/c11` a un directorio externo. En esa copia se redirigió únicamente el destino de volcados de `lowt`. La evidencia anterior conserva sus archivos.

| Reproducción de etapa 11 | Resultado |
| --- | --- |
| B1, B3 y B6 | Reflexión con flujo medio y ambos signos de pulso, gasto estacionario y cinco semiproblemas convergentes. Se conserva la formulación de la boca exterior. |
| B8 | Con 0,5/2,4/10/30 L, el flujo HLLC desde un estado en reposo reflejó +0,057/+0,014/+0,004/+0,001 veces la amplitud. |
| LOWT, 8000 RPM ×2 | Celda 64/64, ciclo 5, 171,315°. Mach 1,64 previo al estado inadmisible. El diagnóstico se produjo fuera del reintento de paso. |
| M2T, 8000 y 9500 RPM | Las corridas en ×2 y ×4 fallaron. Las corridas ×1 alcanzaron periodicidad. |
| T4EXH | Fallo de 0,25 y 0,40 m a 3000/4500/6000 RPM. A 0,65 m falló 4500 RPM. A 0,85 m convergieron los tres regímenes. |
| MULTI | El caso 4T aislado con presión falló cerca de 611°. Las redes compactas podían conservar energía aun con absorción acústica espuria. |

## NF-02 · Acoplamiento de válvulas, lumbreras y láminas

La formulación anterior intersectaba la capacidad de la válvula con una recta que partía de la presión de pared y tenía pendiente proporcional a A/a. Dependía de una característica que, al cambiar el signo de u±a, podía dejar de llegar a la cara. Además, calculaba el término convectivo de momento con la densidad de la celda y no con la traza que transportaba masa y energía. En el caso reproducido, una onda reflejada acentúa el gradiente de presión, el caudal permanece aproximadamente bloqueado y aparece una transición supersónica junto a la lumbrera. La recta deja de ser una condición admisible en ese régimen. La etapa 11 detectaba algunas descomposiciones sólo en la siguiente evaluación, después de aceptar el estado corregido.

En la revisión `ports-v1`, la cara se divide en abertura efectiva `Aₒ=min(Cd·Ageom,A)` y pared de área `A−Aₒ`. Se suma el flujo físico de la traza obtenida del semiproblema de Riemann sobre la abertura y la reacción de presión de la pared sobre el resto. Masa, cada especie, marcador y entalpía total usan el mismo flujo de abertura con signos opuestos en conducto y volumen. La reacción de pared sólo modifica el momento axial. El volumen 0D mezcla el flujo transferido y su momento axial no es un grado de libertad. La lámina conserva su ley de elasticidad cuasiestática, con el salto entre presión de reposo local y cárter, y se cierra ante flujo hacia el tubo. La parte sólida usa la reflexión simétrica de Euler.

En flujo volumen→conducto se conserva composición y entalpía total de remanso del volumen, incluida la referencia química. Se empareja la característica del tubo con la descarga isentrópica y se limita a condición sónica cuando el emparejamiento pediría una velocidad mayor. Conducto→volumen usa la rarefacción o choque de la traza interior con presión del volumen. Para descarga supersónica se exporta la traza interior mientras el choque sea evacuado. Si la contrapresión hace que su velocidad entre al tubo, se aplica el salto de Rankine–Hugoniot. El flujo de momento de cara tiene la forma `Aₒ(p*+ρ*u*²)+(A−Aₒ)p_pared`. El emparejamiento congela γ local. Los volúmenes y las celdas conservan sus propiedades variables. La aproximación homogeneiza el chorro sobre la sección de conducto en la actualización de volúmenes finitos. Los detalles de vena contracta, expansión tridimensional, perfiles tras la válvula y recuperación de presión en un escalón requieren una geometría o calibración adicional. [Relaciones de flujo compresible, NACA 1135](https://www.nasa.gov/wp-content/uploads/2023/03/equations-tables-charts-compressibleflow-report-1135.pdf) · [Blair y Magee, cambios de área no isentrópicos](https://saemobilus.sae.org/papers/non-isentropic-analysis-varying-area-flow-engine-ducting-932399).

Para 2T mono y multicilíndrico la etapa correctora de Heun se construye en un búfer separado. Se valida masa y energía del cilindro y cárter, y densidades parciales y energía interna de cada celda y colector antes de intercambiar buffers. Un estado inadmisible rechaza el paso y conserva el anterior. La nueva tentativa reduce el paso conforme al CFL y al controlador. El integrador 4T ya verificaba esas variables antes del intercambio. Se preservaron el rango de 200–6000 K y los limitadores MUSCL. La modificación de acoplamiento recuperó el caso exacto a 8000 RPM antes de añadir las comprobaciones de pasos o cambiar la reconstrucción. Con ello se distingue la causa primaria de sus amplificadores. La campaña conserva el esquema MUSCL.

Los ensayos automatizados usan aire perfecto para comparar ambos lados de una onda estacionaria de Mach 2 con el salto de Rankine–Hugoniot. También verifican la rama cerrada, la vena sónica a dos contrapresiones, la descarga subsónica y la entalpía y composición del donante. Un estado a 199 K demuestra el rechazo de datos fuera del dominio.

## NF-01 · Colector 0D dinámico

Las conexiones tronco–pleno y pleno–ramal emplean la presión y composición calculadas por el balance instantáneo del pleno. En la aproximación de onda débil, para un ramal de área Aᵣ, un tronco Aₜ y un volumen V, la presión cumple `dp/dt=(c/V)[2 Aᵣ p_inc−(Aᵣ+Aₜ)p]` antes de que la onda reflejada por la boca vuelva al pleno. La onda reflejada tiene amplitud `p−p_inc` a la salida del pleno. La transmitida sigue la presión del pleno con su retardo en el tronco. Una convolución independiente resuelve esa referencia incluyendo el primer retorno local del tronco desde la boca. El tamaño, la geometría, la frecuencia y el tiempo de tránsito determinan signo y fase. El modelo mantiene la compresibilidad y el almacenamiento del volumen. El balance de masa, especies y energía integra cada intercambio una sola vez. [Conservación en uniones de red](https://www.sciencedirect.com/science/article/pii/S0021999117303546).

| V (L) | R mínimo nuevo | Error máximo del ramal / pulso incidente | Error máximo del tronco / pulso incidente |
| --- | ---: | ---: | ---: |
| 0,1 | −0,6327 | 3,12 % | 0,45 % |
| 0,5 | −0,8542 | 3,52 % | 0,20 % |
| 2,4 | −0,9453 | 3,52 % | 0,05 % |
| 10 | −0,9678 | 3,51 % | 0,01 % |
| 30 | −0,9726 | 3,51 % | <0,01 % |

Con el ramal de 0,8 m y secciones de 51/40 mm el mínimo cambia a −0,7478. Las diferencias de presión frente a la referencia independiente incluyen la disipación y fase de MUSCL a 400 celdas. El ensayo comprueba simultáneamente reflexión y transmisión. El plenum de 30 L ilustra el límite de alta compliancia para ese pulso.

La campaña de redes calculó 2, 3 y 4 cilindros con ramales aislados y con colectores, todos con presión. En el bicilíndrico 4T a 4000 RPM, la red eleva la amplitud pico a pico en el pleno de admisión a 12,145 kPa y cambia la amplitud en el puerto de 176,143 a 152,518 kPa. En el 2T a 5000 RPM, la red genera 4,408 kPa de variación en el pleno y cambia la amplitud de puerto de 23,638 a 24,871 kPa. Al modificar simultáneamente fases y longitud de ramal, las amplitudes y los trabajos por cilindro dejan de ser iguales. En 4T, 304,03/304,03 J pasan a 316,36/293,64 J. En 2T, 58,20/58,20 J pasan a 56,62/56,09 J. Se conserva por separado el contraste de fases y geometría como sensibilidad conjunta, sin atribuir el efecto a una sola variable. La red 4T presentó residuales de energía menores que 1,3×10⁻¹¹ J y de especies de interfaz menores que 9,0×10⁻¹⁸ kg en los puntos ensayados.

## Campaña de motores

El par al eje 2T depende de malla y RPM. A 8000 RPM se obtuvieron 4,28356/4,20722/4,18653/4,17310 N·m con ×1/×2/×4/×8. La diferencia ×1→×8 es 2,65 % en valor relativo al refinado. ×2→×8 es 0,82 %. A 4000 RPM la diferencia ×1→×8 del mismo ejemplo fue 4,08 %. A 9000 RPM fue 4,94 %. Se conserva la malla base como modo exploratorio. Una malla ×2 es una mejora para estos casos, con coste mayor y sin garantía universal. La referencia física final exige resolución comprobada y mediciones independientes.

| 2T, RPM | ×1 (N·m) | ×2 (N·m) | ×4 (N·m) | ×8 (N·m) |
| --- | ---: | ---: | ---: | ---: |
| 4000 | 6,18177 | 6,02092 | 5,93979 | 5,93959 |
| 5000 | 6,32836 | 6,35501 | 6,31069 | 6,30521 |
| 6000 | 5,60947 | 5,64387 | 5,61711 | 5,61039 |
| 7000 | 5,10420 | 5,06364 | 5,04108 | 5,03090 |
| 8000 | 4,28356 | 4,20722 | 4,18653 | 4,17310 |
| 9000 | 3,90384 | 3,76886 | 3,73434 | 3,72016 |
| 9500 | 3,97683 | 3,88656 | 3,87360 | 3,86240 |
| 10000 | 3,85379 | 3,77687 | 3,77562 | 3,76781 |

Las 32 simulaciones alcanzaron periodicidad. A 9500 RPM el par ×2 difiere 0,63 % respecto de ×8. Para separar la sensibilidad temporal y de periodicidad del efecto espacial, la corrida ×2 con CFL 0,20, paso angular máximo de 0,1° y tolerancia periódica 10⁻⁶ dio 4,206518 N·m a 8000 y 3,886750 N·m a 9500. Las diferencias respecto del ×2 fueron 0,017 % y 0,005 %. En ×8 a 8000/9500 RPM se obtuvieron 3,496/3,842 kW, trabajo de cilindro de 45,810/45,785 J, trabajo de cárter de −3,776/−3,848 J y 46,147/48,367 mg atrapados. El residual de energía fue menor que 1,8×10⁻¹² J en ambos puntos.

El coste en la máquina Windows Intel i5-10400, 6 núcleos/12 hilos, fue aproximadamente 1,5–4 s para ×1, 6–14 s para ×2, 24–45 s para ×4 y 115–228 s para los primeros seis puntos ×8 con hasta tres trabajos concurrentes. Incluye balance, comprobación de admisibilidad y llegada a periodicidad. Las comparaciones de coste bajo cargas distintas son orientativas. No se cambió el límite térmico, ni la química, ni Cd para recuperar resultados anteriores.

En la serie 4T de duración en segundos como control adicional, los escapes de 0,25/0,40 m recuperaron todas las RPM en ×1 y ×2. El escape de 0,65 m a 6000 RPM en ×1 no alcanzó la tolerancia estricta del control en 60 ciclos y se registró como no periódico. En ×2 alcanzó 6,49644 N·m. La serie con duración de 40° de la contraauditoría se repitió aparte para conservar comparaciones bajo los mismos parámetros. Los CSV y registros conservan periodicidad, conservación, torque, potencia, presión, temperatura y ciclos. Los ensayos son numéricos.

| Escape auditado 4T (m), Wiebe 40° | 3000 RPM (N·m) | 4500 RPM (N·m) | 6000 RPM (N·m) |
| --- | ---: | ---: | ---: |
| 0,25 | 23,63663 | 15,70836 | 7,85995 |
| 0,40 | 23,70304 | 15,75769 | 8,03888 |
| 0,65 | 23,66497 | 16,10640 | 8,00254 |
| 0,85 | 23,13918 | 16,57327 | 7,36455 |

Las 12 configuraciones auditadas de la tabla fueron periódicas en ×1 y ×2, también al exigir tolerancia de 10⁻⁶ dividida por el factor de refinamiento. En la geometría de 0,85 m a 4500 RPM, el par de etapa 11 era 28,91 N·m y la VE 0,906. Con `ports-v1` fueron 16,57 N·m y 0,608 para la misma combustión de 40°. El cambio corresponde a la transferencia de masa y momento en las válvulas. Su tamaño requiere validación de descarga y medidas de presión, especialmente para usar resultados absolutos.

## Configuraciones y resultados almacenados

- Los proyectos creados en Windows y por generadores de CLI usan presión exterior. En JSON se conserva el significado de los valores 0 y 1. Si falta el campo, se carga como `UnspecifiedLegacy` y se muestra el aviso **LEGACY / NO PREDICTIVO**. La opción 2 en el editor identifica ese estado. La rama y los ejemplos históricos permiten repetir las ecuaciones anteriores. La formulación `ports-v1` tiene identidad numérica propia. Abrir un resultado anterior conserva sus datos y muestra la procedencia disponible. Se recomienda recalcular antes de usarlo con la revisión física presente.
- Los proyectos multicilíndricos 4T y los estudios 4T nuevos llevan un resumen SHA-256 de su contenido. Se leen los formatos anteriores para recuperar configuración. Los controles de consistencia se ejecutan también para esos formatos. Se verifican potencia, par, trabajo, cilindrada, periodicidad, temperatura y magnitudes de masa. Los resultados 2T incorporan comprobaciones adicionales de atrapamiento, barrido, combustible, especies y balances. Los estados y las curvas parciales de los estudios se conservan.
- Un hash sin clave detecta alteraciones accidentales cuando corresponde al sobre. El contenido físicamente consistente puede reconstruirse y volver a calcularse el hash. La identidad del autor y la autenticidad siguen fuera del alcance del formato. La procedencia identifica build y revisión del modelo y advierte si el build no coincide o no se puede reproducir desde el sello. Los estudios 4T registran la procedencia al ejecutarse.

Se repitieron los programas de integridad del auditor. Los archivos 4T legítimos se recuperaron y se rechazaron la potencia multiplicada por tres del multicilíndrico y por diez en los 24 puntos del estudio. En 2T se rechazaron, entre otros, la potencia aislada alterada, el combustible negativo, el atrapamiento de 5, los balances de 1 kg/10⁹ J y el pico de 10⁷ K. Un cambio de +5 % simultáneo en potencia, par y BMEP con hash recalculado aún pasa. También puede pasar un diagnóstico anexo de periodicidad discordante con el punto, el consumo específico separado o una firma de build inventada. El archivo de configuración 4T mono conserva el formato v1. Esta evidencia mantiene F-08 y NF-07 en alcance parcial.

## Registro de hallazgos

| ID | Estado | Alcance |
| --- | --- | --- |
| NF-01 | Corregido en el modelo compacto | B8, referencia de compliancia con tronco, sensores de presión y redes completas. Quedan pendientes pérdidas direccionales y resonancias internas de uniones no compactas. |
| NF-02 | Corregido en el dominio ensayado | Flujo de traza y pared, bloqueo, reversión y choque. La expansión local se homogeniza. |
| NF-03 | Corregido en nuevas configuraciones de usuario | Nuevos proyectos con presión. Modo antiguo explícito o aviso cuando faltaba el campo. |
| NF-04 | Mitigado | Advertencia legacy. Se conserva el modelo anterior para reproducibilidad científica. |
| NF-05 | Pendiente | La transferencia y el arrastre cuasiestacionarios subamortiguan ondas según la contraauditoría. |
| NF-06 | Corregido | Corrector 2T validado antes de aceptación, incluidas redes compartidas. |
| NF-07 | Parcial | Nuevos sobres 4T y controles físicos. La autenticidad y datos físicamente plausibles falsificados quedan indeterminados. |
| NF-08 | Restringido | La referencia de pseudoaire rechaza combustión con AFR <14,7 para gasolina nominal. Se permite motorizado sin liberación de calor. |
| NF-09 | Pendiente | Boca ideal sin corrección de extremo ni Cd de boca. |
| NF-10 | Mejorado | Pruebas nuevas para choque, flujo sónico, retorno, transmisión y balance de uniones. Las pruebas históricas conservan sus límites. |

| ID | Estado | Alcance |
| --- | --- | --- |
| F-01 | Corregido con alcance | Extremo físico ideal y unión compacta verificados. |
| F-02 | Parcial | Pared cuasiestacionaria, falta amortiguamiento acústico medido. |
| F-03 | Parcial | Recuperado el refinamiento alto 2T en casos ensayados. Sigue dependiendo de RPM y geometría. |
| F-04 | Corregido como opción | Prescripción de grados o segundos. |
| F-05 | Restringido | Mezcla rica requiere otro modelo químico. La referencia pseudoaire también la restringe. |
| F-06 | Corregido | Un fallo por régimen conserva puntos válidos. |
| F-07 | Pendiente | Láminas cuasiestáticas, geometría y barrido requieren medidas. |
| F-08 | Parcial | Integridad accidental y coherencia física. |
| F-09 | Parcial | Identidad temporal, build y modelo. El sello de árbol sucio no reconstruye el diff. |
| F-10 | Mejorado | Unión dinámica compacta con reflexión y transmisión. Sin topología espacial de la caja. |
| F-11 | Parcial | MUSCL conservado. La malla ×1 de ejemplos es exploratoria. |
| F-12 | Pendiente | Sin ampliación de especies y radicales. |
| F-13 | Pendiente | Sin perfil de leva ni Cd calibrado por alzada/relación de presiones. |
| F-14 | Mejorado | Validación del estado aceptado y conservación de curvas por régimen. |
| F-15 | Mejorado | Referencias externas y soluciones independientes. |
| F-16 | Mejorado | Interfaz reutilizada. Integradores conservados. |
| F-17 | Preservado | Ramas y puntos de recuperación locales. |
| F-18 | Mejorado | Aviso legacy en Desktop y procedencia de resultados recuperados. |
| F-19 | Mejorado | Nuevos ejemplos físicos y antiguos para regresión. Todos sintéticos. |
| F-20 | Preservado | Reproducción y artefactos de pruebas en directorios separados. |

## Reproducción

Con .NET 10, `dotnet run --project tests/Dino2.FourStroke.Tests -c Release -- --stage12 checks` ejecuta las pruebas independientes. Sustituí `checks` por `mesh`, `fine`, `temporal`, `four`, `four-strict` o `networks` para las campañas. `fine` ejecuta ocho casos de alta resolución y tarda varios minutos. `pwsh -File scripts/verify.ps1` reproduce regresiones. `pwsh -File scripts/publish.ps1` crea el portable autocontenido. Los registros del baseline usan el arnés original de la contraauditoría, con una ruta independiente para el volcado LOWT.

## Verificación de Desktop y entrega

El portable Windows x64 con .NET 10.0.15 fue compilado desde el árbol limpio `a4431b7`. Los cambios posteriores a esa compilación afectan la CLI, las pruebas y la documentación. Desde el ZIP extraído se comprobó la carga local de `coreclr.dll`, la referencia Otto, 11 RPM del 4T con conductos, estudios y A/B, guardado, reapertura y exportación. El 2T preservó puntos de una curva parcial y rechazó el archivo manipulado de la primera auditoría. En el 2T multicilíndrico se verificó el flujo de A/B en malla base, estudios, recuperación, exportación y cancelación. La campaña científica conserva por separado el refinamiento absoluto 2T. Pasaron la compilación Release, la regresión completa, las pruebas Desktop y la reproducción CLI de los cuatro ejemplos nuevos.

En el contraste 4T multicilíndrico del portable, aumentar el pleno de admisión de 2,4 a 3,6 L a 4000 RPM pasó de `Superior` en el recorrido histórico de etapa 11 a `Inferior` con `ports-v1`. La diferencia en la malla inicial fue −473,78 W y la banda empírica del contraste 20,13 W. El contraste tardó 175,3 s. El resultado informa sensibilidad numérica dentro del modelo. El cambio conjunto de boca, unión y acoplamiento impide atribuir esa inversión a una sola ecuación con este recorrido.
