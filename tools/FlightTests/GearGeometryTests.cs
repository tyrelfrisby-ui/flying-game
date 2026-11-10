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
/// THE LANDING GEAR'S REAL GEOMETRY (Ty 2026-11-10, flying his own aeroplanes in the game):
///   "all airplanes are overly difficult to steer on asphalt so I think the friction is too high or there isn't enough
///    flex in the gear or something"
///   "They go up on one wheel and strike wingtips rather than using their suspension. The pitts has bungee like the cub
///    no rebound but plenty of travel. The extra has springy fiberglass that flexes nicely"
///   "The cub has bungee too. Pivot at the fuselage attachment and so does the pitts"
///
/// A Cub's and a Pitts' gear legs are rigid and HINGE ON THE FUSELAGE, so compressing them swings the wheels along an
/// arc — outboard as well as up — and the track WIDENS with the load on it. The Extra's fibreglass bow bends and does
/// the same. Before this, every leg in the fleet was a vertical spring at a fixed point: the wheel went straight up and
/// the track never changed.
///
/// These tests hold three things:
///  - the authored pivot is the one in the type's own MODEL (the J-3's and the Great Lakes' GLBs have the hinge rigged
///    for animation; the Pitts' and the Extra's leg meshes are measured where they meet the fuselage);
///  - the track widens by what that geometry says, at rest and at full travel;
///  - what it does to the number this was all about: the side force that puts the aeroplane up on one wheel, against
///    the side force its tyres can actually make.
/// </summary>
[Collection("WorldTerrainActive")]
public class GearGeometryTests : IDisposable
{
    /// <summary>xUnit calls this after EVERY test, pass or fail: the world and the air are process-wide, and a test
    /// that throws half way through a taxi would otherwise leave its surface override installed for the whole run.</summary>
    public void Dispose()
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
    }

    private readonly ITestOutputHelper _out;
    public GearGeometryTests(ITestOutputHelper o) { _out = o; }

    private static AircraftConfig Load(string f) => AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", f));
    private static string Glb(string model, string suffix) => Path.Combine(OwnPittsModelTests.Models, model, model + suffix + ".glb");

    private static GearConfig[] Mains(AircraftConfig c)
        => c.Gear.Where(g => !g.IsTailwheel && System.Math.Abs(g.Pos[1]) > 0.3 && g.GearType != "float-keel").ToArray();

    /// <summary>The leg's run (outboard) and drop (down) from its pivot to its contact point, in metres.</summary>
    private static (double run, double drop) Leg(GearConfig g)
        => (System.Math.Abs(g.Pos[1] - g.LegPivot![1]), g.Pos[2] - g.LegPivot[2]);

    // --------------------------------------------------------------------------------- the model's own linkage

    /// <summary>A named empty (rig) node's position in the model, in the glTF frame → (fwd, up, span).</summary>
    private static (double fwd, double up, double span) Node(string path, string name)
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
                return (-m[12], m[13], m[14]);
            }
        throw new Xunit.Sdk.XunitException(path + ": no node " + name);
    }

    private static List<(double fwd, double up, double span)> Part(string path, params string[] names)
    {
        var parts = OwnPittsModelTests.Vertices(path);
        var all = new List<(double, double, double)>();
        foreach (string nm in names)
        {
            Assert.True(parts.ContainsKey(nm), path + ": no part " + nm);
            all.AddRange(parts[nm].Select(p => ((double)-p.x, (double)p.y, (double)p.z)));
        }
        return all;
    }

    /// <summary>
    /// THE PIVOT IS THE MODEL'S. Per type: the model's own main wheel sets the scale (its drawn contact must be the
    /// config's gear point — <see cref="AerobatGroundHandlingTests.GearMatchesTheModels"/> already holds the Pitts and
    /// the Extra to that), and then the leg's run and drop from the pivot to that contact must be what the config
    /// authors, within 2.5 cm. Differences only, so the model's x shift and height datum cannot enter.
    ///
    /// The J-3 and the Great Lakes carry the hinge as rig nodes, built from the Univair parts list and the 2T-1A-2
    /// layout: those are read straight out. The Pitts' A-frame and the Extra's bow are not rigged, so the authored
    /// pivot is held to their leg MESH instead: the leg must pass through the pivot, and no part of the leg at that
    /// span may stand above it — that is what "where the leg meets the fuselage" means.
    /// </summary>
    [Theory]
    // file, model, wheel part, leg part, rigged pivot node ("" = measure the mesh), tyre radius used in the model
    [InlineData("j3-cub-like.json", "j3-cub-like", "-own", "Wheel_L", "Gear_Leg_L", "Gear_Pivot_L")]
    [InlineData("great-lakes-like.json", "great-lakes-like", "-own", "Wheel_L", "Gear_Axle_L", "Gear_Pivot_L")]
    [InlineData("pitts-s2b-like.json", "pitts-s2b-like", "-own", "Wheels", "Gear_Main", "")]
    [InlineData("extra-300-like.json", "extra-300-like", "-own", "Wheels", "Gear_Main", "")]
    public void ThePivotIsTheOneInTheModel(string file, string model, string suffix, string wheelPart, string legPart, string pivotNode)
    {
        var c = Load(file);
        GearConfig g = Mains(c).First(m => m.Pos[1] > 0);          // the right-hand main; the model's left is +span
        Assert.NotNull(g.LegPivot);
        string path = Glb(model, suffix);

        // The drawn main wheel: its own half of the model, its span centre and the bottom of its tyre.
        var wheel = Part(path, wheelPart).Where(p => p.span > 0.3 && p.fwd > -1.5).ToList();
        Assert.NotEmpty(wheel);
        double wheelSpan = 0.5 * (wheel.Max(p => p.span) + wheel.Min(p => p.span));
        double contactUp = wheel.Min(p => p.up);
        double k = System.Math.Abs(g.Pos[1]) / wheelSpan;          // model → config scale, from the thing being measured
        Assert.InRange(k, 0.95, 1.05);

        double pivotSpan, pivotUp;
        if (pivotNode.Length > 0)
        {
            var p = Node(path, pivotNode);
            (pivotSpan, pivotUp) = (System.Math.Abs(p.span), p.up);
        }
        else
        {
            // The leg's inboard top: the highest leg vertex inboard of half the half-track (the fuselage end of it).
            var legV = Part(path, legPart).Where(p => p.span > 0 && p.span < 0.5 * wheelSpan && p.fwd > -1.5).ToList();
            Assert.NotEmpty(legV);
            pivotUp = legV.Max(p => p.up);
            pivotSpan = legV.Where(p => p.up > pivotUp - 0.02).Average(p => p.span);
        }

        (double run, double drop) = Leg(g);
        double runModel = (wheelSpan - pivotSpan) * k, dropModel = (pivotUp - contactUp) * k;
        _out.WriteLine($"{file}: model pivot span {pivotSpan:F3} up {pivotUp:F3}, contact span {wheelSpan:F3} up {contactUp:F3}, scale {k:F4}" +
                       $" | run {runModel:F3} vs {run:F3} m, drop {dropModel:F3} vs {drop:F3} m" +
                       $" | splay {LandingGear.SplayPerM(g):F2} m out per m of compression, roll centre {LandingGear.SplayPerM(g) * System.Math.Abs(g.Pos[1]):F2} m up" +
                       $" (CG {g.Pos[2] - c.Mass.Cg[2]:F2} m up)");
        Assert.InRange(run - runModel, -0.025, 0.025);
        Assert.InRange(drop - dropModel, -0.025, 0.025);

        // The leg really passes through the authored pivot, and nothing of it stands above the pivot there.
        var leg = Part(path, legPart).Where(p => p.span > 0 && p.fwd > -1.5).ToList();
        double pivotUpCfg = pivotUp, spanCfg = pivotSpan;
        Assert.True(leg.Any(p => System.Math.Abs(p.span - spanCfg) < 0.06 && System.Math.Abs(p.up - pivotUpCfg) < 0.06),
                    $"{file}: no {legPart} geometry within 6 cm of the pivot");
        double above = leg.Where(p => p.span < spanCfg + 0.03).DefaultIfEmpty((0, pivotUpCfg, 0)).Max(p => p.up);
        Assert.InRange(above - pivotUpCfg, -1.0, 0.05);
    }

    // --------------------------------------------------------------------------------- the track widens

    /// <summary>The static load on ONE main, and on the tailwheel, from the three-point stance: moments about the
    /// other wheel. (The share, not the spring — it is the same arithmetic the brake capacity uses.)</summary>
    private static (double main, double tail) StaticShare(AircraftConfig c)
    {
        GearConfig m = Mains(c)[0];
        GearConfig? t = c.Gear.FirstOrDefault(x => x.IsTailwheel && x.GearType != "tip-skid" && x.GearType != "float-keel");
        double xm = m.Pos[0] - c.Mass.Cg[0], xt = t != null ? t.Pos[0] - c.Mass.Cg[0] : xm - 4.0;
        double tail = System.Math.Clamp(xm / (xm - xt), 0.0, 0.5);
        return (0.5 * (1 - tail), tail);
    }

    /// <summary>
    /// How much the track grows: the leg's own compression under the aeroplane's weight (the tyre takes its share
    /// separately and squashing a tyre moves nothing outboard), times the splay. Printed at rest and on the stop, so
    /// the number is on the record for the next person who flies it.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like.json")]
    [InlineData("pa18-cub-like.json")]
    [InlineData("pa18-bush-like.json")]
    [InlineData("pitts-s2b-like.json")]
    [InlineData("extra-300-like.json")]
    [InlineData("great-lakes-like.json")]
    public void TheTrackWidensWithTheLoadOnIt(string file)
    {
        var c = Load(file);
        GearConfig g = Mains(c).First(m => m.Pos[1] > 0);
        (double share, _) = StaticShare(c);
        double w = c.Mass.MassKg * 9.81;
        double staticStroke = share * w / g.SpringN;
        double travel = LandingGear.LegTravelM(g);
        double hard = System.Math.Min(3 * staticStroke, travel);        // a firm arrival, or the stop if it is nearer
        double yStatic = LandingGear.ContactPointM(g, staticStroke).Y, yFull = LandingGear.ContactPointM(g, hard).Y;
        double track = 2 * g.Pos[1];
        _out.WriteLine($"{file}: {c.Mass.MassKg:F0} kg, main carries {share * 100:F0} % at {share * w / 1000:F2} kN, leg {g.SpringN / 1000:F0} kN/m" +
                       $" → squash {staticStroke * 1000:F0} mm of {travel * 1000:F0} mm travel ({LandingGear.KindOf(g)})" +
                       $" | track {track:F3} m → {2 * yStatic:F3} at rest (+{(yStatic - g.Pos[1]) * 2000:F0} mm), {2 * yFull:F3} on a firm arrival at {hard * 1000:F0} mm (+{(yFull - g.Pos[1]) * 2000:F0} mm)");
        Assert.InRange(staticStroke, 0.02, 0.12);                       // 2–12 cm of squash: a light aeroplane's gear
        Assert.True(staticStroke < 0.7 * travel, "the aeroplane sits more than two thirds of the way through its travel");
        Assert.InRange(yStatic - g.Pos[1], 0.015, 0.16);                // 1.5–16 cm wider per side at rest
        Assert.True(yFull > yStatic, "the track must keep widening as the leg compresses");
    }

    // --------------------------------------------------------------------------------- up on one wheel

    /// <summary>
    /// THE NUMBER THIS WAS ABOUT: the side force, as a fraction of the weight, that takes the inner main off the
    /// ground. The aeroplane rolls until the inner leg is fully extended (that is where its load reaches zero —
    /// roll × half-track = the static squash), and at that moment the whole main load is on the outer wheel, so the
    /// weight holds it down with the arm the outer CONTACT then has and the side force prises it up with the CG's
    /// height above that contact: f = y/h, both taken in the rolled, compressed geometry.
    ///
    /// Everything the gear's flex does to it is in there: the outer leg's compression lowers the CG and (with a
    /// hinged leg) swings the contact outboard, and the roll moves the CG out over the wheel the other way.
    /// </summary>
    private static (double f, double rollDeg, double armM, double cgHeightM) TipLateralG(AircraftConfig c, bool withLinkage)
    {
        GearConfig g0 = Mains(c).First(m => m.Pos[1] > 0);
        var g = new GearConfig
        {
            Pos = g0.Pos, SpringN = g0.SpringN, DampNs = g0.DampNs, GearType = g0.GearType,
            LegPivot = withLinkage ? g0.LegPivot : null, LegTravelM = g0.LegTravelM,
        };
        (double share, _) = StaticShare(c);
        double w = c.Mass.MassKg * 9.81;
        double stroke0 = share * w / g.SpringN;                       // each main at rest
        double b = System.Math.Abs(g.Pos[1]);
        double roll = stroke0 / b;                                    // the roll that unloads the inner leg
        Vec3 p = LandingGear.ContactPointM(g, 2 * stroke0);           // the outer leg now carries both mains' load
        double z = p.Z - c.Mass.Cg[2];
        double y = p.Y * System.Math.Cos(roll) - z * System.Math.Sin(roll);
        double h = p.Y * System.Math.Sin(roll) + z * System.Math.Cos(roll);
        return (y / h, roll * 180 / System.Math.PI, y, h);
    }

    /// <summary>What the tyres can actually push sideways with, as a fraction of the weight: each wheel's μ on dry
    /// pavement times the share of the weight it carries (a tailwheel's small hard tyre holds half a main's).</summary>
    private static double GripLateralG(AircraftConfig c)
    {
        (double main, double tail) = StaticShare(c);
        GearConfig m = Mains(c)[0];
        GearConfig? t = c.Gear.FirstOrDefault(x => x.IsTailwheel && x.GearType != "tip-skid" && x.GearType != "float-keel");
        return 2 * main * m.TireMu + (t != null ? tail * t.TireMu * LandingGear.TailwheelMuScale : 0.0);
    }

    [Theory]
    [InlineData("j3-cub-like.json")]
    [InlineData("pa18-cub-like.json")]
    [InlineData("pa18-bush-like.json")]
    [InlineData("pitts-s2b-like.json")]
    [InlineData("extra-300-like.json")]
    [InlineData("great-lakes-like.json")]
    public void TheLinkageRaisesTheSideForceThatTipsItOntoOneWheel(string file)
    {
        var c = Load(file);
        var before = TipLateralG(c, false);
        var after = TipLateralG(c, true);
        double grip = GripLateralG(c);
        _out.WriteLine($"{file}: tips at {before.f:F3} g with the contact fixed, {after.f:F3} g on its linkage " +
                       $"(+{(after.f / before.f - 1) * 100:F0} %; {after.rollDeg:F2}° of roll, arm {before.armM:F3} → {after.armM:F3} m, CG {after.cgHeightM:F3} m up) " +
                       $"| its tyres can make {grip:F3} g — {(after.f > grip ? "it can now scrub at full grip without lifting a wheel" : "STILL TIPS BEFORE IT SLIDES")}");
        Assert.True(after.f > before.f, "the linkage must raise the tipping threshold, not lower it");
        Assert.InRange(after.f / before.f - 1, 0.02, 0.60);
    }

    // --------------------------------------------------------------------------------- the spring itself

    /// <summary>
    /// The kinds are not labels. A bungee travels further before it is on its stop than a bending leg, every kind is
    /// linear in its working range (so the stance, and everything tuned against it, is untouched), and past the stop
    /// it is <see cref="LandingGear.LegStopStiffnessFactor"/> times stiffer. And the rebound is undamped on all of
    /// them — Ty's "no rebound" — because the damper only ever sees the closing rate.
    /// </summary>
    [Fact]
    public void EachKindOfLegTravelsItsOwnDistanceAndThenStops()
    {
        var bungee = new GearConfig { Pos = new[] { 0.0, 1.0, 1.0 }, SpringN = 60000, GearType = "bungee" };
        var leaf = new GearConfig { Pos = new[] { 0.0, 1.0, 1.0 }, SpringN = 60000, GearType = "spring-steel" };
        var bow = new GearConfig { Pos = new[] { 0.0, 1.0, 1.0 }, SpringN = 60000, GearType = "spring-composite" };
        var oleo = new GearConfig { Pos = new[] { 0.0, 1.0, 1.0 }, SpringN = 60000, GearType = "oleo" };
        var plain = new GearConfig { Pos = new[] { 0.0, 1.0, 1.0 }, SpringN = 60000 };
        Assert.True(LandingGear.LegTravelM(bungee) > LandingGear.LegTravelM(bow));
        Assert.True(LandingGear.LegTravelM(bow) > LandingGear.LegTravelM(leaf));
        Assert.True(double.IsPositiveInfinity(LandingGear.LegTravelM(oleo)));     // unmeasured: no stop invented for it
        Assert.True(double.IsPositiveInfinity(LandingGear.LegTravelM(plain)));
        Assert.Equal(0.25, LandingGear.LegTravelM(new GearConfig { SpringN = 1, GearType = "bungee", LegTravelM = 0.25 }));

        // Linear up to the stop, eight times stiffer past it, and the same force as a plain spring in between.
        foreach (double x in new[] { 0.01, 0.05, 0.10, LandingGear.BungeeTravelM })
            Assert.Equal(60000 * x, LandingGear.LegForceN(bungee, x), 3);
        double over = LandingGear.BungeeTravelM + 0.02;
        Assert.Equal(60000 * (LandingGear.BungeeTravelM + 0.02 * LandingGear.LegStopStiffnessFactor), LandingGear.LegForceN(bungee, over), 3);
        Assert.Equal(60000 * over, LandingGear.LegForceN(plain, over), 3);
        Assert.Equal(0.0, LandingGear.LegForceN(bungee, -0.01));

        // No pivot, no splay: the fleet's untouched legs keep their fixed contact point exactly.
        Assert.Equal(0.0, LandingGear.SplayPerM(plain));
        Assert.Equal(1.0, LandingGear.ContactPointM(plain, 0.05).Y);
        Assert.Equal(1.0, LandingGear.ContactPointM(plain, 0.05).Z);
    }

    /// <summary>
    /// The arc is an arc: the contact rises by exactly the stroke, stays the leg's own length from the pivot, and goes
    /// outboard on whichever side it is. (The wheel's camber is in it — <see cref="LandingGear.SplayPerM"/> uses the
    /// pivot-to-CONTACT drop, not the pivot-to-axle drop, because a cambered wheel's lowest point moves outboard with
    /// the leg's rotation on top of the axle's own travel.)
    /// </summary>
    [Fact]
    public void TheContactRunsOnTheArcAboutThePivot()
    {
        foreach (double side in new[] { 1.0, -1.0 })
        {
            var g = new GearConfig
            {
                Pos = new[] { 0.1, side * 0.92, 1.2 }, SpringN = 60000, GearType = "bungee",
                LegPivot = new[] { 0.13, side * 0.32, 0.491 },
            };
            Assert.Equal(0.709 / 0.600, LandingGear.SplayPerM(g), 3);
            double len = System.Math.Sqrt(0.600 * 0.600 + 0.709 * 0.709);
            foreach (double stroke in new[] { 0.0, 0.02, 0.05, 0.12 })
            {
                Vec3 p = LandingGear.ContactPointM(g, stroke);
                Assert.Equal(1.2 - stroke, p.Z, 9);                                   // up by exactly the stroke
                double dy = p.Y - side * 0.32, dz = p.Z - 0.491;
                Assert.Equal(len, System.Math.Sqrt(dy * dy + dz * dz), 9);            // still the leg's length away
                Assert.Equal(side * 0.92 + side * (System.Math.Abs(p.Y) - 0.92), p.Y, 9);
                Assert.True(System.Math.Abs(p.Y) >= 0.92 - 1e-12, "outboard, not inboard");
                if (stroke > 0) Assert.True(System.Math.Abs(p.Y) > 0.92);
            }
            // First order it IS the splay rate; the arc's own curvature is under a millimetre over 20 mm of stroke.
            double exact = System.Math.Abs(LandingGear.ContactPointM(g, 0.02).Y);
            Assert.InRange(exact - (0.92 + LandingGear.SplayPerM(g) * 0.02), -0.001, 0.001);
        }
    }

    /// <summary>
    /// THE OTHER HALF OF THE SAME GEOMETRY, WHICH IS NOT MODELLED — read this before adding it.
    ///
    /// A hinged leg also turns a SIDE force at the contact into leg extension: the leg's own balance is
    /// N = k·stroke − Y_outboard·(drop/run), so a skid's inboard force pushes the body UP (jacking: the reason
    /// swing-axle cars tuck under). It is the same drop/run that gives the splay, and on these aeroplanes it runs
    /// from 0.63 (the Great Lakes' half-axle, hinged on the centreline) to 1.82 (the Extra's bow) — roll centres at
    /// 55 % to 114 % of the CG height. So μ·(drop/run) reaches 1 at the tyre's own grip on the Cub, the Pitts and the
    /// Extra, a rigid-link treatment is singular there (the load would run away inside a step), and the splay above
    /// is kinematics only. Which means this model is optimistic about tipping by about as much as leaving the jacking
    /// out is pessimistic — the honest way to settle it is to model what reacts the side load (the shock strut's own
    /// geometry, the leg's lateral bending, the tyre's lateral give), and that is a bigger piece of work.
    ///
    /// This test exists to put the numbers on the record and to fail if a type is ever authored with a linkage so
    /// steep that nothing could be done with it later.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like.json")]
    [InlineData("pa18-bush-like.json")]
    [InlineData("pitts-s2b-like.json")]
    [InlineData("extra-300-like.json")]
    [InlineData("great-lakes-like.json")]
    public void TheJackingThatComesWithTheSameLinkageIsNotModelled(string file)
    {
        var c = Load(file);
        GearConfig g = Mains(c).First(m => m.Pos[1] > 0);
        double r = LandingGear.SplayPerM(g);
        double rc = r * System.Math.Abs(g.Pos[1]), cg = g.Pos[2] - c.Mass.Cg[2];
        _out.WriteLine($"{file}: drop/run {r:F2} → roll centre {rc:F3} m up, {rc / cg * 100:F0} % of the CG's {cg:F3} m; " +
                       $"jacking factor at μ {g.TireMu:F2} is {r * g.TireMu:F2} ({(r * g.TireMu >= 1 ? "singular as a rigid link" : "bounded")})");
        Assert.InRange(r, 0.3, 2.1);
    }

    // --------------------------------------------------------------------------------- how it steers on asphalt

    /// <summary>Hands off the rudder, rolling at <paramref name="v"/> m/s on pavement: how far it wanders in
    /// <paramref name="seconds"/>, and which way. Returns (heading°, metres off the centreline).</summary>
    private static (double hdgDeg, double offM) StraightTaxi(AircraftConfig c, double v, double seconds)
    {
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = WorldTerrain.Surface.Paved;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
        WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldSolids.Boxes.Clear();
        var rest = LandingGear.RestingState(c, 0, 0, 0);
        var ac = new Aircraft(c, new RigidBodyState(rest.Position, rest.Attitude, rest.Attitude.Conjugate().Rotate(new Vec3(v, 0, 0)), Vec3.Zero),
                              ControlDeflections.Neutral);
        var sim = new SimLoop(ac); const double dt = 0.01;
        for (double t = 0; t < seconds; t += dt) sim.RunFor(dt, new ControlInputs(0, -1, 0, 1.0));   // stick back, idle, feet still
        var q = ac.State.Attitude;
        double hdg = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        WorldTerrain.SurfaceOverride = null;
        return (hdg * 180 / System.Math.PI, ac.State.Position.Y);
    }

    /// <summary>
    /// TOE IS PER LEG, AND FOURTEEN TYPES HAD IT ON ONE WHEEL ONLY. Ty 2026-11-10: "all airplanes are overly difficult
    /// to steer on asphalt". The toe-out commit of that morning says "Sixteen types, both mains each" and wrote the
    /// field to the FIRST main of fourteen of them — and <see cref="LandingGear.ToeRad"/> reads it per leg, so those
    /// aeroplanes were rigged 2° out on one wheel and dead straight on the other: a standing side force of about 0.42
    /// of that wheel's load (12 per rad × 2°) with nothing to cancel it, which is half the tyre's friction budget
    /// spent before the pilot touches anything. The Pitts and the Extra — the two he was flying when he chose the
    /// number — are the two the commit did write both, which is why they felt right.
    ///
    /// This holds the rig symmetric by measuring what it DOES, with the aeroplane's own asymmetries divided out: taxi
    /// hands-off three times on pavement — toe off, toe out on both mains, toe out on one — and compare. A symmetric
    /// pair makes no net side force, so it must taxi where the toe-off aeroplane does (what is left is the engine: at
    /// 5 kt the slipstream and the torque are what actually turn a Cub, and they are in all three runs). One main
    /// alone pulls away from both.
    /// </summary>
    [Theory]
    [InlineData("j3-cub-like.json")]
    [InlineData("great-lakes-like.json")]
    [InlineData("decathlon-8kcab-like.json")]
    [InlineData("pitts-s2b-like.json")]
    public void BothMainsAreToedOutTheSameAndItTaxisStraight(string file)
    {
        var c = Load(file);
        GearConfig[] mains = Mains(c);
        Assert.Equal(2, mains.Length);
        Assert.Equal(mains[0].ToeOutDeg, mains[1].ToeOutDeg);
        Assert.NotEqual(0.0, mains[0].ToeOutDeg);                 // every taildragger carries Ty's 2°
        Assert.Equal(-LandingGear.ToeRad(mains[0]), LandingGear.ToeRad(mains[1]), 9);   // out on both: opposite senses

        double toe = mains[0].ToeOutDeg;
        var rigged = StraightTaxi(c, 2.5, 8.0);
        mains[0].ToeOutDeg = mains[1].ToeOutDeg = 0.0;
        var none = StraightTaxi(c, 2.5, 8.0);
        mains[0].ToeOutDeg = toe;                                 // the rig as it was: toe on the first main only
        var lopsided = StraightTaxi(c, 2.5, 8.0);
        mains[1].ToeOutDeg = toe;
        _out.WriteLine($"{file}: 8 s of hands-off taxi at 5 kt on pavement, {toe:F1}° of toe — " +
                       $"off: {none.hdgDeg,5:F1}°, {none.offM,5:F2} m off | both mains: {rigged.hdgDeg,5:F1}°, {rigged.offM,5:F2} m " +
                       $"(Δ {rigged.offM - none.offM:F2} m) | ONE main: {lopsided.hdgDeg,5:F1}°, {lopsided.offM,5:F2} m " +
                       $"(Δ {lopsided.offM - none.offM:F2} m)");
        // A symmetric pair makes no side force of its own, so it lands within 10 cm of the toe-off run — and where the
        // aeroplane is ALREADY drifting it pulls back toward straight rather than doing nothing, which is the whole
        // point of toe-out: the J-3 idles off to the left on its slipstream, 0.18 m in 8 s with the toe off, and 0.13 m
        // with it. One wheel alone goes the other way, further than the pair ever moves it.
        Assert.InRange(rigged.offM - none.offM, -0.10, 0.10);
        Assert.True(System.Math.Abs(rigged.offM) <= System.Math.Abs(none.offM) + 1e-9,
                    "a symmetric pair must not make the drift worse");
        Assert.InRange(rigged.hdgDeg - none.hdgDeg, -0.5, 0.5);
        Assert.True(System.Math.Abs(lopsided.offM - none.offM) > System.Math.Abs(rigged.offM - none.offM) + 0.05,
                    "toe on one main only must pull it off the line the toe-off aeroplane holds (that is the bug this test is here for)");
    }

    // --------------------------------------------------------------------------------- what the wheels do, rolling out

    /// <summary>
    /// Opt-in (GEARPROBE=1): the wheel loads through a three-point rollout, which is how the mechanism behind
    /// "they go up on one wheel" was found — the Pitts lifts its inner main at about 2.5° of roll, and the swing
    /// runs away from there. Both types, for the record and for before/after.
    /// </summary>
    [Theory]
    [InlineData("pitts-s2b-like.json", 29.0)]
    [InlineData("extra-300-like.json", 31.0)]
    public void RolloutWheelLoads(string file, double v)
    {
        if (System.Environment.GetEnvironmentVariable("GEARPROBE") == null) return;
        WorldTerrain.Active = null; WorldTerrain.SurfaceOverride = null;
        Atmosphere.SteadyWind = Vec3.Zero; Atmosphere.ActiveTurbulence = null; Atmosphere.Field = null;
        WorldDecks.All.Clear(); WorldSolids.Shapes.Clear(); WorldSolids.Boxes.Clear();
        var c = Load(file);
        double three = 0;
        {
            var q = LandingGear.RestingState(c, 0, 0, 0).Attitude;
            three = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        }
        var att = new Quat(0, System.Math.Sin(three / 2), 0, System.Math.Cos(three / 2));
        Vec3 cg = c.Mass.CgVec();
        double mainsBelowCg = 0;
        foreach (GearConfig g in c.Gear) if (!g.IsTailwheel) mainsBelowCg = System.Math.Max(mainsBelowCg, att.Rotate(g.PosVec() - cg).Z);
        var s0 = new RigidBodyState(new Vec3(0, 0, -(0.5 + mainsBelowCg)), att, att.Conjugate().Rotate(new Vec3(v, 0, 0.3)), Vec3.Zero);
        var ac = new Aircraft(c, s0, ControlDeflections.Neutral);
        var sim = new SimLoop(ac); const double dt = 0.01;
        double w = c.Mass.MassKg * 9.81, pInt = 0, worstRoll = 0, liftT = -1;
        bool touched = false;
        double Pitch() { var q = ac.State.Attitude; return System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)); }
        double Roll() { var q = ac.State.Attitude; return System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)); }
        double Hdg() { var q = ac.State.Attitude; return System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)); }
        for (double t = 0; t < 40; t += dt)
        {
            double e = Pitch() - three;
            pInt = System.Math.Clamp(pInt + 2.0 * e * dt, -0.6, 0.6);
            double ele = touched ? -1.0 : System.Math.Clamp(pInt + 8.0 * e + 1.2 * ac.State.Rates.Y, -1, 1);
            double hdgErr = Hdg() - System.Math.Clamp(-0.015 * ac.State.Position.Y, -0.04, 0.04);
            double rCmd = System.Math.Clamp(-1.2 * hdgErr, -0.26, 0.26);
            double rud = touched ? System.Math.Clamp(-AerobatGroundHandlingTests.QuickFeet * (ac.State.Rates.Z - rCmd), -1, 1) : 0.0;
            sim.RunFor(dt, new ControlInputs(0, ele, rud, 1.0));
            if (!touched && (LandingGear.AnyMainWheelOnGround(ac.Config, ac.State) || (ac.Tailwheel?.LastLoadN ?? 0) > 1)) touched = true;
            if (!touched) continue;
            var mains = Enumerable.Range(0, ac.Wheels.Count).Where(i => !c.Gear[i].IsTailwheel && System.Math.Abs(c.Gear[i].Pos[1]) > 0.3).ToList();
            double lo = mains.Min(i => ac.Wheels[i].LoadN), hi = mains.Max(i => ac.Wheels[i].LoadN);
            if (liftT < 0 && hi > 0.05 * w && lo <= 0.0) liftT = t;
            if (System.Math.Abs(Roll()) > System.Math.Abs(worstRoll)) worstRoll = Roll();
            if (System.Math.Abs(t / 1.0 - System.Math.Round(t / 1.0)) < 1e-6)
                _out.WriteLine($"  t {t,5:F2} V {System.Math.Sqrt(ac.State.Velocity.X * ac.State.Velocity.X + ac.State.Velocity.Y * ac.State.Velocity.Y),5:F1}" +
                               $" hdg {Hdg() * 57.3,7:F1} r {ac.State.Rates.Z * 57.3,6:F1} roll {Roll() * 57.3,6:F2} y {ac.State.Position.Y,6:F2}" +
                               $" mains N/W {lo / w:F2}/{hi / w:F2} tail {(ac.Tailwheel?.LastLoadN ?? 0) / w:F2} rud {rud,5:F2}");
            if (System.Math.Abs(Hdg()) > 2.9) { _out.WriteLine($"  >>> {file}: past 165° at t {t:F2}"); break; }
        }
        _out.WriteLine($"{file}: ends heading {Hdg() * 57.3:F1}°, {ac.State.Position.Y:F1} m off, worst roll {worstRoll * 57.3:F2}°, " +
                       $"first inner-main lift at t {liftT:F2}, components lost {ac.LostComponents.Count}");
    }
}
