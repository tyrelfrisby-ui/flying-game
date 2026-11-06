using System;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The 2-33's wheel brake and its squeal (Ty 2026-11-05 16:06, phone 2026.1105.1550: "The tire screeches with any
/// braking at all on the 2-33 so the wheel brake is either too strong or the screech fix isn't in yet"). The onset fix
/// (2a20429a) was in that build. What was wrong was the coupling: the sim read the wheel brake off the SPOILER
/// (handle 0.9..1 = brake 0..1), and the touch knob has its spoilers full at 25 % of the pad and its own brake band
/// below 20 %. So the knob's full-spoiler position was already full wheel brake, before the brake band was reached:
/// measured at 25 kt on asphalt, the main wheel locked for 2.44 s and squealed at 0.85 with BrakeInput 0. The handle's
/// brake band is now the input's alone (Core.Vr.SpoilerHandle, VrHandsTests), arriving as BrakeInput.
/// Literals below are from this file's own Measure run on 2026-11-05 after the change.
/// </summary>
[Collection("WorldTerrainActive")]
public class Sgs233BrakeSquealTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public Sgs233BrakeSquealTests(ITestOutputHelper o) { _o = o; Reset(); }
    public void Dispose() => Reset();
    private static void Reset()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null; Atmosphere.SimTimeSec = 0;
    }

    public readonly record struct Roll(double Squeal, double LockedS, double DecelG, double StopM, double MinLoadFrac);

    /// <summary>The 2-33 rolling on its main wheel from <paramref name="kt"/>, the spoiler lever at <paramref name="lever"/>
    /// and the pilot's own brake at <paramref name="brake"/>, for 8 s or until it stops.</summary>
    internal static Roll RollOut(double kt, double lever, double brake, WorldTerrain.Surface surface)
    {
        WorldTerrain.SurfaceOverride = surface;
        try
        {
            AircraftConfig c = TestAircraftConfig.Load();
            var rest = LandingGear.RestingState(c, 0, 0, WorldTerrain.WheelGroundHeightAt(0, 0));
            double v0 = kt * 0.514444;
            Vec3 vb = rest.Attitude.Conjugate().Rotate(new Vec3(v0, 0, 0));
            var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, vb, Vec3.Zero), new ControlDeflections(0, 0, 0, lever));
            for (int i = 0; i < ac.Wheels.Count; i++) ac.Wheels[i].OmegaRadS = v0 / Math.Max(ac.Wheels[i].RollingRadiusM, 0.05);
            var loop = new SimLoop(ac);
            var mix = new GroundSoundMix(ac.Wheels.Count);
            double weight = ac.MassProperties.MassKg * 9.81;
            int main = c.Gear.FindIndex(g => g.Brake);
            const double frame = 0.015;
            double squeal = 0, locked = 0, minLoad = 1, t = 0, v2s = v0;
            ac.BrakeInput = brake;
            for (; t < 8; t += frame)
            {
                loop.RunFor(frame, new ControlInputs(0, 0, 0, lever));
                mix.Update(ac.Wheels, weight, frame);
                squeal = Math.Max(squeal, Math.Max(mix.ChirpGain[0], mix.ChirpGain[1]));
                if (ac.Wheels[main].Locked) locked += frame;
                if (ac.Wheels[main].InContact) minLoad = Math.Min(minLoad, ac.Wheels[main].LoadN / weight);
                if (Math.Abs(t - 2.0) < frame / 2) v2s = ac.State.Velocity.Length;
                if (ac.State.Velocity.Length < 0.5) break;
            }
            double v2 = t >= 2.0 ? v2s : ac.State.Velocity.Length;
            return new Roll(squeal, locked, (v0 - v2) / (Math.Min(t, 2.0) * 9.81), ac.State.Position.X - rest.Position.X, minLoad);
        }
        finally { WorldTerrain.SurfaceOverride = null; }
    }

    /// <summary>Full spoiler (the knob at 25 %, or the strip / handle full aft) with the pilot's brake at zero: the wheel
    /// rolls free and is silent. Before the change: locked 2.44 s, squeal 0.85, 20 m to stop.</summary>
    [Fact]
    public void FullSpoilerWithNoBrakeRollsFreeAndQuiet()
    {
        Roll r = RollOut(25, 1.0, 0.0, WorldTerrain.Surface.Paved);
        _o.WriteLine($"25 kt, full spoiler, no brake: squeal {r.Squeal:F3}, locked {r.LockedS:F2} s, {r.DecelG:F3} g, {r.StopM:F0} m");
        Assert.True(r.Squeal < 0.05, $"squeal {r.Squeal:F3}: full air brake alone must not screech the tyre");
        Assert.Equal(0.0, r.LockedS, 3);
        Assert.InRange(r.DecelG, 0.04, 0.10);   // 0.068 g: the boards and the rolling drag only
    }

    /// <summary>A light brake (the knob's band at 0.2, the strip's last 3 %) on a loaded wheel: it slows, and it is still
    /// under the tyre's friction peak, so it is quiet. Measured 0.152 g, squeal 0.000.</summary>
    [Fact]
    public void LightBrakeSlowsWithoutAScreech()
    {
        foreach (double kt in new[] { 25.0, 15.0 })
        {
            Roll r = RollOut(kt, 0.85, 0.2, WorldTerrain.Surface.Paved);
            _o.WriteLine($"{kt} kt, brake 0.2: squeal {r.Squeal:F3}, locked {r.LockedS:F2} s, {r.DecelG:F3} g");
            Assert.True(r.Squeal < 0.05, $"squeal {r.Squeal:F3} at {kt} kt: a light brake under the friction peak must be quiet");
            Assert.Equal(0.0, r.LockedS, 3);
            Assert.True(r.DecelG > 0.11, $"{r.DecelG:F3} g: the light brake should still slow the glider");
        }
    }

    /// <summary>The other end, so the fix cannot be a mute: full brake still locks the wheel and squeals (0.85, locked
    /// 2.44 s at 25 kt), and the squeal level itself is not touched here.</summary>
    [Fact]
    public void FullBrakeStillLocksAndSqueals()
    {
        Roll r = RollOut(25, 0.85, 1.0, WorldTerrain.Surface.Paved);
        _o.WriteLine($"25 kt, full brake: squeal {r.Squeal:F3}, locked {r.LockedS:F2} s, {r.DecelG:F3} g");
        Assert.True(r.Squeal > 0.5, $"squeal {r.Squeal:F3}: a locked tyre must still screech");
        Assert.True(r.LockedS > 1.0, $"locked only {r.LockedS:F2} s");
    }

    /// <summary>The table for NEEDS YOU: speed × surface × brake, printed, not asserted.</summary>
    [Fact]
    public void Measure()
    {
        foreach (var surf in new[] { WorldTerrain.Surface.Paved, WorldTerrain.Surface.Grass })
            foreach (double kt in new[] { 35.0, 25.0, 15.0 })
                foreach ((double lever, double brake) in new[] { (0.5, 0.0), (1.0, 0.0), (0.85, 0.1), (0.85, 0.2), (0.85, 0.35), (0.85, 0.5), (0.85, 0.75), (0.85, 1.0) })
                {
                    Roll r = RollOut(kt, lever, brake, surf);
                    _o.WriteLine($"{surf,-6} {kt,3} kt lever {lever:F2} brake {brake:F2}: squeal {r.Squeal:F3} locked {r.LockedS:F2} s decel {r.DecelG:F3} g stop {r.StopM:F0} m minLoad {r.MinLoadFrac:F2}");
                }
    }
}
