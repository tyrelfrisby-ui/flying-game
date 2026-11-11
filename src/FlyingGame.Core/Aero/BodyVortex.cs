using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// THE FUSELAGE'S CROSSFLOW VORTICES OVER THE TAIL (lane/claude-endo, 2026-11-03; docs/aircraft/extra-300-endo-DERIVATION.md §6.4).
/// Owner Ty: the sideslip-to-pitch coupling, nose down, is "still too weak by a factor of at least 5". Polhamus, NACA RM L55E20b
/// (1955), figs. 9 and 10: tail on, Cm falls 0.13–0.17 between β 0 and 25–30°; tail off it falls 0.02–0.03. The difference is a
/// vortex the fuselage sheds from its leeward side in sideslip ("associated with the well-known crossflow separation on bodies
/// inclined to the wind", traced to a point ahead of the canopy) passing over the horizontal tail. The model had the body's
/// viscous crossflow FORCE (AeroModel.ApplyFuselage) and nothing of the vortices that carry it.
///
/// The vortices, from the force the model already computes:
///  - IMPULSE (Allen &amp; Perkins, NACA TR 1048; Bryson, J. Appl. Mech. 1959): in a steady flow along the body the viscous crossflow
///    force on the body ahead of a station is the rate at which the crossflow wake's impulse grows, F = U · I′ with
///    I′ = 2ρΓ·y·(1 − a²/r²) for a symmetric pair at distance r from the axis and y off the crossflow line, with its images in the body.
///    F = ½ρU_c²·C_d,c·A_n, the same crossflow drag coefficient and projected area ApplyFuselage uses.
///  - POSITION AND STRENGTH (Föppl 1913; Milne-Thomson §13.71): the standing pair behind a circular cylinder of radius a lies on
///    r² − a² = 2·r·y with Γ = 4π·U_c·y·(1 − a⁴/r⁴) (checked against the zero-velocity condition at the vortex to 1 %). The
///    impulse fixes where on that curve the pair stands. a is the body's mean half-breadth normal to the crossflow (A_n / 2L).
///    On the Extra at β 30° and 54 m/s this gives Γ ≈ 105 m²/s, Γ/(2π·a·U_c) = 1.46, at r = 1.9a. No measured vortex strength for a
///    light-aircraft fuselage was found to check it against; what is checked is the tail's pitching moment against RM L55E20b.
/// Each tail strip then flies in the pair's induced flow, images included, averaged across the strip's own width so the strip next
/// to a vortex sees the mean of its upwash and not the point value (a 2-D vortex's induced velocity integrates across a line; a core
/// of a/10 only keeps the sample points finite). Nothing here is tuned: the inputs are the crossflow drag the fuselage already has,
/// its areas and length, and the flow.
/// Not modelled: the wing intercepting the lower vortex over its root chord (Polhamus' sketch 10) — both vortices are kept; a
/// body whose section changes along its length (one mean radius); vortex drift as the body tapers.
/// </summary>
public static class BodyVortex
{
    /// <summary>Largest distance of the standing pair from the body axis, in body radii (see <see cref="For"/>).</summary>
    public const double MaxRadiusOverA = 2.0;

    /// <summary>
    /// THE ASSUMED QUANTITIES IN THIS MODEL, GATHERED SO THEY CAN BE MEASURED (lane/claude-slip-pitch, 2026-11-10).
    /// lane/claude-endo shipped four of these as constants and marked three of them ASSUMED in its own derivation. The
    /// measurement on today's model (docs/aircraft/CM-BETA.md) says the model carries a NINTH of the tail-carried
    /// Cm(β) that Polhamus measured, so one of the assumptions is wrong by a large factor and arguing about which is
    /// cheaper than sweeping them. <see cref="Shipped"/> is exactly lane/claude-endo's behaviour; SlipPitchProbe
    /// sweeps the rest. Nothing reads these in the game.
    /// </summary>
    public struct Knobs
    {
        /// <summary>The r ≤ this·a cap on where the standing pair sits (lane/claude-endo: ASSUMED).</summary>
        public double MaxRadiusOverA;
        /// <summary>Body incidence (deg) to full strength, and the band over which the symmetric pair fades out.</summary>
        public double SymmetricLimitDeg, FadeDeg;
        /// <summary>Body incidence (deg) at which the crossflow starts to separate, and at which the pair is formed.</summary>
        public double OnsetDeg, FullDeg;
        /// <summary>The share of the LOWER (wing-side) vortex that still reaches the tail. 1 = the full symmetric pair,
        /// which is lane/claude-endo's model; 0 = the upper vortex alone. Polhamus' mechanism is one vortex, and a
        /// symmetric pair about the tail's own plane cancels its net load on the tail by construction — which is
        /// exactly what the measurement found. See docs/aircraft/CM-BETA.md.</summary>
        public double LowerVortexShare;
        /// <summary>The vortex CORE radius as a fraction of the body's mean half-breadth a. lane/claude-endo used 0.1
        /// and called it "a core of a/10 only keeps the sample points finite" -- i.e. it was a numerical guard, not a
        /// physical core. It is not harmless: with a core that small the strip directly under the vortex sees 15 deg of
        /// induced angle and stalls, so the span load becomes a difference of two saturated halves and the net moment on
        /// the tail collapses (docs/aircraft/CM-BETA.md). A separated body vortex is a rolled-up shear layer, not a line.</summary>
        public double CoreOverA;
        /// <summary>The pair's centre height in BODY z at the tail (NED: negative is up). lane/claude-endo used 0, the
        /// config origin. The vortices are shed from the body ahead of the tail and convect aft in the free stream, so
        /// the right value is the body's own midline where they were shed, which is measured data on the types that
        /// have fuselage stations (FuselageShape).</summary>
        public double CentreZ;

        public static Knobs Shipped => new()
        {
            MaxRadiusOverA = BodyVortex.MaxRadiusOverA,
            SymmetricLimitDeg = BodyVortex.SymmetricLimitDeg,
            FadeDeg = BodyVortex.FadeDeg,
            OnsetDeg = BodyVortex.OnsetDeg,
            FullDeg = BodyVortex.FullDeg,
            LowerVortexShare = 1.0,
            CentreZ = 0.0,
            CoreOverA = 0.1,
        };
    }

    /// <summary>The knobs in force. Diagnostics: SlipPitchProbe sets it and puts it back.</summary>
    public static Knobs Diag = Knobs.Shipped;

    /// <summary>Body incidence (deg) to which the symmetric pair stands at full strength, and the band over which it fades out
    /// (CR-3809 §4.1.1: asymmetric from ~25°, entirely asymmetric by ~40°).</summary>
    public const double SymmetricLimitDeg = 25.0, FadeDeg = 15.0;

    /// <summary>Body incidence (deg) below which the crossflow is attached and sheds no vortices, and at which the pair is fully
    /// formed (CR-3809 §4.1.1: "at an angle of attack of about 6° to 8° the crossflow about the body begins to separate").</summary>
    public const double OnsetDeg = 6.0, FullDeg = 8.0;

    /// <summary>The pair for one flow: body-axis centre (z), the frame (crossflow direction X̂ and its normal Ŷ, in body y–z), the
    /// four vortices in that frame and their circulations, and the core. Default = no vortices.</summary>
    public readonly struct Pair
    {
        public readonly bool Active;
        public readonly double Zc, Xy, Xz, Yy, Yz, A, Core;
        public readonly double X1, Y1, G;   // the upper vortex (frame coordinates, m) and |Γ| (m²/s)
        /// <summary>The share of the LOWER vortex (and its image) that reaches the tail: see <see cref="Knobs.LowerVortexShare"/>.</summary>
        public readonly double Lower;
        public Pair(double zc, double xy, double xz, double a, double x1, double y1, double g, double lower, double coreOverA)
        {
            Active = g > 1e-6; Zc = zc; Xy = xy; Xz = xz; Yy = -xz; Yz = xy; A = a; Core = coreOverA * a; X1 = x1; Y1 = y1; G = g; Lower = lower;
        }
    }

    /// <summary>The body's vortex pair for this flow. <paramref name="vBody"/> is the aircraft's velocity relative to the air at the
    /// CG (body axes). No pair without a crossflow body, with the flow tail-first, or with no crossflow.</summary>
    public static Pair For(AircraftConfig config, Vec3 vBody, double rho)
    {
        CrossflowConfig cf = config.Fuselage.Crossflow;
        double u = vBody.X, vc2 = vBody.Y * vBody.Y + vBody.Z * vBody.Z;
        if (cf.LengthM <= 0.1 || cf.Cd <= 0 || (cf.SideArea <= 0 && cf.PlanArea <= 0) || u < 1.0 || vc2 < 1e-4) return default;
        double uc = Math.Sqrt(vc2);
        // FLOW REGIME (Polhamus, NASA CR-3809 §4.1.1 and fig. 4.1): below about 6–8° the crossflow stays attached and there are no
        // vortices (so trim, the rig and the glide table, all at α 0–6°, are untouched); a body at incidence σ sheds the
        // symmetric, counter-rotating pair this models from about 6–8°; above about 25° the pair turns asymmetric, by about 40° the
        // whole vortex flow is asymmetric alternating pairs, and higher still it is the unsteady wake of a cylinder normal to the
        // flow. So the standing pair flies at full strength to σ 25° and fades out by 40°: past that the tail is in a flow this
        // pair does not describe (and in a developed spin, σ 60–90°, it is the wing's and stab's wakes, TailWake, that matter).
        Knobs kn = Diag;
        double sigma = Math.Atan2(uc, u) * 180.0 / Math.PI;
        double regime = Math.Clamp((kn.SymmetricLimitDeg + kn.FadeDeg - sigma) / kn.FadeDeg, 0.0, 1.0)
                        * Math.Clamp((sigma - kn.OnsetDeg) / Math.Max(1e-6, kn.FullDeg - kn.OnsetDeg), 0.0, 1.0);
        if (regime <= 0.0) return default;
        // the AIR's crossflow direction in body y–z: opposite the aircraft's (v, w)
        double xy = -vBody.Y / uc, xz = -vBody.Z / uc;
        double an = cf.SideArea * Math.Abs(xy) + cf.PlanArea * Math.Abs(xz);   // projected area normal to the crossflow
        double a = an / (2.0 * cf.LengthM);
        if (a <= 1e-3) return default;
        // I′/(U_c a²) = F/(ρ U U_c a²) with F = ½ρU_c²·C_d·A_n
        double target = 0.5 * uc * cf.Cd * an / (u * a * a);
        double lo = 1.0 + 1e-6, hi = 40.0;
        for (int k = 0; k < 60; k++)
        {
            double m = 0.5 * (lo + hi);
            if (Impulse(m) < target) lo = m; else hi = m;
        }
        // The standing pair stops growing where the crossflow wake starts to SHED: past r = 2a (Γ/(2π·a·U_c) = 1.4, about one
        // diameter behind the axis, the length of a cylinder's near wake) the impulse goes into vortices that leave the body, not
        // into the pair over the tail. ASSUMED bound (docs/aircraft/extra-300-endo-DERIVATION.md §6.4); without it, at U_c/U above ~1 (α −40°, β 60°) the
        // impulse analogy, a slender-body result, put several hundred m²/s over the stab.
        double r = Math.Min(kn.MaxRadiusOverA, 0.5 * (lo + hi)), y = (r * r - 1.0) / (2.0 * r), x = Math.Sqrt(Math.Max(0.0, r * r - y * y));
        double g = 4.0 * Math.PI * y * (1.0 - 1.0 / (r * r * r * r));
        return new Pair(kn.CentreZ, xy, xz, a, x * a, y * a, regime * g * uc * a, kn.LowerVortexShare, kn.CoreOverA);

        static double Impulse(double rr)
        {
            double yy = (rr * rr - 1.0) / (2.0 * rr);
            return 2.0 * 4.0 * Math.PI * yy * (1.0 - 1.0 / (rr * rr * rr * rr)) * yy * (1.0 - 1.0 / (rr * rr));
        }
    }

    /// <summary>The AIR velocity the pair induces (body axes, y and z; x is zero) at <paramref name="p"/>, averaged across a strip
    /// <paramref name="halfWidth"/> either side of it along body y (horizontal strip) or z (vertical strip).</summary>
    public static Vec3 Induced(in Pair pair, Vec3 p, double halfWidth, bool vertical)
    {
        if (!pair.Active) return Vec3.Zero;
        const int n = 5;
        double sy = 0, sz = 0;
        for (int i = 0; i < n; i++)
        {
            double off = halfWidth * (2.0 * (i + 0.5) / n - 1.0);
            double py = p.Y + (vertical ? 0.0 : off), pz = p.Z + (vertical ? off : 0.0);
            var (vy, vz) = At(pair, py, pz);
            sy += vy; sz += vz;
        }
        return new Vec3(0.0, sy / n, sz / n);
    }

    private static (double Vy, double Vz) At(in Pair pr, double py, double pz)
    {
        // into the frame: X along the air's crossflow, Y its normal (a proper rotation of body y–z, so circulation signs carry over)
        double dy = py, dz = pz - pr.Zc;
        double X = dy * pr.Xy + dz * pr.Xz, Y = dy * pr.Yy + dz * pr.Yz;
        double a2 = pr.A * pr.A, r2 = pr.X1 * pr.X1 + pr.Y1 * pr.Y1;
        // upper (Y > 0, clockwise: −Γ), lower (+Γ), and their images at a²/z̄ and a²/z
        double ix = a2 * pr.X1 / r2, iy = a2 * pr.Y1 / r2;
        double ux = 0, uy = 0, core2 = pr.Core * pr.Core;
        void Add(double vx, double vyy, double k)
        {
            double ex = X - vx, ey = Y - vyy, d2 = ex * ex + ey * ey + core2;
            ux += -ey * k / (2.0 * Math.PI * d2);
            uy += ex * k / (2.0 * Math.PI * d2);
        }
        // The UPPER vortex (over the turtledeck, the one Polhamus traces to a point ahead of the canopy) and its image,
        // then the LOWER one and its image at whatever share of it still reaches the tail.
        double x1 = pr.X1, y1 = pr.Y1, g = pr.G, lo = pr.Lower;
        Add(x1, y1, -g);
        Add(ix, iy, g);
        Add(x1, -y1, g * lo);
        Add(ix, -iy, -g * lo);
        // back to body y–z
        return (ux * pr.Xy + uy * pr.Yy, ux * pr.Xz + uy * pr.Yz);
    }
}
