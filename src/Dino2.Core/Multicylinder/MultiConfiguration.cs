using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;

namespace Dino2.Core.Multicylinder;

// Offsets of firing TDC from cylinder 1, in physical cylinder numbering.
public sealed record FiringPhases(double Cylinder2Deg, double? Cylinder3Deg = null, double? Cylinder4Deg = null)
{
    public int Count => Cylinder4Deg.HasValue ? 4 : Cylinder3Deg.HasValue ? 3 : 2;
    public double[] Values() => Count switch
    {
        2 => [0, Cylinder2Deg],
        3 => [0, Cylinder2Deg, Cylinder3Deg!.Value],
        _ => [0, Cylinder2Deg, Cylinder3Deg ?? double.NaN, Cylinder4Deg!.Value]
    };

    public static FiringPhases Inline(int cylinders) => cylinders switch
    {
        2 => new(360),
        3 => new(240, 480),
        4 => new(540, 180, 360),
        _ => throw new ArgumentOutOfRangeException(nameof(cylinders))
    };

    public int[] FiringOrder() => Values().Select((phase, i) => (phase, i))
        .OrderBy(cylinder => cylinder.phase).Select(cylinder => cylinder.i + 1).ToArray();

    internal void Validate(double period = 720)
    {
        var phases = Values();
        if (phases.Any(p => !double.IsFinite(p) || p < 0 || p >= period) ||
            phases.Distinct().Count() != phases.Length)
            throw new ArgumentException($"Los desfases de combustión deben ser distintos y estar en [0, {period}) grados.");
    }

    public static FiringPhases Even(int cylinders, int period) => (cylinders, period) switch
    {
        (_, 720) => Inline(cylinders),
        (2, 360) => new(180),
        (3, 360) => new(120, 240),
        (4, 360) => new(270, 90, 180),
        _ => throw new ArgumentOutOfRangeException(nameof(cylinders))
    };

    internal static double Wrap(double angle, double period)
    {
        angle = (angle % period + period) % period;
        return angle < 1e-9 || period - angle < 1e-9 ? 0 : angle;
    }
}

public sealed record ManifoldSide(DuctGeometry Trunk, double VolumeM3)
{
    internal void Validate()
    {
        if (Trunk is null || !double.IsFinite(VolumeM3) || VolumeM3 is < .0001 or > .03)
            throw new ArgumentException("Se requiere un conducto común y un volumen de unión de 0,1–30 L.");
        Trunk.Validate();
    }
}

public sealed record SharedManifolds(ManifoldSide Intake, ManifoldSide Exhaust);

public sealed record MultiConfiguration(FourStrokeConfiguration Cylinder, FiringPhases Phases, SharedManifolds? Manifolds)
{
    public const string ModelId = "four-stroke-inline-isooctane-manifolds-v1";
    public double DisplacementM3 => Phases.Count * Cylinder.Geometry.DisplacementM3;

    public static MultiConfiguration Example(int count = 2) => new(
        Thermochemistry.ThermochemicalOptions.EquilibriumExample with
        {
            Thermochemistry = Thermochemistry.ThermochemicalOptions.EquilibriumExample.Thermochemistry!
                with { BurnLaw = Thermochemistry.BurnLaw.PrescribedWiebe },
            Ducts = new(new(.38, .040, .036, 8), new(.52, .038, .034, 8), .45, DuctScheme.Muscl),
            Speeds = new(3000, 5000, 1000),
            Solver = new(.5, 2e-7, .0004, 40)
        }, FiringPhases.Inline(count),
        new(new(new(.38, .066, .070, 8), .0024), new(new(.7, .060, .068, 8), .0014)));

    public void Validate()
    {
        if (Cylinder is null || Phases is null || Cylinder.Ducts is null)
            throw new ArgumentException("Se requieren fases y un modelo 4T termoquímico con ramales 1D.");
        Phases.Validate();
        Cylinder.Validate(false);
        if (Manifolds is { } network)
        {
            if (network.Intake is null || network.Exhaust is null)
                throw new ArgumentException("La red necesita colectores de admisión y escape.");
            network.Intake.Validate();
            network.Exhaust.Validate();
        }
    }
}
