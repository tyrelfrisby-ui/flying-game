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
/// Wheel-skis (owner 2026-10-16: a Super Cub, a Beaver and an Aircam that can land on the glacier). The
/// point of a WHEEL-ski is that it does both: the ski carries the aeroplane on snow, and on anything hard
/// the wheel protrudes through and nothing changes. Both halves are checked here, and the glacier landing
/// is flown rather than asserted.
/// </summary>
public class WheelSkiTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public WheelSkiTests(ITestOutputHelper o) { _o = o; }

    public void Dispose()
    {
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = null;
        FloatHydro.FlatWaterOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero;
    }

    /// <summary>
    /// Throttle CLOSED. The lever is read as (1 − lever) / 2, so it is +1 at idle and −1 at full — which makes
    /// <see cref="ControlInputs.Neutral"/> HALF POWER. The first version of these tests used Neutral and so
    /// measured takeoff runs: the Cub accelerated from 8 to 18 m/s and the ski and the wheel came out the same,
    /// because at half throttle neither one is what is deciding anything.
    /// </summary>
    private static readonly ControlInputs Idle = new(0, 0, 0, 1);

    /// <summary>The three types the owner asked for, the Beaver being the LANDPLANE — floats cannot take skis.</summary>
    public const string Cub = "pa18-cub-like", Beaver = "dhc2-beaver-like", Aircam = "aircam-like";

    private static AircraftConfig Load(string id, bool skis)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        c.Skis = skis;
        return c;
    }

    [Fact]
    public void ASkiIsOnlyASkiOnSnow()
    {
        AircraftConfig c = Load(Cub, true);
        Assert.True(LandingGear.OnSki(c, WorldTerrain.Surface.Snow));
        foreach (WorldTerrain.Surface s in new[] { WorldTerrain.Surface.Paved, WorldTerrain.Surface.Gravel,
                                                   WorldTerrain.Surface.Grass, WorldTerrain.Surface.Rough })
            Assert.False(LandingGear.OnSki(c, s), $"a wheel-ski should be a wheel on {s}");
        // And without the option it is never a ski at all.
        Assert.False(LandingGear.OnSki(Load(Cub, false), WorldTerrain.Surface.Snow));
    }

    [Fact]
    public void AFloatCannotBePutOnASki()
    {
        // The option is a session switch that applies to whatever is loaded, so a floatplane gets asked the
        // question too. Its answer has to be no, whatever the menu says.
        AircraftConfig floats = Load("dhc2-beaver-floats-like", true);
        foreach (GearConfig g in floats.Gear)
            Assert.False(LandingGear.OnSki(floats, g, WorldTerrain.Surface.Snow),
                         "a float keel is not a wheel and cannot carry a ski");
        // While the landplane's wheels all can.
        AircraftConfig land = Load(Beaver, true);
        foreach (GearConfig g in land.Gear)
            Assert.True(LandingGear.OnSki(land, g, WorldTerrain.Surface.Snow));
    }

    [Fact]
    public void TheSkiSlidesAndTheWheelDoesNot()
    {
        // The numbers that make a glacier landing different from a gravel one.
        Assert.True(LandingGear.SkiRollingCoefficient < WorldTerrain.RollingCoefficient(WorldTerrain.Surface.Snow) * 0.5,
                    "a ski should roll far more freely than a wheel in the same snow");
        Assert.True(LandingGear.SkiGripScale < 0.5, "and hold direction far less well");
        Assert.True(LandingGear.SkiWheelLoadShare < 0.25, "and leave the brake only a sliver of the load to work on");
    }

    [Theory]
    [InlineData(Cub)]
    [InlineData(Beaver)]
    [InlineData(Aircam)]
    public void OnSnowASkiRunsOnAfterTheWheelHasStopped(string id)
    {
        // Same aeroplane, same snow, same speed, throttle shut: the one on skis goes much further. This is the
        // thing a ski pilot plans his whole arrival around.
        var (wheelRun, wheelStopped, wheelDecel) = RollOutOnSnow(id, skis: false);
        var (skiRun, skiStopped, skiDecel) = RollOutOnSnow(id, skis: true);
        _o.WriteLine($"{id,-26} wheels {wheelRun,6:0} m ({wheelDecel:F2} m/s², stopped {wheelStopped})   " +
                     $"skis {skiRun,6:0} m ({skiDecel:F2} m/s², stopped {skiStopped})");
        Assert.True(skiRun > wheelRun * 1.5, $"{id}: skis ran {skiRun:0} m against {wheelRun:0} m on wheels");
        // And the deceleration is the surface, not a tuning knob: it should land on the coefficients themselves.
        Assert.True(skiDecel < wheelDecel * 0.6, $"{id}: ski decel {skiDecel:F2} vs wheel {wheelDecel:F2} m/s²");
    }

    /// <summary>
    /// Roll out on flat snow from 8 m/s with the throttle shut. 8 m/s is well below flying speed (the Cub's Vso
    /// is 46 kt) so the weight is genuinely on the skis — at 25 m/s the wing is still carrying it and the
    /// surface cannot matter, which is a fine way to prove nothing at all.
    /// </summary>
    private static (double run, bool stopped, double decel) RollOutOnSnow(string id, bool skis)
    {
        WorldTerrain.Active = null;                 // flat ground at zero
        Atmosphere.SteadyWind = Vec3.Zero;
        AircraftConfig c = Load(id, skis);
        // Force the surface the whole world over: this is a snow test, not a terrain test.
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Snow;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0, 0);
        var state = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(8, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        double start = ac.State.Position.X, v0 = ac.State.Velocity.Length;
        int steps = 0;
        bool stopped = false;
        for (; steps < 12000; steps++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            if (ac.State.Velocity.Length < 0.1) { stopped = true; break; }
        }
        double run = ac.State.Position.X - start;
        double t = (steps + 1) * SimLoop.DefaultFixedDtSec;
        double decel = (v0 - ac.State.Velocity.Length) / t;
        WorldTerrain.SurfaceOverride = null;
        return (run, stopped, decel);
    }

    [Fact]
    public void OnPavementTheSkisChangeNothing()
    {
        // The wheel is through the ski: a runway landing is a runway landing.
        WorldTerrain.Active = null;
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Paved;
        double plain = RollOutOnCurrentSurface(Cub, false);
        double skis = RollOutOnCurrentSurface(Cub, true);
        WorldTerrain.SurfaceOverride = null;
        _o.WriteLine($"on pavement: wheels {plain:0.0} m, wheel-skis {skis:0.0} m");
        Assert.Equal(plain, skis, 3);
    }

    private static double RollOutOnCurrentSurface(string id, bool skis)
    {
        AircraftConfig c = Load(id, skis);
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0, 0);
        var state = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(8, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        double start = ac.State.Position.X;
        for (int i = 0; i < 12000 && ac.State.Velocity.Length > 0.1; i++)
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        return ac.State.Position.X - start;
    }

    [Theory]
    [InlineData(Cub)]
    [InlineData(Beaver)]
    [InlineData(Aircam)]
    public void OnSkisTheBrakesAreNearlyUseless(string id)
    {
        // Why a ski pilot lands uphill: standing on the brakes on snow barely shortens the slide.
        WorldTerrain.Active = null;
        Atmosphere.SteadyWind = Vec3.Zero;
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Snow;
        double free = SlideWithBrakes(id, brake: 0);
        double hard = SlideWithBrakes(id, brake: 1);
        WorldTerrain.SurfaceOverride = null;
        _o.WriteLine($"{id,-26} on skis: no brakes {free,6:0} m, full brakes {hard,6:0} m ({100 * (1 - hard / free):0}% shorter)");
        Assert.True(hard > free * 0.55, $"{id}: brakes on snow cut the slide from {free:0} m to {hard:0} m — too effective for a ski");
        Assert.True(hard < free, "the brakes should still do something");
    }

    private static double SlideWithBrakes(string id, double brake)
    {
        AircraftConfig c = Load(id, skis: true);
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0, 0);
        var state = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(8, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        double start = ac.State.Position.X;
        for (int i = 0; i < 12000 && ac.State.Velocity.Length > 0.1; i++)
        {
            ac.BrakeInput = brake;
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
        }
        return ac.State.Position.X - start;
    }

    [Fact]
    public void ACubOnSkisCanBePutOnTheKnikAndStop()
    {
        // The whole feature, flown on the real glacier: touch down on the tongue going UP the ice, which is
        // how it is done, and come to a stop on the slope without sliding back down it.
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AlaskaRange.Glacier knik = Array.Find(AlaskaRange.Glaciers, g => g.Name == "Knik");

        // A point on the lower tongue, and the uphill direction along the ice.
        double dx = (knik.TermX - knik.HeadX) / knik.LengthM, dy = (knik.TermY - knik.HeadY) / knik.LengthM;
        double s0 = 0.86;
        double px = knik.HeadX + dx * knik.LengthM * s0, py = knik.HeadY + dy * knik.LengthM * s0;
        double uphill = Math.Atan2(-dy, -dx);                   // back up the glacier
        Assert.Equal(WorldTerrain.Surface.Snow, WorldTerrain.SurfaceAt(px, py));

        AircraftConfig c = Load(Cub, true);
        double ground = t.HeightAt(px, py);
        RigidBodyState rest = LandingGear.RestingState(c, px, py, ground, uphill);
        var state = new RigidBodyState(rest.Position, rest.Attitude,
                                       new Vec3(14, 0, 0), Vec3.Zero);      // 27 kt — rolling, not flying
        var ac = new Aircraft(c, state, new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(ac);
        Vec3 start = ac.State.Position;
        double slowest = 99;
        int steps = 0;
        for (; steps < 8000; steps++)
        {
            loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            slowest = Math.Min(slowest, ac.State.Velocity.Length);
            if (double.IsNaN(ac.State.Position.X)) break;
            if (ac.State.Velocity.Length < 0.3) break;
        }
        double run = Math.Sqrt((ac.State.Position.X - start.X) * (ac.State.Position.X - start.X)
                             + (ac.State.Position.Y - start.Y) * (ac.State.Position.Y - start.Y));
        double climb = -(ac.State.Position.Z - start.Z);
        _o.WriteLine($"a Cub on skis, landed uphill on the Knik at 27 kt: ran {run:0} m in {steps * SimLoop.DefaultFixedDtSec:0.0} s, " +
                     $"climbed {climb:0.0} m, slowest {slowest:0.00} m/s");
        Assert.False(double.IsNaN(run), "the landing went to pieces");
        Assert.True(run < 400, $"it ran {run:0} m up the glacier — that is not a landing, that is an arrival");
        Assert.True(climb > 2, "landing uphill should gain height, which is what stops you");
        Assert.True(slowest < 0.4, $"it never stopped (slowest {slowest:0.0} m/s)");
    }

    [Fact]
    public void TheSameCubOnWheelsSlidesMuchFurtherUpTheKnik()
    {
        // The comparison that says the ski is worth fitting: a WHEEL on the same ice digs in and stops sooner,
        // but it also cannot be steered or held — and on the flat (the test above) it is the ski that runs on.
        // Here, uphill, the wheel's drag helps; the point is only that the two are genuinely different.
        var t = new WorldTerrain();
        WorldTerrain.Active = t;
        Atmosphere.SteadyWind = Vec3.Zero;
        AlaskaRange.Glacier knik = Array.Find(AlaskaRange.Glaciers, g => g.Name == "Knik");
        double dx = (knik.TermX - knik.HeadX) / knik.LengthM, dy = (knik.TermY - knik.HeadY) / knik.LengthM;
        double px = knik.HeadX + dx * knik.LengthM * 0.86, py = knik.HeadY + dy * knik.LengthM * 0.86;
        double uphill = Math.Atan2(-dy, -dx);

        double Run(bool skis)
        {
            AircraftConfig c = Load(Cub, skis);
            RigidBodyState rest = LandingGear.RestingState(c, px, py, t.HeightAt(px, py), uphill);
            var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(14, 0, 0), Vec3.Zero),
                                  new ControlDeflections(0, 0, 0, 0));
            var loop = new SimLoop(ac);
            Vec3 s = ac.State.Position;
            for (int i = 0; i < 8000 && ac.State.Velocity.Length > 0.3; i++)
                loop.RunFor(SimLoop.DefaultFixedDtSec, Idle);
            return Math.Sqrt((ac.State.Position.X - s.X) * (ac.State.Position.X - s.X)
                           + (ac.State.Position.Y - s.Y) * (ac.State.Position.Y - s.Y));
        }

        double ski = Run(true), wheel = Run(false);
        _o.WriteLine($"uphill on the Knik at 27 kt: on skis {ski:0} m, on wheels {wheel:0} m");
        Assert.True(ski > wheel * 1.2, $"the ski should slide further even uphill (ski {ski:0} m, wheel {wheel:0} m)");
    }
}
