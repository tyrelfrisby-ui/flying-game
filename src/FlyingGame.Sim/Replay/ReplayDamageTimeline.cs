using System.Collections.Generic;
using FlyingGame.Core;

namespace FlyingGame.Sim.Replay;

/// <summary>
/// WHEN each part broke, so a replay can draw the airframe as it was at any moment (Ty 2026-11-07: replaying a flight
/// that ended in a crash, "slight wing damage did show up on replay"). The pieces that came off are posed by
/// <see cref="ReplayPartTimeline"/>; this is the other half — the damage left in the aeroplane's OWN meshes, the cut
/// seams and bends, which the replay used to draw for every frame because it rebuilt nothing.
///
/// Damage only ever accumulates within one recording: nothing repairs itself mid-flight. So the set of damage at time t
/// is always a PREFIX of this list, which is what makes it cheap — scrubbing forward only adds a cut, and only rewinding
/// past a break needs the airframe rebuilt clean and the earlier cuts re-applied.
/// </summary>
public sealed class ReplayDamageTimeline
{
    private readonly List<(double T, AirframeComponent Comp)> _lost = new();

    /// <summary>How many parts the recording knows about.</summary>
    public int Count => _lost.Count;

    public double TimeAt(int i) => _lost[i].T;
    public AirframeComponent ComponentAt(int i) => _lost[i].Comp;

    /// <summary>Note that <paramref name="comp"/> broke at <paramref name="t"/>. The first loss wins: a part that is
    /// already gone cannot break again, and re-reporting it must not move when it happened.</summary>
    public void Add(double t, AirframeComponent comp)
    {
        foreach ((double _, AirframeComponent c) in _lost) if (c == comp) return;
        if (_lost.Count > 0 && t < _lost[^1].T) t = _lost[^1].T;   // never out of order, whatever the caller's clock did
        _lost.Add((t, comp));
    }

    /// <summary>Is this part gone by <paramref name="t"/>?</summary>
    public bool IsLostAt(double t, AirframeComponent comp)
    {
        foreach ((double et, AirframeComponent c) in _lost) if (c == comp) return t >= et;
        return false;
    }

    /// <summary>How many parts are gone by <paramref name="t"/> — the length of the prefix to draw.</summary>
    public int CountAt(double t)
    {
        int n = 0;
        foreach ((double et, AirframeComponent _) in _lost) { if (et > t) break; n++; }
        return n;
    }

    /// <summary>The parts gone by <paramref name="t"/>, in the order they broke, appended to <paramref name="into"/>.</summary>
    public void LostAt(double t, List<AirframeComponent> into)
    {
        into.Clear();
        foreach ((double et, AirframeComponent c) in _lost) { if (et > t) break; into.Add(c); }
    }

    /// <summary>
    /// The rolling recorder has dropped everything before <paramref name="startT"/>. A part that broke before the window
    /// is gone for EVERY frame still in it, so its break moves up to the window's start rather than being forgotten —
    /// drop it and the replay would draw a wing that left minutes ago.
    /// </summary>
    public void TrimBefore(double startT)
    {
        for (int i = 0; i < _lost.Count; i++) if (_lost[i].T < startT) _lost[i] = (startT, _lost[i].Comp);
    }

    /// <summary>A fresh aeroplane: nothing has broken yet.</summary>
    public void Clear() => _lost.Clear();
}
