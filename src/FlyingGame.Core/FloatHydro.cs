using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// Seaplane float hydrodynamics (owner request: EDO 2000-class straight floats, researched physics).
/// Each float is a prismatic V-bottom hull with a step. Three regimes blend by beam-speed coefficient
/// C_V = V/√(g·b):
///  - DISPLACEMENT (C_V ≲ 0.6): hydrostatic buoyancy per keel station (V-section area × ρ_w g), with
///    quadratic vertical damping and crossflow drag; the two hulls' spread gives roll stiffness and the
///    station distribution gives pitch stiffness — the float sits, rocks and weather-vanes on its own.
///  - PLANING (C_V ≳ 1.6): Savitsky's prismatic planing-surface relations for the FOREBODY (bow→step):
///      C_L0 = τ^1.1 (0.012 √λ + 0.0055 λ^2.5 / C_V²),  C_Lβ = C_L0 − 0.0065 β C_L0^0.6,
///      L = ½ ρ_w V² b² C_Lβ at l_p = λ b (0.75 − 1/(5.21 C_V²/λ² + 2.39)) forward of the step,
///      friction D_f = ½ ρ_w V² (λ b² / cos β) C_f, C_f (ITTC-57) = 0.075/(log10 Re − 2)², plus L·tan τ,
///    with τ = keel trim to the water flow (deg), λ = wetted forebody keel length / beam, β = deadrise.
///    The AFTERBODY ventilates behind the step (its buoyancy fades out) — the "hump" and unstick.
///  - between: linear blend. A water rudder at the stern steers below planing speed.
/// Forces are world-frame (buoyancy/lift up, drag along the water velocity), moments about the CG.
/// Water surface comes from <see cref="WorldTerrain.WaterSurfaceAt"/> (null = no water) unless
/// <see cref="FlatWaterOverride"/> is set (tests).
/// </summary>
public static class FloatHydro
{
    public const double WaterDensity = 1000.0;
    public const double WaterKinematicViscosity = 1.0e-6;

    /// <summary>Test hook: water everywhere at this height (m, up). Null = use the world terrain's water.</summary>
    public static double? FlatWaterOverride { get; set; }

    public static double? WaterSurfaceAt(double x, double y)
    {
        if (FlatWaterOverride.HasValue) return FlatWaterOverride;
        return WorldTerrain.Active?.WaterSurfaceAt(x, y);
    }

    public readonly struct Report
    {
        public readonly double DraftAtStepM, WettedLambda, TrimDeg, SpeedCoefficient, PlaningLiftN, BuoyancyN;
        public Report(double draft, double lambda, double trim, double cv, double lift, double buoy)
        { DraftAtStepM = draft; WettedLambda = lambda; TrimDeg = trim; SpeedCoefficient = cv; PlaningLiftN = lift; BuoyancyN = buoy; }
    }

    public static Report LastReport { get; private set; }

    /// <summary>Per-float state for effects (splash) and instrumentation: filled every Compute.</summary>
    public struct FloatState
    {
        public bool Wet;                 // any station immersed
        public double StepDraftM, BowDraftM, SternDraftM;   // keel immersion at step / bow-most station / stern (after ventilation)
        public double DragN;             // this float's hydrodynamic forward drag (form + friction + planing)
        public double PlaningLiftN, BuoyancyN, SpeedMs;
        public Vec3 StepWorld, BowWorld, SternWorld;        // NED world positions of the keel points
        public double LateralForceN;     // crossflow side force (water-loop driver)
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

    /// <summary>Total world-frame force and moment (about the CG) from all floats. Zero when clear of water.</summary>
    public static (Vec3 Force, Vec3 Moment) Compute(AircraftConfig config, RigidBodyState s, double rudderCmd)
    {
        FloatsConfig main = config.Floats!;
        Vec3 cg = config.Mass.CgVec();
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        double g = Atmosphere.GravityMs2, rho = WaterDensity;
        const int nFore = 8, nAft = 6;

        Vec3 vWorldCg = s.Attitude.Rotate(s.Velocity);
        double draftReport = 0, lamReport = 0, trimReport = 0, cvReport = 0, liftReport = 0, buoyReport = 0;

        // Twin floats (Count 2), or a FLYING-BOAT HULL on the centreline (Count 1) — plus optional wingtip floats that
        // only touch the water when the boat heels (owner 2026-10-02: Hughes H-4). Same hydrodynamics for every one.
        var set = new System.Collections.Generic.List<(FloatsConfig f, double yF, int slot, bool rudder)>(4);
        if (main.Count == 1) set.Add((main, 0.0, 0, true));
        else { set.Add((main, -main.SpreadM * 0.5, 0, true)); set.Add((main, main.SpreadM * 0.5, 1, true)); }
        if (main.TipFloats is { } tip) { set.Add((tip, -tip.SpreadM * 0.5, 2, false)); set.Add((tip, tip.SpreadM * 0.5, 3, false)); }

        foreach ((FloatsConfig f, double yF, int slot, bool hasRudder) in set)
        {
            double beta = f.DeadriseDeg, tanB = System.Math.Tan(beta * System.Math.PI / 180);
            double b = f.BeamM;
            double chineH = 0.5 * b * tanB;                 // keel-to-chine height
            double xStep = f.BowX - f.StepFraction * f.LengthM;
            double xStern = f.BowX - f.LengthM;
            var fs = new FloatState();
            double dragThisFloat = 0, lateralThisFloat = 0;

            // Water speed of the float (use the step point) and speed coefficient.
            Vec3 rStepBody = new Vec3(xStep, yF, f.KeelZ) - cg;
            Vec3 vStepWorld = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rStepBody));
            double vHoriz = System.Math.Sqrt(vStepWorld.X * vStepWorld.X + vStepWorld.Y * vStepWorld.Y);
            double cv = vHoriz / System.Math.Sqrt(g * b);
            // Hydrostatics act at ALL speeds (Savitsky's λ^2.5/C_V² term is the buoyant part; we use the real
            // immersed hull instead, so only his DYNAMIC term is added). The afterbody ventilates as the
            // float climbs onto the step (C_V ≈ 3 → 5, i.e. ~8 → 13 m/s for a 0.66 m beam).
            double wPlane = 0.0;
            double wVent = System.Math.Clamp((cv - 3.0) / 2.0, 0.0, 1.0);

            // ---- station sweep: buoyancy, damping, crossflow drag, wetted forebody length
            double wettedFore = 0, foreLen = f.BowX - xStep, aftLen = xStep - xStern;
            double buoyThisFloat = 0, stepDraft = 0, maxSection = 0;
            Vec3 maxSectionR = Vec3.Zero, maxSectionV = Vec3.Zero;
            for (int pass = 0; pass < 2; pass++)
            {
                int n = pass == 0 ? nFore : nAft;
                double x0 = pass == 0 ? f.BowX : xStep, len = pass == 0 ? foreLen : aftLen;
                double keelAngle = (pass == 0 ? f.ForebodyKeelDeg : f.AfterbodyKeelDeg) * System.Math.PI / 180;
                double dx = len / n;
                for (int i = 0; i < n; i++)
                {
                    double xs = x0 - (i + 0.5) * dx;
                    // Keel z (body, +down): the forebody rises toward the bow, the afterbody toward the stern.
                    double zs = pass == 0 ? f.KeelZ - (xs - xStep) * System.Math.Tan(keelAngle)
                                          : f.KeelZ - (xStep - xs) * System.Math.Tan(keelAngle);
                    Vec3 rBody = new Vec3(xs, yF, zs) - cg;
                    Vec3 rWorld = s.Attitude.Rotate(rBody);
                    Vec3 pWorld = s.Position + rWorld;
                    double? water = WaterSurfaceAt(pWorld.X, pWorld.Y);
                    if (water is null) continue;
                    double draft = pWorld.Z - (-water.Value);        // >0 keel below the surface (z down)
                    if (pass == 1)
                    {
                        // Step ventilation is GEOMETRIC: behind the step the water surface drops away in a wake
                        // that deepens with speed and with distance aft; the afterbody only wets when its keel
                        // dips below that wake. Nose-down on the step → afterbody rewets → tail lifts (restoring).
                        double distAft = xStep - xs;
                        double ventDrop = System.Math.Clamp(0.25 * (cv - 2.5), 0.0, 0.7) * System.Math.Min(1.0, distAft / 1.0);
                        draft -= ventDrop;
                    }
                    if (draft <= 0) continue;
                    if (pass == 0) wettedFore += dx;
                    if (pass == 0 && i == n - 1) { stepDraft = draft; draftReport = System.Math.Max(draftReport, draft); }
                    if (pass == 0 && i == 0) fs.BowDraftM = draft;
                    if (pass == 1 && i == n - 1) fs.SternDraftM = draft;
                    fs.Wet = true;

                    // V-section immersed area, capped at the deck. The float TAPERS (owner 2026-10-02: displacement must be
                    // right — a full-beam, full-depth box from bow to stern displaced ~1.8x a real EDO float): beam and
                    // depth stay full through the middle and narrow toward the bow and the stern.
                    (double bF, double dF) = Taper(f, pass == 0, pass == 0 ? (xs - xStep) / System.Math.Max(foreLen, 1e-6) : (xStep - xs) / System.Math.Max(aftLen, 1e-6));
                    double bL = b * bF, chineL = 0.5 * bL * tanB;
                    double d = System.Math.Min(draft, f.DepthM * dF);
                    double area = d <= chineL ? d * d / tanB : 0.5 * bL * chineL + bL * (d - chineL);
                    double fade = 1.0; // ventilation handled geometrically above
                    double buoy = rho * g * area * dx * fade;
                    buoyThisFloat += buoy;

                    // Station velocity (world) for damping/crossflow.
                    Vec3 vSt = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));
                    double vz = vSt.Z;                                   // +down
                    double plan = System.Math.Min(2 * d / tanB, b) * dx; // wetted plan area of this station
                    double dampZ = -rho * 0.6 * plan * vz * System.Math.Abs(vz) - 250.0 * plan * vz; // quadratic + linear (down = +)
                    // Crossflow (lateral) drag on the immersed side area: yaw damping / weather-vaning.
                    // Crossflow (lateral) drag on the immersed side area: yaw damping / weather-vaning, and — on the
                    // FOREBODY ahead of the CG — the destabilising side force that turns a dug-in bow into a water loop.
                    double sideArea = d * dx;
                    double cdSide = pass == 0 ? 1.2 : 1.0;
                    // KEEL EFFECT: besides the quadratic crossflow drag, an immersed keel/chine hull resists lateral
                    // motion LINEARLY at low speed (lift-like keel side force + added mass), which is what makes a
                    // floatplane track straight and turn slowly at idle instead of spinning on its water rudders.
                    const double keelLinear = 1800.0; // N per (m/s) per m² of immersed side area
                    double fy = (-0.5 * rho * cdSide * sideArea * vSt.Y * System.Math.Abs(vSt.Y) - keelLinear * sideArea * vSt.Y) * fade;
                    lateralThisFloat += fy;
                    // Forward drag is applied elsewhere: form drag ONCE per float (deepest section, below) and
                    // skin friction through Savitsky's wetted-area term. Per-station friction here double-counted it.
                    double vxy = System.Math.Sqrt(vSt.X * vSt.X + vSt.Y * vSt.Y);
                    double dispDrag = 0.0;
                    if (area * fade > maxSection)
                    {
                        maxSection = area * fade; maxSectionV = vSt;
                        // Form drag acts at the immersed section's centroid (≈ d/3 above the keel), not the keel.
                        maxSectionR = s.Attitude.Rotate(new Vec3(xs, yF, zs - d / 3.0) - cg);
                    }
                    Vec3 fWorld = new Vec3(0, fy, -buoy + dampZ);
                    if (vxy > 1e-3) fWorld += new Vec3(-vSt.X / vxy * dispDrag, -vSt.Y / vxy * dispDrag, 0);
                    totalF += fWorld;
                    totalM += Vec3.Cross(rWorld, fWorld);
                }
            }
            buoyReport += buoyThisFloat;
            // Form (wave/pressure) drag of the immersed hull: once per float, on the deepest section, fading as the
            // hull rises onto the step.
            {
                double vxy = System.Math.Sqrt(maxSectionV.X * maxSectionV.X + maxSectionV.Y * maxSectionV.Y);
                if (maxSection > 0 && vxy > 1e-3)
                {
                    double form = 0.5 * rho * 0.05 * maxSection * vxy * vxy; // fine-entry hull: residuary Cd ~0.05 on the midsection
                    Vec3 fWorld = new Vec3(-maxSectionV.X / vxy * form, -maxSectionV.Y / vxy * form, 0);
                    totalF += fWorld; totalM += Vec3.Cross(maxSectionR, fWorld);
                    dragThisFloat += form;
                }
            }

            // ---- Savitsky planing lift/drag on the forebody
            if (stepDraft > 0.005 && vHoriz > 1.0)
            {
                // Trim: forebody keel angle vs the water-plane flow (pitch + keel incidence − flight path).
                Quat q = s.Attitude;
                double pitch = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 180 / System.Math.PI;
                double gamma = System.Math.Atan2(-vStepWorld.Z, System.Math.Max(vHoriz, 0.1)) * 180 / System.Math.PI;
                double tau = System.Math.Clamp(pitch + f.ForebodyKeelDeg - gamma, 0.5, 12.0); // Savitsky data ends ~12-15°; beyond, flow separates
                double tauRad = tau * System.Math.PI / 180;
                // Wetted keel length from the step draft and trim (the keel crosses the surface d/sin τ ahead of the
                // step), chine length shorter by the spray-root offset b·tanβ/(π·tanτ); Savitsky's λ is the mean.
                double lk = System.Math.Min(stepDraft / System.Math.Sin(tauRad), foreLen);
                double lc = System.Math.Max(0.0, lk - b * tanB / (System.Math.PI * System.Math.Tan(tauRad)));
                double lambda = System.Math.Clamp(0.5 * (lk + lc) / b, 0.05, foreLen / b);
                double cvc = System.Math.Max(cv, 0.6);
                // Dynamic (planing) part of Savitsky's C_L0 only — the λ^2.5/C_V² buoyant term is carried by the
                // station hydrostatics above.
                double cl0 = System.Math.Pow(tau, 1.1) * 0.012 * System.Math.Sqrt(lambda);
                double clb = System.Math.Max(0, cl0 - 0.0065 * beta * System.Math.Pow(cl0, 0.6));
                // Chines-dry correction: until the step is immersed to the chine only a strip 2d/tanβ wide is
                // wetted, so the planing surface is narrower than the beam (tames first contact at speed).
                double bEff = System.Math.Min(b, 2.0 * stepDraft / tanB);
                double lift = 0.5 * rho * vHoriz * vHoriz * bEff * bEff * clb;
                double lp = lambda * b * (0.75 - 1.0 / (5.21 * cvc * cvc / (lambda * lambda) + 2.39));
                double re = System.Math.Max(vHoriz * lambda * b / WaterKinematicViscosity, 1e5);
                double cf = 0.075 / System.Math.Pow(System.Math.Log10(re) - 2, 2) + 0.0004;
                double wetted = lambda * b * bEff / System.Math.Cos(beta * System.Math.PI / 180);
                double drag = 0.5 * rho * vHoriz * vHoriz * wetted * cf + lift * System.Math.Tan(tau * System.Math.PI / 180);

                Vec3 rCp = new Vec3(xStep + lp, yF, f.KeelZ) - cg;
                Vec3 rCpWorld = s.Attitude.Rotate(rCp);
                Vec3 fLift = new Vec3(0, 0, -lift);
                Vec3 fDrag = new Vec3(-vStepWorld.X / vHoriz * drag, -vStepWorld.Y / vHoriz * drag, 0);
                totalF += fLift + fDrag;
                totalM += Vec3.Cross(rCpWorld, fLift + fDrag);
                lamReport = lambda; trimReport = tau; cvReport = cv; liftReport += lift;
                dragThisFloat += drag; fs.PlaningLiftN = lift;
            }

            // ---- water rudder at the stern (retracts as the float lifts)
            {
                Vec3 rR = new Vec3(xStern, yF, f.KeelZ - 0.05) - cg;
                Vec3 rRWorld = s.Attitude.Rotate(rR);
                Vec3 pR = s.Position + rRWorld;
                double? water = WaterSurfaceAt(pR.X, pR.Y);
                if (hasRudder && water is not null && pR.Z - (-water.Value) > 0)
                {
                    Vec3 vR = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rR));
                    double vxy = System.Math.Sqrt(vR.X * vR.X + vR.Y * vR.Y);
                    double delta = rudderCmd * f.WaterRudderMaxRad;
                    double fy = 0.5 * rho * vxy * vxy * f.WaterRudderAreaM2 * 2.5 * System.Math.Sin(delta) * (1 - wVent);
                    // Rudder TE right (+) → force to the left in body → yaw right: apply in the body-y sense rotated to world.
                    Vec3 fBody = new Vec3(0, -fy, 0);
                    Vec3 fWorld = s.Attitude.Rotate(fBody);
                    totalF += fWorld;
                    totalM += Vec3.Cross(rRWorld, fWorld);
                }
            }

            fs.StepDraftM = stepDraft; fs.DragN = dragThisFloat; fs.LateralForceN = lateralThisFloat;
            fs.BuoyancyN = buoyThisFloat; fs.SpeedMs = vHoriz;
            fs.StepWorld = s.Position + s.Attitude.Rotate(new Vec3(xStep, yF, f.KeelZ) - cg);
            fs.BowWorld = s.Position + s.Attitude.Rotate(new Vec3(f.BowX, yF, f.KeelZ - foreLen * System.Math.Tan(f.ForebodyKeelDeg * System.Math.PI / 180)) - cg);
            fs.SternWorld = s.Position + s.Attitude.Rotate(new Vec3(xStern, yF, f.KeelZ - aftLen * System.Math.Tan(f.AfterbodyKeelDeg * System.Math.PI / 180)) - cg);
            Floats[slot] = fs;
            if (main.Count == 1 && slot == 0) Floats[1] = fs;   // a hull: both "float" slots read the same state
        }

        LastReport = new Report(draftReport, lamReport, trimReport, cvReport, liftReport, buoyReport);
        return (totalF, totalM);
    }
}
