# Arquitectura del simulador 4T

Actualización de etapa 2: `Thermochemistry` contiene especies NASA, conversión conservativa y el controlador de productos congelados; `EngineSimulation` selecciona explícitamente entre esa formulación y la referencia de pseudoaire. Los tipos de configuración/resultados comparten geometría y análisis, conservando el identificador de cada modelo. La UI conserva la configuración completa y su procedencia al guardar A/B. Ver [modelo-termoquimico.md](modelo-termoquimico.md) e [informe-etapa2.md](informe-etapa2.md). El resto de este documento describe la base introducida en etapa 1.

Se conserva C#, WPF y .NET 10. La [selección tecnológica de etapa 0](arquitectura.md) sigue siendo aplicable. La necesidad concreta de balances diferenciales, convergencia periódica, cancelación y comparación A/B justifica las nuevas piezas; no se introduce un framework de motores ni una plataforma de ejecución.

## Separación del cálculo

```text
WPF / CLI
    ├── Storage: proyecto JSON + CSV + trazas + comparación
    └── Core
         ├── OttoCycle: referencia analítica independiente
         ├── FourStroke/Physics: cinemática, válvulas, calor y fricción
         ├── FourStroke/AirProperties: propiedades termodinámicas
         ├── FourStroke/CylinderEquations: balances instantáneos
         ├── Numerics/RungeKutta: integración numérica independiente
         └── FourStroke/FourStrokeSimulator: control de pasos, eventos y periodicidad
```

Las ecuaciones no acceden a WPF, archivos ni controles de ejecución. El integrador recibe una función y buffers; no conoce válvulas, combustible ni UI. El controlador del caso conoce eventos físicos y tolerancias, gestiona el estado de ciclo, mide el tiempo y devuelve solamente soluciones convergidas. No se acopla el solver a una colección observable ni a componentes gráficos.

El estado m/U/mf se acompaña de cuadraturas para hacer explícitos trabajo, bombeo, calor y flujos. Los eventos de carga y combustión se ejecutan en fronteras de integración, sin condicionales dependientes de que una malla uniforme acierte el ángulo. El marcador de carga fresca se explica en el [modelo](modelo-4t.md); no equivale a un solver de especies químicas.

La física de pseudoaire y las hipótesis de este modelo permanecen dentro de `FourStroke`, con su propio identificador. Un modelo futuro con productos, varias zonas o tubos 1D no tendrá que aceptar sus hipótesis como un contrato universal. Se pueden conservar cinemática, unidades, integrador y formatos de análisis allí donde sigan siendo válidos. Antes de ampliar el estado o el integrador se exigirán pruebas propias del nuevo problema.

## Configuración y resultados

Records inmutables agrupan geometría, contornos, válvulas, combustión, paredes, fricción, RPM y solver. El proyecto guarda una configuración actual y otra original opcional. Ninguna de ellas contiene referencias a controles gráficos. Los archivos de etapa 0 no se migran ni reinterpretan.

Los resultados incluyen la entrada exacta utilizada y colecciones de solo lectura. `CycleSample` es un valor inmutable para evitar una asignación de objeto por muestra. La comparación usa únicamente RPM comunes exactas, informa B−A y no interpola. Las eficiencias sin energía de combustible son indefinidas; CSV las exporta como `NA`. La potencia negativa se conserva.

Las métricas numéricas y los tiempos acompañan cada régimen. No se guardan curvas opacas dentro del proyecto: al abrir se reconstruyen desde las entradas. La memoria de la UI puede conservar A durante la edición de B; guardar conserva ambas configuraciones para reconstruirlas después. No hay persistencia de trabajos parciales ni recuperación de una simulación en curso.

## Interfaz y concurrencia

`FourStrokeWindow` agrupa parámetros por dominio y convierte las unidades de presentación a SI. Una tabla pequeña de accesores tipados hace que cada campo modifique explícitamente su record; no hay evaluación de expresiones, reflexión del modelo ni campos de física sin tipo. `ScientificPlot` recibe puntos calculados y soporta series A/B, valores negativos y trazas p–θ/p–V. El gráfico Otto anterior permanece disponible con su ventana.

Al simular se captura una configuración inmutable, se deshabilita su edición y se ejecuta el cálculo mediante `Task.Run`. La cancelación se comprueba en cada paso; el progreso llega por `IProgress`. Se publican curvas completas después de converger todos los regímenes. Un error o cancelación conserva A y no publica B parcial. La ventana permite cancelar antes de cerrarse; no se abandona un cálculo mutando controles destruidos.

Los coeficientes empíricos se muestran como entradas. La interfaz y los CSV declaran que el modelo no está calibrado ni validado experimentalmente; cargar un motor con dimensiones medidas no cambia por sí solo ese estado.

## Coste y evolución necesaria

Un régimen integra sucesivos ciclos hasta periodicidad. El coste es proporcional a regímenes × ciclos × evaluaciones por ciclo. RK4 con control por duplicación usa normalmente 12 evaluaciones por paso intentado. Los buffers se reservan por régimen, el delegado se conserva durante la integración y las trazas de ciclos descartados reutilizan su lista.

La física se ejecuta secuencialmente: las mediciones del caso de 11 RPM no justifican añadir planificación o paralelismo. Se mantienen los diagnósticos y pruebas para decidir mejoras a partir de evidencia. La próxima complejidad científica necesaria es mejorar propiedades/composición de productos y contrastar datos experimentales antes de prometer precisión de modificaciones. La dinámica 1D requerirá otro esquema de estabilidad/acoplamiento; no debe insertarse dentro de eventos de la ventana ni suponerse resuelta por el integrador 0D actual.
