using System.Text.Json;
using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// The rig a model brings with it (docs/models/*-rig.json) and the maths that drives it. Two halves:
///
///  - the map itself — clamping, running past the ends, wrapping, lever notches. None of this shows in a
///    render: an altimeter hand that clamps at 1000 ft looks exactly like one that doesn't until you climb.
///  - the EXTRA 300's own table, read straight out of the file the Blender script writes, against what the sim
///    can actually drive and against the aeroplane's POH. A regenerated rig that renames a param, drops a node
///    or quietly re-rigs a surface fails here rather than in play.
///
/// The one invariant worth stating plainly: a rig node's map must read ZERO at the param the model was DRAWN
/// at, because the driver applies interp() to the imported rest pose. A rig that breaks it moves every needle
/// off its own dial from the first frame, and no still would show which way.
/// </summary>
public class ModelRigTests
{
    private static RigNode Node(string name, string param, bool move, double[] p, double[] v,
                                double wrap = 0, bool extrap = false, double[]? detents = null,
                                double ax = 0, double ay = 1, double az = 0)
        => new(name, "", param, move, ax, ay, az, 0, 0, 0, p, v, wrap, extrap, detents);

    // ---- the map ---------------------------------------------------------------------------------------------

    [Fact]
    public void AMapRunsStraightBetweenItsPointsAndStopsAtItsEnds()
    {
        RigNode n = Node("Needle", "airspeed_kias", false, new[] { 0.0, 40.0, 260.0 }, new[] { 0.0, 25.0, 335.0 });
        Assert.Equal(0.0, n.Value(0), 9);
        Assert.Equal(25.0, n.Value(40), 9);
        Assert.Equal(180.0, n.Value(40 + 220 * (180 - 25) / 310.0), 6);   // halfway along the second leg
        Assert.Equal(335.0, n.Value(260), 9);
        Assert.Equal(0.0, n.Value(-50), 9);                               // clamps rather than running backwards
        Assert.Equal(335.0, n.Value(400), 9);
    }

    [Fact]
    public void TheAltimeterHandsKeepGoingRoundAndTheCompassCardWrapsOnce()
    {
        RigNode hand = Node("Needle_Alt100_R", "altitude_ft", false, new[] { 0.0, 1000.0 }, new[] { 0.0, 360.0 }, extrap: true);
        Assert.Equal(1080.0, hand.Value(3000), 6);        // three turns up, and it keeps going
        Assert.Equal(-360.0, hand.Value(-1000), 6);       // and below sea level, the other way

        RigNode card = Node("Compass_Card_R", "heading_deg", false, new[] { 0.0, 360.0 }, new[] { 0.0, 360.0 }, wrap: 360);
        Assert.Equal(10.0, card.Value(370), 6);           // round the top, not off the end
        Assert.Equal(350.0, card.Value(-10), 6);
        Assert.Equal(90.0, card.Value(90 + 360 * 7), 6);
    }

    [Fact]
    public void ANotchedLeverSitsInANotch()
    {
        // The Extra's ignition key: OFF, R, L, BOTH, START, and nothing in between.
        RigNode key = Node("MagKey_R", "magneto", false, new[] { 0.0, 1.0, 2.0, 3.0, 4.0 },
                           new[] { 0.0, 30.0, 60.0, 90.0, 130.0 }, detents: new[] { 0.0, 1.0, 2.0, 3.0, 4.0 });
        Assert.Equal(90.0, key.Value(2.8), 9);            // nearest notch, not 84 degrees
        Assert.Equal(60.0, key.Value(2.4), 9);
        Assert.Equal(130.0, key.Value(9.0), 9);
    }

    [Fact]
    public void AMapThatDoesNotAscendIsRejectedRatherThanInterpolatedBackwards()
    {
        Assert.Throws<ArgumentException>(() =>
            Node("Bad", "aileron", false, new[] { 1.0, 0.0, -1.0 }, new[] { -30.0, 0.0, 30.0 }));
        Assert.Throws<ArgumentException>(() =>
            Node("Bad", "aileron", false, new[] { -1.0, 1.0 }, new[] { 0.0 }));
    }

    [Fact]
    public void ATurnAndASlideDoNotConvertToUnityTheSameWay()
    {
        // glTFast negates x. That is a REFLECTION, so a direction to slide along flips its x while an axis to
        // turn about flips the other two. Crossing them rakes a hinge the wrong way and sends every lever
        // backwards, and neither shows in a still.
        RigNode n = Node("X", "rudder", false, new[] { -1.0, 1.0 }, new[] { -30.0, 30.0 }, ax: 0.2, ay: -0.3, az: 0.9);
        Assert.Equal((0.2, 0.3, -0.9), n.TurnAxisUnity);
        Assert.Equal((-0.2, -0.3, 0.9), n.SlideAxisUnity);
    }

    [Fact]
    public void EveryParamTheSimKnowsIsReadableAndAnythingElseIsReportedNotIgnored()
    {
        var inputs = new RigInputs
        {
            Aileron = 0.5, Elevator = -0.25, Rudder = 1, Spoiler = 0.75, Trim = -1, TowRelease = 0.4,
            AirspeedMph = 55, AltitudeFt = 3200, VarioKt = -2.5, HeadingDeg = 275, SideslipDeg = -6,
            BrakeLeft = 0.2, BrakeRight = 0.8, Throttle = 0.6, PropLever = 1, Mixture = 0.9,
            FuelSelector = 2, Magneto = 3, Master = 1,
            CanopyOpen = 0.3, CanopyLock = 0, CanopyJettison = 0.5,
            AirspeedKias = 145, VsiFpm = -700, Rpm = 2450, ManifoldInHg = 24.5,
            OilPressPsi = 72, OilTempF = 190, ChtF = 380, FuelCentreFrac = 0.4,
            TurnRateDps = 3, SlipBall = -0.5, GLoad = 4.2, GMaxSeen = 6.1, GMinSeen = -3.4,
            ComFreqMhz = 122.8, ComStandbyMhz = 121.5, ComVolume = 0.7,
            XpdrSquawk = 1200, XpdrMode = 3, XpdrKnobClicks = 17,
        };
        foreach ((string param, double want) in new[]
                 {
                     ("aileron", 0.5), ("elevator", -0.25), ("rudder", 1.0), ("spoiler", 0.75), ("trim", -1.0),
                     ("tow_release", 0.4), ("airspeed_mph", 55.0), ("altitude_ft", 3200.0), ("vario_kt", -2.5),
                     ("heading_deg", 275.0), ("sideslip_deg", -6.0),
                     ("brake_left", 0.2), ("brake_right", 0.8), ("throttle", 0.6), ("prop", 1.0), ("mixture", 0.9),
                     ("fuel_selector", 2.0), ("magneto", 3.0), ("master", 1.0),
                     ("canopy_open", 0.3), ("canopy_lock", 0.0), ("canopy_jettison", 0.5),
                     ("airspeed_kias", 145.0), ("vsi_fpm", -700.0), ("rpm", 2450.0), ("manifold_inhg", 24.5),
                     ("oil_press_psi", 72.0), ("oil_temp_f", 190.0), ("cht_f", 380.0), ("fuel_centre_frac", 0.4),
                     ("turn_rate_dps", 3.0), ("slip_ball", -0.5), ("g_load", 4.2), ("g_max_seen", 6.1), ("g_min_seen", -3.4),
                     ("com_freq_mhz", 122.8), ("com_standby_mhz", 121.5), ("com_volume", 0.7),
                     ("xpdr_squawk", 1200.0), ("xpdr_mode", 3.0), ("xpdr_knob_clicks", 17.0),
                 })
        {
            Assert.True(inputs.TryGet(param, out double got), $"the sim should supply '{param}'");
            Assert.Equal(want, got, 9);
        }
        Assert.False(inputs.TryGet("flap", out _));
        Assert.False(inputs.TryGet("gear", out _));
    }

    [Fact]
    public void ATakenControlSetDoesNotDisturbTheInstruments()
    {
        var panel = new RigInputs { AirspeedKias = 120, AltitudeFt = 4000, Rpm = 2300, Aileron = 0 };
        panel.TakeControlsFrom(new RigInputs { Aileron = 1, Elevator = -0.5, Rudder = 0.25, Spoiler = 0.5 });
        Assert.Equal(1.0, panel.Aileron, 9);
        Assert.Equal(-0.5, panel.Elevator, 9);
        Assert.Equal(120.0, panel.AirspeedKias, 9);     // the panel was not touched
        Assert.Equal(2300.0, panel.Rpm, 9);
    }

    [Fact]
    public void AParkedCockpitHasItsSwitchesWhereAPilotLeavesThem()
    {
        RigInputs parked = RigInputs.Parked();
        Assert.Equal(3.0, parked.Magneto, 9);           // BOTH
        Assert.Equal(1.0, parked.Master, 9);
        Assert.Equal(1.0, parked.CanopyLock, 9);        // shut and locked
        Assert.Equal(0.0, parked.CanopyOpen, 9);
        Assert.Equal(1.0, parked.Mixture, 9);
        Assert.Equal(0.0, parked.GLoad, 9);             // no readings: a remote panel is nobody's to read
    }

    // ---- the Extra's own table -------------------------------------------------------------------------------

    /// <summary>Read a rig table the way the game does, with System.Text.Json (the Unity side uses Newtonsoft,
    /// which this suite does not carry — the FILE is the contract, not the parser).</summary>
    private static ModelRig Read(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", file);
        Assert.True(File.Exists(path), $"the rig table should be beside the test data at {path}");
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
                row.GetProperty("desc").GetString() ?? "",
                extra.TryGetProperty("rig_jettison_param", out JsonElement jp) ? jp.GetString() ?? "" : ""));
        }
        return new ModelRig(root.GetProperty("model").GetString()!, nodes);
    }

    private static ModelRig Extra() => Read("ea300-own-rig.json");

    [Fact]
    public void TheExtrasRigAsksOnlyForThingsTheSimCanGive()
    {
        ModelRig rig = Extra();
        Assert.Equal("extra-300-like-own", rig.Model);
        Assert.Equal(60, rig.Nodes.Count);
        var inputs = new RigInputs();
        foreach (string param in rig.Params())
            Assert.True(inputs.TryGet(param, out _), $"the rig wants '{param}' and the sim has no such value");
    }

    /// <summary>The invariant the whole driver rests on: interp() is applied to the node's IMPORTED pose, so the
    /// map has to read zero at the param the model was drawn at. Checked for every node, with the axes while we
    /// are here — a non-unit axis turns the right way by the wrong amount.</summary>
    [Fact]
    public void EveryNodeIsDrawnWhereItsOwnMapSaysZero()
    {
        foreach (RigNode n in Extra().Nodes)
        {
            Assert.Equal(0.0, n.Value(n.ModelAt), 6);
            double len = Math.Sqrt(n.Ax * n.Ax + n.Ay * n.Ay + n.Az * n.Az);
            Assert.Equal(1.0, len, 3);
        }
    }

    /// <summary>The POH travels (EA 300/L: aileron ±30, elevator ±25, rudder ±30) and the sense of each, which
    /// is the half a render cannot check. The rig's own convention: + degrees about its axis is trailing edge up
    /// / to starboard.</summary>
    [Fact]
    public void TheControlSurfacesCarryTheTravelInThePoh()
    {
        ModelRig rig = Extra();
        RigNode ailL = rig.Find("Aileron_L")!, ailR = rig.Find("Aileron_R")!;
        RigNode eleL = rig.Find("Elevator_L")!, eleR = rig.Find("Elevator_R")!;
        RigNode rud = rig.Find("Rudder")!;

        // Roll right: the LEFT aileron goes trailing edge DOWN and the right one UP, by the same 30 degrees.
        Assert.Equal(-30.0, ailL.Value(1), 6);
        Assert.Equal(30.0, ailR.Value(1), 6);
        Assert.Equal(30.0, ailL.Value(-1), 6);
        Assert.Equal(-30.0, ailR.Value(-1), 6);

        // Stick aft: both elevator halves trailing edge UP, 25 degrees.
        Assert.Equal(25.0, eleL.Value(1), 6);
        Assert.Equal(25.0, eleR.Value(1), 6);
        Assert.Equal(-25.0, eleL.Value(-1), 6);

        // Right pedal: rudder trailing edge to starboard, 30 degrees.
        Assert.Equal(30.0, rud.Value(1), 6);
        Assert.Equal(-30.0, rud.Value(-1), 6);
        Assert.Equal(0.0, rud.Value(0), 9);
    }

    [Fact]
    public void TheCanopyIsTheOnePartThatComesOff()
    {
        ModelRig rig = Extra();
        var comesOff = new List<string>();
        foreach (RigNode n in rig.Nodes) if (n.JettisonParam.Length > 0) comesOff.Add(n.Node);
        Assert.Equal(new[] { "Canopy" }, comesOff);          // nothing else leaves on a bailout

        RigNode canopy = rig.Find("Canopy")!;
        Assert.Equal("canopy_jettison", canopy.JettisonParam);
        Assert.Equal("canopy_open", canopy.Param);
        Assert.Equal(0.0, canopy.Value(0), 9);
        Assert.Equal(100.0, canopy.Value(1), 6);             // 100 degrees over to starboard
        // Both lock levers are children of the canopy, so they leave with it.
        Assert.Equal("Canopy", rig.Find("CanopyLock_F")!.Parent);
        Assert.Equal("Canopy", rig.Find("CanopyLock_R")!.Parent);
        // ...and the two release handles are on the SILL, which stays.
        Assert.Equal("Cockpit", rig.Find("CanopyRelease_F")!.Parent);
        Assert.Equal(0.04, rig.Find("CanopyRelease_R")!.Value(1), 9);   // a 40 mm pull
    }

    [Fact]
    public void BothSeatsSticksAndPedalsAreOnTheSameControls()
    {
        ModelRig rig = Extra();
        foreach (string node in new[] { "StickRoll_F", "StickRoll_R" }) Assert.Equal("aileron", rig.Find(node)!.Param);
        foreach (string node in new[] { "Stick_F", "Stick_R" }) Assert.Equal("elevator", rig.Find(node)!.Param);
        foreach (string node in new[] { "Pedal_F_L", "Pedal_F_R", "Pedal_R_L", "Pedal_R_R" })
            Assert.Equal("rudder", rig.Find(node)!.Param);
        // The pitch stick rides on the roll gimbal, and the toe brakes ride on their pedals.
        Assert.Equal("StickRoll_F", rig.Find("Stick_F")!.Parent);
        Assert.Equal("Pedal_R_L", rig.Find("Brake_R_L")!.Parent);
        Assert.Equal("brake_left", rig.Find("Brake_R_L")!.Param);
        Assert.Equal("brake_right", rig.Find("Brake_F_R")!.Param);
        // A child is listed after its parent, so applying the file in order never poses a child first.
        var seen = new HashSet<string>();
        foreach (RigNode n in rig.Nodes)
        {
            if (n.Parent.Length > 0 && rig.Find(n.Parent) != null)
                Assert.True(seen.Contains(n.Parent), $"{n.Node} is listed before its parent {n.Parent}");
            seen.Add(n.Node);
        }
    }

    [Fact]
    public void ThePanelsNeedlesSweepTheWayTheirDialsAreDrawn()
    {
        ModelRig rig = Extra();
        // The accelerometer: 12 degrees per g either side of nine o'clock, and the tell-tales only go one way.
        Assert.Equal(120.0, rig.Find("Needle_G_R")!.Value(10), 6);
        Assert.Equal(-120.0, rig.Find("Needle_G_R")!.Value(-10), 6);
        Assert.Equal(120.0, rig.Find("Needle_GMax_R")!.Value(10), 6);
        Assert.Equal(0.0, rig.Find("Needle_GMax_R")!.Value(-5), 6);      // the max hand never goes below zero
        Assert.Equal(-120.0, rig.Find("Needle_GMin_R")!.Value(-10), 6);
        Assert.Equal(0.0, rig.Find("Needle_GMin_R")!.Value(5), 6);

        // The two altimeter hands: one turn per 1000 ft and one per 10 000, both unbounded.
        Assert.Equal(360.0, rig.Find("Needle_Alt100_R")!.Value(1000), 6);
        Assert.Equal(360.0, rig.Find("Needle_Alt1000_R")!.Value(10000), 6);
        Assert.Equal(3600.0, rig.Find("Needle_Alt100_R")!.Value(10000), 6);

        // VSI, turn coordinator and ball, all symmetric about neutral.
        Assert.Equal(160.0, rig.Find("Needle_VSI_R")!.Value(3000), 6);
        Assert.Equal(-160.0, rig.Find("Needle_VSI_R")!.Value(-3000), 6);
        Assert.Equal(40.0, rig.Find("TC_Symbol_R")!.Value(6), 6);
        Assert.Equal(18.0, rig.Find("TC_Ball_R")!.Value(1), 6);

        // The ASI. The rig doc's prose says the needle "rests at 0 below 40", but the MAP it ships is
        // 0 kt -> 0 deg, 40 -> 25, 260 -> 335: the needle lifts off the peg smoothly from a standstill rather
        // than sitting on it to 40 kt. The map is what drives the needle, and a needle creeping up off the peg
        // is what a real ASI does, so the data is kept and the prose is the part that is loose
        // (reported to Codex, 2026-10-30). This test pins the DATA so a silent change to it is caught.
        Assert.Equal(12.5, rig.Find("Needle_ASI_R")!.Value(20), 6);
        Assert.Equal(25.0, rig.Find("Needle_ASI_R")!.Value(40), 6);
        Assert.Equal(335.0, rig.Find("Needle_ASI_R")!.Value(260), 6);
        Assert.Equal(0.0, rig.Find("Needle_ASI_R")!.Value(0), 9);

        // The wet compass reads BACKWARDS by design: the card turns with the heading so the number sits under
        // the lubber line, and it wraps instead of unwinding.
        RigNode card = rig.Find("Compass_Card_R")!;
        Assert.Equal(360.0, card.WrapPeriod, 9);
        Assert.Equal(90.0, card.Value(90), 6);
        Assert.Equal(10.0, card.Value(370), 6);
    }
}
