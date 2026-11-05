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
/// Flat spots (Ty 2026-11-05: "a tire that has been locked up introduces a flat spot that is felt as it rolls; the longer
/// it locks up the deeper the flat spot, the more significant the bump as it rolls, and the frequency should be modeled
/// after the actual tire rotation rate"). Nothing here is a flat-spot model of its own: a locked wheel puts all its
/// friction work on the strips under the patch (TyreWear), the worn strips stand the axle lower when they come round
/// (TyreWear.Drop, the gear's contact point), and that is the bump, the load swing and the thump.
/// </summary>
[Collection("WorldTerrainActive")]
public class FlatSpotTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public FlatSpotTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static void SetSpeed(Aircraft ac, double v)
        => ac.State = new RigidBodyState(ac.State.Position, ac.State.Attitude, ac.State.Attitude.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero);

    /// <summary>A 172 rolling at 15 m/s, full pedal for <paramref name="lockS"/> seconds, then released.</summary>
    private static (Aircraft ac, SimLoop sim, int left) LockFor(double lockS)
    {
        AircraftConfig c = Cfg("c172-like");
        RigidBodyState st = LandingGear.RestingState(c, 0, 0, WorldTerrain.WheelGroundHeightAt(0, 0));
        var ac = new Aircraft(c, st, new ControlDeflections(0, 0, 0, 0));
        SetSpeed(ac, 15);
        var sim = new SimLoop(ac);
        sim.RunFor(0.5, new ControlInputs(0, 0, 0, 0));
        if (lockS > 0) { ac.BrakeInput = 1; sim.RunFor(lockS, new ControlInputs(0, 0, 0, 0)); ac.BrakeInput = 0; }
        return (ac, sim, Enumerable.Range(0, ac.Wheels.Count).First(i => ac.Wheels[i].Side < 0));
    }

    /// <summary>The longer the lock, the deeper the spot, on the same short length of tread; a lock this short does not
    /// wear through (that is a burst, TyreWearTests).</summary>
    [Fact]
    public void TheLongerTheLockUpTheDeeperTheSpot()
    {
        double last = 0;
        foreach (double s in new[] { 0.5, 0.8, 1.1, 1.6 })
        {
            (Aircraft ac, _, int left) = LockFor(s);
            TyreWear t = ac.Wheels[left].Tyre!;
            var spots = t.FlatSpots();
            _o.WriteLine($"locked {s:F1} s: {spots.Count} spot(s), " + string.Join(", ", spots.Select(p => $"{p.DepthM * 1000:F2} mm deep, {p.LengthM * 100:F0} cm long at {p.AngleRad * 180 / Math.PI:F0} deg")));
            Assert.Single(spots);
            Assert.False(t.Burst);
            Assert.True(spots[0].DepthM > 1.5 * last, $"a {s} s lock should be well deeper than a shorter one");
            Assert.True(spots[0].LengthM < 0.15, "a flat spot is the patch's length, not the tyre's");
            last = spots[0].DepthM;
        }
        Assert.True(last > 0.5e-3, "a second-long lock should leave a spot you can feel (over half a millimetre)");
    }

    /// <summary>The bump comes once a revolution, at V/(2πr), at taxi speed and at a take-off roll alike (where the wheel
    /// turns further than a patch length every step), and it is in the gear's force and the ride. Twins: the same lock,
    /// then one copy has its tread made good again, so the difference between them is the spot and nothing else; the
    /// sound twin has no bumps and no thump.</summary>
    [Theory]
    [InlineData(4.0)]
    [InlineData(12.0)]
    [InlineData(28.0)]
    public void OneBumpEveryRevolutionAndNoneWithoutASpot(double v)
    {
        (Aircraft ac, SimLoop sim, int left) = LockFor(1.0);
        (Aircraft twin, SimLoop twinSim, _) = LockFor(1.0);
        TyreWear spot = ac.Wheels[left].Tyre!;
        Assert.Equal(spot.MaxWornM, twin.Wheels[left].Tyre!.MaxWornM);     // deterministic: the twins are one aeroplane
        foreach (WheelState x in twin.Wheels) if (x.Tyre != null) Array.Clear(x.Tyre.WornM);
        SetSpeed(ac, v); SetSpeed(twin, v);
        WheelState w = ac.Wheels[left], tw = twin.Wheels[left];
        // One step for the twin to stand back up off the spot it no longer has (that settling is not a bump).
        sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0)); twinSim.RunFor(0.005, new ControlInputs(0, 0, 0, 0));
        w.ThumpJ = 0; tw.ThumpJ = 0;
        double thr = 0.1 * spot.MaxWornM;
        double t = 0, turned = 0, lastAng = w.AngleRad, first = double.NaN, lastBump = 0, thump = 0, twinThump = 0;
        double dLoadMax = 0, dAccMax = 0; int bumps = 0, twinBumps = 0; bool inBump = false;
        Vec3 v0 = ac.State.Attitude.Rotate(ac.State.Velocity), tv0 = twin.State.Attitude.Rotate(twin.State.Velocity);
        for (int i = 0; i < 400; i++)
        {
            sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0)); twinSim.RunFor(0.005, new ControlInputs(0, 0, 0, 0)); t += 0.005;
            double d = w.AngleRad - lastAng; d -= 2 * Math.PI * Math.Round(d / (2 * Math.PI)); turned += d; lastAng = w.AngleRad;
            bool b = w.DropM > thr;
            if (b && !inBump) { bumps++; if (double.IsNaN(first)) first = t; lastBump = t; }
            inBump = b;
            if (tw.DropM > thr) twinBumps++;
            thump += w.ThumpJ; w.ThumpJ = 0; twinThump += tw.ThumpJ; tw.ThumpJ = 0;
            // What the spot does: the difference in the wheel's load, and in the vertical acceleration of the aeroplane.
            Vec3 v1 = ac.State.Attitude.Rotate(ac.State.Velocity), tv1 = twin.State.Attitude.Rotate(twin.State.Velocity);
            dLoadMax = Math.Max(dLoadMax, Math.Abs(w.LoadN - tw.LoadN));
            if (i > 0) dAccMax = Math.Max(dAccMax, Math.Abs((v1.Z - v0.Z) - (tv1.Z - tv0.Z)) / 0.005 / 9.81);
            v0 = v1; tv0 = tv1;
        }
        double revs = turned / (2 * Math.PI), rate = revs / t, hz = bumps > 1 ? (bumps - 1) / (lastBump - first) : 0;
        _o.WriteLine($"{v} m/s, {spot.MaxWornM * 1000:F2} mm spot: {revs:F2} revs ({rate:F2} rev/s; V/2πr at the start {v / (2 * Math.PI * w.RollingRadiusM):F2}), " +
                     $"{bumps} bumps at {hz:F2} Hz; against the sound twin: load up to {dLoadMax:F0} N apart, vertical accel up to {dAccMax:F3} g, " +
                     $"thump {thump:F1} J vs {twinThump:F2} J");
        Assert.Equal(0, twinBumps);
        Assert.True(twinThump < 0.05, "a sound tyre does not thump");
        Assert.InRange(bumps, Math.Floor(revs), Math.Ceiling(revs));
        Assert.Equal(rate, hz, rate * 0.04);
        Assert.True(thump > 0.5 * bumps, "each pass is a thump");
        Assert.True(dLoadMax > 50, "the spot should be in the gear's force");
        Assert.True(dAccMax > 0.005, "and in the ride");
    }

    /// <summary>A second lock-up with the wheel at another angle is a second spot; and the swept-arc drop never steps
    /// over a spot, however far the wheel turns in a step, while it gives the same impulse a fine step does.</summary>
    [Fact]
    public void ASecondLockUpIsASecondSpotAndAFastWheelNeverStepsOverOne()
    {
        AircraftConfig c = Cfg("c172-like");
        WheelState w = WheelState.For(c).First(x => x.Side < 0);
        TyreWear t = w.Tyre!;
        Assert.Equal(0.0, t.Drop(0.3, 1.2));
        Assert.Empty(t.FlatSpots());
        for (int i = 0; i < 60; i++) t.Abrade(0.6 * 3500 * 12 * 0.005, 12, 3500, 0.5, 0.5, 0.005, WorldTerrain.Surface.Paved);
        for (int i = 0; i < 60; i++) t.Abrade(0.6 * 3500 * 12 * 0.005, 12, 3500, 3.5, 3.5, 0.005, WorldTerrain.Surface.Paved);
        var spots = t.FlatSpots();
        _o.WriteLine(string.Join(", ", spots.Select(p => $"{p.DepthM * 1000:F2} mm at {p.AngleRad:F2} rad")));
        Assert.Equal(2, spots.Count);
        Assert.Equal(0.5, spots[0].AngleRad, 0.1);
        Assert.Equal(3.5, spots[1].AngleRad, 0.1);

        // Coarse (a 1.2 rad step) and fine (0.01 rad) passes over the 0.5 rad spot: the coarse one still sees it, and
        // the area under drop(angle) — the impulse the gear gets — agrees.
        _o.WriteLine($"drop at -0.5 / 0.5 / 1.5 rad: {t.Drop(-0.5) * 1000:F3} / {t.Drop(0.5) * 1000:F3} / {t.Drop(1.5) * 1000:F3} mm, swept -0.5..1.5: {t.Drop(-0.5, 1.5) * 1000:F3} mm");
        Assert.True(t.Drop(0.5) > 0 && t.Drop(-0.5) == 0 && t.Drop(1.5) == 0);
        Assert.True(t.Drop(-0.5, 1.5) > 0.05 * t.Drop(0.5), "a step that sweeps over the spot must feel it");
        double coarse = 0, fine = 0;
        for (double a = -1.5; a < 2.5 - 1e-9; a += 2.0) coarse += t.Drop(a, a + 2.0) * 2.0;
        for (double a = -1.5; a < 2.5 - 1e-9; a += 0.01) fine += t.Drop(a, a + 0.01) * 0.01;
        Assert.Equal(fine, coarse, fine * 0.1);
    }
}
