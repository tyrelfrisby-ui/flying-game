using System;
using System.Collections.Generic;
using System.Globalization;
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
/// THE INVERTED FLAT SPIN, ENTERED THE WAY HE FLIES IT (INV_FLAT=1). Ty 2026-11-10: "inverted flat spins are now
/// broken not even possible ... They keep rolling into upright spins on me", then the sequence: "Enter a normal
/// inverted spin with right rudder then add power and right aileron", and the symptom: "The aileron is rolling it
/// out to knife edge then it sometimes flips to upright".
///
/// THE ORDER IS THE WHOLE POINT. PittsPowerSpinTargetTests reaches its inverted flat spin by applying rudder,
/// aileron and power TOGETHER from the stall, and that still works (alpha -86.6). Flown his way - a normal inverted
/// spin first at idle, THEN power and aileron onto an established rotation - the aileron arrives on a different
/// aeroplane. This flies that, and reports the BANK, because knife edge is a roll attitude and nothing else here
/// was watching roll.
/// </summary>
public class InvertedFlatSpinProbe
{
    private readonly ITestOutputHelper _o;
    public InvertedFlatSpinProbe(ITestOutputHelper o) { _o = o; }
    private const double D = 180.0 / Math.PI;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("INV_FLAT") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        string id = Environment.GetEnvironmentVariable("INV_FLAT_ID") ?? "pitts-s2b-like";
        string path = Path.Combine(d.FullName, "configs", "aircraft", id + ".json");
        bool verbose = Environment.GetEnvironmentVariable("INV_FLAT_V") != null;

        _o.WriteLine($"{id}: normal inverted spin on right rudder at idle, then full power + full right aileron");
        _o.WriteLine("  finK spoil |  normal inverted spin  |  after power + right aileron       | WENT");
        _o.WriteLine("             |  alpha  bank   s/turn  |  alpha   bank(min..max)    s/turn  | UPRIGHT?");
        foreach (double fk in Nums("INV_FINK", new[] { -1.0 }))
            foreach (double sp in Nums("INV_SPOIL", new[] { -1.0 }))
            {
                var cfg = AircraftConfigLoader.LoadFromFile(path);
                cfg.SpinAero ??= new SpinAeroConfig();
                if (fk >= 0) cfg.SpinAero.FinSidewashK = fk;
                if (sp >= 0) cfg.SpinAero.ControlSpoilerK = sp;
                Run(cfg, verbose);
            }
    }

    private void Run(AircraftConfig cfg, bool verbose)
    {
        const double IDLE = 1.0, FULL = -1.0;
        // Level INVERTED at the trim speed, then PUSH to the stall at idle.
        double v = 50;
        var att = new Quat(1, 0, 0, 0);                       // rolled 180 about the body x axis
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), att, new Vec3(v, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 300 && ac.State.Velocity.Length > 27; i++) sim.RunFor(0.1, new ControlInputs(0, Math.Min(1, 0.15 + 0.01 * i), 0, IDLE));

        // A NORMAL INVERTED SPIN: full push, full RIGHT rudder, idle, no aileron. Two turns.
        double turns = 0;
        for (int i = 0; i < 400 && turns < 2.0; i++)
        { sim.RunFor(0.1, new ControlInputs(0, 1, 1, IDLE)); turns += Math.Abs(ac.State.Rates.Z) * 0.1 / (2 * Math.PI); Wrap(ac); }
        double aNorm = Alpha(ac), bNorm = Bank(ac), rNorm = ac.State.Rates.Z;

        // THEN full power and full RIGHT aileron, onto the established rotation.
        double bMin = 999, bMax = -999, aEnd = 0, rEnd = 0; bool wentUpright = false;
        for (int i = 0; i < 300; i++)
        {
            sim.RunFor(0.1, new ControlInputs(1.0, 1, 1, FULL));
            Wrap(ac);
            double a = Alpha(ac), b = Bank(ac);
            if (i > 20) { bMin = Math.Min(bMin, b); bMax = Math.Max(bMax, b); }
            if (a > 0) wentUpright = true;                    // alpha crossing zero IS the flip to upright
            aEnd = a; rEnd = ac.State.Rates.Z;
            if (verbose && i % 10 == 9)
                _o.WriteLine($"        {(i + 1) * 0.1,5:F1} s | alpha {a,7:F1} | bank {b,7:F1} | r {ac.State.Rates.Z,6:F2} | TAS {ac.State.Velocity.Length,5:F1}");
        }
        _o.WriteLine($"  {cfg.SpinAero!.FinSidewashK,4:F2} {cfg.SpinAero.ControlSpoilerK,5:F2} |"
            + $" {aNorm,6:F1} {bNorm,6:F1} {Turn(rNorm),7:F2}  |"
            + $" {aEnd,7:F1} {bMin,7:F0}..{bMax,-7:F0} {Turn(rEnd),7:F2}  | {(wentUpright ? "YES" : "no")}");
    }

    /// <summary>Bank: +-180 is upright-level for an INVERTED aeroplane, 0 is upright, +-90 is knife edge.</summary>
    private static double Bank(Aircraft ac)
    {
        var yBody = ac.State.Attitude.Rotate(new Vec3(0, 1, 0));
        var zBody = ac.State.Attitude.Rotate(new Vec3(0, 0, 1));
        return Math.Atan2(yBody.Z, -zBody.Z) * D;
    }

    private static double Turn(double r) => Math.Abs(r) > 1e-6 ? 2 * Math.PI / Math.Abs(r) : double.NaN;
    private static double Alpha(Aircraft ac) => Math.Atan2(ac.State.Velocity.Z, ac.State.Velocity.X) * D;

    private static double[] Nums(string env, double[] fallback)
    {
        string? s = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var list = new List<double>();
        foreach (var p in s.Split(',', StringSplitOptions.RemoveEmptyEntries)) list.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 600) ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 1800), ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
