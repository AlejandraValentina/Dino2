# Dino2

Simulador de motores en desarrollo, C# / WPF / .NET 10. Primer incremento ejecutable: ciclo **Otto ideal de aire**, monocilíndrico 4T. Permite configurar geometría y condiciones termodinámicas, calcular varios regímenes, inspeccionar potencia y par **indicados ideales**, guardar/abrir configuraciones JSON y exportar resultados trazables a CSV.

**No predice todavía las curvas de un motor real.** El par constante y la potencia lineal con RPM son consecuencias del modelo ideal. No hay validación experimental ni cálculo de potencia al cigüeñal. El ejemplo no representa un motor medido.

El objetivo definitivo es un simulador predictivo de motores reales 4T y 2T, capaz de evaluar modificaciones, conservar el original, comparar variantes y posteriormente ejecutar estudios de parámetros. Este incremento verifica el flujo de trabajo y la termodinámica; no reemplaza ese objetivo.

## Abrir la aplicación en Windows

Distribución local preparada: `artifacts/Dino2-win-x64.zip`. Extraer **todo** el ZIP a una carpeta y abrir `Dino2.Desktop.exe`. También puede abrirse directamente `artifacts/win-x64/Dino2.Desktop.exe`. Incluye el runtime: el usuario no necesita instalar .NET. Los artefactos son generados, no se suben a Git.

1. Cargar el ejemplo inicial o usar **Abrir…** con `examples/otto-400cc.json` (también incluido en el ZIP).
2. Introducir diámetro, carrera, compresión, presión absoluta y temperatura iniciales, calor específico aportado, gamma y constante del gas. Desplazar el panel izquierdo para editar RPM inicial/final/paso.
3. Pulsar **Calcular**. Las curvas tienen ejes separados con unidades; el cursor permite consultar puntos. La pestaña **Tabla RPM** muestra sus valores y **Estados y balance** permite inspeccionar la termodinámica.
4. Usar **Guardar como…** para conservar la configuración. Al abrirla se recalculan los resultados con **Calcular**. Se advierte antes de descartar cambios al abrir, cargar el ejemplo o cerrar.
5. **Exportar CSV…** incluye entradas SI, identificador de modelo, estados, balance y resultados sin redondear. Las primeras líneas `#` son metadatos; el separador es coma y el decimal es punto.

La edición acepta coma o punto decimal, sin separadores de miles. Las RPM deben ser enteras. Cambiar un campo elimina los resultados previos y deshabilita la exportación hasta recalcular. Un archivo inválido no reemplaza la configuración activa.

## Compilar y ejecutar desde código

Requisitos: Windows x64 y [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). No requiere Python, Node, Visual Studio ni paquetes NuGet externos para la aplicación y sus pruebas C#.

Desde la raíz del repositorio, en PowerShell:

```powershell
dotnet build Dino2.slnx -c Release
dotnet run --project src/Dino2.Desktop -c Release
```

En el entorno de desarrollo donde se preparó esta entrega hay un SDK local ignorado por Git. Si el `dotnet` global sigue en versión 9, sustituir `dotnet` por `.\.tools\dotnet\dotnet.exe`. `global.json` requiere la familia 10.0 y admite bandas posteriores de esa familia. Los scripts detectan automáticamente el SDK local si existe.

## Verificar y reproducir

```powershell
.\scripts\verify.ps1
# En una sesión sin escritorio interactivo:
.\scripts\verify.ps1 -SkipDesktop
```

Los ejecutores de pruebas son programas de consola con aserciones y código de salida no cero ante fallos. **`dotnet test` no ejecuta estas pruebas**. El script compila, ejecuta 13 grupos de física/archivos, realiza 10 comprobaciones de la ventana y reproduce el ejemplo en `artifacts/verification/otto-400cc.csv`. La prueba de escritorio abre y cierra una ventana, y genera capturas PNG.

Reproducción independiente de la interfaz:

```powershell
dotnet run --project src/Dino2.Cli -c Release -- examples/otto-400cc.json resultados.csv
```

Referencia opcional independiente, Python 3 (solo biblioteca estándar):

```powershell
python scripts/reference.py
```

El ejemplo debe dar aproximadamente **249,875305 J/ciclo**, **19,884445 N·m** y **12,493765 kW a 6000 RPM**, indicados ideales. La integración numérica independiente tiene tolerancia relativa `1e-7`.

## Generar la distribución

```powershell
.\scripts\publish.ps1
.\scripts\smoke-publish.ps1
```

El primer script produce la carpeta `artifacts/win-x64` y `artifacts/Dino2-win-x64.zip`. El segundo abre el ejecutable publicado, activa el cálculo mediante Windows UI Automation, verifica el resultado y que el runtime se cargue desde la distribución, y cierra la ventana. Requiere una sesión interactiva de Windows.

El primer publish requiere red para descargar los paquetes oficiales del runtime. Distribución en carpeta, sin instalador ni firma de código en esta etapa. Se incluye documentación del modelo y el ejemplo.

## Documentación y alcance

- [Arquitectura y elección tecnológica](docs/arquitectura.md).
- [Ecuaciones, hipótesis, unidades y límites del modelo](docs/modelo-otto.md).
- [Verificación realizada y reproducción](docs/verificacion.md).
- [Dirección científica y próximos incrementos](docs/evolucion.md).

El proyecto Dino anterior no fue consultado ni modificado. El código es nuevo. No se ha hecho push.
