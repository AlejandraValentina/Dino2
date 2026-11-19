# Calibración y validación — protocolo y datos revisados

Revisión realizada el 19 de noviembre de 2026. **No se declara validación experimental de Dino2 ni calibración experimental realizada en esta etapa.** El contraste con Cantera y los casos analíticos verifican implementaciones numéricas. Reproducir los coeficientes ajustados de una publicación tampoco equivale a validar un motor independiente.

## Conjuntos investigados

| Fuente primaria | Información encontrada / acceso | Decisión para esta entrega |
|---|---|---|
| [TCC-III, University of Michigan](https://volker-sick.engin.umich.edu/home/variability-phenomena), DOI [fired full view](https://doi.org/10.7302/Z2H41PCD) | Se anuncian geometría, presión, imágenes y condiciones; combustibles metano y propano. Deep Blue respondió HTTP 403 desde este entorno. | No se descargaron trazas. Además, requeriría representar el combustible correspondiente; no se asigna iso-octano a esas mediciones. |
| [ETRF surrogate gasoline fuels test bench data, Zenodo 21408734](https://zenodo.org/records/21408734) | ZIP accesible y descargado. Se inspeccionó un MAT v7.3: canales de RPM, lambda, CA50, encendido ECU, IMEP, aire, combustible, presión media de admisión y temperatura de escape. Son mezclas de carburantes, con calibraciones ECU base/óptimas. | El archivo inspeccionado contiene valores agregados; no suministra una traza p–θ, geometría ni alzadas/Cd suficientes para reproducir este modelo. No se inventaron esos datos ni se consideró que una calibración ECU fuese calibración de Dino2. |
| [Cruccolini, Mendeley 6tjbcd9csy.1](https://data.mendeley.com/datasets/6tjbcd9csy/1) | Se anuncian CSV de presión de 101 combustiones y promedio, dos puntos CH₄/aire λ=1,4, chispa convencional y corona. | Combustible/mezcla fuera del modelo de iso-octano implementado; la corona tampoco está representada. Candidato para otro dominio, no validación de v2. |
| [Yang et al., Mendeley vp596b2kdn.1](https://data.mendeley.com/datasets/vp596b2kdn/1) | Presión, desempeño y emisiones de motor dual metanol/gas natural. | Necesita termodinámica y combustión de esos combustibles. No se sustituyeron por iso-octano para generar un error experimental aparente. |
| [Datos CFD de Darmstadt, Data in Brief](https://pmc.ncbi.nlm.nih.gov/articles/PMC9189782/) | Geometría, válvulas y presiones tabuladas accesibles. La descripción identifica expresamente la tabla 2 como datos simulados de un primer ciclo usados como contorno. | No se presentan esas presiones como mediciones experimentales independientes. |
| [Metghalchi y Keck 1982](https://doi.org/10.1016/0010-2180%2882%2990127-4) | Datos experimentales y coeficientes de velocidad laminar de iso-octano; se revisaron ecuaciones y tablas de la fuente primaria. | Fundamenta una correlación. Comparar la correlación con los mismos ajustes publicados es reproducción de su ajuste, no validación independiente de Dino2 ni de la duración turbulenta en un motor. |

El ZIP ETRF se inspeccionó como investigación, sin redistribuir los archivos originales. La existencia de datos abiertos no implica que contengan todas las condiciones de un ensayo reproducible para este modelo. El estado de una fuente y su licencia deben revisarse al incorporar una futura copia de datos.

## Datos mínimos de un caso elegible

- Identificador, versión y fuente de datos; combustible y composición, fase, humedad, lambda/AFR y su incertidumbre.
- Diámetro, carrera, biela, cámara/compresión y referencia de ángulo de PMS; definición del volumen cuando existan cavidades especiales.
- Cantidad, diámetro y alzada frente a ángulo de válvulas; distribución y coeficientes de descarga o información para identificarlos.
- RPM, encendido y su referencia, presión/temperatura/composición de admisión y escape; identificación de ubicación de sensores y estabilidad de los contornos.
- Temperatura de metal o una estimación explícita con incertidumbre; temperatura de refrigerante no debe tratarse sin más como pared uniforme medida.
- Presión absoluta del cilindro frente a ángulo, resolución, referencia/pegging, alineación de PMS, ciclos y variabilidad; intervalos de datos ausentes o filtrados.
- Si se comparará potencia al cigüeñal: par medido, accesorios incluidos, combustible y aire medidos y condiciones del banco. Una presión de cilindro sola no identifica fricción y accesorios.

Si falta un dato, debe estimarse como tal y estudiarse su incertidumbre o excluirse el caso del objetivo cuantitativo que lo necesita. No se convierte un parámetro desconocido en un valor experimental por tomarlo del ejemplo.

## Separación de identificación y evaluación independiente

1. **Congelar el protocolo y las particiones antes de ajustar:** separar puntos completos de RPM/carga/avance y campañas o réplicas. No repartir aleatoriamente muestras angulares de una misma combustión entre entrenamiento y validación; eso filtra información del mismo ciclo.
2. **Verificar el solver sin ajustar el motor:** masa, elementos, energía química, casos cerrados, propiedades, refinamientos y periodicidad. La evidencia de esta entrega está en [informe-etapa2.md](informe-etapa2.md).
3. **Identificar intercambio/pérdidas por etapas cuando haya datos:** arrastre y caudales para Cd/contornos; calor o presión con temperaturas conocidas para térmica; arrastre o diferencia indicada/freno con accesorios definidos para FMEP. No usar combustión para compensar una admisión o fricción incorrecta.
4. **Calibrar combustión solo con los casos reservados para ello:** duración de referencia, coeficiente de turbulencia y forma Wiebe; documentar método, valores previos/posteriores, incertidumbre y rango. CA50 o una curva de calor inferida de presión comparten hipótesis con el modelo y no son mediciones independientes de todas sus variables.
5. **Congelar parámetros y evaluar casos no utilizados:** informar también resultados malos y fallos de dominio/convergencia. No recalibrar con los casos de validación y seguir llamándolos independientes.
6. **Cuantificar error e incertidumbre:** incluir ruido/variabilidad de ciclo, incertidumbre de PMS, presión, caudales, temperatura y geometría. Hacer sensibilidades de esos datos y de discretización por separado. El error experimental no se infiere del residual numérico.

## Métricas de evaluación

Para una malla angular común y referencias documentadas:

```text
RMSEp = sqrt[Σ wθ (p_sim − p_exp)² / Σ wθ]        [Pa]
sesgo_p = Σ wθ (p_sim − p_exp) / Σ wθ            [Pa]
Wi_exp = ∮ p_exp dV; IMEP_exp = Wi_exp/Vd
error_CA50 = CA50_sim − CA50_ref                 [°]
error_par = Tbrake_sim − Tbrake_exp              [N m]
```

Definir antes si se compara todo 720° o un intervalo cerrado, y si IMEP es bruto o neto. Si no existe presión durante intercambio, no se inventa un trabajo de bombeo experimental. Las métricas relativas solo se calculan con denominadores no nulos y su definición explícita. CA50 debe especificar método de inferencia de calor y no confundirse con un sensor directo de fracción quemada.

Una eventual validación debe informar combustible, geometría, rango, métricas y casos usados; no produce un sello universal para todos los motores o modificaciones. La comparación A/B de dos simulaciones sigue siendo una sensibilidad hasta medir el original y la modificación con sus incertidumbres.

## Procedencia en la aplicación

La pestaña **Evidencia** guarda estado estimado o calibrado, fuente, datos y rango para combustión, calor y fricción. Es una declaración del usuario y no ejecuta ni certifica una calibración. Los ejemplos de esta entrega permanecen estimados. Una fuente de coeficientes publicada es una procedencia, no una calibración de esos coeficientes para el motor configurado.

Por las limitaciones de los conjuntos revisados y del dominio implementado, no existe aquí una cifra de error predictivo experimental del motor. El informe cuantifica lo que sí se verificó y mantiene ese resultado como **no determinado**, sin sustituirlo por comparación con datos sintéticos.
