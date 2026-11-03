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
/// Ty's glacier and mountain surfaces (2026-11-02, docs/pending/ISLAND-TERRAIN-SURFACES-TY.md): glacier ground is
/// SNOW (smooth, landable, one ski drag) or ICE (never level; crevasse fields that are real holes), boulders ride
/// the ice, small snow patches sit high on the glaciated mountains and ledges on the dry ones — and the surface
/// kinds drive the gear.
///
/// Every position, height, slope and area below is a LITERAL, copied by hand from the generator's report
/// (the header of IslandRelief.Generated.cs and docs/pending/ISLAND-TERRAIN-CLAUDE.md, "Surfaces"). Moving a
/// patch, or changing what the generator makes, has to be a deliberate edit here too.
/// </summary>
// INSTALLS the island into the global WorldTerrain.Active: LandingGear and AirframeContact read the ground through it.
[Collection("WorldTerrainActive")]
public class IslandSurfaceTests : IDisposable
{
    private readonly ITestOutputHelper _o;

    public IslandSurfaceTests(ITestOutputHelper o)
    {
        _o = o;
        WorldTerrain.Active = new WorldTerrain();
        WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    // ---- the designated landing places (generator report, 2026-11-02) ----------------------------------
    // id, sim x (north) m, sim y (east) m, uphill heading deg true, landable length x width m, slope deg,
    // cross slope deg (+ rising to the right of the uphill heading), height at the centre m, shape.
    public static readonly (string id, double x, double y, double hdg, double len, double wid, double slope, double cross, double z, string shape)[] Patches =
    {
        ("SP1", 6550, -3150, 0, 160, 40, 1, 0.1, 875.1, "plane"),
        ("SP2", 6250, -2800, 0, 130, 35, 3, 1.6, 866.2, "plane"),
        ("SP3", 7000, -3250, 0, 150, 35, 4, -3.0, 867.6, "plane"),
        ("SP4", -5600, -5200, 90, 110, 30, 8, -3.0, 1073.7, "plane"),
    };
    public static readonly (string id, double x, double y, double hdg, double len, double wid, double slope, double cross, double z, string shape)[] Ledges =
    {
        ("LD1", -850, 4600, 90, 150, 45, 0, 0.3, 1333.0, "flat"),
        ("LD2", -950, 6550, 90, 130, 40, 1, -2.0, 1177.3, "flat"),
        ("LD3", 3800, -9600, 90, 170, 45, 4, -2.0, 350.6, "sloped"),
        ("LD4", -6700, 4000, 0, 150, 40, 7, -2.0, 395.4, "sloped"),
        ("LD5", -2050, 4750, 0, 140, 45, 0, 0.0, 1152.6, "rounded"),
    };

    /// <summary>A point `along` m up the slope and `across` m to its right from a site's centre (sim x, y).</summary>
    private static (double x, double y) On(double cx, double cy, double hdg, double along, double across)
    {
        double h = hdg * Math.PI / 180;
        return (cx + along * Math.Cos(h) - across * Math.Sin(h), cy + along * Math.Sin(h) + across * Math.Cos(h));
    }

    private static double H(double x, double y) => WorldTerrain.Active!.HeightAt(x, y);

    [Fact]
    public void EverySnowPatchIsSnowAtItsListedHeightAndSlope()
    {
        foreach (var s in Patches)
        {
            double a = s.len / 2 - 5, b = s.wid / 2 - 3;
            foreach (var (al, ac) in new[] { (0.0, 0.0), (a, 0.0), (-a, 0.0), (0.0, b), (0.0, -b), (a, b), (-a, -b) })
            {
                var (x, y) = On(s.x, s.y, s.hdg, al, ac);
                Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(x, y));
            }
            var (ux, uy) = On(s.x, s.y, s.hdg, a, 0); var (dx, dy) = On(s.x, s.y, s.hdg, -a, 0);
            var (rx, ry) = On(s.x, s.y, s.hdg, 0, b); var (lx, ly) = On(s.x, s.y, s.hdg, 0, -b);
            double along = Math.Atan((H(ux, uy) - H(dx, dy)) / (2 * a)) * 180 / Math.PI;
            double cross = Math.Atan((H(rx, ry) - H(lx, ly)) / (2 * b)) * 180 / Math.PI;
            _o.WriteLine($"{s.id}: {H(s.x, s.y):F2} m, along {along:F2} deg, cross {cross:F2} deg");
            // Heights are stored to 5 cm; the patch is an exact plane under the Catmull-Rom surface.
            Assert.InRange(H(s.x, s.y), s.z - .3, s.z + .3);
            Assert.InRange(along, s.slope - .2, s.slope + .2);
            Assert.InRange(cross, s.cross - .2, s.cross + .2);
        }
    }

    [Fact]
    public void EveryLedgeIsRockAtItsListedHeightAndShape()
    {
        foreach (var s in Ledges)
        {
            double a = s.len / 2 - 5, b = s.wid / 2 - 3;
            foreach (var (al, ac) in new[] { (0.0, 0.0), (a, 0.0), (-a, 0.0), (0.0, b), (0.0, -b) })
            {
                var (x, y) = On(s.x, s.y, s.hdg, al, ac);
                Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(x, y));
            }
            var (ux, uy) = On(s.x, s.y, s.hdg, a, 0); var (dx, dy) = On(s.x, s.y, s.hdg, -a, 0);
            double hi = H(ux, uy), lo = H(dx, dy), mid = H(s.x, s.y);
            double along = Math.Atan((hi - lo) / (2 * a)) * 180 / Math.PI;
            _o.WriteLine($"{s.id} ({s.shape}): {mid:F2} m, ends {lo:F2}..{hi:F2}, along {along:F2} deg");
            Assert.InRange(mid, s.z - .3, s.z + .3);
            Assert.InRange(along, s.slope - .2, s.slope + .2);
            // A rounded ledge has its crown above the straight line between its ends: 2.5 m over the full
            // length, (a / (L/2))^2 of that at the sample points.
            double crown = mid - (hi + lo) / 2, want = s.shape == "rounded" ? 2.5 * (a / (s.len / 2)) * (a / (s.len / 2)) : 0;
            Assert.InRange(crown, want - .3, want + .3);
        }
    }

    [Fact]
    public void EveryLandingPlaceIsClearOfStripsApproachesLakesCavesSpawnsAndNavaids()
    {
        var W = IslandTerrain.World;
        foreach (var s in Patches.Concat(Ledges))
        {
            var p = new IslandPoint(s.y / 1000, s.x / 1000);
            double r = Math.Sqrt(s.len * s.len + s.wid * s.wid) / 2000;   // km, the site's half-diagonal
            foreach (var f in IslandTerrain.Fields)
            {
                var d = p - f.Centre;
                double outside = Math.Max(Math.Abs(d.Dot(f.U)) - f.Length / 2000, Math.Abs(d.Dot(f.V)) - f.Width / 2000);
                Assert.True(outside > r, $"{s.id} is {outside * 1000:F0} m from {f.Id}");
            }
            foreach (var e in W["clearance_budgets"].Items)
            {
                if (e.S("kind") == "airway") continue;
                var u = e["u"].Point; var dp = p - e["a"].Point; double alongK = -dp.Dot(u), crossK = Math.Abs(dp.E * u.N - dp.N * u.E);
                double guard = e.N("guard_km", .25), length = e.N("length");
                double outside = Math.Max(Math.Max(-alongK - guard, alongK - length - guard), crossK - (.15 + .05 * Math.Max(0, alongK)) - guard);
                Assert.True(outside > r, $"{s.id} is inside an approach/departure surface");
            }
            foreach (var q in W["water"].Items) Assert.True(IslandTerrain.EllipseRadius(p, q) > 1.2, $"{s.id} is by a lake");
            foreach (var q in W["starts"].Items) Assert.True((p - q["centre"].Point).Length > .5, $"{s.id} is by spawn {q.S("id")}");
            foreach (var id in new[] { "F1", "F2" }) Assert.True((p - IslandTerrain.Feature(id)["centre"].Point).Length > 1.0, $"{s.id} is by cave {id}");
            foreach (var q in W["glaciers"].Items) Assert.True((p - q["ski_zone"]["centre"].Point).Length > 1.5, $"{s.id} is by a ski zone");
        }
    }

    // ---- the glacier -------------------------------------------------------------------------------------

    [Fact]
    public void TheSkiZonesStaySnowFlatAndFreeOfIceDetail()
    {
        // Knik (-6.2, 0.1) at 1,100 ft and Fairweather (-3.9, 4.7) at 1,300 ft: unchanged, flat, SNOW, and no
        // crevasse, undulation or boulder within 1.5 km of either (no published heading, so every direction).
        // Flat to 100 m in every direction: that is what both zones have been since v2 (measured on HEAD 0df90495,
        // identical) — Knik's canyon walls rise from 110 m, short of the spec's 450 x 100 m. Reported, not changed.
        foreach (var (name, e, n, ft) in new[] { ("Knik", -6.2, 0.1, 1100.0), ("Fairweather", -3.9, 4.7, 1300.0) })
        {
            double cx = n * 1000, cy = e * 1000;
            Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(cx, cy));
            Assert.InRange(H(cx, cy), ft * .3048 - .05, ft * .3048 + .05);
            for (double r = 0; r <= 100; r += 25) for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4; Assert.InRange(H(cx + r * Math.Cos(a), cy + r * Math.Sin(a)) - ft * .3048, -.05, .05);
            }
            for (double r = 0; r <= 1500; r += 50) for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8; var p = new IslandPoint((cy + r * Math.Sin(a)) / 1000, (cx + r * Math.Cos(a)) / 1000);
                Assert.Equal(0.0, IslandRelief.FineDetail(p));
                var (_, rough, crev, _) = IslandRelief.Classes(p);
                Assert.True(rough == 0 && crev == 0, $"{name}: ice detail {rough:F2}/{crev:F2} at {r:F0} m");
            }
        }
    }

    [Fact]
    public void NoIceDetailOnAnyStripApproachOrSpawn()
    {
        // Every runway, strip and pad (and 300 m round it), every published and visual approach/departure surface,
        // every spawn (500 m): no crevasse, no undulation, no boulder.
        var W = IslandTerrain.World; int probes = 0;
        void Clear(IslandPoint p, string what)
        {
            probes++;
            var (_, rough, crev, _) = IslandRelief.Classes(p);
            Assert.True(rough == 0 && crev == 0 && IslandRelief.FineDetail(p) == 0, $"ice detail under {what} at ({p.E:F3},{p.N:F3})");
        }
        foreach (var f in IslandTerrain.Fields)
            for (double a = -f.Length / 2 - 300; a <= f.Length / 2 + 300; a += 50) for (double c = -f.Width / 2 - 300; c <= f.Width / 2 + 300; c += 50)
                Clear(f.Centre + f.U * (a / 1000) + f.V * (c / 1000), f.Id);
        foreach (var e in W["clearance_budgets"].Items)
        {
            if (e.S("kind") == "airway") continue;
            var u = e["u"].Point; var v = new IslandPoint(u.N, -u.E); double length = e.N("length");
            for (double t = 0; t <= length; t += .05) for (double c = -1; c <= 1; c += .25)
                Clear(e["a"].Point - u * t + v * (c * (.15 + .05 * t)), "an approach");
        }
        foreach (var q in W["starts"].Items) for (double r = 0; r <= .5; r += .05) for (int k = 0; k < 8; k++)
            Clear(q["centre"].Point + IslandPoint.Heading(k * 45) * r, q.S("id"));
        _o.WriteLine($"{probes} probes clear");
    }

    [Fact]
    public void TheGlacierIsSnowOrIceAndBothAreThere()
    {
        // Over every glacier node: SNOW or ICE (or rock where a boulder sits on it), and the areas the
        // generator reported: 28.41 km2 of snow and 10.02 km2 of bare ice on 38.43 km2 (25 m nodes, ice > 0.5).
        double snow = 0, ice = 0, cell = .025 * .025;
        for (double e = -12; e < 9; e += .025) for (double n = -10.4; n < 8.4; n += .025)
        {
            var p = new IslandPoint(e, n); if (IslandRelief.Channels(p).ice <= .5) continue;
            var s = IslandTerrain.SurfaceAt(p.X, p.Y);
            if (s == WorldTerrain.Surface.Snow) snow += cell;
            else if (s == WorldTerrain.Surface.GlacierIce) ice += cell;
            else Assert.True(s == WorldTerrain.Surface.Gravel || IslandTerrain.Fields.Any(f => f.Contains(p)) || IslandRelief.Classes(p).ledge > .5,
                             $"glacier ground at ({e:F3},{n:F3}) is {s}");
        }
        _o.WriteLine($"snow {snow:F2} km2, bare ice {ice:F2} km2");
        Assert.InRange(snow, 27.4, 29.4);
        Assert.InRange(ice, 9.0, 11.0);
    }

    // Literal glacier places from the generator's survey (sim x north, y east): a near-level run of bare ice on
    // the southern ice's tongue, a run of glacier snow, and a full-strength crevasse field in its icefall.
    private const double IceX = -4100, IceY = -6100, SnowX = 4000, SnowY = 750, CrevX = -3000, CrevY = -4150;

    /// <summary>Peak-to-trough of the height about its best straight line, over a 200 m line through (x, y).</summary>
    private static double Relief(double x, double y, double hdgDeg, double half = 100, bool quadratic = false)
    {
        double h = hdgDeg * Math.PI / 180; var pts = new List<(double t, double z)>();
        for (double t = -half; t <= half; t += 2.5) pts.Add((t, H(x + t * Math.Cos(h), y + t * Math.Sin(h))));
        if (quadratic)
        {
            // Least squares z = a + b t + c t^2 (the samples are symmetric about t = 0, so b decouples).
            double n = pts.Count, s2 = pts.Sum(p => p.t * p.t), s4 = pts.Sum(p => Math.Pow(p.t, 4));
            double sz = pts.Sum(p => p.z), stz = pts.Sum(p => p.t * p.z), st2z = pts.Sum(p => p.t * p.t * p.z);
            double c = (n * st2z - s2 * sz) / (n * s4 - s2 * s2), a = (sz - c * s2) / n, b = stz / s2;
            var q = pts.Select(p => p.z - (a + b * p.t + c * p.t * p.t)).ToList();
            return q.Max() - q.Min();
        }
        double mt = pts.Average(p => p.t), mz = pts.Average(p => p.z);
        double k = pts.Sum(p => (p.t - mt) * (p.z - mz)) / pts.Sum(p => (p.t - mt) * (p.t - mt));
        var r = pts.Select(p => p.z - (mz + k * (p.t - mt))).ToList();
        return r.Max() - r.Min();
    }

    [Fact]
    public void SnowIsSmoothAndBareIceIsNeverLevel()
    {
        Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(SnowX, SnowY));
        Assert.Equal(WorldTerrain.Surface.GlacierIce, WorldTerrain.SurfaceAt(IceX, IceY));
        // Snow: the glacier's own gentle curvature only — under 0.5 m off a straight line over 200 m in any direction.
        for (int k = 0; k < 4; k++) Assert.InRange(Relief(SnowX, SnowY, k * 45), 0, .5);
        // Ice: hummocks of up to 1.5 m over ~110 m. Along any 200 m line on bare ice the ground is off a straight
        // line by at least 0.1 m — twice the +/-5 cm the authored landing zones are flat to, so even the smooth
        // patches are never level. (No ceiling: a 200 m line also crosses real landforms — a tidewater front, a
        // valley wall, a moraine — so the spread off the glacier's own curve is only reported.)
        int lines = 0; var rng = new Random(11); var clean = new List<double>();
        for (double x = -10000; x < 8000; x += 100) for (double y = -12000; y < 9000; y += 100)
        {
            var p = new IslandPoint(y / 1000, x / 1000);
            if (IslandRelief.Classes(p).rough < .95 || IslandRelief.Classes(p).crevasse > 0 || IslandRelief.FineDetail(p) != 0) continue;
            // The whole 200 m line on bare ice: no crevasse or boulder anywhere along it. Where a moraine stripe crosses
            // it the ice-cored ridge (up to 17.5 m, Surfaces.MaxRidgeM) adds to the hummocks.
            double hd = rng.Next(0, 180), hr = hd * Math.PI / 180; bool plain = true, debris = false;
            for (double t = -100; t <= 100 && plain; t += 2.5)
            {
                var q = new IslandPoint((y + t * Math.Sin(hr)) / 1000, (x + t * Math.Cos(hr)) / 1000);
                plain = IslandRelief.Classes(q).rough >= .95 && IslandRelief.Classes(q).crevasse == 0 && IslandRelief.FineDetail(q) == 0;
                debris |= IslandRelief.Channels(q).debris >= .1;
            }
            if (!plain) continue;
            double r = Relief(x, y, hd, 100); lines++;
            double curved = Relief(x, y, hd, 100, quadratic: true);
            Assert.True(r >= .1, $"bare ice at x {x} y {y}: only {r:F2} m off a straight line over 200 m");
            clean.Add(curved);
        }
        clean.Sort();
        _o.WriteLine($"bare-ice lines off the glacier's curve: median {clean[clean.Count / 2]:F2} m, 90 % {clean[(int)(clean.Count * .9)]:F2} m, max {clean[^1]:F2} m");
        _o.WriteLine($"{lines} bare-ice lines, snow relief {Relief(SnowX, SnowY, 0):F2} m, ice relief here {Relief(IceX, IceY, 180):F2} m");
        Assert.True(lines > 50, $"only {lines} bare-ice lines found");
    }

    [Fact]
    public void ACrevasseIsADeepCutInTheGroundTheAirframeHits()
    {
        // Full-strength field: along the fall line, cuts at least 15 m below the ice either side (Nye depth 22 m),
        // walls steeper than 60 deg, and the surface there is ice.
        Assert.Equal(WorldTerrain.Surface.GlacierIce, WorldTerrain.SurfaceAt(CrevX, CrevY));
        double deepest = 0, steepest = 0; double h0 = -11 * Math.PI / 180;
        var pts = new List<(double t, double z)>();
        for (double t = -80; t <= 80; t += .5) pts.Add((t, H(CrevX + t * Math.Cos(h0), CrevY + t * Math.Sin(h0))));
        for (int i = 20; i < pts.Count - 20; i++)
        {
            double rim = Math.Max(pts.Take(i).Skip(i - 20).Max(p => p.z), pts.Skip(i).Take(20).Max(p => p.z));
            deepest = Math.Max(deepest, rim - pts[i].z);
            steepest = Math.Max(steepest, Math.Atan(Math.Abs(pts[i + 1].z - pts[i].z) / .5) * 180 / Math.PI);
        }
        _o.WriteLine($"crevasse field at x {CrevX} y {CrevY}: deepest cut {deepest:F1} m, steepest wall {steepest:F0} deg");
        Assert.True(deepest >= 15, $"deepest cut only {deepest:F1} m");
        Assert.True(steepest >= 60, $"steepest wall only {steepest:F0} deg");
        // What the airframe meets is that same ground: contact and the analytic normal read it.
        int walls = 0; for (double t = -80; t <= 80; t += 1) if (WorldTerrain.IsWallAt(CrevX + t * Math.Cos(h0), CrevY + t * Math.Sin(h0), Math.Tan(60 * Math.PI / 180))) walls++;
        Assert.True(walls >= 4, $"only {walls} wall samples along 160 m of crevasse field");
    }

    [Fact]
    public void BouldersAreRockStandingOnTheIce()
    {
        // The four biggest (generator survey): sim x, y, radius, height above the ice.
        var big = new (double x, double y, double r, double h)[]
        {
            (-5559.9, -4661.0, 11.9, 9.1), (2725.6, -3157.3, 10.8, 8.3), (7354.0, -166.8, 11.3, 7.9), (7478.4, 39.2, 11.0, 7.6),
        };
        foreach (var b in big)
        {
            Assert.Equal(WorldTerrain.Surface.Gravel, WorldTerrain.SurfaceAt(b.x, b.y));
            // Above the mean of the ice on four sides (a sloping glacier cancels out of the mean).
            double ring = 0; for (int k = 0; k < 4; k++) ring += H(b.x + (b.r + 4) * Math.Cos(k * Math.PI / 2), b.y + (b.r + 4) * Math.Sin(k * Math.PI / 2)) / 4;
            double stands = H(b.x, b.y) - ring;
            _o.WriteLine($"boulder x {b.x} y {b.y}: stands {stands:F1} m above the ice round it");
            Assert.InRange(stands, .6 * b.h, b.h + 3);
        }
        Assert.Equal(44, IslandRelief.Boulders().Count);
    }

    // ---- what the gear feels -----------------------------------------------------------------------------

    [Fact]
    public void GlacierIceHasHardSlipperyCoefficients()
    {
        const WorldTerrain.Surface ice = WorldTerrain.Surface.GlacierIce;
        // Each number is a measured one, copied by hand from its source (see the notes at each definition).
        // Rolling: ice is rigid under a tyre, so it is the hard-surface value — paved's 0.025, and no sinkage term.
        Assert.Equal(0.025, WorldTerrain.RollingCoefficient(ice), 9);
        Assert.Equal(0.025, LandingGear.SnowSinkageRolling(Cub(skis: false, bushwheels: true), ice, 4000), 9);
        // Grip: melting ice is the FAA's wet ice, Nil; the AC 25-32 water-film coefficient 0.053 over NASA TP-2917's
        // 0.5 dry = 0.106 of dry pavement. Below Poor (cold) ice, 0.083 / 0.5 = 0.166, and below packed snow.
        Assert.Equal(0.106, LandingGear.SurfaceGrip(ice), 9);
        Assert.True(LandingGear.SurfaceGrip(ice) < 0.083 / 0.5);
        Assert.True(LandingGear.SurfaceGrip(ice) < LandingGear.SurfaceGrip(WorldTerrain.Surface.PackedGlacierSnow));
        // A keel or skid sliding on it: metal on ice, 0.03 (steel-ice; brass-ice is 0.02 at 0 C).
        Assert.Equal(0.03, WorldTerrain.KeelSlidingCoefficient(ice), 9);
        // On hard ice the wheel of a wheel-ski carries the load; on snow the ski does, at the same drag everywhere.
        var cfg = new AircraftConfig { Skis = true };
        Assert.False(LandingGear.OnSki(cfg, ice));
        Assert.True(LandingGear.OnSki(cfg, WorldTerrain.Surface.Snow));
        Assert.Equal(.055, LandingGear.SkiRolling(WorldTerrain.Surface.Snow), 6);
        // Every patch, the glacier snow and both ski zones report the SAME surface, so the ski drag is the same on
        // all of them: Ty's "consistent ski drag".
        foreach (var s in Patches) Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(s.x, s.y));
        Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(SnowX, SnowY));
        Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(100, -6200));    // Knik ski zone
        Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(4700, -3900));   // Fairweather ski zone
    }

    /// <summary>Munro's (1989) roughness length of a profile: h = 2 sd about the mean, f = runs above the mean,
    /// z0 = h^2 f / (4 X).</summary>
    private static (double z0, double spacing) Munro(Func<double, double> z, double length, double step)
    {
        var v = new List<double>(); for (double t = 0; t < length; t += step) v.Add(z(t));
        double mean = v.Average(), sd = Math.Sqrt(v.Sum(e => (e - mean) * (e - mean)) / v.Count), h = 2 * sd;
        int f = 0; for (int i = 1; i < v.Count; i++) if (v[i] > mean && v[i - 1] <= mean) f++;
        return (h * h * f / (4 * length), length / Math.Max(1, f));
    }

    [Fact]
    public void BareIceIsAsRoughAsMeasuredGlacierIceAndSnowIsSmooth()
    {
        // Brock and others (2006), Haut Glacier d'Arolla: melting glacier ice z0 mean 2.24 mm (ln z0 sd 0.89),
        // hummocks 1.0 m apart. The wheel's relief on the ice, profiled the way they profiled it (40 m traverses, every
        // 2 cm) on every heading, must come out as that ice — not tuned to it: it is derived from those two numbers.
        double sum = 0, spaced = 0; int n = 0;
        for (int deg = 0; deg < 180; deg += 10)
        {
            double a = deg * Math.PI / 180;
            for (int k = 0; k < 3; k++)
            {
                double x0 = IceX + 37 * k, y0 = IceY - 23 * k;
                Assert.Equal(WorldTerrain.Surface.GlacierIce, WorldTerrain.SurfaceAt(x0, y0));
                var (z0, spacing) = Munro(t => WorldTerrain.MicroBumpAt(x0 + t * Math.Cos(a), y0 + t * Math.Sin(a)), 40, .02);
                // Each profile within Brock's 2 sd of ln z0 (0.81 +/- 1.78 in ln mm): 0.38-13.3 mm.
                Assert.InRange(z0, .00038, .0133);
                sum += z0; spaced += spacing; n++;
            }
        }
        _o.WriteLine($"bare ice: mean Munro z0 {sum / n * 1000:F2} mm, hummocks {spaced / n:F2} m apart, over {n} profiles (Brock 2006: 2.24 mm, 1.0 m)");
        Assert.InRange(sum / n, .00224 * .85, .00224 * 1.15);
        Assert.InRange(spaced / n, .85, 1.15);
        // Zero mean: the height field is the mean ice surface.
        double m = 0; for (int i = 0; i < 4000; i++) m += WorldTerrain.GlacierIceMicroRelief(i * .137, i * .291) / 4000;
        Assert.InRange(m, -.005, .005);
        // And SNOW (Ty: smooth and landable everywhere) carries none.
        for (double t = 0; t < 20; t += .1) Assert.Equal(0, WorldTerrain.MicroBumpAt(SnowX + t, SnowY));
    }

    [Fact]
    public void ACrevasseWallPushesBackAlongItsOwnNormal()
    {
        // The mechanism, at one point: an airframe hard point 10 cm inside the steepest crevasse wall on the profile
        // (measured along the wall's normal) is pushed out along that normal by 10 cm of spring — not straight up
        // by the vertical depth, which on a wall is several times more.
        // The steepest stretch of wall on the profile that is PLANE over the probe's 10 cm (its normal steady within
        // 1 deg for 0.3 m round the point), so the tangent plane is the wall — not a crease where wall meets lip.
        double h0 = -11 * Math.PI / 180; double bestT = 0, bestSlope = 0;
        double Angle(Vec3 a, Vec3 b) => Math.Acos(Math.Clamp(Vec3.Dot(a, b), -1, 1)) * 180 / Math.PI;
        for (double t = -80; t <= 80; t += .25)
        {
            double qx = CrevX + t * Math.Cos(h0), qy = CrevY + t * Math.Sin(h0);
            var nn = WorldTerrain.GroundNormalAt(qx, qy);
            double slope = Math.Acos(-nn.Z) * 180 / Math.PI;
            bool plane = true;
            for (int k = 0; k < 8 && plane; k++)
                plane = Angle(nn, WorldTerrain.GroundNormalAt(qx + .3 * Math.Cos(k * Math.PI / 4), qy + .3 * Math.Sin(k * Math.PI / 4))) < 1;
            if (plane && slope > bestSlope) { bestSlope = slope; bestT = t; }
        }
        double px = CrevX + bestT * Math.Cos(h0), py = CrevY + bestT * Math.Sin(h0);
        Vec3 n = WorldTerrain.GroundNormalAt(px, py);                       // outward, NED
        Vec3 surf = new(px, py, -WorldTerrain.WheelGroundHeightAt(px, py));
        Vec3 inside = surf - n * 0.10;
        var pts = new List<ContactPoint> { new() { Body = Vec3.Zero, Component = AirframeComponent.Fuselage, Name = "probe" } };
        var st = new RigidBodyState(inside, Quat.Identity, Vec3.Zero, Vec3.Zero);
        var (f, _) = AirframeContact.ComputeEveryPoint(pts, Vec3.Zero, st, null);
        double angle = Math.Acos(Math.Clamp(Vec3.Dot(f.Normalized(), n), -1, 1)) * 180 / Math.PI;
        _o.WriteLine($"wall {bestSlope:F0} deg at t {bestT:F2}: force {f.Length / 1000:F1} kN, {angle:F1} deg off the wall normal");
        Assert.True(bestSlope >= 60, $"steepest wall only {bestSlope:F0} deg");
        Assert.InRange(angle, 0, 5);
        // ~10 cm of the 150 kN/m spring (the wall curves, so the plane through the surface point is a tangent).
        Assert.InRange(f.Length, .8 * AirframeContact.SpringNPerM * .10, 1.2 * AirframeContact.SpringNPerM * .10);
    }

    private static AircraftConfig Cub(bool skis, bool bushwheels)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", WheelSkiTests.Cub + ".json"));
        c.Skis = skis; c.Bushwheels = bushwheels; return c;
    }

    /// <summary>Roll a Cub along the ground from (x, y) at `speed` on heading `hdg` for `seconds`, power idle,
    /// and report the RMS of its vertical acceleration (g) and the time it spent with no main wheel down.</summary>
    private (double rmsG, double airborneS) Roll(AircraftConfig c, double x, double y, double hdg, double speed, double seconds)
    {
        double h = hdg * Math.PI / 180;
        RigidBodyState rest = LandingGear.RestingState(c, x, y, H(x, y), h);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(speed, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac); double dt = SimLoop.DefaultFixedDtSec, sum = 0, air = 0; int n = 0;
        double vz = ac.State.Attitude.Rotate(ac.State.Velocity).Z;
        for (double t = 0; t < seconds; t += dt)
        {
            loop.RunFor(dt, new ControlInputs(0, 0, 0, 1));
            double vzNow = ac.State.Attitude.Rotate(ac.State.Velocity).Z, a = (vzNow - vz) / dt / 9.81; vz = vzNow;
            if (t > 2) { sum += a * a; n++; if (!LandingGear.AnyMainWheelOnGround(c, ac.State)) air += dt; }
        }
        return (Math.Sqrt(sum / Math.Max(1, n)), air);
    }

    [Fact]
    public void ASuperCubOnTundraTiresBouncesOnTheIceAndRollsSmoothlyOnTheSnow()
    {
        // Ty: "a Super Cub on tundra tires could land but would bounce around". Rolling out at 23 kt (12 m/s, below
        // flying speed, so what moves it is the ground) along the bare ice and along the glacier snow, idle, hands off.
        AircraftConfig c = Cub(skis: false, bushwheels: true);
        // (The first 2 s are the gear settling from its resting estimate, on both surfaces alike, and are not counted.)
        var ice = Roll(c, IceX, IceY, 0, 12, 7);
        var snow = Roll(c, SnowX, SnowY, 0, 12, 7);
        _o.WriteLine($"tundra-tyre Cub at 23 kt: ice {ice.rmsG:F3} g RMS, {ice.airborneS:F2} s light; snow {snow.rmsG:F3} g RMS, {snow.airborneS:F2} s light");
        Assert.True(ice.rmsG > 3 * snow.rmsG, $"ice {ice.rmsG:F3} g against snow {snow.rmsG:F3} g");
        Assert.True(ice.rmsG > .05, $"the ice only shook it {ice.rmsG:F3} g");
    }

    [Fact]
    public void ACubOnSkisLandsUphillOnTheSteepPatchAndStopsOnIt()
    {
        // SP4: 110 x 30 m rising 8 deg to the east. Touch down 10 m inside the low end at 31 kt going uphill — how a
        // pilot uses a steep patch — and stop on it. The slope does the stopping: along it the ground's normal
        // force has a component g sin 8 deg = 1.37 m/s2 against the climb, and the ski adds mu g cos 8 deg
        // (0.055 running): v^2 / (2 g (sin + mu cos)) = 67 m from 16 m/s, where the flat would take 240.
        var s = Patches[3]; Assert.Equal("SP4", s.id);
        var (px, py) = On(s.x, s.y, s.hdg, -s.len / 2 + 10, 0);
        AircraftConfig c = Cub(skis: true, bushwheels: false);
        double h = s.hdg * Math.PI / 180, th = s.slope * Math.PI / 180;
        RigidBodyState rest = LandingGear.RestingState(c, px, py, H(px, py), h);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(16, 0, 0), Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac); double dt = SimLoop.DefaultFixedDtSec, slowest = 99, uphillMost = double.NegativeInfinity;
        double along = 0, across = 0;
        for (int i = 0; i < 12000; i++)
        {
            loop.RunFor(dt, new ControlInputs(0, 0, 0, 1));
            slowest = Math.Min(slowest, ac.State.Velocity.Length);
            Assert.False(double.IsNaN(ac.State.Position.X));
            double dx = ac.State.Position.X - s.x, dy = ac.State.Position.Y - s.y;
            along = dx * Math.Cos(h) + dy * Math.Sin(h); across = -dx * Math.Sin(h) + dy * Math.Cos(h);
            uphillMost = Math.Max(uphillMost, along);
            if (i > 600 && ac.State.Velocity.Length < .05) break;
        }
        double ran = uphillMost - (-s.len / 2 + 10), want = 16.0 * 16 / (2 * 9.81 * (Math.Sin(th) + .055 * Math.Cos(th)));
        _o.WriteLine($"SP4: ran {ran:F0} m uphill (sin + mu cos: {want:F0} m), at rest {along:+0;-0} m along, {across:+0.0;-0.0} m across, "
                   + $"speed {ac.State.Velocity.Length:F2} m/s, surface {WorldTerrain.SurfaceAt(ac.State.Position.X, ac.State.Position.Y)}");
        Assert.InRange(ran, .8 * want, 1.2 * want);
        // And it stays there: the ski's static hold (4 x 0.055 = 0.22) is more than tan 8 deg (0.14).
        Assert.True(Math.Abs(along) <= s.len / 2 && Math.Abs(across) <= s.wid / 2, $"ran off the patch to {along:F0}/{across:F0} m");
        Assert.True(ac.State.Velocity.Length < .1, $"still moving at {ac.State.Velocity.Length:F2} m/s");
    }

    [Fact]
    public void ACubThatLandsInACrevasseFieldIsWrecked()
    {
        // Ty: "collisions there are real". A tundra-tyre Cub arriving over the full-strength field at 35 kt, 2 m above
        // the ice and sinking 1 m/s, flown straight along the fall line UP the icefall, hands off, idle. It does not
        // roll out: it breaks against the walls, and it is not thrown back into the air by them.
        AircraftConfig c = Cub(skis: false, bushwheels: true);
        double h0 = -11 * Math.PI / 180 + Math.PI;                 // facing the walls: up the fall line
        double x0 = CrevX - 40 * Math.Cos(h0), y0 = CrevY - 40 * Math.Sin(h0);
        RigidBodyState rest = LandingGear.RestingState(c, x0, y0, H(x0, y0), h0);
        var start = new RigidBodyState(rest.Position + new Vec3(0, 0, -2), rest.Attitude, new Vec3(18, 0, 1), Vec3.Zero);
        var ac = new Aircraft(c, start, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac); double dt = SimLoop.DefaultFixedDtSec, worstHit = 0, highest = double.NegativeInfinity, z0 = -start.Position.Z;
        ac.HardImpact += (v, _) => worstHit = Math.Max(worstHit, v);
        for (double t = 0; t < 8; t += dt)
        {
            loop.RunFor(dt, new ControlInputs(0, 0, 0, 1));
            Vec3 p = ac.State.Position;
            Assert.False(double.IsNaN(p.X));
            highest = Math.Max(highest, -p.Z - z0);
        }
        Vec3 e = ac.State.Position; double went = Math.Sqrt((e.X - x0) * (e.X - x0) + (e.Y - y0) * (e.Y - y0));
        _o.WriteLine($"crevasse field: worst impact {worstHit:F1} m/s, lost [{string.Join(", ", ac.LostComponents)}], "
                   + $"went {went:F0} m, at most {highest:F1} m above where it started, speed now {ac.State.Velocity.Length:F1} m/s");
        Assert.NotEmpty(ac.LostComponents);
        Assert.True(worstHit > AirframeContact.NoseBreakMs, $"worst impact only {worstHit:F1} m/s");
        // Up the fall line it climbs with the ice (a 25-50 deg icefall rises 12-24 m in the first 25 m) but the walls
        // must stop it, not throw it: well under 30 m above where it started, and stopped inside the field.
        Assert.True(highest < 30, $"thrown {highest:F1} m above where it started");
        Assert.True(went < 80 && ac.State.Velocity.Length < 2, $"went {went:F0} m and still moving at {ac.State.Velocity.Length:F1} m/s");
    }
}
