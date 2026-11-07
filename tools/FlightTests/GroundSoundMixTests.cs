using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The tyre / brake / ground sound decisions (Core GroundSoundMix; Bridge/Audio only renders them): the crossfade
/// between grounds under each wheel, the stereo pan, and what is NOT heard where (Ty 2026-11-04: no whir or chirp on
/// water or snow, no brakes on skis or floats).
/// </summary>
public class GroundSoundMixTests
{
    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static WheelState Rolling(AircraftConfig c, int gear, GroundMaterial m, double v, double load = 3000)
    {
        var w = new WheelState(c, c.Gear[gear]);
        w.InContact = true; w.Material = m; w.LoadN = load; w.VFwdMs = v; w.OmegaRadS = v / w.RadiusM;
        return w;
    }

    /// <summary>Ty 2026-11-05: every ground-contact voice −4 dB through one shared gain (literal copied by hand).</summary>
    [Fact]
    public void GroundBusIsFourDecibelsDown()
    {
        Assert.Equal(-4.0, GroundSoundMix.OutputGainDb);
        Assert.Equal(0.630957, GroundSoundMix.OutputGain, 5);
    }

    /// <summary>Ty 2026-11-05 ~15:57: the tyre screech keeps its loudness from before the −4 dB (literal by hand).</summary>
    [Fact]
    public void TyreScreechSkipsTheGroundBusCut()
    {
        Assert.Equal(0.0, GroundSoundMix.ScreechGainDb);
        Assert.Equal(1.0, GroundSoundMix.ScreechGain, 6);
    }

    [Fact]
    public void CrossfadeWeightsSumToOneAndAreEqualPower()
    {
        var mix = new GroundSoundMix(1);
        mix.Crossfade(0, GroundMaterial.Asphalt, 0.016);
        Assert.Equal(1.0, mix.Weight(0, GroundMaterial.Asphalt), 12);
        double tAt = -1;
        for (int f = 1; f <= 120; f++)
        {
            mix.Crossfade(0, GroundMaterial.Grass, 0.016);
            double sum = 0, power = 0;
            for (int k = 0; k < GroundSoundMix.Keys; k++)
            {
                sum += mix.Weight(0, (GroundMaterial)k);
                power += Math.Pow(mix.EqualPower(0, (GroundMaterial)k), 2);
            }
            Assert.Equal(1.0, sum, 9);
            Assert.Equal(1.0, power, 9);   // constant loudness through the fade
            if (tAt < 0 && mix.Weight(0, GroundMaterial.Grass) >= 0.5) tAt = f * 0.016;
        }
        // Over the rows' 0.20 / 0.25 s, not instantly and not seconds: half-way in ~0.1–0.3 s.
        Assert.InRange(tAt, 0.08, 0.3);
        Assert.True(mix.Weight(0, GroundMaterial.Grass) > 0.99);
        // At the half-way point both voices are at 1/√2, the equal-power crossing.
        var m2 = new GroundSoundMix(1);
        m2.Crossfade(0, GroundMaterial.Asphalt, 0.001);
        while (m2.Weight(0, GroundMaterial.Gravel) < 0.5) m2.Crossfade(0, GroundMaterial.Gravel, 0.0005);
        Assert.InRange(m2.EqualPower(0, GroundMaterial.Gravel), 0.70, 0.72);
        Assert.InRange(m2.EqualPower(0, GroundMaterial.Asphalt), 0.69, 0.71);
    }

    [Fact]
    public void PanIsEqualPowerAndByTheWheelsSide()
    {
        foreach (double p in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
        {
            var (l, r) = GroundSoundMix.Pan(p);
            Assert.Equal(1.0, l * l + r * r, 12);
        }
        AircraftConfig c = Cfg("c172-like");
        int left = c.Gear.FindIndex(g => g.Pos[1] < -0.3), right = c.Gear.FindIndex(g => g.Pos[1] > 0.3);
        var mix = new GroundSoundMix(c.Gear.Count);
        var wheels = new WheelState[c.Gear.Count];
        for (int i = 0; i < wheels.Length; i++) wheels[i] = new WheelState(c, c.Gear[i]);
        wheels[left] = Rolling(c, left, GroundMaterial.Asphalt, 20);
        wheels[right] = Rolling(c, right, GroundMaterial.Grass, 20);
        mix.Update(wheels, 10000, 0.016);
        int keys = GroundSoundMix.Keys;
        Assert.True(mix.RollingGain[(int)GroundMaterial.Asphalt] > 2 * mix.RollingGain[keys + (int)GroundMaterial.Asphalt], "asphalt is under the LEFT wheel");
        Assert.True(mix.RollingGain[keys + (int)GroundMaterial.Grass] > 2 * mix.RollingGain[(int)GroundMaterial.Grass], "grass is under the RIGHT wheel");
    }

    [Theory]
    [InlineData(GroundMaterial.Water)]
    [InlineData(GroundMaterial.Snow)]
    [InlineData(GroundMaterial.PackedSnow)]
    public void NoWhirNoChirpNoBrakesOnWaterOrSnow(GroundMaterial m)
    {
        AircraftConfig c = Cfg("j3-cub-like");
        int main = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        var w = Rolling(c, main, m, 25);
        w.OverWater = m == GroundMaterial.Water;
        w.Brake01 = 1.0; w.SlipRatio = 0.5;
        w.SlipEnergyJ = 500; w.SlipTimeS = 0.04; w.SlipImpulseNs = 25;   // a full spin-up's worth of slip
        w.SideSlipEnergyJ = 200; w.SideSlipTimeS = 0.04; w.SideSlipImpulseNs = 70;
        var mix = new GroundSoundMix(1);
        mix.Update(new[] { w }, 5000, 0.016);
        for (int s = 0; s < 2; s++)
        {
            Assert.Equal(0f, mix.WhirGain[s]);
            Assert.Equal(0f, mix.ChirpSeconds[s]);
            Assert.Equal(0f, mix.ChirpGain[s]);
            Assert.Equal(0f, mix.ScrubSeconds[s] + mix.ScrubLevel[s]);
            Assert.Equal(0f, mix.ScrapeGain[s]);
            Assert.Equal(0f, mix.SquealGain[s]);
        }
        if (m == GroundMaterial.Water) Assert.True(mix.WaterHiss + mix.WaterSplash > 0, "a wheel in the water makes water noise");
        else Assert.True(mix.RollingGain[(int)m] + mix.RollingGain[GroundSoundMix.Keys + (int)m] > 0, "a wheel on snow crunches");
    }

    [Fact]
    public void OnSkisOnlyTheSkiHisses()
    {
        AircraftConfig c = Cfg("pa18-cub-like");
        int main = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        var w = Rolling(c, main, GroundMaterial.Snow, 20);
        w.OnSki = true; w.Brake01 = 1; w.SlipEnergyJ = 300; w.SlipTimeS = 0.03;
        var mix = new GroundSoundMix(1);
        mix.Update(new[] { w }, 5000, 0.016);
        Assert.True(mix.SkiHiss > 0);
        Assert.Equal(0f, mix.WhirGain[0] + mix.WhirGain[1]);
        Assert.Equal(0f, mix.ChirpSeconds[0] + mix.ChirpSeconds[1]);
        Assert.Equal(0f, mix.ScrapeGain[0] + mix.ScrapeGain[1] + mix.SquealGain[0] + mix.SquealGain[1]);
    }

    /// <summary>The chirp plays the slip the tyre actually made, for as long as it made it — and it is the ground's: full
    /// on asphalt, nothing on grass.</summary>
    [Fact]
    public void ChirpIsTheSpinUpsOwnSlipAndTheGroundsToGive()
    {
        AircraftConfig c = Cfg("c172-like");
        int left = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        foreach (var (m, heard) in new[] { (GroundMaterial.Asphalt, true), (GroundMaterial.Grass, false) })
        {
            var w = Rolling(c, left, m, 25);
            w.SlipEnergyJ = 900; w.SlipTimeS = 0.031; w.SlipImpulseNs = 45; w.SlipRatio = 0.3;   // sliding at 20 m/s
            var mix = new GroundSoundMix(1);
            mix.Update(new[] { w }, 10000, 0.016);
            if (heard)
            {
                Assert.Equal(0.031f, mix.ChirpSeconds[0], 5);          // the duration is the spin-up's
                Assert.Equal(0f, mix.ChirpSeconds[1]);                  // left wheel: left channel
                Assert.True(mix.ChirpGain[0] > 0.3f);
            }
            else Assert.Equal(0f, mix.ChirpSeconds[0] + mix.ChirpSeconds[1]);
            Assert.Equal(0, w.SlipEnergyJ);                              // consumed
        }
    }

    [Fact]
    public void BrakeScrapeGoesWithPressureTimesSpeedAndSquealOnlyWhenHard()
    {
        AircraftConfig c = Cfg("c172-like");
        int left = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        float Scrape(double p, double v, out float squeal)
        {
            var w = Rolling(c, left, GroundMaterial.Asphalt, v);
            w.Brake01 = p;
            var mix = new GroundSoundMix(1);
            mix.Update(new[] { w }, 10000, 0.016);
            squeal = mix.SquealGain[0];
            return mix.ScrapeGain[0];
        }
        float a = Scrape(0.3, 5, out float sq1), b = Scrape(0.6, 5, out _), d = Scrape(0.3, 10, out _);
        Assert.Equal(2 * a, b, 4);
        Assert.Equal(2 * a, d, 4);
        Assert.Equal(0f, sq1);
        Scrape(0.9, 8, out float sq2);
        Assert.True(sq2 > 0);
        Scrape(0.9, 0.1, out float sqStopped);
        Assert.Equal(0f, sqStopped);   // fades as the wheel stops
    }

    [Fact]
    public void WhirFollowsTheWheelsRpmAndFadesAsItSlows()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        int left = c.Gear.FindIndex(g => g.Pos[1] < -0.3);
        var w = new WheelState(c, c.Gear[left]);
        w.OmegaRadS = 25.7 / w.RadiusM;   // just after lift-off at 50 kt, in the air
        var mix = new GroundSoundMix(1);
        mix.Update(new[] { w }, 5000, 0.016);
        float g0 = mix.WhirGain[0], hz0 = mix.WhirHz[0];
        Assert.InRange(hz0, 9 * 25.7 / (2 * Math.PI * w.RadiusM) * 0.99, 9 * 25.7 / (2 * Math.PI * w.RadiusM) * 1.01);
        for (int i = 0; i < 200 * 20; i++) w.Coast(0.005);   // 20 s in its bearings
        mix.Update(new[] { w }, 5000, 0.016);
        Assert.True(mix.WhirGain[0] < 0.6f * g0 && mix.WhirGain[0] > 0, $"whir {mix.WhirGain[0]} vs {g0}");
        Assert.True(mix.WhirHz[0] < hz0);
    }
}
