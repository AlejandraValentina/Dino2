using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

internal static class Interface14CChecks
{
    private static readonly double[] Air = [0, .233, .767, 0, 0, 0, 0];
    private static readonly double[] Products = [0, .015, .694, .177, .100, .010, .004];
    private static readonly double[] Perfect = [1];

    internal static void Run(int seed = 140301)
    {
        CloseChoked();
        DomainAndFreeFlow();
        Predictor();
        Sample(seed);
    }

    private static void CloseChoked()
    {
        const double p = 100000, t = 600, tankP = 400000, gamma = 1.4, r = 287;
        var eq = new GasEquation(1);
        double[] pipe = new double[eq.Size], tank = new double[eq.Size], flux = new double[eq.Size];
        var outer = eq.Encode(tank, Perfect, tankP, t, 0, .3);
        double criticalTemperature = 2 * t / (gamma + 1);
        double criticalPressure = tankP * Math.Pow(2 / (gamma + 1), gamma / (gamma - 1));
        double criticalMass = criticalPressure / (r * criticalTemperature) * Math.Sqrt(gamma * r * criticalTemperature);
        foreach (double mach in new[] { -.31, 0, .36 })
        {
            double incident = mach * Math.Sqrt(gamma * r * t);
            var inside = eq.Encode(pipe, Perfect, p, t, incident, .1);
            double InletVelocity(double facePressure)
            {
                double ratio = facePressure / p;
                return facePressure >= p
                    ? -incident + (facePressure - p) * Math.Sqrt(2 / ((gamma + 1) * inside.Density *
                        (facePressure + p * (gamma - 1) / (gamma + 1))))
                    : -incident + 2 * inside.SoundSpeed / (gamma - 1) *
                        (Math.Pow(ratio, (gamma - 1) / (2 * gamma)) - 1);
            }
            double wall = BisectPressure(x => InletVelocity(x), .01 * p, tankP);
            foreach (double area in new[] { 0, 1e-24, 1e-18, 1e-15, 1e-12, 1e-9, 1e-6, .001 })
            {
                double face = PortBoundary.Flux(eq, 1, area, tank, outer, pipe, inside, flux, out var result);
                if (area == 0)
                {
                    Require(Math.Abs(face-wall) < 5e-8 && flux[0] == 0 && flux[eq.Energy] == 0, "Reacción de cierre exacto.");
                    continue;
                }
                double j = area * criticalMass;
                double z = BisectPressure(x =>
                {
                    double u = InletVelocity(x);
                    return x * u / (r * (t - u * u / (2 * gamma * r / (gamma - 1)))) - j;
                }, wall, criticalPressure);
                Require(Math.Abs(face-z) < 5e-8, $"Presión estrangulada junto al cierre: á={area:R}, M={mach:R}, {face:R} vs {z:R}");
                Require(Math.Abs(flux[0] / j + 1) < 5e-13, "Capacidad crítica independiente.");
                Require(result is { Regime: "Jet", WaveResidualMps: not null }, "Certificado de onda presente.");
                Require(Math.Abs(flux[eq.Momentum] - face - flux[0] * result!.Value.MixedVelocityMps) < 5e-8,
                    "Flujo de momento coherente.");
            }
        }
    }

    private static double BisectPressure(Func<double, double> monotone, double low, double high)
    {
        for (int i = 0; i < 70; i++)
        {
            double pressure = (low + high) * .5;
            if (monotone(pressure) < 0) low = pressure; else high = pressure;
        }
        return (low + high) * .5;
    }

    private static void DomainAndFreeFlow()
    {
        var gas = new GasEquation(7);
        double[] tank = new double[gas.Size], pipe = new double[gas.Size], flux = new double[gas.Size];
        foreach ((double t,double mach,double pb) in new[] { (220d,-.8,60000d), (5100d,.6,400000d) })
        {
            var b = gas.Encode(tank, Air, pb, 380, 0, .1);
            var a = gas.Encode(pipe, Air, 100000, t, 0, .2);
            a = gas.Encode(pipe, Air, 100000, t, mach*a.SoundSpeed, .2);
            Array.Fill(flux, 177.0);
            try { PortBoundary.Flux(gas, 1, .37, tank, b, pipe, a, flux); }
            catch (ModelDomainException)
            {
                Require(flux.All(x => x == 177), "No publicar flujo provisional.");
                continue;
            }
            throw new Exception("Se utilizó la onda fuera de rango.");
        }
        var reservoir = gas.Encode(tank, Air, 60000, 380, 0, .1);
        var incident = gas.Encode(pipe, Air, 100000, 5800, 0, .2);
        incident = gas.Encode(pipe, Air, 100000, 5800, 1.6*incident.SoundSpeed, .2);
        var pressure = PortBoundary.Flux(gas, 1, 1, tank, reservoir, pipe, incident, flux, out var trace);
        Require(pressure == incident.Pressure && trace is { Regime: "Free", MixedTemperatureK: 5800 },
            "La pared ficticia no interviene en la descarga libre.");
        reservoir = gas.Encode(tank, Air, 100500, 380, 0, .1);
        pressure = PortBoundary.Flux(gas, 1, .99, tank, reservoir, pipe, incident, flux, out trace);
        Require(pressure == incident.Pressure && trace is { Regime: "Free", ThroatPressurePa: > 100500, ThroatMach: > 1 },
            "La prueba de salida libre parcial usa la presión de garganta.");
    }

    private static void Predictor()
    {
        foreach (double[] composition in new[] { Perfect, Air })
        foreach (DuctScheme scheme in new[] { DuctScheme.FirstOrder, DuctScheme.Muscl })
        foreach (double unreasonable in new[] { 199.0, 6001.0 })
        {
            var gas = new GasEquation(composition.Length);
            var mesh = new DuctMesh(new(.28, .031, .031, 8), gas, scheme);
            var state = new double[mesh.Size];
            var rate = Enumerable.Repeat(177.0, mesh.Size).ToArray();
            for (int i = 0; i < 8; i++)
                gas.Encode(state.AsSpan(i*gas.Size,gas.Size), composition, i == 0 ? 120000 : 100000,
                    i == 0 ? unreasonable : 350, 0, .4);
            var zero = new double[gas.Size];
            try { mesh.Prepare(state); mesh.Rates(state, rate, zero, zero); }
            catch (ArithmeticException)
            {
                Require(rate.All(x => x == 177), "El predictor inadmisible alcanzó el RHS.");
                continue;
            }
            throw new Exception("El predictor inadmisible no fue rechazado.");
        }
    }

    private static void Sample(int seed)
    {
        var noise = new Random(seed);
        double Log(double minimum, double maximum) => Math.Exp(Math.Log(minimum) + noise.NextDouble() * Math.Log(maximum/minimum));
        int accepted = 0, rejected = 0, free = 0, sonic = 0, reverse = 0;
        for (int i = 0; i < 2048; i++)
        {
            double[] pipeMix = i % 3 == 0 ? Perfect : i % 2 == 0 ? Air : Products;
            double[] donorMix = pipeMix.Length == 1 ? Perfect : i % 2 == 0 ? Products : Air;
            var g = new GasEquation(pipeMix.Length);
            var q = new double[g.Size]; var tank = new double[g.Size];
            var flux = Enumerable.Repeat(177.0, g.Size).ToArray();
            double p = Log(30000, 350000), t = Log(205, 4750), tankT = Log(205, 4400);
            double mach = 3.8*noise.NextDouble()-1.5;
            double back = p * (i % 4 == 0 ? 1 + (i % 8 == 0 ? 1 : -1)*Log(1e-15,1e-3) : Log(.12, 8));
            double alpha = i % 11 == 0 ? 0 : i % 7 == 0 ? 1 : i % 5 == 0 ? 1-Log(1e-14,1e-5) : Log(1e-24,1);
            double area = Log(1e-6,.024);
            var a = g.Encode(q,pipeMix,p,t,0,.2);
            a = g.Encode(q,pipeMix,p,t,mach*a.SoundSpeed,.2);
            var b = g.Encode(tank,donorMix,back,tankT,0,.8);
            double face;
            PortTrace? audit;
            try { face = PortBoundary.Flux(g, area, alpha*area, tank,b,q,a,flux,out audit); }
            catch (ModelDomainException)
            {
                Require(flux.All(x=>x==177), "Un caso inadmisible modificó flujo.");
                rejected++;
                continue;
            }
            catch (NumericalStateException ex)
            {
                throw new Exception(FormattableString.Invariant(
                    $"Semilla={seed} caso={i} α={alpha:R} p={p:R} T={t:R} M={mach:R} pb={back:R} Tb={tankT:R}: {ex.Message}"),ex);
            }
            accepted++;
            Require(flux.All(double.IsFinite) && face > 0, "No finito o presión no positiva aceptados.");
            double j = flux.Take(g.SpeciesCount).Sum();
            if (audit is not { } trace)
            {
                Require(j == 0 && flux[g.Energy] == 0 && flux[g.Momentum] == face * area, "Cierre conserva reacción.");
                continue;
            }
            if (trace.Regime == "Free") free++;
            if (Math.Abs(trace.ThroatMach - 1) < 2e-9) sonic++;
            if (j > 0) reverse++;
            Require(trace.ThroatTemperatureK is >= 200 and <= 6000 && trace.MixedTemperatureK is >= 200 and <= 6000,
                "Planos térmicos admisibles.");
            Require(Math.Abs(j-trace.ThroatMassKgPerS) <= 5e-14*Math.Abs(j) &&
                Math.Abs(flux[g.Momentum]-face*area-j*trace.MixedVelocityMps) <= 1e-13*face*area,
                "Caudal y momento en planos conservativos.");
            var w = new double[g.Size];
            if (i % 8 == 0)
            {
                double pressure = PortBoundary.Flux(g, 2*area, 2*alpha*area,tank,b,q,a,w);
                Require(Math.Abs(face-pressure) <= 1e-11*face, "Invariancia al escalar ambas áreas.");
            }
        }
        Require(accepted > 1000 && rejected > 0 && free > 0 && sonic > 0 && reverse > 0, "Cobertura de regímenes.");
        Console.WriteLine($"PORTS-v4 seed={seed} accepted={accepted} outsideDomain={rejected} free={free} sonic={sonic} discharge={reverse}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
