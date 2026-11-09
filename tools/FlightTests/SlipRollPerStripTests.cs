using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE SEPARATED WING'S SIDESLIP ROLL, APPLIED PER STRIP (owner 2026-11-09: "make each of these real science not lump
/// sums"). It was one bulk C_l*q*S*b scaled by the wing's AVERAGE separated fraction. The measured magnitude is NACA
/// TR-379 and stays — SlipRollGeometryProbe shows the strip wing makes only 10-30 % of it above the stall and reverses
/// sign at the stall, so it carries a spanwise separation effect a strip model cannot derive. What was a lump sum is
/// the application, and these tests pin the three things that changed.
/// </summary>
public class SlipRollPerStripTests
{
    private readonly ITestOutputHelper _out;
    public SlipRollPerStripTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static int StripCount(AircraftConfig c)
    {
        int n = 0; foreach (SurfaceConfig sf in c.Surfaces) n += sf.Strips.Count; return n;
    }

    /// <summary>A flow state whose separation is <paramref name="sep"/> on wing strips selected by <paramref name="pick"/>.</summary>
    private static StripFlowState Flow(AircraftConfig c, double sep, System.Func<SurfaceConfig, StripConfig, bool>? pick = null)
    {
        int n = StripCount(c);
        var f = new StripFlowState();
        f.EnsureSize(n);
        int i = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig st in sf.Strips)
            {
                bool wing = sf.Id.Contains("wing", System.StringComparison.OrdinalIgnoreCase);
                f.Separation[i++] = wing && (pick is null || pick(sf, st)) ? sep : 0.0;
            }
        return f;
    }

    private static double WingArea(AircraftConfig c)
    {
        double s = 0; foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing", System.StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) s += st.Area;
        return s;
    }

    /// <summary>The rolling moment the sideslip term alone contributes: the config with its table minus the same config
    /// with the table off, so every other roll the strips make (dihedral, wing-body position, the fin) cancels.</summary>
    private static double SlipRollMx(string id, double alphaDeg, double betaDeg, double sep,
                                     System.Func<SurfaceConfig, StripConfig, bool>? pick = null)
    {
        var on = Load(id);
        var off = Load(id);
        if (off.SpinAero != null) off.SpinAero.RollDueToSideslip = "";
        var tables = Aircraft.BuildAirfoilTables(on);
        double a = alphaDeg * System.Math.PI / 180.0, b = betaDeg * System.Math.PI / 180.0;
        const double V = 30.0;
        Vec3 v = new Vec3(V * System.Math.Cos(a) * System.Math.Cos(b), V * System.Math.Sin(b), V * System.Math.Sin(a) * System.Math.Cos(b));
        Vec3 mOn = AeroModel.Compute(on, tables, v, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, flowState: Flow(on, sep, pick)).Moment;
        Vec3 mOff = AeroModel.Compute(off, tables, v, Vec3.Zero, Vec3.Zero, 1.225, ControlDeflections.Neutral, flowState: Flow(off, sep, pick)).Moment;
        return mOn.X - mOff.X;
    }

    /// <summary>
    /// CALIBRATED, NOT RETUNED. On a uniformly separated wing the per-strip sum must come to the bulk value the old
    /// code applied, C_l(alpha) * q * S * b at 20 deg of yaw. The constant that makes it so is b / (area-weighted mean
    /// span station), which is geometry, not a fit.
    ///
    /// It comes out 78-88 % of the bulk value, and the shortfall is physics the bulk version left out, not a
    /// miscalibration: every strip now uses ITS OWN local alpha, so decalage and incidence count (the Great Lakes has
    /// the most decalage of the three and shows the biggest difference, 0.78), and ITS OWN dynamic pressure, so a
    /// biplane's wings shading each other counts. The bulk formula used the body's alpha and the free-stream q for a
    /// wing whose strips are at neither. Measured 2026-11-09: Pitts 0.83 at alpha 24 and 0.88 at 40, Great Lakes 0.78
    /// at 28, Extra 0.88 at 32.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like", 24.0)]
    [InlineData("pitts-s2b-like", 40.0)]
    [InlineData("great-lakes-like", 28.0)]
    [InlineData("extra-300-like", 32.0)]
    public void AUniformlySeparatedWingReproducesTheMeasuredCoefficient(string id, double alphaDeg)
    {
        var c = Load(id);
        string kind = c.SpinAero?.RollDueToSideslip ?? "";
        Assert.NotEqual("", kind);
        double S = WingArea(c), b = AeroModel.WingSpan(c);
        double q = 0.5 * 1.225 * 30.0 * 30.0;
        double expect = -AeroModel.SlipRollCl20(kind, alphaDeg) * q * S * b;     // sense +1 upright, beta at the full 20
        double got = SlipRollMx(id, alphaDeg, 20.0, 1.0);
        _out.WriteLine($"{id} a{alphaDeg:F0}: per-strip {got:F0} N.m, bulk {expect:F0} N.m, ratio {got / expect:F3}");
        Assert.InRange(got / expect, 0.75, 1.10);
    }

    /// <summary>
    /// NO DOUBLE COUNT, STRUCTURALLY. In attached flow the strips already make their own roll from sideslip out of
    /// dihedral and wing-body position, and the old bulk term was kept off it by a gate on the wing's average separated
    /// fraction. Now an attached strip simply contributes nothing, so there is no gate to get wrong: this is exactly
    /// zero, not small. (The gate was added after the Super Cub's peak bank went 7 deg to 52 and the 2-33 wound up from
    /// a 22 m/s trim to 43 in moderate turbulence, both from counting this twice.)
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("great-lakes-like")]
    [InlineData("extra-300-like")]
    public void AttachedFlowContributesNothingAtAll(string id)
    {
        foreach (double a in new[] { 0.0, 4, 8, 12 })
            Assert.Equal(0.0, SlipRollMx(id, a, 20.0, 0.0), 9);
    }

    /// <summary>
    /// ONE WING STALLED AND THE OTHER FLYING. This is the case a wing-average cannot express, and it is the spin: the
    /// separated wing carries the whole term and the flying wing carries none of it, so the moment is roughly half what
    /// a fully separated wing gives and it does NOT depend on which half is measured — the term's sign comes from the
    /// sideslip and the attitude, not from which wing is stalled.
    /// </summary>
    [Fact]
    public void OneWingSeparatedCarriesItsOwnHalf()
    {
        double both = SlipRollMx("pitts-s2b-like", 28.0, 20.0, 1.0);
        double right = SlipRollMx("pitts-s2b-like", 28.0, 20.0, 1.0, (sf, st) => st.Pos[1] > 0);
        double left = SlipRollMx("pitts-s2b-like", 28.0, 20.0, 1.0, (sf, st) => st.Pos[1] < 0);
        _out.WriteLine($"both {both:F0} N.m, right wing only {right:F0}, left wing only {left:F0}, sum {right + left:F0}");
        Assert.InRange(right / both, 0.35, 0.65);
        Assert.InRange(left / both, 0.35, 0.65);
        Assert.InRange((right + left) / both, 0.95, 1.05);       // the strips partition the wing: the halves add up
    }
}
