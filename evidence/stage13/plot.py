"""Plot measured bench points and 2T mesh sensitivity with Matplotlib."""

import csv
from pathlib import Path
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt


def table(name):
    with (source / name).open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


source, target = (Path(arg) for arg in sys.argv[1:])
target.mkdir(parents=True, exist_ok=True)
new, old = table('compared.csv'), table('aperture-v1.csv')
colors = ['#163d59', '#197c7a', '#b86630', '#6f5394']
fig, axes = plt.subplots(1, 2, figsize=(10.2, 4), layout='constrained')
for ax, direction, title in zip(axes, ('1', '0'), ('Depósito → conducto', 'Conducto → depósito')):
    ref = [r for r in new if r['feeding'] == direction and r['alpha'] == '0.3' and r['cells'] == '128']
    historic = [r for r in old if r['feeding'] == direction and r['area_fraction'] == '0.3']
    finest = {r['pressure_ratio']: r for r in sorted(historic, key=lambda row: int(row['cells']))}
    previous = sorted(finest.values(), key=lambda row: float(row['pressure_ratio']))
    x = lambda rows, key: [1e5*(float(r[key])-1) for r in rows]
    y = lambda rows, key: [abs(float(r[key])) for r in rows]
    ax.loglog(x(ref, 'ratio'), y(ref, 'reference_kg_s'), '--', color=colors[1], label='Referencia independiente')
    ax.loglog(x(ref, 'ratio'), y(ref, 'mass_port_kg_s'), 'o', markerfacecolor='none', color=colors[0], label='ports-v2')
    ax.loglog(x(previous, 'pressure_ratio'), y(previous, 'mass_aperture_kg_s'), 's', color=colors[2], label='ports-v1 (64–128 celdas)')
    ax.set(title=title, xlabel='Diferencia de presión de depósitos (Pa)', ylabel='Caudal másico (kg/s)')
    ax.grid(alpha=.3, which='both')
axes[0].legend(loc='lower right', fontsize=8)
fig.suptitle('Descarga con área efectiva del 30 % · aire ideal')
fig.savefig(target / 'etapa13-banco.png', dpi=175)
plt.close(fig)

fig, ax = plt.subplots(figsize=(7.4, 4), layout='constrained')
rows = table('2t-summary.csv')
for scale, color in zip((1, 2, 4, 8), colors):
    subset = sorted((int(r['rpm']), float(r['torque_nm'])) for r in rows if r['torque_nm'] and
                    int(r['intake_cells']) == 12*scale and int(r['exhaust_cells']) == 32*scale)
    ax.plot(*zip(*subset), 'o-', markersize=4, linewidth=1, color=color, label=f'×{scale}')
ax.set(title='Monocilíndrico 2T · par al cigüeñal por malla', xlabel='Régimen (RPM)', ylabel='Par al cigüeñal (N·m)')
ax.grid(alpha=.3)
ax.legend(ncol=4, loc='upper right')
fig.savefig(target / 'etapa13-2t-malla.png', dpi=175)
