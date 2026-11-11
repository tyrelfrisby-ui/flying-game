using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Engine;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// A THROWN BLADE, END TO END. Ty 2026-11-11: "Yes thrown blade also follows physics and causes damage", and
/// "And falls from the sky [unt]il it hits the ground".
///
/// Everything about the departure is derived — mass from Ty's blade rule inverted out of the authored inertia,
/// azimuth from the shaft angle the sim was already integrating, speed from the tangential velocity that centre of
/// mass had. These pin that it is derived and not chosen: the same failure at a different crank angle has to throw
/// the blade somewhere else.
/// </summary>
[Collection("WorldTerrainActive")]
public class ThrownBladeTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public ThrownBladeTests(ITestOutputHelper o) { _o = o; Calm(); }
    public void Dispose() => Calm();

    private static void Calm()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
        FloatHydro.FlatWaterOverride = null;
    }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    private static Aircraft Flying(AircraftConfig c, double tas, double altM)
        => new(c, new RigidBodyState(new Vec3(0, 0, -altM), Quat.Identity, new Vec3(tas, 0, 0), Vec3.Zero), ControlDeflections.Neutral);

    private static CrashRelay Relay(Aircraft ac, double groundUp = 0.0)
        => new(ac, new FlatWreckWorld(null, groundUp), subscribe: false);

    /// <summary>The azimuth is the shaft angle, and the direction is the tangent there. psi = 0 is the blade out to
    /// the right and going down — BladeElement's own convention, so the two cannot drift apart.</summary>
    [Fact]
    public void TheThrowDirectionIsTheTangentAtTheShaftAngle()
    {
        Vec3 right = EngineOverspeed.ThrowDirectionBody(0.0, +1);
        Assert.Equal(0.0, right.Y, 9);
        Assert.Equal(1.0, right.Z, 9);                     // at the right, going DOWN
        Vec3 bottom = EngineOverspeed.ThrowDirectionBody(0.5 * Math.PI, +1);
        Assert.Equal(-1.0, bottom.Y, 9);                   // at the bottom, going LEFT
        Assert.Equal(0.0, bottom.Z, 9);
        // A left-hand propeller throws the other way from the same clock position.
        Assert.Equal(-right.Y, EngineOverspeed.ThrowDirectionBody(0.0, -1).Y, 9);
        // And the radius points where the blade was.
        Vec3 r0 = EngineOverspeed.ThrowRadiusBody(0.0, 2.0);
        Assert.Equal(0.5, r0.Y, 9);                        // 25 % of a 2 m diameter, out to the right
        Assert.Equal(0.0, r0.Z, 9);
    }

    /// <summary>
    /// THE SAME FAILURE AT A DIFFERENT CRANK ANGLE THROWS IT SOMEWHERE ELSE. Four clock positions, four directions,
    /// and the aeroplane is identical in every one. Nothing here is randomised to hide the absence of a rule.
    /// </summary>
    [Fact]
    public void TheAzimuthDecidesWhereItGoes()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        foreach (double psi in new[] { 0.0, 0.5 * Math.PI, Math.PI, 1.5 * Math.PI })
        {
            var ac = Flying(c, 70.0, 1500.0);
            CrashRelay relay = Relay(ac);
            WreckPiece? blade = relay.ThrowPropellerBlade(2700.0, psi);
            Assert.NotNull(blade);
            Vec3 v = blade!.State.Attitude.Rotate(blade.State.Velocity) - ac.State.Attitude.Rotate(ac.State.Velocity);
            _o.WriteLine($"psi {psi * 180 / Math.PI,5:F0} deg: blade leaves at ({v.X,7:F1},{v.Y,7:F1},{v.Z,7:F1}) m/s relative");
            Assert.True(v.Length > 100.0, $"it is flung, not dropped ({v.Length:F0} m/s)");
        }
        // The four directions are genuinely different: up at one clock position, down at the opposite one.
        var a0 = Flying(c, 70.0, 1500.0); var r0 = Relay(a0);
        var a2 = Flying(c, 70.0, 1500.0); var r2 = Relay(a2);
        WreckPiece b0 = r0.ThrowPropellerBlade(2700.0, 0.0)!;
        WreckPiece b2 = r2.ThrowPropellerBlade(2700.0, Math.PI)!;
        double z0 = b0.State.Attitude.Rotate(b0.State.Velocity).Z, z2 = b2.State.Attitude.Rotate(b2.State.Velocity).Z;
        _o.WriteLine($"psi 0 leaves with vz {z0:F1}, psi 180 with vz {z2:F1}");
        Assert.True(z0 > 0.0 && z2 < 0.0, "opposite clock positions throw it opposite ways");
    }

    /// <summary>The blade's own numbers, and the aeroplane gets lighter by exactly what left it.</summary>
    [Fact]
    public void TheBladeTakesItsOwnMassWithIt()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Flying(c, 70.0, 1500.0);
        double before = ac.MassProperties.MassKg;
        CrashRelay relay = Relay(ac);
        relay.ThrowPropellerBlade(2700.0, 0.0);
        (double m, double speed, double psi) = relay.LastThrownBlade!.Value;
        _o.WriteLine($"Pitts threw {m:F2} kg at {speed:F0} m/s from psi {psi * 180 / Math.PI:F0} deg; " +
                     $"aeroplane {before:F1} -> {ac.MassProperties.MassKg:F1} kg");
        Assert.InRange(m, 5.0, 6.5);                        // Ty's rule on the authored inertia
        Assert.InRange(speed, 120.0, 160.0);
        // AND THE ENGINE WENT WITH IT, which is the point: the blade's unbalance beats the mount by 46x, so the two
        // are one event. ShedMass recomputes the mass PROPERTIES for both, so what left the aeroplane is the blade
        // plus the installed engine and the aeroplane is that much lighter — not a visual, the real numbers.
        Assert.True(relay.EngineGone, "a 225 kN unbalance does not stay on the mount");
        Assert.Equal(before - ac.ShedMassKg, ac.MassProperties.MassKg, 6);
        Assert.True(ac.ShedMassKg > m, $"the engine's mass went too ({ac.ShedMassKg:F0} kg against the blade's {m:F1})");
    }

    /// <summary>
    /// AND IT FALLS UNTIL IT HITS THE GROUND. Ty's words. No lifetime, no distance cull: thrown at altitude it is
    /// still being stepped, still moving, and still above the ground a long way downrange — and it arrives.
    /// </summary>
    [Fact]
    public void ItFallsFromTheSkyUntilItHitsTheGround()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        var ac = Flying(c, 70.0, 1000.0);
        CrashRelay relay = Relay(ac, groundUp: 0.0);
        WreckPiece blade = relay.ThrowPropellerBlade(2700.0, 0.5 * Math.PI)!;
        Vec3 start = blade.State.Position;

        double t = 0.0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (t < 120.0 && blade.Phase == WreckPhase.InAir)
        {
            relay.Step(1.0 / 50.0);
            t += 1.0 / 50.0;
        }
        sw.Stop();
        Vec3 end = blade.State.Position;
        double downrange = Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y));
        _o.WriteLine($"from 1,000 m: in the air {t:F1} s, fell {end.Z - start.Z:F0} m, {downrange:F0} m downrange, " +
                     $"phase {blade.Phase}; {sw.Elapsed.TotalMilliseconds:F0} ms of CPU for {t:F0} s of fall");
        Assert.NotEqual(WreckPhase.InAir, blade.Phase);
        Assert.True(t > 5.0, "a kilometre takes real time to fall");
        Assert.True(end.Z > start.Z + 900.0, "it got to the ground");
        // THE COST OF KEEPING IT IN THE AIR THE WHOLE WAY, which is the thing to watch if drop altitudes grow or
        // several pieces are falling at once. Measured: 1,576 ms of CPU for 47.4 s of fall with TWO pieces in the
        // air (the blade, and the engine its unbalance took off the mount) — 3 % of real time. It was 4,278 ms
        // before CrashRelay's broad phase, which is what the distance check there bought.
        Assert.True(sw.Elapsed.TotalMilliseconds < 3000.0, $"a long fall must stay cheap ({sw.Elapsed.TotalMilliseconds:F0} ms)");
    }

    // ---- the mount, and what the aeroplane becomes ---------------------------------------------------------------

    /// <summary>
    /// THE MOUNT CANNOT CARRY IT, AND THE MARGIN IS WHY THE DERIVED LIMIT IS SAFE TO SHIP. The limit itself is
    /// DERIVED and NOT READ (EngineOverspeed.MountLimitG has the paper trail: CAR 4a 4a.84(d)'s -1.5 is a wing
    /// lift-truss condition by the standard's own words, TCDS A-691 states no load factors at all, and 4a.191 lands
    /// near 1.9-2.0 g on a wing area A-691 does not publish). What makes it acceptable anyway is that the answer
    /// does not depend on it: this measures the ratio across the whole plausible range of mount strengths.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    public void TheUnbalanceBeatsAnyPlausibleMount(string id)
    {
        AircraftConfig c = Load(id);
        PropulsionConfig p = c.Propulsion!;
        var ac = Flying(c, 70.0, 1500.0);
        CrashRelay relay = Relay(ac);
        relay.ThrowPropellerBlade(p.MaxRpm, 0.0);
        double unbalance = relay.LastUnbalanceN;
        double engineKg = FlyingGame.Core.Wreck.WaterPhysics.PistonEngineDryKg(p.MaxPowerW) * 1.4;
        _o.WriteLine($"{id}: unbalance {unbalance / 1000.0:F1} kN against an engine of about {engineKg:F0} kg — " +
                     $"{unbalance / EngineOverspeed.MountCapacityN(engineKg):F0}x a {EngineOverspeed.MountLimitG:F2} g mount, " +
                     $"{unbalance / (15.0 * 9.80665 * engineKg):F1}x a 15 g one");
        Assert.True(EngineOverspeed.MountFails(unbalance, engineKg));
        // The conclusion has to be the same anywhere in the plausible range, or the derived limit would be load-bearing.
        for (double g = 1.5; g <= 15.0; g += 0.5)
            Assert.True(unbalance > g * 9.80665 * engineKg, $"{id}: a {g:F1} g mount would hold it ({unbalance / 1000.0:F1} kN)");
        Assert.True(relay.EngineGone, "and so the engine goes");
    }

    /// <summary>
    /// WHAT HAPPENS TO THE PILOT NEXT IS THE CG, NOT THE SEPARATION. Taking the engine off the nose of a light
    /// single is an enormous AFT shift, and ShedMass has to carry it as mass, inertia AND centre of gravity — not as
    /// a visual. Measured here, printed, so the size of it is on the record.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    public void LosingTheEngineIsAnEnormousAftCgShift(string id)
    {
        AircraftConfig c = Load(id);
        var ac = Flying(c, 60.0, 1500.0);
        double massBefore = ac.MassProperties.MassKg, iyyBefore = ac.MassProperties.Iyy;
        Vec3 cgBefore = ac.CgShiftBody;
        CrashRelay relay = Relay(ac);
        WreckPiece? engine = relay.SeparateEngine();
        Assert.NotNull(engine);
        Vec3 cgAfter = ac.CgShiftBody;
        double shiftM = cgAfter.X - cgBefore.X;
        double chord = c.Surfaces.Count > 0 && c.Surfaces[0].Strips.Count > 0 ? c.Surfaces[0].Strips[0].Chord : 1.0;
        _o.WriteLine($"{id}: engine {engine!.MassKg:F0} kg gone — mass {massBefore:F0} -> {ac.MassProperties.MassKg:F0} kg, " +
                     $"Iyy {iyyBefore:F0} -> {ac.MassProperties.Iyy:F0}, CG moved {shiftM * 100:F0} cm AFT " +
                     $"({shiftM / chord * 100:F0} % of a {chord:F2} m chord)");
        Assert.True(engine.MassKg > 50.0, $"{id}: an installed engine is a real mass ({engine.MassKg:F0} kg)");
        Assert.Equal(massBefore - engine.MassKg, ac.MassProperties.MassKg, 6);
        Assert.True(ac.MassProperties.Iyy < iyyBefore, "the engine's parallel-axis share of the pitch inertia goes with it");
        Assert.True(shiftM < -0.05, $"{id}: the CG must move AFT by a real distance ({shiftM * 100:F0} cm)");
    }

    /// <summary>
    /// AND ON THIS AEROPLANE IT MISSES — which is a measurement, not a shortcut, and it is worth writing down
    /// because it was not what I expected. My brief said a biplane's two wings and cabin were within a metre of the
    /// disc and would be struck; they are not, and the reason is geometry rather than opinion. DO NOT "FIX" THIS.
    ///
    /// A thrown blade leaves TANGENTIALLY, in the plane of the disc, and keeps the aeroplane's forward velocity. So
    /// at the instant it goes it has no aft motion relative to the aeroplane at all: it travels straight out
    /// sideways, or up, or down, in a plane 1.6 m ahead of the Pitts' body origin, where there is nothing but air.
    /// Measured across all sixteen clock positions, the closest it comes to any airframe box is printed below, and
    /// it is clear every time — at 140 m/s it is 70 m away inside half a second, long before drag can let the
    /// aeroplane catch up with it.
    ///
    /// THE THING THAT DOES HIT THE AEROPLANE IS THE ENGINE, for exactly the reason Ty gave: it leaves from the nose
    /// while the aeroplane is still flying forward, so the relative motion carries it AFT down the fuselage. That is
    /// measured in WreckStrikeTests.ADepartedEngineTracksAftAlongTheFuselage, where a J-3's engine released at the
    /// nose strikes the Cabin. The strike machinery is the same for both; the geometry is what differs, and it is
    /// the geometry deciding rather than me.
    /// </summary>
    [Fact]
    public void AThrownBladeOnThisAeroplaneClearsIt()
    {
        AircraftConfig c = Load("pitts-s2b-like");
        AirframeHydroModel hm = AirframeHydroModel.Build(c);
        Vec3 cg = c.Mass.CgVec();
        double closest = double.MaxValue;
        string where = "";
        for (int k = 0; k < 16; k++)
        {
            double psi = 2.0 * Math.PI * k / 16.0;
            var ac = Flying(c, 70.0, 1500.0);
            CrashRelay relay = Relay(ac);
            WreckPiece blade = relay.ThrowPropellerBlade(2700.0, psi)!;
            for (int i = 0; i < 200; i++)
            {
                relay.Step(1.0 / 200.0);
                Assert.Null(relay.LastStrike);
                // How close the blade's CG came to any airframe box, in the aeroplane's own frame.
                Vec3 pb = ac.State.Attitude.Conjugate().Rotate(blade.State.Position - ac.State.Position) + cg;
                foreach (HydroElement e in hm.Elements)
                {
                    Vec3 d = pb - e.Centre;
                    double gap = Math.Max(Math.Max(Math.Abs(d.X) - e.Half.X, Math.Abs(d.Y) - e.Half.Y), Math.Abs(d.Z) - e.Half.Z);
                    if (gap < closest) { closest = gap; where = $"psi {360.0 * k / 16.0:F0} deg, {e.Atom}"; }
                }
            }
        }
        _o.WriteLine($"across all 16 clock positions the blade's closest approach to the airframe was {closest:F2} m ({where})");
        Assert.True(closest > 0.0, "it clears the aeroplane at every clock position — measured, not assumed");
    }
}
