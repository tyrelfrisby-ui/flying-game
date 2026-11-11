using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Landing;

/// <summary>Where the flow keeps what it remembers between launches (PlayerPrefs on a device, a dictionary in a test).</summary>
public interface ILandingStore
{
    string? Get(string key);
    void Set(string key, string value);
}

public sealed class MemoryLandingStore : ILandingStore
{
    public readonly Dictionary<string, string> Values = new();
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string value) => Values[key] = value;
}

/// <summary>One seat on page 2.</summary>
public sealed class SeatRow
{
    public string SeatId = "";
    public string Label = "";
    /// <summary>This seat has its own set of controls (a cockpit seat with an authored view point), so an invited
    /// player may sit in it as a CFI or a pilot friend.</summary>
    public bool HasControls;
    public Occupant Occupant;
    public SeatRole Role;
    public double Kg;
    public double MaxKg;
    public string? InviteName;
    public string IconKey => ThumbnailKeys.Icon("seat", Occupant == Occupant.InvitedPlayer ? (Role == SeatRole.DualControl ? "invite-dual" : "invite-passenger") : Occupant.ToString());
}

/// <summary>A fuel tank or a baggage station on page 2.</summary>
public sealed class StationRow
{
    public string StationId = "";
    public string Label = "";
    public AircraftLoad.StationKind Kind;
    public double Kg, MaxKg;
    public double Fraction => MaxKg > 0 ? Kg / MaxKg : 0;
}

/// <summary>
/// THE LANDING FLOW (Ty 2026-11-05 05:48, docs/LANDING-V2.md). The state machine and every view-model the pages bind to.
/// One instance drives the iPhone / iPad / Mac page and the Quest panel alike; nothing here draws.
///
///  PAGE 1, CHOOSE: the hero (the last-flown aeroplane; the 2-33 the first time), five menus in the stack (Aircraft,
///    Configuration, Location, Start, Camera view) and the activity pair (Challenges, Lessons). Picking a challenge or
///    lesson RESTRICTS the stack's menus (<see cref="ActivityRules"/>); picking Free flight lifts that.
///  PAGE 2, LOAD: the seats (me, a default person, or an invited player as passenger or on the other controls), the
///    fuel and the baggage, the weight-and-balance plot (warn, never block), and the settings: reliability, weather,
///    time of day, units (Standard | Metric, <see cref="DisplayUnits"/>). LAUNCH.
///
/// Every change bumps <see cref="Version"/>, which is what the live 3-D preview and the views watch.
/// <see cref="Launch"/> remembers the choices (the aeroplane becomes the next session's hero) and raises
/// <see cref="Launched"/>; the Bridge applies the selection to the game and flies.
/// </summary>
public sealed class LandingFlow
{
    public const double DefaultPersonKg = 77.0;   // FAA AC 120-27F standard adult, 170 lb
    /// <summary>My weight when I move to a seat that had no reference mass: the fleet's reference pilot (86 kg, 190 lb).</summary>
    public const double DefaultPilotKg = 86.0;

    public readonly LandingCatalog Catalog;
    private readonly ILandingStore _store;
    private readonly Func<string, AircraftConfig?> _configFor;
    private readonly Dictionary<string, AircraftLoad?> _loads = new();

    public LandingPage Page { get; private set; } = LandingPage.Choose;
    public PickerKind OpenPicker { get; private set; } = PickerKind.None;
    public int Version { get; private set; }

    // ---- the selection ----
    public string FamilyId { get; private set; }
    public string GearId { get; private set; } = "standard";
    public int LocationIndex { get; private set; }
    /// <summary>Where the flight BEGINS (<see cref="PickerKind.Start"/>). Not the camera: that is <see cref="CameraView"/>.</summary>
    public Viewpoint Viewpoint { get; private set; } = Viewpoint.InTheAir;
    /// <summary>Which camera the flight starts in (<see cref="PickerKind.CameraView"/>). The default is the one the
    /// game has always started in, so nobody who never opens the menu sees a change.</summary>
    public CameraView CameraView { get; private set; } = CameraView.RelativeWind;
    public string? ActivityId { get; private set; }
    public TimeOfDay TimeOfDay { get; private set; } = TimeOfDay.Midday;
    public Reliability Reliability { get; private set; } = Reliability.NeverFails;
    public string WeatherId { get; private set; } = "custom";   // "custom" = the conditions as they stand (no change for anyone upgrading)
    public double PilotKg { get; private set; } = DefaultPilotKg;
    /// <summary>How the pages show weights, heights and winds (display only; every number here stays SI).</summary>
    public UnitSystem Units { get; private set; } = DisplayUnits.Default;
    public string? InviteName { get; private set; }

    public readonly List<SeatRow> Seats = new();
    public readonly List<StationRow> Stations = new();

    public event Action? Changed;
    public event Action<LandingFlow>? Launched;

    /// <param name="configFor">Loads an aircraft config by id (null if it cannot): the seats, tanks and placards come from it.</param>
    public LandingFlow(LandingCatalog catalog, ILandingStore store, Func<string, AircraftConfig?> configFor)
    {
        Catalog = catalog; _store = store; _configFor = configFor;
        FamilyId = LandingCatalog.FirstTimeAircraftId;
        Restore();
    }

    // ================================================================ derived selection

    public AirframeFamily Family => Catalog.Families.Find(f => f.Id == FamilyId) ?? Catalog.Families[0];
    public GearConfig Gear => Family.Configs.Find(c => c.Id == GearId) ?? Family.Standard;
    /// <summary>The config file that will fly.</summary>
    public string AircraftId => Gear.AircraftId;
    public LocationInfo? Location => Catalog.Locations.Count == 0 ? null : Catalog.Locations[Math.Clamp(LocationIndex, 0, Catalog.Locations.Count - 1)];
    public ActivityRules Rules => Catalog.RulesFor(ActivityId);
    public ActivityInfo? Activity => Catalog.Activity(ActivityId);
    public ActivityKind ActivityKind => Activity?.Kind ?? ActivityKind.FreeFlight;
    public string LocationId => Location?.Id ?? "none";

    public string HeroTitle => Family.Name;
    public string HeroSubtitle =>
        $"{Gear.Name}  ·  {Location?.Name ?? ""}  ·  {ViewpointName}  ·  {Activity?.Name ?? "Free flight"}";
    public List<string> HeroThumbnails => ThumbnailKeys.AircraftFallbacks(Family.Id, GearId).ToList();

    public string ViewpointName =>
        Rules.FixedViewpointNote ?? LandingCatalog.Viewpoints.First(v => v.v == Viewpoint).name;

    public string CameraViewName => LandingCatalog.CameraViews.First(v => v.v == CameraView).name;

    // ================================================================ navigation

    public bool CanGoBack => Page == LandingPage.Load;
    public bool CanGoNext => Page == LandingPage.Choose;
    public bool CanLaunch => Page == LandingPage.Load;   // W&B never blocks (Ty 2026-11-05 addendum)

    public void Next() { if (CanGoNext) { OpenPicker = PickerKind.None; Page = LandingPage.Load; RebuildLoad(); Bump(); } }
    public void Back() { if (LaunchConfirmPending) { LaunchConfirmPending = false; Bump(); return; } if (OpenPicker != PickerKind.None) ClosePicker(); else if (CanGoBack) { Page = LandingPage.Choose; Bump(); } }
    public void GoTo(LandingPage p) { OpenPicker = PickerKind.None; LaunchConfirmPending = false; Page = p; if (p == LandingPage.Load) RebuildLoad(); Bump(); }

    public void Open(PickerKind k)
    {
        if (Page != LandingPage.Choose && k != PickerKind.None) return;   // the menus belong to page 1
        OpenPicker = OpenPicker == k ? PickerKind.None : k;
        Bump();
    }
    public void ClosePicker() { if (OpenPicker != PickerKind.None) { OpenPicker = PickerKind.None; Bump(); } }

    /// <summary>A picker that would offer exactly one card says so, and the view shows it as a fixed line rather than
    /// a menu (the gear of a glider, the viewpoint a lesson sets).</summary>
    public bool IsFixed(PickerKind k) => k switch
    {
        PickerKind.Configuration => Items(k).Count <= 1,
        PickerKind.Start => Rules.ViewpointLocked,
        PickerKind.Location => Rules.FixedLocation || Catalog.Locations.Count <= 1,
        // The camera is never fixed by an activity: what you look THROUGH is not part of any contest. (A side-view
        // practice drill points its own camera at the runway once it begins; that is the drill's camera, not a
        // restriction on the menu, and the drill puts it back.)
        _ => false,
    };

    // ================================================================ page 1 pickers

    public List<PickerItem> Items(PickerKind k)
    {
        var list = new List<PickerItem>();
        var rules = Rules;
        switch (k)
        {
            case PickerKind.Aircraft:
                foreach (var f in Catalog.Families)
                {
                    if (!rules.Allows(f)) continue;   // restricted lists: a lesson offers its trainers only
                    list.Add(new PickerItem
                    {
                        Kind = k, Id = f.Id, Title = f.Name, Selected = f.Id == FamilyId,
                        Subtitle = f.Configs.Count > 1 ? string.Join(" · ", f.Configs.Select(c => c.Name)) : f.Standard.Name,
                        Thumbnails = ThumbnailKeys.AircraftFallbacks(f.Id, "standard").ToList(), IconKey = ThumbnailKeys.Icon("aircraft", f.IsGlider ? "glider" : "powered"),
                    });
                }
                break;
            case PickerKind.Configuration:
                foreach (var c in Family.Configs)
                {
                    bool ok = !rules.StandardGearOnly || c.Kind == GearKind.Standard || Family.Configs.Count == 1;
                    if (!ok) continue;
                    list.Add(new PickerItem
                    {
                        Kind = k, Id = c.Id, Title = c.Name, Selected = c.Id == GearId, Subtitle = GearBlurb(c.Kind),
                        Thumbnails = ThumbnailKeys.AircraftFallbacks(Family.Id, c.Id).ToList(), IconKey = ThumbnailKeys.Icon("gear", c.Id),
                    });
                }
                break;
            case PickerKind.Location:
                foreach (var l in Catalog.Locations)
                {
                    if (rules.FixedLocation && l.Index != LocationIndex) continue;
                    list.Add(new PickerItem
                    {
                        Kind = k, Id = l.Id, Title = l.Name, Subtitle = DisplayUnits.Elevation(l.ElevationFt * DisplayUnits.MPerFt, Units), Selected = l.Index == LocationIndex,
                        Thumbnails = ThumbnailKeys.LocationFallbacks(Family.Id, GearId, l.Id, Viewpoint).ToList(), IconKey = ThumbnailKeys.Icon("location", "airport"),
                    });
                }
                break;
            case PickerKind.Start:
                foreach (var (v, name, blurb) in LandingCatalog.Viewpoints)
                {
                    if (rules.FixedViewpoint.HasValue && v != rules.FixedViewpoint) continue;
                    if (rules.FixedViewpointNote != null && v != Viewpoint) continue;
                    list.Add(new PickerItem
                    {
                        Kind = k, Id = v.ToString(), Title = rules.FixedViewpointNote ?? name, Subtitle = rules.FixedViewpointNote != null ? "set by " + (ActivityKind == ActivityKind.Lesson ? "the lesson" : "the challenge") : blurb,
                        Selected = v == Viewpoint, Enabled = !rules.ViewpointLocked || v == Viewpoint,
                        Thumbnails = ThumbnailKeys.LocationFallbacks(Family.Id, GearId, LocationId, v).ToList(), IconKey = ThumbnailKeys.Icon("viewpoint", v.ToString()),
                    });
                }
                break;
            case PickerKind.CameraView:
                // No thumbnails: a camera view has nothing to photograph that the hero does not already show, and the
                // card is read as words (the view's name and where the camera sits).
                foreach (var (v, name, blurb) in LandingCatalog.CameraViews)
                    list.Add(new PickerItem { Kind = k, Id = v.ToString(), Title = name, Subtitle = blurb, Selected = v == CameraView, IconKey = ThumbnailKeys.Icon("view", v.ToString()) });
                break;
            case PickerKind.Challenge:
            case PickerKind.Lesson:
                list.Add(new PickerItem { Kind = k, Id = "", Title = "Free flight", Subtitle = "Anything, anywhere", Selected = ActivityId == null, IconKey = ThumbnailKeys.Icon("activity", "free") });
                foreach (var a in k == PickerKind.Challenge ? Catalog.Challenges : Catalog.Lessons)
                {
                    var r = Catalog.RulesFor(a.Id);
                    bool any = Catalog.Families.Any(r.Allows);
                    list.Add(new PickerItem
                    {
                        Kind = k, Id = a.Id, Title = a.Name, Subtitle = r.AircraftNote ?? "", Selected = a.Id == ActivityId,
                        Enabled = any, DisabledReason = any ? null : "No aeroplane can fly it",
                        IconKey = ThumbnailKeys.Icon("activity", a.Id),
                    });
                }
                break;
        }
        return list;
    }

    /// <summary>Pick a card. Closes the menu. Returns false (and changes nothing) for a card that is not offered.</summary>
    public bool Pick(PickerKind k, string id)
    {
        var item = Items(k).Find(i => i.Id == id);
        if (item == null || !item.Enabled) return false;
        switch (k)
        {
            case PickerKind.Aircraft:
                FamilyId = id;
                GearId = Family.Configs.Any(c => c.Id == GearId) ? GearId : "standard";
                break;
            case PickerKind.Configuration: GearId = id; break;
            case PickerKind.Location: LocationIndex = Catalog.Locations.First(l => l.Id == id).Index; break;
            case PickerKind.Start: Viewpoint = (Viewpoint)Enum.Parse(typeof(Viewpoint), id); break;
            case PickerKind.CameraView: CameraView = (CameraView)Enum.Parse(typeof(CameraView), id); break;
            case PickerKind.Challenge:
            case PickerKind.Lesson:
                ActivityId = string.IsNullOrEmpty(id) ? null : id;
                ConformToRules();
                break;
        }
        OpenPicker = PickerKind.None;
        RebuildLoad();
        Bump();
        return true;
    }

    /// <summary>After an activity is picked: move every left-hand choice into what it allows. The aeroplane stays if it
    /// may; otherwise the first allowed type in menu order.</summary>
    private void ConformToRules()
    {
        var r = Rules;
        if (!r.Allows(Family))
        {
            var first = Catalog.Families.Find(f => r.Allows(f));
            if (first != null) FamilyId = first.Id;
        }
        if (r.StandardGearOnly) GearId = "standard";
        if (r.FixedViewpoint.HasValue) Viewpoint = r.FixedViewpoint.Value;
    }

    private static string GearBlurb(GearKind k) => k switch
    {
        GearKind.Standard => "As it comes",
        GearKind.Bushwheels => "Big soft tires for gravel and bars",
        GearKind.WheelSkis => "Lands on the glacier, wheels through on hard ground",
        GearKind.Floats => "Water only, gross weight +20 %",
        GearKind.Amphibian => "Water or runway",
        GearKind.Hull => "A flying boat",
        GearKind.FootLaunch => "Run off a hill",
        _ => "",
    };

    // ================================================================ page 2: people, fuel, baggage

    public AircraftLoad? Load => LoadFor(AircraftId);

    private AircraftLoad? LoadFor(string aircraftId)
    {
        if (_loads.TryGetValue(aircraftId, out var l)) return l;
        AircraftConfig? cfg = null;
        try { cfg = _configFor(aircraftId); } catch (Exception) { }
        _loads[aircraftId] = l = cfg == null ? null : new AircraftLoad(cfg);
        return l;
    }

    /// <summary>Rebuild page 2's rows for the aeroplane now selected, from what was remembered for that type.</summary>
    private void RebuildLoad()
    {
        Seats.Clear(); Stations.Clear();
        var load = Load;
        string ac = AircraftId;
        if (load == null || load.Slots.All(s => s.Kind != AircraftLoad.StationKind.Seat))
            Seats.Add(new SeatRow { SeatId = "pilot", Label = "Pilot", HasControls = true, Occupant = Occupant.Me, Kg = PilotKg, MaxKg = 0 });
        if (load == null) return;
        string? meSeat = _store.Get($"landing.me.{ac}");
        var seatSlots = load.Slots.Where(s => s.Kind == AircraftLoad.StationKind.Seat).ToList();
        if (seatSlots.Count > 0 && (meSeat == null || seatSlots.All(s => s.Id != meSeat))) meSeat = seatSlots[0].Id;   // the primary seat (SeatChoice)
        foreach (var s in load.Slots)
        {
            if (s.Kind == AircraftLoad.StationKind.Seat)
            {
                var row = new SeatRow { SeatId = s.Id, Label = s.Label, HasControls = s.Config.HasEye, MaxKg = s.MaxKg };
                // NEVER TOUCHED = AS AUTHORED. A station nobody has set keeps its reference mass, the loading every book
                // number was tuned at (AircraftLoad's invariant), so opening this page changes nobody's aeroplane: my
                // seat weighs its reference, another seat with a reference mass in it holds a default person of that
                // mass, a tank holds its reference fuel. Only a choice moves anything.
                string kgKey = $"landing.kg.{ac}.{s.Id}";
                if (s.Id == meSeat) { row.Occupant = Occupant.Me; row.Kg = ReadKg(kgKey, s.Config.ReferenceKg); }
                else
                {
                    string? saved = _store.Get($"landing.seat.{ac}.{s.Id}");
                    ParseSeat(saved, row);
                    if (row.Occupant == Occupant.Me) row.Occupant = Occupant.Empty;
                    double kg = ReadKg(kgKey, s.Config.ReferenceKg);
                    if (saved == null && kg > 0) row.Occupant = Occupant.DefaultPerson;   // a load set before v2, or authored
                    row.Kg = row.Occupant == Occupant.Empty ? 0 : kg > 0 ? kg : DefaultPersonKg;
                    if (!row.HasControls) row.Role = SeatRole.Passenger;
                    if (row.Occupant == Occupant.InvitedPlayer) row.InviteName = InviteName;
                }
                Seats.Add(row);
                load.Set(s.Id, row.Kg);
            }
            else
            {
                double kg = ReadKg($"landing.kg.{ac}.{s.Id}", s.Config.ReferenceKg);
                kg = Math.Clamp(kg, 0, s.MaxKg > 0 ? s.MaxKg : kg);
                Stations.Add(new StationRow { StationId = s.Id, Label = s.Label, Kind = s.Kind, Kg = kg, MaxKg = s.MaxKg });
                load.Set(s.Id, kg);
            }
        }
    }

    private double ReadKg(string key, double fallback) =>
        double.TryParse(_store.Get(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static void ParseSeat(string? s, SeatRow row)
    {
        row.Occupant = Occupant.Empty; row.Role = SeatRole.Passenger;
        if (string.IsNullOrEmpty(s)) return;
        string[] p = s!.Split(':');
        if (Enum.TryParse(p[0], out Occupant o)) row.Occupant = o;
        if (p.Length > 1 && Enum.TryParse(p[1], out SeatRole r)) row.Role = r;
    }

    /// <summary>Put someone in a seat. Exactly one seat is <see cref="Occupant.Me"/>: choosing Me for another seat moves
    /// me there (the old seat empties); a seat cannot stop being mine except by my moving. Dual control needs a seat with
    /// controls. Returns false if refused.</summary>
    public bool SetSeat(string seatId, Occupant who, SeatRole role = SeatRole.Passenger)
    {
        var row = Seats.Find(s => s.SeatId == seatId);
        if (row == null) return false;
        if (row.Occupant == Occupant.Me && who != Occupant.Me) return false;
        if (role == SeatRole.DualControl && (!row.HasControls || who != Occupant.InvitedPlayer)) role = SeatRole.Passenger;
        if (who == Occupant.Me)
        {
            foreach (var other in Seats.Where(s => s.Occupant == Occupant.Me && s != row)) { other.Occupant = Occupant.Empty; other.Kg = 0; other.Role = SeatRole.Passenger; }
            row.Kg = PilotKg;
        }
        else if (who == Occupant.Empty) row.Kg = 0;
        else if (row.Occupant == Occupant.Empty || row.Occupant == Occupant.Me) row.Kg = DefaultPersonKg;
        row.Occupant = who; row.Role = role;
        row.InviteName = who == Occupant.InvitedPlayer ? InviteName : null;
        ApplySeatsToLoad();
        Bump();
        return true;
    }

    /// <summary>Cycle a seat's occupant the way one tap on its card does: empty → default person → invited passenger →
    /// invited on the controls (where the seat has them) → empty. My own seat does not cycle.</summary>
    public void CycleSeat(string seatId)
    {
        var row = Seats.Find(s => s.SeatId == seatId);
        if (row == null || row.Occupant == Occupant.Me) return;
        if (row.Occupant == Occupant.Empty) SetSeat(seatId, Occupant.DefaultPerson);
        else if (row.Occupant == Occupant.DefaultPerson) SetSeat(seatId, Occupant.InvitedPlayer, SeatRole.Passenger);
        else if (row.Role == SeatRole.Passenger && row.HasControls) SetSeat(seatId, Occupant.InvitedPlayer, SeatRole.DualControl);
        else SetSeat(seatId, Occupant.Empty);
    }

    public void SetSeatKg(string seatId, double kg)
    {
        var row = Seats.Find(s => s.SeatId == seatId);
        if (row == null || row.Occupant == Occupant.Empty) return;
        kg = Math.Max(20, kg);
        if (row.MaxKg > 0) kg = Math.Min(kg, row.MaxKg);
        row.Kg = kg;
        if (row.Occupant == Occupant.Me) PilotKg = kg;
        ApplySeatsToLoad();
        Bump();
    }

    public void SetInviteName(string? name)
    {
        InviteName = string.IsNullOrWhiteSpace(name) ? null : name;
        foreach (var s in Seats) if (s.Occupant == Occupant.InvitedPlayer) s.InviteName = InviteName;
        Bump();
    }

    public void SetStationKg(string stationId, double kg)
    {
        var row = Stations.Find(s => s.StationId == stationId);
        if (row == null) return;
        row.Kg = Math.Clamp(kg, 0, row.MaxKg > 0 ? row.MaxKg : Math.Max(kg, 0));
        Load?.Set(stationId, row.Kg);
        Bump();
    }

    /// <summary>Every tank to the same fraction of its capacity (Full = 1, Tabs = 0.5).</summary>
    public void SetFuelFraction(double f)
    {
        f = Math.Clamp(f, 0, 1);
        foreach (var r in Stations.Where(s => s.Kind == AircraftLoad.StationKind.Fuel)) { r.Kg = r.MaxKg * f; Load?.Set(r.StationId, r.Kg); }
        Bump();
    }

    public double FuelFraction
    {
        get
        {
            double kg = 0, max = 0;
            foreach (var r in Stations.Where(s => s.Kind == AircraftLoad.StationKind.Fuel)) { kg += r.Kg; max += r.MaxKg; }
            return max > 0 ? kg / max : 0;
        }
    }

    private void ApplySeatsToLoad()
    {
        var load = Load;
        if (load == null) return;
        foreach (var s in Seats) if (load.Find(s.SeatId) != null) load.Set(s.SeatId, s.Kg);
    }

    /// <summary>The page-2 plot. Floats and amphibians take <see cref="LandingCatalog.FloatGrossFactor"/> × the wheel
    /// config's placard when that has one (Ty 2026-11-05 addendum).</summary>
    public WeightBalance? WeightBalance
    {
        get
        {
            var load = Load;
            if (load == null) return null;
            return FlyingGame.Core.Landing.WeightBalance.From(load, GrossOverrideKg(), Units);
        }
    }

    /// <summary>The placard gross the plot uses for this configuration, or 0 for the config's own.
    ///
    /// A FLOAT CONFIG THAT CARRIES ITS OWN PLACARD KEEPS IT. The 1.2 factor is a stand-in for types whose seaplane
    /// gross nobody has looked up, and it is not close: on the J-3 it makes 1,464 lb where FAA A-691 Rev 34 gives
    /// 1,300 lb on Edo 60-1320 floats, 1.066x the landplane's 1,220 and not 1.2x. Once a certificate figure is
    /// authored as the float file's own maxTakeoffMassKg, overriding it would make an authored, sourced number
    /// unreadable — the same trap as loading.emptyCg, which LoadingConfig never read (docs/WEIGHT-AND-BALANCE.md,
    /// 2026-11-11). Today only j3-cub-floats-like authors one; pa18-floats-like, dhc2-beaver-floats-like and
    /// aircam-amphib-like have no loading block at all, so they still take the factor.</summary>
    public double GrossOverrideKg()
    {
        if (Gear.Kind != GearKind.Floats && Gear.Kind != GearKind.Amphibian) return 0;
        if ((Load?.PlacardGrossKg ?? 0) > 0) return 0;      // this configuration's own certificate wins
        var wheels = LoadFor(Family.Standard.AircraftId);
        double g = wheels?.PlacardGrossKg ?? 0;
        return g > 0 ? g * LandingCatalog.FloatGrossFactor : 0;
    }

    // ================================================================ page 2: settings

    public void SetTimeOfDay(TimeOfDay t) { TimeOfDay = t; Bump(); }
    public void SetReliability(Reliability r) { Reliability = r; Bump(); }
    public void SetWeather(string id) { WeatherId = id; Bump(); }

    /// <summary>Standard | Metric. Remembered AT ONCE (not at LAUNCH like the rest): it is how the game talks to this
    /// pilot, not a choice about this flight, so backing out of the page must not lose it.</summary>
    public void SetUnits(UnitSystem u)
    {
        Units = u;
        _store.Set(DisplayUnits.PrefKey, DisplayUnits.ToStored(u));
        Bump();
    }

    /// <summary>A mass in the chosen units ("170 lb" / "77 kg"), and one tap of a − / + weight button, in kg.</summary>
    public string MassText(double kg) => DisplayUnits.Mass(kg, Units);
    public string MassNumber(double kg) => DisplayUnits.MassNumber(kg, Units);
    public string MassUnit => DisplayUnits.MassUnit(Units);
    public double MassStepKg => DisplayUnits.MassStepKg(Units);
    public bool ConditionsLocked => Rules.ConditionsLocked;

    /// <summary>0..1 brightness of the chosen time of day, for the page's light/dark feedback.</summary>
    public double Light => LandingCatalog.TimesOfDay.First(t => t.t == TimeOfDay).light;
    /// <summary>The page's semantic dark theme (LandingVisuals.Theme(night)): sunset and anything darker.</summary>
    public bool IsDark => Light < 0.5;
    public double SunHour => LandingCatalog.TimesOfDay.First(t => t.t == TimeOfDay).hour;
    public double Unreliability => LandingCatalog.Reliabilities.First(r => r.r == Reliability).unreliability;

    public List<PickerItem> TimeItems() => LandingCatalog.TimesOfDay.Select(t => new PickerItem
    { Kind = PickerKind.None, Id = t.t.ToString(), Title = t.name, Subtitle = $"{(int)t.hour}:{(int)((t.hour % 1) * 60):00}", Selected = t.t == TimeOfDay, IconKey = ThumbnailKeys.Icon("time", t.t.ToString()) }).ToList();

    public List<PickerItem> ReliabilityItems() => LandingCatalog.Reliabilities.Select(r => new PickerItem
    { Kind = PickerKind.None, Id = r.r.ToString(), Title = r.name, Subtitle = r.blurb, Selected = r.r == Reliability, IconKey = ThumbnailKeys.Icon("reliability", r.r.ToString()) }).ToList();

    public List<PickerItem> WeatherItems() => LandingCatalog.WeatherPresets.Select(w => new PickerItem
    {
        Kind = PickerKind.None, Id = w.id, Title = w.name, Subtitle = w.windKt > 0 ? DisplayUnits.WindFromKt(w.windKt, Units) : "no wind", Selected = w.id == WeatherId,
        Enabled = !ConditionsLocked, DisabledReason = ConditionsLocked ? "Set by the contest" : null, IconKey = ThumbnailKeys.Icon("weather", w.id),
    }).Concat(new[] { new PickerItem { Kind = PickerKind.None, Id = "custom", Title = "Custom", Subtitle = "your own wind", Selected = WeatherId == "custom", Enabled = !ConditionsLocked, IconKey = ThumbnailKeys.Icon("weather", "custom") } }).ToList();

    public List<PickerItem> UnitItems() => new[] { UnitSystem.Standard, UnitSystem.Metric }.Select(u => new PickerItem
    { Kind = PickerKind.None, Id = u.ToString(), Title = DisplayUnits.Name(u), Subtitle = DisplayUnits.Blurb(u), Selected = u == Units, IconKey = ThumbnailKeys.Icon("units", u.ToString()) }).ToList();

    // ================================================================ launch

    /// <summary>What LAUNCH will do, a line each (the page's summary strip and the Quest panel's last rows).</summary>
    public List<string> Summary()
    {
        var lines = new List<string> { $"{Family.Name}, {Gear.Name}", $"{Location?.Name}, {ViewpointName}, {CameraViewName} view", Activity?.Name ?? "Free flight" };
        var people = Seats.Where(s => s.Occupant != Occupant.Empty).Select(s => s.Occupant switch
        {
            Occupant.Me => $"me ({s.Label})",
            Occupant.DefaultPerson => $"passenger ({s.Label})",
            _ => $"{s.InviteName ?? "invited player"} ({s.Label}, {(s.Role == SeatRole.DualControl ? "on the controls" : "passenger")}) - invite not sent yet",
        });
        lines.Add(string.Join(", ", people));
        var wb = WeightBalance;
        if (wb != null) lines.Add(MassText(wb.TotalKg) + (wb.HasGross ? $" of {MassNumber(wb.MaxGrossKg)}" : "") + (wb.InLimits ? "" : "  -  " + string.Join("; ", wb.Warnings)));
        lines.Add($"{LandingCatalog.TimesOfDay.First(t => t.t == TimeOfDay).name}, {LandingCatalog.WeatherPresets.FirstOrDefault(w => w.id == WeatherId).name ?? "Custom weather"}, {LandingCatalog.Reliabilities.First(r => r.r == Reliability).name.ToLowerInvariant()}");
        return lines;
    }

    /// <summary>The confirmation LAUNCH asks for when the weight and balance is out of limits (Ty t882u, 2026-11-06: this
    /// exact text; it supersedes the t881u draft). Buttons <see cref="WbGoBackLabel"/> | <see cref="WbFlyLabel"/>.</summary>
    public const string WbOutOfLimitsMessage = "Your W&B is out of limits which will negatively affect aircraft handling and performance.";
    public const string WbGoBackLabel = "Go back";
    public const string WbFlyLabel = "Fly";

    public enum LaunchRequest { NotAllowed, Launched, NeedsConfirmation }

    /// <summary>True while the W&amp;B confirmation is up: the page shows <see cref="WbOutOfLimitsMessage"/> and waits for
    /// <see cref="ConfirmLaunch"/>.</summary>
    public bool LaunchConfirmPending { get; private set; }

    /// <summary>What every LAUNCH / FLY button calls. In limits (or no plot) it launches at once; out of limits it raises
    /// <see cref="LaunchConfirmPending"/> and launches nothing until <see cref="ConfirmLaunch"/> says which.</summary>
    public LaunchRequest RequestLaunch()
    {
        if (!CanLaunch) return LaunchRequest.NotAllowed;
        if (WeightBalance is { InLimits: false })
        {
            LaunchConfirmPending = true;
            Bump();
            return LaunchRequest.NeedsConfirmation;
        }
        return Launch() ? LaunchRequest.Launched : LaunchRequest.NotAllowed;
    }

    /// <summary>The confirmation's answer: <paramref name="fly"/> false is "Go back" (back to the loading, nothing
    /// launched), true is "Fly" (launches as loaded). False when no confirmation was pending.</summary>
    public bool ConfirmLaunch(bool fly)
    {
        if (!LaunchConfirmPending) return false;
        LaunchConfirmPending = false;
        if (!fly) { Page = LandingPage.Load; Bump(); return false; }
        Bump();
        return Launch();
    }

    /// <summary>LAUNCH from page 2, unconditionally: the W&amp;B is a question, never a block, and this is the answer
    /// "Fly". Buttons call <see cref="RequestLaunch"/>. Remembers everything, then raises <see cref="Launched"/>.</summary>
    public bool Launch()
    {
        if (!CanLaunch) return false;
        LaunchConfirmPending = false;
        Save();
        Launched?.Invoke(this);
        return true;
    }

    // ================================================================ memory

    private const string K = "landing.";

    private void Save()
    {
        _store.Set(K + "lastFlown", FamilyId);
        _store.Set(K + "gear." + FamilyId, GearId);
        _store.Set(K + "location", LocationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _store.Set(K + "viewpoint", Viewpoint.ToString());      // the START; the key keeps its old name (PickerKind.Start)
        _store.Set(K + "cameraView", CameraView.ToString());
        _store.Set(K + "activity", ActivityId ?? "");
        _store.Set(K + "time", TimeOfDay.ToString());
        _store.Set(K + "reliability", Reliability.ToString());
        _store.Set(K + "weather", WeatherId);
        _store.Set(K + "pilotKg", PilotKg.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _store.Set(DisplayUnits.PrefKey, DisplayUnits.ToStored(Units));
        string ac = AircraftId;
        foreach (var s in Seats)
        {
            if (s.Occupant == Occupant.Me) _store.Set($"landing.me.{ac}", s.SeatId);
            else _store.Set($"landing.seat.{ac}.{s.SeatId}", $"{s.Occupant}:{s.Role}");
            if (s.Occupant != Occupant.Me && s.Occupant != Occupant.Empty) _store.Set($"landing.kg.{ac}.{s.SeatId}", s.Kg.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (var st in Stations) _store.Set($"landing.kg.{ac}.{st.StationId}", st.Kg.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void Restore()
    {
        string? last = _store.Get(K + "lastFlown");
        var fam = last == null ? null : Catalog.FamilyOf(last);
        fam ??= Catalog.FamilyOf(LandingCatalog.FirstTimeAircraftId) ?? (Catalog.Families.Count > 0 ? Catalog.Families[0] : null);
        if (fam == null) throw new InvalidOperationException("the landing catalog has no aircraft");
        FamilyId = fam.Id;
        // A saved variant id ("pa18-floats-like") restores its configuration too.
        if (last != null && LandingCatalog.Variants.TryGetValue(last, out var v)) GearId = LandingCatalog.GearId(v.gear);
        string? gear = _store.Get(K + "gear." + FamilyId);
        if (gear != null && Family.Configs.Any(c => c.Id == gear)) GearId = gear;
        if (Family.Configs.All(c => c.Id != GearId)) GearId = Family.Standard.Id;
        if (int.TryParse(_store.Get(K + "location"), out int li)) LocationIndex = Math.Clamp(li, 0, Math.Max(0, Catalog.Locations.Count - 1));
        if (Enum.TryParse(_store.Get(K + "viewpoint"), out Viewpoint vp)) Viewpoint = vp;
        // A saved "Locked" is not offered any more (it was never offered): fall back to the default rather than
        // launching into a frozen direction nobody picked.
        if (Enum.TryParse(_store.Get(K + "cameraView"), out CameraView cv) && cv != Landing.CameraView.Locked) CameraView = cv;
        string? act = _store.Get(K + "activity");
        ActivityId = string.IsNullOrEmpty(act) || Catalog.Activity(act) == null ? null : act;
        if (Enum.TryParse(_store.Get(K + "time"), out TimeOfDay t)) TimeOfDay = t;
        if (Enum.TryParse(_store.Get(K + "reliability"), out Reliability r)) Reliability = r;
        string? w = _store.Get(K + "weather");
        if (w != null) WeatherId = w;
        PilotKg = ReadKg(K + "pilotKg", DefaultPilotKg);
        Units = DisplayUnits.Parse(_store.Get(DisplayUnits.PrefKey));
        ConformToRules();
        RebuildLoad();
    }

    /// <summary>Take over choices made somewhere else (the classic chapters behind "More settings", which write the
    /// session directly): the type and configuration that file + flags mean, the airport, the start, the activity. Then
    /// the activity's rules, as for any pick. Nothing is saved until LAUNCH.</summary>
    public void Adopt(string aircraftId, bool bushwheels, bool skis, int locationIndex, Viewpoint viewpoint, string? activityId)
    {
        var fam = Catalog.FamilyOf(aircraftId);
        if (fam != null)
        {
            FamilyId = fam.Id;
            var cfg = fam.Configs.FirstOrDefault(c => c.AircraftId == aircraftId && c.Bushwheels == bushwheels && c.Skis == skis)
                      ?? fam.Configs.FirstOrDefault(c => c.AircraftId == aircraftId) ?? fam.Standard;
            GearId = cfg.Id;
        }
        LocationIndex = Math.Clamp(locationIndex, 0, Math.Max(0, Catalog.Locations.Count - 1));
        Viewpoint = viewpoint;
        ActivityId = Catalog.Activity(activityId) != null ? activityId : null;
        ConformToRules();
        RebuildLoad();
        Bump();
    }

    /// <summary>Start the flow over with the aeroplane and choices the store holds (opening the menu again).</summary>
    public void Reopen() { Page = LandingPage.Choose; OpenPicker = PickerKind.None; RebuildLoad(); Bump(); }

    private void Bump() { Version++; Changed?.Invoke(); }
}
