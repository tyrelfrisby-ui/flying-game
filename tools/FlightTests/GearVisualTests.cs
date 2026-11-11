using System.Linq;
using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// THE DRAWN WHEEL AND THE LEG IT HANGS ON (Ty 2026-11-10, shown that the gear now flexes and hinges on the fuselage
/// — commit 4805726a — while the models are drawn at one fixed state and nothing moves them: <i>"Wire it — two types
/// are already rigged"</i>).
///
/// Every number here is MEASURED off the GLB the game actually loads, never off a drawing, because the models are the
/// authority on their own geometry (the standing rule from docs/models: measure the downloaded mesh). What the tests
/// hold:
///
///  1. the model draws its tyre's contact at the config's authored gear point, so the whole of a leg's stroke is
///     visual error until something moves the drawn wheel;
///  2. the hinge angle <see cref="LandingGear.LegHingeRad"/> computes is the one the model's own build script
///     published on the node;
///  3. how far that angle has to be corrected for the model's own hinge LINE — nothing on the J-3, 14-16 % on the
///     Great Lakes, which is why the drawn side solves against the measured axis instead of taking this angle;
///  4. the SIGN: compression swings the contact UP and OUTBOARD, extension down and inboard;
///  5. the sim publishes the leg's stroke per leg (<see cref="WheelState.LegStrokeM"/>), which is the one thing the
///     drawn side cannot work out for itself.
///
/// SCOPE. Exactly two GLBs carry the hinge rigged with a wheel to put on it: j3-cub-like-own and great-lakes-like-own
/// (three liveries). j3-cub-skis-like-own has Gear_Pivot_L/R and Gear_HingeAxis_L/R but no Wheel_L/R at all — it is on
/// skis — so it has no drawn contact to measure and is deliberately left alone. Every other type in the fleet has no
/// pivot node and must be untouched.
/// </summary>
public class GearVisualTests
{
    private readonly ITestOutputHelper _out;
    public GearVisualTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
    private static string Glb(string model) => Path.Combine(OwnPittsModelTests.Models, model, model + "-own.glb");

    private static GearConfig[] Mains(AircraftConfig c)
        => c.Gear.Where(g => !g.IsTailwheel && System.Math.Abs(g.Pos[1]) > 0.3 && g.GearType != "float-keel").ToArray();

    // ---------------------------------------------------------------------------------------------------------------
    // Reading the model. The frame is the one GearGeometryTests uses: (fwd, up, span) = (-gltf x, gltf y, gltf z), and
    // the model's LEFT is +span (= the config's -y). Only differences from the pivot are ever used, so the model's own
    // x datum and height datum cannot enter.
    // ---------------------------------------------------------------------------------------------------------------

    private readonly record struct P3(double Fwd, double Up, double Span)
    {
        public static P3 operator -(P3 a, P3 b) => new(a.Fwd - b.Fwd, a.Up - b.Up, a.Span - b.Span);
        public double Len => System.Math.Sqrt(Fwd * Fwd + Up * Up + Span * Span);
        public P3 Unit { get { double n = Len; return new P3(Fwd / n, Up / n, Span / n); } }
        public override string ToString() => $"({Fwd:F4}, {Up:F4}, {Span:F4})";
    }

    /// <summary>A named node's position in the model (empties included — they carry no mesh, so Vertices misses them).</summary>
    private static P3 Node(string path, string name)
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes");
        int n = nodes.GetArrayLength();
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = -1;
        for (int i = 0; i < n; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        for (int i = 0; i < n; i++)
            if (nodes[i].TryGetProperty("name", out JsonElement nm) && nm.GetString() == name)
            {
                double[] m = OwnPittsModelTests.World(nodes, parent, i);
                return new P3(-m[12], m[13], m[14]);
            }
        throw new Xunit.Sdk.XunitException(path + ": no node " + name);
    }

    /// <summary>One of a node's own `extras` (what the Blender build script wrote on it), as a double.</summary>
    private static double Extra(string path, string node, string key)
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes");
        for (int i = 0; i < nodes.GetArrayLength(); i++)
            if (nodes[i].TryGetProperty("name", out JsonElement nm) && nm.GetString() == node
                && nodes[i].TryGetProperty("extras", out JsonElement ex) && ex.TryGetProperty(key, out JsonElement v))
                return v.GetDouble();
        throw new Xunit.Sdk.XunitException($"{path}: node {node} has no extras.{key}");
    }

    /// <summary>The LEFT drawn wheel (+span): the lowest point of its tyre — its drawn contact — and the span of its
    /// centre plane, which is where that contact sits laterally. The node and anything the exporter split off it.</summary>
    private static (P3 contact, double lowest) DrawnContact(string path, string wheel)
    {
        var parts = OwnPittsModelTests.Vertices(path);
        var v = parts.Where(kv => kv.Key == wheel || kv.Key.StartsWith(wheel + "_"))
                     .SelectMany(kv => kv.Value).Select(p => new P3(-p.x, p.y, p.z)).Where(p => p.Span > 0).ToList();
        Assert.NotEmpty(v);
        double lowest = v.Min(p => p.Up);
        double span = 0.5 * (v.Max(p => p.Span) + v.Min(p => p.Span));
        double fwd = 0.5 * (v.Max(p => p.Fwd) + v.Min(p => p.Fwd));
        return (new P3(fwd, lowest, span), lowest);
    }

    /// <summary>Rodrigues: <paramref name="v"/> turned <paramref name="rad"/> about the unit axis <paramref name="u"/>.</summary>
    private static P3 Turn(P3 u, P3 v, double rad)
    {
        double c = System.Math.Cos(rad), s = System.Math.Sin(rad);
        double d = u.Fwd * v.Fwd + u.Up * v.Up + u.Span * v.Span;
        var cr = new P3(u.Up * v.Span - u.Span * v.Up, u.Span * v.Fwd - u.Fwd * v.Span, u.Fwd * v.Up - u.Up * v.Fwd);
        return new P3(v.Fwd * c + cr.Fwd * s + u.Fwd * d * (1 - c),
                      v.Up * c + cr.Up * s + u.Up * d * (1 - c),
                      v.Span * c + cr.Span * s + u.Span * d * (1 - c));
    }

    /// <summary>How far a point of the leg RISES when the leg turns <paramref name="rad"/> about the model's own hinge
    /// line — exactly what the drawn side has to solve, and the thing the planar angle only approximates.</summary>
    private static double RiseAbout(P3 axis, P3 arm, double rad) => Turn(axis, arm, rad).Up - arm.Up;

    /// <summary>The angle about the model's own hinge line that raises <paramref name="arm"/> by <paramref name="rise"/>.</summary>
    private static double SolveAbout(P3 axis, P3 arm, double rise)
    {
        double lo = 0, hi = 1.2;                                     // 0 .. 69 deg: past anything a leg can swing
        if (RiseAbout(axis, arm, hi) < rise) return double.NaN;
        for (int i = 0; i < 80; i++)
        {
            double m = 0.5 * (lo + hi);
            if (RiseAbout(axis, arm, m) < rise) lo = m; else hi = m;
        }
        return 0.5 * (lo + hi);
    }

    // model id, config, the rigged pivot / axis / wheel node names
    public static IEnumerable<object[]> Rigged() => new[]
    {
        new object[] { "j3-cub-like", "j3-cub-like.json" },
        new object[] { "great-lakes-like", "great-lakes-like.json" },
    };

    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// (1) THE MODEL DRAWS ITS CONTACT WHERE THE CONFIG AUTHORS IT, so the drawn wheel is wrong by the WHOLE leg
    /// stroke until something turns it: a sitting aeroplane's tyres are drawn buried by the static squash and its
    /// track drawn narrower by that times the splay.
    ///
    /// Measured here, and it is worth having on the record because the Great Lakes' own node says otherwise: its
    /// `hinge_deg_as_exported` is 15.79 deg, i.e. build_great_lakes.py exported the gear in the STATIC pose (it solved
    /// the hinge angle that lands the 3-point height on the published 7 ft 4 in), and anyone reading that would
    /// subtract 15.79 deg from every drawn angle. They must not: the config's own gear point was then taken off that
    /// exported pose, so model and config agree exactly and the drawn offset is ZERO for this type too. The authored
    /// point is whatever the model draws — that is the convention, and it is checkable, which is why it is checked
    /// here rather than assumed from the build script's wording.
    /// </summary>
    [Theory]
    [MemberData(nameof(Rigged))]
    public void TheModelDrawsItsContactAtTheAuthoredGearPoint(string model, string file)
    {
        var c = Load(file);
        GearConfig g = Mains(c).First(m => m.Pos[1] < 0);            // the LEFT main; the model's left is +span
        string path = Glb(model);
        P3 pivot = Node(path, "Gear_Pivot_L");
        (P3 contact, _) = DrawnContact(path, "Wheel_L");

        // (fwd, up, span) -> the config's (x, y, z): y = -span, z = -up.
        double drawnZ = -contact.Up, drawnY = -contact.Span;
        double drawnStroke = g.Pos[2] - drawnZ;                       // + = the model draws its wheel BELOW the authored point
        _out.WriteLine($"{model}: pivot {pivot}, drawn contact {contact} → config frame y {drawnY:F4} z {drawnZ:F4}" +
                       $" | authored y {g.Pos[1]:F4} z {g.Pos[2]:F4} | drawn at a stroke of {drawnStroke * 1000:+0.0;-0.0;0.0} mm");
        Assert.InRange(drawnStroke, -0.005, 0.005);                   // the model IS drawn free-hanging, within 5 mm
        Assert.InRange(drawnY - g.Pos[1], -0.02, 0.02);               // ...and at the authored track, within 2 cm

        // So the error a sitting aeroplane is drawn with is the static stroke, whole.
        double share = StaticMainShare(c), stroke = share * c.Mass.MassKg * 9.81 / g.SpringN;
        Vec3 standing = LandingGear.ContactPointM(g, stroke);
        _out.WriteLine($"  at rest the leg squashes {stroke * 1000:F0} mm, so the drawn tyre is buried {stroke * 1000:F0} mm" +
                       $" and each drawn wheel is {System.Math.Abs(standing.Y - g.Pos[1]) * 1000:F0} mm too far inboard" +
                       $" (track drawn {System.Math.Abs(standing.Y - g.Pos[1]) * 2000:F0} mm narrow)");
        Assert.True(stroke > 0.02, "a static squash under 20 mm would not be worth drawing");
    }

    /// <summary>The share of the weight one main carries in the three-point stance (moments about the tailwheel),
    /// the same arithmetic GearGeometryTests.StaticShare uses.</summary>
    private static double StaticMainShare(AircraftConfig c)
    {
        GearConfig m = Mains(c)[0];
        GearConfig? t = c.Gear.FirstOrDefault(x => x.IsTailwheel && x.GearType != "tip-skid" && x.GearType != "float-keel");
        double xm = m.Pos[0] - c.Mass.Cg[0], xt = t != null ? t.Pos[0] - c.Mass.Cg[0] : xm - 4.0;
        return 0.5 * (1 - System.Math.Clamp(xm / (xm - xt), 0.0, 0.5));
    }

    /// <summary>
    /// (2) THE ANGLE IS THE MODEL'S OWN. build_j3_cub.py solved the hinge angle for 0.100 m of wheel (axle) rise with
    /// the full 3-D arc about Gear_Pivot_L → Gear_HingeAxis_L and published it on the node: `hinge_deg_full` 9.019.
    /// <see cref="LandingGear.HingeRadForRise"/> is the planar form of the same thing, so it must land on that number.
    /// </summary>
    [Fact]
    public void TheHingeAngleIsTheOneTheModelPublishes()
    {
        string path = Glb("j3-cub-like");
        P3 pivot = Node(path, "Gear_Pivot_L"), axle = Node(path, "Wheel_L"), hinge = Node(path, "Gear_HingeAxis_L");
        double rise = Extra(path, "Gear_Pivot_L", "wheel_rise_full_m");
        double published = Extra(path, "Gear_Pivot_L", "hinge_deg_full");

        P3 arm = axle - pivot, axis = (hinge - pivot).Unit;
        double planar = LandingGear.HingeRadForRise(System.Math.Abs(arm.Span), -arm.Up, rise) * 180 / System.Math.PI;
        double exact = SolveAbout(axis, arm, rise) * 180 / System.Math.PI;
        _out.WriteLine($"j3-cub-like: axle {System.Math.Abs(arm.Span):F4} m out, {-arm.Up:F4} m below the pivot;" +
                       $" {rise * 1000:F0} mm of axle rise = {published:F3} deg published, {exact:F4} deg re-solved on the model's axis," +
                       $" {planar:F4} deg from the sim's planar arc (difference {planar - published:+0.000;-0.000;0.000} deg)");
        Assert.InRange(exact - published, -0.01, 0.01);               // we read the model the way its builder did
        Assert.InRange(planar - published, -0.05, 0.05);              // and the sim's planar form is that same angle
    }

    /// <summary>
    /// (3) WHY THE DRAWN SIDE SOLVES ITS OWN ANGLE. <see cref="LandingGear.LegHingeRad"/> is the angle of a leg whose
    /// hinge line is square to the arc. The J-3's is, to 1.97 deg, and turning its drawn leg by that angle lifts the
    /// contact 99.9 % of the asked stroke — nothing to correct. The Great Lakes' axle hinges at the fuselage
    /// CENTRELINE with the compression strut running aft, so its line is canted 35.8 deg in plan and the same angle
    /// lifts the wheel only ~87 % as far: the drawn Great Lakes would sit 13 mm low at a 100 mm stroke. The bridge
    /// therefore solves the angle against the axis it measures on the model, and this test is what says it must.
    ///
    /// REJECTED: handing the drawn side <see cref="LandingGear.LegHingeRad"/> and being done with it (one line, no
    /// solve). It is right for the J-3 to a tenth of a millimetre and wrong for the Great Lakes by an eighth of its
    /// travel, and the next rigged model's cant is unknown — so the correction is measured per model, not per type.
    /// </summary>
    [Theory]
    [MemberData(nameof(Rigged))]
    public void TheDrawnLegNeedsItsOwnAxisNotThePlanarOne(string model, string file)
    {
        var c = Load(file);
        GearConfig g = Mains(c).First(m => m.Pos[1] < 0);
        string path = Glb(model);
        P3 pivot = Node(path, "Gear_Pivot_L"), hinge = Node(path, "Gear_HingeAxis_L");
        (P3 contact, _) = DrawnContact(path, "Wheel_L");
        P3 axis = (hinge - pivot).Unit, arm = contact - pivot;
        double cant = System.Math.Acos(System.Math.Min(1, System.Math.Abs(axis.Fwd))) * 180 / System.Math.PI;
        _out.WriteLine($"{model}: hinge axis {axis} — canted {cant:F2} deg from fore-and-aft; contact arm {arm}");

        double worst = 1.0;
        foreach (double stroke in new[] { 0.02, 0.05, 0.10 })
        {
            double planar = LandingGear.LegHingeRad(g, stroke);
            double got = RiseAbout(axis, arm, planar);                // what the planar angle actually lifts the DRAWN wheel
            double needed = SolveAbout(axis, arm, stroke);
            worst = System.Math.Min(worst, got / stroke);
            _out.WriteLine($"  stroke {stroke * 1000,3:F0} mm: planar {planar * 180 / System.Math.PI,6:F3} deg lifts {got * 1000,6:F1} mm" +
                           $" ({100 * got / stroke:F1} % of asked) | the model's axis needs {needed * 180 / System.Math.PI,6:F3} deg (x{needed / planar:F3})");
            // Whatever the cant, the solve against the model's own axis hits the stroke.
            Assert.InRange(RiseAbout(axis, arm, needed) - stroke, -1e-4, 1e-4);
        }
        // The two rigged models, stated so a third one's cant cannot slip in unnoticed.
        if (model == "j3-cub-like") { Assert.InRange(cant, 0.0, 3.0); Assert.True(worst > 0.995, $"the J-3's hinge is square to the arc: {worst:P1}"); }
        else { Assert.InRange(cant, 30.0, 40.0); Assert.InRange(worst, 0.80, 0.90); }
    }

    /// <summary>
    /// (4) THE SIGN, which is the whole point of the job and the one thing that cannot be got wrong: COMPRESSION
    /// swings the contact UP and OUTBOARD, extension toward free hang takes it DOWN and INBOARD. The authored point is
    /// free hang (test 1), so the drawn wheel must be turned up and outboard from where the model draws it, by the
    /// leg's current stroke — never down. Held on the sim's kinematics and again on the drawn arc, because getting
    /// it backwards would draw the aeroplane standing on wheels narrower and lower than its own model.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like.json")]
    [InlineData("great-lakes-like.json")]
    [InlineData("pitts-s2b-like.json")]
    [InlineData("extra-300-like.json")]
    public void CompressionTakesTheContactUpAndOutboard(string file)
    {
        var c = Load(file);
        foreach (GearConfig g in Mains(c))
        {
            double side = System.Math.Sign(g.Pos[1]);
            Vec3 prev = g.PosVec();
            double prevAngle = 0;
            for (double stroke = 0.01; stroke <= 0.12; stroke += 0.01)
            {
                Vec3 p = LandingGear.ContactPointM(g, stroke);
                double angle = LandingGear.LegHingeRad(g, stroke);
                Assert.True(p.Z < prev.Z, $"{file} y{g.Pos[1]:F2} at {stroke:F2} m: the contact must rise (z {p.Z:F4} vs {prev.Z:F4})");
                Assert.True(side * p.Y > side * prev.Y, $"{file} y{g.Pos[1]:F2} at {stroke:F2} m: the contact must go outboard ({p.Y:F4} vs {prev.Y:F4})");
                Assert.True(angle > prevAngle, $"{file} y{g.Pos[1]:F2} at {stroke:F2} m: the hinge angle must keep opening");
                // It rises by EXACTLY the stroke: that is what the angle is defined to deliver.
                Assert.InRange(g.Pos[2] - p.Z - stroke, -1e-9, 1e-9);
                prev = p; prevAngle = angle;
            }
            Vec3 full = LandingGear.ContactPointM(g, 0.12);
            _out.WriteLine($"{file} main y {g.Pos[1]:+0.00;-0.00}: splay {LandingGear.SplayPerM(g):F2} m out per m" +
                           $" | at 120 mm of stroke the contact is 120 mm up and {System.Math.Abs(full.Y - g.Pos[1]) * 1000:F0} mm out," +
                           $" hinge {LandingGear.LegHingeRad(g, 0.12) * 180 / System.Math.PI:F2} deg");
            Assert.Equal(0.0, LandingGear.LegHingeRad(g, 0.0));        // free hanging: no rotation, the model's own pose
            Assert.Equal(0.0, LandingGear.LegHingeRad(g, -0.05));      // and a nonsense stroke moves nothing
        }
    }

    /// <summary>A type with no pivot authored must get no rotation at all — the drawn gear of every unrigged model
    /// in the fleet stays exactly where its draughtsman put it.</summary>
    [Theory]
    [InlineData("p51d-like.json")]
    [InlineData("dc3-like.json")]
    [InlineData("glider-2-33-like.json")]
    public void ALegWithNoPivotAuthoredNeverMoves(string file)
    {
        var c = Load(file);
        foreach (GearConfig g in c.Gear)
        {
            if (g.LegPivot != null && g.LegPivot.Length >= 3) continue;
            Assert.Equal(0.0, LandingGear.SplayPerM(g));
            Assert.Equal(0.0, LandingGear.LegHingeRad(g, 0.10));
            Assert.Equal(g.Pos[2], LandingGear.ContactPointM(g, 0.10).Z);
        }
    }

    /// <summary>
    /// (5) THE SIM PUBLISHES THE STROKE. <see cref="WheelState.LegStrokeM"/> is the one thing the drawn side cannot
    /// work out for itself, so it has to be right: zero with the wheels in the air, the static squash sitting on the
    /// ground, and the SAME number the force went on at — <see cref="LandingGear.ContactPointM"/>'s own argument.
    /// Checked against the contact point the leg is actually loaded at, not against a second estimate of the squash.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like.json")]
    [InlineData("great-lakes-like.json")]
    public void TheSimPublishesEachLegsStroke(string file)
    {
        var c = Load(file);
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null; Atmosphere.SteadyWind = Vec3.Zero;

        // In the air, well clear of the ground: every leg hangs free and the drawn wheel must not move.
        var air = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -300), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        var airSim = new SimLoop(air);
        for (double t = 0; t < 0.5; t += 0.02) airSim.RunFor(0.02, new ControlInputs(0, 0, 0, 1.0));
        foreach (WheelState w in air.Wheels) Assert.Equal(0.0, w.LegStrokeM);

        // Standing on three points, settled.
        RigidBodyState rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, Vec3.Zero, Vec3.Zero), ControlDeflections.Neutral);
        var sim = new SimLoop(ac);
        for (double t = 0; t < 5; t += 0.02) sim.RunFor(0.02, new ControlInputs(0, -1, 0, 1.0));

        double w0 = c.Mass.MassKg * 9.81;
        for (int i = 0; i < c.Gear.Count; i++)
        {
            GearConfig g = c.Gear[i];
            WheelState ws = ac.Wheels[i];
            if (LandingGear.SplayPerM(g) <= 0) continue;               // tailwheel: no pivot, nothing to draw
            double hinge = LandingGear.LegHingeRad(g, ws.LegStrokeM) * 180 / System.Math.PI;
            Vec3 at = LandingGear.ContactPointM(g, ws.LegStrokeM);
            _out.WriteLine($"{file} leg {i} (y {g.Pos[1]:+0.00;-0.00}): load {ws.LoadN / w0:P0} of W, tyre {ws.TireDeflM * 1000:F1} mm," +
                           $" LEG {ws.LegStrokeM * 1000:F1} mm → hinge {hinge:F2} deg, contact {ws.LegStrokeM * 1000:F1} mm up and" +
                           $" {System.Math.Abs(at.Y - g.Pos[1]) * 1000:F1} mm outboard of where the model draws it");
            Assert.True(ws.InContact, $"{file} leg {i} is not on the ground at rest");
            Assert.InRange(ws.LegStrokeM, 0.015, 0.14);                // a light aeroplane's squash, 15-140 mm
            Assert.True(hinge > 0.5, $"{file} leg {i}: {hinge:F2} deg of hinge is not worth drawing");
            // The stroke is the LEG's, not the whole contact compression: the tyre's share is not in it.
            Assert.True(ws.TireDeflM >= 0 && ws.TireDeflM < ws.LegStrokeM + 0.05);
        }

        // Both mains the same, sitting level: a stroke that differed side to side would be drawn as a list.
        var mains = Enumerable.Range(0, c.Gear.Count).Where(i => !c.Gear[i].IsTailwheel && System.Math.Abs(c.Gear[i].Pos[1]) > 0.3).ToList();
        Assert.Equal(2, mains.Count);
        Assert.InRange(ac.Wheels[mains[0]].LegStrokeM - ac.Wheels[mains[1]].LegStrokeM, -0.002, 0.002);
    }
}
