using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>Ground effect (owner 2026-10-03: "is it even modeled?"): near the ground the wing makes more lift for the same
/// angle of attack, much less induced drag, and the tail loses downwash (nose-down) — McCormick's φ(h/b).</summary>
public class GroundEffectTests
{
    private readonly ITestOutputHelper _o;
    public GroundEffectTests(ITestOutputHelper o) { _o = o; }

    [Theory]
    [InlineData("pa28-archer-like")]
    [InlineData("c172-like")]
    public void NearTheGroundMoreLiftLessDragNoseDown(string id)
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var tables = Aircraft.BuildAirfoilTables(c);
        double a = 6 * Math.PI / 180, v = 30;
        var vb = new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a));
        var ctl = ControlDeflections.Neutral;
        (Vec3 fHigh, Vec3 mHigh) = AeroModel.Compute(c, tables, vb, Vec3.Zero, Vec3.Zero, 1.2, ctl);
        (Vec3 fLow, Vec3 mLow) = AeroModel.Compute(c, tables, vb, Vec3.Zero, Vec3.Zero, 1.2, ctl, wingHeightAglM: 1.0);
        double L(Vec3 f) => f.X * Math.Sin(a) - f.Z * Math.Cos(a);
        double D(Vec3 f) => -(f.X * Math.Cos(a) + f.Z * Math.Sin(a));
        _o.WriteLine($"{id}: span {AeroModel.WingSpan(c):F1} m; at 1 m: lift {L(fLow) / L(fHigh):P0} of free air, drag {D(fLow) / D(fHigh):P0}, pitch moment {mHigh.Y:F0} → {mLow.Y:F0} N·m; L/D {L(fHigh) / D(fHigh):F1} → {L(fLow) / D(fLow):F1}");
        Assert.True(L(fLow) > L(fHigh) * 1.03, "more lift in ground effect");
        Assert.True(D(fLow) < D(fHigh) * 0.95, "less drag in ground effect");
        Assert.True(mLow.Y < mHigh.Y, "nose-down: the tail loses its downwash");
    }

    /// <summary>
    /// The wing's height above the CG is geometry plus the LIVE CG, not a cached difference. Weight and balance moves
    /// the CG vertically (wing fuel against low baggage), and the cache used to hold `meanWingZ - cg.Z`, so a loaded
    /// aircraft would have flown ground effect at its empty-weight wing height — wrong only in the flare, where it is
    /// hardest to notice and most dangerous to be wrong.
    /// </summary>
    [Fact]
    public void MovingTheCgVerticallyMovesTheWingHeightGroundEffectSees()
    {
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", "c172-like.json"));
        double before = AeroModel.WingHeightAboveCg(c);
        AeroModel.WingSpan(c);                       // prime the geometry cache, as a flight would
        c.Mass.Cg[2] += 0.25;                        // body z is + down: the CG drops a quarter metre
        double after = AeroModel.WingHeightAboveCg(c);
        _o.WriteLine($"c172: wing {before:F3} m above the CG, {after:F3} m after the CG drops 0.25 m");
        Assert.Equal(before + 0.25, after, 9);
        Assert.True(before > 0, $"the 172 is a high-wing: the wing should sit above the CG, got {before:F3} m");
    }
}
