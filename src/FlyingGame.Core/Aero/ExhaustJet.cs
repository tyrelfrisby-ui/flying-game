using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// THE EXHAUST JET (Ty 2026-10-13: "a 540 ci engine turning 2700 rpm and the exhaust pipe is a 4 inch diameter — calculate
/// the velocity and model it correctly"; it leaves the pipe 30° down from the longitudinal axis).
///
/// EXIT VELOCITY, from the engine's own mass flow. A four-stroke draws its displacement once every two revolutions:
///   ṁ_air = ρ∞ · VE · (MAP/p∞) · V_d · rpm / 120,  ṁ = ṁ_air · (1 + 1/AFR)
/// leaving the pipe hot (ρ_e = p / (R · T_e)) through its area A:  U₀ = ṁ / (ρ_e · A).
/// The Extra's 540 in³ at 2,700 rpm, VE 0.85, full power (AFR 12.5): 0.224 kg/s; at 900 K (ρ_e 0.39) through 4 in (81 cm²)
/// → U₀ ≈ 70 m/s. Manifold pressure follows the throttle (≈ 0.3 atm closed → ambient open).
/// HOW MANY PIPES, AND HOW BIG (Ty 2026-11-11): a type's smoke pipes ARE its exhaust stacks — he said so when asked — so
/// the bore in that formula is the one he measured and the area is N of them. The Extra's two 2.0 in stacks are 40.5 cm²
/// against the 4 in default's 81, and the Pitts' two 1.5 in are 22.8: HALF and A QUARTER of the area, so U₀ goes to
/// 141 and 251 m/s. The engine's mass flow is unchanged — it is the same aeroplane, measured properly — and so is every
/// coefficient below: VE, AFR, γ, r_c, T_e and α are the same numbers they were fitted to in October, and the diameter
/// is an INPUT, not one of them. At 900 K the speed of sound is 601 m/s, so even the Pitts' 251 m/s is M 0.42: a short
/// straight stack at full power, which is what it is.
/// PUMPING (2026-11-04): part throttle also leaves more burnt gas behind in each cylinder — the exhaust is pushed out
/// against the ambient while the charge is drawn in against the throttled manifold — so the fresh charge (and with it the
/// mass through the pipe) falls by more than MAP alone: residual fraction x_r = (1/r_c)·(p_exh/p_man)^(1/γ), γ 1.35, and
/// the fresh charge ∝ (1 − x_r) against its value wide open (where the VE above was measured). r_c defaults to 8.7 (the
/// AEIO-540); at idle MAP that is a further 0.81, wide open exactly 1. The pulse train this mean is spread over is
/// <see cref="ExhaustPulse"/>.
///
/// THE JET AFTER THE PIPE: a Lagrangian jet element (Lee &amp; Cheung's JETLAG method). The element carries its EXCESS
/// velocity w over the local air; it entrains ambient air two ways and keeps its momentum M·w:
///   shear entrainment   ΔM_s = ρ∞ · 2π b h · α · |w| · dt          (α = 0.057, round jet)
///   forced entrainment  ΔM_f = ρ∞ · 2 b h · |U⊥| · dt              (the crossflow across the jet's axis)
///   ΔM = max(ΔM_s, ΔM_f);  w ← M w / (M + ΔM);  h ∝ |w| (continuity);  b = √(M / (ρ π h)).
/// In still air that is the free round jet (centreline speed ∝ 1/x after the core); in the spin's crossflow the jet is
/// swamped within a metre or so and bends into the airflow — the jet-in-crossflow trajectory, not a 20 m free jet.
/// </summary>
public static class ExhaustJet
{
    public const double ShearAlpha = 0.057, GasR = 287.0;

    /// <summary>Exit velocity (m/s) of a piston engine's exhaust pipe. <paramref name="pipes"/>: how many stacks of that
    /// bore share the engine's flow (the area is N of them, so each one's speed is the whole flow through the total
    /// area). 1 unless a type authors a measured bank.</summary>
    public static double ExitVelocity(double displacementM3, double rpm, double throttle01, double airDensity, double pipeDiameterM,
                                      double exhaustTempK = 900, double volumetricEfficiency = 0.85, double airFuelRatio = 12.5,
                                      double compressionRatio = 8.7, int pipes = 1)
    {
        if (displacementM3 <= 0 || pipeDiameterM <= 0 || rpm <= 0) return 0;
        double mapRatio = 0.3 + 0.7 * System.Math.Clamp(throttle01, 0, 1);
        double rc = System.Math.Max(2, compressionRatio);
        double fresh = (1 - System.Math.Pow(1 / mapRatio, 1 / 1.35) / rc) / (1 - 1 / rc);
        double mAir = airDensity * volumetricEfficiency * fresh * mapRatio * displacementM3 * rpm / 120.0;
        double m = mAir * (1 + 1 / airFuelRatio);
        double p = airDensity * GasR * 288.15;   // (the pipe exits to the ambient pressure)
        double rhoE = p / (GasR * exhaustTempK);
        double area = System.Math.Max(1, pipes) * System.Math.PI * pipeDiameterM * pipeDiameterM / 4;
        return m / (rhoE * area);
    }

    /// <summary>A type's mean exit velocity from its smoke and engine configs (the displacement estimated from the power,
    /// ≈ 0.556 hp/in³, when the smoke config does not give it). The bore and the stack count are the type's own
    /// (<see cref="DataContracts.SmokeConfig.JetPipeDiameterM"/>): a measured bank of stacks where it has one, the 4 in
    /// default where it has not. EVERY path that needs an exit velocity goes through here, so none of them can read a
    /// different pipe from the one the smoke comes out of.</summary>
    public static double ExitVelocity(DataContracts.SmokeConfig? sm, DataContracts.PropulsionConfig pr, double rpm, double throttle01, double airDensity)
    {
        double cuIn = sm != null && sm.DisplacementCuIn > 0 ? sm.DisplacementCuIn : pr.MaxPowerW / 745.7 / 0.556;
        return ExitVelocity(cuIn * 16.387e-6, rpm, throttle01, airDensity, sm?.JetPipeDiameterM ?? 4 * 0.0254, sm?.ExhaustTempK ?? 900,
                            compressionRatio: sm?.CompressionRatio ?? 8.7, pipes: sm?.JetPipeCount ?? 1);
    }

    /// <summary>A jet element leaving the pipe: its excess velocity, mass, thickness and radius.</summary>
    public struct Element
    {
        public Vec3 W; public double M, H, B;
        public bool Spent => W.Length < 0.5;
    }

    /// <summary>The element as it leaves a pipe of diameter <paramref name="d"/> at <paramref name="u0"/> along
    /// <paramref name="dir"/> (hot gas, <paramref name="exhaustTempK"/>).</summary>
    public static Element Start(Vec3 dir, double u0, double d, double airDensity, double exhaustTempK = 900)
    {
        double rhoE = airDensity * 288.15 / exhaustTempK, b = d / 2, h = d;
        return new Element { W = dir * u0, M = rhoE * System.Math.PI * b * b * h, H = h, B = b };
    }

    /// <summary>The most of its own mass an element may entrain in ONE integration substep. The entrainment law is
    /// stiff where the jet is young — ΔM goes as b·h·|w| while M goes as b²h, so a narrow fast jet swallows SEVERAL
    /// TIMES its own mass in one 4 ms step — and explicit Euler cannot be asked to do that. 2 % keeps the substep in
    /// the regime where Euler is Euler; see <see cref="Step"/>.</summary>
    public const double MaxEntrainPerStep = 0.02;
    /// <summary>A ceiling on the substeps one <see cref="Step"/> may take, so a pathological state cannot hang a frame.
    /// 4,096 covers the worst the fleet asks for (the Pitts' first step off a 1.5 in stack at 251 m/s wants ~950).</summary>
    public const int MaxSubsteps = 4096;

    /// <summary>One step: entrain from the air (moving at <paramref name="air"/> past the element, relative to the
    /// source), keep the momentum. <paramref name="omega"/>: the frame's rotation (a world-fixed excess turns in body axes).
    ///
    /// IT SUBSTEPS, because the law is stiff where the jet is young and explicit Euler on a 4 ms step was not solving it
    /// (2026-11-11). ΔM/M for the whole step says how many substeps it takes — enough that none of them entrains more
    /// than <see cref="MaxEntrainPerStep"/> — and <paramref name="substeps"/> can pin that count instead (1 = the old
    /// single Euler step, which is how the before/after plates are measured).
    ///
    /// WHAT IT WAS DOING. At <c>dt</c> 0.004 — <c>SmokeTrail.FlowDt</c> and <see cref="AirframeFlow.Deflection"/>'s
    /// default — the first step took 66 % of the excess off a 4 in stack at 71 m/s and 95 % off two 1.5 in stacks at 251,
    /// and one such step at 251 m/s is a metre of travel. The free jet's TRAVEL survived it, because the travel is all in
    /// the long 1/x tail; the marker's NET PUSH, which is what gets drawn, did not. Deflection at cruise gave 0.54 m
    /// against 0.39 converged on the old bore (+38 %, and it had been so for as long as the model existed) and 1.35 m
    /// against 0.62 on the Pitts' new one (+118 %).
    ///
    /// THIS IS A CONVERGENCE FIX AND NOT A RETUNE. Not one coefficient moved: α, the continuity law h ∝ |w|,
    /// b = √(M/ρπh), the max() of the shear and forced branches and the momentum M·w are all exactly as they were. The
    /// answer it now gives IS the answer this model always had — it is the dt → 0 limit, reached at the dt the game
    /// actually marches. The cost falls on the jet's own arithmetic only: the expensive part of a Deflection step is the
    /// airframe-flow velocity evaluation, which is per OUTER step and is untouched.
    /// (A smaller <c>FlowDt</c> would have worked too and was the other candidate. It is the wrong lever here: FlowDt is
    /// also the cache-index granularity in <c>SmokeTrail.Pushed</c> — a puff's push is looked up as age/FlowDt into a
    /// stored path — so shrinking it multiplies both the stored path length and every outer flow evaluation, to fix a
    /// stiffness that lives entirely inside this one function.)</summary>
    public static void Step(ref Element e, Vec3 air, double airDensity, double dt, Vec3 omega, int substeps = 0)
        => Step(ref e, air, airDensity, dt, omega, substeps, out _);

    /// <summary>The same step, also handing back HOW FAR THE EXCESS CARRIED THE GAS over it — ∫w dt across the substeps,
    /// not w·dt at the speed it started with.
    ///
    /// This is the other half of the 2026-11-11 convergence fix and the half that actually mattered. A marker's position
    /// was advanced by the excess it had at the START of the step, held for the whole 4 ms: at 251 m/s that credits it a
    /// metre of travel at full speed over a step in which the real jet has already given most of its excess away. Making
    /// the jet's own decay converge on its own made that WORSE, not better (+6 %), because a correctly-decaying jet is
    /// faster at the start of each step than an over-damped one — the position integrator was the dominant error all
    /// along. <see cref="AirframeFlow.Deflection"/> now adds this displacement instead.</summary>
    public static void Step(ref Element e, Vec3 air, double airDensity, double dt, Vec3 omega, int substeps, out Vec3 carried)
    {
        carried = Vec3.Zero;
        if (e.W.Length < 1e-6 || dt <= 0) return;
        int n = substeps > 0 ? System.Math.Min(substeps, MaxSubsteps) : Substeps(e, air, airDensity, dt);
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            carried += e.W * h;
            if (!EulerStep(ref e, air, airDensity, h, omega)) return;
        }
    }

    /// <summary>How many substeps <see cref="Step"/> needs here: enough that no substep entrains more than
    /// <see cref="MaxEntrainPerStep"/> of the element's mass. ΔM/M falls as the element grows and slows, so the count
    /// taken from the state at the START of the step is the conservative one for the whole step.</summary>
    public static int Substeps(in Element e, Vec3 air, double airDensity, double dt)
    {
        double wl = e.W.Length;
        if (wl < 1e-6 || e.M <= 0 || dt <= 0) return 1;
        Vec3 axis = e.W / wl;
        Vec3 cross = air - Vec3.Dot(air, axis) * axis;
        // max(ΔM_shear, ΔM_forced) = ρ · 2 b h · dt · max(π α |w|, |U⊥|)
        double dm = airDensity * 2 * e.B * e.H * dt * System.Math.Max(System.Math.PI * ShearAlpha * wl, cross.Length);
        double want = System.Math.Ceiling(dm / (e.M * MaxEntrainPerStep));
        return want <= 1 ? 1 : want >= MaxSubsteps ? MaxSubsteps : (int)want;
    }

    /// <summary>One explicit Euler substep of the entrainment law, exactly as it has always been written. False when the
    /// element has no excess left to carry.</summary>
    private static bool EulerStep(ref Element e, Vec3 air, double airDensity, double dt, Vec3 omega)
    {
        double wl = e.W.Length; if (wl < 1e-6) return false;
        Vec3 axis = e.W / wl;
        Vec3 cross = air - Vec3.Dot(air, axis) * axis;
        double dms = airDensity * 2 * System.Math.PI * e.B * e.H * ShearAlpha * wl * dt;
        double dmf = airDensity * 2 * e.B * e.H * cross.Length * dt;
        double dm = System.Math.Max(dms, dmf);
        Vec3 w = e.W * (e.M / (e.M + dm));
        w -= Vec3.Cross(omega, w) * dt;   // (body axes turn under it)
        double hNew = e.H * System.Math.Max(0.05, w.Length / wl);
        e.M += dm; e.W = w; e.H = hNew;
        e.B = System.Math.Sqrt(e.M / (airDensity * System.Math.PI * System.Math.Max(1e-6, hNew)));
        return true;
    }
}
