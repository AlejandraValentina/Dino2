"""Compare periodic engine traces from the two instrumented copies."""
import argparse, csv, json
from pathlib import Path
import numpy as np

arg = argparse.ArgumentParser()
arg.add_argument("baseline", type=Path)
arg.add_argument("current", type=Path)
arg.add_argument("output", type=Path)
a = arg.parse_args()

def read(path, four):
    d = json.loads(path.read_text(encoding="utf-8-sig"))
    result = d["outcome"]
    point = next(iter(result["Points"]), None) if four else result["Runs"][0]["Point"]
    return d["counts"], point, result.get("Failures", []) if four else result["Runs"][0]["Failure"]

def delta(q, p, key):
    return p[key] - q[key]

def port(q, p, angle, key, end):
    samples = np.arange(end + 1)
    def interpolate(trace):
        clean = sorted({x[angle]: x[key] for x in trace if isinstance(x.get(key), (float,int))}.items())
        return np.interp(samples, [x[0] for x in clean], [x[1] for x in clean])
    if not q["Trace"] or not p["Trace"] or key not in q["Trace"][0] or key not in p["Trace"][0]:
        return ""
    return float(np.max(np.abs(interpolate(p["Trace"]) - interpolate(q["Trace"]))))

rows = []
for baseline in sorted(a.baseline.glob("*.json")):
    current = a.current / baseline.name
    if not current.exists():
        continue
    four = baseline.name.startswith("4t")
    cnt0, q, fail0 = read(baseline, four)
    cnt1, p, fail1 = read(current, four)
    r = {"case": baseline.stem, "failure_v3": fail0 or "", "failure_v4": fail1 or ""}
    for key in ("Calls","Below1E12","Below1E9","ChokedNear","WaveErrorMax","Domain","Numerical","Iterations","Smallest"):
        r[key+"_v3"],r[key+"_v4"] = cnt0[key],cnt1[key]
    if q is not None and p is not None:
        torque, power = ("BrakeTorqueNm","BrakePowerW") if four else ("ShaftTorqueNm","ShaftPowerW")
        r.update(torque_v3=q[torque], torque_v4=p[torque], torque_delta_Nm=delta(q,p,torque),
                 power_delta_W=delta(q,p,power), cylinder_peak_pressure_delta_Pa=delta(q,p,"PeakPressurePa"),
                 periodic_v3=q["PeriodicError"], periodic_v4=p["PeriodicError"])
        if four:
            r.update(pumping_delta_J=delta(q,p,"PumpingWorkJ"), cylinder_work_delta_J=delta(q,p,"GrossWorkJ"))
        else:
            r.update(crankcase_delta_J=delta(q,p,"CrankcaseWorkJ"), cylinder_work_delta_J=delta(q,p,"CylinderWorkJ"),
                     trapped_delta_mg=1e6*delta(q,p,"FreshTrappedKg"))
        angle = "AngleDeg"
        r["exhaust_trace_max_delta_Pa"] = port(q,p,angle,"ExhaustPortPressurePa",720 if four else 360)
        r["intake_trace_max_delta_Pa"] = port(q,p,angle,"IntakePortPressurePa" if four else "IntakeValvePressurePa",720 if four else 360)
    rows.append(r)
with a.output.open("w",newline="",encoding="utf-8") as stream:
    keys=list(dict.fromkeys(k for row in rows for k in row))
    writer=csv.DictWriter(stream,fieldnames=keys)
    writer.writeheader()
    writer.writerows(rows)
