"""Independent equilibrium comparison using Cantera 3.2.0. Run verify.ps1 first."""
import csv
import json
from pathlib import Path

import cantera as ct
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "stage3"
SPECIES = ["C8H18,isooctane", "O2", "N2", "CO2", "H2O", "CO", "H2"]
EXTENDED = ["OH", "O", "H", "NO"]
DATA = {sp.name: sp for sp in ct.Species.list_from_file("nasa_gas.yaml")}


def gas(names):
    return ct.Solution(thermo="ideal-gas", species=[DATA[n] for n in names])


reduced = gas(SPECIES[1:])
radicals = gas(SPECIES[1:] + EXTENDED[:-1])
expanded = gas(SPECIES[1:] + EXTENDED)
fresh = gas(SPECIES)
closed_results = []
for case in json.loads((OUT / "closed-vessels.json").read_text()):
    fresh.set_equivalence_ratio(case["phi"], SPECIES[0], "O2:21,N2:79")
    fresh.TP = case["t"], case["p"]
    # Independent reconstruction from fresh reactants and elemental stoichiometry.
    n = fresh.Y / fresh.molecular_weights
    fuel = n[0]
    amounts = [max(0.0, n[1] - 12.5 * fuel), n[2], 8 * fuel, 9 * fuel, 0, 0]
    density = fresh.density
    energy = fresh.int_energy_mass
    assert abs(1 / density - case["volume"]) < 1e-10
    assert abs(energy - case["energy"]) < .1
    for phase in (reduced, radicals, expanded):
        phase.TDX = case["frozenT"], density, dict(zip(SPECIES[1:], amounts))
        phase.UV = energy, 1 / density
        phase.equilibrate("UV", solver="element_potential", rtol=1e-10)
    dt = case["temperature"] - reduced.T
    dp = (case["pressure"] - reduced.P) / reduced.P
    dy = float(max(abs(np.array(case["composition"][1:]) - reduced.Y)))
    assert abs(dt) < .002 and abs(dp) < 1e-6 and dy < 1e-7, (dt, dp, dy)
    closed_results.append({
        "phi": case["phi"], "initial_T_K": case["t"], "initial_p_Pa": case["p"],
        "frozen_T_K": case["frozenT"], "dino_T_K": case["temperature"],
        "reduced_T_K": reduced.T, "radicals_T_K": radicals.T, "expanded_T_K": expanded.T,
        "delta_T_K": dt, "delta_p_relative": dp, "max_delta_mass_fraction": dy,
        "expanded_NO_mole_fraction": expanded["NO"].X[0],
    })

columns = ["fuel_kg", "oxygen_kg", "nitrogen_kg", "carbon_dioxide_kg",
           "water_kg", "carbon_monoxide_kg", "hydrogen_kg"]
with (OUT / "v3-3000.trace.csv").open(encoding="utf-8") as stream:
    trace = [{key: float(value) for key, value in row.items()}
             for row in csv.DictReader(line for line in stream if not line.startswith("#"))]
cycle_reference = []
for angle in (355, 365, 380, 410, 450, 499):
    sample = min(trace, key=lambda s: abs(s["angle_deg"] - angle))
    masses = np.array([sample[c] for c in columns[1:]])
    product_mass = sum(masses)
    # Remove inert unmetabolized fuel from the Gibbs problem but use the full cylinder volume.
    for phase in (reduced, radicals, expanded):
        phase.TDY = sample["temperature_K"], product_mass / sample["volume_m3"], dict(zip(SPECIES[1:], masses))
        phase.equilibrate("UV", solver="element_potential", rtol=1e-10)
    assert abs(reduced.T - sample["temperature_K"]) < .002
    total_p = expanded.P + sample["fuel_kg"] * ct.gas_constant / fresh.molecular_weights[0] * expanded.T / sample["volume_m3"]
    cycle_reference.append({
        "angle_deg": sample["angle_deg"], "Dino_T_K": sample["temperature_K"],
        "expanded_T_K": expanded.T, "radicals_T_K": radicals.T, "Dino_p_Pa": sample["pressure_Pa"],
        "expanded_p_Pa": total_p, "expanded_NO_mole_fraction_excluding_fuel": expanded["NO"].X[0],
    })
result = {"cantera": ct.__version__, "closed_vessels": closed_results,
          "instantaneous_cycle_states_at_3000_rpm": cycle_reference}
(OUT / "cantera.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(f"PASS {len(closed_results)} UV vessels and {len(cycle_reference)} local cylinder states, Cantera {ct.__version__}")
