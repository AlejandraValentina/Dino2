# Dirección del producto y progresión científica

Este documento registra la secuencia acordada antes del desarrollo 4T. Ya existe una capacidad 0D con comparación A/B manual; su alcance entregado y los próximos pasos científicos figuran en [informe-4t.md](informe-4t.md). Los subsistemas avanzados de química y dinámica 1D siguen pendientes.

La finalidad de Dino2 es configurar motores reales 4T y 2T y predecir cómo cambian potencia y par con las RPM y con modificaciones concretas. Se conservará una configuración original para comparar variantes manuales y, más adelante, estudios automáticos de parámetros. El usuario debe acceder a una interfaz sencilla sin perder acceso a hipótesis, procedencia de datos, parámetros calibrados y límites de validez.

Este documento orienta decisiones; los subsistemas futuros descritos aquí **no están implementados** y no se considera completada una capacidad por crear sus carpetas o interfaces.

## 1. Cerrar el flujo inicial

Se entrega un ciclo Otto ideal como referencia analítica, edición de entradas, barrido RPM, gráficas de potencia/par indicados, estados y balance, archivos reabribles, CSV trazable, ejecutable Windows y pruebas. El modelo se identifica explícitamente; no se denomina simulación predictiva de un motor real.

## 2. Siguiente incremento: un 4T con respuesta física a RPM

El siguiente desarrollo debe entregar un modelo de cilindro 0D resuelto por ángulo de cigüeñal, con balances de masa y energía, cinemática biela-manivela y estado termodinámico coherente. La selección concreta de ecuaciones, correlaciones y método numérico se fundamentará en investigación antes de implementarla. No basta con añadir un factor dependiente de RPM a la potencia del Otto.

Para poder evaluar modificaciones de avance y distribución se necesitan combustión de duración finita e intercambio de gases a través de válvulas con áreas y tiempos definidos. Una primera frontera de complejidad razonable es flujo cuasiestacionario entre cilindro y depósitos de admisión/escape con condiciones de contorno explícitas, incluyendo bloqueo sónico si corresponde. No se prometerán resonancias ni sensibilidad válida a longitud de escape mientras no exista un modelo de ondas.

La evolución de energía debe relacionar tiempo y ángulo mediante ω, integrar trabajo y cuantificar calor a paredes según un modelo documentado. Los parámetros de combustión y coeficientes de descarga no son mediciones por defecto: cada supuesto y cada calibración se identificará. Un modelo de combustión prescrito puede servir como etapa verificable, siempre que se indique qué efectos no predice (por ejemplo, cambios de duración al variar turbulencia).

Para obtener potencia al cigüeñal se necesitará un modelo separado y documentado de fricción y accesorios. El bombeo debe contabilizarse de forma consistente dentro del ciclo de 720°; no se restará dos veces. Hasta incorporar pérdidas fundamentadas, se mostrarán únicamente resultados indicados.

**Aceptación propuesta:** cerrar periódicamente masa/energía del ciclo, comprobar convergencia en ciclos y refinamiento de paso angular, recuperar casos analíticos apropiados, obtener curvas RPM sin factores arbitrarios y responder al menos al avance de encendido y a un cambio de distribución/alzada mediante sus ecuaciones. Comparar con un conjunto experimental de geometría y condiciones conocidas antes de declarar precisión predictiva, separando datos de calibración y validación. Si faltan datos, se indicará que solo existe verificación numérica y evaluación de tendencias del modelo.

La capacidad se desarrollará por tramos verificables hasta cumplir esa aceptación; no se exige implementar todos los efectos en una sola modificación de código.

## 3. Comparación manual A/B

Al disponer de curvas físicamente sensibles a RPM se entregará la comparación manual. El original será un snapshot conservado; editar una variante no modificará el original. Cada resultado quedará asociado a una configuración y versión de modelo, condiciones de contorno y parámetros de calibración.

La comparación mostrará ambas curvas y diferencias absolutas de potencia/par por RPM, indicando magnitud y signo. Las diferencias porcentuales necesitarán tratar explícitamente referencias cero. Se comparará una malla común o se identificará cualquier interpolación; solo habrá comparación en el rango común. Se distinguirán resultados indicados y al cigüeñal y se comprobará que condiciones/modelos sean comparables.

Ahora no existe una pantalla A/B. Se puede conservar un JSON/CSV manualmente, pero eso no se presenta como la función de comparación terminada.

## 4. Estudios automáticos

Reutilizarán la misma operación de simulación y los snapshots de A/B para evaluar un parámetro y un rango. Cada estudio conservará configuraciones, resultados, fallos y objetivo elegido. El resultado óptimo de un modelo no equivaldrá automáticamente al mejor motor real. No se implementan todavía optimizador, gestor general de ejecución ni paralelismo.

## 5. Modelo 2T y dinámica 1D

El 2T necesitará geometría y temporización de lumbreras, barrido, cortocircuito de carga fresca, admisión/cárter cuando corresponda y condiciones específicas de escape. No será un 4T con el divisor RPM cambiado a 60. Se incorporará cuando el 4T y los contratos comunes estén probados.

La dinámica 1D de admisión/escape resolverá conservación de masa, cantidad de movimiento y energía, con condiciones de unión y acoplamiento al cilindro. Exigirá estabilidad numérica, convergencia espacial/temporal, conservación entre dominios y pruebas de ondas antes de usarla para evaluar geometrías de escape. La selección del esquema y la implementación se posponen hasta contar con el problema físico y sus casos de referencia.

## Límites de arquitectura que se preservan desde hoy

- El modelo Otto y su configuración tienen nombre y versión propios. No se obliga a futuros modelos a aceptar qin, gamma constante o estados iniciales cerrados. Sus archivos antiguos seguirán significando lo mismo.
- La física no depende de WPF, JSON ni de objetos de la ventana. Los futuros componentes de cilindro, gas, combustión, intercambio térmico y fricción se introducirán cuando haya una ecuación y un uso concreto que separarlos; no como plugins vacíos.
- El solver numérico administrará integración, convergencia y errores numéricos; las ecuaciones físicas definirán balances y propiedades. La UI recibirá resultados y diagnósticos sin convertirse en solver.
- Las entradas y resultados actuales son snapshots inmutables y utilizan SI. Esto permite conservar originales y variantes y ejecutar fuera del hilo de UI. La unidad de ejecución futura deberá ser una configuración explícita a un régimen y condiciones determinadas; no un conjunto de controles gráficos ni una curva predefinida.
- Los resultados deben representar separadamente potencia indicada, bombeo cuando corresponda y potencia al cigüeñal, con magnitudes no disponibles marcadas como tales. No se rellenarán salidas ausentes con ceros que parezcan mediciones.
- Las constantes asumidas, correlaciones elegidas y parámetros calibrados se documentarán con su procedencia y rango. Una convergencia numérica no será una validación física; una validación en un motor/rango no se generalizará sin evidencia.

No se añaden hoy interfaces universales de motores, bases de datos, motores de flujos de trabajo ni componentes 1D sin comportamiento. La separación actual permite incorporarlos de forma incremental sin convertir el Otto en un contrato científico obligatorio.
