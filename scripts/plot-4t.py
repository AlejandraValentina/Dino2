"""Optional technical-report figures. Requires matplotlib; numerical checks do not."""
import csv
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

root = Path(__file__).resolve().parents[1]
source = root / "artifacts/4t-validation"
output = root / "docs/figures"
output.mkdir(parents=True, exist_ok=True)


def read(name):
    with (source / name).open(encoding="utf-8-sig") as stream:
        return [{k: float(v) for k, v in row.items()} for row in csv.DictReader(line for line in stream if not line.startswith("#"))]


plt.rcParams.update({"font.size": 11, "axes.spines.top": False, "axes.spines.right": False})
fig, axes = plt.subplots(2, 1, figsize=(10, 8), sharex=True, constrained_layout=True)
for name, label, color in [("baseline.csv", "Original A", "#176b8c"), ("lift-4mm.csv", "B: alzada admisión 4 mm", "#c06b19"), ("spark-5deg.csv", "C: avance 5° APMS", "#7b4fa3")]:
    rows = read(name)
    axes[0].plot([r["rpm"] for r in rows], [r["brake_power_W"] / 1000 for r in rows], "o-", label=label, color=color, markersize=4)
    axes[1].plot([r["rpm"] for r in rows], [r["brake_torque_Nm"] for r in rows], "o-", label=label, color=color, markersize=4)
base = read("baseline.csv")
axes[0].plot([r["rpm"] for r in base], [r["indicated_power_W"] / 1000 for r in base], "--", label="A: potencia indicada", color="#65727a")
axes[0].set_ylabel("Potencia [kW]")
axes[1].set_ylabel("Par al cigüeñal [N·m]")
axes[1].set_xlabel("Régimen [RPM]")
for axis in axes:
    axis.grid(alpha=.25)
    axis.set_ylim(bottom=0)
axes[0].legend(loc="upper left", fontsize=9)
fig.suptitle("Dino2 · Caso sintético 402,12 cm³\nPredicción 0D sin validación experimental; pérdidas sin calibrar", fontsize=13)
fig.savefig(output / "4t-curves.png", dpi=180)
plt.close(fig)

trace = read("baseline-3000.trace.csv")
fig, axes = plt.subplots(1, 2, figsize=(12, 4.4), constrained_layout=True)
axes[0].plot([r["angle_deg"] for r in trace], [r["pressure_Pa"] / 1e5 for r in trace], color="#176b8c")
axes[0].set_xlabel("Ángulo [° desde PMS de intercambio]")
axes[0].set_ylabel("Presión absoluta [bar]")
axes[1].plot([r["volume_m3"] * 1e6 for r in trace], [r["pressure_Pa"] / 1e5 for r in trace], color="#c06b19")
axes[1].set_xlabel("Volumen [cm³]")
axes[1].set_ylabel("Presión absoluta [bar]")
for axis in axes:
    axis.grid(alpha=.25)
fig.suptitle("Ciclo periódico a 3000 RPM · Caso sintético, pseudoaire sin química", fontsize=13)
fig.savefig(output / "4t-cycle.png", dpi=180)
plt.close(fig)

study = json.loads((source / "numerical-study.json").read_text())
fig, axis = plt.subplots(figsize=(8, 4.7), constrained_layout=True)
for rpm in (1000, 3000, 6000):
    rows = sorted((r for r in study["grid"] if r["rpm"] == rpm), key=lambda r: r["h"])
    reference = rows[0]["NetWorkJ"]
    axis.loglog([r["h"] for r in rows[1:]], [abs(r["NetWorkJ"] - reference) / abs(reference) for r in rows[1:]], "o-", label=f"{rpm} RPM")
axis.set_xlabel("Máximo paso angular permitido [°]")
axis.set_ylabel("Diferencia relativa de trabajo frente a máximo 0,125°")
axis.grid(which="both", alpha=.25)
axis.legend()
axis.set_title("Refinamiento numérico · Tolerancia local 2e−8\nPasos adaptativos; no es una malla uniforme")
fig.savefig(output / "4t-grid.png", dpi=180)
plt.close(fig)
print("Report figures:", output)
