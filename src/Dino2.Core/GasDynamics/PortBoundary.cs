using Dino2.Core.FourStroke;
using Dino2.Core.Thermochemistry;

namespace Dino2.Core.GasDynamics;

internal readonly record struct PortTrace(double ThroatPressurePa, double ThroatTemperatureK, double ThroatMach,
    double MixedPressurePa, double MixedVelocityMps, double MixedTemperatureK, int Iterations)
{
    internal double ThroatMassKgPerS { get; init; }
    internal string Regime { get; init; } = "";
    internal double? WaveResidualMps { get; init; }
    internal bool FlowFromReservoir { get; init; }
    internal double? MapCd { get; init; }
    internal double? MapMassResidualKgPerS { get; init; }
    internal double? MapEffectiveAreaM2 { get; init; }
    internal int MapIterations { get; init; }
}

internal static class PortBoundary
{
    private enum Regime { Step, Jet, SonicJet, Contraction, Free }
    private readonly record struct Solution(PortState Face, PortState Throat, int Iterations, Regime Regime, double? WaveResidual = null);

    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux) =>
        Flux(gas, totalArea, openArea, chamber, reservoir, edge, pipe, flux, out _, true);

    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux, out PortTrace? trace) =>
        Flux(gas, totalArea, openArea, chamber, reservoir, edge, pipe, flux, out trace, true);

    internal static double OneWayFlux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux) =>
        Flux(gas, totalArea, openArea, chamber, reservoir, edge, pipe, flux, out _, false);

    /// <summary>
    /// Resolves an equivalent opening area so the ports-v4 solution carries the mass flow prescribed by
    /// a mass-based Cd map at the *same accepted* mixed-plane pressure. This replaces, rather than
    /// multiplies, the historical opening capacity; the final flux remains a certified ports-v4 state.
    /// </summary>
    internal static double MappedFlux(GasEquation gas, double totalArea, double maximumOpenArea,
        double referenceAreaM2, double normalizedOpening, string referenceAreaConvention,
        DischargeMap forwardMap, DischargeMap? reverseMap, bool reservoirToPipeIsForward,
        ReadOnlySpan<double> chamber, GasState reservoir, ReadOnlySpan<double> edge, GasState pipe,
        Span<double> flux, out PortTrace? trace)
    {
        if (!(maximumOpenArea >= 0 && maximumOpenArea <= totalArea && referenceAreaM2 > 0) ||
            !double.IsFinite(maximumOpenArea + referenceAreaM2))
            throw new ArgumentOutOfRangeException(nameof(maximumOpenArea));
        ValidateMap(forwardMap, referenceAreaConvention);
        if (reverseMap is not null) ValidateMap(reverseMap, referenceAreaConvention);
        if (maximumOpenArea == 0)
            return Flux(gas, totalArea, 0, chamber, reservoir, edge, pipe, flux, out trace);

        Span<double> trialFlux = stackalloc double[gas.Size];
        double lo = 0, hi = maximumOpenArea;
        double atHi = MapResidual(gas, totalArea, hi, referenceAreaM2, normalizedOpening, forwardMap,
            reverseMap, reservoirToPipeIsForward, chamber, reservoir, edge, pipe, trialFlux, out _, out _);
        if (atHi < -1e-10)
            throw new ModelDomainException("La capacidad máxima certificada de ports-v4 no alcanza el caudal requerido por el mapa Cd en este estado.");
        int iterations = 0;
        for (; iterations < 48; iterations++)
        {
            double mid = (lo + hi) * .5;
            double residual = MapResidual(gas, totalArea, mid, referenceAreaM2, normalizedOpening, forwardMap,
                reverseMap, reservoirToPipeIsForward, chamber, reservoir, edge, pipe, trialFlux, out _, out _);
            if (residual > 0) hi = mid; else lo = mid;
            if (Math.Abs(residual) <= 1e-10) break;
        }
        double effectiveArea = (lo + hi) * .5;
        double finalPressure = Flux(gas, totalArea, effectiveArea, chamber, reservoir, edge, pipe, flux, out trace);
        if (trace is not { } finalTrace)
            throw new NumericalStateException("El mapa Cd produjo caudal nulo en una abertura positiva.");
        double finalResidual = MapResidual(gas, totalArea, effectiveArea, referenceAreaM2, normalizedOpening,
            forwardMap, reverseMap, reservoirToPipeIsForward, chamber, reservoir, edge, pipe, trialFlux,
            out _, out double finalCd);
        trace = finalTrace with { MapCd = finalCd, MapMassResidualKgPerS = finalResidual,
            MapEffectiveAreaM2 = effectiveArea, MapIterations = iterations + 1 };
        if (Math.Abs(finalResidual) > Math.Max(1e-9, 1e-7 * Math.Abs(finalTrace.ThroatMassKgPerS)))
            throw new NumericalStateException($"No convergió el acoplamiento Cd–ports-v4: residuo {finalResidual:R} kg/s.");
        return finalPressure;
    }

    private static double MapResidual(GasEquation gas, double totalArea, double openingArea, double referenceAreaM2,
        double normalizedOpening, DischargeMap forwardMap, DischargeMap? reverseMap, bool reservoirToPipeIsForward,
        ReadOnlySpan<double> chamber, GasState reservoir, ReadOnlySpan<double> edge, GasState pipe,
        Span<double> trialFlux, out PortTrace? stateTrace, out double cd)
    {
        Flux(gas, totalArea, openingArea, chamber, reservoir, edge, pipe, trialFlux, out stateTrace);
        if (stateTrace is not { } tr || Math.Abs(tr.ThroatMassKgPerS) <= 1e-15)
        { cd = 0; return 0; }
        bool forward = tr.FlowFromReservoir == reservoirToPipeIsForward;
        var map = SelectMap(forwardMap, reverseMap, forward);
        var upstream = tr.FlowFromReservoir ? reservoir : Stagnation(pipe);
        double downstreamPressure = tr.FlowFromReservoir ? tr.MixedPressurePa : reservoir.Pressure;
        double ratio = downstreamPressure / upstream.Pressure;
        cd = map.Evaluate(normalizedOpening, ratio);
        double ideal = Math.Abs(ReactiveCylinder.Nozzle(referenceAreaM2 * cd,
            upstream.Pressure, upstream.Temperature, upstream.GasConstant, upstream.Gamma,
            downstreamPressure, tr.MixedTemperatureK, upstream.GasConstant, upstream.Gamma));
        return Math.Abs(tr.ThroatMassKgPerS) - ideal;
    }

    private static void ValidateMap(DischargeMap map, string referenceAreaConvention)
    {
        if (!string.Equals(map.Metadata.CompatibleModel, "ports-v4-equivalent-capacity-v1", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.PressureConvention, "upstream_stagnation_to_downstream_static", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(map.Metadata.ReferenceArea, referenceAreaConvention, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El mapa Cd no declara la convención compatible con el acoplamiento de capacidad de ports-v4.");
    }

    private static DischargeMap SelectMap(DischargeMap forward, DischargeMap? reverse, bool isForward)
    {
        var map = isForward ? forward : reverse ??
            (string.Equals(forward.Metadata.Direction, "both", StringComparison.OrdinalIgnoreCase) ? forward : null);
        if (map is null) throw new InvalidOperationException("El mapa Cd no cubre el sentido de flujo actual.");
        if (!string.Equals(map.Metadata.Direction, "both", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(map.Metadata.Direction, isForward ? "forward" : "reverse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El mapa Cd declara un sentido distinto al flujo actual.");
        return map;
    }

    private static GasState Stagnation(GasState state)
    {
        double machSquared = state.Velocity * state.Velocity / (state.Gamma * state.GasConstant * state.Temperature);
        double factor = 1 + .5 * (state.Gamma - 1) * machSquared;
        return state with { Pressure = state.Pressure * Math.Pow(factor, state.Gamma / (state.Gamma - 1)),
            Temperature = state.Temperature * factor, Velocity = 0 };
    }

    private static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux, out PortTrace? trace,
        bool allowReservoirFeed)
    {
        trace = null;
        if (!(totalArea > 0 && openArea >= 0 && double.IsFinite(openArea) && double.IsFinite(totalArea)))
            throw new ArgumentOutOfRangeException(nameof(openArea));
        ValidateDonor(gas, pipe, edge, "conducto");
        double frozenWall = gas.WallPressure(pipe, true);
        var intoTube = pipe with { Velocity = -pipe.Velocity };
        var pipeThermo = new PortThermodynamics(pipe, edge, gas.SpeciesCount);
        var wave = new PortWave(intoTube, pipeThermo);
        if (openArea <= 0)
        {
            var wall = wave.Wall(frozenWall);
            wave.Certify(wall);
            return WallFlux(gas, totalArea, wall.Pressure, flux);
        }
        ValidateDonor(gas, reservoir, chamber, "volumen");
        if (reservoir.Velocity != 0)
            throw new ArgumentException("Este cierre requiere un volumen 0D en reposo.");
        double alpha = Math.Min(1, openArea / totalArea);
        if (alpha <= 0) throw new NumericalStateException("El área relativa positiva no es representable.");
        bool feeding = false;
        Solution solution;
        var thermo = pipeThermo;
        if (pipe.Velocity >= pipe.SoundSpeed && TryFree(intoTube, pipeThermo, alpha, out solution) &&
            reservoir.Pressure <= solution.Throat.Pressure)
        {
            // The throat can carry the incident stream and the downstream static pressure
            // cannot send a compression into it. There is no wall wave in this branch.
        }
        else
        {
            var wall = wave.Wall(frozenWall, allowBound: true);
            if (wall.Speed == 0 && wall.Pressure == reservoir.Pressure)
            {
                wave.Certify(wall);
                return WallFlux(gas, totalArea, wall.Pressure, flux);
            }
            feeding = reservoir.Pressure > wall.Pressure || (reservoir.Pressure == wall.Pressure && wall.Speed > 0);
            if (feeding && !allowReservoirFeed)
            {
                if (wall.Speed != 0)
                    throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda de pared.");
                wave.Certify(wall);
                return WallFlux(gas, totalArea, wall.Pressure, flux);
            }
            if (feeding) thermo = new(reservoir, chamber, gas.SpeciesCount);
            solution = feeding ? Supply(reservoir, thermo, wave, wall, alpha) :
                Discharge(intoTube, pipeThermo, wave, reservoir.Pressure, wall, alpha);
        }
        var mixed = solution.Face;
        var throat = solution.Throat;
        ValidatePlane(throat, thermo, "garganta");
        ValidatePlane(mixed, thermo, "sección media");
        if (!feeding && solution.Regime != Regime.Free) wave.Certify(mixed);
        var donor = feeding ? reservoir : pipe;
        var composition = feeding ? chamber : edge;
        // A single mass flux for the two domains and both planes. The mixed
        // speed is reconstructed from continuity if p cannot represent the
        // last sub-ULP pressure increment next to the wall.
        double j = alpha * throat.MassPerArea(thermo.R);
        double velocity = j / mixed.Density(thermo.R);
        double mass = -j * totalArea;
        double massTolerance = 2e-9 * Math.Abs(j) + mixed.Density(thermo.R) * wave.SpeedFloor(mixed.Pressure);
        if (Math.Abs(mixed.MassPerArea(thermo.R) - j) > massTolerance || (feeding ? j < 0 : j > 0))
            throw new NumericalStateException($"Caudal de garganta incompatible con la sección media: j={j:R}, cara={mixed.MassPerArea(thermo.R):R}, entrada={feeding}.");
        double throatMach = Math.Abs(throat.Speed) / Math.Sqrt(thermo.SoundSquared(throat.Temperature));
        if (throatMach > 1 + 2e-9 && solution.Regime != Regime.Free)
            throw new NumericalStateException("Garganta supersónica en una rama limitada a Mach 1.");
        double faceToThroat = thermo.EnthalpyChange(mixed.Temperature, throat.Temperature) +
            .5 * (throat.Speed * throat.Speed - velocity * velocity);
        double energyTolerance = 2e-8 * thermo.Cp(mixed.Temperature) * mixed.Temperature;
        if (Math.Abs(faceToThroat) > energyTolerance)
            throw new NumericalStateException($"Residuo de entalpía garganta–cara: {faceToThroat:R} J/kg.");
        double entropy = thermo.EntropyChange(throat.Temperature, mixed.Temperature) -
            thermo.R * Math.Log(mixed.Pressure / throat.Pressure);
        double entropyTolerance = 2e-8 * thermo.R;
        if ((feeding ? entropy < -entropyTolerance : Math.Abs(entropy) > entropyTolerance) || !double.IsFinite(entropy))
            throw new NumericalStateException($"Residuo de entropía garganta–cara: {entropy:R} J/(kg·K).");
        if (solution.Regime == Regime.Step)
        {
            double axial = mixed.Pressure - throat.Pressure + j * (velocity - throat.Speed);
            if (Math.Abs(axial) > 2e-9 * mixed.Pressure)
                throw new NumericalStateException($"Residuo de momento en la expansión: {axial:R} Pa.");
        }
        double h0 = (donor.Energy + donor.Pressure) / donor.Density;
        double energyChange = thermo.EnthalpyChange(donor.Temperature, mixed.Temperature) +
            .5 * (velocity * velocity - donor.Velocity * donor.Velocity);
        if (feeding && Math.Abs(energyChange) > energyTolerance)
            throw new NumericalStateException($"Residuo de entalpía donante–cara: {energyChange:R} J/kg.");
        if (!feeding) h0 += energyChange;
        double momentum = totalArea * mixed.Pressure - mass * velocity;
        if (!double.IsFinite(mass * h0) || !double.IsFinite(momentum))
            throw new NumericalStateException("Flujo conservativo no finito en la interfaz.");
        for (int i = 0; i < gas.SpeciesCount; i++) flux[i] = mass * (composition[i] / donor.Density);
        flux[gas.Tag] = mass * (composition[gas.Tag] / donor.Density);
        flux[gas.Energy] = mass * h0;
        flux[gas.Momentum] = momentum;
        trace = new(throat.Pressure, throat.Temperature, throatMach, mixed.Pressure, -velocity, mixed.Temperature, solution.Iterations)
        {
            ThroatMassKgPerS = mass, WaveResidualMps = solution.WaveResidual, FlowFromReservoir = feeding,
            Regime = solution.Regime switch
            {
                Regime.Step => nameof(Regime.Step), Regime.Jet => nameof(Regime.Jet), Regime.SonicJet => nameof(Regime.SonicJet),
                Regime.Free => nameof(Regime.Free), _ => nameof(Regime.Contraction)
            }
        };
        return mixed.Pressure;
    }

    private static double WallFlux(GasEquation gas, double area, double pressure, Span<double> flux)
    {
        double force = pressure * area;
        if (!double.IsFinite(force)) throw new NumericalStateException("Reacción de pared no finita.");
        flux.Clear();
        flux[gas.Momentum] = force;
        return pressure;
    }

    private static Solution Supply(GasState tank, PortThermodynamics thermo, PortWave wave, PortState wall, double alpha)
    {
        double r = thermo.R, t0 = tank.Temperature;
        bool chokes = thermo.TrySonicTemperature(t0, 0, out double ts);
        if (!chokes) ts = PortThermodynamics.MinimumTemperature;
        double sonic = chokes ? Math.Sqrt(thermo.SoundSquared(ts)) : Math.Sqrt(-2 * thermo.EnthalpyChange(t0, ts));
        if (!(sonic > 0)) throw new ModelDomainException("Interfaz fuera de 200–6000 K al acelerar el donante en la garganta.");
        var critical = new PortState(thermo.IsentropicPressure(tank.Pressure, t0, ts), ts, sonic);
        PortState Isentropic(double u)
        {
            double t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
            return new(thermo.IsentropicPressure(tank.Pressure, t0, t), t, u);
        }
        PortState Expanded(PortState jet)
        {
            if (alpha == 1) return jet;
            double j = alpha * jet.MassPerArea(r), k = jet.Pressure + j * jet.Speed;
            double gamma = thermo.SoundSquared(jet.Temperature) / (r * jet.Temperature);
            double m2 = jet.Speed * jet.Speed / (gamma * r * jet.Temperature);
            double factor = 1 - alpha * m2;
            double discriminant = factor * factor + 2 * (gamma + 1) * alpha * (1 - alpha) * m2;
            double u = 2 * j * r * t0 / (k + jet.Pressure * Math.Sqrt(discriminant));
            double t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
            if (!thermo.Perfect)
            {
                double lo = 0, hi = jet.Speed;
                for (int i = 0; i < 20; i++)
                {
                    double residual = u * (k - j * u) - j * r * t;
                    if (Math.Abs(residual) <= 2e-13 * Math.Max(j * r * t, 1e-12)) break;
                    if (residual > 0) hi = u; else lo = u;
                    double next = u - residual / (k - 2 * j * u + j * r * u / thermo.Cp(t));
                    u = next > lo && next < hi ? next : (lo + hi) * .5;
                    t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
                    if (i == 19) throw new NumericalStateException("No se resolvió el momento de la expansión de puerto.");
                }
            }
            return new(k - j * u, t, u);
        }
        // Positive continuation is impossible below the wall pressure. This
        // also avoids evaluating a trial rarefaction into vacuum at tiny α.
        double Incoming(PortState f, out PortState? contact, out double slope)
        {
            contact = null;
            slope = double.NaN;
            if (f.Pressure < wall.Pressure) return -f.Speed;
            try
            {
                var incident = wave.At(f.Pressure, out slope);
                contact = incident;
                return incident.Speed - f.Speed;
            }
            catch (ModelDomainException) when (f.Pressure > wave.UpperPressure) { return double.PositiveInfinity; }
        }
        bool OnWave(PortState f, double residual) =>
            Math.Abs(residual) <= wave.SpeedFloor(f.Pressure) + 2e-11 * f.Speed;

        var first = Expanded(critical);
        if (Incoming(first, out _, out _) <= 0)
        {
            double lo = 0, hi = sonic;
            bool lowerOutside = false;
            double u = hi * .5;
            for (int i = 1; i <= 64; i++)
            {
                var jet = Isentropic(u);
                var face = Expanded(jet);
                double residual = Incoming(face, out var contact, out double waveSlope);
                if (OnWave(face, residual))
                {
                    var matched = contact ?? wave.At(face.Pressure);
                    residual = matched.Speed - face.Speed;
                    if (OnWave(face, residual))
                    {
                        wave.Certify(matched);
                        return new(face, jet, i, Regime.Step, residual);
                    }
                }
                if (u <= lo || u >= hi) break;
                if (residual > 0) { lo = u; lowerOutside = double.IsPositiveInfinity(residual); }
                else hi = u;
                double derivative = StepSlope(thermo, alpha, jet, face, waveSlope);
                u = BracketedNewton(u, residual, derivative, lo, hi);
            }
            if (lowerOutside || wall.Speed != 0)
                throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda receptora de entrada.");
            throw new NumericalStateException("No convergió la compatibilidad de onda en la entrada subcrítica.");
        }
        if (!chokes)
            throw new ModelDomainException("Interfaz fuera de 200–6000 K en la garganta de entrada.");
        double criticalMass = alpha * critical.MassPerArea(r);
        PortState Choked(double u)
        {
            double t = t0 + thermo.TemperatureChange(t0, -.5 * u * u);
            return new(criticalMass * r * t / u, t, u);
        }
        var sonicFace = new PortState(alpha * critical.Pressure, ts, sonic);
        if (Incoming(sonicFace, out var sonicContact, out _) >= 0)
        {
            var contact = sonicContact ?? wave.At(sonicFace.Pressure);
            double residual = contact.Speed - sonic;
            if (residual >= -wave.SpeedFloor(sonicFace.Pressure))
            {
                wave.Certify(contact);
                return new(sonicFace, critical, 0, Regime.SonicJet, residual);
            }
        }
        double low = first.Speed, high = Math.Min(sonic, criticalMass * r * t0 / wall.Pressure);
        // p >= p_wall and T <= T0 imply u <= j R T0/p_wall. Both endpoints
        // now scale with alpha, including openings below the pressure ULP.
        double speed = low + (high - low) * .5;
        for (int count = 1; count <= 64; count++)
        {
            var face = Choked(speed);
            double residual = Incoming(face, out var contact, out double waveSlope);
            if (OnWave(face, residual))
            {
                var matched = contact ?? wave.At(face.Pressure);
                residual = matched.Speed - face.Speed;
                if (OnWave(face, residual))
                {
                    wave.Certify(matched);
                    return new(face, critical, count, Regime.Jet, residual);
                }
            }
            if (speed <= low || speed >= high) break;
            if (residual > 0) low = speed; else high = speed;
            double pressureSlope = -face.Pressure / speed - criticalMass * r / thermo.Cp(face.Temperature);
            speed = BracketedNewton(speed, residual, waveSlope * pressureSlope - 1, low, high);
        }
        // A limiting temperature can bound the bracket without being a valid contact.
        if (wall.Speed != 0 || first.Pressure > wave.UpperPressure)
            throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda receptora de entrada.");
        throw new NumericalStateException("No convergió la compatibilidad de onda en la entrada estrangulada.");
    }

    private static double StepSlope(PortThermodynamics thermo, double alpha, PortState jet, PortState face, double waveSlope)
    {
        if (!double.IsFinite(waveSlope)) return double.NaN;
        // On an isentropic streamline dp/du = -rho*u. Differentiate the
        // area-weighted jet mass and the shoulder momentum relation.
        double rho = jet.Density(thermo.R), pressureSlope = -rho * jet.Speed;
        if (alpha == 1) return waveSlope * pressureSlope - 1;
        double mass = alpha * rho * jet.Speed;
        double massSlope = alpha * rho * (1 - jet.Speed * jet.Speed / thermo.SoundSquared(jet.Temperature));
        double force = jet.Pressure + mass * jet.Speed;
        double forceSlope = pressureSlope + massSlope * jet.Speed + mass;
        double u = face.Speed, rt = thermo.R * face.Temperature;
        double speedSlope = (massSlope * (u * u + rt) - u * forceSlope) /
            (force - 2 * mass * u + mass * thermo.R * u / thermo.Cp(face.Temperature));
        return waveSlope * (forceSlope - massSlope * u - mass * speedSlope) - speedSlope;
    }

    private static double BracketedNewton(double x, double residual, double slope, double lo, double hi)
    {
        double next = x - residual / slope;
        return slope < 0 && next > lo && next < hi && next != x ? next : lo + (hi - lo) * .5;
    }

    private static bool TryFree(GasState pipe, PortThermodynamics thermo, double alpha, out Solution solution)
    {
        var free = new PortState(pipe.Pressure, pipe.Temperature, pipe.Velocity);
        if (alpha == 1)
        {
            solution = new(free, free, 0, Regime.Free);
            return true;
        }
        double kinetic = free.Speed * free.Speed * .5;
        double r = thermo.R, target = -free.MassPerArea(r) / alpha;
        bool sonic = thermo.TrySonicTemperature(free.Temperature, kinetic, out double ts);
        if (!sonic) ts = PortThermodynamics.MaximumTemperature;
        double lo = sonic ? Math.Sqrt(thermo.SoundSquared(ts)) :
            Math.Sqrt(2 * (kinetic - thermo.EnthalpyChange(free.Temperature, ts)));
        double hi = -pipe.Velocity;
        var capacity = new PortState(thermo.IsentropicPressure(free.Pressure, free.Temperature, ts), ts, -lo);
        solution = default;
        if (!(hi >= lo) || -capacity.MassPerArea(r) < target) return false;
        for (int i = 1; i <= 64; i++)
        {
            double u = lo + (hi - lo) * .5;
            double t = free.Temperature + thermo.TemperatureChange(free.Temperature, kinetic - .5 * u * u);
            var throat = new PortState(thermo.IsentropicPressure(free.Pressure, free.Temperature, t), t, -u);
            double residual = -throat.MassPerArea(r) - target;
            if (Math.Abs(residual) <= 2e-11 * target)
            {
                solution = new(free, throat, i, Regime.Free);
                return true;
            }
            if (u <= lo || u >= hi) break;
            if (residual > 0) lo = u; else hi = u;
        }
        throw new NumericalStateException("No convergió la capacidad de la garganta supersónica libre.");
    }

    private static Solution Discharge(
        GasState pipe, PortThermodynamics thermo, PortWave wave, double back, PortState wall, double alpha)
    {
        double r = thermo.R;
        PortState Contracted(PortState bulk, double overBack)
        {
            double pressureLog = overBack < .5 * bulk.Pressure
                ? PortThermodynamics.LogOnePlus(-overBack / bulk.Pressure) : Math.Log(back / bulk.Pressure);
            if (thermo.TryIsentropicTemperature(bulk.Temperature, pressureLog, out double t))
            {
                double kinetic = .5 * bulk.Speed * bulk.Speed - thermo.IsentropicEnthalpyChange(bulk.Temperature, t, pressureLog);
                if (kinetic <= 0) return new(back, t, 0);
                if (2 * kinetic <= thermo.SoundSquared(t)) return new(back, t, -Math.Sqrt(2 * kinetic));
            }
            double ts = thermo.SonicTemperature(bulk.Temperature, .5 * bulk.Speed * bulk.Speed);
            return new(thermo.IsentropicPressure(bulk.Pressure, bulk.Temperature, ts), ts, -Math.Sqrt(thermo.SoundSquared(ts)));
        }
        double Balance(PortState face, PortState throat) => alpha * throat.MassPerArea(r) - face.MassPerArea(r);
        bool incidentSupersonic = -pipe.Velocity >= pipe.SoundSpeed;
        PortState AtBack() => wall.Speed == 0 && wall.Pressure - back <= 1e-9 * wall.Pressure
            ? wave.NearWall(wall, back, back - wall.Pressure) : wave.At(back);
        PortState minimum;
        bool thermalBound = false;
        if (incidentSupersonic)
        {
            double upper = wall.Pressure;
            if (alpha == 1 && back <= upper)
            {
                var atBack = AtBack();
                // The incident stream evacuates when a compression at the prescribed
                // pressure would have nonpositive propagation speed into the duct.
                if (wave.ShockSpeed(atBack) <= 0 && TryFree(pipe, thermo, alpha, out var free)) return free;
                return new(atBack, atBack, 0, Regime.Contraction, 0);
            }
            minimum = wave.StationaryShock();
            if (back <= minimum.Pressure &&
                Balance(minimum, Contracted(minimum, minimum.Pressure - back)) <= 0 &&
                TryFree(pipe, thermo, alpha, out var released)) return released;
        }
        else
        {
            // A subsonic trace at the prescribed pressure needs no sonic auxiliary.
            PortState? atBack = null;
            try
            {
                var f = AtBack();
                if (-f.Speed <= Math.Sqrt(thermo.SoundSquared(f.Temperature))) atBack = f;
            }
            catch (ModelDomainException) when (back < wave.LowerPressure) { }
            if (atBack is { } bulk) minimum = bulk;
            else
            {
                try { minimum = wave.SonicOutflow(); }
                catch (ModelDomainException) when (alpha < 1)
                {
                    minimum = wave.At(wave.LowerPressure);
                    thermalBound = true;
                }
            }
        }
        if (alpha == 1)
            return new(minimum, minimum, 0, Regime.Contraction, 0);

        // No nonzero subsonic contraction reaches a pressure higher than
        // the bulk static pressure. This tightens the bracket at reversal.
        double lo = Math.Max(0, minimum.Pressure - back), wallExcess = wall.Pressure - back, hi = wallExcess;
        if (!(hi >= lo) || (wall.Speed != 0 && Balance(wall, Contracted(wall, hi)) > 0) ||
            (thermalBound && Balance(minimum, Contracted(minimum, lo)) < 0))
            throw new ModelDomainException("Interfaz fuera de 200–6000 K en la onda de descarga.");
        double pressureChange = back - pipe.Pressure;
        PortState mixed = wall, throat = Contracted(wall, hi);
        for (int i = 1; i <= 96; i++)
        {
            double excess = lo + (hi - lo) * .5;
            if (excess == lo || excess == hi)
            {
                double precision = Math.Abs(Math.BitIncrement(mixed.Pressure) - mixed.Pressure);
                double impulse = precision / Math.Sqrt(thermo.SoundSquared(mixed.Temperature));
                if (Math.Abs(Balance(mixed, throat)) <= 32 * impulse)
                    return Checked(mixed, throat, i);
                throw new NumericalStateException($"Residuo de caudal del puerto {Balance(mixed, throat):R} kg/(m²·s), p={mixed.Pressure:R}, p pared={wall.Pressure:R}, p volumen={back:R}, α={alpha:R}.");
            }
            double p = back + excess;
            double fromWall = excess - wallExcess;
            mixed = wall.Speed == 0 && -fromWall <= 1e-9 * wall.Pressure
                ? wave.NearWall(wall, p, fromWall) : wave.At(p, pressureChange + excess);
            throat = Contracted(mixed, excess);
            double discrepancy = Balance(mixed, throat);
            double scale = Math.Max(Math.Abs(mixed.MassPerArea(r)), Math.Abs(alpha * throat.MassPerArea(r)));
            if (Math.Abs(discrepancy) <= 2e-9 * scale) return Checked(mixed, throat, i);
            if (discrepancy > 0) lo = excess; else hi = excess;
        }
        throw new NumericalStateException($"No convergió el balance local de caudal: p={mixed.Pressure:R}, p pared={wall.Pressure:R}, p volumen={back:R}, α={alpha:R}.");

        Solution Checked(PortState face, PortState neck, int iterations)
        {
            // Compare the actual characteristic velocity with the velocity of the shared mass flux.
            double residual = alpha * neck.MassPerArea(r) / face.Density(r) - face.Speed;
            if (Math.Abs(residual) > 2e-9 * Math.Abs(face.Speed) + wave.SpeedFloor(face.Pressure))
                throw new NumericalStateException($"Residuo de onda en la descarga: {residual:R} m/s.");
            return new(face, neck, iterations, Regime.Contraction, residual);
        }
    }

    internal static void ValidateDonor(GasEquation equation, GasState s, ReadOnlySpan<double> values, string plane)
    {
        if (!(double.IsFinite(s.Density) && s.Density > 0 && double.IsFinite(s.Pressure) && s.Pressure > 0 &&
            double.IsFinite(s.SoundSpeed) && s.SoundSpeed > 0 && double.IsFinite(s.GasConstant) && s.GasConstant > 0) ||
            !double.IsFinite(s.Energy) || !double.IsFinite(s.Velocity) || !double.IsFinite(values[equation.Tag]))
            throw new NumericalStateException($"Estado no físico en la interfaz, plano {plane}.");
        double density = 0;
        for (int i = 0; i < equation.SpeciesCount; i++)
        {
            if (!double.IsFinite(values[i]) || values[i] < -1e-12)
                throw new NumericalStateException($"Especie inadmisible en la interfaz, plano {plane}, especie {i}.");
            density += values[i];
        }
        double eosPressure = s.Density * s.GasConstant * s.Temperature;
        if (Math.Abs(density - s.Density) > 1e-11 * s.Density || Math.Abs(eosPressure - s.Pressure) > 1e-11 * s.Pressure)
            throw new NumericalStateException($"Densidad y presión incompatibles con el estado conservativo en {plane}.");
        PortThermodynamics.RequireTemperature(s.Temperature, plane);
    }

    private static void ValidatePlane(PortState state, PortThermodynamics thermo, string plane)
    {
        PortThermodynamics.RequireTemperature(state.Temperature, plane);
        double a2 = thermo.SoundSquared(state.Temperature);
        if (!(state.Pressure > 0 && state.Temperature > 0 && a2 > 0) ||
            !double.IsFinite(state.Pressure) || !double.IsFinite(a2) || !double.IsFinite(state.Speed))
            throw new NumericalStateException($"Estado no físico en la interfaz, plano {plane}.");
    }
}
