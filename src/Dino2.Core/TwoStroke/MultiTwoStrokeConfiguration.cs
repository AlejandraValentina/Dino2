using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Multicylinder;

namespace Dino2.Core.TwoStroke;

public sealed record TwoStrokeSharedExhaust(DuctGeometry Primary, double JunctionVolumeM3);

public sealed record MultiTwoStrokeConfiguration(TwoStrokeConfiguration Cylinder, FiringPhases Phases,
    ManifoldSide? SharedIntake = null, TwoStrokeSharedExhaust? SharedExhaust = null)
{
    public const string ModelId = "two-stroke-inline-reed-expansion-isooctane-v1";
    public double DisplacementM3 => Cylinder.Geometry.DisplacementM3 * Phases.Count;

    public static MultiTwoStrokeConfiguration Example(int cylinders = 2)
    {
        var c = TwoStrokeConfiguration.Example;
        return new(c with
        {
            Expansion = c.Expansion with
            {
                InletDiameterM = .057, BellyDiameterM = .105, StingerDiameterM = .035
            },
            Speeds = new(4000, 5000, 1000),
            Numerics = c.Numerics with { PeriodicTolerance = .0002 }
        }, FiringPhases.Even(cylinders, 360),
            new(new(.26, .058, .058, 8), .0012),
            new(new(.24, .036, .035, 12), .00055));
    }

    public void Validate()
    {
        if (Cylinder is null || Phases is null) throw new ArgumentException("Falta la configuración 2T o sus fases.");
        Cylinder.Validate();
        Phases.Validate(360);
        SharedIntake?.Validate();
        if (SharedExhaust is { } e)
        {
            if (e.Primary is null || !double.IsFinite(e.JunctionVolumeM3) || e.JunctionVolumeM3 is < .0001 or > .03)
                throw new ArgumentException("Escape compartido: primario y volumen de unión de 0,1–30 L.");
            e.Primary.Validate();
        }
    }
}

public readonly record struct MultiTwoStrokeSample(double AngleDeg, double? IntakeJunctionPa, double? ExhaustJunctionPa,
    double? IntakeTrunkPa, double? ExhaustTrunkPa, double GasTorqueNm);

public sealed record MultiTwoStrokeCylinder(int Number, double PhaseDeg, TwoStrokePoint Point);

public sealed record MultiTwoStrokePoint(int Rpm, double IndicatedPowerW, double ShaftPowerW, double IndicatedTorqueNm,
    double ShaftTorqueNm, double ImepPa, double BmepPa, double? BsfcGPerKwh,
    IReadOnlyList<MultiTwoStrokeCylinder> Cylinders, IReadOnlyList<MultiTwoStrokeSample> Cycle,
    TwoStrokeBalance Balance, double MaximumInterfaceSpeciesKg, double MaximumInterfaceEnergyJ, long CellEvaluations);

public sealed record MultiTwoStrokeRun(int Rpm, TwoStrokeRunStatus Status, MultiTwoStrokePoint? Point, string? Failure,
    TwoStrokeConvergence? Convergence, double Milliseconds);

public sealed record MultiTwoStrokeSweep(MultiTwoStrokeConfiguration Configuration, IReadOnlyList<MultiTwoStrokeRun> Runs)
{
    public bool Complete => Runs.Count == Configuration.Cylinder.Speeds.Values().Count() &&
        Runs.All(r => r.Status == TwoStrokeRunStatus.Periodic);
}
