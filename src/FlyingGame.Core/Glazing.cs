using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>What a type's canopy, windshield and windows are made of.</summary>
public enum GlazingMaterial { None, CastAcrylic, StretchedAcrylic, LaminatedGlass }

/// <summary>Which piece of glazing a pane is (for the log, the water's hatches and the remnant's look).</summary>
public enum GlazingKind { Canopy, Windshield, Windscreen, SideWindow, Window }

/// <summary>How a type's cockpit is closed: a canopy over the pilot, a cabin with a windshield and windows, open cockpits
/// behind windscreens, or nothing to glaze at all.</summary>
public enum CockpitEnclosure { None, Canopy, Cabin, Open }

/// <summary>
/// One pane of glazing: a set of SENSOR points on its outer surface, body frame (config origin, +x forward, +y right,
/// +z down). The points push nothing — the airframe's hard points (<see cref="AirframeContact"/>) already carry the
/// aeroplane, so adding glass changes no crash — they only ask "did the glass itself arrive on something?".
/// </summary>
public sealed class GlazingPane
{
    public string Id = "";
    public GlazingKind Kind;
    public Vec3[] Points = System.Array.Empty<Vec3>();
    public bool Shattered;
    /// <summary>Per point: inside the ground or a solid at the end of last step (an arrival is a change from false).</summary>
    public bool[] Inside = System.Array.Empty<bool>();
    /// <summary>What shattered it: closing speed, m/s, the obstacle ("ground", "solid", "tree", "water"), and where (body).</summary>
    public double ShatterClosingMs;
    public string ShatterBy = "";
    public Vec3 ShatterAtBody;
}

/// <summary>
/// CANOPY, WINDSHIELD AND WINDOW GLASS IN A CRASH (Ty 2026-11-05 ~13:32, Q028b: "Make it accurate — canopy is very tough
/// plastic; stays attached if unlatched; breaks/shatters ONLY if it takes a hard enough DIRECT impact (not merely because of
/// a crash)", with a jagged broken-glass look; and ~18:23: every aeroplane's canopy, windshield and side windows, not only
/// the Pitts').
///
/// Until this there was no glass in the crash model at all (crash-tuning decision (c): "no canopy part exists in the
/// sim"), except the water's pressure hatches (<see cref="Wreck.WaterSlam"/>: a canopy or windshield bursting under a
/// ditching's slam pressure, which floods the cabin but drew nothing).
///
/// The rule: a pane shatters only when ITS OWN surface arrives on something — one of its sensor points goes into the
/// ground or a solid at a closing speed above <see cref="ShatterClosingMs"/>, or a tree stem strong enough to break it
/// (<see cref="TreeStructure.BreakForceN"/> above <see cref="FractureForceN"/>) meets it, or the water's slam bursts the
/// matching hatch. Deceleration, a crushed fuselage, a wing torn off, a hard landing — loads that reach the glass only
/// through its frame — never do: a canopy rides through all of them, latched or not (nothing here opens a latch or pulls
/// a hinge pin; the headset's <see cref="Vr.CanopyDynamics"/> keeps that part, and it is airspeed that tears a canopy
/// off its hinges, not a crash).
///
/// The numbers:
///  • Fracture under a point load (a stem, a post): a simply supported plate of radius a under a load spread over r₀
///    reaches σ_max = 3P/(2πt²)·[(1 + ν)·ln(a/r₀) + 1] (Roark, Formulas for Stress and Strain, Table 11.2 case 16).
///    With a = 0.30 m, r₀ = 0.02 m, ν = 0.37 the bracket is 4.7, so P_f = σ_f·t²/2.24 statically; acrylic is markedly
///    stronger at impact strain rates, taken as ×2 (ASSUMED, the round figure for PMMA's rate sensitivity). Cast acrylic
///    (MIL-PRF-5425, <see cref="Wreck.WaterPhysics.AcrylicFtuPa"/>, 3/16 in): 1.4 kN, so a stem thinner than ~2.4 cm snaps
///    on the glass and leaves it whole — the soft top of a tree (the top ~1.3 m, Q141) does not break a canopy.
///  • Arrival on the ground or a solid: that surface does not snap, so what decides is how hard the glass meets it; and
///    the load spreads over the area that touches, which a point-load rule cannot say. The closing speeds are CHOSEN, not
///    derived (Ty's "very tough": comfortably above the 2–4 m/s at which a nosed-over taildragger settles onto its
///    canopy; below the cabin's own 8 m/s break speed): cast acrylic 6 m/s, stretched acrylic (the fighters' canopies,
///    MIL-PRF-25690, roughly twice cast acrylic's impact toughness) 8 m/s, an airliner's laminated glass windshield
///    12 m/s.
/// </summary>
public static class Glazing
{
    /// <summary>A stem must be closing at least this fast to break glass at all (a walk-pace taxi into a branch), m/s.</summary>
    public const double MinStemClosingMs = 2.0;
    /// <summary>Point-load geometry factor, P_f = σ·t² / this (see the class notes).</summary>
    public const double PointLoadFactor = 2.24;
    /// <summary>Impact strength over static strength (ASSUMED).</summary>
    public const double RateFactor = 2.0;

    /// <summary>Closing speed onto the ground or a solid that shatters a pane, m/s (chosen: see the class notes).</summary>
    public static double ShatterClosingMs(GlazingMaterial m) => m switch
    {
        GlazingMaterial.CastAcrylic => 6.0,
        GlazingMaterial.StretchedAcrylic => 8.0,
        GlazingMaterial.LaminatedGlass => 12.0,
        _ => double.PositiveInfinity,
    };

    /// <summary>Thickness, m: 3/16 in cast acrylic (<see cref="Wreck.WaterPhysics.GlazingThicknessM"/>), 1/4 in stretched
    /// acrylic, ~1 in for a laminated airliner windshield (ASSUMED).</summary>
    public static double ThicknessM(GlazingMaterial m) => m switch
    {
        GlazingMaterial.CastAcrylic => Wreck.WaterPhysics.GlazingThicknessM,
        GlazingMaterial.StretchedAcrylic => 0.25 * 0.0254,
        GlazingMaterial.LaminatedGlass => 1.0 * 0.0254,
        _ => 0.0,
    };

    /// <summary>Strength, Pa: acrylic 10 ksi (<see cref="Wreck.WaterPhysics.AcrylicFtuPa"/>); stretched acrylic 11 ksi
    /// (ASSUMED, MIL-PRF-25690 sheet runs a little above cast); heat-strengthened glass 100 MPa (ASSUMED).</summary>
    public static double StrengthPa(GlazingMaterial m) => m switch
    {
        GlazingMaterial.CastAcrylic => Wreck.WaterPhysics.AcrylicFtuPa,
        GlazingMaterial.StretchedAcrylic => 11000 * 6894.757,
        GlazingMaterial.LaminatedGlass => 100e6,
        _ => 0.0,
    };

    /// <summary>The point load that fractures a pane at impact rates, N: σ·t²/2.24 × 2.</summary>
    public static double FractureForceN(GlazingMaterial m)
    {
        double t = ThicknessM(m);
        return m == GlazingMaterial.None ? double.PositiveInfinity : RateFactor * StrengthPa(m) * t * t / PointLoadFactor;
    }

    /// <summary>A direct arrival on the ground or a solid at <paramref name="closingMs"/>: does the pane shatter?</summary>
    public static bool ShattersOnSurface(GlazingMaterial m, double closingMs) => closingMs > ShatterClosingMs(m);

    /// <summary>A tree stem of diameter <paramref name="stemM"/> met at <paramref name="closingMs"/>: the stem snaps on the
    /// glass (and leaves it whole) unless it can push harder than the glass can take.</summary>
    public static bool ShattersOnStem(GlazingMaterial m, double stemM, double closingMs) =>
        m != GlazingMaterial.None && closingMs > MinStemClosingMs && TreeStructure.BreakForceN(stemM) > FractureForceN(m);

    /// <summary>What the type's glazing is made of.</summary>
    public static GlazingMaterial MaterialFor(string aircraftId) => aircraftId switch
    {
        "hang-glider-like" or "paraglider-en-b-like" or "target-drone-like" => GlazingMaterial.None,
        "boeing-737-like" or "dc3-like" or "hughes-h4-like" => GlazingMaterial.LaminatedGlass,
        "p51d-like" or "f86-sabre-like" => GlazingMaterial.StretchedAcrylic,
        _ => GlazingMaterial.CastAcrylic,
    };

    /// <summary>How the type's cockpit is closed.</summary>
    public static CockpitEnclosure EnclosureFor(string aircraftId) => aircraftId switch
    {
        "hang-glider-like" or "paraglider-en-b-like" or "target-drone-like" => CockpitEnclosure.None,
        "great-lakes-like" or "stearman-pt17-like" or "jn4-jenny-like" or "aircam-like" or "aircam-amphib-like" => CockpitEnclosure.Open,
        "geebee-r2-like" => CockpitEnclosure.Canopy,
        _ => CanopyEgress.ShellCanopyLeaves(aircraftId) ? CockpitEnclosure.Canopy : CockpitEnclosure.Cabin,
    };

    /// <summary>
    /// The panes for a type without measured ones (the headless sim, tests): placed from the seats' eyes and the fuselage's
    /// own cabin-top hard point. The game replaces these with points measured on the drawn glass itself
    /// (Bridge GlassShatter → <c>Aircraft.SetGlazing</c>), so the sensors sit where the glass the pilot sees is.
    /// </summary>
    public static List<GlazingPane> DefaultPanes(AircraftConfig c, IReadOnlyList<ContactPoint> contacts)
    {
        var panes = new List<GlazingPane>();
        if (MaterialFor(c.Id) == GlazingMaterial.None) return panes;
        CockpitEnclosure enc = EnclosureFor(c.Id);
        if (enc == CockpitEnclosure.None) return panes;
        ContactPoint? top = null;
        foreach (ContactPoint p in contacts) if (p.Name == "cabin-top") top = p;
        double rBody = top is not null ? System.Math.Max(0.25, -top.Body.Z / 1.1) : 0.5;
        var eyes = new List<Vec3>();
        if (c.Loading is { } ld) foreach (StationConfig s in ld.Seats) if (s.HasEye) eyes.Add(s.EyeVec());
        if (eyes.Count == 0) eyes.Add(new Vec3(top?.Body.X ?? 0.3, 0, -0.55 * rBody));
        eyes.Sort((a, b) => b.X.CompareTo(a.X));   // front seat first
        Vec3 front = eyes[0], rear = eyes[^1];
        double halfW = System.Math.Clamp(0.55 * rBody, 0.25, 0.6);
        switch (enc)
        {
            case CockpitEnclosure.Canopy:
            {
                var pts = new List<Vec3>();
                foreach (Vec3 e in eyes) { pts.Add(new Vec3(e.X + 0.15, 0, e.Z - 0.17)); pts.Add(new Vec3(e.X - 0.20, 0, e.Z - 0.15)); pts.Add(new Vec3(e.X, -halfW, e.Z + 0.02)); pts.Add(new Vec3(e.X, halfW, e.Z + 0.02)); }
                panes.Add(Pane("canopy", GlazingKind.Canopy, pts));
                panes.Add(Pane("windshield", GlazingKind.Windshield, new[] { new Vec3(front.X + 0.45, 0, front.Z - 0.02), new Vec3(front.X + 0.40, -0.7 * halfW, front.Z + 0.08), new Vec3(front.X + 0.40, 0.7 * halfW, front.Z + 0.08) }));
                break;
            }
            case CockpitEnclosure.Cabin:
                panes.Add(Pane("windshield", GlazingKind.Windshield, new[] { new Vec3(front.X + 0.55, 0, front.Z - 0.05), new Vec3(front.X + 0.50, -halfW, front.Z + 0.05), new Vec3(front.X + 0.50, halfW, front.Z + 0.05), new Vec3(front.X + 0.70, 0, front.Z + 0.15) }));
                panes.Add(Pane("window-L", GlazingKind.SideWindow, new[] { new Vec3(front.X, -halfW - 0.08, front.Z), new Vec3(0.5 * (front.X + rear.X) - 0.2, -halfW - 0.08, front.Z + 0.05) }));
                panes.Add(Pane("window-R", GlazingKind.SideWindow, new[] { new Vec3(front.X, halfW + 0.08, front.Z), new Vec3(0.5 * (front.X + rear.X) - 0.2, halfW + 0.08, front.Z + 0.05) }));
                break;
            case CockpitEnclosure.Open:
                for (int i = 0; i < eyes.Count; i++)
                    panes.Add(Pane(eyes.Count == 1 ? "windscreen" : i == 0 ? "windscreen-front" : "windscreen-rear", GlazingKind.Windscreen,
                        new[] { new Vec3(eyes[i].X + 0.45, 0, eyes[i].Z - 0.02), new Vec3(eyes[i].X + 0.42, -0.18, eyes[i].Z + 0.05), new Vec3(eyes[i].X + 0.42, 0.18, eyes[i].Z + 0.05) }));
                break;
        }
        return panes;
    }

    public static GlazingPane Pane(string id, GlazingKind kind, IEnumerable<Vec3> pts)
    {
        var p = new GlazingPane { Id = id, Kind = kind, Points = pts.ToArray() };
        p.Inside = new bool[p.Points.Length];
        return p;
    }

    /// <summary>The water's hatches (<see cref="Wreck.AirframeHydroModel"/>: "canopy broken", "windshield broken") a pane
    /// of this kind is part of: the canopy hatch is the canopy and its windscreen, all one piece of acrylic.</summary>
    public static bool HatchCovers(string hatchWhy, GlazingKind kind) =>
        hatchWhy.StartsWith("canopy broken", System.StringComparison.Ordinal) ? kind is GlazingKind.Canopy or GlazingKind.Windshield or GlazingKind.Windscreen
        : hatchWhy.StartsWith("windshield broken", System.StringComparison.Ordinal) && kind is GlazingKind.Windshield or GlazingKind.Windscreen;

    /// <summary>Whether a shattered pane of this kind opens the water hatch <paramref name="hatchWhy"/> (a side window opens
    /// none: the water model does not carry it as a hatch).</summary>
    public static bool OpensHatch(GlazingKind kind, string hatchWhy) =>
        kind is GlazingKind.Canopy or GlazingKind.Windshield or GlazingKind.Windscreen
        && (hatchWhy.StartsWith("canopy broken", System.StringComparison.Ordinal) || hatchWhy.StartsWith("windshield broken", System.StringComparison.Ordinal));
}
