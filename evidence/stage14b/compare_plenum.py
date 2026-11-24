"""Reduce matched plenum-volume runs to paired deltas without interpolation."""

import argparse
import cmath
import csv
import json
import math
from pathlib import Path


def harmonic(cycle, key, order):
    period = 720
    samples = sorted({row["CrankAngleDeg"] % period: row[key] for row in cycle}.items())
    mean = sum(pressure for _, pressure in samples) / len(samples)
    phasors = [
        (angle, (pressure - mean) * cmath.exp(-2j * math.pi * order * angle / period))
        for angle, pressure in samples
    ]
    wrapped = (phasors[0][0] + period, phasors[0][1])
    return sum(
        (angle_b - angle_a) * (value_a + value_b) / period
        for (angle_a, value_a), (angle_b, value_b) in zip(phasors, [*phasors[1:], wrapped], strict=True)
    )


def relative_phase(a, b):
    if abs(a) == 0 or abs(b) == 0:
        return ""
    return math.degrees(cmath.phase(b / a))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("campaign", type=Path)
    parser.add_argument("output_csv", type=Path)
    args = parser.parse_args()
    rows = []
    for mode in ("network", "network-fine", "network-time"):
        groups = {}
        for path in sorted((args.campaign / mode).glob("*.json")):
            payload = json.loads(path.read_text(encoding="utf-8-sig"))
            key = payload["trunkLength"], payload["scale"]
            groups.setdefault(key, {})[payload["volume"]] = payload["result"]["Points"][0]
        for (length, scale), points in groups.items():
            if len(points) != 2:
                continue
            a, b = [points[key] for key in sorted(points)]
            if not a["Metrics"] or not b["Metrics"]:
                raise ValueError(f"Incomplete pair at {length}, ×{scale}")
            # The two evenly spaced cylinders excite two events per cycle.
            p2a, p2b = (harmonic(point["Cycle"], "IntakeJunctionPa", 2) for point in (a, b))
            for cylinder_a, cylinder_b in zip(a["Cylinders"], b["Cylinders"], strict=True):
                row = {
                    "mode": mode, "length_m": length, "scale": scale,
                    "cylinder": cylinder_a["CylinderNumber"],
                    "shaft_power_delta_W": b["Metrics"]["BrakePowerW"] - a["Metrics"]["BrakePowerW"],
                    "shaft_torque_delta_Nm": b["Metrics"]["BrakeTorqueNm"] - a["Metrics"]["BrakeTorqueNm"],
                    "intake_pressure_order2_delta_deg": relative_phase(p2a, p2b),
                    "intake_pressure_order2_delta_Pa": abs(p2b) - abs(p2a),
                }
                for original, output in (
                    ("NetWorkJ", "work_delta_J"),
                    ("PumpingWorkJ", "pumping_delta_J"),
                    ("FreshAirAtIntakeCloseKg", "fresh_delta_kg"),
                    ("NetIntakeMassKg", "net_intake_delta_kg"),
                ):
                    row[output] = cylinder_b[original] - cylinder_a[original]
                rows.append(row)
    if not rows:
        raise ValueError("No matched pairs")
    with args.output_csv.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0])
        writer.writeheader()
        writer.writerows(rows)


if __name__ == "__main__":
    main()
