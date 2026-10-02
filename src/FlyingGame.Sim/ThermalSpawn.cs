using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// "Start in a thermal" (owner 2026-10-01): already circling — banked, at the minimum-sink speed FOR THAT BANK, trimmed,
/// on a circle centred on the core at that height — so holding the bank keeps the aircraft climbing in the thermal.
///   Min sink in a turn: at load factor n = 1/cos φ the same lift coefficient needs √n more speed, so the turning
///   min-sink speed is V_ms·√n (sink × n^1.5). The trim (α, elevator) is the wings-level trim at the equivalent speed V/√n
///   (same CL). Bank 35°, steeper (to 60°) if the circle would not fit inside ~45 % of the core radius.
/// </summary>
public static class ThermalSpawn
{
    public const double DriftLeadSec = 60;   // start this much of the expected drift upwind of the core
    public readonly struct Plan
    {
        public readonly RigidBodyState State;
        public readonly double ElevatorRad, BankRad, SpeedMs, RadiusM, MinSinkWingsLevelMs;
        public readonly Vec3 Core;
        public Plan(RigidBodyState state, double elevatorRad, double bankRad, double speedMs, double radiusM, double vms, Vec3 core)
        { State = state; ElevatorRad = elevatorRad; BankRad = bankRad; SpeedMs = speedMs; RadiusM = radiusM; MinSinkWingsLevelMs = vms; Core = core; }
    }

    /// <summary>Wings-level minimum-sink speed from the sim's own trim polar.</summary>
    public static double MinSinkSpeed(AircraftConfig cfg, double altitudeM)
    {
        double vRef = cfg.SpawnIasMs > 1 ? cfg.SpawnIasMs : 25.0, best = double.MaxValue, vBest = vRef;
        for (double v = 8; v <= vRef * 1.6; v += 0.5)
        {
            TrimSolver.Result t = TrimSolver.SolveGliderTrim(cfg, v, altitudeM);
            if (!t.Converged || t.GlideRatio <= 0 || double.IsNaN(t.GlideRatio) || t.AlphaRad > 13 * Math.PI / 180) continue;
            double sink = v / t.GlideRatio;
            if (sink < best) { best = sink; vBest = v; }
        }
        return vBest;
    }

    public static Plan Compute(AircraftConfig cfg, Thermal th, double altitudeM)
    {
        double vms = MinSinkSpeed(cfg, altitudeM);
        double zf = Math.Clamp(altitudeM / th.TopAltitudeM, 0, 1);
        double coreR = th.CoreRadiusM * (1.0 + 0.8 * zf);
        double bank = 35 * Math.PI / 180, v = 0, r = 0;
        for (; bank <= 60 * Math.PI / 180 + 1e-9; bank += Math.PI / 180)
        {
            v = 1.05 * vms * Math.Sqrt(1 / Math.Cos(bank));   // 5 % above min sink: margin over the stall in the turn
            r = v * v / (Atmosphere.GravityMs2 * Math.Tan(bank));
            if (r <= 0.45 * coreR) break;
        }
        bank = Math.Min(bank, 60 * Math.PI / 180);
        double n = 1 / Math.Cos(bank);
        TrimSolver.Result trim = TrimSolver.SolveGliderTrim(cfg, v / Math.Sqrt(n), altitudeM);
        double alpha = trim.AlphaRad, gamma = -Math.Atan(n / Math.Max(1.0, trim.GlideRatio));   // descending turn (still air)

        // A RIGHT-hand circle around the core at this height: heading north, the core 90° to the right (east).
        // In a wind the plume leans at the rate the AIR rises; an aircraft climbing slower than that drifts downwind of the
        // core at wind × (1 − climb / w_axis). Centre the circle that drift × DriftLeadSec upwind (as a pilot would), so the
        // drift carries it across the core instead of out of it. (A glider in a strong core barely drifts at all.)
        Vec3 core = th.CoreAt(altitudeM);
        Vec3 windH = Atmosphere.SteadyWind * Atmosphere.WindGradientFactor(new Vec3(0, 0, -altitudeM));
        if (windH.X != 0 || windH.Y != 0)
        {
            double wAxis = -th.WindAt(new Vec3(core.X, core.Y, -altitudeM)).Z;
            double wHere = -th.WindAt(new Vec3(core.X + r, core.Y, -altitudeM)).Z;
            double sinkTurn = v * n / Math.Max(1.0, trim.GlideRatio);
            double climb = Math.Max(0, wHere - sinkTurn);
            double lag = wAxis > 0.5 ? Math.Clamp(1 - climb / wAxis, 0, 1) : 0;
            core = core - new Vec3(windH.X, windH.Y, 0) * (lag * DriftLeadSec);
        }
        double psi = 0;
        var right = new Vec3(-Math.Sin(psi), Math.Cos(psi), 0);
        Vec3 pos = new Vec3(core.X, core.Y, -altitudeM) - right * r;

        double theta = gamma + alpha * Math.Cos(bank);
        Quat q = Mul(Mul(Axis(0, 0, 1, psi), Axis(0, 1, 0, theta)), Axis(1, 0, 0, bank));
        var vAir = new Vec3(v * Math.Cos(gamma) * Math.Cos(psi), v * Math.Cos(gamma) * Math.Sin(psi), -v * Math.Sin(gamma));
        Vec3 vGround = vAir + Atmosphere.SteadyWind * Atmosphere.WindGradientFactor(pos);
        double omega = Atmosphere.GravityMs2 * Math.Tan(bank) / v;   // turn rate about world down (+ = right)
        var state = new RigidBodyState(pos, q, q.Conjugate().Rotate(vGround), q.Conjugate().Rotate(new Vec3(0, 0, omega)));
        return new Plan(state, trim.ElevatorRad, bank, v, r, vms, core);
    }

    private static Quat Axis(double x, double y, double z, double a) => new(x * Math.Sin(a / 2), y * Math.Sin(a / 2), z * Math.Sin(a / 2), Math.Cos(a / 2));
    private static Quat Mul(Quat a, Quat b) => Quat.Multiply(a, b);
}
