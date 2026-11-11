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
        Assert.True(f.Pick(PickerKind.Start, "OnTheRunway"));
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
        Assert.True(f.IsFixed(PickerKind.Start));
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
        Assert.False(f.IsFixed(PickerKind.Start));
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
        Assert.Equal(new[] { "InAeroBox" }, f.Items(PickerKind.Start).Select(i => i.Id).ToArray());

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
        // Zero fuel: 710 kg, CG −80/710 = −0.112676 m, also aft of −0.10.
        Assert.Equal(710.0, wb.ZeroFuelKg, 6);
        Assert.Equal(-0.112676, wb.ZeroFuelCgM, 6);
        Assert.True(wb.ZeroFuelCgAft);
        // Factual labels only (Ty t881u); the amounts ride separately, in the default units (Standard):
        // 40 kg / 0.45359237 = 88.18 lb; 0.008108 m = 0.32 in; 0.012676 m = 0.50 in. Metric: 40 kg, 8 mm, 13 mm.
        Assert.Equal(new[] { "Over gross", "Aft c.g.", "Aft c.g. at zero fuel" }, wb.Warnings.ToArray());
        Assert.Equal(new[] { "Over gross by 88 lb", "Aft c.g. by 0.3 in", "Aft c.g. at zero fuel by 0.5 in" }, wb.Details.ToArray());
        f.SetUnits(UnitSystem.Metric);
        Assert.Equal(new[] { "Over gross", "Aft c.g.", "Aft c.g. at zero fuel" }, f.WeightBalance!.Warnings.ToArray());
        Assert.Equal(new[] { "Over gross by 40 kg", "Aft c.g. by 8 mm", "Aft c.g. at zero fuel by 13 mm" }, f.WeightBalance!.Details.ToArray());
        Assert.True(wb.CanLaunch);
        Assert.True(f.Launch());
    }

    // ---------------------------------------------------------------- W&B envelope, labels, launch question (Ty t880u / t881u / t882u, 2026-11-06)

    /// <summary>The synthetic tandem with an envelope whose forward limit moves with weight, as a certificate writes it:
    /// +0.10 m at 550 kg or less, straight line to +0.02 m at 650 kg and above; aft −0.10 m throughout.</summary>
    private static AircraftConfig Sloped()
    {
        var c = Tandem();
        c.Loading.CgLimits = new List<CgLimitConfig>
        {
            new() { MassKg = 650, ForwardM = 0.02, AftM = -0.10 },   // out of order on purpose: rows are sorted by weight
            new() { MassKg = 550, ForwardM = 0.10, AftM = -0.10 },
        };
        return c;
    }

    [Fact]
    public void AWeightDependentEnvelopeIsInterpolatedAndHeldAtItsEnds()
    {
        var load = new FlyingGame.Core.AircraftLoad(Sloped());
        Assert.True(load.HasCgEnvelope);
        Assert.Equal((0.10, -0.10), load.CgLimitsAt(400));    // "at 550 kg or less"
        Assert.Equal((0.10, -0.10), load.CgLimitsAt(550));
        var mid = load.CgLimitsAt(600);                        // half way: 0.10 − 0.5 × 0.08
        Assert.Equal(0.06, mid.forwardM, 12);
        Assert.Equal(-0.10, mid.aftM, 12);
        var top = load.CgLimitsAt(650);
        Assert.Equal(0.02, top.forwardM, 12);
        Assert.Equal(-0.10, top.aftM, 12);
        Assert.Equal((0.02, -0.10), load.CgLimitsAt(900));    // above the last row it holds

        // A row with its forward limit aft of its aft limit is not an envelope: refused whole, never half-used.
        var bad = Sloped();
        bad.Loading.CgLimits[1].ForwardM = -0.2;
        Assert.False(new FlyingGame.Core.AircraftLoad(bad).HasCgEnvelope);
    }

    [Fact]
    public void TheEnvelopeIsDrawnUpTheForwardLimitAcrossTheGrossAndDownTheAft()
    {
        var wb = WeightBalance.From(new FlyingGame.Core.AircraftLoad(Sloped()));
        // Floor = empty weight 493 kg (a certificate's lowest weight is "or less", not a limit); top = gross 700 kg.
        var expect = new (double cg, double kg)[]
        {
            (0.10, 493), (0.10, 550), (0.02, 650), (0.02, 700), (-0.10, 700), (-0.10, 650), (-0.10, 550), (-0.10, 493), (0.10, 493),
        };
        Assert.Equal(expect.Length, wb.EnvelopeCg.Count);
        for (int i = 0; i < expect.Length; i++)
        {
            Assert.Equal(expect[i].cg, wb.EnvelopeCg[i].cgM, 9);
            Assert.Equal(expect[i].kg, wb.EnvelopeCg[i].kg, 9);
            Assert.Equal(wb.PlotX(expect[i].cg), wb.Envelope[i].x, 9);
            Assert.Equal(wb.PlotY(expect[i].kg), wb.Envelope[i].y, 9);
        }
        Assert.Equal(new[] { "Forward c.g. limit", "Aft c.g. limit", "Max gross" }, wb.Limits.Select(l => l.Label).ToArray());
        Assert.Equal(4, wb.Limits[0].Points.Count);
        Assert.Equal(0.0, wb.Envelope[0].y, 9);                // the bottom edge sits on the plot's floor
        Assert.True(wb.PlotX(0.10) < wb.PlotX(-0.10), "forward must be on the LEFT, as on a paper chart");
    }

    [Fact]
    public void ASlopedForwardLimitCatchesWhatABoxWouldMiss()
    {
        // 120 kg in front: 643 kg, CG 21.5 / 643 = +0.033437 m. The limit at 643 kg is 0.10 − 0.93 × 0.08 = +0.0256 m.
        var f = NewFlow(cfg: _ => Sloped());
        f.Next();
        f.SetSeatKg("front", 120);
        var wb = f.WeightBalance!;
        Assert.Equal(643.0, wb.TotalKg, 6);
        Assert.Equal(0.033437, wb.CgM, 6);
        Assert.Equal(0.0256, wb.FwdLimitM, 9);
        Assert.True(wb.CgForward);
        Assert.False(wb.TakeoffInside);
        // Zero fuel is 613 kg at +0.035073 m, inside its own weight's limit of 0.10 − 0.63 × 0.08 = +0.0496 m.
        Assert.True(wb.ZeroFuelInside);
        Assert.Equal(new[] { "Forward c.g." }, wb.Warnings.ToArray());
    }

    [Fact]
    public void TheZeroFuelPointIsCheckedAgainstItsOwnWeightsLimits()
    {
        // The tank moved forward to +0.5 m (the empty CG re-solves, the reference is still 600 kg at 0); gross 800.
        // 120 kg in the back: 720 kg, CG −60/720 = −0.083333 m, inside. Burn the 30 kg off: 690 kg, −75/690 = −0.108696.
        var c = Tandem(gross: 800);
        c.Loading.Tanks[0].Station = new double[] { 0.5, 0, 0 };
        var f = NewFlow(cfg: _ => c);
        f.Next();
        f.SetSeat("rear", Occupant.DefaultPerson);
        f.SetSeatKg("rear", 120);
        var wb = f.WeightBalance!;
        Assert.Equal(-0.083333, wb.CgM, 6);
        Assert.True(wb.TakeoffInside);
        Assert.Equal(690.0, wb.ZeroFuelKg, 6);
        Assert.Equal(-0.108696, wb.ZeroFuelCgM, 6);
        Assert.False(wb.ZeroFuelInside);
        Assert.Equal(new[] { "Aft c.g. at zero fuel" }, wb.Warnings.ToArray());
        Assert.False(wb.InLimits);
        Assert.Equal(LandingFlow.LaunchRequest.NeedsConfirmation, f.RequestLaunch());
        Assert.Equal(-0.083333, f.WeightBalance!.CgM, 6);           // and the fuel was put back exactly
    }

    [Fact]
    public void TheAxesTickInTheChosenUnitsFromTheTypesDatum()
    {
        // Datum 1 m forward of body 0. Limits ±0.10 m, both points at 0, so the frame is ±0.124 m: 0.876 .. 1.124 m aft of
        // datum = 34.49 .. 44.25 in (step 2) or 876 .. 1124 mm (step 50). Weight 493 .. 770 kg = 1086.9 .. 1697.6 lb (step
        // 200) or step 100 kg.
        var c = Tandem();
        c.Loading.HasCgDatum = true; c.Loading.CgDatumM = 1.0;
        var std = WeightBalance.From(new FlyingGame.Core.AircraftLoad(c), units: UnitSystem.Standard);
        Assert.Equal("C.G. (in aft of datum)", std.XAxisTitle);
        Assert.Equal("Weight (lb)", std.YAxisTitle);
        Assert.Equal(new[] { "36", "38", "40", "42", "44" }, std.XTicks.Select(t => t.Label).ToArray());
        Assert.Equal(0.154839, std.XTicks[0].At, 6);
        Assert.Equal(new[] { "1,200", "1,400", "1,600" }, std.YTicks.Select(t => t.Label).ToArray());
        Assert.Equal(0.185238, std.YTicks[0].At, 6);
        Assert.Equal("39.4 in", std.CgText(0.0));
        Assert.Equal("1323 lb at 39.4 in", std.TakeoffText);

        var met = WeightBalance.From(new FlyingGame.Core.AircraftLoad(c), units: UnitSystem.Metric);
        Assert.Equal("C.G. (mm aft of datum)", met.XAxisTitle);
        Assert.Equal("Weight (kg)", met.YAxisTitle);
        Assert.Equal(new[] { "900", "950", "1000", "1050", "1100" }, met.XTicks.Select(t => t.Label).ToArray());
        Assert.Equal(0.096774, met.XTicks[0].At, 6);
        Assert.Equal(new[] { "500", "600", "700" }, met.YTicks.Select(t => t.Label).ToArray());
        Assert.Equal(0.025271, met.YTicks[0].At, 6);
        Assert.Equal("1000 mm", met.CgText(0.0));
        // The units change the words, never the aeroplane.
        Assert.Equal(std.PointX, met.PointX, 12);
        Assert.Equal(std.PointY, met.PointY, 12);

        // No datum located: measured from the body reference, and the axis says so.
        Assert.Equal("C.G. (in aft of reference)", WeightBalance.From(new FlyingGame.Core.AircraftLoad(Tandem())).XAxisTitle);
    }

    [Fact]
    public void OutOfLimitsIsWordedFactuallyAndNeverCoaxes()
    {
        var f = NewFlow();
        f.Next();
        f.SetSeat("rear", Occupant.DefaultPerson);
        f.SetSeatKg("rear", 120);
        f.SetStationKg("bags", 20);
        var allowed = new[] { "Over gross", "Forward c.g.", "Aft c.g.", "Forward c.g. at zero fuel", "Aft c.g. at zero fuel" };
        var wb = f.WeightBalance!;
        Assert.NotEmpty(wb.Warnings);
        Assert.All(wb.Warnings, w => Assert.Contains(w, allowed));
        foreach (string line in wb.Warnings.Concat(wb.Details).Concat(f.Summary()))
            Assert.DoesNotContain("still fly", line, System.StringComparison.OrdinalIgnoreCase);

        // Nor anywhere in the landing page's code, flat or headset (Ty t881u: "Never say 'you can still fly'").
        string root = RepoRoot();
        var files = new[] { "src/FlyingGame.Core/Landing", "unity/Assets/_Project/Scripts/Bridge/Landing" }
            .SelectMany(d => System.IO.Directory.GetFiles(System.IO.Path.Combine(root, d), "*.cs"))
            .Append(System.IO.Path.Combine(root, "unity/Assets/_Project/Scripts/Bridge/QuestMenu.cs"));
        foreach (string file in files)
            Assert.DoesNotContain("still fly", System.IO.File.ReadAllText(file), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchOutOfLimitsAsksInTyesExactWordsAndGoBackLaunchesNothing()
    {
        // t882u, copied by hand. The t881u draft ("...affect handling and aircraft performance.") must not ship.
        Assert.Equal("Your W&B is out of limits which will negatively affect aircraft handling and performance.", LandingFlow.WbOutOfLimitsMessage);
        Assert.NotEqual("Your W&B is out of limits which will negatively affect handling and aircraft performance.", LandingFlow.WbOutOfLimitsMessage);
        Assert.Equal("Go back", LandingFlow.WbGoBackLabel);
        Assert.Equal("Fly", LandingFlow.WbFlyLabel);

        var store = new MemoryLandingStore();
        var f = NewFlow(store);
        int launched = 0;
        f.Launched += _ => launched++;
        f.Next();
        f.SetSeat("rear", Occupant.DefaultPerson);
        f.SetSeatKg("rear", 120);
        Assert.False(f.WeightBalance!.InLimits);

        Assert.Equal(LandingFlow.LaunchRequest.NeedsConfirmation, f.RequestLaunch());
        Assert.True(f.LaunchConfirmPending);
        Assert.Equal(0, launched);
        Assert.False(store.Values.ContainsKey("landing.lastFlown"));   // nothing remembered either

        Assert.False(f.ConfirmLaunch(fly: false));                    // Go back
        Assert.False(f.LaunchConfirmPending);
        Assert.Equal(LandingPage.Load, f.Page);                       // back on the loading, the load untouched
        Assert.Equal(120.0, f.Seats.Single(x => x.SeatId == "rear").Kg, 9);
        Assert.Equal(0, launched);
        Assert.False(f.ConfirmLaunch(fly: true));                     // no question pending: nothing to answer
        Assert.Equal(0, launched);
    }

    [Fact]
    public void FlyAtTheQuestionLaunchesAsLoaded()
    {
        var f = NewFlow();
        int launched = 0;
        f.Launched += _ => launched++;
        f.Next();
        f.SetSeat("rear", Occupant.DefaultPerson);
        f.SetSeatKg("rear", 120);
        Assert.Equal(LandingFlow.LaunchRequest.NeedsConfirmation, f.RequestLaunch());
        Assert.True(f.ConfirmLaunch(fly: true));
        Assert.Equal(1, launched);
        Assert.False(f.LaunchConfirmPending);
        Assert.False(f.WeightBalance!.InLimits);                      // flown out of limits, as asked: a warning, never a block
    }

    [Fact]
    public void InLimitsLaunchesWithoutAsking()
    {
        var f = NewFlow();
        int launched = 0;
        f.Launched += _ => launched++;
        f.Next();
        Assert.True(f.WeightBalance!.InLimits);
        Assert.Equal(LandingFlow.LaunchRequest.Launched, f.RequestLaunch());
        Assert.False(f.LaunchConfirmPending);
        Assert.Equal(1, launched);
    }

    /// <summary>The verified envelopes, against the certificates' own numbers copied by hand (CLAUDE.md: literals, not
    /// the file under test). Arms are read back off the chart's own axis (inches / mm aft of the type's datum), so this
    /// also pins each datum. Sources: FAA TCDS A8SO Rev. 21 (Pitts S-2B), A-691 (J3C-65), A18EA Rev. 11 (2T-1A-2), EASA
    /// TCDS A.038 (Swift S-1), Grob G 103 Acro [POH] 2.7, FAA Glider Data Sheet G2EA Rev. 2 (SGS 2-33).</summary>
    [Theory]
    //          id                          kg at     fwd arm  aft arm   units      gross kg
    [InlineData("pitts-s2b-like",           737.09,   89.58,   90.50,   "in",      737.09)]    // acrobatic at 1625 lb
    [InlineData("pitts-s2b-like",           669.05,   86.35,   90.50,   "in",      737.09)]    // 1475 lb
    [InlineData("pitts-s2b-like",           600.00,   86.35,   90.50,   "in",      737.09)]    // "1475 lb or less"
    [InlineData("pitts-s2b-like",           703.07,   87.965,  90.50,   "in",      737.09)]    // straight line, half way
    [InlineData("j3-cub-like",              553.38,   10.6,    22.7,    "in",      553.38)]
    // A-691 Rev 34 section V (J3C-65S) with EDO 60-1320 floats at its 1300 lb gross — the float this aeroplane
    // wears. Its aft limit is 3.7 in TIGHTER than the landplane's +22.7, which is why the landplane's envelope
    // cannot be copied across, and the certificate quotes three different seaplane grosses by float model
    // (1160 McKinley / 1200 Edo, Wollam, Heath / 1300 Edo 60-1320, Edo 92-1400, Wollam 1200).
    [InlineData("j3-cub-floats-like",       589.67,   10.6,    19.0,    "in",      589.67)]
    [InlineData("great-lakes-like",         816.47,   20.0,    23.8,    "in",      816.47)]
    [InlineData("glider-swift-s1-like",     410.0,    257,     414,     "mm",      410.0)]
    // G2EA Rev 2 covers the 2-33, 2-33A and 2-33AK at 1040 lb; the Class II Utility limits +11.86/+19.76 are the
    // envelope authored here. The datum is the wing root leading edge, confirmed by the manual's own stations:
    // 78.20 - 11.86 = 86.10 - 19.76 = 66.34, two numbers that had to agree and did.
    [InlineData("glider-2-33-like",          471.74,   11.86,   19.76,   "in",      471.74)]
    [InlineData("glider-grob103-acro-like", 600.0,    270,     480,     "mm",      600.0)]
    public void TheVerifiedEnvelopesAreTheCertificatesOwnNumbers(string id, double kg, double fwdArm, double aftArm, string unit, double grossKg)
    {
        var load = new FlyingGame.Core.AircraftLoad(Real(id));
        Assert.True(load.HasCgEnvelope, $"{id} has no envelope");
        Assert.True(load.HasCgDatum, $"{id} has no datum");
        var wb = WeightBalance.From(load, units: unit == "mm" ? UnitSystem.Metric : UnitSystem.Standard);
        (double f, double a) = load.CgLimitsAt(kg);
        double tol = unit == "mm" ? 0.1 : 0.01;
        Assert.InRange(wb.ArmValue(f), fwdArm - tol, fwdArm + tol);
        Assert.InRange(wb.ArmValue(a), aftArm - tol, aftArm + tol);
        Assert.Equal(grossKg, wb.MaxGrossKg, 2);
        // And each type as authored sits inside its own certificate, takeoff and zero fuel (the check that validates a
        // datum mapping, docs/WEIGHT-AND-BALANCE.md).
        Assert.True(wb.InLimits, $"{id} as authored: {string.Join(", ", wb.Details)}");
    }

    /// <summary>Exactly these types have a verified CG envelope. A new one is a deliberate edit here, with its source;
    /// a type left out shows "No envelope authored" and no invented box (Ty t880u: never invent aircraft limits).</summary>
    [Fact]
    public void OnlyTheVerifiedTypesHaveAnEnvelope()
    {
        string dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "TestData");
        var withEnvelope = System.IO.Directory.GetFiles(dir, "*.json")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Where(id => new FlyingGame.Core.AircraftLoad(Real(id!)).HasCgEnvelope)
            .OrderBy(id => id, System.StringComparer.Ordinal).ToArray();
        // j3-cub-floats-like joined the list 2026-11-11, from A-691 Rev 34's own J3C-65S / Edo 60-1320 rows.
        // glider-2-33-like joined the same day, from G2EA Rev 2. It is the game's default aeroplane and had
        // carried no gross weight, no datum and no CG limits at all.
        Assert.Equal(new[] { "glider-2-33-like", "glider-grob103-acro-like", "glider-swift-s1-like", "great-lakes-like", "j3-cub-floats-like", "j3-cub-like", "pitts-s2b-like" }, withEnvelope);

        foreach (string id in new[] { "c172-like", "extra-300-like" })
        {
            var wb = WeightBalance.From(new FlyingGame.Core.AircraftLoad(Real(id)));
            Assert.False(wb.HasEnvelope, id);
            Assert.Empty(wb.Envelope);
            Assert.DoesNotContain(wb.Limits, l => l.Label != WeightBalance.MaxGrossLineLabel);
        }
    }

    private static AircraftConfig Real(string id) =>
        FlyingGame.Core.DataContracts.AircraftConfigLoader.LoadFromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "TestData", id + ".json"));

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "configs"))) d = d.Parent;
        Assert.True(d != null, "could not find the repo root above " + System.AppContext.BaseDirectory);
        return d!.FullName;
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
        // The configs author gross as maxTakeoffMassKg (J-3: 553.38); this float file authors none, which is the
        // case the 1.2 factor exists for.
        System.Func<string, AircraftConfig?> cfg = id => id == "j3-cub-like" ? Tandem(gross: 0, takeoff: 553.38) : Tandem(gross: 0);
        var f = NewFlow(cfg: cfg);
        f.Pick(PickerKind.Aircraft, "j3-cub-like");
        Assert.Equal(553.38, f.WeightBalance!.MaxGrossKg, 6);
        f.Pick(PickerKind.Configuration, "floats");
        Assert.Equal(664.056, f.WeightBalance!.MaxGrossKg, 6);
        Assert.Equal(664.056, f.GrossOverrideKg(), 6);
    }

    /// <summary>...but a float config that carries its OWN certificate placard keeps it (2026-11-11). The 1.2 factor
    /// is a stand-in for a figure nobody has looked up, and on the real J-3 it is not close: it makes 664.06 kg =
    /// 1,464 lb where FAA A-691 Rev 34 gives 1,300 lb on Edo 60-1320 floats. Overriding an authored, sourced number
    /// would make it unreadable, which is the trap loading.emptyCg was.</summary>
    [Fact]
    public void AFloatConfigWithItsOwnCertificateGrossIsNotOverriddenByTheFactor()
    {
        System.Func<string, AircraftConfig?> cfg = id => id.StartsWith("j3-cub") ? Real(id) : Tandem();
        var f = NewFlow(cfg: cfg);
        f.Pick(PickerKind.Aircraft, "j3-cub-like");
        Assert.Equal(553.38, f.WeightBalance!.MaxGrossKg, 2);          // 1220 lb, the landplane certificate
        f.Pick(PickerKind.Configuration, "floats");
        Assert.Equal(0.0, f.GrossOverrideKg(), 9);                     // no factor: the float file speaks for itself
        Assert.Equal(589.67, f.WeightBalance!.MaxGrossKg, 2);          // 1300 lb, not 1.2 x 1220 = 1464
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
        var final = f.Items(PickerKind.Start).Single(i => i.Id == "OnFinal");
        Assert.Equal("loc/pitts-s2b-like/standard/valley/OnFinal", final.ThumbnailKey);
        var mesa = f.Items(PickerKind.Location).Single(i => i.Id == "MESA");
        Assert.Equal("loc/pitts-s2b-like/standard/mesa/InTheAir", mesa.ThumbnailKey);
    }

    // ---------------------------------------------------------------- the camera (Ty 2026-11-11)

    /// <summary>"We need a viewpoint button like flight path tail fly by cockpit etc": a menu of its own, separate
    /// from START (which is what the button labelled VIEWPOINT had been offering). The four Ty named are at the top
    /// of the list, the default is the view the game has always started in, and the pick survives a launch.</summary>
    [Fact]
    public void TheCameraViewIsItsOwnMenuAndIsRememberedAcrossLaunches()
    {
        var store = new MemoryLandingStore();
        var f = NewFlow(store);
        Assert.Equal(CameraView.RelativeWind, f.CameraView);          // nobody who never opens it sees a change
        Assert.Equal(new[] { "Relative wind", "Cockpit", "Tail", "Fly-by" }, f.Items(PickerKind.CameraView).Take(4).Select(i => i.Title).ToArray());
        Assert.False(f.IsFixed(PickerKind.CameraView));               // no activity owns the camera
        Assert.Single(f.Items(PickerKind.CameraView), i => i.Selected);

        Assert.True(f.Pick(PickerKind.CameraView, "Cockpit"));
        Assert.Equal(CameraView.Cockpit, f.CameraView);
        Assert.Equal("Cockpit", f.CameraViewName);
        Assert.Equal(Viewpoint.InTheAir, f.Viewpoint);                // and it did NOT move the start
        f.Next();
        Assert.True(f.Launch());
        Assert.Equal(CameraView.Cockpit, NewFlow(store).CameraView);
    }

    /// <summary>Direction lock is a toggle in flight, never a start: it freezes whatever direction the camera happens
    /// to point the instant it is engaged, and at the spawn frame that is nothing anyone chose. A value saved by some
    /// other route falls back to the default rather than launching into it.</summary>
    [Fact]
    public void DirectionLockIsNotOfferedAsAStartingCamera()
    {
        var f = NewFlow();
        Assert.DoesNotContain("Locked", f.Items(PickerKind.CameraView).Select(i => i.Id));
        Assert.False(f.Pick(PickerKind.CameraView, "Locked"));
        var store = new MemoryLandingStore();
        store.Values["landing.cameraView"] = "Locked";
        Assert.Equal(CameraView.RelativeWind, NewFlow(store).CameraView);
    }

    /// <summary>A camera view has nothing to photograph, so its card carries no thumbnail key — which is what tells
    /// the view to draw the card as words instead of a placeholder letter.</summary>
    [Fact]
    public void CameraViewCardsCarryNoThumbnail()
    {
        foreach (var it in NewFlow().Items(PickerKind.CameraView))
        {
            Assert.Empty(it.Thumbnails);
            Assert.StartsWith("icon/view/", it.ThumbnailKey);
            Assert.NotEqual("", it.Subtitle);
        }
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
