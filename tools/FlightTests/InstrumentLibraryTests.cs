using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlyingGame.Core.Instruments;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>The shared cockpit instrument library (Ty 2026-11-05 06:53: "we don't need 6 different coordinators ... we
/// just need one and it gets used 6 times"). The maps are literals copied by hand from
/// tools/own_models/instruments/library.py; the mounts are held against the GLBs they sit in; and no aircraft builder may
/// draw an instrument the library has (the scan at the bottom).</summary>
public class InstrumentLibraryTests
{
    private readonly ITestOutputHelper _o;
    public InstrumentLibraryTests(ITestOutputHelper o) => _o = o;

    private static string Repo => OwnPittsModelTests.Repo;
    private static string Models => OwnPittsModelTests.Models;

    private static InstrumentPart Part(InstrumentDesign d, string node) => d.Parts.Single(p => p.Node == node);

    // ---- the designs -----------------------------------------------------------------------------------------------

    [Fact]
    public void StandardRatePutsTheWingsOnTheTwentyDegreeIndex()
    {
        InstrumentPart wings = Part(InstrumentLibrary.TurnCoordinator, "TC_Airplane");
        Assert.Equal("turn_rate_dps", wings.Param);
        Assert.Equal(20.0, wings.Value(3.0), 9);       // 3 deg/s right: right wing down 20 deg, clockwise
        Assert.Equal(-20.0, wings.Value(-3.0), 9);
        Assert.Equal(40.0, wings.Value(9.0), 9);       // the stop, at 2 rates
        InstrumentPart ball = Part(InstrumentLibrary.TurnCoordinator, "TC_Ball");
        Assert.Equal(-18.0, ball.Value(1.0), 9);       // ball to the RIGHT: anti-clockwise from 6 o'clock
        Assert.Equal(18.0, ball.Value(-2.0), 9);
    }

    [Fact]
    public void TheOneAirspeedLawPutsTheFloorAtThirtyAndTheTopAtThreeThirty()
    {
        InstrumentPart mph = InstrumentLibrary.AsiNeedle("mph", 40, 200);
        Assert.Equal("airspeed_mph", mph.Param);
        Assert.Equal(15.0, mph.Value(20), 9);
        Assert.Equal(30.0, mph.Value(40), 9);
        Assert.Equal(180.0, mph.Value(120), 9);
        Assert.Equal(330.0, mph.Value(250), 9);
        InstrumentPart kt = InstrumentLibrary.ExtraAsi.Needle;
        Assert.Equal("airspeed_kias", kt.Param);
        Assert.Equal(180.0, kt.Value(150), 9);         // 30 + 300 * 110 / 220
        Assert.Equal(330.0, kt.Value(260), 9);
    }

    [Fact]
    public void AltimeterHandsAndTheOtherGyrosReadAsDrawn()
    {
        Assert.Equal(126.0, Part(InstrumentLibrary.Altimeter, "ALT_Hand100").Value(350), 9);
        Assert.Equal(360.0 + 126.0, Part(InstrumentLibrary.Altimeter, "ALT_Hand100").Value(1350), 9);   // round and round
        Assert.Equal(156.6, Part(InstrumentLibrary.Altimeter, "ALT_Hand1000").Value(4350), 9);
        Assert.Equal(15.66, Part(InstrumentLibrary.Altimeter, "ALT_Hand10000").Value(4350), 9);
        InstrumentPart vsi = Part(InstrumentLibrary.VerticalSpeed, "VSI_Needle");
        Assert.Equal(-90.0, vsi.Value(0), 9);          // 9 o'clock
        Assert.Equal(-45.0, vsi.Value(500), 9);
        Assert.Equal(80.0, vsi.Value(2500), 9);
        Assert.Equal(-180.0, vsi.Value(-1000), 9);
        Assert.Equal(-20.0, Part(InstrumentLibrary.Attitude, "AI_Roll").Value(20), 9);   // horizon turns opposite the bank
        InstrumentPart pitch = Part(InstrumentLibrary.Attitude, "AI_Pitch");
        Assert.True(pitch.Slide);
        Assert.Equal(-0.15, pitch.Value(10), 9);      // nose up: horizon DOWN, 0.015 face radii a degree
        Assert.Equal(-0.30, pitch.Value(35), 9);
        Assert.Equal(-90.0, Part(InstrumentLibrary.Heading, "DG_Card").Value(90), 9);
        Assert.Equal(-450.0, Part(InstrumentLibrary.Heading, "DG_Card").Value(450), 9);
    }

    /// <summary>Ty 07:26, "G meter should be standardized too": one law, 0 g at 9 o'clock and 12 deg per g, the followers on
    /// it; every g-meter in the fleet is the library's, with its aeroplane's own red lines.</summary>
    [Fact]
    public void OneGMeterWithItsFollowersEverywhere()
    {
        InstrumentPart main = Part(InstrumentLibrary.GMeter, "GM_Needle");
        Assert.Equal("g_load", main.Param);
        Assert.Equal(-90.0, main.Value(0), 9);
        Assert.Equal(-78.0, main.Value(1), 9);
        Assert.Equal(30.0, main.Value(12), 9);
        Assert.Equal(-210.0, main.Value(-10), 9);
        Assert.Equal("g_max_seen", Part(InstrumentLibrary.GMeter, "GM_Max").Param);
        Assert.Equal("g_min_seen", Part(InstrumentLibrary.GMeter, "GM_Min").Param);
        Assert.Equal(-6.0, Part(InstrumentLibrary.GMeter, "GM_Max").Value(7), 9);   // the follower reads the same law: -90 + 7 x 12
        var g = InstrumentLibrary.ModelsWithMounts
            .SelectMany(m => InstrumentLibrary.MountsFor(m).Where(x => x.Kind == InstrumentKind.GMeter).Select(x => m + ":" + x.Slot)).ToArray();
        Assert.Equal(new[] { "extra-300-like-own:G_R", "extra-300-like-own:G_F", "great-lakes-like-own:G_R", "pitts-s2b-like-own:G_R", "pitts-s2b-like-own:G_F" }, g);
        foreach (string a in new[] { "extra-300-like", "great-lakes-like", "pitts-s2b-like" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "configs", "aircraft", a + ".json")));
            Assert.True(doc.RootElement.GetProperty("instruments").TryGetProperty("gMeter", out _), a);
            Assert.True(File.Exists(Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", InstrumentLibrary.GMeterFace(a) + ".png")), a);
        }
    }

    /// <summary>The 172's gyro instruments have models of their own: the library TC is driven in RATES there.</summary>
    [Fact]
    public void TheC172DrivesTheLibraryGyrosFromItsOwnInstrumentModels()
    {
        var m = InstrumentLibrary.C172().ToDictionary(x => x.Slot);
        InstrumentPart tc = m["TC"].PartFor(Part(InstrumentLibrary.TurnCoordinator, "TC_Airplane"));
        Assert.Equal("tc_needle", tc.Param);
        Assert.Equal(20.0, tc.Value(1.0), 9);
        Assert.Equal("ai_bank_deg", m["AI"].PartFor(Part(InstrumentLibrary.Attitude, "AI_Roll")).Param);
        Assert.Equal("ai_pitch_deg", m["AI"].PartFor(Part(InstrumentLibrary.Attitude, "AI_Pitch")).Param);
        Assert.Equal("dg_card_deg", m["DG"].PartFor(Part(InstrumentLibrary.Heading, "DG_Card")).Param);
        // and the ball is the library's own
        Assert.Equal("slip_ball", m["TC"].PartFor(Part(InstrumentLibrary.TurnCoordinator, "TC_Ball")).Param);
    }

    // ---- ONE turn coordinator, used everywhere there is one --------------------------------------------------------

    /// <summary>Ty's six: the 172, the Extra (three liveries, one table), the Great Lakes front and rear. Every one is the
    /// library's; the J-3 and the gliders have none.</summary>
    [Fact]
    public void EveryTurnCoordinatorInTheFleetIsTheLibraryOne()
    {
        var tcs = InstrumentLibrary.ModelsWithMounts
            .SelectMany(m => InstrumentLibrary.MountsFor(m).Where(x => x.Kind == InstrumentKind.TurnCoordinator).Select(x => m + ":" + x.Slot))
            .ToArray();
        Assert.Equal(new[] { "c172-like-own:TC", "extra-300-like-own:TC_R", "great-lakes-like-own:TC_F", "great-lakes-like-own:TC_R" }, tcs);
        int glbs = 0;
        foreach (string m in InstrumentLibrary.ModelsWithMounts)
            foreach (string glb in Liveries(m))
            {
                var names = Glb.Nodes(glb).Select(n => n.Name).ToHashSet();
                if (InstrumentLibrary.MountsFor(m).Any(x => x.Kind == InstrumentKind.TurnCoordinator)) glbs++;
                _o.WriteLine($"{Path.GetFileName(glb)}: {names.Count} nodes");
            }
        Assert.True(glbs >= 5, $"{glbs} GLBs with a turn coordinator");   // 172, three Extras, Great Lakes (two panels)
    }

    // ---- the GLBs ----------------------------------------------------------------------------------------------------

    /// <summary>Every library GLB has its root, face, bezel and parts, and every moving part sits ON the dial centre
    /// (no translation, no rotation): the runtime turns a node about its own origin (CLAUDE.md, the Pitts fuel valve).</summary>
    [Fact]
    public void EveryLibraryGlbHasItsPartsOnTheDialCentre()
    {
        foreach (InstrumentDesign d in InstrumentLibrary.Designs)
        {
            string path = Path.Combine(Models, "instruments", d.Resource + ".glb");
            Assert.True(File.Exists(path), path);
            var nodes = Glb.Nodes(path).ToDictionary(n => n.Name);
            Assert.True(nodes.ContainsKey(d.Root), d.Root);
            Assert.True(nodes.ContainsKey(d.FaceNode), d.FaceNode);
            Assert.True(nodes.ContainsKey(d.BezelNode), d.BezelNode);
            foreach (InstrumentPart p in d.Parts)
            {
                Assert.True(nodes.TryGetValue(p.Node, out var n), $"{d.Resource}: {p.Node}");
                Assert.Equal(0.0, n!.Translation.Select(Math.Abs).Max(), 9);
                Assert.Equal(1.0, n.Rotation[3], 9);
            }
            _o.WriteLine($"{d.Resource}: {nodes.Count} nodes, parts {string.Join(", ", d.Parts.Select(p => p.Node))}");
        }
    }

    /// <summary>Each aeroplane's ASI scale is the one its config declares (configs/aircraft/&lt;id&gt;.json
    /// instruments.airspeed, which tools/own_models/instruments/build.py draws its face from): two places, kept together.</summary>
    [Fact]
    public void AirspeedScalesAreTheConfigs()
    {
        foreach (InstrumentLibrary.AsiScale s in InstrumentLibrary.AsiScales)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, "configs", "aircraft", s.Aircraft + ".json")));
            JsonElement a = doc.RootElement.GetProperty("instruments").GetProperty("airspeed");
            Assert.Equal(s.Units, a.GetProperty("units").GetString());
            Assert.Equal(s.Floor, a.GetProperty("scaleMin").GetDouble());
            Assert.Equal(s.Top, a.GetProperty("scaleMax").GetDouble());
            Assert.True(File.Exists(Path.Combine(Repo, "unity", "Assets", "_Project", "Resources", s.FaceArt + ".png")), s.FaceArt);
        }
        // the literals, by hand
        Assert.Equal(("mph", 40.0, 200.0), (InstrumentLibrary.C172Asi.Units, InstrumentLibrary.C172Asi.Floor, InstrumentLibrary.C172Asi.Top));
        Assert.Equal(("kt", 40.0, 260.0), (InstrumentLibrary.ExtraAsi.Units, InstrumentLibrary.ExtraAsi.Floor, InstrumentLibrary.ExtraAsi.Top));
        Assert.Equal(("mph", 40.0, 200.0), (InstrumentLibrary.GreatLakesAsi.Units, InstrumentLibrary.GreatLakesAsi.Floor, InstrumentLibrary.GreatLakesAsi.Top));
        Assert.Equal(("mph", 40.0, 240.0), (InstrumentLibrary.PittsAsi.Units, InstrumentLibrary.PittsAsi.Floor, InstrumentLibrary.PittsAsi.Top));
    }

    // ---- the mounts against the models ------------------------------------------------------------------------------

    /// <summary>The GLBs the game can place for a model (LiveryChoice.OwnSuffix, by hand): every livery shares the
    /// instrument nodes. The Great Lakes' red-sunburst / white-stripes GLBs are not reachable and are not checked.</summary>
    private static IEnumerable<string> Liveries(string ownModel)
    {
        string model = ownModel.Substring(0, ownModel.Length - "-own".Length);
        string[] suffixes = model switch
        {
            "extra-300-like" => new[] { "-own", "-own-n300uy", "-own-unmarked" },
            "pitts-s2b-like" => new[] { "-own", "-own-n1191", "-own-factory-unmarked", "-own-checker-unmarked" },
            _ => new[] { "-own" },
        };
        foreach (string s in suffixes) Assert.Equal(s, FlyingGame.Core.LiveryChoice.OwnSuffix(model, s switch
        {
            "-own-n300uy" => "n300uy", "-own-unmarked" => "unmarked", "-own-n1191" => "n1191",
            "-own-factory-unmarked" => "factory-unmarked", "-own-checker-unmarked" => "checker-unmarked",
            _ => model == "extra-300-like" ? "sunrise" : model == "pitts-s2b-like" ? "n31dg" : "",
        }));
        return suffixes.Select(s => Path.Combine(Models, model, model + s + ".glb"));
    }

    /// <summary>Every node a mount switches off or lifts is in EVERY livery of its model, slots are unique, and where the
    /// first node is the old needle on its pivot the table's centre is that pivot to a millimetre (the numbers were read
    /// off the GLB; this is what tells a rebuilt model from the table).</summary>
    [Fact]
    public void EveryMountMatchesTheModelItSitsIn()
    {
        foreach (string m in InstrumentLibrary.ModelsWithMounts)
        {
            var mounts = InstrumentLibrary.MountsFor(m);
            Assert.Equal(mounts.Count, mounts.Select(x => x.Slot).Distinct().Count());
            foreach (string glb in Liveries(m))
            {
                var nodes = Glb.Nodes(glb).ToDictionary(n => n.Name);
                foreach (InstrumentMount x in mounts)
                {
                    Assert.InRange(x.RadiusM, 0.02, 0.045);
                    foreach (string h in x.Hide.Concat(x.Lift)) Assert.True(nodes.ContainsKey(h), $"{Path.GetFileName(glb)} {x.Slot}: {h}");
                    string first = x.Hide[0];
                    if (first.Contains("Face") || first.Contains("Bezel")) continue;
                    // In the face's plane (the library face may sit on another depth than the old part's pivot: the 172's
                    // AI and DG pivots are 1-6 mm behind its common dial plane).
                    double[] p = nodes[first].World;
                    double dx = p[0] - x.Cx, dy = p[1] - x.Cy, dz = p[2] - x.Cz, along = dx * x.Nx + dy * x.Ny + dz * x.Nz;
                    double d = Math.Sqrt(Math.Max(0, dx * dx + dy * dy + dz * dz - along * along));
                    _o.WriteLine($"{Path.GetFileName(glb)} {x.Slot}: centre {d * 1000:F2} mm across, {along * 1000:F1} mm along the normal from {first}");
                    Assert.True(d < 0.0012, $"{Path.GetFileName(glb)} {x.Slot}: {d * 1000:F2} mm from {first}");
                    Assert.True(Math.Abs(along) < 0.007, $"{Path.GetFileName(glb)} {x.Slot}: {along * 1000:F1} mm off the face plane");
                }
            }
        }
    }

    [Fact]
    public void PeriodAndNotYetMovedAircraftAreNamed()
    {
        Assert.Empty(InstrumentLibrary.MountsFor("j3-cub-like-own"));            // period-correct: keeps its own
        Assert.Empty(InstrumentLibrary.MountsFor("glider-2-33-like-own"));
        foreach (string f in InstrumentLibrary.PeriodVariants.Keys)
            Assert.True(File.Exists(Path.Combine(Repo, "tools", "own_models", f)), f);
    }

    // ---- nobody builds their own -------------------------------------------------------------------------------------

    /// <summary>The names only a builder DRAWING that instrument writes: its needle / symbol nodes and its face art.</summary>
    internal static readonly (InstrumentKind Kind, string Pattern)[] Signatures =
    {
        (InstrumentKind.TurnCoordinator, @"TC_Symbol|TC_Ball|Ball_TC|Needle_TC|TC_airplane|TC_slip_ball|TC_rate_mark|dial-tc|dial_tc|TURN COORD|TURN 2 MIN"),
        (InstrumentKind.Airspeed, @"Needle_ASI|Airspeed_needle|Needle_Airspeed|ASI_speed_arc|dial-asi|dial_asi|'AIRSPEED'|'KNOTS'"),
        (InstrumentKind.Altimeter, @"Needle_Alt|Altimeter_needle|Altimeter_1000|dial-alt\b|dial_alt\b|'ALT FT'|'100 FEET'"),
        (InstrumentKind.VerticalSpeed, @"Needle_VSI|VSI_needle|dial-vsi|dial_vsi|'VERTICAL SPEED'"),
        (InstrumentKind.Attitude, @"AI_roll|AI_pitch|AI_horizon|live-attitude|dial-ai\b|'ATTITUDE'"),
        (InstrumentKind.Heading, @"DG_card|DG_heading_bug|dial-dg\b"),
        (InstrumentKind.GMeter, @"Needle_G_|Needle_GMax|Needle_GMin|dial-g\b|dial_g\b|'G UNITS'|'ACCEL'"),
    };

    /// <summary>Files under tools/own_models that READ old models (checkers, renderers, the post-passes that retire old
    /// nodes) rather than build cockpits. Each says why.</summary>
    internal static readonly Dictionary<string, string> Readers = new()
    {
        ["check_c172_cockpit.py"] = "checker: asserts the old six-pack is ABSENT from a library-built 172",
        ["check_great_lakes_rig.py"] = "checker",
        ["check_instrument_style.py"] = "checker",
        ["check_iac_assets.py"] = "checker",
        ["check_vr_phase3.py"] = "checker",
        ["check_eye_interiors.py"] = "checker",
        ["glb_visuals.py"] = "the GLB editor (retires named nodes)",
        ["instrument_face_style.py"] = "paint post-pass over every face",
        ["style_fleet_instruments.py"] = "paint post-pass over every face",
        ["gl_panel_v2.py"] = "post-pass: retires the old Great Lakes ASI/ALT/TC needles (RETIRED) and moves the panel",
    };

    /// <summary>No aircraft cockpit script draws its own copy of a library instrument. A builder that needs one asks the
    /// library (tools/own_models/instruments) and the mount table; a period-correct face is an exception that has to be
    /// named in <see cref="InstrumentLibrary.PeriodVariants"/>.</summary>
    [Fact]
    public void NoAircraftBuilderDrawsItsOwnLibraryInstrument()
    {
        string dir = Path.Combine(Repo, "tools", "own_models");
        var failures = new List<string>();
        foreach (string path in Directory.GetFiles(dir, "*.py").OrderBy(x => x))
        {
            string f = Path.GetFileName(path);
            if (InstrumentLibrary.PeriodVariants.ContainsKey(f) || Readers.ContainsKey(f)) continue;
            if (f.StartsWith("render_") || f.StartsWith("compose_") || f.Contains("_render") || f.Contains("fourview") || f.Contains("_shots")) continue;
            string[] lines = File.ReadAllLines(path);
            foreach (var (kind, pattern) in Signatures)
                for (int i = 0; i < lines.Length; i++)
                    if (Regex.IsMatch(lines[i], pattern)) failures.Add($"{f}:{i + 1} draws a {kind}: {lines[i].Trim().Substring(0, Math.Min(110, lines[i].Trim().Length))}");
        }
        foreach (string s in failures) _o.WriteLine(s);
        Assert.True(failures.Count == 0, $"{failures.Count} lines in aircraft builders draw a library instrument; first: {failures.FirstOrDefault()}");
    }

    [Fact]
    public void TheScanCatchesAPrivateTurnCoordinator()
    {
        // what lane/codex-turn-coord f2e544ee had to edit in four builders
        string line = "            n = 'TC_Symbol_' + sfx; s_ = rf / 0.492 / 512";
        Assert.Contains(Signatures, s => s.Kind == InstrumentKind.TurnCoordinator && Regex.IsMatch(line, s.Pattern));
        Assert.Contains(Signatures, s => s.Kind == InstrumentKind.Airspeed && Regex.IsMatch("put('Needle_ASI_R', ...)", s.Pattern));
    }

    // ---- a small GLB reader --------------------------------------------------------------------------------------------

    internal static class Glb
    {
        internal sealed class Node
        {
            public string Name = ""; public double[] Translation = { 0, 0, 0 }; public double[] Rotation = { 0, 0, 0, 1 };
            public double[] Scale = { 1, 1, 1 }; public int[] Children = Array.Empty<int>(); public double[] World = { 0, 0, 0 };
            public double[]? Matrix;   // column-major, when the node has one instead of TRS
        }

        public static List<Node> Nodes(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            int len = BitConverter.ToInt32(raw, 12);
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(raw, 20, len));
            var list = new List<Node>();
            foreach (JsonElement n in doc.RootElement.GetProperty("nodes").EnumerateArray())
            {
                var x = new Node { Name = n.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "" };
                if (n.TryGetProperty("translation", out var t)) x.Translation = t.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                if (n.TryGetProperty("rotation", out var r)) x.Rotation = r.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                if (n.TryGetProperty("scale", out var s)) x.Scale = s.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                if (n.TryGetProperty("children", out var c)) x.Children = c.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                if (n.TryGetProperty("matrix", out var mx)) x.Matrix = mx.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                list.Add(x);
            }
            var parent = new int[list.Count]; Array.Fill(parent, -1);
            for (int i = 0; i < list.Count; i++) foreach (int c in list[i].Children) parent[c] = i;
            for (int i = 0; i < list.Count; i++)
            {
                double[] p = { 0, 0, 0 };
                for (int k = i; k >= 0; k = parent[k]) p = Apply(list[k], p);
                list[i].World = p;
            }
            return list;
        }

        private static double[] Apply(Node n, double[] p)
        {
            if (n.Matrix is double[] m)
                return new[] { m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12], m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13], m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14] };
            double sx = p[0] * n.Scale[0], sy = p[1] * n.Scale[1], sz = p[2] * n.Scale[2];
            double qx = n.Rotation[0], qy = n.Rotation[1], qz = n.Rotation[2], qw = n.Rotation[3];
            // v' = v + 2 q.xyz x (q.xyz x v + w v)
            double cx = qy * sz - qz * sy + qw * sx, cy = qz * sx - qx * sz + qw * sy, cz = qx * sy - qy * sx + qw * sz;
            double rx = sx + 2 * (qy * cz - qz * cy), ry = sy + 2 * (qz * cx - qx * cz), rz = sz + 2 * (qx * cy - qy * cx);
            return new[] { rx + n.Translation[0], ry + n.Translation[1], rz + n.Translation[2] };
        }
    }
}
