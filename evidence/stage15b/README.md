# Evidencia parcial de Etapa 15B

Esta carpeta conserva las regresiones de entrada/final y los fixtures sintéticos de Etapa 15B.

- `stage15a-regression.log`: compilación y regresión general antes de extender la física.
- `final-regression.log`: `scripts/verify.ps1` en el árbol de producción final de esta iteración; terminó correctamente.
- `final-regression-verified.log`: primer intento final, preservado para auditar los fallos detectados de compatibilidad y tolerancia antes de corregirlos.
- `cd-map-synthetic.csv`: grilla analítica bilineal; no contiene datos experimentales.
- `reed-motor-periodicity.txt`: campaña sintética de motor con lámina dinámica que no alcanzó periodicidad.
- `../../tests/Dino2.FourStroke.Tests/Program.cs`: referencia/interpolación, rechazo de extrapolación,
  integración de área en 4T 0D, importación y serialización del mapa.
- `../../tests/Dino2.TwoStroke.Tests/Program.cs`: referencias de rigidez/frecuencia de viga y contacto modal.
- `../../src/Dino2.Core/TwoStroke/ReedDynamics.cs`: implementación de la reducción modal.
- `../../src/Dino2.Storage/DischargeMapFile.cs`: formato CSV y verificación SHA-256.

Reproducción: `../../scripts/verify.ps1`.
La simulación de motor completo con la lámina dinámica todavía no alcanza periodicidad y los transfers
individuales 1D aún no están implementados. Por tanto, esta evidencia no constituye aceptación de 15B.
