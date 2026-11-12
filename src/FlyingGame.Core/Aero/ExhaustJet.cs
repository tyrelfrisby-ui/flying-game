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

    /// <summary>One step: entrain from the air (moving at <paramref name="air"/> past the element, relative to the
    /// source), keep the momentum. <paramref name="omega"/>: the frame's rotation (a world-fixed excess turns in body axes).
    ///
    /// IT IS EXPLICIT EULER AND IT IS STEP-SENSITIVE NEAR THE PIPE, which matters more since 2026-11-11 put the real
    /// (smaller) stacks in: ΔM goes as b·h·|w| while M goes as b²h, so a narrow fast jet entrains several times its own
    /// mass in one 4 ms step. At <c>dt</c> 0.004 — what <c>SmokeTrail.FlowDt</c> and <see cref="AirframeFlow.Deflection"/>
    /// use — the first step takes 66 % of the excess off a 4 in stack at 71 m/s and 95 % off two 1.5 in stacks at 251.
    /// The free jet's TRAVEL survives it (11.49 m against 11.68 converged, and 13.04 against 13.68), because the travel
    /// is all in the long 1/x tail. The marker's NET PUSH does not: Deflection at cruise gives 0.54 m at dt 0.004 against
    /// 0.39 converged on the old bore (+38 %, and it has been that way as long as the model has existed) and 1.35 m
    /// against 0.62 on the Pitts' new one (+118 %). So the drawn plume is pushed about twice as far as this model
    /// actually says, and the real change from the new bore is 0.39 → 0.62 m, not 0.54 → 1.35.
    /// NOT FIXED HERE ON PURPOSE: the cure is substepping (or a smaller FlowDt), it is a numerical-accuracy change rather
    /// than anything Ty approved, and it would move the smoke on all nine piston types at once — a fleet sweep that wants
    /// its own lane and its own before/after plates. SmokeStackJetProbe prints the table.</summary>
    public static void Step(ref Element e, Vec3 air, double airDensity, double dt, Vec3 omega)
    {
        double wl = e.W.Length; if (wl < 1e-6) return;
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
    }
}
