namespace FlyingGame.Core.Landing;

// LANDING PAGE v2 (Ty 2026-11-05 05:48, docs/LANDING-V2.md): "it is better to have two or three simpler pages you have
// to step through than one very difficult page". These are the plain types the flow and every view of it share. Nothing
// here knows about Unity or IMGUI: the iPhone/iPad/Mac page (Bridge/Landing/LandingView), the Quest panel
// (Bridge/QuestMenu) and the tests all read the same state.

/// <summary>The pages you step through, in order. Page 1 picks the aeroplane and where it starts; page 2 loads it and sets
/// the conditions; LAUNCH is on page 2.</summary>
public enum LandingPage { Choose = 0, Load = 1 }

/// <summary>The thumbnail menus that open over page 1 (one at a time). <see cref="Challenge"/> and <see cref="Lesson"/>
/// are the right-hand column's lists.</summary>
public enum PickerKind { None, Aircraft, Configuration, Location, Viewpoint, Challenge, Lesson }

/// <summary>Free flight, or one of the two right-hand lists. A challenge or lesson restricts the left-hand pickers
/// (<see cref="ActivityRules"/>).</summary>
public enum ActivityKind { FreeFlight, Challenge, Lesson }

/// <summary>What the aeroplane stands on. Floats / amphibian / hull are their own configs (their own physics); the
/// bushwheel and wheel-ski options on a type without its own variant are the session flags the sim already reads
/// (SessionSettings.Bushwheels / Skis).</summary>
public enum GearKind { Standard, Bushwheels, WheelSkis, Floats, Amphibian, Hull, FootLaunch }

/// <summary>Where the aeroplane begins. SAME NAMES AND ORDER as Bridge SessionSettings.Start (an EditMode test holds
/// that), so the two convert by value.</summary>
public enum Viewpoint { InTheAir, OnTheRunway, OnFinal, InAeroBox, InCombatZone, InThermal }

/// <summary>Who sits in a seat. <see cref="Me"/> is the player (exactly one seat). <see cref="DefaultPerson"/> is a
/// stock occupant who only weighs something; <see cref="InvitedPlayer"/> is another player (a stub until the invite
/// transport exists: it weighs as a default person and is listed on the launch summary).</summary>
public enum Occupant { Empty, Me, DefaultPerson, InvitedPlayer }

/// <summary>What an occupant other than me may do: ride along, or have the other set of controls (a CFI or a pilot
/// friend). Only a seat with its own controls offers <see cref="DualControl"/>.</summary>
public enum SeatRole { Passenger, DualControl }

/// <summary>Time of day presets. <see cref="Light"/> in <see cref="LandingCatalog.TimesOfDay"/> is the 0..1 brightness
/// the page uses for its light/dark feedback.</summary>
public enum TimeOfDay { Morning, Midday, Afternoon, Sunset }

/// <summary>Engine reliability presets: a multiplier on FlyingGame.Core.Engine.EngineReliability's standard rate
/// (0 = never fails).</summary>
public enum Reliability { NeverFails, Realistic, Tired, Training }

/// <summary>One card in a thumbnail menu. <see cref="ThumbnailKey"/> is what the thumbnail pipeline
/// (<see cref="ThumbnailKeys"/>) looks up; <see cref="Enabled"/> false keeps the card visible but greyed with
/// <see cref="DisabledReason"/> (a challenge restricts it, for example).</summary>
public sealed class PickerItem
{
    public PickerKind Kind;
    public string Id = "";
    public string Title = "";
    public string Subtitle = "";
    /// <summary>The picture to show, most specific key first (<see cref="ThumbnailKeys"/>): the view shows the first
    /// that exists, else its icon (<see cref="IconKey"/>).</summary>
    public List<string> Thumbnails = new();
    public string IconKey = "";
    public string ThumbnailKey => Thumbnails.Count > 0 ? Thumbnails[0] : IconKey;
    public bool Selected;
    public bool Enabled = true;
    public string? DisabledReason;
    public override string ToString() => $"{Id}{(Selected ? "*" : "")}{(Enabled ? "" : " (off)")}";
}

/// <summary>An aeroplane in the aircraft menu: a TYPE. Its variants on floats / bushwheels are its
/// <see cref="Configs"/>, not separate cards.</summary>
public sealed class AirframeFamily
{
    public string Id = "";                  // the standard variant's aircraft id
    public string Name = "";
    public List<GearConfig> Configs = new();
    public bool IsGlider;                   // no engine (sailplanes, hang glider, paraglider)
    public GearConfig Standard => Configs[0];
}

/// <summary>One configuration of a type: which config file flies (<see cref="AircraftId"/>) and the two session gear
/// flags that go with it.</summary>
public sealed class GearConfig
{
    public string Id = "";                  // "standard", "bushwheels", "wheel-skis", "floats", "amphibian", "hull", "foot"
    public GearKind Kind;
    public string Name = "";
    public string AircraftId = "";
    public bool Bushwheels;
    public bool Skis;
}

/// <summary>A place on the island to start from (WorldTerrain.Airports, by index).</summary>
public sealed class LocationInfo
{
    public int Index;
    public string Id = "";
    public string Name = "";
    public double ElevationFt;
}

/// <summary>A challenge or lesson as the right-hand column offers it.</summary>
public sealed class ActivityInfo
{
    public string Id = "";
    public string Name = "";
    public ActivityKind Kind;
}
