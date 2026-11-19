"""Independent conservation audit of exported traces; Python standard library only.

Recomputes geometry, sensible u/h, valve mass flux, wall heat and trapezoidal
flux/work integrals from the input snapshot and trace. Never imports Dino2.
This verifies a numerical implementation, not experimental engine validity.
"""
import csv
import json
import math
import sys
from pathlib import Path

path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("artifacts/4t-validation/baseline-3000.trace.csv")
lines = path.read_text(encoding="utf-8-sig").splitlines()
c = json.loads(next(s.split("=", 1)[1] for s in lines if s.startswith("# configuration_SI=")))
rpm = int(next(s.split("=", 1)[1] for s in lines if s.startswith("# rpm=")))
rows = [{k: float(v) for k, v in row.items()} for row in csv.DictReader(s for s in lines if not s.startswith("#"))]
lo = [sum(v) for v in zip([.79 * x for x in [3.53100528, -1.23660987e-4, -5.02999437e-7, 2.43530612e-9, -1.40881235e-12]],
                         [.21 * x for x in [3.78245636, -2.99673415e-3, 9.847302e-6, -9.68129508e-9, 3.24372836e-12]])]
hi = [sum(v) for v in zip([.79 * x for x in [2.95257626, 1.39690057e-3, -4.92631691e-7, 7.86010367e-11, -4.60755321e-15]],
                         [.21 * x for x in [3.66096083, 6.56365523e-4, -1.41149485e-7, 2.05797658e-11, -1.29913248e-15]])]
R = 8.31446261815324 / (.79 * .0280134 + .21 * .0319988)


def integral(a, t):
    return math.fsum(x * t ** (i + 1) / (i + 1) for i, x in enumerate(a))


def cp(t):
    return R * math.fsum(x * t**i for i, x in enumerate(lo if t <= 1000 else hi))


def u(t):
    return R * (integral(lo, t) if t <= 1000 else integral(hi, t) + integral(lo, 1000) - integral(hi, 1000)) - R * t


def h(t):
    return u(t) + R * t


def lift(v, angle):
    phase = (angle - v["opensDeg"]) % 720
    duration = v["closesDeg"] - v["opensDeg"]
    return v["maximumLiftM"] * math.sin(math.pi * phase / duration)**2 if phase < duration else 0


def massflow(v, reservoir, row):
    a = v["dischargeCoefficient"] * v["count"] * min(math.pi * v["diameterM"] * lift(v, row["angle_deg"]), math.pi * v["diameterM"]**2 / 4)
    p1, p2 = reservoir["pressurePa"], row["pressure_Pa"]
    t = reservoir["temperatureK"] if p1 > p2 else row["temperature_K"]
    if p1 == p2 or a == 0:
        return 0
    gamma = cp(t) / (cp(t) - R)
    ratio = min(p1, p2) / max(p1, p2)
    critical = (2 / (gamma + 1)) ** (gamma / (gamma - 1))
    if ratio <= critical:
        # Independent sonic density * sonic velocity evaluation.
        ts = t * 2 / (gamma + 1)
        ps = max(p1, p2) * critical
        flux = ps / (R * ts) * math.sqrt(gamma * R * ts)
    else:
        mach = math.sqrt(2 / (gamma - 1) * (ratio ** (-(gamma - 1) / gamma) - 1))
        ts = t / (1 + (gamma - 1) / 2 * mach * mach)
        flux = min(p1, p2) / (R * ts) * mach * math.sqrt(gamma * R * ts)
    return math.copysign(a * flux, p1 - p2)


geometry = c["geometry"]
area = math.pi * geometry["boreM"]**2 / 4
radius, rod = geometry["strokeM"] / 2, geometry["rodLengthM"]
clearance = area * geometry["strokeM"] / (geometry["compressionRatio"] - 1)
mass_rates, enthalpies, wall_rates = [], [], []
for row in rows:
    angle = row["angle_deg"] * math.pi / 180
    displacement = radius * (1 - math.cos(angle)) + rod - math.sqrt(rod**2 - radius**2 * math.sin(angle)**2)
    assert abs(clearance + area * displacement - row["volume_m3"]) < 1e-14
    assert abs(row["pressure_Pa"] * row["volume_m3"] - row["mass_kg"] * R * row["temperature_K"]) < 1e-8
    assert abs(row["internal_energy_J"] - row["mass_kg"] * u(row["temperature_K"])) < 1e-8
    mi = massflow(c["intakeValve"], c["intake"], row)
    me = massflow(c["exhaustValve"], c["exhaust"], row)
    assert abs(mi - row["intake_flow_kg_s"]) < 1e-10
    assert abs(me - row["exhaust_flow_kg_s"]) < 1e-10
    mass_rates.append(mi + me)
    enthalpies.append(mi * h(c["intake"]["temperatureK"] if mi > 0 else row["temperature_K"]) + me * h(c["exhaust"]["temperatureK"] if me > 0 else row["temperature_K"]))
    wall_area = 2 * area + math.pi * geometry["boreM"] * (clearance / area + displacement)
    coefficient = 130 * row["volume_m3"]**(-.06) * (row["pressure_Pa"] / 1e5)**.8 * row["temperature_K"]**(-.4) * (2 * geometry["strokeM"] * rpm / 60 + 1.4)**.8
    wall_rates.append(c["thermal"]["hohenbergMultiplier"] * coefficient * wall_area * (row["temperature_K"] - c["thermal"]["wallTemperatureK"]))


def trap_time(values):
    return math.fsum((values[i] + values[i-1]) / 2 * (rows[i]["angle_deg"] - rows[i-1]["angle_deg"]) / (6 * rpm) for i in range(1, len(rows)))


work = math.fsum((rows[i]["pressure_Pa"] + rows[i-1]["pressure_Pa"]) / 2 * (rows[i]["volume_m3"] - rows[i-1]["volume_m3"]) for i in range(1, len(rows)))
delta_u = rows[-1]["internal_energy_J"] - rows[0]["internal_energy_J"]
ivc = min(rows, key=lambda r: abs(r["angle_deg"] - c["intakeValve"]["closesDeg"]))
fuel = ivc["fresh_mass_kg"] / (c["combustion"]["airFuelRatio"] + 1)
qin = fuel * c["combustion"]["lowerHeatingValueJPerKg"] * c["combustion"]["heatReleaseEfficiency"]
mass_residual = rows[-1]["mass_kg"] - rows[0]["mass_kg"] - trap_time(mass_rates)
energy_residual = delta_u - qin + trap_time(wall_rates) - trap_time(enthalpies) + work
print(json.dumps({"rpm": rpm, "samples": len(rows), "independent_work_J": work,
                  "independent_mass_residual_kg": mass_residual, "independent_energy_residual_J": energy_residual}, indent=2))
assert abs(mass_residual) < 3e-4 * max(r["mass_kg"] for r in rows)
assert abs(energy_residual) < .001 * qin
assert abs(work - rows[-1]["work_J"]) < .03
print("PASS independent 4T trace audit (trapezoidal quadrature, distinct from solver ledgers)")
