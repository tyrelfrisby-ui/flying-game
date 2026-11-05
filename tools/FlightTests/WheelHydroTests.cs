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
/// A wheel on water (owner 2026-10-16: tyres skim before they reach land, and getting too slow flips it).
/// The numbers are checked against the documented hydroplaning speed, and the flip is FLOWN rather than
/// asserted — it has to come out of the moment arm, not out of a special case.
/// </summary>
// Mutates the global FloatHydro.FlatWaterOverride, so it must not run in parallel with another class that
// does: one class's finally clears the water out from under another class's seaplane mid-flight.
[Collection("WorldTerrainActive")]
public class WheelHydroTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public WheelHydroTests(ITestOutputHelper o) { _o = o; }

    public void Dispose()
    {
        FloatHydro.FlatWaterOverride = null;
        WorldTerrain.Active = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    private static AircraftConfig Cub() =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "pa18-cub-like.json"));

    [Fact]
    public void TheHydroplaningSpeedIsTheDocumentedOne()
    {
        // V_p (kt) = 9√p. A 10 psi bushwheel goes on top at about 28 kt; a 200 psi airliner tyre at 127.
        Assert.Equal(28.5, WheelHydro.HydroplaneSpeedMs(10) / 0.514444, 1);
        Assert.Equal(127.3, WheelHydro.HydroplaneSpeedMs(200) / 0.514444, 1);
        // And the pressure balance that underlies it: ½ρV² = p at roughly this speed.
        double vp = WheelHydro.HydroplaneSpeedMs(10);
        double dynamic = 0.5 * WheelHydro.WaterDensityKgM3 * vp * vp;      // Pa
        double tyre = 10 * 6894.76;                                         // Pa
        Assert.InRange(dynamic / tyre, 1.2, 2.2);                           // the empirical 9 vs the ideal 7.2
    }

    [Fact]
    public void ABushwheelRunsALowPressureAndAnAirlinerAHighOne()
    {
        // The estimate has to put a 35-inch bushwheel near 10 psi and a heavy jet's tyre near 200, because
        // that is what decides whether a pilot can skim a gravel bar at all.
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double psi = WheelHydro.TirePressurePsi(cub, main);
        _o.WriteLine($"Cub main tyre: r={LandingGear.TireRadiusOf(cub, main):0.000} m → {psi:0} psi → planes at {WheelHydro.HydroplaneSpeedMs(psi) / 0.514444:0} kt");
        Assert.InRange(psi, 8, 25);          // a Cub's 8.00-6 runs 18 psi in the POH

        var heavy = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "boeing-737-like.json"));
        GearConfig jet = heavy.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double jetPsi = WheelHydro.TirePressurePsi(heavy, jet);
        _o.WriteLine($"737 main tyre: r={LandingGear.TireRadiusOf(heavy, jet):0.000} m → {jetPsi:0} psi → planes at {WheelHydro.HydroplaneSpeedMs(jetPsi) / 0.514444:0} kt");
        Assert.True(jetPsi > psi * 3, "an airliner's tyre has to be far harder than a bushwheel");
    }

    private static double Lift(AircraftConfig c, GearConfig g, double h, double v)
    {
        Vec3 f = WheelHydro.Force(c, g, h, new Vec3(v, 0, 0), out _, out _, out _);
        return -f.Z;
    }

    /// <summary>The immersion at which the tyre's lift carries <paramref name="load"/>, or NaN if it cannot (bisection
    /// up to the axle, where the lift is greatest).</summary>
    private static double RideDepth(AircraftConfig c, GearConfig g, double load, double v)
    {
        double r = LandingGear.TireRadiusOf(c, g), lo = 0, hi = r;
        if (Lift(c, g, hi, v) < load) return double.NaN;
        for (int i = 0; i < 60; i++) { double m = 0.5 * (lo + hi); if (Lift(c, g, m, v) < load) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    [Fact]
    public void ThePressureCoefficientIsHornesLaw()
    {
        // ½ρV_p²C = p with V_p = 9√p kt: C = 2·6894.76 / (1000·(9·0.514444)²) = 13789.52 / 21437.0, worked by hand: 0.6433.
        Assert.Equal(0.6433, WheelHydro.PressureCoefficient, 4);
    }

    [Fact]
    public void TheForceGrowsFromNothingAtFirstTouch()
    {
        // Ty 2026-11-03: a gentle touch stopped the Extra dead. The water's hold on a tyre must start at zero.
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cub, main));
        double w = cub.Mass.MassKg * 9.81;
        // The planing lift grows as the wetted length, √(2rh): continuous, steep, and zero at zero.
        foreach (double v in new[] { 0.5 * vp, vp, 2 * vp })
        {
            double prev = 0;
            foreach (double h in new[] { 1e-9, 1e-7, 1e-5, 1e-3 })
            {
                double f = WheelHydro.Force(cub, main, h, new Vec3(v, 0, 0.5), out _, out _, out _).Length;
                Assert.True(f > prev, $"at {v:F1} m/s the force must grow with the immersion");
                prev = f;
            }
            Vec3 first = WheelHydro.Force(cub, main, 1e-9, new Vec3(v, 0, 0.5), out _, out _, out _);
            Assert.True(first.Length < 0.002 * w, $"at {v:F1} m/s, 1 nm in: {first.Length:F1} N");
        }
    }

    [Fact]
    public void TheRideHeightRisesWithSpeedAndAtVpTheWettedAreaIsTheFootprint()
    {
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double psi = WheelHydro.TirePressurePsi(cub, main), vp = WheelHydro.HydroplaneSpeedMs(psi);
        double load = cub.Mass.MassKg * 9.81 * 0.88 / 2;
        double r = LandingGear.TireRadiusOf(cub, main), b = WheelHydro.WidthOf(cub, main);
        double hSlow = RideDepth(cub, main, load, 0.8 * vp), hVp = RideDepth(cub, main, load, vp), hFast = RideDepth(cub, main, load, 1.5 * vp);
        _o.WriteLine($"ride depth under {load:F0} N: {hSlow * 1000:F0} mm at 0.8 Vp, {hVp * 1000:F0} at Vp, {hFast * 1000:F0} at 1.5 Vp");
        Assert.True(hSlow > hVp && hVp > hFast, "faster rides higher");
        // At V_p the water's pressure is the tyre's: the planform carrying the load is the dry footprint W/p.
        WheelHydro.Force(cub, main, hVp, new Vec3(vp, 0, 0), out _, out _, out WheelHydro.Parts parts);
        double footprint = load / (psi * 6894.76);
        Assert.Equal(1.0, b * parts.WettedLengthM / footprint, 2);
        // Below the dig-in speed nothing holds it up.
        double vDig = WheelHydro.DigInSpeedMs(cub, main, load);
        _o.WriteLine($"V_p {vp:F1} m/s, dig-in {vDig:F1} m/s ({vDig / vp:F2} V_p)");
        Assert.True(double.IsNaN(RideDepth(cub, main, load, 0.95 * vDig)));
        Assert.False(double.IsNaN(RideDepth(cub, main, load, 1.05 * vDig)));
    }

    [Fact]
    public void DeeperWaterMeansMoreDragAndTheLiftPeaksAtTheAxle()
    {
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double r = LandingGear.TireRadiusOf(cub, main), v = 10;
        double prevDrag = 0;
        foreach (double h in new[] { 0.02, 0.1, 0.5 * r, r, 1.5 * r, 2 * r })
        {
            Vec3 f = WheelHydro.Force(cub, main, h, new Vec3(v, 0, 0), out _, out _, out _);
            Assert.True(-f.X > prevDrag, $"drag must grow with depth (h {h:F3})");
            prevDrag = -f.X;
        }
        Assert.True(Lift(cub, main, r, v) > Lift(cub, main, 0.5 * r, v));
        Assert.True(Lift(cub, main, r, v) > Lift(cub, main, 1.5 * r, v));
        // In planing the drag is the planing surface's L·tan τ: D/L = tan(φ_w/2) = h/l_f.
        WheelHydro.Force(cub, main, 0.02, new Vec3(v, 0, 0), out _, out _, out WheelHydro.Parts p);
        Assert.Equal(0.02 / p.WettedLengthM, p.PressureDragN / p.LiftN, 6);
    }

    [Fact]
    public void TheTyrePressureTableFollowsTheOwnersRuleAndTheBooks()
    {
        // The rule: psi = gross lb / 100. Read per 100 lb rather than per 10, because per 10 would put a 1,750 lb Cub
        // at 175 psi and per 100 lands on the POH's 18. Since 2026-11-04 the configs STATE the pressure on each leg
        // (the book figures and the rule's values were copied into them as they were), so this holds the configs to it.
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double lb = cub.Mass.MassKg * 2.20462;
        _o.WriteLine($"Cub gross {lb:0} lb");

        cub.Bushwheels = false;
        double std = TirePressure.Psi(cub, main);
        _o.WriteLine($"  standard: {std:0.0} psi → planes at {WheelHydro.HydroplaneSpeedMs(std) / 0.514444:0} kt");
        Assert.Equal(18, std, 0);                           // the PA-18 POH figure, from the table

        // A type with no book entry falls to the rule exactly.
        var noBook = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "decathlon-8kcab-like.json"));
        GearConfig dm = noBook.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double dLb = noBook.Mass.MassKg * 2.20462;
        // The rule's 18 psi left the Decathlon's 6.00-6 below its rated deflection once pressure became the tyre's
        // spring (2026-11-04), so its config states the pressure that puts it there (TR R-64 eq. 27): 21.0, never
        // below the rule's figure.
        Assert.Equal(21.0, TirePressure.Psi(noBook, dm), 1);
        Assert.True(TirePressure.Psi(noBook, dm) >= dLb / 100.0 - 0.05);
        // The old bushwheel rule (gross / 200) is retired: the option fits a real tundra tyre at the pressure that puts
        // it at its rated deflection, inside its own range (TireCatalog.PressureForLoadPsi).
        noBook.Bushwheels = true;
        TireSpec tundra = TireCatalog.Of(dm)!;
        double bushPsi = TirePressure.Psi(noBook, dm);
        Assert.True(tundra.Tundra, $"the option should fit a tundra tyre, got {tundra.Id}");
        Assert.InRange(bushPsi, tundra.MinPsi, tundra.MaxPsi);
        _o.WriteLine($"Decathlon gross {dLb:0} lb → {dLb / 100:0.0} psi standard; bushwheels: {tundra.Id} at {bushPsi:0.0} psi");
    }

    /// <summary>Ty 2026-11-04: above V_p every aeroplane can ski unless its load beats the water. That needs V_p to be a
    /// speed at which the water CAN carry the static load, i.e. the footprint load/p fits inside the tyre's b·r. The
    /// owner's rule gave the Extra 18 psi on 0.155 m tyres (a 47 in² footprint on 28 in² of tyre). Literals by hand:
    /// Extra mains 0.932 of 820 kg (moment balance, mains x 0.347, tail −5.032, CG −0.02) = 3,748 N each over
    /// b·r = 0.1178 × 0.155 m² = 29.8 psi; Pitts 0.897 of 737 kg on r 0.170 = 21.4 psi. The Cub, 172 and J-3 keep their
    /// book or rule figures.</summary>
    [Fact]
    public void NoTyreIsBlownBelowWhatCarriesItsLoadSoEveryTyreCanCarryItAtVp()
    {
        AircraftConfig L(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        GearConfig Main(AircraftConfig c) => c.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        AircraftConfig extra = L("extra-300-like"), pitts = L("pitts-s2b-like"), c172 = L("c172-like"), j3 = L("j3-cub-like");
        Assert.Equal(29.8, WheelHydro.TirePressurePsi(extra, Main(extra)), 1);
        Assert.Equal(49.1, WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(extra, Main(extra))) / 0.514444, 1);
        Assert.Equal(21.4, WheelHydro.TirePressurePsi(pitts, Main(pitts)), 1);
        Assert.Equal(18.0, WheelHydro.TirePressurePsi(Cub(), Main(Cub())), 1);
        Assert.Equal(38.0, WheelHydro.TirePressurePsi(c172, Main(c172)), 1);
        Assert.Equal(29.0, WheelHydro.TirePressurePsi(c172, c172.Gear.Find(g => Math.Abs(g.Pos[1]) < 0.3)!), 1);
        Assert.Equal(12.2, WheelHydro.TirePressurePsi(j3, Main(j3)), 1);

        // And for every wheeled type and every leg: at V_p the leg's static load is within what its tyre can lift.
        foreach (object[] row in WheelWaterTests.Wheeled())
        {
            AircraftConfig c = L((string)row[0]);
            foreach (GearConfig g in c.Gear)
            {
                double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(c, g));
                double vDig = WheelHydro.DigInSpeedMs(c, g, WheelHydro.StaticLoadN(c, g));
                _o.WriteLine($"{row[0],-22} x {g.Pos[0],6:F2}: V_p {vp / 0.514444,5:F1} kt, static dig-in {vDig / 0.514444,5:F1} kt");
                Assert.True(vDig <= vp * (1 + 1e-9), $"{row[0]} leg at x {g.Pos[0]}: dig-in {vDig / 0.514444:F1} kt above V_p {vp / 0.514444:F1}");
            }
        }
    }

    [Fact]
    public void BushwheelsFitATundraTyreRunSofterAndPlaneSlower()
    {
        // The option the owner wants on every aeroplane: since 2026-11-04 a real tundra tyre from the catalog (it used
        // to be twice the tyre at half the pressure), and the whole point of it — it stays on top of the water down to a
        // much lower speed.
        var plain = Cub();
        var bush = Cub();
        bush.Bushwheels = true;
        GearConfig gp = plain.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        GearConfig gb = bush.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;

        TireSpec fitted = TireCatalog.Of(gb)!;
        Assert.True(fitted.Tundra);
        Assert.Equal(fitted.RadiusM, LandingGear.TireRadiusOf(bush, gb), 9);
        Assert.True(LandingGear.TireRadiusOf(bush, gb) > 1.25 * LandingGear.TireRadiusOf(plain, gp), "a tundra tyre is a much bigger tyre");
        // On the same axle: the contact point drops by the difference in radius.
        Assert.Equal(gp.Pos[2] + fitted.RadiusM - LandingGear.TireRadiusOf(plain, gp), gb.Pos[2], 9);

        double pPlain = WheelHydro.TirePressurePsi(plain, gp), pBush = WheelHydro.TirePressurePsi(bush, gb);
        Assert.True(pBush < pPlain * 0.6, $"bushwheels should run much softer ({pBush:0.0} vs {pPlain:0.0} psi)");

        double vPlain = WheelHydro.HydroplaneSpeedMs(pPlain), vBush = WheelHydro.HydroplaneSpeedMs(pBush);
        _o.WriteLine($"Cub planes at {vPlain / 0.514444:0} kt on standard tyres, {vBush / 0.514444:0} kt on bushwheels");
        Assert.True(vBush < vPlain * 0.8, "soft tyres go on top of the water at a lower speed");

        // And the fat tyre drags more once it IS ploughing, because there is more of it in the water.
        Vec3 fPlain = WheelHydro.Force(plain, gp, 0.12, new Vec3(8, 0, 0), out _, out _, out _);
        Vec3 fBush = WheelHydro.Force(bush, gb, 0.12, new Vec3(8, 0, 0), out _, out _, out _);
        Assert.True(Math.Abs(fBush.X) > Math.Abs(fPlain.X), "a wider tyre pushes more water");
    }

    [Fact]
    public void ACubSkimsShallowWaterFastAndNosesOverBelowThePlaningSpeed()
    {
        // The real case, and the one the owner described: a hand's depth of water (0.10 m) over a gravel bed, which is
        // what the braided rivers are. Flown in, not started in the water: a light touch at skimming speed skims.
        AircraftConfig cfg = Cub();
        GearConfig main = cfg.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cfg, main));
        _o.WriteLine($"Cub planes at {vp:0.0} m/s ({vp / 0.514444:0} kt)");
        FloatHydro.FlatWaterOverride = 0.10;
        try
        {
            // At the skimming speed of the all-types test (1.15 × its slowest glide trim: 1.5 V_p for the PA-18).
            var fast = WheelWaterTouch.Run(cfg, 0.5, 3.0, holdPitchDeg: 3);   // nose held at 3°: on trim it floats in ground effect
            _o.WriteLine($"  {fast.V / vp:F2} V_p: " + fast);
            Assert.True(fast.TouchAt > 0, "never touched");
            Assert.True(fast.NoseOverAt < 0, "above the planing speed it should skim, not dig in");
            Assert.True(fast.PeakDecelG < 0.5, $"peak {fast.PeakDecelG:F2} g");
            Assert.True(fast.Lost.Count == 0 && fast.Damage.Count == 0);

            // Power off it slows on the water — and in a hand's depth it does not go over: the bed holds the tyre's
            // immersion to 0.10 m, so the plough drag (½ρV²·C·b·0.10) never reaches the nose-over moment. (In deep
            // water it does: WheelWaterTests.SlowingBelowThePlaningSpeedDigsIn….)
            var idle = WheelWaterTouch.Run(cfg, 0.5, 15.0, speed: 1.3 * vp, holdPitchDeg: 4);
            _o.WriteLine("  1.3 V_p, idle, slowing: " + idle);
            Assert.True(idle.MinSpeed < 0.5 * vp, "it should slow right down in the water");
            Assert.True(idle.PeakDecelG < 1.0, $"peak {idle.PeakDecelG:F2} g");
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }
}
