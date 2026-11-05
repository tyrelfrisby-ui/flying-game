using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Vr;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// NO SEAT BELTS AND NO HEADSET CORDS (Ty 2026-11-05: "Remove seat belts and headset wires completely (may revisit).
/// Bailout is just a button press and it happens."). Core Vr/BeltsAndCords.cs decides what a belt is; the game strips those
/// nodes and materials from every placed model (Bridge CockpitBeltStrip), so GLBs built before today are clean too.
/// </summary>
public class BeltsAndCordsTests
{
    // The belt nodes the shipped GLBs carried on 2026-11-05, copied by hand from a scan of Resources/Models.
    [Theory]
    [InlineData("Harness_Rear")]
    [InlineData("Harness_Front")]
    [InlineData("Harness_Buckle_Rear")]
    [InlineData("Harness_Buckle_Secondary_Front")]
    [InlineData("Harness_Release_Primary_Rear")]
    [InlineData("Grip_Harness_Release_Front")]
    [InlineData("Harness_Strap_Lap_Primary_L_Rear")]
    [InlineData("Harness_Strap_Shoulder_R_Front")]
    [InlineData("Harness_Strap_Crotch_Rear")]
    [InlineData("Harness_Front_Buckle")]          // 2-33
    [InlineData("Shoulder_Harness_F_1")]          // Grob 103
    [InlineData("Lap_Harness_R_-1")]
    [InlineData("Buckle_F")]
    [InlineData("Buckle_R")]
    [InlineData("Model:Buckle_F")]               // as placed by AirframeBuilder
    [InlineData("Model:Harness_Strap_Crotch_Rear")]
    [InlineData("Lap_belt_L")]                    // C172
    [InlineData("Belt_buckle_R")]
    [InlineData("Rear_lap_belt")]
    [InlineData("Headset_Plug")]                  // pilot gear: the cord's plug
    [InlineData("Grip_Headset_Plug")]
    [InlineData("Headset_Cable_Root")]
    public void BeltAndCordNodesAreStripped(string node) => Assert.True(BeltsAndCords.IsBeltOrCordNode(node), node);

    [Theory]
    [InlineData("Headset_Jack_Rear")]             // the socket in the sidewall stays
    [InlineData("Headset_Jack_Front_Mic")]
    [InlineData("Parachute_Harness_L")]           // the parachute's own harness is the bail-out
    [InlineData("Canopy_Jettison_Cable_Front")]
    [InlineData("Pitts_Release_Cable_Left")]
    [InlineData("TowCable_F")]
    [InlineData("GL_TrimCord_Run")]
    [InlineData("WaterRudder_Cable")]
    [InlineData("Seat_Pan_F")]
    [InlineData("Stick_Rear")]
    [InlineData("SequenceCard_Face")]
    [InlineData("")]
    public void EverythingElseStays(string node) => Assert.False(BeltsAndCords.IsBeltOrCordNode(node), node);

    [Theory]
    [InlineData("Cockpit_harness", true)]         // Great Lakes / Pitts, inside the one cockpit mesh
    [InlineData("Cockpit_lap_belt", true)]
    [InlineData("Int_Webbing", true)]             // 2-33
    [InlineData("Harness_Webbing", true)]
    [InlineData("Harness_Release", true)]
    [InlineData("belt", true)]                    // Grob
    [InlineData("Cockpit_harness (Instance)", true)]
    [InlineData("Cockpit_harness.001", true)]
    [InlineData("Harness_Steel", false)]          // the jacks are drawn in it too
    [InlineData("Cockpit_seat", false)]
    [InlineData("Parachute_Webbing", false)]
    [InlineData("Cockpit_tube", false)]
    public void BeltMaterials(string mat, bool belt) => Assert.Equal(belt, BeltsAndCords.IsBeltMaterial(mat));

    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("extra-300-like")]
    [InlineData("great-lakes-like")]
    [InlineData("glider-2-33-like")]
    [InlineData("j3-cub-like")]
    public void NoTypeStrapsThePilotIn(string id)
    {
        Assert.False(BeltsAndCords.BeltsWorn);
        SeatHarness h = SeatHarness.ForCockpit(id);
        Assert.Empty(h.Releases);
        Assert.False(h.Latched);
        Assert.Equal(1.0, h.Harness01, 9);       // the rig's harness_latch: released
    }

    [Fact]
    public void WithNoBeltTheClimbOutIsNotHeldBack()
    {
        // Strapped in, the seat will rise only StrappedRiseM; free, the push takes him past it.
        var climb = new ClimbOut();
        climb.Grab(new Vec3(0, 0, 0), ExitSide.Left);
        bool latched = SeatHarness.ForCockpit("pitts-s2b-like").Latched;
        for (int i = 1; i <= 40; i++) climb.Step(new Vec3(0, -0.01 * i, 0), latched, true, 0.0);
        Assert.True(climb.RiseM > ClimbOut.StrappedRiseM + 0.05, $"rose {climb.RiseM:F3} m");
    }

    /// <summary>Every node in every shipped airframe GLB that LOOKS like a belt is one the rules strip: a new belt name in a
    /// rebuilt model must not slip past the runtime strip.</summary>
    [Fact]
    public void EveryBeltLikeNodeInTheShippedModelsIsCaught()
    {
        var look = new Regex("harness|belt|buckle|webbing|strap|headset_(plug|cable|cord)", RegexOptions.IgnoreCase);
        var missed = new List<string>();
        int glbs = 0;
        foreach (string path in Directory.GetFiles(OwnPittsModelTests.Models, "*.glb", SearchOption.AllDirectories))
        {
            string dir = Path.GetFileName(Path.GetDirectoryName(path)!);
            if (dir.StartsWith("pilot-")) continue;    // the jumper's body and gear, never in an airframe
            glbs++;
            var (json, _) = OwnPittsModelTests.ReadGlb(path);
            if (!json.TryGetProperty("nodes", out var nodes)) continue;
            foreach (var n in nodes.EnumerateArray())
            {
                string name = n.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                if (look.IsMatch(name) && !name.Contains("Parachute") && !BeltsAndCords.IsBeltOrCordNode(name)) missed.Add(Path.GetFileName(path) + ":" + name);
            }
        }
        Assert.True(glbs > 10, $"only {glbs} GLBs found");
        Assert.True(missed.Count == 0, "belt-like nodes the strip would miss: " + string.Join(", ", missed.Distinct().Take(20)));
    }
}
