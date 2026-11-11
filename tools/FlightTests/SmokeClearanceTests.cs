using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// No smoke through ANY part of ANY aeroplane (Ty t914u, 2026-11-06; the Pitts' upright flat spin, smoke coming out of
/// the top of the cowling, was the case). The marker paths already go round (<see cref="AirframeFlowTests"/>); what did
/// not was what is DRAWN around them: every puff and pulse tube is a soft blob stretched along a segment, and a segment
/// laid straight between two points that went round opposite sides of the body, or stretched back along a flight path
/// that runs through the aeroplane, crosses it. <see cref="AirframeFlow.ClearSegment"/> keeps the centre outside and cuts
/// the segment where it enters a body; SmokeTrail runs it on every piece near the airframe, live and in the replay.
/// </summary>
public class SmokeClearanceTests
{
    private readonly ITestOutputHelper _out;
    public SmokeClearanceTests(ITestOutputHelper o) { _out = o; }

    private static string Repo => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
    private static AircraftConfig Load(string file) => AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(Repo, "configs", "aircraft", file));

    public static IEnumerable<object[]> EveryAirframe() => AirframeFlowTests.EveryAirframe();

    private static AirframeFlow FlowFor(AircraftConfig cfg, out double len, out double span)
    {
        span = cfg.Surfaces.Count > 0 ? cfg.Surfaces.Max(sf => sf.Strips.Count == 0 ? 0 : sf.Strips.Max(st => System.Math.Abs(st.Pos[1]))) * 2 : 8;
        if (span < 0.5) span = 8;
        len = cfg.Fuselage?.Crossflow?.LengthM is double fl && fl > 0.5 ? fl : 0.8 * span;
        double radius = cfg.Fuselage?.Crossflow?.BodyRadiusM is double br && br > 0 ? br : 0.5;
        return new AirframeFlow(cfg, 0.4 * len, radius);
    }

    /// <summary>The attitudes the report and the request name: cruise, high α, the upright and inverted flat spins, inverted
    /// cruise, and a knife-edge slip. Body velocity and rates (body axes, z down).</summary>
    private static IEnumerable<(string name, Vec3 V, Vec3 w)> States()
    {
        Vec3 At(double v, double aDeg, double side = 0) { double a = aDeg * System.Math.PI / 180; return new Vec3(v * System.Math.Cos(a), side, v * System.Math.Sin(a)); }
        yield return ("cruise", At(55, 3), Vec3.Zero);
        yield return ("high-alpha", At(30, 30), new Vec3(0, 0.2, 0));
        yield return ("upright-flat", At(25, 93, 1.5), new Vec3(0.2, -0.3, -3.5));   // Pitts approved upright flat: α 92.9°, 201 deg/s
        yield return ("inverted-flat", At(25, -88, -1.5), new Vec3(-0.2, 0.3, 3.3));
        yield return ("inverted-cruise", At(50, -6), Vec3.Zero);
        yield return ("knife-edge", new Vec3(45, 20, 5), new Vec3(0, 0, 0.3));
    }

    /// <summary>Every type, every attitude: pieces centred on every marker of the exhaust's (and an upstream sheet's) path,
    /// stretched every which way 0.1–3 m, plus pieces centred INSIDE the body. After ClearSegment no point of any spine is
    /// inside a body, and a piece that was already clear is returned exactly as it was.</summary>
    [Theory]
    [MemberData(nameof(EveryAirframe))]
    public void NoDrawnPieceOfSmokeIsLeftInsideTheAirframe(string file)
    {
        var cfg = Load(file);
        var flow = FlowFor(cfg, out double len, out double span);
        var exhausts = new List<Vec3>();
        // A measured bank of pipes (the Extra and the Pitts, Ty 2026-11-11) releases smoke from every mouth, so every
        // mouth is a start this sweep has to cover.
        if (cfg.Smoke?.Pipes != null && cfg.Smoke.Pipes.TryPipes(out Vec3[] mouths, out _)) exhausts.AddRange(mouths);
        if (cfg.Smoke != null && cfg.Smoke.TryOutlet(out Vec3 outlet)) exhausts.Add(outlet);
        if (exhausts.Count == 0) exhausts.Add(new Vec3(0.4 * len - 0.16 * len, 0, flow.RadiusAt(0.24 * len) + 0.05));
        var dirs = new List<Vec3> { new(1, 0, 0), new(0, 0, 1), new(0, 1, 0), new Vec3(1, 0, 1) * (1 / System.Math.Sqrt(2)), new Vec3(-0.3, 0.8, -0.5) };
        int pieces = 0, trimmed = 0, untouchedChecked = 0;
        uint rng = 12345;
        double Rnd() { rng = rng * 1664525u + 1013904223u; return (rng >> 8) / 16777216.0; }
        foreach (var (name, V, w) in States())
        {
            var starts = new List<Vec3>(exhausts);
            for (double x = -0.6 * len; x <= 0.4 * len; x += 0.2 * len) for (double y = -0.5 * span; y <= 0.5 * span; y += 0.25 * span) starts.Add(-1.0 * V * (1.0 / V.Length) * 3 + new Vec3(x, y, 0));
            var centres = new List<Vec3>();
            uint seed = 1;
            foreach (var r0 in starts)
            {
                if (flow.Inside(r0, 0.02)) continue;
                var d = flow.Deflection(r0, V, w, Vec3.Zero, 0, 0.1016, 1.0, maxT: 0.6, seed: seed++);
                Vec3 pos = r0;
                for (int i = 0; i < d.Count; i++)
                {
                    if (i % 5 == 0) centres.Add(pos + d[i]);
                    Vec3 Onset(Vec3 q) => -1.0 * (V + Vec3.Cross(w, q));
                    Vec3 fm = pos + Onset(pos) * 0.002; pos += Onset(fm) * 0.004;
                }
            }
            // And pieces whose centre is on the body's own axis (as a stretched puff's centre can be).
            for (double x = -0.5 * len; x <= 0.35 * len; x += 0.1 * len) centres.Add(new Vec3(x, 0, 0));
            foreach (var c0 in centres)
            {
                foreach (var dir0 in dirs)
                {
                    Vec3 dir = dir0 * (1.0 / dir0.Length);
                    double half = 0.05 + 1.5 * Rnd();
                    bool wasClear = !flow.Inside(c0, 0.02) && !SpineHits(flow, c0, dir, half, 0.0);
                    var (c, h) = flow.ClearSegment(c0, dir, half, out bool t);
                    pieces++; if (t) trimmed++;
                    Assert.False(SpineHits(flow, c, dir, h, 0.0), $"{file} {name}: piece at {c0} along {dir} (half {half:0.00}) still crosses the airframe after clearing: {c} half {h:0.000}");
                    Assert.True(h <= half + 1e-9);
                    if (wasClear && !t) { Assert.Equal(c0, c); Assert.Equal(half, h); untouchedChecked++; }
                }
            }
        }
        _out.WriteLine($"{file}: {pieces} pieces, {trimmed} cut or moved, {untouchedChecked} clear ones untouched");
        Assert.True(trimmed > 0, "the set must include pieces that crossed the airframe, or it tests nothing");
    }

    /// <summary>Does the spine c ± dir·half pass inside a body (sampled every 5 mm)?</summary>
    private static bool SpineHits(AirframeFlow flow, Vec3 c, Vec3 dir, double half, double pad)
    {
        int n = System.Math.Max(2, (int)(2 * half / 0.005));
        for (int i = 0; i <= n; i++) if (flow.Inside(c + dir * (-half + 2 * half * i / n), 0.01)) return true;
        return false;
    }

    /// <summary>The report, reduced to its geometry: the Pitts in its upright flat spin (α ≈ 93°: the air comes up from
    /// under the belly), a pulse tube or a stretched puff laid straight from its belly exhaust up past the cowling's top.
    /// Before: it crosses the body, and what is drawn of it above the cowl looks like smoke coming out of the cowling.
    /// After: only the part on the exhaust's side, under the belly, is drawn.</summary>
    [Fact]
    public void ThePittsFlatSpinPieceThroughTheCowlingIsCutAtTheSkin()
    {
        var cfg = Load("pitts-s2b-like.json");
        var flow = FlowFor(cfg, out _, out _);
        Assert.True(cfg.Smoke.TryOutlet(out Vec3 ex));
        Vec3 up = new(0, 0, -1);
        Vec3 c0 = ex + up * 0.25;   // a 1.6 m piece centred just above the exhaust, along the flat spin's relative wind (body up)
        double half = 0.8;
        Assert.True(SpineHits(flow, c0, up, half, 0), "the reproduction must cross the cowling");
        var (c, h) = flow.ClearSegment(c0, up, half, out bool t);
        Assert.True(t);
        Assert.False(SpineHits(flow, c, up, h, 0));
        Assert.True(c.Z > ex.Z - 0.25 && c.Z - h > -0.0, $"kept on the belly side: centre z {c.Z:0.000}, top {c.Z - h:0.000}");
        _out.WriteLine($"exhaust {ex}; piece {c0} ±{half} → {c} ±{h:0.000}");
    }

    /// <summary>The fuselage as drawn: a synthetic biplane — a body of radius 0.5 m whose axis is 0.1 m below the CG, an
    /// upper wing's centre section 0.35 m above the body on cabanes, a fin over the tail and a tailwheel under it on its
    /// own. The body is measured as the body (the upper wing is not merged into it, the tailwheel is not), within 5 cm.</summary>
    [Fact]
    public void TheDrawnFuselageIsMeasuredWithoutTheUpperWingOrTheTailwheel()
    {
        var pts = new List<Vec3>();
        for (double x = -3; x <= 1.8; x += 0.02)
            for (int k = 0; k < 64; k++) { double th = k * 2 * System.Math.PI / 64; pts.Add(new Vec3(x, 0.5 * System.Math.Sin(th), 0.1 + 0.5 * System.Math.Cos(th))); }
        for (double x = 0; x <= 1.2; x += 0.02) for (double y = -3; y <= 3; y += 0.05) { pts.Add(new Vec3(x, y, -0.75)); pts.Add(new Vec3(x, y, -0.85)); }   // upper wing
        for (double x = -3.2; x <= -2.9; x += 0.02) for (double z = 0.9; z <= 1.1; z += 0.02) pts.Add(new Vec3(x, 0, z));   // tailwheel, 0.3 m below
        Assert.True(AirframeFlow.MeasureFuselage(pts, null, out double[] xs, out double[] rs, out double[] zs));
        for (int i = 0; i < xs.Length; i++)
        {
            if (xs[i] < -2.9 || xs[i] > 1.7) continue;
            Assert.InRange(rs[i], 0.45, 0.55);
            Assert.InRange(zs[i], 0.05, 0.15);
        }
        Assert.True(xs[0] > xs[^1], "nose first, as SetFuselage wants");
    }

    /// <summary>The cost the game pays (Ty: mobile performance): a frame's worth of pieces near the Pitts in its flat spin.
    /// SmokeTrail tests only pieces under 3 s old within the airframe's reach — in a flat spin a few hundred a frame — and
    /// this times them on the bench.</summary>
    [Fact]
    public void ClearingAFramesPiecesIsCheap()
    {
        var cfg = Load("pitts-s2b-like.json");
        var flow = FlowFor(cfg, out double len, out double span);
        var rnd = new System.Random(7);
        var pieces = new List<(Vec3 c, Vec3 d, double h)>();
        for (int i = 0; i < 400; i++)
        {
            Vec3 c = new((rnd.NextDouble() - 0.5) * len, (rnd.NextDouble() - 0.5) * span, (rnd.NextDouble() - 0.5) * 3);
            Vec3 d = new(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
            pieces.Add((c, d * (1.0 / System.Math.Max(1e-6, d.Length)), 0.1 + rnd.NextDouble()));
        }
        foreach (var p in pieces) flow.ClearSegment(p.c, p.d, p.h, out _);   // warm
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 50;
        for (int f = 0; f < Frames; f++) foreach (var p in pieces) flow.ClearSegment(p.c, p.d, p.h, out _);
        double msPerFrame = sw.Elapsed.TotalMilliseconds / Frames;
        _out.WriteLine($"400 pieces near the Pitts: {msPerFrame:0.000} ms a frame on this machine ({1000 * msPerFrame / 400:0.0} µs a piece)");
        Assert.True(msPerFrame < 2.0, $"{msPerFrame:0.000} ms a frame for 400 pieces");
    }
}
