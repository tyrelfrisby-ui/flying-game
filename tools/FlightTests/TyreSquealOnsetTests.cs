using System;
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
/// When the tyre starts to squeal (Ty 2026-11-05 08:50: "i took off in a crosswind in a cessna no brakes and the tires
/// were howling so make the threshold higher on when the screetching starts and make it more of a fade in"). A tyre
/// under its friction peak is gripping and quiet; it squeals as its slip passes the peak and the patch slides, fading in
/// over a band of slip (GroundSoundMix.SideSquealOnset / ForeSquealOnset) and, sideways, over ~0.2 s.
/// </summary>
[Collection("WorldTerrainActive")]
public class TyreSquealOnsetTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public TyreSquealOnsetTests(ITestOutputHelper o) { _o = o; Reset(); }
    public void Dispose() => Reset();
    private static void Reset()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null; Atmosphere.SimTimeSec = 0;
    }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>The onset bands, by hand: smoothstep 3u² − 2u³ across each band.</summary>
    [Fact]
    public void OnsetIsASmoothstepFromTheTyresFrictionPeak()
    {
        Assert.Equal(0.0, GroundSoundMix.SideSquealOnset(0.9), 12);      // under the peak: gripping, quiet
        Assert.Equal(0.0, GroundSoundMix.SideSquealOnset(1.0), 12);
        Assert.Equal(0.15625, GroundSoundMix.SideSquealOnset(1.25), 12);  // u = 0.25
        Assert.Equal(0.5, GroundSoundMix.SideSquealOnset(1.5), 12);
        Assert.Equal(1.0, GroundSoundMix.SideSquealOnset(2.0), 12);
        Assert.Equal(0.0, GroundSoundMix.SideScrubOnset(0.75), 12);
        Assert.Equal(0.5, GroundSoundMix.SideScrubOnset(1.125), 12);
        Assert.Equal(1.0, GroundSoundMix.SideScrubOnset(1.5), 12);
        Assert.Equal(0.0, GroundSoundMix.ForeSquealOnset(1.25), 12);      // 0.10 at κ0 = 0.08
        Assert.Equal(0.5, GroundSoundMix.ForeSquealOnset(1.875), 12);
        Assert.Equal(1.0, GroundSoundMix.ForeSquealOnset(2.5), 12);       // 0.20 at κ0 = 0.08
        Assert.Equal(1.0, GroundSoundMix.ForeSquealOnset(12.5), 12);      // a locked wheel, κ = 1
    }

    /// <summary>A sideways slide past the peak fades in over ~0.2 s rather than switching on: 1 − e^(−t/0.2) of its level.</summary>
    [Fact]
    public void SidewaysSquealFadesInOverAFifthOfASecond()
    {
        AircraftConfig c = Cfg("c172-like");
        int left = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        var w = new WheelState(c, c.Gear[left]);
        var mix = new GroundSoundMix(1);
        const double dt = 0.02;
        float first = 0, at200 = 0, last = 0;
        for (int f = 0; f < 50; f++)
        {
            // 4 kN sliding sideways at 8 m/s for the whole frame, all of it past the peak (nothing "under").
            w.InContact = true; w.Material = GroundMaterial.Asphalt; w.LoadN = 4000; w.VFwdMs = 20;
            w.SideSlipEnergyJ = 4000 * 8 * dt; w.SideSlipTimeS = dt; w.SideSlipImpulseNs = 4000 * dt;
            mix.Update(new[] { w }, 10000, dt);
            if (f == 0) first = mix.ChirpGain[0];
            if (f == 9) at200 = mix.ChirpGain[0];
            last = mix.ChirpGain[0];
        }
        _o.WriteLine($"sideways squeal: first frame {first:F3}, 0.2 s {at200:F3}, 1 s {last:F3}");
        Assert.InRange(first / last, 0.08, 0.12);          // 1 − e^(−0.1) = 0.095
        Assert.InRange(at200 / last, 0.60, 0.67);          // 1 − e^(−1) = 0.632
        Assert.True(last > 0.5f, "a fast sideways slide past the peak squeals");
        // The same slide with half its energy under the band: the envelope lets go toward 0.5 at τ = 0.1 s.
        w.InContact = true; w.Material = GroundMaterial.Asphalt; w.LoadN = 4000; w.VFwdMs = 20;
        w.SideSlipEnergyJ = 4000 * 8 * dt; w.SideSlipTimeS = dt; w.SideSlipImpulseNs = 4000 * dt; w.SideSlipUnderSquealJ = 0.5 * w.SideSlipEnergyJ;
        mix.Update(new[] { w }, 10000, dt);
        Assert.InRange(mix.ChirpGain[0] / last, 0.5, 0.75);
    }

    /// <summary>
    /// A 15 kt direct crosswind from the right, full power, no brakes, on asphalt: aileron into the wind, rudder (and the
    /// nosewheel with it) holding the heading and the centreline until 55 kt. The nose tyre is steered as the C172 POH
    /// has it, ±10° on the pedals (the config links it at ±29°: see the next test). Literal ceilings: squeal under 0.05
    /// of full over the whole roll (silent), scrub under 0.35 once rolling above 10 kt (a faint rub at most; pulling away,
    /// the nose steered hard over scuffs at walking pace, ~0.5, and is printed).
    /// </summary>
    [Fact]
    public void CessnaCrosswindTakeoffRollWithNoBrakesDoesNotSqueal()
    {
        (double squeal, double scrub, double y) = CrosswindRoll(10 * Math.PI / 180);
        Assert.True(y < 5, "the pilot lost the centreline");
        Assert.True(squeal < 0.05, $"squeal {squeal:F3}: a crosswind roll with no brakes must not howl");
        Assert.True(scrub < 0.35, $"scrub {scrub:F3}: no more than a faint rub");
    }

    /// <summary>The same roll with the config's own ±29° pedal-linked nosewheel: printed, not asserted. Holding the
    /// centreline takes ~0.6 left rudder, which steers the nose ~17° and slides its tyre at 3–4× its friction peak, so it
    /// still squeals (QUESTIONS-FOR-TY: the 172's nosewheel steering).</summary>
    [Fact]
    public void CessnaCrosswindTakeoffRollWithTheConfigsNoseSteeringIsMeasured()
    {
        (double squeal, double scrub, _) = CrosswindRoll(0);
        _o.WriteLine($"config nose steering: squeal {squeal:F3}, scrub {scrub:F3}");
    }

    /// <summary>The roll; <paramref name="noseSteerRad"/> > 0 replaces the nosewheel's pedal steering limit.</summary>
    private (double squeal, double scrub, double maxY) CrosswindRoll(double noseSteerRad)
    {
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Paved;
        try
        {
            AircraftConfig c = Cfg("c172-like");
            int nose = c.Gear.FindIndex(g => g.Pos[0] > c.Mass.Cg[0] + 0.5);
            if (noseSteerRad > 0) c.Gear[nose].MaxSteerRad = noseSteerRad;
            var rest = LandingGear.RestingState(c, 0, 0, WorldTerrain.WheelGroundHeightAt(0, 0));
            var ac = new Aircraft(c, rest, new ControlDeflections(0, 0, 0, 0));
            Atmosphere.SteadyWind = new Vec3(0, -15 * 0.514444, 0);   // from the right
            var loop = new SimLoop(ac);
            var mix = new GroundSoundMix(ac.Wheels.Count);
            double weight = ac.MassProperties.MassKg * 9.81;
            const double frame = 0.015;   // three 200 Hz steps
            double maxSqueal = 0, maxScrub = 0, startScrub = 0, rudInt = 0, maxY = 0, ias = 0, t = 0;
            ac.BrakeInput = 0;
            for (; t < 40 && ias < 55 * 0.514444; t += frame)
            {
                RigidBodyState s = ac.State;
                (double phi, _, double psi) = TaildraggerNoTipStrikeTests.Euler(s.Attitude);
                Vec3 vAir = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
                ias = vAir.Length;
                double r = s.Rates.Z, y = s.Position.Y;
                rudInt = Math.Clamp(rudInt - psi * frame, -1, 1);
                double rud = Math.Clamp(-2.0 * psi - 0.6 * r - 0.02 * y + rudInt, -1, 1);
                double ail = Math.Clamp(1.0 - 1.0 * phi, -1, 1);
                loop.RunFor(frame, new ControlInputs(ail, 0, rud, -1));   // lever −1 = full power
                mix.Update(ac.Wheels, weight, frame);
                bool rolling = s.Velocity.X > 10 * 0.514444;   // the pivot at walking pace as the nose first steers is a scuff of its own
                for (int sd = 0; sd < 2; sd++)
                {
                    maxSqueal = Math.Max(maxSqueal, mix.ChirpGain[sd]);
                    if (rolling) maxScrub = Math.Max(maxScrub, mix.ScrubLevel[sd]); else startScrub = Math.Max(startScrub, mix.ScrubLevel[sd]);
                }
                maxY = Math.Max(maxY, Math.Abs(y));
            }
            _o.WriteLine($"172, 15 kt crosswind, nose steer ±{c.Gear[nose].MaxSteerRad * 180 / Math.PI:F0}°: {t:F1} s to {ias / 0.514444:F0} kt, |y| ≤ {maxY:F1} m | squeal peak {maxSqueal:F3} | scrub peak {maxScrub:F3} above 10 kt, {startScrub:F3} pulling away");
            Assert.True(ias > 50 * 0.514444, "the roll never reached rotation speed");
            return (maxSqueal, maxScrub, maxY);
        }
        finally { WorldTerrain.SurfaceOverride = null; Atmosphere.SteadyWind = Vec3.Zero; }
    }

    /// <summary>The left main held locked (zero spin) at 40 kt on asphalt: a skid at slip ratio 1, which squeals at full
    /// onset from the first frame. Literal floor: squeal level above 0.5 at once and on average over the last 0.45 s, at
    /// the tyre's own pitch.</summary>
    [Fact]
    public void LockedWheelSkidStillSqueals()
    {
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Paved;
        try
        {
            AircraftConfig c = Cfg("c172-like");
            var rest = LandingGear.RestingState(c, 0, 0, WorldTerrain.WheelGroundHeightAt(0, 0));
            Vec3 vb = rest.Attitude.Conjugate().Rotate(new Vec3(40 * 0.514444, 0, 0));
            var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, vb, Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
            var loop = new SimLoop(ac);
            var mix = new GroundSoundMix(ac.Wheels.Count);
            double weight = ac.MassProperties.MassKg * 9.81;
            int left = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
            for (int i = 0; i < ac.Wheels.Count; i++) ac.Wheels[i].OmegaRadS = Math.Abs(vb.X) / Math.Max(ac.Wheels[i].RollingRadiusM, 0.05);
            const double frame = 0.015;
            double sum = 0, first = 0, hz = 0; int n = 0;
            for (int f = 0; f < 60; f++)
            {
                for (int k = 0; k < 3; k++) { ac.Wheels[left].OmegaRadS = 0; loop.RunFor(SimLoop.DefaultFixedDtSec, new ControlInputs(0, 0, 0, 0)); }
                mix.Update(ac.Wheels, weight, frame);
                if (f == 0) first = mix.ChirpGain[0];
                if (f >= 30) { sum += mix.ChirpGain[0]; n++; hz = mix.ChirpHz[0]; }
            }
            double mean = sum / n, f0 = GroundSoundMix.SquealResonanceHz(ac.Wheels[left].SoundOdM);
            _o.WriteLine($"172 left main locked at 40 kt: first frame {first:F3}, mean level {mean:F3} (last 0.45 s), {hz:F0} Hz (resonance {f0:F0} Hz)");
            Assert.True(first > 0.5, "a locked wheel squeals at once");
            Assert.True(mean > 0.5, $"a locked-wheel skid must squeal (level {mean:F3})");
            Assert.InRange(hz, 0.6 * f0, 1.35 * f0);
        }
        finally { WorldTerrain.SurfaceOverride = null; }
    }
}
