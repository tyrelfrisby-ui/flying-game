using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Owner 2026-09-12: a taildragger yawed on the ground must not reverse the swerve on its own — the mains ahead of
/// the CG feed it; the castoring tailwheel and the fin can only slow it. Hands off, a started swerve grows.</summary>
public class GroundLoopTests
{
    private readonly ITestOutputHelper _out;
    public GroundLoopTests(ITestOutputHelper o) { _out = o; }
    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));

    /// <summary>Cub rolling at v, nose yawed `yawDeg` LEFT of its velocity, hands off at idle. Returns the yaw-from-velocity history.</summary>
    private List<(double t, double yawDeg, double rate)> Swerve(string file, double v, double yawDeg, double lever = 1.0)
    {
        var c = Load(file); WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        double psi = yawDeg * Math.PI / 180;
        var yaw = new Quat(0, 0, Math.Sin(psi / 2), Math.Cos(psi / 2));
        Quat att = Quat.Multiply(yaw, rest.Attitude);
        // Velocity along +x (the runway); the nose points psi left of it → body-frame velocity has a right-going component.
        Vec3 vBody = att.Conjugate().Rotate(new Vec3(v, 0, 0));
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, att, vBody, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        var hist = new List<(double, double, double)>();
        _out.WriteLine($"{file} v={v}: stance pitch {Math.Asin(Math.Clamp(2 * (rest.Attitude.W * rest.Attitude.Y - rest.Attitude.Z * rest.Attitude.X), -1, 1)) * 57.3:F1}°");
        for (double t = 0; t <= 4.0; t += 0.02)
        {
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, lever));
            var q = ac.State.Attitude;
            double heading = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            Vec3 vw = q.Rotate(ac.State.Velocity);
            double track = Math.Atan2(vw.Y, vw.X);
            double rel = heading - track; while (rel > Math.PI) rel -= 2 * Math.PI; while (rel < -Math.PI) rel += 2 * Math.PI;
            hist.Add((t, rel * 180 / Math.PI, ac.State.Rates.Z * 180 / Math.PI));
            if (Math.Abs(t - Math.Round(t * 4) / 4) < 0.011 && t <= 2.0)
                _out.WriteLine($"  t={t,4:F2}  yaw-from-track {rel * 57.3,6:F1}°  rate {ac.State.Rates.Z * 57.3,6:F1}°/s  V {ac.State.Velocity.Length,5:F1}  tw angle {(ac.Tailwheel?.AngleRad ?? 0) * 57.3,6:F1}° tw side {ac.Tailwheel?.LastLateralN ?? 0,6:F0} N load {ac.Tailwheel?.LastLoadN ?? 0,5:F0} N");
        }
        return hist;
    }

    /// <summary>Heading history: a swerve of `yawDeg` (nose left of track) at speed v, hands off at idle.</summary>
    private (double headingChangeDeg, double minAlongDeg, double maxRateDegS) HeadingSwing(string file, double v, double yawDeg)
    {
        var c = Load(file); WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        double psi = yawDeg * Math.PI / 180;
        Quat att = Quat.Multiply(new Quat(0, 0, Math.Sin(psi / 2), Math.Cos(psi / 2)), rest.Attitude);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, att, att.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        double h0 = Heading(ac), minAlong = 0, maxRate = 0;
        for (double t = 0; t <= 4.0; t += 0.02)
        {
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
            double d = Wrap(Heading(ac) - h0) * Math.Sign(psi);   // + = swinging further the way it started
            minAlong = Math.Min(minAlong, d);
            maxRate = Math.Max(maxRate, ac.State.Rates.Z * Math.Sign(psi));
        }
        return (Wrap(Heading(ac) - h0) * Math.Sign(psi) * 180 / Math.PI, minAlong * 180 / Math.PI, maxRate * 180 / Math.PI);
    }
    private static double Heading(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)); }
    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    /// <summary>
    /// Owner's real-life benchmarks (2026-09-12): hands-off on the ground the Extra is mildly stable, the Decathlon neutral
    /// to slightly unstable, the Pitts unstable; and NO taildragger ever swings back against a swerve — the castoring
    /// tailwheel at best mitigates it. Seed a 5°/s yaw rate at taxi speed (8 m/s) and look at the rate 3 s later.
    /// </summary>
    [Theory]
    [InlineData("extra-300-like.json", 0.2, 0.9)]
    [InlineData("decathlon-8kcab-like.json", 0.7, 1.6)]
    [InlineData("pitts-s2b-like.json", 2.5, 100.0)]
    [InlineData("pa18-cub-like.json", 0.8, 3.0)]
    [InlineData("pa18-bush-like.json", 0.8, 3.0)]
    [InlineData("stearman-pt17-like.json", 1.0, 4.0)]
    [InlineData("pa25-pawnee-like.json", 0.4, 1.6)]
    [InlineData("geebee-r2-like.json", 2.5, 100.0)]
    [InlineData("cassutt-f1-like.json", 1.5, 100.0)]
    [InlineData("p51d-like.json", 0.5, 2.0)]
    public void GroundStabilityMatchesTheBenchmarks(string file, double minRatio, double maxRatio)
    {
        var c = Load(file);
        foreach (double v in new[] { 8.0, 14.0 })
        {
            var log = new System.Text.StringBuilder();
            var r = GroundStabilityProbe.YawRateGrowth(c, v, 5, 3, log);
            _out.WriteLine($"{file} v={v}: yaw rate x{r.ratio:F2} after 3 s, heading {r.heading:F1}°\n{log}");
            Assert.True(r.heading > 0, $"{file} v={v}: the heading swung back against the swerve ({r.heading:F1}°)");
            Assert.True(r.ratio > -0.05, $"{file} v={v}: the yaw rate reversed (x{r.ratio:F2})");
            if (v == 8.0) Assert.InRange(r.ratio, minRatio, maxRatio);
        }
    }

    [Fact]
    public void CubSwerveTrace()
    {
        var h = Swerve("pa18-cub-like.json", 10, 8.0);
        _out.WriteLine($"start {h[0].yawDeg:F1}°, end {h[^1].yawDeg:F1}°");
    }
}

public class GroundStabilityProbe
{
    private readonly ITestOutputHelper _out;
    public GroundStabilityProbe(ITestOutputHelper o) { _out = o; }

    /// <summary>Seed a yaw rate (no slip step) hands-off at idle and report how the rate evolves: ratio &gt; 1 = divergent.</summary>
    public static (double ratio, double heading, double lead, double h, double izz, double mass) YawRateGrowth(AircraftConfig c, double v, double r0DegS, double seconds, System.Text.StringBuilder? log = null)
    {
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, rest.Attitude.Conjugate().Rotate(new Vec3(v, 0, 0)), new Vec3(0, 0, r0DegS * Math.PI / 180)), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        // mains lead ahead of the CG in the ground frame at this stance, and the CG height
        Vec3 cg = c.Mass.CgVec(); double lead = 0, h = 0; int n = 0;
        foreach (GearConfig g in c.Gear) if (!g.IsTailwheel && g.PosVec().X < 2.0 && g.PosVec().X > -1.0) { Vec3 w = rest.Attitude.Rotate(g.PosVec() - cg); lead += -w.X; h = w.Z; n++; }
        lead = n > 0 ? -lead / n : 0;
        double h0 = Heading(ac), rate = r0DegS; double rEnd = 0;
        for (double t = 0; t < seconds; t += 0.02)
        {
            sim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
            rEnd = ac.State.Rates.Z * 180 / Math.PI;
            if (log != null && Math.Abs(t / 0.5 - Math.Round(t / 0.5)) < 1e-6) log.Append($"  t={t:F1} r={rEnd:F2}°/s hdg={Wrap(Heading(ac) - h0) * 57.3:F1}°\n");
        }
        return (rEnd / r0DegS, Wrap(Heading(ac) - h0) * 180 / Math.PI, lead, h, c.Mass.Inertia.Izz, c.Mass.MassKg);
    }
    private static double Heading(Aircraft ac) { var q = ac.State.Attitude; return Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)); }
    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    [Fact]
    public void TableOfTaildraggerGroundStability()
    {
        string[] files = { "pa18-cub-like.json", "pa18-bush-like.json", "decathlon-8kcab-like.json", "pitts-s2b-like.json", "stearman-pt17-like.json", "pa25-pawnee-like.json", "extra-300-like.json", "geebee-r2-like.json", "cassutt-f1-like.json", "p51d-like.json" };
        foreach (string f in files)
        {
            var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
            var lo = YawRateGrowth(c, 8, 5, 3);
            var hi = YawRateGrowth(c, 14, 5, 3);
            double lam = c.Mass.MassKg * 10 * lo.lead / c.Mass.Inertia.Izz;
            _out.WriteLine($"{f,-28} m {lo.mass,5:F0} Izz {lo.izz,5:F0} lead {lo.lead * 100,5:F1} cm h {lo.h * 100:F0} cm  m·v·a/Izz@10 {lam:F2}/s | 8 m/s: rate x{lo.ratio:F2} hdg {lo.heading,6:F1}° | 14 m/s: rate x{hi.ratio:F2} hdg {hi.heading,6:F1}°");
        }
    }
}

public class GroundLoopMomentProbe
{
    private readonly ITestOutputHelper _out;
    public GroundLoopMomentProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void GearYawMomentsInASwerve()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        WorldTerrain.Active = null; Atmosphere.SteadyWind = Vec3.Zero;
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        Quat att = rest.Attitude;
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, att, att.Conjugate().Rotate(new Vec3(10, 0, 0)), new Vec3(0, 0, 5 * Math.PI / 180)), ControlDeflections.Neutral) { CaptureForces = true };
        var sim = new SimLoop(ac);
        Vec3 cg = c.Mass.CgVec();
        for (int i = 0; i < 20; i++)
        {
            sim.RunFor(0.05, new ControlInputs(0, 0, 0, 1.0));
            double mz = 0, fy = 0, mzTail = 0, fyTail = 0, mzAero = 0, mzOther = 0, mzMainsLong = 0;
            var kinds = new System.Collections.Generic.Dictionary<string, double>();
            foreach (var f in ac.LastForces)
            {
                Vec3 r = f.PosBody - cg;
                double m = r.X * f.ForceBody.Y - r.Y * f.ForceBody.X + f.MomentBody.Z;
                if (f.Kind == "gear") { if (f.PosBody.X < -2) { mzTail += m; fyTail += f.ForceBody.Y; if (i < 2) _out.WriteLine($"    tail sample pos {f.PosBody} F {f.ForceBody} M {f.MomentBody}"); } else { mz += m; fy += f.ForceBody.Y; mzMainsLong += -r.Y * f.ForceBody.X; } }
                else if (f.Kind == "contact") mzOther += m;
                else { mzAero += m; kinds[f.Kind] = kinds.GetValueOrDefault(f.Kind) + m; }
            }
            var vb = ac.State.Attitude.Conjugate().Rotate(ac.State.Velocity);
            var vg = ac.State.Velocity; var fwd = ac.State.Attitude.Rotate(new Vec3(1,0,0)); double beta = (Math.Atan2(vg.Y, vg.X) - Math.Atan2(fwd.Y, fwd.X)) * 57.3;
            string ks = string.Join(" ", kinds.Where(k => Math.Abs(k.Value) > 5).Select(k => $"{k.Key}:{k.Value:F0}"));
            _out.WriteLine($"t={i * 0.05:F2} beta {beta,5:F1}° mains Fy {fy,6:F0} Mz {mz,6:F0} (long {mzMainsLong,5:F0}) | tail Mz {mzTail,6:F0} | aero {mzAero,6:F0} [{ks}] | contact {mzOther,5:F0} | r {ac.State.Rates.Z * 57.3,6:F2}°/s");
        }
    }
}
