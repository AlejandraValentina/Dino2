namespace Dino2.Core.Numerics;

public delegate void OdeFunction(double x, ReadOnlySpan<double> state, Span<double> derivative);

public static class RungeKutta
{
    // Classical RK4; caller owns workspace (5*n) so no allocations per step.
    public static void Step(OdeFunction function, double x, ReadOnlySpan<double> state,
        double step, Span<double> destination, Span<double> workspace)
    {
        int n = state.Length;
        var k1 = workspace[..n]; var k2 = workspace.Slice(n, n);
        var k3 = workspace.Slice(2 * n, n); var k4 = workspace.Slice(3 * n, n);
        var temporary = workspace.Slice(4 * n, n);
        function(x, state, k1);
        for (int i = 0; i < n; i++) temporary[i] = state[i] + step / 2 * k1[i];
        function(x + step / 2, temporary, k2);
        for (int i = 0; i < n; i++) temporary[i] = state[i] + step / 2 * k2[i];
        function(x + step / 2, temporary, k3);
        for (int i = 0; i < n; i++) temporary[i] = state[i] + step * k3[i];
        function(x + step, temporary, k4);
        for (int i = 0; i < n; i++) destination[i] = state[i] + step / 6 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]);
    }
}
