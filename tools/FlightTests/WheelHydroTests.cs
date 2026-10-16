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

    [Fact]
    public void TheWaterCarriesTheWheelAtSpeedAndNotAtAWalk()
    {
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cub, main));
        const double load = 3000;

        // Fast: the water holds the wheel up and the drag is spray.
        Vec3 fast = WheelHydro.Force(cub, main, 0.10, load, new Vec3(vp * 1.2, 0, 0), out bool planingFast, out double phiFast);
        Assert.True(planingFast, "at 1.2 V_p it should be on top");
        Assert.Equal(1.0, phiFast, 3);
        Assert.Equal(-load, fast.Z, 0);                         // NED: the whole load, upward
        double dragFast = Math.Sqrt(fast.X * fast.X + fast.Y * fast.Y);

        // Slow: almost no support, and a great deal more drag for the speed.
        Vec3 slow = WheelHydro.Force(cub, main, 0.10, load, new Vec3(vp * 0.35, 0, 0), out bool planingSlow, out double phiSlow);
        Assert.False(planingSlow);
        Assert.InRange(phiSlow, 0.1, 0.2);
        Assert.True(-slow.Z < load * 0.2, "a wheel at a third of V_p is not being held up");
        double dragSlow = Math.Sqrt(slow.X * slow.X + slow.Y * slow.Y);
        // The drag COEFFICIENT is what jumps: nine times, from spray to ploughing.
        double cdFast = dragFast / (0.5 * 1000 * (vp * 1.2) * (vp * 1.2));
        double cdSlow = dragSlow / (0.5 * 1000 * (vp * 0.35) * (vp * 0.35));
        _o.WriteLine($"V_p {vp:0.0} m/s: planing Cd·A {cdFast:0.0000}, ploughing Cd·A {cdSlow:0.0000}");
        Assert.True(cdSlow > cdFast * 5, "the ploughing drag coefficient has to be far higher than the spray one");
    }

    [Fact]
    public void DeeperWaterMeansMoreDragAndNoMoreSupport()
    {
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cub, main));
        Vec3 shallow = WheelHydro.Force(cub, main, 0.03, 3000, new Vec3(vp * 0.5, 0, 0), out _, out _);
        Vec3 deep = WheelHydro.Force(cub, main, 0.30, 3000, new Vec3(vp * 0.5, 0, 0), out _, out _);
        Assert.True(Math.Abs(deep.X) > Math.Abs(shallow.X) * 3, "deeper immersion has to drag much harder");
        Assert.Equal(shallow.Z, deep.Z, 1);                      // the support is the speed's business, not the depth's
    }

    [Fact]
    public void TheTyrePressureTableFollowsTheOwnersRuleAndTheBooks()
    {
        // The rule: psi = gross lb / 100, and half that on bushwheels. Read per 100 lb rather than per 10,
        // because per 10 would put a 1,750 lb Cub at 175 psi and per 100 lands on the POH's 18.
        AircraftConfig cub = Cub();
        GearConfig main = cub.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double lb = cub.Mass.MassKg * 2.20462;
        _o.WriteLine($"Cub gross {lb:0} lb");

        cub.Bushwheels = false;
        double std = TirePressure.Psi(cub, main, false);
        _o.WriteLine($"  standard: {std:0.0} psi → planes at {WheelHydro.HydroplaneSpeedMs(std) / 0.514444:0} kt");
        Assert.Equal(18, std, 0);                           // the PA-18 POH figure, from the table

        // A type with no book entry falls to the rule exactly.
        var noBook = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "decathlon-8kcab-like.json"));
        GearConfig dm = noBook.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double dLb = noBook.Mass.MassKg * 2.20462;
        Assert.Equal(dLb / 100.0, TirePressure.Psi(noBook, dm, false), 1);
        Assert.Equal(dLb / 200.0, TirePressure.Psi(noBook, dm, true), 1);
        _o.WriteLine($"Decathlon gross {dLb:0} lb → {dLb / 100:0.0} psi standard, {dLb / 200:0.0} on bushwheels");
    }

    [Fact]
    public void BushwheelsDoubleTheTyreHalveThePressureAndPlaneSlower()
    {
        // The option the owner wants on every aeroplane: twice the tyre, half the pressure, and the whole
        // point of it — it stays on top of the water down to a much lower speed.
        var plain = Cub();
        var bush = Cub();
        bush.Bushwheels = true;
        GearConfig gp = plain.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        GearConfig gb = bush.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;

        Assert.Equal(LandingGear.TireRadiusOf(plain, gp) * 2, LandingGear.TireRadiusOf(bush, gb), 4);

        double pPlain = WheelHydro.TirePressurePsi(plain, gp), pBush = WheelHydro.TirePressurePsi(bush, gb);
        Assert.True(pBush < pPlain * 0.6, $"bushwheels should run much softer ({pBush:0.0} vs {pPlain:0.0} psi)");

        double vPlain = WheelHydro.HydroplaneSpeedMs(pPlain), vBush = WheelHydro.HydroplaneSpeedMs(pBush);
        _o.WriteLine($"Cub planes at {vPlain / 0.514444:0} kt on standard tyres, {vBush / 0.514444:0} kt on bushwheels");
        Assert.True(vBush < vPlain * 0.8, "soft tyres go on top of the water at a lower speed");

        // And the fat tyre drags more once it IS ploughing, because there is more of it in the water.
        Vec3 fPlain = WheelHydro.Force(plain, gp, 0.12, 3000, new Vec3(8, 0, 0), out _, out _);
        Vec3 fBush = WheelHydro.Force(bush, gb, 0.12, 3000, new Vec3(8, 0, 0), out _, out _);
        Assert.True(Math.Abs(fBush.X) > Math.Abs(fPlain.X), "a wider tyre pushes more water");
    }

    [Fact]
    public void ACubSkimsShallowWaterFastAndNosesOverBelowThePlaningSpeed()
    {
        // The real case, and the one the owner described: a hand's depth of water over a gravel bed, which is
        // what the braided rivers are. Above the planing speed the water carries the wheels and they skim
        // clear of the bottom; below it they are in the bed with a wall of water in front of them.
        //
        // The dangerous band is JUST below planing speed, not at a crawl — plough drag goes as V², so at a
        // walking pace it merely stops you. That is also how it goes wrong in reality.
        AircraftConfig cfg = Cub();
        GearConfig main = cfg.Gear.Find(g => !g.IsTailwheel && Math.Abs(g.Pos[1]) > 0.3)!;
        double vp = WheelHydro.HydroplaneSpeedMs(WheelHydro.TirePressurePsi(cfg, main));
        _o.WriteLine($"Cub planes at {vp:0.0} m/s ({vp / 0.514444:0} kt)");

        var fast = Run(vp * 1.3);
        _o.WriteLine($"  {vp * 1.3:0.0} m/s (1.3 V_p): worst pitch {fast.pitch * 180 / Math.PI:+0.0;-0.0}°, "
                     + $"{fast.decel:0.0} m/s lost, wheel off the bed {fast.offBed * 100:0}% of the time");
        Assert.False(fast.flipped, "above the planing speed it should skim, not dig in");
        Assert.True(fast.offBed > 0.5, "the water should be carrying the wheels most of the time");

        var marginal = Run(vp * 0.8);
        _o.WriteLine($"  {vp * 0.8:0.0} m/s (0.8 V_p): worst pitch {marginal.pitch * 180 / Math.PI:+0.0;-0.0}°, "
                     + $"{marginal.decel:0.0} m/s lost, wheel off the bed {marginal.offBed * 100:0}%");
        Assert.True(marginal.flipped || marginal.pitch < -20 * Math.PI / 180,
                    $"below the planing speed the wheels plough and it should go over (worst pitch {marginal.pitch * 180 / Math.PI:0.0}°)");

        var crawl = Run(3.0);
        _o.WriteLine($"  3.0 m/s (a crawl): worst pitch {crawl.pitch * 180 / Math.PI:+0.0;-0.0}°, {crawl.decel:0.0} m/s lost");
        Assert.False(crawl.flipped, "at a walking pace the water just stops you");
    }

    private (double pitch, bool flipped, double decel, double offBed) Run(double speedMs)
    {
        // Flat bed at zero with 0.10 m of water over it.
        WorldTerrain.Active = null;
        FloatHydro.FlatWaterOverride = 0.10;
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig cfg = Cub();
        RigidBodyState rest = LandingGear.RestingState(cfg, 0, 0, 0, 0);
        var state = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(speedMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(cfg, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        double worst = 0;
        bool flipped = false;
        int steps = 0, off = 0;
        for (int i = 0; i < 500; i++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, ControlInputs.Neutral);
            if (double.IsNaN(ac.State.Position.X)) break;
            Quat q = ac.State.Attitude;
            double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
            if (Math.Abs(pitch) > Math.Abs(worst)) worst = pitch;
            if (pitch < -30 * Math.PI / 180) flipped = true;
            steps++;
            if (!LandingGear.AnyMainWheelOnGround(cfg, ac.State)) off++;
            if (ac.State.Velocity.Length < 0.3) break;
        }
        double decel = speedMs - ac.State.Velocity.Length;
        FloatHydro.FlatWaterOverride = null;
        return (worst, flipped, decel, steps > 0 ? (double)off / steps : 0);
    }
}
