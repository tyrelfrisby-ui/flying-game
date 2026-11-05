using FlyingGame.Sim.Ratings;

namespace FlyingGame.Sim.Jobs;

/// <summary>What kind of flying a job is.</summary>
public enum JobKind { Bush, Float, Backcountry, AirMail, Airline }

/// <summary>
/// How a job is navigated. THE LIST IS THE RULE: there is no GPS, no moving map, no own-ship marker and no
/// distance-to-waypoint here, and there never will be (owner, absolute). A job is flown by looking out of the
/// window, by the paper map, by the compass and the clock, and by the simulated ground stations.
/// </summary>
public enum NavMeans { Pilotage, PaperMap, CompassAndClock, FourCourseRange, Ndb, Vor, Dme }

/// <summary>
/// A job: carry something from one field to another in a particular kind of aeroplane, once the pilot is
/// good enough to be trusted with it.
///
/// THESE ARE STUBS (owner 2026-11-01: "job-scenario stubs … no big content"). Each one names its aeroplane,
/// its two fields, how it is navigated and what level it needs, and nothing flies them yet — there is no
/// runner, no cargo, no pay. They exist so the progression has somewhere to lead after Commercial and so the
/// aeroplanes the jobs need are on the record as gaps. No job is military, and none ever needs to know where
/// the aeroplane is: arrival is landing on the destination field, which the field itself can tell.
/// </summary>
public sealed class JobScenario
{
    public string Id = "";
    public string Title = "";
    public JobKind Kind;
    /// <summary>Any one of these. A placeholder makes the job "coming soon".</summary>
    public string[] Aircraft = Array.Empty<string>();
    /// <summary>Airport ids from the island data (IslandTerrain.World["airports"]).</summary>
    public string From = "", To = "";
    public NavMeans[] Navigation = Array.Empty<NavMeans>();
    /// <summary>The level the pilot must hold.</summary>
    public CertLevel MinLevel = CertLevel.Pilot;
    /// <summary>Jobs that must have been flown first (the airline track: the DC-3 before the 737-200).</summary>
    public string[] RequiresJobs = Array.Empty<string>();
    /// <summary>What the player is told before the job.</summary>
    public string Brief = "";
}

public static class Jobs
{
    public static readonly JobScenario[] All =
    {
        new()
        {
            Id = "backcountry-supply", Title = "Backcountry supply run", Kind = JobKind.Backcountry,
            Aircraft = new[] { "pa18-bush-like" }, From = "KMYL", To = "3U2",
            Navigation = new[] { NavMeans.Pilotage, NavMeans.PaperMap, NavMeans.CompassAndClock },
            MinLevel = CertLevel.Pilot,
            Brief = "Groceries and a spare part for the backcountry strip. Find it from the paper map and the shape of the ground; look at the strip before you commit to it.",
        },
        new()
        {
            Id = "bush-mail-cub", Title = "Bush mail by Cub", Kind = JobKind.Bush,
            Aircraft = new[] { "pa18-cub-like", "j3-cub-like" }, From = "PATK", To = "PANC",
            Navigation = new[] { NavMeans.Pilotage, NavMeans.PaperMap },
            MinLevel = CertLevel.Pilot,
            Brief = "A sack of letters to the city field. Map in your lap, landmarks out of the window.",
        },
        new()
        {
            Id = "float-freight-beaver", Title = "Float freight to the outer island", Kind = JobKind.Float,
            Aircraft = new[] { "dhc2-beaver-floats-like" }, From = "PALH", To = "SITX",
            Navigation = new[] { NavMeans.Pilotage, NavMeans.CompassAndClock, NavMeans.Ndb },
            MinLevel = CertLevel.Pilot,
            Brief = "Drums of fuel to the waterfront. Over open water, hold the compass heading and the clock until the island comes up.",
        },
        new()
        {
            Id = "float-hop-super-cub", Title = "Float hop to the anchorage", Kind = JobKind.Float,
            Aircraft = new[] { "pa18-floats-like", "j3-cub-floats-like" }, From = "PALH", To = "KITE",
            Navigation = new[] { NavMeans.Pilotage, NavMeans.PaperMap },
            MinLevel = CertLevel.Pilot,
            Brief = "One passenger and a cooler. Pick the landing lane by the wind on the water.",
        },
        new()
        {
            Id = "air-mail-beech18", Title = "Night air mail", Kind = JobKind.AirMail,
            Aircraft = new[] { "beech18-like" }, From = "PANC", To = "PATK",
            Navigation = new[] { NavMeans.FourCourseRange, NavMeans.Ndb, NavMeans.CompassAndClock },
            MinLevel = CertLevel.Instrument,
            Brief = "The mail goes through. Fly the range leg by ear, and time the let-down from station passage.",
        },
        new()
        {
            Id = "airline-dc3", Title = "Airline: the DC-3", Kind = JobKind.Airline,
            Aircraft = new[] { "dc3-like" }, From = "KSNA", To = "KMEV",
            Navigation = new[] { NavMeans.Vor, NavMeans.Ndb, NavMeans.Dme, NavMeans.CompassAndClock },
            MinLevel = CertLevel.Commercial,
            Brief = "Twenty-one passengers over the mountains. Radial out, radial in, and DME to tell you when to start down.",
        },
        new()
        {
            Id = "airline-737-200", Title = "Airline: the 737-200", Kind = JobKind.Airline,
            Aircraft = new[] { "boeing-737-200-like" }, From = "KSNA", To = "PANC",
            Navigation = new[] { NavMeans.Vor, NavMeans.Dme, NavMeans.Ndb },
            MinLevel = CertLevel.Commercial, RequiresJobs = new[] { "airline-dc3" },
            Brief = "Steam gauges and a jet. The same radials as the DC-3, twice as fast, so be ahead of it.",
        },
    };

    public static JobScenario? ById(string id)
    {
        foreach (JobScenario j in All) if (j.Id == id) return j;
        return null;
    }

    /// <summary>A job whose every aeroplane is a placeholder cannot be flown yet; menus show it as coming soon.</summary>
    public static bool ComingSoon(JobScenario j)
    {
        foreach (string ac in j.Aircraft) if (Hangar.IsPickable(ac)) return false;
        return true;
    }

    /// <summary>Is the pilot good enough for this job? (Whether it can be flown at all is <see cref="ComingSoon"/>.)</summary>
    public static bool Open(JobScenario j, CertLevel level, ICollection<string> jobsFlown)
    {
        if (level < j.MinLevel) return false;
        foreach (string r in j.RequiresJobs) if (!jobsFlown.Contains(r)) return false;
        return true;
    }
}
