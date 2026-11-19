using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.GasDynamics;

public sealed record MeshSignal(double Coarse, double Medium, double Fine, double ObservedEnvelope, bool AboveObservedVariation)
{
    public static MeshSignal From(double coarse, double medium, double fine)
    {
        double first = Math.Abs(medium - coarse), last = Math.Abs(fine - medium);
        double envelope = 2 * Math.Max(first, last);
        bool sameSign = Math.Sign(coarse) == Math.Sign(medium) && Math.Sign(medium) == Math.Sign(fine);
        return new(coarse, medium, fine, envelope, sameSign && last <= first && Math.Abs(fine) > envelope);
    }
}

public sealed record DuctResolution(int Rpm, MeshSignal BrakePowerW, MeshSignal BrakeTorqueNm, MeshSignal Filling)
{
    public static bool CanEvaluate(FourStrokeResult a, FourStrokeResult b)
    {
        if (a.Configuration.Ducts is not { } left || b.Configuration.Ducts is not { } right) return false;
        return left.Scheme == right.Scheme && left.Courant == right.Courant &&
            left.Intake.Cells <= 64 && left.Exhaust.Cells <= 64 &&
            right.Intake.Cells <= 64 && right.Exhaust.Cells <= 64 &&
            a.Configuration with { Ducts = right, Speeds = b.Configuration.Speeds } == b.Configuration;
    }

    public static DuctResolution Evaluate(FourStrokeResult a, FourStrokeResult b, int rpm, CancellationToken cancellation = default,
        IProgress<string>? progress = null)
    {
        if (!CanEvaluate(a, b))
            throw new ArgumentException("El contraste requiere el mismo modelo, ajustes, mallas base de hasta 64 celdas y cambio solo de geometría de conductos.");
        var left = a.Points.Single(p => p.Rpm == rpm);
        var right = b.Points.Single(p => p.Rpm == rpm);
        FourStrokePoint Refine(FourStrokeConfiguration config, int scale)
        {
            var duct = config.Ducts!;
            var settings = config with
            {
                Speeds = new(rpm, rpm, 1),
                Ducts = duct with { Intake = duct.Intake with { Cells = duct.Intake.Cells * scale },
                    Exhaust = duct.Exhaust with { Cells = duct.Exhaust.Cells * scale } }
            };
            return ReactiveSimulator.Simulate(settings, rpm, cancellation);
        }
        progress?.Report($"{rpm} RPM · A/B con el doble de celdas…");
        var mediumA = Refine(a.Configuration, 2);
        var mediumB = Refine(b.Configuration, 2);
        progress?.Report($"{rpm} RPM · A/B con cuatro veces más celdas…");
        var fineA = Refine(a.Configuration, 4);
        var fineB = Refine(b.Configuration, 4);
        return new(rpm, MeshSignal.From(right.BrakePowerW - left.BrakePowerW, mediumB.BrakePowerW - mediumA.BrakePowerW,
                fineB.BrakePowerW - fineA.BrakePowerW),
            MeshSignal.From(right.BrakeTorqueNm - left.BrakeTorqueNm, mediumB.BrakeTorqueNm - mediumA.BrakeTorqueNm,
                fineB.BrakeTorqueNm - fineA.BrakeTorqueNm),
            MeshSignal.From(right.VolumetricEfficiency - left.VolumetricEfficiency, mediumB.VolumetricEfficiency - mediumA.VolumetricEfficiency,
                fineB.VolumetricEfficiency - fineA.VolumetricEfficiency));
    }
}
