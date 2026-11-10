using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHEN the propeller took its damage, so a replay draws the prop of the moment being watched (Ty 2026-11-10, build
/// 2026.1110.0619: "Replay shows damaged propeller that is not turning and bent wings prior to the crash bug is back").
///
/// The bends were recorded on 2026-11-08; the propeller was not. Its four readings a blade — the straight span the air
/// works on, the span a strike affected, which way a metal curl went, whether a brittle blade's piece left — were read
/// LIVE by AirframeVisual.DamageBlades, which during a playback is whatever the aeroplane ended up as. So a flight that
/// ended in a prop strike opened on the curled or shattered propeller from the first frame, minutes before the strike.
/// These pin the recording that fixes it.
/// </summary>
[Collection("WorldTerrainActive")]
public class ReplayPropShapeTests
{
    private readonly ITestOutputHelper _out;
    public ReplayPropShapeTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string n) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    /// <summary>An aeroplane standing with its propeller turning, 100 m clear of anything.</summary>
    private static (Aircraft ac, SimLoop sim, Vec3 strikePos, Quat att) Standing(string id, double depth)
    {
        AircraftConfig c = Load(id);
        WorldTerrain.Active = null;
        // The lowest point of the prop disc below the CG: put the aeroplane so that point is `depth` into the ground.
        double low = AirframeContact.BuildPoints(c).Where(p => p.PropDisc).Max(p => p.Body.Z - c.Mass.CgVec().Z);
        var att = new Quat(0, 0, 0, 1);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -100), att, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        return (ac, new SimLoop(ac), new Vec3(0, 0, -(low - depth)), att);
    }

    /// <summary>Hold the turning propeller <paramref name="depth"/> into the ground for <paramref name="hold"/> s at full
    /// throttle, then lift clear — the same shape as PropBladeDamageTests.Strike, on the ground only.</summary>
    private static void Strike(Aircraft ac, SimLoop sim, Vec3 pos, Quat att, double depth, double hold)
    {
        var lever = new ControlInputs(0, 0, 0, -1);   // full throttle
        try
        {
            sim.RunFor(0.5, lever);                   // the engine up to its rpm, nowhere near the ground
            for (double t = 0; t < hold - 1e-9; t += 0.005)
            {
                ac.State = new RigidBodyState(pos, att, Vec3.Zero, Vec3.Zero);
                sim.RunFor(0.005, lever);
            }
            ac.State = new RigidBodyState(new Vec3(0, 0, -100), att, Vec3.Zero, Vec3.Zero);
            sim.RunFor(0.3, lever);
        }
        finally { Atmosphere.SimTimeSec = 0; }
    }

    private static bool AnyBladeDamaged(in AirframeShape s)
    {
        for (int k = 0; k < s.BladesPerProp; k++)
        {
            PropBladeShape b = s.Blade(0, k);
            if (b.AffectedM > PropBladeShape.TolM || b.Broken || b.Curl != 0) return true;
        }
        return false;
    }

    // ---- the defect itself --------------------------------------------------------------------------------------------

    /// <summary>The whole report, in one: a wooden propeller that meets the ground is NOT shattered before the strike that
    /// shattered it, and is shattered after.</summary>
    [Fact]
    public void ThePropIsNOTDamagedBeforeTheStrikeThatDamagedIt()
    {
        (Aircraft ac, SimLoop sim, Vec3 pos, Quat att) = Standing("j3-cub-like", 0.05);
        var line = new ReplayShapeTimeline();
        Assert.True(line.Record(0.0, ac));              // taxiing out, the prop straight
        Strike(ac, sim, pos, att, 0.05, 0.3);
        Assert.True(line.Record(5.0, ac));              // a NEW shape: the blades have changed
        Assert.Equal(2, line.Count);

        Assert.True(line.TryShapeAt(2.0, out AirframeShape before));
        Assert.False(AnyBladeDamaged(before));
        Assert.Equal(2, before.BladesPerProp);
        Assert.Equal(0.915, before.Blade(0, 0).AeroRadiusM, 9);   // the J-3's 72 in propeller, both blades whole
        Assert.Equal(0.915, before.Blade(0, 1).AeroRadiusM, 9);

        Assert.True(line.TryShapeAt(7.0, out AirframeShape after));
        Assert.True(AnyBladeDamaged(after));
        _out.WriteLine($"after the strike: blade 0 {after.Blade(0, 0).AeroRadiusM:F4} m aff {after.Blade(0, 0).AffectedM * 1000:F1} mm " +
                       $"broken {after.Blade(0, 0).Broken}; blade 1 {after.Blade(0, 1).AeroRadiusM:F4} m aff {after.Blade(0, 1).AffectedM * 1000:F1} mm " +
                       $"broken {after.Blade(0, 1).Broken}; propeller lost {after.PropellerLost}");
        // Wood: the struck span leaves, so a damaged blade is shorter and broken, never curled.
        for (int k = 0; k < 2; k++)
        {
            PropBladeShape b = after.Blade(0, k);
            Assert.Equal(0, b.Curl);
            if (b.AffectedM <= PropBladeShape.TolM) continue;
            Assert.True(b.Broken);
            Assert.Equal(0.915 - b.AffectedM, b.AeroRadiusM, 9);
        }
    }

    /// <summary>A strike deep enough to stop the engine loses the PROPELLER, which is what folds the drawn blade tips back
    /// and takes the blur disc away (AirframeVisual.ShowPropBend). The flag is recorded, so the fold goes on at the strike
    /// and not from the first frame.</summary>
    [Fact]
    public void TheWholePropellerFoldIsRecordedAtTheStrikeThatStoppedTheEngine()
    {
        (Aircraft ac, SimLoop sim, Vec3 pos, Quat att) = Standing("j3-cub-like", 0.25);
        var line = new ReplayShapeTimeline();
        line.Record(0.0, ac);
        Strike(ac, sim, pos, att, 0.25, 1.0);
        Assert.True(ac.EngineStopped, "a propeller 25 cm into the ground for a second should stop the engine");
        Assert.True(ac.IsLost(AirframeComponent.Propeller));
        line.Record(5.0, ac);

        Assert.True(line.TryShapeAt(2.0, out AirframeShape before));
        Assert.False(before.PropellerLost);
        Assert.True(line.TryShapeAt(7.0, out AirframeShape after));
        Assert.True(after.PropellerLost);
    }

    /// <summary>An untouched propeller is one entry for the whole flight, as the untouched airframe is: five seconds of a
    /// propeller turning at full power, 100 m clear of anything, must not fill the timeline a frame at a time.</summary>
    [Fact]
    public void AnUntouchedPropellerIsStillOneEntry()
    {
        (Aircraft ac, SimLoop sim, _, Quat att) = Standing("j3-cub-like", 0.0);
        var line = new ReplayShapeTimeline();
        Assert.True(line.Record(0.0, ac));
        var lever = new ControlInputs(0, 0, 0, -1);
        var held = new Vec3(0, 0, -100);
        try
        {
            for (double t = 0.1; t < 5.0; t += 0.1)
            {
                ac.State = new RigidBodyState(held, att, Vec3.Zero, Vec3.Zero);   // held clear: this is about the blades, not the flight
                sim.RunFor(0.1, lever);
                line.Record(t, ac);
            }
        }
        finally { Atmosphere.SimTimeSec = 0; }
        Assert.Equal(1, line.Count);
        Assert.True(ac.EngineRpm > 1000.0, $"the propeller should be turning: {ac.EngineRpm:F0} rpm");
    }

    /// <summary>A glider has no propeller, so nothing of one is recorded — and asking for a blade gives a straight blade of
    /// no span rather than throwing.</summary>
    [Fact]
    public void AGliderRecordsNoBlades()
    {
        AircraftConfig c = Load("glider-2-33-like");
        WorldTerrain.Active = null;
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -600), Quat.Identity, new Vec3(25, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        AirframeShape s = AirframeShape.Capture(0.0, ac);
        Assert.Equal(0, s.BladesPerProp);
        Assert.Equal(0, s.PropCount);
        Assert.Empty(s.Blades);
        Assert.Equal(0.0, s.Blade(0, 0).AeroRadiusM, 12);
        Assert.False(s.Blade(0, 0).Broken);
    }

    // ---- the shape's own bookkeeping (no sim: hand-built shapes) ------------------------------------------------------

    private static AirframeShape Shape(double t, PropBladeShape[] blades, int perProp, bool lost) =>
        new AirframeShape(t, Array.Empty<PanelSet>(), 0, 0, 0, 0, 0, false, 0, 0, blades, perProp, lost);

    /// <summary>A blade that changes is a new shape; a change below a millimetre is not worth an entry.</summary>
    [Fact]
    public void ABladeThatChangesIsANewShapeAndATenthOfAMillimetreIsNot()
    {
        AirframeShape a = Shape(0, new[] { new PropBladeShape(0.915, 0, 0, false), new PropBladeShape(0.915, 0, 0, false) }, 2, false);
        AirframeShape sameish = Shape(1, new[] { new PropBladeShape(0.9151, 0.0001, 0, false), new PropBladeShape(0.915, 0, 0, false) }, 2, false);
        AirframeShape curled = Shape(2, new[] { new PropBladeShape(0.835, 0.080, -1, false), new PropBladeShape(0.915, 0, 0, false) }, 2, false);
        AirframeShape folded = Shape(3, new[] { new PropBladeShape(0.915, 0, 0, false), new PropBladeShape(0.915, 0, 0, false) }, 2, true);

        Assert.True(a.SameShapeAs(sameish));
        Assert.False(a.SameShapeAs(curled));
        Assert.False(a.SameShapeAs(folded));   // the whole-propeller fold is a shape of its own
    }

    /// <summary>Trimming the rolling window to a new start carries the propeller with everything else. The shape in force
    /// at the new start is rebuilt at that moment, and a field left out of that rebuild would mend the prop silently.</summary>
    [Fact]
    public void TrimmingCarriesTheBladesForward()
    {
        var damaged = new[] { new PropBladeShape(0.800, 0.115, 0, true), new PropBladeShape(0.915, 0, 0, false) };
        AirframeShape s = Shape(10.0, damaged, 2, true).At(100.0);
        Assert.Equal(100.0, s.Time, 12);
        Assert.True(s.PropellerLost);
        Assert.Equal(2, s.BladesPerProp);
        Assert.Equal(1, s.PropCount);
        Assert.Equal(0.800, s.Blade(0, 0).AeroRadiusM, 12);
        Assert.Equal(0.115, s.Blade(0, 0).AffectedM, 12);
        Assert.True(s.Blade(0, 0).Broken);
        Assert.Equal(0.915, s.Blade(0, 1).AeroRadiusM, 12);
    }

    /// <summary>A twin's blades are flattened [prop × blades + blade], the same way the sim keeps them, so each hub asks
    /// for its own propeller and a blade index past the end does not reach into the next.</summary>
    [Fact]
    public void EachPropellerGetsItsOwnBlades()
    {
        var blades = new[]
        {
            new PropBladeShape(0.90, 0.00, 0, false), new PropBladeShape(0.90, 0.00, 0, false),   // prop 0: whole
            new PropBladeShape(0.60, 0.30, 1, false), new PropBladeShape(0.90, 0.00, 0, false),   // prop 1: one blade curled forward
        };
        AirframeShape s = Shape(0, blades, 2, false);
        Assert.Equal(2, s.PropCount);
        Assert.Equal(0.90, s.Blade(0, 0).AeroRadiusM, 12);
        Assert.Equal(0.60, s.Blade(1, 0).AeroRadiusM, 12);
        Assert.Equal(1, s.Blade(1, 0).Curl);
        Assert.Equal(0.0, s.Blade(1, 2).AeroRadiusM, 12);   // past the blade count: nothing, not prop 2's blade 0
        Assert.Equal(0.0, s.Blade(5, 0).AeroRadiusM, 12);   // past the propeller count
    }
}
