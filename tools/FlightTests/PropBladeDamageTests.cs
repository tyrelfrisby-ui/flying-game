using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Per-blade prop strikes and what each material does (Ty 2026-11-05 9:04: "prop hits should make accurate sounds on both
/// the ground and water too per blade strike. prop damage should be accurate metal blades curl back if less than zero
/// thrust and curl forward if more than zero thrust wood props shatter with jagged wood at the point they are broken off
/// at but they only break off the parts that hit and then the prop is modeled with a shorter span ... the metal props
/// model only the portion that isn't bent for the thrust ... the vibration is a factor of how much blade span gets
/// affected more span more vibration").
///
/// Literals worked by hand (python, 2026-11-05) from the formulas in PropBladeDamage's documentation, not read from the code:
///  - Performance, J-3 at full static power (2,237.5 rpm, red line 2,575, so the engine can be driven to 1.2·2575/2237.5 =
///    1.381×): one blade of two at 0.9 R₀ → thrust 0.96727, rpm 1.06491; both at 0.5 R₀ → thrust 0.18922, rpm 1.38101
///    (the overspeed limit); a constant-speed three-blade with one at 0.9 R₀ → thrust 0.97843, rpm 1 (the governor takes it
///    up); all three at 0.5 R₀, overspeed 1.2 → thrust 0.28766, rpm 1.2.
///  - The blade's sign of thrust, J-3 (fixed pitch, design 45 m/s at 2,575 rpm, β₀.₇₅ = 17.7°): idle (700 rpm) at 25 m/s on
///    the landing roll → α − α₀ = −5.76°, it is being driven by the air: a struck tip curls AFT; full power standing → +20.67°,
///    it pulls: FORWARD.
/// </summary>
[Collection("WorldTerrainActive")]
public class PropBladeDamageTests
{
    private readonly ITestOutputHelper _out;
    public PropBladeDamageTests(ITestOutputHelper o) { _out = o; }
    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));

    // ---- the fleet's props --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("j3-cub-like", PropMaterial.Wood, 2)]
    [InlineData("j3-cub-floats-like", PropMaterial.Metal, 2)]
    [InlineData("extra-300-like", PropMaterial.Composite, 3)]
    [InlineData("pitts-s2b-like", PropMaterial.Metal, 2)]
    [InlineData("cirrus-sr22-like", PropMaterial.Metal, 3)]
    [InlineData("p51d-like", PropMaterial.Metal, 4)]
    [InlineData("jn4-jenny-like", PropMaterial.Wood, 2)]
    [InlineData("aircam-like", PropMaterial.Composite, 3)]
    public void EveryPropSaysWhatItIsMadeOf(string id, PropMaterial material, int blades)
    {
        AircraftConfig c = Load(id);
        Assert.Equal(material, PropBladeDamage.MaterialOf(c.Propulsion));
        Assert.Equal(blades, PropBladeDamage.BladeCountOf(c.Propulsion));
        Assert.False(string.IsNullOrWhiteSpace(c.Propulsion!.PropMaterialSource));
    }

    [Fact]
    public void EveryPropellerConfigCarriesAMaterial()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.json"))
        {
            string text = File.ReadAllText(f);
            if (!text.Contains("\"propulsion\"")) continue;
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(f);
            if (c.Propulsion is not { PropDiameterM: > 0 }) continue;
            Assert.True(text.Contains("\"propMaterial\""), Path.GetFileName(f) + " has a propeller and no propMaterial");
        }
    }

    // ---- the disc's performance and shake ----------------------------------------------------------------------------

    [Fact]
    public void TheThrustAndRpmOfADamagedDiscAreTheHandWorkedOnes()
    {
        double cap = 1.2 * 2575 / 2237.5;
        (double t, double k) = PropBladeDamage.Performance(new[] { 0.9, 1.0 }, 1.0, false, cap);
        Assert.Equal(0.96727, t, 5); Assert.Equal(1.06491, k, 5);
        (t, k) = PropBladeDamage.Performance(new[] { 0.5, 0.5 }, 1.0, false, cap);
        Assert.Equal(0.18922, t, 5); Assert.Equal(1.38101, k, 5);
        (t, k) = PropBladeDamage.Performance(new[] { 0.9, 1.0, 1.0 }, 1.0, true, 1.2);
        Assert.Equal(0.97843, t, 5); Assert.Equal(1.0, k, 9);
        (t, k) = PropBladeDamage.Performance(new[] { 0.5, 0.5, 0.5 }, 1.0, true, 1.2);
        Assert.Equal(0.28766, t, 5); Assert.Equal(1.2, k, 9);
        (t, k) = PropBladeDamage.Performance(new[] { 1.0, 1.0 }, 1.0, false, cap);
        Assert.Equal(1.0, t); Assert.Equal(1.0, k);
    }

    /// <summary>Thrust falls with every bit of span lost, on one blade or on all of them, fixed pitch or constant speed.</summary>
    [Fact]
    public void ThrustDropsWithLostSpan()
    {
        foreach (bool cs in new[] { false, true })
        {
            double lastOne = 1.0 + 1e-12, lastAll = 1.0 + 1e-12;
            for (double lost = 0.02; lost <= 0.7 + 1e-9; lost += 0.02)
            {
                double one = PropBladeDamage.Performance(new[] { 1 - lost, 1.0 }, 1.0, cs, 1.38).thrustScale;
                double all = PropBladeDamage.Performance(new[] { 1 - lost, 1 - lost }, 1.0, cs, 1.38).thrustScale;
                Assert.True(one < lastOne, $"cs {cs}: one blade {lost:F2} lost: {one:F4} after {lastOne:F4}");
                Assert.True(all < lastAll, $"cs {cs}: both {lost:F2} lost: {all:F4} after {lastAll:F4}");
                Assert.True(all <= one);
                lastOne = one; lastAll = all;
            }
        }
    }

    /// <summary>More span affected, more shake: one blade or both, curled (either way) or broken off.</summary>
    [Fact]
    public void VibrationGrowsWithTheAffectedSpan()
    {
        double r0 = 0.915, m = PropBladeDamage.BladeMassKg(r0, PropMaterial.Metal), w = 2237.5 * Math.PI / 30, thrust = 1500;
        foreach (bool broken in new[] { false, true })
        foreach (int curl1 in new[] { 1, -1 })
        {
            double lastOne = 0, lastBoth = 0;
            for (double l = 0.005; l <= 0.3; l += 0.005)
            {
                PropBladeDamage.Shake one = PropBladeDamage.Imbalance(new[] { r0 - l, r0 }, new[] { l, 0.0 }, new[] { broken ? 0 : 1, 0 },
                    new[] { broken, false }, r0, m, w, thrust);
                PropBladeDamage.Shake both = PropBladeDamage.Imbalance(new[] { r0 - l, r0 - l }, new[] { l, l }, new[] { broken ? 0 : 1, broken ? 0 : curl1 },
                    new[] { broken, broken }, r0, m, w, thrust);
                Assert.True(one.MountLoadN > lastOne, $"broken {broken}: one blade {l:F3} m: {one.MountLoadN:F1} N after {lastOne:F1}");
                Assert.True(both.MountLoadN > lastBoth, $"broken {broken} curl {curl1}: both {l:F3} m: {both.MountLoadN:F1} N after {lastBoth:F1}");
                lastOne = one.MountLoadN; lastBoth = both.MountLoadN;
            }
        }
        PropBladeDamage.Shake straight = PropBladeDamage.Imbalance(new[] { r0, r0 }, new[] { 0.0, 0.0 }, new[] { 0, 0 }, new[] { false, false }, r0, m, w, thrust);
        Assert.Equal(0.0, straight.MountLoadN, 9);
    }

    [Fact]
    public void TheBladesWeighWhatTheirSectionsDo()
    {
        Assert.Equal(1.60, PropBladeDamage.BladeMassKg(0.915, PropMaterial.Metal), 2);
        Assert.Equal(0.56, PropBladeDamage.BladeMassKg(0.915, PropMaterial.Wood), 2);
        Assert.Equal(0.79, PropBladeDamage.BladeMassKg(0.915, PropMaterial.Composite), 2);
        (double f, _) = PropBladeDamage.Outboard(1 - 0.01 / 0.915);
        Assert.Equal(4.8e-3, f * PropBladeDamage.BladeMassKg(0.915, PropMaterial.Wood), 4);
    }

    [Fact]
    public void TheBladeIsPullingUnderPowerAndDrivenAtIdleOnTheLandingRoll()
    {
        PropulsionConfig pc = Load("j3-cub-like").Propulsion!;
        double r0 = 0.915;
        Assert.Equal(-5.76, PropBladeDamage.BladeAlphaRad(pc, r0, 700 * Math.PI / 30, 25, false) * 180 / Math.PI, 2);
        Assert.Equal(20.67, PropBladeDamage.BladeAlphaRad(pc, r0, 2237.5 * Math.PI / 30, 0, false) * 180 / Math.PI, 2);
    }

    // ---- in the sim --------------------------------------------------------------------------------------------------

    /// <summary>The aeroplane held level with its lowest blade tip <paramref name="depth"/> into water at 3 m (or into flat
    /// ground at 0 when <paramref name="water"/> is false) for <paramref name="hold"/> s at a throttle and airspeed, then
    /// lifted 100 m clear for 0.3 s.</summary>
    private (Aircraft ac, List<ContactEvent> ev, List<double> rotorRad) Strike(string id, bool water, double depth, double hold, double throttle,
        double speed = 0, double phaseTimeS = 0.5)
    {
        AircraftConfig c = Load(id);
        WorldTerrain.Active = null;
        double low = AirframeContact.BuildPoints(c).Where(p => p.PropDisc).Max(p => p.Body.Z - c.Mass.CgVec().Z);
        var att = new Quat(0, 0, 0, 1);
        double surface = water ? 3.0 : 0.0;
        var pos = new Vec3(0, 0, -(surface + low - depth));
        var vel = new Vec3(speed, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -100), att, vel, Vec3.Zero), ControlDeflections.Neutral);
        var ev = new List<ContactEvent>(); ac.Contact += ev.Add;
        var sim = new SimLoop(ac);
        var lever = new ControlInputs(0, 0, 0, 1 - 2 * throttle);
        if (water) FloatHydro.FlatWaterOverride = surface;
        var rotor = new List<double>();
        try
        {
            sim.RunFor(0.5, lever);   // the engine up to its rpm, nowhere near the surface
            sim.RunFor(0.005 * Math.Round((phaseTimeS - 0.5) / 0.001), lever);   // where the blades are: a few more 5 ms turns first
            for (double t = 0; t < hold - 1e-9; t += 0.005)
            {
                ac.State = new RigidBodyState(pos, att, vel, Vec3.Zero);
                double w0 = ac.StrikeRotorRpm() * Math.PI / 30;
                sim.RunFor(0.005, lever);
                rotor.Add(w0 * 0.005);
            }
            ac.State = new RigidBodyState(new Vec3(0, 0, -100), att, vel, Vec3.Zero);
            sim.RunFor(0.3, lever);
        }
        finally { FloatHydro.FlatWaterOverride = null; Atmosphere.SimTimeSec = 0; }
        int n = ac.PropBladeCount;
        _out.WriteLine($"{id} {(water ? "water" : "ground")} {depth * 100:F1} cm {hold:F3} s thr {throttle:F1} v {speed:F0}: stopped {ac.EngineStopped}, rpm {ac.EngineRpm:F0}; " +
                       string.Join(", ", Enumerable.Range(0, n).Select(k => $"blade {k} {ac.PropBladeRadiusM(0, k):F4} m aff {ac.PropBladeAffectedM(0, k) * 1000:F1} mm curl {ac.PropBladeCurl(0, k):+0;-0;0} broken {ac.PropBladeBroken(0, k)} strikes {ac.PropBladeStrikes(0, k)}")) +
                       $"; thrust x{ac.PropThrustScale():F4}, vibration {ac.PropVibrationG():F4} g, mount {ac.PropMountLoadN():F0} N; " +
                       $"{ev.Count(e => e.Kind == ContactEventKind.PropBladeStrike)} blade strikes");
        return (ac, ev, rotor);
    }

    /// <summary>A metal tip dipped under full power (standing, the blade pulling) curls FORWARD; the same at idle on the landing
    /// roll (25 m/s, the air driving the blade) curls AFT. Ground and water alike.</summary>
    [Theory]
    [InlineData(true, 0.04, 1.0, 0.0, 1)]
    [InlineData(true, 0.01, 0.0, 25.0, -1)]
    [InlineData(false, 0.01, 1.0, 0.0, 1)]
    [InlineData(false, 0.01, 0.0, 25.0, -1)]
    public void MetalCurlsForwardUnderPowerAndAftAtIdle(bool water, double depth, double throttle, double speed, int curl)
    {
        (Aircraft ac, List<ContactEvent> ev, _) = Strike("j3-cub-floats-like", water, depth, 0.2, throttle, speed);
        Assert.Equal(PropMaterial.Metal, ac.PropBladeMaterial);
        int curled = 0;
        for (int k = 0; k < ac.PropBladeCount; k++)
        {
            if (ac.PropBladeAffectedM(0, k) <= 0) continue;
            curled++;
            Assert.Equal(curl, ac.PropBladeCurl(0, k));
            Assert.False(ac.PropBladeBroken(0, k));
            // The curl is cosmetic: the air sees the straight span, the curl still reaches 0.64 of itself past the bend.
            double l = ac.PropBladeAffectedM(0, k);
            Assert.Equal(0.915 - l, ac.PropBladeRadiusM(0, k), 9);
            Assert.Equal(0.915 - l + l * 2 / Math.PI, ac.PropBladeReachM(0, k), 9);
        }
        Assert.True(curled > 0);
        Assert.All(ev.Where(e => e.Kind == ContactEventKind.PropBladeBent), e => Assert.Equal(curl, (int)e.Intensity));
        Assert.DoesNotContain(ev, e => e.Kind == ContactEventKind.PropBladeBroken);
    }

    /// <summary>A wooden blade brushing the ground for one step, at a dozen instants: whichever blade sweeps the struck arc
    /// loses exactly the span that went in (the tip 1 cm into the ground: 1 cm, at the contact radius), and leaves as a
    /// piece; the other blade is untouched. The engine runs on; the shorter blade flies on.</summary>
    [Fact]
    public void AWoodTipStrikeShortensOnlyTheStruckBladeByTheContactDepth()
    {
        int broke = 0;
        for (int i = 0; i < 12; i++)
        {
            (Aircraft ac, List<ContactEvent> ev, _) = Strike("j3-cub-like", false, 0.01, 0.005, 1.0, 0, 0.5 + i * 0.001);
            Assert.Equal(PropMaterial.Wood, ac.PropBladeMaterial);
            Assert.False(ac.EngineStopped);
            double a0 = 0.915 - ac.PropBladeRadiusM(0, 0), a1 = 0.915 - ac.PropBladeRadiusM(0, 1);
            Assert.Equal(0.0, Math.Min(a0, a1), 9);
            if (Math.Max(a0, a1) <= 0) continue;
            broke++;
            int k = a0 > 0 ? 0 : 1;
            Assert.InRange(Math.Max(a0, a1), 0.0095, 0.0110);   // the 1 cm it went in (the aeroplane settles a hair in a step)
            Assert.True(ac.PropBladeBroken(0, k));
            Assert.False(ac.PropBladeBroken(0, 1 - k));
            Assert.Equal(0, ac.PropBladeCurl(0, k));
            Assert.Equal(ac.PropBladeRadiusM(0, k), ac.PropBladeReachM(0, k), 12);   // a break ends where it broke
            ContactEvent piece = Assert.Single(ev, e => e.Kind == ContactEventKind.PropBladeBroken);
            Assert.Equal(k, piece.Blade);
            Assert.Equal(Math.Max(a0, a1), piece.Value, 9);
            Assert.InRange(piece.Intensity, 0.004, 0.006);   // the outer 1 cm of a 0.915 m birch blade: 4.8 g (PropBladeDamage.BladeMassKg)
            Assert.True(ac.PropThrustScale() < 1.0);
            Assert.True(ac.PropVibrationG() > 0);
        }
        Assert.True(broke >= 2, $"only {broke} of 12 brushes met a blade");
    }

    /// <summary>The Extra's composite blades break off where they hit, in the water as on the ground, and only the blades that
    /// hit: a 2 cm dip for one step at a dozen rotor angles.</summary>
    [Fact]
    public void ACompositeBladeBreaksAtTheContactAndOnlyThere()
    {
        int dips = 0;
        for (int i = 0; i < 12; i++)
        {
            (Aircraft ac, List<ContactEvent> ev, _) = Strike("extra-300-like", true, 0.02, 0.005, 1.0, 0, 0.5 + i * 0.001);
            Assert.Equal(PropMaterial.Composite, ac.PropBladeMaterial);
            Assert.Equal(3, ac.PropBladeCount);
            int broken = Enumerable.Range(0, 3).Count(k => ac.PropBladeBroken(0, k));
            Assert.True(broken < 3, "a fifth of a turn cannot pass all three blades");
            Assert.Equal(broken, ev.Count(e => e.Kind == ContactEventKind.PropBladeBroken));
            if (broken == 0) continue;
            dips++;
            for (int k = 0; k < 3; k++)
            {
                if (!ac.PropBladeBroken(0, k)) { Assert.Equal(1.0, ac.PropBladeRadiusM(0, k), 9); continue; }
                Assert.InRange(1.0 - ac.PropBladeRadiusM(0, k), 0.019, 0.022);   // the 2 cm that went in
                Assert.Equal(0, ac.PropBladeCurl(0, k));
            }
        }
        Assert.True(dips >= 2, $"only {dips} of 12 dips met a blade");
    }

    /// <summary>The deeper a metal tip goes into the water, the more span curls and the harder the airframe shakes.</summary>
    [Fact]
    public void TheShakeInTheSimGrowsWithTheStrike()
    {
        double last = 0;
        foreach (double d in new[] { 0.005, 0.01, 0.02, 0.04 })
        {
            (Aircraft ac, _, _) = Strike("j3-cub-floats-like", true, d, 0.05, 1.0, 0, 0.503);   // a few turns: both blades
            Assert.False(ac.EngineStopped);
            double g = ac.PropVibrationG();
            Assert.True(g > last, $"{d * 100:F1} cm: {g:F4} g after {last:F4}");
            last = g;
        }
    }

    /// <summary>Every pass of every blade through the struck arc is one strike (and one sound): for a held dip, as many as the
    /// rotor's own turns × blades (±1 a blade for where the dip starts and ends), each at its own instant in the step,
    /// spaced a blade-pass apart, all on the surface that was hit.</summary>
    [Theory]
    [InlineData("j3-cub-floats-like", true, 0.04)]
    [InlineData("pitts-s2b-like", false, 0.005)]
    [InlineData("extra-300-like", true, 0.0003)]   // under the composite blade's 0.5 mm failing depth: it strikes, and nothing breaks
    public void OneStrikeSoundPerBladeContact(string id, bool water, double depth)
    {
        (Aircraft ac, List<ContactEvent> ev, List<double> rotor) = Strike(id, water, depth, 0.5, 1.0);
        var strikes = ev.Where(e => e.Kind == ContactEventKind.PropBladeStrike).ToList();
        int n = ac.PropBladeCount;
        double turns = rotor.Sum() / (2 * Math.PI);
        // Blades that still reach the surface after their first pass: a curled metal tip does (0.64 of its curl), and so
        // does every blade of a dip shallower than the water's failing depth.
        int reaching = Enumerable.Range(0, n).Count(k => ac.PropBladeStrikes(0, k) > 1);
        _out.WriteLine($"{turns:F2} turns, {reaching} blades kept reaching, {strikes.Count} strikes");
        Assert.Equal(n, reaching);
        Assert.InRange(strikes.Count, (int)Math.Floor(turns * n) - n, (int)Math.Ceiling(turns * n) + n);
        Assert.Equal(strikes.Count, Enumerable.Range(0, n).Sum(k => ac.PropBladeStrikes(0, k)));
        Assert.All(strikes, e => Assert.Equal(water, e.SurfaceCode == ContactPoint.SurfaceWater));
        for (int i = 1; i < strikes.Count; i++) Assert.True(strikes[i].TimeSec >= strikes[i - 1].TimeSec, "strikes out of order");
        // Each blade's own strikes come once a turn: consecutive passes of blade 0 are one revolution apart.
        var b0 = strikes.Where(e => e.Blade == 0).Select(e => e.TimeSec).ToList();
        for (int i = 2; i < b0.Count; i++)
        {
            double rev = b0[i] - b0[i - 1], rpm = 60.0 / rev;
            Assert.InRange(rpm, 200, 3000);
        }
        // Blade k follows blade k − 1 round (never two blades at once).
        Assert.All(strikes.Zip(strikes.Skip(1)), p => Assert.NotEqual(p.First.Blade, p.Second.Blade));
    }
}
