namespace Dino2.Core.GasDynamics;

// Physical mouths and compact junctions are full-area interfaces. The open
// exterior retains the outgoing characteristic policy in supersonic discharge.
internal static class ReservoirBoundary
{
    internal static double Flux(GasEquation equation, ExteriorBoundary mode, ReadOnlySpan<double> exterior,
        GasState ambient, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux, bool allowUpstreamShock = false)
    {
        if (mode != ExteriorBoundary.PressureReservoir)
        {
            equation.Flux(exterior, ambient, edge, pipe, flux);
            return pipe.Pressure;
        }
        if (!allowUpstreamShock && pipe.Velocity <= -pipe.SoundSpeed)
        {
            PortBoundary.ValidateDonor(equation, pipe, edge, "boca supersónica");
            equation.PhysicalFlux(edge, pipe, flux);
            return pipe.Pressure;
        }

        // PortBoundary uses a coordinate pointing toward the reservoir. Reflect
        // the conservative state and then the fluxes with odd axial parity.
        Span<double> mirrored = stackalloc double[equation.Size];
        edge.CopyTo(mirrored);
        mirrored[equation.Momentum] = -mirrored[equation.Momentum];
        double pressure = PortBoundary.Flux(equation, 1, 1, exterior, ambient, mirrored,
            pipe with { Velocity = -pipe.Velocity }, flux);
        for (int i = 0; i < flux.Length; i++)
            if (i != equation.Momentum) flux[i] = -flux[i];
        return pressure;
    }

    internal static double RightFlux(GasEquation gas, ReadOnlySpan<double> plenum, GasState reservoir,
        ReadOnlySpan<double> edge, GasState interior, Span<double> flux) =>
        PortBoundary.Flux(gas, 1, 1, plenum, reservoir, edge, interior, flux);
}
