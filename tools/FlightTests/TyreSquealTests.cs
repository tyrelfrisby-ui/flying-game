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
/// The tyre's own voice (Ty 2026-11-05: "the tire sounds need work it is a pitch/frequency thing for the chirp and
/// bearing noise is too loud"). The squeal's pitch comes from the tyre's size and the patch's slip speed, a slow
/// sideways slide is a low scrub rather than a tone, the level is the log of the slip power, and the bearing whir is
/// quiet and goes with the square of the wheel's rpm. Literals worked by hand from the formulas in GroundSoundMix.
/// </summary>
[Collection("WorldTerrainActive")]
public class TyreSquealTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public TyreSquealTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>700 Hz·m over the catalog's outside diameter (in × 0.0254), by hand.</summary>
    [Theory]
    [InlineData("8.00-4", 18.0, 1531.06)]    // J-3 main
    [InlineData("6.00-6", 17.5, 1574.80)]    // Decathlon, Great Lakes, C172 mains
    [InlineData("8.00-6", 19.5, 1413.29)]    // PA-18, Pawnee
    [InlineData("5.00-5", 14.2, 1940.78)]    // a nosewheel
    [InlineData("scott-8in", 8.0, 3444.88)]  // the Scott tailwheel
    [InlineData("351510.R1", 35.0, 787.40)]  // 35 in bushwheel
    public void SquealResonanceGoesAsOneOverTheTyresDiameter(string id, double odIn, double hz)
    {
        TireSpec t = TireCatalog.Get(id)!;
        Assert.Equal(odIn, t.OdIn, 6);
        Assert.Equal(hz, GroundSoundMix.SquealResonanceHz(t.OdM), 1);
    }

    [Fact]
    public void SquealPitchRisesWithSlipSpeedWithinItsBounds()
    {
        double od = 18.0 * 0.0254, f0 = 1531.06;
        Assert.Equal(f0, GroundSoundMix.TyreSquealHz(od, 10), 1);              // the reference slip
        Assert.Equal(f0 * 1.2011, GroundSoundMix.TyreSquealHz(od, 25), 0);     // 2.5^0.2: a touchdown's first slide
        Assert.Equal(f0 * 0.7248, GroundSoundMix.TyreSquealHz(od, 2), 0);      // 0.2^0.2: nearly caught up
        Assert.Equal(f0 * 0.6, GroundSoundMix.TyreSquealHz(od, 0.1), 1);       // floor
        Assert.Equal(f0 * 1.35, GroundSoundMix.TyreSquealHz(od, 100), 1);      // ceiling
        double prev = 0;
        for (double v = 0.2; v < 60; v *= 1.2)
        {
            double f = GroundSoundMix.TyreSquealHz(od, v);
            Assert.True(f >= prev, $"squeal pitch fell at {v:F2} m/s");
            prev = f;
            // The small tyre is always higher than the big one at the same slip (Ty: 8.00-6 / 6.00-6 squeak higher).
            Assert.True(GroundSoundMix.TyreSquealHz(17.5 * 0.0254, v) > GroundSoundMix.TyreSquealHz(35 * 0.0254, v));
            Assert.True(GroundSoundMix.TyreScrubHz(od, v) < 0.75 * GroundSoundMix.TyreSquealHz(od, v), $"scrub not below squeal at {v:F2}");
        }
        Assert.Equal(0.42 * f0, GroundSoundMix.TyreScrubHz(od, 3), 1);         // the scrub's reference
    }

    [Fact]
    public void SlowSlidingScrubsFastSlidingSquealsAndLevelIsTheLogOfPower()
    {
        Assert.Equal(0, GroundSoundMix.TonalFraction(1.0));
        Assert.Equal(0, GroundSoundMix.TonalFraction(1.5));
        Assert.Equal(0.5, GroundSoundMix.TonalFraction(3.75), 9);
        Assert.Equal(1, GroundSoundMix.TonalFraction(6.0));
        Assert.Equal(1, GroundSoundMix.TonalFraction(25));
        Assert.Equal(0, GroundSoundMix.SlipLevel(50));
        Assert.Equal(1.0 / 3, GroundSoundMix.SlipLevel(500), 9);
        Assert.Equal(2.0 / 3, GroundSoundMix.SlipLevel(5000), 9);
        Assert.Equal(1, GroundSoundMix.SlipLevel(50000), 9);
        Assert.Equal(1, GroundSoundMix.SlipLevel(1e7), 9);
    }

    private static WheelState Main(AircraftConfig c, out int idx)
    {
        idx = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        var w = new WheelState(c, c.Gear[idx]);
        w.InContact = true; w.Material = GroundMaterial.Asphalt; w.LoadN = 3000; w.VFwdMs = 20; w.OmegaRadS = 20 / w.RadiusM;
        return w;
    }

    /// <summary>The mix, end to end: a fast fore-aft slide is a squeal at the J-3 tyre's pitch for that slip, a slow sideways
    /// one a scrub only, on the wheel's own side.</summary>
    [Fact]
    public void TheMixPitchesEachSlideFromItsOwnSlipSpeed()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        WheelState w = Main(c, out _);
        Assert.Equal(18.0 * 0.0254, w.SoundOdM, 9);
        // 1 kJ over 30 ms at 20 m/s: 33 kW of squeal (tonal 1).
        w.SlipEnergyJ = 1000; w.SlipTimeS = 0.03; w.SlipImpulseNs = 50;
        var mix = new GroundSoundMix(1);
        mix.Update(new[] { w }, 5000, 0.016);
        Assert.Equal(0.03f, mix.ChirpSeconds[0], 5);
        Assert.Equal((float)GroundSoundMix.TyreSquealHz(w.SoundOdM, 20), mix.ChirpHz[0], 1);
        Assert.InRange(mix.ChirpHz[0], 1750, 1775);                               // 1531 × 2^0.2 = 1758.7
        Assert.Equal(0f, mix.ScrubSeconds[0] + mix.ScrubSeconds[1]);             // all tonal at 20 m/s
        Assert.Equal(0f, mix.ChirpSeconds[1]);                                   // a left wheel
        // A crabbed touchdown: 2.4 kN sliding sideways at 1.2 m/s for 50 ms: a scrub, no squeal.
        w.SideSlipEnergyJ = 2400 * 1.2 * 0.05; w.SideSlipTimeS = 0.05; w.SideSlipImpulseNs = 2400 * 0.05;
        mix.Update(new[] { w }, 5000, 0.016);
        Assert.Equal(0f, mix.ChirpSeconds[0]);
        Assert.Equal(0.05f, mix.ScrubSeconds[0], 5);
        Assert.Equal((float)GroundSoundMix.TyreScrubHz(w.SoundOdM, 1.2), mix.ScrubCentreHz[0], 1);
        Assert.InRange(mix.ScrubCentreHz[0], 470, 500);                          // 0.42 × 1531 × 0.4^0.3 = 486.4
        Assert.Equal((float)GroundSoundMix.SlipLevel(2400 * 1.2) * 1.0f, mix.ScrubLevel[0] * 0.7071f / (float)GroundSoundMix.Pan(-0.5).L, 3);
    }

    /// <summary>Ty: bearing noise too loud. 0.0032 at 25.7 m/s rim speed (−14 dB from 0.016), ∝ rpm².</summary>
    [Fact]
    public void BearingWhirIsQuietAndGoesWithTheSquareOfRpm()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        float Whir(double rim)
        {
            var w = new WheelState(c, c.Gear[c.Gear.FindIndex(g => g.Pos[1] < -0.3)]);
            w.OmegaRadS = rim / w.RollingRadiusM;   // in the air: nothing but the bearing
            var mix = new GroundSoundMix(1);
            mix.Update(new[] { w }, 5000, 0.016);
            return mix.WhirGain[0] + mix.WhirGain[1];
        }
        double pan = GroundSoundMix.Pan(-0.5).L + GroundSoundMix.Pan(-0.5).R;
        Assert.Equal(0.0032 * pan, Whir(25.7), 5);
        Assert.Equal(0.25, Whir(12.85) / Whir(25.7), 3);
        Assert.True(20 * Math.Log10(0.016 / 0.0032) > 13.9);
        Assert.True(Whir(5) < 0.0032 * pan * 0.05, "taxiing, the bearing is all but silent");
    }

    /// <summary>
    /// Emergent, in the sim: each taildragger arrives three-point on dry asphalt at its touchdown speed, crabbed 1.5 or 4 m/s
    /// sideways over the ground (a stiff crosswind not fully kicked out). The spin-up must squeal at the tyre's own pitch,
    /// falling as the wheel catches up, and the side slip must scrub, lower. Prints the table.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like", 17.0, 4.0)]
    [InlineData("j3-cub-like", 17.0, 1.5)]
    [InlineData("pa18-cub-like", 18.0, 4.0)]
    [InlineData("decathlon-8kcab-like", 25.0, 4.0)]
    [InlineData("great-lakes-like", 26.0, 4.0)]
    [InlineData("pa25-pawnee-like", 25.0, 4.0)]
    [InlineData("pa18-bush-like", 16.0, 4.0)]
    public void CrabbedTouchdownSquealsAtTheTyresPitchAndScrubsLower(string id, double vTouch, double drift)
    {
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Paved;
        try
        {
            AircraftConfig c = Cfg(id);
            var rest = LandingGear.RestingState(c, 0, 0, WorldTerrain.WheelGroundHeightAt(0, 0));
            Quat att = rest.Attitude;
            Vec3 cg = c.Mass.CgVec();
            double low = double.MinValue;
            foreach (GearConfig g in c.Gear) low = Math.Max(low, att.Rotate(g.PosVec() - cg).Z);
            var pos = new Vec3(0, 0, -(WorldTerrain.WheelGroundHeightAt(0, 0) + low + 0.05));
            Vec3 vb = att.Conjugate().Rotate(new Vec3(vTouch, drift, 0.6));
            var ac = new Aircraft(c, new RigidBodyState(pos, att, vb, Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
            var loop = new SimLoop(ac);
            var mix = new GroundSoundMix(ac.Wheels.Count);
            double weight = ac.MassProperties.MassKg * 9.81;
            int main = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
            double od = ac.Wheels[main].SoundOdM, firstHz = 0, lastHz = 0, chirpS = 0, scrubS = 0, scrubHz = 0, maxChirp = 0, maxScrub = 0;
            for (int f = 0; f < 60; f++)
            {
                loop.RunFor(1.0 / 60, new ControlInputs(0, 0, 0, 0));
                mix.Update(ac.Wheels, weight, 1.0 / 60);
                if (mix.ChirpSeconds[0] > 0)
                {
                    if (firstHz == 0) firstHz = mix.ChirpHz[0];
                    lastHz = mix.ChirpHz[0];
                    chirpS += mix.ChirpSeconds[0]; maxChirp = Math.Max(maxChirp, mix.ChirpGain[0]);
                }
                if (mix.ScrubSeconds[0] > 0) { scrubS += mix.ScrubSeconds[0]; scrubHz = Math.Max(scrubHz, mix.ScrubCentreHz[0]); maxScrub = Math.Max(maxScrub, mix.ScrubLevel[0]); }
            }
            double f0 = GroundSoundMix.SquealResonanceHz(od);
            _o.WriteLine($"{id} drift {drift} m/s: tyre OD {od / 0.0254:F1} in, resonance {f0:F0} Hz | squeal {chirpS * 1000:F0} ms, {firstHz:F0} → {lastHz:F0} Hz, level {maxChirp:F2} | scrub {scrubS * 1000:F0} ms, ≤ {scrubHz:F0} Hz, level {maxScrub:F2}");
            Assert.True(chirpS > 0, "no spin-up squeal");
            Assert.InRange(firstHz, 0.6 * f0, 1.35 * f0);
            Assert.True(lastHz <= firstHz, "the squeal must not rise as the wheel catches up");
            Assert.True(scrubS > 0, "a crabbed touchdown must scrub");
            Assert.True(scrubHz < firstHz, "the scrub is lower than the squeal");
        }
        finally { WorldTerrain.SurfaceOverride = null; }
    }
}
