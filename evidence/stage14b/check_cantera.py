"""Independent Cantera 3.2 reference for the audited hot moving shock.

Run with the header and rows emitted by the probe's edge mode.
The reference uses properties from Cantera's GRI-Mech, never from Dino2.
"""

import argparse
import csv
import math
from pathlib import Path

import cantera as ct


def at_prescribed_pressure(gas, source_t, source_p, mach, back_p, mixture):
    gas.TPY = source_t, source_p, mixture
    v0, h0, s0 = 1 / gas.density, gas.h, gas.s
    u0 = mach * gas.sound_speed
    dp = back_p - source_p

    def hugoniot(temperature):
        gas.TP = temperature, back_p
        return gas.h - h0 - dp * (1 / gas.density + v0) / 2

    lower, upper = source_t, 6000
    if hugoniot(lower) * hugoniot(upper) >= 0:
        raise ValueError("Hugoniot outside the reference range")
    for _ in range(65):
        middle = (lower + upper) / 2
        if hugoniot(middle) > 0:
            upper = middle
        else:
            lower = middle
    temperature = (lower + upper) / 2
    gas.TP = temperature, back_p
    volume = 1 / gas.density
    shock_frame_mass = math.sqrt(dp / (v0 - volume))
    shock_speed = u0 - shock_frame_mass * v0
    velocity = shock_speed + shock_frame_mass * volume
    return temperature, velocity, gas.density * velocity, gas.s - s0, shock_speed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("edge_csv", type=Path)
    parser.add_argument("output_csv", type=Path)
    inputs = parser.parse_args()
    with inputs.edge_csv.open(encoding="utf-8-sig", newline="") as stream:
        rows = [
            row for row in csv.DictReader(stream)
            if row["label"] == "thermal"
            and tuple(float(row[key]) for key in ("edge_t", "edge_m", "tank_pa")) == (1800, 2, 700000)
        ]
    if not rows:
        raise ValueError("Missing audited hot shock")
    gas = ct.Solution("gri30.yaml")
    reference = at_prescribed_pressure(
        gas, 1800, 100000, 2, 700000, {"O2": .018, "N2": .739, "CO2": .151, "H2O": .092}
    )
    temp, velocity, mass, entropy, shock_speed = reference
    report = []
    for row in rows:
        flux = float(row["flux"])
        report.append({
            "alpha": float(row["alpha"]),
            "face_temp_K": float(row["t_face"]),
            "face_speed_m_s": float(row["u_face"]),
            "mass_flux_kg_m2_s": flux,
            "axial_flux_Pa": float(row["axial"]),
            "h0_flux_J_kg": float(row["energy"]) / flux if flux else "",
            "reference_full_temp_K": temp,
            "reference_full_speed_m_s": velocity,
            "reference_full_mass_kg_m2_s": mass,
            "reference_entropy_gain_J_kg_K": entropy,
            "reference_shock_speed_m_s": shock_speed,
        })
    full = next(row for row in report if row["alpha"] == 1)
    near = next(row for row in report if row["alpha"] == .999999999)
    with inputs.output_csv.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=report[0])
        writer.writeheader()
        writer.writerows(report)
    # Independent properties and molecular weights need not match bit for bit.
    assert entropy > 0
    # The production table and GRI-Mech use different coefficient sets.
    # This cross-table regression checks the states within one per mille.
    assert abs(full["face_temp_K"] / temp - 1) < 0.001
    assert abs(full["face_speed_m_s"] / velocity - 1) < 0.001
    assert abs(full["mass_flux_kg_m2_s"] / mass - 1) < 0.001
    assert abs(full["h0_flux_J_kg"] - near["h0_flux_J_kg"]) < 0.01
    assert abs(full["axial_flux_Pa"] - near["axial_flux_Pa"]) < 0.01


if __name__ == "__main__":
    main()
