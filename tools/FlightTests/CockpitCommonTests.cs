using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Instruments;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>Ty's 2026-11-05 cockpit spec, the parts every aerobatic aeroplane shares: the IAC card deck, the altimeter's
/// Kollsman setting, the g-meter's followers, and (ADDENDUM) the J-3's fuel wire.</summary>
public class IacCardDeckTests
{
    [Fact]
    public void TheTapCyclesTysOrderThenWraps()
    {
        var d = new IacCardDeck();
        d.Select("iac-2026-primary-left");
        var seen = new List<string> { d.CurrentId };
        for (int i = 0; i < 10; i++) seen.Add(d.Next().Id);
        Assert.Equal(new[]
        {
            "iac-2026-primary-left", "iac-2026-primary-right", "iac-2026-sportsman-left", "iac-2026-sportsman-right",
            "iac-2026-intermediate-left", "iac-2026-intermediate-right", "iac-2026-advanced-left", "iac-2026-advanced-right",
            "iac-2026-unlimited-left", "iac-2026-unlimited-right", "iac-2026-primary-left",
        }, seen);
    }

    [Fact]
    public void AFreshDeckShowsTheSportsmanKnown() => Assert.Equal("iac-2026-sportsman-left", new IacCardDeck().CurrentId);

    [Fact]
    public void UploadedCardsJoinTheCycleAndCanBeRemoved()
    {
        var d = new IacCardDeck();
        string id = d.NewUserId("Freestyle 2026!");
        Assert.Equal("user-freestyle-2026", id);
        Assert.True(d.AddUser(id, "Freestyle 2026"));
        Assert.Equal(id, d.CurrentId);                         // the upload goes straight onto the holder
        Assert.Equal("user-freestyle-2026-2", d.NewUserId("Freestyle 2026"));
        Assert.Equal("iac-2026-primary-left", d.Next().Id);     // after his cards, back to the start
        d.Previous();
        Assert.Equal(id, d.CurrentId);
        Assert.True(d.RemoveUser(id));
        Assert.Equal("iac-2026-primary-left", d.CurrentId);     // the card after it
        Assert.False(d.RemoveUser("iac-2026-primary-left"));    // the IAC's own cannot be removed
        Assert.Equal(10, d.All.Count);
    }

    [Fact]
    public void TheSelectionSurvivesASaveByIdNotByIndex()
    {
        var d = new IacCardDeck();
        d.AddUser("user-airshow", "Airshow");
        d.AddUser("user-free", "Free");
        d.Select("user-free");
        string saved = d.Save();
        var e = IacCardDeck.Load(saved);
        Assert.Equal("user-free", e.CurrentId);
        Assert.Equal(new[] { "user-airshow", "user-free" }, e.UserCards.Select(c => c.Id));
        // A card that is gone falls back to the default, never to "whatever is at that index now".
        var f = IacCardDeck.Load("current\tuser-gone\nuser\tuser-free\tFree\n");
        Assert.Equal(IacCardDeck.DefaultId, f.CurrentId);
        Assert.Equal(IacCardDeck.DefaultId, IacCardDeck.Load("garbage\n\t\t\n").CurrentId);
    }

    [Fact]
    public void ALetterPageFillsTheHalfSizeCardAndOtherShapesAreFittedWhole()
    {
        Assert.Equal((0, 0, 1275, 1650, false), LetterCard.Fit(612, 792));        // 8.5 x 11 in, in points
        var a4 = LetterCard.Fit(595, 842);                                          // A4: taller, fitted by height
        Assert.Equal(1650, a4.H); Assert.True(a4.W < 1275 && a4.X > 0);
        var land = LetterCard.Fit(792, 612);                                        // landscape letter: turned
        Assert.True(land.Rotated); Assert.Equal((0, 0, 1275, 1650), (land.X, land.Y, land.W, land.H));
    }

    [Fact]
    public void EveryBuiltInCardHasItsTextureAtLetterSize()
    {
        string dir = Path.Combine(OwnPittsModelTests.Repo, "unity", "Assets", "_Project", "Resources", "SequenceCards");
        foreach (IacCard c in IacCardDeck.BuiltIn)
        {
            string p = Path.Combine(dir, c.Id + ".png");
            Assert.True(File.Exists(p), p);
            byte[] b = File.ReadAllBytes(p);
            int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            Assert.Equal((1275, 1650), (w, h));
        }
    }
}

public class KollsmanAltimeterTests
{
    private const double Pa6000 = 81191.0;   // ISA at 6,000 ft: 101325·(1 − 6000/145366.45)^5.2559, worked by hand

    [Fact]
    public void TheStandardSettingAtStandardSeaLevelReadsZero() =>
        Assert.Equal(0.0, KollsmanAltimeter.IndicatedFt(101325.0, 29.9213), 0);

    [Fact]
    public void SetToQnhItReadsFieldElevationAndZeroedItReadsZero()
    {
        var s = new AltimeterSetting();
        s.ForType("pitts-s2b-like");
        Assert.True(s.SetToRead(Pa6000, 6000.0));
        Assert.Equal(29.92, s.InHg, 2);
        Assert.InRange(s.IndicatedFt(Pa6000), 5990.0, 6010.0);   // within 10 ft (the window's 0.01 inHg is ~9 ft)
        Assert.True(s.SetToRead(Pa6000, 0.0));                   // zero the field: the aerobatic window reaches it
        Assert.Equal(23.98, s.InHg, 2);
        Assert.InRange(s.IndicatedFt(Pa6000), -10.0, 10.0);
        // Climbing 1,000 ft above that field it reads 1,048, not 1,000: the instrument is calibrated in the standard
        // atmosphere FROM ITS SETTING DOWN, i.e. as if the field were at sea level where the air is denser, so zeroed at
        // altitude it over-reads height by about T0 / T(6,000 ft) = 288.15 / 276.3 (the real instrument does the same).
        double p7000 = 101325.0 * Math.Pow(1 - 7000 / 145366.45, 1 / 0.190263);
        Assert.InRange(s.IndicatedFt(p7000), 1040.0, 1055.0);
    }

    [Fact]
    public void AStandardWindowCannotZeroAHighFieldAndStopsAtItsEnd()
    {
        var s = new AltimeterSetting();
        s.ForType("c172-like");
        Assert.Equal(28.10, s.Range.MinInHg, 9); Assert.Equal(31.00, s.Range.MaxInHg, 9);
        Assert.False(s.SetToRead(Pa6000, 0.0));
        Assert.Equal(28.10, s.InHg, 9);                          // the knob is on its stop
        Assert.False(s.TrySet("23.98", out string why));
        Assert.Contains("28.10-31.00", why);
        Assert.Equal(28.10, s.InHg, 9);                          // a refused value changes nothing
    }

    [Theory]
    [InlineData("29.92", 29.92)]
    [InlineData("29,92", 29.92)]
    [InlineData("2992", 29.92)]
    [InlineData("30.01 inHg", 30.01)]
    [InlineData("1013", 29.91)]
    [InlineData("1013.25 hPa", 29.92)]
    [InlineData("24.5", 24.50)]
    [InlineData("22.00", 22.00)]
    public void TypedValuesInsideTheAerobaticWindowAreTaken(string text, double want)
    {
        Assert.True(KollsmanAltimeter.TryParse(text, KollsmanAltimeter.Aerobatic, out double v, out string why), why);
        Assert.Equal(want, v, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("21.99")]
    [InlineData("31.01")]
    [InlineData("0")]
    [InlineData("-29.92")]
    [InlineData("99999")]
    [InlineData("NaN")]
    [InlineData("1200 hPa")]
    public void AbsurdOrOutOfLimitValuesAreRefused(string text) =>
        Assert.False(KollsmanAltimeter.TryParse(text, KollsmanAltimeter.Aerobatic, out _, out _));

    [Theory]
    [InlineData("pitts-s2b-like", true)]
    [InlineData("extra-300-like", true)]
    [InlineData("great-lakes-like", true)]
    [InlineData("decathlon-8kcab-like", true)]
    [InlineData("glider-grob103-acro-like", true)]
    [InlineData("c172-like", false)]
    [InlineData("j3-cub-like", false)]
    [InlineData("glider-2-33-like", false)]
    public void TheAerobaticTypesGetTheExtendedWindow(string id, bool aero) =>
        Assert.Equal(aero ? 22.00 : 28.10, KollsmanAltimeter.RangeFor(id).MinInHg, 9);

    [Fact]
    public void TheKnobStepsInHundredthsAndStopsAtTheEnds()
    {
        var s = new AltimeterSetting();
        s.ForType("extra-300-like");
        s.Turn(30.99);
        s.Step(+5);
        Assert.Equal(31.00, s.InHg, 9);
        s.Step(-3);
        Assert.Equal(30.97, s.InHg, 9);
    }
}

public class GMeterFollowerTests
{
    [Fact]
    public void TheFollowersHoldTheExtremesUntilPushedThenSpringBackToThePointer()
    {
        var g = new GMeterFollowers();
        Assert.Equal((1.0, 1.0), (g.Max, g.Min));
        foreach (double x in new[] { 1.0, 3.5, 6.2, 2.0, -0.5, -2.8, 1.0 }) g.Step(x);
        Assert.Equal(6.2, g.Max, 9);
        Assert.Equal(-2.8, g.Min, 9);
        g.Reset(1.0);
        Assert.Equal((1.0, 1.0), (g.Max, g.Min));
        g.Reset(4.0);                                            // pushed while pulling 4 g: both go to the pointer
        Assert.Equal((4.0, 4.0), (g.Max, g.Min));
        g.Step(12.0);
        Assert.Equal(10.0, g.Max, 9);                            // the dial's stop
    }
}

public class CubFuelWireTests
{
    /// <summary>The J-3's nose-tank wire: 0.221 m of travel (build_j3_cub.py FUEL_TRAVEL), modelled at full.</summary>
    private static readonly RigNode Wire = new("FuelGauge_Wire", "", "fuel", true, 0, 1, 0, -0.967, 0.3937, 0,
        new[] { 0.0, 1.0 }, new[] { -0.221, 0.0 }, modelAt: 1.0);

    [Theory]
    [InlineData("j3-cub-own-rig.json")]
    [InlineData("j3-cub-floats-own-rig.json")]
    public void TheWireIsALiveRigRowOnTheFuelParam(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "rigs", file)));
        JsonElement row = doc.RootElement.GetProperty("rig").EnumerateArray().Single(r => r.GetProperty("node").GetString() == "FuelGauge_Wire");
        Assert.Equal("fuel", row.GetProperty("param").GetString());
        Assert.Equal("move", row.GetProperty("kind").GetString());
        Assert.Equal(-0.221, row.GetProperty("map")[0][1].GetDouble(), 6);
        Assert.DoesNotContain(doc.RootElement.GetProperty("static").EnumerateArray(), r => r.GetProperty("node").GetString() == "FuelGauge_Wire");
        Assert.True(new RigInputs().TryGet("fuel", out _));
    }

    [Theory]
    [InlineData("j3-cub-like")]
    [InlineData("j3-cub-floats-like")]
    public void BurningFuelLowersTheWireMonotonically(string id)
    {
        AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        var ac = new Aircraft(c, new RigidBodyState(new Vec3(0, 0, -1500), new Quat(0, 0, 0, 1), new Vec3(35, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
        string tank = ac.Load.Slots.First(s => s.Kind == AircraftLoad.StationKind.Fuel).Id;
        double cap = ac.Load.Slots.First(s => s.Id == tank).MaxKg;
        ac.Load.Set(tank, 0.06 * cap);                          // a nearly dry tank, so a minute of full power shows
        ac.ApplyLoad();
        var loop = new SimLoop(ac, 1.0 / 200.0);
        var wireM = new List<double>();
        var frac = new List<double>();
        for (int s = 0; s <= 60; s++)
        {
            double f = FuelGauges.TankFraction(ac.Load, tank);
            frac.Add(f);
            wireM.Add(Wire.Value(f));
            loop.RunFor(1.0, new ControlInputs(0, 0, 0, -1.0));  // full power (lever −1)
        }
        Assert.True(ac.FuelFlowKgPerSec > 0, "the engine burns nothing");
        for (int i = 1; i < wireM.Count; i++)
        {
            Assert.True(frac[i] <= frac[i - 1] + 1e-12, $"fuel rose at {i} s");
            Assert.True(wireM[i] <= wireM[i - 1] + 1e-12, $"wire rose at {i} s: {wireM[i - 1]:F5} -> {wireM[i]:F5}");
        }
        Assert.True(wireM[^1] < wireM[0] - 0.0005, $"wire moved only {(wireM[0] - wireM[^1]) * 1000:F2} mm in 60 s");
        Assert.InRange(wireM[0], -0.221 * 0.95, -0.221 * 0.93);   // 6 % fuel: the wire sits 94 % of its travel down
    }

    [Fact]
    public void FullShowsTheWholeWireAndEmptyOnlyTheHook()
    {
        Assert.Equal(0.0, Wire.Value(1.0), 9);
        Assert.Equal(-0.221, Wire.Value(0.0), 9);
        Assert.Equal(-0.1105, Wire.Value(0.5), 9);
    }
}

public class PanelInstrumentNameTests
{
    // Literal node names from the 2026-11-05 scan of the shipped GLBs (and the contract's new nodes).
    [Theory]
    [InlineData("RearPanel_Altimeter_Face", PanelInstrument.Altimeter, PanelSeat.Rear)]
    [InlineData("FrontPanel_Altimeter_Bezel", PanelInstrument.Altimeter, PanelSeat.Front)]
    [InlineData("Face_P3b_Altimeter_R", PanelInstrument.Altimeter, PanelSeat.Rear)]
    [InlineData("Bezel_P3b_Altimeter_F", PanelInstrument.Altimeter, PanelSeat.Front)]
    [InlineData("Needle_Alt100_R", PanelInstrument.Altimeter, PanelSeat.Rear)]
    [InlineData("Needle_Alt1000_F", PanelInstrument.Altimeter, PanelSeat.Front)]
    [InlineData("Needle_Alt_100ft", PanelInstrument.Altimeter, PanelSeat.Rear)]
    [InlineData("Dial_ALT_F", PanelInstrument.Altimeter, PanelSeat.Front)]
    [InlineData("Dial_Alt100_R", PanelInstrument.Altimeter, PanelSeat.Rear)]
    [InlineData("C172_Altimeter_case", PanelInstrument.Altimeter, PanelSeat.Rear)]
    [InlineData("C172_Altimeter_baro", PanelInstrument.AltimeterKnob, PanelSeat.Rear)]
    [InlineData("FrontPanel_Altimeter_Knob", PanelInstrument.AltimeterKnob, PanelSeat.Front)]
    [InlineData("RearPanel_Altimeter_Kollsman", PanelInstrument.AltimeterWindow, PanelSeat.Rear)]
    [InlineData("RearPanel_G_meter_Face", PanelInstrument.GMeter, PanelSeat.Rear)]
    [InlineData("FrontPanel_G_meter_Bezel", PanelInstrument.GMeter, PanelSeat.Front)]
    [InlineData("Needle_G_R", PanelInstrument.GMeter, PanelSeat.Rear)]
    [InlineData("Needle_GMax_F", PanelInstrument.GMeter, PanelSeat.Front)]
    [InlineData("Face_P3b_G_R", PanelInstrument.GMeter, PanelSeat.Rear)]
    [InlineData("FrontPanel_G_meter_Reset", PanelInstrument.GMeterReset, PanelSeat.Front)]
    [InlineData("RearPanel_G_meter_Reset", PanelInstrument.GMeterReset, PanelSeat.Rear)]
    [InlineData("Model:Needle_Alt100_R", PanelInstrument.Altimeter, PanelSeat.Rear)]   // as placed by AirframeBuilder
    [InlineData("Model:Needle_G_F", PanelInstrument.GMeter, PanelSeat.Front)]
    public void InstrumentsAreFoundWhateverTheModelCallsThem(string node, PanelInstrument kind, PanelSeat seat) =>
        Assert.Equal((kind, seat), PanelInstrumentNames.Classify(node));

    [Theory]
    [InlineData("Ctl_Toggle_Alternator")]
    [InlineData("Mount_AlternateAir_Front")]
    [InlineData("C172_Master_ALT")]
    [InlineData("Legacy_P3b_RearPanel_Altimeter_Face")]
    [InlineData("Legacy_P3b_Needle_G_R")]
    [InlineData("Grip_Alternator")]
    [InlineData("SequenceCard_Face")]
    [InlineData("Gear_L")]
    [InlineData("Needle_Gyro_R")]
    public void OtherNodesAreNotInstruments(string node) =>
        Assert.Equal(PanelInstrument.None, PanelInstrumentNames.Classify(node).Kind);
}
