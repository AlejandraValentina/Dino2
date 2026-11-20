using Dino2.Core.GasDynamics;
using Dino2.Core.FourStroke;

namespace Dino2.Core.Thermochemistry;

// State: 5 species masses (before the current analytic reaction extent), total U,
// W, Wpump, Qwall, Hflow, net intake fuel, fresh-air tag, 5 signed species-flow integrals.
// V3 appends CO and H2 masses (17-18) and their signed flow integrals (19-20).
public sealed class ReactiveCylinder
{
    public int SpeciesCount { get; }
    private int CylinderDimension => SpeciesCount == 5 ? 17 : 21;
    public int Dimension => Ducts?.Dimension ?? CylinderDimension;
    internal DuctCoupling? Ducts { get; }
    internal double CflDegrees => Ducts!.Courant * Ducts.StableTime * (6.0 * rpm);
    internal static int MassIndex(int species) => species < 5 ? species : 17 + species - 5;
    internal static int FlowIndex(int species) => species < 5 ? 12 + species : 19 + species - 5;
    private bool equilibrating;
    internal bool IsEquilibrating => equilibrating;
    private readonly bool useEquilibrium;
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
        useEquilibrium = c.Thermochemistry!.UsesEquilibrium;
        SpeciesCount = useEquilibrium ? SpeciesThermodynamics.ExtendedCount : SpeciesThermodynamics.Count;
        intake = SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio);
        exhaust = SpeciesThermodynamics.ExhaustMixture(c.Combustion.AirFuelRatio);
        Array.Resize(ref intake, SpeciesCount);
        Array.Resize(ref exhaust, SpeciesCount);
        intakeH = SpeciesThermodynamics.Enthalpy(intake, c.Intake.TemperatureK);
        exhaustH = SpeciesThermodynamics.Enthalpy(exhaust, c.Exhaust.TemperatureK);
        intakeR = SpeciesThermodynamics.GasConstant(intake); exhaustR = SpeciesThermodynamics.GasConstant(exhaust);
        intakeGamma = SpeciesThermodynamics.Gamma(intake, c.Intake.TemperatureK);
        exhaustGamma = SpeciesThermodynamics.Gamma(exhaust, c.Exhaust.TemperatureK);
        if (c.Ducts is not null) Ducts = new(c, intake, exhaust, CylinderDimension);
        BurnStart = 360 - c.Combustion.SparkAdvanceDeg + 6 * rpm * c.Combustion.IgnitionDelayS;
    }
    public void Initialize(Span<double> state)
        => Initialize(state, 0);

    internal void Initialize(Span<double> state, double angle)
    {
        double mass = config.Exhaust.PressurePa * SliderCrank.At(config.Geometry, angle).VolumeM3 /
            (exhaustR * config.Exhaust.TemperatureK);
        for (int i = 0; i < 5; i++) state[i] = mass * exhaust[i];
        state[5] = SpeciesThermodynamics.Energy(state[..5], config.Exhaust.TemperatureK);
        Ducts?.Initialize(state);
    }
    public void PhysicalSpecies(double angle, ReadOnlySpan<double> state, Span<double> masses)
    {
        double extent = ReactionActive ? TargetFuel * HeatRelease.Fraction((angle - BurnStart) / BurnDuration, config.Combustion.WiebeExponent) : 0;
        for (int i = 0; i < masses.Length; i++) masses[i] = state[MassIndex(i)] + extent * SpeciesThermodynamics.ReactionMassRatio(i);
    }
    public void BeginReaction(ReadOnlySpan<double> state)
    {
        Span<double> masses = stackalloc double[SpeciesCount];
        PhysicalSpecies(BurnStart, state, masses);
        SocT = SpeciesThermodynamics.Temperature(masses, state[5]);
        double mass = SpeciesThermodynamics.TotalMass(masses);
        SocP = mass * SpeciesThermodynamics.GasConstant(masses) * SocT / SliderCrank.At(config.Geometry, BurnStart).VolumeM3;
        SocResidual = 1 - state[11] * (config.Combustion.AirFuelRatio + 1) / config.Combustion.AirFuelRatio / mass;
        // Tag roundoff at the pure-fresh limit is not a physical residual correction.
        if (SocResidual is < 0 and > -1e-10) SocResidual = 0;
        BurnDuration = 6 * rpm * ConditionedBurn.DurationSeconds(config, rpm, SocT, SocP, SocResidual);
        if (!double.IsFinite(BurnDuration) || BurnDuration <= 0 || BurnStart + BurnDuration >= config.ExhaustValve.OpensDeg)
            throw new ModelDomainException($"{rpm} RPM: la combustión condicionada no termina antes del escape. Revise duración y condiciones.");
        TargetFuel = config.Combustion.HeatReleaseEfficiency * Math.Min(state[0], state[1] / -SpeciesThermodynamics.ReactionMassRatio(1));
        ReactionActive = true;
        equilibrating = useEquilibrium && TargetFuel > 0;
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
    internal double Resolve(double angle, double volume, ReadOnlySpan<double> state, Span<double> masses)
    {
        PhysicalSpecies(angle, state, masses);
        return equilibrating
            ? ProductEquilibrium.Solve(masses, state[5], volume, masses)
            : SpeciesThermodynamics.Temperature(masses, state[5]);
    }

    internal void FreezeProducts(double angle, Span<double> state, Span<double> extents)
    {
        if (!equilibrating) return;
        Span<double> products = stackalloc double[SpeciesCount];
        Resolve(angle, SliderCrank.At(config.Geometry, angle).VolumeM3, state, products);
        for (int i = 5; i < SpeciesCount; i++)
            extents[i - 5] = (products[i] - state[MassIndex(i)]) / SpeciesThermodynamics.MolecularWeight(i);
        for (int i = 0; i < SpeciesCount; i++) state[MassIndex(i)] = products[i];
        equilibrating = false;
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
        => Evaluate(angle, state, rates, null, null);

    internal void Evaluate(double angle, ReadOnlySpan<double> state, Span<double> rates,
        DuctBoundary? intakeBoundary, DuctBoundary? exhaustBoundary)
    {
        Evaluations++;
        var position = SliderCrank.At(config.Geometry, angle);
        Span<double> species = stackalloc double[SpeciesCount];
        double t = Resolve(angle, position.VolumeM3, state, species);
        double mass = SpeciesThermodynamics.TotalMass(species);
        double r = SpeciesThermodynamics.GasConstant(species), gamma = SpeciesThermodynamics.Gamma(species, t);
        double p = mass * r * t / position.VolumeM3;
        double dps = 6.0 * rpm;
        double enthalpy;
        if (Ducts is not null)
        {
            Span<double> chamber = stackalloc double[SpeciesCount + 3];
            double density = FillChamber(chamber, species, position.VolumeM3, state[5], state[11]);
            Span<double> induction = stackalloc double[chamber.Length];
            Span<double> discharge = stackalloc double[chamber.Length];
            var primitive = new GasState(density, 0, p, chamber[SpeciesCount + 1], Math.Sqrt(gamma * r * t), t, gamma, r);
            Ducts.Evaluate(angle, dps, state, rates, chamber, primitive, induction, discharge, intakeBoundary, exhaustBoundary);
            for (int i = 0; i < SpeciesCount; i++)
            {
                double flow = (induction[i] + discharge[i]) / dps;
                rates[MassIndex(i)] = flow;
                rates[FlowIndex(i)] = flow;
            }
            enthalpy = induction[SpeciesCount + 1] + discharge[SpeciesCount + 1];
            rates[10] = induction[0] / dps;
            rates[11] = (induction[SpeciesCount + 2] + discharge[SpeciesCount + 2]) / dps;
        }
        else
        {
            double mi = Nozzle(ValveFlow.EffectiveArea(config.IntakeValve, angle), config.Intake.PressurePa,
                config.Intake.TemperatureK, intakeR, intakeGamma, p, t, r, gamma);
            double me = Nozzle(ValveFlow.EffectiveArea(config.ExhaustValve, angle), config.Exhaust.PressurePa,
                config.Exhaust.TemperatureK, exhaustR, exhaustGamma, p, t, r, gamma);
            double h = SpeciesThermodynamics.Enthalpy(species, t);
            enthalpy = mi * (mi >= 0 ? intakeH : h) + me * (me >= 0 ? exhaustH : h);
            for (int i = 0; i < SpeciesCount; i++)
            {
                double flow = mi * (mi >= 0 ? intake[i] : species[i] / mass) + me * (me >= 0 ? exhaust[i] : species[i] / mass);
                rates[MassIndex(i)] = flow / dps;
                rates[FlowIndex(i)] = flow / dps;
            }
            rates[10] = mi * (mi >= 0 ? intake[0] : species[0] / mass) / dps;
            rates[11] = (Math.Max(0, mi) * (1 - intake[0]) + (Math.Min(0, mi) + Math.Min(0, me)) * state[11] / mass) / dps;
        }
        double wall = config.Thermal.HohenbergMultiplier * LossModels.HeatTransferCoefficient(position.VolumeM3, p, t,
            2 * config.Geometry.StrokeM * rpm / 60) * position.WettedAreaM2 * (t - config.Thermal.WallTemperatureK);
        double work = p * position.VolumeDerivativeM3PerDeg;
        rates[5] = (enthalpy - wall) / dps - work; // Total chemical + sensible energy. NO added LHV heat source.
        rates[6] = work; rates[7] = PumpingInterval ? work : 0;
        rates[8] = wall / dps; rates[9] = enthalpy / dps;
    }

    private double FillChamber(Span<double> chamber, ReadOnlySpan<double> masses, double volume, double energy, double tag)
    {
        double density = 0;
        for (int i = 0; i < SpeciesCount; i++)
        {
            chamber[i] = masses[i] / volume;
            density += chamber[i];
        }
        chamber[SpeciesCount] = 0;
        chamber[SpeciesCount + 1] = energy / volume;
        chamber[SpeciesCount + 2] = tag / volume;
        return density;
    }

    public CycleSample Sample(double angle, ReadOnlySpan<double> state, double alreadyConsumed)
    {
        var pos = SliderCrank.At(config.Geometry, angle);
        Span<double> masses = stackalloc double[SpeciesCount];
        double t = Resolve(angle, pos.VolumeM3, state, masses);
        double mass = SpeciesThermodynamics.TotalMass(masses);
        double r = SpeciesThermodynamics.GasConstant(masses), gamma = SpeciesThermodynamics.Gamma(masses, t);
        double p = mass * r * t / pos.VolumeM3;
        double burned = alreadyConsumed + (ReactionActive ? TargetFuel * HeatRelease.Fraction((angle - BurnStart) / BurnDuration, config.Combustion.WiebeExponent) : 0);
        double mi, me, intakePressure, exhaustPressure;
        if (Ducts is not null)
        {
            Span<double> chamber = stackalloc double[SpeciesCount + 3];
            double density = FillChamber(chamber, masses, pos.VolumeM3, state[5], state[11]);
            var primitive = new GasState(density, 0, p, chamber[SpeciesCount + 1], Math.Sqrt(gamma * r * t), t, gamma, r);
            (mi, me, intakePressure, exhaustPressure) = Ducts.Sample(angle, state, chamber, primitive);
        }
        else
        {
            intakePressure = config.Intake.PressurePa;
            exhaustPressure = config.Exhaust.PressurePa;
            mi = Nozzle(ValveFlow.EffectiveArea(config.IntakeValve, angle), intakePressure, config.Intake.TemperatureK, intakeR, intakeGamma, p, t, r, gamma);
            me = Nozzle(ValveFlow.EffectiveArea(config.ExhaustValve, angle), exhaustPressure, config.Exhaust.TemperatureK, exhaustR, exhaustGamma, p, t, r, gamma);
        }
        return new(angle, pos.VolumeM3, mass, t, p, state[11] * (config.Combustion.AirFuelRatio + 1) / config.Combustion.AirFuelRatio,
            ValveFlow.Lift(config.IntakeValve, angle), ValveFlow.Lift(config.ExhaustValve, angle),
            mi, me,
            state[5], state[6], burned * SpeciesThermodynamics.LowerHeatingValue, state[8], state[9])
        { Composition = SpeciesMasses.From(masses), IntakePortPressurePa = intakePressure, ExhaustPortPressurePa = exhaustPressure };
    }
}
