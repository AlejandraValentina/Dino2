# Verificación del primer incremento

Registro histórico de etapa 0. Para las pruebas del simulador físico 4T y la distribución actual, consultar [informe-4t.md](informe-4t.md).

Ejecutada localmente el 19 de noviembre de 2026, Windows 10 x64 build 19045, SDK .NET 10.0.404, compilación Release. Python 3.11 se usó únicamente como referencia numérica independiente. No se usaron el código ni las arquitecturas del Dino anterior.

## Resultados

| Comprobación | Resultado |
|---|---|
| Compilación de la solución | Correcta, 0 advertencias / 0 errores |
| Núcleo y archivos | 13/13 grupos de pruebas aprobados |
| Interfaz WPF | 10/10 comprobaciones aprobadas |
| Integración numérica independiente en Python | Aprobada, tolerancia relativa 1e-7 |
| Reproducción por CLI | 11 puntos, de 1000 a 6000 RPM |
| Revisión visual | Curvas, tabla RPM, estados/balance, tamaño mínimo y caso cero/un punto |
| Ejecutable Windows x64 publicado | Abre y calcula mediante UI Automation; runtime cargado desde la distribución |

`scripts/smoke-publish.ps1` comprobó el punto de entrada real del ejecutable, activó **Calcular**, verificó `249.875 J/ciclo` en el resumen y confirmó que `coreclr.dll` se cargó desde `artifacts/win-x64`, sin usar el runtime instalado globalmente. Los archivos de distribución no se incluyen en commits.

## Cobertura científica y de software

Los 13 grupos del ejecutor `tests/Dino2.Tests` verifican:

1. Referencia del ejemplo para geometría, masa, estados, eficiencia, trabajo e IMEP.
2. Conservación de energía y ecuación de estado en 150 configuraciones con semilla fija 734.
3. Trabajo neto por integración numérica de p(V)dV en compresión y expansión, 50000 subdivisiones por proceso, frente al trabajo analítico.
4. Frecuencia de ciclo 4T e identidad potencia = par × velocidad angular a todos los regímenes.
5. Extremos de barridos, intervalo no divisible y un solo punto.
6. Caso de aporte de calor cero.
7. Escalado físico con geometría, calor y presión, y aumento de eficiencia ideal con compresión.
8. Rechazo de entradas físicas inválidas, NaN e infinitos.
9. Rechazo de mallas RPM inválidas, paso que desborda y cantidad excesiva de puntos.
10. Finitud y balance en esquinas del dominio numérico aceptado.
11. Guardado/apertura/recalculado deterministas, reemplazo de archivo existente y conservación de este ante parámetros inválidos.
12. JSON incompleto, mal formado, nulo, desconocido o incompatible.
13. CSV con snapshot del input, identificador de modelo y números sin pérdida por cambio de cultura decimal.

Las aserciones analíticas usan por defecto `error ≤ 1e-10 + 1e-11·|referencia|`, en las unidades de cada magnitud. La integral numérica usa tolerancia relativa `1e-7`. Estas tolerancias caracterizan comprobaciones computacionales; no son incertidumbre experimental.

La prueba WPF crea la ventana real en un hilo STA con los mismos estilos que la aplicación. Activa el botón de cálculo y edita controles; verifica cálculo inicial y tabla, invalidación al editar, coma decimal, guardado/reapertura, recálculo determinista, errores visibles para RPM fraccionarias y rechazo de no finitos, conservación de datos al fallar una carga y renderizado con calor cero/un solo régimen. Inspecciona los resultados de las acciones, no solo la existencia de controles.

La prueba ejercita `SaveTo`/`LoadFrom`, las mismas operaciones usadas por los botones de archivo. **No automatiza los diálogos nativos de selección de archivo ni las respuestas de confirmación de descarte.** El modelo tampoco contiene ecuaciones diferenciales: no corresponde afirmar convergencia temporal de un solver todavía inexistente.

## Referencia independiente

`scripts/reference.py` lee el ejemplo y evalúa el trabajo mediante integración de presión respecto al volumen, usando 100000 subdivisiones por proceso. No importa código C# ni calcula trabajo mediante η·Qin. El resultado observado es:

```text
Wi por integral numérica = 249.87530530669966 J/ciclo
Wi analítico C#          = 249.87530538485063 J/ciclo
Par ideal C#            = 19.884445004297937 N m
Pi a 6000 RPM C#        = 12493.76526924253 W
```

El residual de energía del ejemplo observado en la interfaz es aproximadamente `2.84e-14 J/ciclo`. Las últimas cifras pueden variar levemente con plataforma/runtime. No usar todas las cifras de doble precisión como precisión científica del motor.

## Repetir la verificación

```powershell
.\scripts\verify.ps1
python scripts/reference.py   # opcional, Python 3
.\scripts\publish.ps1
.\scripts\smoke-publish.ps1
```

Los scripts detienen la ejecución al fallar un comando. Sin sesión gráfica, usar `verify.ps1 -SkipDesktop`: en ese caso **no** se han verificado las comprobaciones de ventana en esa ejecución. Para correr un grupo específico:

```powershell
dotnet run --project tests/Dino2.Tests -c Release
dotnet run --project tests/Dino2.Desktop.Tests -c Release
```

No hay dependencias de paquetes de pruebas. `dotnet test` no descubre estos ejecutores. Su código de salida es 0 para éxito y 1 para fallos; no debe sustituirse el script por una compilación o un `dotnet test` vacío.

Las pruebas de ventana generan `artifacts/verification/desktop-default.png`, `desktop-rpm-table.png`, `desktop-states.png`, `desktop-minimum.png` y `desktop-zero-single.png`. También generan el JSON/CSV de su escenario de edición de 90 mm, separado del ejemplo canónico de 80 mm reproducido como `otto-400cc.csv`.

## Pendientes y límites reales

No se ensayaron otras versiones de Windows, ARM64, escalas DPI distintas de la sesión local, corrupción por fallo eléctrico, archivos multiusuario ni diálogos de archivo automatizados. La distribución no tiene instalador ni firma. No existe validación experimental, modelo de fricción, intercambio de gases, combustión finita, predicción de ganancias de modificaciones, A/B integrado, estudio automático ni modelo 2T. El [plan científico](evolucion.md) fija el siguiente incremento verificable.
