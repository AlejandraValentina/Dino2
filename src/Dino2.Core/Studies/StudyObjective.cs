namespace Dino2.Core.Studies;

public enum ObjectiveKind { PeakPower, PeakTorque, MeanPower, MeanTorque, HighTorqueLimitedLowLoss, MinimumBsfc }
public sealed record RpmBand(int Start, int End);

public sealed record StudyObjective(ObjectiveKind Kind, RpmBand? Band = null, RpmBand? LowBand = null, double AllowedLowLossNm = 0)
{
    public bool Minimizes => Kind == ObjectiveKind.MinimumBsfc;
    public string Unit => Kind switch
    {
        ObjectiveKind.PeakPower or ObjectiveKind.MeanPower => "W",
        ObjectiveKind.MinimumBsfc => "g/kWh",
        _ => "N·m"
    };

    public void Validate(IReadOnlyList<int> rpm, bool tracksFuel)
    {
        if (!Enum.IsDefined(Kind) || !double.IsFinite(AllowedLowLossNm) || AllowedLowLossNm < 0)
            throw new ArgumentException("Objetivo o pérdida de torque inválidos.");
        if (Kind == ObjectiveKind.MinimumBsfc && !tracksFuel)
            throw new ArgumentException("Consumo específico requiere el balance de combustible de un modelo termoquímico.");
        void Check(RpmBand band)
        {
            if (band.Start > band.End || !rpm.Contains(band.Start) || !rpm.Contains(band.End))
                throw new ArgumentException("Los dos extremos de cada banda deben ser RPM de la grilla.");
        }
        if (Band is not null) Check(Band);
        if (Kind == ObjectiveKind.HighTorqueLimitedLowLoss)
        {
            if (LowBand is null || Band is null || LowBand.End >= Band.Start)
                throw new ArgumentException("El compromiso requiere una banda baja separada de la banda alta.");
            Check(LowBand);
        }
    }

    public ObjectiveValue Evaluate(IReadOnlyList<StudyPoint> points, IReadOnlyList<StudyPoint>? reference = null, int revolutionsPerCycle = 2)
    {
        if (revolutionsPerCycle is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(revolutionsPerCycle));
        if (points.Count == 0) return new(null, null, "Sin curva completa");
        var band = Slice(points, Band);
        if (band.Length == 0) return new(null, null, "Banda sin muestras");
        switch (Kind)
        {
            case ObjectiveKind.PeakPower: return new(band.Max(p => p.BrakePowerW), null);
            case ObjectiveKind.PeakTorque: return new(band.Max(p => p.BrakeTorqueNm), null);
            case ObjectiveKind.MeanPower: return new(Average(band, p => p.BrakePowerW), null);
            case ObjectiveKind.MeanTorque: return new(Average(band, p => p.BrakeTorqueNm), null);
            case ObjectiveKind.HighTorqueLimitedLowLoss:
                if (reference is null || reference.Count == 0) return new(null, null, "Falta base convergida para evaluar la restricción");
                double margin = Average(Slice(points, LowBand), p => p.BrakeTorqueNm) -
                    Average(Slice(reference, LowBand), p => p.BrakeTorqueNm) + AllowedLowLossNm;
                return new(Average(band, p => p.BrakeTorqueNm), margin, margin < 0 ? "Pérdida en baja superior al límite" : null);
            case ObjectiveKind.MinimumBsfc:
                if (band.Any(p => p.BrakePowerW <= 0 || p.FuelMassKgPerCycle <= 0))
                    return new(null, null, "Consumo específico indefinido con potencia o combustible neto no positivos");
                return new(3.6e9 * Average(band, p => p.FuelMassKgPerCycle * p.Rpm / (60 * revolutionsPerCycle)) /
                    Average(band, p => p.BrakePowerW), null);
            default: throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }

    private static StudyPoint[] Slice(IReadOnlyList<StudyPoint> points, RpmBand? band) =>
        points.Where(p => band is null || p.Rpm >= band.Start && p.Rpm <= band.End).ToArray();

    // Uniform weighting per RPM; endpoints are actual samples. No synthetic engine points.
    private static double Average(StudyPoint[] points, Func<StudyPoint, double> sample)
    {
        if (points.Length == 1) return sample(points[0]);
        double area = 0;
        for (int i = 1; i < points.Length; i++)
            area += (sample(points[i - 1]) + sample(points[i])) * .5 * (points[i].Rpm - points[i - 1].Rpm);
        return area / (points[^1].Rpm - points[0].Rpm);
    }

    public double Preference(double score) => Minimizes ? -score : score;
}

public sealed record ObjectiveValue(double? Score, double? LowMarginNm, string? Exclusion = null)
{
    public bool Eligible => Score.HasValue && Exclusion is null;
}
