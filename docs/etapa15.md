# Dino2 · Etapa 15 — modelos físicos de alta fidelidad

**Estado de entrega de esta iteración: CONDITIONAL / RESTRICTED.** Se incorporaron dos capacidades optativas con entradas geométricas trazables: perfiles medidos de alzada 4T y perfiles de ancho agregado de lumbrera 2T frente a altura. La etapa completa no está cerrada: los modelos dinámicos de láminas, transfers 1D, dinámica de barrido, mapas de descarga y varios cierres acústicos/térmicos siguen diferidos. No hay validación experimental integral ni preparación suficiente para declarar abierta la calibración de etapa 16.

## 1. Resumen ejecutivo — implementado parcialmente

El cambio permite importar una tabla de ángulo de cigüeñal y alzada para admisión o escape. El perfil reemplaza la ley seno-cuadrado histórica, se interpola linealmente, respeta el máximo configurado y alimenta tanto el área de cortina 0D como el acoplamiento de conductos `ports-v4`. El proyecto almacena los puntos, el nombre del archivo fuente y su SHA-256. La ley histórica se mantiene cuando no existe perfil.

La base de etapa 14C se preservó en `stage14c/recovery-87125f6`; el trabajo se realiza en `stage15/modelos-fisicos`. El portable 14C cotejado tiene SHA-256 `599a82add0e94e56a647dd44b739e8d44661fa49035ced40a8cd424a35535813`, igual al manifiesto entregado. El commit del binario es `4deeb3075c43a2f95aa3e2361473974754d389c5`; la comparación de fuentes indica que los cambios posteriores hasta `87125f6` no alteran producción.

## 2. Estado de referencia — verificado

- Git estaba limpio en `87125f6efd4109f768c41c3dd35d86226c9ba51d` antes de crear las referencias locales.
- `docs/etapa14c.md` confirma dominio de interfaz 200–6000 K, contrato de aceptación antes de publicar flujo y límites físicos de las campañas. La configuración de comparación sigue disponible mediante `RecoveryExample`/proyectos existentes.
- La base 14C declara 112 casos de descarga, controles termoquímicos, campañas 2T, 4T 11/11 RPM, redes de 2–4 cilindros, B8 y estudios A/B.
- El ZIP de evidencia coincide con `Dino2-stage14c-verificacion.txt`: 292 entradas. Hash SHA-256: `16d9ddf2ee5da3a3c6d18e379b695ec7505ee3f3ab5e8ed0a185b2ed4ad2a570`.
- La regresión posterior completó `scripts/verify.ps1`: build sin errores/advertencias, controles de interfaz `ports-v4`, 4T 11/11, Desktop, 2T y redes multicilíndricas, Otto y CLI. Se repitieron las semillas reservadas `104729` y `196613`. El portable se extrajo y arrancó; `coreclr.dll` se cargó desde el directorio extraído.
- Después de agregar igualdad estructural de perfiles y persistencia de resultados con contorno, se reconstruyó toda la solución y se repitieron las suites 2T (incluida reapertura íntegra del resultado perfilado) y 4T 23/23. Sus salidas están en `evidence/stage15/final-targeted.log`.

## 3. Investigación bibliográfica — investigada

La literatura respalda que un perfil de alzada medido y un mapa de descarga son datos distintos: el primero fija cinemática/área de cortina, mientras Cd representa contracción y pérdidas de flujo del montaje. Danov describe Cd instantáneo dependiente de geometría, alzada y relación de presiones a partir de presión–ángulo y modelo de intercambio ([SAE 970642](https://doi.org/10.4271/970642)). Un estudio de banco muestra dependencia de los coeficientes con las condiciones de presión ([SAE 2017-01-5022](https://doi.org/10.4271/2017-01-5022)). Por eso esta iteración no infiere ni modifica Cd al importar una leva.

Para láminas, la literatura documenta pétalos como vigas acopladas al flujo y que el régimen de giro afecta apertura y cierre; una validación requiere datos dinámicos y motor instrumentado ([Jajcevic et al., SAE 2010-32-0015](https://doi.org/10.4271/2010-32-0015)). El modelo actual carece de masa, amortiguamiento, contacto dinámico y trazas de alzada, de modo que una constante de tiempo supuesta no sería identificable.

Los límites de barrido de mezcla perfecta y desplazamiento perfecto, y la combinación de Benson/Brandham, son referencias analíticas útiles, pero no describen universalmente el loop scavenging. Mediciones publicadas de composición atrapada han encontrado proximidad al límite de mezcla perfecta de dos zonas en un motor concreto ([Foudray y Ghandhi, SAE 2003-32-0081](https://doi.org/10.4271/2003-32-0081)); eso apoya medir especies y temperatura antes de ajustar cierres a otro motor.

## 4. Análisis de sensibilidad inicial — parcialmente investigado

La evidencia local anterior muestra que entradas geométricas y cierres no tienen el mismo peso:

| Evidencia numérica preservada | Efecto observado | Lectura y límite |
|---|---:|---|
| 4T pseudoaire, 3000 RPM: alzada máxima de 4 mm frente al ejemplo | Potencia 9,752 a 8,067 kW (−17,3 %) | Alta sensibilidad del llenado; no valida magnitud física frente a banco |
| 4T ejemplo, 3000 a 6000 RPM | VE 0,890 a 0,732 | El modelo de válvula/contorno domina la respuesta alta; la VE sigue dependiendo de parámetros asumidos |
| 2T, etapa 11: factor de extracción fresca 0,75/1/1,25 a 5000 RPM | 8,916/8,717/8,546 N·m | El cierre de barrido cambia torque y masa atrapada; se necesita composición medida |
| 2T, 5000 RPM: rectangular base frente a contorno triangular sintético de transfer | Potencia al eje 2067,521 a 2268,357 W | Cambiar ancho con altura cambia área-tiempo y resultado completo; sensibilidad sintética, no comparación con ensayo |
| 2T, cámara de expansión escalada ×0,8/×1,25 | Cambia preferencia geométrica entre 4000 y 6000 RPM | Longitud/área alteran fase y resonancia, con sensibilidad de malla documentada |
| Etapa 14C, `ports-v4` frente a interfaz previa | Hasta 105,6 kPa en traza local a 9500 RPM; −3,014 W | Evidencia de importancia de la aceptación de interfaz cerca del cierre; no es mejora experimental |

En consecuencia, las prioridades con mejor razón impacto/evidencia son: perfiles reales y mapas de descarga medidos; geometría de lumbrera y eventos; mediciones de barrido; dinámica de lámina; después transfer 1D y correcciones de extremos/uniones. El orden también refleja identificabilidad: el perfil se mide directamente con rueda/levantamiento, Cd con banco de flujo, mientras que mezcla por zonas y radiación efectiva son más correlacionadas con pérdidas y temperatura.

## 5. Matriz de prioridades — investigada

Escala cualitativa: alto/medio/bajo; «riesgo» es riesgo de introducir una solución no robusta.

| Fenómeno | Impacto esperado | Evidencia/identificabilidad | Coste | Riesgo | Prioridad/estado |
|---|---|---|---|---|---|
| Perfiles de leva medidos | Alto en llenado, cruce y bombeo | Alta si hay grados/alzada | Bajo | Bajo | P1, parcial implementado |
| Mapas Cd(alzada, presión, dirección) | Alto en caudal y trabajo de bombeo | Alta con banco correctamente reducido | Bajo–medio | Medio | P1, diferido |
| Geometría real de lumbreras 2T | Alto en área-tiempo y chorro | Alta con medición dimensional | Bajo | Bajo–medio | P1, perfil de ancho agregado parcial implementado |
| Lámina dinámica | Alto cerca de resonancia/altas RPM | Media; requiere alzada/presión sincronizadas | Medio | Alto por contacto y rigidez acoplada | P2, diferido |
| Transfers 1D | Alto en retraso, inercia y fase | Media; requiere geometría y presión | Alto | Alto por acoplamiento de fronteras | P2, diferido |
| Barrido multizona y cortocircuito | Alto en masa fresca atrapada/BSFC | Baja sin trazador o composición | Bajo–medio | Medio | P2, no calibrar aún |
| Garganta/inertancia y radiación | Medio–alto en fase/reflexión | Media con geometría y pulsos | Bajo–medio | Medio | P3, diferido |
| Pérdida pulsante, calor y colectores | Medio–alto según montaje | Media/baja sin temperatura y presión de pared | Medio | Medio | P3, diferido |
| Combustión y fricción | Alto en torque al eje | Medible, pero identificable sólo con presión/par/fuel | Medio | Medio | P1 de etapa 16, fuera de este bloque |

## 6. Modelo incorporado — implementado

`PoppetValve.LiftProfile` es una secuencia opcional de puntos `(CrankAngleDeg, LiftM)` para el ciclo completo 0–720° de cigüeñal, alineado al PMS de combustión. Se exige periodicidad en extremos, ángulos estrictamente crecientes, alzada no negativa y dentro de la alzada máxima, eventos de apertura/cierre con alzada cero, y procedencia más SHA-256. `ValveFlow.Lift` usa interpolación lineal por tramos y periodo 720°. El área efectiva sigue siendo `Cd × número de válvulas × min(π D L, π D²/4)`, por lo que no supera el área de garganta.

Un CSV importable usa encabezado `crank_angle_deg,lift_mm`; separador coma o punto y coma, decimal con punto, e incluye 0° y 720°. La interpolación lineal es monotónica por segmento y no genera sobrepaso. No se convierten grados de árbol de levas en silencio: hay que exportar/medir la curva en grados de cigüeñal. No se altera la ley cuando el perfil está ausente.

Para 2T, `PistonPort.WidthProfile` permite importar pares `(altura sobre el techo, ancho agregado)` en mm mediante `height_mm,width_mm`. El perfil cubre desde cero hasta la altura configurada. El área geométrica abierta es la integral trapezoidal exacta de la interpolación lineal del ancho respecto a la altura expuesta por el pistón; el área efectiva sigue siendo `Cd × min(garganta, área geométrica)`. El área rectangular histórica se conserva cuando no hay tabla. Un ancho agregado puede representar la suma neta de ventanas/puentes a cada altura, pero no separa jets, coeficientes o eventos de transfers individuales.

## 7. Arquitectura e integración — parcial

La función central de alzada abastece 4T 0D, 4T con conductos 1D, vista de ciclo, multicilíndrico basado en la configuración y estudios. La serialización estándar del proyecto conserva puntos y metadatos. Desktop ofrece botones separados de importación para admisión y escape; tooltip y estado declaran unidades, alineación, interpolación y limitación de procedencia. En 2T, el área integrada se usa en los flujos reversibles de transfer y en el cierre de lumbrera de escape, manteniendo el mismo `Cd` y `ports-v4`. La ventana 2T importa contornos separados para transfer y escape, y muestra fuente/hash y el alcance agregado.

No se agregó editor de curvas, ajuste de curva, mapa de descarga, perfil de tren de válvulas, ni exportación de una vista gráfica. La alzada es cinemática prescrita, sin masa de válvula, resorte o flotación.

## 8. Verificaciones independientes — implementadas para perfiles

Los controles nuevos comprueban interpolación en punto medio conocido, periodicidad angular positiva/negativa, cierre en 720°, área geométrica dentro de garganta, rechazo de alzada superior al máximo, importación CSV y SHA-256, y cambio no nulo de la simulación con una traza sintética. Para lumbreras verifican rectángulo equivalente, triángulo de área analítica conocida, integración parcial, cierre, CSV, hash y persistencia con integridad SHA-256. Una comparación 2T sintética comprueba impacto de área-tiempo en ciclo completo. Estas comprobaciones de implementación no son experimentos.

Los demás criterios de Etapa 15 permanecen pendientes: referencia de viga para lámina, transferencia de onda en ducto de transfer, comparación con contornos XY reales, límites de barrido integrados y matrices de sensibilidad refinadas por cada nuevo subsistema.

## 9. Resultados antes/después y rendimiento — verificación parcial

En el punto 4T pseudoaire sintético de 3000 RPM, una forma de leva sintética pasó de 36,498208 a 36,368984 N·m indicados (−0,129224 N·m, −0,354 %). En 2T a 5000 RPM, el contorno triangular sintético de transfer pasó de 2067,521 a 2268,357 W al eje. Ambos cambios sólo demuestran sensibilidad del área-tiempo modelada; no miden exactitud ni ganancia real. La regresión completa posterior pasó tras ambos cambios y la geometría histórica sigue predeterminada. La integración del perfil de ancho recorre sus segmentos abiertos en cada evaluación; su coste debe medirse con perfiles de alta resolución. No se afirma mejora experimental de torque, potencia o VE.

## 10. Preguntas 2T, acústica, multi y etapa 16 — diferido

- Admisión 2T: continúa la lámina cuasiestática con `ports-v4`; esta iteración no cambia área, fuerza ni dinámica de pétalo.
- Transfers: siguen como garganta 0D–0D reversible; no capturan columna gaseosa ni fase local.
- Barrido: permanecen inventarios y extracción por un factor de cierre. Delivery ratio, atrapamiento, barrido y carga se distinguen en resultados, pero falta identificar el cierre por trazador/composición.
- Lumbreras: se admite ancho agregado variable con altura para escape y transfer; la apertura se acopla a la cinemática biela–manivela. No hay contorno 2D/3D, masking con coordenadas, tratamiento separado de puentes ni transfers múltiples con presiones independientes.
- Acústica: Euler 1D representa ondas distribuidas existentes; inertancia compacta, radiación del extremo, pérdida pulsante y diferencia de fase validada experimentalmente siguen ausentes.
- Multicilíndrico: se preservan cierres compartidos y mezcladores existentes; no se cambia la arquitectura de colector.
- Combustión/fricción: leyes prescritas y correlaciones siguen requiriendo medición de cilindro y par al freno.

## 11. Plan realista de calibración — investigado, no iniciado

Antes de etapa 16 se requieren: (1) perfil de válvulas con PMS/PMI y referencia de ángulo; (2) diámetro, cortina y banco de flujo con ambos sentidos, alzada y presión para Cd; (3) presión de cilindro y conductos sincronizada; (4) torque, RPM, combustible y condiciones ambiente; (5) temperatura de gas/pared; (6) para 2T, levantamiento de lámina o presión diferencial de alta frecuencia, geometría de transfer/lumbrera, fracción residual y un método de trazador. Separar conjuntos de identificación y reserva; estimar parámetros con intervalos y correlaciones; no ajustar Cd, barrido, calor y fricción simultáneamente contra torque solamente.

## 12. Limitaciones y modelos diferidos — explícitos

No hay base experimental Dino2 aportada para perfiles, mapas Cd o curvas de motores de referencia. Se difieren dinámica de lámina, transfers 1D, contornos XY/masking y transfers independientes, modelos de barrido por zonas, mapas de descarga, inertancia, radiación, pérdida pulsante, pared avanzada, colectores no uniformes y calibración física de combustión/fricción. Los diferidos son esenciales para la meta completa de Etapa 15; por ello el estado permanece condicional y no `PHYSICS ENHANCED / READY FOR CALIBRATION`.

La comparación de formulaciones con simuladores comerciales/reconocidos no se ejecutó. No se dispone aquí de un caso de motor con geometría, condiciones límite, mapas y datos comunes que permita una comparación reproducible; una comparación sólo de curvas finales confundiría diferencias de calibración con diferencias de formulación. El inventario bibliográfico sí identifica formulaciones y mediciones que deben formar un caso de comparación futuro.

## 13. Respuestas a las preguntas de cierre

1. Las simplificaciones más limitantes identificadas son Cd constante, perfiles de válvula/área simplificados, admisión 2T cuasiestática, transfers 0D–0D y barrido con cierre empírico; el peso varía según el resultado.
2. Se incorporó el uso de perfiles de alzada reales 4T, porque son medibles directamente, afectan área-tiempo y encajan con interfaces existentes de bajo riesgo.
3. La ecuación de área de cortina y referencias de válvula/descarga respaldan la separación geométrica y Cd; la referencia inmediata de prueba es además interpolación analítica independiente. No existe aún contraste experimental de esta implementación.
4. La dinámica de admisión 2T no cambió. El área de transfer/escape ahora puede variar con altura mediante una curva agregada; no describe dirección de chorros ni varias ventanas independientes.
5. No se incorporó dinámica de lámina; la literatura la justifica, pero faltan parámetros/mediciones para identificar un modelo y validar contacto/energía.
6. No hay transfers 1D nuevos; el beneficio esperado es capturar retardo, inercia y onda, y queda pendiente.
7. No se seleccionó un nuevo modelo universal de barrido. Mezcla perfecta y desplazamiento son límites; el cierre actual se conserva etiquetado como incierto.
8. Se pueden representar perfiles rectangulares y aproximar contornos trapezoidales/ovalados con ancho agregado por altura. No se importan contornos XY, masking, puente separado ni curvas área–ángulo directas.
9. Se importan pares grados de cigüeñal/alzada y se usa interpolación lineal periódica; la ley se acopla a áreas 0D/1D.
10. No hay mapas Cd nuevos. Cd actual continúa aplicado al área efectiva, sin duplicar pérdidas añadidas.
11. No se añadieron fenómenos acústicos. Permanecen ondas Euler 1D y límites de boca existentes.
12. No hay mejora validada de atenuación o fase.
13. En ciclos sintéticos completos, el perfil cambió torque 4T −0,129224 N·m a 3000 RPM y potencia 2T +200,836 W a 5000 RPM. Son sensibilidades de implementación; faltan motores y curvas medidas para evaluar predicción.
14. Se puede estudiar sensibilidad a forma de alzada 4T y ancho agregado de lumbrera 2T, respetando eventos, garganta y `Cd` actual. Otras modificaciones quedan limitadas por modelos actuales.
15. Persiste incertidumbre experimental en perfiles instalados, Cd, transferencia térmica, combustión, fricción, barrido, ondas/terminaciones y parámetros de lámina.
16. El campo de perfil nulo conserva el camino histórico; regresiones completas deben confirmarlo. El contrato `ports-v4` no fue modificado.
17. Coste: almacenamiento O(n) por perfil y búsqueda O(log n) por evaluación; falta medición comparativa con perfiles reales largos.
18. Quedan diferidos los modelos enumerados en la sección 12.
19. Faltan mediciones de alzada, Cd bidireccional, presión angular en cilindro/conductos, torque/fuel y datos 2T de lámina, barrido y transfer.
20. No; Etapa 16 debe esperar a completar al menos la campaña física adicional y un inventario de datos identificables. El estado es `CONDITIONAL / RESTRICTED`.
