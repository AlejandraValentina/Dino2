using Dino2.Core.FourStroke;

namespace Dino2.Core.GasDynamics;

internal sealed partial class DuctMesh
{
    private readonly GasState[]? centers, faces;
    private readonly double[]? primitives, faceValues;
    private double faceCflTime;

    internal ReadOnlySpan<double> FaceValues(int face) => faceValues.AsSpan(face * equation.Size, equation.Size);

    internal GasState BoundaryState(ReadOnlySpan<double> tube, bool right, Span<double> value)
    {
        if (faces is not null)
        {
            int index = right ? faces.Length - 1 : 0;
            FaceValues(index).CopyTo(value);
            return faces[index];
        }
        (right ? tube[^equation.Size..] : tube[..equation.Size]).CopyTo(value);
        return equation.Decode(value);
    }

    // Piecewise linear primitive reconstruction; the finite-volume update remains conservative.
    internal void Prepare(ReadOnlySpan<double> state)
    {
        if (centers is null) return;
        for (int i = 0; i < Cells; i++) DecodeCenter(state, i);
        faceCflTime = double.PositiveInfinity;
        for (int i = 0; i < Cells; i++) Reconstruct(i, false);
    }

    internal void PrepareValve(ReadOnlySpan<double> state)
    {
        if (centers is null) return;
        for (int i = Cells - 3; i < Cells; i++) DecodeCenter(state, i);
        Reconstruct(Cells - 1, true);
    }

    private void DecodeCenter(ReadOnlySpan<double> state, int i)
    {
        int n = equation.SpeciesCount, width = equation.Size, stride = width + 1;
        var q = state.Slice(i * width, width);
        double last = centers![i].Temperature;
        GasState center;
        try { center = equation.Decode(q, last is >= 200 and <= 6000 ? last : 600); }
        catch (NumericalStateException ex)
        {
            throw new NumericalStateException($"Celda {i + 1}/{Cells}: {ex.Message}");
        }
        centers[i] = center;
        var v = primitives.AsSpan(i * stride, stride);
        for (int k = 0; k < n; k++) v[k] = q[k] / center.Density;
        v[n] = center.Velocity;
        v[n + 1] = center.Pressure;
        v[n + 2] = center.Temperature;
        v[n + 3] = q[equation.Tag] / center.Density;
    }

    private void Reconstruct(int i, bool valveOnly)
    {
        int n = equation.SpeciesCount, width = equation.Size, stride = width + 1;
        Span<double> slope = stackalloc double[stride];
        Span<double> edge = stackalloc double[stride];
        var center = primitives.AsSpan(i * stride, stride);
        // One-sided limited extrapolation at the duct terminations.
        int middle = Math.Clamp(i, 1, Cells - 2);
        var previous = primitives.AsSpan((middle - 1) * stride, stride);
        var central = primitives.AsSpan(middle * stride, stride);
        var following = primitives.AsSpan((middle + 1) * stride, stride);
        double fraction = 1;
        for (int k = 0; k < stride; k++)
        {
            double dl = central[k] - previous[k], dr = following[k] - central[k];
            slope[k] = .5 * Minmod(2 * dl, Minmod(.5 * (dl + dr), 2 * dr));
            if (k < n || (k == n + 3 && !boundedPassiveTag))
                fraction = Bound(fraction, Math.Max(0, center[k]), slope[k]);
            if (k == n + 1) fraction = Bound(fraction, .99 * center[k], slope[k]);
            if (n > 1 && k == n + 2)
                fraction = Bound(fraction, Math.Min(center[k] - 200, 6000 - center[k]), slope[k]);
        }
        // A passive mass fraction has its own bounds. Its slope must not reduce hydro slopes.
        if (boundedPassiveTag)
        {
            double tag = center[n + 3];
            double room = Math.Max(0, Math.Min(tag, 1 - tag));
            slope[n + 3] = Math.Clamp(slope[n + 3], -room, room);
        }
        for (int side = valveOnly ? 1 : 0; side < 2; side++)
        {
            double amount = side == 0 ? -fraction : fraction;
            for (int k = 0; k < stride; k++) edge[k] = center[k] + amount * slope[k];
            double sum = 0;
            for (int k = 0; k < n; k++)
            {
                edge[k] = Math.Max(0, edge[k]); // Face roundoff; cell averages are never clipped.
                sum += edge[k];
            }
            for (int k = 0; k < n; k++) edge[k] /= sum;
            if (boundedPassiveTag)
                edge[n + 3] = Math.Clamp(center[n + 3] + (side == 0 ? -1 : 1) * slope[n + 3], 0, 1);
            int index = 2 * i + side;
            var reconstructed = equation.EncodeFace(faceValues.AsSpan(index * width, width), edge[..n],
                edge[n + 1], edge[n + 2], edge[n], edge[n + 3]);
            faces![index] = reconstructed;
            if (!valveOnly)
                faceCflTime = Math.Min(faceCflTime, volume[i] / (Math.Max(area[i], area[i + 1]) *
                    (Math.Abs(reconstructed.Velocity) + reconstructed.SoundSpeed)));
        }
    }

    private static double Bound(double fraction, double room, double slope) =>
        Math.Abs(slope) > room ? Math.Min(fraction, Math.Max(0, room) / Math.Abs(slope)) : fraction;

    private static double Minmod(double a, double b) =>
        a > 0 && b > 0 ? Math.Min(a, b) : a < 0 && b < 0 ? Math.Max(a, b) : 0;
}
