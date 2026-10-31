using System;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Mountain roughness and seaplane-lake smoothing (owner 2026-10-27: "I want some mountains to be rough and some to be
/// more smooth; make the mountains with lakes to land seaplanes on smoother"). A rough mountain stirs more turbulence and
/// spins a harder rotor and sink than a smooth one in the same wind over the same slope; over and around a seaplane lake
/// the terrain's turbulence, rotor and sink are cut by the configured factor; every blend is continuous; and the start
/// airport is still exactly calm.
/// </summary>
[Collection("WorldTerrainActive")]
public class MountainRoughnessTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or FAIL. Atmosphere.SteadyWind / ActiveTurbulence /
    /// Field are global: a class that sets the weather and resets it only at the end of a test method leaves
    /// that weather blowing through every test that runs after it when the test throws. That is what put the
    /// whole fleet into a stall in SpawnSpeedTests while all 34 passed in isolation.</summary>
    public void Dispose()
    {
        Atmosphere.SteadyWind = FlyingGame.Core.MathTypes.Vec3.Zero;
        Atmosphere.ActiveTurbulence = null;
        Atmosphere.Field = null;
    }

    private readonly ITestOutputHelper _out;
    public MountainRoughnessTests(ITestOutputHelper output) => _out = output;

    private static AtmosphereFieldConfig Cfg() =>
        AtmosphereFieldConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "atmosphere", "regional-atmosphere.json"));

    private static T WithWorld<T>(Func<WorldTerrain, T> body)
    {
        var t = new WorldTerrain(); WorldTerrain.Active = t;
        try { return body(t); }
        finally { WorldTerrain.Active = null; Atmosphere.Field = null; Atmosphere.Thermals.Clear(); Atmosphere.SimTimeSec = 0; }
    }

    // A 600 m escarpment running along x, plain at 300 m to the east, plateau at 900 m to the west.
    private static double Bluff(double x, double y) => 300 + 600 * 0.5 * (1 + Math.Tanh((-y - 1100) / 150.0));

    [Fact]
    public void RoughMountainStirsMoreThanASmoothOneInTheSameWind()
    {
        const double rough = 1.6, smooth = 0.6;
        var c = new AtmosphereFieldConfig
        {
            Synoptic = new SynopticConfig { WindFromDeg = 270, WindSpeedKt = 25 },   // off the plateau: the plain is the lee
            CalmZone = new CalmZoneConfig { Enabled = false },
            Regions = { new RegionConfig { Id = "test", Shape = "global", LapseRateCPerKm = 6.5, RoughnessM = 0.03 } },
            Convection = new ConvectionConfig { BackgroundUpdraftMs = 0 },
            // The same bluff: its northern half (x < 0) rough, its southern half smooth.
            Roughness =
            {
                new RoughnessZoneConfig { Id = "rough", Shape = "box", CenterX = -6000, HalfX = 5500, HalfY = 20000, BlendM = 500, Factor = rough, LengthScale = 0.75 },
                new RoughnessZoneConfig { Id = "smooth", Shape = "box", CenterX = 6000, HalfX = 5500, HalfY = 20000, BlendM = 500, Factor = smooth, LengthScale = 1.2 },
            },
        };
        var f = new AtmosphereField(c) { Heating = (_, _) => 0.0, Terrain = Bluff };

        double maxSig(double x) => Scan(f, x, s => s.OrographicSigmaMs, Math.Max);
        double maxRotor(double x) => Scan(f, x, s => s.RotorMs, Math.Max);
        double minLee(double x) => Scan(f, x, s => s.LeeMs, Math.Min);
        double maxLift(double x) => Scan(f, x, s => s.SlopeLiftMs, Math.Max);
        double sR = maxSig(-3000), sS = maxSig(3000), rR = maxRotor(-3000), rS = maxRotor(3000), lR = minLee(-3000), lS = minLee(3000);
        _out.WriteLine($"rough (×{rough}): σ_oro {sR:F2} m/s, rotor {rR:F2} m/s, lee sink {lR:F2} m/s");
        _out.WriteLine($"smooth (×{smooth}): σ_oro {sS:F2} m/s, rotor {rS:F2} m/s, lee sink {lS:F2} m/s");
        Assert.True(sR > 1.0 && rR > 2.0 && lR < -2.0, "the lee of the bluff has real turbulence, rotor and sink");
        Assert.True(sR > 2 * sS && rR > 2 * rS && lR < 2 * lS, "the rough mountain is measurably rougher");
        Assert.Equal(rough / smooth, sR / sS, 6);
        Assert.Equal(rough / smooth, rR / rS, 6);
        Assert.Equal(rough / smooth, lR / lS, 6);
        // The laminar part is the same: a smooth ridge soars as well as a rough one.
        Assert.Equal(maxLift(-3000), maxLift(3000), 9);

        // At one point in the rotor: the total turbulence is greater and the eddies shorter on the rough side.
        AtmosphereSample a = f.Sample(new Vec3(-3000, -500, -(Bluff(0, -500) + 60)), 0), b = f.Sample(new Vec3(3000, -500, -(Bluff(0, -500) + 60)), 0);
        _out.WriteLine($"at 60 m AGL 600 m into the lee: rough σ {a.SigmaHorizMs:F2}/{a.SigmaVertMs:F2} L {a.LengthHorizM:F0}; smooth σ {b.SigmaHorizMs:F2}/{b.SigmaVertMs:F2} L {b.LengthHorizM:F0}");
        Assert.True(a.SigmaHorizMs > 1.3 * b.SigmaHorizMs && a.SigmaVertMs > 1.3 * b.SigmaVertMs);
        Assert.True(a.LengthHorizM < b.LengthHorizM);
    }

    /// <summary>The extreme of a quantity over the bluff's lee at one x (y across the lee, 10–600 m AGL).</summary>
    private static double Scan(AtmosphereField f, double x, Func<AtmosphereSample, double> q, Func<double, double, double> pick)
    {
        double best = 0;
        for (double y = -1000; y <= 800; y += 20)
            for (double agl = 10; agl <= 600; agl += 10)
                best = pick(best, q(f.Sample(new Vec3(x, y, -(Bluff(0, y) + agl)), 0)));
        return best;
    }

    [Fact]
    public void TheWorldsMountainsDifferAndTheZonesAreTheTerrainsOwn()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            AtmosphereField.RoughnessZone Z(string id) => f.RoughnessZones.Single(z => z.Id == id);
            // Resolved from the terrain code, not typed in.
            Assert.Equal(0.5 * (AlaskaRange.X0 + AlaskaRange.X1), Z("alaska-range").CenterX);
            Assert.Equal(0.5 * (AlpineRange.Y0 + AlpineRange.Y1), Z("alpine-range").CenterY);
            Assert.Equal(AlaskaRange.Peaks.Single(p => p.Name == "The High One").X, Z("the-high-one").CenterX);
            Assert.Equal(AlpineRange.Peaks.Single(p => p.Name == "Dragontail").RadiusM, Z("dragontail").RadiusM);

            // Each mountain at its own heart, on its own ground.
            var at = new (string name, double x, double y)[]
            {
                ("The High One", 4200, -5900), ("Huntington", 5500, -4800), ("Foraker", 5400, -7200),
                ("Frank Church canyon", FrankChurch.X0 + 2500, -12700), ("Mesa wall", 0, WorldTerrain.EdgeMeanY(2) + 200),
                ("Bench wall", 0, WorldTerrain.EdgeMeanY(1) + 200), ("Reno mesas", -5250, -6000),
                ("Dragontail", 7400, -9400), ("Alpine range", 3900, -10800), ("Valley bluff", 0, WorldTerrain.EdgeMeanY(0) + 200),
                ("Colchuck", 5000, -12700), ("Sea cliffs", -4000, Coast.MeanShoreY), ("Rolling hills", -1700, 2050),
            };
            foreach (var (name, x, y) in at) _out.WriteLine($"{name,-20} roughness ×{f.MountainRoughnessAt(x, y).factor:F2}  L ×{f.MountainRoughnessAt(x, y).lengthScale:F2}");
            double R(string n) { var p = at.Single(a => a.name == n); return f.MountainRoughnessAt(p.x, p.y).factor; }
            Assert.True(R("The High One") > R("Mesa wall") && R("Mesa wall") > R("Bench wall") && R("Bench wall") > R("Valley bluff"));
            Assert.True(R("Frank Church canyon") > 1.3 && R("Reno mesas") > 1.1);
            Assert.True(R("Dragontail") > R("Alpine range") && R("Alpine range") > R("Colchuck"));
            Assert.True(R("Alpine range") < 1.0 && R("Sea cliffs") < 0.7 && R("Rolling hills") < 0.6);
            Assert.True(R("The High One") / R("Rolling hills") > 3, "the variety is noticeable");
            return 0;
        });
    }

    [Fact]
    public void SeaplaneLakesComeFromTheTerrainsWaterData()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            string[] names = f.SeaplaneLakes.Select(l => l.Name).ToArray();
            _out.WriteLine(string.Join(", ", names));
            foreach (AlpineRange.Lake l in AlpineRange.Lakes)
                Assert.Equal(l.Landable, names.Contains("alpine:" + l.Name));        // the ones you can only fly into stay rough
            foreach (AlaskaRange.Glacier g in AlaskaRange.Glaciers) Assert.Contains("alaska:" + g.Name, names);
            foreach (WorldTerrain.Airport a in WorldTerrain.Airports) Assert.Contains("plateau:" + a.Name, names);
            Assert.Contains("canyon", names);
            // Every smoothed lake's centre is water in the world, and fully smoothed.
            foreach (AtmosphereField.SeaplaneLake l in f.SeaplaneLakes)
            {
                Assert.True(t.WaterSurfaceAt(l.CentreX, l.CentreY).HasValue, $"{l.Name}: its centre is water");
                Assert.Equal(1.0, f.LakeSmoothingAt(l.CentreX, l.CentreY));
            }
            return 0;
        });
    }

    /// <summary>Points over a lake, on its shore margin and down its approach corridors (all in its full-smoothing core).</summary>
    private static IEnumerable<(string where, double x, double y)> LakeCore(AtmosphereField.SeaplaneLake l, LakeSmoothingConfig c)
    {
        yield return ("water", l.CentreX, l.CentreY);
        if (l.SemiMajorM > 0)
        {
            double h = l.HeadingRad, cs = Math.Cos(h), sn = Math.Sin(h);
            yield return ("shore+margin", l.CentreX - sn * (l.SemiMinorM + 0.9 * c.ShoreMarginM), l.CentreY + cs * (l.SemiMinorM + 0.9 * c.ShoreMarginM));
        }
        foreach (var (x0, y0, x1, y1, hw) in l.Segments)
            foreach (double tt in new[] { 0.5, 0.8, 1.0 })
                yield return ($"approach {tt:P0}", x0 + (x1 - x0) * tt, y0 + (y1 - y0) * tt);
    }

    [Fact]
    public void SeaplaneLakesAndTheirApproachesAreSmootherByTheConfiguredFactor()
    {
        WithWorld(t =>
        {
            AtmosphereFieldConfig on = Cfg(), off = Cfg();
            off.LakeSmoothing.Enabled = false;
            LakeSmoothingConfig lc = on.LakeSmoothing;
            int checkedPts = 0;
            var lakesWithAir = new HashSet<string>();
            foreach (double from in new[] { 0.0, 90, 180, 270 })
            {
                on.Synoptic.WindFromDeg = off.Synoptic.WindFromDeg = from;
                on.Synoptic.WindSpeedKt = off.Synoptic.WindSpeedKt = 25;
                var f = new AtmosphereField(on) { Heating = (_, _) => 0.0 };
                var f0 = new AtmosphereField(off) { Heating = (_, _) => 0.0 };
                foreach (AtmosphereField.SeaplaneLake l in f.SeaplaneLakes)
                    foreach (var (where, x, y) in LakeCore(l, lc))
                        foreach (double agl in new[] { 15.0, 60, 150 })
                        {
                            var p = new Vec3(x, y, -(t.HeightAt(x, y) + agl));
                            AtmosphereSample s = f.Sample(p, 10), s0 = f0.Sample(p, 10);
                            if (s.CalmWeight > 0) continue;   // the home field's own (tested below)
                            string at = $"{l.Name} {where} {agl} m AGL wind from {from}";
                            Assert.Equal(1.0, s.LakeSmoothing);
                            // Compared with the same mountain away from the lake: the factor, exactly.
                            Assert.Equal(lc.Factor, s.RoughnessFactor / f.MountainRoughnessAt(x, y).factor, 9);
                            Assert.Equal(s0.RoughnessFactor * lc.Factor, s.RoughnessFactor, 9);
                            if (s0.OrographicSigmaMs > 1e-6) Assert.Equal(lc.Factor, s.OrographicSigmaMs / s0.OrographicSigmaMs, 6);
                            if (s0.RotorMs > 1e-6) Assert.Equal(lc.Factor, s.RotorMs / s0.RotorMs, 6);
                            if (s0.LeeMs < -1e-6) Assert.Equal(lc.Factor, s.LeeMs / s0.LeeMs, 6);
                            Assert.True(s.SigmaHorizMs < s0.SigmaHorizMs && s.SigmaVertMs < s0.SigmaVertMs, $"lighter turbulence: {at}");
                            // The laminar air is untouched: the same wind and slope lift.
                            Assert.Equal(s0.SlopeLiftMs, s.SlopeLiftMs, 9);
                            Assert.Equal(s0.SpeedUp, s.SpeedUp, 9);
                            if (s0.OrographicSigmaMs > 0.3 || s0.RotorMs > 0.5 || s0.LeeMs < -0.5) lakesWithAir.Add(l.Name);
                            checkedPts++;
                        }
                // Clear of every lake by its blend, the air is exactly what it would have been.
                foreach (AtmosphereField.SeaplaneLake l in f.SeaplaneLakes)
                {
                    double far = l.Segments.Count > 0 ? 3500 : 2500;
                    var q = new Vec3(l.CentreX + 0.7 * far, l.CentreY + 0.7 * far, 0);
                    if (f.LakeSmoothingAt(q.X, q.Y) > 0) continue;
                    q = new Vec3(q.X, q.Y, -(t.HeightAt(q.X, q.Y) + 60));
                    Assert.Equal(f0.Sample(q, 10).SigmaHorizMs, f.Sample(q, 10).SigmaHorizMs);
                }
            }
            _out.WriteLine($"{checkedPts} lake-core samples; lakes with real mountain air over them: {string.Join(", ", lakesWithAir)}");
            Assert.Contains("alpine:Outlet", lakesWithAir);
            Assert.Contains("alpine:Shelf", lakesWithAir);
            Assert.Contains("alpine:Larch", lakesWithAir);
            Assert.True(checkedPts > 300);
            return 0;
        });
    }

    [Fact]
    public void SmoothingAtALakeMeasured()
    {
        // The before/after numbers for the log: the worst point of each landable alpine lake's core, 25 kt from each way.
        WithWorld(t =>
        {
            AtmosphereFieldConfig on = Cfg(), off = Cfg();
            off.LakeSmoothing.Enabled = false;
            foreach (AtmosphereField.SeaplaneLake l in new AtmosphereField(on).SeaplaneLakes.Where(k => k.Name.StartsWith("alpine:") || k.Name.StartsWith("alaska:")))
            {
                double bs = 0, br = 0, bl = 0, as_ = 0, ar = 0, al = 0, bS = 0, aS = 0; string worst = "";
                foreach (double from in new[] { 0.0, 90, 180, 270 })
                {
                    on.Synoptic.WindFromDeg = off.Synoptic.WindFromDeg = from;
                    on.Synoptic.WindSpeedKt = off.Synoptic.WindSpeedKt = 25;
                    var f = new AtmosphereField(on) { Heating = (_, _) => 0.0 };
                    var f0 = new AtmosphereField(off) { Heating = (_, _) => 0.0 };
                    foreach (var (where, x, y) in LakeCore(l, on.LakeSmoothing))
                        foreach (double agl in new[] { 15.0, 60, 150 })
                        {
                            var p = new Vec3(x, y, -(t.HeightAt(x, y) + agl));
                            AtmosphereSample s = f.Sample(p, 10), s0 = f0.Sample(p, 10);
                            if (s0.OrographicSigmaMs > bs) { bs = s0.OrographicSigmaMs; as_ = s.OrographicSigmaMs; bS = s0.SigmaVertMs; aS = s.SigmaVertMs; worst = $"{where} {agl} m, from {from:000}"; }
                            if (s0.RotorMs > br) { br = s0.RotorMs; ar = s.RotorMs; }
                            if (s0.LeeMs < bl) { bl = s0.LeeMs; al = s.LeeMs; }
                        }
                }
                _out.WriteLine($"{l.Name,-14} worst σ_oro {bs:F2} → {as_:F2} m/s (total σw {bS:F2} → {aS:F2}; {worst}); rotor {br:F2} → {ar:F2} m/s; lee sink {bl:F2} → {al:F2} m/s");
            }
            return 0;
        });
    }

    [Fact]
    public void TheBlendsAreContinuous()
    {
        WithWorld(t =>
        {
            var f = new AtmosphereField(Cfg());
            double maxLakeStep = 0, maxRoughStep = 0, maxLenStep = 0;
            // Out from every lake along 16 bearings, through its corridor and blend, 1 m at a time.
            foreach (AtmosphereField.SeaplaneLake l in f.SeaplaneLakes)
                for (int k = 0; k < 16; k++)
                {
                    double b = k * Math.PI / 8, prevW = f.LakeSmoothingAt(l.CentreX, l.CentreY), prevR = f.OrographicRoughnessAt(l.CentreX, l.CentreY);
                    for (double r = 1; r <= 3500; r += 1)
                    {
                        double x = l.CentreX + Math.Cos(b) * r, y = l.CentreY + Math.Sin(b) * r;
                        double w = f.LakeSmoothingAt(x, y), R = f.OrographicRoughnessAt(x, y);
                        maxLakeStep = Math.Max(maxLakeStep, Math.Abs(w - prevW));
                        maxRoughStep = Math.Max(maxRoughStep, Math.Abs(R - prevR));
                        prevW = w; prevR = R;
                    }
                }
            // Across the whole world on lines of x and of y: every mountain's edge.
            for (double y = WorldTerrain.MeshMinY; y <= WorldTerrain.MeshMaxY; y += 1500)
            {
                var prev = f.MountainRoughnessAt(WorldTerrain.MeshMinX, y);
                for (double x = WorldTerrain.MeshMinX + 1; x <= WorldTerrain.MeshMaxX; x += 1)
                {
                    var r = f.MountainRoughnessAt(x, y);
                    maxRoughStep = Math.Max(maxRoughStep, Math.Abs(r.factor - prev.factor)); maxLenStep = Math.Max(maxLenStep, Math.Abs(r.lengthScale - prev.lengthScale));
                    prev = r;
                }
            }
            for (double x = WorldTerrain.MeshMinX; x <= WorldTerrain.MeshMaxX; x += 1000)
            {
                var prev = f.MountainRoughnessAt(x, WorldTerrain.MeshMinY);
                for (double y = WorldTerrain.MeshMinY + 1; y <= WorldTerrain.MeshMaxY; y += 1)
                {
                    var r = f.MountainRoughnessAt(x, y);
                    maxRoughStep = Math.Max(maxRoughStep, Math.Abs(r.factor - prev.factor)); maxLenStep = Math.Max(maxLenStep, Math.Abs(r.lengthScale - prev.lengthScale));
                    prev = r;
                }
            }
            _out.WriteLine($"largest change in 1 m: lake weight {maxLakeStep:F5}, roughness {maxRoughStep:F5}, length scale {maxLenStep:F5}");
            // A smootherstep across a 600 m blend changes by at most 1.875/600 = 0.003 of its range in a metre; across the
            // narrowest (300 m) roughness blend, 0.006 × the 1.35 jump from the Alaska summit to the plateau. No steps.
            Assert.True(maxLakeStep < 0.004, $"lake weight step {maxLakeStep}");
            Assert.True(maxRoughStep < 0.01, $"roughness step {maxRoughStep}");
            Assert.True(maxLenStep < 0.01, $"length-scale step {maxLenStep}");

            // And the sampled air: the turbulence σ walking off Outlet's approach into the rough range, 2 m at a time.
            AtmosphereField.SeaplaneLake o = f.SeaplaneLakes.Single(l => l.Name == "alpine:Outlet");
            f.SynopticFromRad = 270 * Math.PI / 180; f.SynopticSpeedMs = 25 * AtmosphereField.KtToMs;
            double prevS = double.NaN, maxJump = 0;
            for (double r = 0; r <= 3000; r += 2)
            {
                double x = o.CentreX - r, y = o.CentreY;   // due south, across the range's grain
                AtmosphereSample s = f.Sample(new Vec3(x, y, -(AlpineRange.BaseM + 1200)), 10);
                if (!double.IsNaN(prevS)) maxJump = Math.Max(maxJump, Math.Abs(s.RoughnessFactor - prevS));
                prevS = s.RoughnessFactor;
            }
            Assert.True(maxJump < 0.02, $"sampled roughness jumps {maxJump}");
            return 0;
        });
    }

    [Fact]
    public void TheCalmStartAirportIsStillExactlyZeroUnderItsLakes()
    {
        WithWorld(t =>
        {
            var c = Cfg(); c.Synoptic.WindSpeedKt = 30;
            var f = new AtmosphereField(c);
            AtmosphereField.CalmZone z = f.Calm!;
            // The canyon lake (and the Valley's own) lie in the home field's calm zone: smoothing must not leak into it, and the
            // calm air must not be "a small number" there either.
            AtmosphereField.SeaplaneLake canyon = f.SeaplaneLakes.Single(l => l.Name == "canyon");
            Assert.Equal(1.0, z.HorizontalWeight(canyon.CentreX, canyon.CentreY));
            int n = 0;
            foreach (AtmosphereField.SeaplaneLake l in f.SeaplaneLakes)
                for (double dx = -1500; dx <= 1500; dx += 150)
                    for (double dy = -1500; dy <= 1500; dy += 150)
                    {
                        double x = l.CentreX + dx, y = l.CentreY + dy;
                        if (z.HorizontalWeight(x, y) < 1.0) continue;
                        foreach (double agl in new[] { 1.0, 30, 150 })
                        {
                            double alt = t.HeightAt(x, y) + agl;
                            if (alt > z.CeilingMslM) continue;
                            AtmosphereSample s = f.Sample(new Vec3(x, y, -alt), 123.4);
                            Assert.True(s.CalmWeight == 1.0 && s.MeanWind.X == 0 && s.MeanWind.Y == 0 && s.MeanWind.Z == 0 && s.Gust.X == 0 && s.Gust.Y == 0 && s.Gust.Z == 0
                                        && s.SigmaHorizMs == 0 && s.SigmaVertMs == 0 && s.OrographicSigmaMs == 0 && s.RotorMs == 0 && s.LeeMs == 0,
                                $"{l.Name} +({dx}, {dy}) {agl} m: exactly calm");
                            Assert.Equal(Atmosphere.PressureAtAltitude(alt), s.PressurePa);
                            n++;
                        }
                    }
            _out.WriteLine($"{n} calm samples over the home field's lakes, all exactly zero");
            Assert.True(n > 100);
            return 0;
        });
    }
}
