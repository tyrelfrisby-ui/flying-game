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
    }

    private readonly List<Plate> _plates = new();
    private readonly double _noseX, _tailX, _r0, _axisZ;
    private double[] _px, _pr, _pz;   // a measured fuselage (stations x ascending, radius, axis height); null = the analytic shape

    /// <summary>The fuselage as measured on the model: at stations <paramref name="x"/> (descending, nose first), its radius
    /// and the height of its axis (body z).</summary>
    public void SetFuselage(double[] x, double[] radius, double[] axisZ) { if (x != null && x.Length >= 2) { _px = Rev(x); _pr = Rev(radius); _pz = Rev(axisZ); } }   // (kept tail first: ascending x)

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
        for (int i = 1; i < s.Length; i++) if (at <= s[i]) { double t = (at - s[i - 1]) / System.Math.Max(1e-9, s[i] - s[i - 1]); return v[i - 1] + t * (v[i] - v[i - 1]); }
        return v[v.Length - 1];
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

    /// <summary>The exhaust jet: its exit speed and how fast it decays into the air (≈ 0.5 m of travel — a turbulent jet
    /// is spent within ~10–20 stack diameters).</summary>
    public const double JetSpeed = 25.0, JetTau = 0.02;

    /// <summary>Hold a point outside every body: inside the fuselage → out to its surface; across a plate during the step
    /// from <paramref name="from"/> → back to the side it came from.</summary>
    public Vec3 KeepOut(Vec3 from, Vec3 to)
    {
        foreach (var p in _plates)
        {
            double s = p.Vertical ? -to.Z : to.Y;
            if (s < p.SMin || s > p.SMax) continue;
            double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
            if (to.X > le || to.X < te) continue;
            double e0 = (p.Vertical ? from.Y : from.Z) - plane, e1 = (p.Vertical ? to.Y : to.Z) - plane;
            if (e0 * e1 < 0 || System.Math.Abs(e1) < 0.03)
            {
                double side = e0 != 0 ? System.Math.Sign(e0) : (e1 != 0 ? System.Math.Sign(e1) : 1);
                double eta = side * 0.03;
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

    /// <summary>Inside a body (for the tests): in the fuselage, or within <paramref name="tol"/> of a plate's face.</summary>
    public bool Inside(Vec3 r, double tol = 0.01)
    {
        double R = RadiusAt(r.X);
        if (R > 0.02 && new Vec3(0, r.Y, r.Z - AxisAt(r.X)).Length < R) return true;
        foreach (var p in _plates)
        {
            double s = p.Vertical ? -r.Z : r.Y;
            if (s < p.SMin || s > p.SMax) continue;
            double le = Lerp(p.S, p.Le, s), te = Lerp(p.S, p.Te, s), plane = Lerp(p.S, p.Plane, s);
            if (r.X <= le && r.X >= te && System.Math.Abs((p.Vertical ? r.Y : r.Z) - plane) < tol) return true;
        }
        return false;
    }

    /// <summary>A smoke marker released at body point <paramref name="r0"/> with the airplane flying at body velocity V,
    /// body rates ω (held for the pass — a few seconds at most), and an exhaust jet blowing along <paramref name="jetDir"/>
    /// (JetSpeed, decaying in JetTau). Returns, every <paramref name="dt"/>, how far the marker has been pushed (body axes at
    /// that moment) from where the undisturbed air would have carried it; it stops once the marker is a body length clear.
    /// In a separated wake the marker also random-walks with the eddy diffusivity (√(2D·dt) a step) — a turbulent eddy's
    /// share of the smoke; <paramref name="seed"/> picks which (the same puff, the same path, every redraw).</summary>
    public List<Vec3> Deflection(Vec3 r0, Vec3 V, Vec3 omega, Vec3 jetDir, double dt = 0.004, double maxT = 4.0, uint seed = 1)
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
        for (double t = 0; t < maxT; t += dt)
        {
            Vec3 jet = jetDir * (JetSpeed * System.Math.Exp(-t / JetTau));
            // (midpoint steps: the rotating frame turns the free path through a circle)
            Vec3 Onset(Vec3 q) => -1.0 * (V + Vec3.Cross(omega, q));
            Vec3 k1 = Velocity(r, Onset(r), out double D) + jet, rm = r + k1 * (0.5 * dt);
            Vec3 rn = r + (Velocity(rm, Onset(rm)) + jet) * dt;
            if (D > 0) { double sd = System.Math.Sqrt(2 * D * dt); rn += new Vec3(Gauss() * sd, Gauss() * sd, Gauss() * sd); }
            r = KeepOut(r, rn);
            Vec3 f1 = Onset(rFree) + jet, fm = rFree + f1 * (0.5 * dt);
            rFree += (Onset(fm) + jet) * dt;
            outp.Add(r - rFree);
            if (r.Length > 1.3 * L && rFree.Length > 1.3 * L) break;
        }
        return outp;
    }
}
