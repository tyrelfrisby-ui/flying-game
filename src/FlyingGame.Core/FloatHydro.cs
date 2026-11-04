using System.Runtime.CompilerServices;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Seaplane float (and flying-boat hull) hydrodynamics, from first principles (owner 2026-11-02: "the water drag is too
/// high on the j3 on floats … the boyancy and planing on step all seem weak do a deep dive on the hydrodynamics").
/// No damping or "keel" fudges: every term below is a named piece of physics with its source, and every constant is
/// either a property of water or a published coefficient. docs/pending/HYDRO-J3-CLAUDE.md has the term table.
///
/// GEOMETRY. Each float is a <see cref="FloatHull"/>: stations bow → stern, each a closed section, cut by the
/// instantaneous water plane (heel, trim, heave and the local water height all enter through that plane).
///
/// FORCES, per float:
///  1. BUOYANCY: ρ g A(x) per station at the immersed section's centroid → the true displaced volume, the centre of
///     buoyancy moving with heel and trim, and so the real metacentric heights (Archimedes; strip integration).
///  2. STEP VENTILATION and the afterbody: behind a ventilated step the afterbody runs in the forebody's wake, whose
///     surface is SAVITSKY &amp; MORABITO (2010) "Surface wave contours associated with the forebody wake of stepped
///     planing hulls", Marine Technology 47(1), centreline profile (as reproduced by Gray-Stephens, Tezdogan &amp; Day
///     2021, Ship Technology Research, eq. 1): H/b = 0.17 (A + 0.03 L_k τ^1.5) sin((π/C_V)(X/3)^1.5) above the forebody
///     keel line extended aft (X, L_k in beams, τ in degrees; A = 2.0 for 20–30° deadrise, 1.5 for 10°). Whether the
///     step is ventilated at all is set by the transom-draft Froude number F_T = V/√(g d_step): wet below ≈2, dry above
///     ≈4 (transom-stern ventilation: Maki, Doctors et al. 2006; Robards &amp; Doctors 2003), linear between.
///     The pressure the dry step face no longer carries is the "hydrostatic drag" −ρ g Q_step (Q = ∫h dA of the
///     forebody's section at the step) — for a prismatic planing surface exactly Savitsky's buoyant Δ·tan τ.
///  3. PLANING (SAVITSKY 1964, "Hydrodynamic design of planing hulls", Marine Technology 1(1)), forebody, step = transom:
///     C_L0 = τ^1.1 (0.012 λ^½ + 0.0055 λ^2.5/C_V²), C_Lβ = C_L0 − 0.0065 β C_L0^0.6. The λ^2.5/C_V² term IS the
///     buoyancy, which (1) already carries from the real volume; so only the dynamic part is added, reduced by the
///     same deadrise ratio C_Lβ/C_L0, at Savitsky's dynamic centre 0.75 λ b ahead of the step, normal to the bottom
///     (so its drag is L tan τ). λ = (L_K + L_C)/2b with L_K the geometric wetted keel (from the station cuts) and
///     L_K − L_C = b tan β/(π tan τ) (Wagner's pile-up, Savitsky eq. 3); chines dry: Wagner's wetted beam π d/tan β.
///  4. FRICTION, ITTC-1957: C_F = 0.075/(log₁₀ Re − 2)² + ΔC_F 0.0004 (the roughness allowance Savitsky 1964 used), on
///     the wetted area — the station girths, or Savitsky's λ b²/cos β with the pile-up, whichever is the larger — at the
///     wetted length's Reynolds number, and in planing at Savitsky's mean bottom velocity V₁ = V (1 − 0.012 τ^1.1/(λ^½
///     cos τ))^½. Applied along the bottom.
///  5. WHISKER SPRAY drag (Savitsky, DeLorme &amp; Datla 2007, "Inclusion of whisker spray drag in performance prediction
///     method for high-speed planing hulls", Marine Technology 44(1)): friction on the bottom area the spray sheet
///     wets ahead of the spray root, between the root line (angle α to the keel, tan α = π tan τ/(2 tan β)) and the
///     jet (2α), b²/(8 sin 2α cos β) per side, its momentum along the hull cos 2α.
///  6. WAVE-MAKING (displacement regime): MICHELL's thin-ship integral on the actual hull at its rest waterline
///     (<see cref="FloatHull.MichellResistance"/>), tabulated in speed once per type, scaled by (displaced volume /
///     rest volume)² (thin-ship resistance is quadratic in the offsets), and handed over to (2)–(3) as the step
///     ventilates (×(1 − ventilated fraction)): once the step is dry the forebody's pressure drag IS the planing term.
///  7. SIDE FORCE: slender-body (Munk 1924 / Jones 1946) lift of the immersed hull as a low-aspect-ratio surface
///     with its free-surface image, f(x) = U ∂(m′v)/∂x, m′ = ρ π T²/2, up to the largest section (flow leaves there: the
///     step), plus viscous crossflow drag ½ ρ C_Dc T v|v| (C_Dc = 1.0 for a hard-chine section: Hoerner 1965, Fluid-
///     Dynamic Drag §3; the value Faltinsen 2005 uses for planing-hull crossflow). Both at mid-draft of each section,
///     so their roll moment arm is the real one.
///  8. HEAVE/PITCH/ROLL DAMPING in the displacement regime: wave radiation, 2-D strip theory with the deep-water Haskind
///     relation B₃₃′ = ρ g² Ā²/ω³, Ā = 2|sin(kB/2)| e^(−k T_m) (Froude–Krylov over the waterline beam, Newman 1977
///     §6.17), k = ω²/g at the type's own heave natural frequency ω = √(ρ g A_wp / m); plus viscous ½ ρ C_D B w|w|
///     with C_D = 1.2 (2-D V/plate normal to the flow, Hoerner 1965). Faded out with (1 − ventilated fraction): in
///     planing the same damping comes from (3), whose τ carries the heave and pitch velocity.
///  9. WATER RUDDER: a low-aspect-ratio plate under the stern, geometric immersion, Helmbold lift slope
///     2π A/(2 + √(A² + 4)) at A = 2 × the immersed aspect ratio (the float bottom over its root is an end plate:
///     Whicker &amp; Fehlner 1958), plus crossflow C_Dc 1.2 sin²α (Hoerner): C_N = C_Lα sin α cos α + 1.2 sin²α, normal
///     to the blade. It is effective only while its blade is in the water — on the step it is not.
///     A DRAWN, retractable blade (<see cref="FloatsConfig.WaterRudderChordM"/> &gt; 0: the J-3's) is modelled from where
///     the blade really is instead, cell by cell against the water and the step's wake, swung up by the pilot's retract
///     cable (<see cref="DrawnWaterRudder"/>).
/// Not modelled (stated, not fudged): added mass (it lowers the bob frequency), the two floats' wave interference,
/// the deck awash, spray lift, moving decks. Forces are world-frame, moments about the CG.
/// </summary>
public static class FloatHydro
{
    public const double WaterDensity = 1000.0;
    public const double WaterKinematicViscosity = 1.0e-6;   // fresh water ~20 °C (ITTC 2011 fresh-water table: 1.004e-6)

    // ---- published coefficients (see the summary for each source) ----
    public const double RoughnessAllowance = 0.0004;        // ΔC_F, Savitsky 1964
    public const double CrossflowCd = 1.0;                  // hard-chine section in crossflow (Hoerner 1965; Faltinsen 2005)
    public const double HeaveDragCd = 1.2;                  // 2-D V-bottom / plate normal to the flow (Hoerner 1965)
    public const double PlateCrossflowCd = 1.2;             // water-rudder blade, flat plate normal to the flow (Hoerner 1965)
    public const double VentWetFT = 2.0, VentDryFT = 4.0;   // transom-draft Froude numbers: step wet / fully ventilated

    /// <summary>
    /// Test hook: water everywhere at this height (m, up). Null = use the world terrain's water. Production never
    /// sets this; only tests do.
    ///
    /// THREAD-LOCAL deliberately (owner asked 2026-10-31 whether the real fix was being made, and it is this).
    /// As a plain static it was one value shared by seven test classes, and xUnit runs classes in parallel: each
    /// class wrapped its work in `finally { FlatWaterOverride = null; }`, so one class finishing cleared the water
    /// out from under another class's seaplane mid-flight. The hull was left floating on nothing and went NaN at
    /// rest — eight float and flying-boat tests failing in the suite while every one of them passed alone.
    ///
    /// Thread-local, a class cannot see another's override at all, so the collision cannot happen rather than
    /// merely being scheduled around. It costs nothing anywhere else: production never writes it, so every
    /// production thread reads the same null it read before, none of the ten call sites change, and the contact
    /// path — which asks for this at every contact point at every RK4 stage — is untouched.
    ///
    /// The alternative was passing a water source through all ten call sites. That is a real API change through the
    /// hot path for no gameplay benefit, when the only thing actually broken was tests sharing a static.
    /// </summary>
    public static double? FlatWaterOverride { get => _flatWaterOverride; set => _flatWaterOverride = value; }

    [ThreadStatic] private static double? _flatWaterOverride;

    public static double? WaterSurfaceAt(double x, double y)
    {
        if (FlatWaterOverride.HasValue) return FlatWaterOverride;
        return WorldTerrain.Active?.WaterSurfaceAt(x, y);
    }

    /// <summary>Water under a point at a known height. Behind a plunge fall the river runs out over an
    /// overhang, so the same x/y has water on the shelf and dry rock far below it — a wheel or a float needs
    /// the answer for where it actually is, not for the column it stands in.</summary>
    public static double? WaterSurfaceAt(double x, double y, double up)
    {
        if (FlatWaterOverride.HasValue) return FlatWaterOverride;
        return WorldTerrain.Active?.WaterSurfaceAt(x, y, up);
    }

    public readonly struct Report
    {
        public readonly double DraftAtStepM, WettedLambda, TrimDeg, SpeedCoefficient, PlaningLiftN, BuoyancyN;
        public readonly double DragN, VentilatedFraction;
        public Report(double draft, double lambda, double trim, double cv, double lift, double buoy, double drag = 0, double vent = 0)
        { DraftAtStepM = draft; WettedLambda = lambda; TrimDeg = trim; SpeedCoefficient = cv; PlaningLiftN = lift; BuoyancyN = buoy; DragN = drag; VentilatedFraction = vent; }
    }

    public static Report LastReport { get; private set; }

    /// <summary>Per-float state for effects (splash) and instrumentation: filled every Compute.</summary>
    public struct FloatState
    {
        public bool Wet;                 // any station immersed
        public double StepDraftM, BowDraftM, SternDraftM;   // keel immersion at step / bow-most wet station / stern
        public double DragN;             // this float's hydrodynamic resistance along the water velocity (all terms)
        public double PlaningLiftN, BuoyancyN, SpeedMs;
        public Vec3 StepWorld, BowWorld, SternWorld;        // NED world positions of the keel points
        public double LateralForceN;     // side force (slender-body lift + crossflow)
        public double TrimDeg, Lambda, VentilatedFraction, WaterRudderN;
        // Drag breakdown (N): friction, planing pressure (dynamic L tan τ), hydrostatic step-face, wave (Michell), spray
        public double FrictionN, PlaningPressureN, StepFaceN, WaveN, SprayN;
    }
    public static readonly FloatState[] Floats = new FloatState[4];   // 0 = left, 1 = right (hull: both), 2/3 = wingtip floats

    /// <summary>Beam and depth fractions at a station: <paramref name="u"/> = 0 at the step .. 1 at the bow (forebody) or the
    /// stern (afterbody). Full through the first part of each run, then linear to the configured end fractions.</summary>
    public static (double beam, double depth) Taper(FloatsConfig f, bool fore, double u)
    {
        double full = fore ? f.ForebodyFullFraction : 0.0;
        double t = System.Math.Clamp((u - full) / System.Math.Max(1e-6, 1.0 - full), 0.0, 1.0);
        double be = fore ? f.BowBeamFraction : f.SternBeamFraction, de = fore ? f.BowDepthFraction : f.SternDepthFraction;
        return (1.0 - (1.0 - be) * t, 1.0 - (1.0 - de) * t);
    }

    /// <summary>ITTC-1957 model–ship correlation line plus Savitsky's roughness allowance. Re clamped to 1e5 (the line's
    /// lower end; below it the flow is laminar and the drag is negligible anyway).</summary>
    public static double FrictionCoefficient(double re)
    {
        double lr = System.Math.Log10(System.Math.Max(re, 1e5));
        return 0.075 / ((lr - 2) * (lr - 2)) + RoughnessAllowance;
    }

    /// <summary>Savitsky (1964) lift coefficient of a prismatic planing surface, deadrise-corrected: (C_L0, C_Lβ).
    /// τ in degrees, β in degrees.</summary>
    public static (double Cl0, double ClBeta) SavitskyLift(double tauDeg, double lambda, double cv, double betaDeg)
    {
        double cl0 = System.Math.Pow(System.Math.Max(tauDeg, 0), 1.1) * (0.012 * System.Math.Sqrt(System.Math.Max(lambda, 0))
                     + 0.0055 * System.Math.Pow(System.Math.Max(lambda, 0), 2.5) / System.Math.Max(cv * cv, 1e-6));
        double clb = cl0 - 0.0065 * betaDeg * System.Math.Pow(cl0, 0.6);
        return (cl0, System.Math.Max(0.0, clb));
    }

    /// <summary>Savitsky &amp; Morabito (2010) centreline wake height above the forebody keel line extended aft, in beams.
    /// X and L_k in beams, τ in degrees. The sine is held at its crest past the first quarter wave (the formula is
    /// fitted for X ≤ 3 b; beyond the crest the real wake rises on to the free surface, which caps it).</summary>
    public static double WakeHeightBeams(double xBeams, double lkBeams, double tauDeg, double cv, double betaDeg)
    {
        if (xBeams <= 0 || cv <= 0) return 0.0;
        double a = betaDeg >= 20 ? 2.0 : 1.5 + 0.5 * System.Math.Clamp((betaDeg - 10) / 10, 0, 1);
        double arg = System.Math.Min(System.Math.PI / cv * System.Math.Pow(xBeams / 3.0, 1.5), System.Math.PI / 2);
        return 0.17 * (a + 0.03 * lkBeams * System.Math.Pow(System.Math.Max(tauDeg, 0), 1.5)) * System.Math.Sin(arg);
    }

    /// <summary>Fraction of the step (or a transom) that is ventilated (dry), from the transom-draft Froude number.</summary>
    public static double VentilatedFraction(double speed, double depth)
    {
        if (depth <= 1e-4) return 1.0;
        double ft = speed / System.Math.Sqrt(Atmosphere.GravityMs2 * depth);
        return System.Math.Clamp((ft - VentWetFT) / (VentDryFT - VentWetFT), 0.0, 1.0);
    }

    /// <summary>Helmbold's lift slope (per rad) of a low-aspect-ratio wing.</summary>
    public static double HelmboldSlope(double ar) => 2 * System.Math.PI * ar / (2 + System.Math.Sqrt(ar * ar + 4));

    // ---- rest hydrostatics, once per type --------------------------------------------------------------------------

    /// <summary>What a float type does at rest in calm water at its config weight, level trim; and the Michell table.</summary>
    public sealed class Statics
    {
        public double RestWaterlineZ;     // datum z (down) of the level rest waterline
        public double RestVolumeEach;     // m³ displaced per main float at rest
        public double VolumeEach;         // m³, the whole float
        public double WaterplaneTotal;    // m², all main floats at rest
        public double HeaveOmega;         // rad/s, √(ρ g A_wp / m)
        public double[] WaveSpeeds = System.Array.Empty<double>(), WaveN = System.Array.Empty<double>();
        public double WaveAt(double v)
        {
            if (WaveSpeeds.Length == 0 || v <= WaveSpeeds[0]) return 0.0;
            if (v >= WaveSpeeds[^1]) return WaveN[^1] * (WaveSpeeds[^1] / v) * (WaveSpeeds[^1] / v);   // (decays past the table)
            int i = System.Array.BinarySearch(WaveSpeeds, v); if (i < 0) i = ~i - 1;
            double t = (v - WaveSpeeds[i]) / (WaveSpeeds[i + 1] - WaveSpeeds[i]);
            return WaveN[i] + t * (WaveN[i + 1] - WaveN[i]);
        }
    }

    private static readonly ConditionalWeakTable<AircraftConfig, Statics> StaticsCache = new();

    public static Statics RestStatics(AircraftConfig c) => StaticsCache.GetValue(c, BuildStatics);

    private static Statics BuildStatics(AircraftConfig c)
    {
        FloatsConfig f = c.Floats!;
        FloatHull h = FloatHull.For(f);
        int n = f.Count == 1 ? 1 : 2;
        double share = c.Mass.MassKg / WaterDensity / n;
        var st = new Statics { VolumeEach = h.Volume };
        st.RestVolumeEach = System.Math.Min(share, 0.98 * st.VolumeEach);
        st.RestWaterlineZ = h.WaterlineFor(st.RestVolumeEach);
        // Waterplane at rest: the waterline beam (both crossings) times each station's weight.
        var sy = new double[h.MaxPoly]; var sz = new double[h.MaxPoly]; var cut = new FloatHull.Cut();
        double awp = 0;
        for (int i = 0; i < h.X.Length; i++)
        {
            h.CutStation(i, -st.RestWaterlineZ, 0, 1, ref cut, sy, sz);
            if (cut.PortCross && cut.StbdCross) awp += (cut.StbdY - cut.PortY) * h.W[i];
        }
        st.WaterplaneTotal = awp * n;
        st.HeaveOmega = System.Math.Sqrt(WaterDensity * Atmosphere.GravityMs2 * System.Math.Max(st.WaterplaneTotal, 1e-3) / c.Mass.MassKg);
        // Michell at the rest waterline, 0.25 … 10 m/s (past the hump the step is dry and the wave term is handed over).
        var vs = new System.Collections.Generic.List<double>(); var rs = new System.Collections.Generic.List<double>();
        for (double v = 0.25; v <= 10.001; v += 0.25) { vs.Add(v); rs.Add(h.MichellResistance(st.RestWaterlineZ, v, WaterDensity, Atmosphere.GravityMs2)); }
        st.WaveSpeeds = vs.ToArray(); st.WaveN = rs.ToArray();
        return st;
    }

    // ---- the per-step computation -----------------------------------------------------------------------------------

    [ThreadStatic] private static FloatHull.Cut[]? _cuts;
    [ThreadStatic] private static double[]? _sy, _sz, _mp, _vy, _ux;

    private static void Scratch(FloatHull h)
    {
        int n = h.X.Length, m = h.MaxPoly;
        if (_cuts == null || _cuts.Length < n) { _cuts = new FloatHull.Cut[n]; _mp = new double[n]; _vy = new double[n]; _ux = new double[n]; }
        if (_sy == null || _sy.Length < m) { _sy = new double[m]; _sz = new double[m]; }
    }

    /// <summary>The float set: (block, y of its centreline, state slot, carries a water rudder).</summary>
    private static int FloatSet(FloatsConfig main, (FloatsConfig f, double yF, int slot, bool rudder)[] set)
    {
        int k = 0;
        bool left = main.WaterRudderSides != "right", right = main.WaterRudderSides != "left";
        if (main.Count == 1) set[k++] = (main, 0.0, 0, true);
        else { set[k++] = (main, -main.SpreadM * 0.5, 0, left); set[k++] = (main, main.SpreadM * 0.5, 1, right); }
        if (main.TipFloats is { } tip) { set[k++] = (tip, -tip.SpreadM * 0.5, 2, false); set[k++] = (tip, tip.SpreadM * 0.5, 3, false); }
        return k;
    }

    [ThreadStatic] private static (FloatsConfig f, double yF, int slot, bool rudder)[]? _set;

    /// <summary>Total world-frame force and moment (about the CG) from all floats. Zero when clear of water.</summary>
    public static (Vec3 Force, Vec3 Moment) Compute(AircraftConfig config, RigidBodyState s, double rudderCmd)
        => Compute(config, s, rudderCmd, 0.0);

    /// <param name="waterRudderUp01">How far the pilot has the water rudder raised: 0 = down (the spring's default,
    /// and every water start), 1 = hung on the hook, fully up. Only read for a drawn, retractable blade.</param>
    public static (Vec3 Force, Vec3 Moment) Compute(AircraftConfig config, RigidBodyState s, double rudderCmd, double waterRudderUp01)
    {
        FloatsConfig main = config.Floats!;
        LastWaterRudder = default;
        Statics stx = RestStatics(config);
        _set ??= new (FloatsConfig, double, int, bool)[4];
        int nSet = FloatSet(main, _set);
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        double draftReport = 0, lamReport = 0, trimReport = 0, cvReport = 0, liftReport = 0, buoyReport = 0, dragReport = 0, ventReport = 0;
        for (int k = 0; k < nSet; k++)
        {
            (FloatsConfig f, double yF, int slot, bool hasRudder) = _set[k];
            FloatState fs = OneFloat(config, f, yF, hasRudder && f == main, slot >= 2 ? null : stx, s, rudderCmd, waterRudderUp01, ref totalF, ref totalM);
            Floats[slot] = fs;
            if (main.Count == 1 && slot == 0) Floats[1] = fs;   // a hull: both "float" slots read the same state
            buoyReport += fs.BuoyancyN; liftReport += fs.PlaningLiftN; dragReport += fs.DragN;
            if (slot < 2)
            {
                draftReport = System.Math.Max(draftReport, fs.StepDraftM);
                if (fs.PlaningLiftN > 0) { lamReport = fs.Lambda; trimReport = fs.TrimDeg; }
                cvReport = fs.SpeedMs / System.Math.Sqrt(Atmosphere.GravityMs2 * FloatHull.For(f).Beam);
                ventReport = System.Math.Max(ventReport, fs.VentilatedFraction);
            }
        }
        LastReport = new Report(draftReport, lamReport, trimReport, cvReport, liftReport, buoyReport, dragReport, ventReport);
        return (totalF, totalM);
    }

    private static FloatState OneFloat(AircraftConfig config, FloatsConfig f, double yF, bool hasRudder, Statics? stx,
        RigidBodyState s, double rudderCmd, double waterRudderUp01, ref Vec3 sumF, ref Vec3 sumM)
    {
        (FloatState fs, Vec3 force, Vec3 moment) = OneFloatImpl(config, f, yF, hasRudder, stx, s, rudderCmd, waterRudderUp01);
        sumF += force; sumM += moment;
        return fs;
    }

    private static (FloatState, Vec3, Vec3) OneFloatImpl(AircraftConfig config, FloatsConfig f, double yF, bool hasRudder, Statics? stx,
        RigidBodyState s, double rudderCmd, double waterRudderUp01)
    {
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        FloatHull h = FloatHull.For(f);
        Scratch(h);
        FloatHull.Cut[] cuts = _cuts!;
        double g = Atmosphere.GravityMs2, rho = WaterDensity;
        Vec3 cg = config.Mass.CgVec();
        Quat q = s.Attitude;
        Vec3 ex = q.Rotate(new Vec3(1, 0, 0)), ey = q.Rotate(new Vec3(0, 1, 0)), ez = q.Rotate(new Vec3(0, 0, 1));
        var fs = new FloatState();
        int n = h.X.Length;

        // Water height at the bow, step and stern keel points (interpolated along x between them).
        Vec3 Pt(double x, double y, double z) => s.Position + q.Rotate(new Vec3(x, yF + y, z) - cg);
        Vec3 pBow = Pt(h.BowX, 0, h.KeelOf(0)), pStep = Pt(h.StepX, 0, h.KeelZStep), pStern = Pt(h.SternX, 0, h.KeelOf(n - 1));
        double? wB = WaterSurfaceAt(pBow.X, pBow.Y, -pBow.Z), wS = WaterSurfaceAt(pStep.X, pStep.Y, -pStep.Z), wA = WaterSurfaceAt(pStern.X, pStern.Y, -pStern.Z);
        fs.StepWorld = pStep; fs.BowWorld = pBow; fs.SternWorld = pStern;
        if (wB is null && wS is null && wA is null) return (fs, totalF, totalM);
        double wb = wB ?? wS ?? wA!.Value, ws = wS ?? wb, wa = wA ?? ws;
        // A float out of the water entirely (every keel point above the surface by more than its depth): nothing to do.
        double WaterAt(double x) => x >= h.StepX ? ws + (wb - ws) * (x - h.StepX) / System.Math.Max(h.BowX - h.StepX, 1e-6)
                                                 : ws + (wa - ws) * (h.StepX - x) / System.Math.Max(h.StepX - h.SternX, 1e-6);

        // Water velocity relative to the float at the step (world), speed and speed coefficient.
        Vec3 Vel(double x, double y, double z) => q.Rotate(s.Velocity + Vec3.Cross(s.Rates, new Vec3(x, yF + y, z) - cg));
        Vec3 vStep = Vel(h.StepX, 0, h.KeelZStep);
        double vH = System.Math.Sqrt(vStep.X * vStep.X + vStep.Y * vStep.Y);
        fs.SpeedMs = vH;
        double b = h.Beam, beta = h.DeadriseRad, betaDeg = beta * 180 / System.Math.PI, tanB = System.Math.Tan(beta);

        // Forebody trim to the flow: the prismatic keel line's angle above the horizontal along the motion, plus the
        // step's sink angle (Savitsky's τ is the angle of the bottom to the oncoming water).
        Vec3 dk = q.Rotate(new Vec3(1, 0, h.ForeKeelSlope).Normalized());
        double dkh = vH > 1e-6 ? (dk.X * vStep.X + dk.Y * vStep.Y) / vH : System.Math.Sqrt(dk.X * dk.X + dk.Y * dk.Y);
        double keelPitch = System.Math.Atan2(-dk.Z, System.Math.Max(System.Math.Abs(dkh), 1e-9)) * (dkh >= 0 ? 1 : -1);
        double sink = System.Math.Atan2(vStep.Z, System.Math.Max(vH, 0.05));   // NED: +Z down = sinking
        double tauRad = keelPitch + sink, tauDeg = tauRad * 180 / System.Math.PI;

        // Station plane depth function h = h0 + hy·y + hz·z (exact for a plane water surface over the station).
        void Plane(int i, double water, out double h0, out double hy, out double hz)
        {
            Vec3 r = new Vec3(h.X[i], yF, 0) - cg;
            Vec3 rw = q.Rotate(r);
            h0 = water + s.Position.Z + rw.Z; hy = ey.Z; hz = ez.Z;
        }

        // ---- forebody (undisturbed surface) ----
        double buoy = 0;
        for (int i = 0; i <= h.LastFore; i++)
        {
            Plane(i, WaterAt(h.X[i]), out double h0, out double hy, out double hz);
            h.CutStation(i, h0, hy, hz, ref cuts[i], _sy!, _sz!);
        }
        double dStep = cuts[h.LastFore].KeelDepth;
        // Wetted keel length ahead of the step, from the station cuts (the keel crosses the surface between two stations).
        double lk = 0;
        if (dStep > 0)
        {
            lk = h.ForebodyLength;
            for (int i = h.LastFore - 1; i >= 0; i--)
            {
                double ka = cuts[i].KeelDepth, kb = cuts[i + 1].KeelDepth;
                if (ka > 0) continue;
                lk = h.X[i + 1] - h.StepX + (h.X[i] - h.X[i + 1]) * kb / System.Math.Max(kb - ka, 1e-9);
                break;
            }
        }
        double vent = VentilatedFraction(vH, dStep);
        fs.VentilatedFraction = vent;

        // ---- afterbody: in the forebody's wake once the step ventilates ----
        double cvB = vH / System.Math.Sqrt(g * b);
        for (int i = h.FirstAft; i < n; i++)
        {
            double water = WaterAt(h.X[i]);
            Plane(i, water, out double h0, out double hy, out double hz);
            if (vent > 0 && dStep > 0)
            {
                // The wake surface's height (up) at this station: the forebody keel line extended aft, plus H.
                double xa = h.StepX - h.X[i];
                Vec3 pk = Pt(h.X[i], 0, h.KeelZStep - h.ForeKeelSlope * xa);
                double wakeUp = -pk.Z + b * WakeHeightBeams(xa / b, lk / b, tauDeg, cvB, betaDeg);
                h0 -= vent * System.Math.Max(0.0, water - wakeUp);
            }
            h.CutStation(i, h0, hy, hz, ref cuts[i], _sy!, _sz!);
        }

        // ---- 1. buoyancy, station by station; and the per-station water velocities for 7/8 ----
        double wetFrontX = double.NaN, wetBackX = double.NaN, girthFore = 0, girthAft = 0, wetAftFront = double.NaN, wetAftBack = double.NaN;
        Vec3 cbSum = Vec3.Zero;
        double viscZ = 0, kRad = stx != null ? stx.HeaveOmega * stx.HeaveOmega / g : 0;
        for (int i = 0; i < n; i++)
        {
            ref FloatHull.Cut c = ref cuts[i];
            if (c.Area <= 0) { _mp![i] = 0; continue; }
            fs.Wet = true;
            if (double.IsNaN(wetFrontX)) wetFrontX = h.X[i];
            wetBackX = h.X[i];
            if (h.Aft[i]) { girthAft += c.Girth * h.W[i]; if (double.IsNaN(wetAftFront)) wetAftFront = h.X[i]; wetAftBack = h.X[i]; }
            else girthFore += c.Girth * h.W[i];
            double fb = rho * g * c.Area * h.W[i];
            buoy += fb;
            Vec3 rc = q.Rotate(new Vec3(h.X[i], yF + c.Cy, c.Cz) - cg);
            Vec3 fW = new Vec3(0, 0, -fb);
            cbSum += rc * fb;
            // 8. vertical damping at the section centroid: radiation (displacement regime) + viscous
            Vec3 vc = q.Rotate(s.Velocity + Vec3.Cross(s.Rates, new Vec3(h.X[i], yF + c.Cy, c.Cz) - cg));
            double bwl = c.PortCross && c.StbdCross ? c.StbdY - c.PortY : h.Beam;
            double tm = c.Area / System.Math.Max(bwl, 1e-3);
            double abar = 2 * System.Math.Abs(System.Math.Sin(kRad * bwl / 2)) * System.Math.Exp(-kRad * tm);
            double b33 = stx != null ? rho * g * g * abar * abar / (stx.HeaveOmega * stx.HeaveOmega * stx.HeaveOmega) : 0;
            double fz = -(1 - vent) * b33 * vc.Z * h.W[i] - 0.5 * rho * HeaveDragCd * bwl * vc.Z * System.Math.Abs(vc.Z) * h.W[i];
            viscZ += fz;
            fW += new Vec3(0, 0, fz);
            totalF += fW; totalM += Vec3.Cross(rc, fW);
            // for the side force: added mass per length with the free-surface image, local lateral and axial water speed
            double T = System.Math.Max(0, c.KeelDepth);
            _mp![i] = rho * System.Math.PI * T * T / 2;
            Vec3 vb = s.Velocity + Vec3.Cross(s.Rates, new Vec3(h.X[i], yF, 0.5 * (c.WetTopZ + h.KeelOf(i))) - cg);
            _vy![i] = vb.Y; _ux![i] = vb.X;
        }
        fs.BuoyancyN = buoy;
        fs.StepDraftM = System.Math.Max(0, dStep);
        fs.BowDraftM = System.Math.Max(0, cuts[0].KeelDepth);
        for (int i = 0; i < n; i++) if (cuts[i].KeelDepth > 0) { fs.BowDraftM = cuts[i].KeelDepth; break; }
        fs.SternDraftM = System.Math.Max(0, cuts[n - 1].KeelDepth);
        if (!fs.Wet) return (fs, totalF, totalM);
        Vec3 cb = buoy > 0 ? cbSum * (1.0 / buoy) : q.Rotate(new Vec3(h.StepX, yF, h.KeelZStep) - cg);

        // Unit vector of the horizontal water velocity over the float (drag direction) and the bottom's direction.
        Vec3 dragDir = vH > 1e-6 ? new Vec3(-vStep.X / vH, -vStep.Y / vH, 0) : Vec3.Zero;
        Vec3 alongBottom = vH > 1e-6 ? (dkh >= 0 ? dk * -1.0 : dk) : Vec3.Zero;   // opposite to the motion, along the keel
        double drag = 0;
        void Apply(Vec3 rBodyFromDatum, Vec3 fWorld) { Vec3 rw = q.Rotate(rBodyFromDatum - cg); totalF += fWorld; totalM += Vec3.Cross(rw, fWorld); }

        // ---- 2. the dry step face (and the float's own stern transom) ----
        {
            ref FloatHull.Cut cf = ref cuts[h.LastFore];
            double qa = h.FirstAft < n ? cuts[h.FirstAft].Q : 0;
            double fStep = rho * g * vent * System.Math.Max(0, cf.Q - qa);
            ref FloatHull.Cut cs = ref cuts[n - 1];
            double vStern = VentilatedFraction(vH, cs.KeelDepth);
            double fStern = rho * g * vStern * System.Math.Max(0, cs.Q);
            // along body −x (the face is a station plane)
            if (fStep > 0) Apply(new Vec3(h.X[h.LastFore], yF + cf.Cy, cf.Cz), ex * -fStep);
            if (fStern > 0) Apply(new Vec3(h.X[n - 1], yF + cs.Cy, cs.Cz), ex * -fStern);
            fs.StepFaceN = fStep + fStern; drag += fStep + fStern;
        }

        // ---- 3–5. planing forebody (Savitsky) ----
        double rdeg = 0, lambda = 0, bw = b, lift = 0, sForeSav = 0, v1 = vH;
        if (lk > 1e-3 && dStep > 0 && vH > 0.3 && tauDeg > 0.1)
        {
            double tanT = System.Math.Tan(tauRad);
            double lc = lk - b * tanB / (System.Math.PI * tanT);
            if (lc > 0) lambda = (lk + lc) / (2 * b);
            else { bw = System.Math.Min(b, System.Math.PI * dStep / tanB); lambda = lk / (2 * bw); }
            lambda = System.Math.Min(lambda, h.ForebodyLength / bw);
            double cv = vH / System.Math.Sqrt(g * bw);
            double xcp = h.StepX + 0.75 * lambda * bw;
            double zcp = h.KeelZStep + h.ForeKeelSlope * (xcp - h.StepX);
            // The incidence the dynamic lift sees is at its own centre of pressure: the bottom's sink there includes
            // the pitch rate times the arm from the CG (quasi-steady planing; this is the planing surface's pitch
            // damping, which the step point alone does not have).
            Vec3 vCp = Vel(xcp, 0, zcp);
            double tauDyn = keelPitch + System.Math.Atan2(vCp.Z, System.Math.Max(vH, 0.05));
            double tauDynDeg = System.Math.Max(0.0, tauDyn * 180 / System.Math.PI);
            (double cl0, double clb) = SavitskyLift(tauDynDeg, lambda, cv, betaDeg);
            double ratio = cl0 > 1e-9 ? clb / cl0 : 0;
            double clDyn = ratio * System.Math.Pow(tauDynDeg, 1.1) * 0.012 * System.Math.Sqrt(lambda);
            lift = 0.5 * rho * vH * vH * bw * bw * clDyn;
            tanT = System.Math.Tan(System.Math.Max(0.0, tauDyn));
            Vec3 fLift = new Vec3(0, 0, -lift) + dragDir * (lift * tanT);
            Apply(new Vec3(xcp, yF, zcp), fLift);
            fs.PlaningPressureN = lift * tanT; drag += lift * tanT;
            // mean bottom velocity, planing wetted area (with the pile-up)
            double pr = 0.012 * System.Math.Pow(tauDeg, 1.1) / (System.Math.Sqrt(System.Math.Max(lambda, 1e-3)) * System.Math.Cos(tauRad));
            v1 = vH * System.Math.Sqrt(System.Math.Max(0.0, 1 - pr));
            sForeSav = lambda * bw * bw / System.Math.Cos(beta);
            // 5. whisker spray
            double alpha = System.Math.Atan(System.Math.PI * tanT / (2 * tanB));
            double s2a = System.Math.Sin(2 * alpha);
            if (s2a > 1e-3)
            {
                double aws = 2 * bw * bw / (8 * s2a * System.Math.Cos(beta));
                double lws = 0.5 * bw / s2a;
                double dws = 0.5 * rho * vH * vH * aws * FrictionCoefficient(vH * lws / WaterKinematicViscosity) * System.Math.Cos(2 * alpha);
                Apply(new Vec3(h.StepX + lk, yF, h.KeelZStep + h.ForeKeelSlope * lk), dragDir * dws);
                fs.SprayN = dws; drag += dws;
            }
            rdeg = tauDeg;
        }
        fs.PlaningLiftN = lift; fs.TrimDeg = tauDeg; fs.Lambda = lambda;

        // ---- 3b. the afterbody planing in the forebody's wake (or in the undisturbed water at high trim) ----
        // A wetted afterbody is a planing surface of its own, with the stern as its trailing edge: same Savitsky
        // dynamic lift, at its own incidence to the water it meets (the angle between its keel and the local
        // effective surface, from the keel depths, plus the stern's sink angle), its own wetted length and the same
        // beam. Its lift is aft of the CG: it is what holds the nose down at the hump and what slaps the stern up when
        // the trim is too high (the upper-limit porpoise). Only once the step ventilates: before that the afterbody
        // is part of one closed displacement hull and the flow follows it.
        if (vent > 0 && vH > 0.3 && h.FirstAft < n && cuts[n - 1].KeelDepth > 0)
        {
            int j = n - 1;
            while (j > h.FirstAft && cuts[j - 1].KeelDepth > 0) j--;
            double ka = j > h.FirstAft ? cuts[j - 1].KeelDepth : cuts[j].KeelDepth, kb = cuts[j].KeelDepth;
            double xFront = j > h.FirstAft ? h.X[j] + (h.X[j - 1] - h.X[j]) * kb / System.Math.Max(kb - ka, 1e-9) : h.X[j];
            double la = System.Math.Max(0, xFront - h.SternX);
            double dS = cuts[n - 1].KeelDepth;
            if (la > 0.02)
            {
                Vec3 vSt = Vel(h.SternX, 0, h.KeelOf(n - 1));
                double tauA = System.Math.Atan2(dS, la) + System.Math.Atan2(vSt.Z, System.Math.Max(vH, 0.05));
                double tauAd = tauA * 180 / System.Math.PI;
                if (tauAd > 0.1)
                {
                    double tanTa = System.Math.Tan(tauA);
                    double bA = 0; foreach (var p in h.Poly[n - 1]) bA = System.Math.Max(bA, 2 * System.Math.Abs(p.Y));
                    bA = System.Math.Max(bA, 0.5 * h.Beam);
                    double lcA = la - bA * tanB / (System.Math.PI * tanTa);
                    double bwA = bA, lamA;
                    if (lcA > 0) lamA = (la + lcA) / (2 * bA);
                    else { bwA = System.Math.Min(bA, System.Math.PI * dS / tanB); lamA = la / (2 * bwA); }
                    double cvA = vH / System.Math.Sqrt(g * bwA);
                    (double c0a, double cba) = SavitskyLift(tauAd, lamA, cvA, betaDeg);
                    double liftA = 0.5 * rho * vH * vH * bwA * bwA * (c0a > 1e-9 ? cba / c0a : 0) * System.Math.Pow(tauAd, 1.1) * 0.012 * System.Math.Sqrt(lamA) * vent;
                    double xcpA = h.SternX + 0.75 * lamA * bwA;
                    Apply(new Vec3(xcpA, yF, h.KeelOf(n - 1)), new Vec3(0, 0, -liftA) + dragDir * (liftA * tanTa));
                    fs.PlaningLiftN += liftA; fs.PlaningPressureN += liftA * tanTa; drag += liftA * tanTa;
                }
            }
        }

        // ---- 4. friction: forebody (planing or displacement), afterbody ----
        if (vH > 0.05)
        {
            double sFore = System.Math.Max(girthFore, sForeSav);
            double lFore = double.IsNaN(wetFrontX) ? 0 : System.Math.Max(lambda * bw, System.Math.Max(0, System.Math.Min(wetFrontX, h.BowX) - h.StepX));
            double vf = sForeSav > girthFore ? v1 : vH;
            double dfF = sFore > 0 ? 0.5 * rho * vf * vf * sFore * FrictionCoefficient(vf * System.Math.Max(lFore, 0.05) / WaterKinematicViscosity) : 0;
            double dfA = 0;
            if (girthAft > 0)
            {
                double lAft = System.Math.Max(0.05, wetAftFront - wetAftBack);
                dfA = 0.5 * rho * vH * vH * girthAft * FrictionCoefficient(vH * lAft / WaterKinematicViscosity);
            }
            double xf = h.StepX + 0.5 * System.Math.Max(lFore, 0);
            if (dfF > 0) Apply(new Vec3(xf, yF, h.KeelZStep + h.ForeKeelSlope * (xf - h.StepX)), alongBottom * dfF);
            if (dfA > 0) Apply(new Vec3(0.5 * (wetAftFront + wetAftBack), yF, h.KeelZStep), dragDir * dfA);
            fs.FrictionN = dfF + dfA; drag += dfF + dfA;
        }

        // ---- 6. wave-making (Michell), displacement regime ----
        if (stx != null && vH > 0.2 && vent < 1)
        {
            double vr = buoy / (rho * g) / System.Math.Max(stx.RestVolumeEach, 1e-6);
            double rw = stx.WaveAt(vH) * vr * vr * (1 - vent);
            if (rw > 0)
            {
                Vec3 fw = dragDir * rw;
                totalF += fw; totalM += Vec3.Cross(cb, fw);
                fs.WaveN = rw; drag += rw;
            }
        }

        // ---- 7. side force: slender-body lift up to the largest section, then viscous crossflow ----
        {
            double lat = 0;
            int jmax = 0; for (int i = 1; i < n; i++) if (_mp![i] > _mp[jmax]) jmax = i;
            double u = 0; int cnt = 0;
            for (int i = 0; i < n; i++) if (_mp![i] > 0) { u += _ux![i]; cnt++; }
            u = cnt > 0 ? u / cnt : 0;
            bool fwd = u >= 0;
            double prev = 0, zAt = h.KeelZStep, xAt = h.StepX;
            // walk with the flow: bow → largest section going ahead, stern → largest section going astern
            for (int step = 0; step < n; step++)
            {
                int i = fwd ? step : n - 1 - step;
                if (fwd ? i > jmax : i < jmax) break;
                double mv = _mp![i] * _vy![i];
                double fy = -System.Math.Abs(u) * (mv - prev);
                prev = mv;
                ref FloatHull.Cut c = ref cuts[i];
                // a dry station (the keel out of the water between two wet ones) takes its change at the last wet section
                if (c.Area > 0) { zAt = 0.5 * (c.WetTopZ + h.KeelOf(i)); xAt = h.X[i]; }
                if (fy == 0) continue;
                Apply(new Vec3(xAt, yF, zAt), ey * fy);
                lat += fy;
            }
            for (int i = 0; i < n; i++)
            {
                ref FloatHull.Cut c = ref cuts[i];
                if (c.Area <= 0) continue;
                double T = System.Math.Max(0, c.KeelDepth);
                double fy = -0.5 * rho * CrossflowCd * T * _vy![i] * System.Math.Abs(_vy[i]) * h.W[i];
                Apply(new Vec3(h.X[i], yF, 0.5 * (c.WetTopZ + h.KeelOf(i))), ey * fy);
                lat += fy;
            }
            fs.LateralForceN = lat;
        }

        // ---- 9. water rudder ----
        if (hasRudder && f.WaterRudderChordM > 0 && f.WaterRudderSpanM > 0 && f.WaterRudderMaxRad > 0)
        {
            // The drawn blade. It hangs aft of the step, in the forebody's wake: once the step ventilates the free surface
            // over it is the wake's (Savitsky & Morabito, as the afterbody stations above), not the undisturbed water.
            double up01 = f.WaterRudderRetractable ? System.Math.Clamp(waterRudderUp01, 0.0, 1.0) : 0.0;
            double yP = config.Floats!.Count == 1 ? 0.0 : System.Math.Sign(yF) * f.WaterRudderPivotY;
            double wakeDrop = 0;
            if (vent > 0 && dStep > 0)
            {
                double xr = f.WaterRudderPivotX - f.WaterRudderBladeAftM;
                double xa = h.StepX - xr;
                if (xa > 0)
                {
                    Vec3 pk = Pt(xr, 0, h.KeelZStep - h.ForeKeelSlope * xa);
                    double wakeUp = -pk.Z + b * WakeHeightBeams(xa / b, lk / b, tauDeg, cvB, betaDeg);
                    wakeDrop = vent * System.Math.Max(0.0, WaterAt(xr) - wakeUp);
                }
            }
            (Vec3 wf, Vec3 wm, WaterRudderReport rep) = DrawnWaterRudder(f, s, cg, new Vec3(f.WaterRudderPivotX, yP, f.WaterRudderPivotZ),
                rudderCmd * f.WaterRudderMaxRad, up01 * f.WaterRudderRetractRad, wakeDrop);
            rep.Retract01 = up01;
            totalF += wf; totalM += wm;
            fs.WaterRudderN = rep.SideForceBodyN;
            LastWaterRudder = rep;
        }
        else if (hasRudder && f.WaterRudderAreaM2 > 0 && f.WaterRudderMaxRad > 0)
        {
            double span = f.WaterRudderSpanM > 0 ? f.WaterRudderSpanM : System.Math.Sqrt(2 * f.WaterRudderAreaM2);
            double xr = f.WaterRudderSpanM > 0 ? f.WaterRudderX : h.SternX;
            double tipZ = f.WaterRudderSpanM > 0 ? f.WaterRudderTipZ : h.KeelOf(n - 1) + span;
            Vec3 pTip = Pt(xr, 0, tipZ);
            double? wr = WaterSurfaceAt(pTip.X, pTip.Y, -pTip.Z);
            double immersed = wr is null ? 0 : System.Math.Clamp((wr.Value + pTip.Z) / System.Math.Max(System.Math.Abs(ez.Z), 0.2), 0, span);
            if (immersed > 1e-3)
            {
                double area = f.WaterRudderAreaM2 * immersed / span;
                double ar = 2 * immersed * immersed / area;   // the float bottom over the root is an end plate: 2 × geometric
                double delta = rudderCmd * f.WaterRudderMaxRad;
                Vec3 pc = new Vec3(xr, yF, tipZ - 0.5 * immersed);
                Vec3 vb = s.Velocity + Vec3.Cross(s.Rates, pc - cg);
                double wx = -vb.X, wy = -vb.Y, w2 = wx * wx + wy * wy;
                if (w2 > 1e-6)
                {
                    double wm = System.Math.Sqrt(w2);
                    // blade normal (body, horizontal): trailing edge right (+δ) → chord (−cos δ, sin δ), normal (sin δ, cos δ)
                    double nx = System.Math.Sin(delta), ny = System.Math.Cos(delta);
                    double wn = (wx * nx + wy * ny) / wm;
                    double al = System.Math.Asin(System.Math.Clamp(System.Math.Abs(wn), 0, 1));
                    double cn = HelmboldSlope(ar) * System.Math.Sin(al) * System.Math.Cos(al) + PlateCrossflowCd * System.Math.Sin(al) * System.Math.Sin(al);
                    double fn = 0.5 * rho * w2 * area * cn * System.Math.Sign(wn);
                    double ff = 0.5 * rho * w2 * 2 * area * FrictionCoefficient(wm * area / immersed / WaterKinematicViscosity);
                    Vec3 fBody = new Vec3(fn * nx + ff * wx / wm, fn * ny + ff * wy / wm, 0);
                    Apply(pc, q.Rotate(fBody));
                    fs.WaterRudderN = fn;
                }
            }
        }

        fs.DragN = drag;
        return (fs, totalF, totalM);
    }

    // ---- added mass ---------------------------------------------------------------------------------------------------

    /// <summary>The water the floats carry with them when they accelerate, body axes about the CG.</summary>
    public readonly struct AddedMass
    {
        public readonly Vec3 Linear;    // kg along body x, y, z
        public readonly Vec3 Angular;   // kg m² about body x, y, z
        public AddedMass(Vec3 linear, Vec3 angular) { Linear = linear; Angular = angular; }
        public static readonly AddedMass None = new(Vec3.Zero, Vec3.Zero);
    }

    /// <summary>
    /// Strip-theory added mass of the immersed floats (the 10th term; owner 2026-11-02, the porpoising deep dive). Per
    /// unit length, from the same station cuts: heave m′₃₃ = ρ π c²/2 with c the half-width at the waterline (a flat
    /// section on the free surface, the high-frequency limit that applies to slamming and planing: Wagner 1932, Faltinsen
    /// 2005 §8), sway m′₂₂ = ρ π T²/2 (the side-force added mass of term 7). Summed over the length and moved to the CG:
    /// roll Σ m′₃₃ y² + m′₂₂ z², pitch Σ m′₃₃ x², yaw Σ m′₂₂ x². Surge is negligible for a slender hull and left out.
    /// </summary>
    public static AddedMass AddedMassOf(AircraftConfig config, RigidBodyState s)
    {
        FloatsConfig main = config.Floats!;
        _set ??= new (FloatsConfig, double, int, bool)[4];
        int nSet = FloatSet(main, _set);
        double rho = WaterDensity;
        Vec3 cg = config.Mass.CgVec();
        Quat q = s.Attitude;
        Vec3 ey = q.Rotate(new Vec3(0, 1, 0)), ez = q.Rotate(new Vec3(0, 0, 1));
        double my = 0, mz = 0, ix = 0, iy = 0, iz = 0;
        for (int k = 0; k < nSet; k++)
        {
            (FloatsConfig f, double yF, _, _) = _set[k];
            FloatHull h = FloatHull.For(f);
            Scratch(h);
            Vec3 pStep = s.Position + q.Rotate(new Vec3(h.StepX, yF, h.KeelZStep) - cg);
            double? w = WaterSurfaceAt(pStep.X, pStep.Y, -pStep.Z);
            if (w is null) continue;
            var c = new FloatHull.Cut();
            for (int i = 0; i < h.X.Length; i++)
            {
                Vec3 rw = q.Rotate(new Vec3(h.X[i], yF, 0) - cg);
                h.CutStation(i, w.Value + s.Position.Z + rw.Z, ey.Z, ez.Z, ref c, _sy!, _sz!);
                if (c.Area <= 0) continue;
                double half = c.PortCross && c.StbdCross ? 0.5 * (c.StbdY - c.PortY) : 0.5 * h.Beam;
                double T = System.Math.Max(0, c.KeelDepth);
                double m33 = rho * System.Math.PI * half * half / 2 * h.W[i], m22 = rho * System.Math.PI * T * T / 2 * h.W[i];
                double dx = h.X[i] - cg.X, dy = yF + c.Cy - cg.Y, dz = c.Cz - cg.Z;
                mz += m33; my += m22;
                ix += m33 * dy * dy + m22 * dz * dz; iy += m33 * dx * dx; iz += m22 * dx * dx;
            }
        }
        return new AddedMass(new Vec3(0, my, mz), new Vec3(ix, iy, iz));
    }

    /// <summary>
    /// The added mass applied after an RK4 step, implicitly (it multiplies the acceleration, so it cannot be a force in
    /// the step): every body-axis velocity and rate carries the momentum of the aeroplane plus its entrained water,
    /// (m + m_a) v. Water newly entrained is set moving at the hull's speed out of the hull's own momentum — the
    /// von Kármán / Wagner slam — and water shed (the hull lifting out) keeps its momentum in the wake rather than
    /// handing it back. The same rule <c>HydroBody.Correct</c> uses for the airframe, extended to the rotations.
    /// </summary>
    public static RigidBodyState CorrectForAddedMass(RigidBodyState before, RigidBodyState after, MassProperties mp, AddedMass old, AddedMass now, double dt)
    {
        if (now.Linear.LengthSquared + now.Angular.LengthSquared + old.Linear.LengthSquared + old.Angular.LengthSquared < 1e-12) return after;
        static double Axis(double vOld, double vRk, double m, double maOld, double maNew)
        {
            double mOld = m + maOld, mNew = m + maNew;
            return ((mNew >= mOld ? mOld : mNew) * vOld + m * (vRk - vOld)) / mNew;
        }
        Vec3 vo = before.Velocity, vr = after.Velocity, wo = before.Rates, wr = after.Rates;
        var v = new Vec3(Axis(vo.X, vr.X, mp.MassKg, old.Linear.X, now.Linear.X), Axis(vo.Y, vr.Y, mp.MassKg, old.Linear.Y, now.Linear.Y),
                         Axis(vo.Z, vr.Z, mp.MassKg, old.Linear.Z, now.Linear.Z));
        var w = new Vec3(Axis(wo.X, wr.X, mp.Ixx, old.Angular.X, now.Angular.X), Axis(wo.Y, wr.Y, mp.Iyy, old.Angular.Y, now.Angular.Y),
                         Axis(wo.Z, wr.Z, mp.Izz, old.Angular.Z, now.Angular.Z));
        Vec3 pos = after.Position + after.Attitude.Rotate(v - vr) * dt;   // re-advanced with the corrected velocity
        return new RigidBodyState(pos, after.Attitude, v, w);
    }

    // ---- spray roots: the same cut, for the spray (one source of truth) -----------------------------------------------

    /// <summary>One emission point on the spray root.</summary>
    public struct SprayRoot
    {
        public Vec3 Position;      // NED world, on the hull at the waterline
        public Vec3 JetVelocity;   // NED world: the water leaving the root
        public double MassFlux;    // kg/s carried by this root segment
        public int Slot;
    }

    /// <summary>
    /// Where the water leaves the hull, and how fast. At every station whose section the waterline crosses on a bottom
    /// or side facet (from the same <see cref="FloatHull.CutStation"/> as the forces), the spray root is the waterline
    /// crossing moved out by Wagner's pile-up (π/2 of the calm-water half-width on the V bottom, Savitsky 1964 eq. 3,
    /// capped at the chine). The jet: the relative water velocity there, projected into the facet's plane, REFLECTED
    /// about the waterline (spray-root) line through the next station's crossing — the Wagner / Savitsky spray-root
    /// jet: it leaves at the incoming speed (Bernoulli, atmospheric pressure in the jet) at twice the root angle, so in
    /// the earth frame its speed is 2V sin α (α = the root line's angle to the flow). The point's velocity includes the
    /// aircraft's yaw, roll and pitch rates. Mass flux per metre of root: ρ |u_p| δ with Wagner's 2-D jet thickness
    /// δ = (π/4) sin²τ_f · l (Green 1936: the jet's momentum carries the planing plate's ideal drag; τ_f = the flow's
    /// incidence on the facet, l = the section's wetted half-width at the root, its 2-D chord in the 2D+t view).
    /// </summary>
    public static int SprayRoots(AircraftConfig config, RigidBodyState s, SprayRoot[] output)
    {
        FloatsConfig main = config.Floats!;
        _set ??= new (FloatsConfig, double, int, bool)[4];
        int nSet = FloatSet(main, _set), count = 0;
        double rho = WaterDensity;
        Vec3 cg = config.Mass.CgVec();
        Quat q = s.Attitude;
        Vec3 ey = q.Rotate(new Vec3(0, 1, 0)), ez = q.Rotate(new Vec3(0, 0, 1));
        for (int k = 0; k < nSet && count < output.Length; k++)
        {
            (FloatsConfig f, double yF, int slot, _) = _set[k];
            FloatHull h = FloatHull.For(f);
            Scratch(h);
            FloatHull.Cut[] cuts = _cuts!;
            int n = h.X.Length;
            Vec3 pStep = s.Position + q.Rotate(new Vec3(h.StepX, yF, h.KeelZStep) - cg);
            double? w = WaterSurfaceAt(pStep.X, pStep.Y, -pStep.Z);
            if (w is null) continue;
            for (int i = 0; i < n; i++)
            {
                Vec3 rw = q.Rotate(new Vec3(h.X[i], yF, 0) - cg);
                h.CutStation(i, w.Value + s.Position.Z + rw.Z, ey.Z, ez.Z, ref cuts[i], _sy!, _sz!);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i + 1 < n && count < output.Length; i++)
                {
                    ref FloatHull.Cut c = ref cuts[i], c2 = ref cuts[i + 1];
                    bool cr = side < 0 ? c.PortCross : c.StbdCross, cr2 = side < 0 ? c2.PortCross : c2.StbdCross;
                    if (!cr || !cr2 || h.Aft[i] != h.Aft[i + 1]) continue;
                    if (h.Aft[i] && i > 0) { }   // afterbody roots are real too (it is in the water at displacement speeds)
                    (double y1, double z1, int e1) = side < 0 ? (c.PortY, c.PortZ, c.PortEdge) : (c.StbdY, c.StbdZ, c.StbdEdge);
                    (double y2, double z2) = side < 0 ? (c2.PortY, c2.PortZ) : (c2.StbdY, c2.StbdZ);
                    // the facet the waterline crosses: edge e1 of station i's polygon
                    var poly = h.Poly[i];
                    var pa = poly[e1]; var pb = poly[(e1 + 1) % poly.Length];
                    // Wagner pile-up on the bottom (the facet below the chine): out to π/2 of the crossing's half-width
                    double chineY = 0; foreach (var p in poly) chineY = System.Math.Max(chineY, System.Math.Abs(p.Y));
                    bool bottom = System.Math.Abs(pa.Y) < chineY - 1e-4 || System.Math.Abs(pb.Y) < chineY - 1e-4;
                    double pile = bottom ? System.Math.Min(System.Math.PI / 2 * System.Math.Abs(y1), chineY) / System.Math.Max(System.Math.Abs(y1), 1e-6) : 1.0;
                    double ry = y1 * pile, rz = pa.Z + (pb.Z - pa.Z) * (System.Math.Abs(pb.Y - pa.Y) > 1e-9 ? (ry - pa.Y) / (pb.Y - pa.Y) : 0.5);
                    if (double.IsNaN(rz) || System.Math.Abs(ry) > chineY + 1e-6) { ry = y1; rz = z1; }
                    Vec3 pBody = new Vec3(h.X[i], yF + ry, rz);
                    // facet: along x (to the next station's crossing) and along the section edge
                    Vec3 tSec = new Vec3(0, pb.Y - pa.Y, pb.Z - pa.Z);
                    Vec3 tRoot = new Vec3(h.X[i + 1] - h.X[i], y2 * pile - ry, z2 - rz);
                    // The facet's own longitudinal direction: the SAME polygon edge at the next station (the rings share
                    // their vertex order), so a root line that crosses from the bottom to the chine between two stations
                    // does not make a plane across two facets.
                    var poly2 = h.Poly[i + 1];
                    Vec3 tLong = poly2.Length == poly.Length
                        ? new Vec3(h.X[i + 1] - h.X[i], 0.5 * (poly2[e1].Y + poly2[(e1 + 1) % poly2.Length].Y - pa.Y - pb.Y), 0.5 * (poly2[e1].Z + poly2[(e1 + 1) % poly2.Length].Z - pa.Z - pb.Z))
                        : tRoot;
                    Vec3 nrm = Vec3.Cross(tLong, tSec);
                    if (nrm.LengthSquared < 1e-12 || tRoot.LengthSquared < 1e-12) continue;
                    nrm = nrm.Normalized();
                    if (nrm.Y * side < 0 && System.Math.Abs(nrm.Y) > 1e-6) nrm = nrm * -1.0;    // outward
                    else if (System.Math.Abs(nrm.Y) <= 1e-6 && nrm.Z < 0) nrm = nrm * -1.0;
                    Vec3 vPt = s.Velocity + Vec3.Cross(s.Rates, pBody - cg);   // body
                    Vec3 u = vPt * -1.0;                                        // water relative to the hull
                    double un = u.Dot(nrm);
                    if (un >= 0) continue;                                     // the water is not running into this facet
                    Vec3 up = u - nrm * un;
                    double upm = up.Length;
                    if (upm < 0.3) continue;
                    Vec3 tIn = tRoot - nrm * tRoot.Dot(nrm);                    // the root line, in the facet's plane
                    if (tIn.LengthSquared < 1e-12) continue;
                    Vec3 t = tIn.Normalized();
                    Vec3 jetRel = t * (2 * up.Dot(t)) - up;                    // reflection about the root line
                    Vec3 jetWorld = q.Rotate(vPt + jetRel);
                    if (jetWorld.Z >= 0) continue;   // water that does not leave the surface upward is wave, not spray
                    double sinTf = -un / u.Length;
                    // 2-D chord of the cross-flow section that throws this jet: its wetted half-width at the root
                    // (Wagner's wedge in the 2D+t view of the hull; the root cannot throw more than its own section).
                    double delta = System.Math.PI / 4 * sinTf * sinTf * System.Math.Abs(ry);
                    double ds = tRoot.Length;
                    output[count++] = new SprayRoot
                    {
                        Position = s.Position + q.Rotate(pBody - cg), JetVelocity = jetWorld,
                        MassFlux = rho * upm * delta * ds, Slot = slot,
                    };
                }
            }
        }
        return count;
    }

    /// <summary>What the drawn water rudder did on the last Compute (instrumentation and tests).</summary>
    public struct WaterRudderReport
    {
        public double Retract01, DeltaRad, ImmersedAreaM2, ImmersedSpanM, AspectRatio, LiftSlopePerRad, AlphaRad,
                      SpeedMs, ReynoldsNumber, LiftN, DragN, SideForceBodyN, YawMomentNm;
    }
    public static WaterRudderReport LastWaterRudder { get; private set; }

    /// <summary>Stall of the blade's lift: low-aspect-ratio plates stall late (Hoerner, Fluid-Dynamic Lift, ch. 3:
    /// AR ≈ 0.7 holds its lift to the mid-30s of degrees). Past it the lift is held, not dropped.</summary>
    public const double WaterRudderStallRad = 35.0 * System.Math.PI / 180.0;
    private const int BladeCellsChord = 16, BladeCellsSpan = 12;
    private static readonly (double u, double v)[] BladeCells = BuildBladeCells();
    private static (double u, double v)[] BuildBladeCells()
    {
        var l = new System.Collections.Generic.List<(double, double)>();
        for (int i = 0; i < BladeCellsChord; i++)
            for (int j = 0; j < BladeCellsSpan; j++)
            {
                double u = (i + 0.5) / BladeCellsChord - 0.5, v = (j + 0.5) / BladeCellsSpan - 0.5;
                if (u * u + v * v <= 0.25) l.Add((u, v));
            }
        return l.ToArray();
    }

    /// <summary>
    /// The water rudder as the hydrofoil it is (Ty 2026-11-02, t278u: "from first principles, no fudge").
    ///
    /// The blade is an elliptic flat plate, chord c fore-aft and span b vertical, area S = π/4·c·b. It turns with the
    /// pedals about a vertical post through the pivot (δ, trailing edge right = +) and the retract cable swings it up and
    /// aft about the same pivot, in its own plane (θ, 0 = down). Nothing about it is assumed wet:
    ///  - IMMERSED AREA: the blade is cut into cells; a cell counts only if its centre is below the water surface. A blade
    ///    swung up out of the water, or a stern lifted clear on the step, has S_imm = 0 and makes no force at all.
    ///  - LIFT-CURVE SLOPE from the immersed aspect ratio AR = b_imm²/S_imm (b_imm = the vertical extent of the wetted
    ///    part of the blade) by Helmbold's low-aspect-ratio lifting-surface equation a = 2π·AR / (2 + √(AR² + 4)), which goes to
    ///    slender-wing theory (π·AR/2) as AR → 0 and to 2π as AR → ∞. No image in the free surface or the hull is
    ///    taken (the blade hangs aft of the transom, the free surface cutting it with nothing to end-plate it): the
    ///    conservative choice.
    ///  - ANGLE OF ATTACK from the water's velocity relative to the blade at its immersed centroid, V + ω×r: the
    ///    rudder angle plus the stern's own sideslip, so a swinging stern is damped by the same blade that turns it.
    ///  - LIFT = ½·ρ_water·V²·S_imm·a·α (α held at the stall), perpendicular to the flow.
    ///  - DRAG = ½·ρ_water·V²·S_imm·(2·C_f + C_L²/(π·AR)): both wetted faces at the ITTC-57 friction line
    ///    C_f = 0.075/(log10 Re − 2)², Re = V·c/ν, plus the induced drag of an elliptic loading (e = 1), along the flow.
    /// The force acts at the immersed centroid; its moment is taken about the CG. Returns world-frame force and moment.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment, WaterRudderReport Report) DrawnWaterRudder(FloatsConfig f, RigidBodyState s,
        Vec3 cg, Vec3 pivotBody, double deltaRad, double retractRad, double wakeDropM = 0.0)
    {
        var rep = new WaterRudderReport { DeltaRad = deltaRad };
        double c = f.WaterRudderChordM, b = f.WaterRudderSpanM;
        double cd = System.Math.Cos(deltaRad), sd = System.Math.Sin(deltaRad);
        double ct = System.Math.Cos(retractRad), st = System.Math.Sin(retractRad);
        Vec3 eAft = new Vec3(-cd, sd, 0), eDown = new Vec3(0, 0, 1);     // body: blade chord (aft) and the post (down)
        Vec3 n = new Vec3(sd, cd, 0);                                      // the blade's normal (its right face)

        Vec3 pivotW = s.Position + s.Attitude.Rotate(pivotBody - cg);
        double? water = WaterSurfaceAt(pivotW.X, pivotW.Y, -pivotW.Z);
        if (water is null) return (Vec3.Zero, Vec3.Zero, rep);
        Vec3 aftW = s.Attitude.Rotate(eAft), downW = s.Attitude.Rotate(eDown);

        double cellArea = System.Math.PI * 0.25 * c * b / BladeCells.Length;   // cells carry the exact ellipse area
        double area = 0, sumA = 0, sumD = 0, maxDepth = 0, minDepth = double.MaxValue;
        foreach ((double u, double v) in BladeCells)
        {
            double a0 = f.WaterRudderBladeAftM + u * c, d0 = f.WaterRudderBladeBelowM + v * b;
            double a1 = a0 * ct + d0 * st, d1 = d0 * ct - a0 * st;            // swung up and aft by the retract angle
            double depth = pivotW.Z + a1 * aftW.Z + d1 * downW.Z + water.Value - wakeDropM;   // NED: +down; water is height up
            if (depth <= 0) continue;
            area += cellArea; sumA += a1 * cellArea; sumD += d1 * cellArea;
            maxDepth = System.Math.Max(maxDepth, depth); minDepth = System.Math.Min(minDepth, depth);
        }
        if (area <= 0) return (Vec3.Zero, Vec3.Zero, rep);

        // The immersed span is the wetted part's own vertical extent (cell centres ± half a cell), whether its top is
        // the water surface (a blade dipping in) or the blade's own edge (a blade wholly under).
        double cellH = b / BladeCellsSpan * System.Math.Abs(ct) + c / BladeCellsChord * System.Math.Abs(st);
        double span = System.Math.Min(maxDepth - minDepth + cellH, System.Math.Sqrt(b * b * ct * ct + c * c * st * st));   // ≤ the blade's own height
        double ar = span * span / area;
        double slope = 2 * System.Math.PI * ar / (2 + System.Math.Sqrt(ar * ar + 4));
        Vec3 rC = pivotBody + eAft * (sumA / area) + eDown * (sumD / area) - cg;
        Vec3 vB = s.Velocity + Vec3.Cross(s.Rates, rC);
        Vec3 w = new Vec3(-vB.X, -vB.Y, 0);                                 // the water past the blade, in its plane of lift
        double speed = w.Length;
        rep.ImmersedAreaM2 = area; rep.ImmersedSpanM = span; rep.AspectRatio = ar; rep.LiftSlopePerRad = slope; rep.SpeedMs = speed;
        if (speed < 1e-3) return (Vec3.Zero, Vec3.Zero, rep);

        Vec3 wh = w * (1.0 / speed);
        double wn = Vec3.Dot(wh, n), wt = Vec3.Dot(wh, eAft);
        double alpha = System.Math.Atan2(wn, System.Math.Abs(wt));
        double cl = slope * System.Math.Clamp(alpha, -WaterRudderStallRad, WaterRudderStallRad);
        double re = System.Math.Max(speed * c / WaterKinematicViscosity, 1e4);
        double cf = 0.075 / System.Math.Pow(System.Math.Log10(re) - 2, 2);
        double cdrag = 2 * cf + cl * cl / (System.Math.PI * ar);
        double q = 0.5 * WaterDensity * speed * speed;
        Vec3 perp = new Vec3(-wh.Y, wh.X, 0);                               // perpendicular to the flow, horizontal
        if (Vec3.Dot(perp, n) < 0) perp = perp * -1.0;                      // signed so +C_L pushes along +n
        double lift = q * area * cl, drag = q * area * cdrag;
        Vec3 fB = perp * lift + wh * drag;
        Vec3 fW = s.Attitude.Rotate(fB);
        Vec3 mW = Vec3.Cross(s.Attitude.Rotate(rC), fW);
        rep.AlphaRad = alpha; rep.ReynoldsNumber = re; rep.LiftN = lift; rep.DragN = drag; rep.SideForceBodyN = fB.Y;
        rep.YawMomentNm = Vec3.Cross(rC, fB).Z;
        return (fW, mW, rep);
    }
}
