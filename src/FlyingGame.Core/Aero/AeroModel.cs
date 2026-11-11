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
    // Held in a field rather than written as a method group at the call site: WingGeom is on the step's path, and a
    // method-group conversion allocates a delegate per call on every language version before C# 11. The lambda this
    // replaced was cached by the compiler, so this keeps the allocation behaviour exactly as it was.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]>.CreateValueCallback
        _measureWingGeom = MeasureWingGeom;
    private static double[] WingGeom(AircraftConfig c) => _span.GetValue(c, _measureWingGeom);

    /// <summary>
    /// THE SAME MEASUREMENT, WITHOUT THE CACHE (the model-identity diagnostic, Ty 2026-11-09).
    ///
    /// Every per-config geometry table in this file and in TailWake / PropJet / BiplaneCellule is a
    /// ConditionalWeakTable keyed on the AircraftConfig OBJECT, on the standing assumption that one object is one
    /// aeroplane for its whole life. Nothing in the loader violates that today — UnityAircraftConfigLoader
    /// deserializes a fresh object per load and caches nothing — but if anything ever reused or re-pointed a config,
    /// the tables would quietly keep serving the FIRST aeroplane's geometry under the second one's name, which is
    /// precisely the shape of the bug Ty reported ("almost like it was the extra flying but the pitts livery").
    ///
    /// So the diagnostic reads the span twice: once through <see cref="WingSpan"/> (the table) and once through this
    /// (the strips as authored, right now). They agree on every healthy aeroplane. If they ever disagree, the caches
    /// are holding another aeroplane's geometry and the report says so instead of leaving it to be inferred. It
    /// allocates and loops the strips, so it is for a diagnostic line and never for the step.
    /// </summary>
    public static double WingSpanUncached(AircraftConfig c) => MeasureWingGeom(c)[0];

    private static double[] MeasureWingGeom(AircraftConfig cfg)
    {
        double maxY = 0, zSum = 0, aSum = 0, ySum = 0;
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            if (!sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (StripConfig st in sf.Strips) { Vec3 p = st.PosVec(); maxY = Math.Max(maxY, Math.Abs(p.Y) + 0.5 * st.Area / Math.Max(0.1, st.Chord)); zSum += p.Z * st.Area; aSum += st.Area; ySum += Math.Abs(p.Y) * st.Area; }
        }
        // [2] is the area-weighted mean span station and [3] the wing area: together they calibrate the per-strip
        // sideslip-roll distribution (see SLIP ROLL, PER STRIP in the strip loop) so that a uniformly separated wing
        // reproduces the TR-379 coefficient exactly.
        return new[] { 2 * maxY, aSum > 0 ? zSum / aSum : double.NaN, aSum > 0 ? ySum / aSum : 0.0, aSum };
    }

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
        _systemAr.GetValue(config, cfg => MeasureSystemAspectRatios(cfg, null))[surfaceIndex];

    /// <summary>
    /// The same, measured over only the strips that are STILL THERE (<paramref name="stripMask"/>, AeroModel strip
    /// order; null = the aeroplane as authored, which is what the per-config cache above holds).
    ///
    /// A WING THAT HAS LOST ITS OUTBOARD PANELS IS A DIFFERENT WING (Ty 2026-11-10, flying a J-3 whose outer panels he
    /// had torn off: "the glide ratio was inexplicably good it should be bad with short stubby wing roots I assume it
    /// is counting the missing wing in the aero"). He was right, and about the induced drag rather than the lift: the
    /// lift of a departed strip really does go (the strip mask drops it from the sum), but the stub that remained went
    /// on flying at the WHOLE wing's aspect ratio, because this was a per-config cache measured once from the strips as
    /// authored. Induced drag is cl²/(π·A·e), so a stub flying at A = 6.96 instead of its own 4.17 pays two thirds of
    /// the induced drag it should — and induced drag is most of the drag at best glide, which is exactly where the
    /// glide ratio is read.
    ///
    /// Measured, J-3: span 10.74 m and A 6.96 whole; with both outboard panels gone the wing is 6.80 m of span and
    /// 11.09 m² , so A is 4.17. Not cached per config, because the mask is the AIRCRAFT's and two aeroplanes of a type
    /// are damaged differently; the aeroplane rebuilds this when its mask changes and hands it to
    /// <see cref="Compute"/> through <see cref="StripFlowState.SystemAspectRatio"/>.
    /// </summary>
    public static double[] MeasureSystemAspectRatios(AircraftConfig cfg, bool[]? stripMask)
    {
        var start = new int[cfg.Surfaces.Count];
        for (int i = 0, n = 0; i < cfg.Surfaces.Count; i++) { start[i] = n; n += cfg.Surfaces[i].Strips.Count; }
        var ar = new double[cfg.Surfaces.Count];
        for (int i = 0; i < cfg.Surfaces.Count; i++)
        {
            string key = SystemKey(cfg.Surfaces[i].Id);
            bool vert = cfg.Surfaces[i].Id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || cfg.Surfaces[i].Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);
            var group = new SurfaceConfig { Id = key, Strips = new List<StripConfig>() };
            for (int j = 0; j < cfg.Surfaces.Count; j++)
            {
                SurfaceConfig other = cfg.Surfaces[j];
                if (SystemKey(other.Id) != key) continue;
                for (int k = 0; k < other.Strips.Count; k++)
                {
                    int idx = start[j] + k;
                    if (stripMask is not null && idx < stripMask.Length && !stripMask[idx]) continue;
                    group.Strips.Add(other.Strips[k]);
                }
            }
            // A system with nothing left of it keeps its authored ratio: it makes no lift either way, and a zero would
            // divide into the induced drag of whatever still reads it.
            ar[i] = group.Strips.Count > 0 ? GeometricAspectRatio(group, vert) : SystemAspectRatio(cfg, i, vert);
        }
        return ar;
    }

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
            // A set-back hinge (ControlAxisConfig.HingeSetBackFrac): the flap that cambers the section is the part AFT of
            // the hinge line; the nose ahead of it rises into the cove and lifts little (DATCOM 4.1.1.2 / 6.1.1.1 measure
            // c_f from the hinge line). The section chord is unchanged.
            double setBack = Math.Clamp(cfg.Controls?.Aileron?.HingeSetBackFrac ?? 0.0, 0.0, 0.5);
            e[i] = ch * (1.0 - setBack) / (ch + cf);
        }
        return e;
    }

    /// <summary>Diagnostics only (<c>SlipPitchProbe</c>): fly the tail WITHOUT the fuselage's crossflow vortices
    /// (<see cref="BodyVortex"/>), as the model did before 2026-11-10, so the before and the after of the
    /// sideslip-to-pitch coupling come out of one run of one binary. Never set in the game.</summary>
    public static bool NoBodyVortexForDiagnostics;

    /// <summary>UNDER DEVELOPMENT, off unless BODYVORTEX is set (lane/claude-slip-pitch): the fuselage's crossflow
    /// vortices over the tail. Carried over from the orphaned lane/claude-endo and MEASURED on today's model, where it
    /// delivers a ninth of the tail-carried Cm(beta) the data wants and nothing at all past beta 40 deg. See
    /// docs/aircraft/CM-BETA.md. Kept off so the shipped model is unchanged while the mechanism is being found.</summary>
    public static bool BodyVortexEnabled = Environment.GetEnvironmentVariable("BODYVORTEX") != null;

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

    /// <summary>
    /// FORGET THE CACHED MUNK MOMENT for this config, so the next step recomputes it from
    /// <see cref="FuselageConfig.MunkScale"/> as it stands now.
    ///
    /// It exists for the Developer tuning page (see FlyingGame.Core.Tuning.TunableCatalog), and it is the ONLY
    /// config-keyed cache that page has to invalidate: every other number it offers is re-read from the config each
    /// step, and the remaining tables here cache GEOMETRY — spans, aspect ratios, load points, section tables — which
    /// the page does not touch, because strip positions and areas are measured facts rather than knobs. Nothing else
    /// should call this; a knob that needs more than this one line does not belong on that page.
    /// </summary>
    public static void ForgetFuselageMunk(AircraftConfig config) => _munk.Remove(config);

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
            if (!map.Derived.ContainsKey(dKey)) map.Derived[dKey] = FiniteSpanSection(data, a0, a);
            map.Keys[i] = dKey;
        }
        return map;
    });

    /// <summary>
    /// The finite surface's table from a 2-D section table: each point moves to the geometric angle at which the surface
    /// makes that section lift, α = α₂D + Cl₂D(α₂D)·(1/a − 1/a₀), the induced angle of its own lift (nonlinear lifting-line,
    /// Sivells &amp; Neely, NACA Report 865, 1947; Anderson, Fundamentals of Aerodynamics §5.4). In the linear range it is exactly
    /// the Helmbold rescale α₂D·a₀/a this replaced. The rescale stretched the WHOLE axis, so on the Pitts (a/a₀ 0.51) the
    /// table's 90° point sat at 177° and a stab broadside to the flow read the section at 51°: Cl 0.95 instead of 0, a
    /// normal force flat at ~0.83 from 35° to 90° instead of rising to the table's 1.19, and an elevator that moved nothing
    /// there (owner 2026-11-03: "the elevator control is also weak at high aoa"). The induced angle goes with the lift, so
    /// broadside it is zero and the post-stall data — already a finite-aspect-ratio plate in these tables (Cd 1.19 at 90°,
    /// Viterna &amp; Corrigan's 1.11 + 0.018·AR) — stays where it was measured.
    /// Where the section's lift falls faster than 1/(1/a − 1/a₀) past the stall, the mapping folds back on itself: two
    /// section states at one geometric angle, which is the finite wing's stall jump. The table keeps the attached branch to
    /// the furthest angle it reaches and the separated branch from there on.
    /// </summary>
    /// <summary>
    /// A SEPARATED SURFACE DOES NOT GIVE LESS THE HARDER IT IS PUSHED. Past the stall this makes the section's NORMAL
    /// force Cn = Cl*cos(a) + Cd*sin(a) non-decreasing in |alpha|, by lifting Cd where the table would have it fall.
    ///
    /// A two-dimensional NACA 0012 drops Cn from 1.211 at 14 deg to 0.955 at 25 - the lift collapsing at the stall -
    /// and for a 2D section that is right. For a finite-span TAIL it is not: past the stall the flow is fully
    /// separated and the surface behaves as a flat plate, whose normal force climbs monotonically to about 1.2 at 90
    /// deg. More than wrong, a stretch of dCn/dalpha &lt; 0 is NEGATIVE PITCH DAMPING by definition - a tail that
    /// gives less force the further it is pushed DRIVES the motion instead of resisting it. Measured on the Pitts
    /// (StabHighAlphaProbe, STAB_HI=1) the tail's dM/dq came out at +129 N.m per rad/s at alpha 30, the wrong sign
    /// outright, with the resisting force falling at 12-14, 25-30 and 60-70 deg.
    ///
    /// Ty 2026-11-09, having isolated it by flying both directions: "spins to the right are about right it is just
    /// the left spin that has too much flattening especially at idle it must be the stabilizer at high angle of
    /// attack... look for a cliff in stabilizer damping or resisting force with aoa." Both spins meet the same tail;
    /// only the LEFT one needs it to win against a nose-up gyroscopic couple, so only the left one shows it.
    ///
    /// Cd is the term raised rather than Cl because past the stall the normal force IS pressure drag - the lift is
    /// gone - so this adds nothing to the section's circulation, and it only ever acts where the table was falling.
    /// </summary>
    private static void MonotonicPastStall(double[] alpha, double[] cl, double[] cd)
    {
        if (alpha.Length < 3) return;
        for (int sign = 1; sign >= -1; sign -= 2)
        {
            // Walk outward from ZERO, not from the stall peak, and never let Cn fall back. Starting at the peak left
            // the notch BELOW it untouched - on the Pitts the tail still gave less at 14 deg than at 12, which is the
            // same negative-damping defect in miniature. A tail of aspect ratio 2.8 separates gradually and has no
            // sharp two-dimensional stall break to preserve, so the rule holds all the way out to 90 deg.
            int peak = -1; double best = double.NegativeInfinity;
            for (int i = 0; i < alpha.Length; i++)
            {
                double ad = alpha[i] * sign;
                if (ad < 0.0 || ad > 0.02) continue;                    // the point nearest zero on this side
                if (best == double.NegativeInfinity || ad < best) { best = ad; peak = i; }
            }
            if (peak < 0) continue;
            double hold = Cn(alpha[peak], cl[peak], cd[peak]) * sign;
            for (int i = peak + sign; i >= 0 && i < alpha.Length; i += sign)
            {
                if (alpha[i] * sign <= 0.0 || alpha[i] * sign > 0.5 * Math.PI) break;   // same side, out to 90 deg
                double cn = Cn(alpha[i], cl[i], cd[i]) * sign;
                if (cn < hold)
                {
                    double sa = Math.Abs(Math.Sin(alpha[i]));
                    if (sa > 1e-3) cd[i] += (hold - cn) / sa;           // make up the shortfall as pressure drag
                }
                else hold = cn;
            }
        }
        static double Cn(double a, double l, double d) => l * Math.Cos(a) + d * Math.Sin(a);
    }

    /// <summary>
    /// OFF since 2026-11-09, measured by `PlateClampProbe` (PLATE_CLAMP=1). `MonotonicPastStall` holds the derived
    /// table's normal force to its RUNNING MAXIMUM past the stall, so on any section with a real stall drop it pins
    /// Cn dead flat from the stall until the genuine curve climbs back above the pre-stall peak. On the Pitts' tail
    /// that is CN 1.03 flat from 35° to 60°, and a flat Cn is zero control effectiveness: the elevator's authority
    /// at α 45 was 15.7 % of its α 0 value with the clamp and 22.7 % without, and at α 60, 6.8 % against 9.5 %.
    /// That is the owner's own report — "the elevator and rudder authority in the spin are both significantly
    /// weaker than the real thing" — and a third of it was this patch.
    ///
    /// It was added (689790f5) because the tail's pitch damping dM/dq was +129 N·m per rad/s at α 30, the wrong
    /// SIGN. That no longer reproduces: with the clamp off the same measurement is −149, damping, and it is damping
    /// at every α from 10 to 60. The anti-damping was fixed by the tail work of the same day, not by this.
    ///
    /// Kept as a seam rather than deleted because it is the instrument that measured its own cost, and because a
    /// genuinely monotonic Cn is still the right thing for a SEPARATED surface — it just has to come from the
    /// section, not from a clamp. See tools/aircraft_builder/flat_plate.py.
    /// </summary>
    public static bool ClampNormalForcePastStall = Environment.GetEnvironmentVariable("CN_CLAMP") != null;

    public static AirfoilTableData FiniteSpanSection(AirfoilTableData data, double a0, double a)
    {
        double k = 1.0 / a - 1.0 / a0;
        int n = data.AlphaRad.Length;
        var geo = new double[n];
        for (int i = 0; i < n; i++) geo[i] = data.AlphaRad[i] + data.Cl[i] * k;
        // Outward from the point nearest zero, both ways, keeping only points that advance.
        int z = 0;
        for (int i = 1; i < n; i++) if (Math.Abs(data.AlphaRad[i]) < Math.Abs(data.AlphaRad[z])) z = i;
        var keep = new List<int> { z };
        for (int i = z + 1; i < n; i++) if (geo[i] > geo[keep[^1]]) keep.Add(i);
        var neg = new List<int>();
        double last = geo[z];
        for (int i = z - 1; i >= 0; i--) if (geo[i] < last) { neg.Add(i); last = geo[i]; }
        neg.Reverse();
        neg.AddRange(keep);
        var outAlpha = neg.Select(i => geo[i]).ToArray();
        var outCl = neg.Select(i => data.Cl[i]).ToArray();
        var outCd = neg.Select(i => data.Cd[i]).ToArray();
        var outCm = neg.Select(i => data.Cm[i]).ToArray();
        if (ClampNormalForcePastStall) MonotonicPastStall(outAlpha, outCl, outCd);
        return new AirfoilTableData { AlphaRad = outAlpha, Cl = outCl, Cd = outCd, Cm = outCm };
    }

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
        WindGradient windGradient = default,
        bool propellerIsSolved = false,
        BladeElement.SwirlProfile? swirlProfile = null,
        Vec3[]? stripAirBody = null)
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
        SpoilerConfig? brakes = config.Controls.Spoiler is { Physical: true } sp && controls.SpoilerFraction > 0.0 ? sp : null;

        // wakeStalledFracOverride >= 0 supplies a LAGGED separation state from the caller (stall
        // hysteresis — separated wakes develop fast and wash out slowly). Negative = instantaneous.
        WingWake wake = ComputeWingWake(config, bodyVelocity, windBody, bodyRates, cg, wakeStalledFracOverride, surfaceMask, stripMask, airfoilTables);

        // The downwash the tail flies in (below, per strip) is also the flow the wakes are carried in.
        double epsTail = (config.StallDynamics.DownwashLagEnabled && flowState is not null
            ? flowState.DownwashEpsLagged
            : 0.4 * Math.Clamp(wake.FlowAlpha, -0.5, 0.5) * (1.0 - wake.StalledFraction));
        epsTail = (epsTail + 0.4 * flapDownwashRad * (1.0 - wake.StalledFraction)) * gePhi;
        // Every wing and stabiliser section's own wake (TailWake): where it goes, how wide and how deep.
        long tWakes = SimProfile.Begin();
        TailWake.Field wakes = TailWake.Prepare(config, airfoilTables, bodyVelocity - windBody, bodyRates, cg, flowState, surfaceMask, stripMask, epsTail, controls);
        SimProfile.End(SimProfile.Scope.Wakes, tWakes);
        // THE THRUST JET (PropJet, owner 2026-11-08): the propeller's tube of faster air, traced from the disc through
        // the air's own motion, so it tilts with alpha and beta and bends under pitch and yaw rate. One trace serves
        // every strip below. Off (and free) whenever the prop is doing nothing to the air.
        long tJet = SimProfile.Begin();
        PropJet.Jet jet = PropJet.Prepare(config, bodyVelocity - windBody, bodyRates, cg, slipstreamDeltaU, slipstreamRadius, slipstreamSwirlMs, swirlProfile);
        SimProfile.End(SimProfile.Scope.Jet, tJet);

        // THE FUSELAGE'S CROSSFLOW VORTICES OVER THE TAIL (BodyVortex). One pair per evaluation, from the crossflow
        // drag the fuselage already carries; every non-wing strip then flies in the flow it induces. See BodyVortex for
        // the derivation and the sources, and docs/aircraft/CM-BETA.md for the measurement that said it was missing.
        BodyVortex.Pair bodyVortex = NoBodyVortexForDiagnostics || !BodyVortexEnabled ? default
                                   : BodyVortex.For(config, bodyVelocity - windBody, airDensity);

        // THE SEPARATED WING'S ROLLING MOMENT DUE TO SIDESLIP, accumulated STRIP BY STRIP as they are flown (owner
        // 2026-11-09: "make each of these real science not lump sums"). It used to be one bulk C_l*q*S*b at the bottom
        // of this method, scaled by the wing's average separated fraction to keep it from double-counting the roll the
        // strips already make in attached flow. The measured magnitude is TR-379 and stays -- the strips produce only
        // 10-30 % of it above the stall and reverse sign at the stall (SlipRollGeometryProbe), so it carries a
        // three-dimensional spanwise separation effect a strip model cannot derive. What was a lump sum is the
        // APPLICATION, and that is now per strip: each strip's own separation, its own local sideslip, its own dynamic
        // pressure and its own span station. Three things follow that a wing-average cannot do -- an attached strip
        // contributes nothing, so the double count goes structurally instead of by a gate; one wing stalled and the
        // other flying makes an asymmetric moment, which is the spin; and in a spin the advancing wing's higher q
        // counts for more, because the q is the strip's own.
        //
        // CALIBRATED, not retuned: for a uniformly separated wing the strip sum must come to C_l*q*S*b, which fixes
        // the constant at b / (area-weighted mean span station). It lands at 78-88 % of the old bulk value, and the
        // shortfall is physics the bulk version omitted -- it used the BODY's alpha and the FREE-STREAM q for a wing
        // whose strips are at neither, so decalage and a biplane's wings shading each other now count
        // (SlipRollPerStripTests).
        //
        // WHY THE OLD GATE EXISTED, kept here because it is the thing not to regress: TR-379 measured a SEPARATED wing,
        // and in attached flow the strip wing already makes its own roll from sideslip out of dihedral and wing-body
        // position, so the bulk term counted it twice and an aeroplane with real dihedral got roughly double. Hands on
        // the trim through 40 s of moderate turbulence, the Super Cub's peak bank went 7 deg to 52 and the 2-33 wound up
        // from a 22 m/s trim to 43. Scaling by the wing's average separated fraction confined it; applying it per strip
        // removes the need for any gate, because an attached strip contributes nothing by construction.
        // docs/SPIN-ROTARY-DATA.md calls the gate "the faithful fix"; it was rejected once on a measurement the same
        // document later shows was wrong (the Pitts' entry gated at 17 deg on a 12 deg wing).
        string slipRollKind = config.SpinAero?.RollDueToSideslip ?? "";
        double stabHighAlphaK = config.SpinAero?.StabHighAlphaK ?? 1.0;
        double finSidewashK = config.SpinAero?.FinSidewashK ?? 0.0;
        double stabSidewashK = config.SpinAero?.StabSidewashK ?? 0.0;
        double controlSpoilerK = config.SpinAero?.ControlSpoilerK ?? 0.0;
        // Where that term starts, in degrees of the strip's own flow (SpinAeroConfig.ControlSpoilerOnsetDeg, 30 by
        // default = what shipped). It is a field because the angle belongs to the SECTION, not to the fleet: 30 was
        // right for a NACA 0012 tail and is late for a flat plate, which stalls at 9-12 deg. See the config doc.
        double spoilerOnsetDeg = config.SpinAero?.ControlSpoilerOnsetDeg ?? 30.0;
        double[] wingGeom = WingGeom(config);
        double slipRollK = slipRollKind.Length > 0 && wingGeom[2] > 1e-6 ? wingGeom[0] / wingGeom[2] : 0.0;
        const double SlipRollBeta20 = 20.0 * Math.PI / 180.0;
        double slipRollMx = 0.0;
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

            // The aeroplane's own, where it has one: a wing that has lost panels flies the aspect ratio of what is
            // LEFT of it (MeasureSystemAspectRatios). Falls back to the config's cache on an undamaged aeroplane.
            double aspectRatio = flowState?.SystemAspectRatio is { } sysAr && surfaceIndex < sysAr.Length
                ? sysAr[surfaceIndex] : SystemAspectRatio(config, surfaceIndex, isVertical);

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
            // THE SYMMETRIC lift interference this wing's Cl is scaled by further down (1 unless the type asks for it).
            // It is needed HERE, before the rolling loads, because scaling Cl scales every contribution to the strip's
            // alpha indiscriminately -- including the roll rate's and the aileron's, which carry their OWN ratios from
            // their own antisymmetric loadings (0.81 and 0.92 on the Stearman against 0.66 for its lower wing's lift).
            // So those two are DIVIDED by it below, and the Cl scaling then leaves each of them at its own ratio
            // instead of multiplying this one on top. Measured, when it was not: the Great Lakes' steady roll rate rose
            // 66.4 -> 78.0 deg/s, because the aeroplane kept its aileron moment and lost a fifth of its roll damping.
            double kCelluleWing = inCellule && config.Cellule?.LiftInterference == true
                ? cellule!.LiftInterferenceFor(surface.Id) : 1.0;

            foreach (StripConfig strip in surface.Strips)
            {
                int idx = stripIndex++; // counted for EVERY strip, including low-speed skips
                if (flowState is not null && idx < flowState.HingeQ.Length) flowState.HingeQ[idx] = 0.0;
                if (stripMask is not null && idx < stripMask.Length && !stripMask[idx])
                {
                    continue; // this strip left with a broken-off component (wing half, tail): no aero from it
                }
                Vec3 stripAt = LoadPoint(config, idx, strip);   // a hinged section's loads act at the SECTION quarter-chord
                // A bent panel (WingStructure): the row has turned about the panel's station — its load point has moved
                // with it and its dihedral has grown by the bend.
                double bendRad = flowState?.BendRad is { } bends && idx < bends.Length ? bends[idx] : 0.0;
                if (bendRad != 0.0 && flowState!.BendOffset is { } offs && idx < offs.Length) stripAt += offs[idx];
                Vec3 r = stripAt - cg;
                // The induction is a property of ATTACHED circulation (it is the lattice's), so it fades with this strip's
                // own separation: a stalled, spinning cellule is left exactly as it was.
                double stripAttached = flowState is not null && idx < flowState.Separation.Length ? 1.0 - flowState.Separation[idx] : 1.0;
                // The Cl scaling fades with this strip's separation for the same reason the two antisymmetric factors do
                // -- it is the same lattice's induction -- so all three go to 1 together on a fully stalled strip, and
                // dividing by it here is exact at every separation level and not just at the ends.
                double kCellStrip = 1.0 - (1.0 - kCelluleWing) * stripAttached;
                Vec3 rollRates = bodyRates;
                if (inCellule)
                    rollRates = new Vec3(bodyRates.X * (1.0 - (1.0 - cellule!.RollDampingInduction) * stripAttached) / kCellStrip,
                                         bodyRates.Y, bodyRates.Z);
                Vec3 vLocal = bodyVelocity - windBody + Vec3.Cross(rollRates, r);
                // The air is not the same across the airframe (regional atmosphere): the strip sees the wind where IT is.
                if (windGradient.Active) vLocal -= windGradient.PerX * r.X + windGradient.PerY * r.Y;
                // Another aeroplane's wake (VortexWake): the air its trailing lines move at THIS strip, sampled where the strip
                // is. Whatever it does to the aeroplane — the roll toward a vortex's turn, the sink between a pair — is this
                // strip's own lift answering to it.
                if (stripAirBody is not null && idx < stripAirBody.Length) vLocal -= stripAirBody[idx];
                Vec3 vAirStrip = vLocal;   // the relative wind here before the slipstream: what carries the wakes over this strip

                // THE FUSELAGE'S CROSSFLOW VORTICES (BodyVortex; Polhamus, NACA RM L55E20b): the air they move at this
                // strip, averaged across the strip's width. Tail surfaces only -- the vortices are shed from the body
                // AHEAD of the tail, and over the wing they are not yet a wake but the attached crossflow the wing-body
                // term below already describes. They are carried WITH the air, so this goes in before the jet and before
                // the wakes, which ride on the flow it leaves.
                if (!isWing && bodyVortex.Active)
                {
                    double halfWidth = 0.5 * (strip.Chord > 1e-9 ? strip.Area / strip.Chord : 0.0);
                    vLocal -= BodyVortex.Induced(bodyVortex, stripAt, halfWidth, isVertical);
                    vAirStrip = vLocal;
                }

                // Propeller slipstream: a surface inside the (contracted) prop wash sees the accelerated flow — this is
                // why a prop aircraft has elevator and rudder authority at low speed (taxi, takeoff roll, floats on the
                // hump, STOL). WHERE that wash is comes from PropJet's traced tube, so a surface outside it sees nothing
                // however the airframe is pointing, and the increment is a VECTOR: it carries the jet's own direction, so
                // this strip's dynamic pressure and its incidence both follow from it.
                //
                // THE SWIRL goes where it always went: the stabiliser, which feels it as incidence. Not the fin, whose
                // share of the swirl is the propeller model's bulk yaw term (PropModel.Parts.SlipYaw) and would be
                // counted twice. And not the WING, though the tube now reaches its root: a root is not in free air but
                // against the fuselage, which blocks and turns the swirl, and the two roots' lift changes largely cancel
                // while the small net roll that is left is already inside each type's torque, P-factor and aileron rig.
                // Put there as bare solid-body rotation it swung the roll moment with alpha hard enough to flip its
                // SIGN (the Pitts: +3.6 N.m/deg to -2.4), which no static rig can hold - every powered type then wandered
                // 25 deg of heading in a minute hands off (RigTests.HandsOffLevelAtRigPower). The root still takes the
                // jet's AXIAL increment, which is unambiguous: more q, more lift.
                if (jet.Any)
                {
                    // The fin's exclusion was bookkeeping against PropModel's bulk SlipYaw, and it EXPIRES with it: a
                    // blade-element propeller never reaches that line, so with the fin still out nothing yawed the
                    // aeroplane from the spiral slipstream at all. The rig found it the moment the six fitted types were
                    // re-solved - the J-3's rigged fin offset collapsed from +0.364 deg to -0.004, because a rigged fin
                    // exists only to cancel a yaw that had stopped existing. Solved propeller: the swirl reaches the fin
                    // and the yaw comes out of geometry, with the tube's own taper and displacement. Unsolved: SlipYaw
                    // still does it and the fin stays out.
                    Vec3 jetAdd = PropJet.RelWind(jet, stripAt, (!isVertical || propellerIsSolved) && !isWing);
                    if (jetAdd.LengthSquared > 0.0) vLocal += jetAdd;
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
                    // THE FIN SITS IN THE FLOW THE FUSELAGE ACCELERATES AROUND ITSELF (SpinAeroConfig.FinSidewashK).
                    // Potential flow past a cylinder has surface speed 2V·sin θ, peaking 90° from the stagnation
                    // line — and for a crossflow arriving at the SIDE of a fuselage those two points are the top
                    // and the bottom, which is exactly where a fin stands. So the lateral component is amplified by
                    // 1 + k·(R/r)², up to 2× at the root (4× the lateral dynamic pressure) and decaying to free
                    // stream a body-diameter out. A strip model otherwise hands every strip the free-stream lateral
                    // flow however close to the body it sits. Zero without crossflow, so ordinary flight, the spin
                    // entry and the attached rudder are untouched. See the config doc for why this and not the
                    // section, and for the geometry caveat the knob carries.
                    if (finSidewashK > 0.0 && config.Fuselage.Crossflow is { } fcf)
                    {
                        double rBody = FuselageShape.HasStations(fcf)
                            ? FuselageShape.At(fcf, stripAt.X).HalfZ
                            : (fcf.LengthM > 1e-6 ? 0.5 * fcf.SideArea / fcf.LengthM : fcf.BodyRadiusM);
                        double midZ = FuselageShape.HasStations(fcf) ? FuselageShape.At(fcf, stripAt.X).MidZ : 0.0;
                        if (rBody > 1e-6)
                        {
                            double rStrip = Math.Max(Math.Abs(stripAt.Z - midZ), rBody);
                            double amp = 1.0 + finSidewashK * (rBody * rBody) / (rStrip * rStrip);
                            vLocal = new Vec3(vLocal.X, vLocal.Y * amp, vLocal.Z);
                        }
                    }
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
                    // THE STABILISER SITS IN THE SAME ACCELERATED FLOW, TURNED THROUGH 90 DEGREES
                    // (SpinAeroConfig.StabSidewashK). Ty 2026-11-10, after watching a clip of his own flat spin:
                    // "The elevator gets the same dynamic pressure power you just gave the rudder". It is the same
                    // potential flow and the same 2V·sin θ: in a flat spin the crossflow arrives at the fuselage
                    // from BELOW, so the two points of peak surface speed are the left and right flanks — which is
                    // where the horizontal tail stands, just as the fin stands at the top for a crossflow from the
                    // side. So the NORMAL component is amplified by 1 + k·(R/r)², with R the body's local half-WIDTH
                    // and r the strip's distance off the centreline.
                    //
                    // RAMPED IN ON THE STRIP'S OWN FLOW ANGLE, 20°→40°, and this is the difference from the fin. A
                    // fin's amplified component is the lateral one, which is zero in symmetric flight, so the fin's
                    // knob cannot touch ordinary flying. The stabiliser's is the NORMAL one, which is never zero:
                    // applied flat it would multiply every type's tail incidence and move the whole fleet's trim.
                    // Below 20° the fuselage is a streamlined body lying along its own flow and the existing
                    // downwash and trim stand; past 40° it is a bluff cylinder in crossflow and the cylinder
                    // solution is the right one.
                    if (stabSidewashK > 0.0 && !isWing && config.Fuselage.Crossflow is { } scf)
                    {
                        double rBody = FuselageShape.HasStations(scf)
                            ? FuselageShape.At(scf, stripAt.X).HalfY
                            : (scf.LengthM > 1e-6 ? 0.5 * scf.SideArea / scf.LengthM : scf.BodyRadiusM);
                        double flowAng = Math.Abs(Math.Atan2(vLocal.Z, vLocal.X)) * 57.2957795;
                        if (flowAng > 90.0) flowAng = 180.0 - flowAng;
                        double ramp = Math.Clamp((flowAng - 20.0) / 20.0, 0.0, 1.0);
                        if (rBody > 1e-6 && ramp > 0.0)
                        {
                            double rStrip = Math.Max(Math.Abs(stripAt.Y), rBody);
                            double amp = 1.0 + ramp * stabSidewashK * (rBody * rBody) / (rStrip * rStrip);
                            vLocal = new Vec3(vLocal.X, vLocal.Y, vLocal.Z * amp);
                        }
                    }

                    double dihedral = strip.DihedralRad + bendRad;
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

                // A row whose deflection the structure decides (a slaved or disconnected aileron, WingStructure): its own
                // value, rigging included — it is not where the stick puts it.
                double overrideRad = flowState?.DeflectionOverride is { } ovr && idx < ovr.Length ? ovr[idx] : double.NaN;
                double controlDeflRad = strip.Control is null ? 0.0 : !double.IsNaN(overrideRad) ? overrideRad : controls.GetDeflection(strip.Control.Surface);
                // RIGGING (RigConfig): the ailerons' neutral sits AileronRad off the stick's, and the whole fin, rudder and
                // all, is set FinOffsetRad to the centreline. Built into the airframe; zero on an unrigged type.
                if (strip.Control is not null && strip.Control.Surface == "aileron" && double.IsNaN(overrideRad)) controlDeflRad += config.Rig.AileronRad;
                double stripIncidence = strip.IncidenceRad + (isVertical ? config.Rig.FinOffsetRad : 0.0);
                if (flowState?.IncidenceDeltaRad is { } incd && idx < incd.Length) stripIncidence += incd[idx];   // a bent tail boom
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
                // NORMALISED BY THE STRIP'S OWN IN-PLANE SPEED, not by the 3-D one. This used to divide by
                // vLocal.Length, which charges a control surface for the SPANWISE flow over it - and a control
                // surface cannot feel spanwise flow, because its hinge line IS the spanwise direction: rotating a
                // plate about an axis parallel to the flow does not change its angle to the flow at all. It is the
                // independence principle, which this loop already applies everywhere else that matters (`planeSpeed`
                // and therefore `q` are in-plane; the fin's own alpha is atan2(v, u) and ignores w). The one place
                // the 3-D speed survived was here, so the geometry was paid for twice.
                // IT FALLS ALMOST ENTIRELY ON THE FIN, which is the imbalance: a fin's spanwise direction is
                // VERTICAL, so in a spin its spanwise flow is the whole descent velocity while its in-plane flow is
                // only what the rotation gives it - the Pitts' fin at alpha 82 flies 21.6 m/s in plane against 32.7
                // m/s of total, so the rudder was cut by a third for nothing. A stabiliser's spanwise direction is
                // LATERAL, where the only flow is the rotation's own, and it loses under a tenth.
                // Ordinary flight is untouched to the digit: at small alpha and beta the in-plane and the 3-D speed
                // are the same number, and the clamp at 2x holds the factor at 1.0 either way out to 60 deg.
                // AND IT IS NOT A LEVER ON THE SPIN, measured, so do not reach for it as one: A/B'd on 2026-11-10 it
                // moved SpinAuthorityProbe's rudder and elevator authority by not one digit at any alpha from 30 to
                // 90, and the Pitts' inverted-spin trajectory by under 1 %. The reason is the arithmetic wall -
                // `fin-lowAR` holds Cl flat from 25 deg out, so handing the rudder 26 deg of incidence shift instead
                // of 17 samples the same flat table - and it is why ControlSpoilerK had to exist. This is a
                // double-count removed, not a fix for anything.
                double chordwiseFactor = Math.Clamp(2.0 * vLocal.X / Math.Max(planeSpeed, MinSpeedMs), -1.0, 1.0);
                // Owner (CFI) observation: moving the stick from pro-spin THROUGH neutral toward
                // anti-spin, the elevator hits a 'stall' — down-deflection into the stalled tail flow
                // loses ~half its power (the deflected-down surface works in the stab's separated
                // wake). Gated by wake stalled fraction: zero effect in normal flight.
                double deflEff = controlDeflRad;
                // THE ELEVATOR'S SATURATION IS GONE (Ty 2026-11-09: "remove all of the saturation on all of the
                // airplanes"). It was a fitted tanh haircut - deflEff = ds*tanh(d/ds) with ds = Controls.Elevator.
                // SaturationDeg - put in so a 172 would not balance at 22 deg of wing AoA on full aft stick. That is
                // a WING-stall problem being paid for at the elevator, and the fleet's values had drifted from 3 deg
                // to 120 with no derivation: at 3 deg an elevator is very nearly dead. On the Pitts it was 20.3 deg
                // against 27 of travel, so full stick bought 17.6 - a 35 % cut before any aerodynamics, on top of an
                // elevator that already loses 84 % of its power between alpha 10 and 30. He flies both the Pitts and
                // the Extra and reports the elevator and rudder "significantly weaker than the real thing".
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
                    controlDeltaAlpha *= (1.0 - (1.0 - cellule!.AileronInduction) * stripAttached) / kCellStrip;

                // AILERONS in separated flow (owner directive): a deflected aileron on a stalled wing
                // section stops commanding lift but KEEPS its pressure load — so roll authority fades
                // with this strip's own separation memory while adverse yaw grows. Attached flight
                // keeps full roll power; its adverse yaw is the induced-drag asymmetry the polar already gives.
                bool isAileronStrip = strip.Control is not null && strip.Control.Surface == "aileron";
                double flapSep = 0.0, flapDelta = 0.0;
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
                    // The deflected surface's separated-flow pressure load (FLAP NORMAL, below): on the hinged row only, the one
                    // that physically moves (the fixed row's gain is a camber, not a deflection).
                    if (Math.Abs(strip.Control!.Gain) >= 0.9) { flapSep = sep; flapDelta = strip.Control.Gain * controlDeflRad; }
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

                // Each strip at its own Reynolds number (V·c/ν), where the section has a Reynolds family; else the one table.
                double stripRe = planeSpeed * strip.Chord / KinematicViscosity(airDensity);
                AeroCoefficients coeffs = SampleAtReynolds(config, airfoilTables, sectionKey, table, alpha, stripRe);
                // The hinged aileron row's drag: its own deflected-surface value, as before (see AILERON SECTIONS above).
                double hingedCd = double.NaN;
                if (!double.IsNaN(hingedDragDelta))
                    hingedCd = SampleAtReynolds(config, airfoilTables, sectionKey, table,
                                                (alphaBase + stripIncidence + hingedDragDelta) * (cosSweep < 0.9999 ? cosSweep : 1.0), stripRe).Cd;

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
                // wings of the same area would: each flies in the other's field. Scaling Cl here rather than capping
                // it takes the lift slope down with the peak, which is what the interference actually does, and the
                // induced-drag term below then follows from the reduced Cl on its own. OFF unless the type asks for
                // it -- see CelluleConfig and BiplaneCellule.
                //
                // THE FACTOR IS DERIVED, NOT FITTED (2026-11-10). It is BiplaneCellule.LiftInterferenceFor: the
                // lift-slope interference the class's own Biot-Savart lattice computes from this aeroplane's gap,
                // stagger, span and planform -- Prandtl's and Munk's biplane theory (NACA Rep. 116 / Rep. 151) solved
                // on the real wings -- and it is PER WING, because Munk's result is that a staggered cellule's two
                // wings are not affected alike. It replaces ClMaxFactor here, which was a standard-form fit to a
                // CLmax curve being multiplied in as though it were a lift slope; that one is now reported as an
                // independent cross-check and applied nowhere.
                // The roll rate's and the ailerons' contributions to this strip's alpha were divided by kCellStrip where
                // they were applied, so this scaling restores each of them to its own antisymmetric ratio and lands in
                // full only on the SYMMETRIC loading, which is the one it is the factor for.
                if (isWing && inCellule && kCellStrip < 1.0 - 1e-9)
                    coeffs = new AeroCoefficients(coeffs.Cl * kCellStrip, coeffs.Cd, coeffs.Cm);

                // PHYSICAL AIRBRAKES (SpoilerConfig.Physical): the strips under the brake plates lose the lift aft of the
                // plate and carry the plate's drag. Applied before the induced drag so a strip that has lost lift also
                // sheds its own induced drag -- the rest of the wing, flying at a higher angle to make up the lift, pays it.
                double cdBrake = 0.0;
                if (brakes is not null && isWing)
                {
                    double overlap = brakes.SpanOverlap(Math.Abs(strip.Pos[1]), strip.Chord > 1e-9 ? strip.Area / strip.Chord : 0.0);
                    if (overlap > 0.0)
                    {
                        double f = Math.Clamp(controls.SpoilerFraction, 0.0, 1.0) * overlap;
                        coeffs = new AeroCoefficients(coeffs.Cl * (1.0 - f * (1.0 - brakes.BrakeChordFraction)), coeffs.Cd, coeffs.Cm);
                        double ue = brakes.BrakeLocalVelocityRatio;
                        cdBrake = SpoilerConfig.BrakePlateCd * ue * ue * brakes.BrakeHeightM * f / strip.Chord;
                    }
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
                    flowState.InstCd[idx] = coeffs.Cd + cdInduced + cdBrake;
                    flowState.InstCm[idx] = coeffs.Cm;
                    flowState.ChordM[idx] = strip.Chord;
                    if (config.StallDynamics.UnsteadyLagEnabled && flowState.LagPrimed)
                    {
                        coeffs = new AeroCoefficients(flowState.LagCl[idx], flowState.LagCd[idx], flowState.LagCm[idx]);
                        cdInduced = 0; cdBrake = 0; // lagged Cd already contains them
                    }
                }

                // Tail strips fly in whatever wing or stabiliser wake reaches them (TailWake: each section's own wake, NACA
                // Report 648 width and depth from that section's drag). It replaces the single-wing wake band, its 0.45
                // maximum loss, the fin's 0.5 of it, the stab wedge's extra half, and the flat η_t = 0.9 (DATCOM's typical
                // value, which is this same wake averaged over a typical tail).
                // A biplane's wing strips fly in the OTHER wing's wake when it reaches them (TailWake.WingQFactor: only near
                // |α| 90°, where the air crosses the cellule from one wing to the other).
                // The receiving strip's own chord goes in: the geometric shadow marches from its load point and uses the
                // half-chord as the tolerance that expresses "half of this section is in shadow", so the wrong chord is
                // the wrong answer, not a scale factor.
                double qFactor = isWing ? TailWake.WingQFactor(wakes, stripAt, vAirStrip, surfaceIndex, strip.Chord)
                                        : TailWake.QFactor(wakes, stripAt, vAirStrip, isVertical, surfaceIndex, strip.Chord);

                double q = 0.5 * airDensity * planeSpeed * planeSpeed * qFactor;
                if (!isWing && strip.PosVec().X < -1.0 && ForceDebug.Samples is not null)
                {
                    // Tail flow for the overlay (protocol 7): the local airflow this stab strip flies in (downwash, slipstream,
                    // swirl, rotation included) as a body vector, and (α_t, ε, η_t = q_local / q_freestream).
                    // VERTICAL SURFACES INCLUDED since 2026-11-09: the fin and rudder were excluded, so the one diagnostic
                    // that says whether a surface has any dynamic pressure left did not cover the surface the owner reports
                    // as weakest in a spin. `SpinAuthorityProbe` found the rudder making 0.2 °/s² of yaw at α 91 and only
                    // 15.6 at α 30, where the elevator recovers from 1.3 to 44 — and there was no way to see why.
                    Vec3 vInf = bodyVelocity - windBody; double v2 = Math.Max(1e-6, vInf.X * vInf.X + vInf.Y * vInf.Y + vInf.Z * vInf.Z);
                    ForceDebug.Add(stripAt, new Vec3(planeSpeed * Math.Cos(alphaBase), vLocal.Y, planeSpeed * Math.Sin(alphaBase)),
                        new Vec3(alphaBase + strip.IncidenceRad, tailEps, qFactor * planeSpeed * planeSpeed / v2), "tailflow");
                    // And the SAME η_t split into its two factors, which the single number hides and which are not
                    // comparable between a horizontal and a vertical surface: `planeSpeed` is the flow in the strip's OWN
                    // plane (vertical: sqrt(u²+v²), horizontal: sqrt(u²+w²)), so a fin at α 90 is legitimately edge-on and
                    // its in-plane speed collapses for geometric reasons that are nothing to do with being blanketed.
                    //   X = planeSpeed (m/s, in-plane)   Y = qFactor (the wake/blanketing loss alone)   Z = η_t
                    // Mixing the two cost an hour on 2026-11-09: the fin's η_t of 0.10 against the stab's 1.44 reads like
                    // 8x of blanketing and is mostly the edge-on geometry.
                    if (ForceDebug.FlowDiagnostics)
                        ForceDebug.Add(stripAt, new Vec3(planeSpeed, qFactor, qFactor * planeSpeed * planeSpeed / v2),
                            new Vec3(isVertical ? 1 : 0, Math.Sqrt(v2), 0), "tailq");
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

                // SLIP ROLL, PER STRIP (see the accumulator's note above). This strip's share of TR-379.
                if (isWing && slipRollK > 0.0 && attached < 1.0)
                {
                    double vSlipLen = vLocal.Length;
                    if (vSlipLen > MinSpeedMs)
                    {
                        double betaLocal = Math.Asin(Math.Clamp(vLocal.Y / vSlipLen, -1.0, 1.0));
                        double cl20 = SlipRollCl20(slipRollKind, alpha * 180.0 / Math.PI);
                        if (cl20 > 0.0)
                        {
                            double bc = Math.Clamp(betaLocal, -SlipRollBeta20, SlipRollBeta20);
                            double sense = alpha >= 0 ? 1.0 : -1.0;     // inverted: negative lift, the moment reverses
                            slipRollMx += -sense * slipRollK * cl20 * (bc / SlipRollBeta20)
                                          * (1.0 - attached) * q * strip.Area * Math.Abs(strip.PosVec().Y);
                        }
                    }
                }
                double highLiftCl = (flapCl + slatCl) * attached;
                double clTotal = coeffs.Cl + highLiftCl;

                double lift = q * strip.Area * clTotal;
                double drag = q * strip.Area * (coeffs.Cd + cdInduced + flapCd + cdBrake);
                // STAB EFFECTIVENESS AT 60-90 DEGREES (SpinAeroConfig.StabHighAlphaK, owner 2026-11-09 after flying
                // 1912: "same flat spin problems make the stab more effective 60-90 aoa"). Scales the whole force of
                // a HORIZONTAL TAIL strip - so the stabiliser's own pitching moment and what its elevator can command
                // move together - over the band the owner named, folded about 90 deg and ramped in over 50-60 so
                // nothing steps. Nothing below 50 deg of LOCAL angle is touched, so no ordinary flight condition can
                // see it. See the config doc for why this is a knob and not a derivation.
                if (!isWing && !isVertical && stabHighAlphaK != 1.0)
                {
                    double ad = Math.Abs(alphaBase) * 57.2957795;
                    ad %= 360.0; if (ad > 180.0) ad = 360.0 - ad; if (ad > 90.0) ad = 180.0 - ad;
                    double ramp = Math.Clamp((ad - 50.0) / 10.0, 0.0, 1.0);
                    double f = 1.0 + (stabHighAlphaK - 1.0) * ramp;
                    lift *= f; drag *= f;
                }
                // Branch damage (dented leading edge, torn fabric): less lift and more drag from this strip.
                // THE TWO ARE READ SEPARATELY. ExtraCd used to be nested inside the LiftFactor test, so a strip given
                // extra drag and no lift loss was silently charged nothing — which is every drag-only damage there
                // can be, among them the torn end of a wing that has lost its outboard panel
                // (AircraftContactDamage.AddTornEndDrag, Ty 2026-11-10). Nothing in the fleet wrote one without the
                // other before, so this changes no existing number: ExtraCd is zero where it is not set.
                if (flowState?.LiftFactor is { } lf && idx < lf.Length) lift *= lf[idx];
                if (flowState?.ExtraCd is { } xcd && idx < xcd.Length) drag += q * strip.Area * xcd[idx];
                double momentC4 = q * strip.Area * strip.Chord * (coeffs.Cm + flapCm);

                Vec3 force = liftDir * lift - dragDir * drag;

                // A DEFLECTED CONTROL ON A BROADSIDE SURFACE IS A SPOILER, NOT A FLAP (SpinAeroConfig.ControlSpoilerK).
                // Ty 2026-11-10, from a clip of his own Pitts flat spin: "the elevator literally does nothing in the
                // spin", and "pushing the stick forward during a spin accelerates the spin ... I don't think our sim
                // gives the elevator enough authority to do that".
                //
                // MEASURED FIRST, because the obvious causes are all innocent (ElevatorBreakdownProbe, ELE_BRK=1). In
                // the developed spin the horizontal tail's own strips fly at 68-89 deg of LOCAL flow with eta_t
                // 1.16-1.22 - MORE dynamic pressure than the free stream, because the tail is swung 3.2 m out on a
                // 4.7 rad/s rotation. Area 1.88 m2 and a 3.2 m arm are both right. The only factor missing is the
                // coefficient: the elevator's whole travel changes Cn by 0.387 at alpha 30 but only 0.062 at alpha 80.
                //
                // That collapse is the flat-plate law being honest. The model moves a control by shifting its section's
                // incidence, and post-stall the section is CN = Cd_max*sin(theta), which is FLAT at theta ~ 90: bending
                // the back of the plate 27 deg either way changes which way the force points and not how big it is.
                // The probe shows exactly that - at alpha 80 the elevator moves 296 N of CHORDWISE force and 48 N of
                // normal force, and the chordwise force acts on a 0.07 m arm.
                //
                // What a real flapped plate does there is not a flap at all. The deflected surface sits in the
                // separated wake and displaces it to one side, so the BASE PRESSURE over the two faces of the fixed
                // surface stops matching: the whole tail feels a force normal to ITS OWN plane, proportional to how
                // far the control is bent into the wake (sin delta) and to how fully the surface is broadside
                // (sin alpha_chord). Hoerner treats spoilers and deflected plates in separated flow this way; strip
                // theory with a plain section table cannot produce it, because the table only knows one angle.
                //
                // Ramped in over 20 deg from SpinAeroConfig.ControlSpoilerOnsetDeg (30 deg as shipped, so 30-50) of
                // the strip's OWN flow angle, so attached flight, the spin entry and every normal control input are
                // untouched; it exists only where the section law has gone flat. 30 deg is where that is true of a
                // NACA 0012, whose stall is at 14; a FLAT-PLATE tail stalls at 9-12 and goes flat sooner, which is
                // why the onset is a field now and not a constant.
                //
                // BOTH TAILS since 2026-11-10. It shipped on the horizontal tail alone for one build, only so that the
                // fin's sidewash and this would not move at once; once the owner had flown that ("That is it! Now it
                // behaves like a Pitts really does") the restriction had no reason left. The rudder needs it for the
                // same reason the elevator did - `fin-lowAR` holds Cl flat from 25 deg out, which is the documented
                // arithmetic wall that defeated three attempts at reshaping that curve - and the Extra needs it most:
                // its fin stands too far off the body for FinSidewashK to reach, so its rudder gained only 1.0 -> 1.4
                // deg/s2 at alpha 80 from the crossflow that transformed the Pitts'.
                if (controlSpoilerK > 0.0 && !isWing && strip.Control is not null
                    && Math.Abs(strip.Control.Gain) >= 0.9 && Math.Abs(deflEff) > 1e-9)
                {
                    double aChord = alphaBase + stripIncidence;
                    double ad = Math.Abs(aChord) * 57.2957795;
                    ad %= 360.0; if (ad > 180.0) ad = 360.0 - ad; if (ad > 90.0) ad = 180.0 - ad;
                    double ramp = Math.Clamp((ad - spoilerOnsetDeg) / 20.0, 0.0, 1.0);
                    if (ramp > 0.0)
                    {
                        double n = controlSpoilerK * Math.Sin(strip.Control.Gain * deflEff) * Math.Abs(Math.Sin(aChord))
                                   * ramp * q * strip.Area;
                        Vec3 spoil = (liftDir * Math.Cos(aChord) - dragDir * Math.Sin(aChord)) * n;
                        force += spoil;
                        // Its own sample, or it is invisible to every probe: the overlay builds the "lift"/"drag"
                        // records from the scalars above, not from `force`, so a term added to the vector afterwards
                        // is felt by the aeroplane and seen by nobody. (That is how the first measurement of this
                        // term read as doing nothing at all.)
                        if (ForceDebug.Samples is not null) ForceDebug.Add(stripAt, spoil, Vec3.Zero, "lift");
                    }
                }
                // FLAP NORMAL (run #4, owner 2026-11-03: "full opposite aileron actually reduces the spin rate and lowers the
                // nose"). In separated flow the pressure on a plate acts NORMAL to the plate (Hoerner, Fluid-Dynamic Drag ch. 3),
                // and a deflected aileron is a plate turned δ about its hinge: its share of the section's normal force tilts
                // with it, so the deflection adds a CHORDWISE force CN·sin δ on the flap area — aft on the surface whose
                // trailing edge is deflected into the windward side, forward on the one deflected away. Odd in δ, so a pair
                // makes a yaw couple toward the down-going aileron (the adverse yaw that grows past the stall) and no net
                // drag. Turn the aeroplane over and the windward side changes with the sign of α, so the couple mirrors.
                // This replaces 1.2·sin²δ·1.5·sep applied as DRAG along the local wind: even in δ, the same on both wings, it
                // damped the rotation (the spin slowed with either aileron), and at spin α its chordwise part pushed aft on a
                // cellule that sits above the CG, pitching the nose body-UP whichever way up the aeroplane was: flatter upright,
                // steeper inverted. docs/CONTROL-SURFACES.md § "The aileron in separated flow".
                if (flapSep > 0.0)
                {
                    double cn = Math.Abs(coeffs.Cl * Math.Cos(alpha) + coeffs.Cd * Math.Sin(alpha));   // the section's normal-force coefficient
                    double fx = -Math.Sign(Math.Sin(alpha)) * cn * Math.Sin(flapDelta) * q * strip.Area * flapSep;
                    force += new Vec3(fx, 0, 0);
                    if (ForceDebug.Samples is not null) ForceDebug.Add(stripAt, new Vec3(fx, 0, 0), Vec3.Zero, "drag");
                }
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
        if (slipRollMx != 0.0)
        {
            totalMoment += new Vec3(slipRollMx, 0, 0);
            ForceDebug.Add(cg, Vec3.Zero, new Vec3(slipRollMx, 0, 0), "moment");
        }

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
                if (config.StallDynamics.SeparationOnPastStallDeg is double onPast && airfoilTables is not null
                    && airfoilTables.TryGetValue(strip.Airfoil, out AirfoilTable? oTab))
                {
                    // A type whose separation band is measured from each strip's own stall (StallDynamicsConfig) counts
                    // the strip stalled from that same angle, so the stalled FRACTION and the separation MEMORY agree.
                    // WingWake.StallAlpha's fixed 15° is the fleet's calibration against ~14° feel tables; on the
                    // Extra's derived wing (static stall ~22°) it collapsed the tail downwash five degrees early and the
                    // aeroplane could not be pulled to its own stall at idle.
                    stallAt = Math.Max(2.0 * Math.PI / 180.0, (localAlpha < 0.0 ? -oTab.AlphaClMinRad : oTab.AlphaClMaxRad) + onPast * Math.PI / 180.0);
                }
                else if (localAlpha < 0.0 && airfoilTables is not null && airfoilTables.TryGetValue(strip.Airfoil, out AirfoilTable? sTab))
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
        if (controls.SpoilerFraction <= 0.0 || config.Controls.Spoiler.Physical)
        {
            return;   // (physical brakes are flown on the wing strips themselves)
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

    /// <summary>Dynamic viscosity of air (Pa s): ISA sea level, 1.789e-5. Its temperature dependence (about -2 % per
    /// 1000 m in the standard atmosphere) is left out; the density term carries the altitude.</summary>
    public const double AirDynamicViscosity = 1.789e-5;
    public static double KinematicViscosity(double airDensity) => AirDynamicViscosity / Math.Max(airDensity, 1e-3);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, Dictionary<string, (double logRe, string key)[]>> _reFamilies = new();
    /// <summary>For every table in a Reynolds family, the family's members sorted by Re (cached per config).</summary>
    private static Dictionary<string, (double logRe, string key)[]> ReFamilies(AircraftConfig config) => _reFamilies.GetValue(config, cfg =>
    {
        var byFamily = new Dictionary<string, List<(double, string)>>();
        foreach (KeyValuePair<string, AirfoilTableData> kv in cfg.AirfoilTables)
            if (!string.IsNullOrEmpty(kv.Value.ReynoldsFamily) && kv.Value.ReynoldsNumber > 0)
            {
                if (!byFamily.TryGetValue(kv.Value.ReynoldsFamily, out var list)) byFamily[kv.Value.ReynoldsFamily] = list = new();
                list.Add((Math.Log(kv.Value.ReynoldsNumber), kv.Key));
            }
        var map = new Dictionary<string, (double, string)[]>();
        foreach (var fam in byFamily.Values)
        {
            var sorted = fam.OrderBy(m => m.Item1).ToArray();
            if (sorted.Length < 2) continue;
            foreach (var m in sorted) map[m.Item2] = sorted;
        }
        return map;
    });

    /// <summary>The section's coefficients at this angle, at the strip's own Reynolds number when its table belongs to
    /// a Reynolds family (linear in log Re between the bracketing members, held at the family's ends -- no
    /// extrapolation past what was measured); otherwise the table itself.</summary>
    public static AeroCoefficients SampleAtReynolds(AircraftConfig config, IReadOnlyDictionary<string, AirfoilTable> tables,
                                                    string key, AirfoilTable table, double alphaRad, double reynolds)
    {
        if (!ReFamilies(config).TryGetValue(key, out var fam) || !(reynolds > 0)) return table.Sample(alphaRad);
        double lr = Math.Log(reynolds);
        if (lr <= fam[0].logRe) return tables[fam[0].key].Sample(alphaRad);
        if (lr >= fam[^1].logRe) return tables[fam[^1].key].Sample(alphaRad);
        int i = 0;
        while (i < fam.Length - 2 && lr > fam[i + 1].logRe) i++;
        double t = (lr - fam[i].logRe) / (fam[i + 1].logRe - fam[i].logRe);
        AeroCoefficients a = tables[fam[i].key].Sample(alphaRad), b = tables[fam[i + 1].key].Sample(alphaRad);
        return new AeroCoefficients(a.Cl + t * (b.Cl - a.Cl), a.Cd + t * (b.Cd - a.Cd), a.Cm + t * (b.Cm - a.Cm));
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
    /// <summary>TR-379's measured C_l at 20 deg of yaw, interpolated at this angle of attack. Measured data, which is
    /// why it stays: the strip wing produces only 10-30 % of it above the stall and REVERSES SIGN at the stall itself
    /// (SlipRollGeometryProbe), so the three-dimensional spanwise separation effect it carries is not something a strip
    /// model can derive. What changed on 2026-11-09 is WHERE it is applied: per strip, not as one bulk moment.</summary>
    public static double SlipRollCl20(string kind, double alphaDeg)
    {
        if (kind.Length == 0) return 0.0;
        double a = Math.Abs(alphaDeg); if (a > 90) a = 180 - a;
        double[] tab = kind == "biplane" ? SlipRollBi : SlipRollMono;
        int i0 = 0; while (i0 < SlipRollAlpha.Length - 2 && a > SlipRollAlpha[i0 + 1]) i0++;
        double t = (a - SlipRollAlpha[i0]) / (SlipRollAlpha[i0 + 1] - SlipRollAlpha[i0]);
        return tab[i0] + Math.Clamp(t, 0, 1) * (tab[i0 + 1] - tab[i0]);
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
