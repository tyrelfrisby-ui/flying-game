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
/// The tread (Ty 2026-11-04, Q026: keep the 172's lock-up; a locked tyre screeches, smokes, wears through and bursts,
/// and a flat drags, sits the aeroplane down on its side and thumps). TyreWear, docs/TIRE-SPINUP-AND-GROUND-SOUND.md §8.
/// Nothing here tells the tyre it is locked: it is the same wear law on every strip of tread, and the lock is the wheel's.
/// </summary>
[Collection("WorldTerrainActive")]
public class TyreWearTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public TyreWearTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }
    public void Dispose() { WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Aircraft OnRunway(string id, double speed)
    {
        var c = Cfg(id);
        var st = LandingGear.RestingState(c, 0, 0, WorldTerrain.WheelGroundHeightAt(0, 0));
        var vBody = st.Attitude.Conjugate().Rotate(new Vec3(speed, 0, 0));
        return new Aircraft(c, new RigidBodyState(st.Position, st.Attitude, vBody, Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
    }

    private static double GroundSpeed(Aircraft ac) { Vec3 v = ac.State.Attitude.Rotate(ac.State.Velocity); return Math.Sqrt(v.X * v.X + v.Y * v.Y); }

    /// <summary>A C172 on the runway at <paramref name="speed"/>, idle, full pedal held until it stops (or 20 s). Returns
    /// the worst main tyre's deepest wear, whether either burst, and how far it had skidded (m) when one did.</summary>
    private (double wornMm, bool burst, double burstAtM, double distM, double abradedCm3, double peakRiseK) FullPedalStop(
        double speed, WorldTerrain.Surface surface, double pedal = 1.0)
    {
        WorldTerrain.SurfaceOverride = surface;
        try
        {
            Aircraft ac = OnRunway("c172-like", speed);
            var sim = new SimLoop(ac);
            double dist = 0, burstAt = double.NaN, abraded = 0, rise = 0;
            for (int i = 0; i < 2000 && GroundSpeed(ac) > 0.3; i++)
            {
                ac.BrakeInput = pedal;
                double v0 = GroundSpeed(ac);
                sim.RunFor(0.01, new ControlInputs(0, 0, 0, 0.0));
                dist += 0.5 * (v0 + GroundSpeed(ac)) * 0.01;
                for (int g = 0; g < ac.Wheels.Count; g++)
                {
                    WheelState w = ac.Wheels[g];
                    if (!ac.Config.Gear[g].Brake || w.Tyre == null) continue;
                    abraded += w.AbradedM3; w.AbradedM3 = 0;
                    rise = Math.Max(rise, w.Tyre.PatchRiseK);
                    if (w.Tyre.Burst && double.IsNaN(burstAt)) burstAt = dist;
                }
            }
            double worn = ac.Wheels.Where((w, g) => ac.Config.Gear[g].Brake && w.Tyre != null).Max(w => w.Tyre!.MaxWornM);
            bool burst = ac.Wheels.Any(w => w.Tyre != null && w.Tyre.Burst);
            return (worn * 1000, burst, burstAt, dist, abraded * 1e6, rise);
        }
        finally { WorldTerrain.SurfaceOverride = null; }
    }

    /// <summary>Ty: "a flat flaps-up landing with lockup can reasonably flat the tires". A 172 that touches down flapless
    /// (~60 kt) and stands on the pedals locks its mains on dry asphalt and wears through; the same pedal at ~25 kt
    /// flat-spots the tyres and leaves them up; on grass (soft, cool, μ 0.52) the lock does no real harm.</summary>
    [Fact]
    public void ALockedFlaplessLandingBurstsTheMainsButASlowLockOnlyFlatSpotsThem()
    {
        var fast = FullPedalStop(31, WorldTerrain.Surface.Paved);
        var slow = FullPedalStop(13, WorldTerrain.Surface.Paved);
        var grass = FullPedalStop(28, WorldTerrain.Surface.Grass);
        foreach (var (name, r) in new[] { ("asphalt 60 kt", fast), ("asphalt 25 kt", slow), ("grass 55 kt", grass) })
            _o.WriteLine($"{name}: worn {r.wornMm:F2} mm, burst {r.burst} at {r.burstAtM:F0} m of {r.distM:F0} m, rubber {r.abradedCm3:F0} cm3, peak rise {r.peakRiseK:F0} K");
        Assert.True(fast.burst, "a locked flapless landing should wear through");
        Assert.False(slow.burst);
        Assert.InRange(slow.wornMm, 0.3, 9.0);      // a flat spot you can see, not through
        Assert.False(grass.burst);
        Assert.True(grass.wornMm < 0.5 * slow.wornMm, "grass is soft and cool: much less wear");
    }

    /// <summary>Braking hard WITHOUT locking (60 % pedal) does no visible harm: the slip is a few per cent, the rubber stays
    /// cold, and the work is spread round the whole tread.</summary>
    [Fact]
    public void HardBrakingWithoutLockingDoesNotWearTheTyre()
    {
        var r = FullPedalStop(25, WorldTerrain.Surface.Paved, 0.6);
        _o.WriteLine($"60 % pedal from 49 kt: worn {r.wornMm:F4} mm, rubber {r.abradedCm3:F2} cm3, peak rise {r.peakRiseK:F0} K");
        Assert.False(r.burst);
        Assert.True(r.wornMm < 0.05, $"{r.wornMm:F3} mm");
    }

    /// <summary>A normal arrival: the spin-up scrubs a little rubber (the puff of smoke you see) off the arc the wheel turned
    /// while it slipped, ~a few hundredths of a mm — a few hundred landings of tread — and nothing more.</summary>
    [Fact]
    public void ASpinUpScrubsAFewHundredthsOfAMillimetre()
    {
        double V = 50 * 0.514444, sink = 3 * 0.3048;
        AircraftConfig c = Cfg("c172-like");
        double pitch = 6 * Math.PI / 180;
        var att = new Quat(0, Math.Sin(pitch / 2), 0, Math.Cos(pitch / 2));
        Vec3 cg = c.Mass.CgVec();
        double low = double.MinValue;
        foreach (GearConfig g in c.Gear) low = Math.Max(low, att.Rotate(g.PosVec() - cg).Z);
        var pos = new Vec3(0, 0, -(WorldTerrain.WheelGroundHeightAt(0, 0) + low + 0.05));
        var a = new Aircraft(c, new RigidBodyState(pos, att, att.Conjugate().Rotate(new Vec3(V, 0, sink)), Vec3.Zero), new ControlDeflections(0, 0, 0, 0));
        var loop = new SimLoop(a);
        double abraded = 0;
        for (int i = 0; i < 100; i++) { loop.RunFor(0.005, new ControlInputs(0, 0, 0, 1.0)); foreach (WheelState w in a.Wheels) { abraded += w.AbradedM3; w.AbradedM3 = 0; } }
        WheelState main = a.Wheels.First(w => w.Tyre != null && w.Side != 0);
        int worn = main.Tyre!.WornM.Count(h => h > 1e-7);
        _o.WriteLine($"spin-up: max worn {main.Tyre.MaxWornM * 1000:F4} mm over {worn} of {TyreWear.Bins} strips, rubber (all wheels) {abraded * 1e6:F2} cm3");
        Assert.DoesNotContain(a.Wheels, w => w.Tyre != null && w.Tyre.Burst);
        Assert.InRange(main.Tyre.MaxWornM * 1000, 0.003, 0.1);
        Assert.True(worn >= 6, "the spin-up's wear is smeared along the arc it turned, not one spot");
    }

    /// <summary>The flat spot is a flat SPOT: a wheel locked in one place wears the strips in its patch and none of the others.</summary>
    [Fact]
    public void ALockedWheelWearsOnlyThePatch()
    {
        AircraftConfig c = Cfg("c172-like");
        GearConfig g = c.Gear.First(x => x.Brake);
        var t = new TyreWear(c, g, LandingGear.TireRadiusOf(c, g), 3200);
        double work = 0.64 * 3200 * 20 * 0.005;                  // μk·N·v·dt, locked at 20 m/s
        for (int i = 0; i < 100; i++) t.Abrade(work, 20, 3200, 1.0, 1.0, 0.005, WorldTerrain.Surface.Paved);
        double l = t.PatchLengthM(3200, t.InflationPa);
        int worn = t.WornM.Count(h => h > 0);
        int expect = (int)Math.Round(l / (2 * Math.PI * t.RadiusM / TyreWear.Bins));
        _o.WriteLine($"patch {l * 100:F1} cm, {worn} strips worn (≈ {expect}), max {t.MaxWornM * 1000:F2} mm, rise {t.PatchRiseK:F0} K");
        Assert.InRange(worn, expect - 1, expect + 2);
        Assert.True(t.PatchRiseK > TyreWear.ReversionRiseK);
    }

    /// <summary>The wear law conserves the work: with the rubber at one temperature, the volume torn off is γ times the
    /// friction work, however the patch moved over the strips.</summary>
    [Fact]
    public void TheVolumeTornOffIsGammaTimesTheWork()
    {
        AircraftConfig c = Cfg("c172-like");
        GearConfig g = c.Gear.First(x => x.Brake);
        var t = new TyreWear(c, g, LandingGear.TireRadiusOf(c, g), 3200);
        // Slow sliding: cold rubber, γ = γ_cold everywhere.
        double v = 0.2, work = 0.6 * 3200 * v * 0.005, vol = 0;
        for (int i = 0; i < 50; i++) vol += t.Abrade(work, v, 3200, -0.03 * i, -0.03 * (i + 1), 0.005, WorldTerrain.Surface.Paved);
        Assert.True(t.PatchRiseK < TyreWear.SoftenRiseK);
        Assert.Equal(50 * work * TyreWear.GammaColdM3PerJ, vol, 6 * 50 * work * TyreWear.GammaColdM3PerJ * 1e-6 + 1e-18);
    }

    /// <summary>The friction flux per patch area is μ·p·v whatever the load (the load sizes the patch), so a locked tyre's
    /// life is a DISTANCE of tens of metres (NASA TN D-6963), nearly the same for a light and a heavy one at the same
    /// pressure. Not exactly: a longer patch heats the ground under it for longer, so it runs hotter (Blok: the flash
    /// temperature goes with √(l/v)) and the heavier tyre goes through somewhat sooner.</summary>
    [Fact]
    public void ALockedTyresLifeIsADistanceWhateverItCarries()
    {
        AircraftConfig c = Cfg("c172-like");
        GearConfig g = c.Gear.First(x => x.Brake);
        double Dist(double n)
        {
            var t = new TyreWear(c, g, LandingGear.TireRadiusOf(c, g), 4000);
            double v = 25, dt = 0.005, d = 0;
            while (!t.Burst && d < 2000) { t.Abrade(0.64 * n * v * dt, v, n, 0, 0, dt, WorldTerrain.Surface.Paved); d += v * dt; }
            return d;
        }
        double light = Dist(2000), heavy = Dist(5000);
        _o.WriteLine($"locked at 25 m/s, wears through after {light:F1} m at 2 kN, {heavy:F1} m at 5 kN");
        Assert.InRange(heavy / light, 0.6, 1.0);
        Assert.InRange(light, 20, 150);
    }

    /// <summary>
    /// A FLAT left main on a C172 at rest and rolling: the aeroplane sits down on its left side (the wheel stands on its
    /// rim and casing), it takes far more to keep it rolling (the casing's hysteresis), and the load on the flat wheel
    /// goes lumpy once a revolution as the worn-through strip passes under the axle.
    /// </summary>
    [Fact]
    public void AFlatMainTiltsTheAeroplaneDragsAndThumpsOnceARevolution()
    {
        (double rollDeg, double decel, double loadSpread, double thumpJ, double revHz) Run(bool flat)
        {
            Aircraft ac = OnRunway("c172-like", 0);
            int left = Enumerable.Range(0, ac.Wheels.Count).First(i => ac.Wheels[i].Side < 0);
            if (flat)
            {
                TyreWear t = ac.Wheels[left].Tyre!;
                // Wear it through at one spot, the way a lock does (the same law; just quicker than a test wants to run).
                while (!t.Burst) t.Abrade(0.64 * 3200 * 25 * 0.005, 25, 3200, 0.5, 0.5, 0.005, WorldTerrain.Surface.Paved);
                for (int i = 0; i < 100; i++) t.Leak(0.01);
            }
            var sim = new SimLoop(ac);
            sim.RunFor(3.0, new ControlInputs(0, 0, 0, 0.0));
            // Roll: the right wing's axis in the world (NED, z down): left wing down reads negative.
            double roll = Math.Asin(Math.Clamp(ac.State.Attitude.Rotate(new Vec3(0, 1, 0)).Z, -1, 1)) * 180 / Math.PI;
            // Now push it along at 8 m/s and let it roll at idle: how fast does it slow, and how lumpy is the flat wheel?
            ac.State = new RigidBodyState(ac.State.Position, ac.State.Attitude, ac.State.Attitude.Conjugate().Rotate(new Vec3(8, 0, 0)), Vec3.Zero);
            sim.RunFor(0.5, new ControlInputs(0, 0, 0, 0.0));
            double v0 = GroundSpeed(ac), lo = double.MaxValue, hi = 0, thump = 0;
            for (int i = 0; i < 200; i++)
            {
                sim.RunFor(0.005, new ControlInputs(0, 0, 0, 0.0));
                double n = ac.Wheels[left].LoadN; lo = Math.Min(lo, n); hi = Math.Max(hi, n);
                thump += ac.Wheels[left].ThumpJ; ac.Wheels[left].ThumpJ = 0;
            }
            double decel = (v0 - GroundSpeed(ac)) / 1.0;
            double rev = GroundSpeed(ac) / (2 * Math.PI * ac.Wheels[left].RollingRadiusM);
            return (roll, decel, (hi - lo) / Math.Max(1, 0.5 * (hi + lo)), thump, rev);
        }
        var sound = Run(false);
        var flatR = Run(true);
        _o.WriteLine($"sound: roll {sound.rollDeg:F2} deg, coasting decel {sound.decel:F3} m/s2, left load spread {sound.loadSpread:P0}, thump {sound.thumpJ:F1} J");
        _o.WriteLine($"flat:  roll {flatR.rollDeg:F2} deg, coasting decel {flatR.decel:F3} m/s2, left load spread {flatR.loadSpread:P0}, thump {flatR.thumpJ:F1} J at {flatR.revHz:F1} rev/s");
        // NED body roll: left wing down is negative.
        Assert.True(flatR.rollDeg < sound.rollDeg - 1.5, "the aeroplane should sit down on the flat side");
        // Both run at idle, whose thrust pushes them along; the difference is the flat's extra drag.
        Assert.True(flatR.decel - sound.decel > 0.3, "a flat should drag");
        Assert.True(flatR.loadSpread > sound.loadSpread + 0.2, "a flat should roll lumpily");
        Assert.True(flatR.thumpJ > 10 * Math.Max(sound.thumpJ, 0.1));
    }

    /// <summary>The ear: one pop when a tyre goes, a slap at the flat wheel's revolution rate, nothing from a sound tyre.</summary>
    [Fact]
    public void TheMixPopsOnceAndFlapsAtTheRevolutionRate()
    {
        AircraftConfig c = Cfg("c172-like");
        WheelState[] w = WheelState.For(c);
        int left = Enumerable.Range(0, w.Length).First(i => w[i].Side < 0);
        foreach (WheelState x in w) { x.InContact = true; x.LoadN = 3000; x.VFwdMs = 10; x.Material = GroundMaterial.Asphalt; x.OmegaRadS = 10 / x.RadiusM; }
        var mix = new GroundSoundMix(w.Length);
        mix.Update(w, 10000, 0.016);
        Assert.Equal(0, mix.Pops[0] + mix.Pops[1]);
        Assert.Equal(0f, mix.FlapGain[0]);
        TyreWear t = w[left].Tyre!;
        while (!t.Burst) w[left].Wear(0.64 * 3200 * 25 * 0.005, 25, 3200, 0, 0, 0.005);
        for (int i = 0; i < 100; i++) t.Leak(0.01);
        mix.Update(w, 10000, 0.016);
        Assert.Equal(1, mix.Pops[0]);
        Assert.Equal(0, mix.Pops[1]);
        mix.Update(w, 10000, 0.016);
        Assert.Equal(0, mix.Pops[0]);
        Assert.True(mix.FlapGain[0] > 0.5f && mix.FlapGain[1] == 0f);
        Assert.Equal(10 / (2 * Math.PI * w[left].RollingRadiusM), mix.FlapHz[0], 3);
    }
}
