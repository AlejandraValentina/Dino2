using System.Diagnostics;
using Dino2.Core.Studies;

namespace Dino2.Core.TwoStroke;

public sealed record TwoStrokeComparisonEvidence(int Rpm, StudyCheck Check);

public static class TwoStrokeComparison
{
    public static StudyCheck Verify(TwoStrokeConfiguration a, TwoStrokeConfiguration b, TwoStrokePoint pa,
        TwoStrokePoint pb, CancellationToken cancellation = default, IProgress<string>? progress = null, bool refreshBaseline = false)
    {
        if (pa.Rpm != pb.Rpm || !a.Speeds.Values().Contains(pa.Rpm) || !b.Speeds.Values().Contains(pb.Rpm))
            throw new ArgumentException("El contraste requiere el mismo régimen en A y B.");
        if (a.Numerics.Scheme != b.Numerics.Scheme || a.Numerics.Exterior != b.Numerics.Exterior ||
            a.Combustion.DurationDeg.HasValue != b.Combustion.DurationDeg.HasValue || a.Numerics.Courant != b.Numerics.Courant ||
            a.Numerics.MaximumStepDeg != b.Numerics.MaximumStepDeg || a.Numerics.PeriodicTolerance != b.Numerics.PeriodicTolerance)
            throw new ArgumentException("Usá el mismo esquema, CFL, paso máximo y tolerancia periódica en A y B.");
        static bool SimilarSpacing(double lengthA, int nA, double lengthB, int nB) =>
            Math.Abs(lengthB / lengthA * nA - nB) <= 1 && Math.Abs(lengthA / lengthB * nB - nA) <= 1;
        if (!SimilarSpacing(a.IntakeDuct.LengthM, a.IntakeDuct.Cells, b.IntakeDuct.LengthM, b.IntakeDuct.Cells) ||
            !SimilarSpacing(a.Expansion.LengthM, a.Expansion.Cells, b.Expansion.LengthM, b.Expansion.Cells))
            throw new ArgumentException("Igualá aproximadamente el tamaño de celda en A y B antes de contrastar.");
        var clock = Stopwatch.StartNew();
        int rpm = pa.Rpm;
        if (refreshBaseline)
        {
            progress?.Report($"A/B a {rpm} RPM · actualizando malla base");
            pa = TwoStrokeSimulator.Simulate(a, rpm, false, cancellation);
            pb = a == b ? pa : TwoStrokeSimulator.Simulate(b, rpm, false, cancellation);
        }
        var levels = new List<StudyLevel> { Level(1, pa, pb) };
        foreach (int scale in new[] { 2, 4 })
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"A/B a {pa.Rpm} RPM · resolución ×{scale}");
            try
            {
                var left = TwoStrokeSimulator.Simulate(TwoStrokeStudy.Refine(a, scale), pa.Rpm, false, cancellation);
                var right = a == b ? left : TwoStrokeSimulator.Simulate(TwoStrokeStudy.Refine(b, scale), pb.Rpm, false, cancellation);
                levels.Add(Level(scale, left, right));
            }
            catch (Exception ex) when (ex is ArithmeticException or ArgumentException)
            {
                levels.Add(new(scale, null, null, null, null, null, ex.Message));
                break;
            }
        }
        var (verdict, band) = StudyVerification.Classify(levels);
        return new(1, 0, verdict, band, levels.AsReadOnly(), clock.Elapsed.TotalMilliseconds,
            NumericalRevision: a.Revision);
    }

    private static StudyLevel Level(int scale, TwoStrokePoint a, TwoStrokePoint b) =>
        new(scale, b.ShaftTorqueNm, a.ShaftTorqueNm, b.ShaftTorqueNm - a.ShaftTorqueNm, null, null, null);
}
