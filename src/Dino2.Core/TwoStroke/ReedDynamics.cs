namespace Dino2.Core.TwoStroke;

/// <summary>Uniform rectangular cantilever leaf reduced to its first bending mode, normalized to unit tip motion.</summary>
public sealed record ReedLeaf(double LengthM, double WidthM, double ThicknessM, double DensityKgM3,
    double YoungModulusPa, double DampingRatio, double LimitM, double EffectivePressureAreaM2)
{
    public double SecondMomentM4 => WidthM * Math.Pow(ThicknessM, 3) / 12;
    public double TotalMassKg => DensityKgM3 * WidthM * ThicknessM * LengthM;
    // First-mode generalized mass for a uniform cantilever, with mode shape normalized to unity at its tip.
    public double ModalMassKg => .236 * TotalMassKg;
    public double TipStiffnessNPerM => 3 * YoungModulusPa * SecondMomentM4 / Math.Pow(LengthM, 3);
    public double ModalDampingNsPerM => 2 * DampingRatio * Math.Sqrt(ModalMassKg * TipStiffnessNPerM);
    public double NaturalFrequencyHz => Math.Sqrt(TipStiffnessNPerM / ModalMassKg) / (2 * Math.PI);

    public void Validate()
    {
        if (!(LengthM > 0 && WidthM > 0 && ThicknessM > 0 && DensityKgM3 > 0 && YoungModulusPa > 0 &&
              DampingRatio >= 0 && DampingRatio < 1 && LimitM > 0 && EffectivePressureAreaM2 > 0) ||
            !double.IsFinite(NaturalFrequencyHz))
            throw new ArgumentException("Parámetros físicos inválidos para la lámina modal.");
    }
}

public readonly record struct ReedState(double LiftM, double VelocityMps);

/// <summary>Reduced, pressure-driven first-mode beam. Contact is perfectly inelastic at seat and stop.</summary>
public static class ReedDynamics
{
    public static double Acceleration(ReedLeaf leaf, ReedState state, double pressureDifferencePa) =>
        (pressureDifferencePa * leaf.EffectivePressureAreaM2 - leaf.TipStiffnessNPerM * state.LiftM -
         leaf.ModalDampingNsPerM * state.VelocityMps) / leaf.ModalMassKg;

    /// <summary>RK4 under constant pressure, internally substepped to 1/80 of the undamped modal period.
    /// Seat/stop impacts remove normal velocity, hence do not add kinetic energy.</summary>
    public static ReedState Advance(ReedLeaf leaf, ReedState initial, double pressureDifferencePa, double dtSeconds)
    {
        leaf.Validate();
        if (!(dtSeconds >= 0) || !double.IsFinite(dtSeconds) || !double.IsFinite(pressureDifferencePa))
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        double hMax = 1 / (80 * leaf.NaturalFrequencyHz);
        int steps = Math.Max(1, (int)Math.Ceiling(dtSeconds / hMax));
        double h = dtSeconds / steps;
        var state = initial;
        for (int n = 0; n < steps; n++)
        {
            var k1 = Derivative(leaf, state, pressureDifferencePa);
            var k2 = Derivative(leaf, Add(state, k1, h / 2), pressureDifferencePa);
            var k3 = Derivative(leaf, Add(state, k2, h / 2), pressureDifferencePa);
            var k4 = Derivative(leaf, Add(state, k3, h), pressureDifferencePa);
            state = new(state.LiftM + h / 6 * (k1.LiftM + 2 * k2.LiftM + 2 * k3.LiftM + k4.LiftM),
                state.VelocityMps + h / 6 * (k1.VelocityMps + 2 * k2.VelocityMps + 2 * k3.VelocityMps + k4.VelocityMps));
            if (state.LiftM <= 0) state = new(0, Math.Max(0, state.VelocityMps));
            else if (state.LiftM >= leaf.LimitM) state = new(leaf.LimitM, Math.Min(0, state.VelocityMps));
        }
        return state;
    }

    public static double StoredEnergy(ReedLeaf leaf, ReedState state) =>
        .5 * leaf.ModalMassKg * state.VelocityMps * state.VelocityMps +
        .5 * leaf.TipStiffnessNPerM * state.LiftM * state.LiftM;

    private static ReedState Derivative(ReedLeaf leaf, ReedState state, double pressure) =>
        new(state.VelocityMps, Acceleration(leaf, state, pressure));
    private static ReedState Add(ReedState s, ReedState d, double scale) =>
        new(s.LiftM + d.LiftM * scale, s.VelocityMps + d.VelocityMps * scale);
}
