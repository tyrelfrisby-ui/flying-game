using FlyingGame.Core.MathTypes;
namespace FlyingGame.Core;

/// <summary>The visual glide path aid at a runway end. Only airports with an instrument approach have one.</summary>
public enum GlidePathAid { None, Papi, Vasi }

/// <summary>
/// One airport's lighting, as written down (Ty 2026-11-05 08:44: "only airports with instrument approaches have vasi or
/// papi make sure both are represented at one airport or more the rest of the airports have no visual aids and roughly
/// half have runway lights for night flying").
/// </summary>
public readonly struct AirportLightingPlan
{
    public readonly string Airport;
    /// <summary>The airport has a published instrument approach (the island's <c>ifr.approaches</c>).</summary>
    public readonly bool InstrumentApproach;
    public readonly GlidePathAid Aid;
    /// <summary>The runway ends that carry the aid, as "field id:runway number", e.g. "PHDH 8/26:26".</summary>
    public readonly string[] AidEnds;
    /// <summary>Runway edge, threshold and end lights for night flying.</summary>
    public readonly bool RunwayLights;
    public AirportLightingPlan(string airport, bool instrumentApproach, GlidePathAid aid, bool runwayLights, params string[] aidEnds)
    { Airport = airport; InstrumentApproach = instrumentApproach; Aid = aid; RunwayLights = runwayLights; AidEnds = aidEnds; }
}

/// <summary>
/// AIRPORT LIGHTING: which airport has what, and the three shared parts — the PAPI (<see cref="PapiPart"/>), the 2-bar
/// VASI (<see cref="VasiPart"/>) and the runway edge light (<see cref="EdgeLightPart"/>). This file is their design and
/// where they stand; <c>Bridge/AirportLights.cs</c> is the only thing that draws them. No airport, builder or GLB
/// carries one of its own (<c>AirportLightingTests.EachAidIsOneSharedPart</c>).
///
/// A light's colour is never painted on. Every PAPI box and VASI bar is white above its setting angle and red below
/// it, and what a pilot sees is the angle from that lens up to his own eye (<see cref="ElevationDeg"/>), so the
/// 2-white / 2-red picture exists only on the 3° path. The bidirectional edge lights work the same way by direction:
/// each face has a colour and shows it only to an eye on its side.
/// </summary>
public static class AirportLighting
{
    /// <summary>
    /// Every airport (<see cref="WindsockPart.IsAirport"/>: every land field but Drake and the beaches), 39 of them.
    ///
    /// INSTRUMENT APPROACHES are the island's published procedures (docs/pending/island-spec.json, <c>ifr.approaches</c>,
    /// copied by hand): PANC ILS 16 / ILS 34, KSNA LOC BC 2L, PHDH ILS 26, KMYL RNAV 17 / LDA/DME 17, KMEV Range 35 /
    /// RNAV 35, KPGA VOR/DME 34 / RNAV 16, KAVX VOR-A (circling), PATK NDB 2. Eight airports. The aid goes on each
    /// runway end a procedure leads to; KAVX's VOR-A is a circling approach with no runway of its own, so both ends of
    /// its one runway get it.
    ///
    /// PAPI or VASI: the VASI is the older, simpler system and is kept at the two small fields, PATK and KAVX (both
    /// 3,000-3,500 ft runways, non-precision only). The other six have the PAPI.
    ///
    /// RUNWAY LIGHTS ("roughly half"): 19 of 39. All eight instrument airports (an approach at night needs a lit runway),
    /// and the other public fields with a hard runway (PALH's gravel strip, SITX, TERN, NHVN, KITE, RIME), and the five
    /// longest airparks (AP1, AP3, AP8, AP10, AP12, 2,400-2,800 ft). Unlit: the Idaho backcountry strips (Johnson Creek
    /// 3U2, the Frank Church strips, Knik River), and the nine shorter airparks.
    /// </summary>
    public static readonly AirportLightingPlan[] Table =
    {
        new("PANC", true, GlidePathAid.Papi, true, "PANC 16/34:16", "PANC 16/34:34"),
        new("KSNA", true, GlidePathAid.Papi, true, "KSNA 2L/20R:2L"),
        new("PHDH", true, GlidePathAid.Papi, true, "PHDH 8/26:26"),
        new("KMYL", true, GlidePathAid.Papi, true, "KMYL 17/35:17"),
        new("KMEV", true, GlidePathAid.Papi, true, "KMEV 17/35:35"),
        new("KPGA", true, GlidePathAid.Papi, true, "KPGA 16/34:16", "KPGA 16/34:34"),
        new("KAVX", true, GlidePathAid.Vasi, true, "KAVX 5/23:5", "KAVX 5/23:23"),
        new("PATK", true, GlidePathAid.Vasi, true, "PATK 2/20:2"),
        new("PALH", false, GlidePathAid.None, true),
        new("SITX", false, GlidePathAid.None, true),
        new("TERN", false, GlidePathAid.None, true),
        new("NHVN", false, GlidePathAid.None, true),
        new("KITE", false, GlidePathAid.None, true),
        new("RIME", false, GlidePathAid.None, true),
        new("AP1", false, GlidePathAid.None, true),
        new("AP3", false, GlidePathAid.None, true),
        new("AP8", false, GlidePathAid.None, true),
        new("AP10", false, GlidePathAid.None, true),
        new("AP12", false, GlidePathAid.None, true),
        new("3U2", false, GlidePathAid.None, false),
        new("AP2", false, GlidePathAid.None, false),
        new("AP4", false, GlidePathAid.None, false),
        new("AP5", false, GlidePathAid.None, false),
        new("AP6", false, GlidePathAid.None, false),
        new("AP7", false, GlidePathAid.None, false),
        new("AP9", false, GlidePathAid.None, false),
        new("AP11", false, GlidePathAid.None, false),
        new("AP13", false, GlidePathAid.None, false),
        new("AP14", false, GlidePathAid.None, false),
        new("Chamberlain", false, GlidePathAid.None, false),
        new("Big Creek", false, GlidePathAid.None, false),
        new("Thomas Creek", false, GlidePathAid.None, false),
        new("Sulphur Creek", false, GlidePathAid.None, false),
        new("Soldier Bar", false, GlidePathAid.None, false),
        new("Cabin Creek", false, GlidePathAid.None, false),
        new("Dewey Moore", false, GlidePathAid.None, false),
        new("Indian Creek", false, GlidePathAid.None, false),
        new("Mackay Bar", false, GlidePathAid.None, false),
        new("Knik River", false, GlidePathAid.None, false),
    };

    /// <summary>The plan for an airport; an airport not in the table has no instrument approach and no lights.</summary>
    public static AirportLightingPlan For(string airport)
    {
        foreach (var p in Table) if (p.Airport == airport) return p;
        return new AirportLightingPlan(airport, false, GlidePathAid.None, false);
    }
    public static bool HasInstrumentApproach(string airport) => For(airport).InstrumentApproach;

    /// <summary>A runway with a glide path aid is an instrument runway: its edge lights turn yellow over the last
    /// 2,000 ft (<see cref="EdgeLightPart.CautionZoneM"/>).</summary>
    public static bool IsInstrumentRunway(IslandField f)
    {
        foreach (string e in For(f.Field).AidEnds) if (e.StartsWith(f.Id + ":", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Which end of <paramref name="f"/> a runway number names: -1 for the first number (its threshold at
    /// the strip's -U end, landing along +U), +1 for the second.</summary>
    public static int EndSide(IslandField f, string number)
    {
        string[] nums = f.Numbers.Split('/');
        if (nums.Length > 0 && nums[0] == number) return -1;
        if (nums.Length > 1 && nums[1] == number) return 1;
        throw new ArgumentException($"{f.Id} has no runway {number}");
    }

    /// <summary>Elevation angle (deg) from a lens up to the eye (sim NED, m): what decides every glide path colour.</summary>
    public static double ElevationDeg(GlidePathUnit u, Vec3 eye)
    {
        double dx = eye.X - u.Point.X, dy = eye.Y - u.Point.Y;
        return Math.Atan2(-eye.Z - u.LensM, Math.Sqrt(dx * dx + dy * dy)) * 180 / Math.PI;
    }

    // ---- the sites ----------------------------------------------------------------------------------------------

    private static List<GlidePathUnit> _units;
    private static List<EdgeLamp> _lamps;
    /// <summary>Every PAPI box and VASI box in the world.</summary>
    public static IReadOnlyList<GlidePathUnit> GlidePathUnits => _units ??= BuildUnits();
    /// <summary>Every runway edge, threshold and end light in the world.</summary>
    public static IReadOnlyList<EdgeLamp> EdgeLamps => _lamps ??= BuildLamps();

    private static List<GlidePathUnit> BuildUnits()
    {
        var list = new List<GlidePathUnit>();
        foreach (var plan in Table)
        foreach (string end in plan.AidEnds)
        {
            int colon = end.LastIndexOf(':');
            string id = end.Substring(0, colon), number = end.Substring(colon + 1);
            IslandField f = IslandTerrain.Fields.First(q => q.Id == id);
            if (plan.Aid == GlidePathAid.Papi) list.AddRange(PapiPart.UnitsFor(f, number));
            else if (plan.Aid == GlidePathAid.Vasi) list.AddRange(VasiPart.UnitsFor(f, number));
        }
        return list;
    }

    private static List<EdgeLamp> BuildLamps()
    {
        var list = new List<EdgeLamp>();
        foreach (var plan in Table)
        {
            if (!plan.RunwayLights) continue;
            var fields = IslandTerrain.Fields.Where(f => f.Field == plan.Airport && WindsockPart.IsAirport(f)).ToArray();
            bool hasHard = fields.Any(f => !StripFinish.LiesNaturally(f.Surface));
            foreach (var f in fields)
            {
                // A grass runway beside a hard one is the day-VFR one (KMEV 13G/31G): its hard neighbour is lit.
                if (hasHard && StripFinish.LiesNaturally(f.Surface)) continue;
                list.AddRange(EdgeLightPart.LampsFor(f, IsInstrumentRunway(f)));
            }
        }
        return list;
    }

    /// <summary>A point beside a runway: along / across in metres from its centre (+across is +V, the right of +U).</summary>
    internal static IslandPoint At(IslandField f, double along, double across) => f.Centre + f.U * (along / 1000) + f.V * (across / 1000);
}

/// <summary>One box of a PAPI or a VASI.</summary>
public readonly struct GlidePathUnit
{
    public readonly string FieldId, Runway; public readonly GlidePathAid Kind;
    /// <summary>PAPI: 0 is the box nearest the runway (set highest). VASI: 0-1 the downwind bar, 2-3 the upwind bar.</summary>
    public readonly int Index;
    public readonly IslandPoint Point;
    /// <summary>Ground under the box and the height of its lens centre (m above sea level).</summary>
    public readonly double GroundM, LensM;
    /// <summary>The angle (deg above horizontal) where this box changes from red (below) to white (above).</summary>
    public readonly double SettingDeg;
    /// <summary>True heading (deg) the box looks along: out toward the approaching aeroplane.</summary>
    public readonly double FacingDeg;
    public GlidePathUnit(string fieldId, string runway, GlidePathAid kind, int index, IslandPoint point, double ground, double lens, double setting, double facing)
    { FieldId = fieldId; Runway = runway; Kind = kind; Index = index; Point = point; GroundM = ground; LensM = lens; SettingDeg = setting; FacingDeg = facing; }
    public string Id => $"{FieldId}:{Runway}:{Kind}:{Index}";

    /// <summary>How white the box looks from <paramref name="eye"/> (sim NED, m): 0 red, 1 white, between them the
    /// narrow pink band of a real lens's filter edge.</summary>
    public double WhiteFraction(Vec3 eye)
    {
        double band = Kind == GlidePathAid.Papi ? PapiPart.TransitionDeg : VasiPart.TransitionDeg;
        return Math.Clamp((AirportLighting.ElevationDeg(this, eye) - SettingDeg) / band + .5, 0, 1);
    }

    /// <summary>How bright the box looks from <paramref name="eye"/>: full inside ±10° of the way it faces, fading out
    /// by 15°, dark from behind or from below the ground (FAA L-880 / L-851 horizontal coverage).</summary>
    public double Intensity(Vec3 eye)
    {
        double dx = eye.X - Point.X, dy = eye.Y - Point.Y, h = FacingDeg * Math.PI / 180;
        double fwd = dx * Math.Cos(h) + dy * Math.Sin(h), side = -dx * Math.Sin(h) + dy * Math.Cos(h);
        if (fwd <= 0) return 0;
        double az = Math.Abs(Math.Atan2(side, fwd)) * 180 / Math.PI;
        return Math.Clamp((15 - az) / 5, 0, 1) * (AirportLighting.ElevationDeg(this, eye) > -.5 ? 1 : 0);
    }
}

/// <summary>
/// THE PAPI (FAA AC 150/5345-28, L-880: four boxes in a row on the left of the runway). For a 3° path the boxes are set,
/// from the runway outward, at 3°30', 3°10', 2°50' and 2°30', so an eye on the path sees the two inner boxes red and
/// the two outer white; 3 white is slightly high (above 3°10'), 4 white high (above 3°30'), 3 red slightly low, 4 red
/// low (below 2°30').
/// </summary>
public static class PapiPart
{
    public const double PathDeg = 3.0;
    public static readonly double[] SettingDeg = { 3.5, 3 + 10 / 60.0, 2 + 50 / 60.0, 2.5 };
    /// <summary>Width of the red-to-white change: a real PAPI's is under 3 minutes of arc.</summary>
    public const double TransitionDeg = 3 / 60.0;
    /// <summary>Threshold crossing height on the path, 40 ft (aeroplane height group 1: eye to wheels under 10 ft).</summary>
    public const double ThresholdCrossingM = 40 * 0.3048;
    /// <summary>The inner box's gap from the runway edge (50 ft) and the spacing between boxes (30 ft).</summary>
    public const double EdgeGapM = 15.2, SpacingM = 9.1;
    public const double LensHeightM = 0.9;
    public static readonly Vec3 BoxSizeM = new(1.0, 0.7, 0.6);   // across, up, along

    /// <summary>Distance (m) from the threshold to the row: the line at 3° from the lens crosses the threshold at
    /// <see cref="ThresholdCrossingM"/> above the runway.</summary>
    public static double FromThresholdM => (ThresholdCrossingM - LensHeightM) / Math.Tan(PathDeg * Math.PI / 180);

    public static IEnumerable<GlidePathUnit> UnitsFor(IslandField f, string runway)
    {
        int s = AirportLighting.EndSide(f, runway);
        double along = s * (f.Length / 2 - FromThresholdM);
        double facing = (f.Heading + (s < 0 ? 180 : 0)) % 360;   // looks out past its own threshold
        double lens = f.Plane(AirportLighting.At(f, along, 0)) + LensHeightM;
        for (int i = 0; i < 4; i++)
        {
            // The landing pilot's left is s * V (s = -1: landing along +U, left is -V).
            var p = AirportLighting.At(f, along, s * (f.Width / 2 + EdgeGapM + i * SpacingM));
            yield return new GlidePathUnit(f.Id, runway, GlidePathAid.Papi, i, p, IslandTerrain.HeightKm(p), lens, SettingDeg[i], facing);
        }
    }

    /// <summary>White boxes seen from <paramref name="eye"/> (a box counts white past the middle of its pink band).</summary>
    public static int WhiteCount(IEnumerable<GlidePathUnit> row, Vec3 eye) => row.Count(u => u.WhiteFraction(eye) > .5);
}

/// <summary>
/// THE 2-BAR VASI (VASI-4: two boxes in each of two bars, on the left of the runway). The downwind bar stands 500 ft in
/// from the threshold, the upwind bar 700 ft beyond it. Each box is white above its setting angle and red below:
/// downwind bar 2.75°, upwind bar 3.25°, a 3° path between them. On the path the far (upwind) bar is red over the near
/// (downwind) bar white; below 2.75° both are red, above 3.25° both white.
/// </summary>
public static class VasiPart
{
    public const double PathDeg = 3.0, DownwindSettingDeg = 2.75, UpwindSettingDeg = 3.25;
    /// <summary>A VASI's pink band is wider than a PAPI's (its filter edge is a plain red glass in a slot).</summary>
    public const double TransitionDeg = 0.1;
    public const double DownwindFromThresholdM = 500 * 0.3048, BarSeparationM = 700 * 0.3048;
    public const double EdgeGapM = 15.2, BoxSpacingM = 7.6, LensHeightM = 0.8;
    public static readonly Vec3 BoxSizeM = new(1.6, 0.6, 0.6);

    public static IEnumerable<GlidePathUnit> UnitsFor(IslandField f, string runway)
    {
        int s = AirportLighting.EndSide(f, runway);
        double facing = (f.Heading + (s < 0 ? 180 : 0)) % 360;
        for (int bar = 0; bar < 2; bar++)
        {
            double along = s * (f.Length / 2 - DownwindFromThresholdM - bar * BarSeparationM);
            double lens = f.Plane(AirportLighting.At(f, along, 0)) + LensHeightM;
            for (int k = 0; k < 2; k++)
            {
                var p = AirportLighting.At(f, along, s * (f.Width / 2 + EdgeGapM + k * BoxSpacingM));
                yield return new GlidePathUnit(f.Id, runway, GlidePathAid.Vasi, bar * 2 + k, p, IslandTerrain.HeightKm(p), lens,
                    bar == 0 ? DownwindSettingDeg : UpwindSettingDeg, facing);
            }
        }
    }

    public enum Picture { AllWhite, RedOverWhite, AllRed, Mixed }
    /// <summary>What the pilot sees from <paramref name="eye"/>: both bars white (high), far red over near white (on
    /// the path), both red (low).</summary>
    public static Picture See(IEnumerable<GlidePathUnit> bars, Vec3 eye)
    {
        bool nearWhite = bars.Where(u => u.Index < 2).All(u => u.WhiteFraction(eye) > .5);
        bool nearRed = bars.Where(u => u.Index < 2).All(u => u.WhiteFraction(eye) < .5);
        bool farWhite = bars.Where(u => u.Index >= 2).All(u => u.WhiteFraction(eye) > .5);
        bool farRed = bars.Where(u => u.Index >= 2).All(u => u.WhiteFraction(eye) < .5);
        if (nearWhite && farWhite) return Picture.AllWhite;
        if (nearWhite && farRed) return Picture.RedOverWhite;
        if (nearRed && farRed) return Picture.AllRed;
        return Picture.Mixed;
    }
}

public enum LampColour { None, White, Yellow, Green, Red }

/// <summary>A runway light: one or two faces. A face shows its colour to an eye on its side of the lamp (along the
/// runway); <see cref="LampColour.None"/> on <see cref="TowardMinusU"/> with a white <see cref="TowardPlusU"/> and
/// <see cref="Omni"/> set is a plain all-round edge light.</summary>
public readonly struct EdgeLamp
{
    public readonly string FieldId; public readonly IslandPoint Point; public readonly double GroundM;
    public readonly bool Threshold, Omni;
    /// <summary>The colour seen by an eye toward +U of the lamp, and toward -U.</summary>
    public readonly LampColour TowardPlusU, TowardMinusU;
    /// <summary>The runway's +U heading (true deg).</summary>
    public readonly double HeadingDeg;
    public EdgeLamp(string fieldId, IslandPoint point, double ground, bool threshold, bool omni, LampColour plus, LampColour minus, double heading)
    { FieldId = fieldId; Point = point; GroundM = ground; Threshold = threshold; Omni = omni; TowardPlusU = plus; TowardMinusU = minus; HeadingDeg = heading; }

    /// <summary>The colour an eye (sim NED, m) sees: decided by which side of the lamp it is on.</summary>
    public LampColour SeenFrom(Vec3 eye)
    {
        if (Omni) return TowardPlusU;
        double h = HeadingDeg * Math.PI / 180;
        double fwd = (eye.X - Point.X) * Math.Cos(h) + (eye.Y - Point.Y) * Math.Sin(h);
        return fwd >= 0 ? TowardPlusU : TowardMinusU;
    }
}

/// <summary>
/// THE RUNWAY EDGE LIGHT (FAA AC 150/5340-30, MIRL L-861): a row each side, 2 ft outside the edge, no more than 200 ft
/// apart, white. On an instrument runway the last 2,000 ft (or half the runway, if shorter) before each end shows
/// yellow to the pilot rolling toward that end and white the other way. Across each end, a threshold/end bar: green
/// out toward the approach, red in toward the runway.
/// </summary>
public static class EdgeLightPart
{
    public const double MaxSpacingM = 200 * 0.3048, EdgeOutsideM = 0.6, CautionZoneM = 2000 * 0.3048;
    /// <summary>Threshold/end lights per side of the centreline, and their spacing (MIRL: in two groups at the edges).</summary>
    public const int ThresholdPerSide = 4; public const double ThresholdSpacingM = 3.0, ThresholdOutsideM = 0.6;
    public const double LensHeightM = 0.35;

    public static IEnumerable<EdgeLamp> LampsFor(IslandField f, bool instrumentRunway)
    {
        int n = Math.Max(2, (int)Math.Ceiling(f.Length / MaxSpacingM));
        double caution = Math.Min(CautionZoneM, f.Length / 2);
        for (int k = 1; k < n; k++)
        {
            double along = -f.Length / 2 + k * f.Length / n;
            foreach (int side in new[] { -1, 1 })
            {
                var p = AirportLighting.At(f, along, side * (f.Width / 2 + EdgeOutsideM));
                if (!Placeable(p)) continue;
                double ground = IslandTerrain.HeightKm(p);
                if (instrumentRunway && Math.Abs(along) > f.Length / 2 - caution)
                {
                    // In the zone before the +U end, the pilot heading for it is on the -U side and sees yellow.
                    bool nearPlus = along > 0;
                    yield return new EdgeLamp(f.Id, p, ground, false, false,
                        nearPlus ? LampColour.White : LampColour.Yellow, nearPlus ? LampColour.Yellow : LampColour.White, f.Heading);
                }
                else yield return new EdgeLamp(f.Id, p, ground, false, true, LampColour.White, LampColour.None, f.Heading);
            }
        }
        foreach (int end in new[] { -1, 1 })
        {
            if (f.Field == "DRAKE") continue;
            for (int side = -1; side <= 1; side += 2)
            for (int j = 0; j < ThresholdPerSide; j++)
            {
                double across = side * (f.Width / 2 - j * ThresholdSpacingM);
                if (side * across < 1.5) continue;   // keep the centre of a narrow strip clear
                var p = AirportLighting.At(f, end * (f.Length / 2 + ThresholdOutsideM), across);
                if (!Placeable(p)) continue;
                // Green faces out of the runway (the approach side of this end), red faces in.
                yield return new EdgeLamp(f.Id, p, IslandTerrain.HeightKm(p), true, false,
                    end > 0 ? LampColour.Green : LampColour.Red, end > 0 ? LampColour.Red : LampColour.Green, f.Heading);
            }
        }
    }

    /// <summary>Dry ground outside the caves. <see cref="IslandTerrain.WaterAt"/> gives a lake's level wherever its basin
    /// reaches (under the end of KMYL, 38 m below the runway), so a lamp is in water only if its ground is below that.</summary>
    private static bool Placeable(IslandPoint p)
    {
        double? water = IslandTerrain.WaterAt(p.X, p.Y);
        if (water is double w && IslandTerrain.HeightKm(p) < w + .3) return false;
        return !IslandCavities.DrakeInside(p) && !IslandCavities.PirateInside(p);
    }
}
