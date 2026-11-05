using System.Text.Json;
using FlyingGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-04: "the instruments are dark and illegible most of the time … make them full time legible". Which
/// materials get the instrument lighting (InstrumentFaces → Bridge InstrumentLighting → FlyingGame/Instrument).
/// The names are literals copied by hand from the GLBs' material lists (2026-11-04), not read back from the files.
/// </summary>
public class InstrumentFacesTests
{
    private readonly ITestOutputHelper _o;
    public InstrumentFacesTests(ITestOutputHelper o) { _o = o; }

    [Theory]
    // Extra (P3b one-plane panel and the original panel), Great Lakes
    [InlineData("P3b_Dial_ASI")] [InlineData("P3b_Dial_Temps")] [InlineData("P3b_Needle")] [InlineData("P3b_SecondNeedle")]
    [InlineData("P3b_Telltale")] [InlineData("Dial_ASI")] [InlineData("Dial_HOBBS")] [InlineData("Cockpit_needle")]
    [InlineData("Cockpit_telltale")] [InlineData("Cockpit_lubber")] [InlineData("Cockpit_tc_ball")] [InlineData("Compass_Card")]
    [InlineData("Compass_Card_Front")] [InlineData("Compass_Face_Front")] [InlineData("Cockpit_gauge_face")]
    [InlineData("172_markings")] [InlineData("172_instrument_black")] [InlineData("172_normal_arc")]
    [InlineData("FrontPanel_Airspeed_Face")] [InlineData("FrontPanel_Tachometer_Face")]
    // Pitts painted faces
    [InlineData("RearPanel_Airspeed_Face")] [InlineData("RearPanel_G_meter_Face")]
    // 2-33, J-3
    [InlineData("Int_Needle")] [InlineData("Dial_VARIO")] [InlineData("Gauge_face")] [InlineData("Panel_gauge_faces")]
    [InlineData("Needle_red")] [InlineData("Needle_orange")] [InlineData("Placard_text_white")]
    // Unity's runtime spellings
    [InlineData("P3b_Dial_G (Instance)")] [InlineData("Dial_ASI.001")]
    public void Instrument_materials_are_lit(string name) => Assert.True(InstrumentFaces.IsInstrument(name), name);

    [Theory]
    [InlineData("P3b_Bezel")] [InlineData("Cockpit_bezel")] [InlineData("Cockpit_gauge_bezel")] [InlineData("RearPanel_Compass_Bezel")]
    [InlineData("RearPanel_Airspeed_Bezel")] [InlineData("Gauge_bezel_black")] [InlineData("P3b_Panel")] [InlineData("Cockpit_panel")]
    [InlineData("Avionics_Face_COM")] [InlineData("Cockpit_avionics_display")] [InlineData("Cockpit_knob_red")]
    [InlineData("SequenceCard_Face")] [InlineData("Glass")] [InlineData("Paint_ea_top")] [InlineData("RearPanel_UpperExtension")]
    [InlineData("")] [InlineData(null)]
    public void Metal_paint_and_lcds_are_not(string? name) => Assert.False(InstrumentFaces.IsInstrument(name), name ?? "null");

    [Fact]
    public void The_floor_and_glow_are_modest()
    {
        // A white marking (albedo ~0.9) in a dark cockpit reads at albedo x (floor + glow): readable, not a lamp.
        float dark = 0.9f * (InstrumentFaces.LightFloor + InstrumentFaces.MarkingGlow);
        Assert.InRange(dark, 0.45, 0.65);
        // A black face (albedo ~0.06) stays black in the dark: contrast above 8:1.
        Assert.True(dark / (0.06f * (InstrumentFaces.LightFloor + InstrumentFaces.MarkingGlow)) > 8);
    }

    /// <summary>Every own cockpit GLB the game loads has its gauge faces caught by the rule (one instrument material
    /// at least, and every material whose name says dial / needle / gauge face, bezel aside).</summary>
    [Theory]
    [InlineData("extra-300-like", "extra-300-like-own")]
    [InlineData("extra-300-like", "extra-300-like-own-n300uy")]
    [InlineData("extra-300-like", "extra-300-like-own-unmarked")]
    [InlineData("great-lakes-like", "great-lakes-like-own")]
    [InlineData("pitts-s2b-like", "pitts-s2b-like-own")]
    [InlineData("glider-2-33-like", "glider-2-33-like-own")]
    [InlineData("j3-cub-like", "j3-cub-like-own")]
    public void Every_own_cockpit_has_its_gauges_caught(string model, string file)
    {
        var (j, _) = OwnPittsModelTests.ReadGlb(Path.Combine(OwnPittsModelTests.Models, model, file + ".glb"));
        var names = j.GetProperty("materials").EnumerateArray().Select(m => m.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "").ToList();
        var lit = names.Where(InstrumentFaces.IsInstrument).ToList();
        _o.WriteLine($"{file}: {lit.Count} of {names.Count} materials lit: {string.Join(", ", lit)}");
        Assert.NotEmpty(lit);
        var missed = names.Where(n => !InstrumentFaces.IsInstrument(n) && !n.Contains("ezel", StringComparison.Ordinal)
                                      && (n.Contains("Dial", StringComparison.Ordinal) || n.Contains("Needle", StringComparison.OrdinalIgnoreCase)
                                          || n.Contains("gauge_face", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.Empty(missed);
    }
}
