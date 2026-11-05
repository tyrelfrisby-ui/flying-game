using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.Landing;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Landing page v2 (Ty 2026-11-05 05:48 + addendum, docs/LANDING-V2.md): the flow, its restricted lists, last-flown
/// memory, the seats, the warn-never-block weight and balance, the float placard and the thumbnail keys. Every expected
/// value is a literal (CLAUDE.md: a test that reads the file it is checking proves nothing); the fleet here is a
/// hand-copied slice of SessionSettings.Fleet, and the aeroplane a synthetic one whose numbers are worked below.
/// </summary>
public sealed class LandingFlowTests
{
    private static readonly (string id, string name)[] Fleet =
    {
        ("hang-glider-like", "Hang Glider-like"), ("glider-2-33-like", "2-33-like Glider"), ("c172-like", "172-like"),
        ("pa18-cub-like", "Super Cub-like"), ("pa18-bush-like", "Super Cub-like, Bushwheels"), ("pa18-floats-like", "Super Cub-like on Floats"),
        ("j3-cub-like", "J-3 Cub-like"), ("j3-cub-floats-like", "J-3 Cub-like on Floats"), ("pitts-s2b-like", "Pitts S-2-like"), ("great-lakes-like", "Great Lakes-like"),
        ("hughes-h4-like", "H-4-like"), ("aircam-like", "AirCam-like"), ("aircam-amphib-like", "AirCam-like Amphib"),
    };
    private static readonly (string, string, double)[] Airports = { ("KMEV", "KMEV", 4700), ("VALLEY", "VALLEY", 1200), ("MESA", "MESA", 6100) };
    private static readonly (string?, string)[] Challenges = { (null, "Free flight"), ("event:race", "Air Racing"), ("event:aerobox", "Aerobatic box"), ("a1c1-wings-level", "Wings level") };
    private static readonly (string, string)[] Lessons = { ("lesson:straight", "Straight"), ("lesson:climb-vy", "Climb Vy") };

    private static LandingCatalog Catalog() => new(Fleet, Airports, Challenges, Lessons,
        challengeAircraft: id => id == "a1c1-wings-level" ? "glider-2-33-like" : null,
        isScored: id => id == "event:race",
        lessonNeedsEngine: id => id == "lesson:climb-vy");

    /// <summary>A two-seat tandem: 600 kg as authored, CG at 0; front seat (+0.5 m) 77 kg in the reference, rear
    /// (−0.5 m) empty; one 60 kg tank at 0 holding 30 in the reference; baggage (−1.0 m) 20 kg max. So the empty
    /// airframe is 600 − 107 = 493 kg with its CG at −38.5/493 m. Gross 700 kg, CG limits +0.10 / −0.10 m.</summary>
    private static AircraftConfig Tandem(double gross = 700, double takeoff = 0) => new()
    {
        Mass = new MassConfig { MassKg = 600, Cg = new double[] { 0, 0, 0 }, Inertia = new InertiaConfig { Ixx = 1000, Iyy = 1200, Izz = 2000 } },
        Loading = new LoadingConfig
        {
            MaxGrossMassKg = gross, MaxTakeoffMassKg = takeoff, CgForwardLimitM = 0.10, CgAftLimitM = -0.10,
            Seats = new List<StationConfig>
            {
                new() { Id = "front", Label = "Front seat", Station = new double[] { 0.5, 0, 0 }, MaxKg = 120, ReferenceKg = 77, Eye = new double[] { 0.5, 0, -0.8 } },
                new() { Id = "rear", Label = "Rear seat", Station = new double[] { -0.5, 0, 0 }, MaxKg = 120, ReferenceKg = 0, Eye = new double[] { -0.5, 0, -0.8 } },
            },
            Tanks = new List<StationConfig> { new() { Id = "main", Label = "Main", Station = new double[] { 0, 0, 0 }, MaxKg = 60, ReferenceKg = 30 } },
            Baggage = new List<StationConfig> { new() { Id = "bags", Label = "Baggage", Station = new double[] { -1.0, 0, 0 }, MaxKg = 20 } },
        },
    };

    private static LandingFlow NewFlow(MemoryLandingStore? store = null, System.Func<string, AircraftConfig?>? cfg = null) =>
        new(Catalog(), store ?? new MemoryLandingStore(), cfg ?? (_ => Tandem()));

    // ---------------------------------------------------------------- catalog

    [Fact]
    public void VariantsFoldIntoTheirTypeAsConfigurations()
    {
        var cat = Catalog();
        Assert.Equal(new[] { "hang-glider-like", "glider-2-33-like", "c172-like", "pa18-cub-like", "j3-cub-like", "pitts-s2b-like", "great-lakes-like", "hughes-h4-like", "aircam-like" },
                     cat.Families.Select(f => f.Id).ToArray());
        var cub = cat.FamilyOf("pa18-floats-like")!;
        Assert.Equal("pa18-cub-like", cub.Id);
        Assert.Equal(new[] { "standard:pa18-cub-like:False:False", "bushwheels:pa18-bush-like:False:False", "wheel-skis:pa18-cub-like:False:True", "floats:pa18-floats-like:False:False" },
                     cub.Configs.Select(c => $"{c.Id}:{c.AircraftId}:{c.Bushwheels}:{c.Skis}").ToArray());
        // A type with no bushwheel file of its own gets the session's generic big tyres.
        Assert.Equal("bushwheels:c172-like:True:False", cat.FamilyOf("c172-like")!.Configs.Where(c => c.Id == "bushwheels").Select(c => $"{c.Id}:{c.AircraftId}:{c.Bushwheels}:{c.Skis}").Single());
        Assert.Equal(new[] { "standard", "bushwheels", "wheel-skis", "amphibian" }, cat.FamilyOf("aircam-like")!.Configs.Select(c => c.Id).ToArray());
        Assert.Equal(new[] { "standard" }, cat.FamilyOf("glider-2-33-like")!.Configs.Select(c => c.Id).ToArray());
        Assert.Equal(new[] { "hull" }, cat.FamilyOf("hughes-h4-like")!.Configs.Select(c => c.Id).ToArray());
        Assert.Equal(new[] { "foot" }, cat.FamilyOf("hang-glider-like")!.Configs.Select(c => c.Id).ToArray());
    }

    // ---------------------------------------------------------------- last flown

    [Fact]
    public void AFirstTimerStartsInThe233()
    {
        var f = NewFlow();
        Assert.Equal("glider-2-33-like", f.AircraftId);
        Assert.Equal("standard", f.GearId);
        Assert.Equal(LandingPage.Choose, f.Page);
        Assert.Equal("2-33-like Glider", f.HeroTitle);
        Assert.Equal(new[] { "ac/glider-2-33-like/standard" }, f.HeroThumbnails.ToArray());
    }

    [Fact]
    public void LaunchRemembersTheAeroplaneAndItsConfigurationForNextTime()
    {
        var store = new MemoryLandingStore();
        var f = NewFlow(store);
        Assert.True(f.Pick(PickerKind.Aircraft, "pa18-cub-like"));
        Assert.True(f.Pick(PickerKind.Configuration, "floats"));
        Assert.True(f.Pick(PickerKind.Location, "MESA"));
        Assert.True(f.Pick(PickerKind.Viewpoint, "OnTheRunway"));
        f.SetTimeOfDay(TimeOfDay.Sunset);
        f.Next();
        bool launched = false; f.Launched += _ => launched = true;
        Assert.True(f.Launch());
        Assert.True(launched);

        var again = NewFlow(store);
        Assert.Equal("pa18-floats-like", again.AircraftId);
        Assert.Equal("floats", again.GearId);
        Assert.Equal(2, again.LocationIndex);
        Assert.Equal(Viewpoint.OnTheRunway, again.Viewpoint);
        Assert.Equal(TimeOfDay.Sunset, again.TimeOfDay);
        Assert.Equal(LandingPage.Choose, again.Page);
    }

    [Fact]
    public void ASavedVariantIdRestoresItsTypeAndGear()
    {
        var store = new MemoryLandingStore();
        store.Set("landing.lastFlown", "j3-cub-floats-like");   // what SessionSettings had saved before v2
        var f = NewFlow(store);
        Assert.Equal("j3-cub-like", f.FamilyId);
        Assert.Equal("floats", f.GearId);
        Assert.Equal("j3-cub-floats-like", f.AircraftId);
    }

    // ---------------------------------------------------------------- navigation

    [Fact]
    public void TheMenusBelongToPageOneAndBackClosesAMenuFirst()
    {
        var f = NewFlow();
        Assert.False(f.CanLaunch);
        Assert.False(f.Launch());
        f.Open(PickerKind.Location);
        Assert.Equal(PickerKind.Location, f.OpenPicker);
        f.Back();
        Assert.Equal(PickerKind.None, f.OpenPicker);
        Assert.Equal(LandingPage.Choose, f.Page);
        f.Next();
        Assert.Equal(LandingPage.Load, f.Page);
        f.Open(PickerKind.Aircraft);
        Assert.Equal(PickerKind.None, f.OpenPicker);
        Assert.True(f.CanLaunch);
        f.Back();
        Assert.Equal(LandingPage.Choose, f.Page);
    }

    [Fact]
    public void EveryChangeBumpsTheVersionThePreviewWatches()
    {
        var f = NewFlow();
        int v = f.Version;
        f.Pick(PickerKind.Aircraft, "pitts-s2b-like");
        Assert.True(f.Version > v);
        v = f.Version;
        Assert.False(f.Pick(PickerKind.Configuration, "floats"));   // the Pitts has no float file: not offered
        Assert.Equal(v, f.Version);
    }

    // ---------------------------------------------------------------- restricted lists

    [Fact]
    public void ALessonOffersItsTrainersOnStandardGearAndSetsItsOwnStart()
    {
        var f = NewFlow();
        f.Pick(PickerKind.Aircraft, "pitts-s2b-like");
        Assert.True(f.Pick(PickerKind.Lesson, "lesson:straight"));
        Assert.Equal(new[] { "glider-2-33-like", "c172-like", "pa18-cub-like", "j3-cub-like" }, f.Items(PickerKind.Aircraft).Select(i => i.Id).ToArray());
        Assert.Equal("glider-2-33-like", f.FamilyId);              // the Pitts is not a trainer: the first one in menu order
        Assert.True(f.IsFixed(PickerKind.Viewpoint));
        Assert.Equal("The lesson sets its own start", f.ViewpointName);
        f.Pick(PickerKind.Aircraft, "pa18-cub-like");
        Assert.Equal(new[] { "standard" }, f.Items(PickerKind.Configuration).Select(i => i.Id).ToArray());
        // An engine lesson drops the glider.
        Assert.True(f.Pick(PickerKind.Lesson, "lesson:climb-vy"));
        Assert.DoesNotContain("glider-2-33-like", f.Items(PickerKind.Aircraft).Select(i => i.Id));
        // Free flight lifts every restriction.
        Assert.True(f.Pick(PickerKind.Lesson, ""));
        Assert.Null(f.ActivityId);
        Assert.Equal(9, f.Items(PickerKind.Aircraft).Count);
        Assert.False(f.IsFixed(PickerKind.Viewpoint));
    }

    [Fact]
    public void AChallengeIsFlownInItsOwnAeroplaneAndTheBoxInAnAerobat()
    {
        var f = NewFlow();
        f.Pick(PickerKind.Aircraft, "c172-like");
        Assert.True(f.Pick(PickerKind.Challenge, "a1c1-wings-level"));
        Assert.Equal(new[] { "glider-2-33-like" }, f.Items(PickerKind.Aircraft).Select(i => i.Id).ToArray());
        Assert.True(f.IsFixed(PickerKind.Location));

        Assert.True(f.Pick(PickerKind.Challenge, "event:aerobox"));
        Assert.Equal(new[] { "pitts-s2b-like", "great-lakes-like" }, f.Items(PickerKind.Aircraft).Select(i => i.Id).ToArray());
        Assert.Equal(Viewpoint.InAeroBox, f.Viewpoint);
        Assert.Equal(new[] { "InAeroBox" }, f.Items(PickerKind.Viewpoint).Select(i => i.Id).ToArray());

        // A scored contest: standard gear, the conditions are the contest's.
        f.Pick(PickerKind.Challenge, "");
        f.Pick(PickerKind.Aircraft, "pa18-cub-like");
        f.Pick(PickerKind.Configuration, "bushwheels");
        Assert.True(f.Pick(PickerKind.Challenge, "event:race"));
        Assert.Equal("standard", f.GearId);
        Assert.True(f.ConditionsLocked);
        Assert.All(f.WeatherItems(), w => Assert.False(w.Enabled));
        Assert.DoesNotContain("glider-2-33-like", f.Items(PickerKind.Aircraft).Select(i => i.Id));
    }

    // ---------------------------------------------------------------- seats

    [Fact]
    public void ExactlyOneSeatIsMineAndOnlyAnInvitedPlayerTakesTheOtherControls()
    {
        var f = NewFlow();
        f.Next();
        Assert.Equal(new[] { "front:Me:Passenger:77", "rear:Empty:Passenger:0" }, f.Seats.Select(Row).ToArray());
        Assert.False(f.SetSeat("front", Occupant.Empty));                 // I cannot leave my own seat empty
        f.SetSeat("rear", Occupant.DefaultPerson, SeatRole.DualControl);  // a default person rides; it never flies
        Assert.Equal("rear:DefaultPerson:Passenger:77", Row(f.Seats[1]));
        f.SetSeat("rear", Occupant.Me);                                    // moving me empties the seat I left; I weigh the reference pilot
        Assert.Equal(new[] { "front:Empty:Passenger:0", "rear:Me:Passenger:86" }, f.Seats.Select(Row).ToArray());
        f.SetSeat("front", Occupant.InvitedPlayer, SeatRole.DualControl);
        Assert.Equal("front:InvitedPlayer:DualControl:77", Row(f.Seats[0]));
    }

    [Fact]
    public void OneTapOnASeatCyclesWhoSitsInIt()
    {
        var f = NewFlow();
        f.Next();
        var seen = new List<string>();
        for (int i = 0; i < 5; i++) { f.CycleSeat("rear"); seen.Add($"{f.Seats[1].Occupant}:{f.Seats[1].Role}"); }
        Assert.Equal(new[] { "DefaultPerson:Passenger", "InvitedPlayer:Passenger", "InvitedPlayer:DualControl", "Empty:Passenger", "DefaultPerson:Passenger" }, seen.ToArray());
        f.CycleSeat("front");   // mine: does not cycle
        Assert.Equal(Occupant.Me, f.Seats[0].Occupant);
    }

    private static string Row(SeatRow s) => $"{s.SeatId}:{s.Occupant}:{s.Role}:{s.Kg:0}";

    // ---------------------------------------------------------------- weight and balance

    [Fact]
    public void WeightAndBalanceWarnsButNeverBlocksLaunch()
    {
        var f = NewFlow();
        f.Next();
        // Never touched = as authored: me in front at its 77 kg reference, the tank at its 30 kg reference:
        // 493 + 77 + 30 = 600 kg (the authored mass), CG (−38.5 + 38.5)/600 = 0 (the authored CG).
        var wb = f.WeightBalance!;
        Assert.Equal(600.0, wb.TotalKg, 6);
        Assert.Equal(0.0, wb.CgM, 9);
        Assert.True(wb.InLimits);
        Assert.Equal(0.5, wb.PointX, 9);                                      // centred between the limits
        Assert.Equal(570.0, wb.ZeroFuelKg, 6);
        Assert.Equal(5, wb.Envelope.Count);

        // 120 kg in the back and 20 kg of bags: 740 kg, CG (−60 − 20)/740 = −0.108108 m.
        f.SetSeat("rear", Occupant.DefaultPerson);
        f.SetSeatKg("rear", 120);
        f.SetStationKg("bags", 20);
        wb = f.WeightBalance!;
        Assert.Equal(740.0, wb.TotalKg, 6);
        Assert.Equal(-0.108108, wb.CgM, 6);
        Assert.True(wb.OverGross);
        Assert.True(wb.CgAft);
        // Worded in the default units (Standard): 40 kg / 0.45359237 = 88.18 lb. Metric words it in kg.
        Assert.Equal(new[] { "Over gross by 88 lb: it will fly like it", "CG aft of the limit: light, twitchy pitch, hard to recover from a spin" }, wb.Warnings.ToArray());
        f.SetUnits(UnitSystem.Metric);
        Assert.Equal(new[] { "Over gross by 40 kg: it will fly like it", "CG aft of the limit: light, twitchy pitch, hard to recover from a spin" }, f.WeightBalance!.Warnings.ToArray());
        Assert.True(wb.CanLaunch);
        Assert.True(f.Launch());
    }

    // ---------------------------------------------------------------- units (Ty 2026-11-05 10:56)

    [Fact]
    public void UnitsDefaultToStandardWhenNothingIsSaved()
    {
        var store = new MemoryLandingStore();
        var f = NewFlow(store);
        Assert.Equal(UnitSystem.Standard, f.Units);
        Assert.False(store.Values.ContainsKey("landing.units"));   // opening the page writes nothing
        Assert.Equal(new[] { "Standard:True", "Metric:False" }, f.UnitItems().Select(i => $"{i.Title}:{i.Selected}").ToArray());
        // Anything unreadable is the default too.
        store.Set("landing.units", "furlongs");
        Assert.Equal(UnitSystem.Standard, NewFlow(store).Units);
    }

    [Fact]
    public void TheUnitsChoiceIsRememberedAtOnceAndAcrossLaunches()
    {
        var store = new MemoryLandingStore();
        var f = NewFlow(store);
        f.Next();
        f.SetUnits(UnitSystem.Metric);
        Assert.Equal("metric", store.Values["landing.units"]);   // saved without LAUNCH: backing out does not lose it
        Assert.Equal(UnitSystem.Metric, NewFlow(store).Units);

        var g = NewFlow(store);
        g.Next();
        g.SetUnits(UnitSystem.Standard);
        Assert.True(g.Launch());
        Assert.Equal("standard", store.Values["landing.units"]);
        Assert.Equal(UnitSystem.Standard, NewFlow(store).Units);
    }

    [Fact]
    public void UnitsChangeWhatIsShownAndNothingThatIsFlown()
    {
        var f = NewFlow();
        f.Next();
        var front = f.Seats.Single(s => s.SeatId == "front");
        // Standard: 77 kg = 169.76 lb; one tap is 10 lb = 4.5359237 kg.
        Assert.Equal("170 lb", f.MassText(front.Kg));
        Assert.Equal(4.5359237, f.MassStepKg, 9);
        Assert.Equal("1,200 ft", f.Items(PickerKind.Location).Single(i => i.Id == "VALLEY").Subtitle);   // page 1 reads the same choice
        f.SetSeatKg("front", front.Kg + f.MassStepKg);
        Assert.Equal(81.5359237, f.Seats.Single(s => s.SeatId == "front").Kg, 9);   // the flow stays in kg
        Assert.Equal("180 lb", f.MassText(f.Seats.Single(s => s.SeatId == "front").Kg));
        double totalKg = f.WeightBalance!.TotalKg, cg = f.WeightBalance!.CgM;

        f.SetUnits(UnitSystem.Metric);
        Assert.Equal("82 kg", f.MassText(f.Seats.Single(s => s.SeatId == "front").Kg));
        Assert.Equal(5.0, f.MassStepKg, 9);
        Assert.Equal("366 m", f.Items(PickerKind.Location).Single(i => i.Id == "VALLEY").Subtitle);   // 1,200 × 0.3048 = 365.76
        Assert.Equal(totalKg, f.WeightBalance!.TotalKg, 12);   // the load is not touched by the display
        Assert.Equal(cg, f.WeightBalance!.CgM, 12);
        Assert.Equal(new[] { "Standard:False", "Metric:True" }, f.UnitItems().Select(i => $"{i.Title}:{i.Selected}").ToArray());
    }

    [Fact]
    public void DisplayUnitsConvertWithTheExactDefinitions()
    {
        Assert.Equal("190 lb", DisplayUnits.Mass(86.0, UnitSystem.Standard));     // 86 / 0.45359237 = 189.60
        Assert.Equal("86 kg", DisplayUnits.Mass(86.0, UnitSystem.Metric));
        Assert.Equal("4,700 ft", DisplayUnits.Elevation(1432.56, UnitSystem.Standard));   // 4,700 × 0.3048
        Assert.Equal("1,433 m", DisplayUnits.Elevation(1432.56, UnitSystem.Metric));
        Assert.Equal("15 kt", DisplayUnits.WindFromKt(15, UnitSystem.Standard));
        Assert.Equal("28 km/h", DisplayUnits.WindFromKt(15, UnitSystem.Metric));      // 15 × 1.852 = 27.78
        Assert.Equal("landing.units", DisplayUnits.PrefKey);
        Assert.Equal(UnitSystem.Metric, DisplayUnits.Parse("Metric"));
        Assert.Equal(UnitSystem.Standard, DisplayUnits.Parse(null));
    }

    /// <summary>The page must not change anybody's aeroplane by being opened: on the real configs, a type nobody has
    /// loaded flies at exactly its authored mass and CG (the masses below are the configs' mass.massKg, copied).</summary>
    [Theory]
    [InlineData("pitts-s2b-like")]
    [InlineData("j3-cub-like")]
    [InlineData("c172-like")]
    [InlineData("glider-2-33-like")]
    [InlineData("great-lakes-like")]
    public void OpeningThePageLeavesTheAuthoredMassAlone(string id)
    {
        AircraftConfig Real(string a) => FlyingGame.Core.DataContracts.AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TestData", a + ".json"));
        var real = Real(id);
        var f = NewFlow(cfg: Real);
        var fam = f.Catalog.FamilyOf(id)!;
        f.Pick(PickerKind.Aircraft, fam.Id);
        f.Next();
        Assert.Equal(real.Mass.MassKg, f.WeightBalance!.TotalKg, 6);
        Assert.Equal(real.Mass.Cg[0], f.WeightBalance!.CgM, 6);
    }

    [Fact]
    public void FloatsGetTwentyPercentMoreGrossThanTheirWheels()
    {
        // The configs author gross as maxTakeoffMassKg (J-3: 553.38); the float file authors none.
        System.Func<string, AircraftConfig?> cfg = id => id == "j3-cub-like" ? Tandem(gross: 0, takeoff: 553.38) : Tandem(gross: 0);
        var f = NewFlow(cfg: cfg);
        f.Pick(PickerKind.Aircraft, "j3-cub-like");
        Assert.Equal(553.38, f.WeightBalance!.MaxGrossKg, 6);
        f.Pick(PickerKind.Configuration, "floats");
        Assert.Equal(664.056, f.WeightBalance!.MaxGrossKg, 6);
        Assert.Equal(664.056, f.GrossOverrideKg(), 6);
    }

    [Fact]
    public void FuelPresetsFillEveryTankToTheSameFraction()
    {
        var f = NewFlow();
        f.Next();
        Assert.Equal(0.5, f.FuelFraction, 9);              // a tank never set holds its reference fuel (30 of 60)
        f.SetFuelFraction(0.5);
        Assert.Equal(30.0, f.Stations.Single(s => s.StationId == "main").Kg, 9);
        f.SetStationKg("main", 99);
        Assert.Equal(60.0, f.Stations.Single(s => s.StationId == "main").Kg, 9);   // capped at the tank
    }

    // ---------------------------------------------------------------- thumbnails

    [Fact]
    public void ThumbnailKeysFallBackFromTheExactPictureToTheAirport()
    {
        Assert.Equal(new[]
        {
            "loc/pa18-cub-like/floats/kmev/OnTheRunway", "loc/pa18-cub-like/standard/kmev/OnTheRunway",
            "loc/any/kmev/OnTheRunway", "loc/any/kmev/InTheAir",
        }, ThumbnailKeys.LocationFallbacks("pa18-cub-like", "floats", "KMEV", Viewpoint.OnTheRunway).ToArray());
        Assert.Equal("loc/any/kmev/OnTheRunway",
                     ThumbnailKeys.Resolve(ThumbnailKeys.LocationFallbacks("pa18-cub-like", "floats", "KMEV", Viewpoint.OnTheRunway), k => k.StartsWith("loc/any")));
        Assert.Null(ThumbnailKeys.Resolve(ThumbnailKeys.AircraftFallbacks("pitts-s2b-like", "standard"), _ => false));
        Assert.Equal("ac/glider-2-33-like/standard", ThumbnailKeys.Aircraft("glider-2-33-like", "standard"));
        Assert.Equal("icon/weather/s-turns--1", ThumbnailKeys.Icon("weather", "S turns +1"));
    }

    [Fact]
    public void TheViewpointCardsShowThatAeroplaneAtThatStartHere()
    {
        var f = NewFlow();
        f.Pick(PickerKind.Aircraft, "pitts-s2b-like");
        f.Pick(PickerKind.Location, "VALLEY");
        var final = f.Items(PickerKind.Viewpoint).Single(i => i.Id == "OnFinal");
        Assert.Equal("loc/pitts-s2b-like/standard/valley/OnFinal", final.ThumbnailKey);
        var mesa = f.Items(PickerKind.Location).Single(i => i.Id == "MESA");
        Assert.Equal("loc/pitts-s2b-like/standard/mesa/InTheAir", mesa.ThumbnailKey);
    }

    [Fact]
    public void ChoicesMadeOnTheClassicChaptersAreAdopted()
    {
        var f = NewFlow();
        f.Adopt("pa18-cub-like", bushwheels: false, skis: true, 1, Viewpoint.OnFinal, null);
        Assert.Equal("wheel-skis", f.GearId);
        Assert.Equal(1, f.LocationIndex);
        Assert.Equal(Viewpoint.OnFinal, f.Viewpoint);
        f.Adopt("pa18-bush-like", false, false, 0, Viewpoint.InTheAir, "lesson:straight");
        Assert.Equal("standard", f.GearId);                // the lesson's rule: standard gear
        Assert.Equal("lesson:straight", f.ActivityId);
    }
}
