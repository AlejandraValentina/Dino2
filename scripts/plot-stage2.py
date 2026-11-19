"""Optional report figures for the explicitly frozen-products stage-2 model."""
import csv
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

root=Path(__file__).resolve().parents[1]
source=root/"artifacts/stage2"; output=root/"docs/figures";output.mkdir(exist_ok=True)

def read(name):
    with (source/name).open(encoding="utf-8-sig") as stream:
        return [{k:float(v) for k,v in row.items()} for row in csv.DictReader(s for s in stream if not s.startswith("#"))]

plt.rcParams.update({"font.size":11,"axes.spines.top":False,"axes.spines.right":False})
fig,axes=plt.subplots(2,1,figsize=(10,8),sharex=True,constrained_layout=True)
for name,label,color in [("reference-v1-matched.csv","v1 pseudoaire · entradas equiparadas","#65727a"),
                         ("frozen-prescribed.csv","v2 especies · Wiebe prescrita","#176b8c"),
                         ("reactive.csv","v2 especies · Wiebe escalada estimada","#c06b19")]:
    rows=read(name)
    axes[0].plot([r["rpm"] for r in rows],[r["brake_power_W"]/1000 for r in rows],"o-",label=label,color=color,markersize=4)
    axes[1].plot([r["rpm"] for r in rows],[r["brake_torque_Nm"] for r in rows],"o-",color=color,markersize=4)
axes[0].set_ylabel("Potencia estimada al cigüeñal [kW]");axes[0].legend(fontsize=9)
axes[1].set_ylabel("Par estimado al cigüeñal [N·m]");axes[1].set_xlabel("Régimen [RPM]")
for axis in axes:axis.grid(alpha=.25);axis.set_ylim(bottom=0)
fig.suptitle("Comparación de modelos · Caso sintético 402,12 cm³\nDiferencias numéricas, sin validación experimental ni ganancia mecánica demostrada",fontsize=13)
fig.savefig(output/"stage2-model-comparison.png",dpi=180);plt.close(fig)

rows=read("reactive-3000.trace.csv")
fig,axes=plt.subplots(2,1,figsize=(10,7),sharex=True,constrained_layout=True)
for key,label in [("fuel_kg","iso-octano"),("oxygen_kg","O₂"),("nitrogen_kg","N₂"),("carbon_dioxide_kg","CO₂"),("water_kg","H₂O")]:
    axes[0].plot([r["angle_deg"] for r in rows],[r[key]/r["mass_kg"] for r in rows],label=label)
axes[0].set_ylabel("Fracción másica");axes[0].legend(ncol=5,fontsize=9);axes[0].grid(alpha=.25)
axes[1].plot([r["angle_deg"] for r in rows],[r["temperature_K"] for r in rows],color="#c06b19")
axes[1].set_ylabel("Temperatura homogénea [K]");axes[1].set_xlabel("Ángulo [° desde PMS de intercambio]");axes[1].grid(alpha=.25)
fig.suptitle("Especies y temperatura · Ciclo periódico a 3000 RPM\nProductos congelados; disociación no resuelta",fontsize=13)
fig.savefig(output/"stage2-species.png",dpi=180);plt.close(fig)

assessment=source/"cantera-assessment.json"
if assessment.exists():
    data=json.loads(assessment.read_text())["constant_volume_cases"]
    fig,axis=plt.subplots(figsize=(8,4.7),constrained_layout=True)
    x=list(range(len(data)))
    axis.bar([i-.17 for i in x],[r["frozen_T_K"] for r in data],width=.34,label="Productos congelados")
    axis.bar([i+.17 for i in x],[r["equilibrium_T_K"] for r in data],width=.34,label="Equilibrio UV · Cantera")
    axis.set_xticks(x,[f"T₀={r['initial_T_K']:.0f} K\np₀={r['initial_p_Pa']/1e5:.0f} bar" for r in data])
    axis.set_ylabel("Temperatura final [K]");axis.grid(axis="y",alpha=.25);axis.legend()
    axis.set_title("Evaluación de disociación · Recipiente adiabático a volumen constante\nVerificación numérica; no es una validación experimental del motor")
    fig.savefig(output/"stage2-equilibrium.png",dpi=180);plt.close(fig)
print("Stage-2 figures:",output)
