using System.Text.Json;
using Dino2.Core.Studies;
using Dino2.Core.TwoStroke;

const int rpm = 9500;
const double shaftOffsetNm = 0.00005;
var original = TwoStrokeConfiguration.RecoveryExample with { Speeds = new(rpm, rpm, 1) };
var variant = original with
{
    Expansion = original.Expansion with
    {
        BellyDiameterM = original.Expansion.BellyDiameterM + 1e-5,
        DiffuserM = original.Expansion.DiffuserM + 5e-6
    },
    Friction = original.Friction with
    {
        ConstantPa = original.Friction.ConstantPa - shaftOffsetNm * (2 * System.Math.PI) / original.Geometry.DisplacementM3
    }
};
var a = TwoStrokeSimulator.Simulate(original, rpm, false);
var b = TwoStrokeSimulator.Simulate(variant, rpm, false);
var check = TwoStrokeComparison.Verify(original, variant, a, b);
System.Console.WriteLine(JsonSerializer.Serialize(new { rpm, original, variant, check }));
return check.Verdict == StudyVerdict.Indistinguishable ? 0 : 1;
