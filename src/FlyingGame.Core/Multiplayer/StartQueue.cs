using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Multiplayer;

/// <summary>
/// Another pilot as a start decision sees them: where they are (sim world, NED — x north, y east, z DOWN, so an
/// altitude MSL is −z), how they are moving, how old that report is, and — if they have pressed FLY but are not
/// flying yet — which start they are waiting for and since when.
///
/// There are three states a pilot can be in and the difference matters to every rule below:
///  • FLYING (<see cref="HoldingFor"/> null). An aeroplane, at Position, moving at Velocity.
///  • WAITING FOR A FIXED START (<see cref="HoldingFor"/> = that start's <see cref="StartPoint.Key"/>). They are
///    not anywhere at all: frozen, hidden from the others, holding for a runway or a box. Position is meaningless
///    and must not be used as traffic — their START POINT is what has to be kept clear, which rule 4 of
///    <see cref="RandomAirStart"/> does.
///  • WAITING WITH A RANDOM START ALREADY CLAIMED (<see cref="HoldingFor"/> = <see cref="StartQueue.RandomClaim"/>).
///    They are not there yet either, but they have committed to being there within a second or two, and Position
///    and Velocity are that claim. This is the state that closes the hole two simultaneous random starts would
///    otherwise leave (see <see cref="StartQueue.RandomClaim"/>).
/// </summary>
public readonly struct Traffic
{
    public readonly string Id;
    public readonly Vec3 Position, Velocity;
    /// <summary>Seconds since this report was received. A stale report is widened by speed × age before use.</summary>
    public readonly double AgeS;
    /// <summary>The <see cref="StartPoint.Key"/> this pilot is queued for, <see cref="StartQueue.RandomClaim"/> if
    /// they are holding with a random start claimed, or null when they are flying.</summary>
    public readonly string? HoldingFor;
    /// <summary>When they joined that queue or made that claim, on a clock every pilot shares (Unix time). The queue
    /// and the claim tie-break both order on this, so it has to come from the same clock for everybody — which is
    /// why it is Unix time and not a per-device stopwatch.</summary>
    public readonly double HoldingSinceS;

    public Traffic(string id, Vec3 position, Vec3 velocity, double ageS = 0, string? holdingFor = null, double holdingSinceS = 0)
    {
        Id = id; Position = position; Velocity = velocity; AgeS = Math.Max(0, ageS);
        HoldingFor = string.IsNullOrEmpty(holdingFor) ? null : holdingFor; HoldingSinceS = holdingSinceS;
    }

    /// <summary>Flying now.</summary>
    public bool Airborne => HoldingFor == null;

    /// <summary>Holding, with a random start already claimed at <see cref="Position"/>.</summary>
    public bool HasRandomClaim => HoldingFor == StartQueue.RandomClaim;

    /// <summary>Is, or is about to be, physically at <see cref="Position"/> — so <see cref="Position"/> and
    /// <see cref="Velocity"/> mean something. False for a pilot queued for a fixed start, who is nowhere.</summary>
    public bool OccupiesSpace => Airborne || HasRandomClaim;
}

/// <summary>A FIXED start that one pilot uses at a time: a runway threshold, a point on final, an aerobatic box, an
/// authored in-air start. Its zone is a vertical cylinder; anybody flying inside it occupies it.</summary>
public sealed class StartPoint
{
    public enum Kind { Runway, Final, AeroBox, AirStart }

    public string Key { get; }
    public string Name { get; }
    public Kind Type { get; }
    public double X { get; }
    public double Y { get; }
    /// <summary>Middle of the zone, m MSL.</summary>
    public double AltitudeM { get; }
    public double RadiusM { get; }
    public double HalfHeightM { get; }
    /// <summary>How long one pilot holds the start, start to clear of the zone (see <see cref="StartQueue"/>).</summary>
    public double ServiceS { get; }

    public StartPoint(string key, string name, Kind type, double x, double y, double altitudeM, double radiusM, double halfHeightM, double serviceS)
    {
        Key = key; Name = name; Type = type; X = x; Y = y; AltitudeM = altitudeM;
        RadiusM = radiusM; HalfHeightM = halfHeightM; ServiceS = serviceS;
    }

    // ---- The zone and the service time for each kind, and where each number comes from ----
    //
    // A zone is not a separation standard. It answers one question: "is somebody still USING this start?" — so it
    // is sized to the piece of air a pilot occupies while getting away from the start, and the service time to how
    // long that takes. The separation standards are a different problem and live in RandomAirStart.
    //
    //  • RUNWAY — 400 m round the threshold, 200 ft (60 m) above it; 60 s. A light aeroplane in this fleet rolls
    //    250–400 m in 20–40 s and is through 200 ft inside a minute, so the cylinder contains anybody rolling,
    //    lifting off, or sitting on the numbers. MEASURED against the fleet: the shortest take-off roll here is the
    //    J-3 (~120 m) and the longest the DC-3 and H-4, which is why the cylinder is the roll of the longest rather
    //    than an average.
    //  • FINAL — 600 m round the 300 ft final start, ±300 ft (90 m); 45 s. At the 30–40 m/s approach speeds of this
    //    fleet, 600 m is ~15–20 s to fly through, doubled to 45 s for the slow types (the 2-33 approaches at 22 m/s
    //    and a paraglider at 10).
    //  • AEROBATIC BOX — the box itself: 600 m radius from its centre, which covers the corners of the 1,000 m
    //    square (half-diagonal 707 m, so 600 m is slightly inside the corners and that is deliberate — a performer
    //    at the very corner is leaving, not working). Floor 100 m to ceiling 1,067 m AGL, which are AeroBox's own
    //    numbers (328 ft / 3,500 ft, the IAC box). 240 s, because a competition sequence is 3–5 minutes and the
    //    box belongs to ONE performer for the whole of it — this is the one start where sharing is not a near miss
    //    but a wrecked sequence.
    //  • AIR START — 1,500 m round the authored point, ±1,000 ft (300 m); 30 s. An air start leaves at cruise,
    //    50–80 m/s for the powered types, so 1,500 m is ~20–30 s.
    //
    // REJECTED: making the runway zone the whole 2,400 m runway. It is one runway shared by both ends and the
    // crosswind strip crosses it, so the whole-runway zone made two pilots at opposite thresholds queue behind each
    // other for no reason. The threshold cylinder is what a pilot actually occupies.
    public static StartPoint Runway(string key, string name, double x, double y, double groundM) =>
        new(key, name, Kind.Runway, x, y, groundM + 30, 400, 30, 60);
    public static StartPoint Final(string key, string name, double x, double y, double altitudeM) =>
        new(key, name, Kind.Final, x, y, altitudeM, 600, 90, 45);
    public static StartPoint Box(string key, string name, double cx, double cy, double groundM) =>
        new(key, name, Kind.AeroBox, cx, cy, groundM + (AeroBox.FloorAglM + AeroBox.CeilingAglM) / 2, 600, (AeroBox.CeilingAglM - AeroBox.FloorAglM) / 2, 240);
    public static StartPoint Air(string key, string name, double x, double y, double altitudeM) =>
        new(key, name, Kind.AirStart, x, y, altitudeM, 1500, 300, 30);
}

/// <summary>
/// The QUEUE for fixed starts. Owner 2026-11-03, verbatim: <i>"multiplayer starts: ground starts and designated
/// start points (airports, aerobatic boxes, etc.) use a queue, and each fixed location shows its estimated wait
/// time"</i>.
///
/// Pure, and it takes the other pilots as a plain list, so it is the same model whether that list comes from the
/// relay's roster, the LAN mesh or a test. There IS a live session layer now (NetSession, over either transport),
/// so the list is real in a room today — but nothing here knows that.
///
/// A start is free when nobody is flying in its zone AND nobody queued for it is ahead of you. Queue order is
/// first come, first served on <see cref="Traffic.HoldingSinceS"/>, ties broken by pilot id, so EVERY CLIENT
/// COMPUTES THE SAME ORDER FROM THE SAME ROSTER and no server has to referee it. That matters: the relay is a
/// dumb fan-out that forwards the state flags untouched, and adding a referee to it would mean a server change,
/// a deploy, and a build that cannot fly against the shipped relay.
/// </summary>
public static class StartQueue
{
    /// <summary>
    /// The <see cref="Traffic.HoldingFor"/> value of a pilot who is holding with a RANDOM start already chosen.
    /// The leading `*` cannot collide with a real <see cref="StartPoint.Key"/> (those are built from field and
    /// runway identifiers, which are letters and digits).
    ///
    /// WHY THIS EXISTS. Two pilots who press FLY within the same second both hold, both pick a random start, and
    /// neither is traffic to the other — a pilot waiting for a fixed start is nowhere, so the obvious reading of
    /// "ignore pilots who are not flying" lets two random starts land on top of each other. That is precisely the
    /// collision the owner asked to prevent ("so multiplayer players don't start at the same spot and collide"),
    /// so a random start, once chosen, is PUBLISHED as a claim in the state the client already sends, and every
    /// other chooser treats it as traffic.
    ///
    /// The simultaneous case is then resolved with no referee by the same rule the queue uses: a claim binds only
    /// against choosers who came AFTER it (<see cref="Before"/> on holding time, ties to the lower id). The
    /// earlier claimant keeps its start; the later one re-picks around it. Both clients reach that conclusion from
    /// the same roster, so they cannot disagree about who yields.
    /// </summary>
    public const string RandomClaim = "*random";

    public readonly struct Status
    {
        /// <summary>Pilots flying in the zone now.</summary>
        public readonly int Occupants;
        /// <summary>Pilots queued for this start ahead of you (all of them, if you are not queued).</summary>
        public readonly int Ahead;
        public readonly double WaitS;
        public Status(int occupants, int ahead, double waitS) { Occupants = occupants; Ahead = ahead; WaitS = waitS; }
        /// <summary>1 = next to go.</summary>
        public int Position => Ahead + 1;
        public bool Clear => Occupants == 0 && Ahead == 0;
    }

    /// <summary>Is this pilot inside the start's zone (horizontal radius and vertical band)? A pilot queued for a
    /// fixed start is nowhere and never occupies anything; a random claim does, because they will be there.</summary>
    public static bool Occupies(StartPoint p, in Traffic t)
    {
        if (!t.OccupiesSpace) return false;
        double dx = t.Position.X - p.X, dy = t.Position.Y - p.Y;
        return dx * dx + dy * dy <= p.RadiusM * p.RadiusM && Math.Abs(-t.Position.Z - p.AltitudeM) <= p.HalfHeightM;
    }

    /// <summary>
    /// Where you stand for this start. `selfId` / `selfSinceS`: you, if you are queued — leave them out (the
    /// default +∞ puts everybody ahead of you) and you get the view from the START PAGE, which is the honest one
    /// before you have joined the queue at all: "this is the wait if you pick this now".
    /// </summary>
    public static Status For(StartPoint p, IReadOnlyList<Traffic> traffic, string? selfId = null, double selfSinceS = double.PositiveInfinity)
    {
        int occ = 0, ahead = 0;
        foreach (Traffic t in traffic)
        {
            if (t.Id == selfId) continue;
            if (Occupies(p, t)) occ++;
            else if (t.HoldingFor == p.Key && Before(t.HoldingSinceS, t.Id, selfSinceS, selfId)) ahead++;
        }
        // The pilot in the zone is, on average, half way through their turn; a second pilot somehow in the same
        // zone has a full turn to go; each pilot queued ahead has a full turn. Deliberately an OVER-estimate at the
        // margin (ceil in WaitText, a full turn rather than half for the extras): a wait that comes good early is
        // a pleasant surprise and a wait that overruns is a bug report.
        double wait = (occ > 0 ? p.ServiceS / 2 + (occ - 1) * p.ServiceS : 0) + ahead * p.ServiceS;
        return new Status(occ, ahead, wait);
    }

    /// <summary>First come first served; the same time goes to the lower id (ordinal), so every client agrees.
    /// A null `idB` is "me, not queued yet", which loses every tie — I am behind anybody already waiting.</summary>
    public static bool Before(double sinceA, string idA, double sinceB, string? idB) =>
        sinceA < sinceB || (sinceA == sinceB && string.CompareOrdinal(idA, idB ?? "") < 0);

    /// <summary>"no wait", "~40 s", "~3 min" — rounded UP, never promising sooner than the model says.</summary>
    public static string WaitText(double waitS)
    {
        if (waitS <= 0) return "no wait";
        if (waitS < 60) return $"~{Math.Ceiling(waitS / 10) * 10:0} s";
        return $"~{Math.Ceiling(waitS / 60):0} min";
    }
}
