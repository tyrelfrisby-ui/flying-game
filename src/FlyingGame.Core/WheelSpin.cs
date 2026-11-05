using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// A wheel that TURNS (Ty 2026-11-04: "real physics, no fudge"). Until this existed every tyre rolled perfectly from
/// the first instant of contact, so a touchdown had no spin-up and the brake was a force with a lock-up branch.
///
/// THE MODEL. Each wheel has an angular speed ω and a polar inertia I. The tyre's longitudinal force comes from its
/// SLIP: the patch moves over the ground at s = v_fwd − ω·r, and with the slip ratio κ = s / max(|v_fwd|, V0)
///
///     D = μN · fall(κ) · sat(σ) · σx/σ,     σx = κ/κ0,  σ = |(σx, σy)|
///
/// where σy is the tyre's lateral demand in units of μN (its cornering force over μN) — so a tyre already skidding
/// sideways has less to give fore and aft, and a locked tyre's force points along its slip, the way the locked branch
/// this replaces pushed it against the sliding velocity. sat blends tanh (pure longitudinal: smooth, no chatter) with
/// the clamp the lateral model has always had (pure cornering is unchanged to the newton). fall(κ) takes the
/// coefficient from its peak (κ ≲ 0.15) to the kinetic value of a locked, sliding tyre, 0.8 of it — the same μ_k the
/// old locked branch used. D pushes the aircraft back (when the wheel is slower than the ground) and spins the wheel up:
///
///     I·dω/dt = D·r − T_brake − T_bearing
///
/// The brake is a TORQUE (Coulomb: it opposes the rotation and holds a stopped wheel up to its value), so lock-up is
/// not a branch: it happens when the brake asks for more than the ground can give, T_brake > max(D)·r, and the wheel
/// stops. Spin-up is the same equation with no brake: a wheel touching down at ω = 0 is dragged up to ω·r = v by μN,
/// in t ≈ I·v/(μ N r²) (docs/TIRE-SPINUP-AND-GROUND-SOUND.md), and that drag, at the bottom of the tyre, is the
/// spin-up drag spike and the nose-down moment. Nothing else is added for either.
///
/// INTEGRATION. The slip dynamics are stiff (a time constant of a millisecond or less at taxi speed), far stiffer than
/// the 5 ms rigid-body step, so the wheel is advanced ONCE per step, from the step's first RK4 stage, in sub-steps of
/// at most <see cref="MaxSubStepS"/>, linearly implicit in the slip (so it cannot ring), and never carried past the
/// synchronous speed in one sub-step. The force handed to the aircraft is the one that changed the wheel's angular
/// momentum, I·Δω/Δt + the brake and bearing torques actually applied, over r: what the wheel gained, the aeroplane
/// lost, to the newton-second.
///
/// ROLLING RESISTANCE is not in here. It stays a direct force at the patch (WorldTerrain.RollingCoefficient): most of
/// it on soft ground is the tyre ploughing, which is not a torque on the wheel, and on snow it is larger than the
/// grip (0.14–0.35 against 0.16), which no torque through the patch could deliver.
/// </summary>
public static class WheelSpin
{
    /// <summary>Slip ratio at which the longitudinal force reaches tanh(1) = 76 % of μN: aircraft tyres peak at 10–20 %
    /// slip (NASA TP-2917, the 737 brake runs; ESDU 71026), and a tanh with κ0 = 0.08 is 96 % there.</summary>
    public const double SlipRatioScale = 0.08;
    /// <summary>The speed below which the slip ratio is taken against this rather than against the ground speed: a slip
    /// ratio is meaningless at a standstill. 1 m/s keeps the stopped-wheel friction finite (a stiff damper rather than
    /// a sign) and the RK4 step stable for every type from the hang glider to the 737.</summary>
    public const double ReferenceSpeedMs = 1.0;
    /// <summary>A locked tyre's sliding friction over its peak — the μ_k ≈ 0.8 μ the locked branch had (dry rubber on
    /// asphalt 0.6–0.65 sliding against 0.8 peak).</summary>
    public const double KineticRatio = 0.8;
    /// <summary>Slip ratios over which the coefficient falls from its peak to the sliding value.</summary>
    public const double FallStart = 0.15, FallEnd = 1.0;
    /// <summary>Polar inertia over m·r² for an aircraft wheel: the tyre (most of the mass) is a thick ring at 0.7–0.9 r,
    /// the wheel halves and disc a solid near the hub. 0.65 is the docs' figure.</summary>
    public const double InertiaFactor = 0.65;
    public const double MaxSubStepS = 0.0005;
    /// <summary>Bearing and seal friction, Coulomb part, per wheel (N·m): two small tapered-roller bearings with grease
    /// seals, ~0.1–0.3 N·m each (SKF friction model); scaled with the wheel's mass for the big ones.</summary>
    public const double BearingTorquePerKg = 0.04;
    /// <summary>The viscous part, as the spin-down time of a free wheel in the air: 20–40 s (docs §4). It is what lets
    /// the bearing whir fade out over half a minute after take-off rather than stop.</summary>
    public const double BearingSpinDownS = 30.0;
    /// <summary>Slip power (W) above which a tyre is audibly slipping (the chirp and the skid voice).</summary>
    public const double AudibleSlipPowerW = 50.0;

    /// <summary>Does this leg have a wheel that turns? Float keels, skids and a hang glider's feet do not.</summary>
    public static bool IsWheel(GearConfig g)
        => g.GearType is not ("float-keel" or "nose-skid" or "tip-skid" or "tail-skid" or "feet");

    /// <summary>The wheel's rotating mass: the catalog tyre's (tyre, tube, wheel and — on a braked leg — the disc), a
    /// test's override, or for a code-built leg with no tyre ~160·r² kg (a fit through light-aircraft assemblies).</summary>
    public static double MassOf(AircraftConfig c, GearConfig g)
    {
        if (g.WheelMassKg > 0) return g.WheelMassKg;
        TireSpec? t = TireCatalog.Of(g);
        if (t != null) return t.RotatingMassKg(g.Brake);
        return 160.0 * System.Math.Pow(LandingGear.TireRadiusOf(c, g), 2);
    }

    /// <summary>The wheel's polar inertia: DERIVED from where the catalog tyre's mass is (TireSpec.InertiaKgM2), or
    /// for an override mass / a leg with no tyre, 0.65·m·r².</summary>
    public static double InertiaOf(AircraftConfig c, GearConfig g)
    {
        if (g.WheelInertiaKgM2 > 0) return g.WheelInertiaKgM2;
        TireSpec? t = TireCatalog.Of(g);
        if (t != null && g.WheelMassKg <= 0) return t.InertiaKgM2(g.Brake);
        double r = LandingGear.TireRadiusOf(c, g);
        return InertiaFactor * MassOf(c, g) * r * r;
    }

    /// <summary>The longitudinal force D (≥ 0 when the patch slips forward over the ground, i.e. the wheel is slower
    /// than the ground; D pushes the aircraft back) for slip ratio κ and lateral demand σy (cornering force over μN,
    /// unsigned). <paramref name="muN"/> is the peak μ times the load.</summary>
    public static double Longitudinal(double kappa, double sigmaY, double muN, double kappa0 = SlipRatioScale)
    {
        double sx = kappa / kappa0, sy = System.Math.Abs(sigmaY);
        double s2 = sx * sx + sy * sy;
        if (s2 < 1e-18) return muN * kappa / kappa0;
        double sig = System.Math.Sqrt(s2);
        double wx = sx * sx / s2;
        double sat = wx * System.Math.Tanh(sig) + (1 - wx) * System.Math.Min(sig, 1.0);
        return muN * Fall(kappa) * sat * sx / sig;
    }

    /// <summary>From peak friction to sliding friction as the slip ratio grows (smoothstep between FallStart and FallEnd).</summary>
    public static double Fall(double kappa)
    {
        double t = System.Math.Clamp((System.Math.Abs(kappa) - FallStart) / (FallEnd - FallStart), 0, 1);
        return 1 - (1 - KineticRatio) * t * t * (3 - 2 * t);
    }

    /// <summary>The most longitudinal force the tyre can give at this load and lateral demand, over all slips: the brake
    /// torque that locks the wheel is this × r.</summary>
    public static double Peak(double sigmaY, double muN, double kappa0 = SlipRatioScale)
    {
        double best = 0;
        for (int i = 1; i <= 80; i++) best = System.Math.Max(best, Longitudinal(i * 0.0125, sigmaY, muN, kappa0));
        return best;
    }

    /// <summary>
    /// Give every wheel the spin the state implies: a wheel on (or within 5 cm of) the ground rolls without slip at its
    /// patch's forward speed, one in the air is still (an aeroplane placed on an approach has had its wheels stop).
    /// </summary>
    public static void SeedRolling(AircraftConfig c, RigidBodyState s, WheelState[] wheels)
    {
        MathTypes.Vec3 cg = c.Mass.CgVec();
        MathTypes.Vec3 fwd = s.Attitude.Rotate(new MathTypes.Vec3(1, 0, 0));
        for (int i = 0; i < wheels.Length && i < c.Gear.Count; i++)
        {
            WheelState w = wheels[i];
            if (!w.IsWheel || w.RadiusM <= 0) { w.OmegaRadS = 0; continue; }
            MathTypes.Vec3 rBody = c.Gear[i].PosVec() - cg;
            MathTypes.Vec3 p = s.Position + s.Attitude.Rotate(rBody);
            double groundH = WorldTerrain.WheelGroundHeightAt(p.X, p.Y, -p.Z);
            if (!double.IsFinite(groundH) || p.Z + groundH < -0.05) { w.OmegaRadS = 0; continue; }
            MathTypes.Vec3 v = s.Attitude.Rotate(s.Velocity + MathTypes.Vec3.Cross(s.Rates, rBody));
            double fl = System.Math.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y);
            double vFwd = fl > 1e-6 ? (v.X * fwd.X + v.Y * fwd.Y) / fl : 0;
            // The tyre sits as deep as the static split of the leg's compression puts it, and rolls on that radius.
            double x = System.Math.Max(0, p.Z + groundH);
            if (w.Tire != null && x > 0)
            {
                w.TireDeflM = w.TireDeflNextM = c.Gear[i].SpringN > 0 ? x * c.Gear[i].SpringN / (c.Gear[i].SpringN + w.TireStiffnessNPerM) : x;
                w.RollingRadiusM = TireModel.RollingRadiusM(w.Tire, w.TireDeflM);
            }
            w.OmegaRadS = double.IsFinite(vFwd) ? vFwd / w.RollingRadiusM : 0;
        }
    }

    public static double SlipRatio(double vFwd, double omega, double r)
        => (vFwd - omega * r) / System.Math.Max(System.Math.Abs(vFwd), ReferenceSpeedMs);

    /// <summary>The result of one wheel step.</summary>
    public struct StepResult
    {
        /// <summary>Mean longitudinal force on the AIRCRAFT along the tyre's forward axis over the step (N; negative = drag
        /// when rolling forward).</summary>
        public double ForceN;
        public double SlipRatio, SlipPowerW, SlipEnergyJ, SlipTimeS, FallFactor;
        /// <summary>∫|D|dt over the step (N·s): with SlipEnergyJ it gives the patch's mean slip speed, E / ∫|D|dt.</summary>
        public double SlipImpulseNs;
        public bool Locked;
    }

    /// <summary>
    /// Advance one wheel by <paramref name="dt"/> with its load, ground speed and lateral demand held at the values
    /// given (the step's first stage). <paramref name="brakeTorqueNm"/> ≥ 0 is the brake's Coulomb torque.
    /// </summary>
    public static StepResult Advance(ref double omega, double inertia, double r, double muN, double vFwd, double sigmaY,
                                     double brakeTorqueNm, double bearingTorqueNm, double dt, double kappa0 = SlipRatioScale)
    {
        var res = new StepResult { FallFactor = 1 };
        if (dt <= 0 || inertia <= 0 || r <= 0) return res;
        // A non-finite input (an aeroplane deep inside geometry arrives here with NaN speeds) must not throw — a throw
        // here leaves FlightSimDriver.Update without its ApplyStateToTransform, a frozen aeroplane — and must not
        // poison the wheel: no force this step, and a spin that is not a number is a wheel at rest.
        if (!double.IsFinite(omega)) omega = 0;
        if (!double.IsFinite(vFwd) || !double.IsFinite(muN) || !double.IsFinite(sigmaY) || !double.IsFinite(brakeTorqueNm)) return res;
        int n = System.Math.Max(1, (int)System.Math.Ceiling(dt / MaxSubStepS - 1e-9));
        double h = dt / n;
        double vref = System.Math.Max(System.Math.Abs(vFwd), ReferenceSpeedMs);
        double visc = inertia / BearingSpinDownS;                 // viscous bearing drag, N·m per rad/s
        double coulomb = System.Math.Max(0, brakeTorqueNm) + System.Math.Max(0, bearingTorqueNm);
        double sync = vFwd / r;
        double sumD = 0;
        for (int i = 0; i < n; i++)
        {
            double k = (vFwd - omega * r) / vref;
            double d = Longitudinal(k, sigmaY, muN, kappa0);
            const double eps = 1e-4;
            double dDdk = (Longitudinal(k + eps, sigmaY, muN, kappa0) - Longitudinal(k - eps, sigmaY, muN, kappa0)) / (2 * eps);
            // ∂D/∂ω = −∂D/∂κ · r/v_ref. Only the stabilising (falling-with-ω) part goes in the implicit denominator: past
            // the peak the curve turns over, which is the physical instability that locks a wheel, and is left explicit.
            double stiff = System.Math.Max(0, dDdk) * r / vref;   // −∂D/∂ω
            double denom = inertia + h * (r * stiff + visc);
            double free = h * (d * r - visc * omega) / denom;
            double w1 = omega + free;
            // Coulomb torques (brake, bearing seal) oppose the way the wheel is going; if they can stop it inside the
            // sub-step it stays stopped (the brake holds it: locked, or simply parked).
            double stop = h * coulomb / denom;
            double dir = w1 != 0 ? System.Math.Sign(w1) : 0;
            double w2 = w1 - dir * stop;
            bool stuck = dir == 0 || System.Math.Sign(w2) != dir;
            if (stuck) w2 = 0;
            // Never carried through the synchronous speed in one sub-step by the slip alone.
            bool clamped = false;
            if (!stuck && (omega - sync) * (w2 - sync) < 0 && (d * r) * (w2 - omega) > 0) { w2 = sync; clamped = true; }
            // The force is the linearised D at the new ω, which is exactly what the wheel's equation used, so the
            // wheel's angular momentum and the aircraft's impulse agree (a stopped wheel's brake takes whatever is
            // left, inside its torque). Only the sync clamp breaks the linearisation; there, count what turned it.
            double dUsed = clamped
                ? (inertia * (w2 - omega) / h + visc * w2 + dir * coulomb) / r
                : d - stiff * (w2 - omega);
            double kEnd = (vFwd - w2 * r) / vref;
            dUsed = System.Math.Clamp(dUsed, -muN, muN);
            omega = w2;
            sumD += dUsed;
            double slipV = System.Math.Abs(vFwd - omega * r);
            double p = System.Math.Abs(dUsed) * slipV;
            res.SlipEnergyJ += p * h;
            res.SlipImpulseNs += System.Math.Abs(dUsed) * h;
            if (p > AudibleSlipPowerW) res.SlipTimeS += h;
            res.SlipPowerW = p;
            res.SlipRatio = kEnd;
            res.FallFactor = Fall(kEnd);
            res.Locked = stuck && System.Math.Abs(vFwd) > 0.3;
        }
        res.ForceN = -sumD / n;
        return res;
    }

    /// <summary>
    /// A wheel with no spin state — a leg the caller keeps none for (the tests' and probes' direct calls) or one
    /// that is not a wheel (a skid, a hang glider's feet): its steady state. Rolling, the brake's torque is
    /// balanced by the ground, D = T/r; if it asks for more than the tyre's peak, the wheel locks and slides at
    /// the sliding friction. Returns the longitudinal force on the aircraft along the tyre (signed against v_fwd).
    /// </summary>
    public static double QuasiStatic(double vFwd, double sigmaY, double muN, double brakeForceN, out bool locked, out double fall,
                                     double kappa0 = SlipRatioScale)
    {
        locked = false; fall = 1;
        if (brakeForceN <= 0) return 0;
        double sign = System.Math.Sign(double.IsFinite(vFwd) && vFwd != 0 ? vFwd : 1);
        double peak = Peak(sigmaY, muN, kappa0);
        if (brakeForceN <= peak || System.Math.Abs(vFwd) <= 0.3) return -sign * System.Math.Min(brakeForceN, peak);
        locked = true; fall = KineticRatio;
        return -sign * Longitudinal(1.0, sigmaY, muN, kappa0);
    }
}

/// <summary>
/// One wheel's spin and what it is doing on the ground, kept by the aircraft (<c>Aircraft.Wheels</c>, config.Gear
/// order) and advanced by <see cref="LandingGear.Compute(AircraftConfig, RigidBodyState, double, double, double, double, System.Collections.Generic.IReadOnlyCollection{AirframeComponent}?, System.Collections.Generic.List{AirframeComponent}?, bool[]?, ImpactRecorder?, LandingGear.TailwheelState?, bool, int, double, double[]?, WheelState[]?)"/>
/// on each step's first stage. Everything below the spin is the contact as that stage found it — the sound, skid
/// marks and smoke read it and never write it.
/// </summary>
public sealed class WheelState
{
    public readonly bool IsWheel;
    /// <summary>A float keel or a skid: metal or wood sliding on the ground, which scrapes (a hang glider's feet do not).</summary>
    public readonly bool Scrapes;
    /// <summary>RadiusM is the tyre's FREE radius; the wheel rolls on <see cref="RollingRadiusM"/>.</summary>
    public readonly double InertiaKgM2, RadiusM, BearingTorqueNm;
    /// <summary>The catalog tyre on this leg (null: a skid, feet, or a code-built leg with no tyre — rigid, as before),
    /// its pressure, and what they make of it: spring, damper, the deflection where the rim bottoms.</summary>
    public readonly TireSpec? Tire;
    public readonly double PressurePsi, TireStiffnessNPerM, TireDampingNsPerM, TireBottomM;
    /// <summary>The tyre's deflection at the start of this step, and the end-of-step value the step's stages work out
    /// (the next step starts from it).</summary>
    public double TireDeflM, TireDeflNextM;
    /// <summary>The contact patch as the step's first stage found it, the ground pressure under it, and the slip ratio
    /// scale it gave the tyre (TireModel.SlipRatioScale).</summary>
    public double FootprintLengthM, FootprintWidthM, GroundPressurePa, SlipScale = WheelSpin.SlipRatioScale;
    /// <summary>−1 left of the centreline, +1 right, 0 on it.</summary>
    public readonly int Side;

    /// <summary>Angular speed, rad/s, + = rolling forward.</summary>
    public double OmegaRadS;
    /// <summary>The longitudinal tyre force per newton of load this step (signed along the tyre's forward axis): each
    /// RK4 stage applies it to its own load, so the step's impulse is the wheel's.</summary>
    public double ForcePerLoad;
    /// <summary>How much of μN is left for cornering by the longitudinal slip (its fall toward the sliding value).</summary>
    public double FallFactor = 1;
    /// <summary>Set by the step's first stage when it advanced the wheel; the aircraft clears it after the step and lets
    /// a wheel nobody advanced coast in its bearings.</summary>
    public bool Advanced;

    public bool InContact, OnSki, OverWater, Locked;
    public double LoadN, VFwdMs, SlipRatio, SlipPowerW, Brake01, BrakeTorqueNm;
    public WorldTerrain.Surface Surface;
    public GroundMaterial Material;
    /// <summary>The wheel's ground speed if it were rolling without slip: ω·r (r the rolling radius, a flat's smaller one).</summary>
    public double RimSpeedMs => OmegaRadS * RollingRadiusM;

    /// <summary>The tread (<see cref="TyreWear"/>): null for a leg that is not a wheel.</summary>
    public readonly TyreWear? Tyre;
    /// <summary>How far the wheel has turned (rad, + = rolling forward). The tread under the patch is at −AngleRad.</summary>
    public double AngleRad;
    /// <summary>How much lower the axle stands than on a sound tyre this step (<see cref="TyreWear.Drop"/>): what the
    /// gear's contact point is raised by, and what comes off the rolling radius.</summary>
    public double DropM;
    /// <summary>The ground's load on this tyre at the last step it touched (kept across steps: the patch it sizes is the
    /// one the next step's drop is measured over).</summary>
    public double TyreLoadN;
    /// <summary>The tyre's contact point (world, NED) on the last step it touched.</summary>
    public MathTypes.Vec3 PatchWorld;
    /// <summary>R − δ/3 at the current deflection (TireModel): what a sound tyre rolls on, before any flat.</summary>
    public double SoundRollingRadiusM;
    /// <summary>What the wheel rolls on: the sound tyre's rolling radius, less a worn or flat tyre's drop.</summary>
    public double RollingRadiusM
    {
        get => System.Math.Max(SoundRollingRadiusM - DropM, 0.25 * RadiusM);
        set => SoundRollingRadiusM = value;
    }
    // For whoever listens or draws, read and cleared by them: rubber torn off (m³: the smoke), the work the axle did
    // falling into and climbing out of a worn or flat patch (J: the thump), and how many times the tyre has burst.
    public double AbradedM3, ThumpJ;
    public int Bursts;

    // Accumulated for whoever listens (the sound), read and cleared by them: slip energy (J) and time (s) spent
    // audibly slipping since the last read. A spin-up lasting 10 ms falls between two frames; these carry it across.
    // The impulses (∫|F|dt, N·s) give the patch's mean slip speed over the same time, E / ∫|F|dt: fore and aft (the
    // spin-up, a skid) and sideways (a crabbed touchdown, a scrubbing turn), each from its own force and slip.
    public double SlipEnergyJ, SlipTimeS, SlipImpulseNs;
    public double SideSlipEnergyJ, SideSlipTimeS, SideSlipImpulseNs;
    /// <summary>The tyre's outside diameter (m) for the sound: the catalog's, legacy sizes included, else twice the
    /// leg's radius. It sets the pitch of the squeal (<see cref="GroundSoundMix.SquealResonanceHz"/>).</summary>
    public readonly double SoundOdM;

    public WheelState(AircraftConfig c, GearConfig g)
    {
        IsWheel = WheelSpin.IsWheel(g);
        Scrapes = !IsWheel && g.GearType != "feet";
        RadiusM = LandingGear.TireRadiusOf(c, g);
        RollingRadiusM = RadiusM;
        TireSpec? spec = IsWheel ? TireCatalog.Of(g) : null;
        SoundOdM = spec != null && spec.OdM > 0 ? spec.OdM : 2 * RadiusM;
        Tire = spec != null && spec.HasCarcass ? spec : null;     // a legacy size is rigid, as it always was
        if (Tire != null)
        {
            PressurePsi = TirePressure.Psi(c, g);
            TireStiffnessNPerM = TireModel.StiffnessNPerM(Tire, PressurePsi);
            TireDampingNsPerM = TireModel.DampingNsPerM(Tire, TireStiffnessNPerM, WheelSpin.MassOf(c, g));
            TireBottomM = TireModel.BottomDeflectionM(Tire);
        }
        InertiaKgM2 = IsWheel ? WheelSpin.InertiaOf(c, g) : 0;
        BearingTorqueNm = IsWheel ? WheelSpin.BearingTorquePerKg * WheelSpin.MassOf(c, g) : 0;
        Side = g.Pos[1] < -0.05 ? -1 : g.Pos[1] > 0.05 ? 1 : 0;
        if (IsWheel && RadiusM > 0) Tyre = new TyreWear(c, g, RadiusM, StaticLoadOf(c, g));
    }

    /// <summary>This leg's rough share of the weight at rest — braked mains 0.88 of it between them, the other wheels
    /// the rest (the same split LandingGear sizes the brake with). Only the tyre's reference deflection reads it.</summary>
    public static double StaticLoadOf(AircraftConfig c, GearConfig g)
    {
        int braked = 0, other = 0;
        foreach (GearConfig x in c.Gear) { if (!WheelSpin.IsWheel(x)) continue; if (x.Brake) braked++; else other++; }
        double w = (c.Mass != null ? c.Mass.MassKg : 800) * 9.81;
        if (braked == 0) return w / System.Math.Max(1, other);
        return g.Brake ? 0.88 * w / braked : 0.12 * w / System.Math.Max(1, other);
    }

    public static WheelState[] For(AircraftConfig c)
    {
        var w = new WheelState[c.Gear.Count];
        for (int i = 0; i < w.Length; i++) w[i] = new WheelState(c, c.Gear[i]);
        return w;
    }

    /// <summary>
    /// What the step did to the tread: the friction work at the patch is laid on the strips it swept while the wheel
    /// turned from <paramref name="angleFrom"/> to <paramref name="angleTo"/> (unwrapped), which becomes its angle; a tyre
    /// that goes through this step bursts.
    /// </summary>
    public void Wear(double workJ, double slipSpeedMs, double loadN, double angleFrom, double angleTo, double dt)
    {
        if (Tyre != null)
        {
            bool wasBurst = Tyre.Burst;
            AbradedM3 += Tyre.Abrade(workJ, slipSpeedMs, loadN, -angleFrom, -angleTo, dt, Surface);
            if (Tyre.Burst && !wasBurst) Bursts++;
            Tyre.Leak(dt);
        }
        if (double.IsFinite(angleTo)) AngleRad = WrapAngle(angleTo);
    }

    /// <summary>Forget the last step's contact (the step's first stage refills it).</summary>
    public void ClearContact()
    {
        InContact = false; OnSki = false; OverWater = false; Locked = false;
        LoadN = 0; SlipPowerW = 0; SlipRatio = 0; Brake01 = 0; BrakeTorqueNm = 0; ForcePerLoad = 0; FallFactor = 1;
        FootprintLengthM = 0; FootprintWidthM = 0; GroundPressurePa = 0;
    }

    /// <summary>Not advanced against the ground this step (in the air, on a ski, in the water): the wheel turns down in
    /// its bearings (Coulomb seal drag + the viscous spin-down).</summary>
    public void Coast(double dt)
    {
        ForcePerLoad = 0; FallFactor = 1; SlipPowerW = 0; SlipRatio = 0; Locked = false;
        if (!IsWheel || InertiaKgM2 <= 0) { OmegaRadS = 0; return; }
        double w0 = OmegaRadS;
        double w = OmegaRadS * System.Math.Exp(-dt / WheelSpin.BearingSpinDownS);
        double stop = BearingTorqueNm * dt / InertiaKgM2;
        OmegaRadS = System.Math.Abs(w) <= stop ? 0 : w - System.Math.Sign(w) * stop;
        if (double.IsFinite(OmegaRadS)) AngleRad = WrapAngle(AngleRad + 0.5 * (w0 + OmegaRadS) * dt);
        // Off the ground: nothing squashes the tyre, and the strips cool.
        TyreLoadN = 0; DropM = Tyre != null ? Tyre.Drop(-AngleRad) : 0;
        if (Tyre != null) { Tyre.LeaveGround(); Tyre.Leak(dt); }
    }

    /// <summary>Keep a wheel angle within one turn of zero (the tread is periodic; the double must not grow forever).</summary>
    public static double WrapAngle(double a)
    {
        const double twoPi = 2 * System.Math.PI;
        if (a >= -twoPi && a <= twoPi) return a;
        return a - twoPi * System.Math.Floor(a / twoPi);
    }

    /// <summary>Read and clear the slip accumulated since the last call.</summary>
    public void TakeSlip(out double energyJ, out double timeS)
    {
        energyJ = SlipEnergyJ; timeS = SlipTimeS;
        SlipEnergyJ = 0; SlipTimeS = 0; SlipImpulseNs = 0;
        SideSlipEnergyJ = 0; SideSlipTimeS = 0; SideSlipImpulseNs = 0;
    }

    /// <summary>One direction of the patch's slip since the last read: energy, audible time, and the mean slip speed
    /// E / ∫|F|dt (0 if there was no force).</summary>
    public readonly struct Slip
    {
        public readonly double EnergyJ, TimeS, SpeedMs;
        public Slip(double e, double t, double impulse)
        {
            EnergyJ = e; TimeS = t;
            SpeedMs = impulse > 1e-9 && double.IsFinite(e / impulse) ? e / impulse : 0;
        }
    }

    /// <summary>Read and clear both directions of slip accumulated since the last call.</summary>
    public void TakeSlip(out Slip fore, out Slip side)
    {
        fore = new Slip(SlipEnergyJ, SlipTimeS, SlipImpulseNs);
        side = new Slip(SideSlipEnergyJ, SideSlipTimeS, SideSlipImpulseNs);
        TakeSlip(out double _, out double _);
    }
}
