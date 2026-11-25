# Dino2 · Etapa 15B — descarga, láminas y transfers

**Veredicto: CONDITIONAL / RESTRICTED.** Se desarrollaron un mapa Cd tabulado para el camino 4T 0D y un primer modelo de lámina modal acoplado al cálculo 2T. Falta la integración de mapas con `ports-v4`, la verificación de periodicidad y energía del modelo dinámico en motor completo, y el bloque de transfers independientes 1D. No se considera cerrada la etapa ni se recomienda iniciar el barrido multizona de 15C.

## Estado inicial y regresión de 15A

La rama de trabajo era `stage15/modelos-fisicos`, limpia en `af93787`; `d85b801` incorpora perfiles de alzada y `af93787` perfiles de ancho de lumbrera. Se creó la referencia local `stage15a/recovery-af93787`; ya existía `stage14c/recovery-87125f6`. La regresión de entrada se ejecutó con `scripts/verify.ps1` antes de editar producción. Compilación y suites numéricas, termoquímicas, de estudios, Desktop, multicilíndricas, CLI, campañas 2T y barrido 4T de 11 RPM completaron correctamente. El log preservado es `evidence/stage15b/stage15a-regression.log`. Esta regresión cubre la revisión 15A, no el código final 15B.

Una compilación posterior lanzada a la vez que Desktop intentaba copiar DLL bloqueadas; falló por concurrencia de procesos, no por errores de compilación. La compilación serial subsiguiente de los proyectos Core/Storage y suites específicas completó sin advertencias. Al finalizar los cambios se ejecutó `scripts/verify.ps1` sobre el árbol completo: compilación limpia, 13/13 pruebas generales, 24/24 grupos 4T, 15/15 termoquímica, 7/7 estudios, campañas 2T, redes multicilíndricas, Desktop y reproducciones CLI completaron correctamente. El registro es `evidence/stage15b/final-regression.log`. En ese momento el worktree estaba en `ui/mahapps-shell` (tras un cambio concurrente de rama desde `stage15/modelos-fisicos`) y contenía cambios Desktop ajenos a esta implementación; por eso la corrida no corresponde a un commit aislado de Etapa 15B.

## Investigación y definición de Cd

Un coeficiente de contracción `Cc` describe la relación geométrica de la vena contracta; no equivale al coeficiente de descarga basado en masa. El mapa implementado representa `Cd = m_dot_real / m_dot_ideal`, referido al área de cortina geométrica declarada y al flujo ideal isentrópico de gas perfecto calculado desde el remanso aguas arriba hacia la presión estática aguas abajo. La ecuación de flujo contiene el bloqueo sónico mediante el cociente crítico de presiones. El coeficiente no es una pérdida de presión total ni un coeficiente de recuperación estática independiente. Los coeficientes de banco dependen de área de referencia, ubicaciones de tomas y reducción de datos; no se convierten coeficientes de convención desconocida.

La definición sigue las reducciones publicadas de flujo de válvula y su advertencia sobre área de referencia [NACA TN 915](https://ntrs.nasa.gov/citations/19930090553), [estudio de normalización de banco](https://journals.sagepub.com/doi/10.1155/2013/482317). La dependencia con relación de presión bajo alta relación y flujo no estacionario está documentada experimentalmente en [On discharge from poppet valves](https://link.springer.com/article/10.1007/s00348-017-2478-8). Las mediciones estacionarias no identifican sin más las pérdidas pulsantes del motor.

El CSV exige metadatos `reference_area`, `pressure_convention`, `direction`, `units`, `source`, `compatible_model` y `data_kind`, seguido por `opening_fraction,pressure_ratio,cd`. La importación conserva SHA-256. La grilla es rectangular, sin huecos, con ejes crecientes. La interpolación bilineal reproduce nodos, es continua dentro de cada celda y no produce sobrepaso; rechaza explícitamente consultas fuera del dominio y valores negativos. No impone un techo universal de Cd. El fixture en `evidence/stage15b/cd-map-synthetic.csv` está marcado sintético.

La consulta se integró a la válvula del camino 4T 0D y al área de cortina, respetando el límite geométrico de garganta. El modelo histórico se mantiene si no hay mapa. **No está integrada aún en `ports-v4`, 4T 1D, lumbreras 2T ni lámina.** Aplicar Cd como una segunda reducción de área después de la expansión compacta duplicaría potencialmente pérdida y recuperación. La integración correcta requiere que la consulta dependa del estado de garganta/interfaz que finalmente acepta el solucionador y que el contrato de onda, energía y momento sea resuelto conjuntamente. Por eso esos usos no están habilitados.

## Modelo dinámico de lámina

Se añadió una reducción de primer modo de viga en voladizo, normalizada al desplazamiento de punta:

`m_eff x'' + c x' + k x = Δp A_eff`, con `k = 3 E I / L³`, `I = b t³/12`, `m_eff = 0.236 ρ b t L` y `c = 2 ζ sqrt(k m_eff)`. Las entradas separan longitud, ancho, espesor, densidad, módulo, amortiguamiento, límite de apertura y área de presión efectiva. La frecuencia se obtiene de `sqrt(k/m_eff)/(2π)`; el primer modo se contrastó con la raíz 1,875104 de Euler–Bernoulli con error menor al 1,5 % en el caso sintético. La deflexión estática se contrastó con `F L³/(3 E I)`.

La integración usa RK4 con pasos internos de hasta 1/80 del periodo modal en la referencia independiente. En el motor, el paso angular se restringe a 1/20 del periodo modal y el estado de la lámina participa del predictor/corrector y del criterio de periodicidad. Los contactos con asiento y tope se proyectan a la restricción y anulan la velocidad normal saliente; es un impacto perfectamente inelástico, sin rebote, que disipa energía. La apertura determina el área de cortina y se permite flujo inverso mientras siga abierta. La formulación de viga y modal es coherente con la teoría de vigas; la investigación numérica de válvulas de lámina identifica la importancia del contacto en [Jajcevic et al.](https://www.inderscience.com/info/inarticle.php?artid=59199).

El modelo de motor sintético a 5000 RPM no alcanzó el criterio de periodicidad después de 200 ciclos (residuo `3,422e-3`, dominado por energía en la celda 26 del conducto de escape; parámetros y salida en `evidence/stage15b/reed-motor-periodicity.txt`). Se detectó además que el primer acoplamiento al cierre histórico mantenía la válvula cerrada; se corrigió el cálculo de apertura instantánea. Aun así, la periodicidad no está conseguida. Tampoco se ha cerrado el balance de trabajo `Δp A_eff x'` entre gas, lámina y contacto, ni se validó la respuesta con presión sincronizada. La dinámica de lámina permanece optativa y no debe usarse para afirmar resultados de motor verificados.

## Transfers 1D

No se implementaron transfers individuales. El modelo sigue usando el intercambio 0D–0D existente entre cárter y cilindro, con una lumbrera agregada dependiente de altura. No hay almacenamiento de masa, especies o energía dentro de cada transfer, ondas, fase, reflexiones, conductos múltiples, volúmenes de unión ni asociación de ventanas individuales. Por tanto, no hay campaña de comparación 0D–0D/1D, ni evidencia de tránsito de onda o diferencias entre transfers. Esta ausencia es un objetivo central incompleto, no una limitación atribuible únicamente a falta de datos experimentales.

## Integración, persistencia y compatibilidad

El mapa es un objeto serializable en configuración y guarda puntos, metadatos y hash; se verificó serialización del objeto. La lámina dinámica se almacena como parámetro opcional de `Reed` y añade `reed_lift_m` a las trazas CSV. Los caminos históricos conservan valores por defecto. Desktop aún no ofrece importación/edición de mapas o parámetros de viga; tampoco se verificó la persistencia integral de mapas/modelo en proyectos, estudios A/B ni redes multicilíndricas.

`ports-v4` no se modificó ni reconstruyó. La regresión general del worktree pasó, pero no existe todavía un commit aislado de producción 15B. Falta probar la integración de las funciones nuevas con `ports-v4`, evaluación de rendimiento comparativa y portable final. No se generó un portable final, porque el código 15B no cumple aún los criterios científicos.

## Parámetros y calibración

| Parámetro | Unidad | Medición requerida | Observable | Estado |
|---|---|---|---|---|
| Área de referencia y convención Cd | m² / definición | Geometría y protocolo del banco | caudal másico, presión total/estática | Entrada obligatoria; mapas de prueba sintéticos |
| Cd(apertura, relación, sentido) | adimensional | Banco bidireccional con tomas documentadas | caudal másico y relación de presión | No calibrado; sólo 4T 0D |
| Longitud, ancho, espesor de lámina | m | Metrología de pétalo instalado | geometría/modal | No calibrado |
| Densidad y módulo elástico | kg/m³, Pa | ficha/material y ensayo de muestra | masa y flexión | No asignar a fabricante sin fuente |
| ζ / amortiguamiento | — | respuesta libre o armónica | frecuencia, decremento, fase | sintético |
| Área efectiva de presión | m² | geometría/campo de presión | fuerza y alzada | reducida, no validada |
| Transfer: longitud y área por estación | m, m² | medición geométrica | fase y amplitud de onda | no implementado |

## Respuestas de cierre

1. Cd es un cociente de caudal másico real respecto de flujo ideal isentrópico, con área de cortina y remanso explícitos.
2. No se aplica el mapa sobre `ports-v4`; la integración debe resolver Cd y garganta conjuntamente para evitar contar dos veces la expansión compacta.
3. Interpolación bilineal acotada; fuera del rectángulo tabulado se lanza error, sin extrapolación.
4. Primer modo de viga en voladizo, reducido a masa, rigidez y amortiguamiento.
5. `k` y `m_eff` se derivan de dimensiones y material bajo hipótesis de viga uniforme; `ζ`, área de presión y límite requieren medida/ensayo.
6. Rigidez y deflexión se contrastan con Euler–Bernoulli; frecuencia con la raíz modal 1,875104; el amortiguamiento sólo se ensayó como respuesta numérica del oscilador, sin referencia de amortiguamiento experimental.
7. Contacto proyectado sin penetración y perfectamente inelástico, sin rebote. La energía perdida no se está contabilizando aún en el balance de gas.
8. Presión del extremo de admisión impulsa el estado estructural; la alzada limita el área y el flujo inverso permanece posible mientras está abierta.
9. Aún no se garantiza para el sistema acoplado: falta contabilizar el trabajo presión–estructura en los inventarios de energía.
10. No se integraron conductos de transfer 1D.
11. No; sólo existe el transfer agregado histórico.
12. No hay volúmenes nuevos de transfer, así que no se duplicaron volúmenes; la arquitectura con almacenamiento 1D está pendiente.
13. El modelo existente transporta mezcla y marcador de frescura por el flujo de transfer agregado; no hay transporte distribuido dentro de un conducto de transfer.
14. El cierre 0D–0D no resuelve inercia de columna, almacenamiento distribuido, tiempo de tránsito, ondas, reflexiones ni diferencias entre conductos.
15. El mapa cambia el caudal 4T 0D sintético según la tabla. No se presenta cambio de fase/onda validado. La lámina dinámica no alcanzó periodicidad, así que no se reportan resultados aceptados de motor.
16. No hay comparación de motor completo aceptada con la lámina; los transfers no cambiaron.
17. El código de `ports-v4` permanece intacto; la regresión de entrada 15A pasó. Falta regresión final tras los cambios actuales.
18. El mapa usa grilla y búsqueda binaria/interpolación constante por celda. La dinámica de lámina requiere pasos de hasta 1/20 de periodo modal en el motor; falta medir costo final.
19. Banco de flujo en ambos sentidos y relaciones/alzadas documentadas; geometría y material de lámina; presión sincronizada, alzada, frecuencia y amortiguamiento; para transfers, geometría por estación, presiones/caudal por fase y composición.
20. No. La Etapa 15B sigue condicional: faltan transfers 1D, integración `ports-v4` del mapa y cierre/validación energética y periódica de la lámina.

## Condiciones para continuar

Completar la integración de mapas en las interfaces `ports-v4` mediante resolución conjunta consistente; verificar importación y persistencia completa. Resolver el balance de energía y contacto de lámina, obtener periodicidad bajo refinamiento temporal y ejecutar campañas 2T controladas. Implementar conductos de transfer múltiples con geometría, inventario y transporte conservativo reutilizando `DuctMesh`. Sólo después de esas verificaciones y la regresión general del commit final corresponde evaluar preparación para 15C.
