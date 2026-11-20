# Modelo 2T multicilíndrico en línea

**Identificador científico** two-stroke-inline-reed-expansion-isooctane-v1.

## Cilindros, cárteres y fases

El modelo representa dos, tres o cuatro cilindros en línea con geometría común. Cada uno conserva un cárter estanco independiente, admisión por láminas, lumbrera de escape, transfers, masa por especie, energía y dos inventarios de mezcla en el cilindro. Se aplica la cinemática biela-manivela existente. Con desplazamiento Vd, holgura Vc₀ y volumen instantáneo Vc(θ), el volumen del cárter es Vk,min + Vd + Vc₀ − Vc(θ). Su presión se calcula de sus propios inventarios. Las láminas son cuasiestáticas, con apertura en función del salto de presión. Los transfers son reversibles y las lumbreras tienen el área que descubre el pistón. Se conservan las convenciones de la etapa 7 y las propiedades de iso-octano en fase gaseosa. [Modelo 2T](modelo-2t.md) · [Consolidación y límites del barrido](etapa7.md).

El reloj global recorre 360°. El origen es la apertura de escape del cilindro 1. Para desfase δᵢ y apertura local θe, el ángulo local es θe + ((θ − δᵢ) mod 360°). Los desfases corresponden también a las fases de los muñones cuando los cilindros tienen geometría y distribución comunes. El orden se deriva ordenando los desfases. Los ejemplos tienen intervalos de 180°, 120° y 90° y secuencias 1–2, 1–2–3 y 1–3–4–2. Se admiten otros desfases distintos dentro de una vuelta. El modelo prescribe RPM y fase, con dinámica torsional fuera del alcance.

## Red de intercambio

La admisión y el escape se conmutan por separado. Los cárteres quedan aislados físicamente en las cuatro topologías. Cada conducto conserva su orientación numérica desde el exterior hacia el cilindro.

| Lado | Independiente | Compartido |
|---|---|---|
| Admisión | Depósito y ramal por cárter, seguido de láminas | Depósito, tronco 1D y unión 0D hasta los ramales 1D y las láminas de cada cárter |
| Escape | Lumbrera, cámara segmentada de expansión y depósito por cilindro | Lumbrera, primario 1D por cilindro, unión 0D, cámara de expansión común y depósito de salida |

Configuración compartida para dos cilindros. Las flechas indican conexiones capaces de transmitir presión. El caudal por las láminas se restringe a su sentido de apertura.

~~~mermaid
flowchart TB
    R["Depósito de admisión"] --- A["Tronco 1D y unión"]
    A --- L1["Ramal y láminas 1"]
    A --- L2["Ramal y láminas 2"]
    L1 --> K1["Cárter 1"]
    L2 --> K2["Cárter 2"]
    K1 ---|Transfers| C1["Cilindro 1"]
    K2 ---|Transfers| C2["Cilindro 2"]
    C1 --- E1["Lumbrera y primario 1"]
    C2 --- E2["Lumbrera y primario 2"]
    E1 --- E["Unión de escape"]
    E2 --- E
    E --- X["Expansión común y depósito de salida"]
~~~

La cámara segmentada contiene cabecera, difusor, tramo central, cono convergente y salida. Su geometría es la del conducto después de la unión en modo compartido. Los primarios definen distancias de propagación hasta la unión. Al pasar de expansión individual a compartida se modifica la topología y deben introducirse dimensiones propias del sistema. Los ejemplos compartidos usan 57 mm de diámetro de entrada, 105 mm en el tramo central y 35 mm de salida común. Es una geometría sintética para verificación.

En todos los tubos se resuelven las ecuaciones de Euler cuasi-1D con volumen y área de caras variables, fuente axial p dA/dx y reconstrucción MUSCL limitada o primer orden. Se conserva el limitador de marcador pasivo de la etapa 7. Se usa el flujo HLLC con respaldo HLL del núcleo común. La lumbrera y las láminas se acoplan por característica y capacidad de tobera usando el mismo flujo para tubo y volumen. La unión compacta es adiabática y perfectamente mezclada, en reposo medio. El flujo integrado de cada cara actualiza por pares, con signo opuesto, masa por especie, energía total y marcador. La presión de cada unión surge de su estado y su volumen. Sobre esa presión interactúan las ondas de los diferentes ramales y siguen resolviéndose las presiones locales de cada lámina o lumbrera. [Modelo de uniones reutilizado](modelo-multicilindrico-4t.md) · [Torregrosa et al., pulsos en uniones](https://doi.org/10.3390/app7050480).

La unión transmite al soporte el desequilibrio de cantidad de movimiento direccional. Conserva energía total y permite que energía cinética entrante contribuya al inventario interno. Quedan fuera de esta formulación los ángulos de uniones T/Y, sus coeficientes de pérdidas, geometrías distribuidas dentro de la unión, paredes viscosas y estructuras 3D de mezcla. Es una aproximación de volumen pequeño frente a las longitudes de onda resueltas. Los resultados de un estudio CFD en un bicilíndrico instrumentado muestran que un modelo 1D puede sobreestimar el taponamiento y subestimar la pérdida de presión. [Zeng y Strauss, SAE 2003-32-0020](https://doi.org/10.4271/2003-32-0020).

## Termoquímica y rendimiento

En cada cárter se integra dUₖ/dt = Σṁh₀ − pₖ dVₖ/dt. En el cilindro se resta además Q̇pared de Hohenberg. U incluye energía de formación. La conversión Wiebe modifica especies, y el equilibrio UV reducido puede representar la disociación y recombinación en el tramo con lumbreras cerradas. Al abrir escape se congela la composición. Los tramos sin combustión transportan especies explícitas. La eficiencia de barrido se define como mezcla fresca atrapada dividida por masa total atrapada. La de atrapamiento usa la masa fresca suministrada por transfers más el retorno fresco desde escape. Se exportan también suministro por transfers, salida y retorno frescos por escape y masa residual. Un cambio en llenado puede acompañarse de cambios opuestos en suministro y atrapamiento.

Por cilindro, el trabajo indicado Wᵢ integra pᵢ dVᵢ + pₖᵢ dVₖᵢ. Se suman los Wᵢ y se restan pérdidas medias FMEPᵢ Vdᵢ una sola vez. P = W × RPM/60, T = W/(2π) y MEP = W dividido por cilindrada total. El par instantáneo mostrado suma (pᵢ − pₖᵢ) dVᵢ/dθ_rad. Incluye solo presión de gases. El consumo específico usa combustible neto que atraviesa las láminas y potencia estimada positiva. Se conservan límites de combustión prescrita y pérdidas empíricas.

## Integración y balance

La red completa y todos los cilindros avanzan en RK2 SSP explícito sobre el mismo vector y las mismas etapas. El paso se limita por los eventos de todos los cilindros, por CFL en tubos y por V/(ΣA · c) de las uniones. Se rechazan etapas que exceden CFL o estado termoquímico admisible. Los eventos de inicio de combustión, cierre y congelación se aplican en la fase local. El indicador de combustión activa y su carga objetivo se conservan a través del corte global.

Se exige periodicidad de 360° en dos ciclos consecutivos. Participan presión y composición a través de energías e inventarios, estado de todos los tubos y uniones, trabajo y memoria de combustión. La norma del conjunto es la máxima de sus componentes, con escalas físicas documentadas para 2T y colectores. Las curvas omiten potencia de puntos no periódicos y preservan el diagnóstico dominante. No se impone simetría de fase ni continuación entre RPM.

Los balances incluyen trabajo de cilindro y cárter, calor, inventarios, energía química y entalpía de fronteras exteriores. Las contribuciones internas se cancelan al sumar el motor. Se calculan residual de masa, de todas las especies tras contabilizar reacción y equilibrio, de elementos y de marcador fresco con su reinicio de clasificación al inicio de la combustión. La red compartida tiene un residual aparte de especies y energía que comprueba la igualdad de transferencias con los ramales.

El contraste A/B ejecuta las configuraciones por separado. Forma pares de espaciamientos comparables y aplica ×1, ×2 y ×4 a cada malla con reducciones del paso angular y de la tolerancia periódica. Su banda empírica sigue el criterio de la etapa 5. Para estudios se reutilizan ejes mecánicos 2T, remallado por longitud, malla factorial, objetivos y clasificación. Se deduplican configuraciones y el paralelismo se aplica solamente a motores independientes. Los volúmenes de las uniones y la geometría de primarios se editan mediante A/B. [Estudios y resolución](etapa5.md).

## Evidencia experimental

El dominio de verificación comprende los ejemplos de 124,8 cm³ por cilindro, 4000 y 5000 RPM y geometría de colector compacta. El trabajo comercial de un bicilíndrico con escape plegado comparó presión medida y estimaciones de oxígeno atrapado. El trabajo de Cartwright y Fleck ensayó secciones modulares con dinamómetro y sensores en admisión, cárter, cilindro y escape. El ensayo de barrido de Fleck y Thornhill usó barrido externo de un ciclo. Los datos accesibles consultados carecen de una combinación reproducible de geometría, contornos y trazas sincronizadas que permita identificar y validar de forma independiente el ejemplo de Dino2. [Zeng et al., SAE 2004-32-0039](https://doi.org/10.4271/2004-32-0039) · [Cartwright y Fleck, SAE 942515](https://doi.org/10.4271/942515) · [Fleck y Thornhill, SAE 941684](https://doi.org/10.4271/941684).

La verificación de balance, convergencia y sensibilidad corresponde al modelo numérico. Una campaña experimental requiere, por topología, dimensiones efectivas, sincronización de lumbreras, parámetros de láminas, presiones y temperaturas por ángulo, gases atrapados, combustible, consumo y par. Se requieren RPM y geometrías reservadas para contrastar predicciones con parámetros ya fijados. [Resultados y reproducción](etapa10.md).
