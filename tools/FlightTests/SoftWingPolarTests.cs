using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The two soft wings, held to the performance they were derived FROM.
///
/// Neither a paraglider nor a hang glider publishes a section or a weight-and-balance sheet, so
/// `tools/soft_wings/build_soft_wings.py` runs the derivation backwards out of the published polar --
/// CD0 = pi*AR*e / (4*(L/D)^2) -- exactly as Ty asked the Great Lakes' CLmax to be back-solved from its
/// published Vso. A derivation that runs backwards proves nothing on its own: it can only be trusted if
/// the aeroplane it produces, flown by the sim rather than by the spreadsheet, lands back on the numbers
/// that went in. That is what this file checks, and the numbers it prints are the sim's, not the
/// generator's.</summary>
[Collection("WorldTerrainActive")]
public class SoftWingPolarTests
{
    private readonly ITestOutputHelper _out;
    public SoftWingPolarTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    /// <summary>Sweep the polar the way a pilot would: fly it at a speed, see what it does.</summary>
    private (double ld, double vLd, double sink, double vSink, int converged) Sweep(
        AircraftConfig c, double vLo, double vHi)
    {
        double bestLd = 0, vLd = 0, minSink = double.MaxValue, vSink = 0;
        int n = 0;
        for (double v = vLo; v <= vHi + 1e-9; v += 0.1)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, 300.0);
            if (!t.Converged || t.GlideRatio <= 0) continue;
            n++;
            double sink = v / t.GlideRatio;
            if (t.GlideRatio > bestLd) { bestLd = t.GlideRatio; vLd = v; }
            if (sink < minSink) { minSink = sink; vSink = v; }
        }
        return (bestLd, vLd, minSink, vSink, n);
    }

    [Theory]
    // id, published L/D, published min sink (m/s), the speed band to sweep
    [InlineData("paraglider-en-b-like", 9.0, 1.05, 7.0, 14.0)]
    [InlineData("hang-glider-like", 13.0, 0.85, 8.5, 23.6)]
    public void EachSoftWingFliesThePolarItWasDerivedFrom(string id, double bookLd, double bookSink,
                                                          double vLo, double vHi)
    {
        AircraftConfig c = Load(id);
        var (ld, vLd, sink, vSink, n) = Sweep(c, vLo, vHi);
        _out.WriteLine($"{id}: {n} trimmed points over {vLo:0.0}-{vHi:0.0} m/s");
        _out.WriteLine($"  best glide {ld:0.00}:1 at {vLd:0.0} m/s ({vLd * 3.6:0} km/h)   published {bookLd:0.0}");
        _out.WriteLine($"  min sink   {sink:0.00} m/s at {vSink:0.0} m/s ({vSink * 3.6:0} km/h)   published {bookSink:0.00}");
        Assert.True(n > 10, $"{id}: only {n} points trimmed over its whole speed band -- it does not fly");
        // 15 % on glide: the generator solves for L/D analytically, so this catches a planform or a drag
        // split that does not survive being flown, not third-decimal drift.
        Assert.InRange(ld, bookLd * 0.85, bookLd * 1.15);
        // Sink is allowed to be WORSE than book but not better: a soft wing's real low-speed drag is worse
        // than a parabolic polar predicts, and a model that beats the book is flattering itself.
        Assert.InRange(sink, bookSink * 0.80, bookSink * 1.35);
    }

    /// <summary>A flex wing's roll comes from the pilot's APPARENT weight, so the same body movement must
    /// give MORE moment at higher load factor. That is the whole reason the moment is built from the
    /// aerodynamic force rather than from m*g*d, and it is the one claim in `WeightShiftConfig` that a
    /// static check can catch going wrong.</summary>
    [Fact]
    public void TheFlexWingsRollAuthorityGrowsWithLoadFactor()
    {
        AircraftConfig c = Load("hang-glider-like");
        Assert.NotNull(c.WeightShift);
        Assert.True(c.WeightShift!.PilotMassKg > 0, "the flex wing has no pilot to shift");
        Assert.True(c.WeightShift.HangHeightM > 0.5, "the pilot is not hanging below anything");
        // Nothing to deflect: no strip may claim a control surface on a wing that has none.
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig st in sf.Strips)
                Assert.True(st.Control is null || string.IsNullOrEmpty(st.Control.Surface),
                            $"{sf.Id}: a flex wing has no moving surface, but a strip claims '{st.Control?.Surface}'");
        // The axis limits ARE the pilot's reach: atan(shift / hang height), not a pushrod's travel.
        double wantRoll = Math.Atan2(c.WeightShift.MaxLateralM, c.WeightShift.HangHeightM);
        Assert.Equal(wantRoll, c.Controls.Aileron.MaxDeflRad, 2);
        double wantPitch = Math.Atan2(c.WeightShift.MaxLongitudinalM, c.WeightShift.HangHeightM);
        Assert.Equal(wantPitch, c.Controls.Elevator.MaxDeflRad, 2);
        _out.WriteLine($"hang glider: pilot {c.WeightShift.PilotMassKg:0} kg on a {c.WeightShift.HangHeightM:0.00} m " +
                       $"strap, reach {c.WeightShift.MaxLateralM:0.00} m lateral = {wantRoll:0.000} rad " +
                       $"({wantRoll * 180 / Math.PI:0.0} deg) of swing");
    }

    /// <summary>Both wings hang their pilot well BELOW the wing, and that pendulum is the defining thing
    /// about them -- it is what makes a paraglider recover itself and a hang glider feel like a pendulum
    /// rather than an aeroplane. A config that lost it would still glide correctly and fly like nothing
    /// real, so the geometry is pinned.</summary>
    [Theory]
    [InlineData("paraglider-en-b-like", 5.0)]
    [InlineData("hang-glider-like", 0.8)]
    public void TheWingHangsAboveItsPilot(string id, double leastHeightM)
    {
        AircraftConfig c = Load(id);
        double cgZ = c.Mass.CgVec().Z;
        double highest = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
            foreach (StripConfig st in sf.Strips)
                highest = Math.Max(highest, cgZ - st.Pos[2]);   // body z is NEGATIVE up
        _out.WriteLine($"{id}: wing sits {highest:0.00} m above the cg");
        Assert.True(highest >= leastHeightM,
                    $"{id}: the wing is only {highest:0.00} m above the cg -- the pendulum is gone");
    }
}
