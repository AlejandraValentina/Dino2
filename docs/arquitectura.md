# Arquitectura inicial y decisiones

Fecha de decisión: 19 de noviembre de 2026. Alcance entregado: una funcionalidad vertical de escritorio para un ciclo Otto ideal de aire. Destino del producto: simulación predictiva de motores reales 4T y 2T, evaluación de modificaciones, comparación original/variante y estudios de parámetros. El modelo inicial es una referencia termodinámica, no la arquitectura física definitiva.

## Tecnología

| Alternativa | Ventajas para este proyecto | Coste o limitación | Decisión |
|---|---|---|---|
| C# + WPF + .NET 10 | Escritorio Windows, tipado, doubles y ejecución compilada; mismo lenguaje en física, archivos e interfaz; distribución con runtime | WPF funciona únicamente en Windows | Elegida: coincide con la plataforma pedida |
| Python + PySide6 | Ecosistema científico amplio; interfaz Qt | Entorno Python/Qt y empaquetado adicional; los bucles intensivos futuros necesitarían evaluar vectorización o código compilado | Viable, pero no aporta una ventaja necesaria al primer ciclo analítico |
| C# + Avalonia | Reutiliza C# y ofrece varias plataformas | Dependencia gráfica adicional para una necesidad multiplataforma no solicitada | Reconsiderar si cambia el alcance de plataforma |
| Electron + interfaz web | Ecosistema de gráficos y UI web | Añade Chromium/Node y comunicación con el cálculo si se usa otro lenguaje | No justificado para el alcance actual |
| C++ + Qt | Control de memoria y cálculo compilado | Mayor coste de mantenimiento y empaquetado para la misma funcionalidad inicial | Reconsiderar solo para kernels medidos que lo requieran |

Hechos de plataforma consultados en fuentes primarias: [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview), [Qt for Python](https://doc.qt.io/qtforpython-6.10/index.html), [Avalonia](https://docs.avaloniaui.net/docs/supported-platforms) y [modelo de procesos de Electron](https://www.electronjs.org/docs/latest/tutorial/process-model). Los costes comparativos son juicio de diseño, no un benchmark entre frameworks.

Se elige .NET 10 LTS frente a los SDK/runtimes 8/9 instalados originalmente. La [política oficial de soporte](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) da soporte a la rama 10 hasta noviembre de 2028; las ramas 8/9 terminaron soporte el 10 de noviembre de 2026. Se probó con SDK 10.0.404. No se utiliza el SDK global 9 para construir esta solución.

## Dependencias y responsabilidades

```text
Dino2.Desktop ──→ Dino2.Core
       │
       └───────→ Dino2.Storage ──→ Dino2.Core
Dino2.Cli ─────→ Dino2.Core + Dino2.Storage
```

- **Core**: configuración inmutable del modelo Otto, validación numérica y física, estados y solución analítica. Sin WPF, archivos, reloj, aleatoriedad, estado global mutable ni servicios externos. Los límites públicos usan SI; el nombre `OttoConfiguration` evita tratar gamma, calor aportado y condiciones iniciales como el contrato de todo motor futuro.
- **Storage**: serialización JSON versionada, rechazo de modelos incompatibles, escritura temporal y reemplazo del archivo, CSV con metadatos. No contiene ecuaciones físicas.
- **Desktop**: conversión de unidades de presentación, edición, acciones del usuario, invalidación de resultados y renderizado. La ventana llama al núcleo; no implementa termodinámica. `CurvePlot` solo recibe puntos calculados.
- **CLI**: el mismo recorrido configuración → núcleo → CSV, sin interfaz. Facilita reproducir y revisar resultados.
- **Tests**: dos ejecutores pequeños: física/archivos y ventana WPF. Sin framework de pruebas, para evitar dependencias en esta primera entrega. Su límite es que no tienen descubrimiento de `dotnet test`; el script y el README hacen explícito el comando correcto. Se puede migrar a un framework cuando el número de casos o las herramientas de desarrollo lo justifique.

El primer núcleo combina una evaluación analítica del ciclo y su conversión a una malla RPM, ambas puras y deterministas. No hay todavía integrador temporal, scheduler, contenedor de dependencias, sistema de plugins ni plataforma de ejecución. Cuando exista un sistema de ecuaciones diferenciales, el integrador y el control de convergencia se separarán de las ecuaciones del modelo. No corresponde introducirlos para un problema que ahora tiene solución cerrada.

## Persistencia y trazabilidad

JSON tiene `schemaVersion`, `model` y `configuration`. `model=otto-air-standard-v1` fija ecuaciones y supuestos. Todos los campos de configuración son obligatorios y los desconocidos se rechazan; no se rellenan silenciosamente configuraciones incompletas con el ejemplo. El CSV contiene la configuración efectivamente calculada, estados del ciclo, balance y valores SI de doble precisión. JSON es el formato reabrible; CSV es una salida de análisis con metadatos.

Al guardar se escribe y vacía un archivo temporal en el mismo directorio antes de reemplazar el destino. Esto evita truncar el archivo existente ante un error durante la serialización. No se promete protección absoluta frente a fallo eléctrico, recuperación histórica ni guardado concurrente multiusuario. Las configuraciones se recalculan al abrir; no hay caché opaca.

Cambios de ecuaciones o significado de parámetros necesitan otra versión de modelo. Los futuros modelos tendrán configuraciones propias: no se reinterpretará este JSON como un motor 4T predictivo. Todavía no se necesita un registro dinámico de modelos ni migraciones.

## Interfaz y coste de cálculo

La ventana usa eventos directos y una operación sincrónica, porque el trabajo actual es pequeño y está acotado a 2000 puntos. No se añade MVVM ni una cola de trabajos para este tamaño de editor. No hay ecuaciones en los eventos. Las colecciones de salida son de solo lectura y cada cálculo conserva su snapshot inmutable de entrada.

El ciclo se resuelve una vez: coste O(1). El barrido genera N puntos: tiempo/memoria O(N). Los gráficos recorren N puntos sin interpolación física ni splines. Solo se dibujan marcadores para hasta 60 puntos. Un único punto y calor cero tienen representación definida. No hay paralelismo porque no existe evidencia que justifique su coste.

El futuro cálculo transitorio requerirá ejecución fuera del hilo de UI, cancelación y publicación de una solución completa. Las ecuaciones y las colecciones inmutables actuales permiten ese cambio sin arrastrar controles gráficos al solver. No se afirma aún rendimiento de modelos 1D ni se elige una tecnología nativa antes de medirlos.
