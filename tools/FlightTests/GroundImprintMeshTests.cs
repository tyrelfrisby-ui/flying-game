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
/// Drawing the marks the ground keeps (<see cref="GroundImprintDrawing"/>, <see cref="GroundImprintMesh"/>).
///
/// The record says a Cub's rut on a dirt strip is 3.3 mm deep and nothing clamps it (docs/GROUND-IMPRINTS.md:
/// "VISIBILITY IS THE RENDERER'S BUSINESS"). These pin the other half of that bargain — that 3.3 mm is DRAWN as
/// flattened, bruised ground rather than as nothing or as a trench — and that the geometry lies on the surface the
/// wheels are on, costs what it says it costs, and is rebuilt incrementally.
///
/// Engine-free on purpose: every one of these runs headless, so iOS, the Mac, the Quest and the widget are all
/// covered by the same assertions.
/// </summary>
[Collection("WorldTerrainActive")]
public class GroundImprintMeshTests : IDisposable
{
    private readonly ITestOutputHelper _o;
    public GroundImprintMeshTests(ITestOutputHelper o)
    {
        _o = o; SimGlobals.Calm(); Atmosphere.SimTimeSec = 0; GroundImprints.Clear();
    }
    public void Dispose()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; GroundImprints.Clear();
    }

    private static AircraftConfig Cfg(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));

    private static Aircraft Roll(AircraftConfig c, WorldTerrain.Surface surface, double seconds, double speedMs,
        double brake = 0.0, double rudder = 0.0)
    {
        WorldTerrain.SurfaceOverride = surface;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        var s = new RigidBodyState(rest.Position, rest.Attitude, new Vec3(speedMs, 0, 0), Vec3.Zero);
        var ac = new Aircraft(c, s, ControlDeflections.Neutral) { BrakeInput = brake };
        new SimLoop(ac).RunFor(seconds, new ControlInputs(0, 0, rudder, 1.0));
        return ac;
    }

    private static GroundImprint Mark(double lengthM, double widthM, double depthM, GroundImprintKind kind,
        GroundMaterial m, double x = 0, double y = 0) =>
        new(new Vec3(x, y, 0), new Vec3(x + lengthM, y, 0), widthM, depthM, kind, m, 0, 1);

    private static (Vec3[] p, Vec3[] n, float[] rgba) One(in GroundImprint k)
    {
        var p = new Vec3[GroundImprintDrawing.VerticesPerMark];
        var n = new Vec3[GroundImprintDrawing.VerticesPerMark];
        var rgba = new float[GroundImprintDrawing.VerticesPerMark * 4];
        GroundImprintDrawing.Build(in k, p, n, rgba, 0);
        return (p, n, rgba);
    }

    // ---- where the geometry goes -------------------------------------------------------------------------------

    /// <summary>
    /// A mark lies ON the ground — not in it and not above it — and on a SLOPE as well as on the flat, which means
    /// every vertex is sampled against the terrain and not just the mark's own midpoint. The only thing between the
    /// mark and the ground is <see cref="GroundImprintDrawing.SurfaceLiftM"/>, the clearance that stops it z-fighting.
    /// </summary>
    [Fact]
    public void EveryVertexOfAMarkSitsOnTheGroundItLiesOn()
    {
        WorldTerrain.Active = new WorldTerrain();
        // Walk the island looking for sloping ground: a wreck's scar down a hillside is the case a flat test misses.
        double steepest = 0; double sx = 0, sy = 0;
        for (double x = -3000; x <= 3000; x += 137)
            for (double y = -3000; y <= 3000; y += 137)
            {
                if (WorldTerrain.GroundHeightAt(x, y) < 1) continue;
                Vec3 up = WorldTerrain.GroundNormalAt(x, y);
                double slope = Math.Sqrt(up.X * up.X + up.Y * up.Y) / Math.Max(1e-9, Math.Abs(up.Z));
                if (slope > steepest) { steepest = slope; sx = x; sy = y; }
            }
        _o.WriteLine($"steepest ground found: {Math.Atan(steepest) * 180 / Math.PI:F1} deg at ({sx:F0}, {sy:F0})");
        Assert.True(steepest > 0.2, "this test needs some slope to be worth anything");

        foreach ((double x, double y) at in new[] { (0.0, 0.0), (sx, sy) })
        {
            // A broad wreck scar, so the across-the-width sampling is exercised too.
            GroundImprint k = Mark(6.0, 1.2, 0.08, GroundImprintKind.Scar, GroundMaterial.Dirt, at.x, at.y);
            (Vec3[] p, Vec3[] n, _) = One(in k);
            for (int i = 0; i < p.Length; i++)
            {
                double ground = -WorldTerrain.GroundHeightAt(p[i].X, p[i].Y);   // NED: z is down
                double above = ground - p[i].Z;                                  // positive = the vertex is above it
                Assert.True(Math.Abs(above - GroundImprintDrawing.SurfaceLiftM) < 1e-9,
                    $"vertex {i} stands {1000 * above:F3} mm off its own ground at ({at.x}, {at.y})");
            }
        }
    }

    /// <summary>
    /// A chain of stretches has no gap in it. A curving track is a chain, and consecutive stretches are one physics
    /// step apart — 12 cm at a 14 m/s touchdown — so each mark is drawn half a width past both its ends
    /// (<see cref="GroundImprintDrawing.CapFraction"/>). Without that a landing roll is a dotted line.
    /// </summary>
    [Fact]
    public void ConsecutiveStretchesOverlapInsteadOfLeavingAGap()
    {
        const double width = 0.20, gap = 0.12;    // a Cub's rut, and one step at 14 m/s
        GroundImprint a = Mark(3.0, width, 0.01, GroundImprintKind.Rut, GroundMaterial.Grass);
        GroundImprint b = Mark(3.0, width, 0.01, GroundImprintKind.Rut, GroundMaterial.Grass, 3.0 + gap);
        (Vec3[] pa, _, float[] ra) = One(in a);
        (Vec3[] pb, _, float[] rb) = One(in b);
        double endOfA = pa.Max(v => v.X), startOfB = pb.Min(v => v.X);
        _o.WriteLine($"a ends at {endOfA:F3} m, b starts at {startOfB:F3} m: overlap {1000 * (endOfA - startOfB):F0} mm");
        Assert.True(endOfA > startOfB, "the caps have to meet across the step's gap");
        // And the two caps blend to EXACTLY the mark's own alpha, instead of to a dark bead or a light band: the cap
        // alpha solves 1 - (1 - c)^2 = a (GroundImprintDrawing.CapAlpha), so the overlap composes back to a.
        float capA = ra[(0 * 4 + 1) * 4 + 3], bodyA = ra[(1 * 4 + 1) * 4 + 3];
        Assert.Equal(GroundImprintDrawing.CapAlpha(bodyA), capA, 5);
        double combined = 1 - (1 - capA) * (1 - rb[(0 * 4 + 1) * 4 + 3]);
        _o.WriteLine($"floor alpha {bodyA:F3}, cap {capA:F3}, the two caps over each other {combined:F3}");
        Assert.Equal(bodyA, combined, 5);
        // And it stays exact across the whole range of marks the record holds, which a fixed cap fraction cannot do.
        foreach (float level in new[] { 0.12f, 0.3f, 0.6f, 0.85f, 1f })
        {
            float cap = GroundImprintDrawing.CapAlpha(level);
            Assert.Equal(level, 1 - (1 - cap) * (1 - cap), 5);
            Assert.InRange(cap, 0f, level);
        }
    }

    /// <summary>A stretch only one step old has no length, and is drawn as a square patch of its own width rather
    /// than as nothing. (Most marks in a ground loop are this.)</summary>
    [Fact]
    public void AStretchWithNoLengthIsStillAPatchOfItsOwnWidth()
    {
        GroundImprint k = Mark(0, 0.30, 0.02, GroundImprintKind.Rut, GroundMaterial.Snow);
        (Vec3[] p, _, float[] rgba) = One(in k);
        double spanX = p.Max(v => v.X) - p.Min(v => v.X), spanY = p.Max(v => v.Y) - p.Min(v => v.Y);
        _o.WriteLine($"{spanX:F3} x {spanY:F3} m");
        Assert.InRange(Math.Max(spanX, spanY), 0.29, 0.31);
        Assert.InRange(Math.Min(spanX, spanY), 0.29, 0.31);
        Assert.True(rgba.Where((_, i) => i % 4 == 3).Any(a => a > 0.1), "and it is actually drawn");
    }

    // ---- what it looks like -------------------------------------------------------------------------------------

    /// <summary>
    /// The record's whole bargain, pinned: the marks it actually holds are spread across the visible range instead of
    /// being bunched at invisible or saturated, and NOTHING the record kept is drawn at nothing. A 5 mm floor in the
    /// record was rejected because it erased a Cub's tracks on a dirt strip; putting that floor back in the renderer
    /// as an invisible alpha would be the same mistake wearing a different hat.
    /// </summary>
    [Fact]
    public void AShallowRutIsFaintAndADeepOneIsPlainAndNeitherIsInvisible()
    {
        var ladder = new (string what, GroundImprintKind kind, GroundMaterial m, double depth)[]
        {
            ("Cub's rut on a dirt strip",        GroundImprintKind.Rut,   GroundMaterial.Dirt, 0.0033),
            ("Cub's rut on mown grass",          GroundImprintKind.Rut,   GroundMaterial.Grass, 0.0056),
            ("Super Cub's ski track in snow",    GroundImprintKind.Track, GroundMaterial.Snow, 0.019),
            ("the same Cub's bare tyre in snow", GroundImprintKind.Rut,   GroundMaterial.Snow, 0.115),
        };
        foreach ((string what, GroundImprintKind kind, GroundMaterial m, double depth) in ladder)
        {
            GroundImprintLook look = GroundImprintDrawing.For(kind, m, depth);
            _o.WriteLine($"{what,-34} {1000 * depth,6:F1} mm -> alpha {look.Alpha:F3}, shoulder {look.Shoulder01:F2}, rgb ({look.R:F2} {look.G:F2} {look.B:F2})");
            Assert.InRange(look.Alpha, GroundImprintDrawing.MinAlpha, 1.0);
        }
        // The record's own minimum is still drawn.
        Assert.True(GroundImprintDrawing.For(GroundImprintKind.Rut, GroundMaterial.Dirt, GroundImprints.MinDepthM).Alpha
                    >= GroundImprintDrawing.MinAlpha);
        // A millimetre of rut is plainly fainter than a hand's depth of it, and deeper is never fainter.
        double last = -1;
        foreach (double d in new[] { 0.001, 0.003, 0.01, 0.03, 0.10, 0.30 })
        {
            double a = GroundImprintDrawing.For(GroundImprintKind.Rut, GroundMaterial.Dirt, d).Alpha;
            Assert.True(a >= last, $"{d} m read {a}");
            last = a;
        }
        Assert.True(GroundImprintDrawing.For(GroundImprintKind.Rut, GroundMaterial.Dirt, 0.0033).Alpha
                  < GroundImprintDrawing.For(GroundImprintKind.Rut, GroundMaterial.Dirt, 0.115).Alpha * 0.6);
    }

    /// <summary>
    /// The colour of a mark is the surface's OWN palette, darkened — the same per-surface palette the dust of a crash
    /// uses (Ty 2026-11-04: "Crashing on snow kicks up BROWN dust; it must be white"). So a ski track in snow is
    /// white-grey and a rut in grass is green-brown, and neither can quietly disagree with the ground the aeroplane
    /// was rolling on.
    /// </summary>
    [Fact]
    public void AMarkIsTheSurfacesOwnColourInItsOwnShade()
    {
        foreach (GroundMaterial m in new[] { GroundMaterial.Snow, GroundMaterial.Grass, GroundMaterial.Dirt,
                                             GroundMaterial.Sand, GroundMaterial.Gravel, GroundMaterial.Rough })
        {
            CrashDust.Look dust = CrashDust.For((int)GroundMaterials.Row(m).Physics);
            GroundImprintLook rut = GroundImprintDrawing.For(GroundImprintKind.Rut, m, 0.05);
            GroundImprintLook gouge = GroundImprintDrawing.For(GroundImprintKind.Gouge, m, 0.05);
            _o.WriteLine($"{m,-10} dust ({dust.R:F2} {dust.G:F2} {dust.B:F2}) rut ({rut.R:F2} {rut.G:F2} {rut.B:F2}) gouge ({gouge.R:F2} {gouge.G:F2} {gouge.B:F2}) shoulder {rut.Shoulder01:F2}");
            Assert.True(rut.R < dust.R && rut.G < dust.G && rut.B < dust.B, "a hollow is in its own shade");
            Assert.True(gouge.R < rut.R, "torn open is darker than compacted: it has brought up the subsoil");
            // The HUE is the surface's: snow stays white, turf stays green.
            Assert.Equal(dust.G > dust.R, rut.G > rut.R);
            Assert.Equal(dust.B > dust.R, rut.B > rut.R);
        }
        // Turf's root mat holds a near-vertical shoulder and reads nearly as dark as the floor; dry sand's slumps
        // away and barely shows. Straight out of the soil table's angle of repose.
        Assert.True(GroundImprintDrawing.ShoulderAlpha01(GroundMaterial.Grass)
                  > GroundImprintDrawing.ShoulderAlpha01(GroundMaterial.Sand));
    }

    /// <summary>
    /// The headline: a Super Cub's SKI leaves a broader, fainter mark in the snow than its own bare tyre leaves in the
    /// same snow, and it is drawn that way. That is flotation, through the real contact path and all the way to the
    /// vertices a renderer uploads.
    /// </summary>
    [Fact]
    public void ASkiTrackIsBroaderAndFainterThanTheSameCubsTyreRutInTheSameSnow()
    {
        AircraftConfig onSkis = Cfg("pa18-cub-like"); onSkis.Skis = true;
        Roll(onSkis, WorldTerrain.Surface.Snow, 4.0, 12.0);
        var ski = Widest(GroundImprintKind.Track);
        GroundImprints.Clear();

        Roll(Cfg("pa18-cub-like"), WorldTerrain.Surface.Snow, 4.0, 12.0);
        var tyre = Widest(GroundImprintKind.Rut);

        GroundImprintLook skiLook = GroundImprintDrawing.For(in ski);
        GroundImprintLook tyreLook = GroundImprintDrawing.For(in tyre);
        _o.WriteLine($"ski   {ski}  -> alpha {skiLook.Alpha:F3}");
        _o.WriteLine($"tyre  {tyre} -> alpha {tyreLook.Alpha:F3}");
        Assert.True(ski.WidthM > tyre.WidthM, "the ski's mark is the broader");
        // Depth is NOT compared here, and deliberately: a ski has no sinkage model of its own yet
        // (docs/GROUND-IMPRINTS.md, "Not here"), so its track's depth is the plastic part of the ski's contact
        // PENETRATION while the tyre's is a Bekker sinkage. Those are two different measurements and an arriving
        // ski's spring compression is not comparable with a rolling tyre's compaction. The settled comparison —
        // 0.338 m x 1.9 cm against 0.320 m x 11.5 cm — is pinned where it belongs, in
        // GroundImprintTests.ACubOnSkisLeavesABroadTrackInTheSnowAndItIsTheSkisOwnWidth.
        // What IS this file's business: at the same depth on the same ground, a ski's broad flat track is drawn
        // fainter than a tyre's rut, because a ski is the flattest, smoothest, most lightly marked thing anything
        // leaves - which is the whole point of a ski.
        foreach (double d in new[] { 0.005, 0.02, 0.05, 0.15 })
            Assert.True(GroundImprintDrawing.For(GroundImprintKind.Track, GroundMaterial.Snow, d).Alpha
                      < GroundImprintDrawing.For(GroundImprintKind.Rut, GroundMaterial.Snow, d).Alpha,
                        $"at {1000 * d:F0} mm");
    }

    private static GroundImprint Widest(GroundImprintKind kind)
    {
        GroundImprintField f = GroundImprints.World;
        GroundImprint best = default; bool any = false;
        for (int i = 0; i < f.Count; i++)
            if (f[i].Kind == kind && (!any || f[i].WidthM > best.WidthM)) { best = f[i]; any = true; }
        Assert.True(any, $"no {kind} was laid at all");
        return best;
    }

    // ---- what it costs ------------------------------------------------------------------------------------------

    /// <summary>
    /// What the DRAWING costs, asserted so that growing it has to be a decision and not a drift: one mesh, one
    /// material, one draw call for every mark in the world, inside a 16-bit index buffer so the phone and the Quest
    /// get the cheap path. 18,432 triangles for the ring's full 1,024 stretches.
    /// </summary>
    [Fact]
    public void TheWholeWorldsMarksAreOneDrawCallInsideASixteenBitIndexBuffer()
    {
        int verts = GroundImprintField.WorldBudget * GroundImprintDrawing.VerticesPerMark;
        int tris = GroundImprintField.WorldBudget * GroundImprintDrawing.TrianglesPerMark;
        _o.WriteLine($"{GroundImprintField.WorldBudget} marks = {verts} vertices, {tris} triangles");
        Assert.True(verts <= 65535, $"{verts} vertices needs a 32-bit index buffer");
        Assert.True(tris <= 20000, $"{tris} triangles");
        var mesh = new GroundImprintMesh(GroundImprints.World);
        Assert.Equal(verts, mesh.Positions.Length);
        Assert.Equal(tris * 3, mesh.Indices.Length);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, verts - 1));
        // An empty field draws nothing at all.
        Assert.False(mesh.Refresh() && mesh.Count > 0);
    }

    /// <summary>
    /// A refresh rewrites only the marks that CHANGED. The field's version moves on every physics step a wheel is
    /// rolling, and at most a dozen stretches change in a step (one per contact in contact) — so a landing roll must
    /// not cost 16,384 vertices of work per step. It is the same lesson as the ConditionalWeakTable in the hot path
    /// on 2026-11-09, one layer up.
    /// </summary>
    [Fact]
    public void ARefreshRewritesOnlyTheMarksThatChanged()
    {
        AircraftConfig c = Cfg("j3-cub-like");
        WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Dirt;
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, new Vec3(10, 0, 0), Vec3.Zero),
                              ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        var mesh = new GroundImprintMesh(GroundImprints.World);
        sim.RunFor(3.0, new ControlInputs(0, 0, 0, 1.0));
        Assert.True(mesh.Refresh(), "the first refresh builds everything there is");
        int built = mesh.ChangedCount;
        Assert.Equal(GroundImprints.World.Count, mesh.Count);

        Assert.False(mesh.Refresh(), "nothing has moved: a second refresh does nothing at all");
        Assert.Equal(0, mesh.ChangedCount);

        int worst = 0;
        for (int i = 0; i < 40; i++)
        {
            sim.RunFor(1.0 / 120.0, new ControlInputs(0, 0, 0, 1.0));
            if (mesh.Refresh()) worst = Math.Max(worst, mesh.ChangedCount);
        }
        _o.WriteLine($"first refresh rewrote {built} marks; the worst step after that rewrote {worst}");
        Assert.True(worst <= 12, $"a step rewrote {worst} marks; a Cub has three wheels");
    }

    /// <summary>Clearing the field for a new flight blanks the drawing with it — the bug this lane was handed
    /// (StructuralDamage.Repair now calls GroundImprints.Clear beside Forest's and StrippedTrees'). A renderer that
    /// kept drawing the last flight's ruts would be the same bug one layer up.</summary>
    [Fact]
    public void ClearingTheFieldBlanksTheDrawing()
    {
        Roll(Cfg("j3-cub-like"), WorldTerrain.Surface.Dirt, 2.0, 10.0);
        var mesh = new GroundImprintMesh(GroundImprints.World);
        Assert.True(mesh.Refresh());
        Assert.True(mesh.Count > 0 && mesh.Triangles > 0);

        GroundImprints.Clear();
        Assert.True(mesh.Refresh(), "the renderer is told");
        Assert.Equal(0, mesh.Count);
        Assert.Equal(0, mesh.Triangles);
        Assert.All(mesh.Rgba.Where((_, i) => i % 4 == 3), a => Assert.Equal(0f, a));
    }

    /// <summary>A real landing roll, end to end: an aeroplane rolls, the marks are laid by the contact physics and the
    /// geometry comes out finite, on the ground, and the right way round.</summary>
    [Fact]
    public void ARollingCubsMarksComeOutAsFiniteGeometryOnTheGround()
    {
        WorldTerrain.Active = new WorldTerrain();
        Aircraft ac = Roll(Cfg("j3-cub-like"), WorldTerrain.Surface.Grass, 5.0, 14.0);
        var mesh = new GroundImprintMesh(GroundImprints.World);
        Assert.True(mesh.Refresh());
        Assert.True(mesh.Count > 5, $"{mesh.Count} marks");
        _o.WriteLine($"{mesh.Count} marks, {mesh.Triangles} triangles, {mesh.ChangedCount} built");
        for (int i = 0; i < mesh.Count * GroundImprintDrawing.VerticesPerMark; i++)
        {
            Vec3 p = mesh.Positions[i], n = mesh.Normals[i];
            Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z), $"vertex {i} is {p}");
            Assert.True(Math.Abs(n.Length - 1) < 1e-9, $"normal {i} is {n}");
            Assert.True(n.Z < 0, "the surface's normal points UP, and up is −z in NED");
        }
        Assert.All(mesh.Rgba, v => Assert.InRange(v, 0f, 1f));
    }
}
