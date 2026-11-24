"""Create a private instrumented copy of a frozen core. No edits to production."""
import re, sys
from pathlib import Path

root=Path(sys.argv[1])
port=root/"src/Dino2.Core/GasDynamics/PortBoundary.cs"
body=port.read_text(encoding="utf-8-sig")
start=[m for m in re.finditer(r"(?m)^    (?:private|internal) static double Flux\((?:(?!=>|\{).)*?out PortTrace\? trace(?:(?!=>|\{).)*?\)\s*\{",body,re.S)]
assert len(start)==1
match=start[0]
wrapper=match.group().replace("internal static","private static",1).replace("\n    {","")
current="bool allowReservoirFeed" in wrapper
call="gas, totalArea, openArea, chamber, reservoir, edge, pipe, flux, out trace"
if current: call+=", allowReservoirFeed"
wrapper+=f"""
    {{
        try
        {{
            double p = Uninstrumented({call});
            InterfaceAudit.Accept(gas, totalArea, openArea, edge, pipe, flux, trace);
            return p;
        }}
        catch (ModelDomainException) {{ InterfaceAudit.Reject(true); throw; }}
        catch (NumericalStateException) {{ InterfaceAudit.Reject(false); throw; }}
    }}

"""
port.write_text(body[:match.start()]+wrapper+match.group().replace(" Flux("," Uninstrumented(",1)+body[match.end():],encoding="utf-8")
code = """
using Dino2.Core.FourStroke;
namespace Dino2.Core.GasDynamics;
public static class InterfaceAudit
{
    [ThreadStatic] private static long calls, positive, below1E12, below1E9, chokedNear, domain, numerical, iterations;
    [ThreadStatic] private static double smallest, waveErrorMax, peakMomentum, peakEnergy;
    public sealed record Counts(long Calls,long Positive,long Below1E12,long Below1E9,long ChokedNear,long Domain,
        long Numerical,long Iterations,double Smallest,double WaveErrorMax,double PeakMomentumN,double PeakEnergyW);
    public static Counts Snapshot() => new(calls,positive,below1E12,below1E9,chokedNear,domain,numerical,iterations,
        positive==0?0:smallest,waveErrorMax,peakMomentum,peakEnergy);
    public static void Reset()
    {
        calls=positive=below1E12=below1E9=chokedNear=domain=numerical=iterations=0;
        smallest=double.PositiveInfinity;
        waveErrorMax=peakMomentum=peakEnergy=0;
    }
    internal static void Reject(bool outside)
    {
        calls++;
        if(outside)domain++;else numerical++;
    }
    internal static void Accept(GasEquation gas,double total,double opening,ReadOnlySpan<double> edge,GasState pipe,
        ReadOnlySpan<double> flux,PortTrace? trace)
    {
        calls++;
        double a=Math.Min(1,opening/total);
        if(a>0)
        {
            positive++;
            smallest=Math.Min(smallest,a);
            if(a<=1e-12)below1E12++;
            if(a<=1e-9)below1E9++;
        }
        peakMomentum=Math.Max(peakMomentum,Math.Abs(flux[gas.Momentum]));
        peakEnergy=Math.Max(peakEnergy,Math.Abs(flux[gas.Energy]));
        if(trace is not { } f)return;
        iterations+=f.Iterations;
        if(a<=0||a>1e-6||f.MixedVelocityMps>=0||Math.Abs(f.ThroatMach-1)>1e-6)return;
        chokedNear++;
        try
        {
            var wave=new PortWave(pipe with {Velocity=-pipe.Velocity},new PortThermodynamics(pipe,edge,gas.SpeciesCount));
            double residual=Math.Abs(wave.At(f.MixedPressurePa).Speed+f.MixedVelocityMps);
            waveErrorMax=Math.Max(waveErrorMax,residual);
        }
        catch (ArithmeticException) { } // diagnostic only, never modifies the simulation
    }
}
"""
(root/"src/Dino2.Core/GasDynamics/InterfaceAudit.cs").write_text(code,encoding="utf-8")
