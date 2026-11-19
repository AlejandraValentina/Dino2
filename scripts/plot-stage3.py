"""Plot reproducible stage 3 comparisons after verify.ps1."""
import csv
from pathlib import Path

import matplotlib.pyplot as plt

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts" / "stage3"


def load(name):
    with (ARTIFACTS / name).open(encoding="utf-8") as file:
        return [{k: float(v) for k, v in row.items()}
                for row in csv.DictReader(line for line in file if not line.startswith("#"))]


fig, axes = plt.subplots(2, 2, figsize=(10, 6.5), layout="constrained")
fig.suptitle("Dino2 4T · Comparación numérica con entradas equiparadas", weight="bold")
for filename, label, color in [
    ("v1-matched.csv", "v1 Pseudoaire", "#666666"),
    ("v2-matched.csv", "v2 Productos congelados", "#2a6ebb"),
    ("v3.csv", "v3 Equilibrio UV reducido", "#b34220"),
]:
    data = load(filename)
    axes[0, 0].plot([p["rpm"] for p in data], [p["brake_power_W"] / 1e3 for p in data], ".-", label=label, color=color)
    axes[0, 1].plot([p["rpm"] for p in data], [p["peak_temperature_K"] for p in data], ".-", label=label, color=color)
axes[0, 0].set(xlabel="Régimen [RPM]", ylabel="Potencia al cigüeñal [kW]")
axes[0, 1].set(xlabel="Régimen [RPM]", ylabel="Temperatura pico [K]")
axes[0, 0].legend(fontsize=8)

for filename, label, color in [("v2-3000.trace.csv", "v2", "#2a6ebb"), ("v3-3000.trace.csv", "v3", "#b34220")]:
    data = [p for p in load(filename) if 320 <= p["angle_deg"] <= 560]
    axes[1, 0].plot([p["angle_deg"] for p in data], [p["pressure_Pa"] / 1e5 for p in data], label=label, color=color)
    if label == "v3":
        for field, species, shade in [("carbon_monoxide_kg", "CO", "#b34220"), ("hydrogen_kg", "H₂", "#227d68")]:
            axes[1, 1].plot([p["angle_deg"] for p in data], [p[field] * 1e6 for p in data], color=shade, label=species)
axes[1, 0].set(xlabel="Ángulo · 3000 RPM [°]", ylabel="Presión en cilindro [bar]")
axes[1, 1].set(xlabel="Ángulo · 3000 RPM [°]", ylabel="Masa de especie [mg]")
axes[1, 1].axvline(500, ls=":", color="#666666", label="Apertura escape")
for axis in axes[1]:
    axis.legend(fontsize=8)
for axis in axes.flat:
    axis.grid(alpha=.25)
fig.savefig(ROOT / "docs" / "figures" / "stage3-comparison.png", dpi=160)
