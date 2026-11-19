using Dino2.Core.FourStroke;

namespace Dino2.Core.Thermochemistry;

// State: 5 species masses (before the current analytic reaction extent), total U,
// W, Wpump, Qwall, Hflow, net intake fuel, fresh-air tag, 5 signed species-flow integrals.
public sealed class ReactiveCylinder
{
    public const int Dimension = 17;
    private readonly FourStrokeConfiguration config;
    private readonly int rpm;
    private readonly double[] intake, exhaust;
    private readonly double intakeH, exhaustH, intakeR, exhaustR, intakeGamma, exhaustGamma;
    public double BurnStart { get; }
    public double BurnDuration { get; private set; }
    public double TargetFuel { get; private set; }
    public bool ReactionActive { get; private set; }
    public bool PumpingInterval { get; set; }
    public double SocT { get; private set; }
    public double SocP { get; private set; }
    public double SocResidual { get; private set; }
    public bool ExtrapolatedCorrelation { get; private set; }
    public int Evaluations { get; private set; }

    public ReactiveCylinder(FourStrokeConfiguration c, int speed)
    {
        config = c; rpm = speed;
        intake = SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio);
        exhaust = SpeciesThermodynamics.ExhaustMixture(c.Combustion.AirFuelRatio);
        intakeH = SpeciesThermodynamics.Enthalpy(intake, c.Intake.TemperatureK);
        exhaustH = SpeciesThermodynamics.Enthalpy(exhaust, c.Exhaust.TemperatureK);
        intakeR = SpeciesThermodynamics.GasConstant(intake); exhaustR = SpeciesThermodynamics.GasConstant(exhaust);
        intakeGamma = SpeciesThermodynamics.Gamma(intake, c.Intake.TemperatureK);
        exhaustGamma = SpeciesThermodynamics.Gamma(exhaust, c.Exhaust.TemperatureK);
        BurnStart = 360 - c.Combustion.SparkAdvanceDeg + 6 * rpm * c.Combustion.IgnitionDelayS;
    }
    public void Initialize(Span<double> state)
    {
        double mass = config.Exhaust.PressurePa * SliderCrank.At(config.Geometry, 0).VolumeM3 /
            (exhaustR * config.Exhaust.TemperatureK);
        for (int i = 0; i < 5; i++) state[i] = mass * exhaust[i];
        state[5] = SpeciesThermodynamics.Energy(state[..5], config.Exhaust.TemperatureK);
    }
    public void PhysicalSpecies(double angle, ReadOnlySpan<double> state, Span<double> masses)
    {
        double extent = ReactionActive ? TargetFuel * HeatRelease.Fraction((angle - BurnStart) / BurnDuration, config.Combustion.WiebeExponent) : 0;
        for (int i = 0; i < 5; i++) masses[i] = state[i] + extent * SpeciesThermodynamics.ReactionMassRatio(i);
    }
    public void BeginReaction(ReadOnlySpan<double> state)
    {
        SocT = SpeciesThermodynamics.Temperature(state[..5], state[5]);
        double mass = SpeciesThermodynamics.TotalMass(state[..5]);
        SocP = mass * SpeciesThermodynamics.GasConstant(state[..5]) * SocT / SliderCrank.At(config.Geometry, BurnStart).VolumeM3;
        SocResidual = 1 - state[11] * (config.Combustion.AirFuelRatio + 1) / config.Combustion.AirFuelRatio / mass;
        // Tag roundoff at the pure-fresh limit is not a physical residual correction.
        if (SocResidual is < 0 and > -1e-10) SocResidual = 0;
        BurnDuration = 6 * rpm * ConditionedBurn.DurationSeconds(config, rpm, SocT, SocP, SocResidual);
        if (!double.IsFinite(BurnDuration) || BurnDuration <= 0 || BurnStart + BurnDuration >= config.ExhaustValve.OpensDeg)
            throw new ArithmeticException($"{rpm} RPM: la combustión condicionada no termina antes del escape. Revise duración y condiciones.");
        TargetFuel = config.Combustion.HeatReleaseEfficiency * Math.Min(state[0], state[1] / -SpeciesThermodynamics.ReactionMassRatio(1));
        ReactionActive = true;
        ExtrapolatedCorrelation = config.Thermochemistry!.BurnLaw == BurnLaw.ConditionScaledWiebe &&
            (SocT < 298 || SocT > 700 || SocP < .4 * 101325 || SocP > 50 * 101325 || SocResidual > .2 ||
             (SocResidual > 0 && SpeciesThermodynamics.StoichiometricAfr / config.Combustion.AirFuelRatio < .999));
    }
    public double CommitReaction(Span<double> state)
    {
        for (int i = 0; i < 5; i++) state[i] += TargetFuel * SpeciesThermodynamics.ReactionMassRatio(i);
        ReactionActive = false;
        return TargetFuel;
    }
    public static double Nozzle(double area, double p1, double t1, double r1, double gamma1,
        double p2, double t2, double r2, double gamma2)
    {
        if (area <= 0 || p1 == p2) return 0;
        bool forward = p1 > p2;
        double pu = Math.Max(p1, p2), pd = Math.Min(p1, p2), t = forward ? t1 : t2,
            r = forward ? r1 : r2, gamma = forward ? gamma1 : gamma2;
        double ratio = pd / pu;
        double critical = Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        double factor = ratio <= critical ? Math.Sqrt(gamma) * Math.Pow(2 / (gamma + 1), (gamma + 1) / (2 * (gamma - 1))) :
            Math.Sqrt(2 * gamma / (gamma - 1) * (Math.Pow(ratio, 2 / gamma) - Math.Pow(ratio, (gamma + 1) / gamma)));
        return (forward ? 1 : -1) * area * pu / Math.Sqrt(r * t) * factor;
    }

    public void Evaluate(double angle, ReadOnlySpan<double> state, Span<double> rates)
    {
        Evaluations++;
        Span<double> species = stackalloc double[5]; PhysicalSpecies(angle, state, species);
        double mass = SpeciesThermodynamics.TotalMass(species);
        double t = SpeciesThermodynamics.Temperature(species, state[5]);
        var position = SliderCrank.At(config.Geometry, angle);
        double r = SpeciesThermodynamics.GasConstant(species), gamma = SpeciesThermodynamics.Gamma(species, t);
        double p = mass * r * t / position.VolumeM3;
        double mi = Nozzle(ValveFlow.EffectiveArea(config.IntakeValve, angle), config.Intake.PressurePa,
            config.Intake.TemperatureK, intakeR, intakeGamma, p, t, r, gamma);
        double me = Nozzle(ValveFlow.EffectiveArea(config.ExhaustValve, angle), config.Exhaust.PressurePa,
            config.Exhaust.TemperatureK, exhaustR, exhaustGamma, p, t, r, gamma);
        double h = SpeciesThermodynamics.Enthalpy(species, t);
        double enthalpy = mi * (mi >= 0 ? intakeH : h) + me * (me >= 0 ? exhaustH : h);
        double wall = config.Thermal.HohenbergMultiplier * LossModels.HeatTransferCoefficient(position.VolumeM3, p, t,
            2 * config.Geometry.StrokeM * rpm / 60) * position.WettedAreaM2 * (t - config.Thermal.WallTemperatureK);
        double dps = 6.0 * rpm;
        for (int i = 0; i < 5; i++)
        {
            double flow = mi * (mi >= 0 ? intake[i] : species[i] / mass) + me * (me >= 0 ? exhaust[i] : species[i] / mass);
            rates[i] = flow / dps;
            rates[12 + i] = flow / dps;
        }
        double work = p * position.VolumeDerivativeM3PerDeg;
        rates[5] = (enthalpy - wall) / dps - work; // Total chemical + sensible energy. NO added LHV heat source.
        rates[6] = work; rates[7] = PumpingInterval ? work : 0;
        rates[8] = wall / dps; rates[9] = enthalpy / dps;
        rates[10] = mi * (mi >= 0 ? intake[0] : species[0] / mass) / dps;
        rates[11] = (Math.Max(0, mi) * (1 - intake[0]) + (Math.Min(0, mi) + Math.Min(0, me)) * state[11] / mass) / dps;
    }
    public CycleSample Sample(double angle, ReadOnlySpan<double> state, double alreadyConsumed)
    {
        Span<double> masses = stackalloc double[5]; PhysicalSpecies(angle, state, masses);
        double mass = SpeciesThermodynamics.TotalMass(masses), t = SpeciesThermodynamics.Temperature(masses, state[5]);
        double r = SpeciesThermodynamics.GasConstant(masses), gamma = SpeciesThermodynamics.Gamma(masses, t);
        var pos = SliderCrank.At(config.Geometry, angle);
        double p = mass * r * t / pos.VolumeM3;
        double burned = alreadyConsumed + (ReactionActive ? TargetFuel * HeatRelease.Fraction((angle - BurnStart) / BurnDuration, config.Combustion.WiebeExponent) : 0);
        return new(angle, pos.VolumeM3, mass, t, p, state[11] * (config.Combustion.AirFuelRatio + 1) / config.Combustion.AirFuelRatio,
            ValveFlow.Lift(config.IntakeValve, angle), ValveFlow.Lift(config.ExhaustValve, angle),
            Nozzle(ValveFlow.EffectiveArea(config.IntakeValve, angle), config.Intake.PressurePa, config.Intake.TemperatureK, intakeR, intakeGamma, p, t, r, gamma),
            Nozzle(ValveFlow.EffectiveArea(config.ExhaustValve, angle), config.Exhaust.PressurePa, config.Exhaust.TemperatureK, exhaustR, exhaustGamma, p, t, r, gamma),
            state[5], state[6], burned * SpeciesThermodynamics.LowerHeatingValue, state[8], state[9])
        { Composition = SpeciesMasses.From(masses) };
    }
}
