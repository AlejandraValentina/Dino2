# Modelo termoquímico 4T — etapa 2

Identificador: `four-stroke-isooctane-frozen-v2`. El modelo `four-stroke-0d-air-v1` de pseudoaire y el Otto ideal se conservan como referencias independientes. Se reutilizan geometría, perfiles de válvulas, correlaciones de pérdidas e integrador; se reemplazan propiedades, balances de especies y tratamiento de energía química. No se reinterpretan silenciosamente archivos anteriores.

## Dominio físico implementado

Un cilindro homogéneo 0D, mezcla previamente vaporizada de **iso-octano puro** y aire seco 21% O₂/79% N₂ molar. No es una gasolina comercial ni un modelo de inyección/evaporación. Se transportan cinco especies: C₈H₁₈, O₂, N₂, CO₂ y H₂O gaseosos. Las propiedades y R dependen de temperatura y composición. La reacción global usa productos completos y congelados; no resuelve CO, H₂, radicales, disociación ni cinética de autoignición.

Se admiten mezclas frescas de equivalencia 0,8≤φ≤1. La restricción evita representar mezclas ricas mediante productos completos incorrectos. Una conversión menor que uno deja combustible y oxígeno sin reaccionar; no inventa una pérdida de energía sin destino. Especies y energía remanentes permanecen en el cilindro o salen con los flujos. Los gases residuales no se reemplazan por aire al iniciar un ciclo.

El depósito de admisión tiene composición fresca definida por AFR. El depósito de escape tiene composición de productos completos de esa mezcla y p/T prescritas. Es una condición de contorno infinita, no un colector que almacene composición y ondas. El flujo reverso transporta la composición y entalpía del donante. El alcance de otras hipótesis geométricas y de intercambio se mantiene como en [modelo-4t.md](modelo-4t.md).

## Datos termodinámicos y referencia energética

Polinomios NASA7 de [Cantera 3.2.0, `nasa_gas.yaml`](https://raw.githubusercontent.com/Cantera/cantera/v3.2.0/data/nasa_gas.yaml): iso-octano X4/85, N₂ TPIS78, O₂ TPIS89, CO₂ L7/88, H₂O L8/89; 200–6000 K. Fuente original: [NASA TM-4513](https://ntrs.nasa.gov/citations/19940013151). Las [ecuaciones NASA7](https://cantera.org/stable/reference/thermo/species-thermo.html) se implementan en C# sin depender de Cantera en la aplicación.

```text
cp_i(T)/R_i = a1 + a2 T + a3 T² + a4 T³ + a5 T⁴
h_i(T) = hf_i(298,15 K) + ∫298,15..T cp_i(T) dT
u_i(T) = h_i(T) − R_i T
R_i = Ru/M_i; Ru = 8,31446261815324 J/(mol K)
U = Σ m_i u_i(T); Rmezcla = Σ m_i R_i / Σ m_i
p V = T Σ m_i R_i
```

La entalpía de formación a 298,15 K procede del polinomio publicado, incluyendo a6. Se integra cp de forma continua en 1000 K; esto elimina los pequeños saltos de constantes entre ajustes independientes. La diferencia frente a las entalpías de Cantera sin esa continuidad se cuantifica en el [informe](informe-etapa2.md), no se considera una nueva calibración.

Masa atómica en kg/mol: C=0,012011, H=0,001008, O=0,015999, N=0,014007. Todas las masas moleculares y relaciones estequiométricas se derivan de ellas. Se invierte U(T, composición) por Newton, verificando positividad y rango. Un estado irresoluble reduce el paso y luego falla explícitamente; no se reemplaza por una temperatura plausible. Se tolera únicamente redondeo de especies de hasta 1e−13 kg, que se comprueba en pruebas y no se elimina del balance.

## Estequiometría, masa y elementos

```text
C8H18 + 12,5 O2 → 8 CO2 + 9 H2O
νmasa = [−1, −12,5 MO2/Mfuel, 0, 8 MCO2/Mfuel, 9 MH2O/Mfuel]
AFResteq = 12,5 (MO2 + 79/21 MN2) / Mfuel
φ = AFResteq / AFR
dm_i/dt = Σj mdot_j Ydonante,j,i + νmasa_i · dξfuel/dt
```

El vector de reacción conserva exactamente masa y C/H/O/N en aritmética exacta. El transporte no utiliza una composición de aire para gases quemados. Se integran cinco flujos de especies para auditar cada ecuación y los cuatro balances elementales.

La masa de combustible objetivo de cada combustión es la conversión especificada multiplicada por el mínimo entre combustible disponible y su equivalente de oxígeno. Incluye el combustible residual presente, con su energía química. El resto no desaparece. El marcador de aire fresco se renueva al abrir admisión solamente para calcular llenado/residuo; **no borra especies físicas ni energía**.

## Energía química y entalpía de flujo

```text
dUtotal/dt = Σj mdot_j hdonante,j − Qdot_pared − p dV/dt
PCIgas = −ΔHreacción(298,15 K) / Mfuel     (H2O vapor)
Econversión_diagnóstico = ξfuel · PCIgas
```

**No se añade `ξfuel·PCI` como fuente térmica a Utotal:** Utotal y las entalpías ya incluyen formación. La temperatura aumenta al convertirse reactivos en productos con distinta energía de formación a U compatible con trabajo, pared y flujo. Añadir además el PCI contaría dos veces la energía química.

`heat_released_J` se conserva como nombre de salida común, pero en v2 es un diagnóstico de conversión en base a **entalpía de reacción a 298,15 K**. No es un aporte externo de calor ni presupone calor de reacción a volumen constante. El cambio de número de moles y su contribución RT se contabilizan mediante u=h−RT. El CSV explica la base energética.

El PCI no es un parámetro independiente del combustible. La UI lo muestra como calculado y el núcleo rechaza un PCI incompatible. La eficiencia usa combustible neto que cruza la admisión por ciclo × PCI; conserva el combustible sin quemar que cruza el escape como flujo químico. Esto difiere de la aproximación de carga encerrada usada por v1 y debe considerarse al comparar eficiencias.

## Combustión: dos leyes explícitamente prescritas

**Wiebe prescrita:** misma fase temporal/forma del incremento anterior, aplicada ahora a conversión de especies y no como calor externo. Es la referencia para aislar el cambio de propiedades.

**Wiebe escalada por condiciones:** duración de referencia modificada con temperatura, presión y residuo del estado homogéneo no quemado al inicio de combustión, equivalencia fresca, escala de cámara y una aproximación de turbulencia. Se evalúa antes de liberar calor; no se usa la temperatura media de gas ardiendo como si fuese temperatura de gas no quemado.

[Metghalchi y Keck, 1982](https://doi.org/10.1016/0010-2180%2882%2990127-4), ecuaciones 16–18, tabla 3A, iso-octano:

```text
SL0 = 0,2632 − 0,8472 (φ − 1,13)²                 [m/s]
α = 2,18 − 0,8(φ−1); β = −0,16 + 0,22(φ−1)
SL = SL0 (Tu/298 K)^α (pu/101325 Pa)^β (1−2,1 fres)
```

Rango experimental publicado: φ=0,8–1,5, T=298–700 K, p=0,4–50 atm; ensayos de dilución f=0–0,2 para mezcla estequiométrica. El código restringe φ fresca a 0,8–1 para su química y no admite f>0,2 en la ley escalada. Evalúa la correlación fuera del rango térmico/de presión **con un diagnóstico visible de extrapolación**; también marca que aplicar la dilución ajustada a mezcla pobre es una extrapolación. No transfiere el error de ajuste de esa correlación a una supuesta precisión del motor.

La duración se escala mediante:

```text
Up = 2 S RPM / 60
Seff = SL + Ct Up
τ = τref · (B/Bref) · (SLref + Ct Upref)/(SLsoc + Ct Up)
Δθcomb = 6 RPM τ
SOC = 360° − avance + 6 RPM · retraso_ignición
```

La suma de una escala laminar y una de turbulencia es una aproximación fenomenológica, no una llama resuelta. τref y Ct requieren identificación del motor; los valores ilustrativos 2 ms y 0,3 **son estimados**, no ajustados en esta entrega. No se resuelven área de frente, kernel, entrainment, extinción ni dinámica de turbulencia. La escala B es una aproximación de longitud relativa de cámara. El retraso de ignición sigue prescrito; no se predice por química.

Se mantiene la Wiebe normalizada de v1 sobre [SOC, SOC+Δθ]. CA10/50/90 se calculan a partir de esa ley. La combustión debe terminar antes de apertura de escape; no se trunca para forzar una curva. El avance influye tanto en fase como en las condiciones usadas para la duración escalada. Compresión/llenado/residuos también pueden afectar esa duración, pero los resultados siguen dependiendo de la calibración pendiente.

La [fuente primaria completa](https://james-keck-memorial-collection.unibs.it/JCKeck-papers/MetghalchiKeck-CombustionFlame-48-191-1982.pdf) permite distinguir los coeficientes de iso-octano de los de una gasolina comercial. Se usa la reducción lineal 1−2,1f publicada en ese trabajo, no una versión diferente tomada sin identificar de otra correlación.

## Integración, eventos y periodicidad

Se comparte RK4 con control por duplicación del paso. El nuevo controlador conserva por ciclo cinco masas, Utotal y el marcador de aire fresco; exige repetición de todas ellas y del trabajo durante dos ciclos consecutivos. La duración condicionada se determina en SOC y agrega dinámicamente su evento de finalización.

Para evitar masas negativas por sobreintegrar una fuente de reacción que puede ser exactamente completa, la extensión química dentro del intervalo cerrado se evalúa analíticamente con xb(θ). Los estados integrados de especies contienen el transporte; las masas físicas suman ν·ξ durante cada etapa RK. Al terminar la combustión se incorpora exactamente esa conversión al estado. La integración térmica continúa resolviendo trabajo y pared con la composición física de cada etapa. No es una corrección artificial posterior de masa ni un término energético añadido.

Las cuadraturas independientes verifican `Δmi − flujo_i − νi ξ`, elementos y `ΔUtotal − Hflujo + Qpared + W`. El cierre interno se acompaña de refinamientos y una segunda integración de trazas con propiedades de Cantera; no se presenta el cierre algebraico como error numérico independiente.

## Pérdidas y evidencia

Se revisaron las unidades de Hohenberg (V en m³, p en bar, T en K, Up en m/s, h en W/(m² K)) y Chen–Flynn (A en Pa, B adimensional, C en Pa/(m/s), D en Pa/(m/s)²). Las pruebas originales se conservan. No se modificaron coeficientes para conseguir una forma de curva. La ambigüedad dimensional impresa del término cuadrático en la fuente de fricción sigue identificada; no hay evidencia para convertir una interpretación de literatura en un coeficiente medido del motor.

Combustión, transferencia térmica y fricción tienen registros de procedencia: `Estimated` o `Calibrated`, fuente, identificador de datos y rango. Declarar `Calibrated` requiere completar el identificador, pero es **una declaración del usuario**, no una verificación automática del experimento ni validación independiente. Modificar coeficientes de esos grupos en la UI invalida su estado de calibración declarado. Los valores del ejemplo son todos estimados.

## Disociación y límite predictivo

La evaluación independiente UV con Cantera muestra diferencias materiales entre productos congelados y equilibrados en el dominio caliente utilizado. El informe cuantifica esta diferencia; **el solver de producción sigue siendo congelado**. No aplica factores de temperatura, presión o potencia para imitar el equilibrio. Su precisión predictiva no se considera establecida y los picos/calores a alta temperatura requieren un modelo posterior de productos/equilibrio o química reducida, junto con evidencia experimental.

Los resultados permiten estudiar mecanismos y sensibilidades dentro de estas hipótesis. No establecen una ganancia mecánica demostrada, no certifican MBT ni detonación y no representan mezclas ricas, condensación, carburantes comerciales o longitudes de colectores.
