using FlyingGame.Core.MathTypes;
namespace FlyingGame.Core;

/// <summary>
/// THE WINDSOCK (Ty 2026-11-05: "every airport has a wind sock off to the side of the runway at each end and in the
/// middle"). One shared part: this file is its design (dimensions, where it stands, how it moves) and
/// <c>Bridge/Windsock.cs</c> is the only thing that draws it. No airport, builder or GLB carries a sock of its own
/// (<c>WindsockTests.TheSockIsOneSharedPart</c>).
///
/// THE SOCK. FAA AC 150/5345-27E, L-806 size 2: a 12 ft (3.66 m) cone with a 36 in (0.91 m) throat, in five bands
/// (orange, white, orange, white, orange). The specification's two performance points are the whole calibration:
/// it must swing to the wind at 3 kt and stand fully extended at 15 kt. The throat hoop turns on a vertical spindle
/// through its own vertical diameter, 6.1 m (20 ft) above the ground.
///
/// WHERE IT STANDS. Three per runway: abeam each end and abeam the middle (<see cref="Sites"/>), on the terrain's own
/// height, outside the runway safety area, clear of every building, parked aeroplane and other runway.
/// </summary>
public static class WindsockPart
{
    public const double SockLengthM = 3.66, ThroatRadiusM = 0.457, TailRadiusM = 0.18;
    public const int Bands = 5;
    /// <summary>Throat (hoop centre) above the ground the mast stands on.</summary>
    public const double PivotHeightM = 6.1;
    public const double MastRadiusM = 0.05;
    /// <summary>The two points the L-806 specification sets: swings to the wind at 3 kt, fully extended at 15 kt.</summary>
    public const double OrientsMs = 3 * 0.514444, FullExtensionMs = 15 * 0.514444;

    // ---- where it stands ----------------------------------------------------------------------------------------

    /// <summary>The airports: every land runway. The beaches (B1-B6) are landing sites with no airport, the water lanes
    /// have no side to stand on, and Drake's strip runs into a cave. (Big Creek is an airport: the airparks' own
    /// <c>StartsWith("B")</c> test drops it from theirs, which is why this one names the beaches exactly.)</summary>
    public static bool IsAirport(IslandField f) => f.Surface != "water" && f.Field != "DRAKE" && !IsBeach(f);
    private static bool IsBeach(IslandField f) => f.Field.Length >= 2 && f.Field[0] == 'B' && f.Field.Skip(1).All(char.IsDigit);

    /// <summary>Half-width (m) of the runway safety area, FAA AC 150/5300-13B runway design standards (RSA width by design group):
    /// 120 ft (A/B-I small) for runways up to 75 ft wide, 150 ft (B-II) up to 150 ft, 500 ft (C/D) for a paved runway
    /// 150 ft wide or more. Unpaved strips are small-aeroplane strips whatever their width.</summary>
    public static double RsaHalfWidthM(IslandField f)
    {
        double ft = f.Width / IslandTerrain.Feet;
        bool paved = StripFinish.Painted(f.Surface);
        double rsaFt = paved && ft >= 149 ? 500 : ft > 76 ? 150 : 120;
        return rsaFt * IslandTerrain.Feet / 2;
    }
    /// <summary>How far the RSA carries on past each end: 240 ft (A/B-I small), 300 ft (B-II), 1000 ft (C/D).</summary>
    public static double RsaBeyondEndM(IslandField f)
    {
        double half = RsaHalfWidthM(f) / IslandTerrain.Feet;
        return (half > 200 ? 1000 : half > 70 ? 300 : 240) * IslandTerrain.Feet;
    }
    /// <summary>Clearance (m) from the RSA's edge to the mast: a pilot who leaves the safety area still has room
    /// before the sock, and a wingtip over the RSA edge (11 m span) clears it.</summary>
    public const double RsaSetbackM = 12;
    /// <summary>How far in from each threshold the end socks stand: 150 m, or 15 % of a short strip.</summary>
    public static double EndInsetM(IslandField f) => Math.Min(150, .15 * f.Length);
    /// <summary>Clear distance (m) from the mast to any building or parked aeroplane footprint.</summary>
    public const double ObstacleClearM = 15;

    public enum Station { ApproachEnd = 0, Midfield = 1, FarEnd = 2 }

    public readonly struct Site
    {
        public readonly string FieldId; public readonly Station Station; public readonly IslandPoint Point;
        public readonly double GroundM, AlongM, AcrossM;
        public Site(string fieldId, Station station, IslandPoint point, double ground, double along, double across)
        { FieldId = fieldId; Station = station; Point = point; GroundM = ground; AlongM = along; AcrossM = across; }
        public double X => Point.X; public double Y => Point.Y;
        /// <summary>The hoop centre (sim NED, m): where the sock reads the air.</summary>
        public Vec3 Pivot => new(Point.X, Point.Y, -(GroundM + PivotHeightM));
        public string Id => FieldId + ":windsock:" + Station;
    }

    /// <summary>The three socks of one runway. <paramref name="obstacles"/> are the world's blocks (buildings, parked
    /// aeroplanes, everything solid); every other airport runway is kept out by its own safety area.
    /// Each station tries the right side (+V) first, then the left, then steps further out and along; a station that
    /// finds no clear, dry, level spot is left out and reported by <see cref="Unplaced"/>.</summary>
    public static IEnumerable<Site> SitesFor(IslandField f, IReadOnlyList<IslandGeometry.Block> obstacles)
    {
        double end = f.Length / 2 - EndInsetM(f), baseAcross = RsaHalfWidthM(f) + RsaSetbackM;
        foreach (Station st in new[] { Station.ApproachEnd, Station.Midfield, Station.FarEnd })
        {
            double along0 = st == Station.ApproachEnd ? -end : st == Station.FarEnd ? end : 0;
            if (TryPlace(f, along0, baseAcross, obstacles, out var p, out double g, out double along, out double across))
                yield return new Site(f.Id, st, p, g, along, across);
        }
    }

    // Order of trial: same spot on the other side before moving it; out before along; along never past the inset.
    private static readonly double[] OutSteps = { 0, 8, 16, 26, 40 };
    private static readonly double[] AlongSteps = { 0, 25, -25, 50, -50, 80, -80 };
    private static bool TryPlace(IslandField f, double along0, double across0, IReadOnlyList<IslandGeometry.Block> obstacles,
        out IslandPoint at, out double ground, out double along, out double across)
    {
        foreach (double da in AlongSteps)
        foreach (double dc in OutSteps)
        foreach (int side in new[] { 1, -1 })
        {
            along = along0 + (along0 < 0 ? -da : da); across = side * (across0 + dc);
            if (Math.Abs(along) > f.Length / 2) continue;
            at = f.Centre + f.U * (along / 1000) + f.V * (across / 1000);
            ground = IslandTerrain.HeightKm(at);
            if (Clear(f, at, ground, along, obstacles)) return true;
        }
        at = default; ground = along = across = 0; return false;
    }

    /// <summary>Largest height (m) the mast's ground may sit off the runway's own plane abeam it, and the steepest
    /// ground it may stand on: a sock on a cut bank or down in a river bed is not one a pilot reads.</summary>
    public const double MaxStepFromRunwayM = 4, MaxGroundSlope = .25;

    internal static bool Clear(IslandField f, IslandPoint p, double ground, double along, IReadOnlyList<IslandGeometry.Block> obstacles)
    {
        if (IslandTerrain.WaterAt(p.X, p.Y) is not null) return false;
        if (Math.Abs(ground - f.Plane(f.Centre + f.U * (along / 1000))) > MaxStepFromRunwayM) return false;
        if (IslandTerrain.IsWallAt(p.X, p.Y, MaxGroundSlope)) return false;
        if (IslandCavities.DrakeInside(p) || IslandCavities.PirateInside(p)) return false;
        foreach (var other in IslandTerrain.Fields)
            if (InsideRsa(other, p)) return false;
        foreach (var b in obstacles)
            if (DistanceToBlock(b, p.X, p.Y) < ObstacleClearM && b.Top > ground - 1) return false;
        return true;
    }

    /// <summary>Inside a runway's safety area (water lanes too: a sock does not stand in a seaplane lane).</summary>
    public static bool InsideRsa(IslandField f, IslandPoint p)
    {
        var d = p - f.Centre; double a = Math.Abs(d.Dot(f.U)) * 1000, c = Math.Abs(d.Dot(f.V)) * 1000;
        return a <= f.Length / 2 + RsaBeyondEndM(f) && c <= Math.Max(RsaHalfWidthM(f), f.Width / 2);
    }

    /// <summary>Horizontal distance (m) from a point to a block's rotated rectangle (0 inside it).</summary>
    public static double DistanceToBlock(IslandGeometry.Block b, double x, double y)
    {
        double h = b.Heading * Math.PI / 180, dx = x - b.X, dy = y - b.Y;
        double a = Math.Abs(dx * Math.Cos(h) + dy * Math.Sin(h)) - b.Length / 2, c = Math.Abs(-dx * Math.Sin(h) + dy * Math.Cos(h)) - b.Width / 2;
        double ea = Math.Max(0, a), ec = Math.Max(0, c);
        return Math.Sqrt(ea * ea + ec * ec);
    }

    /// <summary>Every sock in the world, airport by airport, runway by runway.</summary>
    public static List<Site> Sites(IReadOnlyList<IslandGeometry.Block> obstacles)
    {
        var all = new List<Site>();
        foreach (var f in IslandTerrain.Fields) if (IsAirport(f)) all.AddRange(SitesFor(f, obstacles));
        return all;
    }
    /// <summary>"field:station" for every station that found nowhere to stand (should be none).</summary>
    public static IEnumerable<string> Unplaced(IReadOnlyList<Site> sites)
    {
        foreach (var f in IslandTerrain.Fields)
            if (IsAirport(f))
                foreach (Station st in new[] { Station.ApproachEnd, Station.Midfield, Station.FarEnd })
                    if (!sites.Any(s => s.FieldId == f.Id && s.Station == st)) yield return f.Id + ":" + st;
    }

    /// <summary>The mast as a solid (an aeroplane that hits it hits it): a 10 cm post to the top of the hoop.</summary>
    public static IslandGeometry.Block MastSolid(Site s) =>
        new(s.Id + "-mast", "white", s.X, s.Y, s.GroundM, s.GroundM + PivotHeightM + ThroatRadiusM, 2 * MastRadiusM, 2 * MastRadiusM);

    /// <summary>The air the sock reads: the very query every aeroplane flies in (<see cref="Atmosphere.WindAtPosition"/>:
    /// mean wind, gusts, thermals, slope and glacier winds) PLUS the world wake field, so a sock does feel an
    /// aeroplane that has just gone past. The windsock lane left a note saying this line belonged here the moment a
    /// wake field existed (<see cref="Aero.Wake.VortexWake"/>, merged 2026-11-10); SampleVelocity returns zero
    /// immediately when nothing is shedding, so a quiet airfield costs nothing.</summary>
    public static Vec3 AirAt(Vec3 pivotNed) =>
        Atmosphere.WindAtPosition(pivotNed) + Aero.Wake.VortexWake.SampleVelocity(pivotNed);
}

/// <summary>
/// How a sock moves (one per sock; <c>Bridge/Windsock.cs</c> steps it every frame from <see cref="WindsockPart.AirAt"/>).
/// Nothing is scripted: it swings and fills only from the wind vector it is given.
///
/// SWING. A weathervane on a swivel: the cone's normal force on its planform, Allen &amp; Perkins (NACA Rep. 1048): the
/// slender-body term 2 sin β |cos β| plus crossflow drag C_dc sin β |sin β| (C_dc 1.2, a cylinder), acting at its centroid, against its inertia (fabric plus the air it carries) and the swivel's
/// Coulomb friction. The flow meets the centroid at β plus the centroid's own swing speed over V, so the same force that
/// rights it also damps it: ζ ≈ 0.55 at every wind speed, a swing that overshoots a little and settles.
/// The friction is set from the specification's 3 kt point: at 3 kt the sock comes round to within 15° of the wind.
///
/// FILL. Air enters the throat only by the wind's component along the sock's own axis, and the fabric fills from the
/// throat back: the filled fraction heads for V_axis / 15 kt (the five bands, one each 3 kt) in about the time the air
/// takes to run the sock's length, and empties at the rate cloth falls (√(L/g)). A filled band lies along the 3-D flow;
/// an empty one hangs <see cref="LimpDroopDeg"/> below the horizontal.
/// </summary>
public sealed class WindsockDynamics
{
    /// <summary>Fabric (nylon, ~0.2 kg/m² over 7.3 m²) and hoop: what swings, with the air in a filled sock.</summary>
    public const double FabricKg = 1.6;
    /// <summary>An empty band hangs this far below the horizontal: nearly straight down, the hoop's lower lip holding
    /// the throat open.</summary>
    public const double LimpDroopDeg = 80;
    public const double CentroidFraction = .42;   // planform centroid of a 0.39 taper cone, from the throat
    private const double CrossflowDrag = 1.2;     // C_dc of a cylinder across the flow

    /// <summary>Direction the sock points (deg true, clockwise from north): downwind once settled.</summary>
    public double AzimuthDeg;
    public double AzimuthRateDegS;
    /// <summary>Filled fraction of the sock's length, 0 (hanging) to 1 (fully extended).</summary>
    public double Fill;
    /// <summary>Pitch of the 3-D flow below the horizontal (deg): a down-gust leans the filled part down.</summary>
    public double FlowPitchDeg;

    public WindsockDynamics(double azimuthDeg = 0) { AzimuthDeg = azimuthDeg; }

    private static double PlanformM2 => WindsockPart.SockLengthM * (WindsockPart.ThroatRadiusM + WindsockPart.TailRadiusM);
    private static double ArmM => CentroidFraction * WindsockPart.SockLengthM;
    private static double InertiaKgM2(double fill, double rho)
    {
        double r0 = WindsockPart.ThroatRadiusM, r1 = WindsockPart.TailRadiusM, l = WindsockPart.SockLengthM;
        double airKg = rho * Math.PI / 3 * l * (r0 * r0 + r0 * r1 + r1 * r1) * fill;
        return (FabricKg + airKg) * ArmM * ArmM;
    }
    /// <summary>Swivel friction (N·m): the aerodynamic moment at 15° off the wind at 3 kt with the sock as full as it is
    /// at 3 kt (the specification's "swings to the wind at 3 kt").</summary>
    public static readonly double SwivelFrictionNm = YawMoment(15 * Math.PI / 180, WindsockPart.OrientsMs, 1.225, WindsockPart.OrientsMs / WindsockPart.FullExtensionMs);

    /// <summary>The restoring moment (N·m) at flow angle β (rad) and wind speed v, with the sock filled to fill.
    /// Only the horizontal reach of the sock is a weathervane: a hanging sock's arm is its cos(droop).</summary>
    public static double YawMoment(double beta, double v, double rho, double fill)
    {
        double q = .5 * rho * v * v, reach = Math.Cos(ChordDroopDeg(fill) * Math.PI / 180);
        double cn = 2 * Math.Sin(beta) * Math.Abs(Math.Cos(beta)) + CrossflowDrag * Math.Sin(beta) * Math.Abs(Math.Sin(beta));
        return q * PlanformM2 * cn * ArmM * reach;
    }

    /// <summary>The angle (deg below horizontal) of the line from the hoop to the tail, for a fill.</summary>
    public static double ChordDroopDeg(double fill, double flowPitchDeg = 0)
    {
        double dx = 0, dz = 0;
        for (int i = 0; i < WindsockPart.Bands; i++)
        {
            double p = BandPitchDeg(i, fill, flowPitchDeg) * Math.PI / 180;
            dx += Math.Cos(p); dz += Math.Sin(p);
        }
        return Math.Atan2(dz, dx) * 180 / Math.PI;
    }
    /// <summary>Band i's pitch below the horizontal: filled bands along the flow, empty ones hanging, the band that is
    /// filling in between.</summary>
    public static double BandPitchDeg(int i, double fill, double flowPitchDeg = 0)
    {
        double s = Math.Clamp(fill * WindsockPart.Bands - i, 0, 1);
        return LimpDroopDeg + (flowPitchDeg - LimpDroopDeg) * s;
    }
    public double ChordDroopDeg() => ChordDroopDeg(Fill, FlowPitchDeg);

    /// <summary>Advance by dt (s) in the wind <paramref name="windNed"/> (m/s, the direction the air moves).</summary>
    public void Step(Vec3 windNed, double rho, double dt)
    {
        // Small fixed substeps: the swing's natural frequency is ~6 rad/s at 15 kt, ~16 at 40 kt.
        int n = Math.Max(1, (int)Math.Ceiling(dt / .01)); double h = dt / n;
        for (int k = 0; k < n; k++) Substep(windNed, rho, h);
    }

    private void Substep(Vec3 w, double rho, double dt)
    {
        double vh = Math.Sqrt(w.X * w.X + w.Y * w.Y);
        double psi = AzimuthDeg * Math.PI / 180, rate = AzimuthRateDegS * Math.PI / 180;
        // Flow angle at the centroid: the wind's bearing relative to the sock, less the centroid's own swing.
        double toward = Math.Atan2(w.Y, w.X);
        double beta = Wrap(toward - psi);
        double reachArm = ArmM * Math.Cos(ChordDroopDeg() * Math.PI / 180);
        double betaEff = vh > 1e-6 ? beta - Math.Atan2(rate * reachArm, vh) : 0;
        double inertia = InertiaKgM2(Fill, rho);
        double moment = YawMoment(betaEff, vh, rho, Fill);
        if (vh < 1e-6) moment = -Math.Sign(rate) * .5 * rho * PlanformM2 * Math.Pow(rate * reachArm, 2) * ArmM; // still air: drag of the swing itself
        // Swivel friction: Coulomb, sticking while the moment is below it.
        if (Math.Abs(rate) < 1e-3 && Math.Abs(moment) <= SwivelFrictionNm) rate = 0;
        else
        {
            double friction = SwivelFrictionNm * (Math.Abs(rate) >= 1e-3 ? -Math.Sign(rate) : -Math.Sign(moment));
            double next = rate + (moment + friction) / inertia * dt;
            if (Math.Abs(rate) >= 1e-3 && Math.Sign(next) != Math.Sign(rate) && Math.Abs(moment) <= SwivelFrictionNm) next = 0;
            rate = next;
        }
        psi = Wrap(psi + rate * dt);
        AzimuthDeg = (psi * 180 / Math.PI + 360) % 360; AzimuthRateDegS = rate * 180 / Math.PI;

        // Fill: by the air entering the throat, i.e. the wind along the sock's axis.
        double vAxis = Math.Max(0, vh * Math.Cos(Wrap(toward - psi)));
        double target = Math.Clamp(vAxis / WindsockPart.FullExtensionMs, 0, 1);
        double tau = target > Fill ? WindsockPart.SockLengthM / Math.Max(.5, vAxis) : Math.Sqrt(WindsockPart.SockLengthM / 9.80665);
        Fill += (target - Fill) * (1 - Math.Exp(-dt / tau));
        // The filled part lies along the 3-D flow (NED z is down: a down-gust pitches it down).
        double pitch = vh > .05 ? Math.Atan2(w.Z, vh) * 180 / Math.PI : 0;
        FlowPitchDeg += (pitch - FlowPitchDeg) * (1 - Math.Exp(-dt / tau));
    }

    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
}
