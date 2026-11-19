using System.Diagnostics;
using System.Text.Json;
using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;
using Dino2.Storage;

string output = "artifacts/stage2"; Directory.CreateDirectory(output);
var options = new JsonSerializerOptions { WriteIndented = true };
void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, options));
var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Near(double a, double b, double tolerance) => Assert(double.IsFinite(a) && Math.Abs(a - b) <= tolerance, $"Expected {b:R}, actual {a:R}");
void Reject(Action action)
{
    try { action(); } catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidDataException or JsonException) { return; }
    throw new Exception("Invalid input accepted");
}
var c = ThermochemicalOptions.Example;
var one = c with { Speeds = new(3000, 3000, 1) };
FourStrokePoint? baseline = null;

Test("Stoichiometric species, elemental conservation and derived chemical energy", () =>
{
    Near(Enumerable.Range(0,5).Sum(SpeciesThermodynamics.ReactionMassRatio), 0, 1e-14);
    for (int e = 0; e < 4; e++)
        Near(Enumerable.Range(0,5).Sum(i => SpeciesThermodynamics.ReactionMassRatio(i) * SpeciesThermodynamics.ElementMassFraction(i,e)), 0, 1e-14);
    foreach (double phi in new[] { .8, 1.0 })
    {
        var fresh = SpeciesThermodynamics.FreshMixture(SpeciesThermodynamics.StoichiometricAfr / phi);
        var products = SpeciesThermodynamics.ExhaustMixture(SpeciesThermodynamics.StoichiometricAfr / phi);
        Near(fresh.Sum(), 1, 1e-14); Near(products.Sum(), 1, 1e-14);
        Assert(products.All(x => x >= 0) && products[0] == 0, "Products contain negative species or fuel");
        for (int e = 0; e < 4; e++) Near(Enumerable.Range(0,5).Sum(i => (fresh[i] - products[i]) * SpeciesThermodynamics.ElementMassFraction(i,e)), 0, 1e-14);
    }
    Assert(SpeciesThermodynamics.LowerHeatingValue is > 43e6 and < 46e6, "Gaseous iso-octane LHV outside reference interval");
    Near(SpeciesThermodynamics.H(3,298.15) * SpeciesThermodynamics.MolecularWeight(3), -393510, 100);
    Near(SpeciesThermodynamics.H(4,298.15) * SpeciesThermodynamics.MolecularWeight(4), -241826, 100);
});
Test("Temperature-dependent mixture U, cp, R and inversion; NASA reference vectors", () =>
{
    var vectors = new List<object>();
    foreach (double t in new[] { 250.0, 298.15, 700, 999, 1001, 1800, 2800, 5000 })
    for (int i = 0; i < 5; i++)
    {
        Near((SpeciesThermodynamics.H(i,t+.001)-SpeciesThermodynamics.H(i,t-.001))/.002, SpeciesThermodynamics.Cp(i,t), .001);
        vectors.Add(new { species=SpeciesThermodynamics.Name(i),t,cp=SpeciesThermodynamics.Cp(i,t),h=SpeciesThermodynamics.H(i,t),u=SpeciesThermodynamics.U(i,t) });
    }
    var fresh = SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio);
    var products = SpeciesThermodynamics.ExhaustMixture(c.Combustion.AirFuelRatio);
    foreach (var mass in new[] { fresh, products, fresh.Zip(products,(a,b)=>(a+b)/2).ToArray() })
    foreach (double t in new[] { 250.0, 320, 999.9, 1000.1, 2400, 5000 })
        Near(SpeciesThermodynamics.Temperature(mass, SpeciesThermodynamics.Energy(mass,t)),t,1e-7);
    Assert(Math.Abs(SpeciesThermodynamics.GasConstant(fresh)-SpeciesThermodynamics.GasConstant(products))>1, "Composition does not change R");
    Save("thermo-vectors.json", vectors);
});
Test("Closed constant-volume reaction conserves total chemical energy without extra LHV", () =>
{
    var fresh = SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio);
    var products = SpeciesThermodynamics.ExhaustMixture(c.Combustion.AirFuelRatio);
    var cases = new List<object>();
    foreach (double t in new[] { 300.0, 600, 800 })
    {
        double energy = SpeciesThermodynamics.Energy(fresh,t);
        double burnedT = SpeciesThermodynamics.Temperature(products,energy);
        Near(SpeciesThermodynamics.Energy(products,burnedT), energy, 1e-5);
        Assert(burnedT > 2200 && burnedT < 4000, "Frozen product flame temperature implausible");
        cases.Add(new { initialTemperatureK=t, totalSpecificEnergy=energy, productTemperatureK=burnedT, fresh, products });
    }
    Save("constant-volume.json",cases);
});
Test("Laminar correlation and conditioned duration respond to temperature, pressure, dilution and RPM", () =>
{
    double speed = ConditionedBurn.LaminarSpeed(600,2e6,1,.05);
    Assert(ConditionedBurn.LaminarSpeed(650,2e6,1,.05)>speed, "Temperature trend wrong");
    Assert(ConditionedBurn.LaminarSpeed(600,3e6,1,.05)<speed, "Pressure trend wrong");
    Assert(ConditionedBurn.LaminarSpeed(600,2e6,1,.1)<speed, "Dilution trend wrong");
    Assert(ConditionedBurn.DurationSeconds(c,6000,600,2e6,.05)<ConditionedBurn.DurationSeconds(c,3000,600,2e6,.05), "No RPM dependence of burn duration");
    Assert(ConditionedBurn.DurationSeconds(c with{Combustion=c.Combustion with{AirFuelRatio=SpeciesThermodynamics.StoichiometricAfr/.8}},3000,600,2e6,.05)>
        ConditionedBurn.DurationSeconds(c,3000,600,2e6,.05),"Reference normalization canceled the lean-mixture flame-speed response");
    double[] phi = [.8,1,1.2], measured = [.1925,.2700,.2763];
    Save("published-correlation-reproduction.json", phi.Select((f,i)=>new { phi=f, publishedFitReferenceSpeed=measured[i], implementedCorrelationSpeed=ConditionedBurn.LaminarSpeed(298,101325,f,0),
        note="Same-paper fitted data, NOT independent engine validation" }).ToArray());
});
Test("Open reactive cycle closes mass, all species, elements and total energy", () =>
{
    baseline = ReactiveSimulator.Simulate(one,3000);
    var p=baseline; var d=p.Chemistry!;
    Near(p.MassResidualKg,0,1e-12); Near(p.EnergyResidualJ,0,1e-7);
    Near(d.MaximumSpeciesResidualKg,0,1e-12); Near(d.MaximumElementResidualKg,0,1e-12);
    Near(p.HeatReleasedJ,d.FuelConsumedKg*SpeciesThermodynamics.LowerHeatingValue,1e-9);
    foreach (var sample in p.Trace)
    {
        var masses=sample.Composition.ToArray();
        Near(masses.Sum(),sample.MassKg,1e-14);
        Assert(masses.All(v=>v>=-1e-13),"Negative species");
        Near(sample.PressurePa*sample.VolumeM3,sample.MassKg*SpeciesThermodynamics.GasConstant(masses)*sample.TemperatureK,1e-8);
        Near(SpeciesThermodynamics.Energy(masses,sample.TemperatureK),sample.InternalEnergyJ,1e-8);
    }
    Assert(p.PeriodicError<c.Solver.PeriodicTolerance,"Species periodicity not reached");
    Assert(d.Ca10Deg<d.Ca50Deg&&d.Ca50Deg<d.Ca90Deg,"Burn phasing unordered");
    Console.WriteLine($"Reactive 3000: W={p.NetWorkJ:R}J Pb={p.BrakePowerW:R}W Tpeak={p.PeakTemperatureK:F2}K duration={d.BurnDurationDeg:F3}deg");
});
Test("Temporal and cycle convergence; independent p dV quadrature", () =>
{
    var grid=new List<object>();
    FourStrokePoint? reference=null,medium=null;
    foreach(double step in new[]{1.0,.5,.25,.125})
    {
        var p=ReactiveSimulator.Simulate(one with {Solver=c.Solver with{MaximumStepDeg=step,RelativeTolerance=2e-8}},3000);
        grid.Add(new{step,p.NetWorkJ,p.PeakPressurePa,p.VolumetricEfficiency,p.Cycles});
        if(step==.5)medium=p; if(step==.125)reference=p;
    }
    Near(medium!.NetWorkJ,reference!.NetWorkJ,.05);
    Near(medium.PeakPressurePa/reference.PeakPressurePa,1,.001);
    double work=0;
    for(int i=1;i<reference.Trace.Count;i++) work+=(reference.Trace[i].PressurePa+reference.Trace[i-1].PressurePa)/2*(reference.Trace[i].VolumeM3-reference.Trace[i-1].VolumeM3);
    Near(work,reference.NetWorkJ,.02);
    var periodic=new List<object>();
    foreach(double tolerance in new[]{1e-4,1e-6,1e-8})
    {
        var p=ReactiveSimulator.Simulate(one with{Solver=c.Solver with{PeriodicTolerance=tolerance}},3000);
        Assert(p.PeriodicError<tolerance,"Requested cycle tolerance not met");
        Near(p.NetWorkJ,reference.NetWorkJ,.05);
        periodic.Add(new{tolerance,p.Cycles,p.PeriodicError,p.NetWorkJ});
    }
    Save("convergence.json",new{grid,periodic});
});
Test("Files preserve model and declared evidence; reject incompatible fuel/energy", () =>
{
    string path=Path.Combine(output,"roundtrip.json");
    FourStrokeFiles.Save(path,new(c,FourStrokeConfiguration.Example));
    var loaded=FourStrokeFiles.Load(path);
    Assert(loaded.Current==c&&loaded.Original==FourStrokeConfiguration.Example,"Model lost on disk");
    Reject(()=> (c with{Combustion=c.Combustion with{AirFuelRatio=12}}).Validate());
    Reject(()=> (c with{Combustion=c.Combustion with{LowerHeatingValueJPerKg=43e6}}).Validate());
    Reject(()=> (c with{Thermochemistry=c.Thermochemistry! with{CombustionEvidence=new(EvidenceStatus.Calibrated,"some source","","no range")}}).Validate());
    var p=baseline!; var result=new FourStrokeResult(one,[p],0){Model=ThermochemicalOptions.ModelId};
    FourStrokeFiles.Export(Path.Combine(output,"point.csv"),result);
    FourStrokeFiles.ExportTrace(Path.Combine(output,"point.trace.csv"),result,3000);
    string text=File.ReadAllText(Path.Combine(output,"point.csv"));
    Assert(text.Contains("maximumSpeciesResidualKg")&&text.Contains("chemical_energy="),"Missing energy/species provenance");
});
Test("Unreacted fuel carries chemical enthalpy; cancellation and nonconvergence stay explicit", () =>
{
    var motored=EngineSimulation.Run(one with{Combustion=c.Combustion with{HeatReleaseEfficiency=0}}).Points[0];
    Near(motored.HeatReleasedJ,0,1e-10);Near(motored.EnergyResidualJ,0,1e-7);
    Assert(motored.BrakePowerW<0&&motored.Trace.All(s=>s.Composition.FuelKg>=-1e-13),"Unburned fuel disappeared or generated work");
    using var cancel=new CancellationTokenSource();cancel.Cancel();
    bool cancelled=false;
    try{EngineSimulation.Run(one,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
    Assert(cancelled,"Cancellation ignored");
    Reject(()=>EngineSimulation.Run(one with{Solver=c.Solver with{MaximumCycles=3}}));
    Reject(()=>FourStrokeSimulator.Run(one));
});
Test("Reproducible reference-v1, frozen-v2 and conditioned-v2 comparisons and sensitivities", () =>
{
    var legacy=EngineSimulation.Run(c with{Thermochemistry=null});
    var frozen=EngineSimulation.Run(c with{Thermochemistry=c.Thermochemistry! with{BurnLaw=BurnLaw.PrescribedWiebe}});
    var reactive=EngineSimulation.Run(c);
    FourStrokeFiles.Export(Path.Combine(output,"reference-v1-matched.csv"),legacy);
    FourStrokeFiles.Export(Path.Combine(output,"frozen-prescribed.csv"),frozen);
    FourStrokeFiles.Export(Path.Combine(output,"reactive.csv"),reactive);
    FourStrokeFiles.ExportTrace(Path.Combine(output,"reactive-3000.trace.csv"),reactive,3000);
    FourStrokeFiles.ExportComparison(Path.Combine(output,"properties.comparison.csv"),legacy,frozen);
    FourStrokeFiles.ExportComparison(Path.Combine(output,"burn-law.comparison.csv"),frozen,reactive);
    var complete=EngineSimulation.Run(one with{Combustion=c.Combustion with{HeatReleaseEfficiency=1}});
    FourStrokeFiles.ExportTrace(Path.Combine(output,"complete-3000.trace.csv"),complete,3000);
    Save("complete-diagnostics.json",complete.Points[0].Chemistry!);
    Near(reactive.Points.Single(p=>p.Rpm==3000).NetWorkJ,baseline!.NetWorkJ,0);
    (string Name,FourStrokeConfiguration Config)[] variants=[
        ("spark5",c with{Combustion=c.Combustion with{SparkAdvanceDeg=5}}),
        ("spark35",c with{Combustion=c.Combustion with{SparkAdvanceDeg=35}}),
        ("compression8",c with{Geometry=c.Geometry with{CompressionRatio=8}}),
        ("compression12",c with{Geometry=c.Geometry with{CompressionRatio=12}}),
        ("lift4mm",c with{IntakeValve=c.IntakeValve with{MaximumLiftM=.004}}),
        ("ivc260",c with{IntakeValve=c.IntakeValve with{ClosesDeg=260}}),
        ("intake80kPa",c with{Intake=c.Intake with{PressurePa=80000}}),
        ("exhaust125kPa",c with{Exhaust=c.Exhaust with{PressurePa=125000}}),
        ("intake300K",c with{Intake=c.Intake with{TemperatureK=300}}),
        ("exhaust900K",c with{Exhaust=c.Exhaust with{TemperatureK=900}})];
    var study=new List<object>();
    var sensitivityConvergence=new List<object>();
    var fineBase=EngineSimulation.Run(c with{Speeds=new(3500,3500,1),Solver=c.Solver with{MaximumStepDeg=.25,RelativeTolerance=2e-8}}).Points[0];
    foreach(var (name,config) in variants)
    {
        var result=EngineSimulation.Run(config with{Speeds=new(1000,6000,2500)});
        foreach(var p in result.Points)
        {
            var a=reactive.Points.Single(q=>q.Rpm==p.Rpm);
            // Downstream exhaust temperature need not affect a cycle without reverse flow.
            if(name!="exhaust900K") Assert(Math.Abs(p.BrakePowerW-a.BrakePowerW)>1,"Modification has no represented effect: "+name);
            Near(p.Chemistry!.MaximumElementResidualKg,0,1e-12); Near(p.EnergyResidualJ,0,1e-7);
            study.Add(new{name,p.Rpm,p.IndicatedPowerW,p.BrakePowerW,p.BrakeTorqueNm,p.PeakTemperatureK,p.VolumetricEfficiency,deltaBrakeW=p.BrakePowerW-a.BrakePowerW,p.Chemistry});
        }
        FourStrokeFiles.Export(Path.Combine(output,name+".csv"),result);
        var fine=EngineSimulation.Run(config with{Speeds=new(3500,3500,1),Solver=c.Solver with{MaximumStepDeg=.25,RelativeTolerance=2e-8}}).Points[0];
        double coarseDelta=result.Points.Single(p=>p.Rpm==3500).NetWorkJ-reactive.Points.Single(p=>p.Rpm==3500).NetWorkJ;
        double fineDelta=fine.NetWorkJ-fineBase.NetWorkJ;
        Near(coarseDelta,fineDelta,.05);
        sensitivityConvergence.Add(new{name,rpm=3500,coarseDeltaJ=coarseDelta,fineDeltaJ=fineDelta,errorJ=coarseDelta-fineDelta});
    }
    Save("sensitivities.json",study);
    Save("sensitivity-convergence.json",sensitivityConvergence);
    EngineSimulation.Run(c); // warm-up
    var times=new List<double>(); var allocations=new List<long>();
    for(int i=0;i<3;i++)
    {
        long before=GC.GetAllocatedBytesForCurrentThread();
        var r=EngineSimulation.Run(c); times.Add(r.ElapsedMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread()-before);
    }
    Save("benchmark.json",new{runtime=Environment.Version.ToString(),timesMs=times,medianMs=times.Order().ElementAt(1),allocatedBytes=allocations,
        rhsEvaluations=reactive.Points.Sum(p=>p.RhsEvaluations),acceptedSteps=reactive.Points.Sum(p=>p.AcceptedSteps)});
});

int failures=0;
foreach(var(name,action)in tests)
{
    var clock=Stopwatch.StartNew();
    try{action();Console.WriteLine($"PASS {name} ({clock.Elapsed.TotalMilliseconds:F0} ms)");}
    catch(Exception ex){failures++;Console.Error.WriteLine("FAIL "+name+": "+ex);}
}
Console.WriteLine($"{tests.Count-failures}/{tests.Count} reactive test groups passed.");
return failures==0?0:1;
