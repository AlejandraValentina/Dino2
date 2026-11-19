using System.Globalization;
using System.Text.Json;
using Dino2.Core;
using Dino2.Storage;

var checks = new List<(string Name, Action Check)>();
var example = OttoConfiguration.Example;
var baseline = OttoCycle.Calculate(example);
var temporary = Path.Combine(Path.GetTempPath(), "Dino2-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);

void Check(string name, Action test) => checks.Add((name, test));
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Near(double actual, double expected, double relative = 1e-11, double absolute = 1e-10)
{
    Assert(double.IsFinite(actual) && Math.Abs(actual - expected) <= absolute + relative * Math.Abs(expected),
        $"Expected {expected:R}, actual {actual:R}");
}
void Reject(Action operation)
{
    try { operation(); }
    catch (Exception e) when (e is ArgumentException or JsonException or InvalidDataException) { return; }
    throw new Exception("Invalid input was accepted");
}

Check("Independent numerical reference (80 x 80 mm, r=10)", () =>
{
    Near(baseline.DisplacementM3, 0.00040212385965949355);
    Near(baseline.MassKg, 0.0005189364558775243);
    Near(baseline.WorkJ, 249.8753053848507);
    Near(baseline.Efficiency, 0.6018928294465028);
    Near(baseline.ImepPa, 621388.9063843106);
    Near(baseline.States[1].TemperatureK, 753.565929452874);
    Near(baseline.States[2].TemperatureK, 1868.5485078500865);
    Near(baseline.States[3].TemperatureK, 743.8825595021572);
    Near(baseline.States[2].PressurePa, 6228495.026166956);
});
Check("Energy conservation over 150 deterministic configurations", () =>
{
    var random = new Random(734);
    for (int i = 0; i < 150; i++)
    {
        var c = example with { BoreM = .03 + random.NextDouble() * .12,
            StrokeM = .03 + random.NextDouble() * .12,
            CompressionRatio = 2 + random.NextDouble() * 18,
            Gamma = 1.15 + random.NextDouble() * .4,
            InitialPressurePa = 50_000 + random.NextDouble() * 250_000,
            InitialTemperatureK = 250 + random.NextDouble() * 200,
            HeatInputJPerKg = random.NextDouble() * 1_500_000 };
        var result = OttoCycle.Calculate(c);
        Near(result.HeatInJ - result.HeatOutJ, result.WorkJ);
        foreach (var state in result.States)
            Near(state.PressurePa * state.VolumeM3,
                result.MassKg * c.GasConstantJPerKgK * state.TemperatureK);
        Assert(result.Efficiency > 0 && result.Efficiency < 1, "Efficiency outside [0,1]");
        Assert(result.States[2].TemperatureK >= result.States[1].TemperatureK, "Heat addition cooled gas");
    }
});
Check("Net work equals independent numerical integral of p dV", () =>
{
    var s = baseline.States;
    double Integrate(CycleState start, double endVolume)
    {
        const int n = 50_000;
        double dv = (endVolume - start.VolumeM3) / n;
        double integral = 0;
        for (int i = 0; i < n; i++)
        {
            double v = start.VolumeM3 + (i + .5) * dv;
            integral += start.PressurePa * Math.Pow(start.VolumeM3 / v, example.Gamma) * dv;
        }
        return integral;
    }
    Near(Integrate(s[0], s[1].VolumeM3) + Integrate(s[2], s[3].VolumeM3), baseline.WorkJ, 1e-7);
});
Check("Four-stroke cycle frequency and torque-power identity at every RPM", () =>
{
    foreach (var p in baseline.Points)
    {
        Near(p.IndicatedPowerW, baseline.WorkJ * p.Rpm / 120);
        Near(p.IndicatedPowerW, p.IndicatedTorqueNm * 2 * Math.PI * p.Rpm / 60);
        Near(p.IndicatedTorqueNm * 4 * Math.PI, baseline.WorkJ);
    }
    Near(baseline.Points[0].IndicatedPowerW, 2082.2942115404226);
    Near(baseline.Points[^1].IndicatedPowerW, 12493.765269242534);
});
Check("RPM sweep endpoints, non-divisible interval and single point", () =>
{
    Assert(baseline.Points.Count == 11 && baseline.Points[^1].Rpm == 6000, "Endpoint missing");
    var odd = OttoCycle.Calculate(example with { RpmStart = 1000, RpmEnd = 2100, RpmStep = 500 });
    Assert(odd.Points.Select(p => p.Rpm).SequenceEqual([1000, 1500, 2000]), "Unexpected grid");
    Assert(OttoCycle.Calculate(example with { RpmEnd = 1000 }).Points.Count == 1, "Single RPM failed");
});
Check("Zero heat yields zero work, power and torque", () =>
{
    var result = OttoCycle.Calculate(example with { HeatInputJPerKg = 0 });
    Near(result.WorkJ, 0);
    Near(result.HeatOutJ, 0);
    foreach (var p in result.Points) { Near(p.IndicatedPowerW, 0); Near(p.IndicatedTorqueNm, 0); }
});
Check("Geometry, heat and pressure scaling", () =>
{
    Near(OttoCycle.Calculate(example with { StrokeM = example.StrokeM * 2 }).WorkJ, baseline.WorkJ * 2);
    Near(OttoCycle.Calculate(example with { HeatInputJPerKg = example.HeatInputJPerKg * 2 }).WorkJ, baseline.WorkJ * 2);
    Near(OttoCycle.Calculate(example with { InitialPressurePa = example.InitialPressurePa * 2 }).WorkJ, baseline.WorkJ * 2);
    Assert(OttoCycle.Calculate(example with { CompressionRatio = 12 }).Efficiency > baseline.Efficiency, "r does not increase efficiency");
});
Check("Invalid physical inputs and nonfinite numbers rejected", () =>
{
    OttoConfiguration[] invalid = [example with { BoreM = 0 }, example with { StrokeM = -1 },
        example with { CompressionRatio = 1 }, example with { Gamma = 1 },
        example with { InitialPressurePa = -1 }, example with { InitialTemperatureK = 0 },
        example with { HeatInputJPerKg = -1 }, example with { GasConstantJPerKgK = 0 },
        example with { BoreM = double.NaN }, example with { HeatInputJPerKg = double.PositiveInfinity },
        example with { Gamma = double.NegativeInfinity }];
    foreach (var c in invalid) Reject(() => OttoCycle.Calculate(c));
});
Check("Invalid and excessive RPM sweeps rejected without looping", () =>
{
    OttoConfiguration[] invalid = [example with { RpmStart = 0 }, example with { RpmEnd = 999 },
        example with { RpmEnd = 30001 }, example with { RpmStep = 0 },
        example with { RpmStep = int.MaxValue }, example with { RpmStart = 1, RpmEnd = 30000, RpmStep = 1 }];
    foreach (var c in invalid) Reject(() => OttoCycle.Calculate(c));
});
Check("Engineering envelope corner cases remain finite", () =>
{
    foreach (double ratio in new[] { 1.01, 30.0 })
    foreach (double gamma in new[] { 1.05, 1.67 })
    {
        var result = OttoCycle.Calculate(example with { CompressionRatio = ratio, Gamma = gamma,
            BoreM = 1, StrokeM = 1, InitialPressurePa = 1e6, InitialTemperatureK = 150, HeatInputJPerKg = 3e6 });
        Assert(double.IsFinite(result.WorkJ), "Nonfinite work");
        Near(result.HeatInJ - result.HeatOutJ, result.WorkJ);
    }
});
Check("Configuration persistence and deterministic recalculation", () =>
{
    var path = Path.Combine(temporary, "motor.json");
    ConfigurationFile.Save(path, example);
    var loaded = ConfigurationFile.Load(path);
    Assert(loaded == example, "Configuration changed on disk");
    Near(OttoCycle.Calculate(loaded).WorkJ, baseline.WorkJ, 0, 0);
    ConfigurationFile.Save(path, example with { BoreM = .09 });
    Near(ConfigurationFile.Load(path).BoreM, .09);
    Reject(() => ConfigurationFile.Save(path, example with { Gamma = 0 }));
    Near(ConfigurationFile.Load(path).BoreM, .09);
    Assert(!Directory.EnumerateFiles(temporary, "*.tmp").Any(), "Temporary file leaked");
});
Check("Malformed, incomplete, incompatible and unknown JSON rejected", () =>
{
    var goodPath = Path.Combine(temporary, "good.json");
    ConfigurationFile.Save(goodPath, example);
    string good = File.ReadAllText(goodPath);
    string[] invalid = ["{", "null", "{}", good.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
        good.Replace(OttoCycle.ModelId, "other-model"),
        good.Replace("\"boreM\": 0.08,", ""), good.Replace("\"gamma\": 1.4", "\"gamma\": 0"),
        good.Replace("\"boreM\"", "\"unknown\""),
        "{\"schemaVersion\":1,\"model\":\"otto-air-standard-v1\",\"configuration\":null}"];
    var path = Path.Combine(temporary, "invalid.json");
    foreach (var text in invalid) { File.WriteAllText(path, text); Reject(() => ConfigurationFile.Load(path)); }
});
Check("CSV round trip numbers and traceability across decimal cultures", () =>
{
    var previous = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-UY");
        var path = Path.Combine(temporary, "results.csv");
        ResultCsv.Save(path, baseline);
        var lines = File.ReadAllLines(path);
        Assert(lines.Any(l => l.Contains(OttoCycle.ModelId)), "Missing model");
        Assert(lines.Any(l => l.StartsWith("# configuration_SI=")), "Missing input snapshot");
        var data = lines.Where(l => !l.StartsWith('#')).Skip(1).ToArray();
        Assert(data.Length == baseline.Points.Count, "CSV point count changed");
        var values = data[^1].Split(',');
        Near(double.Parse(values[2], CultureInfo.InvariantCulture), baseline.Points[^1].IndicatedPowerW, 0, 0);
    }
    finally { CultureInfo.CurrentCulture = previous; }
});

int failures = 0;
try
{
    foreach (var (name, test) in checks)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + e); }
    }
    Console.WriteLine($"{checks.Count - failures}/{checks.Count} test groups passed.");
}
finally { Directory.Delete(temporary, recursive: true); }
return failures == 0 ? 0 : 1;
