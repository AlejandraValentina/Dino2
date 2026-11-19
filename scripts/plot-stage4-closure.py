"""Plot the stage 4 closure evidence after the numerical campaign."""
import json
from pathlib import Path

import matplotlib.pyplot as plt

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/stage4-closure"

def load(name):
    return json.loads((OUT / name).read_text(encoding="utf-8"))

fig, (waves, difference) = plt.subplots(1, 2, figsize=(10.5, 4.2), layout="constrained")
fig.suptitle("Dino2 4T · Resolución de ondas y sensibilidad A/B", weight="bold")
data = load("waves.json")["comparisons"]
campaign = load("campaign.json")["rows"]
for scheme, label, color in [("FirstOrder", "Primer orden", "#666666"), ("Muscl", "MUSCL", "#176a95")]:
    runs = [r for r in data if r["scheme"] == scheme]
    waves.loglog([r["cells"] for r in runs], [r["travellingL1Pa"] for r in runs], "o-", color=color, label=label)
    rows = [r for r in campaign if r["scheme"] == scheme]
    difference.plot([r["cells"] for r in rows], [r["result"]["DeltaPowerW"] for r in rows], "o-", color=color,
                    label=label + " · igual N")
matched = [r for r in load("matched-spacing.json") if r["cellsA"] != r["cellsB"]]
difference.plot([r["cellsA"] for r in matched], [r["deltaW"] for r in matched], "s-", color="#b65028",
                label="MUSCL · Δx semejante")
waves.set(xlabel="Celdas del tubo", ylabel="Error L¹ del pulso [Pa]")
difference.set(xlabel="Celdas de admisión en A", ylabel="Δ potencia B−A a 4000 RPM [W]")
for axis in (waves, difference):
    axis.grid(alpha=.25)
    axis.legend(fontsize=8)
fig.savefig(ROOT / "docs/figures/stage4-closure.png", dpi=170)
