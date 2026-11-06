using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Wreck;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 13:41 (iPhone 2026.1105.1140, Q028): "Pitts tree impact lost propeller, retained wings, pitched nearly
/// vertical; aircraft vanished while crash sounds continued, then reappeared after post-crash bailout."
///
/// Nothing hid the aeroplane: the TREE was drawn over it. Square on a fir's stem the Pitts loses its nose, stands on it and
/// slides down the stem, and rests with its CG 0.4 m from the axis — inside the drawn 1.5 m trunk box and under the lowest
/// skirt, while the sim's stem is 0.42 m thick. <see cref="StrippedTrees"/> draws a tree the airframe has gone into as the
/// sim has it. Expected values are literals worked by hand from the drawing's constants (ForestBuilder's detail tree) and
/// the Pitts config (half-size = the larger of its semispan, 2.995 m, and half its 5.189 m length; body radius 0.498 m).
/// Trees are a <see cref="FixedTrees"/> list on flat ground at 0.
/// </summary>
[Collection("WorldTerrainActive")]
public class StrippedTreesTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public StrippedTreesTests(ITestOutputHelper o) { _o = o; WorldTerrain.Active = null; CrashEvents.ClearForTests(); Forest.ClearBroken(); StrippedTrees.Clear(); }
    public void Dispose() { WorldTerrain.Active = null; CrashEvents.ClearForTests(); Forest.ClearBroken(); StrippedTrees.Clear(); }

    private static AircraftConfig Load(string n) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", n + ".json"));
    private static Forest.Tree Tree(double x, double y, double h) => new(x, y, 0.0, h, 0.22 + h / 24.0, 0, 0);

    [Fact]
    public void TheDrawnTreeIsWhatForestBuilderDraws()
    {
        // 25 m fir: RadiusM 0.22 + 25/24 = 1.2617; crown 2.4 × that = 3.028 m.
        var t = Tree(0, 0, 25);
        // Trunk box 1.2 × 1.2617 = 1.514 m across, its corner 0.757·√2 = 1.0705 m out, up to 0.33 × 25 = 8.25 m.
        Assert.True(StrippedTrees.InsideDrawn(t, 1.06, 0, 8.2, 0));
        Assert.False(StrippedTrees.InsideDrawn(t, 1.08, 0, 4.0, 0));
        Assert.True(StrippedTrees.InsideDrawn(t, 0.5, 0, 8.24, 0));
        // Lowest skirt: base 0.22 × 25 = 5.5 m, corner reach 3.028·√2 = 4.282 m there; half way up its 10.5 m rise, 2.141 m.
        Assert.True(StrippedTrees.InsideDrawn(t, 4.27, 0, 5.5, 0));
        Assert.False(StrippedTrees.InsideDrawn(t, 4.30, 0, 5.5, 0));
        Assert.True(StrippedTrees.InsideDrawn(t, 2.13, 0, 10.75, 0));
        Assert.False(StrippedTrees.InsideDrawn(t, 2.16, 0, 10.75, 0));
        // The tree's own skirts, never drawn in the far tree's layout by mistake.
        Assert.Equal(new[] { 0.22, 0.52, 0.78 }, StrippedTrees.DetailSkirts.Select(s => s.BaseFrac));
        Assert.Equal(new[] { 0.42, 0.34, 0.24 }, StrippedTrees.DetailSkirts.Select(s => s.RiseFrac));
        Assert.Equal(new[] { 1.00, 0.72, 0.42 }, StrippedTrees.DetailSkirts.Select(s => s.RadiusFrac));
    }

    /// <summary>Ty's crash: square on the stem. Before, the airframe spent the whole slide and the rest after it inside the
    /// drawn tree; now the skirts it went through are gone, the ones above it stay, and the stem is the sim's.</summary>
    [Fact]
    public void ThePittsSquareOnAStemIsInsideTheDrawnTreeAndTheTreeIsStrippedRoundIt()
    {
        var c = Load("pitts-s2b-like");
        var tr = Tree(40, 0, 25);
        var src = new FixedTrees(tr);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -12), Quat.Identity, new Vec3(35, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        ac.Trees = src;
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        var sim = new SimLoop(ac);
        double half = 0.5 * Math.Max(2.0 * WingPanels.Semispan(c), c.Fuselage.Crossflow.LengthM), body = c.Fuselage.Crossflow.BodyRadiusM;
        _o.WriteLine($"half-size {half:0.000} m, body {body:0.000} m");
        double insideS = 0, lowestUp = double.PositiveInfinity, minPitch = 0;
        int changes = 0;
        for (int k = 0; k < 240; k++)
        {
            sim.RunFor(1.0 / 60, new ControlInputs(0, 0, 0, 1));
            relay.Step(1.0 / 60);
            Vec3 p = ac.State.Position;
            if (StrippedTrees.InsideDrawn(tr, p.X, p.Y, -p.Z, body)) insideS += 1.0 / 60;
            if (StrippedTrees.Observe(src, p, body, half)) changes++;
            lowestUp = Math.Min(lowestUp, -p.Z);
            Vec3 f = ac.State.Attitude.Rotate(new Vec3(1, 0, 0));
            minPitch = Math.Min(minPitch, Math.Asin(Math.Clamp(-f.Z, -1, 1)) * 180 / Math.PI);
        }
        Vec3 end = ac.State.Position;
        double rho = Math.Sqrt((end.X - tr.X) * (end.X - tr.X) + (end.Y - tr.Y) * (end.Y - tr.Y));
        _o.WriteLine($"inside the drawn tree {insideS:0.00} s; at rest {rho:0.00} m from the axis, {-end.Z:0.00} m up; pitch reached {minPitch:0}°; " +
                     $"lost [{string.Join(",", ac.LostComponents)}]; drawn changed {changes}×");
        Assert.True(StrippedTrees.TryGet(tr, out StrippedTrees.Band b));
        _o.WriteLine($"band {b.LoM:0.00} .. {b.HiM:0.00} m; mask {StrippedTrees.DrawnMask(tr)}");
        // Ty's report, reproduced: the nose goes, the wings stay, it stands near vertical against the stem.
        Assert.Contains(AirframeComponent.Nose, ac.LostComponents);
        Assert.DoesNotContain(AirframeComponent.WingLeft, ac.LostComponents);
        Assert.True(minPitch < -75, $"pitch {minPitch:0}");
        Assert.True(rho < 1.0, $"rests {rho:0.00} m from the axis, inside the old 1.07 m trunk box");
        Assert.True(insideS > 1.5, $"inside the drawn tree {insideS:0.00} s");
        // Entered at about 8.5 m up and slid to the ground: the lowest skirt (5.5–16 m) is gone, the middle one (13–21.5 m)
        // stands above the band's top (about 8.6 + 2.995 = 11.6 m), and the top one (19.5–25.5 m).
        Assert.False(StrippedTrees.SkirtDrawn(tr, StrippedTrees.DetailSkirts[0]));
        Assert.True(StrippedTrees.SkirtDrawn(tr, StrippedTrees.DetailSkirts[1]));
        Assert.True(StrippedTrees.SkirtDrawn(tr, StrippedTrees.DetailSkirts[2]));
        Assert.True(b.HiM < 13.0, $"band top {b.HiM:0.00}");
        // A rebuild for each skirt that goes (and the first strip), not one a frame.
        Assert.InRange(changes, 1, 4);
    }

    /// <summary>The wing into the stem, the fuselage 3.2 m clear of the axis at the crown's middle: the wing takes what it
    /// takes and the tree is drawn as it was (no skirt reaches 3.2 − 0.498 m out at that height: the middle one reaches
    /// 0.72·3.028·√2·(1 − (h − 13)/8.5)).</summary>
    [Fact]
    public void AWingThroughATreeWithTheFuselageOutsideStripsNothing()
    {
        var c = Load("pitts-s2b-like");
        var tr = Tree(40, 3.2, 25);
        var src = new FixedTrees(tr);
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -16), Quat.Identity, new Vec3(35, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        ac.Trees = src;
        var relay = new CrashRelay(ac, new FlatWreckWorld(null, 0.0));
        var sim = new SimLoop(ac);
        double half = 0.5 * Math.Max(2.0 * WingPanels.Semispan(c), c.Fuselage.Crossflow.LengthM), body = c.Fuselage.Crossflow.BodyRadiusM;
        for (int k = 0; k < 90; k++)
        {
            sim.RunFor(1.0 / 60, new ControlInputs(0, 0, 0, 1));
            relay.Step(1.0 / 60);
            StrippedTrees.Observe(src, ac.State.Position, body, half);
        }
        _o.WriteLine($"strikes {ac.TreeStrikeLog.Count}; lost [{string.Join(",", ac.LostComponents)}]");
        Assert.NotEmpty(ac.TreeStrikeLog);
        Assert.Equal(0, StrippedTrees.Count);
        Assert.Equal(0, StrippedTrees.DrawnMask(tr));
    }

    [Fact]
    public void ARepeatObservationDoesNotRedrawAndANewFlightStandsTheTreeAgain()
    {
        var tr = Tree(0, 0, 25);
        var src = new FixedTrees(tr);
        int v0 = StrippedTrees.Version;
        // CG 0.4 m off the axis, 1 m up: inside the trunk box. Band 1 ± 2.92 → only the trunk; no skirt reaches down to it.
        Assert.True(StrippedTrees.Observe(src, new Vec3(0.4, 0, -1.0), 0.498, 2.92));
        Assert.Equal(8, StrippedTrees.DrawnMask(tr));
        Assert.False(StrippedTrees.Observe(src, new Vec3(0.4, 0, -1.2), 0.498, 2.92));
        // 4 m up: the band reaches 6.92 m and takes the lowest skirt (from 5.5 m), close-up and far (far: from 0.20 × 25 = 5 m).
        Assert.True(StrippedTrees.Observe(src, new Vec3(0.4, 0, -4.0), 0.498, 2.92));
        Assert.Equal(8 | 1 | 16, StrippedTrees.DrawnMask(tr));
        Assert.True(StrippedTrees.Version > v0);
        int v1 = StrippedTrees.Version;
        StrippedTrees.Clear();
        Assert.True(StrippedTrees.Version > v1);
        Assert.Equal(0, StrippedTrees.DrawnMask(tr));
        Assert.True(StrippedTrees.SkirtDrawn(tr, StrippedTrees.DetailSkirts[0]));
    }
}
