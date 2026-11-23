"""Derive engine statistics from the unaltered JSON traces. Python 3, standard library."""

from bisect import bisect_right
import cmath
import csv
import json
import math
from pathlib import Path
import sys


def documents(root, *groups):
    for group in groups:
        for path in sorted((root / group).glob('*.json')):
            yield path.stem, json.loads(path.read_text(encoding='utf-8-sig'))


def write(name, rows):
    if not rows:
        return
    with (root / (name + '.csv')).open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def flow(trace, key, rpm, positive=True):
    vals = [max(0, (1 if positive else -1) * p[key]) for p in trace]
    return 1e6 * sum((b['AngleDeg'] - a['AngleDeg']) * (qa + qb) / 2
                     for a, b, qa, qb in zip(trace, trace[1:], vals, vals[1:])) / (6 * rpm)


def peak_to_peak(trace, key):
    vals = [p[key] for p in trace]
    return max(vals) - min(vals)


def harmonics(trace, angle_key, value_key, revolution):
    # Periodic linear interpolation is used only to put saved samples on a uniform grid.
    samples = {s[angle_key]: s[value_key] for s in trace}
    start = min(samples)
    samples[start + revolution] = samples[start]
    times = sorted(a for a in samples if a <= start + revolution)
    count = 512
    values = []
    for k in range(count):
        a = start + k * revolution / count
        i = bisect_right(times, a)
        l, h = times[i - 1:i + 1]
        values.append(samples[l] + (samples[h] - samples[l]) * (a - l) / (h - l))
    return [2 / count * sum(v * cmath.exp(-2j * math.pi * n * k / count) for k, v in enumerate(values))
            for n in range(1, 33)]


def first_linear_mode(volume, trunk):
    # Compact plenum compliance and two equal, closed-end inlet runners, with
    # a pressure node at the outer mouth of the trunk. Linear, uniform c.
    c, runner = 358.8, .38
    a_runner, a_trunk = (math.pi * d*d/4 for d in (.04, .07))
    low, high = 0.0, min(c/(4*runner), c/(2*trunk)) * .9999
    for _ in range(50):
        mid = (low + high) / 2
        k = 2 * math.pi * mid / c
        residual = k*volume + 2*a_runner*math.tan(k*runner) - a_trunk/math.tan(k*trunk)
        if residual > 0:
            high = mid
        else:
            low = mid
    return (low + high) / 2


root = Path(sys.argv[1])
mono = []
for case, data in documents(root, 'mono'):
    point = data['result']['Points'][0]
    tr = point['Trace']
    ivc = data['configuration']['IntakeValve']['ClosesDeg']
    close = min(tr, key=lambda a: abs(a['AngleDeg'] - ivc))
    intake = [q for q in data['throat'] if q[1] == 1]
    exhaust = [q for q in data['throat'] if q[1] == 2]
    mono.append(dict(case=case, torque_nm=point['BrakeTorqueNm'], VE=point['VolumetricEfficiency'],
        ivc_fresh_mg=1e6*close['FreshMassKg'], gross_j=point['GrossWorkJ'], pump_j=point['PumpingWorkJ'],
        friction_j=point['FrictionWorkJ'], residual=point['Chemistry']['SocResidualFraction'],
        ca50_deg=point['Chemistry']['Ca50Deg'], intake_mg=flow(tr, 'IntakeFlowKgPerS', 4500),
        return_mg=flow(tr, 'IntakeFlowKgPerS', 4500, False),
        intake_p2p_kpa=peak_to_peak(tr, 'IntakePortPressurePa') / 1000,
        exhaust_p2p_kpa=peak_to_peak(tr, 'ExhaustPortPressurePa') / 1000,
        intake_throat_mach=max((s[5] for s in intake), default=None),
        exhaust_throat_mach=max((s[5] for s in exhaust), default=None),
        intake_max_gap_kpa=max((abs(s[3]-s[6])/1000 for s in intake), default=None),
        exhaust_max_gap_kpa=max((abs(s[3]-s[6])/1000 for s in exhaust), default=None)))
write('mono-summary', mono)

network = []
for case, data in documents(root, 'network', 'network-fine'):
    point = data['result']['Points'][0]
    tr = point['Cycle']
    node = harmonics(tr, 'CrankAngleDeg', 'IntakeJunctionPa', 720)
    trunk = harmonics(tr, 'CrankAngleDeg', 'IntakeTrunkPa', 720)
    f = point['Rpm'] / 120
    row = dict(case=case, scale=data['scale'], volume_l=1000*data['volume'], length=data['trunkLength'],
        power_w=point['Metrics']['BrakePowerW'], VE=point['Metrics']['VolumetricEfficiency'],
        intake_p2p_kpa=peak_to_peak(tr, 'IntakeJunctionPa') / 1000,
        first_linear_mode_hz=first_linear_mode(data['volume'], data['trunkLength']),
        node_dominant_hz=f * (1 + max(range(len(node)), key=lambda i: abs(node[i]))),
        node_h2_kpa=abs(node[1])/1000, node_minus_trunk_h2_deg=math.degrees(cmath.phase(node[1] * trunk[1].conjugate())),
        periodic=point['PeriodicError'], cycles=point['Cycles'], elapsed_ms=point['ElapsedMilliseconds'])
    for c in point['Cylinders']:
        n = c['CylinderNumber']
        row.update({f'port{n}_p2p_kpa': peak_to_peak(c['Trace'], 'IntakePortPressurePa')/1000,
            f'net_flow{n}_mg': 1e6*c['NetIntakeMassKg'], f'fresh{n}_mg': 1e6*c['FreshAirAtIntakeCloseKg'],
            f'work{n}_j': c['NetWorkJ']})
    network.append(row)
write('network-summary', network)

two = []
for case, data in documents(root, 'two', 'two-fine'):
    run = data['result']['Runs'][0]
    rpm, p = run['Rpm'], run['Point']
    row = dict(case=case, rpm=rpm, intake_cells=data['c']['IntakeDuct']['Cells'], exhaust_cells=data['c']['Expansion']['Cells'],
        status=run['Status'], torque_nm=None, power_w=None, fresh_mg=None, trapping=None, scavenging=None,
        crankcase_j=None, exhaust_p2p_kpa=None, intake_p2p_kpa=None, exhaust_dominant_hz=None,
        reed_mg=None, transfer_mg=None, cycles=None, periodic=None, accepted_steps=None, mass_kg=None, energy_j=None)
    if p:
        tr = p['Trace']
        ex = harmonics(tr, 'AngleDeg', 'ExhaustPortPressurePa', 360)
        row.update(torque_nm=p['ShaftTorqueNm'], power_w=p['ShaftPowerW'], fresh_mg=p['FreshTrappedKg']*1e6,
            trapping=p['TrappingEfficiency'], scavenging=p['ScavengingEfficiency'], crankcase_j=p['CrankcaseWorkJ'],
            exhaust_p2p_kpa=peak_to_peak(tr, 'ExhaustPortPressurePa')/1000,
            intake_p2p_kpa=peak_to_peak(tr, 'IntakeValvePressurePa')/1000,
            exhaust_dominant_hz=(rpm/60) * (1 + max(range(len(ex)), key=lambda i: abs(ex[i]))),
            reed_mg=flow(tr, 'ReedKgPerS', rpm), transfer_mg=flow(tr, 'TransferKgPerS', rpm),
            cycles=p['Cycles'], periodic=p['PeriodicError'], accepted_steps=p['TotalAcceptedSteps'],
            mass_kg=p['Balance']['MassKg'], energy_j=p['Balance']['EnergyJ'])
    two.append(row)
write('2t-summary', two)
