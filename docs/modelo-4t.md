# Modelo 4T `four-stroke-0d-air-v1`

## Alcance y estado científico

Motor alternativo monocilíndrico 4T de encendido provocado, cilindro homogéneo 0D conectado a depósitos infinitos por válvulas. Resuelve masa y energía durante 720° con calor finito, paredes y pérdidas mecánicas. Produce estimaciones indicadas y al cigüeñal que varían con RPM por tiempos de flujo, combustión y transferencia térmica. El Otto ideal sigue en archivos/clases independientes y conserva sus ecuaciones y formato.

**Es una predicción de un modelo reducido, sin calibración ni validación experimental de Dino2.** No predice química, velocidad de llama, detonación, disociación, ondas en tuberías, fugas, elasticidad, accionamiento de válvulas, emisiones ni límites mecánicos. La forma de la curva resulta del cálculo, pero no prueba su exactitud para un motor real. El ejemplo es sintético.

## Contratos, unidades y referencias angulares

Todos los datos del núcleo y JSON usan SI: m, kg, s, Pa absolutos, K, J; excepciones explícitas: ángulos en grados y régimen en rev/min. 0° = PMS de intercambio, 180° = PMI de admisión, 360° = PMS de combustión, 540° = PMI de expansión, 720° = siguiente PMS de intercambio. Admisión puede abrir antes de 0°; cierre de escape puede superar 720°. Las funciones son periódicas.

El proyecto 4T guarda `current` y `original` (opcional, explícitamente nulo cuando no existe). No se convierten archivos Otto a motores 4T. Cada cambio de ecuaciones que modifique su significado necesita otra versión de modelo. El CSV conserva entradas completas, versión, resultados, diagnósticos y unidades. Tiempos de cálculo son mediciones, no resultados deterministas del motor.

## Cinemática exacta del mecanismo centrado

Con radio r=S/2, biela l, área de pistón Ap=πB²/4:

```text
x(θ) = r (1 − cos θ) + l − sqrt(l² − r² sin² θ)
Vd = Ap S; Vc = Vd / (rc − 1); V(θ) = Vc + Ap x(θ)
dV/dθrad = Ap [r sin θ + r² sin θ cos θ / sqrt(l² − r² sin² θ)]
dV/dθdeg = dV/dθrad · π/180
Awall = 2 Ap + π B (Vc/Ap + x)
```

Cabeza y pistón planos, cámara equivalente cilíndrica y mecanismo sin descentrado. El área mojada no resuelve detalles de cámara, válvulas ni culata.

## Termodinámica de pseudoaire con cp(T)

Composición fija 79% N₂ + 21% O₂ molar. R=Ru/(0,79·0,0280134 + 0,21·0,0319988), en J/(kg K). Se mezclan coeficientes NASA7 de cp/R por fracción molar. Se integra cp para h sensible y se usa u=h−RT. La integración es continua en 1000 K; el origen de energía sensible es arbitrario pero consistente en cilindro y flujos. Se invierte u(T) por Newton y se comprueba el intervalo de propiedades 200–6000 K. No se extrapola silenciosamente fuera de ese intervalo.

Fuente de datos: [Cantera 3.2.0, `nasa_gas.yaml`, N₂ TPIS78 y O₂ TPIS89](https://raw.githubusercontent.com/Cantera/cantera/v3.2.0/data/nasa_gas.yaml), basado en [NASA TM-4513, McBride et al., 1993](https://ntrs.nasa.gov/citations/19940013151). Fórmulas del polinomio: [documentación termodinámica de Cantera](https://cantera.org/stable/reference/thermo/species-thermo.html). Se toman datos termodinámicos; no se incorpora Cantera como dependencia ni se copia su solver.

**Limitación principal:** combustible y productos mantienen las propiedades del pseudoaire. La masa de combustible está incluida en el flujo de mezcla, pero no se siguen especies ni cambia R con la reacción. La cp variable mejora el tratamiento energético del gas frente a gamma constante; no sustituye las propiedades de productos ni un equilibrio químico. Picos de varios miles de K, particularmente >3000 K, necesitan cautela adicional. El programa los muestra y no los interpreta como límites admisibles.

## Válvulas y flujo compresible

Perfil idealizado de alzada: `L=Lmax sin²(π·fase/duración)` entre apertura y cierre; cero fuera. El área efectiva es `Cd · Nvalv · min(π d L, π d²/4)`. Es área de cortina con límite de garganta; Cd constante representa pérdidas geométricas no resueltas. No es una leva medida ni una predicción de separación de flujo.

Se identifica el lado de mayor presión como depósito aguas arriba, con estado de remanso igual al estado 0D de ese lado. El caudal puede cambiar de signo; se usa T, h y gamma del donante. En cada evaluación se aproxima gamma como constante local del gas aguas arriba:

```text
β = pdown / pup; βcrit = (2/(γ+1))^(γ/(γ−1))
mdot = Aeff pup / sqrt(R Tup) · F(β)
Fsub = sqrt[2γ/(γ−1) · (β^(2/γ) − β^((γ+1)/γ))]
Fchoked = sqrt(γ) · (2/(γ+1))^((γ+1)/(2(γ−1)))
```

Se aplica Fchoked cuando β≤βcrit. Fuente de conservación y límite sónico: [NASA Glenn, Mass Flow Choking](https://www.grc.nasa.gov/www/k-12/airplane/mflchk.html). La hipótesis de gamma local constante en la tobera es una aproximación frente a integrar una expansión con cp variable. Los depósitos tienen p y T prescritas; no almacenan ondas ni el gas expulsado por reflujo. El modelo no predice resonancias ni ganancias por longitud de colectores.

## Balances del cilindro

Estado integrado: masa m, energía interna U=m·u(T), marcador de mezcla fresca mf y cuadraturas. Los caudales de admisión/escape son positivos hacia el cilindro:

```text
dm/dt = mdot_intake + mdot_exhaust
dU/dt = Qdot_comb − Qdot_wall − p dV/dt + Σ(mdot_j h_donor,j)
p = m R T / V
dmf/dt = max(mdot_intake,0)
          + [min(mdot_intake,0)+min(mdot_exhaust,0)] · mf/m
```

Una entrada por escape se considera gas residual, no mezcla fresca. El marcador se renueva a cero al abrir admisión, sin cambiar m ni U; etiqueta la nueva carga. Al cerrar admisión se obtiene `m_fuel=mf/(AFR+1)` y `m_air_fresh=AFR·m_fuel`. La masa de combustible no se añade otra vez. El residuo de ciclos previos permanece en m y U; se supone químicamente inerte. No hay conservación de especies de oxígeno ni predicción de extinción por dilución o mezclas pobres/ricas.

La energía se obtiene por calor prescrito del combustible; no se modela una reacción que determine su liberación. AFR y eficiencia de liberación deben entenderse dentro de esa limitación. No se calcula evaporación ni temperatura de combustible separada.

## Combustión y encendido

Inicio SOC = `360 − avance + 6·RPM·retraso_s`. Duración angular = `6·RPM·duración_s`. Se prescribe una duración temporal constante como entrada, **sin afirmar que la turbulencia real mantenga esa duración al cambiar RPM**. Así se representan tiempo finito y desplazamiento del calor respecto al pistón, no una cinética predictiva de ignición.

Para fase z=(θ−SOC)/duración, se integra una Wiebe normalizada:

```text
xb(z) = [1 − exp(−a z^(n+1))] / [1 − exp(−a)], a=6,908
dQcomb/dθ = m_fuel PCI η_release · dxb/dθ
```

Se limita xb a 0 antes y 1 después del intervalo, con derivada interior. La normalización asegura liberar exactamente la fracción de energía especificada en la duración finita. La implementación separa los eventos para no sumar calor fuera del intervalo. La combustión debe estar íntegramente entre cierre de admisión y apertura de escape para todas las RPM solicitadas; se rechaza la configuración si no se cumple. Referencia sobre uso y limitaciones de la ley prescrita: [informe técnico NETL, sección Heat release rate](https://www.netl.doe.gov/sites/default/files/2020-05/DICE-CRCC-Pre-FEED-Final-Report_1.pdf) y [formulación publicada de Wiebe](https://www.mdpi.com/1996-1073/15/19/7263). Los parámetros no están calibrados para el caso de Dino2.

## Transferencia térmica

Hohenberg: `h = 130 V^(-0,06) p_bar^(0,8) T^(-0,4) (Up+1,4)^(0,8)` W/(m² K); Up=2S·RPM/60. `Qdot_wall=mult·h·Awall·(T−Twall)`, positivo hacia la pared; puede ser negativo cuando la pared calienta el gas. La pared es uniforme y su temperatura prescrita. Multiplicador 1 en el ejemplo; 0 permite ensayos adiabáticos.

Referencia original: [Hohenberg, SAE 790825, 1979](https://www.sae.org/publications/technical-papers/content/790825/); forma operativa y unidades reproducidas en [Xarin/catool](https://www.xarin.co.uk/cylinder-pressure-analysis/heat-transfer-coefficient.html). Es una correlación empírica transferida de otros motores, no una calibración del motor simulado. No se incluyen radiación ni dinámica térmica del metal.

## Trabajo, bombeo y fricción

```text
Wi_net = ∮720° p dV
Wpumping = ∫0..180° p dV + ∫540..720° p dV  (con signo)
Wi_gross = Wi_net − Wpumping
FMEP = A + B pmax + C Up + D Up²
Wfriction = FMEP Vd; Wbrake = Wi_net − Wfriction
Pi = Wi_net RPM/120; Pb = Wbrake RPM/120
Ti = Wi_net/(4π); Tb = Wbrake/(4π)
BMEP = Wbrake/Vd
VE = m_air_fresh / [ρ_intake Vd]
ηi = Wi_net/(m_fuel PCI); ηb = Wbrake/(m_fuel PCI)
```

El bombeo está incluido en el trabajo neto: no se resta dos veces. La partición bruto/bombeo se define por carreras geométricas, aunque las válvulas abran o cierren fuera de los PMI/PMS. No se recortan potencias negativas. Si la energía de combustible es cero, las eficiencias son indefinidas y el CSV indica `NA`.

Fricción: forma Chen–Flynn, discutida en [Papagiannakis y Lytras, 2025, ecuación 7](https://www.mdpi.com/1996-1073/18/13/3440). Los valores de ejemplo se transfieren de [Kitanović et al., MVM2012-026, página impresa 153, ecuación 13](https://motori.mas.bg.ac.rs/wp-content/uploads/2023/01/MVM-2012-Kitanovic.pdf): A=0,5 bar, B=0,006, CCF=600 y QCF=0,2, aplicados allí a S·n/2 con n en RPM. Como S·n/2=15·Up, se usan A=50000 Pa, C=9000 Pa/(m/s), D=45 Pa/(m/s)². **La unidad impresa de QCF en esa fuente es dimensionalmente inconsistente con su término cuadrático; se interpreta según la ecuación.** Es una incertidumbre de la transferencia, no un coeficiente validado. Todos los coeficientes son editables y deben reemplazarse por datos de fricción del motor objetivo antes de confiar en Pb. No se añaden accesorios separados; el alcance de cualquier ajuste de FMEP debe identificarlos para evitar doble conteo.

## Método numérico y fallos explícitos

El integrador RK4 está separado de las ecuaciones físicas. Se trabaja en grados con dt/dθ=1/(6·RPM). Un paso completo y dos medios pasos proporcionan estimación de error `/15`; se acepta la solución de dos medios pasos, se adapta el paso y se impone un máximo angular. No hay asignaciones de arrays por evaluación numérica. Los pasos se cortan en eventos de válvulas, renovación/captura de carga, combustión y fronteras de la partición de trabajo.

Cada ciclo conserva m/U/mf y reinicia solo cuadraturas. Se arranca a PMS de intercambio con p/T del escape y gas residual. Se exige repetición de m, U, mf y trabajo por ciclo durante dos ciclos consecutivos, como mínimo desde el tercero. La inicialización no se presenta como ciclo estabilizado. Se rechaza un punto si no alcanza periodicidad en el máximo permitido; no se rellenan RPM con extrapolaciones ni se publica una curva incompleta.

El error local usa `|y_half−y_full|/[15·tol·(s_i+max(|y_start|,|y_half|))]`, con escalas de 1e−5 kg para masas y 10 J para energías/trabajos. El residual periódico usa diferencias normalizadas por estas escalas y el estado final. Son tolerancias numéricas del solver, no incertidumbres experimentales. El caso por defecto usa máximo 0,5°, tolerancia local 2e−7, periódica 1e−6 y hasta 60 ciclos. También se limita el número total de pasos y el paso mínimo; estados no positivos o propiedades fuera de rango provocan reducción de paso y fallo explícito si persisten, sin recortar artificialmente p, T ni m.

Se integran por separado flujos de masa, entalpía, calor y trabajo para diagnosticar el balance de cada ciclo, incluyendo el pequeño cambio de estado restante. El cierre de estas cuadraturas no demuestra por sí solo exactitud; se acompaña de referencias analíticas, refinamientos y una integración independiente de las trazas.

## Dominio de uso y evidencia

Los límites de entrada del software se comprueban en `FourStrokeConfiguration.Validate`; no son un mapa de operación validado. Se permite 500–10000 RPM y hasta 100 puntos por barrido; el usuario debe restringir el rango al motor y a las hipótesis aplicables. Alzada, distribución, compresión, avance y contornos afectan físicamente la solución dentro del modelo, pero sus ganancias reales necesitan contrastación experimental. Consultar el [informe técnico](informe-4t.md) para resultados, error de discretización y rendimiento medidos.
