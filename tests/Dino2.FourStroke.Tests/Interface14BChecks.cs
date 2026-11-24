using Dino2.Core.FourStroke;
using Dino2.Core.GasDynamics;
using Dino2.Core.Thermochemistry;

internal static class Interface14BChecks
{
    private static readonly double[] Products = [0, .018, .739, .151, .092, 0, 0];
    private static readonly double[] Air = [0, .233, .767, 0, 0, 0, 0];

    internal static void Run()
    {
        foreach (double t in new[] { 300, 900, 1800, 3300 })
        {
            HotShock(t);
            StationaryShock(t, Air);
            StationaryShock(t, Products);
        }
        NearReversal();
        Domain();
    }

    private static void StationaryShock(double temperature, double[] composition)
    {
        const double p1 = 100000;
        var gas = new GasEquation(composition.Length);
        var conserved = new double[gas.Size];
        var initial = gas.Encode(conserved, composition, p1, temperature, 0, 0);
        var incident = gas.Encode(conserved, composition, p1, temperature, -2 * initial.SoundSpeed, 0);
        var thermo = new PortThermodynamics(incident, conserved, gas.SpeciesCount);
        var wave = new PortWave(incident, thermo);
        double mass = incident.Density * incident.Velocity;
        double h0 = SpeciesThermodynamics.Enthalpy(composition, temperature) + .5 * incident.Velocity * incident.Velocity;
        double axial = p1 + mass * incident.Velocity;
        var after = wave.StationaryShock(wave.Wall(gas.WallPressure(incident, false)).Pressure);
        double downstreamMass = after.Pressure * after.Speed / (incident.GasConstant * after.Temperature);
        Near(downstreamMass / mass, 1, 3e-11, "masa en choque estacionario");
        Near(after.Pressure + downstreamMass * after.Speed, axial, .0001, "momento en choque estacionario");
        Near(SpeciesThermodynamics.Enthalpy(composition, after.Temperature) + .5 * after.Speed * after.Speed,
            h0, .001, "entalpía total en choque estacionario");
        Check(after.Pressure > p1 && Math.Abs(after.Speed) < Math.Sqrt(thermo.SoundSquared(after.Temperature)),
            "El choque estacionario debe comprimir y dejar una traza subsónica.");
    }

    private static void HotShock(double temperature)
    {
        var gas = new GasEquation(7);
        var pipe = new double[gas.Size]; var volume = new double[gas.Size]; var flux = new double[gas.Size];
        var atRest = gas.Encode(pipe, Products, 100000, temperature, 0, .35);
        var upstream = gas.Encode(pipe, Products, 100000, temperature, 2 * atRest.SoundSpeed, .35);
        var back = gas.Encode(volume, Air, 700000, 350, 0, .1);
        PortBoundary.Flux(gas, 1, 1, volume, back, pipe, upstream, flux, out var full);
        var post = full!.Value;
        double rho = post.MixedPressurePa / (upstream.GasConstant * post.MixedTemperatureK);
        double u = post.MixedVelocityMps, a = upstream.Velocity;
        double shock = (rho * u - upstream.Density * a) / (rho - upstream.Density);
        Check(shock < 0 && u > 0, "El choque móvil debe entrar al conducto.");
        Near(post.MixedPressurePa + rho * Math.Pow(u - shock, 2),
            upstream.Pressure + upstream.Density * Math.Pow(a - shock, 2), .0001, "momento en marco del choque");
        double h1 = SpeciesThermodynamics.Enthalpy(Products, temperature);
        double h2 = SpeciesThermodynamics.Enthalpy(Products, post.MixedTemperatureK);
        Near(h2 + .5 * Math.Pow(u - shock, 2), h1 + .5 * Math.Pow(a - shock, 2), .001, "energía en marco del choque");
        double cpOverT(double t) => Products.Select((fraction, i) => fraction * SpeciesThermodynamics.Cp(i, t) / t).Sum();
        double ds = 0;
        for (int i = 0; i < 4096; i++)
        {
            double dt = (post.MixedTemperatureK - temperature) / 4096;
            ds += cpOverT(temperature + (i + .5) * dt) * dt;
        }
        ds -= upstream.GasConstant * Math.Log(post.MixedPressurePa / upstream.Pressure);
        Check(ds > 0, "El choque compresivo debe generar entropía.");
        double referenceMass = flux.Take(gas.SpeciesCount).Sum();
        double referenceH = flux[gas.Energy] / referenceMass, referenceMomentum = flux[gas.Momentum];
        Near(referenceH, h2 + .5 * u * u, .001, "entalpía transportada");
        foreach (double alpha in new[] { .9, .99, .9999, .999999, .999999999 })
        {
            PortBoundary.Flux(gas, 1, alpha, volume, back, pipe, upstream, flux, out var trace);
            double mass = flux.Take(gas.SpeciesCount).Sum();
            double localMass = trace!.Value.MixedPressurePa * trace.Value.MixedVelocityMps / (upstream.GasConstant * trace.Value.MixedTemperatureK);
            Near(mass, localMass, 1e-9, "masa en la sección");
            Near(mass, trace.Value.ThroatMassKgPerS, 1e-9, "masa en garganta");
            if (alpha > .9999999)
            {
                Near(mass, referenceMass, .0001, "límite de masa a sección plena");
                Near(flux[gas.Energy] / mass, referenceH, .02, "límite de entalpía a sección plena");
                Near(flux[gas.Momentum], referenceMomentum, .01, "límite de momento a sección plena");
            }
        }
    }

    private static void NearReversal()
    {
        var gas = new GasEquation(1);
        var pipe = new double[gas.Size]; var tank = new double[gas.Size]; var flux = new double[gas.Size];
        var inside = gas.Encode(pipe, [1], 100000, 300, 0, .2);
        foreach (double dp in new[] { -500.0, -1e-3, -1e-6, 0, 1e-6, 1e-3, 500 })
        foreach (double alpha in new[] { 0, 1e-12, 1e-9, 1e-6, .03, .3, .999999999, 1 })
        {
            var outside = gas.Encode(tank, [1], 100000 + dp, 300, 0, .8);
            PortBoundary.Flux(gas, 1, alpha, tank, outside, pipe, inside, flux, out var result);
            if (alpha == 0 || dp == 0)
            {
                Near(flux[0], 0, 0, "masa en equilibrio o cierre");
                Near(flux[gas.Energy], 0, 0, "energía en equilibrio o cierre");
                Near(flux[gas.Momentum], 100000, 0, "reacción en reposo");
                continue;
            }
            Check(Math.Sign(flux[0]) == -Math.Sign(dp), "Inversión con signo incompatible.");
            var port = result!.Value;
            double throat = alpha * port.ThroatPressurePa / (287 * port.ThroatTemperatureK) *
                port.ThroatMach * Math.Sqrt(1.4 * 287 * port.ThroatTemperatureK);
            Near(Math.Abs(flux[0]), throat, 2e-14 * throat + 1e-25, "capacidad de garganta publicada");
            Check(Math.Abs(flux[0]) <= alpha * 240, "Caudal por encima del máximo sónico de los dos donantes.");
        }
        var reverse = gas.Encode(tank, [1], 99999.999999, 300, 0, 0);
        PortBoundary.Flux(gas, 1, 1e-12, tank, reverse, pipe, inside, flux);
        double incompressible = 1e-12 * Math.Sqrt(2 * inside.Density * (inside.Pressure - reverse.Pressure));
        Near(flux[0] / incompressible, 1, .0002, "límite inercial inverso de abertura diminuta");
    }

    private static void Domain()
    {
        var gas = new GasEquation(7);
        var pipe = new double[gas.Size]; var tank = new double[gas.Size];
        var flux = Enumerable.Repeat(123.0, gas.Size).ToArray();
        var inlet = gas.Encode(pipe, Air, 20000, 300, 0, 0);
        var cold = gas.Encode(tank, Products, 500000, 225, 0, 0);
        Reject(cold, inlet);
        var initial = gas.Encode(pipe, Products, 100000, 5000, 0, 0);
        var hot = gas.Encode(pipe, Products, 100000, 5000, 2 * initial.SoundSpeed, 0);
        var back = gas.Encode(tank, Air, 700000, 400, 0, 0);
        Reject(back, hot);
        void Reject(GasState left, GasState right)
        {
            try { PortBoundary.Flux(gas, 1, .3, tank, left, pipe, right, flux); }
            catch (ModelDomainException ex) when (ex.Message.Contains("garganta") || ex.Message.Contains("sección media"))
            {
                Check(flux.All(x => x == 123), "Se escribió un flujo antes de validar los planos.");
                return;
            }
            throw new Exception("Se aceptó una interfaz fuera del dominio termoquímico.");
        }
    }

    private static void Near(double actual, double expected, double tolerance, string name) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"{name}: {actual:R}, esperado {expected:R} ± {tolerance:R}");

    private static void Check(bool condition, string detail)
    {
        if (!condition) throw new Exception(detail);
    }
}
