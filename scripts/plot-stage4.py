"""Generate numerical comparison for the stage 4 report after verify.ps1."""
import csv
from pathlib import Path

import matplotlib.pyplot as plt

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "artifacts/stage4"


def read(name):
    with (OUTPUT / name).open(encoding="utf-8") as stream:
        return [{k: float(v) for k, v in row.items()}
                for row in csv.DictReader(line for line in stream if not line.startswith("#"))]


fig, axes = plt.subplots(2, 2, figsize=(11, 7), layout="constrained")
fig.suptitle("Dino2 4T · Conductos 1D · Caso numérico", weight="bold")
for file, label, color in [
    ("v3-reservoirs.csv", "v3 · Depósitos 0D", "#666666"),
    ("v4-base.csv", "v4 · Admisión 350 mm", "#1f6e9f"),
    ("v4-long-intake.csv", "v4 · Admisión 550 mm", "#b84b22"),
]:
    points = read(file)
    rpm = [p["rpm"] for p in points]
    axes[0, 0].plot(rpm, [p["brake_power_W"] / 1000 for p in points], ".-", color=color, label=label)
    axes[0, 1].plot(rpm, [p["volumetric_efficiency"] for p in points], ".-", color=color)
axes[0, 0].legend(fontsize=8)
axes[0, 0].set(xlabel="RPM", ylabel="Potencia al cigüeñal [kW]")
axes[0, 1].set(xlabel="RPM", ylabel="Eficiencia volumétrica [–]")

for file, label, color in [
    ("v4-base-4000.trace.csv", "350 mm", "#1f6e9f"),
    ("v4-long-intake-4000.trace.csv", "550 mm", "#b84b22"),
]:
    points = read(file)
    angle = [p["angle_deg"] for p in points]
    for axis, field in zip(axes[1], ("intake_port_pressure_Pa", "exhaust_port_pressure_Pa")):
        axis.plot(angle, [p[field] / 1e5 for p in points], color=color, lw=1.1, label=label)
axes[1, 0].set(xlabel="Ángulo a 4000 RPM [°]", ylabel="Presión admisión [bar]")
axes[1, 1].set(xlabel="Ángulo a 4000 RPM [°]", ylabel="Presión escape [bar]")
axes[1, 0].legend(fontsize=8)
for axis in axes.flat:
    axis.grid(alpha=.25)
fig.savefig(ROOT / "docs/figures/stage4-ducts.png", dpi=160)
