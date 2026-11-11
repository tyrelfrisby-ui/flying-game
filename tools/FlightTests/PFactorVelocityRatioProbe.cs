using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (PFACTOR_VR=1). WHAT VELOCITY RATIO IS MISSING FROM THE LEGACY P-FACTOR, derived and then measured
/// against the blade-element integral this model already carries. NOTHING IS CHANGED HERE: PFactorK feeds every type
/// in the fleet and re-shaping it is a re-rig and a handling change, so this probe only states the honest form and
/// what it would be worth.
///
/// THE LEGACY TERM (PropModel.Compute, the non-solved path, which is the path the Pitts and the Extra actually fly
/// because AircraftEngine.SolvesShaft is gated on the governor having LOST its oil):
///
///     zOffset = -RotationSign * PFactorK * R * sin(beta)        PFactorK = 0.35
///
/// There is no velocity in it at all. The thrust centre's offset from the hub is asserted to be 0.35 R per unit
/// sin(beta) whether the aeroplane is doing 100 kt or 50, and whether the propeller is turning 700 rpm or 2,700.
///
/// THE DERIVATION. Azimuth psi = 0 is the blade going DOWN on the right of a right-hand prop (BladeElement.Solve's own
/// convention), so a positive crossflow v_c adds to that blade's speed. At radius x = r/R the section meets the air at
///
///     v_t = Omega R x + v_c cos psi = Omega R (x + mu cos psi),     mu = v_c / (Omega R)
///     v_a = v_axial + v_i                                           (azimuth-independent to first order)
///
/// so writing v_t = v_t0 (1 + eps) with v_t0 = Omega R x and eps = (mu / x) cos psi, the azimuthal perturbation of an
/// element's thrust dT = 0.5 rho W^2 (B c dx R) c_n has exactly two first-order pieces:
///
///   1. DYNAMIC PRESSURE.  W^2 = v_a^2 + v_t^2, so d(W^2)/W^2 = 2 (v_t0^2 / W^2) eps = 2 cos^2(phi) eps.
///   2. INCIDENCE.         phi = atan(v_a / v_t), so d(phi) = -(v_a v_t0 / W^2) eps = -sin(phi) cos(phi) eps, and the
///                         incidence alpha = theta - phi RISES on the down-going blade by the same amount. With a
///                         linear section that is d(c_n)/c_n = (c_l_alpha / c_l) sin(phi) cos(phi) eps.
///
/// Both are proportional to eps, i.e. to (mu / x) cos psi. The first moment of thrust across the disc is
/// M_T = sum dT * r * cos(psi); the mean of cos^2(psi) over the azimuth is 1/2, the unperturbed dT is
/// azimuth-independent, and the r = R x in the weight CANCELS the 1/x in eps. That cancellation is the reason the
/// answer is clean in mu and not in some awkward radial moment:
///
///     M_T = (1/2) mu R * integral( dT/dx * [ 2 cos^2(phi) + (c_l_alpha / c_l) sin(phi) cos(phi) ] dx )
///
/// and dividing by T R:
///
///     offset / R = K_blade * mu,      K_blade = (1/2) < 2 cos^2(phi) + (c_l_alpha / c_l) sin(phi) cos(phi) >_T
///
/// with < >_T the THRUST-WEIGHTED radial mean. So the honest form of the legacy expression is
///
///     offset = K_blade * R * v_cross / (Omega R) = K_blade * v_cross / Omega
///
/// — the offset is a LENGTH set by the crossflow and the shaft speed, and R drops out of it entirely. PFactorK * R
/// is that length frozen at one operating point. K_blade is not a universal constant either: phi depends on the
/// advance ratio and on the blade angle the governor has set, so it varies with the operating point, which is the
/// second thing a bare coefficient cannot carry.
///
/// WHAT THIS PROBE DOES, in three parts:
///   A. MEASURES K_blade out of BladeElement.Solve, whose Result.ThrustMomentNm is exactly the sum(dT r cos psi)
///      above, by sweeping the crossflow at a fixed shaft speed and reading offset/R against mu. If the derivation is
///      right the relation is linear through the origin and its slope is K_blade.
///   B. CHECKS THE FITTED CONSTANT: 0.35 should fall out of K_blade * V / (Omega R) near the speed PFactorK was
///      fitted at (~100 kt). If it does, the derivation is right rather than merely different.
///   C. FLIES THE PINNED TEST'S OWN ENTRY (InvertedSpinPowerTests.DevelopedInvertedSpin: push to the break at IDLE,
///      right rudder, 12 s of FULL POWER with the ailerons NEUTRAL, then full pro-spin aileron) and prints, step by
///      step, the legacy pitch moment against the derived form's at the same state, so the difference can be put
///      beside the budget's standing deficit instead of guessed at.
///
/// ThrottleLever is INVERTED: -1 is FULL POWER, +1 is idle, and ControlInputs.Neutral's 0 is HALF. Every number below
/// is at lever -1, full power, which Aircraft reads as throttle01 = 1.0.
///
/// ========================= WHAT IT MEASURED, 2026-11-11, pitts-s2b-like at the honest rig =========================
///
/// 1. THE DERIVATION HOLDS. offset/R is linear in mu through the origin out of the blade's own integral, and K_blade
///    is flat to 3 % across the whole range of mu the spin ever reaches (K 1.051 at mu 0.007, 1.033 at mu 0.107), so
///    the first-order result is not being used outside its range.
///
/// 2. AND IT REPRODUCES THE SHIPPED CONSTANT. At 4,000 ft and 92 kt at cruise power the blade's own offset is
///    0.0145 m where 0.35 R sin(beta) gives 0.0146 — agreement to 1 %, K_blade 1.896 x V/(Omega R) 0.1825 = 0.346.
///    At full power the crossing is near 104 kt. So 0.35 IS K_blade * V / (Omega R) at about 100 kt, which is the
///    check that this is the same quantity and not a different one.
///
/// 3. THE CONSTANT CANNOT BE RIGHT EVERYWHERE, because what it stands for moves by 3x over this type's own envelope:
///        K_blade * V / (Omega R)   =  0.35  at 92 kt cruise power     (the constant, by construction)
///                                  =  0.785 at the stored rig point   (70.45 m/s, 0.75 power: K 2.887)
///                                  =  0.11  in the inverted flat spin (29.8 m/s, full power: K 1.03)
///    Two causes, both pulling the same way in a spin: V/(Omega R) halves (0.199 -> 0.107), and K_blade halves
///    (2.05 -> 1.03) because the blade is working at high incidence where c_l_alpha/c_l is small, so a given change
///    of incidence across the azimuth buys proportionally much less extra lift. The ratio honest/legacy is
///    (K_blade / 0.35) * |V| / (Omega R), which is 1.0 at the fit speed by construction and 0.31 in the spin.
///
/// 4. SO THE LEGACY P-FACTOR IS ABOUT 3x TOO LARGE IN THE INVERTED SPIN, and it is nose-DOWN there. Through the
///    departure window of the pinned test's own DEVELOP phase (full power, ailerons neutral), on the SAME thrust:
///        t 1.0 s  beta 28   Mpf_legacy  -579   Mpf_derived  -219   difference  +360 N.m nose up
///        t 1.5 s  beta 42   Mpf_legacy  -830   Mpf_derived  -280   difference  +550
///        t 2.0 s  beta 54   Mpf_legacy -1000   Mpf_derived  -314   difference  +687
///        t 2.5 s  beta 82   Mpf_legacy -1223   Mpf_derived  -360   difference  +863
///    Against the budget's standing deficit at the honest rig (Msum -945 at t 1.5, -933 at t 2.0) that closes 58 %
///    and 74 %; against the 420 N.m the honest rig costs over the old one it is 1.3-1.6x, i.e. it more than pays for
///    the re-rig. It does not overshoot: -945 + 550 is still -395 nose-down, which is LESS nose-down than the old
///    rig's -489 at the same step, and the old rig holds the spin today.
///
/// 5. AND IT IS ENOUGH TO HOLD THE SPIN, which is the part that would delete the decision. The pinned test's own
///    entry and its own three assertions, with PFactorK set to the 0.11 the derived form gives at this spin (a
///    SENSITIVITY TEST, not the honest form, which is velocity-dependent - see Sensitivity()):
///        honest rig, PFactorK 0.350   inverted  0.0 s of 20   mean alpha  +76.8   lost
///        honest rig, PFactorK 0.110   inverted 20.0 s of 20   mean alpha  -84.0   HOLDS
///        old rig,    PFactorK 0.350   inverted 20.0 s of 20   mean alpha  -88.4   HOLDS
///        old rig,    PFactorK 0.110   inverted 20.0 s of 20   mean alpha  -84.2   HOLDS
///    Both rigs hold once the term is the right size, so the two honest routes stop disagreeing.
///
/// 6. WHAT THIS IS NOT A LICENCE TO DO. Re-shaping the term is NOT rig-neutral: at the stored rig point the honest
///    form is 2.24x the constant, which at the rig's 2.08 deg alpha and ~1,650 N of thrust moves the P-factor's YAW
///    moment by about 26 N.m - small beside the ~500 N.m errors the 2026-11-10 re-rig fixed, but not zero, and the
///    pitch share there is identically zero only because the rig is solved at zero sideslip. The term also feeds
///    every type in the fleet. AND THE HONEST FORM IS NOT ONE NEW CONSTANT: K_blade is itself a function of the
///    operating point, and the only honest source for it is the azimuthal integral this file reads it out of - which
///    is to say the real repair is to put this type on the blade-element path (AircraftEngine.SolvesShaft is gated on
///    the governor having LOST its oil, so with oil the Pitts never reaches BladeElementForces at all), not to fit a
///    better coefficient. That route is already written up as Ty's call in docs/RIGGING.md.
/// </summary>
public class PFactorVelocityRatioProbe
{
    private readonly ITestOutputHelper _out;
    public PFactorVelocityRatioProbe(ITestOutputHelper o) { _out = o; }

    private const double Full = -1.0, Idle = 1.0;

    private static string Repo
    {
        get
        {
            DirectoryInfo? d = new(AppContext.BaseDirectory);
            while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent;
            return d!.FullName;
        }
    }

    private static AircraftConfig Load(string id) =>
        AircraftConfigLoader.LoadFromFile(Path.Combine(Repo, "configs", "aircraft", id + ".json"));

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("PFACTOR_VR") is null) return;
        string id = Environment.GetEnvironmentVariable("PFACTOR_ID") ?? "pitts-s2b-like";
        var cfg = Load(id);
        var prop = cfg.Propulsion!;
        var sb = new System.Text.StringBuilder();
        sb.Append($"=== {cfg.Id}   R {0.5 * prop.PropDiameterM:0.000} m   PFactorK {prop.PFactorK}   "
                  + $"thrustLineZ {prop.ThrustLineZ}   governedRpm {prop.GovernedRpm}   fine/coarse {prop.FineStopDeg}/{prop.CoarseStopDeg}\n\n");

        // ---- A and B: K_blade out of the blade's own azimuthal integral, and the check against 0.35 -------------
        sb.Append(Slope(prop, "CRUISE-ish  (the condition PFactorK was fitted near): 4,000 ft, 100 kt axial", 1219.2, 51.44, 0.75));
        sb.Append(Slope(prop, $"THIS TYPE'S OWN RIG POINT: {cfg.Rig!.AltitudeM:0} m, {cfg.Rig.TasMs:0.0} m/s, "
                              + $"{cfg.Rig.ThrottleFraction:0.00} power - where a re-shaped term would or would not de-rig it",
                        cfg.Rig.AltitudeM, cfg.Rig.TasMs, cfg.Rig.ThrottleFraction));
        sb.Append(Slope(prop, "FULL POWER at 100 kt axial", 1219.2, 51.44, 1.0));
        sb.Append(Slope(prop, "WHERE THE HONEST FORM EQUALS 0.35 at cruise power: 4,000 ft, 92 kt axial", 1219.2, 47.3, 0.75));
        sb.Append(Slope(prop, "WHERE THE HONEST FORM EQUALS 0.35 at full power: 4,000 ft, 109 kt axial", 1219.2, 56.1, 1.0));
        sb.Append(Slope(prop, "SPIN-ish    (full power, low axial, 3,000 m): 10 m/s axial", 3000.0, 10.0, 1.0));
        sb.Append(Slope(prop, "SPIN-ish    (full power, 3,000 m): 20 m/s axial", 3000.0, 20.0, 1.0));

        // ---- C: the pinned test's own entry, legacy against the derived form -------------------------------------
        sb.Append(Flown(cfg));

        // ---- D: does closing that gap hold the spin? A SENSITIVITY TEST AND NOT A FIX ---------------------------
        sb.Append(Sensitivity(id));
        _out.WriteLine(sb.ToString());
    }

    /// <summary>
    /// offset/R against mu out of BladeElement.Solve. Result.ThrustMomentNm IS sum(dT r cos psi), so offset =
    /// ThrustMomentNm / ThrustN is the thrust centre's displacement toward the down-going blade with no coefficient
    /// anywhere in it. Linear through the origin is the derivation's own prediction; the slope is K_blade.
    /// </summary>
    private static string Slope(PropulsionConfig prop, string what, double altM, double vAxial, double thr)
    {
        double rho = Atmosphere.DensityAtAltitude(altM);
        double radius = 0.5 * prop.PropDiameterM;
        // The rpm and blade angle the engine and the governor settle on here (PropModel.SteadyDrive: "where the shaft
        // WOULD settle", which is what an ablation probe wants whether the sim integrates it or not).
        var drive = PropModel.SteadyDrive(prop, thr, vAxial, rho);
        double rpm = drive.ShaftRpm > 0 ? drive.ShaftRpm : PropModel.RunningRpm(prop, thr, 1.0);
        double pitch = drive.BladePitchDeg > 0 ? drive.BladePitchDeg : prop.BladePitch75Deg;
        double omega = rpm * 2.0 * Math.PI / 60.0, omegaR = omega * radius;
        var g = new BladeElement.Geometry(prop.PropBladeCount, radius, pitch, prop.ActivityFactor);

        var sb = new System.Text.StringBuilder();
        sb.Append($"--- {what}\n");
        sb.Append($"    rho {rho:0.000}  shaft {rpm:0} rpm  blade {pitch:0.00} deg at 0.75R  Omega*R {omegaR:0.0} m/s\n");
        sb.Append("      v_cross     mu   thrust    M_T    offset  offset/R   K=(offset/R)/mu | legacy 0.35*sin(b) at this v_c\n");
        foreach (double vc in new[] { 2.0, 5.0, 10.0, 20.0, 30.0, 40.0, 60.0 })
        {
            var r = BladeElement.Solve(g, vAxial, vc, rpm, rho);
            double offset = r.ThrustN > 1e-6 ? r.ThrustMomentNm / r.ThrustN : 0.0;
            double mu = vc / omegaR;
            // The legacy expression asked at the SAME crossflow: it reads the crossflow as sin(beta) of the total
            // speed, so sin(beta) = v_c / |V| with |V| = sqrt(v_axial^2 + v_c^2).
            double sinB = vc / Math.Sqrt(vAxial * vAxial + vc * vc);
            double legacy = prop.PFactorK * radius * sinB;
            sb.Append($"    {vc,8:0.0} {mu,6:0.000} {r.ThrustN,8:0} {r.ThrustMomentNm,6:0} {offset,9:0.0000} {offset / radius,9:0.0000}"
                      + $" {(mu > 1e-9 ? offset / radius / mu : 0),17:0.000} | {legacy,9:0.0000} m ({legacy / radius:0.000} R)\n");
        }
        return sb.Append('\n').ToString();
    }

    /// <summary>
    /// THE PINNED TEST'S ENTRY, exactly (InvertedSpinPowerTests.DevelopedInvertedSpin), then the hold it asserts on.
    /// For each step: the state, the thrust the legacy path made, the legacy pitch moment from P-factor, and the same
    /// moment with the derived velocity ratio in it. The spin is BISTABLE, so the entry is stated and not assumed.
    /// </summary>
    private static string Flown(AircraftConfig cfg)
    {
        var prop = cfg.Propulsion!;
        double radius = 0.5 * prop.PropDiameterM;
        var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(1, 0, 0, 0), new Vec3(60, 0, 0), Vec3.Zero),
                              new ControlDeflections(0, 0, 0, 0, 0));
        var sim = new SimLoop(ac);
        for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
            sim.RunFor(0.1, new ControlInputs(0, Math.Min(1, 0.2 + 0.02 * i), 0, Idle));

        var sb = new System.Text.StringBuilder();
        sb.Append("--- THE PINNED ENTRY, BOTH PHASES: push to the break at idle, right rudder, then 12 s of FULL\n");
        sb.Append("    POWER with the ailerons NEUTRAL (the DEVELOP phase, which is where the pitch budget's standing\n");
        sb.Append("    deficit was measured), then 20 s of FULL pro-spin aileron at FULL POWER (the HOLD phase, which\n");
        sb.Append("    is what the pinned test asserts on). Lever -1 throughout the powered part. K_vr is K_blade\n");
        sb.Append("    measured at this step's own axial speed, crossflow and shaft speed; Mpf_leg and Mpf_vr are the\n");
        sb.Append("    SAME thrust on the legacy offset and on the derived one. Mprop is the whole thrust group's\n");
        sb.Append("    pitch moment as the budget reads it.  + nose up.\n");
        sb.Append("     t alpha  beta    Vt   Vx    Vy    Vz     p     q     r |    T  OmR |  mu  sinB |"
                  + " zOff_leg zOff_vr | Mpf_leg Mpf_vr  dMpf | Mprop | K_vr | Vc_cg Vc_hub\n");
        ac.CaptureForces = true;
        var cg = cfg.Mass.CgVec();
        Vec3 propArm = Aircraft.NoseBody(cfg) - cg;
        double sumLeg = 0, sumVr = 0; int n = 0;
        double devLeg = 0, devVr = 0; int devN = 0;
        for (int step = 0; step < 128; step++)
        {
            bool develop = step < 48;                       // 12 s at 0.25 s a step, ailerons neutral
            if (develop && step == 0) sb.Append("    [DEVELOP: ailerons NEUTRAL, full power]\n");
            if (!develop && step == 48) sb.Append("    [HOLD: full pro-spin aileron, full power - the 20 s the test asserts on]\n");
            sim.RunFor(0.25, new ControlInputs(develop ? 0 : 1, 1, 1, Full));
            Wrap(ac);
            var st = ac.State;
            Vec3 v = st.Velocity;
            double vt = v.Length;
            double alpha = Math.Atan2(v.Z, v.X) * 57.2958;
            double beta = Math.Asin(Math.Clamp(v.Y / Math.Max(vt, 1e-6), -1, 1)) * 57.2958;

            // The thrust the legacy path actually made this step. NormalForceK is 0 on this type, so the "thrust"
            // sample's x force IS PropModel's thrust scalar and nothing else is folded into it. Mprop is the whole
            // thrust group's pitch moment, reconstructed the way the budget probe reads it (the sample's moment is a
            // PURE one since 01cba1c5, so Cross(pos - cg, F) + M rebuilds it exactly and does not double-count).
            double thrustN = 0, mProp = 0;
            foreach (var f in ac.LastForces)
            {
                if (f.Kind != "thrust") continue;
                thrustN += f.ForceBody.X;
                mProp += (Vec3.Cross(f.PosBody - cg, f.ForceBody) + f.MomentBody).Y;
            }

            // The legacy offset, reproduced from the same state the model saw: the CG's velocity, sin(beta), no
            // velocity ratio. RotationSign is +1 here, so zOffset = -PFactorK * R * sin(beta).
            double sinB = v.Y / Math.Max(vt, 1e-6);
            double zOffLeg = -prop.RotationSign * prop.PFactorK * radius * sinB;

            // The derived form at this step's OWN conditions. The crossflow is the vector part of the velocity across
            // the shaft, |(Vy, Vz)|; its pitch share is the Vy component, which is what sin(beta) stood for.
            double rpm = PropModel.RunningRpm(prop, 1.0, 1.0);
            double omegaR = rpm * 2.0 * Math.PI / 60.0 * radius;
            double vcCg = Math.Sqrt(v.Y * v.Y + v.Z * v.Z);
            // AND AT THE DISC, not at the CG: the hub is a long arm ahead of the centre of rotation, so omega x r adds
            // several m/s of crossflow the CG's velocity does not carry. PropModel.NormalForce is already handed
            // velocity + omega x propArm for exactly this reason; the legacy P-factor is not.
            Vec3 vHub = v + Vec3.Cross(st.Rates, propArm);
            double vcHub = Math.Sqrt(vHub.Y * vHub.Y + vHub.Z * vHub.Z);
            double mu = vcCg / omegaR;
            double kvr = KBlade(prop, Math.Max(0.0, v.X), vcCg, rpm, -st.Position.Z);
            // offset = K_blade * v_cross / Omega, taken along the crossflow and resolved onto the pitch axis the same
            // way the legacy term is: the Vy share of the crossflow.
            double zOffVr = vcCg > 1e-6 ? -prop.RotationSign * kvr * (vcCg / (rpm * 2.0 * Math.PI / 60.0)) * (v.Y / vcCg) : 0.0;

            double mLeg = thrustN * zOffLeg, mVr = thrustN * zOffVr;
            if (develop) { devLeg += mLeg; devVr += mVr; devN++; }
            else { sumLeg += mLeg; sumVr += mVr; n++; }
            if (step % 2 == 0 || step < 12 || (step >= 48 && step < 56))
                sb.Append($"{step * 0.25,6:0.0} {alpha,5:0} {beta,5:0} {vt,5:0.0} {v.X,4:0} {v.Y,5:0} {v.Z,5:0}"
                          + $" {st.Rates.X * 57.3,5:0} {st.Rates.Y * 57.3,5:0} {st.Rates.Z * 57.3,5:0} |"
                          + $" {thrustN,4:0} {omegaR,4:0} | {mu,4:0.00} {sinB,5:0.00} |"
                          + $" {zOffLeg,8:0.0000} {zOffVr,7:0.0000} | {mLeg,7:0} {mVr,6:0} {mVr - mLeg,5:0} |"
                          + $" {mProp,5:0} | {kvr,4:0.00} | {vcCg,5:0.0} {vcHub,6:0.0}\n");
        }
        sb.Append($"    MEAN over the 12 s DEVELOP phase (ailerons neutral): Mpf_legacy {devLeg / Math.Max(1, devN):0} N.m, "
                  + $"Mpf_derived {devVr / Math.Max(1, devN):0} N.m, difference {(devVr - devLeg) / Math.Max(1, devN):+0;-0} N.m nose up\n");
        sb.Append($"    MEAN over the asserted 20 s HOLD: Mpf_legacy {sumLeg / Math.Max(1, n):0} N.m, "
                  + $"Mpf_derived {sumVr / Math.Max(1, n):0} N.m, difference {(sumVr - sumLeg) / Math.Max(1, n):+0;-0} N.m nose up\n");
        return sb.ToString();
    }

    /// <summary>
    /// WOULD CLOSING THAT GAP HOLD THE SPIN? The pinned test's own entry and its own three assertions, run against a
    /// PROBE-LOCAL copy of the config with PFactorK scaled. NOTHING IS WRITTEN: this is the same kind of override
    /// InvSpinPowerHoldProbe already takes for the prop inertia and the two spin-aero keys.
    ///
    /// READ IT FOR WHAT IT IS. A scaled CONSTANT is not the derived form — the honest form is velocity-dependent, so
    /// a constant of 0.11 would be right in this spin and wrong by 3x at cruise, and it would be a fleet re-rig
    /// besides. And PFactorK scales the YAW offset (yOffset, from sin(alpha)) as well as the pitch one, which has the
    /// identical defect but is not what was derived above, so this moves two terms and not one. It answers only
    /// "is the pitch budget's deficit the thing that loses the spin", which is worth knowing before anyone decides
    /// whether to re-shape the term.
    /// </summary>
    private static string Sensitivity(string id)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("\n--- SENSITIVITY (not a fix): the pinned test's entry and assertions with PFactorK scaled.\n");
        sb.Append("    0.350 is the shipped constant; 0.110 is what the derived form gives AT THIS SPIN'S conditions\n");
        sb.Append("    (0.35 x 0.31, see part C); 0.000 is the bound. Held > 18.0 s of 20, mean alpha < -55, mean\n");
        sb.Append("    |w| > 120 deg/s are the three assertions.\n");
        sb.Append("    The OLD rig (aileron 0.001678, fin 0.034855 rad) is run beside the honest one because it holds\n");
        sb.Append("    the spin today at 0.35: if the correction kept it holding there too, the two routes stop\n");
        sb.Append("    disagreeing. Neither rig is written anywhere; both are probe-local copies.\n");
        sb.Append("    rig     PFactorK | inverted  mean alpha  mean |w| | verdict\n");
        foreach ((string rigName, double ailRad, double finRad) in new[]
                 { ("honest", -1.0, -1.0), ("old   ", 0.001678, 0.034855) })
        foreach (double k in new[] { 0.35, 0.11, 0.0 })
        {
            var cfg = Load(id);
            cfg.Propulsion!.PFactorK = k;
            if (ailRad >= 0) { cfg.Rig!.AileronRad = ailRad; cfg.Rig.FinOffsetRad = finRad; }
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -3000), new Quat(1, 0, 0, 0), new Vec3(60, 0, 0), Vec3.Zero),
                                  new ControlDeflections(0, 0, 0, 0, 0));
            var sim = new SimLoop(ac);
            for (int i = 0; i < 100 && ac.State.Velocity.Length > 33; i++)
                sim.RunFor(0.1, new ControlInputs(0, Math.Min(1, 0.2 + 0.02 * i), 0, Idle));
            for (int i = 0; i < 48; i++) { sim.RunFor(0.25, new ControlInputs(0, 1, 1, Full)); Wrap(ac); }
            double invertedSec = 0, aSum = 0, wSum = 0; int n = 0;
            for (int i = 0; i < 80; i++)
            {
                sim.RunFor(0.25, new ControlInputs(1, 1, 1, Full));
                Wrap(ac);
                var st = ac.State;
                var yb = st.Attitude.Rotate(new Vec3(0, 1, 0));
                if (Math.Abs(Math.Atan2(yb.Z, st.Attitude.Rotate(new Vec3(0, 0, 1)).Z)) * 57.2958 > 120) invertedSec += 0.25;
                aSum += Math.Atan2(st.Velocity.Z, st.Velocity.X) * 57.2958;
                wSum += st.Rates.Length * 57.2958;
                n++;
            }
            bool pass = invertedSec > 18.0 && aSum / n < -55.0 && wSum / n > 120.0;
            sb.Append($"    {rigName}    {k,6:0.000} | {invertedSec,7:0.0}s {aSum / n,11:0.0} {wSum / n,9:0} | {(pass ? "HOLDS" : "lost")}\n");
        }
        return sb.ToString();
    }

    /// <summary>K_blade = (offset/R)/mu measured at this operating point out of the blade's own azimuthal integral.</summary>
    private static double KBlade(PropulsionConfig prop, double vAxial, double vCross, double rpm, double altM)
    {
        if (vCross < 1e-6 || rpm <= 0) return 0.0;
        double rho = Atmosphere.DensityAtAltitude(altM);
        double radius = 0.5 * prop.PropDiameterM;
        var drive = PropModel.SteadyDrive(prop, 1.0, vAxial, rho);
        double pitch = drive.BladePitchDeg > 0 ? drive.BladePitchDeg : prop.BladePitch75Deg;
        if (pitch <= 0) pitch = prop.FineStopDeg;
        var g = new BladeElement.Geometry(prop.PropBladeCount, radius, pitch, prop.ActivityFactor);
        var r = BladeElement.Solve(g, vAxial, vCross, rpm, rho);
        if (r.ThrustN <= 1e-6) return 0.0;
        double offset = r.ThrustMomentNm / r.ThrustN;
        double mu = vCross / (rpm * 2.0 * Math.PI / 60.0 * radius);
        return offset / radius / mu;
    }

    private static void Wrap(Aircraft ac)
    {
        if (-ac.State.Position.Z < 800)
            ac.State = new RigidBodyState(new Vec3(ac.State.Position.X, ac.State.Position.Y, ac.State.Position.Z - 2200),
                                          ac.State.Attitude, ac.State.Velocity, ac.State.Rates);
    }
}
