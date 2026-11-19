"""Counterfactuals in a dedicated detached checkout at fac1ffc. Run from the repo root."""
from pathlib import Path
import json, subprocess, csv
root=Path.cwd()
checkout=root/'.tools/stage4-boundary-study'
dest=root/'artifacts/stage4-closure'
dotnet=root/'.tools/dotnet/dotnet.exe'
mesh=checkout/'src/Dino2.Core/GasDynamics/DuctMesh.cs'
rec=checkout/'src/Dino2.Core/GasDynamics/DuctReconstruction.cs'
def checked_in(file):
    name = file.relative_to(checkout).as_posix()
    return subprocess.check_output(['git', 'show', f'fac1ffc:{name}'], cwd=root).decode('utf-8-sig')

original_mesh = checked_in(mesh)
original_rec = checked_in(rec)
project=json.loads((root/'examples/4t-ducts-muscl-ab.json').read_text())
out=[]
try:
    for mode in ['constant-boundaries','constant-interior']:
        mesh.write_text(original_mesh.replace('if (faces is null)\n                {','if (faces is null || Cells > 0)\n                {') if mode=='constant-interior' else original_mesh,encoding='utf-8')
        rec.write_text(original_rec.replace('if (faces is not null)\n        {','if (faces is not null && Cells == 0)\n        {',1) if mode=='constant-boundaries' else original_rec,encoding='utf-8')
        subprocess.run([str(dotnet),'build','src/Dino2.Cli','-c','Release','-v','quiet'],cwd=checkout,check=True,stdout=subprocess.DEVNULL)
        for cells in ([24,48] if mode=='constant-boundaries' else [24]):
            cfg=json.loads(json.dumps(project))
            for key in ['current','original']:
                c=cfg['project'][key]
                c['speeds']={'start':4000,'end':4000,'step':1}
                c['ducts']['intake']['cells']=cells
                c['ducts']['exhaust']['cells']=cells*4//3
            input_file=dest/(mode+'.json')
            input_file.write_text(json.dumps(cfg),encoding='utf-8')
            output=dest/f'{mode}-{cells}.csv'
            subprocess.run([str(dotnet),str(checkout/'src/Dino2.Cli/bin/Release/net10.0/Dino2.Cli.dll'),'--4t',str(input_file),str(output)],check=True,stdout=subprocess.DEVNULL)
            with output.with_suffix('.comparison.csv').open() as f:
                point=next(csv.DictReader(x for x in f if not x.startswith('#')))
            out.append(dict(mode=mode,cells=cells,deltaW=float(point['power_difference_W']),deltaVe=float(point['volumetric_efficiency_difference'])))
            print(out[-1],flush=True)
finally:
    mesh.write_text(original_mesh, encoding='utf-8')
    rec.write_text(original_rec, encoding='utf-8')

(dest/'boundary-counterfactual.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
