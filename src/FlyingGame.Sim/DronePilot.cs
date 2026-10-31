using FlyingGame.Core;
using FlyingGame.Core.Combat;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Target-drone autopilot (owner 2026-09-12). Two temperaments:
///  • STEADY (the DC-3s): lumbers between waypoints around the zone at a set height and speed.
///  • AEROBATIC (the Cassutts): fast, and between legs flies a randomly chosen manoeuvre — loop, barrel roll,
///    Immelmann, split-S, scissors, jinks, a hard break turn — with random direction, size and duration, so the next
///    one cannot be predicted. A floor (250 m AGL) and the zone walls override everything: below the floor it rolls
///    level and climbs, outside the walls it turns back in. Once a wing panel is gone the sim takes over.
/// </summary>
public sealed class DronePilot
{
    public enum Manoeuvre { Cruise, Loop, BarrelRoll, Immelmann, SplitS, Scissors, Jink, BreakTurn, Recover }

    public double CruiseMs = 45.0, MaxBankDeg = 35.0, FloorAglM = 250.0, CeilingAglM = 1800.0;
    /// <summary>How far ahead along the present track the pilot looks for rising ground, in seconds of flight
    /// (owner-reported 2026-10-31). Height is held over the HIGHEST ground within this reach, not the ground
    /// underneath: a reactive floor cannot save a drone from a cliff FACE. The island's height field rises 364 m in
    /// 12 m horizontally near (3245, 880) — 0.18 s of warning at cruise — and five of the fifteen AI aeroplanes flew
    /// into terrain inside three minutes there, against none of fifteen in the legacy world.
    ///
    /// Thirty seconds, because the reach has to be the CLIMB distance, not a glance: at 70 m/s and the 10 degree
    /// pitch limit, out-climbing that 364 m takes 2,100 m of travel, and a fifteen-second look gave 1,100 m — the
    /// drone saw the wall and still could not clear it. A look this long is also why <see cref="Escape"/> exists:
    /// past a certain rise the honest answer is to turn away rather than pull into the hill.</summary>
    public double LookaheadSec = 30.0;
    /// <summary>A waypoint counts as reached inside this radius (formation leaders turn gently: give them more room).</summary>
    public double CaptureRadiusM = 250.0;
    public bool Aerobatic;
    public readonly List<Vec3> Waypoints = new();
    public int Next { get; private set; }
    public Manoeuvre Current { get; private set; } = Manoeuvre.Cruise;
    public int ManoeuvresFlown { get; private set; }
    public readonly HashSet<Manoeuvre> Flown = new();
    private readonly Random _rng;
    private double _t, _legT, _dir = 1, _param, _phaseAcc, _lastPitch, _cruiseFor;
    private int _stage;

    public DronePilot(int seed, bool aerobatic = false, double cruiseMs = 45.0)
    {
        _rng = new Random(seed);
        Aerobatic = aerobatic; CruiseMs = cruiseMs;
        double cx = CombatZone.CentreX, cy = CombatZone.CentreY;
        double rx = (CombatZone.X1 - CombatZone.X0) * 0.30, ry = (CombatZone.Y1 - CombatZone.Y0) * 0.30;
        int n = 6; double phase = _rng.NextDouble() * 6.28;
        for (int i = 0; i < n; i++)
        {
            double a = phase + i * 6.28318 / n;
            Waypoints.Add(new Vec3(cx + rx * Math.Cos(a) * (0.8 + 0.4 * _rng.NextDouble()), cy + ry * Math.Sin(a) * (0.8 + 0.4 * _rng.NextDouble()), (aerobatic ? 500 : 300) + 500 * _rng.NextDouble()));   // Z = height AGL here
        }
        Next = _rng.Next(n);
        _cruiseFor = 6 + 10 * _rng.NextDouble();
    }

    /// <summary>The highest ground within the next <see cref="LookaheadSec"/> of flight along the present track, so a
    /// drone begins its climb at the foot of a rise rather than at its face. Four samples: at cruise and a 10 degree
    /// climb limit, anything nearer than this is unavoidable anyway.</summary>
    private double GroundAhead(RigidBodyState s, Quat q, double v)
    {
        double hi = WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
        Vec3 vw = q.Rotate(s.Velocity);
        double vh = Math.Sqrt(vw.X * vw.X + vw.Y * vw.Y);
        if (vh < 1.0) return hi;
        double reach = Math.Min(4000.0, Math.Max(200.0, v * LookaheadSec));
        for (int i = 1; i <= 8; i++)
        {
            double d = reach * i / 8.0;
            hi = Math.Max(hi, WorldTerrain.GroundHeightAt(s.Position.X + vw.X / vh * d, s.Position.Y + vw.Y / vh * d));
        }
        return hi;
    }

    /// <summary>Which way to turn when the ground ahead cannot be out-climbed: the signed bank, left or right,
    /// toward whichever side has the lower terrain, or zero when turning would not help. Climbing is the first
    /// answer and this is the second — a drone that only ever pulls flies into the hill at a steeper angle.</summary>
    private double Escape(RigidBodyState s, Quat q, double v, double aglAhead)
    {
        if (aglAhead > FloorAglM * 0.6) return 0.0;        // the climb has room to work
        Vec3 vw = q.Rotate(s.Velocity);
        double vh = Math.Sqrt(vw.X * vw.X + vw.Y * vw.Y);
        if (vh < 1.0) return 0.0;
        double reach = Math.Min(4000.0, Math.Max(200.0, v * LookaheadSec));
        double fx = vw.X / vh, fy = vw.Y / vh;
        double left = 0, right = 0;
        for (int i = 1; i <= 4; i++)
        {
            double d = reach * i / 4.0;
            // 45 degrees either side of the nose: far enough to find a way out, near enough to still be a turn.
            double c = 0.7071, t = 0.7071;
            left = Math.Max(left, WorldTerrain.GroundHeightAt(s.Position.X + (fx * c - fy * t) * d, s.Position.Y + (fy * c + fx * t) * d));
            right = Math.Max(right, WorldTerrain.GroundHeightAt(s.Position.X + (fx * c + fy * t) * d, s.Position.Y + (fy * c - fx * t) * d));
        }
        double maxBank = MaxBankDeg * Math.PI / 180;
        return left < right ? maxBank : -maxBank;          // +bank = right wing down = turn right, so toward +y
    }

    /// <summary>The orbit a combat zone AUTHORS (IslandCombat.Zone): six waypoints on a circle about the zone's own
    /// centre, at its own height. The authored radius is a target and is widened to what the drone can actually fly —
    /// 150 m at 65 m/s needs a 70 degree bank, which nothing here holds — and the floor comes down to suit a zone
    /// whose authored orbit sits lower than the legacy 250 m (CZ5 orbits at 600 ft MSL over an 188 ft mesa).</summary>
    public DronePilot(int seed, bool aerobatic, double cruiseMs, double centreX, double centreY, double radiusM, double heightAglM)
    {
        _rng = new Random(seed);
        Aerobatic = aerobatic; CruiseMs = cruiseMs;
        double flyable = cruiseMs * cruiseMs / (9.80665 * Math.Tan(MaxBankDeg * Math.PI / 180));
        double r = Math.Max(radiusM, flyable);
        FloorAglM = Math.Clamp(heightAglM - 60, 40, FloorAglM);
        CeilingAglM = Math.Max(heightAglM + 300, FloorAglM + 200);
        CaptureRadiusM = Math.Max(150.0, r * 0.35);
        int n = 6; double phase = _rng.NextDouble() * 6.28;
        for (int i = 0; i < n; i++)
        {
            double a = phase + i * 6.28318 / n;
            Waypoints.Add(new Vec3(centreX + r * Math.Cos(a), centreY + r * Math.Sin(a), heightAglM));
        }
        Next = _rng.Next(n);
        _cruiseFor = 6 + 10 * _rng.NextDouble();
        _orbit = true;
    }

    /// <summary>True when this pilot flies an authored zone orbit rather than the legacy box's waypoints: the legacy
    /// box walls do not apply to it, and its own circle keeps it in place.</summary>
    private readonly bool _orbit;

    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    public ControlInputs Update(Aircraft drone, double dt)
    {
        RigidBodyState s = drone.State; Quat q = s.Attitude;
        double roll = Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        double pitch = Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1));
        double psi = Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        double v = s.Velocity.Length;
        double ground = WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);
        double agl = -s.Position.Z - ground;
        // The height the pilot FLIES to is measured against the worst ground it is about to be over, not the ground
        // beneath it. Everything below uses aglAhead; plain agl stays for the ceiling, which is a real altitude.
        double aglAhead = -s.Position.Z - GroundAhead(s, q, v);
        bool inside = _orbit || (s.Position.X > CombatZone.X0 + 150 && s.Position.X < CombatZone.X1 - 150 && s.Position.Y > CombatZone.Y0 + 150 && s.Position.Y < CombatZone.Y1 - 150);
        _t += dt;

        // Safety first: too low, too slow or outside → recover / come back, whatever was being flown.
        double vMin = CruiseMs * 0.55;
        if (Current != Manoeuvre.Recover && (aglAhead < FloorAglM || v < vMin || agl > CeilingAglM))
        {
            Current = Manoeuvre.Recover; _stage = 0; _legT = 0;
        }
        if (Current == Manoeuvre.Recover)
        {
            _legT += dt;
            // Roll wings level, then pull to a climb (or push if too high) until back inside the band.
            // Climb, and if the rise cannot be out-climbed, turn toward the lower ground while climbing. The bank
            // costs climb rate, which is why it is only asked for when the climb alone has already lost.
            double escape = Escape(s, q, v, aglAhead);
            double bankWanted = escape == 0.0 ? 0.0 : escape;
            double ail = Math.Clamp((bankWanted - roll) * 1.5 - s.Rates.X * 0.3, -1, 1);
            double pitchCmd = aglAhead < FloorAglM ? 12 * Math.PI / 180 : agl > CeilingAglM ? -6 * Math.PI / 180 : 3 * Math.PI / 180;
            double ele = Math.Abs(roll - bankWanted) < 0.5 ? Math.Clamp(-(pitchCmd - pitch) * 3.0 + s.Rates.Y * 0.5, -0.7, 0.5) : 0.1;
            bool safe = aglAhead > FloorAglM + 80 && agl < CeilingAglM - 50 && v > vMin * 1.2 && Math.Abs(roll) < 0.3;
            if (safe || _legT > 12) { Current = Manoeuvre.Cruise; _legT = 0; _cruiseFor = 4 + 8 * _rng.NextDouble(); }
            return new ControlInputs(ail, AoaLimit(ele, s, v), Math.Clamp(-s.Velocity.Y * 0.02, -0.3, 0.3), -1.0);
        }

        if (Current == Manoeuvre.Cruise)
        {
            _legT += dt;
            // Only start a manoeuvre with room AHEAD: ~700 m along the track must still be inside (a loop or a break turn
            // carries the drone on; started at the edge heading out, it would end up outside).
            Vec3 vw = q.Rotate(s.Velocity); double vh = Math.Max(1, Math.Sqrt(vw.X * vw.X + vw.Y * vw.Y));
            double ax = s.Position.X + vw.X / vh * 700, ay = s.Position.Y + vw.Y / vh * 700;
            bool roomAhead = _orbit || (ax > CombatZone.X0 + 150 && ax < CombatZone.X1 - 150 && ay > CombatZone.Y0 + 150 && ay < CombatZone.Y1 - 150);
            if (Aerobatic && inside && roomAhead && _legT > _cruiseFor && aglAhead > FloorAglM + 120 && v > CruiseMs * 0.85) StartRandomManoeuvre(pitch);
            else return Cruise(s, q, roll, pitch, psi, v, aglAhead);
        }

        // ---- manoeuvres: scripted on attitude and rates, full power throughout
        _legT += dt;
        double a2 = 0, e2 = 0;
        switch (Current)
        {
            case Manoeuvre.Loop:
                // Pull ~3.5 g and keep wings level through 360° of pitch; done when upright again after the top.
                a2 = Math.Clamp(-Wrap(roll - (pitch > 0 && _phaseAcc > 2.5 ? Math.PI : 0)) * 0.8, -0.6, 0.6);
                if (_phaseAcc > 2.5 && Math.Abs(Wrap(roll)) > 2.5) a2 = 0;   // over the top: inverted is correct
                e2 = -0.75;
                _phaseAcc += Math.Abs(s.Rates.Y) * dt;
                if (_phaseAcc > 6.0 && Math.Abs(pitch) < 0.35 && Math.Abs(Wrap(roll)) < 0.8) Finish();
                if (_legT > 14) Finish();
                break;
            case Manoeuvre.BarrelRoll:
                a2 = 0.85 * _dir; e2 = -0.35;
                _phaseAcc += Math.Abs(s.Rates.X) * dt;
                if (_phaseAcc > 6.1 * _param && Math.Abs(Wrap(roll)) < 0.5) Finish();
                if (_legT > 10) Finish();
                break;
            case Manoeuvre.Immelmann:
                if (_stage == 0) { e2 = -0.75; a2 = Math.Clamp(-Wrap(roll) * 0.8, -0.5, 0.5); _phaseAcc += Math.Abs(s.Rates.Y) * dt; if (_phaseAcc > 2.9) _stage = 1; }
                else { a2 = 0.9 * _dir; e2 = 0.05; if (Math.Abs(Wrap(roll)) < 0.35 && _legT > 2) Finish(); }
                if (_legT > 12) Finish();
                break;
            case Manoeuvre.SplitS:
                if (_stage == 0) { a2 = 0.9 * _dir; e2 = 0.05; if (Math.Abs(Wrap(roll)) > 2.8) _stage = 1; }
                else { e2 = -0.75; a2 = 0; _phaseAcc += Math.Abs(s.Rates.Y) * dt; if (_phaseAcc > 2.9 && Math.Abs(Wrap(roll)) < 0.6) Finish(); }
                if (_legT > 12) Finish();
                break;
            case Manoeuvre.Scissors:
                // Alternate hard reversals: bank ~70° one way and pull, then the other; _param = reversals left.
                { double targetBank = 1.2 * _dir; a2 = Math.Clamp((targetBank - roll) * 1.6, -1, 1); e2 = -0.55; }
                if (_legT > _phaseAcc + 1.4 + _rng.NextDouble() * 0.8) { _phaseAcc = _legT; _dir = -_dir; _param -= 1; if (_param <= 0) Finish(); }
                break;
            case Manoeuvre.Jink:
                // Sharp random rolls and pushes/pulls, each held a fraction of a second.
                if (_legT > _phaseAcc + 0.35 + _rng.NextDouble() * 0.5) { _phaseAcc = _legT; _dir = _rng.NextDouble() < 0.5 ? -1 : 1; _lastPitch = _rng.NextDouble() < 0.6 ? -0.6 : 0.35; }
                a2 = 0.9 * _dir; e2 = _lastPitch;
                if (_legT > _param) Finish();
                break;
            case Manoeuvre.BreakTurn:
                { double targetBank = 1.25 * _dir; a2 = Math.Clamp((targetBank - roll) * 1.6, -1, 1); e2 = -0.6; }
                if (_legT > _param) Finish();
                break;
        }
        double rud = Math.Clamp(Math.Clamp(a2, -1, 1) * 0.5 - s.Velocity.Y * 0.02, -0.6, 0.6);
        // Angle-of-attack limiter (like the tug's): the open-loop pulls (scissors, break turns) held a fixed aft stick; with the
        // real pitch stability (2026-10-06) that stick reached past the stall and spun the drone in. Ease off past ~12°.
        return new ControlInputs(Math.Clamp(a2, -1, 1), AoaLimit(Math.Clamp(e2, -0.8, 0.6), s, v), rud, -1.0);
    }

    private void Finish() { Current = Manoeuvre.Cruise; _legT = 0; _cruiseFor = 3 + 9 * _rng.NextDouble(); }

    private void StartRandomManoeuvre(double pitch)
    {
        // Weighted pick, never the same one twice in a row.
        Manoeuvre[] pool = { Manoeuvre.Loop, Manoeuvre.BarrelRoll, Manoeuvre.Immelmann, Manoeuvre.SplitS, Manoeuvre.Scissors, Manoeuvre.Scissors, Manoeuvre.Jink, Manoeuvre.Jink, Manoeuvre.BreakTurn, Manoeuvre.BreakTurn };
        Manoeuvre pick;
        do pick = pool[_rng.Next(pool.Length)]; while (pick == _last);
        _last = pick;
        Current = pick; _legT = 0; _stage = 0; _phaseAcc = 0; _dir = _rng.NextDouble() < 0.5 ? -1 : 1;
        _param = pick switch
        {
            Manoeuvre.BarrelRoll => _rng.NextDouble() < 0.3 ? 2 : 1,     // sometimes two in a row
            Manoeuvre.Scissors => 3 + _rng.Next(3),                        // 3–5 reversals
            Manoeuvre.Jink => 1.5 + 2.5 * _rng.NextDouble(),               // seconds
            Manoeuvre.BreakTurn => 2.5 + 4 * _rng.NextDouble(),
            _ => 1,
        };
        ManoeuvresFlown++; Flown.Add(pick);
    }
    private Manoeuvre _last = Manoeuvre.Cruise;

    private ControlInputs Cruise(RigidBodyState s, Quat q, double roll, double pitch, double psi, double v, double agl)
    {
        Vec3 wp = Waypoints[Next];
        double dx = wp.X - s.Position.X, dy = wp.Y - s.Position.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < CaptureRadiusM) { Next = (Next + 1) % Waypoints.Count; wp = Waypoints[Next]; dx = wp.X - s.Position.X; dy = wp.Y - s.Position.Y; }
        double headingCmd = Math.Atan2(dy, dx);
        double hErr = Wrap(headingCmd - psi);
        double maxBank = MaxBankDeg * Math.PI / 180;
        double bankCmd = Math.Clamp(hErr * 1.6, -maxBank, maxBank);
        double ail = Math.Clamp((bankCmd - roll) * 1.5 - s.Rates.X * 0.25, -1, 1);
        double hErrM = wp.Z - agl;
        double pitchCmd = Math.Clamp(hErrM * 0.0025, -8 * Math.PI / 180, 10 * Math.PI / 180) + Math.Abs(bankCmd) * 0.15;
        double ele = Math.Clamp(-(pitchCmd - pitch) * 3.0 + s.Rates.Y * 0.5, -0.7, 0.5);
        double thr = Math.Clamp(0.6 + (CruiseMs - v) * 0.05 - hErrM * -0.0005, 0.15, 1.0);
        // Rudder WITH the aileron (the ailerons carry their real induced drag since 2026-10-06 — uncoordinated, the turns
        // were wide and slow and the drone spent a third of its time outside the zone) + the ball.
        double rud = Math.Clamp(ail * 0.5 - s.Velocity.Y * 0.02, -0.6, 0.6);
        return new ControlInputs(ail, AoaLimit(ele, s, v), rud, 1.0 - 2.0 * thr);
    }

    /// <summary>Never hold the wing past ~12° (a maximum-performance pull, not a stall): every law here, the recovery's dive
    /// pull-out most of all, asked for more aft stick than the real pitch stability (2026-10-06) needs to stall the drone.</summary>
    private static double AoaLimit(double ele, RigidBodyState s, double v)
    {
        double alpha = v > 5 ? Math.Atan2(s.Velocity.Z, s.Velocity.X) : 0.0;
        return alpha > 12 * Math.PI / 180 ? Math.Max(ele, Math.Min(0.6, (alpha - 12 * Math.PI / 180) * 8.0)) : ele;
    }
}
