"""Compare the finite-volume bench with independently posed steady equations.

Usage: python scripts/check-stage13-aperture.py INPUT.csv OUTPUT.csv
Uses only the Python standard library.
"""

import csv
import math
import sys

GAMMA, R, T0 = 1.4, 287.0, 300.0
AREA = math.pi * .04 ** 2 / 4
CP = GAMMA * R / (GAMMA - 1)


def mass_flux(p0, pressure):
    temperature = T0 * (pressure / p0) ** ((GAMMA - 1) / GAMMA)
    velocity = math.sqrt(2 * CP * (T0 - temperature))
    return pressure * velocity / (R * temperature), velocity


def steady_reference(ratio, fraction, feeding):
    p2 = 100_000.0
    p0 = p2 * ratio
    critical = p0 * (2 / (GAMMA + 1)) ** (GAMMA / (GAMMA - 1))
    nozzle = AREA * fraction * mass_flux(p0, max(p2, critical))[0]
    if not feeding or fraction == 1:
        return nozzle, nozzle

    # Solve pressure at the step with the far pressure held fixed. The mixed
    # velocity comes from continuity and h0 and enters the momentum equation.
    def step_residual(throat_pressure):
        throat_flux, throat_velocity = mass_flux(p0, throat_pressure)
        j = fraction * throat_flux
        u = 2 * j * R * T0 / (p2 + math.sqrt(p2**2 + 2 * T0 * (j * R)**2 / CP))
        return throat_pressure + j * (throat_velocity - u) - p2

    low, high = critical, p0
    if step_residual(low) >= 0:
        return nozzle, AREA * fraction * mass_flux(p0, critical)[0]
    for _ in range(60):
        mid = (low + high) / 2
        if step_residual(mid) > 0:
            high = mid
        else:
            low = mid
    return nozzle, AREA * fraction * mass_flux(p0, (low + high) / 2)[0]


def main(source, destination):
    with open(source, newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != 112:
        raise ValueError(f"Expected 112 cases, found {len(rows)}")
    for row in rows:
        if row["status"] != "ok" or row["composition"] != "ideal":
            raise ValueError(row)
        nozzle, reference = steady_reference(float(row["ratio"]), float(row["alpha"]), row["feeding"] == "1")
        port, far = (float(row[key]) for key in ("mass_port_kg_s", "mass_far_kg_s"))
        row.update(nozzle_kg_s=nozzle, reference_kg_s=reference,
                   error_percent=100 * (abs(port) / reference - 1),
                   stationarity_percent=100 * abs(port - far) / abs(port))
    with open(destination, "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    for direction in ("1", "0"):
        selected = [row for row in rows if row["feeding"] == direction]
        print(direction, "max reference error %", max(abs(row["error_percent"]) for row in selected),
              "max stationarity difference %", max(row["stationarity_percent"] for row in selected))
    for direction in ("1", "0"):
        selected = sorted((float(row["ratio"]) - 1, abs(float(row["mass_port_kg_s"]))) for row in rows
                          if row["feeding"] == direction and row["alpha"] == "0.3" and row["cells"] == "128")
        (dp1, q1), (dp2, q2) = selected[:2]
        print(direction, "small pressure exponent", math.log(q2 / q1) / math.log(dp2 / dp1))
    if max(max(abs(row["error_percent"]), row["stationarity_percent"]) for row in rows) > .5:
        raise AssertionError("Aperture benchmark did not reach the reference or stationary tolerance of 0.5%")


if __name__ == "__main__":
    main(*sys.argv[1:])
