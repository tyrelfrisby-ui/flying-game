using System;
using System.IO;
using System.Linq;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The surface tracks' contact events (surface-tracks spec PART A; Ty 2026-11-04: "anything contacting a surface leaves
/// tracks", and 09:48 "tracks are a teaching tool"). Each step the aeroplane reports what touched the surface: where,
/// how wide, how deep, how hard, how it slid, what it was and how wet the snow is. These check the values against
/// the physics that made them and against hand-copied numbers.
/// </summary>
[Collection("WorldTerrainActive")]
public class SurfaceContactTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public SurfaceContactTests(ITestOutputHelper o)
    {
        _o = o; SimGlobals.Calm(); SurfaceContacts.SnowLiquidWaterPercent = null;
        Atmosphere.SimTimeSec = 0;
    }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; SurfaceContacts.SnowLiquidWaterPercent = null; }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    // ThrottleLever 1 is idle (Aircraft: throttle = (1 − lever) / 2).

    /// <summary>Put it down at rest on flat ground of this surface and let it settle; the last step's contacts.</summary>
    private static Aircraft Settle(AircraftConfig c, WorldTerrain.Surface surface, double seconds = 3.0, double speedMs = 0.0)
    {
        WorldTerrain.SurfaceOverride = surface;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        var s = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(speedMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, s, ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        sim.RunFor(seconds, new ControlInputs(0, 0, 0, 1.0));
        return ac;
    }

    private static SurfaceContact[] All(SurfaceContactBuffer b)
    {
        var a = new SurfaceContact[b.Count];
        for (int i = 0; i < b.Count; i++) a[i] = b[i];
        return a;
    }

    [Fact]
    public void ACubAtRestOnGrassReportsThreeTyresCarryingItsWeight()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Aircraft ac = Settle(c, WorldTerrain.Surface.Grass);
        SurfaceContact[] k = All(ac.SurfaceContacts);
        foreach (SurfaceContact x in k) _o.WriteLine(x.ToString());
        Assert.Equal(0, ac.SurfaceContacts.Dropped);
        Assert.Equal(3, k.Length);
        Assert.All(k, x => Assert.Equal(SurfaceContactType.Tire, x.Type));
        Assert.All(k, x => Assert.Equal(GroundMaterial.Grass, x.Material));
        Assert.All(k, x => Assert.Equal(0.0, x.SnowWetness01));
        double weight = ac.MassProperties.MassKg * 9.81, load = k.Sum(x => x.LoadN);
        Assert.InRange(load, 0.95 * weight, 1.05 * weight);
        // The mains' footprint is the tyre's own at its deflection, as WheelState holds it, and a real width: an
        // 8.00-6 is 8 in (0.20 m) across its section, and its patch is narrower than that.
        foreach (SurfaceContact x in k)
        {
            WheelState w = ac.Wheels[x.Source];
            if (w.Tire != null) Assert.Equal(w.FootprintWidthM, x.WidthM);
            else Assert.Equal(TireCatalog.Of(c.Gear[x.Source])!.SectionWidthM, x.WidthM);   // a rigid legacy tyre: its section
            Assert.Equal(w.PatchWorld, x.PositionWorld);
            Assert.True(Math.Abs(x.SurfaceUpWorld.Z + 1) < 1e-9, "level ground: the normal is world-up");
            Assert.True(x.SlipWorld.Length < 0.05, $"at rest nothing slides: {x.SlipWorld.Length:F3} m/s");
        }
        SurfaceContact main = k.First(x => !c.Gear[x.Source].IsTailwheel);
        Assert.InRange(main.WidthM, 0.05, 0.20);
        Assert.InRange(main.LengthM, 0.05, 0.30);
        // A rut on grass: millimetres to a couple of centimetres for a light tyre (more on sand and snow, below).
        Assert.InRange(main.DepthM, 0.001, 0.03);
    }

    [Fact]
    public void TheRutFollowsTheGroundAndIsNoneOnPavementOrIce()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        double Rut(WorldTerrain.Surface s)
        {
            SurfaceContact[] k = All(Settle(Cfg("j3-cub-like"), s).SurfaceContacts);
            SurfaceContact m = k.First(x => !c.Gear[x.Source].IsTailwheel);
            _o.WriteLine($"{s,-18} {m}");
            return m.DepthM;
        }
        double paved = Rut(WorldTerrain.Surface.Paved), ice = Rut(WorldTerrain.Surface.GlacierIce);
        double gravel = Rut(WorldTerrain.Surface.Gravel), dirt = Rut(WorldTerrain.Surface.Dirt), grass = Rut(WorldTerrain.Surface.Grass);
        double sand = Rut(WorldTerrain.Surface.Sand), snow = Rut(WorldTerrain.Surface.Snow);
        Assert.Equal(0.0, paved);
        Assert.Equal(0.0, ice);
        // Every soft ground gives: a few millimetres on the firm ones, sand more, settled snow most. (Among gravel, dirt and
        // grass the order is the rolling model's — its pressure exponent 1/n — and a soft Cub tyre puts gravel first.)
        string all = $"gravel {gravel:F4} dirt {dirt:F4} grass {grass:F4} sand {sand:F4} snow {snow:F4}";
        foreach (double firm in new[] { gravel, dirt, grass }) Assert.True(firm > 0.001 && firm < 0.01 && sand > firm, all);
        Assert.True(snow > sand, all);
        Assert.InRange(sand, 0.005, 0.03);
        Assert.InRange(snow, 0.02, 0.15);
    }

    [Fact]
    public void GroundSinkageInvertsBekkersRollingRelation()
    {
        // R/W = z/((n+1)·l): a 6.00-6 with a ground share of 0.05 over a 0.15 m patch on dirt (n = 0.7) sinks
        // 0.05 × 1.7 × 0.15 = 12.75 mm. Checked against the tyre's own footprint and hysteresis here.
        TireSpec t = TireCatalog.Get("6.00-6")!;
        double delta = TireModel.StaticDeflectionM(t, 30, 4000);
        double l = TireModel.FootprintLengthM(t, delta), hyst = TireModel.HysteresisCoefficient(t, delta);
        double z = TireModel.GroundSinkageM(t, hyst + 0.05, WorldTerrain.Surface.Dirt, delta);
        Assert.Equal(0.05 * 1.7 * l, z, 12);
        Assert.Equal(0.0, TireModel.GroundSinkageM(t, hyst, WorldTerrain.Surface.Dirt, delta));
        Assert.Equal(0.0, TireModel.GroundSinkageM(null, 0.2, WorldTerrain.Surface.Dirt, delta));
        // The reference tyre is the table's reference: its hysteresis is pavement's 0.025.
        Assert.Equal(0.025, hyst, 9);
    }

    /// <summary>
    /// A GRAZING TOUCH DOES NOT REPORT A TRENCH, and the deepest rut of a landing is at its heaviest load.
    ///
    /// <see cref="TireModel.GroundSinkageM(TireSpec, double, WorldTerrain.Surface, double)"/> inverts Bekker's
    /// relation, and the footprint length cancels out of it exactly, so the rut is a function of the GROUND PRESSURE
    /// alone. Taking that pressure as the leg's whole normal load over the carcass's own footprint is not one tyre's
    /// two numbers, and at the onset of a touchdown they part company: a J-3 put down on grass at 14 m/s was measured
    /// reporting a 558 mm rut on a step carrying 281 N, because the footprint on that step was 10 x 13 mm and the
    /// pressure therefore read 2,806 kPa — 25x what the same tyre reads standing.
    ///
    /// <see cref="TireModel.GroundRutM"/> evaluates the same relation at the pressure the CARCASS is transmitting
    /// (<see cref="TireModel.CarcassGroundPressurePa"/>), which is bounded by the tyre's inflation pressure because
    /// that is what a pneumatic tyre's mean contact pressure IS. Nothing the aeroplane feels changes: the rolling
    /// resistance it is charged for still comes from the whole load.
    /// </summary>
    [Fact]
    public void AGrazingTouchDoesNotPressATrenchAndTheRutGrowsWithTheLoad()
    {
        TireSpec t = TireCatalog.Get("8.00-6")!;
        const double psi = 18;

        // The carcass's own mean pressure cannot run away: eq. 24 over eq. 12 tends to 0.42 (p + 0.08 p_r) as the
        // deflection goes to nothing and to 1.04 of it when the tyre is deep. The classical "a tyre's ground pressure
        // is its inflation pressure", and it is what stops the grazing touch.
        double inflation = (psi + TireModel.CarcassPressureFraction * t.MaxPsi) * TireModel.Psi;
        double bottomed = TireModel.BottomDeflectionM(t);
        foreach (double delta in new[] { 0.0005, 0.001, 0.005, 0.02, 0.05, 0.10, 0.99 * bottomed })
            Assert.InRange(TireModel.CarcassGroundPressurePa(t, psi, delta) / inflation, 0.40, 1.10);
        // Past bottoming the rim is on the ground through two folds of sidewall and the pressure leaves that band for
        // real: that is not this relation's business any more, and it is why AircraftImprints bounds a tyre's rut by
        // its own rolling radius.
        Assert.True(TireModel.CarcassGroundPressurePa(t, psi, 1.3 * bottomed) > 1.10 * inflation);
        // Under about a tenth of a millimetre of deflection GroundPressurePa's 1 cm2 area floor takes over and the
        // ratio falls away below that band. That is the safe direction - it UNDER-reads the pressure, so a tyre that
        // has barely touched presses less rather than more - and it is why nothing here needs a floor of its own.
        foreach (double delta in new[] { 1e-6, 1e-5, 1e-4 })
            Assert.InRange(TireModel.CarcassGroundPressurePa(t, psi, delta) / inflation, 0.0, 0.45);

        // A grazing touch: the leg is holding a real load on a carcass that has barely squashed. The OLD path reads a
        // trench; the carcass path reads next to nothing, which is what a tyre that has barely touched has done.
        double graze = 1e-4;                                    // 0.1 mm of carcass
        double legLoadN = 281;                                  // the load on the measured step
        double was = TireModel.GroundSinkageM(t, TireModel.RollingCoefficient(t, psi, WorldTerrain.Surface.Grass, legLoadN, graze),
                                              WorldTerrain.Surface.Grass, graze);
        double now = TireModel.GroundRutM(t, psi, WorldTerrain.Surface.Grass, graze);
        Assert.True(was > 0.3, $"the measurement this fixes: the old path read {1000 * was:F0} mm");
        Assert.True(now < 0.002, $"a graze presses {1000 * now:F2} mm");

        // And the rut GROWS WITH THE LOAD, monotonically, at the deflection that load actually makes — which is the
        // thing a steady-state relation is for. A Cub main's static load is about 1.8 kN.
        double last = -1;
        foreach (double loadN in new[] { 500.0, 1000.0, 1800.0, 4000.0, 8000.0, 16000.0, 22000.0 })
        {
            double delta = TireModel.StaticDeflectionM(t, psi, loadN);
            double z = TireModel.GroundRutM(t, psi, WorldTerrain.Surface.Grass, delta);
            Assert.True(z >= last, $"{loadN} N read {1000 * z:F2} mm after {1000 * last:F2} mm");
            last = z;
        }
        // The whole of a Cub's landing, from standing to a 22 kN three-point arrival, stays in Ty's "a few cm".
        Assert.InRange(last, 0.003, 0.05);
        Assert.Equal(0.0, TireModel.GroundRutM(null, psi, WorldTerrain.Surface.Grass, 0.02));
    }

    [Fact]
    public void ALockedWheelSlidesItsWholeSpeedAndARollingOneDoesNot()
    {
        AircraftConfig c = Cfg("c172-like");
        Aircraft ac = Settle(c, WorldTerrain.Surface.Paved, seconds: 1.0, speedMs: 12.0);
        SurfaceContact[] rolling = All(ac.SurfaceContacts);
        foreach (SurfaceContact x in rolling) _o.WriteLine("rolling " + x);
        Assert.NotEmpty(rolling);
        Assert.All(rolling, x => Assert.True(x.SlipWorld.Length < 0.5, $"a free wheel rolls: slip {x.SlipWorld.Length:F2} m/s"));
        Assert.All(rolling, x => Assert.True(x.GroundVelocityWorld.Length > 8, "the patch moves over the ground"));
        Assert.All(rolling, x => Assert.True(Vec3.Dot(x.HeadingWorld, x.GroundVelocityWorld) > 0.95 * x.GroundVelocityWorld.Length, "rolling along its heading"));

        ac.BrakeInput = 1.0;
        var sim = new SimLoop(ac);
        bool sawLock = false;
        for (int i = 0; i < 40 && !sawLock; i++)
        {
            sim.RunFor(0.025, new ControlInputs(0, 0, 0, 1.0));
            foreach (SurfaceContact x in All(ac.SurfaceContacts))
            {
                if (!x.Locked) continue;
                sawLock = true;
                _o.WriteLine("locked " + x);
                // The rubber stands still on the wheel, so it slides at the patch's whole ground speed: the skid mark.
                Assert.Equal(x.GroundVelocityWorld.Length, x.SlipWorld.Length, 1);
                Assert.True(x.SlipPowerW > 1000, $"a skid's friction power: {x.SlipPowerW:F0} W");
                Assert.Equal(GroundMaterial.Asphalt, x.Material);
            }
        }
        Assert.True(sawLock, "full brake on a light load at 12 m/s locks a main");
    }

    [Fact]
    public void AWheelSkiOnSnowReportsASkiAndTheSnowWetness()
    {
        AircraftConfig c = Cfg("pa18-cub-like");
        c.Skis = true;
        Aircraft ac = Settle(c, WorldTerrain.Surface.Snow);
        SurfaceContact[] k = All(ac.SurfaceContacts);
        foreach (SurfaceContact x in k) _o.WriteLine(x.ToString());
        SurfaceContact[] skis = k.Where(x => x.Type == SurfaceContactType.Ski).ToArray();
        Assert.True(skis.Length >= 2, "both mains on their skis");
        // UPDATED 2026-11-10 (lane/claude-imprints). This asserted the ski model's 0.25 x 1.8 m DEFAULTS, because when
        // it was written on 2026-11-06 a wheel-ski had no geometry in the physics at all. It does now
        // (SkiGear.Geometry, built from the airframe's own dimensions; lane/claude-quest-islandnav merged the ski work
        // on 2026-11-10), and LandingGear records the bearing ski's real running surface, so the contact reports the
        // ski this aeroplane actually carries. A Super Cub's comes out 0.289 x 1.980 m.
        // PER LEG, because the skis are not all the same: a Super Cub's mains come out 0.289 x 1.980 m and the
        // TAILWHEEL's ski 0.173 x 1.188 m, each sized from the airframe and from the tyre it seats.
        SkiGear.Geometry main = SkiGear.For(c, c.Gear.First(g => SkiGear.LegTakesSki(g) && !g.IsTailwheel));
        Assert.True(main.WidthM > SurfaceContacts.DefaultSkiWidthM, $"the real ski is not the default: {main.WidthM:F3} m");
        foreach (SurfaceContact x in skis)
        {
            SkiGear.Geometry geo = SkiGear.For(c, c.Gear[x.Source]);
            Assert.Equal(geo.WidthM, x.WidthM, 9);
            Assert.Equal(geo.LengthM, x.LengthM, 9);
            Assert.Equal(x.LoadN / (geo.WidthM * geo.LengthM), x.PressurePa, 6);
        }
        Assert.True(skis.Any(x => Math.Abs(x.WidthM - main.WidthM) < 1e-9), "the mains report the main ski");
        Assert.All(skis, x => Assert.Equal(GroundMaterial.Snow, x.Material));
        // No snow model on this tip: nothing makes snow wet, so it reads dry.
        Assert.All(skis, x => Assert.Equal(0.0, x.SnowWetness01));

        // With one (the ski lane's SnowConditions), the wetness follows its liquid water: 8 % (its wet spring snow)
        // is 8/15 of soaking.
        double calls = 0;
        SurfaceContacts.SnowLiquidWaterPercent = (s, at, up) => { calls++; return 8.0; };
        new SimLoop(ac).RunFor(0.1, new ControlInputs(0, 0, 0, 1.0));
        SurfaceContact ski = All(ac.SurfaceContacts).First(x => x.Type == SurfaceContactType.Ski);
        Assert.Equal(8.0 / 15.0, ski.SnowWetness01, 12);
        Assert.True(calls > 0);
    }

    [Fact]
    public void WetnessIsTheIcssgLiquidWaterScale()
    {
        Assert.Equal(0.0, SurfaceContacts.WetnessFromLiquidWater(0));
        Assert.Equal(0.2, SurfaceContacts.WetnessFromLiquidWater(3), 12);     // moist / wet boundary
        Assert.Equal(8.0 / 15.0, SurfaceContacts.WetnessFromLiquidWater(8), 12);
        Assert.Equal(1.0, SurfaceContacts.WetnessFromLiquidWater(15));
        Assert.Equal(1.0, SurfaceContacts.WetnessFromLiquidWater(40));
        Assert.Equal(0.0, SurfaceContacts.WetnessFromLiquidWater(-2));
        Assert.Equal(0.0, SurfaceContacts.WetnessFromLiquidWater(double.NaN));
    }

    [Fact]
    public void TheWetnessHookIsAskedOnlyAboutSnow()
    {
        int calls = 0;
        SurfaceContacts.SnowLiquidWaterPercent = (s, at, up) => { calls++; return 10.0; };
        Aircraft ac = Settle(Cfg("j3-cub-like"), WorldTerrain.Surface.Dirt, seconds: 0.5);
        Assert.Equal(0, calls);
        Assert.All(All(ac.SurfaceContacts), x => Assert.Equal(0.0, x.SnowWetness01));
    }

    [Fact]
    public void FloatsOnTheWaterReportTheirBeamDraftAndTheWatersPush()
    {
        FloatHydro.FlatWaterOverride = 0.0;
        try
        {
            AircraftConfig c = Cfg("pa18-floats-like");
            var state = new RigidBodyState(new Vec3(0, 0, -(c.Floats!.KeelZ - 0.25)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero);
            var ac = new Aircraft(c, state, ControlDeflections.Neutral);
            new SimLoop(ac).RunFor(30, new ControlInputs(0, 0, 0, 1.0));
            SurfaceContact[] k = All(ac.SurfaceContacts);
            foreach (SurfaceContact x in k) _o.WriteLine(x.ToString());
            SurfaceContact[] fl = k.Where(x => x.Type == SurfaceContactType.Float).ToArray();
            Assert.Equal(2, fl.Length);
            Assert.All(fl, x => Assert.Equal(GroundMaterial.Water, x.Material));
            Assert.All(fl, x => Assert.Equal(FloatHull.For(c.Floats).Beam, x.WidthM));
            Assert.All(fl, x => Assert.InRange(x.DepthM, 0.12, 0.5));   // FloatTests' sane draft at rest
            Assert.Equal(-1, Math.Sign(fl[0].PositionWorld.Y - fl[1].PositionWorld.Y));   // left, then right
            double weight = ac.MassProperties.MassKg * 9.81;
            Assert.InRange(fl.Sum(x => x.LoadN), 0.9 * weight, 1.1 * weight);
            Assert.All(fl, x => Assert.True(Math.Abs(x.HeadingWorld.X) > 0.99, "keel along the heading"));
            Assert.All(fl, x => Assert.True(x.GroundVelocityWorld.Length < 0.5, "at rest on the water"));
            Assert.Equal(0, ac.SurfaceContacts.Dropped);
        }
        finally { FloatHydro.FlatWaterOverride = null; }
    }

    [Fact]
    public void FloatsOnSnowLeaveAFloatMarkOnTheSnow()
    {
        AircraftConfig c = Cfg("pa18-floats-like");
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Snow;
        double keel = 0; foreach (GearConfig g in c.Gear) keel = Math.Max(keel, g.Pos[2]);   // FloatsOnLandTests' start
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -(keel - 0.02)), new Quat(0, 0, 0, 1), new Vec3(8, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        new SimLoop(ac).RunFor(1.0, new ControlInputs(0, 0, 0, 1.0));
        SurfaceContact[] k = All(ac.SurfaceContacts);
        foreach (SurfaceContact x in k) _o.WriteLine(x.ToString());
        Assert.NotEmpty(k);
        Assert.All(k, x => Assert.Equal(SurfaceContactType.Float, x.Type));
        Assert.All(k, x => Assert.Equal(GroundMaterial.Snow, x.Material));
        Assert.All(k, x => Assert.Equal(FloatHull.For(c.Floats!).Beam, x.WidthM));
        Assert.All(k, x => Assert.True(x.SlipWorld.Length > 0.5 && x.SlipWorld.X > 0, "a keel slides the way it goes"));
    }

    [Fact]
    public void AGlidersSkidIsASkid()
    {
        AircraftConfig c = Cfg("glider-2-33-like");
        // Nose down on the skid: the resting attitude is on the main wheel and whatever it sits on.
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
        WheelState[] wheels = WheelState.For(c);
        var buf = new SurfaceContactBuffer(c);
        for (int i = 0; i < c.Gear.Count; i++)
        {
            GearConfig g = c.Gear[i];
            if (WheelSpin.IsWheel(g)) continue;
            WheelState w = wheels[i];
            w.InContact = true; w.LoadN = 500; w.Material = GroundMaterial.Grass; w.Surface = WorldTerrain.Surface.Grass;
            w.GroundUpWorld = new Vec3(0, 0, -1); w.HeadingWorld = new Vec3(1, 0, 0); w.GroundVelocityWorld = new Vec3(3, 0, 0);
        }
        SurfaceContacts.Collect(c, LandingGear.RestingState(c, 0, 0, 0), wheels, false, buf, 0);
        SurfaceContact[] k = All(buf);
        Assert.Equal(c.Gear.Count(g => !WheelSpin.IsWheel(g)), k.Length);
        Assert.All(k, x => Assert.Equal(SurfaceContactType.Skid, x.Type));
        Assert.All(k, x => Assert.Equal(SurfaceContacts.SkidShoeWidthM, x.WidthM));
        Assert.All(k, x => Assert.Equal(new Vec3(3, 0, 0), x.SlipWorld));   // a skid always slides
    }

    [Fact]
    public void TheSkiHookReportsWhatTheSkiModelGives()
    {
        AircraftConfig c = Cfg("pa18-cub-like");
        var buf = new SurfaceContactBuffer(c);
        SurfaceContacts.SnowLiquidWaterPercent = (s, at, up) => 3.0;
        // A ski 0.30 × 2.0 m, 4 cm into the snow under 4 kN, sliding at 10 m/s and 1 m/s sideways on a slope.
        Vec3 up = new Vec3(0.1, 0, -1) / Math.Sqrt(1.01);
        Assert.True(SurfaceContacts.AddSki(buf, 1, new Vec3(5, 6, -7), up, new Vec3(1, 0, 0), new Vec3(10, 1, 0.5),
            0.30, 2.0, 0.04, 4000, WorldTerrain.Surface.Snow, 12.5));
        Assert.False(SurfaceContacts.AddSki(buf, 2, Vec3.Zero, up, new Vec3(1, 0, 0), Vec3.Zero, 0.3, 2, 0, 0, WorldTerrain.Surface.Snow, 0));
        SurfaceContact k = buf[0];
        Assert.Equal(1, buf.Count);
        Assert.Equal(SurfaceContactType.Ski, k.Type);
        Assert.Equal(1, k.Source);
        Assert.Equal(0.30, k.WidthM); Assert.Equal(2.0, k.LengthM); Assert.Equal(0.04, k.DepthM);
        Assert.Equal(4000 / 0.6, k.PressurePa, 9);
        Assert.Equal(0.2, k.SnowWetness01, 12);
        Assert.Equal(GroundMaterial.Snow, k.Material);
        Assert.Equal(12.5, k.TimeSec);
        Assert.Equal(0.0, Vec3.Dot(k.GroundVelocityWorld, up), 12);   // in the slope's plane
        Assert.Equal(0.0, Vec3.Dot(k.HeadingWorld, up), 12);
        Assert.Equal(1.0, k.HeadingWorld.Length, 12);
    }

    [Fact]
    public void CollectingAllocatesNothing()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        Aircraft ac = Settle(c, WorldTerrain.Surface.Snow, seconds: 0.5);
        SurfaceContacts.SnowLiquidWaterPercent = (s, at, up) => 5.0;
        var wheels = ac.Wheels;
        SurfaceContactBuffer buf = ac.SurfaceContacts;
        RigidBodyState st = ac.State;
        SurfaceContacts.Collect(c, st, wheels, false, buf, 0);   // warm (JIT)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) SurfaceContacts.Collect(c, st, wheels, false, buf, i);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3, buf.Count);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void EveryStepIsReported()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Grass;
        var ac = new Aircraft(c, LandingGear.RestingState(c, 0, 0, 0), ControlDeflections.Neutral);
        int steps = 0; long last = 0;
        ac.SurfaceContactsStepped += b => { steps++; Assert.Equal(last + 1, b.Step); last = b.Step; };
        for (int i = 0; i < 50; i++) ac.Step(new ControlInputs(0, 0, 0, 1.0), 0.005);
        Assert.Equal(50, steps);
    }
}
