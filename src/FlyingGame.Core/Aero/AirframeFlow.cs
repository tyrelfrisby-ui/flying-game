using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// THE AIR AROUND THE AIRFRAME, for smoke (Ty 2026-10-13: "don't let the smoke pass through the airplane. it needs to go
/// around it … this will require some aerodynamic modeling"). A puff of smoke is a marker in the air; in the airplane's
/// (rotating) frame the air arrives at −(V + ω × r) and has to go AROUND the fuselage and the flying surfaces, not
/// through them. This is the near-field disturbance those bodies make, by superposition:
///  - FUSELAGE: slender-body theory — each cross-section a circle of radius R(x) in the crossflow (the 2-D doublet,
///    d = R²/ρ² (U⊥ − 2(U⊥·n)n), zero normal velocity on the surface); the axial flow passes.
///  - WING, STAB, FIN: each section a flat plate in the flow normal to it — the exact 2-D potential flow round a plate
///    (complex velocity u − iv = −iUn z / √(z² − a²)), so the air goes round the leading and trailing edges; the chordwise
///    and spanwise flow pass. Behind the plate the flow SEPARATES (a bluff plate at spin α, not the potential flow's
///    reattachment): on the lee side the disturbance is dropped, so air that has gone round an edge leaves along it.
///    The edge's infinite speed is capped at 2.5 × the onset.
/// A puff stepped through this field is also held out of every body (projected back to the surface), so a coarse step
/// can't tunnel through.
/// Body axes, metres from the CG (x fwd, y right, z down).
/// </summary>
public sealed class AirframeFlow
{
    private sealed class Plate
    {
        public bool Vertical;            // the fin (normal = y), else a horizontal surface (normal = z)
        public double[] S = System.Array.Empty<double>(), Le = System.Array.Empty<double>(), Te = System.Array.Empty<double>(), Plane = System.Array.Empty<double>();
        public double SMin, SMax;
        public double SInLo = double.MaxValue, SInHi = double.MinValue;   // the span the body owns, not the plate (empty: lo > hi)
    }

    private readonly List<Plate> _plates = new();
    private readonly double _noseX, _tailX, _r0, _axisZ;
    private double[] _px, _pr, _pz;   // a measured fuselage (stations x ascending, radius, axis height); null = the analytic shape

    /// <summary>The fuselage as measured on the model: at stations <paramref name="x"/> (descending, nose first), its radius
    /// and the height of its axis (body z).</summary>
    public void SetFuselage(double[] x, double[] radius, double[] axisZ) { if (x != null && x.Length >= 2) { _px = Rev(x); _pr = Rev(radius); _pz = Rev(axisZ); ClipPlatesToSkin(); Bound(); } }   // (kept tail first: ascending x)

    /// <summary>THE FUSELAGE AS DRAWN, for every type (Ty t914u): station by station along the model's vertices (body axes,
    /// m from the CG), the run of the centreline (|y| &lt; 0.1 m) that is the body — its skins joined across the walled
    /// gap between them, so a biplane's upper centre section over its cabane, or a tailwheel under its spring, is not
    /// taken for fuselage (a strut with vertices at mid-gap would join them: the keep-out is then too big, never too small) — its
    /// half-depth, widened to the body's half-width across the middle of that run (capped at 1.3 × the half-depth, so a
    /// mid-set wing root is not taken for it), and the height of its middle. The fin's root caps the run over the fin's
    /// chord. Returns false (and the analytic shape stays) when the points cannot give four stations. Shared by the game's
    /// smoke and the Aero Widget, so both keep smoke out of the airframe that is actually on screen.</summary>
    public static bool MeasureFuselage(IReadOnlyList<Vec3> pts, AircraftConfig cfg, out double[] xs, out double[] radius, out double[] axisZ)
    {
        xs = radius = axisZ = System.Array.Empty<double>();
        if (pts == null || pts.Count < 100) return false;
        double finRoot = double.NaN, finLe = double.MinValue, finTe = double.MaxValue;
        if (cfg != null)
        {
            Vec3 cg = cfg.Mass.CgVec();
            foreach (var sf in cfg.Surfaces)
            {
                bool vert = false; foreach (var st in sf.Strips) if (System.Math.Abs(st.Pos[2] - sf.Strips[0].Pos[2]) > 0.2) vert = true;
                if (!vert) continue;
                foreach (var st in sf.Strips) { double z = st.Pos[2] - cg.Z; if (double.IsNaN(finRoot) || z > finRoot) finRoot = z; finLe = System.Math.Max(finLe, st.Pos[0] - cg.X + 0.25 * st.Chord); finTe = System.Math.Min(finTe, st.Pos[0] - cg.X - 0.75 * st.Chord); }
            }
        }
        double nose = double.MinValue, tail = double.MaxValue;
        foreach (var p in pts) { nose = System.Math.Max(nose, p.X); tail = System.Math.Min(tail, p.X); }
        // Bin the points by station once (10 cm bins), so the scan is O(n), not O(n × stations).
        int nb = (int)System.Math.Ceiling((nose - tail) / 0.1) + 2;
        if (nb < 4 || nb > 2000) return false;
        var bins = new List<Vec3>[nb];
        foreach (var p in pts) { int b = (int)((nose - p.X) / 0.1); if (b >= 0 && b < nb) (bins[b] ??= new List<Vec3>()).Add(p); }
        var ox = new List<double>(); var orad = new List<double>(); var oz = new List<double>();
        var zc = new List<double>();
        for (int b = 0; b < nb; b++)
        {
            if (bins[b] == null) continue;
            double x = nose - 0.1 * (b + 0.5);
            zc.Clear();
            foreach (var p in bins[b]) if (System.Math.Abs(p.Y) < 0.1) zc.Add(p.Z);
            if (zc.Count < 2) continue;
            zc.Sort();
            // A model is a SHELL: on the centreline there are only skins (the top, the belly, an upper wing's two faces, a
            // tailwheel). Skins are joined into one body across a gap when the gap is small (< 0.25 m) or WALLED — the body's
            // own sides have points beside the centreline at mid-gap height; the air between a fuselage and the upper wing
            // over its cabane, or under it down to a tailwheel, has none. The longest joined run is the body.
            var runs = new List<(double lo, double hi)>();
            double lo = zc[0];
            for (int i = 1; i <= zc.Count; i++)
                if (i == zc.Count || zc[i] - zc[i - 1] > 0.06) { runs.Add((lo, zc[i - 1])); if (i < zc.Count) lo = zc[i]; }
            double bestLo = runs[0].lo, bestHi = runs[0].hi, curLo = runs[0].lo, curHi = runs[0].hi;
            for (int r = 1; r <= runs.Count; r++)
            {
                bool join = false;
                if (r < runs.Count)
                {
                    double g0 = curHi, g1 = runs[r].lo;
                    if (g1 - g0 < 0.25) join = true;
                    else
                    {
                        double m0 = g0 + (g1 - g0) / 3, m1 = g1 - (g1 - g0) / 3, wallMax = System.Math.Max(0.3, 0.75 * (g1 - curLo));
                        bool left = false, right = false;
                        foreach (var p in bins[b]) if (p.Z > m0 && p.Z < m1 && System.Math.Abs(p.Y) > 0.1 && System.Math.Abs(p.Y) < wallMax) { if (p.Y < 0) left = true; else right = true; }
                        join = left && right;
                    }
                }
                if (join) { curHi = runs[r].hi; continue; }
                if (curHi - curLo > bestHi - bestLo) { bestLo = curLo; bestHi = curHi; }
                if (r < runs.Count) { curLo = runs[r].lo; curHi = runs[r].hi; }
            }
            if (!double.IsNaN(finRoot) && x <= finLe + 0.1 && x >= finTe - 0.1) bestLo = System.Math.Max(bestLo, finRoot - 0.05);
            if (bestHi - bestLo < 0.02) continue;
            double hd = 0.5 * (bestHi - bestLo), mid = 0.5 * (bestHi + bestLo), hw = 0;
            foreach (var p in bins[b]) if (System.Math.Abs(p.Z - mid) < 0.5 * hd && System.Math.Abs(p.Y) < 1.3 * hd) hw = System.Math.Max(hw, System.Math.Abs(p.Y));
            ox.Add(x); orad.Add(System.Math.Max(hd, hw)); oz.Add(mid);
        }
        if (ox.Count < 4) return false;
        xs = ox.ToArray(); radius = orad.ToArray(); axisZ = oz.ToArray();
        return true;
    }

    private double AxisAt(double x)
    {
        if (_px == null) return _axisZ;
        return Lerp(_px, _pz, x);
    }
    private static double[] Rev(double[] a) { var b = (double[])a.Clone(); System.Array.Reverse(b); return b; }

    /// <param name="noseX">The spinner tip, m ahead of the CG.</param>
    /// <param name="radius">The fuselage's largest radius (m).</param>
    public AirframeFlow(AircraftConfig cfg, double noseX, double radius, double axisZ = 0)
    {
        Vec3 cg = cfg.Mass.CgVec();
        _noseX = noseX; _r0 = radius; _axisZ = axisZ;
        double tail = double.MaxValue;
        var raw = new List<(bool vert, List<(double s, double le, double te, double plane)> st)>();
        foreach (var sf in cfg.Surfaces)
        {
            if (sf.Strips.Count == 0) continue;
            double ySpread = 0, zSpread = 0;
            foreach (var st in sf.Strips) { ySpread = System.Math.Max(ySpread, System.Math.Abs(st.Pos[1] - sf.Strips[0].Pos[1])); zSpread = System.Math.Max(zSpread, System.Math.Abs(st.Pos[2] - sf.Strips[0].Pos[2])); }
            bool vert = zSpread > ySpread;
            var list = new List<(double s, double le, double te, double plane)>();
            foreach (var st in sf.Strips)
            {
                Vec3 p = st.PosVec() - cg;   // (the strip's quarter chord)
                double le = p.X + 0.25 * st.Chord, te = p.X - 0.75 * st.Chord;
                tail = System.Math.Min(tail, te);
                list.Add(vert ? (-p.Z, le, te, p.Y) : (p.Y, le, te, p.Z));
            }
            list.Sort((a, b) => a.s.CompareTo(b.s));
            raw.Add((vert, list));
        }
        _tailX = tail == double.MaxValue ? -0.6 * noseX : tail;
        // A control surface listed on its own (aileron, elevator, rudder) hinges on its surface's trailing edge: it extends that
        // surface's chord rather than being a second plate.
        var used = new bool[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            if (used[i]) continue;
            var (vert, st) = raw[i];
            var le = st.Select(q => q.le).ToArray(); var te = st.Select(q => q.te).ToArray();
            for (int j = 0; j < raw.Count; j++)
            {
                if (j == i || used[j] || raw[j].vert != vert) continue;
                bool joins = false;
                foreach (var c in raw[j].st)
                {
                    int k = Nearest(st, c.s);
                    if (System.Math.Abs(st[k].plane - c.plane) < 0.2 && System.Math.Abs(c.le - te[k]) < 0.15 && c.s >= st[0].s - 0.3 && c.s <= st[st.Count - 1].s + 0.3) joins = true;
                }
                if (!joins) continue;
                used[j] = true;
                foreach (var c in raw[j].st) { int k = Nearest(st, c.s); te[k] = System.Math.Min(te[k], c.te); }
            }
            double half = st.Count > 1 ? 0.5 * (st[st.Count - 1].s - st[0].s) / (st.Count - 1) : 0.3;
            _plates.Add(new Plate
            {
                Vertical = vert, S = st.Select(q => q.s).ToArray(), Le = le, Te = te, Plane = st.Select(q => q.plane).ToArray(),
                SMin = st[0].s - half, SMax = st[st.Count - 1].s + half,
            });
        }
        // (a pair of plates the same surface's two halves listed apart stay two plates: each covers its own span)
        ClipPlatesToSkin();
        Bound();
    }

    private const double Eta = 0.03;   // how far off a plate's face KeepOut stands a point

    /// <summary>Does this plate occupy the station <paramref name="s"/> — is there a surface there at all?</summary>
    private static bool Occupies(Plate p, double s) => s >= p.SMin && s <= p.SMax && !(s > p.SInLo && s < p.SInHi);

    /// <summary>A surface starts at the fuselage skin. Over the span where a plate's own face lies inside the body — or so
    /// close outside it that there is no room to stand <see cref="Eta"/> off that face and still be clear of the body — the
    /// plate is not an obstacle; the body is, and it alone. Without this a marker at a wing or tail root is caught between
    /// two keep-outs whose feasible sets do not overlap: the plate pushes it to the face, the body pushes it radially back
    /// through the face, and neither ever lets go. (It is not enough to drop the span that is merely BURIED — the Extra's
    /// stabiliser root emerges 17 mm outside a 206 mm body radius and still leaves no room, which is why dropping only the
    /// buried span cleared the tail and then deadlocked again at the wing root.)</summary>
    private void ClipPlatesToSkin()
    {
        foreach (var p in _plates)
        {
            const int n = 240;
            int first = -1, last = -1;
            for (int i = 0; i <= n; i++)
                if (!RoomBesideFace(p, p.SMin + (p.SMax - p.SMin) * i / n)) { if (first < 0) first = i; last = i; }
            if (first < 0) { p.SInLo = double.MaxValue; p.SInHi = double.MinValue; continue; }
            double At(int i) => p.SMin + (p.SMax - p.SMin) * i / n;
            // The scan only brackets the edge; bisect to it, then stand 5 mm clear on the body's side of the line. A
            // cutout a few mm too WIDE costs nothing (the body guards that span anyway); a few mm too narrow leaves the
            // deadlock exactly where the scan stepped over the boundary.
            p.SInLo = first == 0 ? p.SMin : Edge(p, At(first), At(first - 1)) - 0.005;
            p.SInHi = last == n ? p.SMax : Edge(p, At(last), At(last + 1)) + 0.005;
        }
    }

    /// <summary>Bisect for the station where the plate first has room beside it: noRoom has none, hasRoom does.</summary>
    private double Edge(Plate p, double noRoom, double hasRoom)
    {
        for (int i = 0; i < 24; i++)
        {
            double mid = 0.5 * (noRoom + hasRoom);
            if (RoomBesideFace(p, mid)) hasRoom = mid; else noRoom = mid;
        }
        return hasRoom;
    }

    /// <summary>Is there room to stand Eta off BOTH faces of this plate at station s, anywhere along its chord, without
    /// being inside the body? Both faces, because a marker arrives from whichever side it likes.</summary>
    private bool RoomBesideFace(Plate p, double s)
    {
        double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
        const int nx = 6;
        for (int i = 0; i <= nx; i++)
        {
            double x = te + (le - te) * i / nx, R = RadiusAt(x);
            if (R <= 0.02) continue;
            double az = AxisAt(x);
            for (int side = -1; side <= 1; side += 2)
            {
                double y = p.Vertical ? plane + side * Eta : s;
                double z = p.Vertical ? -s : plane + side * Eta;
                if (new Vec3(0, y, z - az).Length <= R * 1.02) return false;
            }
        }
        return true;
    }

    private static int Nearest(List<(double s, double le, double te, double plane)> st, double s)
    {
        int k = 0; for (int i = 1; i < st.Count; i++) if (System.Math.Abs(st[i].s - s) < System.Math.Abs(st[k].s - s)) k = i; return k;
    }

    /// <summary>The fuselage radius at station x: a rounded cowl over the first 12 % of the length, full to 35 %, then a
    /// taper to a quarter at the tail.</summary>
    public double RadiusAt(double x)
    {
        if (_px != null) return x < _px[0] || x > _px[_px.Length - 1] ? 0 : Lerp(_px, _pr, x);
        double L = _noseX - _tailX; if (L <= 0) return 0;
        double d = (_noseX - x) / L;   // 0 nose … 1 tail
        if (d < 0 || d > 1.02) return 0;
        if (d < 0.12) { double u = 1 - d / 0.12; return _r0 * System.Math.Sqrt(System.Math.Max(0, 1 - u * u)); }
        if (d < 0.35) return _r0;
        return _r0 * (1 - 0.75 * System.Math.Min(1, (d - 0.35) / 0.65));
    }

    private static double Lerp(double[] s, double[] v, double at)
    {
        if (s.Length == 1 || at <= s[0]) return v[0];
        if (at >= s[s.Length - 1]) return v[v.Length - 1];
        // (binary search: a measured fuselage has ~50-100 stations and this runs for every puff near the airframe)
        int lo = 0, hi = s.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (s[mid] < at) lo = mid; else hi = mid; }
        double t = (at - s[lo]) / System.Math.Max(1e-9, s[hi] - s[lo]);
        return v[lo] + t * (v[hi] - v[lo]);
    }

    /// <summary>The air's velocity relative to the airframe at body point r, given the onset (the undisturbed air at r,
    /// −(V + ω × r)).</summary>
    public Vec3 Velocity(Vec3 r, Vec3 onset) => Velocity(r, onset, out _);

    // SEPARATED WAKES (Ty 2026-10-13: "there would be airflow separation on top of the wings with eddy currents that would
    // capture some of that smoke and spread it across the upper wing area … a cavity or pocket of smoke above the wing root
    // … more on one side than the other … especially inverted spins where the exhaust is emitted directly into the cavity").
    // Behind a stalled section — the plate's SHADOW along the local flow, out to a bubble ≈ 1.5 projected chords deep — and
    // behind the fuselage in crossflow (a cylinder separates: its shadow, ≈ 4 radii deep), the air is dead: the mean flow
    // there is a fraction of the onset (it leaks out downstream slowly; the spanwise flow, which in a spin differs wing to
    // wing, keeps more of its strength), and it is turbulent — an eddy diffusivity ≈ 0.011·U·(the body's projected size)
    // (free-shear-layer mixing, Schlichting). Smoke near the shear layer at the edges is mixed in by that diffusion; smoke
    // released inside stays in the pocket and spreads.
    private const double StallAlphaDeg = 15, WakeDepth = 1.5, WakeMean = 0.12, WakeSpan = 0.6, EddyK = 0.011, ShearM = 0.3;   // (EddyK: Schlichting's plane wake, ε ≈ 0.022·U·b over the half-width b = size/2)

    /// <summary>As <see cref="Velocity(Vec3, Vec3)"/>, and the eddy diffusivity there (m²/s, 0 = attached flow).</summary>
    public Vec3 Velocity(Vec3 r, Vec3 onset, out double diffusivity)
    {
        diffusivity = 0;
        Vec3 u = onset;
        Vec3? wake = null;
        // Fuselage: the crossflow doublet of the local section on its windward side; its separated wake to lee.
        double R = RadiusAt(r.X);
        if (R > 0.02)
        {
            Vec3 rho = new(0, r.Y, r.Z - AxisAt(r.X)); double rl = rho.Length;
            Vec3 uc = new(0, onset.Y, onset.Z); double ucl = uc.Length;
            if (rl > 1e-6)
            {
                Vec3 n = rho / rl;
                if (Vec3.Dot(uc, n) <= 0 || ucl < 1e-6)
                {
                    double k = R * R / System.Math.Max(rl * rl, R * R);
                    u += k * (uc - 2 * Vec3.Dot(uc, n) * n);
                }
                else
                {
                    // Lee side: in the shadow (closer to the flow line through the axis than a radius), within 4 radii.
                    Vec3 ud = uc / ucl; double down = Vec3.Dot(rho, ud); double off = (rho - down * ud).Length;
                    if (down < 4 * R)
                    {
                        if (off < R) { wake = Slower(wake, new Vec3(0.6 * onset.X, 0, 0) + WakeMean * uc); diffusivity = System.Math.Max(diffusivity, EddyK * ucl * 2 * R); }
                        else if (off < R + ShearM) diffusivity = System.Math.Max(diffusivity, 0.5 * EddyK * ucl * 2 * R * (1 - (off - R) / ShearM));
                    }
                }
            }
        }
        // Plates: the normal-flow plate potential on the windward side; behind a stalled section its separated wake.
        foreach (var p in _plates)
        {
            double s = p.Vertical ? -r.Z : r.Y;
            if (s < p.SMin - 0.4 || s > p.SMax + 0.4) continue;
            double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
            double a = 0.5 * (le - te); if (a < 0.02) continue;
            double xi = r.X - 0.5 * (le + te), eta = (p.Vertical ? r.Y : r.Z) - plane;
            double un = p.Vertical ? onset.Y : onset.Z, ut = onset.X;
            double tipFade = s < p.SMin ? 1 - (p.SMin - s) / 0.4 : s > p.SMax ? 1 - (s - p.SMax) / 0.4 : 1;
            if (eta * un > 0)
            {
                // The lee side. Stalled (the local α past the stall) → a wake in the plate's shadow along the local flow.
                double alpha = System.Math.Atan2(System.Math.Abs(un), System.Math.Abs(ut)) * 180 / System.Math.PI;
                if (alpha < StallAlphaDeg || tipFade < 1) continue;
                double xi0 = xi - eta * ut / un;   // where this point's flow line crosses the plate's plane
                double edge = System.Math.Abs(xi0) - a, depth = WakeDepth * 2 * a * System.Math.Sin(alpha * System.Math.PI / 180);
                double size = 2 * a * System.Math.Sin(alpha * System.Math.PI / 180), U = System.Math.Sqrt(un * un + ut * ut);
                if (System.Math.Abs(eta) > depth + ShearM) continue;
                if (edge <= 0 && System.Math.Abs(eta) <= depth)
                {
                    // Dead air: leaking out downstream slowly; the spanwise flow kept in part. (Where wakes overlap — the wing
                    // root's pocket under the fuselage's — the deadest air holds the smoke.)
                    Vec3 nrm = p.Vertical ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1), spanD = p.Vertical ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
                    wake = Slower(wake, WakeMean * (ut * new Vec3(1, 0, 0) + un * nrm) + WakeSpan * Vec3.Dot(onset, spanD) * spanD);
                    diffusivity = System.Math.Max(diffusivity, EddyK * U * size);
                }
                else if (edge < ShearM) diffusivity = System.Math.Max(diffusivity, 0.5 * EddyK * U * size * (1 - System.Math.Max(0, edge) / ShearM));
                continue;
            }
            // u − iv = −i·Un·z / √(z² − a²), with z = ξ + iη; the disturbance is that minus the onset (−i·Un).
            var z = new System.Numerics.Complex(xi, eta);
            var root = System.Numerics.Complex.Sqrt(z - a) * System.Numerics.Complex.Sqrt(z + a);
            if (root.Magnitude < 1e-6) continue;
            var w = -System.Numerics.Complex.ImaginaryOne * un * (z / root - 1);
            double dXi = w.Real, dEta = -w.Imaginary;
            double cap = 2.5 * System.Math.Abs(un), m = System.Math.Sqrt(dXi * dXi + dEta * dEta);
            if (m > cap && m > 1e-9) { dXi *= cap / m; dEta *= cap / m; }
            dXi *= tipFade; dEta *= tipFade;
            u += p.Vertical ? new Vec3(dXi, dEta, 0) : new Vec3(dXi, 0, dEta);
        }
        return wake ?? u;
    }
    private static Vec3 Slower(Vec3? a, Vec3 b) => a.HasValue && a.Value.Length < b.Length ? a.Value : b;


    /// <summary>Hold a point outside every body: inside the fuselage → out to its surface; across a plate during the step
    /// from <paramref name="from"/> → back to the side it came from.</summary>
    public Vec3 KeepOut(Vec3 from, Vec3 to)
    {
        foreach (var p in _plates)
        {
            double s = p.Vertical ? -to.Z : to.Y;
            if (!Occupies(p, s)) continue;
            double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
            if (to.X > le || to.X < te) continue;
            double e0 = (p.Vertical ? from.Y : from.Z) - plane, e1 = (p.Vertical ? to.Y : to.Z) - plane;
            if (e0 * e1 < 0 || System.Math.Abs(e1) < 0.03)
            {
                double side = e0 != 0 ? System.Math.Sign(e0) : (e1 != 0 ? System.Math.Sign(e1) : 1);
                double eta = side * Eta;
                to = p.Vertical ? new Vec3(to.X, plane + eta, to.Z) : new Vec3(to.X, to.Y, plane + eta);
            }
        }
        double R = RadiusAt(to.X);
        if (R > 0.02)
        {
            double az = AxisAt(to.X); Vec3 rho = new(0, to.Y, to.Z - az); double rl = rho.Length;
            if (rl < R * 1.02)
            {
                Vec3 n = rl > 1e-6 ? rho / rl : new Vec3(0, 0, 1);
                to = new Vec3(to.X, 0, az) + n * (R * 1.02);
            }
        }
        return to;
    }

    /// <summary>How far from the CG any part of this airframe reaches (m): a point farther than this cannot touch it.</summary>
    public double Reach
    {
        get
        {
            double r = System.Math.Max(System.Math.Abs(_noseX), System.Math.Abs(_tailX)) + _r0;
            if (_px != null) for (int i = 0; i < _px.Length; i++) r = System.Math.Max(r, System.Math.Abs(_px[i]) + _pr[i] + System.Math.Abs(_pz[i]));
            foreach (var p in _plates)
            {
                double s = System.Math.Max(System.Math.Abs(p.SMin), System.Math.Abs(p.SMax)), x = 0, pl = 0;
                foreach (double v in p.Le) x = System.Math.Max(x, System.Math.Abs(v));
                foreach (double v in p.Te) x = System.Math.Max(x, System.Math.Abs(v));
                foreach (double v in p.Plane) pl = System.Math.Max(pl, System.Math.Abs(v));
                r = System.Math.Max(r, System.Math.Sqrt(s * s + x * x + pl * pl));
            }
            return r;
        }
    }

    /// <summary>A point held outside every body: out of the fuselage to its surface, off a plate to the side it is on.</summary>
    public Vec3 PushOut(Vec3 p) { Vec3 q = KeepOut(p, p); return KeepOut(q, q); }

    /// <summary>A DRAWN piece of smoke (a puff or a pulse tube: a soft blob stretched along a segment), body axes, made to
    /// stay out of the airframe (Ty t914u: "no smoke may pass through any portion of any airplane"). The marker paths
    /// themselves already go round (<see cref="Deflection"/>, <see cref="KeepOut"/>); what did not was the DRAWING laid
    /// between and around them: a young puff is stretched back along the flight path over one frame of travel, and in a
    /// flat spin the flight path is straight down, so the stretch ran from the belly exhaust UP through the cowling and
    /// out of its top (the Pitts report). Here the centre is held outside, and each half of the segment is cut where it
    /// first enters a body: what is drawn is the part of it that is in the air, on the centre's side. Returns the new
    /// centre and half-length; <paramref name="trimmed"/> says whether anything changed.</summary>
    public (Vec3 centre, double half) ClearSegment(Vec3 centre, Vec3 axisUnit, double half, out bool trimmed)
    {
        trimmed = false;
        Vec3 c = centre;
        if (Inside(c, 0.02)) { c = PushOut(c); trimmed = true; }
        if (half <= 1e-4) return (c, half);
        double fwd = Clearance(c, axisUnit, half), back = Clearance(c, -1.0 * axisUnit, half);
        if (fwd >= half && back >= half) return (c, half);
        trimmed = true;
        // The kept part runs from c − back to c + fwd: its middle, and half its length.
        return (c + axisUnit * (0.5 * (fwd - back)), 0.5 * (fwd + back));
    }

    /// <summary>How far from <paramref name="c"/> (outside every body) along <paramref name="dir"/> the spine stays in the
    /// air, up to <paramref name="max"/>. Each plate: the stretch of the spine inside its face's keep-out band (|η| &lt; 2 cm,
    /// linear along the spine, so found exactly), searched at 1 cm for where it is over the plate. The fuselage: only the
    /// stretch of the spine within the body's bounding tube, sampled every 3 cm against the skin plus 0.5 % (a chord that
    /// grazes a 0.3 m section dips under it by 0.4 mm in 3 cm; the centre is held at 2 %).</summary>
    private double Clearance(Vec3 c, Vec3 dir, double max)
    {
        double best = max;
        const double Tol = 0.02;
        foreach (var p in _plates)
        {
            double s0 = p.Vertical ? -c.Z : c.Y, ds = p.Vertical ? -dir.Z : dir.Y;
            // Quick reject: the spine's span range misses the plate's span.
            double sA = s0, sB = s0 + ds * best;
            if (System.Math.Max(sA, sB) < p.SMin - 0.05 || System.Math.Min(sA, sB) > p.SMax + 0.05) continue;
            double e0 = (p.Vertical ? c.Y : c.Z) - Lerp(p.S, p.Plane, s0), de = p.Vertical ? dir.Y : dir.Z;
            // (the plane's height changes along the span — a dihedral — so the band is widened by that change over the spine)
            double slope = System.Math.Abs(Lerp(p.S, p.Plane, sB) - Lerp(p.S, p.Plane, sA)) + 0.01;
            double d1, d2;
            if (System.Math.Abs(de) < 1e-9) { if (System.Math.Abs(e0) > Tol + slope) continue; d1 = 0; d2 = best; }
            else
            {
                d1 = (-(Tol + slope) - e0) / de; d2 = ((Tol + slope) - e0) / de;
                if (d1 > d2) (d1, d2) = (d2, d1);
                d1 = System.Math.Max(0, d1); d2 = System.Math.Min(best, d2);
                if (d1 > d2) continue;
            }
            int n = System.Math.Clamp((int)System.Math.Ceiling((d2 - d1) / 0.01), 1, 200);
            for (int i = 0; i <= n; i++)
            {
                double d = d1 + (d2 - d1) * i / n;
                if (InsidePlate(p, c + dir * d, Tol)) { best = System.Math.Max(0, d - 0.01); break; }
            }
        }
        // The fuselage, within its bounding tube only.
        if (_fRmax > 0.02)
        {
            double xa = c.X, xb = c.X + dir.X * best, lo = 0, hi = best;
            if (System.Math.Abs(dir.X) > 1e-9)
            {
                double t1 = (_fxMin - c.X) / dir.X, t2 = (_fxMax - c.X) / dir.X;
                if (t1 > t2) (t1, t2) = (t2, t1);
                lo = System.Math.Max(lo, t1); hi = System.Math.Min(hi, t2);
            }
            else if (xa < _fxMin || xa > _fxMax) hi = -1;
            if (hi >= lo)
            {
                // ... and only if the spine comes within the tube's radius of its axis line at all
                Vec3 a0 = new(0, c.Y, c.Z - _fzMid), ad = new(0, dir.Y, dir.Z);
                double add = Vec3.Dot(ad, ad), tc = add > 1e-12 ? System.Math.Clamp(-Vec3.Dot(a0, ad) / add, lo, hi) : lo;
                double closest = (a0 + ad * tc).Length;
                if (closest < _fRmax + _fzHalf + 0.01)
                {
                    // (every 3 cm, and 2 mm inside each end of the tube: a body that ends square — the analytic tail's last
                    // 2 % — is thinner than a step)
                    int n = System.Math.Clamp((int)System.Math.Ceiling((hi - lo) / 0.03), 1, 96);
                    double prev = lo;   // (clear up to where the spine enters the tube)
                    for (int i = 0; i <= n + 1; i++)
                    {
                        // the grid, with one extra sample 2 mm short of the far end in its place order
                        double d = i == 0 ? lo + 0.002 : i <= n ? lo + (hi - lo) * i / n : hi;
                        if (i == n) d = System.Math.Max(prev, hi - 0.002);
                        if (d <= 0) continue;
                        Vec3 q = c + dir * d;
                        double R = RadiusAt(q.X);
                        if (R > 0.02 && new Vec3(0, q.Y, q.Z - AxisAt(q.X)).Length < R * 1.005) { best = System.Math.Max(0, prev - 0.005); break; }
                        prev = d;
                    }
                }
            }
        }
        return best;
    }

    private static bool InsidePlate(Plate p, Vec3 r, double tol)
    {
        double s = p.Vertical ? -r.Z : r.Y;
        if (!Occupies(p, s)) return false;
        double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
        return r.X <= le && r.X >= te && System.Math.Abs((p.Vertical ? r.Y : r.Z) - plane) < tol;
    }

    // The fuselage's bounding tube, for the quick rejects above (set with the shape).
    private double _fxMin, _fxMax, _fRmax, _fzMid, _fzHalf;
    private void Bound()
    {
        if (_px != null)
        {
            _fxMin = _px[0]; _fxMax = _px[_px.Length - 1]; _fRmax = 0; double zl = double.MaxValue, zh = double.MinValue;
            for (int i = 0; i < _px.Length; i++) { _fRmax = System.Math.Max(_fRmax, _pr[i]); zl = System.Math.Min(zl, _pz[i]); zh = System.Math.Max(zh, _pz[i]); }
            _fzMid = 0.5 * (zl + zh); _fzHalf = 0.5 * (zh - zl);
        }
        else { _fxMin = _noseX - 1.02 * System.Math.Max(0, _noseX - _tailX) + 1e-6; _fxMax = _noseX;   // (RadiusAt's own ends: the tail is cut square 2 % past it)
                _fRmax = _r0; _fzMid = _axisZ; _fzHalf = 0; }
    }

    /// <summary>Inside a body (for the tests): in the fuselage, or within <paramref name="tol"/> of a plate's face.
    /// <paramref name="tol"/> must stay below <see cref="Eta"/>: the plate span is clipped so that there is room to stand
    /// Eta off a face and still be clear of the body, so a tolerance wider than that asks for room nothing promises.</summary>
    public bool Inside(Vec3 r, double tol = 0.01)
    {
        double R = RadiusAt(r.X);
        if (R > 0.02 && new Vec3(0, r.Y, r.Z - AxisAt(r.X)).Length < R) return true;
        foreach (var p in _plates)
        {
            double s = p.Vertical ? -r.Z : r.Y;
            if (!Occupies(p, s)) continue;
            double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
            if (r.X <= le && r.X >= te && System.Math.Abs((p.Vertical ? r.Y : r.Z) - plane) < tol) return true;
        }
        return false;
    }

    /// <summary>A smoke marker released at body point <paramref name="r0"/> with the airplane flying at body velocity V,
    /// body rates ω (held for the pass — a few seconds at most), in the exhaust jet leaving along <paramref name="jetDir"/> at
    /// <paramref name="jetU0"/> from a pipe of <paramref name="pipeD"/> (<see cref="ExhaustJet"/>: it entrains the air and
    /// bends into the crossflow). Returns, every <paramref name="dt"/>, how far the marker is from where the undisturbed air
    /// released at r0 would be (body axes at that moment) — the jet and the airframe's push together; it stops once the jet
    /// is spent and the marker is a body length clear. In a separated wake the marker also random-walks with the eddy
    /// diffusivity (√(2D·dt) a step) — a turbulent eddy's share of the smoke; <paramref name="seed"/> picks which (the same
    /// puff, the same path, every redraw).</summary>
    /// <param name="jetSubsteps">Passed to <see cref="ExhaustJet.Step"/>: 0 lets it choose (what the game uses), 1 pins
    /// the single Euler step the march used before 2026-11-11, for the before/after plates.</param>
    public List<Vec3> Deflection(Vec3 r0, Vec3 V, Vec3 omega, Vec3 jetDir, double jetU0, double pipeD, double airDensity,
                                 double dt = 0.004, double maxT = 4.0, uint seed = 1, int jetSubsteps = 0)
    {
        var outp = new List<Vec3> { Vec3.Zero };
        Vec3 r = r0, rFree = r0;
        double L = System.Math.Max(1, _noseX - _tailX);
        foreach (var p in _plates) L = System.Math.Max(L, System.Math.Max(System.Math.Abs(p.SMin), System.Math.Abs(p.SMax)));
        uint st = seed * 747796405u + 2891336453u;
        double Gauss()
        {
            // (Box–Muller from a PCG-style hash: deterministic per seed)
            st = st * 747796405u + 2891336453u; double u1 = ((st >> 8) + 1) / 16777217.0;
            st = st * 747796405u + 2891336453u; double u2 = (st >> 8) / 16777216.0;
            return System.Math.Sqrt(-2 * System.Math.Log(u1)) * System.Math.Cos(2 * System.Math.PI * u2);
        }
        bool jetOn = jetU0 > 0.5 && pipeD > 0 && jetDir.LengthSquared > 1e-9;
        var jet = jetOn ? ExhaustJet.Start(jetDir * (1.0 / jetDir.Length), jetU0, pipeD, airDensity) : default;
        for (double t = 0; t < maxT; t += dt)
        {
            // (midpoint steps: the rotating frame turns the free path through a circle)
            Vec3 Onset(Vec3 q) => -1.0 * (V + Vec3.Cross(omega, q));
            Vec3 u1 = Velocity(r, Onset(r), out double D);
            // THE JET'S OWN DISPLACEMENT over this step, integrated (ExhaustJet.Step's `carried`), rather than its excess
            // at the start of the step held for the whole of it. At 251 m/s the latter credited a marker a metre of travel
            // at full speed across 4 ms in which the real jet has already given most of its excess away - the dominant
            // error in the drawn push (2026-11-11).
            Vec3 jetMove = Vec3.Zero;
            if (jetOn) { ExhaustJet.Step(ref jet, u1, airDensity, dt, omega, jetSubsteps, out jetMove); if (jet.Spent) jetOn = false; }
            Vec3 rm = r + u1 * (0.5 * dt) + jetMove * 0.5;
            Vec3 rn = r + Velocity(rm, Onset(rm)) * dt + jetMove;
            if (D > 0) { double sd = System.Math.Sqrt(2 * D * dt); rn += new Vec3(Gauss() * sd, Gauss() * sd, Gauss() * sd); }
            r = KeepOut(r, rn);
            Vec3 fm = rFree + Onset(rFree) * (0.5 * dt);
            rFree += Onset(fm) * dt;
            outp.Add(r - rFree);
            if (!jetOn && r.Length > 1.3 * L && rFree.Length > 1.3 * L) break;
        }
        return outp;
    }
}
