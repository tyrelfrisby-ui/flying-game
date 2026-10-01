using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>One recorded instant of a flight: everything the display needs to redraw it (pose, surfaces, power, gear, g).</summary>
public readonly struct ReplayFrame
{
    public readonly double Time;
    public readonly RigidBodyState State;
    public readonly ControlDeflections Deflections;
    public readonly double Throttle01, FlapFraction, GearExtension, LoadFactorZ;
    /// <summary>Local wind (world NED) at the aircraft when recorded — the relative-wind camera and airspeed need it.</summary>
    public readonly Vec3 WindWorld;

    public ReplayFrame(double time, RigidBodyState state, ControlDeflections deflections, double throttle01, double flapFraction,
        double gearExtension, double loadFactorZ, Vec3 windWorld)
    {
        Time = time; State = state; Deflections = deflections; Throttle01 = throttle01; FlapFraction = flapFraction;
        GearExtension = gearExtension; LoadFactorZ = loadFactorZ; WindWorld = windWorld;
    }

    public static ReplayFrame Capture(double time, Aircraft a, Vec3 windWorld) =>
        new(time, a.State, a.CurrentDeflections, a.Throttle01, a.FlapFraction, a.GearExtension, a.LoadFactorZ, windWorld);

    public void ApplyTo(Aircraft a) => a.SetReplayPose(State, Deflections, Throttle01, FlapFraction, GearExtension, LoadFactorZ);
}

/// <summary>
/// Rolling flight recorder for the in-game replay: keeps the last <see cref="MaxSeconds"/> of flight at
/// <see cref="SampleHz"/> and returns an interpolated frame for any time inside it (position/velocity/rates
/// linear, attitude slerped). Engine-agnostic so it is unit-tested headless.
/// </summary>
public sealed class FlightRecorder
{
    public readonly double SampleHz;
    public readonly double MaxSeconds;
    private readonly ReplayFrame[] _ring;
    private int _start, _count;

    public FlightRecorder(double sampleHz = 30.0, double maxSeconds = 600.0)
    {
        SampleHz = sampleHz;
        MaxSeconds = maxSeconds;
        _ring = new ReplayFrame[(int)System.Math.Ceiling(sampleHz * maxSeconds) + 1];
    }

    public int Count => _count;
    public double StartTime => _count == 0 ? 0 : At(0).Time;
    public double EndTime => _count == 0 ? 0 : At(_count - 1).Time;
    public double Duration => EndTime - StartTime;

    public ReplayFrame At(int i) => _ring[(_start + i) % _ring.Length];

    public void Clear() { _start = 0; _count = 0; }

    /// <summary>Add a frame if at least one sample period has passed since the last one (call every display frame).</summary>
    public bool Record(ReplayFrame f)
    {
        if (_count > 0 && f.Time - EndTime < 1.0 / SampleHz - 1e-9)
        {
            if (f.Time >= EndTime) return false;
            Clear();   // time went backwards: a new flight
        }
        if (_count < _ring.Length) { _ring[(_start + _count) % _ring.Length] = f; _count++; }
        else { _ring[_start] = f; _start = (_start + 1) % _ring.Length; }
        return true;
    }

    /// <summary>The flight at time <paramref name="t"/> (clamped to the recording), interpolated between samples.</summary>
    public ReplayFrame Sample(double t)
    {
        if (_count == 0) throw new System.InvalidOperationException("empty recording");
        if (t <= StartTime) return At(0);
        if (t >= EndTime) return At(_count - 1);
        int lo = 0, hi = _count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (At(mid).Time <= t) lo = mid; else hi = mid; }
        ReplayFrame a = At(lo), b = At(hi);
        double u = (t - a.Time) / (b.Time - a.Time);
        return Lerp(a, b, u);
    }

    private static double L(double a, double b, double u) => a + (b - a) * u;
    private static Vec3 L(Vec3 a, Vec3 b, double u) => new(L(a.X, b.X, u), L(a.Y, b.Y, u), L(a.Z, b.Z, u));

    public static Quat Slerp(Quat a, Quat b, double u)
    {
        double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (dot < 0) { b = new Quat(-b.X, -b.Y, -b.Z, -b.W); dot = -dot; }
        if (dot > 0.9995) return new Quat(L(a.X, b.X, u), L(a.Y, b.Y, u), L(a.Z, b.Z, u), L(a.W, b.W, u)).Normalized();
        double th = System.Math.Acos(dot), s = System.Math.Sin(th);
        double wa = System.Math.Sin((1 - u) * th) / s, wb = System.Math.Sin(u * th) / s;
        return new Quat(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb, a.W * wa + b.W * wb);
    }

    public static ReplayFrame Lerp(ReplayFrame a, ReplayFrame b, double u)
    {
        var s = new RigidBodyState(L(a.State.Position, b.State.Position, u), Slerp(a.State.Attitude, b.State.Attitude, u),
            L(a.State.Velocity, b.State.Velocity, u), L(a.State.Rates, b.State.Rates, u));
        ControlDeflections da = a.Deflections, db = b.Deflections;
        var d = new ControlDeflections(L(da.AileronRad, db.AileronRad, u), L(da.ElevatorRad, db.ElevatorRad, u),
            L(da.RudderRad, db.RudderRad, u), L(da.SpoilerFraction, db.SpoilerFraction, u), L(da.FlapFraction, db.FlapFraction, u), L(da.SlatFraction, db.SlatFraction, u));
        return new ReplayFrame(L(a.Time, b.Time, u), s, d, L(a.Throttle01, b.Throttle01, u), L(a.FlapFraction, b.FlapFraction, u),
            L(a.GearExtension, b.GearExtension, u), L(a.LoadFactorZ, b.LoadFactorZ, u), L(a.WindWorld, b.WindWorld, u));
    }
}
