using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// RIGGING (owner 2026-11-02: "trim/rig all of the airplanes for hands off level 75% power. so the aileron will be
/// trimmed so that neutral is zero roll at 75% power and the vertical stabilizer will be angled such that the
/// yaw/sideslip is zero at 75% level flight").
///
/// Solves the full six-degree-of-freedom steady state — all three forces and all three moments zero, no rates — on
/// exactly the forces the sim flies (<see cref="AeroModel.Compute"/> with the tail slipstream and its swirl,
/// <see cref="Aircraft.PropulsionForces"/> for torque, P-factor, spiral slipstream and thrust line, the weight-shift
/// swing where there is one). Stick and rudder are centred, sideslip is zero, and the unknowns are:
///   powered — true airspeed, angle of attack, elevator, aileron rig, fin offset and bank, in LEVEL flight at a given
///             fraction of rated power (pitch attitude follows from α and bank: no climb, no descent);
///   gliders — pitch attitude (the glide path), angle of attack, elevator, aileron rig, fin offset and bank, at a
///             given airspeed (best glide, <see cref="GlideRigTas"/>).
/// The bank is tiny (a fraction of a degree): with zero sideslip the fin's own lift is a side force, and the only
/// thing that can carry it in straight flight is a sliver of the lift vector.
///
/// A type without both ailerons and a fin (the hang glider and the paraglider) has nothing to rig: its lateral
/// unknowns are held at zero and only the longitudinal trim is solved, with the lateral residual reported.
///
/// This is an OFFLINE solver: RigTool runs it and writes the two rig angles into each config, where they are fixed
/// geometry. <see cref="SolveLevel"/> (rig held) is what an in-air start uses to put the aeroplane on its trimmed
/// speed and elevator; nothing here runs per frame.
/// </summary>
public static class RigSolver
{
    /// <summary>The rig power: 75 % of rated (PropModel: shaft power = MaxPowerW × throttle; a jet's thrust likewise).</summary>
    public const double RigThrottle = 0.75;
    /// <summary>The rig altitude: 4,000 ft ISA, between the island's coastal in-air starts (1,500–1,900 ft) and its
    /// inland ones (5,800–6,500 ft). The engine model has no altitude lapse, so 75 % is the same power at any height.</summary>
    public const double RigAltitudeM = 1219.2;

    /// <summary>(A class with setters, not init-only: Unity's compiler has no IsExternalInit.)</summary>
    public sealed class Result
    {
        public bool Converged { get; set; }
        public int Iterations { get; set; }
        public double TasMs { get; set; }
        public double AlphaRad { get; set; }
        public double ThetaRad { get; set; }
        public double BankRad { get; set; }
        public double ElevatorRad { get; set; }
        public double AileronRigRad { get; set; }
        public double FinOffsetRad { get; set; }
        public double Throttle { get; set; }
        public double AltitudeM { get; set; }
        /// <summary>Normalised residual [Fx, Fy, Fz]/W and [L, M, N]/(W·1 m) at the solution.</summary>
        public double[] Residual { get; set; } = Array.Empty<double>();
        public bool LateralSolved { get; set; }
        /// <summary>Climb (+) / descent (−) angle of the velocity, rad.</summary>
        public double GammaRad { get; set; }
        public double ResidualNorm => Norm(Residual);
    }

    /// <summary>Whether the type has the two things a lateral rig needs: aileron strips and a fin.</summary>
    public static bool CanRig(AircraftConfig c)
    {
        bool ail = false, fin = false;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            if (IsVertical(sf)) fin = true;
            foreach (StripConfig st in sf.Strips) if (st.Control?.Surface == "aileron") ail = true;
        }
        return ail && fin && c.WeightShift is null;
    }

    private static bool IsVertical(SurfaceConfig sf) =>
        sf.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase) || sf.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

    /// <summary>The rig condition for the type: level at <see cref="RigThrottle"/> if it has an engine, best glide if not.</summary>
    public static Result SolveRig(AircraftConfig c, double altitudeM = RigAltitudeM) =>
        c.Propulsion is null ? SolveGlide(c, GlideRigTas(c, altitudeM), altitudeM, solveRig: true)
                             : SolvePoweredLevel(c, RigThrottle, altitudeM, solveRig: true);

    /// <summary>Level flight at this throttle with the config's own rig held: airspeed, α, elevator and bank. The in-air start.</summary>
    public static Result SolveLevel(AircraftConfig c, double throttle, double altitudeM) => SolvePoweredLevel(c, throttle, altitudeM, solveRig: false);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, object> _aileronAtRigAltitude = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, object> _tableCache = new();

    private static IReadOnlyDictionary<string, AirfoilTable> Tables(AircraftConfig c)
    {
        if (_tableCache.TryGetValue(c, out object? t)) return (IReadOnlyDictionary<string, AirfoilTable>)t;
        var built = Aircraft.BuildAirfoilTables(c);
        _tableCache.AddOrUpdate(c, built);   // AddOrUpdate, not Add: see AileronTrimForAltitude's cache note
        return built;
    }

    /// <summary>
    /// The aileron deflection, rad, that zeroes the ROLLING MOMENT at an already-solved steady level state — a 1-D
    /// secant on L(δa) through <see cref="SteadyForces"/>, nothing else moved.
    ///
    /// WHY 1-D AND NOT A RIG RE-SOLVE, which is what this was first written as and had to be thrown away. Re-solving
    /// the whole rig at the start altitude (<see cref="SolveRig"/>, six unknowns, a 6×6 finite-difference Jacobian with
    /// the lateral pair in it) measured **16.1× the cost of the <see cref="SolveLevel"/> that a spawn already pays**
    /// — 3,189 ms against 198 ms on a loaded machine; the absolutes are contention-dependent but the ratio is not.
    /// <c>AirStart.ComputeRandom</c> can call <c>Compute</c> five times while it hunts for a separated multiplayer
    /// start, so that is up to five of them, and a spawn hitch is something the pilot feels on every single flight
    /// and reads as a different bug. (This repo has paid for exactly that mistake before: the blade-element solve
    /// going per-step made the J-3 "noticeably slower" for days and cost a wrong hypothesis about a memory leak.)
    ///
    /// The 1-D version is affordable because the thing being cancelled is a rolling moment and nothing else: one
    /// `SteadyForces` evaluation per secant step, about eight of them, against SolveLevel's ten iterations of a 5×5
    /// Jacobian. It is also a more direct answer to the question — the aileron that zeroes L here — than re-rigging,
    /// which would also move the fin offset, the bank, α and the speed to get there.
    ///
    /// Second order and deliberately ignored: a few hundredths of a degree of aileron changes Fz and so the trimmed α
    /// by a hair. Over the whole reachable band the preset is under 0.1°, so re-converging the longitudinal trim
    /// around it would buy less than the solve's own tolerance.
    /// </summary>
    public static double AileronForZeroRoll(AircraftConfig c, Result level)
    {
        var tables = Tables(c);
        double rho = Atmosphere.DensityAtAltitude(level.AltitudeM);
        double thr = c.Propulsion is null ? 0.0 : RigThrottle;
        Vec3 vb = new(level.TasMs * Math.Cos(level.AlphaRad), 0, level.TasMs * Math.Sin(level.AlphaRad));
        double w = Math.Max(1e-9, c.Mass.MassKg * 9.80665);
        double Roll(double da)
        {
            (Vec3 _, Vec3 m) = SteadyForces(c, tables, thr, vb, rho, new ControlDeflections(da, level.ElevatorRad, 0, 0));
            return m.X / w;
        }
        // Secant from zero with a 0.1 deg probe: the rolling moment is very nearly linear in aileron over the
        // hundredths of a degree this lives in, so this converges in a handful of steps. Bracketed to +-2 deg, which
        // is two orders of magnitude more than any measured preset, so a type whose L never crosses zero cannot run
        // away; it returns the clamp and the difference in AileronTrimForAltitude then reports whatever it reports.
        const double probe = 0.1 * Math.PI / 180.0, lim = 2.0 * Math.PI / 180.0;
        double x0 = 0.0, f0 = Roll(x0), x1 = probe, f1 = Roll(x1);
        for (int i = 0; i < 20 && Math.Abs(f1) > 1e-10; i++)
        {
            double d = f1 - f0;
            if (Math.Abs(d) < 1e-18) break;
            double x2 = Math.Clamp(x1 - f1 * (x1 - x0) / d, -lim, lim);
            x0 = x1; f0 = f1; x1 = x2; f1 = Roll(x1);
        }
        return Math.Clamp(x1, -lim, lim);
    }

    /// <summary>
    /// PRESET AILERON TRIM FOR A START AWAY FROM THE RIG ALTITUDE (Ty, 2026-11-10). The aileron deflection, rad, to
    /// hold at an in-air start trimmed to <paramref name="level"/> so that the wings stay level hands off. Zero if it
    /// cannot be derived (<paramref name="why"/> says so) and, by construction, zero at <see cref="RigAltitudeM"/>.
    ///
    /// WHAT THE PROBLEM IS. The aileron rig and the fin offset are solved ONCE, at <see cref="RigAltitudeM"/>, and
    /// written into the config as fixed geometry; <see cref="SolveLevel"/> then holds that geometry and solves only
    /// speed, α, elevator and the sliver of bank. A fixed rig works through dynamic pressure, so at any other height
    /// it over- or under-corrects and leaves a rolling moment that NOTHING trims out — with the stick centred the
    /// aeroplane rolls off. Measured on this model, still air, 10 s hands off at 10,000 ft AGL over KMYL, the fast
    /// types roll 8–15° (<c>tools/FlightTests/HighStartDriftProbe.cs</c>), and Ty now has a start-altitude picker
    /// that reaches that height.
    ///
    /// Ty, 2026-11-10, offered this or re-solving the whole rig at the start altitude, chose: **preset aileron trim**.
    ///
    /// HOW IT IS DERIVED — a difference of two measurements of the aeroplane, nothing typed:
    ///     preset = AileronForZeroRoll(at the start height) − AileronForZeroRoll(at the rig height)
    /// the aileron that cancels the rolling moment the fixed rig leaves HERE, less the one that cancels it where the
    /// rig was solved. It is the minimum that cancels the measured drift because it is exactly the rolling moment
    /// being cancelled and nothing else is touched.
    ///
    /// WHY IT IS A DIFFERENCE AND NOT JUST THE FIRST TERM. The stored rigs do not close on today's model — the
    /// Extra leaves L = −1.3 × 10⁻² and the C-172 −7.9 × 10⁻³ at their own 4,000 ft rig point — so the first term
    /// alone is NOT zero at the rig altitude, which is precisely the case Ty required to stay centred, and it would
    /// quietly fold "this config's rig is stale" into "this start is high". The difference is identically zero at the
    /// rig altitude whatever the config says, and says nothing about staleness. For the same reason it is not measured
    /// against `c.Rig.AileronRad`, which is a hand-copied literal and is a few hundredths of a degree out on most
    /// types (Extra 0.0394°, C-172 0.0322°, Gee Bee 0.0271°).
    ///
    /// WHERE THIS SITS AGAINST "TRIMS CENTRED EVERY FLIGHT", which is Ty's own rule and the stick-free launch fix.
    /// The rule says a flight begins with the trims centred, so that the aeroplane a player meets is the same
    /// aeroplane every time and no leftover trim from the last flight flies the first frame. This puts a NON-ZERO
    /// aileron trim on the aeroplane at a start away from 4,000 ft, which is a breach of that rule, and Ty chose it
    /// knowing so — the lane that found the drift (lane/claude-start-altitude, 2026-11-03) flagged the conflict in
    /// those words and he answered "preset aileron trim" anyway. Three things keep the breach narrow:
    ///   • it is a FUNCTION OF THE START, not a memory of the last flight: two starts at the same height in the same
    ///     air get the same preset to the bit, so the launch stays replicable (LaunchReplicationTests);
    ///   • it is ZERO at the rig altitude, so every test and every flight that starts there is bit-identical to
    ///     before, including the stick-free launch work;
    ///   • it is tiny. Over every height the picker can reach, |preset| comes out under a tenth of a degree, which is
    ///     a stick input under 0.01 of full travel — the trim INDICATOR does not visibly leave centre.
    /// If Ty ever finds an aileron trim that is not centred and is surprised, THIS is the reason, and it is the
    /// paragraph to read. docs/RIGGING.md has the same argument with the measurements.
    ///
    /// Only the AILERON. The fin offset drifts with height too — more than the aileron on some types — and cancelling
    /// it would want preset rudder trim as well. Ty was offered "aileron/rudder" and chose "preset aileron trim", so
    /// the yaw is left alone and the aeroplane keeps whatever small heading drift the fixed fin leaves.
    /// </summary>
    public static double AileronTrimForAltitude(AircraftConfig c, Result level, out string? why)
    {
        why = null;
        // At the rig altitude there is nothing to cancel, and saying so here rather than relying on the subtraction
        // keeps the common case free of any solve at all as well as exactly zero.
        if (Math.Abs(level.AltitudeM - RigAltitudeM) < 1e-6) return 0.0;
        if (!level.Converged) { why = $"{c.Id}: the level solve did not converge — aileron trim left centred"; return 0.0; }
        if (!CanRig(c)) { why = $"{c.Id} has nothing to rig laterally (needs ailerons and a fin)"; return 0.0; }
        if (!_aileronAtRigAltitude.TryGetValue(c, out object? cached))
        {
            // Cached per config: the rig-altitude reference is a property of the airframe and never changes, so only
            // the first in-air start of a type pays the extra SolveLevel. ConditionalWeakTable so a config that goes
            // out of scope is collected with it, as PracticeScenario's alpha cap already does. AddOrUpdate, not Add:
            // the test suite runs collections in parallel and Add THROWS on a duplicate key, so two threads reaching
            // a cold cache for the same config would turn a cache into a crash. Both compute the same answer.
            Result atRig = SolveLevel(c, RigThrottle, RigAltitudeM);
            cached = atRig.Converged ? AileronForZeroRoll(c, atRig) : double.NaN;
            _aileronAtRigAltitude.AddOrUpdate(c, cached);
        }
        double reference = (double)cached;
        if (double.IsNaN(reference))
        {
            why = $"{c.Id}: the level solve at the rig altitude did not converge — aileron trim left centred";
            return 0.0;
        }
        return AileronForZeroRoll(c, level) - reference;
    }

    /// <summary>
    /// <see cref="AileronTrimForAltitude(AircraftConfig, Result, out string?)"/> for a caller that has no solved level
    /// state yet, which is the tests and the probes. The production path (<c>AirStart.Compute</c>) uses the overload
    /// above and passes the <see cref="SolveLevel"/> result it already has, so a spawn pays for the 1-D aileron find
    /// and not for a second Newton solve.
    /// </summary>
    public static double AileronTrimForAltitude(AircraftConfig c, double altitudeM, out string? why)
    {
        if (Math.Abs(altitudeM - RigAltitudeM) < 1e-6) { why = null; return 0.0; }
        Result level = c.Propulsion is null
            ? SolveGlide(c, RigGlideTasAt(c, altitudeM), altitudeM)
            : SolveLevel(c, RigThrottle, altitudeM);
        return AileronTrimForAltitude(c, level, out why);
    }

    /// <summary>Steady glide at this airspeed, rig held (or solved).</summary>
    public static Result SolveGlide(AircraftConfig c, double tasMs, double altitudeM, bool solveRig = false)
    {
        var p = new Problem(c, altitudeM, 0.0, glide: true, fixedV: tasMs);
        TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, tasMs, altitudeM);
        double[] x = { t.Converged ? t.ThetaRad : -0.05, t.Converged ? t.AlphaRad : 0.08, t.Converged ? t.ElevatorRad : 0.0, c.Rig.AileronRad, c.Rig.FinOffsetRad, 0.0 };
        return p.Newton(x, solveRig && CanRig(c));
    }

    /// <summary>Best-glide true airspeed (power off, clean): the speed of the greatest lift-to-drag ratio from the model's
    /// own polar, by golden-section search over trimmed glides.</summary>
    public static double BestGlideTas(AircraftConfig c, double altitudeM = RigAltitudeM)
    {
        double vs = Math.Max(5.0, c.SpawnIasMs > 1 ? c.SpawnIasMs : 22.0);
        double a = 0.5 * vs, b = 2.5 * vs;
        double Ld(double v)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(c, v, altitudeM);
            return t.Converged && !double.IsNaN(t.GlideRatio) ? t.GlideRatio : 0.0;
        }
        // Coarse scan first (the polar can have a flat top and an unconverged low end), then golden section.
        double best = a, bestLd = -1;
        for (int i = 0; i <= 40; i++) { double v = a + (b - a) * i / 40.0, l = Ld(v); if (l > bestLd) { bestLd = l; best = v; } }
        double step = (b - a) / 40.0, lo = Math.Max(a, best - step), hi = Math.Min(b, best + step);
        const double g = 0.6180339887498949;
        double x1 = hi - g * (hi - lo), x2 = lo + g * (hi - lo), f1 = Ld(x1), f2 = Ld(x2);
        for (int i = 0; i < 40; i++)
        {
            if (f1 < f2) { lo = x1; x1 = x2; f1 = f2; x2 = lo + g * (hi - lo); f2 = Ld(x2); }
            else { hi = x2; x2 = x1; f2 = f1; x1 = hi - g * (hi - lo); f1 = Ld(x1); }
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>The solved state's attitude on a heading (rad): heading, then pitch, then the small bank.</summary>
    public static Quat Attitude(Result r, double headingRad)
    {
        Quat yaw = new(0, 0, Math.Sin(headingRad / 2), Math.Cos(headingRad / 2));
        Quat pitch = new(0, Math.Sin(r.ThetaRad / 2), 0, Math.Cos(r.ThetaRad / 2));
        Quat roll = new(Math.Sin(r.BankRad / 2), 0, 0, Math.Cos(r.BankRad / 2));
        return Quat.Multiply(yaw, Quat.Multiply(pitch, roll));
    }

    /// <summary>The glide speed an unpowered type is rigged and started at: best glide from the model's polar — except a
    /// WEIGHT-SHIFT wing, whose hands-off speed is set by where the pilot hangs (its authored spawn speed): with the bar
    /// left alone at best glide the hang glider here is divergent in pitch (it tucks after ~20 s), and the real pilot
    /// flies best glide by holding the bar in, not by trimming for it.</summary>
    public static double GlideRigTas(AircraftConfig c, double altitudeM = RigAltitudeM) =>
        c.WeightShift is not null && c.SpawnIasMs > 1 ? c.SpawnIasMs : BestGlideTas(c, altitudeM);

    /// <summary>That glide speed at this height: the rig speed at the same equivalent airspeed (best L/D is a fixed EAS),
    /// or a fresh search on a type with no rig speed recorded.</summary>
    public static double RigGlideTasAt(AircraftConfig c, double altitudeM) =>
        c.Rig.TasMs > 0 && c.Propulsion is null
            ? c.Rig.TasMs * Math.Sqrt(Atmosphere.DensityAtAltitude(c.Rig.AltitudeM) / Atmosphere.DensityAtAltitude(altitudeM))
            : GlideRigTas(c, altitudeM);

    private static Result SolvePoweredLevel(AircraftConfig c, double throttle, double altitudeM, bool solveRig)
    {
        var p = new Problem(c, altitudeM, throttle, glide: false, fixedV: 0.0);
        if (!solveRig && c.Rig.TasMs > 0)
        {
            // A rigged type: start from its rig condition (same equivalent airspeed at this height) and skip the scan.
            double eas = c.Rig.TasMs * Math.Sqrt(Atmosphere.DensityAtAltitude(c.Rig.AltitudeM) / Atmosphere.DensityAtAltitude(altitudeM));
            Result quick = p.Newton(new[] { eas, c.Rig.AlphaRad, c.Rig.ElevatorTrimRad, c.Rig.AileronRad, c.Rig.FinOffsetRad, c.Rig.BankRad }, false);
            if (quick.Converged) return quick;
        }
        // The power curve has two level speeds; the rig wants the FRONT side. Scan down from fast to find the fastest
        // speed where thrust meets drag, then hand that to Newton.
        double vs = c.SpawnIasMs > 1 ? c.SpawnIasMs : 22.0;
        double vHi = Math.Min(4.0 * vs + 60.0, 340.0), vLo = 0.6 * vs;
        double prevV = double.NaN, prevFx = double.NaN; double[] prevAe = { 0.05, 0.0 };
        double v0 = vs; double[] ae0 = { 0.05, 0.0 };
        for (int i = 0; i <= 120; i++)
        {
            double v = vHi - (vHi - vLo) * i / 120.0;
            (bool ok, double fx, double[] ae) = p.LongitudinalAt(v, prevAe);
            if (!ok) { prevV = double.NaN; continue; }
            if (!double.IsNaN(prevV) && prevFx < 0 && fx >= 0)
            {
                double f = prevFx / (prevFx - fx);
                v0 = prevV + (v - prevV) * f; ae0 = ae; break;
            }
            prevV = v; prevFx = fx; prevAe = ae;
        }
        double[] x = { v0, ae0[0], ae0[1], c.Rig.AileronRad, c.Rig.FinOffsetRad, 0.0 };
        return p.Newton(x, solveRig && CanRig(c));
    }

    /// <summary>Steady forces and moments about the CG (body axes, no gravity) at this air-relative body velocity with
    /// these deflections — what <see cref="Aircraft.StepWithDeflectionTargets"/> assembles in still air, clear of the
    /// ground, gear up, with no rates (so the stall and downwash lags sit at their steady values).</summary>
    /// <summary>The rig solves on many threads; each keeps its own swirl buffer so nothing allocates per evaluation.</summary>
    [ThreadStatic] private static BladeElement.SwirlProfile? _swirlScratch;

    public static (Vec3 Force, Vec3 Moment) SteadyForces(AircraftConfig c, IReadOnlyDictionary<string, AirfoilTable> tables,
        double throttle, Vec3 vBody, double rho, ControlDeflections ctl)
    {
        // THE RIG MUST BE SOLVED WITH THE PROPELLER THE SIM FLIES, and that starts here, before anything is asked of
        // the air: PropDrive.Running carries no shaft rpm, so the blade-element path never engaged and every fitted
        // type was rigged against the eta*P/V model it no longer uses. It is the same shape of mistake as solving
        // against the full-strength slipstream yaw while the sim applied the traced share, which cost sixty rig tests
        // earlier the same night — and it bit twice, because the propeller's FORCES were put on the solved drive while
        // its WASH over the tail was left on the old one.
        PropModel.PropDrive drive = PropModel.SteadyDrive(c.Propulsion, throttle, vBody.X, rho);
        bool solved = c.Propulsion is not null && PropModel.IsSolved(c.Propulsion, drive);
        BladeElement.SwirlProfile prof = _swirlScratch ??= new BladeElement.SwirlProfile();
        (double du, double r, double swirl) = Aircraft.TailSlipstream(c, throttle, vBody, Vec3.Zero, rho, drive, false, prof);
        (Vec3 f, Vec3 m) = AeroModel.Compute(c, tables, vBody, Vec3.Zero, Vec3.Zero, rho, ctl, -1.0, null, du, r,
            null, null, null, double.PositiveInfinity, swirl, default, solved, prof);
        if (c.WeightShift is not null) m += c.WeightShift.SwingMoment(ctl.ElevatorRad, ctl.AileronRad, f, c.Mass.MassKg);
        // The spiral slipstream's yaw on the fin takes the share of the wash that reaches the tail, and so must the
        // SOLVE: the sim applies PropJet.TailShare here, and a rig solved against the full-strength yaw instead leaves
        // whatever the difference is uncancelled in flight. In cruise that difference is about 8 % (the tube is swept a
        // little off the tail by the trim alpha itself), which is a tenth of a degree of fin offset and showed up as
        // every powered type wandering ~25 deg of heading in a minute hands off. The old scalar hid this by returning
        // exactly 1 whenever the wing was attached, so solve and sim agreed by accident.
        double share = PropJet.TailShare(c, vBody, Vec3.Zero, c.Mass.CgVec(), du, r, swirl);
        (Vec3 pf, Vec3 pm) = Aircraft.PropulsionForces(c, throttle, vBody, Vec3.Zero, rho, false, default, null,
            drive, share);
        return (f + pf, m + pm);
    }

    private sealed class Problem
    {
        private readonly AircraftConfig _c;
        private readonly Dictionary<string, AirfoilTable> _tables;
        private readonly double _rho, _w, _thr, _fixedV, _alt;
        private readonly bool _glide;

        public Problem(AircraftConfig c, double altitudeM, double throttle, bool glide, double fixedV)
        {
            _c = c; _tables = Aircraft.BuildAirfoilTables(c);
            _rho = Atmosphere.DensityAtAltitude(altitudeM); _alt = altitudeM;
            _w = c.Mass.MassKg * Atmosphere.GravityMs2;
            _thr = throttle; _glide = glide; _fixedV = fixedV;
        }

        // x = [V (level) or θ (glide), α, δe, δa_rig, δfin, φ]
        private (double v, double alpha, double theta, double phi) Kinematics(double[] x)
        {
            double alpha = x[1], phi = x[5];
            if (_glide) return (_fixedV, alpha, x[0], phi);
            // Level with zero sideslip: the velocity (u, 0, w) has no vertical component → tan θ = tan α · cos φ.
            return (x[0], alpha, Math.Atan(Math.Tan(alpha) * Math.Cos(phi)), phi);
        }

        public double[] Residual(double[] x)
        {
            (double v, double alpha, double theta, double phi) = Kinematics(x);
            Vec3 vb = new(v * Math.Cos(alpha), 0, v * Math.Sin(alpha));
            RigConfig saved = _c.Rig;
            _c.Rig = new RigConfig { AileronRad = x[3], FinOffsetRad = x[4] };
            try
            {
                (Vec3 f, Vec3 m) = SteadyForces(_c, _tables, _thr, vb, _rho, new ControlDeflections(0, x[2], 0, 0));
                Vec3 g = new(-_w * Math.Sin(theta), _w * Math.Sin(phi) * Math.Cos(theta), _w * Math.Cos(phi) * Math.Cos(theta));
                f += g;
                return new[] { f.X / _w, f.Y / _w, f.Z / _w, m.X / _w, m.Y / _w, m.Z / _w };
            }
            finally { _c.Rig = saved; }
        }

        /// <summary>At a fixed speed, wings level, rig held: the α and elevator that zero Fz and M in level flight, and
        /// the Fx left over (thrust − drag along the body, + = would accelerate).</summary>
        public (bool ok, double fx, double[] ae) LongitudinalAt(double v, double[] guess)
        {
            double[] x = { v, guess[0], guess[1], _c.Rig.AileronRad, _c.Rig.FinOffsetRad, 0.0 };
            int[] unk = { 1, 2 }, eq = { 2, 4 };
            bool ok = Iterate(x, unk, eq, 60, 1e-10);
            return (ok, Residual(x)[0], new[] { x[1], x[2] });
        }

        public Result Newton(double[] x, bool solveRig)
        {
            // Rig held: speed (or path), α, elevator and the bank that carries the fin's side force — the rolling and
            // yawing moments are whatever the fixed rig leaves at this condition (zero at the rig condition itself).
            int[] unk = solveRig ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 0, 1, 2, 5 };
            int[] eq = solveRig ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 0, 1, 2, 4 };
            int iters = 0;
            bool ok = Iterate(x, unk, eq, 200, 1e-11, n => iters = n);
            double[] r = Residual(x);
            (double v, double alpha, double theta, double phi) = Kinematics(x);
            double gamma = Math.Asin(Math.Clamp(Math.Sin(theta) * Math.Cos(alpha) - Math.Cos(theta) * Math.Cos(phi) * Math.Sin(alpha), -1, 1));
            return new Result
            {
                Converged = ok, Iterations = iters, TasMs = v, AlphaRad = alpha, ThetaRad = theta, BankRad = phi,
                ElevatorRad = x[2], AileronRigRad = x[3], FinOffsetRad = x[4], Throttle = _thr, AltitudeM = _alt,
                Residual = r, LateralSolved = solveRig, GammaRad = gamma,
            };
        }

        private bool Iterate(double[] x, int[] unk, int[] eq, int maxIter, double tol, Action<int>? report = null)
        {
            int n = unk.Length;
            double eLim = _c.Controls.Elevator.MaxTravelRad, aLim = Math.Max(0.05, _c.Controls.Aileron.MaxTravelRad);
            for (int it = 0; it < maxIter; it++)
            {
                double[] full = Residual(x);
                double[] f = new double[n];
                for (int i = 0; i < n; i++) f[i] = full[eq[i]];
                if (Norm(f) < tol) { report?.Invoke(it); return true; }
                double[,] j = new double[n, n];
                for (int col = 0; col < n; col++)
                {
                    double h = unk[col] == 0 && !_glide ? 1e-4 : 1e-6;
                    double[] xp = (double[])x.Clone(); xp[unk[col]] += h;
                    double[] fp = Residual(xp);
                    for (int row = 0; row < n; row++) j[row, col] = (fp[eq[row]] - f[row]) / h;
                }
                double[] d = SolveLinear(j, f);
                for (int k = 0; k < n; k++)
                {
                    int u = unk[k];
                    double maxStep = u == 0 && !_glide ? 5.0 : u == 2 ? 0.5 * eLim : 0.05;
                    x[u] -= Math.Clamp(d[k], -maxStep, maxStep);
                }
                x[1] = Math.Clamp(x[1], -0.35, 0.45);
                x[2] = Math.Clamp(x[2], -eLim, eLim);
                x[3] = Math.Clamp(x[3], -aLim, aLim);
                x[4] = Math.Clamp(x[4], -0.2, 0.2);
                x[5] = Math.Clamp(x[5], -0.3, 0.3);
                if (!_glide) x[0] = Math.Max(5.0, x[0]); else x[0] = Math.Clamp(x[0], -0.8, 0.8);
            }
            report?.Invoke(maxIter);
            double[] last = Residual(x);
            double s = 0; foreach (int e in eq) s += last[e] * last[e];
            return Math.Sqrt(s) < tol * 100;
        }
    }

    private static double Norm(double[] v) { double s = 0; foreach (double c in v) s += c * c; return Math.Sqrt(s); }

    /// <summary>a·x = b, Gaussian elimination with partial pivoting.</summary>
    private static double[] SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        double[,] m = (double[,])a.Clone(); double[] r = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int row = col + 1; row < n; row++) if (Math.Abs(m[row, col]) > Math.Abs(m[piv, col])) piv = row;
            if (piv != col)
            {
                for (int k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
                (r[col], r[piv]) = (r[piv], r[col]);
            }
            double dg = Math.Abs(m[col, col]) < 1e-14 ? 1e-14 : m[col, col];
            for (int row = col + 1; row < n; row++)
            {
                double fct = m[row, col] / dg;
                for (int k = col; k < n; k++) m[row, k] -= fct * m[col, k];
                r[row] -= fct * r[col];
            }
        }
        double[] x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double s = r[row];
            for (int k = row + 1; k < n; k++) s -= m[row, k] * x[k];
            x[row] = s / (Math.Abs(m[row, row]) < 1e-14 ? 1e-14 : m[row, row]);
        }
        return x;
    }
}
