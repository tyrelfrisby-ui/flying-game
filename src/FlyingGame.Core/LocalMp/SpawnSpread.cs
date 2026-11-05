namespace FlyingGame.Core.LocalMp;

/// <summary>
/// LAN / ONLINE ROOMS: two pilots who press FLY in the same place do not start inside each other (Ty 2026-11-05:
/// "local-network multiplayer with full screen on each device … mostly combat and formation"). Every device spawns at
/// the session's own start, which is the same point for everybody who chose the same field and start, so without
/// this the second pilot to arrive appeared in the first pilot's cockpit.
///
/// No new protocol and no host: each state packet already carries flags, and a pilot's flags now carry how long ago
/// he spawned (<c>NetFlags.a</c>). In the first <see cref="WindowS"/> seconds of a flight, a pilot who finds somebody
/// within <see cref="ClearRadiusM"/> moves himself to the first free formation slot (<see cref="FormationSlot"/>:
/// abreast on the right in the air, in line ahead on a runway or the water), once. Who moves:
///   • the newcomer, when the other has been flying longer than the window (or sends no age: an older build, or the
///     relay of a room that drops it) — the pilot already flying is never moved under his hands;
///   • when both are new, the one whose id sorts LATER (ordinal), so exactly one of the two moves and both devices agree
///     without talking about it.
/// </summary>
public static class SpawnSpread
{
    /// <summary>Only this early in a flight does a pilot move himself.</summary>
    public const double WindowS = 4.0;
    /// <summary>Within this height of each other two aeroplanes count as on top of each other.</summary>
    public const double ClearHeightM = 20.0;
    /// <summary>The most slots tried before giving up (and staying put).</summary>
    public const int MaxSlots = 8;

    /// <summary>Closer than this, centre to centre, and the two are in each other's way: half a formation gap at the tips.</summary>
    public static double ClearRadiusM(double spanM) => Math.Max(1.0, spanM) + 0.5 * FormationSlot.WingtipGapM;

    /// <summary>Another pilot: sim world position (x north, y east, z down) and his flight's age, if he sent one.</summary>
    public readonly struct Other
    {
        public readonly string Id;
        public readonly double X, Y, Z;
        public readonly double? AgeS;
        public Other(string id, double x, double y, double z, double? ageS) { Id = id; X = x; Y = y; Z = z; AgeS = ageS; }
    }

    private static bool Near(double ax, double ay, double az, double bx, double by, double bz, double clear)
    {
        double dx = ax - bx, dy = ay - by;
        return dx * dx + dy * dy < clear * clear && Math.Abs(az - bz) < ClearHeightM;
    }

    /// <summary>Should this pilot move? Only inside his window, only for someone near, and only if it is his turn.</summary>
    public static bool ShouldMove(string selfId, double selfAgeS, double x, double y, double z, IReadOnlyList<Other> others, double clearM)
    {
        if (selfAgeS > WindowS) return false;
        foreach (Other o in others)
        {
            if (!Near(x, y, z, o.X, o.Y, o.Z, clearM)) continue;
            bool otherIsNew = o.AgeS.HasValue && o.AgeS.Value <= WindowS;
            if (!otherIsNew) return true;                                   // he was here first
            if (string.CompareOrdinal(selfId, o.Id) > 0) return true;       // both new: the later id moves
        }
        return false;
    }

    /// <summary>The first slot (1…<see cref="MaxSlots"/>) from the shared start whose point is clear of everybody, or −1.
    /// <paramref name="startX"/>, <paramref name="startY"/>, <paramref name="startZ"/> is where this pilot spawned
    /// (the session's start, the same for all of them).</summary>
    public static int PickSlot(IReadOnlyList<Other> others, double startX, double startY, double startZ, double headingRad,
                               double spanM, double lengthM, bool onSurface, double clearM)
    {
        for (int k = 1; k <= MaxSlots; k++)
        {
            var (dx, dy) = FormationSlot.World(FormationSlot.For(k, spanM, lengthM, onSurface), headingRad);
            double x = startX + dx, y = startY + dy;
            bool free = true;
            foreach (Other o in others) if (Near(x, y, startZ, o.X, o.Y, o.Z, clearM)) { free = false; break; }
            if (free) return k;
        }
        return -1;
    }
}
