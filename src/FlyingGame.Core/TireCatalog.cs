using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// One tyre (and the wheel it is on), as the catalog lists it. Figures are kept in the units their sources print
/// them in (inches, pounds, psi) and converted once; <see cref="Source"/> says where each came from and which are
/// estimates. Tyre masses are the TYRE alone; the wheel halves, the tube and — on a braked leg — the brake disc are
/// separate, because they sit at a different radius and that is what the inertia is made of.
/// </summary>
public sealed class TireSpec
{
    public string Id = "", Name = "", Source = "";
    public double OdIn, WidthIn, RimIn;
    public int Ply;
    public bool HeavyDuty, Tundra;
    /// <summary>A size carried over from lane/claude-tire-spinup (radius and rotating mass unchanged), not yet researched.
    /// Width is 0.38 of the diameter (WheelHydro's old rule). With no carcass data it has NO tyre physics: it is rigid
    /// (the leg alone, as before), its slip scale is the old 0.08, its rolling resistance the surface table's and its
    /// inertia the old 0.65·m·R² — so every type not researched here behaves exactly as it did.</summary>
    public bool Legacy;

    /// <summary>Does this tyre have the data the tyre physics needs (TireModel)? Not a legacy one.</summary>
    public bool HasCarcass => !Legacy;
    /// <summary>Tyres on the leg (a 737's main and nose legs are twin-wheeled). Mass, inertia, spring and footprint
    /// are all per leg.</summary>
    public int Count = 1;
    public double TireLb, TubeLb, WheelLb, DiscLb;
    /// <summary>Brake disc outside diameter (in): the disc is an annulus out to here, in to 0.7 of it.</summary>
    public double DiscOdIn;
    public double MinPsi, TypicalPsi, MaxPsi;
    /// <summary>Rated load at <see cref="MaxPsi"/> (lb), where the source gives one; 0 = not published.</summary>
    public double RatedLoadLb;

    public const double In = 0.0254, Lb = 0.45359237;
    public double OdM => OdIn * In;
    public double RadiusM => OdM / 2;
    public double SectionWidthM => WidthIn * In;
    public double RimRadiusM => RimIn * In / 2;
    public double SectionHeightM => (OdIn - RimIn) / 2 * In;
    public double TireMassKg => TireLb * Lb;

    /// <summary>The rotating mass of the leg (kg): tyre, tube, wheel, and the disc if the leg is braked.</summary>
    public double RotatingMassKg(bool braked) => Count * (TireLb + TubeLb + WheelLb + (braked ? DiscLb : 0)) * Lb;

    // Where a tyre's mass is (bias-ply construction): the tread and the crown plies under it, a band within half an
    // inch of the outside, carry ~60 %; the two sidewalls, an annulus from the bead to the crown, ~30 %; the beads
    // (steel wire bundles at the rim) ~10 %. ESTIMATE — no tyre maker publishes the split; it is what the cut section
    // of a bias tyre looks like, and it puts a balloon tyre's own k² at 0.70–0.75 R², the figure the brief asked for.
    public const double TreadShare = 0.60, SidewallShare = 0.30, BeadShare = 0.10, TreadDepthIn = 0.5;

    /// <summary>The tyre's own polar inertia (kg·m²), one tyre.</summary>
    public double TireInertiaKgM2
    {
        get
        {
            double R = RadiusM, rt = R - TreadDepthIn * In, rb = RimRadiusM, m = TireMassKg;
            return m * (TreadShare * rt * rt + SidewallShare * 0.5 * (rt * rt + rb * rb) + BeadShare * rb * rb);
        }
    }

    /// <summary>
    /// The leg's polar inertia (kg·m²), DERIVED from where each part's mass is — never hand-set:
    /// - the tyre as above;
    /// - the tube, a thin torus filling the section: ⟨ρ²⟩ = R_c² + a²/2 about the axle (R_c its centre radius, a half the
    ///   section height);
    /// - the wheel: 55 % in the barrel and flanges at the rim radius, 45 % in the web and hub as a uniform disc of
    ///   the rim radius (a cast light-aircraft wheel; ESTIMATE of the split);
    /// - the disc, an annulus from 0.7 of its OD to its OD.
    /// </summary>
    public double InertiaKgM2(bool braked)
    {
        if (Legacy) return WheelSpin.InertiaFactor * RotatingMassKg(braked) * RadiusM * RadiusM;
        double rb = RimRadiusM;
        double a = SectionHeightM / 2, rc = rb + a;
        double tube = TubeLb * Lb * (rc * rc + a * a / 2);
        double wheel = WheelLb * Lb * (0.55 * rb * rb + 0.45 * 0.5 * rb * rb);
        double ro = DiscOdIn * In / 2, ri = 0.7 * ro;
        double disc = braked ? DiscLb * Lb * 0.5 * (ro * ro + ri * ri) : 0;
        return Count * (TireInertiaKgM2 + tube + wheel + disc);
    }

    /// <summary>The leg's inertia over its rotating mass times R² (the "k" of I = k·m·R²).</summary>
    public double InertiaFactor(bool braked) => InertiaKgM2(braked) / (RotatingMassKg(braked) * RadiusM * RadiusM);
}

/// <summary>
/// THE TYRE CATALOG (Ty 2026-11-04, "Yes and 35's"): every tyre any aeroplane in the game rolls on, in one table, so
/// that a tyre's size, mass, inertia, carcass and pressures have one source of truth. Configs name a tyre by its id
/// (<see cref="GearConfig.Tire"/>) and give only the pressure they run it at.
///
/// SOURCES (copied by hand; AGENT-LOG 2026-11-04 tundra-tires has the full list with URLs):
/// - [GY] Goodyear Aviation Data Section 2022, §4C Type III (and the 2018 data book, same rows): OD and section width
///   (maximum inflated), rim, ply, rated load and inflation.
/// - [CD] Michelin Condor data sheet (McFarlane), "Avg. Wgt. (lbs.)": tyre weights for the Goodyear sizes.
/// - [GYT] Goodyear 2018 tube table: tube weights.
/// - [MCF] McFarlane Aviation product table, mcfarlaneaviation.com/products/29136--R/ and /351510--R/: Alaskan
///   Bushwheel TYRE weights (used verbatim). [ICA] Airframes Alaska / Alaskan Bushwheels Installation Instructions
///   &amp; ICA rev A (2021): sizes, pressure ranges, wheels, "approximate finished weight" (in brackets in each Source).
///   Bushwheel sizes are nominal (no measured OD is published); they are tubeless (self-sealing, [MCF]).
/// - [ASW] aircraftsupply.com ABI-10650LW-1.5: 9.6 lb wheel assembly. [AS] Aircraft Spruce part pages.
/// - Cleveland wheel and disc weights are NOT published (only shipping weights, 3 lb for a 164-xxx disc); every
///   wheel and disc mass marked EST below is an estimate, and on AGENT-LOG's NEEDS YOU.
/// </summary>
public static class TireCatalog
{
    private static TireSpec T(string id, string name, double od, double w, double rim, int ply, double tireLb, double tubeLb,
                              double wheelLb, double discLb, double minPsi, double typPsi, double maxPsi, double ratedLb, string src,
                              bool tundra = false, bool hd = false)
        => new() { Id = id, Name = name, OdIn = od, WidthIn = w, RimIn = rim, Ply = ply, TireLb = tireLb, TubeLb = tubeLb,
                   WheelLb = wheelLb, DiscLb = discLb, DiscOdIn = 1.15 * rim, MinPsi = minPsi, TypicalPsi = typPsi, MaxPsi = maxPsi,
                   RatedLoadLb = ratedLb, Source = src, Tundra = tundra, HeavyDuty = hd };

    /// <summary>A size carried over unchanged from the spin-up lane: radius (m) and the leg's rotating mass (kg), as that
    /// lane estimated them. Split 55 % tyre / 35 % wheel / 10 % disc (braked) or 60 / 40 (not), width 0.38·d, rim 0.35·d.</summary>
    private static TireSpec L(string id, string name, double radiusM, double massKg, bool braked, double psi, int count = 1)
    {
        double od = 2 * radiusM / TireSpec.In, lb = massKg / TireSpec.Lb / count;
        return new()
        {
            Id = id, Name = name, Legacy = true, Count = count, OdIn = od, WidthIn = 0.38 * od, RimIn = 0.35 * od,
            TireLb = lb * (braked ? 0.55 : 0.60), WheelLb = lb * (braked ? 0.35 : 0.40), DiscLb = braked ? lb * 0.10 : 0,
            DiscOdIn = 1.15 * 0.35 * od, MinPsi = psi * 0.5, TypicalPsi = psi, MaxPsi = psi,
            Source = "LEGACY: radius and rotating mass carried over from lane/claude-tire-spinup's estimate; width, rim and mass split are the catalog's default proportions; not researched",
        };
    }

    // The 6-in Cleveland wheel (40-78 / 40-86 / 40-60 class) and its disc: EST 4.0–4.5 lb and 2.5 lb (a 164-xxx disc
    // ships at 3 lb). The 10-in heavy wheel (Cleveland 40-134, 7.50x10): EST 13.5 lb, the original ABI-10650's weight
    // (the -LW is "over 8 lb lighter per set" than it, [ASW]); its HD disc 164-06306 EST 4 lb.
    private static readonly TireSpec[] Tires =
    {
        // ---- light-aircraft tyres, Type III --------------------------------------------------------------------
        T("5.00-5", "5.00-5 6-ply Flight Custom III (GY 505C66-5)", 14.2, 4.95, 5, 6, 5.5, 1.0, 3.0, 2.0, 18, 35, 50, 1285,
          "[GY] OD 14.2, w 4.95, 1285 lb @ 50 psi; tyre 5.5 lb [CD]; tube 1 lb [GYT]; wheel 3.0 lb, disc 2.0 lb EST; min 18 psi EST"),
        T("6.00-6", "6.00-6 6-ply Flight Custom III (GY 606C66-8)", 17.5, 6.3, 6, 6, 8.9, 1.0, 4.0, 2.5, 15, 30, 42, 1750,
          "[GY] OD 17.5, w 6.3, 1750 lb @ 42 psi; tyre 8.9 lb [CD]; tube 1 lb [GYT]; wheel 4.0 lb, disc 2.5 lb EST"),
        T("8.00-6", "8.00-6 6-ply Flight Special II (GY 806C61-5)", 19.5, 7.95, 6, 6, 12.3, 2.0, 4.5, 2.5, 12, 18, 35, 2050,
          "[GY] OD 19.5, w 7.95, SLR 7.55, 2050 lb @ 35 psi; tyre 12.3 lb [CD] (Air Hawk AB3J4 10.7); tube 2 lb [GYT]; wheel (Cleveland 40-60) 4.5 lb, disc (164-06700) 2.5 lb EST"),
        T("8.50-6", "8.50-6 6-ply Aircraft Rib, tubeless (GY 856T61-1)", 22.1, 8.85, 6, 6, 16.0, 0.0, 4.5, 2.5, 12, 18, 30, 2275,
          "[GY] OD 22.1, w 8.85, SLR 8.39, 2275 lb @ 30 psi; tyre ~16 lb (Aircraft Spruce, search snippet only — UNVERIFIED); wheel 4.5 lb, disc 2.5 lb EST"),
        T("8.00-4", "8.00-4 4-ply (GY 804C41-1)", 18.0, 8.3, 4, 4, 9.0, 2.0, 3.5, 2.0, 8, 15, 24, 1100,
          "[GY] OD 18.0, w 8.3, 1100 lb @ 24 psi; tyre ~9 lb [AS 06-06600]; tube 2 lb EST (the 7.00/8.00-6 tube); wheel 3.5 lb, disc 2.0 lb EST"),
        T("8.50-10", "8.50-10 8-ply Flight Custom III (GY 850C86-2)", 25.65, 8.7, 10, 8, 23.2, 3.0, 13.5, 4.0, 20, 30, 55, 4400,
          "[GY] OD 25.65, w 8.7, SLR 10.19, 4400 lb @ 55 psi; tyre 23.2 lb [CD]; tube 3 lb [GYT]; wheel (Cleveland 40-134) 13.5 lb, disc (164-06306) 4.0 lb EST"),
        // Scott 3200-class 8 in tailwheel on a 2.80/2.50-4: 430 lb rated [AS 06-08000]; the Scott 2600 8 in wheel and
        // tyre 3 lb 13 oz (univair.com, search snippet — UNVERIFIED); OD 8 in nominal; no brake.
        T("scott-8in", "8 in tailwheel, 2.80/2.50-4 4-ply (Scott 3200 class)", 8.0, 2.8, 4, 4, 2.0, 0.5, 1.3, 0, 10, 20, 35, 430,
          "tyre 430 lb rated [AS 06-08000]; wheel + tyre 3.8 lb (Scott 2600, univair snippet, UNVERIFIED); OD 8, w 2.8 nominal; rated psi EST"),

        // ---- Alaskan Bushwheels (tundra tyres; tubeless; TSO-C62d) ------------------------------------------------
        // Pressures 8–20 psi [ICA]; "typical" 12 psi is an ESTIMATE of common practice. Wheels: Cleveland 6.00-6 [ICA].
        T("26126.R", "Alaskan Bushwheel 26x12x6", 26, 12, 6, 0, 26, 0, 4.0, 2.5, 8, 12, 20, 0,
          "[MCF] 26 lb regular (heavy tread 29) [ICA 28]; 8–20 psi [ICA]; Cleveland 6.00-6 wheel 4.0 lb, disc 2.5 lb EST", tundra: true),
        T("29136.R", "Alaskan Bushwheel 29x13x6", 29, 13, 6, 0, 31, 0, 4.0, 2.5, 8, 12, 20, 0,
          "[MCF] 31 lb regular (heavy tread 34) [ICA 30]; 8–20 psi [ICA]; Cleveland 6.00-6 wheel 4.0 lb, disc 2.5 lb EST", tundra: true),
        T("31136.R", "Alaskan Bushwheel 31x13x6", 31, 13, 6, 0, 35, 0, 4.0, 2.5, 8, 12, 20, 0,
          "[MCF] 35 lb regular (heavy tread 39) [ICA 31]; 8–20 psi [ICA]; Cleveland 6.00-6 wheel 4.0 lb, disc 2.5 lb EST", tundra: true),
        // 35 in Super Cub lightweight, 3/16 in tread, on the ABI-10650-LW (9.6 lb, [ASW]) with a 164-03601 disc (EST
        // 2.5 lb). Pressure: 6–10 psi is how it is run on a Super Cub (Ty / parent brief); the range the brief gives for
        // it, 10–30, the ICA prints for the Beaver tyre. Min 6 is that operating practice.
        T("351510.R1", "Alaskan Bushwheel 35x15x10 Super Cub lightweight", 35, 15, 10, 0, 44, 0, 9.6, 2.5, 6, 8, 30, 0,
          "[MCF] 44 lb [ICA 48]; ABI-10650-LW wheel 9.6 lb [ASW]; disc 164-03601 2.5 lb EST; 6–10 psi operating (brief), 30 max", tundra: true),
        // 35 in Beaver heavy duty: 1/4 in tread, DHC-2 Mk I/II/III and PC-6 [ICA]; ply and carcass NOT published; on the
        // Cleveland 40-134 7.50x10 (tubeless) with HD brakes; 10–30 psi [ICA].
        T("351510.R", "Alaskan Bushwheel 35x15x10 Beaver heavy duty", 35, 15, 10, 0, 60, 0, 13.5, 4.0, 10, 12, 30, 0,
          "[MCF] 60 lb [ICA 60], 1/4 in tread; Cleveland 40-134 wheel 13.5 lb EST, HD disc 164-06306 4.0 lb EST; 10–30 psi [ICA]; typical 12 EST", tundra: true, hd: true),

        // ---- carried over unchanged (not researched in this lane) ---------------------------------------------
        L("legacy-aircam-main", "AirCam main", 0.21, 7.0, true, 16.1),
        L("legacy-aircam-amphib-main", "AirCam amphibian float main", 0.15, 3.5, false, 19.4),
        L("legacy-737-main", "737 main leg, twin H44.5x16.5-21", 0.55, 380, true, 205, count: 2),
        L("legacy-737-nose", "737 nose leg, twin 27x7.75-15", 0.343, 70, false, 190, count: 2),
        L("legacy-cassutt-main", "Cassutt main", 0.16, 4.0, true, 7.5),
        L("legacy-cassutt-tail", "Cassutt tailwheel", 0.065, 0.5, false, 6.0),
        L("legacy-sr22-main", "SR22 main, 15x6.00-6", 0.19, 7.0, true, 33.1),
        L("legacy-dc3-main", "DC-3 main", 0.58, 85, true, 255.5),
        L("legacy-dc3-tail", "DC-3 tailwheel", 0.25, 15, false, 204.4),
        L("legacy-beaver-tail", "DHC-2 tailwheel", 0.13, 2.5, false, 38),
        L("legacy-extra-main", "Extra 300 main", 0.155, 5.6, true, 18.1),
        L("legacy-extra-tail", "Extra 300 tailwheel", 0.071, 0.8, false, 14.5),
        L("legacy-f86-nose", "F-86 nose", 0.30, 15, false, 121.7),
        L("legacy-f86-main", "F-86 main", 0.33, 35, true, 152.1),
        L("legacy-geebee-main", "Gee Bee R-2 main", 0.28, 10, true, 28.7),
        L("legacy-geebee-tail", "Gee Bee R-2 tailwheel", 0.10, 1.5, false, 22.9),
        L("legacy-233-tail", "SGS 2-33 tailwheel", 0.075, 0.6, false, 6.2),
        // Grob 103 Acro (merged after this catalog was written): its config's radii, the base 160·r² mass and the
        // gross/100 × 0.8 rule (all three legs on the centreline), so it behaves exactly as before.
        L("legacy-grob103-main", "Grob 103 main", 0.20, 6.4, true, 8.6),
        L("legacy-grob103-nose", "Grob 103 nosewheel", 0.13, 2.704, false, 8.6),
        L("legacy-grob103-tail", "Grob 103 tailwheel", 0.105, 1.764, false, 8.6),
        L("legacy-eb29r-main", "EB29R main", 0.19, 6.0, true, 12.3),
        L("legacy-eb29r-tail", "EB29R tailwheel", 0.105, 1.0, false, 12.3),
        L("legacy-swift-main", "Swift S-1 main, 300x100", 0.15, 3.5, true, 5.8),
        L("legacy-swift-tail", "Swift S-1 tailwheel", 0.10, 0.6, false, 5.8),
        L("legacy-j3-tail", "J-3 tailwheel", 0.076, 0.7, false, 9.8),
        L("legacy-jn4-main", "JN-4 main, 26x4", 0.33, 9.0, false, 19.2),
        L("legacy-p51-main", "P-51D main", 0.34, 30, true, 101.4),
        L("legacy-p51-tail", "P-51D tailwheel", 0.16, 5, false, 81.1),
        L("legacy-pa18bush-tail", "Super Cub bush tailwheel", 0.20, 2.5, false, 8),
        L("legacy-pitts-main", "Pitts S-2B main", 0.17, 5.6, true, 16.2),
        L("legacy-pitts-tail", "Pitts S-2B tailwheel", 0.075, 0.8, false, 13.0),
        L("legacy-stearman-main", "Stearman main", 0.31, 13, true, 26.5),
        L("legacy-stearman-tail", "Stearman tailwheel", 0.11, 2.0, false, 21.2),
        L("legacy-drone-main", "Target drone main", 0.15, 2.5, true, 7.5),
        L("legacy-drone-tail", "Target drone tailwheel", 0.06, 0.4, false, 6.0),
    };

    private static readonly Dictionary<string, TireSpec> ById = Tires.ToDictionary(t => t.Id);

    public static IReadOnlyList<TireSpec> All => Tires;

    public static TireSpec? Get(string? id) => id != null && ById.TryGetValue(id, out TireSpec? t) ? t : null;

    /// <summary>The tyre on this leg, or null (a skid, feet, a float keel, a code-built leg with no tyre named).</summary>
    public static TireSpec? Of(GearConfig g) => string.IsNullOrEmpty(g.Tire) ? null : Get(g.Tire);

    /// <summary>The tyre on this leg if it has the data the tyre physics needs (not a legacy size), else null.</summary>
    public static TireSpec? Physical(GearConfig g) { TireSpec? t = Of(g); return t != null && t.HasCarcass ? t : null; }

    /// <summary>Is this id in the catalog?</summary>
    public static bool Has(string id) => ById.ContainsKey(id);

    /// <summary>
    /// The pressure that puts this tyre at its RATED deflection under <paramref name="loadN"/>: TR R-64 eq. 27, a Type
    /// III tyre at rated conditions carries F ≈ 0.57·p·w·√(w·d). That is how a tundra tyre is set up — pressure for
    /// the load — and it is held to the tyre's own range.
    /// </summary>
    public static double PressureForLoadPsi(TireSpec t, double loadN)
    {
        double wIn = t.WidthIn, dIn = t.OdIn;
        double psi = loadN / 4.448222 / t.Count / (0.57 * wIn * System.Math.Sqrt(wIn * dIn));
        return System.Math.Clamp(psi, t.MinPsi, t.MaxPsi);
    }

    /// <summary>
    /// The tundra tyre the bushwheel option fits when the leg names none: the lightest general tundra tyre that carries
    /// the leg's static load at its rated deflection without going above its typical pressure. The Super Cub
    /// lightweight 35 is left out (it is that aeroplane's tyre, not a general option). Null when none will do (a DC-3,
    /// a 737): those keep their own tyres.
    /// </summary>
    public static TireSpec? TundraFor(double staticLoadN)
    {
        TireSpec? best = null;
        foreach (TireSpec t in Tires)
        {
            if (!t.Tundra || t.Id == "351510.R1") continue;
            double need = staticLoadN / 4.448222 / (0.57 * t.WidthIn * System.Math.Sqrt(t.WidthIn * t.OdIn));
            if (need > t.TypicalPsi) continue;
            if (best == null || t.RotatingMassKg(true) < best.RotatingMassKg(true)) best = t;
        }
        return best;
    }
}

/// <summary>
/// The BUSHWHEELS option (<see cref="AircraftConfig.Bushwheels"/>) as a tyre change: each main wheel gets its
/// tundra tyre on the same axle. Real geometry follows: the axle stays where it is, so the contact point drops by
/// the difference in radius, the aeroplane sits higher and a taildragger sits more tail-low. Nose and tail wheels
/// keep their own tyres. Taking the option off restores exactly what was there.
/// </summary>
public static class TireFit
{
    /// <summary>Is this leg a main wheel for the option: a wheel, not the tail, not a steerable nose.</summary>
    public static bool IsMainWheel(GearConfig g) => WheelSpin.IsWheel(g) && !g.IsTailwheel && !g.IsSteerable;

    public static void Apply(AircraftConfig c, bool on)
    {
        if (c.Gear == null) return;
        int mains = 0; foreach (GearConfig g in c.Gear) if (IsMainWheel(g)) mains++;
        double massKg = c.Mass != null ? c.Mass.MassKg : 800;
        double staticN = massKg * 9.81 * 0.88 / System.Math.Max(1, mains);
        foreach (GearConfig g in c.Gear)
        {
            if (!IsMainWheel(g)) continue;
            if (on && !g.TundraFitted)
            {
                TireSpec? stock = TireCatalog.Of(g);
                TireSpec? tundra = TireCatalog.Get(g.TundraTire) ?? (stock != null && stock.Tundra ? stock : TireCatalog.TundraFor(staticN));
                if (tundra == null || tundra == stock && g.TundraPressurePsi <= 0) continue;   // nothing to fit, or already on it
                g.StockTire = g.Tire; g.StockPosZ = g.Pos[2]; g.StockPressurePsi = g.TirePressurePsi;
                double oldR = LandingGear.TireRadiusOf(c, g);
                g.Tire = tundra.Id;
                g.TirePressurePsi = g.TundraPressurePsi > 0 ? g.TundraPressurePsi : TireCatalog.PressureForLoadPsi(tundra, staticN);
                g.Pos[2] += tundra.RadiusM - oldR;
                g.TundraFitted = true;
            }
            else if (!on && g.TundraFitted)
            {
                g.Tire = g.StockTire; g.Pos[2] = g.StockPosZ; g.TirePressurePsi = g.StockPressurePsi;
                g.TundraFitted = false;
            }
        }
    }
}
