"""Check the partial-opening moving-wave fixture against perfect-gas limits."""

import csv
import sys
from math import isfinite, sqrt


def area_ratio(mach, gamma):
    shape = (2 + (gamma - 1) * mach**2) / (gamma + 1)
    return shape ** ((gamma + 1) / (2 * (gamma - 1))) / mach


gamma, gas_constant = 1.4, 287.0
pressure, temperature, mach, fraction = 100_000, 900, 2, 0.6
throat_ratio = area_ratio(mach, gamma) * fraction
assert throat_ratio > 1
low, high = 1.0, mach
for iteration in range(55):
    mid = (low + high) / 2
    if area_ratio(mid, gamma) < throat_ratio:
        low = mid
    else:
        high = mid
throat_mach = (low + high) / 2
stag = 1 + (gamma - 1) * mach**2 / 2
throat = 1 + (gamma - 1) * throat_mach**2 / 2
throat_pressure = pressure * (stag / throat) ** (gamma / (gamma - 1))
expected_velocity = mach * sqrt(gamma * gas_constant * temperature)
with open(sys.argv[1], encoding="utf-8-sig", newline="") as source:
    samples = [row for row in csv.DictReader(source) if float(row["alpha"]) == fraction]
assert len(samples) == 6
for sample in samples:
    assert float(sample["tank_pa"]) < throat_pressure
    assert not isfinite(float(sample["shock_x"]))
    for field, expected in (("post_p", pressure), ("post_T", temperature), ("post_u", expected_velocity)):
        assert abs(float(sample[field]) / expected - 1) < 1e-12, sample
print(f"Partial opening: M_throat={throat_mach:.10f} p_throat={throat_pressure:.9f} Pa")
print("PASS: no incoming shock and unchanged duct state across six space-time samples.")
