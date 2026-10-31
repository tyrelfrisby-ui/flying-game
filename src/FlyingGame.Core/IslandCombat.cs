namespace FlyingGame.Core;

/// <summary>
/// The island's COMBAT ZONES, read from the world Codex authored (<c>world.combat_zones</c>) and changed in no way.
/// Mine because the AI, the spawn hook and the scoring are mine: the spec says so in each zone — "Claude implements
/// explicit HOT-entry targets/drones spawn; COLD never spawns", and of the drone orbit, "owner: Claude opponent/AI
/// logic".
///
/// Why this exists (owner-reported 2026-10-31, "the game freezes after a while but I hear crashing sounds"):
/// <see cref="Combat.CombatZone"/> is a hard-coded box, X 1300..4400 by Y -100..3900, authored for the old world's
/// gorge and waterfront. On the island that box lies across 2,082 m of relief, and it sits 1,523 m from the PHDH
/// start, 575 m from 3U2's and directly over S4's — so the fifteen AI aeroplanes that orbit it were live from the
/// moment of every FLY, flying into mountainsides. The island's real zones are CZ1, CZ2, CZ3 and CZ5, twelve to
/// fifteen kilometres away from that box and mostly out to sea, and no start is inside any of them.
///
/// Three of the four are annular sectors measured from the world origin; CZ5 is a small polygon over Tern Mesa.
/// </summary>
public static class IslandCombat
{
    private const double Feet = 0.3048;

    public readonly struct Zone
    {
        public readonly string Id, Name;
        /// <summary>Floor and ceiling, metres MSL (the spec authors them in feet).</summary>
        public readonly double FloorM, CeilingM;
        /// <summary>The zone's default status. COLD zones never spawn anything (the spec's word).</summary>
        public readonly bool Hot;
        public readonly bool Enabled;
        /// <summary>The authored orbit the zone's drones fly: centre in sim metres, altitude MSL, count.</summary>
        public readonly double OrbitX, OrbitY, OrbitAltM, OrbitRadiusM;
        public readonly int DroneCount;
        /// <summary>An annular sector: true bearings from the origin and radii in metres. Unused when <see cref="IsPolygon"/>.</summary>
        public readonly double Bearing0Deg, Bearing1Deg, Radius0M, Radius1M;
        public readonly bool IsPolygon;
        /// <summary>The zone's authored ground bullseyes: centre in sim metres and radius. Three in each of CZ1-CZ3,
        /// two in CZ5. Codex authors where they are; the hits and the scoring are mine.</summary>
        public readonly (double X, double Y, double RadiusM)[] GroundTargets;
        private readonly IslandPoint[] _polygon;

        public Zone(IslandValue z)
        {
            Id = z.S("id"); Name = z.S("name");
            FloorM = z.N("floor_msl_ft_target") * Feet;
            CeilingM = z.N("ceiling_msl_ft_target") * Feet;
            Hot = z.S("default_status", "HOT") == "HOT";
            Enabled = z["enabled"].Flag(true);
            IslandValue orbit = z["drone_orbit"];
            IslandPoint c = orbit["centre"].Point;
            OrbitX = c.X; OrbitY = c.Y;
            OrbitAltM = orbit.N("altitude_ft_target") * Feet;
            OrbitRadiusM = orbit.N("radius_m_target");
            DroneCount = (int)orbit.N("count");
            GroundTargets = z["gunnery_targets"].Items
                .Select(t => { IslandPoint c = t["centre"].Point; return (c.X, c.Y, t.N("radius_m", 8)); })
                .ToArray();
            _polygon = z["boundary"].Items.Length >= 3 ? z["boundary"].Points : System.Array.Empty<IslandPoint>();
            IsPolygon = _polygon.Length >= 3;
            Bearing0Deg = z["bearings_true_deg"][0].Number();
            Bearing1Deg = z["bearings_true_deg"][1].Number();
            Radius0M = z["radii_km"][0].Number() * 1000;
            Radius1M = z["radii_km"][1].Number() * 1000;
        }

        /// <summary>Horizontally inside the zone. Sim coordinates: X north, Y east, both metres from the origin.</summary>
        public bool Contains(double x, double y)
        {
            if (IsPolygon)
            {
                bool inside = false;
                for (int i = 0, j = _polygon.Length - 1; i < _polygon.Length; j = i++)
                {
                    double xi = _polygon[i].X, yi = _polygon[i].Y, xj = _polygon[j].X, yj = _polygon[j].Y;
                    if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                }
                return inside;
            }
            double r = System.Math.Sqrt(x * x + y * y);
            if (r < Radius0M || r > Radius1M) return false;
            double brg = System.Math.Atan2(y, x) * 180 / System.Math.PI;   // X north, Y east => true bearing
            if (brg < 0) brg += 360;
            double span = Bearing1Deg - Bearing0Deg; if (span < 0) span += 360;
            double off = brg - Bearing0Deg; if (off < 0) off += 360;
            return off <= span;
        }

        /// <summary>Inside the zone's three-dimensional volume: horizontally in, and between floor and ceiling.</summary>
        public bool Contains(MathTypes.Vec3 simPos)
        {
            double altM = -simPos.Z;
            return altM >= FloorM && altM <= CeilingM && Contains(simPos.X, simPos.Y);
        }

        /// <summary>Metres from (x, y) to the zone's orbit centre — the cheap "is the pilot anywhere near" test.</summary>
        public double DistanceToOrbitM(double x, double y)
        {
            double dx = x - OrbitX, dy = y - OrbitY;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private static Zone[]? _zones;

    /// <summary>The zones the world authors, in its own order.</summary>
    public static Zone[] Zones => _zones ??= IslandTerrain.World["combat_zones"].Items.Select(z => new Zone(z)).ToArray();

    /// <summary>The enabled, HOT zone whose volume holds this position, or null. This is the HOT-ENTRY test: the zone's
    /// drones and targets exist while the pilot is in here and at no other time.</summary>
    public static Zone? ZoneAt(MathTypes.Vec3 simPos)
    {
        foreach (Zone z in Zones) if (z.Enabled && z.Hot && z.Contains(simPos)) return z;
        return null;
    }

    /// <summary>The nearest zone's orbit centre, by horizontal distance.</summary>
    public static Zone? Nearest(double x, double y, out double distanceM)
    {
        Zone? best = null; distanceM = double.MaxValue;
        foreach (Zone z in Zones)
        {
            if (!z.Enabled) continue;
            double d = z.DistanceToOrbitM(x, y);
            if (d < distanceM) { distanceM = d; best = z; }
        }
        return best;
    }
}
