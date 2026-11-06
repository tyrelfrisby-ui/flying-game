using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim.Replay;

/// <summary>The smoke as flown at one recorded instant (Ty t912u: "flew with smoke ON; replay showed none"). The switch and
/// what the oil would have made with it on are kept APART, so a replay can show the flight as flown, or add smoke after the
/// fact with the flow the engine really had at that moment, or mute it — without touching the recording.</summary>
public readonly struct ReplaySmoke
{
    /// <summary>This frame carries the smoke channels. A frame made without them (default) is an older recording.</summary>
    public readonly bool Recorded;
    /// <summary>The smoke switch was on.</summary>
    public readonly bool On;
    /// <summary>The flow the oil made, or would have made, with the switch on (0 … 1): 0 with the engine stopped or the bus
    /// dead, as live (SmokeController.Flow).</summary>
    public readonly float FlowIfOn;
    /// <summary>The exhaust jet's exit speed (m/s), the air's density, the engine rpm and the crank's revolutions: the trail
    /// is drawn from these exactly as live (the pulse tubes are timed by the crank).</summary>
    public readonly double JetU0, AirDensity, Crank;
    public readonly float Rpm;

    public ReplaySmoke(bool on, float flowIfOn, double jetU0, double airDensity, double crank, float rpm)
    {
        Recorded = true; On = on; FlowIfOn = flowIfOn; JetU0 = jetU0; AirDensity = airDensity; Crank = crank; Rpm = rpm;
    }

    /// <summary>The oil's flow with the switch on, now: none with the engine stopped, more with power, because the exhaust
    /// carries it; a glider's canisters are always full; the pump is electric, so none with the bus dead. The one law the
    /// live trail (SmokeController.Flow) and the recording both use.</summary>
    public static float FlowIfOnNow(Aircraft ac)
    {
        if (ac == null) return 0f;
        if (ac.Config.Propulsion == null) return 1f;                  // wingtip canisters
        if (!ac.Electrics.BusLive) return 0f;                         // the pump has no power
        if (!ac.EngineRunning || ac.EngineRpm < 100) return 0f;       // no exhaust (a windmilling dead engine has none)
        return (float)(0.25 + 0.75 * System.Math.Clamp(ac.ShaftPower01, 0, 1));
    }

    /// <summary>The exhaust jet's exit speed now (m/s): the smoke lane's exhaust model, fed what the engine really does.</summary>
    public static double JetU0Now(Aircraft ac, double airDensity)
    {
        var pr = ac?.Config.Propulsion;
        if (pr == null) return 0;
        if (pr.PropDiameterM <= 0) return 25.0 * System.Math.Clamp(ac.Throttle01, 0, 1);
        return ExhaustJet.ExitVelocity(ac.Config.Smoke, pr, ac.EngineRunning ? ac.EngineRpm : 0.0, ac.ShaftPower01, airDensity);
    }

    /// <summary>This moment's channels, for the recorder: the switch, and the rest from the aircraft as it is.</summary>
    public static ReplaySmoke Capture(Aircraft ac, bool on, double crank)
    {
        double rho = Atmosphere.DensityAtAltitude(-ac.State.Position.Z);
        float rpm = ac.Config.Propulsion != null && ac.Config.Propulsion.PropDiameterM > 0 ? (float)ac.EngineRpm : 0f;   // (a jet has no pulses)
        return new ReplaySmoke(on, FlowIfOnNow(ac), JetU0Now(ac, rho), rho, crank, rpm);
    }

    /// <summary>The flow as flown: the switch's state times what the oil made.</summary>
    public float AsFlown => Recorded && On ? FlowIfOn : 0f;

    /// <summary>Between two frames: the switch steps (a frame's state holds until the next), the rest is linear.</summary>
    public static ReplaySmoke Lerp(in ReplaySmoke a, in ReplaySmoke b, double u)
    {
        if (!a.Recorded || !b.Recorded) return u < 1 ? a : b;
        return new ReplaySmoke(a.On, (float)(a.FlowIfOn + (b.FlowIfOn - a.FlowIfOn) * u), a.JetU0 + (b.JetU0 - a.JetU0) * u,
            a.AirDensity + (b.AirDensity - a.AirDensity) * u, a.Crank + (b.Crank - a.Crank) * u, (float)(a.Rpm + (b.Rpm - a.Rpm) * u));
    }
}

/// <summary>How a replay shows the smoke (Ty t912u). A VIEW setting: it never changes the recording.</summary>
public enum SmokeReplayMode { AsFlown, On, Off }

/// <summary>One moment of a replayed trail: what <c>SmokeTrail.Sample</c> is built from (engine-agnostic, so the suite
/// tests what the game draws from).</summary>
public struct ReplaySmokePoint
{
    public double T; public Vec3 Pos; public Quat Att; public Vec3 VelBody, Rates; public float Flow; public double JetU0, AirDensity, Crank; public float Rpm;
}

/// <summary>The replay's smoke: which flow a frame shows in each mode, and the trail's samples at a play head.</summary>
public static class ReplaySmokeView
{
    /// <summary>Names for the replay bar.</summary>
    public static string Label(SmokeReplayMode m) => m switch { SmokeReplayMode.On => "ON", SmokeReplayMode.Off => "OFF", _ => "AS FLOWN" };
    public static SmokeReplayMode Next(SmokeReplayMode m) => m switch { SmokeReplayMode.AsFlown => SmokeReplayMode.On, SmokeReplayMode.On => SmokeReplayMode.Off, _ => SmokeReplayMode.AsFlown };

    /// <summary>The flow frame <paramref name="f"/> shows. As flown: exactly what was flown (an older frame without the
    /// channels shows none, which is what it recorded). On: what the oil would have made at that moment — for an older
    /// frame, estimated from the throttle as live does from power (a glider's canisters: full). Off: none.</summary>
    public static float FlowFor(SmokeReplayMode mode, in ReplayFrame f, bool glider)
    {
        switch (mode)
        {
            case SmokeReplayMode.Off: return 0f;
            case SmokeReplayMode.On:
                if (f.Smoke.Recorded) return f.Smoke.FlowIfOn;
                return glider ? 1f : (float)(0.25 + 0.75 * System.Math.Clamp(f.Throttle01, 0, 1));
            default: return f.Smoke.AsFlown;
        }
    }

    /// <summary>Did this recording ever have the channels? (An older recording: the replay bar can say so.)</summary>
    public static bool AnyRecorded(FlightRecorder rec)
    {
        for (int i = rec.Count - 1; i >= 0; i--) if (rec.At(i).Smoke.Recorded) return true;
        return false;
    }

    /// <summary>The trail at play head <paramref name="head"/>, NEWEST FIRST (SmokeTrail.Build's contract): the frame at the
    /// head itself, then every recorded frame before it back to <paramref name="maxAgeS"/>. A pure function of the
    /// recording, the head and the mode — so seeking, rewinding and scrubbing redraw exactly the trail that was there, with
    /// no particles carried over from where the head was before. Returns false (and leaves the list empty) when nothing in
    /// the window makes smoke.</summary>
    public static bool Window(FlightRecorder rec, double head, SmokeReplayMode mode, bool glider, double maxAgeS, List<ReplaySmokePoint> into)
    {
        into.Clear();
        if (rec.Count == 0 || mode == SmokeReplayMode.Off) return false;
        head = System.Math.Clamp(head, rec.StartTime, rec.EndTime);
        bool any = false;
        ReplayFrame h = rec.Sample(head);
        Add(h, mode, glider, into, ref any);
        // The last frame strictly before the head (the head's own frame is the interpolated one above).
        int lo = 0, hi = rec.Count - 1;
        if (rec.At(0).Time >= head) return any ? true : Clear(into);
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (rec.At(mid).Time < head) lo = mid; else hi = mid; }
        int i = rec.At(hi).Time < head ? hi : lo;
        for (; i >= 0; i--)
        {
            ReplayFrame f = rec.At(i);
            if (head - f.Time > maxAgeS) break;
            Add(f, mode, glider, into, ref any);
        }
        return any ? true : Clear(into);
    }

    private static bool Clear(List<ReplaySmokePoint> l) { l.Clear(); return false; }

    private static void Add(in ReplayFrame f, SmokeReplayMode mode, bool glider, List<ReplaySmokePoint> into, ref bool any)
    {
        float flow = FlowFor(mode, f, glider);
        if (flow > 0.001f) any = true;
        bool ch = f.Smoke.Recorded;
        into.Add(new ReplaySmokePoint
        {
            T = f.Time, Pos = f.State.Position, Att = f.State.Attitude, VelBody = f.State.Velocity, Rates = f.State.Rates, Flow = flow,
            JetU0 = ch ? f.Smoke.JetU0 : 0, AirDensity = ch ? f.Smoke.AirDensity : 0, Crank = ch ? f.Smoke.Crank : 0, Rpm = ch ? f.Smoke.Rpm : 0f,
        });
    }
}

/// <summary>One recorded instant of a flight: everything the display needs to redraw it (pose, surfaces, power, gear, g).</summary>
public readonly struct ReplayFrame
{
    public readonly double Time;
    public readonly RigidBodyState State;
    public readonly ControlDeflections Deflections;
    public readonly double Throttle01, FlapFraction, GearExtension, LoadFactorZ;
    /// <summary>Local wind (world NED) at the aircraft when recorded — the relative-wind camera and airspeed need it.</summary>
    public readonly Vec3 WindWorld;
    /// <summary>The smoke as flown (default = not recorded: an older frame).</summary>
    public readonly ReplaySmoke Smoke;

    public ReplayFrame(double time, RigidBodyState state, ControlDeflections deflections, double throttle01, double flapFraction,
        double gearExtension, double loadFactorZ, Vec3 windWorld, ReplaySmoke smoke = default)
    {
        Time = time; State = state; Deflections = deflections; Throttle01 = throttle01; FlapFraction = flapFraction;
        GearExtension = gearExtension; LoadFactorZ = loadFactorZ; WindWorld = windWorld; Smoke = smoke;
    }

    public static ReplayFrame Capture(double time, Aircraft a, Vec3 windWorld, ReplaySmoke smoke = default) =>
        new(time, a.State, a.CurrentDeflections, a.Throttle01, a.FlapFraction, a.GearExtension, a.LoadFactorZ, windWorld, smoke);

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
            L(a.GearExtension, b.GearExtension, u), L(a.LoadFactorZ, b.LoadFactorZ, u), L(a.WindWorld, b.WindWorld, u), ReplaySmoke.Lerp(a.Smoke, b.Smoke, u));
    }
}
