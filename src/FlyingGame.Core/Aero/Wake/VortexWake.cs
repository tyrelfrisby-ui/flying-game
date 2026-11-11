using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero.Wake;

/// <summary>
/// Every wake in the world (docs/WAKE-VORTEX.md): the registry of <see cref="WakeSource"/>s, their common clock, and
/// the air they move. Process-wide, like <see cref="Atmosphere"/>, and EMPTY unless something creates a source, so an
/// aeroplane nobody registered costs nothing and feels nothing; a test that creates sources calls <see cref="Clear"/>
/// in a finally. World frame NED, metres, m/s.
/// </summary>
public static class VortexWake
{
    private static readonly List<WakeSource> _sources = new();
    public static IReadOnlyList<WakeSource> Sources => _sources;
    /// <summary>True while any wake exists. Aircraft skip the whole wake path when false.</summary>
    public static bool Active => _sources.Count > 0;
    /// <summary>Total time advanced (s).</summary>
    public static double TimeSec { get; private set; }

    /// <summary>The wind that carries the lines; null = <see cref="Atmosphere.MeanWindAtPosition"/> (no gusts: the
    /// turbulence acts through the linking time instead).</summary>
    public static Func<Vec3, Vec3>? WindOverride { get; set; }
    /// <summary>Ambient ε (m²/s³); null = from <see cref="Atmosphere.ActiveTurbulence"/>, or calm air without one.</summary>
    public static double? EddyDissipationOverride { get; set; }
    /// <summary>Ground height (m, up) under a point; null = terrain or water, whichever is higher.</summary>
    public static Func<double, double, double>? GroundOverride { get; set; }

    public static double EddyDissipation => EddyDissipationOverride
        ?? (Atmosphere.ActiveTurbulence is { } t ? WakeVortex.EddyDissipation(t.RmsMs, t.LengthScaleM) : WakeVortex.CalmEddyDissipation);

    internal static Vec3 WindAt(Vec3 p) => WindOverride?.Invoke(p) ?? Atmosphere.MeanWindAtPosition(p);

    internal static double GroundUnder(Vec3 p)
    {
        if (GroundOverride is not null) return GroundOverride(p.X, p.Y);
        double g = WorldTerrain.GroundHeightAt(p.X, p.Y);
        double? w = FloatHydro.WaterSurfaceAt(p.X, p.Y);
        return w.HasValue && w.Value > g ? w.Value : g;
    }

    /// <summary>A new wake for an aeroplane of span <paramref name="spanM"/>. Shed into it every step; when the
    /// aeroplane goes, stop (it detaches by itself after <see cref="WakeSource.StaleSec"/>) or call Detach.</summary>
    public static WakeSource CreateSource(string label, double spanM)
    {
        var s = new WakeSource(label, spanM);
        _sources.Add(s);
        return s;
    }

    /// <summary>Remove every wake and every override.</summary>
    public static void Clear()
    {
        _sources.Clear();
        TimeSec = 0.0;
        WindOverride = null;
        EddyDissipationOverride = null;
        GroundOverride = null;
    }

    /// <summary>Age, decay and move every wake by <paramref name="dt"/> of sim time; drop those that have gone.</summary>
    public static void Advance(double dt)
    {
        if (dt <= 0.0) return;
        TimeSec += dt;
        for (int i = _sources.Count - 1; i >= 0; i--)
            if (!_sources[i].Advance(dt)) _sources.RemoveAt(i);
    }

    /// <summary>Segments (and slipstream pieces) that can reach anything inside the sphere. An aeroplane gathers once a
    /// step with a sphere round its whole airframe and evaluates its strips against the short list.</summary>
    public static void Gather(Vec3 centre, double radius, WakeSource? self, double selfMinAgeSec, List<VortexSegment>? vort, List<JetSegment>? jets)
    {
        vort?.Clear(); jets?.Clear();
        for (int i = 0; i < _sources.Count; i++)
        {
            WakeSource s = _sources[i];
            s.Gather(centre, radius, s == self ? selfMinAgeSec : 0.0, vort, jets);
        }
    }

    /// <summary>The air's velocity at <paramref name="p"/> from a gathered list. <paramref name="cullByInfluence"/>
    /// false skips each segment's own reach test (the gather has already made it for the whole airframe).</summary>
    public static Vec3 VelocityFrom(List<VortexSegment> vort, List<JetSegment>? jets, Vec3 p, bool cullByInfluence = true)
    {
        Vec3 v = Vec3.Zero;
        for (int i = 0; i < vort.Count; i++)
        {
            VortexSegment s = vort[i];
            if (cullByInfluence && WakeSource.DistanceToSegment(p, s.A, s.B) > s.InfluenceM) continue;
            v += WakeVortex.SegmentVelocity(p, s.A, s.B, s.Gamma, s.CoreRadius);
        }
        if (jets is not null) for (int i = 0; i < jets.Count; i++) v += jets[i].VelocityAt(p);
        return v;
    }

    private static readonly List<VortexSegment> _tmpV = new();
    private static readonly List<JetSegment> _tmpJ = new();

    /// <summary>The wake's air velocity at a point (m/s, world NED), every source included.</summary>
    public static Vec3 SampleVelocity(Vec3 p, WakeSource? exclude = null, double excludeYoungerThanSec = 0.0)
    {
        if (_sources.Count == 0) return Vec3.Zero;
        Gather(p, 0.0, exclude, excludeYoungerThanSec, _tmpV, _tmpJ);
        return VelocityFrom(_tmpV, _tmpJ, p);
    }

    /// <summary>The wake's vorticity at a point (1/s, world NED vector).</summary>
    public static Vec3 SampleVorticity(Vec3 p)
    {
        if (_sources.Count == 0) return Vec3.Zero;
        Gather(p, 0.0, null, 0.0, _tmpV, null);
        Vec3 w = Vec3.Zero;
        foreach (VortexSegment s in _tmpV) w += WakeVortex.SegmentVorticity(p, s.A, s.B, s.Gamma, s.CoreRadius);
        return w;
    }

    /// <summary>Every live segment in the world (cleared first). Returns the count.</summary>
    public static int GetActiveSegments(List<VortexSegment> into, List<JetSegment>? jets = null)
    {
        into.Clear(); jets?.Clear();
        for (int i = 0; i < _sources.Count; i++) _sources[i].Gather(Vec3.Zero, double.PositiveInfinity, 0.0, into, jets);
        return into.Count;
    }
}
