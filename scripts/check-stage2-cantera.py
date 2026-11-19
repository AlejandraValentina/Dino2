"""Independent thermochemistry and dissociation assessment; optional Cantera 3.2.0.

Cross-code verification is not experimental engine validation. Equilibrium UV
comparisons are diagnostic states, never corrections applied to Dino2's curves.
"""
import csv
import json
import math
from pathlib import Path
import cantera as ct

root=Path(__file__).resolve().parents[1]
data=root/"artifacts/stage2"
all_species={s.name:s for s in ct.Species.list_from_file("nasa_gas.yaml")}
names=["C8H18,isooctane","O2","N2","CO2","H2O"]
gas=ct.Solution(thermo="ideal-gas",species=[all_species[s] for s in names])
expanded=ct.Solution(thermo="ideal-gas",species=[all_species[s] for s in names+["CO","H2","OH","O","H","NO"]])
vectors=json.loads((data/"thermo-vectors.json").read_text())
maximum={"cp_J_kgK":0.,"h_J_kg":0.,"u_J_kg":0.}
for row in vectors:
    gas.TPY=row["t"],101325,{row["species"]:1}
    for key,actual,expected in (("cp_J_kgK",gas.cp_mass,row["cp"]),("h_J_kg",gas.enthalpy_mass,row["h"]),("u_J_kg",gas.int_energy_mass,row["u"])):
        maximum[key]=max(maximum[key],abs(actual-expected))
assert maximum["cp_J_kgK"]<1e-5
# Dino2 explicitly makes the NASA sensible integral continuous at 1000 K;
# original NASA interval fits have small independent integration-constant jumps.
assert maximum["h_J_kg"]<.2 and maximum["u_J_kg"]<.2

comparisons=[]
for row in json.loads((data/"constant-volume.json").read_text()):
    t=row["initialTemperatureK"]
    pressure={300.:1e5,600.:2e6,800.:3e6}[t]
    fresh=dict(zip(names,row["fresh"])); products=dict(zip(names,row["products"]))
    gas.TPY=t,pressure,fresh
    u,v=gas.int_energy_mass,1/gas.density
    gas.UVY=u,v,products
    frozen=gas.T
    assert abs(frozen-row["productTemperatureK"])<.01
    expanded.TPY=t,pressure,fresh
    expanded.equilibrate("UV",max_steps=3000)
    comparisons.append({"initial_T_K":t,"initial_p_Pa":pressure,"frozen_T_K":frozen,"equilibrium_T_K":expanded.T,
        "temperature_reduction_K":frozen-expanded.T,"CO_mole_fraction":expanded["CO"].X[0],"H2_mole_fraction":expanded["H2"].X[0]})

lines=(data/"complete-3000.trace.csv").read_text().splitlines()
trace=list(csv.DictReader(line for line in lines if not line.startswith("#")))
diagnostics=json.loads((data/"complete-diagnostics.json").read_text())
config=json.loads(next(line.split("=",1)[1] for line in lines if line.startswith("# configuration_SI=")))
burn_end=360-config["combustion"]["sparkAdvanceDeg"]+6*3000*config["combustion"]["ignitionDelayS"]+diagnostics["BurnDurationDeg"]
# Select the first completely reacted, post-burn state, avoiding equilibrium
# combustion of still-unburned fuel being mistaken for a dissociation effect.
sample=next(r for r in trace if float(r["angle_deg"])>=burn_end-1e-7 and abs(float(r["fuel_kg"]))<1e-12)
masses={s:float(sample[k]) for s,k in zip(names,["fuel_kg","oxygen_kg","nitrogen_kg","carbon_dioxide_kg","water_kg"])}
masses={k:max(0,v) for k,v in masses.items()} # only ~1e-20 kg molecular-roundoff in a complete-reaction diagnostic
expanded.TDY=float(sample["temperature_K"]),float(sample["mass_kg"])/float(sample["volume_m3"]),masses
frozen_t,frozen_p=expanded.T,expanded.P
u_before,v_before=expanded.int_energy_mass,1/expanded.density
expanded.equilibrate("UV",max_steps=3000)
assert abs(expanded.int_energy_mass-u_before)<.01 and abs(1/expanded.density-v_before)<1e-10
instant={"angle_deg":float(sample["angle_deg"]),"frozen_T_K":frozen_t,"equilibrium_T_K":expanded.T,
    "frozen_p_Pa":frozen_p,"equilibrium_p_Pa":expanded.P,"temperature_reduction_K":frozen_t-expanded.T,
    "CO_mole_fraction":expanded["CO"].X[0],"H2_mole_fraction":expanded["H2"].X[0],
    "scope":"Instantaneous UV equilibrium of fully burned products; not an equilibrium engine cycle"}
result={"cantera_version":ct.__version__,"classification":"Independent numerical verification, NOT experimental validation",
    "maximum_property_difference":maximum,"constant_volume_cases":comparisons,"post_burn_engine_state":instant}

# Independent open-cycle flux audit, using Cantera thermodynamics and a Mach-based
# subsonic flux expression rather than the implementation's pressure-power form.
lines=(data/"reactive-3000.trace.csv").read_text().splitlines()
config=json.loads(next(line.split("=",1)[1] for line in lines if line.startswith("# configuration_SI=")))
rows=[{k:float(v) for k,v in row.items()} for row in csv.DictReader(s for s in lines if not s.startswith("#"))]
mw=[gas.molecular_weights[gas.species_index(s)] for s in names]
stoich=12.5*(mw[1]+79/21*mw[2])/mw[0]
phi=stoich/config["combustion"]["airFuelRatio"]
fresh_gas=ct.Solution(thermo="ideal-gas",species=[all_species[s] for s in names])
exhaust_gas=ct.Solution(thermo="ideal-gas",species=[all_species[s] for s in names])
fresh_gas.TPX=config["intake"]["temperatureK"],config["intake"]["pressurePa"],{"C8H18,isooctane":1,"O2":12.5/phi,"N2":12.5/phi*79/21}
exhaust_gas.TPX=config["exhaust"]["temperatureK"],config["exhaust"]["pressurePa"],{"CO2":8,"H2O":9,"O2":max(0,12.5/phi-12.5),"N2":12.5/phi*79/21}
species_keys=["fuel_kg","oxygen_kg","nitrogen_kg","carbon_dioxide_kg","water_kg"]

def area(valve,angle):
    phase=(angle-valve["opensDeg"])%720
    duration=valve["closesDeg"]-valve["opensDeg"]
    lift=valve["maximumLiftM"]*math.sin(math.pi*phase/duration)**2 if phase<duration else 0
    return valve["count"]*valve["dischargeCoefficient"]*min(math.pi*valve["diameterM"]*lift,math.pi*valve["diameterM"]**2/4)

def flow(effective_area,reservoir,cylinder):
    upstream,downstream=(reservoir,cylinder) if reservoir.P>cylinder.P else (cylinder,reservoir)
    gamma=upstream.cp_mass/upstream.cv_mass
    ratio=downstream.P/upstream.P
    mach=min(1.,math.sqrt(max(0,2/(gamma-1)*(ratio**(-(gamma-1)/gamma)-1))))
    ts=upstream.T/(1+(gamma-1)*mach*mach/2)
    ps=upstream.P*(ts/upstream.T)**(gamma/(gamma-1))
    r=ct.gas_constant/upstream.mean_molecular_weight
    return (1 if upstream is reservoir else -1)*effective_area*ps/(r*ts)*mach*math.sqrt(gamma*r*ts)

adv=[[] for _ in names]; heat=[]; enthalpy=[]
g=config["geometry"]; piston_area=math.pi*g["boreM"]**2/4
clearance=piston_area*g["strokeM"]/(g["compressionRatio"]-1)
for row in rows:
    masses=[row[k] for k in species_keys]
    gas.TPY=row["temperature_K"],row["pressure_Pa"],dict(zip(names,masses))
    assert abs(gas.density-row["mass_kg"]/row["volume_m3"])<1e-8
    assert abs(gas.int_energy_mass*row["mass_kg"]-row["internal_energy_J"])<1e-4
    mi=flow(area(config["intakeValve"],row["angle_deg"]),fresh_gas,gas)
    me=flow(area(config["exhaustValve"],row["angle_deg"]),exhaust_gas,gas)
    assert abs(mi-row["intake_flow_kg_s"])<1e-9 and abs(me-row["exhaust_flow_kg_s"])<1e-9
    for i in range(5):adv[i].append(mi*(fresh_gas.Y[i] if mi>=0 else gas.Y[i])+me*(exhaust_gas.Y[i] if me>=0 else gas.Y[i]))
    enthalpy.append(mi*(fresh_gas.enthalpy_mass if mi>=0 else gas.enthalpy_mass)+me*(exhaust_gas.enthalpy_mass if me>=0 else gas.enthalpy_mass))
    wall_area=2*piston_area+math.pi*g["boreM"]*(row["volume_m3"]/piston_area)
    coefficient=130*row["volume_m3"]**(-.06)*(row["pressure_Pa"]/1e5)**.8*row["temperature_K"]**(-.4)*(2*g["strokeM"]*3000/60+1.4)**.8
    heat.append(config["thermal"]["hohenbergMultiplier"]*coefficient*wall_area*(row["temperature_K"]-config["thermal"]["wallTemperatureK"]))

def integrate(values):
    return math.fsum((values[i]+values[i-1])/2*(rows[i]["angle_deg"]-rows[i-1]["angle_deg"])/18000 for i in range(1,len(rows)))

at_ivc=min(rows,key=lambda r:abs(r["angle_deg"]-config["intakeValve"]["closesDeg"]))
at_evo=min(rows,key=lambda r:abs(r["angle_deg"]-config["exhaustValve"]["opensDeg"]))
consumed=at_ivc["fuel_kg"]-at_evo["fuel_kg"]
reaction=[-1,-12.5*mw[1]/mw[0],0,8*mw[3]/mw[0],9*mw[4]/mw[0]]
species_residual=[rows[-1][k]-rows[0][k]-integrate(adv[i])-consumed*reaction[i] for i,k in enumerate(species_keys)]
work=math.fsum((rows[i]["pressure_Pa"]+rows[i-1]["pressure_Pa"])/2*(rows[i]["volume_m3"]-rows[i-1]["volume_m3"]) for i in range(1,len(rows)))
energy=rows[-1]["internal_energy_J"]-rows[0]["internal_energy_J"]-integrate(enthalpy)+integrate(heat)+work
assert max(abs(x) for x in species_residual)<1e-7
assert abs(energy)<1 and abs(work-rows[-1]["work_J"])<.03
result["open_cycle_audit"]={"samples":len(rows),"species_residuals_kg":dict(zip(names,species_residual)),"maximum_species_residual_kg":max(abs(x) for x in species_residual),
    "total_energy_residual_J":energy,"independent_work_J":work,"solver_work_J":rows[-1]["work_J"],"chemical_heat_not_added_twice":True}
(data/"cantera-assessment.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
print(json.dumps(result,indent=2))
print("PASS independent thermochemistry; equilibrium assessment completed")
