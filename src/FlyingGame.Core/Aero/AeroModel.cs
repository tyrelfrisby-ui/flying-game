using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// Strip-theory aerodynamic model. Every lifting surface is a list of spanwise strips (from
/// AircraftConfig); each strip gets its own local velocity (body velocity + ω×r + wind), its own local
/// angle of attack (including any control-surface deflection via that strip's gain), and looks up
/// Cl/Cd/Cm from its airfoil's full ±180° table. Nothing about roll damping, adverse yaw, or stall/spin
/// behaviour is special-cased — it all falls out of this per-strip loop, per ARCHITECTURE.md.
/// </summary>
public static class AeroModel
{
    private const double MinSpeedMs = 1e-4;
    private const double MinInducedAspectRatio = 0.5;
    public const double ElevatorSaturationRad = 18.0 * Math.PI / 180.0; // below this, treat as a low-AR body: table drag only

    /// <summary>
    /// Geometric aspect ratio b²/S from the strip layout: span from strip-center extent plus half a
    /// strip width (area/chord) at each tip, area as the strip sum. Single-strip surfaces get AR from
    /// that strip alone (width²/area).
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _span = new();
    /// <summary>Wing span (m) and the wing's area-weighted mean body z (+ down), cached per config.</summary>
    public static double WingSpan(AircraftConfig c) => WingGeom(c)[0];

    /// <summary>
    /// How far the wing sits above the CG (metres, + up), which is the geometry ground effect needs. The CG is read
    /// LIVE and only the wing's own body z is cached: weight and balance moves the CG vertically in flight (wing fuel
    /// against low baggage), and this used to cache the difference — so a loaded aircraft would have flown ground
    /// effect at its empty wing height, silently, in the flare only.
    /// </summary>
    public static double WingHeightAboveCg(AircraftConfig c)
    {
        double wingZ = WingGeom(c)[1];
        return double.IsNaN(wingZ) ? 0 : c.Mass.CgVec().Z - wingZ;
    }
    private static double[] WingGeom(AircraftConfig c) => _span.GetValue(c, cfg =>
    {
        double maxY = 0, zSum = 0, aSum = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            if (!sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (StripConfig st in sf.Strips) { Vec3 p = st.PosVec(); maxY = Math.Max(maxY, Math.Abs(p.Y) + 0.5 * st.Area / Math.Max(0.1, st.Chord)); zSum += p.Z * st.Area; aSum += st.Area; }
        }
        return new[] { 2 * maxY, aSum > 0 ? zSum / aSum : double.NaN };
    });

    /// <summary>
    /// Aspect ratio of the LIFTING SYSTEM a surface belongs to (owner 2026-10-06: "I think your induced drag curve is off"):
    /// a wing and its aileron panels, a stabiliser and its elevator, a fin and its rudder are one lifting surface with one
    /// trailing-vortex system, so induced drag uses b² / (total area of the group). Each config splits them into separate
    /// surfaces; computing AR per piece gave the main wing AR ≈ 8.4 instead of the 172's 7.5 and the aileron strips
    /// (tip-to-tip span over 1.9 m²) an AR over 50 — almost no induced drag outboard — which put the 172's best glide at
    /// 60 kt instead of the POH's 65 and flattened the drag rise below it (the long float).
    /// Groups: "X-aileron" → "X" (wing, wing-upper, wing-lower kept apart: each biplane wing is its own system),
    /// "elevator" → "hStab", "rudder…" → "vStab".
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _systemAr = new();
    public static double SystemAspectRatio(AircraftConfig config, int surfaceIndex, bool isVertical) =>
        _systemAr.GetValue(config, cfg =>
        {
            var ar = new double[cfg.Surfaces.Count];
            for (int i = 0; i < cfg.Surfaces.Count; i++)
            {
                string key = SystemKey(cfg.Surfaces[i].Id);
                bool vert = cfg.Surfaces[i].Id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || cfg.Surfaces[i].Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);
                var group = new SurfaceConfig { Id = key, Strips = new List<StripConfig>() };
                foreach (SurfaceConfig other in cfg.Surfaces)
                    if (SystemKey(other.Id) == key) group.Strips.AddRange(other.Strips);
                ar[i] = GeometricAspectRatio(group, vert);
            }
            return ar;
        })[surfaceIndex];

    /// <summary>
    /// HINGED-SECTION tails (owner 2026-10-06: "research both control power and stick free control position now that the
    /// hinged surface model has been adopted"). A stabiliser and its elevator — a fin and its rudder — are ONE section:
    /// the hinged strips fly the fixed surface's airfoil (they used a separate low-AR table with a different lift slope
    /// and stall, so the elevator's share of the section lift was wrong), and when that airfoil is a 2-D section table
    /// (slope ≥ 5.5/rad: every hStab and most fins were sampling naca0012 at 6.3/rad) it is rescaled to the system's
    /// finite-span slope — DATCOM/Helmbold a = 2πA / (2 + √(A²/κ² + 4)), κ = a₀/2π; fins take A × 1.55 (end-plate effect
    /// of the stab and fuselage). The 2-D slope made every type's tail ~1.7× too strong: stick-fixed static margins of
    /// 25–70 % MAC (real light aircraft 5–20 %), over-damped pitch, and elevator power riding on the stab's inflated slope.
    /// WINGS go through the same test (2026-11-01). When this was written every wing polar in the fleet was
    /// finite-span (clarkY-like 4.47/rad) so wings were skipped; nine wing surfaces authored or rebuilt since
    /// carry 2-D section data instead — Pitts 6.30, Great Lakes 6.19, Extra / Cassutt / Swift S-1 / drone 5.82,
    /// 737 5.53 — and on a wing a 2-D slope overstates the lift AND the roll damping together. Note what that
    /// means for roll RATE: steady p is aileron power over roll damping and both scale with the section slope,
    /// so the error largely CANCELS in the steady rate and shows up in the time constant instead (the Pitts'
    /// time to 90 % went 0.35 s to 0.50 s when this was applied). It is the lift, the stall and the damping
    /// that were wrong, not primarily the roll rate.
    /// Biplanes take each wing's own geometric AR, because wing-upper and wing-lower are separate systems here.
    /// Interference between the two (Munk's biplane theory: each wing in the other's upwash/downwash behaves as
    /// though of lower effective AR) is NOT modelled and would reduce both wings' slopes further.
    /// Returns the table key for a tail surface's strips, or null (use the strip's own airfoil).
    /// </summary>
    public static string? SectionTableKey(AircraftConfig config, int surfaceIndex) => TailSections(config).Keys[surfaceIndex];

    /// <summary>The derived finite-span section tables, keyed as <see cref="SectionTableKey"/> returns them.</summary>
    public static IReadOnlyDictionary<string, AirfoilTableData> DerivedSectionTables(AircraftConfig config) => TailSections(config).Derived;

    /// <summary>
    /// Where a strip's loads act. A fixed strip and the hinged strip behind it (wing + aileron, stab + elevator, fin +
    /// rudder) are ONE section: its lift acts at the section's quarter-chord, LE − ¼·(c_fixed + c_hinged). Each piece used
    /// to load at its OWN quarter-chord, which put the section's α-lift at ~37 % chord (the aileron row 0.9 m behind the
    /// 172's CG carried 11 % of the wing's lift slope there) — a spurious ~0.3 of Cmα stability. Unpaired strips (plain
    /// wing, all-moving stabilator) load at their own position.
    /// </summary>
    public static Vec3 LoadPoint(AircraftConfig config, int stripIndex, StripConfig strip)
    {
        double[] xs = _loadX.GetValue(config, BuildLoadX);
        return stripIndex < xs.Length && !double.IsNaN(xs[stripIndex]) ? new Vec3(xs[stripIndex], strip.Pos[1], strip.Pos[2]) : strip.PosVec();
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _loadX = new();
    private static double[] BuildLoadX(AircraftConfig cfg)
    {
        var all = new List<(StripConfig st, string key, bool vertical, bool hinged)>();
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            bool vert = sf.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || sf.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase) || sf.Id.StartsWith("rudder", StringComparison.OrdinalIgnoreCase);
            foreach (StripConfig st in sf.Strips)
                all.Add((st, SystemKey(sf.Id), vert, st.Control is not null && Math.Abs(st.Control.Gain) >= 0.9));
        }
        var x = new double[all.Count];
        for (int i = 0; i < x.Length; i++) x[i] = double.NaN;
        for (int i = 0; i < all.Count; i++)
        {
            var (st, key, vert, hinged) = all[i];
            if (st.Control is null || vert) continue;   // (vertical tail staged: loads at its own strips for now)
            double span = vert ? st.Pos[2] : st.Pos[1];
            double width = st.Chord > 1e-9 ? st.Area / st.Chord : 0;
            int best = -1; double bestD = double.MaxValue;
            for (int j = 0; j < all.Count; j++)
            {
                var o = all[j];
                if (j == i || o.key != key || o.hinged == hinged || o.st.Control is null || o.st.Control.Surface != st.Control.Surface) continue;
                double os = vert ? o.st.Pos[2] : o.st.Pos[1];
                double ow = o.st.Chord > 1e-9 ? o.st.Area / o.st.Chord : 0;
                double d = Math.Abs(os - span);
                if (d < 0.6 * Math.Max(width, ow) && d < bestD) { bestD = d; best = j; }
            }
            if (best < 0) continue;
            StripConfig fixedSt = hinged ? all[best].st : st, hingedSt = hinged ? st : all[best].st;
            double le = fixedSt.Pos[0] + 0.25 * fixedSt.Chord;
            x[i] = le - 0.25 * (fixedSt.Chord + hingedSt.Chord);
        }
        return x;
    }

    /// <summary>
    /// How much of a FIXED strip's span actually has a hinged surface behind it, 0..1.
    ///
    /// A deflection cambers the fixed strip ahead of the control (gain g, docs/CONTROL-SURFACES.md) — but only
    /// over the span the control really occupies. Where an aileron stops short of the band the fixed strip
    /// covers, the camber was still being applied across the WHOLE band, which buys rolling moment the aeroplane
    /// has no aileron to produce. It inflates roll POWER without inflating roll DAMPING, so it goes straight
    /// into the steady roll rate.
    ///
    /// Coverage is measured as the hinged strip's span divided by the fixed strip's, both as area/chord — the
    /// same rasterised silhouette the configs were built from, so a control that ends mid-band contributes only
    /// the span it occupies. For 28 of the fleet's 32 aileron and elevator systems this is 1.000 to three
    /// decimals and nothing changes. It is NOT 1 for exactly the surfaces rebuilt from their GLB models on
    /// 2026-10-28 — Pitts upper wing 0.681 and lower 0.719, Extra 300 wing 0.826, Great Lakes lower wing 0.877,
    /// and the Pitts, Extra, Great Lakes and Jenny elevators 0.90–0.96 — where the measured fixed rows run
    /// past the ends of the control surface.
    ///
    /// Scaling the gain rather than splitting the strip: in the linear range the camber lift over a band is
    /// proportional to the flapped fraction of it, so the two are equivalent, and scaling leaves the station
    /// list, the strip count, the load points, the aspect ratios and <see cref="WeightAndBalance"/>'s
    /// <c>>= 0.9</c> hinged-row test all untouched. Only the fixed row is scaled; the hinged row is the surface
    /// itself and keeps its gain of 1.
    /// </summary>
    public static double ControlSpanCoverage(AircraftConfig config, int stripIndex)
    {
        double[] c = _coverage.GetValue(config, BuildCoverage);
        return stripIndex >= 0 && stripIndex < c.Length ? c[stripIndex] : 1.0;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _coverage = new();
    private static double[] BuildCoverage(AircraftConfig cfg)
    {
        var all = new List<(StripConfig st, string key, bool hinged, double width)>();
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
                all.Add((st, SystemKey(sf.Id), st.Control is not null && Math.Abs(st.Control.Gain) >= 0.9,
                         st.Chord > 1e-9 ? st.Area / st.Chord : 0.0));
        var cov = new double[all.Count];
        for (int i = 0; i < cov.Length; i++) cov[i] = 1.0;
        for (int i = 0; i < all.Count; i++)
        {
            var (st, key, hinged, width) = all[i];
            if (st.Control is null || hinged || width <= 1e-9) continue;   // hinged rows and plain strips: unscaled
            // The hinged strip of the same system and surface nearest this station, as LoadPoint pairs them.
            double y = st.Pos[1]; int best = -1; double bestD = double.MaxValue;
            for (int j = 0; j < all.Count; j++)
            {
                var o = all[j];
                if (!o.hinged || o.key != key || o.st.Control is null || o.st.Control.Surface != st.Control.Surface) continue;
                double d = Math.Abs(o.st.Pos[1] - y);
                if (d < 0.6 * Math.Max(width, o.width) && d < bestD) { bestD = d; best = j; }
            }
            if (best < 0) continue;
            double c = Math.Clamp(all[best].width / width, 0.0, 1.0);
            // DEADBAND: these spans come from a rasterised silhouette and are not accurate to better than
            // about a per cent, so anything within 1 % of full is full. Without it the fleet's exactly-tiled
            // surfaces measure 0.999 instead of 1.000 and every one of them shifts its trim in the fourth
            // decimal — churn in the generated glide table for no physics. It leaves the four surfaces this
            // is actually about (0.68–0.88) untouched.
            cov[i] = c >= 0.99 ? 1.0 : c;
        }
        return cov;
    }

    /// <summary>
    /// The chord ratio E = c_hinged / (c_fixed + c_hinged) of the aileron SECTION a strip belongs to (fixed row or hinged
    /// row, paired as <see cref="LoadPoint"/> pairs them), or NaN where an aileron strip has no partner — the
    /// paraglider's brake-line strips, which are not a hinged section and keep the old per-strip treatment.
    /// </summary>
    public static double AileronSectionE(AircraftConfig config, int stripIndex)
    {
        double[] e = _aileronE.GetValue(config, BuildAileronE);
        return stripIndex >= 0 && stripIndex < e.Length ? e[stripIndex] : double.NaN;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _aileronE = new();
    private static double[] BuildAileronE(AircraftConfig cfg)
    {
        var all = new List<(StripConfig st, string key, bool hinged, double width)>();
        foreach (SurfaceConfig sf in cfg.Surfaces)
            foreach (StripConfig st in sf.Strips)
                all.Add((st, SystemKey(sf.Id), st.Control is not null && Math.Abs(st.Control.Gain) >= 0.9,
                         st.Chord > 1e-9 ? st.Area / st.Chord : 0.0));
        var e = new double[all.Count];
        for (int i = 0; i < e.Length; i++) e[i] = double.NaN;
        for (int i = 0; i < all.Count; i++)
        {
            var (st, key, hinged, width) = all[i];
            if (st.Control is null || st.Control.Surface != "aileron" || width <= 1e-9) continue;
            int best = -1; double bestD = double.MaxValue;
            for (int j = 0; j < all.Count; j++)
            {
                var o = all[j];
                if (j == i || o.hinged == hinged || o.key != key || o.st.Control is null || o.st.Control.Surface != "aileron") continue;
                double d = Math.Abs(o.st.Pos[1] - st.Pos[1]);
                if (d < 0.6 * Math.Max(width, o.width) && d < bestD) { bestD = d; best = j; }
            }
            if (best < 0) continue;
            double ch = hinged ? st.Chord : all[best].st.Chord, cf = hinged ? all[best].st.Chord : st.Chord;
            e[i] = ch / (ch + cf);
        }
        return e;
    }

    public const double FinEndPlateFactor = 1.55;

    /// <summary>Gilruth & White fuselage pitch factor, 57.3·K_f·w²·L (m³): M_y = this · q · ½ sin 2α. K_f against the wing
    /// root quarter-chord's distance from the nose as a fraction of body length (NACA TR 711, fig. 13).</summary>
    public static double FuselageMunkM3(AircraftConfig config) => _munk.GetValue(config, cfg =>
    {
        CrossflowConfig cf = cfg.Fuselage.Crossflow;
        if (cf.BodyRadiusM <= 0 || cf.LengthM <= 0.1) return new[] { 0.0 };
        double ax = 0, aa = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
            if (sf.Id.StartsWith("wing", StringComparison.OrdinalIgnoreCase))
                foreach (StripConfig st in sf.Strips) { ax += st.PosVec().X * st.Area; aa += st.Area; }
        if (aa <= 0) return new[] { 0.0 };
        double noseX = cf.PlanCenterX + 0.5 * cf.LengthM;
        double frac = Math.Clamp((noseX - ax / aa) / cf.LengthM, 0.05, 0.65);
        double[] fx = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7 }, kf = { 0.005, 0.007, 0.010, 0.016, 0.024, 0.035, 0.047 };
        double k = kf[0];
        for (int i = 0; i < fx.Length - 1; i++) if (frac >= fx[i] && frac <= fx[i + 1]) { k = kf[i] + (kf[i + 1] - kf[i]) * (frac - fx[i]) / (fx[i + 1] - fx[i]); break; }
        if (frac < fx[0]) k = kf[0];
        double w = 2 * cf.BodyRadiusM;
        return new[] { 57.2958 * k * w * w * cf.LengthM * cfg.Fuselage.MunkScale };
    })[0];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _munk = new();

    private sealed class TailSectionMap { public string?[] Keys = System.Array.Empty<string?>(); public Dictionary<string, AirfoilTableData> Derived = new(); }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, TailSectionMap> _tailSections = new();
    private static TailSectionMap TailSections(AircraftConfig config) => _tailSections.GetValue(config, cfg =>
    {
        var map = new TailSectionMap { Keys = new string?[cfg.Surfaces.Count] };
        for (int i = 0; i < cfg.Surfaces.Count; i++)
        {
            string key = SystemKey(cfg.Surfaces[i].Id);
            // Horizontal tails and WINGS. (Fins + rudders: staged — the vertical tail keeps its own tables
            // until its own validation, 2026-10-06.) Wings were left out when this was written because the
            // fleet's wing polars were finite-span already (clarkY-like 4.47/rad); they are not all finite-span
            // any more. The nine wing surfaces rebuilt or authored since carry 2-D section data — Pitts 6.30,
            // Great Lakes 6.19, Extra / Cassutt / Swift / drone 5.82, 737 5.53 — and a 2-D slope on a wing
            // overstates its lift AND its roll damping, the same way it overstated every tail in 2026-10-06.
            // The a0 >= 5.5 test below is what decides; a wing already on a finite-span polar is untouched.
            if (key != "hStab") continue;
            int fixedIdx = cfg.Surfaces.FindIndex(sf => sf.Id == key);
            if (fixedIdx < 0) fixedIdx = i;   // all-moving (stabilator): the surface is its own section
            if (cfg.Surfaces[fixedIdx].Strips.Count == 0) continue;
            string baseAf = cfg.Surfaces[fixedIdx].Strips[0].Airfoil;
            if (!cfg.AirfoilTables.TryGetValue(baseAf, out AirfoilTableData? data)) continue;
            double a0 = SectionSlope(data);
            if (a0 < 5.5) { map.Keys[i] = baseAf; continue; }   // already a finite-span table: share it as-is
            bool vertical = key == "vStab";
            double ar = SystemAspectRatio(cfg, fixedIdx, vertical) * (vertical ? FinEndPlateFactor : 1.0);
            double kappa = a0 / (2 * Math.PI);
            double a = 2 * Math.PI * ar / (2 + Math.Sqrt(ar * ar / (kappa * kappa) + 4));
            double scale = a / a0;
            string dKey = $"{baseAf}@{key}-3d";
            if (!map.Derived.ContainsKey(dKey))
            {
                var t = new double[data.AlphaRad.Length];
                for (int k = 0; k < t.Length; k++) t[k] = data.AlphaRad[k] / scale;   // Cl(α) of the finite surface = Cl₂D(α·a/a₀)
                map.Derived[dKey] = new AirfoilTableData { AlphaRad = t, Cl = data.Cl, Cd = data.Cd, Cm = data.Cm };
            }
            map.Keys[i] = dKey;
        }
        return map;
    });

    /// <summary>Lift slope of a table about zero alpha (per rad).</summary>
    public static double SectionSlope(AirfoilTableData t)
    {
        double Cl(double a)
        {
            for (int i = 0; i < t.AlphaRad.Length - 1; i++)
                if (t.AlphaRad[i] <= a && a <= t.AlphaRad[i + 1]) { double f = (a - t.AlphaRad[i]) / (t.AlphaRad[i + 1] - t.AlphaRad[i]); return t.Cl[i] + f * (t.Cl[i + 1] - t.Cl[i]); }
            return 0;
        }
        return (Cl(0.06) - Cl(-0.06)) / 0.12;
    }

    public static string SystemKey(string id)
    {
        if (id.EndsWith("-aileron", StringComparison.OrdinalIgnoreCase)) return id[..^"-aileron".Length];
        if (id.Equals("elevator", StringComparison.OrdinalIgnoreCase)) return "hStab";
        if (id.StartsWith("rudder", StringComparison.OrdinalIgnoreCase)) return "vStab";
        return id;
    }

    private static double GeometricAspectRatio(SurfaceConfig surface, bool isVertical)
    {
        double area = 0.0, min = double.MaxValue, max = double.MinValue;
        StripConfig? first = null, last = null;
        foreach (StripConfig strip in surface.Strips)
        {
            area += strip.Area;
            double spanCoord = isVertical ? strip.PosVec().Z : strip.PosVec().Y;
            if (spanCoord < min) { min = spanCoord; first = strip; }
            if (spanCoord > max) { max = spanCoord; last = strip; }
        }

        if (area <= 0.0 || first is null || last is null)
        {
            return 0.0;
        }

        double endWidths = (WidthOf(first) + WidthOf(last)) * 0.5;
        double span = (max - min) + endWidths;
        return span * span / area;

        static double WidthOf(StripConfig s) => s.Chord > 1e-9 ? s.Area / s.Chord : 0.0;
    }

    /// <summary>True when surface index i is active: a null mask means every surface flies; otherwise
    /// mask[i] == false removes that surface from the aero (STRUCTURAL FAILURE — the wings have left).</summary>
    private static bool SurfaceActive(bool[]? mask, int i) => mask is null || i >= mask.Length || mask[i];

    /// <summary>
    /// Computes total aerodynamic force and moment (body axes, about the CG) for the aircraft in its
    /// current state. Does NOT include gravity or propulsion — the caller (Aircraft/TrimSolver) adds those.
    /// <paramref name="surfaceMask"/> (optional, indexed like config.Surfaces) masks surfaces OUT without
    /// mutating the config: a masked surface contributes nothing but its strips still advance the flow-state
    /// index so per-strip arrays stay aligned with the config.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(
        AircraftConfig config,
        IReadOnlyDictionary<string, AirfoilTable> airfoilTables,
        Vec3 bodyVelocity,
        Vec3 bodyRates,
        Vec3 windBody,
        double airDensity,
        ControlDeflections controls,
        double wakeStalledFracOverride = -1.0,
        StripFlowState? flowState = null,
        double slipstreamDeltaU = 0.0,
        double slipstreamRadius = 0.0,
        bool[]? surfaceMask = null,
        bool[]? stripMask = null,
        Vec3? meanWindBody = null,
        double wingHeightAglM = double.PositiveInfinity,
        double slipstreamSwirlMs = 0.0,
        WindGradient windGradient = default)
    {
        int stripIndex = 0;
        // GROUND EFFECT (owner 2026-10-03: "is it even modeled?" — it was not). Within about a span of the surface the
        // ground blocks the wing's downwash: McCormick's φ = (16h/b)² / (1 + (16h/b)²) scales the induced drag (φ = 0.5 at
        // h ≈ b/16 ... ~0.88 at b/6), the wing's lift slope rises as if its aspect ratio were AR/φ, and the tail sees φ
        // of the downwash (the nose-down trim change in the flare).
        double span = WingSpan(config);
        double gePhi = 1.0;
        if (span > 0 && wingHeightAglM < span * 1.5)
        {
            double k = 16.0 * Math.Max(0.05, wingHeightAglM) / span;
            gePhi = k * k / (1.0 + k * k);
        }
        Vec3 cg = config.Mass.CgVec();
        double flapDownwashRad = 0.0;
        if (controls.FlapFraction > 0.0)
            foreach (SurfaceConfig sfx in config.Surfaces) foreach (StripConfig stx in sfx.Strips)
                if (stx.Flap is not null) flapDownwashRad = Math.Max(flapDownwashRad, stx.Flap.MaxDeltaAlphaRad * controls.FlapFraction);
        Vec3 totalForce = Vec3.Zero;
        Vec3 totalMoment = Vec3.Zero;

        // wakeStalledFracOverride >= 0 supplies a LAGGED separation state from the caller (stall
        // hysteresis — separated wakes develop fast and wash out slowly). Negative = instantaneous.
        WingWake wake = ComputeWingWake(config, bodyVelocity, windBody, bodyRates, cg, wakeStalledFracOverride, surfaceMask, stripMask, airfoilTables);

        // The downwash the tail flies in (below, per strip) is also the flow the wakes are carried in.
        double epsTail = (config.StallDynamics.DownwashLagEnabled && flowState is not null
            ? flowState.DownwashEpsLagged
            : 0.4 * Math.Clamp(wake.FlowAlpha, -0.5, 0.5) * (1.0 - wake.StalledFraction));
        epsTail = (epsTail + 0.4 * flapDownwashRad * (1.0 - wake.StalledFraction)) * gePhi;
        // Every wing and stabiliser section's own wake (TailWake): where it goes, how wide and how deep.
        TailWake.Field wakes = TailWake.Prepare(config, airfoilTables, bodyVelocity - windBody, bodyRates, cg, flowState, surfaceMask, stripMask, epsTail);

        // THE WING'S OWN STALLED FRACTION, area-weighted, accumulated as the strips are flown. It gates the
        // separated-wing rolling moment at the bottom of this method; see ApplyWingSideslipRoll.
        double gateWingArea = 0.0, gateWingSeparated = 0.0;

        for (int surfaceIndex = 0; surfaceIndex < config.Surfaces.Count; surfaceIndex++)
        {
            SurfaceConfig surface = config.Surfaces[surfaceIndex];
            if (!SurfaceActive(surfaceMask, surfaceIndex))
            {
                stripIndex += surface.Strips.Count; // keep per-strip arrays aligned with the config
                continue;
            }

            bool isWing = surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            bool isVertical = surface.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                               || surface.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

            double aspectRatio = SystemAspectRatio(config, surfaceIndex, isVertical);

            // Wing-body crossflow (dihedral effect of wing position): sideslip flow wraps around the
            // fuselage like 2D potential flow past a cylinder of radius R. A wing root ABOVE the axis
            // sees upwash on the windward side / downwash on the leeward side (high wing = extra
            // dihedral effect); a root BELOW the axis sees the reverse (low wing = anhedral effect).
            // Vertical perturbation of the AIR at body offset (y, z): w_air = -2·v_air·R²·y·z / (y²+z²)².
            double bodyR = config.Fuselage.Crossflow.BodyRadiusM;
            bool wingBody = !isVertical && bodyR > 0.0 && surface.HeightAboveBodyAxisM != 0.0;
            double rootZ = 0.0;
            if (wingBody)
            {
                double minAbsY = double.MaxValue;
                foreach (StripConfig st in surface.Strips)
                {
                    double ay = Math.Abs(st.PosVec().Y);
                    if (ay < minAbsY) { minAbsY = ay; rootZ = st.PosVec().Z; }
                }
            }

            // BIPLANE MUTUAL INDUCTION on the rolling loads (BiplaneCellule.RollDampingInduction / AileronInduction): a
            // cellule's two wings each fly in the other's induced field, which takes a share of the load a roll rate or an
            // aileron puts on them. 1 on every monoplane and every surface outside the cellule.
            BiplaneCellule? cellule = isWing ? BiplaneCellule.For(config) : null;
            bool inCellule = cellule is not null && cellule.Covers(surface.Id);

            foreach (StripConfig strip in surface.Strips)
            {
                int idx = stripIndex++; // counted for EVERY strip, including low-speed skips
                if (flowState is not null && idx < flowState.HingeQ.Length) flowState.HingeQ[idx] = 0.0;
                if (stripMask is not null && idx < stripMask.Length && !stripMask[idx])
                {
                    continue; // this strip left with a broken-off component (wing half, tail): no aero from it
                }
                Vec3 stripAt = LoadPoint(config, idx, strip);   // a hinged section's loads act at the SECTION quarter-chord
                Vec3 r = stripAt - cg;
                // The induction is a property of ATTACHED circulation (it is the lattice's), so it fades with this strip's
                // own separation: a stalled, spinning cellule is left exactly as it was.
                double stripAttached = flowState is not null && idx < flowState.Separation.Length ? 1.0 - flowState.Separation[idx] : 1.0;
                Vec3 rollRates = bodyRates;
                if (inCellule) rollRates = new Vec3(bodyRates.X * (1.0 - (1.0 - cellule!.RollDampingInduction) * stripAttached), bodyRates.Y, bodyRates.Z);
                Vec3 vLocal = bodyVelocity - windBody + Vec3.Cross(rollRates, r);
                // The air is not the same across the airframe (regional atmosphere): the strip sees the wind where IT is.
                if (windGradient.Active) vLocal -= windGradient.PerX * r.X + windGradient.PerY * r.Y;
                Vec3 vAirStrip = vLocal;   // the relative wind here before the slipstream: what carries the wakes over this strip

                // Propeller slipstream: tail surfaces inside the (contracted) prop wash see the accelerated
                // flow — this is why a prop aircraft has elevator and rudder authority at low speed (taxi,
                // takeoff roll, floats on the hump, STOL). Wing strips are left alone (small span fraction).
                if (slipstreamDeltaU > 0.0 && !isWing && System.Math.Abs(strip.PosVec().Y) < slipstreamRadius)
                {
                    vLocal = new Vec3(vLocal.X + slipstreamDeltaU, vLocal.Y, vLocal.Z);
                    // Swirl (owner 2026-10-09): the slipstream rotates with the prop, so the two stab halves see the
                    // flow angle change in opposite senses — a RH prop (clockwise from behind) carries the air UP past the
                    // left half and DOWN past the right. Solid-body rotation across the tube.
                    if (!isVertical && slipstreamSwirlMs != 0.0 && slipstreamRadius > 0.0)
                    {
                        double yy = strip.PosVec().Y;
                        double airDown = slipstreamSwirlMs * (yy / slipstreamRadius);   // + = air moving down (z) at this strip
                        vLocal = new Vec3(vLocal.X, vLocal.Y, vLocal.Z - airDown);
                    }
                }

                if (wingBody)
                {
                    // Strip offset from the fuselage axis: y from the config, z from the root height
                    // (NED: above = negative) plus this strip's rise from the root (dihedral).
                    double y = strip.PosVec().Y;
                    double z = -surface.HeightAboveBodyAxisM + (strip.PosVec().Z - rootZ);
                    double r2 = Math.Max(y * y + z * z, bodyR * bodyR); // inside the body: clamp to the surface
                    // Aircraft-relative-to-air velocity is -air velocity, so Δ(vLocal.Z) = +2·v_air·R²·y·z/r⁴
                    // with v_air = -vLocal.Y.
                    double dw = -2.0 * vLocal.Y * bodyR * bodyR * y * z / (r2 * r2);
                    vLocal = new Vec3(vLocal.X, vLocal.Y, vLocal.Z + dw);
                }

                double alphaBase;
                Vec3 dragDir, liftDir, momentAxis;
                double planeSpeed;
                double tailEps = 0.0;

                if (isVertical)
                {
                    double uu = vLocal.X, vv = vLocal.Y;
                    planeSpeed = Math.Sqrt(uu * uu + vv * vv);
                    if (planeSpeed < MinSpeedMs)
                    {
                        continue;
                    }

                    alphaBase = Math.Atan2(vv, uu);
                    double dx = uu / planeSpeed, dy = vv / planeSpeed;
                    dragDir = new Vec3(dx, dy, 0);
                    liftDir = new Vec3(dy, -dx, 0);
                    momentAxis = new Vec3(0, 0, 1);
                }
                else
                {
                    // Dihedral effect: a panel with dihedral Γ presents its surface to lateral flow,
                    // so sideslip shifts the strip's local AoA — upwind panel gains α, downwind loses
                    // it (Δα ≈ β·Γ). This is THE roll-from-sideslip mechanism (banks a spin into the
                    // rotation); it emerges per strip and keeps working post-stall via the tables.
                    double dihedral = strip.DihedralRad;
                    double sideSign = Math.Sign(strip.PosVec().Y);
                    double uu = vLocal.X;
                    double ww = vLocal.Z * Math.Cos(dihedral) + vLocal.Y * Math.Sin(dihedral) * sideSign;
                    planeSpeed = Math.Sqrt(uu * uu + ww * ww);
                    if (planeSpeed < MinSpeedMs)
                    {
                        continue;
                    }

                    alphaBase = Math.Atan2(ww, uu);

                    // Wing DOWNWASH at the tail: horizontal tail surfaces fly in reduced local alpha
                    // while the wing is attached (eps ~ 0.4*alpha_wing), and the downwash COLLAPSES
                    // as the wing separates — so the tail wakes up nose-down exactly when the spin
                    // pitches deep. This attached/stalled asymmetry is what lets a strong elevator
                    // trim slow flight without tumbling the spin.
                    if (!isWing && strip.PosVec().X < -1.0)
                    {
                        double eps = config.StallDynamics.DownwashLagEnabled && flowState is not null
                            ? flowState.DownwashEpsLagged
                            : 0.4 * Math.Clamp(wake.FlowAlpha, -0.5, 0.5) * (1.0 - wake.StalledFraction);
                        // Flaps add downwash at the tail in proportion to the lift they add (owner 2026-09-15: the
                        // flap / tail interaction): the flap's effective incidence shift, scaled like the wing's own alpha.
                        eps += 0.4 * flapDownwashRad * (1.0 - wake.StalledFraction);
                        alphaBase -= eps * gePhi;   // ground effect: the ground blocks part of the downwash
                        tailEps = eps * gePhi;
                    }

                    double dx = uu / planeSpeed, dz = ww / planeSpeed;
                    dragDir = new Vec3(dx, 0, dz);
                    liftDir = new Vec3(dz, 0, -dx);
                    momentAxis = new Vec3(0, 1, 0);
                }

                double controlDeflRad = strip.Control is null ? 0.0 : controls.GetDeflection(strip.Control.Surface);
                // RIGGING (RigConfig): the ailerons' neutral sits AileronRad off the stick's, and the whole fin, rudder and
                // all, is set FinOffsetRad to the centreline. Built into the airframe; zero on an unrigged type.
                if (strip.Control is not null && strip.Control.Surface == "aileron") controlDeflRad += config.Rig.AileronRad;
                double stripIncidence = strip.IncidenceRad + (isVertical ? config.Rig.FinOffsetRad : 0.0);
                string sectionKey = SectionTableKey(config, surfaceIndex) ?? strip.Airfoil;
                if (!airfoilTables.TryGetValue(sectionKey, out AirfoilTable? table))
                {
                    throw new KeyNotFoundException($"Strip references unknown airfoil '{sectionKey}'.");
                }

                // SPLIT-SURFACE MODEL (owner-directed): fixed surfaces (wing, stab, fin) and their
                // hinged controls (ailerons, elevator, rudder) are SEPARATE strip rows. A control
                // strip's incidence follows its deflection (gain ±1), it samples its own low-AR
                // table, and it carries its own separation memory — so "the stab stalls but the
                // elevator doesn't" is emergent physics, replacing the old effectiveness-fade and
                // flat-plate approximations that lived here.
                // Control response scales with cos(flow angle): full when flow is chordwise, zero at 90,
                // REVERSED in tail-first flow (a TE-down deflection acts LE-down when the flow comes
                // from behind). Without this the deep-gyration cycle gets wrong-signed control forces.
                // Reversal factor uses the CHORDWISE flow component vs total 3D speed: pure vertical
                // crossflow neutralizes controls (factor ~0) rather than reversing them — the planar
                // angle misreads flow-from-below as tail-first and commanded the rudder BACKWARD
                // during deep phases (found via spin direction-reversal hunt).
                double chordwiseFactor = Math.Clamp(2.0 * vLocal.X / Math.Max(vLocal.Length, MinSpeedMs), -1.0, 1.0);
                // Owner (CFI) observation: moving the stick from pro-spin THROUGH neutral toward
                // anti-spin, the elevator hits a 'stall' — down-deflection into the stalled tail flow
                // loses ~half its power (the deflected-down surface works in the stab's separated
                // wake). Gated by wake stalled fraction: zero effect in normal flight.
                double deflEff = controlDeflRad;
                // Plain-flap SATURATION (owner 2026-10-03, stall: "full back elevator … 100 % greaser"): a hinged elevator is a
                // flap on the stab — past ~15–20° of deflection the flow separates off its upper surface and each extra degree
                // buys little. Modelled as δs·tanh(δ/δs), δs = Controls.Elevator.SaturationDeg (default 18°: 30° of travel acts like ~18°). Without it a 172 balanced at
                // ~22° wing AoA with full aft stick (deep stall, spin entry) instead of just past the stall (the nose bobs).
                if (strip.Control is not null && strip.Control.Surface == "elevator")
                {
                    double sat = Math.Max(1.0, config.Controls.Elevator.SaturationDeg) * Math.PI / 180.0;
                    deflEff = sat * Math.Tanh(deflEff / sat);
                }
                // Anti-spin = deflection toward the prevailing flow direction, valid BOTH attitudes:
                // upright spin (flow alpha +) anti-spin is DOWN(+); inverted (alpha -) anti-spin is UP(-).
                // Anti-spin elevator stall (owner): deflection TOWARD the flow direction loses half
                // its power in the stalled tail wake — down(+) in an upright spin (flow alpha +),
                // up(-) in an inverted spin (flow alpha -). Pro-spin elevator (holding the stall) is
                // untouched, so the spin itself sustains.
                if (strip.Control is not null && strip.Control.Surface == "elevator"
                    && controlDeflRad * (wake.FlowAlpha >= 0 ? 1 : -1) > 0)
                {
                    // Gate by ROTATION (persists through the anti-spin push; zero in normal flight):
                    // the tail flow of a rotating aircraft stays separated regardless of the
                    // instantaneous alpha the push produces.
                    double rotGate = Math.Clamp(Math.Sqrt(bodyRates.X * bodyRates.X + bodyRates.Z * bodyRates.Z) / 1.5, 0.0, 1.0);
                    deflEff *= 1.0 - 0.3 * rotGate;   // was 0.5 (owner 2026-09-08: spins too flat, no elevator authority)
                }
                // Spare per-axis multiplier on the commanded incidence. Default 1.0 and unused by every aircraft
                // in the fleet. This is NOT the flap effectiveness τ — τ is already carried by the fixed row's
                // gain g = 0.85·τ(E) (docs/CONTROL-SURFACES.md), so a factor here would count it twice. It does
                // not touch `deltaGeom` below: the surface still sticks out as far as it ever did, so its
                // pressure drag and the adverse yaw are unchanged.
                double controlEff = strip.Control is null
                    ? 1.0
                    : config.Controls.AxisFor(strip.Control.Surface)?.Effectiveness ?? 1.0;
                // SPAN COVERAGE: the fixed row is cambered only over the span the control actually occupies
                // (ControlSpanCoverage). 1.0 for almost the whole fleet; it is the aircraft rebuilt from their
                // models in 2026-10-28 whose fixed rows run past the ends of their ailerons.
                double controlDeltaAlpha = strip.Control is null
                    ? 0.0
                    : strip.Control.Gain * deflEff * chordwiseFactor * controlEff * ControlSpanCoverage(config, idx);
                // AILERON SECTIONS (owner 2026-11-02, "the roll rate is way too high"; DATCOM 6.1.1.1 / Torenbeek). An
                // aileron is the aft E of ONE aerofoil, not an aerofoil of its own: the hinged row used to fly its table at
                // α + δ, which at full travel is past the section's stall (the Pitts at 2° + 25°), and then UN-stalled as the
                // roll itself unloaded it — 24 % more rolling moment at the steady roll than at the first instant of the
                // input, which is the opposite of how a plain flap separates (off the hinge knuckle, set by δ, not by the
                // section's α). The section's lift now moves by Δα = τ(E)·η_δ(E, δ)·δ on BOTH rows: τ the thin-aerofoil flap
                // effectiveness for the section's own chord ratio, η_δ the empirical plain-flap (sealed gap) lift
                // effectiveness, which carries the viscous small-δ loss and the large-deflection fall-off together
                // (PlainFlap). It replaces the authored fixed-row gain (0.45 hand-set across most of the fleet; derived as
                // 0.85·τ on the Pitts, Extra and Great Lakes) for ailerons only; elevators and rudders are unchanged. The
                // hinged row's DRAG stays sampled at its geometric α + δ: the deflected surface's pressure drag, the source
                // of the adverse yaw, is not what this corrects.
                double sectionE = strip.Control is not null && strip.Control.Surface == "aileron" ? AileronSectionE(config, idx) : double.NaN;
                double hingedDragDelta = double.NaN;
                if (!double.IsNaN(sectionE))
                {
                    if (Math.Abs(strip.Control!.Gain) >= 0.9) hingedDragDelta = controlDeltaAlpha;
                    controlDeltaAlpha = Math.Sign(strip.Control.Gain) * PlainFlap.LiftEffectiveness(sectionE, deflEff) * deflEff
                                        * chordwiseFactor * controlEff * ControlSpanCoverage(config, idx);
                }
                if (inCellule && strip.Control is not null && strip.Control.Surface == "aileron")
                    controlDeltaAlpha *= 1.0 - (1.0 - cellule!.AileronInduction) * stripAttached;

                // AILERONS in separated flow (owner directive): a deflected aileron on a stalled wing
                // section stops commanding lift but KEEPS its pressure drag — so roll authority fades
                // with this strip's own separation memory while adverse yaw grows. Attached flight
                // keeps full roll power plus a modest deflection-drag increment (real adverse yaw).
                bool isAileronStrip = strip.Control is not null && strip.Control.Surface == "aileron";
                double cdDeflection = 0.0;
                if (isAileronStrip && Math.Abs(controlDeflRad) > 1e-9)
                {
                    double sep = flowState is not null && idx < flowState.Separation.Length ? flowState.Separation[idx] : 0.0;
                    controlDeltaAlpha *= 1.0 - 0.85 * sep; // post-stall authority halved again (owner: still too strong; NACA shape kept)
                    // Spin-rotation cut (yaw-rate gated so pure aerobatic rolls stay untouched):
                    // in rotating stalled flow the aileron cannot bite even when its strip
                    // momentarily reattaches — kills the inverted reattachment-grab magnitudes.
                    double yawGate = Math.Clamp(Math.Abs(bodyRates.Z) / 1.2, 0.0, 1.0);
                    controlDeltaAlpha *= 1.0 - 0.5 * yawGate * wake.StalledFraction;
                    if (!double.IsNaN(hingedDragDelta)) hingedDragDelta *= (1.0 - 0.85 * sep) * (1.0 - 0.5 * yawGate * wake.StalledFraction);
                    double deltaGeom = strip.Control!.Gain * controlDeflRad;
                    cdDeflection = 1.2 * Math.Sin(deltaGeom) * Math.Sin(deltaGeom) * 1.5 * sep; // separated flow ONLY — attached aileron drag stays the induced-drag asymmetry the polar already gives
                }

                // FLAP: trailing-edge flap shifts the strip's zero-lift angle (added camber) and,
                // below, adds profile drag and raises effective Clmax. Deploy fraction from the
                // "flap" control group. Sweep-scaled like everything else on the strip.
                // FLAP (reworked 2026-09-08): a trailing-edge flap adds camber — a LIFT INCREMENT at the same
                // geometric alpha (ΔCl ≈ Clα·δα·η, η≈0.55 for large deflections), a nose-down section moment,
                // profile drag, and a modestly LOWER stall alpha. The previous model shifted the table lookup
                // by the full camber angle, which stalled a flapped wing near 0° geometric alpha (made slow
                // flapped approaches impossible — floats/STOL). MaxClMax now raises the attached-branch cap.
                double flapCl = 0.0, flapCd = 0.0, flapClMax = 0.0, flapCm = 0.0, flapStallShift = 0.0;
                if (strip.Flap is not null && controls.FlapFraction > 0.0)
                {
                    double ff = controls.FlapFraction;
                    flapCl = 5.7 * strip.Flap.MaxDeltaAlphaRad * ff * 0.55;
                    flapCd = strip.Flap.MaxCd * ff;
                    flapClMax = strip.Flap.MaxClMax * ff;
                    flapCm = -0.12 * ff;                                   // camber → nose-down pitching moment (section)
                    flapStallShift = 0.2 * strip.Flap.MaxDeltaAlphaRad * ff; // stalls ~3° earlier at full flap
                }

                double alpha = alphaBase + stripIncidence + controlDeltaAlpha;

                // WING SWEEP: only the chord-normal flow component makes lift (independence principle).
                // A swept strip at geometric alpha behaves like a straight strip at a reduced effective
                // alpha ~ alpha*cos(sweep); forces then scale by cos²(sweep) via the reduced dynamic
                // pressure the sampled coefficients ride on. Zero-sweep strips unaffected.
                double cosSweep = strip.SweepRad != 0.0 ? Math.Cos(strip.SweepRad) : 1.0;
                if (cosSweep < 0.9999)
                {
                    alpha *= cosSweep;
                }

                AeroCoefficients coeffs = table.Sample(alpha);
                // The hinged aileron row's drag: its own deflected-surface value, as before (see AILERON SECTIONS above).
                double hingedCd = double.NaN;
                if (!double.IsNaN(hingedDragDelta))
                    hingedCd = table.Sample((alphaBase + stripIncidence + hingedDragDelta) * (cosSweep < 0.9999 ? cosSweep : 1.0)).Cd;

                // Two-branch stall: blend the ATTACHED branch (linear lift carried past the static
                // stall — capped dynamic overshoot) with the SEPARATED branch (the table's post-stall
                // data) by this strip's own separation memory. Only meaningful in the transition band;
                // deep post-stall the strip is always separated and the table rules.
                if (flowState is not null && idx < flowState.Separation.Length)
                {
                    flowState.LocalAlphaRad[idx] = alpha;
                    flowState.StallPosRad[idx] = table.AlphaClMaxRad;
                    flowState.StallNegRad[idx] = table.AlphaClMinRad;
                    double sep = flowState.Separation[idx];
                    // SLAT: a deployed leading-edge slat delays separation — the stall angle this strip
                    // can reach before it commits to the separated branch extends by StallExtensionRad.
                    double slatExt = strip.Slat is not null ? strip.Slat.StallExtensionRad * controls.SlatFraction : 0.0;
                    // The stall angle of the branch this strip is on. Upright: the table's Cl-max angle,
                    // extended by a slat and pulled in by flap camber as before. INVERTED: the table's own
                    // Cl-min angle, which on a cambered section arrives several degrees sooner (the Clark Y
                    // table here: -13.0 deg against +16.5 deg). Judging the inverted branch by the upright
                    // angle held the attached branch well past the point where the measured polar says the
                    // flow has already gone. Neither device shifts the inverted angle: a leading-edge slat
                    // energises the UPPER surface, which is the pressure side inverted, and no inverted flap
                    // data backs a shift. |AlphaClMinRad| == AlphaClMaxRad for a symmetric section, so every
                    // aerobatic airplane is untouched.
                    double aClMax = alpha >= 0.0
                        ? table.AlphaClMaxRad + slatExt - flapStallShift
                        : -table.AlphaClMinRad;
                    if (sep < 1.0 && Math.Abs(alpha) > aClMax && Math.Abs(alpha) < aClMax + 20.0 * Math.PI / 180.0)
                    {
                        double sign = Math.Sign(alpha);
                        AeroCoefficients atStall = table.Sample(sign * aClMax);
                        // Attached branch: hold ~Clmax with a gentle continued rise, capped at 1.15x.
                        double clAttached = sign * Math.Min(Math.Abs(atStall.Cl) * 1.15 + flapClMax,
                            Math.Abs(atStall.Cl) + 0.8 * (Math.Abs(alpha) - aClMax));
                        double cl = (1.0 - sep) * clAttached + sep * coeffs.Cl;
                        double cd = (1.0 - sep) * atStall.Cd + sep * coeffs.Cd;
                        double cm = (1.0 - sep) * atStall.Cm + sep * coeffs.Cm;
                        coeffs = new AeroCoefficients(cl, cd, cm);
                    }
                }

                if (!double.IsNaN(hingedCd)) coeffs = new AeroCoefficients(coeffs.Cl, hingedCd, coeffs.Cm);

                // BIPLANE CELLULE (owner 2026-10-28). Two wings one above the other never make what two isolated
                // wings of the same area would: each flies in the other's field. BiplaneCellule measures the gap,
                // stagger and span off these very strips and returns the cellule's CLmax as a fraction of the
                // section's. Scaling Cl here rather than capping it takes the lift slope down with the peak, which
                // is what the interference actually does, and the induced-drag term below then follows from the
                // reduced Cl on its own. OFF unless the type asks for it -- see CelluleConfig and BiplaneCellule.
                if (isWing && config.Cellule?.LiftInterference == true)
                {
                    BiplaneCellule cell = BiplaneCellule.For(config);
                    if (cell.Covers(config.Surfaces[surfaceIndex].Id))
                        coeffs = new AeroCoefficients(coeffs.Cl * cell.ClMaxFactor, coeffs.Cd, coeffs.Cm);
                }

                // Drag polar: airfoil tables carry PROFILE drag only; induced drag is added here
                // per strip as Cl²/(π·AR·e) so it varies with local alpha (and therefore with
                // control deflection — this is where adverse yaw's drag asymmetry comes from).
                // The cos² taper retires the lifting-line term post-stall, where it is invalid
                // and the table's flat-plate drag takes over.
                if (isWing && gePhi < 0.999 && aspectRatio > MinInducedAspectRatio)
                {
                    // Lift slope with the ground's help: a = 2πA/(A+2) at A → A/φ (attached flow; the table carries the stall).
                    double a0 = aspectRatio / (aspectRatio + 2.0), aG = (aspectRatio / gePhi) / (aspectRatio / gePhi + 2.0);
                    coeffs = new AeroCoefficients(coeffs.Cl * (1.0 + (aG / a0 - 1.0) * Math.Max(0, Math.Cos(alpha))), coeffs.Cd, coeffs.Cm);
                }
                double cdInduced = 0.0;
                if (aspectRatio > MinInducedAspectRatio)
                {
                    double cosAlpha = Math.Cos(alpha);
                    double attachedTaper = cosAlpha > 0.0 ? cosAlpha * cosAlpha : 0.0;
                    // Induced drag comes from ALL the lift the section makes — the flap's (and slat's) increment too (owner
                    // 2026-10-06: "the c-172 with full flaps just floats and floats and if i pull up it will balloon 20–30 ft").
                    // The flap lift (up to +0.5 Cl per strip at full flap) used to be added after this, drag-free.
                    double attachedHl = flowState is not null && idx < flowState.Separation.Length ? 1.0 - flowState.Separation[idx] : 1.0;
                    double hlCl = (flapCl + (strip.Slat is not null ? strip.Slat.ClIncrement * controls.SlatFraction : 0.0)) * attachedHl;
                    double clAll = coeffs.Cl + hlCl;
                    cdInduced = clAll * clAll / (Math.PI * aspectRatio * surface.OswaldE) * attachedTaper * (isWing ? gePhi : 1.0);
                }

                // Unsteady-aero lag (proposal 3): forces in separated/attached flow respond over
                // ~3 chords of travel, not instantly. AeroModel records the quasi-steady coefficients;
                // Aircraft advances the lagged copies once per step; when enabled we FLY on the lagged
                // values — the phase lag is real damping and softens reattachment force spikes.
                if (flowState is not null && idx < flowState.InstCl.Length)
                {
                    flowState.InstCl[idx] = coeffs.Cl;
                    flowState.InstCd[idx] = coeffs.Cd + cdInduced + cdDeflection;
                    flowState.InstCm[idx] = coeffs.Cm;
                    flowState.ChordM[idx] = strip.Chord;
                    if (config.StallDynamics.UnsteadyLagEnabled && flowState.LagPrimed)
                    {
                        coeffs = new AeroCoefficients(flowState.LagCl[idx], flowState.LagCd[idx], flowState.LagCm[idx]);
                        cdInduced = 0; cdDeflection = 0; // lagged Cd already contains them
                    }
                }

                // Tail strips fly in whatever wing or stabiliser wake reaches them (TailWake: each section's own wake, NACA
                // Report 648 width and depth from that section's drag). It replaces the single-wing wake band, its 0.45
                // maximum loss, the fin's 0.5 of it, the stab wedge's extra half, and the flat η_t = 0.9 (DATCOM's typical
                // value, which is this same wake averaged over a typical tail).
                double qFactor = isWing ? 1.0 : TailWake.QFactor(wakes, stripAt, vAirStrip, isVertical);

                double q = 0.5 * airDensity * planeSpeed * planeSpeed * qFactor;
                if (!isWing && !isVertical && strip.PosVec().X < -1.0 && ForceDebug.Samples is not null)
                {
                    // Tail flow for the overlay (protocol 7): the local airflow this stab strip flies in (downwash, slipstream,
                    // swirl, rotation included) as a body vector, and (α_t, ε, η_t = q_local / q_freestream).
                    Vec3 vInf = bodyVelocity - windBody; double v2 = Math.Max(1e-6, vInf.X * vInf.X + vInf.Y * vInf.Y + vInf.Z * vInf.Z);
                    ForceDebug.Add(stripAt, new Vec3(planeSpeed * Math.Cos(alphaBase), vLocal.Y, planeSpeed * Math.Sin(alphaBase)),
                        new Vec3(alphaBase + strip.IncidenceRad, tailEps, qFactor * planeSpeed * planeSpeed / v2), "tailflow");
                }
                if (strip.Control is not null && flowState is not null && idx < flowState.HingeQ.Length)
                {
                    // Reversible controls: the flow a free surface trails in — the fixed surface's local angle (downwash,
                    // slipstream, sideslip, rates all included), sign-reversed in tail-first flow, and its own q. Not capped at the
                    // stall any more (owner 2026-10-12): past it the free surface weathervanes (Aircraft.HingeTerms).
                    flowState.HingeAlphaRad[idx] = Math.Clamp(alphaBase + stripIncidence, -0.5 * Math.PI, 0.5 * Math.PI) * chordwiseFactor;
                    flowState.HingeQ[idx] = q;
                    flowState.HingeFlowDir[idx] = chordwiseFactor;
                }
                // Flap lift boost (slat lift too) added to attached-flow Cl; both fade out post-stall
                // (they raise Clmax and extend the linear range, they don't add lift once separated).
                double slatCl = strip.Slat is not null ? strip.Slat.ClIncrement * controls.SlatFraction : 0.0;
                // Flap lift increment fades with separation (a stalled flapped section loses the camber lift);
                // slat lift likewise. (flapClMax only extends the attached-branch cap above.)
                double attached = flowState is not null && idx < flowState.Separation.Length ? 1.0 - flowState.Separation[idx] : 1.0;
                if (isWing) { gateWingArea += strip.Area; gateWingSeparated += strip.Area * (1.0 - attached); }
                double highLiftCl = (flapCl + slatCl) * attached;
                double clTotal = coeffs.Cl + highLiftCl;

                double lift = q * strip.Area * clTotal;
                double drag = q * strip.Area * (coeffs.Cd + cdInduced + cdDeflection + flapCd);
                double momentC4 = q * strip.Area * strip.Chord * (coeffs.Cm + flapCm);

                Vec3 force = liftDir * lift - dragDir * drag;
                Vec3 momentFromForce = Vec3.Cross(r, force);
                Vec3 momentAero = momentAxis * momentC4;

                totalForce += force;
                totalMoment += momentFromForce + momentAero;
                if (ForceDebug.Samples is not null)
                {
                    ForceDebug.Add(stripAt, liftDir * lift, Vec3.Zero, "lift");
                    ForceDebug.Add(stripAt, -dragDir * drag, Vec3.Zero, "drag");
                    ForceDebug.Add(stripAt, Vec3.Zero, momentAero, "moment");
                }
            }
        }

        // Fuselage and spoiler terms see the AIR, not the ground: with the ground velocity a steady crosswind read
        // as a permanent fuselage sideslip and trimmed the aircraft into a real one. They use the MEAN flow
        // (no gusts): the bluff-body crossflow terms are for large-angle flow, and feeding them small-scale gusts
        // pumped the glider's phugoid in moderate turbulence.
        Vec3 airVelocity = bodyVelocity - (meanWindBody ?? windBody);
        ApplySpoilerDrag(config, airVelocity, airDensity, controls, ref totalForce, surfaceMask);
        ApplyFuselage(config, airVelocity, bodyRates, airDensity, ref totalForce, ref totalMoment);
        ApplyWingSideslipRoll(config, airVelocity, airDensity, surfaceMask,
                              gateWingArea > 0.0 ? gateWingSeparated / gateWingArea : 0.0, ref totalMoment);

        return (totalForce, totalMoment);
    }

    /// <summary>
    /// The wing's separation state as a whole: the freestream angle of attack and the stalled fraction of wing area (lagged by
    /// the caller for stall hysteresis). It sets the downwash collapse and the spin-rotation control cuts; where the wakes go
    /// and what they do to the tail is TailWake's, per section.
    /// </summary>
    private readonly struct WingWake
    {
        private const double StallAlphaRad = 15.0 * Math.PI / 180.0;
        private readonly double _flowAlpha;   // freestream alpha
        private readonly double _stalledFrac; // stalled wing area / total wing area

        public WingWake(double flowAlpha, double stalledFrac)
        {
            _flowAlpha = flowAlpha;
            _stalledFrac = stalledFrac;
        }

        public static double StallAlpha => StallAlphaRad;
        public double StalledFraction => _stalledFrac;
        public double FlowAlpha => _flowAlpha;
    }

    /// <summary>Instantaneous stalled fraction of wing area — callers integrate this with an
    /// asymmetric lag (fast separation, slow reattachment) to get the hysteretic wake state.</summary>
    public static double InstantStalledFraction(AircraftConfig config, Vec3 bodyVelocity, Vec3 bodyRates, Vec3 windBody, bool[]? surfaceMask = null, bool[]? stripMask = null, IReadOnlyDictionary<string, AirfoilTable>? airfoilTables = null)
    {
        WingWake w = ComputeWingWake(config, bodyVelocity, windBody, bodyRates, config.Mass.CgVec(), -1.0, surfaceMask, stripMask, airfoilTables);
        return w.StalledFraction;
    }

    private static WingWake ComputeWingWake(AircraftConfig config, Vec3 bodyVelocity, Vec3 windBody, Vec3 bodyRates, Vec3 cg, double stalledFracOverride, bool[]? surfaceMask = null, bool[]? stripMask = null, IReadOnlyDictionary<string, AirfoilTable>? airfoilTables = null)
    {
        int stripIdx = 0;
        Vec3 freestream = bodyVelocity - windBody;
        double flowAlpha = Math.Abs(freestream.X) > MinSpeedMs || Math.Abs(freestream.Z) > MinSpeedMs
            ? Math.Atan2(freestream.Z, freestream.X)
            : 0.0;

        double totalArea = 0.0, stalledArea = 0.0;
        for (int si = 0; si < config.Surfaces.Count; si++)
        {
            SurfaceConfig surface = config.Surfaces[si];
            if (!surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase) || !SurfaceActive(surfaceMask, si))
            {
                stripIdx += surface.Strips.Count;
                continue; // no wing, no wake (a wingless fuselage sheds none over the tail)
            }

            foreach (StripConfig strip in surface.Strips)
            {
                int sIdx = stripIdx++;
                if (stripMask is not null && sIdx < stripMask.Length && !stripMask[sIdx]) continue;
                Vec3 pos = strip.PosVec();
                Vec3 vLocal = freestream + Vec3.Cross(bodyRates, pos - cg);
                double localAlpha = Math.Atan2(vLocal.Z, vLocal.X) + strip.IncidenceRad;
                totalArea += strip.Area;
                // WingWake.StallAlpha is a single calibrated angle, applied to |alpha| so either sign counts.
                // That is right for a symmetric section but not for a cambered one, which reaches its inverted
                // stall sooner: a Clark Y wing stalls at -13.0 deg, so a 15 deg threshold meant an inverted
                // C-172 stall was never reported stalled at all — and StalledFraction is what the lessons and
                // the practice scoring read. Shift the threshold by the section's own asymmetry on the negative
                // branch only, which leaves every upright number and every symmetric section exactly as before.
                double stallAt = WingWake.StallAlpha;
                if (localAlpha < 0.0 && airfoilTables is not null && airfoilTables.TryGetValue(strip.Airfoil, out AirfoilTable? sTab))
                {
                    stallAt = Math.Max(2.0 * Math.PI / 180.0, stallAt + (-sTab.AlphaClMinRad - sTab.AlphaClMaxRad));
                }
                if (Math.Abs(localAlpha) > stallAt)
                {
                    stalledArea += strip.Area;
                }
            }
        }

        if (totalArea <= 0.0)
        {
            return new WingWake(0.0, 0.0);
        }

        double frac = stalledFracOverride >= 0.0 ? stalledFracOverride : stalledArea / totalArea;
        return new WingWake(flowAlpha, frac);
    }

    private static void ApplySpoilerDrag(AircraftConfig config, Vec3 bodyVelocity, double airDensity, ControlDeflections controls, ref Vec3 totalForce, bool[]? surfaceMask = null)
    {
        if (controls.SpoilerFraction <= 0.0)
        {
            return;
        }

        double speed = bodyVelocity.Length;
        if (speed < MinSpeedMs)
        {
            return;
        }

        double wingArea = 0.0;
        bool anyWing = false;
        for (int si = 0; si < config.Surfaces.Count; si++)
        {
            SurfaceConfig surface = config.Surfaces[si];
            if (!surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase) || !SurfaceActive(surfaceMask, si))
            {
                continue;
            }

            anyWing = true;
            foreach (StripConfig strip in surface.Strips)
            {
                wingArea += strip.Area;
            }
        }

        if (surfaceMask is not null && !anyWing)
        {
            return; // the spoiler panels left with the wings
        }

        // Upper-surface spoiler panels: flat-plate drag on the PANEL area (not the whole wing — that made the
        // 2-33 dive vertically at 27 kt with the brakes out; owner 2026-09-08). Full 2-33 spoilers take L/D
        // from ~23 to ~7, i.e. ΔCd ≈ 0.03 on wing area, which 0.45 m² of panel at Cd 1.2 gives.
        const double deployedPlateCd = 1.2;
        double plateArea = config.Controls.Spoiler.PlateAreaM2 > 0 ? config.Controls.Spoiler.PlateAreaM2 : 0.02 * wingArea;
        double q = 0.5 * airDensity * speed * speed;
        double extraDrag = q * plateArea * deployedPlateCd * controls.SpoilerFraction;
        totalForce -= (bodyVelocity / speed) * extraDrag;
        ForceDebug.Add(new Vec3(-0.3, 0, -0.3), -(bodyVelocity / speed) * extraDrag, Vec3.Zero, "spoiler");
    }

    /// <summary>Square-prism quasi-steady transverse force (Parkinson &amp; Smith 1964), along the motion; |ψ| clamped at 0.25.</summary>
    private static double GallopCfy(double psi)
    {
        double p = Math.Clamp(psi, -0.25, 0.25), p2 = p * p;
        return p * (2.69 - p2 * (168.0 - p2 * (6270.0 - p2 * 59900.0)));
    }

    // NACA TR-379 (Knight & Wenzinger, 1932), "Rolling Moments Due to Rolling and Yaw for Four Wing Models in Rotation":
    // rolling-moment coefficient C_l = L / (q*S*b) at pb/2V = 0 vs alpha at 20 deg yaw: Fig. 28 (Clark Y monoplane, rectangular,
    // AR 6) and Fig. 27 (Clark Y unstaggered biplane), read off the published curves every 4 deg (the 5/10/20 deg curves are
    // close to linear in yaw above alpha ~28 deg). The tests stop at alpha 60; to 90 it falls to 0 (at 90 the wing is
    // broadside and yaw only turns it in its own plane: no leading/trailing half; an assumption, linear). See docs/SPIN-ROTARY-DATA.md.
    private static readonly double[] SlipRollAlpha = { 0, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 90 };
    private static readonly double[] SlipRollMono = { 0, 0.003, 0.015, 0.045, 0.078, 0.056, 0.038, 0.034, 0.031, 0.028, 0.026, 0.024, 0.022, 0.020, 0.018, 0 };
    private static readonly double[] SlipRollBi = { 0, 0.003, 0.010, 0.023, 0.038, 0.050, 0.050, 0.043, 0.036, 0.031, 0.027, 0.025, 0.023, 0.021, 0.019, 0 };

    /// <summary>The separated wing's rolling moment due to sideslip (see SpinAeroConfig.RollDueToSideslip): the leading half
    /// (the wing the relative wind meets first) loaded up, the trailing half unloaded. Upright (alpha &gt; 0) a wind from the
    /// right (beta &gt; 0) loads the right wing: a roll LEFT; inverted the loads are negative lift and it reverses. Beta is held
    /// at the tests' +/-20 deg beyond (no data past it).</summary>
    private static void ApplyWingSideslipRoll(AircraftConfig config, Vec3 v, double rho, bool[]? surfaceMask,
                                              double wingStalled, ref Vec3 totalMoment)
    {
        string kind = config.SpinAero?.RollDueToSideslip ?? "";
        if (kind.Length == 0) return;
        if (surfaceMask != null)
        {
            bool anyWing = false;
            for (int i = 0; i < config.Surfaces.Count && i < surfaceMask.Length; i++)
                if (surfaceMask[i] && config.Surfaces[i].Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) anyWing = true;
            if (!anyWing) return;
        }
        double V = v.Length; if (V < MinSpeedMs) return;
        double alpha = Math.Atan2(v.Z, v.X) * 180.0 / Math.PI;           // body alpha, -180..180
        double a = Math.Abs(alpha); if (a > 90) a = 180 - a;            // (reversed flow: the same broadside geometry)
        double beta = Math.Asin(Math.Clamp(v.Y / V, -1, 1));
        double b20 = 20.0 * Math.PI / 180.0, bc = Math.Clamp(beta, -b20, b20);
        double[] tab = kind == "biplane" ? SlipRollBi : SlipRollMono;
        int i0 = 0; while (i0 < SlipRollAlpha.Length - 2 && a > SlipRollAlpha[i0 + 1]) i0++;
        double t = (a - SlipRollAlpha[i0]) / (SlipRollAlpha[i0 + 1] - SlipRollAlpha[i0]);
        double cl20 = tab[i0] + Math.Clamp(t, 0, 1) * (tab[i0 + 1] - tab[i0]);     // C_l at 20 deg yaw
        // THE STALL GATE. TR-379 measured a SEPARATED wing. In attached flow the strip wing already makes
        // its own rolling moment due to sideslip out of dihedral, wing-body position and geometry, so the
        // table counts it a second time and an aeroplane with real dihedral gets roughly double: hands on
        // the trim through 40 s of moderate turbulence, the Super Cub's peak bank went 7 deg -> 52 and the
        // 2-33 wound up from a 22 m/s trim to 43. Scaling by the wing's own separated fraction confines
        // the term to the flow the paper measured, and takes it to zero in attached flow by itself.
        // docs/SPIN-ROTARY-DATA.md calls this "the faithful fix"; it was rejected once on a measurement
        // the same document later shows was wrong (the Pitts' entry gated at 17 deg on a 12 deg wing).
        cl20 *= Math.Clamp(wingStalled, 0.0, 1.0);
        if (cl20 <= 0.0) return;
        double S = 0; foreach (var sf in config.Surfaces) if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) S += st.Area;
        double span = WingSpan(config);
        double q = 0.5 * rho * V * V;
        double sense = alpha >= 0 ? 1.0 : -1.0;                         // inverted: negative lift, the moment reverses
        double L = -sense * cl20 * (bc / b20) * q * S * span;           // wind from the right (beta > 0) loads the right wing: roll left
        totalMoment += new Vec3(L, 0, 0);
        ForceDebug.Add(config.Mass.CgVec(), Vec3.Zero, new Vec3(L, 0, 0), "moment");
    }

    private static void ApplyFuselage(AircraftConfig config, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity, ref Vec3 totalForce, ref Vec3 totalMoment)
    {
        double speed = bodyVelocity.Length;
        if (speed >= MinSpeedMs)
        {
            double q = 0.5 * airDensity * speed * speed;

            // FUSELAGE PITCH (owner 2026-10-06, the stability research): a body in a flow at an angle makes a nose-UP,
            // destabilising moment (Munk; the fuselage, cowl and nacelles move the neutral point forward ~5–10 % MAC on a
            // light aircraft). Gilruth & White (NACA TR 711): Cmα_f = K_f·w²·L/(S·c̄) per degree, K_f from where the wing
            // root quarter-chord sits along the body. Here as a moment, M = 57.3·K_f·q·w²·L·½·sin 2α (linear at small α,
            // bounded at large α where the crossflow terms below take over). Without it every type with an honest CG and
            // tail came out 25–45 % MAC stable.
            double kMunk = FuselageMunkM3(config);
            if (kMunk > 0)
            {
                double aBody = Math.Atan2(bodyVelocity.Z, bodyVelocity.X);
                double my = kMunk * q * 0.5 * Math.Sin(2 * aBody);
                totalMoment += new Vec3(0, my, 0);
                ForceDebug.Add(config.Mass.CgVec(), Vec3.Zero, new Vec3(0, my, 0), "moment");   // the overlay sees the fuselage's (Munk) moment too
            }
            double drag = q * config.Fuselage.Cd0Area;
            totalForce -= (bodyVelocity / speed) * drag;

            // Crude linear side-force term: damps sideslip velocity, doesn't need to be exact.
            double sideForce = -0.5 * airDensity * speed * bodyVelocity.Y * config.Fuselage.SideForceArea;
            totalForce += new Vec3(0, sideForce, 0);
            ForceDebug.Add(config.Mass.CgVec(), -(bodyVelocity / speed) * drag + new Vec3(0, sideForce, 0), Vec3.Zero, "fuselage");

            // Slender-body crossflow drag (quadratic, acts at area centers so it makes MOMENTS):
            // plan-view normal force arrests spin flattening; side-view force weathervanes the nose
            // when the fin is stalled/blanketed at big beta.
            CrossflowConfig cf = config.Fuselage.Crossflow;
            int nSt = cf.LengthM > 0.1 ? 5 : 1;   // proposal 2: distributed stations give real Cm_q / N_r
            for (int si = 0; si < nSt; si++)
            {
                double frac = nSt == 1 ? 0.0 : (si - (nSt - 1) * 0.5) / (nSt - 1); // -0.5..+0.5
                double dx = frac * cf.LengthM;
                if (cf.PlanArea > 0.0)
                {
                    double xs = cf.PlanCenterX + dx;
                    double wLoc = bodyVelocity.Z - bodyRates.Y * (xs - config.Mass.CgVec().X); // omega x r: station z-velocity = w - q*x
                    double fz = -0.5 * airDensity * (cf.PlanArea / nSt) * cf.Cd * wLoc * Math.Abs(wLoc);
                    totalForce += new Vec3(0, 0, fz);
                    ForceDebug.Add(new Vec3(xs, 0, 0), new Vec3(0, 0, fz), Vec3.Zero, "fuselage");
                    totalMoment += Vec3.Cross(new Vec3(xs, 0, 0) - config.Mass.CgVec(), new Vec3(0, 0, fz));
                }
                if (cf.SideArea > 0.0)
                {
                    double xs = cf.SideCenterX + dx;
                    double vLoc = bodyVelocity.Y + bodyRates.Z * (xs - config.Mass.CgVec().X); // omega x r: station y-velocity = v + r*x
                    double fy = -0.5 * airDensity * (cf.SideArea / nSt) * cf.Cd * vLoc * Math.Abs(vLoc);
                    if (cf.SharpSectionFraction > 0)
                    {
                        // The sharp-cornered part of the section in the spin's broadside flow gallops (see SharpSectionFraction).
                        double wLoc = bodyVelocity.Z - bodyRates.Y * (xs - config.Mass.CgVec().X), U = Math.Abs(wLoc);
                        if (U > 1.0)
                        {
                            double psi = vLoc / U, ap = Math.Abs(psi);
                            double fSharp = 0.5 * airDensity * (cf.SideArea / nSt) * U * U * GallopCfy(psi);
                            double fade = Math.Clamp((0.45 - ap) / 0.20, 0.0, 1.0);          // 1 to |ψ| 0.25, 0 by 0.45
                            fy = (1 - cf.SharpSectionFraction * fade) * fy + cf.SharpSectionFraction * fade * fSharp;
                        }
                    }
                    totalForce += new Vec3(0, fy, 0);
                    ForceDebug.Add(new Vec3(xs, 0, 0), new Vec3(0, fy, 0), Vec3.Zero, "fuselage");
                    totalMoment += Vec3.Cross(new Vec3(xs, 0, 0) - config.Mass.CgVec(), new Vec3(0, fy, 0));
                }
            }
        }

        // Cross-section camber (see CrossflowConfig.SectionCamberCl): sideslip crossflow over an
        // asymmetric section makes a body-UP force at the cambered station → nose-down with |β|.
        CrossflowConfig cfc = config.Fuselage.Crossflow;
        if (cfc.SectionCamberCl > 0.0 && cfc.SectionCamberArea > 0.0)
        {
            double xs = cfc.SectionCamberCenterX;
            double vLoc = bodyVelocity.Y + bodyRates.Z * (xs - config.Mass.CgVec().X);
            double fz = -0.5 * airDensity * cfc.SectionCamberArea * cfc.SectionCamberCl * vLoc * vLoc; // body -z = up
            totalForce += new Vec3(0, 0, fz);
            totalMoment += Vec3.Cross(new Vec3(xs, 0, 0) - config.Mass.CgVec(), new Vec3(0, 0, fz));
            ForceDebug.Add(new Vec3(xs, 0, 0), new Vec3(0, 0, fz), Vec3.Zero, "fuselage");
        }
        // The cowl's section, cambered the other way (flat top, round belly): a body-DOWN force ahead of the CG with |β|.
        if (cfc.CowlCamberCl > 0.0 && cfc.CowlCamberArea > 0.0)
        {
            double xs = cfc.CowlCamberCenterX;
            double vLoc = bodyVelocity.Y + bodyRates.Z * (xs - config.Mass.CgVec().X);
            double fz = 0.5 * airDensity * cfc.CowlCamberArea * cfc.CowlCamberCl * vLoc * vLoc;   // body +z = down
            totalForce += new Vec3(0, 0, fz);
            totalMoment += Vec3.Cross(new Vec3(xs, 0, 0) - config.Mass.CgVec(), new Vec3(0, 0, fz));
            ForceDebug.Add(new Vec3(xs, 0, 0), new Vec3(0, 0, fz), Vec3.Zero, "fuselage");
        }

        if (ForceDebug.Samples is not null)
            ForceDebug.Add(config.Mass.CgVec(), Vec3.Zero, -1.0 * new Vec3(config.Fuselage.Damping.P * bodyRates.X, config.Fuselage.Damping.Q * bodyRates.Y, config.Fuselage.Damping.R * bodyRates.Z), "moment");
        totalMoment -= new Vec3(
            config.Fuselage.Damping.P * bodyRates.X,
            config.Fuselage.Damping.Q * bodyRates.Y,
            config.Fuselage.Damping.R * bodyRates.Z);
    }
}

/// <summary>
/// How the wind changes across the airframe, body axes: the change in the body-frame wind per metre along body x
/// (nose → tail) and along body y (left → right wing). The aircraft measures it by sampling the atmosphere at the wing
/// tips and the tail as well as the CG, so a strip at offset r from the CG sees windBody + PerX·r.x + PerY·r.y — a
/// thermal under one wing rolls the aircraft, a gust front reaching the tail first pitches it. default = uniform air.
/// </summary>
public readonly struct WindGradient
{
    public readonly Vec3 PerX, PerY;
    public readonly bool Active;
    public WindGradient(Vec3 perX, Vec3 perY) { PerX = perX; PerY = perY; Active = true; }
}

/// <summary>
/// Plain trailing-edge flap (and aileron) lift effectiveness, sealed gap: Δcl = η_δ · τ · cl_α · δ, with τ = 1 − (θf − sin θf)/π,
/// cos θf = 2E − 1, Glauert's thin-aerofoil result (ARC R&amp;M 1095, 1927), and η_δ the empirical factor that takes the
/// boundary-layer de-cambering at small δ and the flow separating off the flap at large δ. η_δ is read off Olson,
/// "Semi-Empirical Prediction of Aircraft Low-Speed Aerodynamic Characteristics", AIAA 2015-0011 (NASA NTRS 20150006019),
/// Fig. 3(a) "Plain flaps with sealed gap", data from Torenbeek, Synthesis of Subsonic Airplane Design (1982) — the
/// same plain-flap correction DATCOM 6.1.1.1 builds from its (cl_δ/cl_δ,theory) and K' charts. Digitised 2026-11-02 at
/// the plotted markers (every 10°) and on the curves between them at 5°, 15° and 25°; read to about ±0.01.
/// </summary>
public static class PlainFlap
{
    public static readonly double[] ChordRatios = { 0.1, 0.2, 0.3, 0.4 };
    public static readonly double[] DeflectionsDeg = { 0, 5, 10, 15, 20, 25, 30, 40, 50, 60 };
    /// <summary>η_δ[chord ratio][deflection], rows as <see cref="ChordRatios"/>, columns as <see cref="DeflectionsDeg"/>.</summary>
    public static readonly double[][] EtaTable =
    {
        new[] { 0.760, 0.760, 0.735, 0.660, 0.570, 0.495, 0.440, 0.385, 0.365, 0.350 },
        new[] { 0.855, 0.860, 0.833, 0.740, 0.617, 0.535, 0.475, 0.420, 0.385, 0.360 },
        new[] { 0.905, 0.910, 0.880, 0.775, 0.640, 0.555, 0.500, 0.435, 0.395, 0.370 },
        new[] { 0.920, 0.920, 0.885, 0.785, 0.650, 0.560, 0.500, 0.435, 0.400, 0.370 },
    };

    /// <summary>Glauert's thin-aerofoil flap effectiveness τ = ∂α₀/∂δ for chord ratio E.</summary>
    public static double Tau(double e)
    {
        double th = Math.Acos(Math.Clamp(2 * e - 1, -1, 1));
        return 1 - (th - Math.Sin(th)) / Math.PI;
    }

    /// <summary>η_δ at chord ratio E and deflection δ (rad, either sign), bilinear in the table, clamped to its edges.</summary>
    public static double Eta(double e, double deltaRad)
    {
        double d = Math.Min(Math.Abs(deltaRad) * 180.0 / Math.PI, DeflectionsDeg[^1]);
        double ec = Math.Clamp(e, ChordRatios[0], ChordRatios[^1]);
        int i = 0; while (i < ChordRatios.Length - 2 && ec > ChordRatios[i + 1]) i++;
        int j = 0; while (j < DeflectionsDeg.Length - 2 && d > DeflectionsDeg[j + 1]) j++;
        double fe = (ec - ChordRatios[i]) / (ChordRatios[i + 1] - ChordRatios[i]);
        double fd = (d - DeflectionsDeg[j]) / (DeflectionsDeg[j + 1] - DeflectionsDeg[j]);
        double a = EtaTable[i][j] + fd * (EtaTable[i][j + 1] - EtaTable[i][j]);
        double b = EtaTable[i + 1][j] + fd * (EtaTable[i + 1][j + 1] - EtaTable[i + 1][j]);
        return a + fe * (b - a);
    }

    /// <summary>∂α₀/∂δ of the section as a secant at this deflection: τ(E)·η_δ(E, δ). The section's lift moves as though its
    /// incidence moved by this times δ.</summary>
    public static double LiftEffectiveness(double e, double deltaRad) => Tau(e) * Eta(e, deltaRad);
}
