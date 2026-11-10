using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE EXTRA'S ENDO (diagnostic, ENDO=1). Owner 2026-11-08: "extra still does not Endo". An endo is the end-over-end
/// tumble about the span axis an Extra enters from a decelerating vertical line with full power and full aft stick: the
/// nose goes over the top and the aeroplane rotates about its own pitch axis, driven by the propeller's gyroscopic
/// couple and the fuselage's own crossflow, and it continues because the tail has nothing to stop it with — the air
/// over it is neither fast nor attached.
///
/// Two things in this model were holding it up, and this probe is for telling them apart:
///  1. the thrust jet was locked to the body axis, so the tail kept its full blast at any attitude (PropJet);
///  2. the tail's blanketing came from a correlation and not from the aeroplane's own geometry (TailWake.ShadowK),
///     and the Extra was never opted in.
/// ENDO_JETK and ENDO_SK turn each one down or off so the difference is measurable rather than asserted.
/// </summary>
public class ExtraEndoProbe
{
    private readonly ITestOutputHelper _out;
    public ExtraEndoProbe(ITestOutputHelper o) { _out = o; }
    private const double D = 180.0 / Math.PI;

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", id + ".json"));
        if (Environment.GetEnvironmentVariable("ENDO_SK") is string sk && sk.Length > 0)
        { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.ShadowK = double.Parse(sk); }
        if (Environment.GetEnvironmentVariable("ENDO_JETK") is string jk && jk.Length > 0) c.Propulsion!.JetK = double.Parse(jk);
        // The two 2026-11-10 high-alpha authority knobs, so the endo can be measured against them (Ty: "I wonder if
        // it might help the extra do the Endo now", "The extra was lacking both rudder and elevator power at high alpha").
        if (Environment.GetEnvironmentVariable("ENDO_SPOIL") is string spk && spk.Length > 0)
        { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.ControlSpoilerK = double.Parse(spk); }
        if (Environment.GetEnvironmentVariable("ENDO_FINK") is string fnk && fnk.Length > 0)
        { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.FinSidewashK = double.Parse(fnk); }
        if (Environment.GetEnvironmentVariable("ENDO_TLZ") is string tz && tz.Length > 0) c.Propulsion!.ThrustLineZ = double.Parse(tz);
        return c;
    }

    private static double Alpha(RigidBodyState s) => Math.Atan2(s.Velocity.Z, s.Velocity.X) * D;

    /// <summary>Nose-up on a decaying line: pitch attitude <paramref name="thetaDeg"/>, airspeed <paramref name="v"/>
    /// along the body axis, no rotation — the top of a vertical line, where the stick comes back.</summary>
    private static Aircraft OnTheLine(AircraftConfig c, double thetaDeg, double v)
    {
        double th = thetaDeg / D;
        var att = new Quat(0, Math.Sin(th / 2), 0, Math.Cos(th / 2));
        return new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v, 0, 0), Vec3.Zero),
                            ControlDeflections.Neutral);
    }

    private sealed class Run
    {
        public double PitchTurns, PeakQ, FinalQ, AlphaMin = 999, AlphaMax = -999, MinTas = 999;
        public double TailQ = 1.0, JetShare = 1.0;
    }

    private static Run Fly(AircraftConfig c, double thetaDeg, double v, ControlInputs inputs, double seconds)
    {
        Aircraft ac = OnTheLine(c, thetaDeg, v);
        var tables = Aircraft.BuildAirfoilTables(c);
        double h = SimLoop.DefaultFixedDtSec;
        var r = new Run();
        double netQ = 0.0;
        for (double t = 0; t < seconds; t += h)
        {
            ac.Step(inputs, h);
            var s = ac.State;
            if (-s.Position.Z < 600)
                ac.State = new RigidBodyState(new Vec3(s.Position.X, s.Position.Y, s.Position.Z - 2500), s.Attitude, s.Velocity, s.Rates);
            netQ += s.Rates.Y * h;
            r.PeakQ = Math.Max(r.PeakQ, Math.Abs(s.Rates.Y) * D);
            r.FinalQ = s.Rates.Y * D;
            double a = Alpha(s);
            r.AlphaMin = Math.Min(r.AlphaMin, a); r.AlphaMax = Math.Max(r.AlphaMax, a);
            r.MinTas = Math.Min(r.MinTas, s.Velocity.Length);
        }
        r.PitchTurns = Math.Abs(netQ) / (2 * Math.PI);
        // What the tail is flying in at the end: the mean q factor over the stabiliser, and the jet's share of it.
        var st = ac.State;
        (double du, double rad, double sw) = Aircraft.TailSlipstream(c, 1.0, st.Velocity, st.Rates, 1.1, PropModel.PropDrive.Running, false);
        r.JetShare = PropJet.TailShare(c, st.Velocity, st.Rates, c.Mass.CgVec(), du, rad, sw);
        double[] qs = TailWake.StripQFactors(c, tables, st.Velocity, st.Rates);
        double sum = 0.0; int n = 0, idx = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            bool stab = sf.Id.Contains("hstab", StringComparison.OrdinalIgnoreCase) || sf.Id.Contains("elevator", StringComparison.OrdinalIgnoreCase);
            foreach (StripConfig unused in sf.Strips) { if (stab && idx < qs.Length) { sum += qs[idx]; n++; } idx++; }
        }
        r.TailQ = n > 0 ? sum / n : 1.0;
        return r;
    }

    [Fact]
    public void Run_()
    {
        if (Environment.GetEnvironmentVariable("ENDO") == null) return;
        string id = Environment.GetEnvironmentVariable("ENDO_ID") ?? "extra-300-like";
        AircraftConfig c = Load(id);
        _out.WriteLine($"=== {id}  shadowK {c.SpinAero?.ShadowK ?? 0.0}  spoilerK {c.SpinAero?.ControlSpoilerK ?? 0.0}  finK {c.SpinAero?.FinSidewashK ?? 0.0}  jetK {c.Propulsion!.JetK}  thrustLineZ {c.Propulsion.ThrustLineZ:F2}");
        _out.WriteLine("  entry                                   turns  peak q  final q   alpha range     min TAS  tail q  jet share");
        foreach ((string label, double th, double v, ControlInputs inp) in new (string, double, double, ControlInputs)[]
        {
            ("vertical, full aft, full power      ", 88, 10, new ControlInputs(0, -1, 0, -1)),
            ("vertical, full aft+right rudder, pwr", 88, 10, new ControlInputs(0, -1, 1, -1)),
            ("vertical, full aft, idle            ", 88, 10, new ControlInputs(0, -1, 0, 1)),
            ("45 up, aft+left ail+right rud, power", 45, 20, new ControlInputs(-1, -1, 1, -1)),
            ("45 up, full fwd+right rudder, power ", 45, 20, new ControlInputs(0, 1, 1, -1)),
            ("level slow, full aft, full power    ", 10, 18, new ControlInputs(0, -1, 0, -1)),
        })
        {
            Run r = Fly(c, th, v, inp, 10.0);
            _out.WriteLine($"  {label} {r.PitchTurns,6:F2} {r.PeakQ,7:F0} {r.FinalQ,8:F0}   {r.AlphaMin,6:F0}..{r.AlphaMax,-6:F0} {r.MinTas,7:F1} {r.TailQ,7:F2} {r.JetShare,10:F2}");
        }
    }
}
