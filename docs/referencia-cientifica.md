# Referencia científica

Esta guía indexa las ecuaciones y las pruebas por modelo. La interfaz usa etiquetas de unidades y distingue entradas, resultados del balance, pérdidas estimadas y resolución numérica. Las declaraciones de calibración requieren procedencia y mediciones. El caso sintético y las regresiones verifican el cálculo. La evaluación experimental requiere ensayos independientes. [Datos 4T](validacion-experimental.md) · [Datos 2T](datos-experimentales-2t.md).

## Modelos y trazabilidad

| Familia y opción | Ecuaciones y dominio | Implementación y pruebas |
|---|---|---|
| Otto ideal, aire estándar v1 | Gas ideal con cp/cv constante, compresión y expansión isentrópicas y calor a volumen constante. Par indicado ideal constante, un ciclo cada dos revoluciones. [Derivación](modelo-otto.md). | **Dino2.Core/OttoCycle.cs**. **Dino2.Tests**, contraste analítico y balances. |
| 4T pseudoaire v1 | Cilindro 0D abierto con propiedades de aire dependientes de temperatura, válvulas efectivas compresibles y combustión Wiebe prescrita. [Ecuaciones, Hohenberg y pérdidas](modelo-4t.md). | **Dino2.Core/FourStroke**. **Dino2.FourStroke.Tests** comprueba geometría, válvulas, masas y periodicidad. |
| 4T termoquímico congelado v2 | Conserva masa por especie y energía con entalpías de formación. PCI derivado para iso-octano, con conversión prescrita o duración Wiebe escalada. [Especies, propiedades NASA y alcance](modelo-termoquimico.md). | **Dino2.Core/Thermochemistry**. **Dino2.Reactive.Tests** y referencia de Cantera de etapa 2. |
| 4T equilibrio reducido v3 | Equilibrio UV CO₂/CO y H₂O/H₂ durante combustión y expansión cerrada. Productos congelados desde la apertura de escape. [Disociación y Cantera](informe-etapa3.md). | **ProductEquilibrium** y **ReactiveSimulator**. Pruebas UV, energía, elementos y refinamiento en **Dino2.Reactive.Tests**. |
| 4T con conductos v4 y v4 MUSCL | Euler cuasi-1D de sección variable. HLLC/HLL, primer orden o reconstrucción MUSCL limitada y acoplamiento con tobera y característica. [Conservación y ondas](informe-etapa4.md) · [Verificación MUSCL y refinamiento](cierre-etapa4.md). | **Dino2.Core/GasDynamics**. Pruebas de Sod, reflexión, conservación, presión y variantes en **Dino2.FourStroke.Tests** y **Dino2.Reactive.Tests**. |
| 4T en línea con colectores | Cilindros reactivos con fases propias. Ramales y troncos 1D acoplados con dos volúmenes mezclados. [Modelo, dominio y ecuaciones](modelo-multicilindrico-4t.md). | **Dino2.Core/Multicylinder** y **Dino2.Multicylinder.Tests**, balances, ondas, periodicidad global, límite aislado y refinamiento de A/B. |
| 2T con láminas y expansión | Dos volúmenes 0D, transfers reversibles, láminas cuasiestáticas, escape segmentado y dos inventarios de procedencia. Combustión de iso-octano con equilibrio reducido opcional y congelación en la apertura. [Ecuaciones](modelo-2t.md) · [Correcciones y dominio periódico](etapa7.md). | **Dino2.Core/TwoStroke**. **Dino2.TwoStroke.Tests** comprueba geometría, ondas, especies, marcador, energía, convergencia y variantes. |
| Estudios de ambas familias | Objetivos sobre regímenes muestreados y contraste empírico en tres resoluciones. La banda depende de los cambios observados. [Método 4T](etapa5.md) · [Ejemplo 2T](etapa7.md). | **Dino2.Core/Studies** y **TwoStrokeStudy**. **Dino2.Studies.Tests** y **Dino2.TwoStroke.Tests**. |

## Magnitudes que usa Desktop

- **Potencia indicada neta y par indicado.** Se obtienen del trabajo indicado del ciclo. En 4T hay un ciclo por dos vueltas. En 2T hay un ciclo por vuelta y se suma el trabajo del cárter al del cilindro. **Potencia y par estimados al cigüeñal** restan pérdidas mecánicas modeladas.
- **IMEP y BMEP.** Trabajo indicado neto o estimado al cigüeñal dividido por cilindrada. Las tablas pueden convertir Pa a bar. **Eficiencias térmicas** usan el aporte energético modelado o el combustible admitido, según la formulación.
- **4T, eficiencia volumétrica.** Aire fresco retenido al cierre de admisión respecto a la referencia del depósito. Los conductos preservan el transporte del marcador.
- **2T, relación de entrega.** Mezcla fresca bruta suministrada por transfers dividida por masa de referencia. **Atrapamiento** conserva el denominador entrega bruta más retorno fresco del escape. **Atrapada por entrega** usa únicamente la entrega bruta por transfers. **Barrido** compara fresca atrapada con la masa total al cierre. **Carga** compara fresca atrapada con masa de referencia. **Cortocircuito neto** descuenta lo que volvió desde el escape.
- **Temperatura y presión.** K y presión absoluta. Ángulo del cigüeñal en grados. En 4T, 0° es PMS de intercambio y 360° PMS de combustión. En 2T la traza empieza en apertura de escape y conserva el ángulo hasta la apertura siguiente.
- **Convergencia.** Un estado periódico satisface el criterio del modelo en la misma fase del ciclo. Resolución y balances complementan ese criterio. Las mallas comparables de A y B controlan una fuente de variación. Una diferencia pequeña sigue requiriendo evidencia de resolubilidad.

## Límites y verificación

Wiebe prescribe fase y conversión. Hohenberg y FMEP son cierres empíricos. En el 2T, el movimiento de láminas es cuasiestático, el cárter es adiabático y el barrido reduce la estratificación a dos inventarios. La dinámica 1D representa propagación y reflexión, con contornos y uniones aproximados. El barrido ampliado 2T se verificó para un ejemplo de 125 cm³ a 20 regímenes discretos entre 500 y 10 000 RPM. El informe muestra la sensibilidad al refinamiento y a la hipótesis de barrido. [Etapa 7](etapa7.md).
