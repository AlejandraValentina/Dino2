"""Independent reference, Python 3 standard library; no imports from Dino2.

The work oracle integrates compression/expansion p(V), rather than using
the implementation's efficiency-based work. All numbers are synthetic.
"""
import json
import math
from pathlib import Path

root = Path(__file__).resolve().parents[1]
c = json.loads((root / "examples/otto-400cc.json").read_text())["configuration"]
vd = math.pi * c["boreM"] ** 2 / 4 * c["strokeM"]
v2 = vd / (c["compressionRatio"] - 1)
v1 = vd + v2
gamma = c["gamma"]
cv = c["gasConstantJPerKgK"] / (gamma - 1)
mass = c["initialPressurePa"] * v1 / (c["gasConstantJPerKgK"] * c["initialTemperatureK"])
t2 = c["initialTemperatureK"] * (v1 / v2) ** (gamma - 1)
t3 = t2 + c["heatInputJPerKg"] / cv
p3 = mass * c["gasConstantJPerKgK"] * t3 / v2


def integrate(p_start, v_start, v_end):
    n = 100000
    dv = (v_end - v_start) / n
    return math.fsum(p_start * (v_start / (v_start + (i + 0.5) * dv)) ** gamma * dv for i in range(n))


work = integrate(c["initialPressurePa"], v1, v2) + integrate(p3, v2, v1)
print(json.dumps({"work_J_numerical_integral": work, "indicated_torque_Nm": work / (4 * math.pi),
                  "indicated_power_6000rpm_W": work * 6000 / 120}, indent=2))
assert math.isclose(work, 249.8753053848507, rel_tol=1e-7)
print("PASS independent p dV reference (relative tolerance 1e-7)")
