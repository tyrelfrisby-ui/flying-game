using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// The water's own velocity (Ty 2026-11-04: "What happens if a plane crashes in a river? It should be carried by the
/// current; model it correctly, and model current for floatplanes accurately too"). Every hydrodynamic force in the sim
/// (FloatHydro, WheelHydro on a wheel in water, HydroBody/WaterSlam for the airframe and its wreck) is computed from the
/// body's velocity RELATIVE TO THIS, exactly as the aero is computed from the velocity relative to the wind. Nothing
/// anywhere asks "upstream or downstream?": a take-off against the current is longer because the floats are moving
/// through the water faster at the same airspeed, which is all the physics has to know.
///
/// WHERE THE WATER RUNS (the island, <see cref="IslandWater.ColumnAt"/>):
///  - LAKES and the float ponds (the fixed water planes): still, 0.
///  - THE SEA (ground below 0, and a water lane at sea level): a rectilinear semidiurnal tidal stream,
///    U = <see cref="TidalAmplitudeMs"/> · sin(2π t / <see cref="TidalPeriodS"/>), flooding toward
///    <see cref="TidalFloodTrueDeg"/> true and ebbing the other way, depth-averaged, uniform over the sea. The M2
///    constituent's period (12.42 h, Doodson); 0.3 m/s (0.6 kt) is a modest open-coast tidal stream. Slack water at
///    sim time 0. This is a stated simplification: no rotary tide, no coastline steering, no tidal height change.
///  - THE STREAMS (the canyon rivers, <c>world["canyons"]</c>, and the glacial outwash braids): flowing along the
///    channel centreline, from its high end to its low end (the terrain's own height at the two ends decides it).
///    Speed by JARRETT (1984), "Hydraulics of high-gradient streams", J. Hydraul. Eng. 110(11): Manning's equation
///    with n = 0.32 S^0.38 R^−0.16 (SI; 0.39 with R in feet), fitted to 21 Colorado mountain rivers at S 0.002–0.04:
///        V = R^0.827 S^0.12 / 0.32.
///    S is the water surface's slope along the centreline, from the terrain sampled every 25 m and differenced over
///    ±100 m, held to Jarrett's fitted range. LATERAL PROFILE: the depth-averaged velocity at each point across the
///    channel is Jarrett's at the LOCAL depth (the divided-channel / lateral-distribution method, Chow 1959 §6-8) on a
///    parabolic section h(s) = D_max (1 − s²), s = 0 on the centreline, 1 on the bank: u(s) = U_c (1 − s²)^0.827, so it
///    is fastest mid-channel and 0 at the bank. The channel's depth comes from its width: a width/depth ratio of
///    <see cref="WidthToDepth"/> (gravel-bed mountain rivers run 10–20, Leopold &amp; Maddock 1953; Church 1992), so a
///    20 m stream is 1.3 m deep on average and 2.0 m on its centreline. The centreline speed is then held to
///    <see cref="MinRiverMs"/> … <see cref="MaxRiverMs"/> (2–8 kt, the brief's glacial/mountain range; Jarrett's
///    numbers land inside it here except on the steepest, deepest reach, which it caps).
///    BEND EDDY: behind each bend in a channel the flow separates from the INNER bank (Leeder &amp; Bridges 1975;
///    Ferguson, Parsons, Lane &amp; Hardy 2003, Water Resour. Res. 39(11): recirculation at sharp bends, back-flow of the
///    order of 0.2 of the channel speed). Over <see cref="BendEddyLengthWidths"/> channel widths below the bend, on the
///    inner 60 % of that half of the channel, the flow blends toward a back-current of <see cref="BendEddyFraction"/>
///    of the centreline speed, by sin(π a/L) sin(π (|s| − 0.4)/0.6) and by the turn (full at 60°).
///  - VERTICAL PROFILE: the log law of the wall, u(z) = (u*/κ) ln(z/z₀), whose depth average is (u*/κ)(ln(h/z₀) − 1),
///    so u(z)/ū = ln(z/z₀)/(ln(h/z₀) − 1) at height z above the bed (Soulsby 1997, Dynamics of Marine Sands §3.3). z₀
///    = k_s/30 with k_s = 3.5 D84 (Hey 1979) for a cobble bed of D84 = 0.1 m: <see cref="RiverBedZ0M"/>; for the sea's
///    sand <see cref="SeaBedZ0M"/> (Soulsby 1997 table 7). A body at the surface sees about 1.2 ū; one on the bed, 0.
///
/// NOTE ON THE ISLAND'S RIVERS: they are DRAWN as a water sheet 0.15 m over the canyon floor (IslandWater.WaterAt),
/// which is the rendering's choice and not the river's depth: a river that deep could not run at 2 kt (Jarrett at
/// R = 0.15 m gives 0.5 kt). The current uses the channel's hydraulic depth above; what floats and what grounds is
/// still decided by the drawn water and the ground under it, so a float in that sheet sits on the gravel.
///
/// Test hook: <see cref="Override"/> (thread-local, like <see cref="FloatHydro.FlatWaterOverride"/>). Under the flat
/// water override with no current override the water is still, so every existing flat-water test is unchanged.
/// </summary>
public static class WaterCurrent
{
    public const double RiverBedZ0M = 0.012;          // k_s/30, k_s = 3.5 · D84 (Hey 1979), D84 = 0.1 m cobble
    public const double SeaBedZ0M = 0.0004;           // sand (Soulsby 1997, table 7)
    public const double MinRiverMs = 2.0 * 0.514444;  // 2 kt
    public const double MaxRiverMs = 8.0 * 0.514444;  // 8 kt
    public const double JarrettMinSlope = 0.002, JarrettMaxSlope = 0.04;   // Jarrett 1984's fitted range
    public const double WidthToDepth = 15.0;          // bankfull width / mean depth, gravel-bed mountain rivers
    public const double BendEddyFraction = 0.2;       // back-flow at the inner bank below a bend, of the channel speed
    public const double BendEddyLengthWidths = 3.0;   // length of the separation zone, channel widths
    public const double TidalAmplitudeMs = 0.3;       // depth-averaged peak tidal stream, m/s (0.6 kt)
    public const double TidalPeriodS = 12.4206012 * 3600.0;   // M2
    public const double TidalFloodTrueDeg = 45.0;     // the flood sets toward 045° true; the ebb toward 225°

    /// <summary>Jarrett's (1984) mean velocity of a high-gradient stream, m/s: V = R^(2/3) S^(1/2) / n,
    /// n = 0.32 S^0.38 R^−0.16 (SI). S is held to the equation's fitted range.</summary>
    public static double JarrettVelocity(double hydraulicRadiusM, double slope)
    {
        if (hydraulicRadiusM <= 0) return 0;
        double s = System.Math.Clamp(slope, JarrettMinSlope, JarrettMaxSlope);
        return System.Math.Pow(hydraulicRadiusM, 2.0 / 3.0 + 0.16) * System.Math.Pow(s, 0.5 - 0.38) / 0.32;
    }

    /// <summary>
    /// The water column at <paramref name="s"/> across a channel (−1 … 1, + = left of the direction its path is drawn in;
    /// 0 on the centreline, ±1 on the banks) of half-width <paramref name="halfWidthM"/> and water-surface slope
    /// <paramref name="slope"/>, flowing along the unit horizontal (<paramref name="dirNorth"/>, <paramref name="dirEast"/>).
    /// <paramref name="turnRad"/> is the bend at the upstream end of this reach (+ = to the left in path order: the inner
    /// bank is then at s &gt; 0) and <paramref name="belowBendM"/> how far below it the point is. See the class summary.
    /// </summary>
    public static Column Channel(double dirNorth, double dirEast, double s, double halfWidthM, double slope, double turnRad, double belowBendM)
    {
        double dMax = 1.5 * 2 * halfWidthM / WidthToDepth;                 // parabolic section: D_max = 1.5 × the mean depth
        double uc = JarrettVelocity(dMax, slope);
        double k = System.Math.Clamp(uc, MinRiverMs, MaxRiverMs) / System.Math.Max(uc, 1e-9);
        double s2 = System.Math.Min(1, s * s), h = dMax * (1 - s2);
        double u = k * uc * System.Math.Pow(1 - s2, 2.0 / 3.0 + 0.16);
        double len = BendEddyLengthWidths * 2 * halfWidthM;
        if (turnRad != 0 && belowBendM > 0 && belowBendM < len && System.Math.Sign(s) == System.Math.Sign(turnRad) && System.Math.Abs(s) > 0.4)
        {
            double w = System.Math.Min(1, System.Math.Abs(turnRad) / (System.Math.PI / 3)) * System.Math.Sin(System.Math.PI * belowBendM / len)
                       * System.Math.Sin(System.Math.PI * (System.Math.Abs(s) - 0.4) / 0.6);
            u = u * (1 - w) - BendEddyFraction * k * uc * w;
        }
        return new Column(u * dirNorth, u * dirEast, System.Math.Max(0.05, h), RiverBedZ0M, false);
    }

    /// <summary>The log law's velocity at <paramref name="depthBelowSurfaceM"/> in a column <paramref name="columnDepthM"/>
    /// deep over a bed of roughness length <paramref name="z0M"/>, as a multiple of the depth average. 0 at and below the
    /// bed; about 1.2 at the surface of a river 2 m deep.</summary>
    public static double LogProfile(double depthBelowSurfaceM, double columnDepthM, double z0M)
    {
        double lnH = System.Math.Log(columnDepthM / z0M);
        if (lnH <= 1.5) return 1.0;                       // a film over the bed: no profile to speak of
        double z = columnDepthM - System.Math.Max(0.0, depthBelowSurfaceM);
        if (z <= z0M) return 0.0;
        return System.Math.Log(z / z0M) / (lnH - 1.0);
    }

    /// <summary>The tidal stream's phase factor, sin(2π t / T): 0 at slack (t = 0), +1 at the strongest flood.</summary>
    public static double TidalFactor(double simTimeS) => System.Math.Sin(2 * System.Math.PI * simTimeS / TidalPeriodS);

    /// <summary>The water column at a point: its depth-averaged velocity (world NED, horizontal), depth and bed.</summary>
    public readonly struct Column
    {
        public readonly double North, East;   // depth-averaged velocity, m/s; for the sea, at full flood (× TidalFactor)
        public readonly double DepthM, Z0M;
        public readonly bool Tidal;
        public Column(double north, double east, double depthM, double z0M, bool tidal)
        { North = north; East = east; DepthM = depthM; Z0M = z0M; Tidal = tidal; }
        public static readonly Column Still = new(0, 0, 0, RiverBedZ0M, false);
    }

    /// <summary>
    /// Test hook: the water's velocity (world NED, m/s) at (x, y, depth below the surface), in place of the world's.
    /// Null = the world's. THREAD-LOCAL for the reason <see cref="FloatHydro.FlatWaterOverride"/> is: xUnit runs test
    /// classes in parallel, and a shared static set by one would move the water under another. Set and clear it in the
    /// same synchronous scope (CLAUDE.md, "A thread-static override must be cleared on the thread that set it").
    /// </summary>
    public static System.Func<double, double, double, Vec3>? Override { get => _override; set => _override = value; }
    [System.ThreadStatic] private static System.Func<double, double, double, Vec3>? _override;

    /// <summary>A uniform current for tests: <paramref name="north"/>, <paramref name="east"/> m/s everywhere.</summary>
    public static System.Func<double, double, double, Vec3> Uniform(double north, double east)
    {
        var v = new Vec3(north, east, 0);
        return (_, _, _) => v;
    }

    // The last column per thread, reused only for the SAME point (bit for bit, as IslandTerrain.HeightKm's cache): a
    // nearby point's answer would make the result depend on the order of the queries, and AirframeContact's stage cull
    // must change nothing (GroundContactCullTests). The callers that ask twice for one point in a step still hit it.
    [System.ThreadStatic] private static double _cx, _cy;
    [System.ThreadStatic] private static Column _cc;
    [System.ThreadStatic] private static bool _cUsed;

    /// <summary>The water column under (x, y) (NED metres). Still with no world or under the flat-water test hook.</summary>
    public static Column ColumnAt(double x, double y)
    {
        if (FloatHydro.FlatWaterOverride.HasValue || WorldTerrain.Active is null) return Column.Still;
        if (_cUsed && x == _cx && y == _cy) return _cc;
        Column c = IslandWater.ColumnAt(new IslandPoint(y / 1000, x / 1000));
        _cx = x; _cy = y; _cc = c; _cUsed = true;
        return c;
    }

    /// <summary>The water's velocity (world NED, m/s; horizontal) at (x, y) and <paramref name="depthBelowSurfaceM"/>
    /// under its surface.</summary>
    public static Vec3 At(double x, double y, double depthBelowSurfaceM)
    {
        if (_override is { } o) return o(x, y, depthBelowSurfaceM);
        return Of(ColumnAt(x, y), depthBelowSurfaceM);
    }

    private static Vec3 Of(in Column c, double depth)
    {
        if (c.North == 0 && c.East == 0) return Vec3.Zero;
        double k = LogProfile(depth, c.DepthM, c.Z0M) * (c.Tidal ? TidalFactor(Atmosphere.SimTimeSec) : 1.0);
        return new Vec3(c.North * k, c.East * k, 0);
    }

    /// <summary>The water's velocity at the surface (a float, a slick).</summary>
    public static Vec3 SurfaceAt(double x, double y) => At(x, y, 0.0);

    /// <summary>The body's state with its velocity taken relative to water moving at <paramref name="waterWorld"/>
    /// (world): what every hydrodynamic force sees. Position, attitude and rates are the body's own.</summary>
    public static RigidBodyState Relative(in RigidBodyState s, Vec3 waterWorld) =>
        waterWorld.LengthSquared == 0 ? s
            : new RigidBodyState(s.Position, s.Attitude, s.Velocity - s.Attitude.Conjugate().Rotate(waterWorld), s.Rates);

    /// <summary>
    /// The current over a body's extent, for the per-element water forces (HydroBody, WaterSlam): the depth-averaged
    /// velocity at its centre and its horizontal gradient (central differences over the body's own size, so a long
    /// wreck across a river's shear feels the difference end to end and is turned by it), and the column's profile.
    /// Five column queries per body per step, however many elements it has.
    /// </summary>
    public readonly struct Local
    {
        private readonly double _x, _y, _gxx, _gxy, _gyx, _gyy, _depth, _z0, _cap;
        private readonly Vec3 _u;
        private readonly System.Func<double, double, double, Vec3>? _o;
        public readonly bool Moving;

        internal Local(double x, double y, Vec3 u, double gxx, double gxy, double gyx, double gyy, double depth, double z0, double cap)
        {
            _x = x; _y = y; _u = u; _gxx = gxx; _gxy = gxy; _gyx = gyx; _gyy = gyy; _depth = depth; _z0 = z0; _cap = cap; _o = null;
            Moving = cap > 0;
        }
        internal Local(System.Func<double, double, double, Vec3> o) : this() { _o = o; Moving = true; }

        public static readonly Local None = default;
        /// <summary>A current given point by point (a test world's): (x, y, depth below the surface) → world NED m/s.</summary>
        public static Local Of(System.Func<double, double, double, Vec3> field) => new(field);

        /// <summary>The water's velocity (world NED) at a world point <paramref name="depthBelowSurfaceM"/> under the
        /// surface. Linear in position over the body, held to the fastest water sampled around it.</summary>
        public Vec3 At(Vec3 world, double depthBelowSurfaceM)
        {
            if (_o != null) return _o(world.X, world.Y, depthBelowSurfaceM);
            if (!Moving) return Vec3.Zero;
            double dx = world.X - _x, dy = world.Y - _y;
            double ux = _u.X + _gxx * dx + _gxy * dy, uy = _u.Y + _gyx * dx + _gyy * dy;
            double m = System.Math.Sqrt(ux * ux + uy * uy);
            if (m > _cap) { ux *= _cap / m; uy *= _cap / m; }
            double k = LogProfile(depthBelowSurfaceM, _depth, _z0);
            return new Vec3(ux * k, uy * k, 0);
        }
    }

    /// <summary>The current around (x, y) over a body <paramref name="halfSpanM"/> in size (see <see cref="Local"/>).</summary>
    public static Local Around(double x, double y, double halfSpanM)
    {
        if (_override is { } o) return new Local(o);
        Column c = ColumnAt(x, y);
        double h = System.Math.Clamp(halfSpanM, 0.5, 6.0);
        Column xp = ColumnAt(x + h, y), xm = ColumnAt(x - h, y), yp = ColumnAt(x, y + h), ym = ColumnAt(x, y - h);
        double tide = TidalFactor(Atmosphere.SimTimeSec);
        double N(in Column k) => k.Tidal ? k.North * tide : k.North;
        double E(in Column k) => k.Tidal ? k.East * tide : k.East;
        double Speed(in Column k) => System.Math.Sqrt(N(k) * N(k) + E(k) * E(k));
        double cap = System.Math.Max(System.Math.Max(Speed(c), Speed(xp)), System.Math.Max(System.Math.Max(Speed(xm), Speed(yp)), Speed(ym)));
        if (cap == 0) return Local.None;
        double depth = c.DepthM > 0 ? c.DepthM : System.Math.Max(System.Math.Max(xp.DepthM, xm.DepthM), System.Math.Max(yp.DepthM, ym.DepthM));
        return new Local(x, y, new Vec3(N(c), E(c), 0),
            (N(xp) - N(xm)) / (2 * h), (N(yp) - N(ym)) / (2 * h), (E(xp) - E(xm)) / (2 * h), (E(yp) - E(ym)) / (2 * h),
            depth, c.DepthM > 0 ? c.Z0M : RiverBedZ0M, cap);
    }
}
