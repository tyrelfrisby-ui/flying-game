using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Autopilot for the aerotow tug (owner spec): takes off, climbs straight ahead, then flies a left-hand
/// rectangular pattern at pattern altitude until the glider releases; then returns, lands with the rope
/// still attached and rolls to a stop. If the glider gets so far above or below that the rope angle
/// overpowers the tug's pitch authority, the tug pilot releases the rope.
/// Sim frame: x north (runway heading), y east, z DOWN. Runway along +x from the threshold.
/// </summary>
public sealed class TugPilot
{
    public enum Phases { GroundRoll, Climb, Pattern, Return, Final, Flare, Rollout, Done }

    public Phases Phase { get; private set; } = Phases.GroundRoll;
    public string Status { get; private set; } = "rolling";
    public bool WantsRelease { get; private set; }

    // Runway / airport. The pilot works in a RUNWAY FRAME: x along the runway heading from Origin, y to its right;
    // ThresholdX/RunwayY are in that frame (both 0 when Origin is the threshold). Heading 0 / origin 0 = world frame.
    public double ThresholdX, RunwayY, RunwayElevM, RunwayLengthM = FlyingGame.Core.WorldTerrain.RunwayLengthM;
    public double OriginX, OriginY, RunwayHeadingRad;

    /// <summary>Test hook for <see cref="Localize"/>.</summary>
    public RigidBodyState LocalizeForTest(RigidBodyState w) => Localize(w);

    /// <summary>The tug's state seen in the runway frame (position and yaw rotated; body velocity/rates unchanged).</summary>
    private RigidBodyState Localize(RigidBodyState w)
    {
        if (RunwayHeadingRad == 0 && OriginX == 0 && OriginY == 0) return w;
        double c = Math.Cos(RunwayHeadingRad), sn = Math.Sin(RunwayHeadingRad);
        double dx = w.Position.X - OriginX, dy = w.Position.Y - OriginY;
        var pos = new Vec3(dx * c + dy * sn, -dx * sn + dy * c, w.Position.Z);
        double hh = -RunwayHeadingRad / 2;
        Quat att = Quat.Multiply(new Quat(0, 0, Math.Sin(hh), Math.Cos(hh)), w.Attitude);
        return new RigidBodyState(pos, att, w.Velocity, w.Rates);
    }
    // Tow parameters.
    public double TowSpeedMs = 30.0, ClimbRateMs = 2.5, PatternAglM = 300.0, MaxBankDeg = 20.0;
    /// <summary>Approach speed; 0 = derive 1.3·Vs (flaps down) from the tug's weight and wing area.</summary>
    public double ApproachSpeedMs = 0.0;
    /// <summary>0..1 cap on power during the ground roll (the glider pilot advances the tug's throttle).</summary>
    public double PowerLimit01 = 1.0;
    public bool GliderReleased;

    private int _wp;
    private double _overAngleT, _flareT, _touchdownV, _stanceRad;
    private bool _airborne;

    // Left-hand rectangle: upwind end, crosswind end, downwind end, base end (pattern side = -y, west).
    private Vec3 Wp(int i) => i switch
    {
        0 => new Vec3(ThresholdX + 3200, RunwayY, 0),
        1 => new Vec3(ThresholdX + 3200, RunwayY - 1500, 0),
        2 => new Vec3(ThresholdX - 1800, RunwayY - 1500, 0),
        _ => new Vec3(ThresholdX - 1800, RunwayY, 0),
    };

    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    /// <summary>Flare profile: begins with the mains 20 ft up; sink target = FlareGainPerSec × (mains height − 6 in).</summary>
    public const double FlareStartAglM = 6.1, HoldOffAglM = 0.15, FlareGainPerSec = 0.16;
    public double ElevatorScale = 0.55, RudderScale = 0.6;
    private double _pitchTrim;
    private bool _scaled;

    /// <summary>Height of the lowest main wheel and of the tailwheel above the ground under them (world frame).</summary>
    private static (double mains, double tail) WheelHeights(Aircraft tug)
    {
        RigidBodyState w = tug.State; Vec3 cg = tug.Config.Mass.CgVec();
        double mains = double.MaxValue, tail = double.MaxValue;
        foreach (var g in tug.Config.Gear)
        {
            Vec3 p = w.Position + w.Attitude.Rotate(g.PosVec() - cg);
            double h = -p.Z - WorldTerrain.WheelGroundHeightAt(p.X, p.Y);
            if (g.IsTailwheel) tail = Math.Min(tail, h); else mains = Math.Min(mains, h);
        }
        if (mains == double.MaxValue) mains = -w.Position.Z;
        if (tail == double.MaxValue) tail = mains;
        return (mains, tail);
    }

    /// <param name="ropeDirBody">unit vector from the tug's hook toward the glider hook, in the TUG body frame (null if no rope)</param>
    public ControlInputs Update(Aircraft tug, double dt, double ropeTension, Vec3? ropeDirBody)
    {
        RigidBodyState s = Localize(tug.State);
        Quat q = s.Attitude;
        double roll = Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        double psi = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        double v = s.Velocity.Length;
        double agl = -s.Position.Z - WorldTerrain.GroundHeightAt(tug.State.Position.X, tug.State.Position.Y);
        Vec3 vWorld = q.Rotate(s.Velocity);
        double sink = vWorld.Z; // + down
        bool onGround = LandingGear.AnyMainWheelOnGround(tug.Config, s);
        (double mainsAgl, double tailAgl) = WheelHeights(tug);
        if (_stanceRad == 0)
        {
            // Three-point attitude from the gear geometry (the pull in the hold-off stops there).
            var mains = tug.Config.Gear.FindAll(g => !g.IsTailwheel && g.Pos[2] > 0 && Math.Abs(g.Pos[1]) < 3);
            var tws = tug.Config.Gear.FindAll(g => g.IsTailwheel);
            _stanceRad = mains.Count > 0 && tws.Count > 0 ? Math.Atan((mains[0].Pos[2] - tws[0].Pos[2]) / (mains[0].Pos[0] - tws[0].Pos[0])) : 0.15;
        }
        if (!_scaled)
        {
            // Control-power scaling from the type's own trim slope (elevator per degree of angle of attack between 26 and
            // 30 m/s): the laws were tuned at ~1.8°/°; a tail with twice the power gets half the stick.
            _scaled = true;
            try
            {
                TrimSolver.Result a = TrimSolver.SolveGliderTrim(tug.Config, 26, 300), b = TrimSolver.SolveGliderTrim(tug.Config, 30, 300);
                if (a.Converged && b.Converged && Math.Abs(a.AlphaRad - b.AlphaRad) > 1e-3)
                {
                    double slope = Math.Abs((a.ElevatorRad - b.ElevatorRad) / (a.AlphaRad - b.AlphaRad));
                    ElevatorScale = Math.Clamp(slope / 1.8, 0.3, 1.0);
                }

            }
            catch (Exception) { }
        }
        if (ApproachSpeedMs <= 0)
        {
            double area = 0; foreach (var sf in tug.Config.Surfaces) if (sf.Id == "wing") foreach (var st in sf.Strips) area += st.Area;
            double vs = Math.Sqrt(2 * tug.Config.Mass.MassKg * 9.81 / (1.225 * Math.Max(1, area) * 1.9)); // flaps-down CLmax ~1.9
            ApproachSpeedMs = Math.Max(22.0, 1.3 * vs);
        }
        // Flaps: down for the approach and landing, up otherwise (the tow is flown clean).
        tug.FlapFraction = Phase is Phases.Final or Phases.Flare or Phases.Rollout or Phases.Done ? 1.0 : 0.0;

        // ---- phase transitions
        switch (Phase)
        {
            case Phases.GroundRoll:
                if (agl > 4 && v > 24) { Phase = Phases.Climb; _airborne = true; Status = "climbing"; }
                break;
            case Phases.Climb:
                if (agl > 120) { Phase = Phases.Pattern; _wp = s.Position.X < Wp(0).X - 400 ? 0 : 1; Status = "pattern"; }
                if (GliderReleased) { Phase = Phases.Return; _wp = 2; Status = "returning"; }
                break;
            case Phases.Pattern:
                if (Dist2D(s.Position, Wp(_wp)) < 350) _wp = (_wp + 1) % 4;
                if (GliderReleased) { Phase = Phases.Return; _wp = _wp <= 1 ? 1 : _wp; Status = "returning"; }
                break;
            case Phases.Return:
                if (Dist2D(s.Position, Wp(_wp)) < 350)
                {
                    if (_wp == 3) { Phase = Phases.Final; Status = "final"; }
                    else _wp = _wp + 1;
                }
                break;
            case Phases.Final:
                if (mainsAgl < FlareStartAglM) { Phase = Phases.Flare; _flareT = 0; Status = "flare"; }
                break;
            case Phases.Flare:
                _flareT += dt;
                // Hold off at six inches until the TAIL touches (a three-point arrival); safety exits: slow on the mains, or a long hold.
                if ((tailAgl < 0.03 && onGround) || (onGround && v < 0.85 * ApproachSpeedMs / 1.3) || _flareT > 30) { Phase = Phases.Rollout; Status = "rollout"; }
                break;
            case Phases.Rollout:
                if (v < 0.5) { Phase = Phases.Done; Status = "stopped"; }
                break;
        }

        // ---- lateral: heading command → bank → aileron (+rudder for coordination / ground steering)
        // Runway heading with a centreline correction on the ground roll and the initial climb (P-factor and
        // the glider's pull drift a taildragger left otherwise).
        double headingCmd = Math.Clamp(-(s.Position.Y - RunwayY) * 0.012, -12 * Math.PI / 180, 12 * Math.PI / 180);
        if (Phase is Phases.Pattern or Phases.Return)
        {
            Vec3 w = Wp(_wp);
            headingCmd = Math.Atan2(w.Y - s.Position.Y, w.X - s.Position.X);
        }
        else if (Phase is Phases.Final or Phases.Flare or Phases.Rollout)
        {
            // Runway heading with a gentle centreline correction (an aim point flips as you pass it).
            // Track the extended centreline: offset → intercept heading, with a lead on the lateral rate.
            double vy = q.Rotate(s.Velocity).Y;
            headingCmd = Math.Clamp(-((s.Position.Y - RunwayY) * 0.004 + vy * 0.05), -15 * Math.PI / 180, 15 * Math.PI / 180);
        }
        double hErr = Wrap(headingCmd - psi);
        double maxBank = MaxBankDeg * Math.PI / 180;
        // Heading → bank, with yaw-rate damping (the Cub tug hunted ±30° of heading on final once its CG sat where a real
        // Cub's does).
        double hGain = Phase is Phases.Final or Phases.Flare ? 1.0 : 1.5;   // gentler on final: the Cub tug swung ±20° of bank there
        double bankCmd = _airborne && Phase != Phases.Rollout ? Math.Clamp(hErr * hGain - s.Rates.Z * 1.2, -maxBank, maxBank) : 0.0;
        if (Phase is Phases.Flare or Phases.Rollout) bankCmd = 0;
        if (Phase == Phases.Final && agl < 15) bankCmd = Math.Clamp(bankCmd, -8 * Math.PI / 180, 8 * Math.PI / 180); // no big banks near the ground
        double ail = Math.Clamp((bankCmd - roll) * 2.0 - s.Rates.X * 1.0, -1, 1);
        // Airborne: rudder WITH the aileron (owner technique; 2026-10-06: the ailerons now carry their real induced drag —
        // the per-piece aspect ratio had left them almost none — so a banked tug with a quarter-measure of rudder yawed
        // against the turn, banked 32° without turning, drifted 270 m off the centreline and crashed) + the ball (sideslip).
        double beta = v > 5 ? Math.Asin(Math.Clamp(s.Velocity.Y / v, -1, 1)) : 0.0;
        double rud = onGround ? Math.Clamp(hErr * 3.0 - s.Rates.Z * 0.6, -1, 1) : Math.Clamp(ail * 0.5 + bankCmd * 0.25 + hErr * 0.3 - s.Rates.Z * 0.2 + beta * 3.0, -1, 1);

        // ---- longitudinal: sink target → elevator; speed → throttle
        double sinkTarget = 0, vTarget = TowSpeedMs, lever;
        switch (Phase)
        {
            case Phases.Climb: sinkTarget = -ClimbRateMs; break;
            case Phases.Pattern: sinkTarget = Math.Clamp((agl - PatternAglM) * 0.03, -ClimbRateMs, 2.0); break;
            case Phases.Return:
                {
                    // Descend toward 120 m AGL by the base end.
                    sinkTarget = Math.Clamp((agl - 120) * 0.02, -1.0, 2.5); vTarget = TowSpeedMs; break;
                }
            case Phases.Final:
                {
                    double dist = Math.Max(50, (ThresholdX + 150) - s.Position.X);
                    double slopeAlt = dist * Math.Tan(4.0 * Math.PI / 180);
                    sinkTarget = Math.Clamp(v * Math.Tan(4.0 * Math.PI / 180) + (agl - slopeAlt) * 0.08, 0.3, 4.0);
                    vTarget = ApproachSpeedMs; break;
                }
        }
        double elev;
        double alpha = v > 5 ? Math.Atan2(s.Velocity.Z, s.Velocity.X) : 0.0;
        if (Phase == Phases.GroundRoll)
        {
            // Closed-loop pitch attitude: hold the three-point stance until rotation speed, then rotate to a
            // modest climb attitude. (An open-loop stick pull over-rotated a light-tailed tug to 25° and
            // stalled the wing off a wingtip.)
            double stance = pitch;              // whatever the gear stance gives while slow
            double pitchCmd = v < 0.8 * TowSpeedMs ? Math.Min(stance, 9 * Math.PI / 180) : 10 * Math.PI / 180;
            elev = Math.Clamp(-(pitchCmd - pitch) * 5.0 + s.Rates.Y * 0.6, -0.6, 0.4);
            if (v < 10) elev = 0.0;
            lever = 1.0 - 2.0 * Math.Clamp(PowerLimit01, 0, 1);
        }
        else if (Phase == Phases.Flare)
        {
            // Owner 2026-09-12: the pilot flies HEIGHT and SINK, not stick position. From 20 ft the target sink shrinks
            // with the mains' height (an exponential flare) down to six inches, then the mains are held at six inches
            // — pulling progressively as the speed bleeds — until the tail touches. Power at idle throughout.
            double holdErr = mainsAgl - HoldOffAglM;
            double flareSink = Math.Clamp(FlareGainPerSec * holdErr, 0.0, 1.6);
            // Near the stall the wing cannot hold it off any longer: settle at half a metre a second rather than drop.
            double vs = ApproachSpeedMs / 1.3;
            if (v < 1.04 * vs) flareSink = Math.Max(flareSink, 0.5);
            double sinkErr = sink - flareSink;
            elev = Math.Clamp(-0.20 - 0.35 * sinkErr - 0.08 * Math.Min(0, holdErr) * 4 - s.Rates.Y * 0.5, -0.8, 0.3);
            // At the three-point attitude the pull stops INCREASING: hold that attitude closed-loop (the tail is about to
            // touch) — letting the stick go there pitched the nose-heavy Pawnee down onto its prop. Past the stall
            // angle, ease off a little.
            if (pitch > _stanceRad - 0.01) elev = Math.Max(elev, Math.Clamp(-(_stanceRad - pitch) * 4.0 + s.Rates.Y * 0.6, -0.8, 0.3));   // Max = no MORE pull than the attitude hold wants
            if (alpha > 16 * Math.PI / 180) elev = Math.Max(elev, -0.25);
            lever = 1.0;
        }
        else if (Phase is Phases.Rollout or Phases.Done)
        {
            // Taildragger rollout: the stick comes back progressively as the speed bleeds — full aft at touchdown speed
            // ballooned the Pawnee back into the air and it came down on the mains at 6 m/s. Full back once slow: tail
            // pinned, and the nose stays up under the brakes.
            // Hold the three-point attitude closed-loop (tail ON the ground): the pull grows by itself as the speed
            // bleeds; an open schedule let the tail rise at 27 m/s and the Pawnee skipped onto its mains at 4 m/s.
            if (_touchdownV <= 0) _touchdownV = Math.Max(v, 10);
            double bled = Math.Clamp((_touchdownV - v) / Math.Max(6.0, _touchdownV - 9.0), 0.0, 1.0);
            double hold = Math.Clamp(-(_stanceRad + 0.03 - pitch) * 5.0 + s.Rates.Y * 0.6, -0.8, 0.2);
            elev = Math.Min(hold, -0.15 - 0.55 * bled);   // whichever pulls more: the attitude hold or the slow-speed full-back
            lever = 1.0;
        }
        else
        {
            // Airborne: PITCH holds airspeed, POWER holds the climb/descent rate (tow-pilot technique — the
            // excess power goes into climb, and a heavy glider on the rope can't run the speed away).
            // Speed by pitch: a gentler gain with more pitch-rate damping (with the real tail lift curve the old gain set
            // up a 5 s pitch/speed cycle on the Cub tug that slammed the rope to its weak link).
            // ... and TRIMS (a slow integral on the speed error, the pilot's trim wheel, 2026-10-06): proportional-only, the
            // law settled wherever the type's neutral-elevator speed sat — with real pitch stability and full flap on final the
            // Pawnee came down the approach 10 m/s fast and landed a kilometre short.
            _pitchTrim = Math.Clamp(_pitchTrim - 0.006 * (v - vTarget) * dt, -0.4, 0.4);
            elev = Math.Clamp(_pitchTrim - 0.04 * (v - vTarget) - s.Rates.Y * 0.9 - 0.12 * Math.Abs(bankCmd), -0.7, 0.4);
            double thr = Math.Clamp((Phase == Phases.Final ? 0.3 : 0.6) + 0.3 * (sink - sinkTarget), 0.0, 1.0);
            if (Phase == Phases.Climb)
            {
                // Power for the climb rate (a 235 hp tug at full power out-climbs the glider on the rope).
                thr = Math.Clamp(0.75 + 0.3 * (sink - sinkTarget), 0.45, 1.0);
                // Pitch-attitude cap on the initial climb: bleed excess liftoff speed gently, never zoom above the
                // glider (that is what pulls the rope over the tug's tail).
                double cap = 12 * Math.PI / 180;
                if (pitch > cap) elev = Math.Max(elev, (pitch - cap) * 4.0);
            }
            lever = 1.0 - 2.0 * thr;
        }
        // Angle-of-attack limiter: never let a control law hold the wing past ~12° (stall break rolls it off).
        if (v > 15 && alpha > 12 * Math.PI / 180) elev = Math.Max(elev, Math.Min(0.6, (alpha - 12 * Math.PI / 180) * 8.0));

        // Progressive braking on rollout (full brakes at 30 m/s noses a taildragger over): light until slow.
        // Gentle braking with the stick back: with the mains at a real 17° ahead of the CG, hard braking noses a
        // taildragger over (both tugs went on their backs at 0.8).
        // No brakes while the wing still flies (touchdown is ~1.3 Vs): braking at 28 m/s pitched the tail up, the Pawnee
        // skipped and came down on the mains at 5.7 m/s and broke both legs. Stick back and let the speed bleed first.
        tug.BrakeInput = Phase is Phases.Rollout or Phases.Done ? (v > 15 ? 0.0 : v > 9 ? 0.15 : 0.35) : 0.0;
        // Differential braking on the rollout and the slow part of the ground roll: with a castoring tailwheel the
        // rudder alone cannot hold the centreline below flying speed (a real tailwheel pilot steers with the brakes).
        tug.BrakeBias = Phase is Phases.Rollout or Phases.Done ? Math.Clamp(hErr * 2.5, -1.0, 1.0)
            : Phase == Phases.GroundRoll && v < 12 && v > 0.5 ? Math.Clamp(hErr * 2.0, -0.6, 0.6) : 0.0;
        if (Phase == Phases.GroundRoll && v < 12 && v > 0.5 && Math.Abs(hErr) > 0.02) tug.BrakeInput = Math.Max(tug.BrakeInput, 0.12);   // a touch of brake to steer with

        // ---- rope-angle overpower → tug releases (sustained 0.7 s)
        WantsRelease = false;
        if (ropeDirBody.HasValue && ropeTension > 300 && _airborne)
        {
            // Rope angle against the HORIZON (a glider climbing high pitches the tug nose-down, which hid the angle in the
            // body frame), plus a hard case: being pulled nose-down below −8° under load is a kite — release.
            Vec3 ropeWorld = tug.State.Attitude.Rotate(ropeDirBody.Value);
            double up = -ropeWorld.Z; // + = glider above the tug
            bool over = up > Math.Sin(32 * Math.PI / 180) || up < -Math.Sin(22 * Math.PI / 180) || (pitch < -8 * Math.PI / 180 && ropeTension > 1200);
            _overAngleT = over ? _overAngleT + dt : 0;
            if (_overAngleT > 0.7) { WantsRelease = true; Status = "TUG RELEASED (rope angle)"; }
        }
        else _overAngleT = 0;

        // Elevator authority scale: the tails gained ~1.9× pitch power (real tail lift curve + 30° travel, 2026-09-15); every
        // law above was tuned before that, so the stick travel they ask for is scaled back to keep the same response.
        elev *= ElevatorScale;
        rud *= RudderScale;   // the fin table gained ~1.6× too
        return new ControlInputs(ail, elev, rud, lever);
    }

    /// <summary>|Cmδ| (per rad, on the wing's area and mean chord): pitching moment from ±2° of elevator at α 3°, 40 m/s.</summary>
    public static double ElevatorPowerCmDelta(FlyingGame.Core.DataContracts.AircraftConfig c)
    {
        var tables = Aircraft.BuildAirfoilTables(c);
        double v = 40, rho = 1.225, S = 0, cS = 0;
        foreach (var sf in c.Surfaces) if (sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) foreach (var st in sf.Strips) { S += st.Area; cS += st.Area * st.Chord; }
        if (S <= 0) return 0;
        double mac = cS / S, q = 0.5 * rho * v * v, a = 3 * Math.PI / 180;
        double M(double eDeg) => FlyingGame.Core.Aero.AeroModel.Compute(c, tables, new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a)), Vec3.Zero, Vec3.Zero, rho,
            new FlyingGame.Core.Aero.ControlDeflections(0, eDeg * Math.PI / 180, 0, 0)).Moment.Y;
        return Math.Abs((M(2) - M(-2)) / (4 * Math.PI / 180) / (q * S * mac));
    }

    private static double Dist2D(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
