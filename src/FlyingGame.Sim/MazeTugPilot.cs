using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// The tow pilot inside the mountain (owner 2026-10-16): a glider that lands on the strip in the tunnel is
/// launched by a tug which then flies the maze, and the GLIDER PILOT picks the forks through the rope.
///
/// A rope pulling the tug's tail to the left yaws its nose to the right, so a tow pilot being dragged that way
/// would go right — which means the glider pilot chooses a branch by sliding to the OPPOSITE side of the tow.
/// This reads <see cref="RopeLateralBody"/> (the rope's direction in the tug's own body frame, negative when
/// the glider is out to the left), averages it over <see cref="RopeWindowS"/> so a wobble is not a decision,
/// and hands the answer to <see cref="WaterfallTunnel.Route.ChooseByRope"/>. A centred glider gets the safe
/// branch, and the slit is refused however hard the rope asks, because no tow pair can fly a knife edge.
///
/// The flying is the same shape as <see cref="TugPilot"/>'s airborne law — pitch holds the speed, power holds
/// the climb, heading commands bank — but the heading comes from a point up the tunnel rather than a circuit,
/// and the height is held above the FLOOR OF THE BORE rather than above sea level, because the bore descends
/// all the way round.
/// </summary>
public sealed class MazeTugPilot
{
    public const double RopeWindowS = 1.5;

    /// <summary>Tow speed. Slower than the open-air tow: this is a tunnel.</summary>
    public double CruiseMs = 27.0;
    /// <summary>How high above the bore's floor the tug flies. The arch is 32 m at the centreline.</summary>
    public double HeightAboveFloorM = 12.0;
    /// <summary>How far up the tunnel the tug aims. Short, because the bends are 250 m.</summary>
    public double LeadM = 80.0;
    public double MaxBankDeg = 30.0;
    /// <summary>The glider's throttle, which is the launch signal: nothing happens below full.</summary>
    public double PowerLimit01 = 1.0;

    /// <summary>The rope's lateral direction in the tug's body frame — the glider pilot's only control over
    /// where the tow goes. Negative = the glider is out to the left, pulling the tail left.</summary>
    public double RopeLateralBody { get; set; }

    public int LegIndex { get; private set; }
    public bool Airborne { get; private set; }
    public bool Done { get; private set; }
    public string Status { get; private set; } = "waiting";
    /// <summary>The branches taken, in order — for the HUD, and for a test to read back.</summary>
    public System.Collections.Generic.List<string> Took { get; } = new();

    private readonly WaterfallTunnel.Leg[] _legs;
    private double _ropeAvg, _pitchTrim, _stanceRad;
    private int _next = -1;

    public MazeTugPilot(WaterfallTunnel.Leg[] legs, int startLeg)
    {
        _legs = legs;
        LegIndex = startLeg;
        Took.Add(legs[startLeg].Id);
    }

    /// <summary>Where the tug is on its current leg, 0..1, and its SIGNED offset from the centreline
    /// (positive to the right of the way the leg runs).</summary>
    private double Project(Vec3 pos, out double cross)
    {
        WaterfallTunnel.Leg l = _legs[LegIndex];
        double dx = l.X1 - l.X0, dy = l.Y1 - l.Y0, len2 = dx * dx + dy * dy;
        double s = len2 > 0 ? ((pos.X - l.X0) * dx + (pos.Y - l.Y0) * dy) / len2 : 1;
        double px = l.X0 + s * dx, py = l.Y0 + s * dy;
        double len = System.Math.Sqrt(len2);
        cross = len > 0 ? ((pos.X - px) * (-dy / len) + (pos.Y - py) * (dx / len)) : 0;
        return s;
    }

    /// <summary>A point <paramref name="ahead"/> metres up the route from (leg, s), and the floor under it.</summary>
    private (double x, double y, double floor) Aim(int leg, double s, double ahead)
    {
        int at = leg;
        double along = System.Math.Clamp(s, 0, 1) * _legs[at].Length + ahead;
        for (int hop = 0; hop < 12; hop++)
        {
            WaterfallTunnel.Leg l = _legs[at];
            if (along <= l.Length || hop == 11)
            {
                double t = l.Length > 0 ? System.Math.Clamp(along / l.Length, 0, 1) : 1;
                return (l.X0 + (l.X1 - l.X0) * t, l.Y0 + (l.Y1 - l.Y0) * t, l.FloorAt(t));
            }
            along -= l.Length;
            int next = at == LegIndex && _next >= 0 ? _next : WaterfallTunnel.Route.SafeSuccessor(_legs, at);
            if (next < 0)
            {
                // The end of the route: keep aiming at the last point rather than nowhere.
                return (l.X1, l.Y1, l.Z1);
            }
            at = next;
        }
        return (_legs[at].X1, _legs[at].Y1, _legs[at].Z1);
    }

    public ControlInputs Update(Aircraft tug, double dt)
    {
        RigidBodyState st = tug.State;
        Quat q = st.Attitude;
        double roll = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        double pitch = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        double psi = System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        double v = st.Velocity.Length;
        Vec3 vWorld = q.Rotate(st.Velocity);
        double sink = vWorld.Z;                                     // + down
        double up = -st.Position.Z;
        bool onGround = LandingGear.AnyMainWheelOnGround(tug.Config, st);
        if (_stanceRad == 0)
        {
            var mains = tug.Config.Gear.FindAll(g => !g.IsTailwheel && g.Pos[2] > 0 && System.Math.Abs(g.Pos[1]) < 3);
            var tws = tug.Config.Gear.FindAll(g => g.IsTailwheel);
            _stanceRad = mains.Count > 0 && tws.Count > 0
                ? System.Math.Atan((mains[0].Pos[2] - tws[0].Pos[2]) / (mains[0].Pos[0] - tws[0].Pos[0])) : 0.15;
        }

        // ---- the rope, averaged: a wobble is not a decision.
        _ropeAvg += (RopeLateralBody - _ropeAvg) * System.Math.Clamp(dt / RopeWindowS, 0, 1);

        // ---- where we are, and what comes next.
        double s = Project(st.Position, out double cross);
        if (_next < 0 && s > 0.5) _next = WaterfallTunnel.Route.ChooseByRope(_legs, LegIndex, _ropeAvg);
        if (s >= 1.0)
        {
            int next = _next >= 0 ? _next : WaterfallTunnel.Route.ChooseByRope(_legs, LegIndex, _ropeAvg);
            if (next < 0) { Done = true; Status = "out"; }
            else
            {
                LegIndex = next;
                _next = -1;
                Took.Add(_legs[LegIndex].Id);
                s = Project(st.Position, out cross);
            }
        }

        // ---- the height to hold, from the floor a little way ahead (the bore descends all the way round).
        var (ax, ay, aFloor) = Aim(LegIndex, s, LeadM);
        double wantUp = aFloor + HeightAboveFloorM;
        _ = ax; _ = ay;

        // ---- the heading: the LEG's own heading, corrected for how far off the centreline we are.
        //
        // Pure pursuit to a point up the tunnel does not work here. On a 250 m bend a lead of L leaves a
        // steady-state error of about L²/2R — 13 m at an 80 m lead — and that put a wing tip into the rock at
        // the first bend. Tracking the centreline itself, with a rate term, holds the middle of the bore.
        WaterfallTunnel.Leg leg = _legs[LegIndex];
        double legHdg = System.Math.Atan2(leg.Y1 - leg.Y0, leg.X1 - leg.X0);
        double crossRate = vWorld.X * -(leg.Y1 - leg.Y0) / System.Math.Max(1e-9, leg.Length)
                           + vWorld.Y * (leg.X1 - leg.X0) / System.Math.Max(1e-9, leg.Length);
        // Gains an order smaller than the first attempt, which slammed the command 34° over on any transient
        // and threw the tug out of the first bend.
        double correction = System.Math.Clamp(cross * 0.006 + crossRate * 0.06, -0.26, 0.26);
        double headingCmd = legHdg - correction;
        double hErr = Wrap(headingCmd - psi);

        // The bend's own curvature, fed forward: a 250 m bend at 29 m/s needs 19° of bank, and a heading
        // error alone never commands it until the tug is already wide. The curvature comes from how much the
        // next leg's heading differs over this one's length — the chords ARE the arc.
        int ahead = _next >= 0 ? _next : WaterfallTunnel.Route.SafeSuccessor(_legs, LegIndex);
        double kappa = 0;
        if (ahead >= 0 && leg.Length > 1)
        {
            WaterfallTunnel.Leg nl = _legs[ahead];
            double nextHdg = System.Math.Atan2(nl.Y1 - nl.Y0, nl.X1 - nl.X0);
            kappa = Wrap(nextHdg - legHdg) / leg.Length;
        }
        // Airborne only once the wheels are actually off and it is flying: latching this on the ground put
        // the tug into the cruise law at walking pace, which cut the power and flew it into the floor.
        double floorHere = _legs[LegIndex].FloorAt(System.Math.Clamp(s, 0, 1));
        if (!onGround && v > 18 && up > floorHere + 2.5) Airborne = true;

        // ---- lateral: heading → bank → aileron, with yaw-rate damping, as the open-air tug does.
        double maxBank = MaxBankDeg * System.Math.PI / 180;
        double bankFF = System.Math.Atan(v * v * kappa / 9.81);
        double bankCmd = Airborne
            ? System.Math.Clamp(bankFF + hErr * 1.2 - st.Rates.Z * 1.2, -maxBank, maxBank)
            : 0;
        double ail = System.Math.Clamp((bankCmd - roll) * 2.0 - st.Rates.X * 1.0, -1, 1);
        double beta = v > 5 ? System.Math.Asin(System.Math.Clamp(st.Velocity.Y / v, -1, 1)) : 0;
        double rud = onGround
            ? System.Math.Clamp(hErr * 3.0 - st.Rates.Z * 0.6, -1, 1)
            : System.Math.Clamp(ail * 0.5 + bankCmd * 0.25 + hErr * 0.3 - st.Rates.Z * 0.2 + beta * 3.0, -1, 1);

        // ---- longitudinal.
        double elev, lever;
        double alpha = v > 5 ? System.Math.Atan2(st.Velocity.Z, st.Velocity.X) : 0;
        tug.FlapFraction = 0;
        if (!Airborne)
        {
            // The ground roll, inside a tunnel: hold the stance, then rotate gently. Full power is the glider
            // pilot's signal, and until it comes nothing moves.
            double pitchCmd = v < 0.8 * CruiseMs ? System.Math.Min(pitch, 9 * System.Math.PI / 180) : 10 * System.Math.PI / 180;
            elev = System.Math.Clamp(-(pitchCmd - pitch) * 5.0 + st.Rates.Y * 0.6, -0.6, 0.4);
            if (v < 10) elev = 0;
            lever = 1.0 - 2.0 * System.Math.Clamp(PowerLimit01, 0, 1);
            Status = PowerLimit01 >= 0.98 ? "rolling" : "waiting for full throttle";
            tug.BrakeInput = PowerLimit01 >= 0.98 ? 0 : 0.3;
            tug.BrakeBias = v > 0.5 && v < 12 ? System.Math.Clamp(hErr * 2.0, -0.6, 0.6) : 0;
        }
        else
        {
            // PITCH FLIES THE PATH and POWER FLIES THE SPEED — the other way round from the open-air tow,
            // and the right way round in a bore 32 m tall: the height loop has to be the fast one, and the
            // height is measured off the FLOOR, which falls away the whole way round the maze.
            //
            // (The first version used the open-air law — pitch for speed, power for height — and it cut the
            // power the moment the tug climbed off the strip, then flew it into the floor 300 m later.)
            double gammaCmd = System.Math.Clamp((wantUp - up) * 0.05, -0.12, 0.14);      // ±7°, a little more up
            double pitchCmd = System.Math.Clamp(gammaCmd + 0.05, -0.12, 0.22);            // + the cruise angle of attack
            _pitchTrim = System.Math.Clamp(_pitchTrim - 0.35 * (pitchCmd - pitch) * dt, -0.5, 0.35);
            elev = System.Math.Clamp(_pitchTrim - (pitchCmd - pitch) * 3.0 + st.Rates.Y * 0.7
                                     - 0.10 * System.Math.Abs(bankCmd), -0.7, 0.4);
            double thr = System.Math.Clamp(0.55 + 0.09 * (CruiseMs - v) + 0.5 * System.Math.Max(0, gammaCmd), 0.35, 1.0);
            lever = 1.0 - 2.0 * System.Math.Clamp(thr * PowerLimit01, 0, 1);
            _ = sink;
            tug.BrakeInput = 0;
            tug.BrakeBias = 0;
            Status = Done ? "out" : $"{_legs[LegIndex].Id}  {(_ropeAvg < -WaterfallTunnel.Route.RopeDeadBand ? "→ right" : _ropeAvg > WaterfallTunnel.Route.RopeDeadBand ? "← left" : "centred")}";
        }
        // Never hold the wing past the stall, whatever the laws above want.
        if (v > 15 && alpha > 12 * System.Math.PI / 180)
            elev = System.Math.Max(elev, System.Math.Min(0.6, (alpha - 12 * System.Math.PI / 180) * 8.0));
        return new ControlInputs(ail, elev, rud, lever);
    }

    private static double Wrap(double a)
    {
        while (a > System.Math.PI) a -= 2 * System.Math.PI;
        while (a < -System.Math.PI) a += 2 * System.Math.PI;
        return a;
    }
}
