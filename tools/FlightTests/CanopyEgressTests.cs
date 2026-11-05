using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 06:37: on any bail-out the aeroplane's REAL canopy or door leaves; open cockpits (the Cub, the Great Lakes)
/// lose nothing; there is no stand-in. Node names are copied by hand from each GLB's node list (2026-11-05), with a few of
/// the model's other nodes beside them so the rule has to pick.
/// </summary>
public class CanopyEgressTests
{
    private static IReadOnlyList<string> Parts(params string[] nodes) => CanopyEgress.PartsToJettison(nodes);

    [Fact]
    public void PittsAndExtraLoseTheirDetachableCanopy()
    {
        string[] pitts = { "Model:Fuselage", "Canopy", "Canopy_Bow_F", "Canopy_Detachable", "Canopy_Frame", "Canopy_Glass", "Canopy_Hinge_Airframe_Front" };
        Assert.Equal(new[] { "Canopy_Detachable" }, Parts(pitts));
        string[] extra = { "Canopy", "CanopyLock_F", "CanopyRelease_R", "Canopy_COM", "Canopy_Detachable", "Canopy_Frame", "Canopy_Glass", "Canopy_HingeAxis" };
        Assert.Equal(new[] { "Canopy_Detachable" }, Parts(extra));
    }

    [Fact]
    public void OnePieceGliderCanopiesLeaveWhole()
    {
        Assert.Equal(new[] { "Canopy" }, Parts("Fuselage", "Canopy", "Wing_L"));           // Swift S-1, EB-29R
        Assert.Equal(new[] { "Canopy" }, Parts("Model:Canopy", "Model:Fuselage"));          // glTFast's prefix
    }

    [Fact]
    public void ACanopyDrawnInPartsLeavesWithAllItsParts()
    {
        // 2-33 (yellow, the only one): frame, glass and hardware are siblings at the root.
        Assert.Equal(new[] { "Canopy_Frame", "Canopy_Glass", "Canopy_Hardware" }, Parts("Fuselage", "Canopy_Frame", "Canopy_Glass", "Canopy_Hardware", "Seat_Front"));
        // Grob 103: the red jettison knobs stay in the cockpit.
        Assert.Equal(new[] { "Canopy_Frame", "Canopy_Glass" }, Parts("Canopy_Frame", "Canopy_Glass", "Canopy_Jettison_F", "Canopy_Jettison_R"));
    }

    [Fact]
    public void TheCessnaLosesTheDoorHeGoesOutOf()
    {
        string[] c172 = { "C172_Door_L", "C172_Door_latch_L", "C172_Door_latch_R", "C172_Door_R", "Glass.Door.L", "Glass.Door.R", "Glass.Windscreen" };
        Assert.Equal(new[] { "C172_Door_R", "C172_Door_latch_R", "Glass.Door.R" }, Parts(c172));
        Assert.Equal(new[] { "C172_Door_L", "C172_Door_latch_L", "Glass.Door.L" }, CanopyEgress.PartsToJettison(c172, exitRight: false));
    }

    [Fact]
    public void OpenCockpitsAndFoldOutDoorsLoseNothing()
    {
        Assert.Empty(Parts("GL_Windshield_Base_Trim_Front", "Windscreen_Front", "Windscreen_Glass", "Windscreen_Rear"));   // Great Lakes
        Assert.Empty(Parts("DoorLower_R", "DoorUpper_R", "Window_R", "Fuselage"));                                       // J-3 Cub, landplane and floats
        Assert.Empty(Parts("Windscreen", "Fuselage"));                                                                   // Seminole: no door drawn apart
        Assert.Empty(Parts("Fuselage", "Wing", "Propeller"));                                                            // a downloaded model: nothing separate
        Assert.Empty(Parts());
    }

    [Fact]
    public void TheProceduralDomeLeavesOnlyWhereTheTypeHasACanopyThatComesOff()
    {
        foreach (string id in new[] { "cassutt-f1-like", "glasair3-like", "glider-swift-s1-like", "pitts-s2b-like", "extra-300-like", "glider-2-33-like" })
            Assert.True(CanopyEgress.ShellCanopyLeaves(id), id);
        // Open cockpits, wings with no cockpit, and cabins whose dome is their windows.
        foreach (string id in new[] { "aircam-like", "aircam-amphib-like", "hang-glider-like", "paraglider-en-b-like", "j3-cub-like", "great-lakes-like",
                                      "c172-like", "decathlon-8kcab-like", "dhc2-beaver-like", "dhc2-beaver-floats-like", "pa25-pawnee-like", "jn4-jenny-like", "" })
            Assert.False(CanopyEgress.ShellCanopyLeaves(id), id);
    }
}
