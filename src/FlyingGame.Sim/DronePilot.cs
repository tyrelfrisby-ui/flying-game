using FlyingGame.Core;
using FlyingGame.Core.Combat;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Target-drone autopilot (owner 2026-09-10): orbits the combat zone between waypoints at a set height and speed —
/// pitch holds the height, bank steers to the next waypoint, power holds the speed. Nothing clever: it is there to
/// be shot at. Once a wing panel is gone the airframe falls under the sim like anything else.
/// </summary>
public sealed class DronePilot
{
    public double CruiseMs = 45.0, HeightAglM = 500.0, MaxBankDeg = 35.0;
    public readonly List<Vec3> Waypoints = new();
    public int Next { get; private set; }
    private readonly Random _rng;

    public DronePilot(int seed)
    {
        _rng = new Random(seed);
        // A wobbly loop around the zone, well inside its walls, at a height that varies per leg.
        double cx = CombatZone.CentreX, cy = CombatZone.CentreY;
        double rx = (CombatZone.X1 - CombatZone.X0) * 0.32, ry = (CombatZone.Y1 - CombatZone.Y0) * 0.32;
        int n = 6; double phase = _rng.NextDouble() * 6.28;
        for (int i = 0; i < n; i++)
        {
            double a = phase + i * 6.28318 / n;
            Waypoints.Add(new Vec3(cx + rx * Math.Cos(a) * (0.8 + 0.4 * _rng.NextDouble()), cy + ry * Math.Sin(a) * (0.8 + 0.4 * _rng.NextDouble()), 300 + 500 * _rng.NextDouble()));   // Z = height AGL here
        }
        Next = _rng.Next(n);
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
        Vec3 wp = Waypoints[Next];
        double dx = wp.X - s.Position.X, dy = wp.Y - s.Position.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 250) { Next = (Next + 1) % Waypoints.Count; wp = Waypoints[Next]; dx = wp.X - s.Position.X; dy = wp.Y - s.Position.Y; }
        double headingCmd = Math.Atan2(dy, dx);
        double hErr = Wrap(headingCmd - psi);
        double maxBank = MaxBankDeg * Math.PI / 180;
        double bankCmd = Math.Clamp(hErr * 1.6, -maxBank, maxBank);
        double ail = Math.Clamp((bankCmd - roll) * 1.5 - s.Rates.X * 0.25, -1, 1);
        // Height: climb/descend toward the leg's height with a pitch target; speed by power.
        double hErrM = wp.Z - agl;
        double pitchCmd = Math.Clamp(hErrM * 0.0025, -8 * Math.PI / 180, 10 * Math.PI / 180) + Math.Abs(bankCmd) * 0.15;
        double ele = Math.Clamp(-(pitchCmd - pitch) * 3.0 + s.Rates.Y * 0.5, -0.7, 0.5);
        double thr = Math.Clamp(0.6 + (CruiseMs - v) * 0.05 - hErrM * -0.0005, 0.15, 1.0);
        double rud = Math.Clamp(-s.Velocity.Y * 0.02, -0.3, 0.3);   // keep the ball centred
        return new ControlInputs(ail, ele, rud, 1.0 - 2.0 * thr);
    }
}
