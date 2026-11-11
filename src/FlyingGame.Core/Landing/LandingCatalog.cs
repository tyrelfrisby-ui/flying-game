namespace FlyingGame.Core.Landing;

/// <summary>
/// Everything the landing flow can offer, built once from the game's own lists (the fleet, the airports, the challenges
/// and lessons) so the flow itself never reaches into Bridge statics and a test can build one from literals.
///
/// AIRCRAFT ARE TYPES, CONFIGURATIONS ARE THEIR VARIANTS (Ty 2026-11-05: "airplanes tires wheel ski ski floats etc"). The
/// fleet lists "Super Cub-like", "Super Cub-like, Bushwheels" and "Super Cub-like on Floats" as three aeroplanes; here
/// they are one card in the aircraft menu and three cards in its configuration menu. A variant that has its own config
/// file (floats, the PA-18's bushwheels, the AirCam amphibian) flies that file. A powered landplane without one gets the
/// generic options the sim already models on any airframe: bushwheels and wheel-skis (SessionSettings.Bushwheels /
/// Skis). Gliders, the hang glider and the paraglider have one configuration.
/// </summary>
public sealed class LandingCatalog
{
    /// <summary>Variant aircraft id → (the type it belongs to, the gear it stands on). Literal, because which file is
    /// whose float version is a fact about the fleet, not something to infer from a name.</summary>
    public static readonly IReadOnlyDictionary<string, (string family, GearKind gear)> Variants = new Dictionary<string, (string, GearKind)>
    {
        ["pa18-bush-like"] = ("pa18-cub-like", GearKind.Bushwheels),
        ["pa18-floats-like"] = ("pa18-cub-like", GearKind.Floats),
        ["j3-cub-floats-like"] = ("j3-cub-like", GearKind.Floats),
        ["dhc2-beaver-floats-like"] = ("dhc2-beaver-like", GearKind.Floats),
        ["aircam-amphib-like"] = ("aircam-like", GearKind.Amphibian),
    };

    /// <summary>Types that stand on nothing the gear options apply to.</summary>
    public static readonly IReadOnlyDictionary<string, GearKind> FixedGear = new Dictionary<string, GearKind>
    {
        ["hughes-h4-like"] = GearKind.Hull,
        ["paraglider-en-b-like"] = GearKind.FootLaunch,
        ["hang-glider-like"] = GearKind.FootLaunch,
    };

    /// <summary>Ty 2026-11-05 addendum: "Floatplanes always get a higher gross weight of about 20% compared to wheels."
    /// Applies to the weight-and-balance placard only (a warning, never a block, never physics).</summary>
    public const double FloatGrossFactor = 1.2;

    /// <summary>The first-time aeroplane (Ty: "for first timers the 2.33"). The yellow SGS 2-33, CLAUDE.md.</summary>
    public const string FirstTimeAircraftId = "glider-2-33-like";

    /// <summary>LESSONS ARE FLOWN IN TRAINERS ("much more limited than free play"). Literal list; Ty to confirm (Q queue).</summary>
    public static readonly string[] LessonTrainers =
    {
        "glider-2-33-like", "glider-grob103-acro-like", "j3-cub-like", "pa18-cub-like", "c172-like", "pa28-archer-like", "decathlon-8kcab-like",
    };

    /// <summary>The aerobatic box is for aerobatic types.</summary>
    public static readonly string[] Aerobats =
    {
        "pitts-s2b-like", "extra-300-like", "decathlon-8kcab-like", "great-lakes-like", "stearman-pt17-like", "glider-swift-s1-like",
        "glider-grob103-acro-like", "cassutt-f1-like", "p51d-like",
    };

    public static readonly (Viewpoint v, string name, string blurb)[] Viewpoints =
    {
        (Viewpoint.InTheAir, "In the air", "2,000 ft over the field, trimmed"),
        (Viewpoint.OnTheRunway, "On the runway", "At the threshold, engine idling"),
        (Viewpoint.OnFinal, "On final", "300 ft on final, trimmed at best glide"),
        (Viewpoint.InAeroBox, "Aerobatic box", "Running in to the box, 2,300 ft"),
        (Viewpoint.InCombatZone, "Combat zone", "Entering the zone, guns hot"),
        (Viewpoint.InThermal, "In a thermal", "Circling at min sink, trimmed"),
    };

    /// <summary>
    /// THE CAMERA VIEWS page 1 offers (Ty 2026-11-11: "we need a viewpoint button like flight path tail fly by cockpit
    /// etc"). The names are the game's own — ChaseCamera.ViewNames, the list the in-flight VIEW button already cycles —
    /// copied here because Core never sees Bridge; an EditMode test holds the two in step. Nothing was invented: a
    /// second vocabulary for the same cameras would mean the landing page and the VIEW button disagreed about what the
    /// pilot is looking through.
    ///
    /// The blurbs say where the camera IS, because that is the only thing the picture on the card cannot (there are no
    /// camera thumbnails, and a card with no picture is all words). They are drawn from ChaseCamera's own notes.
    /// ChaseCamera.View.Locked is deliberately absent — see <see cref="CameraView"/>.
    ///
    /// MENU ORDER, not enum order: the four Ty named ("flight path tail fly by cockpit") come first, then the fixed
    /// rig views. The enum keeps ChaseCamera's order because that is what converts by value; a menu is read top to
    /// bottom by somebody choosing, and the four he named are the four anyone chooses.
    /// </summary>
    public static readonly (CameraView v, string name, string blurb)[] CameraViews =
    {
        (CameraView.RelativeWind, "Relative wind", "The flight path: behind you, looking where you are really going"),
        (CameraView.Cockpit, "Cockpit", "From the seat, out through the glass"),
        (CameraView.Tail, "Tail", "On the fin, looking forward over the aeroplane"),
        (CameraView.Flyby, "Fly-by", "Parked ahead and off to one side; you fly past it"),
        (CameraView.SideLeft, "Left side", "Alongside, bolted to the airframe"),
        (CameraView.SideRight, "Right side", "Alongside, bolted to the airframe"),
        (CameraView.Front, "Front", "Ahead of the nose, looking back at you"),
        (CameraView.Top, "Top", "Straight down on the wing"),
        (CameraView.Bottom, "Bottom", "Straight up at the belly"),
        (CameraView.RelativeWindAhead, "Relative wind, ahead", "On the flight path, out in front, looking back"),
    };

    /// <summary>Time of day: the sun's local hour and how bright the page shows it (light/dark feedback).</summary>
    public static readonly (TimeOfDay t, string name, double hour, double light)[] TimesOfDay =
    {
        (TimeOfDay.Morning, "Morning", 8.5, 0.70),
        (TimeOfDay.Midday, "Midday", 12.5, 1.00),
        (TimeOfDay.Afternoon, "Afternoon", 16.0, 0.80),
        (TimeOfDay.Sunset, "Sunset", 19.3, 0.35),
    };

    /// <summary>Engine reliability: the multiplier on EngineReliability's standard rate (StandardMtbfHours); 0 never fails.</summary>
    public static readonly (Reliability r, string name, double unreliability, string blurb)[] Reliabilities =
    {
        (Reliability.NeverFails, "Never fails", 0.0, "Nothing breaks"),
        (Reliability.Realistic, "Realistic", 1.0, "A real engine's odds"),
        (Reliability.Tired, "Tired engine", 10.0, "Ten times the odds"),
        (Reliability.Training, "Training", 100.0, "Expect a failure in an hour or two"),
    };

    /// <summary>Weather presets (cards); "custom" is whatever the steppers hold.</summary>
    public static readonly (string id, string name, double windKt, int turbulence, double thermals)[] WeatherPresets =
    {
        ("calm", "Calm", 0, 0, 0.5),
        ("breezy", "Breezy", 10, 1, 1.0),
        ("gusty", "Gusty", 18, 2, 1.0),
        ("soaring", "Soaring day", 6, 1, 2.0),
        ("rough", "Rough", 25, 3, 0.5),
    };

    public readonly List<AirframeFamily> Families = new();
    public readonly List<LocationInfo> Locations = new();
    public readonly List<ActivityInfo> Challenges = new();
    public readonly List<ActivityInfo> Lessons = new();
    private readonly Dictionary<string, ActivityRules> _rules = new();

    /// <param name="fleet">Every flyable aircraft (id, display name), in menu order — SessionSettings.Fleet.</param>
    /// <param name="airports">WorldTerrain.Airports as (id, display name, elevation ft).</param>
    /// <param name="challenges">SessionSettings.Challenges (a null id is free flight and is skipped).</param>
    /// <param name="lessons">SessionSettings.Lessons.</param>
    /// <param name="challengeAircraft">A challenge config's own aircraft (configs/challenges/*.json aircraftId), or null.</param>
    /// <param name="isScored">Contests.IsScored: a scored contest owns the conditions AND the configuration.</param>
    /// <param name="lessonNeedsEngine">SessionSettings.LessonNeedsEngine.</param>
    public LandingCatalog(IEnumerable<(string id, string name)> fleet, IEnumerable<(string id, string name, double elevationFt)> airports,
                          IEnumerable<(string? id, string name)> challenges, IEnumerable<(string id, string name)> lessons,
                          Func<string, string?>? challengeAircraft = null, Func<string, bool>? isScored = null, Func<string, bool>? lessonNeedsEngine = null)
    {
        var fleetList = fleet.ToList();
        var names = fleetList.ToDictionary(f => f.id, f => f.name);
        foreach (var (id, name) in fleetList)
        {
            if (Variants.ContainsKey(id)) continue;            // folded into its type below
            bool glider = id.StartsWith("glider") || id == "hang-glider-like" || id == "paraglider-en-b-like";
            var fam = new AirframeFamily { Id = id, Name = name, IsGlider = glider };
            if (FixedGear.TryGetValue(id, out GearKind fixedKind))
                fam.Configs.Add(new GearConfig { Id = GearId(fixedKind), Kind = fixedKind, Name = GearName(fixedKind), AircraftId = id });
            else
            {
                fam.Configs.Add(new GearConfig { Id = "standard", Kind = GearKind.Standard, Name = glider ? "Standard" : "Standard tires", AircraftId = id });
                if (!glider)
                {
                    // Its own bushwheel file, or the generic big soft tyres.
                    string? ownBush = OwnVariant(fleetList, id, GearKind.Bushwheels);
                    fam.Configs.Add(new GearConfig { Id = "bushwheels", Kind = GearKind.Bushwheels, Name = "Bushwheels", AircraftId = ownBush ?? id, Bushwheels = ownBush == null });
                    fam.Configs.Add(new GearConfig { Id = "wheel-skis", Kind = GearKind.WheelSkis, Name = "Wheel-skis", AircraftId = id, Skis = true });
                    foreach (GearKind water in new[] { GearKind.Floats, GearKind.Amphibian })
                    {
                        string? v = OwnVariant(fleetList, id, water);
                        if (v != null) fam.Configs.Add(new GearConfig { Id = GearId(water), Kind = water, Name = GearName(water), AircraftId = v });
                    }
                }
            }
            Families.Add(fam);
        }

        foreach (var (aid, aname, elevFt) in airports)
            Locations.Add(new LocationInfo { Index = Locations.Count, Id = aid, Name = aname, ElevationFt = elevFt });

        foreach (var (cid, cname) in challenges)
        {
            if (cid == null) continue;
            Challenges.Add(new ActivityInfo { Id = cid, Name = cname, Kind = ActivityKind.Challenge });
            _rules[cid] = RulesForChallenge(cid, challengeAircraft?.Invoke(cid), isScored?.Invoke(cid) ?? false);
        }
        foreach (var (lid, lname) in lessons)
        {
            Lessons.Add(new ActivityInfo { Id = lid, Name = lname, Kind = ActivityKind.Lesson });
            _rules[lid] = new ActivityRules
            {
                Allows = fam => Array.IndexOf(LessonTrainers, fam.Id) >= 0 && !(fam.IsGlider && (lessonNeedsEngine?.Invoke(lid) ?? false)),
                AircraftNote = "Lessons are flown in a trainer",
                StandardGearOnly = true,
                FixedViewpointNote = "The lesson sets its own start",
            };
        }
    }

    private static string? OwnVariant(List<(string id, string name)> fleet, string family, GearKind kind)
    {
        foreach (var (id, _) in fleet)
            if (Variants.TryGetValue(id, out var v) && v.family == family && v.gear == kind) return id;
        return null;
    }

    public static string GearId(GearKind k) => k switch
    {
        GearKind.Bushwheels => "bushwheels", GearKind.WheelSkis => "wheel-skis", GearKind.Floats => "floats",
        GearKind.Amphibian => "amphibian", GearKind.Hull => "hull", GearKind.FootLaunch => "foot", _ => "standard",
    };

    public static string GearName(GearKind k) => k switch
    {
        GearKind.Bushwheels => "Bushwheels", GearKind.WheelSkis => "Wheel-skis", GearKind.Floats => "Floats",
        GearKind.Amphibian => "Amphibian", GearKind.Hull => "Flying boat hull", GearKind.FootLaunch => "Foot launch", _ => "Standard tires",
    };

    /// <summary>The type an aircraft id belongs to (a variant folds into its standard type), or null if not offered.</summary>
    public AirframeFamily? FamilyOf(string aircraftId)
    {
        string fid = Variants.TryGetValue(aircraftId, out var v) ? v.family : aircraftId;
        return Families.Find(f => f.Id == fid);
    }

    /// <summary>The rules a challenge or lesson puts on the left-hand pickers; free flight (null) allows everything.</summary>
    public ActivityRules RulesFor(string? activityId) =>
        activityId != null && _rules.TryGetValue(activityId, out var r) ? r : ActivityRules.FreeFlight;

    public ActivityInfo? Activity(string? id) =>
        id == null ? null : Challenges.Find(a => a.Id == id) ?? Lessons.Find(a => a.Id == id);

    private static bool Powered(AirframeFamily f) => !f.IsGlider;
    private static bool PoweredLandplane(AirframeFamily f) => !f.IsGlider && f.Id != "hughes-h4-like";

    private static ActivityRules RulesForChallenge(string id, string? fixedAircraft, bool scored)
    {
        var r = new ActivityRules { StandardGearOnly = scored, ConditionsLocked = scored };
        if (fixedAircraft != null)
        {
            r.Allows = f => f.Id == fixedAircraft;
            r.AircraftNote = "This challenge is flown in its own aeroplane";
            r.FixedViewpointNote = "The challenge sets its own start";
            r.FixedLocation = true;
            return r;
        }
        switch (id)
        {
            case "event:race": r.Allows = PoweredLandplane; r.AircraftNote = "Powered aeroplanes race"; r.FixedViewpointNote = "800 m short of gate 1"; break;
            case "event:stol": r.Allows = PoweredLandplane; r.AircraftNote = "Powered aeroplanes"; r.FixedViewpointNote = "On a 1.2 km final for the gravel strip"; break;
            case "event:dust": r.Allows = PoweredLandplane; r.AircraftNote = "Powered aeroplanes"; r.FixedViewpointNote = "Lined up on the first pass"; break;
            case "event:aerobox": r.Allows = f => Array.IndexOf(Aerobats, f.Id) >= 0; r.AircraftNote = "Aerobatic types"; r.FixedViewpoint = Viewpoint.InAeroBox; break;
            case "event:combat": r.Allows = Powered; r.AircraftNote = "Powered aeroplanes"; r.FixedViewpoint = Viewpoint.InCombatZone; break;
            case "event:dogfight": r.Allows = PoweredLandplane; r.AircraftNote = "Powered aeroplanes"; r.FixedViewpointNote = "Head-on, 1,500 ft over the zone"; r.FixedLocation = true; break;
            default: r.Allows = Powered; break;
        }
        return r;
    }
}

/// <summary>What a challenge or lesson restricts: which types may fly it, whether the gear is fixed to standard, and
/// whether the start is the activity's own (the viewpoint / location pickers then show the fixed start and nothing else).</summary>
public sealed class ActivityRules
{
    public static readonly ActivityRules FreeFlight = new();
    public Func<AirframeFamily, bool> Allows = _ => true;
    public string? AircraftNote;
    public bool StandardGearOnly;
    public bool ConditionsLocked;
    /// <summary>A fixed viewpoint the pilot cannot change (the aerobatic box, the combat zone).</summary>
    public Viewpoint? FixedViewpoint;
    /// <summary>The activity has a start of its own that is not one of the <see cref="Viewpoint"/>s (it is described here).</summary>
    public string? FixedViewpointNote;
    /// <summary>The activity happens in one place; the location picker shows only the current one.</summary>
    public bool FixedLocation;
    public bool ViewpointLocked => FixedViewpoint.HasValue || FixedViewpointNote != null;
}
