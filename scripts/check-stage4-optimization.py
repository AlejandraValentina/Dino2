from pathlib import Path
import csv, json, re, subprocess

root = Path.cwd()
dist = root / 'artifacts/stage4-closure'
dotnet = root / '.tools/dotnet/dotnet.exe'
cli = root / 'src/Dino2.Cli/bin/Release/net10.0/Dino2.Cli.dll'
old = root / '.tools/stage4-preopt/Dino2.Cli.dll'
project = json.loads((root / 'examples/4t-ducts-muscl-ab.json').read_text(encoding='utf-8'))

def point(path):
    with path.open() as f:
        return next(csv.DictReader(x for x in f if not x.startswith('#')))

def solve(binary, cfg, name):
    input_path = dist / f'{name}.json'
    output = dist / f'{name}.csv'
    input_path.write_text(json.dumps(cfg), encoding='utf-8')
    result = subprocess.check_output([str(dotnet), str(binary), '--4t', str(input_path), str(output)], text=True, encoding='utf-8')
    value = float(re.search(r'([\d.,]+) ms\.', result).group(1).replace(',', '.'))
    return value, point(output)

one = dict(model=project['model'], schemaVersion=1, project=dict(current=project['project']['current'], original=None))
one['project']['current']['speeds'] = dict(start=4000, end=4000, step=1)
one['project']['current']['ducts']['intake']['cells'] = 48
one['project']['current']['ducts']['exhaust']['cells'] = 64
measurements = []
for run in range(2):
    for label, binary in [('before', old), ('after', cli)]:
        timing, row = solve(binary, one, f'opt-{label}-{run}')
        result = dict(case=label, run=run, milliseconds=timing, brakeW=float(row['brake_power_W']),
                      workJ=float(row['net_work_J']), ve=float(row['volumetric_efficiency']), steps=int(row['accepted_steps']))
        measurements.append(result)
        print(result,flush=True)
assert max(r['brakeW'] for r in measurements)-min(r['brakeW'] for r in measurements) < .005
(dist / 'optimization.json').write_text(json.dumps(measurements, indent=2), encoding='utf-8')

paired = []
original = json.loads((root / 'examples/4t-ducts-muscl-ab.json').read_text(encoding='utf-8'))
for cells in (24, 48, 96):
    for match_dx in ((True,) if cells == 96 else (False, True)):
        cfg = json.loads(json.dumps(original))
        for key in ('current','original'):
            end = cfg['project'][key]
            end['speeds'] = dict(start=4000,end=4000,step=1)
            end['ducts']['intake']['cells'] = round(cells * (.55/.35)) if key=='current' and match_dx else cells
            end['ducts']['exhaust']['cells'] = cells*4//3
        name=f'dx-{cells}-{match_dx}'
        solve(cli,cfg,name)
        row=point(dist / f'{name}.comparison.csv')
        paired.append(dict(cellsA=cells, cellsB=cfg['project']['current']['ducts']['intake']['cells'],
                           deltaW=float(row['power_difference_W']),deltaVe=float(row['volumetric_efficiency_difference'])))
        print(paired[-1],flush=True)
(dist / 'matched-spacing.json').write_text(json.dumps(paired,indent=2),encoding='utf-8')
