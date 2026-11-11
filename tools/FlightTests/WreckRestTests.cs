using FlyingGame.Core;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty, 2026-11-09, with IMG_3289: "its parts and pieces need to stay above the surface post crash."
///
/// They did not, and these are the numbers. <see cref="WreckSim"/> asked the world for the ground ONCE, under each
/// piece's centre of gravity, and then tested all eight corners of every one of that piece's boxes against that one
/// height — so a piece lying across a slope was settled as though the hillside were level under the whole of it, its
/// uphill end buried by (half-length × slope) and its downhill end that far in the air. It is not a small error on
/// terrain anyone actually crashes on: of 144 points on a grid over the whole island, 30 are steep enough that a
/// dead aeroplane put down on them goes on sliding, and a 6 m wing panel on a 20 % slope finished 0.6 m under the
/// grass while reporting a clearance of 0.000 m.
///
/// <see cref="GroundPlane"/> is the fix — the ground under a piece as a local plane rather than a point — and these
/// tests are what it has to keep true. The airframe itself never had this bug: AirframeContact samples the terrain
/// under every hard point (or once per tight patch of them), so only the wreck was settling against one height.
/// </summary>
public sealed class WreckRestTests
{
    private readonly ITestOutputHelper _out;
    public WreckRestTests(ITestOutputHelper o) { _out = o; }

    /// <summary>Ground = base + slope·x, and nothing else: the slope is the only thing being tested.</summary>
    private sealed class RampWorld : IWreckWorld
    {
        public double Base, Slope;
        public double? WaterUpAt(double x, double y, double up) => null;
        public double GroundUpAt(double x, double y) => Base + Slope * x;
        public WorldTerrain.Surface SurfaceAt(double x, double y, double up) => WorldTerrain.Surface.Grass;
        public WaterCurrent.Local CurrentAround(double x, double y, double halfSpanM) => WaterCurrent.Local.None;
    }

    /// <summary>A 6 m wing panel as four boxes along its span, the shape <see cref="AirframeHydroModel"/> gives one.</summary>
    private static List<HydroElement> Panel(double halfLenM, bool alongX)
    {
        var els = new List<HydroElement>();
        const int n = 4;
        for (int i = 0; i < n; i++)
        {
            double t = -halfLenM + 2 * halfLenM * (i + 0.5) / n;
            Vec3 half = alongX ? new Vec3(halfLenM / n, 0.5, 0.06) : new Vec3(0.5, halfLenM / n, 0.06);
            Vec3 centre = alongX ? new Vec3(t, 0, 0) : new Vec3(0, t, 0);
            els.Add(new HydroElement("panel" + i, WreckAtom.WingLOuter, centre, half,
                8 * half.X * half.Y * half.Z, 12.0, 0.0));
        }
        return els;
    }

    private static (WreckSim Sim, WreckPiece Piece) Drop(RampWorld world, double halfLenM, bool alongX, double fromM = 2.0)
    {
        var sim = new WreckSim(world);
        WreckPiece p = sim.Add("panel", AirframeComponent.WingLeftOuter, Panel(halfLenM, alongX), Vec3.Zero,
            new RigidBodyState(new Vec3(0, 0, -(world.GroundUpAt(0, 0) + fromM)), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero));
        for (int i = 0; i < 4000; i++) sim.StepOnce();   // 20 s at the sim's own 5 ms step
        return (sim, p);
    }

    /// <summary>The deepest any corner of the piece is below the REAL ground under that corner, m (0 = none is).</summary>
    private static double DeepestBelowSurface(WreckPiece p, IWreckWorld world)
    {
        double worst = 0;
        RigidBodyState s = p.State;
        foreach (HydroElement e in p.Elements)
            for (int k = 0; k < 8; k++)
            {
                Vec3 c = e.Centre - p.CgBody + new Vec3((k & 1) == 0 ? -e.Half.X : e.Half.X,
                    (k & 2) == 0 ? -e.Half.Y : e.Half.Y, (k & 4) == 0 ? -e.Half.Z : e.Half.Z);
                Vec3 w = s.Position + s.Attitude.Rotate(c);
                worst = System.Math.Min(worst, (-w.Z) - world.GroundUpAt(w.X, w.Y));
            }
        return -worst;
    }

    /// <summary>
    /// THE REPORT. A wing panel dropped on a hillside comes to rest with no part of it under the grass, at every
    /// slope from level to the 35 % that the island's steep faces reach. Before <see cref="GroundPlane"/> these same
    /// cases measured 0.000, 0.151, 0.302, 0.604 and 1.057 m buried — exactly half-length × slope, which is the
    /// signature of settling a 6 m object against one height sampled in the middle of it.
    /// </summary>
    [Theory]
    [InlineData(0.00)]
    [InlineData(0.05)]
    [InlineData(0.10)]
    [InlineData(0.20)]
    [InlineData(0.35)]
    public void APieceLyingDownTheFallLineRestsOnTheSurfaceAndNotInIt(double slope)
    {
        var world = new RampWorld { Base = 100.0, Slope = slope };
        (_, WreckPiece p) = Drop(world, 3.0, alongX: true);
        double below = DeepestBelowSurface(p, world);
        _out.WriteLine($"  slope {slope * 100,3:F0} %: deepest corner {below * 100,6:F1} cm below the surface (was {3.0 * slope * 100:F1} cm)");
        Assert.True(below < 0.02, $"a 6 m panel on a {slope * 100:F0} % slope is {below:F3} m into the ground");
    }

    /// <summary>The same across the slope rather than down it: the fit is two-dimensional, so a piece lying along the
    /// contour has to come out as well as one lying down the fall line. The sampling is central differences in x and
    /// in y, and a one-axis fit would pass the test above and fail this one.</summary>
    [Theory]
    [InlineData(0.10)]
    [InlineData(0.25)]
    public void AndAcrossTheSlopeToo(double slope)
    {
        // Ground rising in x; the panel laid along y, then turned a quarter turn so its span runs down x.
        var world = new RampWorld { Base = 250.0, Slope = slope };
        (_, WreckPiece p) = Drop(world, 3.0, alongX: false);
        double acrossIt = DeepestBelowSurface(p, world);
        (_, WreckPiece q) = Drop(world, 3.0, alongX: true);
        double downIt = DeepestBelowSurface(q, world);
        _out.WriteLine($"  slope {slope * 100,3:F0} %: across {acrossIt * 100:F1} cm, down the fall line {downIt * 100:F1} cm");
        Assert.True(acrossIt < 0.02);
        Assert.True(downIt < 0.02);
    }

    /// <summary>Level ground is untouched: the plane's slopes come out zero, so every number a flat case ever gave is
    /// the number it still gives. A piece settles with its lowest corner on the ground, not under it and not above it.</summary>
    [Fact]
    public void LevelGroundIsExactlyWhatItWas()
    {
        var world = new RampWorld { Base = 12.0, Slope = 0.0 };
        (_, WreckPiece p) = Drop(world, 3.0, alongX: true);
        Assert.Equal(0.0, p.ClearanceM, 3);
        Assert.Equal(WreckPhase.OnLand, p.Phase);
        Assert.False(p.HitWater);
        Assert.True(p.HitLand);
    }

    /// <summary>The piece's own clearance report is now measured under the point it is reporting about, so a buried
    /// piece can no longer call itself rested. This is what the bridge and the flood reports read.</summary>
    [Fact]
    public void ClearanceIsMeasuredUnderTheLowestPointAndNotUnderTheCg()
    {
        var world = new RampWorld { Base = 400.0, Slope = 0.25 };
        (_, WreckPiece p) = Drop(world, 3.0, alongX: true);
        _out.WriteLine($"  25 % slope: ClearanceM {p.ClearanceM:F3} m, deepest corner {DeepestBelowSurface(p, world) * 100:F1} cm below the surface");
        Assert.True(System.Math.Abs(p.ClearanceM) < 0.05);
        Assert.True(DeepestBelowSurface(p, world) < 0.02);
    }

    /// <summary>A piece high in the air is not fitted at all (it cannot touch anything), so nothing about a falling
    /// piece's flight changed and no height query was spent on it.</summary>
    [Fact]
    public void APieceWellClearOfTheGroundIsNotFitted()
    {
        var world = new RampWorld { Base = 0.0, Slope = 0.3 };
        var sim = new WreckSim(world);
        WreckPiece p = sim.Add("panel", AirframeComponent.WingLeftOuter, Panel(3.0, true), Vec3.Zero,
            new RigidBodyState(new Vec3(0, 0, -500.0), new Quat(0, 0, 0, 1), Vec3.Zero, Vec3.Zero));
        for (int i = 0; i < 20; i++) sim.StepOnce();
        Assert.False(p.Phase == WreckPhase.OnLand);
        Assert.True(p.ClearanceM > 400);
    }
}
