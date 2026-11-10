using System.Collections.Generic;
using FlyingGame.Core.Combat;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// The fire, the smoke and the fuel leak at one instant: the four flags <c>DamageFx</c> draws its three particle
/// systems a side from, and the one rule that turns them into what is on screen.
///
/// A holed tank streams fuel until it catches; once it is alight the stream is gone and what you see is flame and
/// black smoke. That rule lived in the renderer, where the suite could not reach it. It lives here now, so the drawn
/// answer is the tested answer (<see cref="LeakDrawnLeft"/> / <see cref="LeakDrawnRight"/>).
/// </summary>
public readonly struct DamageFxState
{
    public readonly bool FuelLeakLeft, FuelLeakRight, FireLeft, FireRight;

    public DamageFxState(bool fuelLeakLeft, bool fuelLeakRight, bool fireLeft, bool fireRight)
    {
        FuelLeakLeft = fuelLeakLeft; FuelLeakRight = fuelLeakRight; FireLeft = fireLeft; FireRight = fireRight;
    }

    /// <summary>This moment's flags, straight off the sim's damage state. A null state (an aeroplane with no damage
    /// bookkeeping at all) is nothing burning and nothing leaking.</summary>
    public static DamageFxState Capture(DamageState d) =>
        d == null ? default : new DamageFxState(d.FuelLeakLeft, d.FuelLeakRight, d.FireLeft, d.FireRight);

    /// <summary>A fuel stream is DRAWN on that side only until it catches: after that the same hole feeds the fire.</summary>
    public bool LeakDrawnLeft => FuelLeakLeft && !FireLeft;
    public bool LeakDrawnRight => FuelLeakRight && !FireRight;

    /// <summary>Anything at all to draw.</summary>
    public bool Any => FuelLeakLeft || FuelLeakRight || FireLeft || FireRight;

    public bool SameAs(in DamageFxState o) =>
        FuelLeakLeft == o.FuelLeakLeft && FuelLeakRight == o.FuelLeakRight
        && FireLeft == o.FireLeft && FireRight == o.FireRight;
}

/// <summary>
/// WHEN the aeroplane was alight and WHEN it was streaming fuel, so a replay shows the fire of the moment being
/// watched rather than the fire it ended on (Ty 2026-11-10: "Add the smoke fire fuel leak fixes then fold it in" —
/// the last of the three damage visuals that read the LIVE airframe through a playback).
///
/// Same fault as the bent wing and the propeller, and the same cure: a replay restores the pose and nothing else, so
/// <c>DamageFx.Update</c> was handed the live <see cref="DamageState"/> — which during a playback is whatever the
/// aeroplane ended up as. A flight that ended on fire burned from the first frame, before the bullet that lit it.
///
/// NOT THE SAME SHAPE OF TIMELINE AS <see cref="ReplayDamageTimeline"/>, and that is the whole reason this is its own
/// type. Damage never accumulates backwards, so which parts are LOST is always a prefix of a list. Fire is not: a
/// burning inner panel fails after DamageState.FireBurnSec and takes its own fire and leak with it
/// (<c>DamageState.Lose</c>), so the flags go back OFF at a moment that is not the end of the flight. A prefix cannot
/// say that. This keeps one entry per CHANGE and answers by the entry in force, which can.
///
/// It is also kept apart from <see cref="AirframeShape"/> on purpose. That is the airframe's SHAPE — bends, crush,
/// blade spans, geometry — and a fire changes none of it; folding these in would make a fire report that the shape
/// had changed and make SameShapeAs a lie. Fire is per-aircraft state that other aeroplanes have too (the drones and
/// the opponent draw their own DamageFx), which a timeline can serve and a field on the player's shape cannot.
/// </summary>
public sealed class ReplayDamageFxTimeline
{
    private readonly List<(double T, DamageFxState S)> _states = new();

    public int Count => _states.Count;
    public double TimeAt(int i) => _states[i].T;
    public DamageFxState At(int i) => _states[i].S;

    /// <summary>Note the fire and leak at <paramref name="t"/>. Returns true if this was a CHANGE and was therefore
    /// kept — a flight that is never hit is one entry, as an untouched airframe is one shape.</summary>
    public bool Record(double t, Aircraft a) => Record(t, DamageFxState.Capture(a?.Damage));

    /// <summary>The same from the flags alone (the suite, and any aeroplane whose damage is kept elsewhere).</summary>
    public bool Record(double t, in DamageFxState s)
    {
        if (_states.Count > 0 && _states[_states.Count - 1].S.SameAs(s)) return false;
        if (_states.Count > 0 && t < _states[^1].T) t = _states[^1].T;   // never out of order, whatever the caller's clock did
        _states.Add((t, s));
        return true;
    }

    /// <summary>What was burning at <paramref name="t"/>: the last state the aeroplane was in at or before then. Before
    /// the first entry it is the earliest state known, as <see cref="FlightRecorder.Sample"/> clamps to its nearest
    /// end. False only when nothing has been recorded at all.</summary>
    public bool TryStateAt(double t, out DamageFxState state)
    {
        if (_states.Count == 0) { state = default; return false; }
        int lo = 0, hi = _states.Count - 1, found = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_states[mid].T <= t) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        state = _states[found].S;
        return true;
    }

    /// <summary>The recording's ring has dropped everything before <paramref name="startT"/>. The state in force at
    /// that moment must survive — drop it and the replay puts out a fire that is still burning — so the last entry at
    /// or before it is carried forward to the new start and the rest go.</summary>
    public void TrimBefore(double startT)
    {
        int keep = -1;
        for (int i = 0; i < _states.Count; i++) if (_states[i].T <= startT) keep = i; else break;
        if (keep < 0) return;
        DamageFxState carried = _states[keep].S;
        _states.RemoveRange(0, keep);
        _states[0] = (startT, carried);
    }

    /// <summary>A fresh aeroplane: nothing alight and nothing leaking.</summary>
    public void Clear() => _states.Clear();
}
