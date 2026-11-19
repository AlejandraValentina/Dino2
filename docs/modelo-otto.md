# Modelo `otto-air-standard-v1`

## Propósito y evidencia

Referencia termodinámica para establecer el flujo de Dino2. Es un ciclo cerrado ideal de aire con aporte de calor externo; la asignación de potencia supone un monocilíndrico que completa un ciclo por dos revoluciones, como un 4T. No se resuelven físicamente admisión ni escape. No hay datos de un motor real ni parámetros calibrados experimentalmente.

Los procesos y las relaciones de compresión/expansión siguen el tratamiento del ciclo Otto ideal en [MIT OpenCourseWare, 16.050, parte 2A, páginas 2A-1 a 2A-3](https://live.ocw.mit.edu/courses/16-050-thermal-energy-fall-2002/75666f9248981634a704fdf257ef529a_07_part2a.pdf). La frecuencia de un ciclo por dos revoluciones en un 4T se describe también en [Purdue, notas de C. Wassgren, sección 3.8.4](https://engineering.purdue.edu/~wassgren/teaching/ME20000/NotesAndReading/Lec37_Reading_Wassgren.pdf).

Estas referencias fundamentan las hipótesis y las ecuaciones ideales. La verificación numérica de Dino2 no constituye validación experimental del motor ni de una curva predictiva.

## Entradas

| Símbolo / campo JSON | Significado | Unidad interna | Presentación | Límites de entrada |
|---|---|---|---|---|
| B / `boreM` | Diámetro de cilindro | m | mm | 0,001 a 1 m |
| S / `strokeM` | Carrera | m | mm | 0,001 a 1 m |
| r / `compressionRatio` | Vmáx / Vmín | 1 | 1 | 1,01 a 30 |
| p₁ / `initialPressurePa` | Presión absoluta al inicio de compresión | Pa | kPa | 1000 a 1000000 Pa |
| T₁ / `initialTemperatureK` | Temperatura al inicio de compresión | K | K | 150 a 1500 K |
| qin / `heatInputJPerKg` | Calor añadido por masa de aire | J/kg | kJ/kg aire | 0 a 3000000 J/kg |
| γ / `gamma` | cp/cv constante | 1 | 1 | 1,05 a 1,67 |
| R / `gasConstantJPerKgK` | Constante específica del gas | J/(kg·K) | igual | 200 a 400 |
| `rpmStart`, `rpmEnd`, `rpmStep` | Malla de regímenes | rev/min | RPM | enteros de 1 a 30000; fin ≥ inicio |

Son límites de operación del software, **no un dominio experimentalmente validado**. Se rechazan NaN, infinitos y barridos de más de 2000 puntos. La malla es `inicio + k·paso ≤ fin`; no se agrega un último punto fuera del paso.

El ejemplo usa aire caloríficamente perfecto con R=287 J/(kg·K) y γ=1,4. Son constantes asumidas, no un ajuste a mediciones. `qin=800 kJ/kg` es un escenario térmico ilustrativo: no es PCI del combustible, ni una eficiencia de combustión escondida. Diámetro/carrera/compresión y condiciones iniciales del ejemplo tampoco provienen de ensayos.

## Hipótesis

1. Un cilindro, contenido homogéneo, masa encerrada constante. Gas ideal con R y calores específicos constantes; cv=R/(γ−1), cp=γ·cv.
2. Compresión 1→2 y expansión 3→4 adiabáticas reversibles (isentrópicas).
3. Aporte 2→3 y rechazo 4→1 de calor a volumen constante. Rechazo de calor positivo se reporta como **magnitud de energía saliente**.
4. Sin cinética de combustión, transferencia a paredes, fugas, masa de combustible ni gases residuales diferenciados. La masa es la carga ideal encerrada a p₁/T₁ en Vmáx; no se confunde con masa fresca calculada usando cilindrada y eficiencia volumétrica.
5. Sin pérdidas de bombeo, fricción, accesorios, limitaciones de caudal, variación de llenado o transferencia térmica con RPM. No hay límite de velocidad mecánico ni modelo de detonación.
6. El mismo ciclo se repite a cada régimen. El trabajo por ciclo es independiente de las RPM por construcción.

## Ecuaciones implementadas

Todos los volúmenes en m³; temperaturas absolutas y presiones absolutas. Signo positivo del trabajo: trabajo neto realizado por el gas.

```text
Vd = (π/4) B² S               [m³, cilindrada]
V2 = V3 = Vd / (r − 1)        [m³, volumen de cámara]
V1 = V4 = Vd + V2             [m³, volumen máximo]
m = p1 V1 / (R T1)           [kg]
cv = R / (γ − 1)             [J/(kg K)]

T2 = T1 r^(γ−1)
T3 = T2 + qin/cv
T4 = T3 / r^(γ−1)
pi = m R Ti / Vi             [Pa, cada estado; p1 es la entrada]

Qin = m qin                  [J/ciclo]
Qout = m cv (T4 − T1)        [J/ciclo, magnitud positiva saliente]
ηideal = 1 − r^(1−γ)         [1]
Wi = Qin ηideal             [J/ciclo]
IMEP = Wi / Vd              [Pa, presión media indicada ideal]

f = RPM / 120               [ciclos/s, un ciclo por 2 revoluciones]
Pi = Wi f                   [W, potencia indicada ideal]
ω = 2π RPM / 60             [rad/s]
τi = Wi / (4π) = Pi/ω       [N m, par indicado medio ideal]
```

La identidad independiente `Wi = Qin − Qout` permite comprobar el balance. La implementación evalúa `Qin·ηideal` para evitar cancelaciones innecesarias, y obtiene Qout mediante temperaturas. La integral cerrada ∮p·dV también coincide con Wi; las pruebas la verifican mediante integración numérica independiente.

Cuando qin=0, calor, trabajo y potencia son cero. El número ηideal mostrado es el rendimiento límite del ciclo para qin→0; el cociente W/Qin no está definido en el caso exactamente cero. El balance de la interfaz lo explica.

La potencia indicada es la transferencia de trabajo del gas al pistón, aquí idealizada. **Potencia al cigüeñal/al freno: no calculada.** No se supone que ambas sean iguales ni se añade una eficiencia mecánica sin fundamento.

## Ejemplo reproducible

Entrada: `examples/otto-400cc.json` (B=S=80 mm, r=10, p₁=100 kPa, T₁=300 K, qin=800 kJ/kg aire, γ=1,4, R=287).

| Magnitud | Resultado aproximado |
|---|---|
| Vd | 402,1238597 cm³ |
| Masa | 0,000518936456 kg |
| T₂ / T₃ / T₄ | 753,565929 / 1868,548508 / 743,882560 K |
| p₃ | 6228495,026 Pa |
| Qin / Qout | 415,149165 / 165,273859 J/ciclo |
| Wi | 249,875305 J/ciclo |
| ηideal | 0,6018928294 |
| IMEP | 6,213889064 bar |
| τi (todos los regímenes) | 19,88444500 N·m |
| Pi a 1000 / 6000 RPM | 2,082294212 / 12,49376527 kW |

No se ajustó una curva ni se inventaron puntos de potencia: todos proceden de estas ecuaciones y entradas. Tampoco se presenta este escenario como un motor validado.

## Límites científicos

Los calores específicos reales cambian con temperatura; incluso el pico del ejemplo ya exige cautela sobre esta idealización. Una temperatura o presión calculada no implica viabilidad mecánica o química. El modelo no predice respuesta al avance, alzada, distribución, lumbreras o geometría de admisión/escape. Sus únicas sensibilidades son las de las ecuaciones anteriores; no deben extrapolarse a ganancias reales en un motor.

No se modelan curvas de eficiencia volumétrica ni de pérdidas elegidas para dar una forma plausible. La dependencia real con RPM deberá surgir de nuevos modelos y de condiciones de contorno explícitas, con calibración identificada y validación independiente.
