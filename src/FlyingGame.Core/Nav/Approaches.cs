namespace FlyingGame.Core.Nav;

/// <summary>What a fix is for. A plate's symbology is this enum.</summary>
public enum FixRole { AirwayFix, Iaf, IntermediateFix, Faf, MissedApproachPoint, MissedApproachHold }

/// <summary>A named point on a procedure, with the lowest altitude you may cross it at.</summary>
public readonly struct ProcedureFix
{
    public readonly string Name;
    public readonly double X, Y;
    public readonly FixRole Role;
    /// <summary>Minimum crossing altitude, feet MSL; 0 where the chart prints none.</summary>
    public readonly double MinAltitudeFt;
    /// <summary>How the fix is identified without GPS: a station overhead, or a radial and a DME distance.</summary>
    public readonly string Identified;

    public ProcedureFix(string name, double x, double y, FixRole role, double minAltitudeFt, string identified)
    { Name = name; X = x; Y = y; Role = role; MinAltitudeFt = minAltitudeFt; Identified = identified; }
}

public enum ApproachKind { Ils, Vor, Ndb }

/// <summary>Which way the procedure turn is flown, seen from the outbound course.</summary>
public enum TurnSide { None, Left, Right }

/// <summary>
/// One published procedure. Everything here is derived from the terrain by <see cref="ApproachGeometry"/>
/// and then written down, the way a plate is printed from a survey.
/// </summary>
public sealed class Approach
{
    public string Title = "";                  // "ILS RWY 27"
    public string Airport = "";
    public string Runway = "";
    public ApproachKind Kind;
    /// <summary>The VOR the procedure is built on — the final approach aid for a VOR approach, and for an
    /// ILS the station that gets you to the localizer.</summary>
    public string VorId = "";
    /// <summary>The beacon this procedure is flown on, for an ADF approach.</summary>
    public string NdbId = "";
    public Ils? Localizer;
    /// <summary>
    /// A circling approach: there is no runway to line up with, so the minimum is a height above everything
    /// within 1.3 NM of the field and you look out of the window for the last bit. Its title ends in a
    /// letter rather than a runway number, which is the convention for saying so.
    /// </summary>
    public bool Circling;
    public double FinalCourseDeg;
    public double TdzeFt;
    /// <summary>Decision altitude for an ILS, minimum descent altitude for a VOR approach. Feet MSL.</summary>
    public double MinimumsFt;
    public double HatFt;
    public ProcedureFix[] Fixes = System.Array.Empty<ProcedureFix>();
    /// <summary>The course reversal, where there is one.</summary>
    public TurnSide ProcedureTurn = TurnSide.None;
    public double ProcedureTurnRemainWithinNm;
    public double ProcedureTurnAltitudeFt;
    public string ProcedureTurnFix = "";
    public double MissedApproachClimbToFt;
    public string MissedApproachText = "";
    /// <summary>Why this procedure looks the way it does — the terrain that shaped it.</summary>
    public string Notes = "";

    public ProcedureFix Fix(FixRole role)
    {
        foreach (ProcedureFix f in Fixes) if (f.Role == role) return f;
        throw new System.InvalidOperationException($"{Title} has no {role}");
    }

    public bool Has(FixRole role)
    {
        foreach (ProcedureFix f in Fixes) if (f.Role == role) return true;
        return false;
    }
}

/// <summary>
/// The published approach procedures. See <see cref="ApproachGeometry"/> for how each number was arrived at
/// and docs/NAVIGATION.md for the whole argument.
/// </summary>
public static class Approaches
{
    /// <summary>
    /// Three ILSs and one VOR approach. Every altitude below was measured off this world's terrain by
    /// <see cref="ApproachGeometry"/> and <see cref="Terps"/> — NavProcedureTests re-derives all of them on
    /// every run and fails if the ground has moved under them.
    ///
    /// WHY ALL THREE ILSs ARE ON RUNWAY 27. Not a choice: the terrain decided it. Every runway end in the
    /// world was evaluated against the 34:1 precision surface, and the answer falls out of the shape of the
    /// place — the land climbs westward in three 2,000 ft steps, so a final approach course of 270 descends
    /// toward a threshold with LOWER ground behind it the whole way, and one of 090 descends toward a
    /// threshold with a 2,000 ft wall behind it:
    ///
    ///   | | 27 (from the low side) | 09 (from the high side) |
    ///   |---|---|---|
    ///   | Valley | 580 ft HAT | 9,580 ft HAT |
    ///   | Bench | 480 ft HAT | 6,420 ft HAT |
    ///   | Mesa | **200 ft HAT** | 3,880 ft HAT |
    ///   | Summit | 1,180 ft HAT | **200 ft HAT** |
    ///
    /// Summit is the exception that proves it: it is the TOP plateau, so its low side is the west, and
    /// runway 09 is the one that works there. The 36/18 runways are worse again, because a 4 NM final along
    /// them runs into the escarpments side-on.
    /// </summary>
    public static readonly Approach[] All =
    {
        new()
        {
            Title = "ILS RWY 27", Airport = "Valley", Runway = "27", Kind = ApproachKind.Ils,
            VorId = "VLY", FinalCourseDeg = 270, TdzeFt = 1000,
            Localizer = new Ils("I-VLY", "Valley", "27", 109.10, 850, 562.5, 304.8, 270, 3.0, true, 1580),
            MinimumsFt = 1580, HatFt = 580,
            Fixes = new[]
            {
                new ProcedureFix("SEALS", 850, 7970, FixRole.Iaf, 3100, "VLY R-087 at 4.3 DME"),
                new ProcedureFix("SEALS", 850, 7970, FixRole.Faf, 2500, "VLY R-087 at 4.3 DME"),
                new ProcedureFix("RW27", 850, 562.5, FixRole.MissedApproachPoint, 1580, "the decision altitude"),
                new ProcedureFix("SEALS", 850, 7970, FixRole.MissedApproachHold, 3100, "VLY R-087 at 4.3 DME"),
            },
            ProcedureTurn = TurnSide.Right, ProcedureTurnRemainWithinNm = 2.0,
            ProcedureTurnAltitudeFt = 3100, ProcedureTurnFix = "SEALS",
            MissedApproachClimbToFt = 3100,
            MissedApproachText = "Climb to 3,100 ft on the localizer, then direct SEALS and hold.",
            Notes = "The only approach here with a course reversal, and the only place in this world there "
                  + "was room for one: it is flown over open water, so the 2.0 NM protected area holds "
                  + "nothing but sea. Arriving on V1 from the west the course change is 180 degrees, which "
                  + "is exactly the case a procedure turn exists for. The decision altitude is 580 ft above "
                  + "touchdown rather than 200 because an island 3.6 NM out on the final stands 948 ft above "
                  + "the field and pushes 380 ft through the 34:1 surface.",
        },

        new()
        {
            Title = "ILS RWY 27", Airport = "Bench", Runway = "27", Kind = ApproachKind.Ils,
            VorId = "RIM", FinalCourseDeg = 270, TdzeFt = 3000,
            Localizer = new Ils("I-BCH", "Bench", "27", 109.90, 850, -2887.5, 914.4, 270, 3.0, true, 3480),
            MinimumsFt = 3480, HatFt = 480,
            Fixes = new[]
            {
                new ProcedureFix("BUOYS", 850, 4520, FixRole.Iaf, 4700, "RIM R-088 at 3.1 DME"),
                new ProcedureFix("BUOYS", 850, 4520, FixRole.Faf, 4300, "RIM R-088 at 3.1 DME"),
                new ProcedureFix("RW27", 850, -2887.5, FixRole.MissedApproachPoint, 3480, "the decision altitude"),
                new ProcedureFix("RIM", 600, -1180, FixRole.MissedApproachHold, 4700, "overhead Rimrock"),
            },
            MissedApproachClimbToFt = 4700,
            MissedApproachText = "Climb to 4,700 ft on the localizer, then direct RIM and hold.",
            Notes = "The obstacle that costs this approach its 200 ft minimum is the hill the Rimrock VOR "
                  + "stands on: the crest reaches 3,472 ft, 472 ft above the Bench field, 0.9 NM from the "
                  + "threshold, and pushes 271 ft through the 34:1 surface. The station that gets you here is "
                  + "on the thing in your way, which is worth knowing about and is why it is charted.",
        },

        new()
        {
            Title = "ILS RWY 27", Airport = "Mesa", Runway = "27", Kind = ApproachKind.Ils,
            VorId = "RIM", FinalCourseDeg = 270, TdzeFt = 5000,
            Localizer = new Ils("I-MSA", "Mesa", "27", 110.30, 850, -6187.5, 1524.0, 270, 3.0, true, 5200),
            MinimumsFt = 5200, HatFt = 200,
            Fixes = new[]
            {
                new ProcedureFix("SHELF", 850, 1220, FixRole.Iaf, 4000, "RIM R-084 at 1.3 DME"),
                new ProcedureFix("SHELF", 850, 1220, FixRole.Faf, 6300, "RIM R-084 at 1.3 DME"),
                new ProcedureFix("RW27", 850, -6187.5, FixRole.MissedApproachPoint, 5200, "the decision altitude"),
                new ProcedureFix("CRS", 400, -7920, FixRole.MissedApproachHold, 10700, "overhead Crestline"),
            },
            MissedApproachClimbToFt = 10700,
            MissedApproachText = "Climb to 10,700 ft on the localizer, then direct CRS and hold.",
            Notes = "The clean one: NOTHING penetrates the 34:1 surface on this final anywhere in ten miles, "
                  + "so the decision altitude is the Category I 200 ft above touchdown. It is clean because "
                  + "the whole approach is flown over the plateau staircase BELOW the field — at the final "
                  + "approach fix you are 5,274 ft above ground, and the land climbs two 2,000 ft steps to "
                  + "come up and meet you. It also passes within half a mile of the Valley airport at "
                  + "5,955 ft, which is worth a look on the way past. Straight in off V1 westbound, no "
                  + "course reversal: the airway and the final approach course are the same 270.",
        },

        new()
        {
            Title = "VOR RWY 36", Airport = "Summit", Runway = "36", Kind = ApproachKind.Vor,
            VorId = "SUM", FinalCourseDeg = 360, TdzeFt = 7000,
            MinimumsFt = 7260, HatFt = 260,
            Fixes = new[]
            {
                new ProcedureFix("SUM", 400, -10050, FixRole.Iaf, 8800, "overhead Summit"),
                new ProcedureFix("CEDAR", -5880, -10050, FixRole.Faf, 7600, "SUM R-180 at 3.4 DME"),
                new ProcedureFix("RW36", -1250, -10050, FixRole.MissedApproachPoint, 7260, "SUM R-180 at 0.9 DME"),
                new ProcedureFix("SUM", 400, -10050, FixRole.MissedApproachHold, 8800, "overhead Summit"),
            },
            MissedApproachClimbToFt = 8800,
            MissedApproachText = "Climb to 8,800 ft on the 180 radial outbound, then direct SUM and hold.",
            Notes = "The final approach course is the SUM 180 radial flown inbound on 360, and the Summit VOR "
                  + "sits exactly on the main runway's centreline, so the radial IS the centreline. "
                  + "NO COURSE REVERSAL IS PUBLISHED, and that is the terrain's decision rather than an "
                  + "omission: a procedure turn within 2.0 NM of the station would have to be flown at "
                  + "11,500 ft, within 2.5 NM at 14,500 ft, and within 3.0 NM at 18,600 ft, because Summit "
                  + "is ringed by the alpine range to the north and the Frank Church country to the "
                  + "south-west. Arrive established on the 180 radial outbound.",
        },

        // ---- the non-precision plates (owner 2026-10-18: "build a few vor and adf approaches to put in the
        // approach plate list ... we need semi realistic IFR capability for future IFR lessons").

        new()
        {
            Title = "VOR RWY 27", Airport = "Mesa", Runway = "27", Kind = ApproachKind.Vor,
            VorId = "RIM", FinalCourseDeg = 273, TdzeFt = 5000,
            MinimumsFt = 5260, HatFt = 260,
            Fixes = new[]
            {
                new ProcedureFix("SHELF", 850, 1220, FixRole.Iaf, 5900, "RIM R-084 at 1.4 DME"),
                new ProcedureFix("RIM", 600, -1180, FixRole.Faf, 5900, "overhead Rimrock"),
                new ProcedureFix("RW27", 850, -6187.5, FixRole.MissedApproachPoint, 5260, "2.7 NM from RIM, timed"),
                new ProcedureFix("CRS", 400, -7920, FixRole.MissedApproachHold, 10700, "overhead Crestline"),
            },
            MissedApproachClimbToFt = 10700,
            MissedApproachText = "Climb to 10,700 ft on R-273, then direct CRS and hold.",
            Notes = "The same runway as the Mesa ILS and deliberately so: flying a precision and a "
                  + "non-precision approach to one piece of pavement, and seeing 200 ft of decision height "
                  + "against 260 ft of minimum descent altitude and a step down instead of a glideslope, is "
                  + "the comparison an IFR lesson is built on. The final approach course is the Rimrock VOR's "
                  + "273 radial flown OUTBOUND, which passes within 250 m of the Mesa 27 threshold and is "
                  + "2.9 degrees off the runway — inside the 30 degrees a VOR final is allowed. Rimrock "
                  + "itself is the final approach fix, 2.7 NM out, which is what makes this work without any "
                  + "DME at all: station passage IS the fix.",
        },

        new()
        {
            Title = "NDB RWY 27", Airport = "Bench", Runway = "27", Kind = ApproachKind.Ndb,
            VorId = "RIM", NdbId = "BN", FinalCourseDeg = 270, TdzeFt = 3000,
            MinimumsFt = 3760, HatFt = 760,
            Fixes = new[]
            {
                new ProcedureFix("RIM", 600, -1180, FixRole.Iaf, 4500, "overhead Rimrock"),
                new ProcedureFix("BN", 850, 1742, FixRole.Faf, 4000, "overhead the Bench beacon, 353 kHz"),
                new ProcedureFix("RW27", 850, -2887.5, FixRole.MissedApproachPoint, 3760, "2.5 NM from BN, timed"),
                new ProcedureFix("RIM", 600, -1180, FixRole.MissedApproachHold, 4700, "overhead Rimrock"),
            },
            MissedApproachClimbToFt = 4700,
            MissedApproachText = "Climb to 4,700 ft on 270, then direct RIM and hold.",
            Notes = "An ADF approach, and the reason there is a beacon at all. The Bench beacon sits on the "
                  + "Valley plateau 2.5 NM out on the final — a thousand feet BELOW the field it serves, "
                  + "which is fine for a beacon and would be useless for a VOR, because a low-frequency "
                  + "signal follows the ground instead of needing to see over the wall. From the beacon you "
                  + "fly 270 and time it: the ground comes up 2,000 ft underneath you in two and a half "
                  + "miles, so the whole final is flown nearly level at 4,000 ft to a minimum of 3,760, and "
                  + "the 760 ft of height above touchdown is the escarpment's crest, not the runway's.",
        },

        new()
        {
            Title = "NDB RWY 09", Airport = "Summit", Runway = "09", Kind = ApproachKind.Ndb,
            VorId = "SUM", NdbId = "SM", FinalCourseDeg = 90, TdzeFt = 7000,
            MinimumsFt = 7260, HatFt = 260,
            Fixes = new[]
            {
                new ProcedureFix("CABIN", 850, -14316, FixRole.Iaf, 7700, "SUM R-276 at 2.3 DME"),
                new ProcedureFix("CABIN", 850, -14316, FixRole.Faf, 7700, "SUM R-276 at 2.3 DME"),
                new ProcedureFix("RW09", 850, -10612.5, FixRole.MissedApproachPoint, 7260, "2.0 NM from CABIN, timed"),
                new ProcedureFix("SUM", 400, -10050, FixRole.MissedApproachHold, 10700, "overhead Summit"),
            },
            MissedApproachClimbToFt = 10700,
            MissedApproachText = "Climb to 10,700 ft on 090, then direct SUM and hold.",
            Notes = "Homing to the Summit beacon on 090, which sits 488 m past the far end of runway 09, so "
                  + "the needle sits on the nose the whole way down and station passage is the roll-out. "
                  + "STRAIGHT IN ONLY, established inbound: there is no way to join this from overhead the "
                  + "field, because a course reversal within 2 NM of the station would have to be flown at "
                  + "11,500 ft and you cannot lose 3,000 ft in the 2.3 NM from the station to the fix. That "
                  + "is what a mountain plate reads like, and the restriction is checked by a test rather "
                  + "than asserted in a note. The 260 ft minimum is the lowest of any non-precision approach "
                  + "here, because the Frank Church country west of Summit lies BELOW the plateau.",
        },
    };

    public static Approach? ForAirport(string airport)
    {
        foreach (Approach a in All) if (a.Airport == airport) return a;
        return null;
    }

    public static List<Approach> AllForAirport(string airport)
    {
        var l = new List<Approach>();
        foreach (Approach a in All) if (a.Airport == airport) l.Add(a);
        return l;
    }
}
