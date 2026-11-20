# Etapa 9 — Integración y verificación multicilíndrica 4T

**CLOSED dentro del dominio numérico declarado para colectores compactos.**

## Etapa 8 en main

El merge local **a4ef998** tiene como padres **9e0a6b1** y **4cee2f0**. El árbol integrado coincide con el cierre de etapa 8. La revisión de ramas y worktrees encontró un árbol limpio y no hubo conflictos. Se repitieron las regresiones térmicas, 1D, 2T, Otto, de estudios, archivos y WPF. El portable integrado cargó su propio runtime y ejecutó Otto, 4T con MUSCL, estudios 4T y el recorrido 2T con estudio, refinamiento A/B, reapertura, CSV, dominio parcial y cancelación. La rama **stage9/multicilindrico-4t** parte de ese merge. [Cierre de etapa 8](etapa8.md).

## Red y resultado funcional

Dino2 Desktop abre el módulo desde la pantalla 4T. Permite elegir entre dos, tres o cuatro cilindros en línea, editar fases, ramales, volumen de unión y conductos comunes, comparar A/B y ejecutar estudios con los objetivos existentes. Cada cilindro tiene su cilindro reactivo, válvulas y estado de ramales. Los depósitos exteriores intercambian especies y energía con el motor por dos redes. Se conserva el modo de ramales aislados para reproducir el límite monocilíndrico. [Modelo y ecuaciones](modelo-multicilindrico-4t.md) · [Manual](manual-desktop.md).

Los estados incompletos se conservan con su diagnóstico y sin valores de potencia. El archivo multicilíndrico conserva configuraciones, curvas con presión por cilindro y colector, A/B, evidencia de contraste y estudio ejecutado. Los CSV identifican el modelo y la configuración SI. Los estudios seleccionan casos en la grilla; cada variante puede aplicarse como B y volver a calcularse con las trazas completas.

La regresión completa pasó con compilación Release sin advertencias. La prueba de Windows abrió **Dino2.Desktop.exe** desde el ZIP extraído y verificó la carga de **coreclr.dll** del mismo directorio. Calculó el bicilíndrico, conservó A, modificó el volumen de admisión, verificó el contraste a tres mallas y exportó A/B, ciclo y contribuciones por cilindro. Ejecutó un estudio de tres casos, exportó resultados, guardó y reabrió el proyecto, aplicó B y canceló una simulación conservando el trabajo previo. Los ejemplos de dos, tres y cuatro cilindros se reprodujeron también desde la CLI.

## Verificación científica

- **Problema independiente de tres tubos.** Reposo uniforme, impulso débil en el tronco y detección en un ramal con dos extremos cerrados. Se usaron composiciones diferentes entre ramales en la prueba transitoria. Con 64 celdas por tubo y CFL 0,35 se obtuvieron 24,965 Pa de amplitud transmitida, 24,795 Pa de onda de vuelta y flujo inverso. Duplicar celdas desde 32 cambió la amplitud en 0,700 Pa. Reducir el CFL a la mitad en 64 celdas la cambió en 0,047 Pa. El pico llegó en 1,276 ms frente a un tiempo acústico nominal de 1,088 ms a 344,55 m/s. El volumen compresible y el ancho de pulso desplazan su máximo. El balance de especies y energía de la red inerte con paso fino cerró a 5,98 × 10⁻¹⁹ kg y 7,46 × 10⁻¹³ J.
- **Referencia aislada a 4000 RPM.** El 4T monocilíndrico de etapa 4 dio 357,496353 J/ciclo y 9510,61966 W estimados al cigüeñal. La configuración bicilíndrica aislada dio 714,992728 J/ciclo y 19 021,2401 W. Las pruebas cubren también el límite aislado de tres y cuatro cilindros y un encendido irregular que cruza el corte global. Se calcula el par a partir del trabajo y se comprueba integrando, en forma independiente, las muestras de par indicado del gas.
- **Red acoplada.** Los nueve puntos de la tabla alcanzaron periodicidad conjunta. Los errores absolutos de masa y energía de esos casos fueron inferiores a 9 × 10⁻¹⁸ kg y 8 × 10⁻¹² J. El criterio contempla cilindros, ramales, troncos, uniones y trabajo. Las pruebas verifican transporte de todas las especies y energía en las interfaces, elementos del sistema reactivo, balance de momento axial de los ramales, ausencia de periodicidad y cancelación.
- **Tiempo y ciclos.** En el bicilíndrico a 4000 RPM, reducir el máximo angular de 0,5° a 0,125° sin cambiar la malla modificó el trabajo en +0,00220 J por ciclo. Contra la misma base, reducir la tolerancia periódica de 4 × 10⁻⁴ a 2,5 × 10⁻⁵ modificó el trabajo en −0,0501 J. Se pasó de 27 a 37 ciclos al apretar la periodicidad. Las cifras se refieren a este caso.
- **Resolución A/B.** Pasar de 2,4 a 3,6 L en la unión de admisión del bicilíndrico dio +56,859, +55,478 y +55,283 W a 4000 RPM con espaciamientos ×1, ×2 y ×4. La banda empírica de la diferencia fue 2,762 W. El modelo clasificó B como superior en esa prueba. En la configuración A, la potencia pasó de 16 586,373 W en la malla base a 16 616,462 W en la más fina. El desplazamiento de la potencia absoluta y el de la diferencia pareada se miden por separado.
- **Estudio factorial.** Se ejecutaron dos longitudes de admisión (380 y 400 mm) y dos avances (19 y 20°) a 4000 RPM. Se evaluaron cinco casos incluyendo la base y cuatro curvas únicas. Uno y dos trabajadores produjeron las mismas salidas físicas. Se verificaron inmutabilidad del original, deduplicación, costo antes de ejecutar, aplicación de la variante y recuperación del archivo. Los casos analíticos de selección, objetivos y bandas se conservan en **Dino2.Studies.Tests**. [Etapa 5](etapa5.md).

### Curvas del dominio reproducido

Potencia estimada al cigüeñal en kW y eficiencia volumétrica en porcentaje.

| RPM | 2 cilindros kW / % | 3 cilindros kW / % | 4 cilindros kW / % |
|---:|---:|---:|---:|
| 3000 | 15,840 / 77,69 | 23,419 / 76,97 | 30,482 / 75,81 |
| 4000 | 16,586 / 68,29 | 24,062 / 66,92 | 30,698 / 65,23 |
| 5000 | 14,330 / 58,59 | 20,208 / 56,78 | 25,054 / 54,85 |

A 4000 RPM el bicilíndrico conectado produjo 640,083 J de trabajo total frente a 714,993 J en el límite aislado. Las pulsaciones de presión en ambos lados, los retornos y el cambio de aire admitido se calculan en las interfaces. La pérdida de rendimiento del ejemplo compartido corresponde a esa red y a esas condiciones.

## Rendimiento

Los costos observados reflejan el número de celdas, el paso de integración y la cantidad de ciclos para alcanzar periodicidad. A 4000 RPM los casos compartidos ejecutaron 3,74, 2,79 y 4,17 millones de evaluaciones de celda para dos, tres y cuatro cilindros, respectivamente. La reconstrucción y la recuperación de temperatura de las celdas se ejecutan en las dos etapas de cada paso. Los ensayos del núcleo omiten almacenar trazas en estudios y contrastes, y reutilizan los buffers durante la integración. Se mantienen separados los estados de las configuraciones que ejecutan los trabajadores. En una ejecución factorial el cálculo tardó 12,515 s con un trabajador y 9,373 s con dos. Son mediciones locales con .NET 10.0.15 y 12 procesadores lógicos.

Tiempo y asignación administrada medianos de tres ejecuciones a 4000 RPM con captura de ciclo. El pico de conjunto de trabajo es el del proceso de consola completo, en MiB. El primer renglón usa el 4T independiente de etapa 4. Las diferencias de número de ciclos entre redes explican el tiempo del tricilíndrico.

| Cilindros | Mediana en s | Asignación en MiB | Pico de conjunto de trabajo en MiB |
|---:|---:|---:|---:|
| 1 | 0,251 | 1,05 | 30,6 |
| 2 | 3,513 | 1,33 | 32,7 |
| 3 | 2,627 | 1,85 | 29,5 |
| 4 | 4,007 | 2,41 | 30,1 |

En el portable, las curvas A y B de una RPM tardaron 4,27 y 4,64 s. Su contraste completo a tres resoluciones tardó 186,17 s. El estudio nativo de tres casos y dos curvas únicas tardó 8,28 s.

## Reproducción

~~~powershell
.\scripts\verify.ps1
dotnet run --project tests/Dino2.Multicylinder.Tests -c Release -- --campaign
1..4 | ForEach-Object { dotnet run --project tests/Dino2.Multicylinder.Tests -c Release -- --benchmark $_ }
dotnet run --project src/Dino2.Cli -c Release -- --multi examples/multi-4t-inline4.json artifacts/multi4.csv artifacts/multi4-ciclo.csv
pwsh -File scripts/smoke-multicylinder.ps1 -Application artifacts/stage9-win-x64/Dino2.Desktop.exe -Destination artifacts/stage9-native-evidence
~~~

Podés abrir cualquiera de los tres archivos **multi-4t-inlineN.json** desde el módulo multicilíndrico. Para reproducir el contraste de volumen con un costo acotado, fijá inicio y fin en 4000 RPM, aumentá el límite a 65 ciclos y corré A y B con 2,4 y 3,6 L de admisión. [Guía de publicación](desarrollo.md).

## Límite de la evidencia

Se verificaron balances, fases, ondas, flujo inverso, periodicidad, sensibilidad numérica y flujo de trabajo. No hubo identificación de parámetros ni validación experimental de las curvas. Los contornos son depósitos, los colectores usan volúmenes compactos y los ramales de un lado son iguales. La fricción y la combustión dependen de parámetros transferidos o estimados. El modelo no resuelve distribución espacial dentro de un plenum, pérdidas de uniones orientadas ni torsión. La comparación de datos publicados se limita a identificar fuentes y requisitos de una campaña futura. [Alcance, publicaciones y metodología experimental](modelo-multicilindrico-4t.md).
