using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The rig a model brings with it (docs/models/*-rig.json) and the maths that drives it. Two halves:
///
///  - the map itself — clamping, running past the ends, wrapping, lever notches. None of this shows in a
///    render: an altimeter hand that clamps at 1000 ft looks exactly like one that doesn't until you climb.
///  - the SGS 2-33's own table, read straight out of the file the Blender script writes, against what the
///    sim can actually drive and against the aeroplane's manual. A regenerated rig that renames a param,
///    drops a node or quietly re-rigs a surface fails here rather than in play.
///
/// The sign chain gets its own test: the file's axes are in the model's glTF frame, and whether a given
/// control goes the right way depends on a reflection that is easy to get backwards.
/// </summary>
// INSTALLS a world into the global WorldTerrain.Active: must not run in parallel with anything that steps
// an aeroplane, because LandingGear, AirframeContact and FloatHydro all read the ground through it.
[Collection("WorldTerrainActive")]
public class ModelRigTests
{
    private static RigNode Node(string name, string param, bool move, double[] p, double[] v,
                                double wrap = 0, bool extrap = false, double[]? detents = null,
                                double ax = 0, double ay = 1, double az = 0)
        => new(name, "", param, move, ax, ay, az, 0, 0, 0, p, v, wrap, extrap, detents);

    [Fact]
    public void AMapRunsStraightBetweenItsPointsAndStopsAtItsEnds()
    {
        // The left aileron's real travel: 38 deg up one way, 18 down the other, about one hinge.
        RigNode ail = Node("Aileron_L", "aileron", false, new[] { -1.0, 0.0, 1.0 }, new[] { -38.0, 0.0, 18.0 });
        Assert.Equal(-38.0, ail.Value(-1.0), 6);
        Assert.Equal(0.0, ail.Value(0.0), 6);
        Assert.Equal(18.0, ail.Value(1.0), 6);
        Assert.Equal(-19.0, ail.Value(-0.5), 6);
        Assert.Equal(9.0, ail.Value(0.5), 6);
        // A stick cannot ask for more than full travel.
        Assert.Equal(-38.0, ail.Value(-4.0), 6);
        Assert.Equal(18.0, ail.Value(4.0), 6);

        // The ASI's dial is not linear in airspeed: 0..20 mph covers 15 deg, 20..160 covers the other 330.
        RigNode asi = Node("Needle_ASI_F", "airspeed_mph", false, new[] { 0.0, 20.0, 160.0 }, new[] { 0.0, 15.0, 345.0 });
        Assert.Equal(7.5, asi.Value(10), 6);
        Assert.Equal(15.0, asi.Value(20), 6);
        Assert.Equal(180.0, asi.Value(90), 6);
        Assert.Equal(345.0, asi.Value(500), 6);   // off the top of the dial: the needle stops on the peg
    }

    [Fact]
    public void TheAltimeterHandsKeepGoingRoundAndTheCompassCardWrapsOnce()
    {
        RigNode hundreds = Node("Needle_Alt100_F", "altitude_ft", false, new[] { 0.0, 1000.0 }, new[] { 0.0, 360.0 }, extrap: true);
        RigNode thousands = Node("Needle_Alt1000_F", "altitude_ft", false, new[] { 0.0, 10000.0 }, new[] { 0.0, 360.0 }, extrap: true);
        Assert.Equal(360.0 * 4.5, hundreds.Value(4500), 6);    // four and a half turns at 4500 ft
        Assert.Equal(360.0 * 0.45, thousands.Value(4500), 6);
        Assert.Equal(-36.0, hundreds.Value(-100), 6);          // below sea level the hand goes the other way

        // The compass card is a drum: the param runs round and round, and 370 deg is 010.
        RigNode card = Node("Compass_Card", "heading_deg", false, new[] { 0.0, 360.0 }, new[] { 0.0, 360.0 }, wrap: 360);
        Assert.Equal(10.0, card.Value(370), 6);
        Assert.Equal(350.0, card.Value(-10), 6);
        Assert.Equal(90.0, card.Value(90 + 7200), 6);
    }

    [Fact]
    public void ANotchedLeverSitsInANotch()
    {
        // The 2-33's trim is a four-position quadrant, not a continuum: the lever is always in a notch, even
        // though the sim's trim is a smooth bias. The nearest notch is what the pilot sees.
        var detents = new[] { -1.0, -0.3333, 0.3333, 1.0 };
        RigNode trim = Node("Trim_Lever", "trim", false, new[] { -1.0, 0.0, 1.0 }, new[] { -20.0, 0.0, 20.0 }, detents: detents);
        Assert.Equal(-1.0, trim.Snap(-0.9), 6);
        Assert.Equal(-0.3333, trim.Snap(-0.2), 6);
        Assert.Equal(0.3333, trim.Snap(0.1), 6);      // nothing sits at zero: there is no middle notch
        Assert.Equal(1.0, trim.Snap(0.8), 6);
        Assert.Equal(20.0 * 0.3333, trim.Value(0.1), 4);
        Assert.Equal(-20.0, trim.Value(-0.95), 6);
    }

    [Fact]
    public void AMapThatDoesNotAscendIsRejectedRatherThanInterpolatedBackwards()
    {
        Assert.Throws<ArgumentException>(() =>
            Node("bad", "aileron", false, new[] { 1.0, 0.0 }, new[] { 0.0, 1.0 }));
        Assert.Throws<ArgumentException>(() =>
            Node("bad", "aileron", false, new[] { 0.0, 0.0 }, new[] { 0.0, 1.0 }));
        Assert.Throws<ArgumentException>(() =>
            Node("bad", "aileron", false, new[] { 0.0, 1.0 }, new[] { 0.0 }));
    }

    [Fact]
    public void EveryParamTheSimKnowsIsReadableAndAnythingElseIsReportedNotIgnored()
    {
        var inputs = new RigInputs { Aileron = 0.5, Elevator = -0.25, Rudder = 1, Spoiler = 0.75, Trim = -1, TowRelease = 0.4,
                                     AirspeedMph = 55, AltitudeFt = 3200, VarioKt = -2.5, HeadingDeg = 275, SideslipDeg = -6 };
        foreach ((string param, double want) in new[]
                 {
                     ("aileron", 0.5), ("elevator", -0.25), ("rudder", 1.0), ("spoiler", 0.75), ("trim", -1.0),
                     ("tow_release", 0.4), ("airspeed_mph", 55.0), ("altitude_ft", 3200.0), ("vario_kt", -2.5),
                     ("heading_deg", 275.0), ("sideslip_deg", -6.0),
                 })
        {
            Assert.True(inputs.TryGet(param, out double got), $"the sim should supply '{param}'");
            Assert.Equal(want, got, 9);
        }
        Assert.False(inputs.TryGet("flap", out _));
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    public void CockpitLatchContractIsTheInverseOfExistingLock(double locked, double released)
    {
        // With no hand on a latch the cockpit draws `canopy_latch` as the inverse of the lock (Codex's visual contract);
        // with one, the hand's own latch (lane/claude-vr-cockpit). Merged 2026-11-02: one rule, in Core.
        var inputs = new RigInputs { CanopyLock = locked, CanopyLatch = RigInputs.CanopyLatchFor(locked >= 0.5, null) };
        Assert.True(inputs.TryGet("canopy_latch", out double got));
        Assert.Equal(released, got);
        Assert.Equal(0.3, RigInputs.CanopyLatchFor(true, 0.3));
    }

    // ---- the 2-33's own table -------------------------------------------------------------------------------

    private static ModelRig Sgs233()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "sgs233-own-rig.json");
        Assert.True(File.Exists(path), $"the 2-33's rig table should be beside the test data at {path}");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        var nodes = new List<RigNode>();
        foreach (JsonElement row in root.GetProperty("rig").EnumerateArray())
        {
            var mp = new List<double>(); var mv = new List<double>();
            foreach (JsonElement pt in row.GetProperty("map").EnumerateArray())
            {
                mp.Add(pt[0].GetDouble()); mv.Add(pt[1].GetDouble());
            }
            JsonElement axis = row.GetProperty("axis_gltf"), pivot = row.GetProperty("pivot_gltf");
            JsonElement extra = row.GetProperty("extra");
            double[]? detents = null;
            if (extra.TryGetProperty("rig_detents", out JsonElement det))
            {
                var d = new List<double>();
                foreach (JsonElement e in det.EnumerateArray()) d.Add(e.GetDouble());
                detents = d.ToArray();
            }
            nodes.Add(new RigNode(
                row.GetProperty("node").GetString()!,
                row.GetProperty("parent").ValueKind == JsonValueKind.Null ? "" : row.GetProperty("parent").GetString()!,
                row.GetProperty("param").GetString()!,
                row.GetProperty("kind").GetString() == "move",
                axis[0].GetDouble(), axis[1].GetDouble(), axis[2].GetDouble(),
                pivot[0].GetDouble(), pivot[1].GetDouble(), pivot[2].GetDouble(),
                mp.ToArray(), mv.ToArray(),
                extra.TryGetProperty("rig_map_wrap", out JsonElement w) ? w.GetDouble() : 0.0,
                extra.TryGetProperty("rig_map_extrapolate", out JsonElement ex) && ex.GetInt32() != 0,
                detents,
                row.GetProperty("model").GetDouble(),
                row.GetProperty("desc").GetString() ?? ""));
        }
        return new ModelRig(root.GetProperty("model").GetString()!, nodes);
    }

    [Fact]
    public void TheTwoThirtyThreesRigAsksOnlyForThingsTheSimCanGive()
    {
        ModelRig rig = Sgs233();
        Assert.Equal("glider-2-33-like-own", rig.Model);
        Assert.Equal(30, rig.Nodes.Count);

        var neutral = new RigInputs();
        var names = new HashSet<string>();
        foreach (RigNode n in rig.Nodes)
        {
            Assert.True(names.Add(n.Node), $"two rig nodes are called '{n.Node}'; the binder matches by name");
            Assert.True(neutral.TryGet(n.Param, out _), $"'{n.Node}' wants '{n.Param}', which the sim does not supply");
            double len = Math.Sqrt(n.Ax * n.Ax + n.Ay * n.Ay + n.Az * n.Az);
            Assert.True(Math.Abs(len - 1) < 1e-3, $"'{n.Node}' axis is not a unit vector (|a| = {len:F4})");
            Assert.True(n.MapPoints >= 2, $"'{n.Node}' has a one-point map and so cannot move");
            // The model is DRAWN at the neutral of every param, and the map passes through zero there — so
            // the imported pose IS the rest pose, which is what the binder assumes. (The trim lever is the one
            // oddity: its quadrant has no central notch, so at zero trim it snaps to the nearest, 6.7 deg off.)
            bool drawnAtZero = false;
            for (int i = 0; i < n.MapPoints; i++)
            {
                (double param, double value) = n.MapPoint(i);
                if (Math.Abs(param - n.ModelAt) < 1e-9 && Math.Abs(value) < 1e-9) drawnAtZero = true;
            }
            Assert.True(drawnAtZero, $"'{n.Node}' is drawn at {n.Param} = {n.ModelAt}, where its map does not read zero");
        }
        // Parented nodes hang off something the file also rigs, or off a static group in the GLB.
        var staticGroups = new HashSet<string> { "", "Controls", "Instruments_Front", "Interior" };
        foreach (RigNode n in rig.Nodes)
            Assert.True(staticGroups.Contains(n.Parent) || names.Contains(n.Parent),
                        $"'{n.Node}' hangs off '{n.Parent}', which is neither rigged nor a known static group");
    }

    [Fact]
    public void TheControlSurfacesCarryTheTravelInTheManual()
    {
        ModelRig rig = Sgs233();
        // FAA AFM travels. The ailerons are differential: more up than down, and mirrored left to right.
        Assert.Equal(-38.0, rig.Find("Aileron_L")!.Value(-1), 3);
        Assert.Equal(18.0, rig.Find("Aileron_L")!.Value(1), 3);
        Assert.Equal(-18.0, rig.Find("Aileron_R")!.Value(-1), 3);
        Assert.Equal(38.0, rig.Find("Aileron_R")!.Value(1), 3);
        // Both elevator halves move together, 25 up and 23 down.
        foreach (string half in new[] { "Elevator_L", "Elevator_R" })
        {
            Assert.Equal(25.0, rig.Find(half)!.Value(1), 3);
            Assert.Equal(-23.0, rig.Find(half)!.Value(-1), 3);
        }
        Assert.Equal(30.0, rig.Find("Rudder")!.Value(1), 3);
        Assert.Equal(-30.0, rig.Find("Rudder")!.Value(-1), 3);
        // Dive brakes: 85 deg up through the top skin, 75 down through the bottom, and stowed at 0.
        foreach (string door in new[] { "Spoiler_L", "Spoiler_R" }) Assert.Equal(85.0, rig.Find(door)!.Value(1), 3);
        foreach (string door in new[] { "Spoiler_Lower_L", "Spoiler_Lower_R" }) Assert.Equal(75.0, rig.Find(door)!.Value(1), 3);
        foreach (string door in new[] { "Spoiler_L", "Spoiler_R", "Spoiler_Lower_L", "Spoiler_Lower_R" })
            Assert.Equal(0.0, rig.Find(door)!.Value(0), 6);
        // The knobs pull 50 mm; the pedals slide 60 mm either way, the left pair opposite the right.
        Assert.Equal(0.05, rig.Find("TowRelease_F")!.Value(1), 6);
        Assert.Equal(0.05, rig.Find("TowRelease_R")!.Value(1), 6);
        Assert.Equal(0.06, rig.Find("Pedal_F_L")!.Value(1), 6);
        Assert.Equal(0.06, rig.Find("Pedal_F_R")!.Value(1), 6);
        Assert.Equal(1.0, rig.Find("Pedal_F_L")!.Ax, 6);     // mirrored axes, so the same +value is opposite travel
        Assert.Equal(-1.0, rig.Find("Pedal_F_R")!.Ax, 6);
    }

    [Fact]
    public void TheYawStringsFourLinksAddUpToTheSideslip()
    {
        ModelRig rig = Sgs233();
        string[] chain = { "YawString_1", "YawString_2", "YawString_3", "YawString_4" };
        double total = 0;
        for (int i = 0; i < chain.Length; i++)
        {
            RigNode n = rig.Find(chain[i])!;
            Assert.Equal(i == 0 ? "Interior" : chain[i - 1], n.Parent);   // a chain, so the links accumulate
            total += n.Value(20);
        }
        Assert.Equal(20.0, total, 3);          // the tip is laid over by the full sideslip
        Assert.Equal(8.0, rig.Find("YawString_1")!.Value(20), 3);   // 40 % on the first link, 20 on each of the rest
        Assert.Equal(-20.0, rig.Find("YawString_1")!.Value(-20) + rig.Find("YawString_2")!.Value(-20)
                            + rig.Find("YawString_3")!.Value(-20) + rig.Find("YawString_4")!.Value(-20), 3);
    }

    [Fact]
    public void EachControlGoesTheWayThePilotExpectsOnceTheModelIsImported()
    {
        // The chain that is easy to get backwards: the file's axis is in the model's glTF frame, the importer
        // reflects x, and a TURN converts differently from a SLIDE. So take the axis the binder will actually
        // use, turn the point a quarter of the way AFT of the hinge about it, and see where it ends up.
        // Imported model frame: +x nose, +y up, +z PORT (glTF +x aft, +z port, x negated on import).
        ModelRig rig = Sgs233();
        static (double x, double y, double z) Turn(RigNode n, double deg, (double x, double y, double z) p)
        {
            (double ax, double ay, double az) = n.TurnAxisUnity;
            double len = Math.Sqrt(ax * ax + ay * ay + az * az);
            ax /= len; ay /= len; az /= len;
            double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
            double dot = ax * p.x + ay * p.y + az * p.z;
            // Rodrigues, plain right-handed algebra — which is what Unity's quaternions do to the numbers.
            return (p.x * c + (ay * p.z - az * p.y) * s + ax * dot * (1 - c),
                    p.y * c + (az * p.x - ax * p.z) * s + ay * dot * (1 - c),
                    p.z * c + (ax * p.y - ay * p.x) * s + az * dot * (1 - c));
        }
        var aft = (x: -1.0, y: 0.0, z: 0.0);    // a point on the trailing edge, 1 m behind the hinge

        // Right pedal: the rudder's trailing edge goes to STARBOARD (-z).
        var rud = Turn(rig.Find("Rudder")!, rig.Find("Rudder")!.Value(1), aft);
        Assert.True(rud.z < -0.4, $"right rudder must take the trailing edge to starboard; got z={rud.z:F3}");

        // Roll right: the LEFT aileron goes DOWN and the RIGHT one UP.
        var left = Turn(rig.Find("Aileron_L")!, rig.Find("Aileron_L")!.Value(1), aft);
        var right = Turn(rig.Find("Aileron_R")!, rig.Find("Aileron_R")!.Value(1), aft);
        Assert.True(left.y < -0.2, $"rolling right must put the left aileron's trailing edge DOWN; got y={left.y:F3}");
        Assert.True(right.y > 0.2, $"rolling right must put the right aileron's trailing edge UP; got y={right.y:F3}");

        // Stick aft: both elevator halves' trailing edges go UP.
        foreach (string half in new[] { "Elevator_L", "Elevator_R" })
        {
            var e = Turn(rig.Find(half)!, rig.Find(half)!.Value(1), aft);
            Assert.True(e.y > 0.2, $"stick aft must put {half}'s trailing edge UP; got y={e.y:F3}");
        }

        // Air brake out: the upper door rises through the top skin (+y), the lower one drops below (-y). Each
        // is hinged at its FORWARD edge, so the moving edge is the one aft of the hinge.
        var upper = Turn(rig.Find("Spoiler_L")!, rig.Find("Spoiler_L")!.Value(1), aft);
        var lower = Turn(rig.Find("Spoiler_Lower_L")!, rig.Find("Spoiler_Lower_L")!.Value(1), aft);
        Assert.True(upper.y > 0.5, $"the upper dive-brake door must open UPWARD; got y={upper.y:F3}");
        Assert.True(lower.y < -0.5, $"the lower dive-brake door must open DOWNWARD; got y={lower.y:F3}");

        // The tow release pulls AFT (-x in the imported frame), a slide not a turn.
        RigNode knob = rig.Find("TowRelease_F")!;
        (double sx, _, _) = knob.SlideAxisUnity;
        Assert.True(sx * knob.Value(1) < 0, "pulling the release must move the knob AFT (imported -x)");
    }

    // ---- the brake on the end of the handle ------------------------------------------------------------------

    [Fact]
    public void TheAirBrakeHandleBringsOnTheWheelBrakeAtTheEndOfItsTravel()
    {
        AircraftConfig cfg = TestAircraftConfig.Load();
        SpoilerConfig sp = cfg.Controls.Spoiler;
        Assert.Equal(0.9, sp.WheelBrakeFromSpoiler, 6);
        Assert.Equal(1.0, sp.WheelBrakeFullSpoiler, 6);
        Assert.Equal(0.0, sp.BrakeFromHandle(0.0), 6);
        Assert.Equal(0.0, sp.BrakeFromHandle(0.5), 6);     // half brakes on the approach: the wheel runs free
        Assert.Equal(0.0, sp.BrakeFromHandle(0.9), 6);
        Assert.Equal(0.5, sp.BrakeFromHandle(0.95), 6);
        Assert.Equal(1.0, sp.BrakeFromHandle(1.0), 6);

        // A type with no such linkage gets no brake from its spoilers, however far out they are.
        var none = new SpoilerConfig();
        Assert.Equal(0.0, none.BrakeFromHandle(1.0), 6);
    }

    // What the band does in play is Sgs233BrakeSquealTests: since 2026-11-05 the sim no longer reads the wheel brake
    // off the spoiler (full spoiler on the touch knob was full wheel brake), the input's handle band does. The rollout
    // that stood here (FullAirBrakeStopsTheGliderSoonerWithNoHandOnTheBrake) already returned NaN for both runs at
    // 59d0f7ed, so it had stopped measuring anything.

    [Fact]
    public void TheConfigAndTheRigTableAgreeAboutThatBrake()
    {
        // The model's own table carries the same coupling as advice ("wheel_brake_from_spoiler"). If a
        // rebuilt rig changes it, the config has to follow, and this is where that is noticed.
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", "sgs233-own-rig.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        double from = double.NaN, full = double.NaN;
        foreach (JsonElement row in doc.RootElement.GetProperty("rig").EnumerateArray())
            if (row.GetProperty("extra").TryGetProperty("wheel_brake_from_spoiler", out JsonElement band))
            {
                from = band[0].GetDouble(); full = band[1].GetDouble();
            }
        Assert.False(double.IsNaN(from), "the 2-33's rig should still say where the wheel brake comes in");
        SpoilerConfig sp = TestAircraftConfig.Load().Controls.Spoiler;
        Assert.Equal(from, sp.WheelBrakeFromSpoiler, 6);
        Assert.Equal(full, sp.WheelBrakeFullSpoiler, 6);
    }
}
