using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The tyre catalog and the tyre physics (Ty 2026-11-04, "Yes and 35's"; TireCatalog, TireModel). Expected values are
/// literals copied by hand from the sources the catalog cites, never read back out of the catalog.
/// </summary>
[Collection("WorldTerrainActive")]
public class TireCatalogTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public TireCatalogTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // ---- the catalog ------------------------------------------------------------------------------------------

    /// <summary>The tyre weights Ty's brief gave (McFarlane Aviation's product table, tyre only, lb), copied by hand,
    /// and the Goodyear data-book sizes (OD, section width, in).</summary>
    [Theory]
    [InlineData("26126.R", 26, 12, 6, 26)]
    [InlineData("29136.R", 29, 13, 6, 31)]
    [InlineData("31136.R", 31, 13, 6, 35)]
    [InlineData("351510.R1", 35, 15, 10, 44)]
    [InlineData("351510.R", 35, 15, 10, 60)]
    [InlineData("8.00-6", 19.5, 7.95, 6, 12.3)]
    [InlineData("8.50-6", 22.1, 8.85, 6, 16.0)]
    [InlineData("6.00-6", 17.5, 6.3, 6, 8.9)]
    [InlineData("5.00-5", 14.2, 4.95, 5, 5.5)]
    [InlineData("8.50-10", 25.65, 8.7, 10, 23.2)]
    [InlineData("8.00-4", 18.0, 8.3, 4, 9.0)]
    public void TheCatalogCarriesTheSourcedSizesAndWeights(string id, double od, double w, double rim, double tireLb)
    {
        TireSpec t = TireCatalog.Get(id)!;
        Assert.NotNull(t);
        Assert.Equal(od, t.OdIn, 6); Assert.Equal(w, t.WidthIn, 6); Assert.Equal(rim, t.RimIn, 6); Assert.Equal(tireLb, t.TireLb, 6);
        Assert.False(t.Legacy);
        Assert.False(string.IsNullOrWhiteSpace(t.Source));
    }

    /// <summary>The inertia is DERIVED from where the mass is, and a balloon tyre's own k² is 0.7–0.8 R² (the brief:
    /// "rim/tread-heavy"); the whole wheel, with its hub at a small radius, comes out lower.</summary>
    [Fact]
    public void InertiaComesFromTheMassDistribution()
    {
        foreach (TireSpec t in TireCatalog.All.Where(t => !t.Legacy))
        {
            double own = t.TireInertiaKgM2 / (t.TireMassKg * t.RadiusM * t.RadiusM);
            _o.WriteLine($"{t.Id,-10} tyre {t.TireMassKg,5:F1} kg  rotating {t.RotatingMassKg(true),5:F1} kg  I {t.InertiaKgM2(true),6:F3} kg·m²  tyre k {own:F2}  wheel k {t.InertiaFactor(true):F2}  ({t.Name})");
            if (t.Tundra) Assert.InRange(own, 0.6, 0.85);
            Assert.InRange(t.InertiaFactor(true), 0.3, own);         // the hub and disc sit well inside the tread
            if (t.DiscLb > 0) Assert.True(t.InertiaKgM2(true) > t.InertiaKgM2(false), "the disc turns too");
        }
    }

    /// <summary>The Beaver's heavy-duty 35 against the Super Cub's lightweight 35: same size, the HD tyre is 16 lb more
    /// rubber (60 against 44) on a heavier wheel and disc, and nearly all of it is at the tread.</summary>
    [Fact]
    public void TheHeavyDuty35IsHeavierAndHasMoreInertiaThanTheLightweight()
    {
        TireSpec hd = TireCatalog.Get("351510.R")!, lw = TireCatalog.Get("351510.R1")!;
        _o.WriteLine($"HD 35: rotating {hd.RotatingMassKg(true):F1} kg, I {hd.InertiaKgM2(true):F3} kg·m², I/R² {hd.InertiaKgM2(true) / (hd.RadiusM * hd.RadiusM):F1} kg");
        _o.WriteLine($"LW 35: rotating {lw.RotatingMassKg(true):F1} kg, I {lw.InertiaKgM2(true):F3} kg·m², I/R² {lw.InertiaKgM2(true) / (lw.RadiusM * lw.RadiusM):F1} kg");
        Assert.True(hd.HeavyDuty && !lw.HeavyDuty);
        Assert.Equal(16 * TireSpec.Lb, hd.TireMassKg - lw.TireMassKg, 6);
        Assert.InRange(hd.InertiaKgM2(true) / lw.InertiaKgM2(true), 1.25, 1.45);
    }

    // ---- every config on the catalog ---------------------------------------------------------------------------

    [Fact]
    public void EveryWheelInEveryConfigNamesACatalogTyreAndNoneIsHandSet()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var bad = new List<string>();
        int legs = 0;
        foreach (string f in Directory.GetFiles(dir, "*-like.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            if (!doc.RootElement.TryGetProperty("gear", out JsonElement gear)) continue;
            int i = 0;
            foreach (JsonElement g in gear.EnumerateArray())
            {
                string name = $"{Path.GetFileNameWithoutExtension(f)}[{i++}]";
                string type = g.TryGetProperty("gearType", out var gt) ? gt.GetString() ?? "" : "";
                bool wheel = type is not ("float-keel" or "nose-skid" or "tip-skid" or "tail-skid" or "feet");
                foreach (string key in new[] { "wheelMassKg", "wheelInertiaKgM2", "bushwheels" })
                    if (g.TryGetProperty(key, out _)) bad.Add($"{name} hand-sets {key}");
                if (!wheel) continue;
                legs++;
                if (g.TryGetProperty("tireRadiusM", out _)) bad.Add($"{name} hand-sets tireRadiusM on a wheel");
                string tire = g.TryGetProperty("tire", out var t) ? t.GetString() ?? "" : "";
                if (!TireCatalog.Has(tire)) bad.Add($"{name}: tyre '{tire}' is not in the catalog");
                if (!(g.TryGetProperty("tirePressurePsi", out var p) && p.GetDouble() > 0)) bad.Add($"{name} states no pressure");
                if (g.TryGetProperty("tundraTire", out var tt) && !(TireCatalog.Get(tt.GetString())?.Tundra ?? false)) bad.Add($"{name}: tundraTire is not a tundra tyre");
            }
            if (doc.RootElement.TryGetProperty("bushwheels", out _)) bad.Add($"{Path.GetFileName(f)} sets the session's bushwheel option");
        }
        _o.WriteLine($"{legs} wheel legs checked");
        Assert.True(legs > 60);
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    /// <summary>The assignments Ty made (2026-11-04, 2:39–2:41 PM): the bush Super Cub on the 35 in lightweight, the
    /// standard one on the stock 8.00-6 at its POH 18 psi, and the Beaver's tundra option the heavy-duty 35.</summary>
    [Fact]
    public void TheAssignmentsAreTheOnesTyMade()
    {
        AircraftConfig bush = Cfg("pa18-bush-like"), cub = Cfg("pa18-cub-like"), beaver = Cfg("dhc2-beaver-like");
        foreach (GearConfig g in bush.Gear.Where(TireFit.IsMainWheel)) { Assert.Equal("351510.R1", g.Tire); Assert.Equal(8, g.TirePressurePsi); }
        foreach (GearConfig g in cub.Gear.Where(TireFit.IsMainWheel)) { Assert.Equal("8.00-6", g.Tire); Assert.Equal(18, g.TirePressurePsi); }
        foreach (GearConfig g in beaver.Gear.Where(TireFit.IsMainWheel)) { Assert.Equal("8.50-10", g.Tire); Assert.Equal("351510.R", g.TundraTire); }
        beaver.Bushwheels = true;
        foreach (GearConfig g in beaver.Gear.Where(TireFit.IsMainWheel))
        {
            Assert.Equal("351510.R", g.Tire);
            _o.WriteLine($"Beaver on bushwheels: {g.Tire} at {g.TirePressurePsi:F1} psi, contact point {g.Pos[2]:F3} m below the CG");
        }
        beaver.Bushwheels = false;
        foreach (GearConfig g in beaver.Gear.Where(TireFit.IsMainWheel)) Assert.Equal("8.50-10", g.Tire);
    }

    // ---- the physics ---------------------------------------------------------------------------------------------

    /// <summary>TR R-64 eq. 24 against the one Goodyear static loaded radius the catalog has for a tyre it also has the
    /// rating of: the 8.00-6 6-ply, 2,050 lb at 35 psi, SLR 7.55 in on a 19.5 in tyre — 2.20 in of deflection.</summary>
    [Fact]
    public void TheVerticalFitLandsOnTheDataBooksLoadedRadius()
    {
        TireSpec t = TireCatalog.Get("8.00-6")!;
        double d = TireModel.StaticDeflectionM(t, 35, 2050 * 4.448222) / 0.0254;
        _o.WriteLine($"8.00-6 at 2,050 lb / 35 psi: {d:F2} in (data book 2.20)");
        Assert.InRange(d, 2.20 * 0.85, 2.20 * 1.15);
    }

    /// <summary>The leg and the tyre in series: held long enough the junction settles on the static split, and the
    /// load at that split is the series spring's; at the instant of contact the tyre carries nothing yet.</summary>
    [Fact]
    public void TheTyreIsInSeriesWithTheLeg()
    {
        TireSpec t = TireCatalog.Get("31136.R")!;
        double psi = 10, kl = 35000, cl = 2600, ct = 200, x = 0.08;
        double d = 0, f = 0;
        for (int i = 0; i < 400; i++) d = TireModel.Junction(t, psi, d, x, 0, 0.005, kl, cl, ct, out f);
        double split = d;
        Assert.Equal(kl * (x - split), TireModel.SpringForce(t, psi, split), 0);   // the axle is balanced
        Assert.Equal(kl * (x - split), f, 0);
        double qs = TireModel.QuasiStatic(t, psi, x, 0, kl, cl, out double dq);
        Assert.Equal(split, dq, 4); Assert.Equal(f, qs, 0);
        // Series: softer than either spring alone.
        Assert.True(f < kl * x && f < TireModel.SpringForce(t, psi, x));
        // First touch at 1.8 m/s: the leg's damper is stiff, so the tyre takes the motion and the load starts near zero.
        TireModel.Junction(t, psi, 0, 1e-4, 1.8, 0, kl, cl, ct, out double f0);
        Assert.True(f0 < 0.1 * cl * 1.8, $"first-touch load {f0:F0} N against the leg damper's {cl * 1.8:F0}");
        _o.WriteLine($"31x13 at 10 psi on a 35 kN/m leg, 80 mm compressed: tyre {split * 1000:F1} mm, leg {(x - split) * 1000:F1} mm, {f:F0} N; first touch {f0:F0} N");
    }

    /// <summary>Lower pressure: a longer and larger footprint and a lower ground pressure at the same load (eqs. 5, 12).</summary>
    [Fact]
    public void TheFootprintGrowsAndTheGroundPressureFallsAsThePressureComesDown()
    {
        TireSpec t = TireCatalog.Get("29136.R")!;
        double load = 3500, prevL = 0, prevPg = double.MaxValue;
        foreach (double psi in new[] { 20.0, 14, 10, 8 })
        {
            double d = TireModel.StaticDeflectionM(t, psi, load);
            double l = TireModel.FootprintLengthM(t, d), a = TireModel.FootprintAreaM2(t, d), pg = TireModel.GroundPressurePa(t, load, d);
            _o.WriteLine($"29x13 at {psi,2} psi, {load} N: δ {d * 1000:F0} mm, footprint {l * 1000:F0} × {TireModel.FootprintWidthM(t, d) * 1000:F0} mm, {a * 1e4:F0} cm², ground {pg / TireModel.Psi:F1} psi");
            Assert.True(l > prevL && pg < prevPg);
            // Ground pressure ≈ inflation + carcass (2.4 / 2.3 of it, from eqs. 24 and 12).
            Assert.InRange(pg / TireModel.Psi, 0.8 * psi, psi + 0.08 * t.MaxPsi + 3);
            prevL = l; prevPg = pg;
        }
    }

    /// <summary>Sinkage, and so rolling resistance on soft ground, drops with a bigger and softer tyre: the Cub's
    /// 8.00-6 at 18 psi against each tundra tyre at 8, at the same load.</summary>
    [Fact]
    public void SinkageAndRollingDropWithABiggerSofterTyre()
    {
        double load = 3500;
        TireSpec stock = TireCatalog.Get("8.00-6")!;
        double Roll(TireSpec t, double psi, WorldTerrain.Surface s) => TireModel.RollingCoefficient(t, psi, s, load, TireModel.StaticDeflectionM(t, psi, load));
        foreach (WorldTerrain.Surface s in new[] { WorldTerrain.Surface.Sand, WorldTerrain.Surface.Grass, WorldTerrain.Surface.Snow, WorldTerrain.Surface.Rough, WorldTerrain.Surface.Dirt })
        {
            double prev = Roll(stock, 18, s);
            string line = $"{s,-6} 8.00-6@18 {prev:F3}";
            foreach (string id in new[] { "26126.R", "31136.R", "351510.R1" })
            {
                double r = Roll(TireCatalog.Get(id)!, 8, s);
                line += $"  {id}@8 {r:F3}";
                Assert.True(r < prev, $"{s}: {id} should roll easier than the tyre before it ({r:F3} vs {prev:F3})");
                prev = r;
            }
            _o.WriteLine(line);
        }
        // On a packed glacier zone: how far the tyre goes through the crust.
        double hard = LandingGear.SnowSinkageRolling(stock, 18, WorldTerrain.Surface.PackedGlacierSnow, load);
        double soft = LandingGear.SnowSinkageRolling(TireCatalog.Get("351510.R1")!, 8, WorldTerrain.Surface.PackedGlacierSnow, load);
        _o.WriteLine($"packed snow: 8.00-6@18 {hard:F3}, 35x15@8 {soft:F3}");
        Assert.True(soft < hard);
        // Pavement: no ground share, only the tyre's own hysteresis, and a flatter tyre loses more.
        Assert.True(Roll(TireCatalog.Get("351510.R1")!, 6, WorldTerrain.Surface.Paved) > Roll(TireCatalog.Get("351510.R1")!, 20, WorldTerrain.Surface.Paved));
    }

    /// <summary>
    /// The slip curve's scale κ0 = μN/(r_e·K_x) (TR R-64 eqs. 48, 90) moves with pressure. Hand calculation for a 31x13
    /// carrying 3,500 N with μN = 2,800 N: at 8 psi δ ≈ 47 mm, K_x = 0.53·d·(p + 4·20 psi)·∛(δ/d). What it says, and
    /// what this holds the model to: the carcass term (4·p_r) dominates K_x, so the pressure moves κ0 only modestly —
    /// lower pressure flattens the tyre, which LENGTHENS the footprint and raises K_x by ∛δ, so κ0 falls a little.
    /// (AGENT-LOG: the brief expected a soft tyre to peak later; on pavement TR R-64 does not say that.)
    /// </summary>
    [Fact]
    public void TheSlipCurvePeakMovesWithPressure()
    {
        TireSpec t = TireCatalog.Get("31136.R")!;
        double load = 3500, muN = 0.8 * load;
        double Peak(double k0) { double best = 0, at = 0; for (int i = 1; i <= 400; i++) { double k = i * 0.0025, d = WheelSpin.Longitudinal(k, 0, muN, k0); if (d > best) { best = d; at = k; } } return at; }
        var rows = new List<(double psi, double k0, double peak)>();
        foreach (double psi in new[] { 6.0, 8, 12, 20 })
        {
            double d = TireModel.StaticDeflectionM(t, psi, load);
            double k0 = TireModel.SlipRatioScale(t, psi, muN, d);
            rows.Add((psi, k0, Peak(k0)));
            _o.WriteLine($"31x13 at {psi,2} psi: δ {d * 1000:F0} mm, K_x {TireModel.ForeAftStiffnessNPerM(t, psi, d) / 1000:F0} kN/m, κ0 {k0:F3}, force peaks at slip {Peak(k0):F3}");
        }
        // By hand at 8 psi (δ from the catalog's own eq. 24, printed above): K_x = 0.53 × 0.7874 m × (8 + 80) psi × 6894.76 × ∛(δ/d).
        double d8 = TireModel.StaticDeflectionM(t, 8, load);
        double kx8 = 0.53 * 0.7874 * 88 * 6894.76 * Math.Cbrt(d8 / 0.7874);
        Assert.Equal(muN / ((0.3937 - d8 / 3) * kx8), rows[1].k0, 4);
        Assert.True(rows[0].k0 != rows[3].k0);
        Assert.True(rows[0].k0 < rows[3].k0, "lower pressure: a longer footprint and a smaller κ0 (TR R-64)");
        Assert.True(rows[0].peak <= rows[3].peak, "and the force peaks at a smaller slip");
    }

    // ---- the touchdown matrix --------------------------------------------------------------------------------------

    public readonly record struct Touchdown(double SpinUpQDegS, double MinQDegS, double MaxQDegS, double BounceM, double PeakLoadN, double PeakDeflMm, double DvMs, bool Broke);

    /// <summary>
    /// A wheel landing: the mains 10 cm above a paved runway, the aeroplane at <paramref name="pitchDeg"/> and
    /// <paramref name="speedMs"/> with <paramref name="sinkFps"/> of sink, idle, hands off, 1.5 s flown. Cold wheels
    /// (stopped, as on any arrival) or pre-spun to the ground speed (what the model did before 2026-11-04). Reports the
    /// most nose-DOWN pitch rate in the first 0.15 s after the first touch (the spin-up's window) and over the 1.5 s
    /// (deg/s, negative), the most nose-up, the highest the main wheels get back above the runway after the first touch
    /// (bounce, m), the peak main-wheel load, the peak tyre deflection and the ground speed lost in the first 0.3 s.
    /// </summary>
    internal static Touchdown Land(AircraftConfig c, double sinkFps, bool prespun, double speedMs, double pitchDeg = 6)
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Paved;
        try
        {
            double p = pitchDeg * Math.PI / 180;
            var att = new Quat(0, Math.Sin(p / 2), 0, Math.Cos(p / 2));
            var mains = Enumerable.Range(0, c.Gear.Count).Where(i => TireFit.IsMainWheel(c.Gear[i])).ToArray();
            double mz = mains.Max(i => att.Rotate(c.Gear[i].PosVec() - c.Mass.CgVec()).Z);
            var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -mz - 0.10), att, att.Conjugate().Rotate(new Vec3(speedMs, 0, sinkFps * 0.3048)), Vec3.Zero),
                                  new ControlDeflections(0, 0, 0, 0));
            var sim = new SimLoop(ac);
            sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0));
            if (prespun) foreach (int i in mains) ac.Wheels[i].OmegaRadS = speedMs / ac.Wheels[i].RollingRadiusM;
            double minQ = 0, spinQ = 0, maxQ = 0, bounce = 0, load = 0, defl = 0, v0 = HorizontalSpeed(ac), dv = 0;
            bool touched = false; int since = 0;
            for (int k = 0; k < 300; k++)
            {
                sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0));
                double q = ac.State.Rates.Y * 180 / Math.PI;
                foreach (int i in mains)
                {
                    WheelState w = ac.Wheels[i];
                    if (w.InContact) touched = true;
                    load = Math.Max(load, w.LoadN); defl = Math.Max(defl, w.TireDeflM);
                }
                if (touched)
                {
                    minQ = Math.Min(minQ, q); maxQ = Math.Max(maxQ, q);
                    if (since++ < 30) spinQ = Math.Min(spinQ, q);
                    double low = mains.Min(i => -(ac.State.Position + ac.State.Attitude.Rotate(c.Gear[i].PosVec() - c.Mass.CgVec())).Z);
                    bounce = Math.Max(bounce, low);
                }
                if (k == 60) dv = v0 - HorizontalSpeed(ac);
            }
            return new Touchdown(spinQ, minQ, maxQ, bounce, load, defl * 1000, dv, ac.LostComponents.Count > 0);
        }
        finally { WorldTerrain.SurfaceOverride = null; }
    }

    private static double HorizontalSpeed(Aircraft ac)
    {
        Vec3 v = ac.State.Attitude.Rotate(ac.State.Velocity);
        return Math.Sqrt(v.X * v.X + v.Y * v.Y);
    }

    /// <summary>The PA-18 (the bush Super Cub's airframe and legs) on each tyre, on the same axle: the contact point moves
    /// with the radius against the 35 in it was drawn for.</summary>
    private static AircraftConfig Pa18On(string tire, double psi)
    {
        AircraftConfig c = Cfg("pa18-bush-like");
        foreach (GearConfig g in c.Gear.Where(TireFit.IsMainWheel))
        {
            double r0 = LandingGear.TireRadiusOf(c, g);
            g.Tire = tire; g.TirePressurePsi = psi;
            g.Pos[2] += TireCatalog.Get(tire)!.RadiusM - r0;
        }
        return c;
    }

    /// <summary>The matrix the brief asked for. The stock tyres at the POH's 18 psi; every tundra tyre at the bush Super
    /// Cub's 8 psi. 22 m/s (43 kt), 6° — a wheel landing.</summary>
    [Fact]
    public void TundraPa18TouchdownMatrix()
    {
        _o.WriteLine("| tyre | psi | sink ft/s | wheels | nose-down q, spin-up (0.15 s) deg/s | nose-down q, 1.5 s | nose-up q | bounce m | peak main load N | peak tyre defl mm | ΔV 0.3 s m/s |");
        _o.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        var results = new Dictionary<(string, double, bool), Touchdown>();
        foreach (var (tire, psi) in new[] { ("8.00-6", 18.0), ("8.50-6", 18.0), ("26126.R", 8.0), ("29136.R", 8.0), ("31136.R", 8.0), ("351510.R1", 8.0) })
            foreach (double sink in new[] { 3.0, 4.5, 6.0 })
                foreach (bool pre in new[] { false, true })
                {
                    Touchdown r = Land(Pa18On(tire, psi), sink, pre, 22);
                    results[(tire, sink, pre)] = r;
                    _o.WriteLine($"| {tire} | {psi:F0} | {sink:F1} | {(pre ? "pre-spun" : "cold")} | {r.SpinUpQDegS:F1} | {r.MinQDegS:F1} | {r.MaxQDegS:F1} | {r.BounceM:F3} | {r.PeakLoadN:F0} | {r.PeakDeflMm:F0} | {r.DvMs:F2} |");
                    Assert.True(double.IsFinite(r.MinQDegS) && double.IsFinite(r.BounceM));
                    Assert.False(r.Broke, $"{tire} at {sink} ft/s broke something");
                }
        foreach (var (tire, _) in new[] { ("8.00-6", 0), ("351510.R1", 0) })
            foreach (double sink in new[] { 3.0, 4.5, 6.0 })
            {
                // The spin-up's nose-down impulse and its speed cost come from the cold wheels alone.
                Assert.True(results[(tire, sink, false)].SpinUpQDegS < results[(tire, sink, true)].SpinUpQDegS, $"{tire} {sink}: no spin-up pitch-down");
                Assert.True(results[(tire, sink, false)].DvMs > results[(tire, sink, true)].DvMs, $"{tire} {sink}: no spin-up speed cost");
            }
        // A tundra tyre at 8 psi deflects further than the 8.00-6 at 18 for the same arrival. (Its PEAK LOAD is not lower:
        // the leg's damper sets most of it, and the wide 35 at 8 psi is a stiffer spring than a 26–31 at 8. Printed.)
        foreach (string tundra in new[] { "26126.R", "29136.R", "31136.R", "351510.R1" })
            foreach (double sink in new[] { 3.0, 4.5, 6.0 })
                Assert.True(results[(tundra, sink, true)].PeakDeflMm > results[("8.00-6", sink, true)].PeakDeflMm, $"{tundra} {sink} ft/s");
    }

    /// <summary>The Beaver on its heavy-duty 35s (its tundra option) against its stock 8.50-10s, and the HD 35 against the
    /// lightweight one on the same aeroplane. 26 m/s (50 kt), 5°.</summary>
    [Fact]
    public void BeaverOnHeavyDuty35sTouchdownMatrix()
    {
        AircraftConfig Beaver(string? tire)
        {
            AircraftConfig c = Cfg("dhc2-beaver-like");
            if (tire == "351510.R") c.Bushwheels = true;
            else if (tire != null)
                foreach (GearConfig g in c.Gear.Where(TireFit.IsMainWheel)) { g.TundraTire = tire; }
            if (tire != null && tire != "351510.R") c.Bushwheels = true;
            return c;
        }
        _o.WriteLine("| tyre | psi | sink ft/s | wheels | nose-down q, spin-up (0.15 s) deg/s | nose-down q, 1.5 s | nose-up q | bounce m | peak main load N | peak tyre defl mm | ΔV 0.3 s m/s |");
        _o.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        var res = new Dictionary<(string, double, bool), Touchdown>();
        foreach (string? tire in new[] { null, "351510.R", "351510.R1" })
            foreach (double sink in new[] { 3.0, 4.5, 6.0 })
                foreach (bool pre in new[] { false, true })
                {
                    AircraftConfig c = Beaver(tire);
                    GearConfig m = c.Gear.First(TireFit.IsMainWheel);
                    Touchdown r = Land(c, sink, pre, 26, 5);
                    res[(m.Tire, sink, pre)] = r;
                    _o.WriteLine($"| {m.Tire} | {m.TirePressurePsi:F1} | {sink:F1} | {(pre ? "pre-spun" : "cold")} | {r.SpinUpQDegS:F1} | {r.MinQDegS:F1} | {r.MaxQDegS:F1} | {r.BounceM:F3} | {r.PeakLoadN:F0} | {r.PeakDeflMm:F0} | {r.DvMs:F2} |");
                    Assert.False(r.Broke, $"Beaver on {m.Tire} at {sink} ft/s broke something");
                }
        foreach (double sink in new[] { 3.0, 4.5, 6.0 })
        {
            Assert.True(res[("351510.R", sink, false)].SpinUpQDegS < res[("351510.R", sink, true)].SpinUpQDegS, $"{sink}: no spin-up pitch-down");
            // The heavier tyre costs more to spin up.
            double hd = res[("351510.R", sink, false)].DvMs - res[("351510.R", sink, true)].DvMs;
            double lw = res[("351510.R1", sink, false)].DvMs - res[("351510.R1", sink, true)].DvMs;
            _o.WriteLine($"{sink} ft/s spin-up ΔV: HD 35 {hd:F3} m/s, LW 35 {lw:F3} m/s");
            Assert.True(hd > lw);
        }
    }
}
