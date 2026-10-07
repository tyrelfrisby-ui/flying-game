using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ground surfaces: three parallel strips per airport (paved, gravel, grass) plus the apron; everything
/// else is rough ground with high rolling resistance and wheel-scale bumps. The grass strip has smooth swoops.
/// Rolling coefficients follow published ground-roll data (paved 0.02–0.03, gravel ~0.05, grass 0.05–0.08,
/// rough/soft 0.1–0.3).</summary>
[Collection("WorldTerrainActive")]
public class GroundSurfaceTests
{
    private readonly ITestOutputHelper _out;
    public GroundSurfaceTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void StripsAreParallelAndClassified()
    {
        var a = WorldTerrain.Airports[0];
        Assert.Equal(4, WorldTerrain.AirportStrips.Length);
        foreach (var s in WorldTerrain.AirportStrips) if (s.Kind != "paved-xwind") Assert.Equal(0, s.HeadingDeg);
        // The into-wind runway: 09/27 across the main, 75 % of its length, paved, crossing clear of the other strips.
        var xw = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "paved-xwind");
        Assert.Equal(90, xw.HeadingDeg);
        Assert.Equal(WorldTerrain.XwindRunwayLengthM, xw.Length, 6);
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(a.X + xw.Dx, a.Y + 500));
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(a.X + xw.Dx, a.Y - 500));
        Assert.Equal(WorldTerrain.Surface.Rough, WorldTerrain.SurfaceAt(a.X + xw.Dx, a.Y + 620));
        Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(a.X + 100, a.Y + 420));   // still the gravel strip, not the crossing
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(a.X, a.Y));
        Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(a.X + 100, a.Y + 420));
        Assert.Equal(WorldTerrain.Surface.Grass, WorldTerrain.SurfaceAt(a.X - 100, a.Y - 420));
        Assert.Equal(WorldTerrain.Surface.Paved, WorldTerrain.SurfaceAt(a.X + WorldTerrain.ApronDx, a.Y + WorldTerrain.ApronDy));
        Assert.Equal(WorldTerrain.Surface.Rough, WorldTerrain.SurfaceAt(a.X, a.Y + 200));      // pad infield
        Assert.Equal(WorldTerrain.Surface.Rough, WorldTerrain.SurfaceAt(a.X + 3000, a.Y - 3000));
        Assert.True(WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Paved) < WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Gravel));
        Assert.True(WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Gravel) < WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Grass));
        Assert.True(WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Grass) < WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Rough));
        Assert.InRange(WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Rough), 0.1, 0.3);
    }

    [Fact]
    public void GrassStripSwoopsSmoothlyAndRoughGroundIsBumpy()
    {
        var t = new WorldTerrain();
        var a = WorldTerrain.Airports[0];
        var grass = System.Array.Find(WorldTerrain.AirportStrips, s => s.Kind == "grass");
        double gy = a.Y + grass.Dy, min = 9, max = -9, maxGrade = 0, prev = double.NaN;
        for (double x = a.X + grass.Dx - grass.Length / 2 + 10; x < a.X + grass.Dx + grass.Length / 2 - 10; x += 1)
        {
            double h = t.HeightAt(x, gy) - a.ElevationM;
            min = System.Math.Min(min, h); max = System.Math.Max(max, h);
            if (!double.IsNaN(prev)) maxGrade = System.Math.Max(maxGrade, System.Math.Abs(h - prev));
            prev = h;
        }
        Assert.InRange(max - min, 0.3, 0.6);        // gentle swoops, half a metre at most
        Assert.True(min >= -0.001, "swoops never dip below the pad");
        Assert.True(maxGrade < 0.03, $"smooth flowing: max grade {maxGrade:F3} per metre");
        Assert.Equal(0.0, WorldTerrain.GrassSwoopAt(a.X, a.Y));   // paved runway untouched
        // Rough ground: 10–20 cm lumps within a few metres; gravel a small rattle; paved none.
        WorldTerrain.Active = t;
        double bmin = 9, bmax = -9;
        for (double x = 0; x < 20; x += 0.25) { double b = WorldTerrain.MicroBumpAt(a.X + x, a.Y + 200); bmin = System.Math.Min(bmin, b); bmax = System.Math.Max(bmax, b); }
        Assert.InRange(bmax - bmin, 0.15, 0.4);
        Assert.Equal(0.0, WorldTerrain.MicroBumpAt(a.X, a.Y));
        Assert.True(System.Math.Abs(WorldTerrain.MicroBumpAt(a.X + 100.3, a.Y + 420)) < 0.02);
        WorldTerrain.Active = null;
    }

    [Fact]
    public void RolloutOnRoughGroundIsShortAndViolent()
    {
        var (pavedRoll, pavedG, _) = Rollout(WorldTerrain.Surface.Paved);
        var (roughRoll, roughG, roughRollDeg) = Rollout(WorldTerrain.Surface.Rough);
        _out.WriteLine($"paved: 15→4 m/s in {pavedRoll:F0} m, peak {pavedG:F2} g | rough: {roughRoll:F0} m, peak {roughG:F2} g, max roll {roughRollDeg:F0}°");
        Assert.True(pavedRoll > 120, $"paved rollout {pavedRoll:F0} m");
        Assert.True(roughRoll < 0.7 * pavedRoll, $"rough rollout {roughRoll:F0} m vs paved {pavedRoll:F0} m");
        Assert.True(pavedG < 1.5, $"paved should be smooth (peak {pavedG:F2} g)");
        Assert.True(roughG > 2.0, $"rough ground should hammer the gear (peak {roughG:F2} g)");
    }

    private static (double roll, double peakG, double maxRollDeg) Rollout(WorldTerrain.Surface surface)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));
        if (System.Environment.GetEnvironmentVariable("NOIDLEDRAG") != null) c.Propulsion!.IdleDragCd = 0;
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        var a = WorldTerrain.Airports[0];
        double y = surface == WorldTerrain.Surface.Paved ? a.Y : a.Y + 200;
        double x0 = a.X - 600;
        var mains = c.Gear.FindAll(g => !g.IsTailwheel); var tw = c.Gear.Find(g => g.IsTailwheel)!;
        double pitch = System.Math.Atan((mains[0].Pos[2] - tw.Pos[2]) / (mains[0].Pos[0] - tw.Pos[0]));
        var att = new Quat(0, System.Math.Sin(pitch / 2), 0, System.Math.Cos(pitch / 2));
        double maxWz = -999; foreach (var g in c.Gear) maxWz = System.Math.Max(maxWz, att.Rotate(g.PosVec() - c.Mass.CgVec()).Z);
        // Rolling at 15 m/s (below flying speed, so the wheels carry the weight), idle power, stick back.
        // Start ON the surface: the rough lumps under the wheels are up to 0.2 m proud of the pad — starting at the pad height
        // buried the tyres and spring-launched the Cub into a bounce and a nose-over (test artefact).
        double groundHere = a.ElevationM;
        foreach (var g in c.Gear) { Vec3 p = att.Rotate(g.PosVec() - c.Mass.CgVec()); groundHere = System.Math.Max(groundHere, WorldTerrain.WheelGroundHeightAt(x0 + p.X, y + p.Y) + p.Z - maxWz); }
        // Rolling HORIZONTALLY at 15 m/s (the body-x velocity of the tail-down attitude climbed at 2.9 m/s: a hop, then a hard landing).
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(x0, y, -(groundHere + maxWz + 0.01)), att, att.Conjugate().Rotate(new Vec3(15, 0, 0)), Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        double peakG = 0, slowX = double.NaN, maxRoll = 0;
        for (double tt = 0; tt < 60; tt += 0.02)
        {
            // Feet on the pedals: a taildragger rolled out hands-off ground-loops (GroundLoopTests), so hold the heading.
            var qh = ac.State.Attitude;
            double hdg = System.Math.Atan2(2 * (qh.W * qh.Z + qh.X * qh.Y), 1 - 2 * (qh.Y * qh.Y + qh.Z * qh.Z));
            double rudder = System.Math.Clamp(-hdg * 3.0 - ac.State.Rates.Z * 0.8, -1, 1);
            sim.RunFor(0.02, new ControlInputs(0, -0.4, rudder, 1.0));
            if (tt > 0.9) peakG = System.Math.Max(peakG, System.Math.Abs(ac.LoadFactorZ));   // from the first lumps (a short, violent run can be over in 3 s)
            var q = ac.State.Attitude; maxRoll = System.Math.Max(maxRoll, System.Math.Abs(System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y))));
            if (System.Environment.GetEnvironmentVariable("ROLLTRACE") != null && System.Math.Abs(tt * 10 - System.Math.Round(tt * 10)) < 1e-6) System.Console.WriteLine($"TR {surface} t={tt:F1} x={ac.State.Position.X:F1} v={ac.State.Velocity.Length:F1} roll={maxRoll * 57.3:F0} z={ac.State.Position.Z:F2} g={ac.LoadFactorZ:F2} lost={string.Join(',', ac.LostComponents)}");
            if (ac.State.Velocity.Length < 4.0) { slowX = ac.State.Position.X; break; }
        }
        WorldTerrain.Active = null;
        Assert.False(double.IsNaN(slowX), "never slowed to 4 m/s (idle thrust beats the rolling resistance)");
        return (slowX - x0, peakG, maxRoll * 57.3);
    }

    [Fact]
    public void CropDustScoresCoverageAndWireCrossings()
    {
        // Owner 2026-10-07: the field is part-sprayed in a racetrack; five passes are left (18, 9, 19, 10, 20); a pass on its
        // line, 5–20 ft, 80–110 kt paints the swath; outside the window there's no credit and time is docked; the wires are
        // 100 yards in from the north end.
        var f = CropField.Valley; double elev = f.ElevationM;
        Assert.InRange(f.WireAglAt((f.Y0 + f.Y1) / 2), 30.4, 30.6);   // 100 ft at mid-span
        Assert.InRange(f.WireAglAt(f.PoleY0), 41.9, 42.1);
        Assert.InRange(f.X1 - f.WireX, 91, 92);
        var run = new CropDust();
        Assert.Equal(5, run.PassesLeft);
        Assert.Equal(new[] { 17, 8, 18, 9, 19 }, new[] { run.NextSwath(), 8, 18, 9, 19 });
        Assert.InRange(run.Coverage, 0.749, 0.751);
        void Pass(int k, double agl, double kt, double off = 0, bool south = true)
        {
            double y = run.SwathCentreY(k) + off, v = kt / 1.943844;
            for (double d = -60; d <= 660; d += 2)
            {
                double x = south ? f.X1 + 30 - d : f.X0 - 30 + d;
                run.Update(new Vec3(x, y, -(elev + agl)), agl, v, 2 / v);
            }
        }
        // Pass 1 (swath 18) flown 30 ft up, too high: under the wires, no credit, docked time.
        Pass(17, 9.1, 96);
        Assert.Equal(1, run.PassesUnder); Assert.False(run.SwathComplete(17)); Assert.True(run.PenaltySec > 5);
        double pen = run.PenaltySec;
        // Flown properly: credited, no more penalty; the next target is swath 9 (index 8), northbound.
        Pass(17, 3, 96);
        Assert.True(run.SwathComplete(17)); Assert.Equal(pen, run.PenaltySec, 6); Assert.Equal(8, run.NextSwath()); Assert.False(run.Southbound(8));
        // Flown 10 m off its line: nothing painted.
        Pass(8, 3, 96, off: 10, south: false);
        Assert.False(run.SwathComplete(8));
        foreach (int k in new[] { 8, 18, 9, 19 }) Pass(k, 3, 96, south: run.Southbound(k));
        Assert.True(run.Finished || run.NextSwath() < 0); run.Update(new Vec3(f.X0 - 100, f.Y0, -(elev + 30)), 30, 40, 0.1);
        Assert.True(run.Finished);
        // A run at wire height strikes them.
        var r2 = new CropDust(); double yc = r2.SwathCentreY(17), wire = f.WireAglAt(yc);
        for (double x = f.X1 + 50; x >= f.WireX - 20; x -= 2) r2.Update(new Vec3(x, yc, -(elev + wire)), wire, 45, 0.04);
        Assert.True(r2.WireStrike); Assert.False(r2.Spraying);
    }
}
