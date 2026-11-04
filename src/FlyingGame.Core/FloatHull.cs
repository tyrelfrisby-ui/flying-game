using System.Runtime.CompilerServices;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// One float (or a flying-boat hull) as a SECTIONAL hull: stations from bow to stern, each a closed polygon in the
/// station plane (y across from the float's own centreline, z DOWN, config datum frame), forebody and afterbody split
/// at the step. Everything FloatHydro and the spray need is a cut of these sections by the instantaneous water plane
/// (heel, trim and heave all go in through the plane), so buoyancy, the centre of buoyancy, the waterplane, the spray
/// root and the wetted surface come from one geometry (owner 2026-11-02: "do a deep dive on the hydrodynamics").
///
/// Where the stations come from: <see cref="FloatsConfig.Stations"/> when the config has the drawn hull (the J-3: cut
/// out of our GLB by tools/own_models/j3_float_stations.py, the floats drawn off Piper/EDO dwg 60-S-908A), else they
/// are generated from the block parameters (length, beam, depth, deadrise, the taper fractions), which is the shape
/// every other float type has always floated on.
/// </summary>
public sealed class FloatHull
{
    /// <summary>Station x (datum frame, bow first).</summary>
    public readonly double[] X;
    /// <summary>Closed polygon per station: (y, z) pairs, port deck edge → keel → starboard deck edge.</summary>
    public readonly (double Y, double Z)[][] Poly;
    /// <summary>True for afterbody stations (behind the step).</summary>
    public readonly bool[] Aft;
    /// <summary>Integration weight (m) per station: half of each adjoining segment within the same body.</summary>
    public readonly double[] W;
    public readonly int LastFore, FirstAft;
    public readonly double StepX, BowX, SternX;
    /// <summary>Chine beam at the step (m) and the forebody deadrise there (rad), from the step section's own geometry.</summary>
    public readonly double Beam, DeadriseRad;
    /// <summary>Keel z at the step (forebody side) and the forebody keel's slope dz/dx near the step (rises toward the bow: &lt; 0).</summary>
    public readonly double KeelZStep, ForeKeelSlope;

    private FloatHull(double[] x, (double, double)[][] poly, double stepX)
    {
        X = x; Poly = poly; StepX = stepX;
        int n = x.Length;
        Aft = new bool[n];
        for (int i = 0; i < n; i++) Aft[i] = x[i] < stepX;
        LastFore = -1; FirstAft = n;
        for (int i = 0; i < n; i++) { if (!Aft[i]) LastFore = i; else if (FirstAft == n) FirstAft = i; }
        W = new double[n];
        for (int i = 0; i + 1 < n; i++)
        {
            if (Aft[i] != Aft[i + 1]) continue;   // the step: forebody and afterbody are integrated separately
            double d = x[i] - x[i + 1];
            W[i] += 0.5 * d; W[i + 1] += 0.5 * d;
        }
        BowX = x[0]; SternX = x[n - 1];
        var step = poly[LastFore];
        double half = 0, keel = double.MinValue, chineZ = 0;
        foreach ((double y, double z) in step) { if (y > half) { half = y; chineZ = z; } if (z > keel) keel = z; }
        Beam = 2 * half; KeelZStep = keel;
        DeadriseRad = half > 1e-6 ? System.Math.Atan2(System.Math.Max(keel - chineZ, 1e-4), half) : 20 * System.Math.PI / 180;
        // Forebody keel slope over the prismatic part: from the step to the first forebody station at least a beam ahead.
        int j = LastFore;
        while (j > 0 && x[j] - stepX < Beam) j--;
        ForeKeelSlope = x[j] - x[LastFore] > 1e-6 ? (KeelOf(j) - keel) / (x[j] - x[LastFore]) : 0.0;
    }

    public double KeelOf(int i) { double k = double.MinValue; foreach (var p in Poly[i]) if (p.Z > k) k = p.Z; return k; }
    public double DeckOf(int i) { double k = double.MaxValue; foreach (var p in Poly[i]) if (p.Z < k) k = p.Z; return k; }
    public double ForebodyLength => BowX - StepX;

    private static readonly ConditionalWeakTable<FloatsConfig, FloatHull> Cache = new();

    /// <summary>The hull for a float block (built once per config object).</summary>
    public static FloatHull For(FloatsConfig f) => Cache.GetValue(f, Build);

    private static FloatHull Build(FloatsConfig f)
    {
        if (f.Stations is { Length: >= 3 } st)
        {
            var xs = new double[st.Length];
            var polys = new (double, double)[st.Length][];
            for (int i = 0; i < st.Length; i++)
            {
                double[] r = st[i];
                xs[i] = r[0];
                int m = (r.Length - 1) / 2;           // half-section points, deck edge first, keel last (y = 0)
                var half = new (double Y, double Z)[m];
                for (int k = 0; k < m; k++) half[k] = (r[1 + 2 * k], r[2 + 2 * k]);
                polys[i] = Mirror(half);
            }
            return new FloatHull(xs, polys, f.StepX ?? (f.BowX - f.StepFraction * f.LengthM));
        }
        return Parametric(f);
    }

    /// <summary>A half section (deck edge → keel, y ≥ 0) mirrored into a closed polygon port → keel → starboard.</summary>
    private static (double, double)[] Mirror((double Y, double Z)[] half)
    {
        var p = new System.Collections.Generic.List<(double, double)>(2 * half.Length);
        for (int k = 0; k < half.Length; k++) p.Add((-half[k].Y, half[k].Z));
        for (int k = half.Length - 2; k >= 0; k--) p.Add((half[k].Y, half[k].Z));
        return p.ToArray();
    }

    /// <summary>The block float as stations: V bottom of the configured deadrise to the chine, vertical sides to the deck,
    /// beam and depth tapered by <see cref="FloatHydro.Taper"/> (the shape these floats have always had).</summary>
    private static FloatHull Parametric(FloatsConfig f)
    {
        const int nFore = 16, nAft = 12;
        double xStep = f.BowX - f.StepFraction * f.LengthM, xStern = f.BowX - f.LengthM;
        double tanB = System.Math.Tan(f.DeadriseDeg * System.Math.PI / 180);
        double fk = System.Math.Tan(f.ForebodyKeelDeg * System.Math.PI / 180), ak = System.Math.Tan(f.AfterbodyKeelDeg * System.Math.PI / 180);
        var xs = new System.Collections.Generic.List<double>();
        var ps = new System.Collections.Generic.List<(double, double)[]>();
        void Add(double x, bool fore, double u)
        {
            (double bF, double dF) = FloatHydro.Taper(f, fore, u);
            double half = 0.5 * f.BeamM * bF;
            double keel = fore ? f.KeelZ - (x - xStep) * fk : f.KeelZ - (xStep - x) * ak;
            double chine = keel - half * tanB, deck = keel - f.DepthM * dF;
            if (chine < deck) chine = deck;
            xs.Add(x);
            ps.Add(Mirror(new[] { (half, deck), (half, chine), (0.0, keel) }));
        }
        for (int i = 0; i <= nFore; i++) { double u = 1.0 - (double)i / nFore; Add(xStep + u * (f.BowX - xStep), true, u); }
        for (int i = 0; i <= nAft; i++) { double u = (double)i / nAft; Add(xStep - 1e-3 - u * (xStep - 1e-3 - xStern), false, u); }
        return new FloatHull(xs.ToArray(), ps.ToArray(), xStep - 0.5e-3);
    }

    // ---- the cut ------------------------------------------------------------------------------------------------

    /// <summary>What one station's section has under the water.</summary>
    public struct Cut
    {
        public double Area;                  // m²
        public double Cy, Cz;                // centroid (station plane, y from the float centreline, z down)
        public double Q;                     // first moment of depth over the area, ∫ h dA (m³): the face's hydrostatic load / ρg
        public double Girth;                 // wetted perimeter (m), the waterline chord excluded
        public double KeelDepth;             // depth of the keel below the water (m; negative = clear)
        public bool PortCross, StbdCross;    // the waterline meets the port / starboard half of the section
        public double PortY, PortZ, StbdY, StbdZ;      // ...there
        public int PortEdge, StbdEdge;                 // polygon edge index (k → k+1) of those crossings
        public double WetTopZ;               // shallowest immersed point (z down): the waterline at this station's centreline
    }

    /// <summary>
    /// Cut station <paramref name="i"/> by the water: depth h(y, z) = h0 + hy·y + hz·z (m, positive under water) is the
    /// instantaneous water plane in the station's own coordinates, linear because the plane is (heel and trim are in
    /// hy, hz; heave and the wave height in h0). Sutherland–Hodgman clip of the polygon to h &gt; 0, then the area,
    /// centroid and depth moment of what is left, and where the waterline crosses the hull on each side.
    /// </summary>
    public void CutStation(int i, double h0, double hy, double hz, ref Cut c, double[] scratchY, double[] scratchZ)
    {
        var poly = Poly[i];
        int n = poly.Length, m = 0;
        c = default;
        c.KeelDepth = double.MinValue;
        c.WetTopZ = double.MaxValue;
        for (int k = 0; k < n; k++)
        {
            (double ya, double za) = poly[k];
            (double yb, double zb) = poly[(k + 1) % n];
            double ha = h0 + hy * ya + hz * za, hb = h0 + hy * yb + hz * zb;
            if (ha > c.KeelDepth && ya == 0.0) c.KeelDepth = ha;
            bool ina = ha > 0, inb = hb > 0;
            if (ina) { scratchY[m] = ya; scratchZ[m] = za; m++; }
            if (ina && inb)
            {
                c.Girth += System.Math.Sqrt((yb - ya) * (yb - ya) + (zb - za) * (zb - za));
            }
            else if (ina != inb)
            {
                double t = ha / (ha - hb);
                double yc = ya + t * (yb - ya), zc = za + t * (zb - za);
                scratchY[m] = yc; scratchZ[m] = zc; m++;
                double ywet = ina ? ya : yb, zwet = ina ? za : zb;
                c.Girth += System.Math.Sqrt((yc - ywet) * (yc - ywet) + (zc - zwet) * (zc - zwet));
                // Which side: the crossing's own y (the keel is y = 0; deck-to-deck along the top never crosses
                // unless the deck is awash, which is then also recorded).
                if (yc < 0 || (yc == 0 && ya < 0)) { c.PortCross = true; c.PortY = yc; c.PortZ = zc; c.PortEdge = k; }
                else { c.StbdCross = true; c.StbdY = yc; c.StbdZ = zc; c.StbdEdge = k; }
            }
        }
        if (c.KeelDepth == double.MinValue)
        {
            foreach ((double y, double z) in poly) if (y == 0.0) c.KeelDepth = System.Math.Max(c.KeelDepth, h0 + hy * y + hz * z);
        }
        if (m < 3) return;
        double a2 = 0, cy = 0, cz = 0;
        for (int k = 0; k < m; k++)
        {
            int l = (k + 1) % m;
            double cr = scratchY[k] * scratchZ[l] - scratchY[l] * scratchZ[k];
            a2 += cr; cy += (scratchY[k] + scratchY[l]) * cr; cz += (scratchZ[k] + scratchZ[l]) * cr;
            if (scratchZ[k] < c.WetTopZ) c.WetTopZ = scratchZ[k];
        }
        if (System.Math.Abs(a2) < 1e-12) return;
        c.Area = 0.5 * System.Math.Abs(a2);
        c.Cy = cy / (3 * a2); c.Cz = cz / (3 * a2);
        c.Q = c.Area * (h0 + hy * c.Cy + hz * c.Cz);   // h is linear, so ∫h dA = A · h(centroid)
    }

    /// <summary>Scratch big enough for any station's clipped polygon.</summary>
    public int MaxPoly { get { int m = 0; foreach (var p in Poly) m = System.Math.Max(m, p.Length); return 2 * m + 2; } }

    /// <summary>The full section area at station i (the closed polygon).</summary>
    public double SectionArea(int i)
    {
        var p = Poly[i]; double a = 0;
        for (int k = 0; k < p.Length; k++) { var q = p[(k + 1) % p.Length]; a += p[k].Y * q.Z - q.Y * p[k].Z; }
        return 0.5 * System.Math.Abs(a);
    }

    /// <summary>Full section area at any x, interpolated between stations of the same body (0 outside the hull).</summary>
    public double SectionAreaAt(double x)
    {
        for (int i = 0; i + 1 < X.Length; i++)
        {
            if (x > X[i] || x < X[i + 1] || Aft[i] != Aft[i + 1]) continue;
            double t = (X[i] - x) / System.Math.Max(X[i] - X[i + 1], 1e-9);
            return (1 - t) * SectionArea(i) + t * SectionArea(i + 1);
        }
        return 0.0;
    }

    /// <summary>The whole hull's volume (m³): every section in full.</summary>
    public double Volume { get { double v = 0; for (int i = 0; i < X.Length; i++) v += SectionArea(i) * W[i]; return v; } }

    /// <summary>Half-breadth of section i at depth z (z down, datum frame), for the thin-ship wave integral: the widest
    /// |y| of the polygon at that z (0 above the deck or below the keel).</summary>
    public double HalfBreadth(int i, double z)
    {
        var p = Poly[i]; double best = 0;
        for (int k = 0; k < p.Length; k++)
        {
            var a = p[k]; var b = p[(k + 1) % p.Length];
            if ((a.Z - z) * (b.Z - z) > 0 || a.Z == b.Z) continue;
            double t = (z - a.Z) / (b.Z - a.Z);
            best = System.Math.Max(best, System.Math.Abs(a.Y + t * (b.Y - a.Y)));
        }
        return best;
    }

    // ---- level-trim hydrostatics (for the wave table and the reports) -------------------------------------------

    /// <summary>Displaced volume (m³) with a level waterline at z = <paramref name="zWl"/> (z down: the water is above
    /// every point with z &gt; zWl).</summary>
    public double VolumeBelow(double zWl)
    {
        var sy = new double[MaxPoly]; var sz = new double[MaxPoly];
        double v = 0; var c = new Cut();
        for (int i = 0; i < X.Length; i++) { CutStation(i, -zWl, 0, 1, ref c, sy, sz); v += c.Area * W[i]; }
        return v;
    }

    /// <summary>The level waterline (z down) at which this hull displaces <paramref name="volume"/> (bisection).</summary>
    public double WaterlineFor(double volume)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < X.Length; i++) { lo = System.Math.Min(lo, DeckOf(i)); hi = System.Math.Max(hi, KeelOf(i)); }
        for (int it = 0; it < 60; it++)
        {
            double mid = 0.5 * (lo + hi);
            if (VolumeBelow(mid) > volume) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    // ---- Michell's thin-ship wave resistance ------------------------------------------------------------------

    /// <summary>
    /// Wave-making resistance (N) of this hull alone in deep calm water at speed U, from MICHELL's integral (1898; in the
    /// form of Wehausen 1973, "The wave resistance of ships", Adv. Appl. Mech. 13, eq. 5.10 / Tuck 1989):
    ///   R = (4 ρ g² / π U²) ∫₁^∞ (I² + J²) λ² / √(λ² − 1) dλ,
    ///   I + iJ = ∫∫ ∂Y/∂x (x, z) · exp(−λ² k₀ z) · exp(i λ k₀ x) dx dz,   k₀ = g / U²,
    /// Y the hull's half-breadth, z the depth below the waterline. The hull is cut at the waterline z = zWl (level
    /// trim). The integral is taken exactly over panels on which Y is linear in x (the stations) and constant in z
    /// (Nz layers), so no grid has to resolve the short waves at large λ; λ = cosh u removes the λ = 1 singularity.
    /// Thin-ship theory wants B/L small: 0.12 for the J-3's float. Two floats' wave systems interfere; that is not in
    /// here (one hull, the result is per float).
    /// </summary>
    public double MichellResistance(double zWl, double u, double rho = 1000.0, double g = 9.81)
    {
        if (u < 0.05) return 0.0;
        const int nz = 10;
        double keel = double.MinValue;
        for (int i = 0; i < X.Length; i++) keel = System.Math.Max(keel, KeelOf(i));
        double depth = keel - zWl;
        if (depth <= 0) return 0.0;
        double dz = depth / nz;
        // Panels: (x1, x2, slope of Y in x, z1, z2), Y = half-breadth at each layer's mid depth.
        var panels = new System.Collections.Generic.List<(double x1, double x2, double fx, double z1, double z2)>();
        for (int i = 0; i + 1 < X.Length; i++)
        {
            double xa = X[i], xb = X[i + 1];
            if (xa - xb < 1e-9) continue;
            for (int l = 0; l < nz; l++)
            {
                double zm = zWl + (l + 0.5) * dz;
                double fa = HalfBreadth(i, zm), fb = HalfBreadth(i + 1, zm);
                if (fa == 0 && fb == 0) continue;
                panels.Add((xb, xa, (fa - fb) / (xa - xb), l * dz, (l + 1) * dz));
            }
        }
        double k0 = g / (u * u), sum = 0.0;
        const int nu = 500; const double uMax = 6.0;   // λ = cosh u up to ~200: past that the integrand is ~λ⁻⁴ and gone
        double du = uMax / nu;
        for (int s = 0; s <= nu; s++)
        {
            double uu = s * du, lam = System.Math.Cosh(uu);
            double re = 0, im = 0, kz = lam * lam * k0, kx = lam * k0;
            foreach (var p in panels)
            {
                double zint = (System.Math.Exp(-kz * p.z1) - System.Math.Exp(-kz * p.z2)) / kz;
                // ∫ e^{i kx x} dx = (sin kx x2 − sin kx x1)/kx + i (cos kx x1 − cos kx x2)/kx
                double xr = (System.Math.Sin(kx * p.x2) - System.Math.Sin(kx * p.x1)) / kx;
                double xi = (System.Math.Cos(kx * p.x1) - System.Math.Cos(kx * p.x2)) / kx;
                re += p.fx * zint * xr; im += p.fx * zint * xi;
            }
            // dλ / √(λ² − 1) = du, so the integrand is (I² + J²) λ² du.
            double wgt = (s == 0 || s == nu) ? 0.5 : 1.0;
            sum += wgt * (re * re + im * im) * lam * lam * du;
        }
        return 4.0 * rho * g * g / (System.Math.PI * u * u) * sum;
    }
}
