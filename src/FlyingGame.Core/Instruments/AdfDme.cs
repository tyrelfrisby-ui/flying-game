using FlyingGame.Core.MathTypes;
using FlyingGame.Core.Nav;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// The ADF: a low-frequency receiver, a loop-and-sense antenna, and a needle on a servo that points at the
/// beacon RELATIVE TO THE NOSE. The card is the 1970s kind — not slaved, turned by hand with the knob — so with
/// the card at 000 the needle is a relative bearing, and with the heading set under the top index it is a
/// magnetic bearing, if the pilot keeps it set.
///
/// The bearing is measured in the AIRFRAME'S plane, because that is where the loop is: the signal arrives
/// along the ground (an LF ground wave), the loop turns in the plane of the fuselage floor, so the needle shows
/// the direction to the beacon projected onto that plane. Level, that is the relative bearing. Banked, it is
/// not — the "dip error" a student sees on an ADF turn — and nothing extra has to be written for it. Terrain
/// bending (mountain effect) comes from <see cref="NavReception.NdbBearingErrorDeg"/>.
///
/// The needle is a servo: it follows the receiver's bearing with a 0.3 s lag and slews at no more than 90 °/s
/// (E). With no signal there is no error voltage, so it stops where it was — a dead ADF does not centre.
/// </summary>
public sealed class AdfReceiver
{
    public const double NeedleLagS = 0.3, NeedleSlewDegS = 90.0, MinSignal = 0.2, MinVolts = 10.0, SignalPeriodS = 0.5;

    public double FreqKhz;
    /// <summary>The card under the needle, degrees: the number at the top index. Turned by the knob.</summary>
    public double CardDeg;
    public bool Failed;

    /// <summary>Where the needle points, degrees clockwise from the nose (a relative bearing on a 000 card).</summary>
    public double NeedleDeg { get; private set; }
    /// <summary>The number on the card under the needle's head.</summary>
    public double NeedleOnCardDeg => V.Wrap360(CardDeg + NeedleDeg);
    public bool Receiving { get; private set; }
    public string Ident { get; private set; } = "";

    private double _sigClock = double.MaxValue, _signal, _terrainErr, _lastFreq = double.NaN;

    public AdfReceiver(double freqKhz) { FreqKhz = freqKhz; }

    /// <summary>The bearing the loop finds, degrees clockwise from the nose in the airframe's plane, before the servo.</summary>
    public static double LoopBearingDeg(Quat attitude, Vec3 posWorld, double stationX, double stationY, double errDeg)
    {
        double brg = NavReception.BearingToDeg(posWorld.X, posWorld.Y, stationX, stationY) + errDeg;
        Vec3 dir = new(Math.Cos(brg * V.Deg), Math.Sin(brg * V.Deg), 0);    // the ground wave: horizontal
        Vec3 b = attitude.Conjugate().Rotate(dir);
        return V.Wrap360(Math.Atan2(b.Y, b.X) / V.Deg);
    }

    public void Step(double dt, Quat attitude, Vec3 posWorld, double volts, WorldTerrain? terrain)
    {
        Receiving = false; Ident = "";
        if (Failed || volts < MinVolts) return;
        if (!Navaids_TryNdb(FreqKhz, out Ndb n)) return;
        Ident = n.Id;
        if (FreqKhz != _lastFreq) { _sigClock = double.MaxValue; _lastFreq = FreqKhz; }
        _sigClock += dt;
        if (_sigClock >= SignalPeriodS)
        {
            _sigClock = 0;
            _signal = NavReception.NdbSignal(n, posWorld.X, posWorld.Y);
            _terrainErr = terrain == null ? 0.0 : NavReception.NdbBearingErrorDeg(terrain, n, posWorld.X, posWorld.Y, -posWorld.Z);
        }
        if (_signal < MinSignal) return;
        Receiving = true;
        double target = LoopBearingDeg(attitude, posWorld, n.X, n.Y, _terrainErr);
        double err = NavReception.AngleDiff(NeedleDeg, target);
        double move = err * (1.0 - Math.Exp(-dt / NeedleLagS));
        double lim = NeedleSlewDegS * dt;
        NeedleDeg = V.Wrap360(NeedleDeg + Math.Clamp(move, -lim, lim));
    }

    /// <summary>Put the needle where the servo would have taken it.</summary>
    public void Settle(Quat attitude, Vec3 posWorld, double volts, WorldTerrain? terrain)
    {
        _sigClock = double.MaxValue;
        for (int i = 0; i < 40; i++) Step(0.1, attitude, posWorld, volts, terrain);
    }

    private static bool Navaids_TryNdb(double khz, out Ndb n)
    {
        foreach (Ndb x in Navaids.Ndbs) if (Math.Abs(x.FreqKhz - khz) < 0.5) { n = x; return true; }
        n = default; return false;
    }
}

/// <summary>Where the DME takes its frequency from: the NAV1 or NAV2 selector, or HOLD — keep the last one while
/// the NAV radio is retuned (the 1970s Narco/King units all had this switch).</summary>
public enum DmeSource { Nav1, Nav2, Hold }

/// <summary>
/// The DME, its own box: an interrogator that times the round trip of a pulse pair to the ground transponder.
///
///  - **Distance is slant range**, straight through the air to the antenna. Overhead at 6,000 ft it reads
///    about a mile, and that is the instrument, not an error.
///  - **Ground speed is the rate of change of that range**, smoothed over a few seconds (E 4 s). Flying
///    straight at the station it is your ground speed; flying past it, it falls to zero abeam; and close in
///    at height it under-reads because the slant range changes slower than the ground distance does.
///  - **Time to station** is range over that rate, shown only while closing.
///  - **Search and memory**: on a new channel, or after losing the station for more than its 10 s memory, it
///    searches (dashes, no readings) for 2 s (E) before it locks. During memory it carries on from the last
///    reply rather than drop out over one ridge.
///  - It is UHF (962–1213 MHz): line of sight through <see cref="NavReception.PathSignal"/> at 1,000 MHz.
/// </summary>
public sealed class DmeUnit
{
    public const double SearchS = 2.0, MemoryS = 10.0, GsSmoothS = 4.0, DmeFreqMhz = 1000.0, MinVolts = 10.0, SignalPeriodS = 0.2;

    public DmeSource Source = DmeSource.Nav1;
    public bool Failed;
    /// <summary>The frequency HOLD is keeping.</summary>
    public double HeldFreqMhz { get; private set; } = double.NaN;

    public bool Valid { get; private set; }
    public double DistanceNm { get; private set; }
    public double GroundSpeedKt { get; private set; }
    /// <summary>Minutes to the station while closing; NaN otherwise.</summary>
    public double TimeToStationMin { get; private set; } = double.NaN;
    public string Ident { get; private set; } = "";

    private double _freq = double.NaN, _search, _sinceReply = double.MaxValue, _sigClock = double.MaxValue, _signal;
    private double _lastRange = double.NaN, _rate;

    /// <param name="nav1Mhz">NAV1's frequency.</param>
    /// <param name="nav2Mhz">NAV2's frequency.</param>
    public void Step(double dt, double nav1Mhz, double nav2Mhz, Vec3 posWorld, double volts, WorldTerrain? terrain)
    {
        double f = Source switch
        {
            DmeSource.Nav1 => nav1Mhz,
            DmeSource.Nav2 => nav2Mhz,
            _ => double.IsNaN(HeldFreqMhz) ? nav1Mhz : HeldFreqMhz,
        };
        if (Source != DmeSource.Hold) HeldFreqMhz = f;
        if (Failed || volts < MinVolts) { Reset(); _freq = double.NaN; return; }
        if (f != _freq) { _freq = f; Reset(); _search = 0; _sigClock = double.MaxValue; }

        Ident = "";
        if (!NavReceiver.TryVor(f, out Vor v) || !v.HasDme) { Reset(); return; }
        Ident = v.Id;
        _sigClock += dt;
        if (_sigClock >= SignalPeriodS)
        {
            _sigClock = 0;
            _signal = terrain == null ? 1.0 : NavReception.PathSignal(terrain, v.X, v.Y, v.AntennaM, posWorld.X, posWorld.Y, -posWorld.Z, DmeFreqMhz);
        }
        bool reply = _signal >= Airways.UsableSignal;
        double range = NavReception.SlantRangeNm(v.X, v.Y, v.AntennaM, posWorld.X, posWorld.Y, -posWorld.Z);

        if (reply) _sinceReply = 0; else _sinceReply += dt;
        if (!Valid)
        {
            // Searching: needs replies for the whole search time.
            _search = reply ? _search + dt : 0;
            if (_search < SearchS) return;
            Valid = true; _lastRange = range; _rate = 0; DistanceNm = range;
        }
        if (_sinceReply > MemoryS) { Reset(); _search = 0; return; }
        if (!reply) return;   // memory: hold the last readings

        double r = (_lastRange - range) / Math.Max(dt, 1e-6) * 3600.0;   // knots, + = closing
        _rate += (r - _rate) * (1.0 - Math.Exp(-dt / GsSmoothS));
        _lastRange = range;
        DistanceNm = range;
        GroundSpeedKt = Math.Abs(_rate);
        TimeToStationMin = _rate > 1.0 ? range / _rate * 60.0 : double.NaN;
    }

    private void Reset()
    {
        Valid = false; DistanceNm = 0; GroundSpeedKt = 0; TimeToStationMin = double.NaN; _lastRange = double.NaN;
    }
}
