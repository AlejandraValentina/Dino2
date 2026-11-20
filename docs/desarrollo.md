# Desarrollo y reproducción de Dino2

## Proyectos actuales

| Proyecto | Responsabilidad |
|---|---|
| **Dino2.Core** | Configuraciones inmutables, validación de dominio, ciclo Otto, núcleos 4T y 2T, termodinámica, gas dinámica, ejecución y objetivos de estudio. |
| **Dino2.Storage** | Formatos JSON versionados, validación de resultados, guardado en archivo temporal y exportaciones tabulares. No invoca el solver. |
| **Dino2.Desktop** | WPF, edición en unidades de usuario, presentación, navegación, tareas en segundo plano, cancelación y gráficos. Los cambios a un motor se capturan antes del cálculo. |
| **Dino2.Cli** | Ejecuta los modelos y guarda datos desde archivos de configuración. |
| **Dino2.\*.Tests** | Ejecutores de consola de física, almacenamiento, numerics, estudios e interfaz. Las pruebas de Desktop requieren una sesión gráfica Windows. |

La solución es **Dino2.slnx**. 4T y 2T comparten propiedades de especies, mecanismos numéricos de conductos y objetivos de estudios. Conservan configuraciones, ecuaciones de ciclo y documentos propios. Otto es independiente. La referencia describe los identificadores que se han consolidado. [Referencia científica](referencia-cientifica.md).

## Versionado y campos

- El modelo 4T se identifica por el conjunto de opciones que determina el operador físico. El esquema 1D distingue primer orden y MUSCL.
- El 2T conserva su identidad de modelo físico. Las curvas y los contrastes pueden declarar la revisión numérica. Al actualizar evidencia de una revisión previa se recalcula la base antes del contraste.
- Los archivos Otto, 4T y 2T tienen envoltorios propios, al igual que los estudios. Las configuraciones se guardan en SI. Cuando se cambia un formato, se verifica la lectura de ejemplos previos y se conserva la distinción entre configuración y resultado.
- Para exponer un parámetro, incorporá validación, unidad y condiciones de aplicabilidad en la configuración. Usá **EngineParameters.cs** en 4T o **TwoStrokeEditor.cs** en 2T. Para estudios, actualizá el catálogo físico de **StudyParameters** o **TwoStrokeParameters** y comprobá el costo, la capacidad real del modelo y las variantes. Las lecturas de tabla preservan valores numéricos para ordenar.

## Construcción y pruebas

~~~powershell
dotnet build Dino2.slnx -c Release
.\scripts\verify.ps1
# Para un entorno sin sesión gráfica:
.\scripts\verify.ps1 -SkipDesktop
~~~

**verify.ps1** ejecuta los proyectos de pruebas de consola y los recorridos WPF. Un **dotnet test** por sí solo no ejecuta esas comprobaciones. Para publicar y validar en Windows:

~~~powershell
.\scripts\publish.ps1 -Destination artifacts/stage8-win-x64 -Archive artifacts/Dino2-stage8-win-x64.zip
.\scripts\smoke-publish.ps1 -Application artifacts/stage8-win-x64/Dino2.Desktop.exe
.\scripts\smoke-studies.ps1 -Application artifacts/stage8-win-x64/Dino2.Desktop.exe
.\scripts\smoke-two-stroke.ps1 -Application artifacts/stage8-win-x64/Dino2.Desktop.exe -VerifyResolution -VerifyDomain -AuditFiles
.\scripts\audit-desktop.ps1 -Application artifacts/stage8-win-x64/Dino2.Desktop.exe -Destination artifacts/stage8/screens -Consolidated
~~~

El ZIP contiene un ejecutable x64 con runtime, ejemplos y documentación. La publicación descarga los paquetes oficiales del runtime si faltan en el entorno de construcción. Las capturas nativas usan UI Automation y PrintWindow sobre el ejecutable. Las pruebas WPF generan capturas y verifican el control de gráficos sin depender de una posición de pantalla.

## Reproducir resultados científicos

~~~powershell
dotnet run --project src/Dino2.Cli -c Release -- examples/otto-400cc.json artifacts/otto.csv
dotnet run --project src/Dino2.Cli -c Release -- --4t examples/4t-ducts-muscl-ab.json artifacts/4t.csv artifacts/4t-traza.csv
dotnet run --project src/Dino2.Cli -c Release -- --2t examples/2t-125cc-wide-rpm.json artifacts/2t.csv
.\scripts\repro-stage7.ps1
~~~

Creá **artifacts** si todavía no existe. Las pruebas de termodinámica independiente con Cantera son opcionales. Los scripts Python y las cifras históricas se explican en sus informes. Ejemplos de proyecto y estudios de uso corriente están en **examples**, y los subconjuntos históricos se describen en el índice. [Documentación](indice.md).
