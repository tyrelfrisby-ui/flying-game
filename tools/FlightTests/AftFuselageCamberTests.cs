using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// AFT-FUSELAGE SIDESLIP CAMBER IN CROSSFLOW (CrossflowConfig.AftCamberCl, Ty 2026-11-11: "Fuselage crossflow should
/// trigger the sideslip camber on the aft fuselage and produce a pronounced negative pitching moment on the pitts and
/// extra which should result in flatter inverted spins than upright").
///
/// These pin the term's STRUCTURE — the parts that are forced by the physics and by what it must not disturb — rather
/// than any fitted magnitude. They are deliberately all static (no spin is flown here), so they stay fast and so a
/// later retune of the coefficient cannot quietly break the shape of the law.
/// </summary>
public class AftFuselageCamberTests
{
    private readonly ITestOutputHelper _out;
    public AftFuselageCamberTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        return AircraftConfigLoader.LoadFromFile(Path.Combine(d!.FullName, "configs", "aircraft", id + ".json"));
    }

    /// <summary>The pitching moment the term alone contributes: the full model with it on, minus the same with Cl 0.</summary>
    private static double CamberPitch(AircraftConfig cfg, double alphaDeg, double betaDeg, double yawRate, double cl = 1.0)
    {
        var tables = Aircraft.BuildAirfoilTables(cfg);
        double a = alphaDeg * Math.PI / 180, b = betaDeg * Math.PI / 180;
        const double V = 50.0;
        var v = new Vec3(V * Math.Cos(a) * Math.Cos(b), V * Math.Sin(b), V * Math.Sin(a) * Math.Cos(b));
        var om = new Vec3(0, 0, yawRate);
        double M(double c)
        {
            cfg.Fuselage.Crossflow.AftCamberCl = c;
            return AeroModel.Compute(cfg, tables, v, om, Vec3.Zero, 1.0, new ControlDeflections(0, 0, 0, 0)).Moment.Y;
        }
        double on = M(cl), off = M(0.0);
        cfg.Fuselage.Crossflow.AftCamberCl = 0.0;
        return on - off;
    }

    /// <summary>
    /// THE SIGN IS THE TEST (Ty's prediction): one term, opposite consequences. The aft body is always lifted, and it
    /// is always behind the CG, so the body-axis moment is always NOSE-DOWN. Upright that steepens the spin; inverted
    /// the aeroplane is the other way up, so the same body-axis moment carries α away from −90 the other way and the
    /// spin comes out FLATTER. A term that pitched the same way in earth axes both ways up would be the wrong one.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void ItIsNoseDownInBodyAxesEitherWayUp(string id)
    {
        var cfg = Load(id);
        double up = CamberPitch(cfg, 80, 25, 0), inv = CamberPitch(cfg, -80, 25, 0);
        _out.WriteLine($"{id}: alpha +80 beta 25 -> {up:F0} N.m    alpha -80 beta 25 -> {inv:F0} N.m");
        Assert.True(up < -1.0, $"upright: expected nose-down, got {up:F1} N.m");
        Assert.True(inv < -1.0, $"inverted: expected nose-down, got {inv:F1} N.m");
        // Even in alpha: the body does not know which way up it is.
        Assert.Equal(up, inv, 6);
    }

    /// <summary>
    /// ZERO AT alpha 0, which is what keeps SideslipCamberTests' pinned normal-flight numbers untouched. The published
    /// crossflow share this term carries is sin^2(alpha); at wings-level sideslip there is none of it.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void ItIsSilentInOrdinarySideslip(string id)
    {
        var cfg = Load(id);
        Assert.Equal(0.0, CamberPitch(cfg, 0, 15, 0), 9);
        Assert.Equal(0.0, CamberPitch(cfg, 0, 30, 0), 9);
        // And small, not merely zero at the point. Measured against the term it extends rather than against an
        // absolute number, because that is the comparison that means anything: at a realistic slipping approach it
        // must be a rounding error on the existing camber moment that SideslipCamberTests pins.
        double mine = Math.Abs(CamberPitch(cfg, 5, 15, 0));
        var bare = Load(id);
        bare.Fuselage.Crossflow.AftCamberCl = 0.0;
        var tables = Aircraft.BuildAirfoilTables(bare);
        double a = 5 * Math.PI / 180, b = 15 * Math.PI / 180;
        const double V = 50.0;
        var vel = new Vec3(V * Math.Cos(a) * Math.Cos(b), V * Math.Sin(b), V * Math.Sin(a) * Math.Cos(b));
        double With = AeroModel.Compute(bare, tables, vel, Vec3.Zero, Vec3.Zero, 1.0, new ControlDeflections(0, 0, 0, 0)).Moment.Y;
        bare.Fuselage.Crossflow.SectionCamberCl = 0.0;
        bare.Fuselage.Crossflow.CowlCamberCl = 0.0;
        double Without = AeroModel.Compute(bare, Aircraft.BuildAirfoilTables(bare), vel, Vec3.Zero, Vec3.Zero, 1.0, new ControlDeflections(0, 0, 0, 0)).Moment.Y;
        double existing = Math.Abs(With - Without);
        _out.WriteLine($"{id}: alpha 5 beta 15 -> this term {-mine:F2} N.m against the existing camber's {-existing:F0} N.m ({100 * mine / existing:F1} %)");
        Assert.True(mine / existing < 0.03, $"{mine:F1} N.m is {100 * mine / existing:F1} % of the existing camber term at a slipping approach");
    }

    /// <summary>ZERO AT beta 0: it is a SIDESLIP camber, and a pitching moment at zero sideslip would move every
    /// type's stall break. This is the limit the bare published "+ sin^2 alpha" form does NOT have, and the reason
    /// this term carries the sin^2 beta weight as well — see the field's doc comment.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void ItIsSilentAtZeroSideslip(string id)
    {
        var cfg = Load(id);
        foreach (double a in new[] { 20.0, 60.0, 80.0, -80.0 })
            Assert.Equal(0.0, CamberPitch(cfg, a, 0, 0), 9);
    }

    /// <summary>Even in beta, like the camber term it extends: an asymmetric section pitches the same way whichever
    /// side the air comes from.</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    public void ItIsTheSameSlippingLeftOrRight(string id)
    {
        var cfg = Load(id);
        Assert.Equal(CamberPitch(cfg, 80, 25, 0), CamberPitch(cfg, 80, -25, 0), 6);
    }

    /// <summary>
    /// THE ROTATION TERM IS THE POINT IN A SPIN. In a developed spin the CG is near the spin axis and the body
    /// sideslip is modest, but the aft body is swung sideways through the air at r times its arm. The local lateral
    /// velocity must include that, or the term is asleep exactly where Ty wants it awake.
    /// </summary>
    [Fact]
    public void TheYawRateSwingsTheAftBodyAndTheTermFeelsIt()
    {
        var cfg = Load("pitts-s2b-like");
        double still = CamberPitch(cfg, 80, 0, 0.0);
        double spinning = CamberPitch(cfg, 80, 0, 4.0);     // 4 rad/s, about a developed Pitts spin
        _out.WriteLine($"alpha 80, beta 0: at rest {still:F1} N.m   yawing 4 rad/s {spinning:F0} N.m");
        Assert.Equal(0.0, still, 9);
        Assert.True(spinning < -50.0, $"the rotation term is not reaching the camber station: {spinning:F1} N.m");
    }

    /// <summary>
    /// THE SCALING IS GEOMETRIC: no area and no station is authored for any type. AftBody takes the part of the
    /// side-view silhouette behind the CG. Checked here against the two hand-authored turtledeck stations whose
    /// judgement it replaces — same aeroplane, same ballpark, nothing typed in per type.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", 2.0, -1.36)]
    [InlineData("extra-300-like", 2.8, -1.90)]
    public void TheAftBodyIsDerivedFromTheSilhouetteAndNotAuthored(string id, double authoredArea, double authoredX)
    {
        var cfg = Load(id);
        double[] aft = AeroModel.AftBody(cfg);
        _out.WriteLine($"{id}: derived {aft[0]:F2} m2 at x {aft[1]:F2}  (hand-authored turtledeck: {authoredArea:F2} m2 at x {authoredX:F2})");
        Assert.True(aft[0] > 0, "no aft body derived");
        Assert.True(aft[1] < cfg.Mass.CgVec().X, "the aft body must sit behind the CG");
        Assert.InRange(aft[0] / authoredArea, 0.7, 2.0);
        Assert.InRange(aft[1] - authoredX, -1.5, 0.5);
    }

    /// <summary>Every type gets an aft body, which is what "geometric, not per-type" has to mean — except the soft
    /// wings, which have no fuselage at all and must stay untouched.</summary>
    [Fact]
    public void EveryTypeWithAFuselageGetsOne()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
        int with = 0, without = 0;
        foreach (string f in Directory.GetFiles(Path.Combine(d!.FullName, "configs", "aircraft"), "*.json").Order())
        {
            var cfg = AircraftConfigLoader.LoadFromFile(f);
            double[] aft = AeroModel.AftBody(cfg);
            bool hasFuselage = cfg.Fuselage.Crossflow.LengthM > 0.1 && cfg.Fuselage.Crossflow.SideArea > 0;
            if (hasFuselage) { Assert.True(aft[0] > 0, $"{cfg.Id} has a fuselage but no aft body"); with++; }
            else { Assert.Equal(0.0, aft[0], 9); without++; }
        }
        _out.WriteLine($"{with} types get an aft body, {without} have no fuselage (soft wings)");
        Assert.True(with > 30, $"only {with} types got one");
    }

    /// <summary>The knob: a plain scalar, 1.0 as shipped, 0 off, and strictly proportional so that what Ty sees on
    /// the slider is what he gets. Registered for the Developer page as `fuselage.crossflow.aftCamberGain`.</summary>
    [Fact]
    public void TheGainIsAPlainProportionalScalarDefaultingToOne()
    {
        Assert.Equal(1.0, new CrossflowConfig().AftCamberGain, 9);
        var cfg = Load("pitts-s2b-like");
        double atOne = CamberPitch(cfg, 80, 25, 2.0);
        cfg.Fuselage.Crossflow.AftCamberGain = 2.0;
        double atTwo = CamberPitch(cfg, 80, 25, 2.0);
        cfg.Fuselage.Crossflow.AftCamberGain = 0.0;
        double atZero = CamberPitch(cfg, 80, 25, 2.0);
        cfg.Fuselage.Crossflow.AftCamberGain = 1.0;
        _out.WriteLine($"gain 0 -> {atZero:F1} N.m, gain 1 -> {atOne:F0} N.m, gain 2 -> {atTwo:F0} N.m");
        Assert.Equal(0.0, atZero, 9);
        Assert.Equal(2.0 * atOne, atTwo, 6);
    }
}
