# Manual de Dino2 Desktop

## Inicio y proyectos

Extraé el contenido completo del portable y ejecutá **Dino2.Desktop.exe**. La ventana inicial es **Motor 4T** y carga un ejemplo sintético. **Motor 2T** y **Otto de referencia** abren sus espacios de trabajo. Desde 2T, **Volver a 4T** conserva el proyecto, las curvas y el estudio de la sesión. Para continuar en otro momento, guardá los archivos antes de cerrar Dino2. La aplicación inicia con el ejemplo y cada proyecto guardado se recupera con **Abrir**.

1. **4T.** Elegí **Ejemplo** para iniciar un proyecto o **Abrir…** para usar uno de **examples**. Elegí el modelo físico. El formulario habilita los parámetros pertinentes. Las cifras de **Evidencia** pueden registrarse junto a procedencia y dominio de identificación. Esa declaración depende del respaldo aportado por el usuario.
2. **2T.** Elegí **Motor 2T** y **Ejemplo 2T** o **Abrir proyecto 2T…**. El selector izquierdo organiza motor y RPM, combustión y pérdidas, cárter y admisión, lumbreras y transfers, escape de expansión y resolución numérica. El resumen muestra cilindrada y relaciones de compresión geométrica, atrapada y primaria.
3. Los campos indican unidades. Se acepta coma o punto decimal y las RPM se ingresan como enteros. Las presiones se ingresan en valores absolutos. En los archivos de proyecto, la geometría y las ecuaciones usan SI. Los parámetros fuera de rango y las combinaciones incompatibles se informan en el estado.

## Calcular e interpretar

Configurá inicio, fin y paso de RPM y pulsá **Simular** o **Simular 2T**. El estado indica avance. **Cancelar** detiene el cálculo. En 4T, la curva actual se publica cuando el cálculo termina. En 2T, la tabla conserva los puntos evaluados, con el estado y diagnóstico de cada régimen. Las filas no periódicas o fuera de dominio tienen potencia y par vacíos. La curva de un barrido parcial usa marcadores.

- **Curvas.** La leyenda identifica A, B o Actual. El trazo discontinuo identifica la indicada y el continuo identifica la estimación al cigüeñal en curvas completas. Las líneas guían la lectura entre muestras. El cursor indica valores calculados. Sobre un gráfico, usá la rueda para ampliar, arrastrá para mover la vista ampliada y pulsá **Inicio** o hacé doble clic para ajustar. Con foco en el gráfico funcionan **+**, **−** y las flechas. El menú contextual permite guardar la vista en PNG.
- **Resultados.** La tabla admite ordenación por encabezado. La potencia indicada neta parte del trabajo de los gases. La potencia y el par estimados al cigüeñal incorporan pérdidas modeladas. En 4T, la eficiencia volumétrica mide el llenado en la convención del modelo. En 2T se distinguen entrega, atrapamiento, barrido y carga.
- **Ciclo en 4T.** Elegí la RPM para ver presión, p–V, temperatura y balances. Los modelos termoquímicos muestran composición de fin de ciclo. **Conductos** añade presión y caudal en admisión y escape cuando el motor usa 1D. El caudal positivo se dirige hacia el cilindro.
- **Ciclo y flujos en 2T.** La vista muestra presión de cilindro y cárter, otra gráfica para las ondas junto a láminas y lumbrera de escape, temperatura, caudal y composición residual al cierre. En el gráfico de caudales, transfers positivos se dirigen al cilindro y escape positivo sale del cilindro. Podés desplazarte para recorrer las cuatro gráficas.
- **Diagnósticos.** Interpretá ciclos, residuo periódico, balances, procedencia y estado de convergencia junto con la curva. Una periodicidad aceptada corresponde a un régimen estabilizado con los parámetros elegidos. Los balances y la resolución de malla cuantifican efectos distintos.

![Curva del simulador 2T en el portable](figures/desktop-2t-etapa8.png)

[Captura de la presión de admisión y escape en 2T](figures/desktop-2t-ciclo-etapa8.png).

## Comparaciones y estudios

**Conservar como A** fija una referencia. Editá un parámetro y simulá B. **Quitar A** retira la referencia del proyecto. **A / B** muestra diferencias B−A para RPM comunes calculadas. Las diferencias positivas corresponden al cálculo de esa configuración. Revisá la identidad del modelo y la resolución antes de interpretar el resultado.

Con geometrías y ajustes compatibles, **Comprobar malla A/B** en 4T 1D y **Verificar malla A/B** en 2T ejecutan refinamientos en la RPM elegida. La banda observada orienta la interpretación. Una diferencia que queda dentro de ella puede ser indistinguible. El resultado se refiere a resolución numérica.

- **4T.** Abrí **Estudios**, elegí **Tomar motor actual** o **Tomar original A**, y configurá uno a tres ejes, grilla y objetivo. Revisá el costo y ejecutá. Examiná resultados, curvas, puntos y sensibilidad. El contraste permite elegir referencia, y aplicar B conserva el original del proyecto.
- **2T.** En **Estudios 2T**, abrí **Plan**, elegí motor base, uno o dos parámetros, rangos, objetivo y núcleos. Las RPM provienen de la grilla del motor base. Una banda de objetivo puede seleccionar un subconjunto. Tras ejecutar, **Candidatos** permite contrastar contra la base y **Aplicar como B**. También hay vistas de curva y diagnósticos.
- En ambos casos, la preselección ordena las muestras convergidas por el objetivo. Los estudios pueden guardar sus resultados parcial o completamente, incluidos los contrastes disponibles. Guardalos desde la propia pestaña antes de reemplazarlos o salir. El exportador CSV sirve para análisis externo.

## Archivos y exportación

| Módulo | Proyecto JSON | CSV |
|---|---|---|
| Otto | Configuración del ciclo | Curvas e información del cálculo |
| 4T | Configuraciones A y actual. Al abrir, **Simular** reconstruye las curvas. | Curva, traza de la RPM elegida, original y diferencias si existe A. El contraste disponible genera un JSON adicional. |
| 2T | Configuraciones, curvas disponibles y último contraste A/B | Curva 2T y comparación si existe A. **Exportar ciclo CSV…** guarda la traza de la RPM elegida. El contraste disponible genera un CSV adicional. |
| Estudios | Definición, evaluaciones, estado parcial y contrastes | Resultados y evidencia de clasificación, con unidad e identificación según el formato de la familia |

Cada módulo abre sus propios archivos. Si la apertura falla por versión, contenido, permisos o ruta, el estado informa el error y el proyecto anterior permanece disponible. Guardá con un nombre claro antes de cambiar parámetros. Un PNG conserva la vista elegida, mientras que el CSV conserva los valores numéricos.

## Casos para reproducir

| Caso del directorio **examples** | Recorrido |
|---|---|
| **otto-400cc.json** | En Otto, abrí el ejemplo y pulsá **Calcular**. La potencia indicada ideal sigue la RPM con par constante. |
| **4t-equilibrium.json** | Abrí en 4T y simulá. Inspeccioná presión y temperatura en **Ciclo**. |
| **4t-ducts-muscl-ab.json** | Abrí en 4T, simulá A y B y compará geometrías en **A / B**. Elegí la RPM y contrastá si los ajustes son compatibles. |
| **study-spark-v3.dino-study.json** | Abrí mediante **Abrir estudio…** dentro de 4T. Consultá los resultados guardados. |
| **2t-125cc-escape-ab.json** | Abrí en 2T, simulá y contrastá a 5000 RPM. El informe incluye la resolución de ese caso. |
| **2t-125cc-partial-domain.json** | Simulá en 2T y comprobá la fila de 5000 RPM periódica y la de 10 000 RPM fuera de dominio. |
| **2t-125cc-wide-rpm.json** | Ejecutá la grilla de 500 a 10 000 RPM, de 500 en 500. El cálculo es más largo que el ejemplo de tres puntos. |

Para un cálculo fallido, revisá el mensaje, las unidades, la distribución, el modelo y los parámetros numéricos. En 2T, usá el componente dominante y los ciclos completados para diagnosticar asentamiento. La ficha científica delimita qué parámetros son prescritos o estimados y dónde falta evidencia experimental. [Referencia científica](referencia-cientifica.md).
