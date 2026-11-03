using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>What a wing panel has been through: whole, permanently bent by a medium strike, or gone.</summary>
public enum PanelState { Intact, Bent, Broken }

/// <summary>
/// One wing's outboard panel on one side: the part beyond its STRUCTURAL station (Ty 2026-11-03) — a biplane's
/// interplane strut, a strut-braced wing's lift-strut attach, a cantilever wing's aileron inboard end, or the station
/// the config measured (<see cref="AircraftConfig.WingBreakStationY"/>). A wingtip strike loads it as a cantilever
/// about that station; a medium strike bends it, a hard one breaks it off.
/// </summary>
public sealed class WingPanel
{
    public AirframeComponent Component;
    /// <summary>The wing surface the panel belongs to ("wing", "wing-upper", "wing-lower").</summary>
    public string SurfaceId = "";
    public int Side;              // -1 left, +1 right
    public bool Lower;            // a biplane's lower wing
    public double BreakY;         // |y| of the station, m
    public double BreakZ;         // chord-line z at the station: the bend axis runs along x through (BreakY, BreakZ)
    public double TipY;           // |y| of the outer edge
    public string StationSource = "";
    /// <summary>Strip indices (AeroModel order) outboard of the station: the wing's rows and its control rows.</summary>
    public int[] Strips = System.Array.Empty<int>();
    /// <summary>The hinged aileron rows of this wing and side. A hinge line cannot cross a missing panel, so the
    /// whole aileron leaves with the panel even where it runs inboard of the station.</summary>
    public int[] AileronHinged = System.Array.Empty<int>();
    /// <summary>Fixed rows of this wing and side that carry the aileron's camber effect (gain &lt; 0.9).</summary>
    public int[] AileronFixed = System.Array.Empty<int>();
    public double AreaM2;

    // Strength (WingStructure.EnsureStrength).
    /// <summary>Bending moment the air puts on the station in 1 g flight, N·m (Schrenk lift less inertia relief).</summary>
    public double M1Nm;
    /// <summary>Limit (gMax × M1: permanent set beyond it, 14 CFR 23.305) and ultimate (× ultimateFactor) moments.</summary>
    public double LimitNm, UltimateNm;
    public double MassKg;
    /// <summary>A braced wing (biplane, strut-braced): the spar runs on through the station at the section its inboard
    /// bay needs, so the station is designed by the larger of M1 and that bay's moment (<see cref="DesignNm1"/>).</summary>
    public bool Braced;
    /// <summary>The moment per g the station's spar section was sized for, N·m.</summary>
    public double DesignNm1;
    /// <summary>The panel's own bending stiffness at its tip, N/m (3EI/L³), in series with the contact spring.</summary>
    public double TipStiffnessNPerM;
    public bool StrengthKnown;

    public PanelState State;
    /// <summary>Signed deflection about the station, rad: + = tip UP. Zero while intact.</summary>
    public double BendRad;
    /// <summary>The ELASTIC rotation of the panel about its station under the contact force right now, rad (+ = tip up):
    /// the tip's flex F/k_w, as a cantilever's tip slope 1.5·δ/L. It comes off with the load; <see cref="BendRad"/> stays.</summary>
    public double ElasticRad;
    /// <summary>Largest contact bending moment the station has carried, N·m (tests / probe).</summary>
    public double PeakContactNm;

    /// <summary>Rotate a body point outboard of the station about the bend axis by <paramref name="rad"/> (+ = tip up).</summary>
    public Vec3 Bend(Vec3 p, double rad)
    {
        if (rad == 0 || System.Math.Sign(p.Y) != Side || System.Math.Abs(p.Y) <= BreakY) return p;
        double dy = p.Y - Side * BreakY, dz = p.Z - BreakZ, c = System.Math.Cos(rad), s = System.Math.Sin(rad);
        return new Vec3(p.X, Side * BreakY + dy * c + Side * dz * s, BreakZ + dz * c - Side * dy * s);
    }
}

/// <summary>
/// The wing panels of an aeroplane, their strengths, and the bent-panel law.
///
/// STRENGTH (first principles, the same derivation as the water-impact joints in Wreck/AirframeHydroModel). In 1 g flight
/// the air bends the station by M1 = g·(½·W·(A_wing/A_all)·arm − Σ m_strip·(|y| − y_s)): the side's share of the weight
/// carried by THIS wing (a biplane's two wings split it by area), spread spanwise by Schrenk (NACA TM 948: the mean of
/// the planform and elliptic loadings), less the inertia relief of the panel's own mass (Raymer 12.2 kg/m², scaled with
/// the rest of the airframe onto the config's empty mass). A structure built exactly to its rating carries gMax·M1
/// without permanent set (the LIMIT, 14 CFR 23.305) and gMax·ultimateFactor·M1 before it breaks (the ULTIMATE; the game's
/// ultimate factor, the same one the in-flight wing failure uses). A ground strike is checked against those moments.
///
/// BENT PANEL (Ty 2026-11-03). A strike between the limit and the ultimate leaves the panel bent 5° tip-up (down, struck
/// from above) and much weaker: in flight its deflection follows the load, 5° at 1 g rising to 15° at 2 g, and past 15°
/// it snaps off. Read as a damaged spar, that is a plastic set of 5° held by a tangent stiffness K_t = M1 / 10°, and the
/// set ratchets — a panel taken to 1.5 g stays at 10° when the load comes off. Below the deflection it already has it is
/// treated as rigid (a real spar unloads along its elastic slope, a fraction of a degree here; see the log).
/// </summary>
public static class WingStructure
{
    public const double BentSetRad = 5.0 * System.Math.PI / 180.0;
    public const double SnapRad = 15.0 * System.Math.PI / 180.0;
    /// <summary>The load factor at which a bent panel reaches <see cref="SnapRad"/> (Ty: 2 g).</summary>
    public const double SnapLoadFactor = 2.0;
    /// <summary>A bent panel's deflection per g beyond 1 g: (15° − 5°) / (2 g − 1 g).</summary>
    public const double TangentRadPerG = (SnapRad - BentSetRad) / (SnapLoadFactor - 1.0);

    /// <summary>A bent panel's deflection magnitude at load factor <paramref name="n"/> (in the sense that opens the
    /// bend), given the most it has reached so far.</summary>
    public static double BentDeflection(double n, double peakRad) => System.Math.Max(System.Math.Abs(peakRad), BentSetRad + TangentRadPerG * (n - 1.0));

    /// <summary>The wing surfaces ("wing", "wing-upper", "wing-lower"): ids with "wing" and no control-row suffix.</summary>
    public static bool IsWingBase(string id)
    {
        string l = id.ToLowerInvariant();
        return l.Contains("wing") && !l.Contains("aileron") && !l.Contains("flap") && !l.Contains("slat") && !l.Contains("spoiler");
    }

    /// <summary>The rows belonging to wing <paramref name="baseId"/>: itself and every surface named baseId-…</summary>
    public static bool BelongsTo(string id, string baseId, AircraftConfig c)
    {
        if (id == baseId) return true;
        if (!id.StartsWith(baseId + "-", System.StringComparison.OrdinalIgnoreCase)) return false;
        foreach (SurfaceConfig s in c.Surfaces) if (s.Id != baseId && IsWingBase(s.Id) && s.Id.Length > baseId.Length && id.StartsWith(s.Id, System.StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Whether a surface's strip heights already contain its dihedral. The own-model biplanes' lower wings do (the Pitts's
    /// runs z 0.528 at the root to 0.408 at the tip, as a dihedral of 0.0524 rad says it should); every other config keeps
    /// the strips at one height and leaves the dihedral to <see cref="StripConfig.DihedralRad"/> alone.
    /// </summary>
    public static bool DihedralInPositions(SurfaceConfig sf)
    {
        StripConfig? inner = null, outer = null;
        foreach (StripConfig st in sf.Strips)
        {
            if (st.Pos[1] <= 0) continue;
            if (inner is null || st.Pos[1] < inner.Pos[1]) inner = st;
            if (outer is null || st.Pos[1] > outer.Pos[1]) outer = st;
        }
        if (inner is null || outer is null || inner == outer || System.Math.Abs(outer.DihedralRad) < 1e-4) return false;
        double expected = (outer.Pos[1] - inner.Pos[1]) * System.Math.Tan(outer.DihedralRad);
        return System.Math.Abs((inner.Pos[2] - outer.Pos[2]) - expected) < 0.5 * System.Math.Abs(expected);
    }

    /// <summary>The chord line's height (body z) at span |y| = <paramref name="absY"/>, from strip <paramref name="st"/> of
    /// <paramref name="sf"/>: its own height carried along its dihedral when the heights include it, otherwise risen
    /// from the centreline by |y|·tan Γ.</summary>
    public static double ChordZ(SurfaceConfig sf, StripConfig st, double absY) =>
        DihedralInPositions(sf) ? st.Pos[2] - (absY - System.Math.Abs(st.Pos[1])) * System.Math.Tan(st.DihedralRad)
                                : st.Pos[2] - absY * System.Math.Tan(st.DihedralRad);

    public static bool IsHingedAileron(StripConfig st) => st.Control is { Surface: "aileron" } && System.Math.Abs(st.Control.Gain) >= 0.9;
    public static double Width(StripConfig st) => st.Chord > 1e-6 ? st.Area / st.Chord : 0.3;

    /// <summary>The panels, in a fixed order (wing surfaces in config order, left then right). Empty for a wing with no
    /// spar to break (weight shift) or an aeroplane with no ailerons at all (a paraglider canopy).</summary>
    public static List<WingPanel> Build(AircraftConfig c)
    {
        var panels = new List<WingPanel>();
        if (c.WeightShift is not null) return panels;
        bool anyAileron = false;
        foreach (SurfaceConfig sf in c.Surfaces) foreach (StripConfig st in sf.Strips) if (IsHingedAileron(st)) anyAileron = true;
        if (!anyAileron) return panels;

        var start = new Dictionary<SurfaceConfig, int>();
        int n = 0;
        foreach (SurfaceConfig sf in c.Surfaces) { start[sf] = n; n += sf.Strips.Count; }
        var bases = c.Surfaces.Where(s => IsWingBase(s.Id) && s.Strips.Count > 0).ToList();
        bool biplane = bases.Count > 1;

        // The station of each wing: measured, else the aileron's inboard end, else (a biplane wing with no aileron) the
        // other wing's station, else the generic outer-panel fraction.
        var station = new Dictionary<string, (double y, string why)>();
        foreach (SurfaceConfig b in bases)
        {
            if (c.WingBreakStationY is { } m && m.TryGetValue(b.Id, out double y) && y > 0) { station[b.Id] = (y, "measured (config wingBreakStationY)"); continue; }
            double inb = double.MaxValue;
            foreach (SurfaceConfig sf in c.Surfaces)
                if (BelongsTo(sf.Id, b.Id, c))
                    foreach (StripConfig st in sf.Strips) if (IsHingedAileron(st)) inb = System.Math.Min(inb, System.Math.Abs(st.Pos[1]) - 0.5 * Width(st));
            if (inb < double.MaxValue) station[b.Id] = (inb, "aileron inboard end");
        }
        foreach (SurfaceConfig b in bases)
        {
            if (station.ContainsKey(b.Id)) continue;
            var other = station.Values.Where(v => v.why != "").ToList();
            station[b.Id] = other.Count > 0 ? (other[0].y, "the other wing's station (no aileron on this one)") : (WingPanels.OuterFraction * WingPanels.Semispan(c), "outer-panel fraction (no aileron)");
        }

        foreach (SurfaceConfig b in bases)
        {
            bool lower = biplane && b.Id.ToLowerInvariant().Contains("lower");
            (double ys, string why) = station[b.Id];
            foreach (int side in new[] { -1, 1 })
            {
                var strips = new List<int>(); var hinged = new List<int>(); var fixedAil = new List<int>();
                double area = 0, tip = 0;
                foreach (SurfaceConfig sf in c.Surfaces)
                {
                    if (!BelongsTo(sf.Id, b.Id, c)) continue;
                    for (int i = 0; i < sf.Strips.Count; i++)
                    {
                        StripConfig st = sf.Strips[i];
                        if (System.Math.Sign(st.Pos[1]) != side) continue;
                        int gi = start[sf] + i;
                        if (IsHingedAileron(st)) hinged.Add(gi);
                        else if (st.Control is { Surface: "aileron" }) fixedAil.Add(gi);
                        if (System.Math.Abs(st.Pos[1]) > ys) { strips.Add(gi); area += st.Area; }
                        tip = System.Math.Max(tip, System.Math.Abs(st.Pos[1]) + 0.5 * Width(st));
                    }
                }
                if (strips.Count == 0) continue;
                // The chord line's height at the station, from the base strip nearest it and that strip's dihedral.
                StripConfig near = b.Strips[0];
                foreach (StripConfig st in b.Strips)
                    if (System.Math.Sign(st.Pos[1]) == side && (System.Math.Sign(near.Pos[1]) != side || System.Math.Abs(System.Math.Abs(st.Pos[1]) - ys) < System.Math.Abs(System.Math.Abs(near.Pos[1]) - ys))) near = st;
                double zs = ChordZ(b, near, ys);
                panels.Add(new WingPanel
                {
                    Component = lower ? (side < 0 ? AirframeComponent.WingTipLowerLeft : AirframeComponent.WingTipLowerRight)
                                      : (side < 0 ? AirframeComponent.WingTipLeft : AirframeComponent.WingTipRight),
                    SurfaceId = b.Id, Side = side, Lower = lower, BreakY = ys, BreakZ = zs, TipY = tip, StationSource = why,
                    Braced = biplane || why.StartsWith("measured") || !string.IsNullOrEmpty(c.WingBracing),
                    Strips = strips.ToArray(), AileronHinged = hinged.ToArray(), AileronFixed = fixedAil.ToArray(), AreaM2 = area,
                });
            }
        }
        return panels;
    }

    /// <summary>Fill in M1, the limit and ultimate moments and the panel mass (see the class summary).
    /// <paramref name="structureScale"/> is the factor that lands Raymer's buildup on the config's empty mass.</summary>
    public static void EnsureStrength(List<WingPanel> panels, AircraftConfig c, double structureScale)
    {
        if (panels.Count == 0 || panels[0].StrengthKnown) return;
        var all = new List<StripConfig>();
        foreach (SurfaceConfig sf in c.Surfaces) all.AddRange(sf.Strips);
        double wingArea = 0;
        foreach (SurfaceConfig sf in c.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (StripConfig st in sf.Strips) wingArea += st.Area;
        double g = Atmosphere.GravityMs2, w = c.Mass.MassKg;
        double gMax = c.Limits.GMax > 0 ? c.Limits.GMax : 3.8;
        double uf = c.Limits.UltimateFactor > 0 ? c.Limits.UltimateFactor : 1.5;
        double kgPerM2 = structureScale * Wreck.WaterPhysics.RaymerWingKgPerM2;
        double strength = c.ImpactStrength > 0 ? c.ImpactStrength : 1.0;
        foreach (WingPanel p in panels)
        {
            double planeArea = 0, sideArea = 0, planArm = 0, semi = 0;
            foreach (SurfaceConfig sf in c.Surfaces)
            {
                if (!BelongsTo(sf.Id, p.SurfaceId, c)) continue;
                foreach (StripConfig st in sf.Strips)
                {
                    planeArea += st.Area;
                    semi = System.Math.Max(semi, System.Math.Abs(st.Pos[1]) + 0.5 * Width(st));
                    if (System.Math.Sign(st.Pos[1]) != p.Side) continue;
                    sideArea += st.Area;
                    if (System.Math.Abs(st.Pos[1]) > p.BreakY) planArm += st.Area * (System.Math.Abs(st.Pos[1]) - p.BreakY);
                }
            }
            double arm = sideArea > 0 ? 0.5 * (planArm / sideArea + Wreck.AirframeHydroModel.EllipticArm(p.BreakY, semi)) : 0;
            double mass = 0, relief = 0;
            foreach (int i in p.Strips) { double m = kgPerM2 * all[i].Area; mass += m; relief += m * (System.Math.Abs(all[i].Pos[1]) - p.BreakY); }
            double share = wingArea > 0 ? planeArea / wingArea : 1.0;
            double m1 = g * (0.5 * w * share * arm - relief);
            if (m1 <= 0) m1 = g * 0.5 * w * share * arm;   // a panel heavier than its own lift: still give it a strength
            // A braced spar is one section from the root to the strut and on to the tip: the bay between the root and the
            // strut, a beam on two supports under its share of the lift (planform-spread), needs q·L²/8 of it.
            double design = m1;
            if (p.Braced)
            {
                double rootY = double.MaxValue, bayArea = 0;
                foreach (SurfaceConfig sf in c.Surfaces)
                {
                    if (!BelongsTo(sf.Id, p.SurfaceId, c)) continue;
                    foreach (StripConfig st in sf.Strips)
                    {
                        if (System.Math.Sign(st.Pos[1]) != p.Side) continue;
                        if (sf.Id == p.SurfaceId) rootY = System.Math.Min(rootY, System.Math.Abs(st.Pos[1]) - 0.5 * Width(st));
                        if (System.Math.Abs(st.Pos[1]) <= p.BreakY) bayArea += st.Area;
                    }
                }
                double bay = p.BreakY - System.Math.Max(0.0, rootY);
                if (bay > 0 && sideArea > 0)
                {
                    double lift = g * 0.5 * w * share * bayArea / sideArea;
                    design = System.Math.Max(m1, lift * bay / 8.0);
                }
            }
            p.M1Nm = m1;
            p.DesignNm1 = design;
            p.LimitNm = gMax * design * strength;
            p.UltimateNm = gMax * uf * design * strength;
            p.MassKg = mass;
            // EI of a spar that fails at the ultimate moment: I/c = M_ult/σ_u, c = h/2 → EI = (E/σ_u)·M_ult·h/2.
            double h = DepthAt(p, c), len = p.TipY - p.BreakY;
            if (h > 0 && len > 0) p.TipStiffnessNPerM = 3.0 * (p.UltimateNm * h / (2.0 * SparSigmaOverE)) / (len * len * len);
            p.StrengthKnown = true;
        }
    }

    /// <summary>Ultimate strength over Young's modulus of a wing spar: about 7·10⁻³ for Sitka spruce (MOR 9,400 psi,
    /// E 1.3·10⁶ psi, ANC-18) and 6·10⁻³ for 2024-T3 (440 MPa / 72 GPa) — one figure serves wood and aluminium alike.</summary>
    public const double SparSigmaOverE = 7.2e-3;

    /// <summary>The wing's depth at the panel's station (airfoil thickness × chord of the base strip nearest it), m.</summary>
    public static double DepthAt(WingPanel p, AircraftConfig c)
    {
        double depth = 0, bestD = double.MaxValue;
        foreach (SurfaceConfig sf in c.Surfaces)
            if (sf.Id == p.SurfaceId)
                foreach (StripConfig st in sf.Strips)
                {
                    if (System.Math.Sign(st.Pos[1]) != p.Side) continue;
                    double d = System.Math.Abs(System.Math.Abs(st.Pos[1]) - p.BreakY);
                    if (d < bestD) { bestD = d; depth = Wreck.AirframeHydroModel.ThicknessRatio(st.Airfoil) * st.Chord; }
                }
        return depth;
    }

    /// <summary>
    /// The INTACT panel's own rotation at the station in 1 g flight, rad — to compare with the bent-panel law. The spar's
    /// EI = (E/σ_u)·M_ult·h/2 (see <see cref="EnsureStrength"/>); a panel of length L under a moment falling from M1 at
    /// the station to zero at the tip turns about θ ≈ M1·L/(2EI) = (σ_u/E)·M1·L/(M_ult·h).
    /// </summary>
    public static double IntactRotationAt1g(WingPanel p, AircraftConfig c)
    {
        double h = DepthAt(p, c);
        if (h <= 0 || p.UltimateNm <= 0) return double.NaN;
        return SparSigmaOverE * p.M1Nm * (p.TipY - p.BreakY) / (p.UltimateNm * h);
    }
}

/// <summary>
/// A biplane's aileron slave strut on one side, as the rigid link it is: a fixed-length strut between a pin on a horn of
/// the driven (stick) aileron and a pin on a horn of the slaved one. Each aileron turns about its own hinge line; the
/// slaved aileron goes to the deflection that keeps the strut its length (<see cref="Solve"/>), which with both wings
/// whole and level is very nearly the driven one's. When a wing panel BENDS the pins and hinges outboard of its station
/// move with it, and the strut drives whatever the geometry gives; when either aileron leaves, the link is gone.
/// </summary>
public sealed class SlaveLink
{
    public int Side;
    public string DrivenSurface = "", SlavedSurface = "";
    /// <summary>Hinged strips of each aileron on this side; their gain signs (local deflection = sign × axis value).</summary>
    public int[] DrivenRows = System.Array.Empty<int>(), SlavedRows = System.Array.Empty<int>();
    public double DrivenSign = 1, SlavedSign = 1;
    public Vec3 DrivenHinge, DrivenAxis, DrivenPin, SlavedHinge, SlavedAxis, SlavedPin;
    public double LengthM;
    public bool Connected = true;
    private double _rotSignD = 1, _rotSignS = 1;

    /// <summary>The links described by <see cref="AircraftConfig.SlaveStrut"/> (none without one, or when either
    /// aileron has no hinged rows on a side).</summary>
    public static List<SlaveLink> Build(AircraftConfig c)
    {
        var links = new List<SlaveLink>();
        SlaveStrutConfig? cfg = c.SlaveStrut;
        if (cfg is null) return links;
        var start = new Dictionary<string, int>(); int n = 0;
        foreach (SurfaceConfig sf in c.Surfaces) { start[sf.Id] = n; n += sf.Strips.Count; }
        SurfaceConfig? d = c.Surfaces.FirstOrDefault(s => s.Id == cfg.DrivenSurface), sl = c.Surfaces.FirstOrDefault(s => s.Id == cfg.SlavedSurface);
        if (d is null || sl is null) return links;
        foreach (int side in new[] { -1, 1 })
        {
            var link = new SlaveLink { Side = side, DrivenSurface = d.Id, SlavedSurface = sl.Id };
            if (!Hinge(d, start[d.Id], side, cfg.DrivenY, out link.DrivenHinge, out link.DrivenAxis, out link.DrivenRows, out link.DrivenSign)) continue;
            if (!Hinge(sl, start[sl.Id], side, cfg.SlavedY, out link.SlavedHinge, out link.SlavedAxis, out link.SlavedRows, out link.SlavedSign)) continue;
            // Both horns stand perpendicular to the strut in the x–z plane, pointing forward: n = (−u_z, 0, u_x) for the
            // strut direction u, so the two pins move ALONG the strut by the same amount for the same deflection.
            Vec3 u = link.SlavedHinge - link.DrivenHinge;
            Vec3 nrm = new Vec3(-u.Z, 0, u.X).Normalized();
            if (nrm.X < 0) nrm = nrm * -1.0;
            link.DrivenPin = link.DrivenHinge + nrm * cfg.HornArmM;
            link.SlavedPin = link.SlavedHinge + nrm * cfg.HornArmM;
            link.LengthM = (link.SlavedPin - link.DrivenPin).Length;
            link._rotSignD = TeDownSign(link.DrivenHinge, link.DrivenAxis);
            link._rotSignS = TeDownSign(link.SlavedHinge, link.SlavedAxis);
            links.Add(link);
        }
        return links;
    }

    /// <summary>The hinge line of one aileron on one side (the leading edge of its hinged rows) and the point on it at |y|.</summary>
    private static bool Hinge(SurfaceConfig sf, int start, int side, double absY, out Vec3 at, out Vec3 axis, out int[] rows, out double sign)
    {
        var pts = new List<(Vec3 p, double chord, double tc)>(); var idx = new List<int>(); sign = 1;
        for (int i = 0; i < sf.Strips.Count; i++)
        {
            StripConfig st = sf.Strips[i];
            if (System.Math.Sign(st.Pos[1]) != side || !WingStructure.IsHingedAileron(st)) continue;
            pts.Add((new Vec3(st.Pos[0] + 0.25 * st.Chord, st.Pos[1], st.Pos[2]), st.Chord, 0.0));
            idx.Add(start + i); sign = System.Math.Sign(st.Control!.Gain);
        }
        rows = idx.ToArray(); at = Vec3.Zero; axis = new Vec3(0, side, 0);
        if (pts.Count == 0) return false;
        pts.Sort((a, b) => System.Math.Abs(a.p.Y).CompareTo(System.Math.Abs(b.p.Y)));
        if (pts.Count > 1) axis = (pts[^1].p - pts[0].p).Normalized();
        // Along the hinge line to |y|, from the nearest row.
        var nearest = pts.OrderBy(q => System.Math.Abs(System.Math.Abs(q.p.Y) - absY)).First();
        double t = axis.Y != 0 ? (side * absY - nearest.p.Y) / axis.Y : 0;
        at = nearest.p + axis * t;
        return true;
    }

    private static double TeDownSign(Vec3 hinge, Vec3 axis)
    {
        Vec3 te = hinge + new Vec3(-0.1, 0, 0);
        return Rotate(te, hinge, axis, 0.1).Z > te.Z ? 1.0 : -1.0;
    }

    /// <summary>Rodrigues: <paramref name="p"/> turned by <paramref name="rad"/> about the line through <paramref name="o"/> along unit <paramref name="k"/>.</summary>
    public static Vec3 Rotate(Vec3 p, Vec3 o, Vec3 k, double rad)
    {
        Vec3 v = p - o; double c = System.Math.Cos(rad), s = System.Math.Sin(rad);
        return o + v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c));
    }

    /// <summary>
    /// The slaved aileron's LOCAL deflection (+ = trailing edge down) that keeps the strut its length, for the driven one
    /// at <paramref name="drivenLocalRad"/> and the panels that carry each end bent by <paramref name="bend"/> (null =
    /// straight). Searched within ±<paramref name="limitRad"/> for the root nearest <paramref name="guessRad"/>; a geometry
    /// with no root in range (the strut would have to stretch) pins the slaved aileron at the nearer end of its travel.
    /// </summary>
    public double Solve(double drivenLocalRad, Func<Vec3, Vec3>? bendDriven, Func<Vec3, Vec3>? bendSlaved, double guessRad, double limitRad)
    {
        Vec3 hd = bendDriven?.Invoke(DrivenHinge) ?? DrivenHinge;
        Vec3 ad = bendDriven != null ? (bendDriven(DrivenHinge + DrivenAxis) - hd).Normalized() : DrivenAxis;
        Vec3 pd0 = bendDriven?.Invoke(DrivenPin) ?? DrivenPin;
        Vec3 pd = Rotate(pd0, hd, ad, _rotSignD * drivenLocalRad);
        Vec3 hs = bendSlaved?.Invoke(SlavedHinge) ?? SlavedHinge;
        Vec3 asx = bendSlaved != null ? (bendSlaved(SlavedHinge + SlavedAxis) - hs).Normalized() : SlavedAxis;
        Vec3 ps0 = bendSlaved?.Invoke(SlavedPin) ?? SlavedPin;
        double F(double ds) => (Rotate(ps0, hs, asx, _rotSignS * ds) - pd).Length - LengthM;
        const int N = 48;
        double best = double.NaN, bestDist = double.MaxValue, prevX = -limitRad, prevF = F(prevX);
        for (int i = 1; i <= N; i++)
        {
            double x = -limitRad + 2 * limitRad * i / N, f = F(x);
            if (System.Math.Sign(f) != System.Math.Sign(prevF) || f == 0)
            {
                double lo = prevX, hi = x, flo = prevF;
                for (int k = 0; k < 40; k++)
                {
                    double mid = 0.5 * (lo + hi), fm = F(mid);
                    if (System.Math.Sign(fm) == System.Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
                }
                double root = 0.5 * (lo + hi);
                if (System.Math.Abs(root - guessRad) < bestDist) { bestDist = System.Math.Abs(root - guessRad); best = root; }
            }
            prevX = x; prevF = f;
        }
        if (!double.IsNaN(best)) return best;
        return System.Math.Abs(F(-limitRad)) < System.Math.Abs(F(limitRad)) ? -limitRad : limitRad;
    }
}
