using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Loading;

/// <summary>
/// The arithmetic of weight and balance: where the mean aerodynamic chord is, what percentage of it a CG sits at,
/// and what a given load does to the aeroplane's mass, CG and inertia.
///
/// All of it is pure — geometry and a load in, numbers out. <see cref="LoadSheet"/> is the thing that holds a load
/// and writes the answer into a config.
/// </summary>
public static class WeightAndBalance
{
    /// <summary>
    /// Mean aerodynamic chord (m) and the x of its leading edge (body axes, + forward), measured from the wing
    /// strips. Cached per config: strip geometry never changes, not even when a wing is lost (damage masks strips,
    /// it does not delete them).
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftConfig, double[]> _mac = new();

    /// <summary>Mean aerodynamic chord, metres. Area-weighted chord over every surface whose id contains "wing".</summary>
    public static double MeanAerodynamicChord(AircraftConfig c) => MacGeom(c)[0];

    /// <summary>Body x of the MAC leading edge, metres (+ forward), i.e. the zero of percent MAC.</summary>
    public static double LeadingEdgeMacX(AircraftConfig c) => MacGeom(c)[1];

    private static double[] MacGeom(AircraftConfig c) => _mac.GetValue(c, cfg =>
    {
        // MAC = integral(c^2 dy) / integral(c dy) over the wing, where c is the chord of the whole SECTION: the
        // fixed strip plus the aileron hinged behind it. Taking the strips as they come counts an aileron row as
        // its own narrow little wing and drags the mean chord down — the 172 measured 50 in that way against the
        // 58.8 in on its type certificate, so every percent-MAC figure built on it would have been out by a sixth.
        // Paired, it measures 59.9 in.
        //
        // Strip Pos[0] is the QUARTER CHORD (AeroModel.BuildLoadX takes the leading edge as Pos[0] + 0.25*Chord),
        // so a section's leading edge is the fixed strip's, and the section quarter chord is a quarter of the
        // section chord aft of it.
        var fixedStrips = new List<StripConfig>();
        var hinged = new List<StripConfig>();
        foreach (SurfaceConfig sf in cfg.Surfaces)
        {
            if (!sf.Id.Contains("wing", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (StripConfig st in sf.Strips)
            {
                if (st.Chord <= 1e-9 || st.Area <= 0) continue;
                bool isHinged = st.Control is not null && Math.Abs(st.Control.Gain) >= 0.9;
                (isHinged ? hinged : fixedStrips).Add(st);
            }
        }
        double num = 0, den = 0, qcNum = 0;
        foreach (StripConfig st in fixedStrips)
        {
            double width = st.Area / st.Chord;
            StripConfig? pair = null;
            double best = double.MaxValue;
            foreach (StripConfig h in hinged)
            {
                double hw = h.Area / h.Chord, d = Math.Abs(h.Pos[1] - st.Pos[1]);
                if (d < 0.6 * Math.Max(width, hw) && d < best) { best = d; pair = h; }
            }
            double chord = st.Chord + (pair?.Chord ?? 0);
            double quarterChordX = st.Pos[0] + 0.25 * st.Chord - 0.25 * chord;
            num += chord * chord * width;
            den += chord * width;
            qcNum += quarterChordX * chord * width;
        }
        if (den <= 0) return new[] { 0.0, 0.0 };
        double mac = num / den;
        return new[] { mac, qcNum / den + 0.25 * mac };
    });

    /// <summary>
    /// Where a body-x arm sits on the chord, as a percentage: 0 % is the MAC leading edge, 100 % its trailing edge,
    /// and aft is a bigger number. Returns NaN for a type with no wing to measure (a rocket, a target drone body).
    /// </summary>
    public static double PercentMac(AircraftConfig c, double armX)
    {
        double[] g = MacGeom(c);
        return g[0] <= 1e-9 ? double.NaN : (g[1] - armX) / g[0] * 100.0;
    }

    /// <summary>The body-x arm of a percent-MAC station — the inverse of <see cref="PercentMac"/>.</summary>
    public static double ArmForPercentMac(AircraftConfig c, double percent)
    {
        double[] g = MacGeom(c);
        return g[1] - percent / 100.0 * g[0];
    }

    /// <summary>What the aeroplane weighs, where it balances, and what it resists being rotated about.</summary>
    public readonly struct Result
    {
        public readonly double MassKg;
        public readonly Vec3 Cg;
        public readonly double Ixx, Iyy, Izz, Ixz;
        public Result(double massKg, Vec3 cg, double ixx, double iyy, double izz, double ixz)
        {
            MassKg = massKg; Cg = cg; Ixx = ixx; Iyy = iyy; Izz = izz; Ixz = ixz;
        }
    }

    /// <summary>
    /// Add the empty aeroplane and a list of point masses together: total mass, the CG in all three axes (lateral
    /// too, so one full wing tank against one empty one is actually felt), and the inertia moved to the new CG by
    /// the parallel-axis theorem.
    ///
    /// Ixz is the product of inertia ∫xz dm, the sign convention <see cref="FlyingGame.Core.RigidBody6DOF"/> is written in. A mass
    /// low and ahead of the CG (dx &gt; 0, dz &gt; 0 with z down) adds positive Ixz.
    /// </summary>
    public static Result Solve(LoadingConfig loading, IEnumerable<(Vec3 Arm, double MassKg)> load)
    {
        double me = loading.EmptyMassKg;
        Vec3 ce = new(loading.EmptyCg[0], loading.EmptyCg[1], loading.EmptyCg[2]);

        double m = me;
        Vec3 moment = ce * me;
        var items = new List<(Vec3 Arm, double MassKg)>();
        foreach ((Vec3 arm, double kg) in load)
        {
            if (kg <= 0) continue;
            items.Add((arm, kg));
            m += kg;
            moment += arm * kg;
        }
        if (m <= 1e-9) return new Result(0, ce, 0, 0, 0, 0);
        Vec3 cg = moment / m;

        // Empty inertia is quoted about the EMPTY CG, so it first moves to the new CG, then every point mass is
        // added about that same point.
        InertiaConfig i0 = loading.EmptyInertia;
        Vec3 d = ce - cg;
        double ixx = i0.Ixx + me * (d.Y * d.Y + d.Z * d.Z);
        double iyy = i0.Iyy + me * (d.X * d.X + d.Z * d.Z);
        double izz = i0.Izz + me * (d.X * d.X + d.Y * d.Y);
        double ixz = i0.Ixz + me * (d.X * d.Z);
        foreach ((Vec3 arm, double kg) in items)
        {
            Vec3 r = arm - cg;
            ixx += kg * (r.Y * r.Y + r.Z * r.Z);
            iyy += kg * (r.X * r.X + r.Z * r.Z);
            izz += kg * (r.X * r.X + r.Y * r.Y);
            ixz += kg * (r.X * r.Z);
        }
        return new Result(m, cg, ixx, iyy, izz, ixz);
    }

    /// <summary>
    /// Is this (percent MAC, mass) point inside the certificated envelope? Ray casting on the polygon, with a point
    /// exactly on an edge counted as inside — a load at precisely max gross on the aft limit is legal, and a
    /// floating-point miss there would be a lie.
    /// </summary>
    public static bool InEnvelope(LoadingConfig loading, double percentMac, double massKg)
    {
        List<double[]> p = loading.Envelope;
        if (p.Count < 3 || double.IsNaN(percentMac)) return false;
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            double xi = p[i][0], yi = p[i][1], xj = p[j][0], yj = p[j][1];
            // On this edge?
            double cross = (xj - xi) * (massKg - yi) - (yj - yi) * (percentMac - xi);
            if (Math.Abs(cross) < 1e-6
                && percentMac >= Math.Min(xi, xj) - 1e-9 && percentMac <= Math.Max(xi, xj) + 1e-9
                && massKg >= Math.Min(yi, yj) - 1e-6 && massKg <= Math.Max(yi, yj) + 1e-6) return true;
            if (yi > massKg != yj > massKg && percentMac < (xj - xi) * (massKg - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>The fore-and-aft limits of the envelope at a given mass, in percent MAC. NaN when the mass is off the chart.</summary>
    public static (double Forward, double Aft) LimitsAt(LoadingConfig loading, double massKg)
    {
        List<double[]> p = loading.Envelope;
        double fwd = double.NaN, aft = double.NaN;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            double xi = p[i][0], yi = p[i][1], xj = p[j][0], yj = p[j][1];
            if (yi > massKg == yj > massKg) continue;
            double x = (xj - xi) * (massKg - yi) / (yj - yi) + xi;
            if (double.IsNaN(fwd) || x < fwd) fwd = x;
            if (double.IsNaN(aft) || x > aft) aft = x;
        }
        return (fwd, aft);
    }
}
