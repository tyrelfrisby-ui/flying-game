using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Smoke goes ROUND the airframe (Ty 2026-10-13): markers released at the Extra's belly exhaust, and a sheet of them
/// upstream, carried through <see cref="AirframeFlow"/> in its flat spin, never end up inside the fuselage or through a surface.</summary>
public class AirframeFlowTests
{
    private readonly ITestOutputHelper _out;
    public AirframeFlowTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void SmokeGoesRoundTheAirframeInTheFlatSpin()
    {
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", "extra-300-like.json"));
        var flow = new AirframeFlow(cfg, 2.3, 0.5);
        // The upright flat spin (FlatSpinProbe, full aft): α ≈ 77°, ≈ 28 m/s, yawing left ≈ 225°/s.
        double v = 28, a = 77 * System.Math.PI / 180;
        Vec3 V = new(v * System.Math.Cos(a), 1.5, v * System.Math.Sin(a)), w = new(0.6, -0.4, -3.8);
        Vec3 exhaust = new(2.3 - 1.22, 0, 0.5);
        int checkedPts = 0, pushed = 0;
        var starts = new System.Collections.Generic.List<Vec3> { exhaust };
        for (double x = -4; x <= 2.5; x += 0.25) for (double y = -4.2; y <= 4.2; y += 0.3) starts.Add(new Vec3(x, y, 1.6));   // a sheet below (upstream at this α)
        foreach (var r0 in starts)
        {
            Vec3 r = r0, rFree = r0;
            var jet = r0.Equals(exhaust) ? new Vec3(0, 0, 1) : Vec3.Zero;
            var d = flow.Deflection(r0, V, w, jet, r0.Equals(exhaust) ? 70 : 0, 0.1016, 1.0, seed: (uint)checkedPts + 7);
            double maxD = 0;
            // Re-walk the same path to check every point (Deflection returns the push; the path itself is free + push).
            for (int i = 0; i < d.Count; i++) maxD = System.Math.Max(maxD, d[i].Length);
            if (maxD > 0.05) pushed++;
            Vec3 pos = r0; double t = 0;
            foreach (var di in d)
            {
                // the undisturbed path in the rotating frame (same midpoint stepping as Deflection)
                Assert.False(flow.Inside(pos + di, 0.02), $"marker from {r0} inside the airframe at t {t:0.000}: {pos + di}");
                Vec3 Onset(Vec3 q) => -1.0 * (V + Vec3.Cross(w, q));
                Vec3 fm = pos + Onset(pos) * 0.002; pos += Onset(fm) * 0.004; t += 0.004;   // (the free air: no jet)
                checkedPts++;
            }
        }
        _out.WriteLine($"{starts.Count} markers, {checkedPts} points checked, {pushed} pushed round the airframe");
        Assert.True(pushed > starts.Count / 10, "the airframe should deflect a good share of the sheet");
    }

    /// <summary>The separated wakes hold smoke (Ty 2026-10-13): released at the belly in the INVERTED spin (the belly is the
    /// lee side), puffs stay by the airframe several times longer than the free air would carry them away (a bluff body's
    /// bubble empties about once a shedding cycle, St ≈ 0.15 → ≈ 0.3 s here), and they spread across the span.</summary>
    [Fact]
    public void InvertedSpinExhaustGoesIntoTheWakePocket()
    {
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", "extra-300-like.json"));
        var flow = new AirframeFlow(cfg, 2.3, 0.5);
        double v = 30, a = -50 * System.Math.PI / 180;   // InvSpinProbe: α ≈ −50°, ~2.9 s/turn
        Vec3 V = new(v * System.Math.Cos(a), 2, v * System.Math.Sin(a)), w = new(1.0, 0.5, 1.9);
        Vec3 exhaust = new(2.3 - 1.22, 0, 0.5), jd = new(-0.866, 0, 0.5);   // (the Extra's pipe: 30° down, aft)
        int n = 40; double stay = 0, stayFree = 0, spread = 0;
        for (int i = 0; i < n; i++)
        {
            var d = flow.Deflection(exhaust, V, w, jd, 70, 0.1016, 1.0, seed: (uint)i + 1);
            Vec3 pos = exhaust; double t = 0, maxY = 0; bool freeOut = false, out_ = false;
            for (int j = 0; j < d.Count; j++)
            {
                Vec3 at = pos + d[j];
                Assert.False(flow.Inside(at, 0.02), $"puff {i} inside the airframe at {at}");
                if (!out_ && at.Length > 2.5) { out_ = true; stay += t; }
                if (!freeOut && pos.Length > 2.5) { freeOut = true; stayFree += t; }
                if (!out_) maxY = System.Math.Max(maxY, System.Math.Abs(at.Y));
                Vec3 On(Vec3 q) => -1.0 * (V + Vec3.Cross(w, q));
                Vec3 fm = pos + On(pos) * 0.002; pos += On(fm) * 0.004; t += 0.004;   // (the free air: no jet)
            }
            spread += maxY;
        }
        _out.WriteLine($"inverted: mean time by the airframe {stay / n:0.00} s (free air {stayFree / n:0.00} s), mean span reached {spread / n:0.00} m");
        Assert.True(stay > 1.5 * stayFree, "the lee-side pocket should hold the inverted spin's exhaust");   // (≈ 1.9× with the real 70 m/s jet, 2026-10-13)
        Assert.True(spread / n > 0.3, "the pocket's eddies should spread it along the span");
    }

    [Fact]
    public void ProbeUprightDeflections()
    {
        if (System.Environment.GetEnvironmentVariable("DEFL_PROBE") == null) return;
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", "extra-300-like.json"));
        var flow = new AirframeFlow(cfg, 2.3, 0.5);
        double v = 28, a = 77 * System.Math.PI / 180;
        Vec3 V = new(v * System.Math.Cos(a), 1.5, v * System.Math.Sin(a)), w = new(0.6, -0.4, -3.8);
        Vec3 jd = new(-0.87, 0, 0.5);
        for (uint i = 1; i <= 12; i++)
        {
            var d = flow.Deflection(new Vec3(1.08, 0, 0.5), V, w, jd, 70, 0.1016, 1.0, seed: i);
            double mx = 0; foreach (var q in d) mx = System.Math.Max(mx, q.Length);
            _out.WriteLine($"puff {i}: steps {d.Count} (t_end {d.Count * 0.004:0.00} s)  final |Δ| {d[d.Count - 1].Length:0.0} m  max {mx:0.0}");
        }
    }

    /// <summary>The exhaust's exit velocity from the engine (Ty 2026-10-13): the Extra's 540 in³ at 2,700 rpm, full power, sea
    /// level, through a 4 in pipe at 900 K → ≈ 70 m/s; and the jet in still air slows like a free round jet (centreline ∝ 1/x
    /// past the core), while in the spin's 28 m/s crossflow it is spent within a couple of metres.</summary>
    [Fact]
    public void ExhaustJetVelocityAndDecay()
    {
        double u0 = ExhaustJet.ExitVelocity(540 * 16.387e-6, 2700, 1.0, 1.225, 4 * 0.0254);
        _out.WriteLine($"exit velocity {u0:0.0} m/s");
        Assert.InRange(u0, 65, 75);
        Assert.InRange(ExhaustJet.ExitVelocity(540 * 16.387e-6, 700, 0.0, 1.225, 4 * 0.0254), 2, 10);   // idle: a tenth or so
        foreach (var (name, air) in new[] { ("still air", Vec3.Zero), ("crossflow 28 m/s", new Vec3(0, 0, -28)) })
        {
            var e = ExhaustJet.Start(new Vec3(1, 0, 0), u0, 0.1016, 1.0);
            double x = 0, t = 0; var sb = new System.Text.StringBuilder();
            double next = 0.5;
            while (!e.Spent && t < 5) { x += e.W.X * 0.001; ExhaustJet.Step(ref e, air, 1.0, 0.001, Vec3.Zero); t += 0.001; if (x >= next) { sb.Append($" x {x:0.0} m: {e.W.Length:0} m/s;"); next *= 2; } }
            _out.WriteLine($"{name}: penetration {x:0.00} m in {t:0.00} s —{sb}");
            if (air.Length == 0) Assert.True(x > 5, "a free jet carries metres");
            else Assert.True(x < 2.5, "the crossflow swamps it within a couple of metres");
        }
    }

    /// <summary>Every smoke marker's push stays finite (2026-10-13: a NaN puff turned the airframe under the smoke black):
    /// thousands of releases in the upright flat spin with the 70 m/s jet at 7,800 ft.</summary>
    [Fact]
    public void DeflectionsStayFinite()
    {
        string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        var cfg = AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(repo, "configs", "aircraft", "extra-300-like.json"));
        var flow = new AirframeFlow(cfg, 2.3, 0.5);
        double v = 28, a = 78 * System.Math.PI / 180;
        Vec3 V = new(v * System.Math.Cos(a), 1.5, v * System.Math.Sin(a)), w = new(0.6, -0.4, -3.8), jd = new(-0.866, 0, 0.5);
        int bad = 0; string first = null;
        for (uint i = 1; i <= 3000; i++)
        {
            var d = flow.Deflection(new Vec3(1.08, 0, 0.42), V, w, jd, 70, 0.1016, 0.96, seed: i);
            for (int j = 0; j < d.Count; j++)
                if (!double.IsFinite(d[j].X) || !double.IsFinite(d[j].Y) || !double.IsFinite(d[j].Z)) { bad++; first ??= $"seed {i} step {j}: {d[j]} (prev {d[System.Math.Max(0, j - 1)]})"; break; }
        }
        _out.WriteLine($"non-finite: {bad} / 3000 {first}");
        Assert.True(bad == 0, first);
    }
}
