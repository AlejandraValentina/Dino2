namespace Dino2.Core.GasDynamics;

// The compact aperture and the solid remainder carry different interface fluxes.
// Areas are effective flow areas (including the declared discharge coefficient).
internal static class PortBoundary
{
    internal static double Flux(GasEquation gas, double totalArea, double openArea, ReadOnlySpan<double> chamber,
        GasState reservoir, ReadOnlySpan<double> edge, GasState pipe, Span<double> flux)
    {
        double wall = openArea < totalArea ? gas.WallPressure(pipe, true) : 0;
        double pressure = wall;
        if (openArea > 0)
        {
            pressure = ReservoirBoundary.RightFlux(gas, chamber, reservoir, edge, pipe, flux);
            for (int i = 0; i < flux.Length; i++) flux[i] *= openArea;
        }
        else flux.Clear();
        flux[gas.Momentum] += (totalArea - openArea) * wall;
        return pressure;
    }
}
