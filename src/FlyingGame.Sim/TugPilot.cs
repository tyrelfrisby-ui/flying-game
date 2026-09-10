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
    public double ThresholdX, RunwayY, RunwayElevM, RunwayLengthM = 1500;
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
    private double _overAngleT, _flareT;
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
                if (agl < 5) { Phase = Phases.Flare; _flareT = 0; Status = "flare"; }
                break;
            case Phases.Flare:
                _flareT += dt;
                if (onGround && _flareT > 0.5) { Phase = Phases.Rollout; Status = "rollout"; }
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
        double bankCmd = _airborne && Phase != Phases.Rollout ? Math.Clamp(hErr * 1.5, -maxBank, maxBank) : 0.0;
        if (Phase is Phases.Flare or Phases.Rollout) bankCmd = 0;
        if (Phase == Phases.Final && agl < 15) bankCmd = Math.Clamp(bankCmd, -8 * Math.PI / 180, 8 * Math.PI / 180); // no big banks near the ground
        double ail = Math.Clamp((bankCmd - roll) * 2.0 - s.Rates.X * 0.6, -1, 1);
        double rud = onGround ? Math.Clamp(hErr * 3.0 - s.Rates.Z * 0.6, -1, 1) : Math.Clamp(bankCmd * 0.25 + hErr * 0.3 - s.Rates.Z * 0.2, -1, 1);

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
            // Closed-loop flare: check the sink to ~0.6 m/s and hold it off (an open-loop stick ramp arrived at
            // 4+ m/s in the heavier Pawnee and broke its legs — a leg tears off past 4 m/s).
            double sinkErr = sink - 0.6;
            elev = Math.Clamp(-0.22 - 0.30 * sinkErr - 0.05 * _flareT - s.Rates.Y * 0.5, -0.75, 0.25);
            lever = 1.0;
        }
        else if (Phase is Phases.Rollout or Phases.Done)
        {
            elev = -0.35; lever = 1.0;   // taildragger rollout: stick back, tail down
        }
        else
        {
            // Airborne: PITCH holds airspeed, POWER holds the climb/descent rate (tow-pilot technique — the
            // excess power goes into climb, and a heavy glider on the rope can't run the speed away).
            elev = Math.Clamp(-0.08 * (v - vTarget) - s.Rates.Y * 0.5 - 0.12 * Math.Abs(bankCmd), -0.7, 0.4);
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
        tug.BrakeInput = Phase is Phases.Rollout or Phases.Done ? (v > 20 ? 0.25 : v > 10 ? 0.5 : 0.8) : 0.0;

        // ---- rope-angle overpower → tug releases (sustained 0.7 s)
        WantsRelease = false;
        if (ropeDirBody.HasValue && ropeTension > 300 && _airborne)
        {
            double up = -ropeDirBody.Value.Z; // + = glider above the tug's hook line
            bool over = up > Math.Sin(32 * Math.PI / 180) || up < -Math.Sin(22 * Math.PI / 180);
            _overAngleT = over ? _overAngleT + dt : 0;
            if (_overAngleT > 0.7) { WantsRelease = true; Status = "TUG RELEASED (rope angle)"; }
        }
        else _overAngleT = 0;

        return new ControlInputs(ail, elev, rud, lever);
    }

    private static double Dist2D(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
