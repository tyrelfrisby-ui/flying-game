using System;
using System.Collections.Generic;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A DEPARTED PIECE HITTING THE AEROPLANE IT CAME OFF. Ty 2026-11-11: "The engine and prop while separating should
/// follow an accurate flight path and if that path contacts the airframe it should damage it appropriately" and
/// "Yes thrown blade also follows physics and causes damage".
///
/// What these pin is that the test uses the REAL per-component boxes, that the sweep is what finds a fast piece, and
/// that the relative motion carries a departing engine AFT down the fuselage rather than letting it fall clear.
/// </summary>
public class WreckStrikeTests
{
    private readonly ITestOutputHelper _o;
    public WreckStrikeTests(ITestOutputHelper o) { _o = o; }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>A blade-shaped piece, as mass and reach: one box, 1 m long, a few kg. WreckStrike.Test wants the
    /// state, the mass and the reach and nothing else, which is why no WreckSim is needed to own it here.</summary>
    private static (double Mass, double Reach) Blade(double massKg = 5.7, double lengthM = 1.0)
        => (massKg, 0.5 * lengthM);

    private static RigidBodyState At(Vec3 worldPos, Vec3 worldVel)
        => new(worldPos, Quat.Identity, worldVel, Vec3.Zero);

    // ---- the slab test itself ------------------------------------------------------------------------------------

    [Fact]
    public void SegmentBoxFindsTheEnteringFace()
    {
        // Straight through a unit box from −x: enters the −x face.
        Assert.True(WreckStrike.SegmentBox(new Vec3(-5, 0, 0), new Vec3(5, 0, 0), Vec3.Zero, new Vec3(1, 1, 1),
                                           out double t, out Vec3 n));
        Assert.Equal(0.4, t, 9);                       // −1 is 4/10 of the way from −5 to +5
        Assert.Equal(-1.0, n.X, 9);
        Assert.Equal(0.0, n.Y, 9);

        // Past it, and parallel beside it: no hit either way.
        Assert.False(WreckStrike.SegmentBox(new Vec3(-5, 3, 0), new Vec3(5, 3, 0), Vec3.Zero, new Vec3(1, 1, 1), out _, out _));
        Assert.False(WreckStrike.SegmentBox(new Vec3(-5, 0, 0), new Vec3(-2, 0, 0), Vec3.Zero, new Vec3(1, 1, 1), out _, out _));
    }

    // ---- the sweep is the point ----------------------------------------------------------------------------------

    /// <summary>
    /// A BLADE AT RIM SPEED CROSSES A WHOLE AEROPLANE IN A FEW STEPS, which is why the test has to be swept and not
    /// a pair of end-point checks. Measured here: the blade's relative displacement in one 200 Hz step against the
    /// size of the box it is passing through.
    /// </summary>
    [Fact]
    public void TheSweepCatchesWhatEndPointsWouldMiss()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        AirframeHydroModel hm = AirframeHydroModel.Build(c);
        Vec3 cg = c.Mass.CgVec();
        double dt = 1.0 / 200.0, speed = 130.0;        // the Pitts' blade tip speed at the red line, near enough
        double step = speed * dt;

        // THE THINNEST BOX IS THE ONE THAT PROVES IT. A departed blade travels 0.65 m in a 200 Hz step, and the
        // aeroplane has control surfaces a few centimetres thick: sampled only at the step's end points the blade is
        // on one side at the start and past it at the end, with nothing in between to notice.
        HydroElement thin = hm.Elements[0];
        foreach (HydroElement e in hm.Elements)
            if (Math.Min(e.Half.Y, e.Half.Z) < Math.Min(thin.Half.Y, thin.Half.Z)) thin = e;
        double thickness = 2.0 * Math.Min(thin.Half.Y, thin.Half.Z);
        _o.WriteLine($"a blade at {speed:F0} m/s moves {step:F2} m in one 200 Hz step; " +
                     $"the thinnest airframe box ({thin.Atom}) is {thickness:F3} m through");
        Assert.True(step > thickness, "the step jumps clean over the thinnest surface: end points alone would miss it");

        // Fly it across the cabin, which is the big target, and check the sweep reports the crossing with its energy.
        HydroElement cabin = null!;
        foreach (HydroElement e in hm.Elements) if (e.Atom == WreckAtom.Cabin) { cabin = e; break; }
        Assert.NotNull(cabin);
        var ac = new RigidBodyState(Vec3.Zero, Quat.Identity, Vec3.Zero, Vec3.Zero);
        (double mass, double reach) = Blade();
        Vec3 start = new Vec3(cabin.Centre.X, cabin.Centre.Y - (cabin.Half.Y + reach + 0.5 * step), cabin.Centre.Z) - cg;
        WreckStrike.Hit hit = WreckStrike.Test(At(start, new Vec3(0, speed, 0)), mass, reach, ac, cg, hm.Elements, dt);
        _o.WriteLine($"swept hit: {hit.Atom}, closing {hit.ClosingMs:F1} m/s, {hit.EnergyJ / 1000.0:F1} kJ at t={hit.T:F3}");
        Assert.True(hit.Any, "the swept test has to find it");
        Assert.True(hit.EnergyJ > 20000.0, $"a 5.7 kg blade at 130 m/s is tens of kJ ({hit.EnergyJ / 1000.0:F1} kJ)");
    }

    /// <summary>Nothing in front of it, nothing hit: a piece going the other way must not report a strike.</summary>
    [Fact]
    public void APieceLeavingReportsNothing()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        AirframeHydroModel hm = AirframeHydroModel.Build(c);
        var ac = new RigidBodyState(Vec3.Zero, Quat.Identity, Vec3.Zero, Vec3.Zero);
        (double mass, double reach) = Blade();
        var far = At(new Vec3(0, 0, -50), new Vec3(0, 0, -100));     // 50 m above and climbing away
        Assert.False(WreckStrike.Test(far, mass, reach, ac, c.Mass.CgVec(), hm.Elements, 1.0 / 200.0).Any);
    }

    // ---- the geometry decides what is struck ----------------------------------------------------------------------

    /// <summary>
    /// THE ENGINE DOES NOT FALL CLEAR. It leaves the nose while the aeroplane is still flying forward at speed, so
    /// in the AEROPLANE's frame it tracks aft down the fuselage. Released with no relative velocity at all, the only
    /// motion is the aeroplane's own, and what it meets has to come out of the geometry rather than be chosen.
    ///
    /// Flown here as the aeroplane continuing at 50 m/s while the engine coasts: the engine goes aft past the nose
    /// and into something. Which thing is the model's business, and it is printed.
    /// </summary>
    [Fact]
    public void ADepartedEngineTracksAftAlongTheFuselage()
    {
        AircraftConfig c = Load("j3-cub-like");
        AirframeHydroModel hm = AirframeHydroModel.Build(c);
        Vec3 cg = c.Mass.CgVec();
        double v = 50.0, dt = 1.0 / 200.0;

        // The aeroplane flying level at 50 m/s; the engine let go at the nose station with the aeroplane's velocity
        // and then no thrust of its own — so relative to the aeroplane it slows, which IS going aft.
        var ac = new RigidBodyState(Vec3.Zero, Quat.Identity, new Vec3(v, 0, 0), Vec3.Zero);
        double noseX = 0.0;
        foreach (HydroElement e in hm.Elements) if (e.Atom == WreckAtom.Nose) noseX = Math.Max(noseX, e.Centre.X + e.Half.X);

        (double mass, double reach) = Blade(80.0, 0.6);  // an engine-sized lump, 80 kg
        var hits = new List<WreckAtom>();
        // Start it at the nose, moving 12 m/s slower than the aeroplane (it has lost its thrust and is decelerating).
        RigidBodyState engine = At(new Vec3(noseX - cg.X, 0, 0.2), new Vec3(v - 12.0, 0, 0));
        for (int i = 0; i < 200 && hits.Count == 0; i++)
        {
            WreckStrike.Hit h = WreckStrike.Test(engine, mass, reach, ac, cg, hm.Elements, dt);
            if (h.Any) hits.Add(h.Atom);
            // Coast both on: the aeroplane forward, the engine at its own (slower) speed. The test works in the
            // aircraft frame, so the aeroplane moving forward is what carries the engine aft.
            engine = At(engine.Position + engine.Velocity * dt, engine.Velocity);
            ac = new RigidBodyState(ac.Position + new Vec3(v, 0, 0) * dt, ac.Attitude, ac.Velocity, ac.Rates);
        }
        _o.WriteLine($"J-3 nose station x={noseX:F2} m; engine released there at 12 m/s of closure struck: " +
                     (hits.Count > 0 ? hits[0].ToString() : "nothing in 1 s"));
        Assert.True(hits.Count > 0, "an engine going aft at 12 m/s along a Cub must meet the aeroplane");
        Assert.NotEqual(WreckAtom.TailCone, hits[0]);   // it meets the nose or the cabin first, not the tail
    }

    // ---- what the blow does --------------------------------------------------------------------------------------

    /// <summary>
    /// APPROPRIATELY means against that component's own ultimate, which the model already carries per joint. The
    /// same impulse in the same place is the same outcome every time: nothing here is randomised.
    /// </summary>
    [Fact]
    public void AStrikeIsJudgedAgainstTheJointsOwnUltimate()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        AirframeHydroModel hm = AirframeHydroModel.Build(c);
        Assert.NotEmpty(hm.Joints);
        Joint j = hm.Joints[0];
        _o.WriteLine($"first joint: {j.Component} at {j.At.X:F2},{j.At.Y:F2},{j.At.Z:F2}, ultimate {j.UltimateNm:F0} N.m");

        Vec3 at = j.At + new Vec3(0, 1.0, 0);            // a metre out along the joint
        // A blade's impulse: 5.7 kg arriving at 130 m/s and stopping is 741 N.s.
        double impulse = 5.7 * 130.0;
        double moment = WreckStrike.StrikeMomentNm(impulse, at, j);
        _o.WriteLine($"a 5.7 kg blade at 130 m/s, a metre out: {impulse:F0} N.s = {moment:F0} N.m against {j.UltimateNm:F0}");
        Assert.Equal(impulse * 1.0, moment, 6);
        Assert.Equal(moment > j.UltimateNm, WreckStrike.Severs(impulse, at, j));
        // And a feeble one does not: the same place, a hundredth of the impulse.
        Assert.False(WreckStrike.Severs(impulse * 0.01, at, j));
    }

    /// <summary>The thrown blade's own numbers, end to end: Ty's blade rule gives the mass, the disc's rim speed
    /// gives the velocity, and the energy is what arrives at the airframe.</summary>
    [Fact]
    public void AThrownBladeCarriesTensOfKilojoules()
    {
        foreach (string id in new[] { "pitts-s2b-like", "j3-cub-like" })
        {
            AircraftConfig c = Load(id);
            PropulsionConfig p = c.Propulsion!;
            double bladeKg = EngineOverspeed.BladeMassKg(p.PropInertia, p.PropDiameterM, p.PropBladeCount);
            double rCom = EngineOverspeed.BladeComRadiusM(p.PropDiameterM);
            double omega = p.MaxRpm * 2.0 * Math.PI / 60.0;
            // A whole blade leaves at its CoM's tangential speed; a fixed-pitch outer half at 1.5 x that radius.
            double m = p.ConstantSpeed ? bladeKg : 0.5 * bladeKg;
            double r = p.ConstantSpeed ? rCom : 1.5 * rCom;
            double vTan = omega * r, ke = 0.5 * m * vTan * vTan;
            _o.WriteLine($"{id}: {m:F2} kg leaving at {vTan:F0} m/s tangential = {ke / 1000.0:F1} kJ");
            Assert.True(vTan > 100.0, $"{id}: the rim speed is over 100 m/s ({vTan:F0})");
            // The Pitts' whole blade is 55.8 kJ; the Cub's outer HALF is 8.6 kJ — less mass, but it leaves FASTER
            // (185 m/s against 140) because its centre of mass is at 1.5 times the radius. For scale, a rifle
            // bullet is about 3 kJ, so even the small one is going through whatever it meets.
            Assert.True(ke > 5000.0, $"{id}: kilojoules, not joules ({ke / 1000.0:F1} kJ)");
            if (p.ConstantSpeed) Assert.True(ke > 20000.0, $"{id}: a whole blade is tens of kJ ({ke / 1000.0:F1} kJ)");
        }
    }
}
