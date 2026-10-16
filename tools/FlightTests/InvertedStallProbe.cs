using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (INVSTALL_PROBE=1): what the wing does as it is driven to the stall UPRIGHT and INVERTED, from the
/// same level start at the same stick rate — so the dynamic-stall branch is the only thing that differs. Prints the
/// peak load factor, the angle of attack it peaked at, and where the wake first reports the wing stalled.
///
/// Why it exists: the two-branch stall blended the ATTACHED branch in above the table's UPRIGHT Cl-max angle for
/// BOTH signs of alpha. A cambered section reaches its inverted stall several degrees sooner (the Clark Y table:
/// -13.0 deg against +16.5 deg), so inverted the attached branch was carried past the point where the measured
/// polar says the flow has gone. INVSTALL_ID picks the airplane (default c172-like; try extra-300-like, whose
/// symmetric section should be identical either way).
/// </summary>
public class InvertedStallProbe
{
    private readonly ITestOutputHelper _out;
    public InvertedStallProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void ProbeInvertedStall()
    {
        if (System.Environment.GetEnvironmentVariable("INVSTALL_PROBE") == null) return;
        string id = System.Environment.GetEnvironmentVariable("INVSTALL_ID") ?? "c172-like";
        string repo = System.Environment.GetEnvironmentVariable("INVSTALL_REPO")
            ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", id + ".json"));
        var tables = Aircraft.BuildAirfoilTables(cfg);
        foreach (var sf in cfg.Surfaces)
        {
            if (!sf.Id.Contains("wing", System.StringComparison.OrdinalIgnoreCase) || sf.Strips.Count == 0) continue;
            var t = tables[sf.Strips[0].Airfoil];
            _out.WriteLine($"{id} {sf.Id} airfoil {sf.Strips[0].Airfoil}: stall +{t.AlphaClMaxRad * 57.2958:0.0} deg / {t.AlphaClMinRad * 57.2958:0.0} deg");
            break;
        }
        string vs = System.Environment.GetEnvironmentVariable("INVSTALL_V") ?? "60";
        foreach (string one in vs.Split(','))
        {
            double v = double.Parse(one, System.Globalization.CultureInfo.InvariantCulture);
            _out.WriteLine(Run(cfg, inverted: false, v));
            _out.WriteLine(Run(cfg, inverted: true, v));
        }
    }

    private static string Run(AircraftConfig cfg, bool inverted, double v)
    {
        var att = inverted ? new Quat(1, 0, 0, 0) : new Quat(0, 0, 0, 1);
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -2500), att, new Vec3(v, 0, 0), Vec3.Zero),
            new FlyingGame.Core.Aero.ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        double s = inverted ? 1 : -1;        // pull upright is NEGATIVE elevator; inverted the same hand is a push
        const double idle = 1.0;

        // Settle, then ramp the stick toward the stall at a fixed rate and watch the wing.
        sim.RunFor(2.0, new ControlInputs(0, 0, 0, idle));
        double peakN = 0, alphaAtPeak = 0, ktAtPeak = 0;
        double stallAlpha = double.NaN, stallKt = double.NaN, stallN = double.NaN;
        double maxAbsAlpha = 0, maxFrac = 0;
        for (int i = 0; i < 400; i++)
        {
            sim.RunFor(0.02, new ControlInputs(0, s * System.Math.Min(1.0, 0.02 * i), 0, idle));
            var vb = ac.State.Velocity;
            double alpha = System.Math.Atan2(vb.Z, vb.X) * 57.2958;
            double kt = vb.Length * 1.943844;
            double n = System.Math.Abs(ac.LoadFactorZ);
            if (n > peakN) { peakN = n; alphaAtPeak = alpha; ktAtPeak = kt; }
            maxAbsAlpha = System.Math.Max(maxAbsAlpha, System.Math.Abs(alpha));
            maxFrac = System.Math.Max(maxFrac, ac.StalledFraction);
            if (double.IsNaN(stallAlpha) && ac.StalledFraction > 0.35) { stallAlpha = alpha; stallKt = kt; stallN = ac.LoadFactorZ; }
        }
        return $"{cfg.Id} {(inverted ? "INVERTED" : "UPRIGHT ")} entry {v * 1.943844:0} kt: peak |n| {peakN:0.000} at alpha {alphaAtPeak:0.0} deg, {ktAtPeak:0.0} kt"
             + $"   max |alpha| {maxAbsAlpha:0.0} deg, max stalled fraction {maxFrac:0.00}"
             + (double.IsNaN(stallAlpha) ? "   (wake never reported stalled)" : $"   wing stalled at alpha {stallAlpha:0.0} deg, {stallKt:0.0} kt, n {stallN:0.00}");
    }
}
