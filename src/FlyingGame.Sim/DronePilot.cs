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
        bool inside = s.Position.X > CombatZone.X0 + 150 && s.Position.X < CombatZone.X1 - 150 && s.Position.Y > CombatZone.Y0 + 150 && s.Position.Y < CombatZone.Y1 - 150;
        _t += dt;

        // Safety first: too low, too slow or outside → recover / come back, whatever was being flown.
        double vMin = CruiseMs * 0.55;
        if (Aerobatic && Current != Manoeuvre.Recover && (agl < FloorAglM || v < vMin || agl > CeilingAglM))
        {
            Current = Manoeuvre.Recover; _stage = 0; _legT = 0;
        }
        if (Current == Manoeuvre.Recover)
        {
            _legT += dt;
            // Roll wings level, then pull to a climb (or push if too high) until back inside the band.
            double ail = Math.Clamp(-roll * 1.5 - s.Rates.X * 0.3, -1, 1);
            double pitchCmd = agl < FloorAglM ? 12 * Math.PI / 180 : agl > CeilingAglM ? -6 * Math.PI / 180 : 3 * Math.PI / 180;
            double ele = Math.Abs(roll) < 0.5 ? Math.Clamp(-(pitchCmd - pitch) * 3.0 + s.Rates.Y * 0.5, -0.7, 0.5) : 0.1;
            bool safe = agl > FloorAglM + 80 && agl < CeilingAglM - 50 && v > vMin * 1.2 && Math.Abs(roll) < 0.3;
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
            bool roomAhead = ax > CombatZone.X0 + 150 && ax < CombatZone.X1 - 150 && ay > CombatZone.Y0 + 150 && ay < CombatZone.Y1 - 150;
            if (Aerobatic && inside && roomAhead && _legT > _cruiseFor && agl > FloorAglM + 120 && v > CruiseMs * 0.85) StartRandomManoeuvre(pitch);
            else return Cruise(s, q, roll, pitch, psi, v, agl);
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
