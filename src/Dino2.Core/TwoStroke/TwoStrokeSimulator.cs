using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.TwoStroke;

public sealed partial class TwoStrokeSimulator
{
    private const int N = SpeciesThermodynamics.ExtendedCount, Width = N + 3;
    private const int Old = N, Uc = 2 * N, Case = Uc + 1, Uk = Case + N, CaseTag = Uk + 1;
    private const int Wc = CaseTag + 1, Wk = Wc + 1, Q = Wk + 1, FuelIn = Q + 1;
    private const int Delivered = FuelIn + 1, TransferReturn = Delivered + 1, ExhaustReturn = TransferReturn + 1;
    private const int Short = ExhaustReturn + 1, OuterH = Short + 1, OuterTag = OuterH + 1, Outer = OuterTag + 1,
        DuctHeat = Outer + N, IntakeOffset = DuctHeat + 1;

    private readonly TwoStrokeConfiguration c;
    private readonly TwoStrokeProfile? profile;
    private readonly GasEquation gas = new(N);
    private readonly TwoStrokeDuct inlet, outlet;
    private readonly double[] freshMixture;
    private readonly int rpm, outletOffset, dimension;
    private int ReedLiftIndex => outletOffset + outlet.Size;
    private int ReedVelocityIndex => ReedLiftIndex + 1;
    private readonly double dps, clearance, pistonArea, meanSpeed, open, close, soc, duration;
    private readonly double[] events;
    private double burnFuel;
    private bool burning;

    private TwoStrokeSimulator(TwoStrokeConfiguration configuration, int rpm, TwoStrokeProfile? profile = null,
        DuctGeometry? exhaustPrimary = null)
    {
        c = configuration;
        this.profile = profile;
        this.rpm = rpm;
        dps = 6 * rpm;
        meanSpeed = 2 * c.Geometry.StrokeM * rpm / 60;
        clearance = TwoStrokeGeometry.Clearance(c.Geometry);
        pistonArea = c.Geometry.DisplacementM3 / c.Geometry.StrokeM;
        open = c.Exhaust.Opening(c.Geometry);
        close = 360 - open;
        soc = 360 - c.Combustion.AdvanceDeg + dps * c.Combustion.DelayS;
        duration = c.Combustion.CrankDuration(rpm);
        freshMixture = Extended(SpeciesThermodynamics.FreshMixture(c.Combustion.AirFuelRatio));
        inlet = TwoStrokeDuct.Intake(c, gas, freshMixture, profile);
        outlet = exhaustPrimary is null ? TwoStrokeDuct.Exhaust(c, gas, profile) : TwoStrokeDuct.Primary(c, exhaustPrimary, gas);
        outletOffset = IntakeOffset + inlet.Size;
        dimension = outletOffset + outlet.Size + (c.Reed.DynamicLeaf is null ? 0 : 2);
        var breaks = new List<double> { open, close, soc, soc + duration, open + 360 };
        foreach (var port in new[] { c.Exhaust, c.Transfer })
        {
            foreach (double roof in new[] { port.RoofM, port.RoofM + Math.Min(port.HeightM, port.ThroatM2 / port.WidthM) })
            {
                if (roof >= c.Geometry.StrokeM) continue;
                double angle = (port with { RoofM = roof }).Opening(c.Geometry);
                breaks.Add(angle);
                breaks.Add(360 - angle);
            }
        }
        events = breaks.Distinct().Order().ToArray();
    }

    private static double[] Extended(double[] fractions) => [.. fractions, 0, 0];

    private void Initialize(Span<double> y) => Initialize(y, open);

    private void Initialize(Span<double> y, double angle)
    {
        Span<double> values = stackalloc double[Width];
        double volume = SliderCrank.At(c.Geometry, angle).VolumeM3;
        gas.Encode(values, Extended(SpeciesThermodynamics.ExhaustMixture(Math.Max(c.Combustion.AirFuelRatio, SpeciesThermodynamics.StoichiometricAfr))),
            c.Tail.PressurePa, Math.Max(500, c.Tail.TemperatureK), 0, 0);
        for (int i = 0; i < N; i++) y[Old + i] = values[i] * volume;
        y[Uc] = values[gas.Energy] * volume;
        volume = CrankcaseVolume(volume);
        var charge = gas.Encode(values, freshMixture, c.Intake.PressurePa, c.Intake.TemperatureK, 0, 1);
        for (int i = 0; i < N; i++) y[Case + i] = values[i] * volume;
        y[Uk] = values[gas.Energy] * volume;
        y[CaseTag] = charge.Density * volume;
        inlet.Initialize(y.Slice(IntakeOffset, inlet.Size));
        outlet.Initialize(y[outletOffset..]);
    }

    private double CrankcaseVolume(double cylinderVolume) =>
        c.CrankcaseMinimumM3 + c.Geometry.DisplacementM3 + clearance - cylinderVolume;

    private void CylinderComposition(double angle, ReadOnlySpan<double> y, double volume, Span<double> masses, out double temperature)
    {
        double reacted = burning ? burnFuel * HeatRelease.Fraction((angle - soc) / duration, c.Combustion.WiebeExponent) : 0;
        for (int i = 0; i < N; i++)
        {
            masses[i] = y[i] + y[Old + i] + reacted * SpeciesThermodynamics.ReactionMassRatio(i);
            if (masses[i] < -1e-13 || !double.IsFinite(masses[i]))
                throw new NumericalStateException("Masa parcial no física en el cilindro 2T.");
        }
        temperature = burning && c.Combustion.ProductEquilibrium
            ? ProductEquilibrium.Solve(masses, y[Uc], volume, masses)
            : SpeciesThermodynamics.Temperature(masses, y[Uc]);
    }

    private double Evaluate(double angle, ReadOnlySpan<double> y, Span<double> rate, out TwoStrokeSample sample)
        => Evaluate(angle, y, rate, out sample, null, null, default, default);

    private double Evaluate(double angle, ReadOnlySpan<double> y, Span<double> rate, out TwoStrokeSample sample,
        DuctBoundary? intakeBoundary, DuctBoundary? exhaustBoundary, Span<double> intakeRate, Span<double> exhaustRate)
    {
        rate[..IntakeOffset].Clear();
        var piston = SliderCrank.At(c.Geometry, angle);
        double vc = piston.VolumeM3, vk = CrankcaseVolume(vc);
        double travel = (vc - clearance) / pistonArea;
        Span<double> masses = stackalloc double[N], fractions = stackalloc double[N], outFractions = stackalloc double[N];
        long start = profile is null ? 0 : TwoStrokeProfile.Timestamp();
        CylinderComposition(angle, y, vc, masses, out double tc);
        if (profile is not null)
        {
            long now = TwoStrokeProfile.Timestamp();
            profile.CylinderThermo += now - start;
            profile.Evaluations++;
            start = now;
        }
        var crankcase = y.Slice(Case, N);
        double mc = SpeciesThermodynamics.TotalMass(masses), mk = SpeciesThermodynamics.TotalMass(crankcase);
        double tk = SpeciesThermodynamics.Temperature(crankcase, y[Uk], c.Intake.TemperatureK);
        if (profile is not null) profile.CrankcaseThermo += TwoStrokeProfile.Timestamp() - start;
        double rc = SpeciesThermodynamics.GasConstant(masses), rk = SpeciesThermodynamics.GasConstant(crankcase);
        double pc = mc * rc * tc / vc, pk = mk * rk * tk / vk;
        double fresh = SpeciesThermodynamics.TotalMass(y[..N]), fc = Fraction(fresh, mc), fk = Fraction(y[CaseTag], mk);
        double extracted = ExhaustFreshFraction(fc, c.FreshExtractionRatio);
        for (int i = 0; i < N; i++)
        {
            fractions[i] = crankcase[i] / mk;
            outFractions[i] = fc <= 0 || fc >= 1 || c.FreshExtractionRatio == 1
                ? masses[i] / mc
                : extracted * y[i] / fresh + (1 - extracted) * y[Old + i] / (mc - fresh);
        }
        Span<double> caseDonor = stackalloc double[Width], cylinderDonor = stackalloc double[Width];
        var k = gas.EncodeFace(caseDonor, fractions, pk, tk, 0, fk);
        var e = gas.EncodeFace(cylinderDonor, outFractions, pc, tc, 0, extracted);
        Span<double> reedFlux = stackalloc double[Width], exhaustFlux = stackalloc double[Width];
        Span<double> outerIntake = stackalloc double[Width], outerExhaust = stackalloc double[Width];
        double dtI = inlet.Rates(y.Slice(IntakeOffset, inlet.Size), rate.Slice(IntakeOffset, inlet.Size), k,
            caseDonor, 0, c.Reed, outerIntake, reedFlux, out double pReed, out double intakeHeat, intakeBoundary,
            c.Reed.DynamicLeaf is null ? double.NaN : y[ReedLiftIndex]);
        double dtE = outlet.Rates(y[outletOffset..], rate[outletOffset..], e, cylinderDonor,
            c.Exhaust.Area(travel), null, outerExhaust, exhaustFlux, out double pExhaust, out double exhaustHeat, exhaustBoundary);
        for (int i = 0; i < intakeRate.Length; i++) intakeRate[i] = outerIntake[i] / dps;
        for (int i = 0; i < exhaustRate.Length; i++) exhaustRate[i] = outerExhaust[i] / dps;
        double transfer = ReactiveCylinder.Nozzle(c.Transfer.Area(travel), pk, tk, rk, k.Gamma, pc, tc, rc,
            SpeciesThermodynamics.Gamma(masses, tc));
        double transferTag = transfer * (transfer >= 0 ? fk : fc);
        double transferH = transfer * (transfer >= 0
            ? SpeciesThermodynamics.Enthalpy(crankcase, tk) : SpeciesThermodynamics.Enthalpy(masses, tc));
        double exhaustMass = SpeciesThermodynamics.TotalMass(exhaustFlux[..N]);
        double exhaustTag = exhaustFlux[gas.Tag];
        double inletMass = SpeciesThermodynamics.TotalMass(reedFlux[..N]);
        if (c.Reed.DynamicLeaf is { } leaf)
        {
            rate[ReedLiftIndex] = y[ReedVelocityIndex] / dps;
            rate[ReedVelocityIndex] = ReedDynamics.Acceleration(leaf,
                new(y[ReedLiftIndex], y[ReedVelocityIndex]), pReed - pk) / dps;
        }
        double enteringTag = exhaustMass > 0 ? Fraction(exhaustTag, exhaustMass) : 0;
        for (int i = 0; i < N; i++)
        {
            double fromTransfer = transfer * (transfer >= 0 ? fractions[i] : masses[i] / mc);
            double taggedTransfer = transfer >= 0 ? fromTransfer * fk : transfer * y[i] / mc;
            double taggedExhaust = exhaustMass >= 0 ? exhaustFlux[i] * enteringTag
                : fresh > 0 ? exhaustTag * y[i] / fresh : 0;
            rate[i] = (taggedTransfer + taggedExhaust) / dps;
            rate[Old + i] = (fromTransfer - taggedTransfer + exhaustFlux[i] - taggedExhaust) / dps;
            rate[Case + i] = (reedFlux[i] - fromTransfer) / dps;
            rate[Outer + i] = (outerIntake[i] + outerExhaust[i]) / dps;
        }
        double workC = pc * piston.VolumeDerivativeM3PerDeg, workK = -pk * piston.VolumeDerivativeM3PerDeg;
        double heat = c.Thermal.HohenbergMultiplier * LossModels.HeatTransferCoefficient(vc, pc, tc, meanSpeed)
            * piston.WettedAreaM2 * (tc - c.Thermal.WallTemperatureK) / dps;
        rate[Uc] = (transferH + exhaustFlux[gas.Energy]) / dps - workC - heat;
        rate[Uk] = (reedFlux[gas.Energy] - transferH) / dps - workK;
        rate[CaseTag] = (reedFlux[gas.Tag] - transferTag) / dps;
        rate[Wc] = workC;
        rate[Wk] = workK;
        rate[Q] = heat;
        rate[FuelIn] = reedFlux[0] / dps;
        rate[Delivered] = Math.Max(0, transferTag) / dps;
        rate[TransferReturn] = Math.Max(0, -transferTag) / dps;
        rate[ExhaustReturn] = Math.Max(0, exhaustTag) / dps;
        rate[Short] = Math.Max(0, -exhaustTag) / dps;
        rate[OuterH] = (outerIntake[gas.Energy] + outerExhaust[gas.Energy]) / dps;
        rate[OuterTag] = (outerIntake[gas.Tag] + outerExhaust[gas.Tag]) / dps;
        rate[DuctHeat] = (intakeHeat + exhaustHeat) / dps;
        for (int i = IntakeOffset; i < rate.Length; i++) rate[i] /= dps;
        sample = new(angle, pc, tc, pk, tk, inletMass, transfer, -exhaustMass, vc, vk, fresh, mc,
            pReed, pExhaust) { ReedLiftM = c.Reed.DynamicLeaf is null ? 0 : y[ReedLiftIndex] };
        return Math.Min(dtI, dtE) * c.Numerics.Courant * dps;
    }

    private void ProjectReedContact(Span<double> state)
    {
        if (c.Reed.DynamicLeaf is not { } leaf) return;
        double x = state[ReedLiftIndex], v = state[ReedVelocityIndex];
        if (x <= 0) { state[ReedLiftIndex] = 0; if (v < 0) state[ReedVelocityIndex] = 0; }
        else if (x >= leaf.LimitM) { state[ReedLiftIndex] = leaf.LimitM; if (v > 0) state[ReedVelocityIndex] = 0; }
    }

    internal static double ExhaustFreshFraction(double freshFraction, double extractionRatio) =>
        freshFraction * extractionRatio / (1 - freshFraction + freshFraction * extractionRatio);

    private static double Fraction(double part, double whole) => Math.Clamp(part / whole, 0, 1);
}
