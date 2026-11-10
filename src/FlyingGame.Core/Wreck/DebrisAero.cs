using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>
/// THE AIR'S FORCES ON A DEPARTED PIECE — the thing that makes it tumble instead of fall like a stone.
///
/// Ty 2026-11-10: "Make sure departing wings and canopies use real aerodynamic[s] for movement same for the pilot
/// when bailing out real forces leading to tumbles and flutter etc", and then, sharpening it: "They get lift and
/// drag I want tumble and flutter."
///
/// WHAT WAS WRONG BEFORE. <see cref="HydroBody.Coefficients"/> gave a free piece a per-axis drag
/// F_k = −½ρ·Cd·A_k·|v_k|·v_k at the element's CENTRE, plus a rotational damping term built from each element's tip
/// speed. Two separate defects, and the second one is the reason the pieces fell like stones:
///
///  1. THE LAW IS THE WRONG ONE FOR A PLATE. v_k² is right for a bluff box meeting the flow face-on and badly wrong
///     for a plate at a shallow angle. A departed wing panel IS a finite flat plate at every angle, and a plate's
///     normal force is Cn = Cd_max·sin α on its own AREA (Hoerner, Fluid-Dynamic Drag ch. 3; the same relation
///     AeroModel uses past the stall, where these tables already carry a finite-AR plate's 1.19 at 90° =
///     Viterna &amp; Corrigan's 1.11 + 0.018·AR). Written out, q·A·Cd_max·sin α = ½ρ·Cd_max·A·|v|·|v_n|: the same
///     thing as the old law but with |v| where it had |v_n|, so at α = 10° it was 1/sin α = 5.8 times too SMALL.
///     Note what this term is and is not: it is not "drag plus a lift bolted on". It is ONE force, normal to the
///     plate — and because it stays normal to the PLATE instead of lining up with the wind, its component across
///     the flight path IS the lift. A plate gets its lift for free the moment the law is the right one.
///  2. IT ACTED AT THE CENTRE, so it could never turn the piece. A plate's centre of pressure sits at the quarter
///     chord when the flow is nearly along it and walks back to mid-chord as it goes broadside — measured on flat
///     plates (Flachsbart; NACA plate series): 0.28c at 10°, 0.33c at 20°, 0.38c at 30°, 0.43c at 45°, 0.50c at 90°.
///     <see cref="CpWalk"/>·(1 − sin α) through the two exact end points (0.25c attached, 0.50c broadside by
///     symmetry) reproduces that whole set to about 0.01c. A piece's CG is the mass centroid of its boxes, which for
///     a panel is mid-chord, so the force acts AHEAD of the CG at every angle but 90° and drives the incidence UP.
///     That is the plate's classical static instability about mid-chord, and it is the whole engine of the tumble:
///     the force moves with the angle and only lines up with the CG at broadside.
///
/// WHAT RESISTS IT, AND WHAT LETS IT GO ROUND. Each box is integrated as FOUR strips at two-point Gauss–Legendre
/// nodes, each carrying a quarter of the area and feeling its OWN velocity v + ω × r, so the rotational damping is
/// whatever that distribution produces — the advancing edge at a lower local α than the retreating one — with its
/// own sign, rather than the old |ω|·h tip-speed estimate, which was positive by construction and so could never
/// let anything autorotate. <b>There is no tumble rate and no damping coefficient in this file.</b> WreckSim
/// therefore asks HydroBody for the WATER only (airDrag: false): the air, all of it, is here, counted once.
///
/// The nodes are laid out in the plate's own FLOW FRAME, and the chordwise layout is the thing that decides whether
/// a plate can tumble at all. Three textbook facts move together with how attached the flow is:
///   the CP      quarter chord attached, mid-chord broadside (above);
///   the LAG     a plate's bound circulation is set by the flow at the three-quarter-chord point and acts at the
///               quarter chord — Theodorsen 1935; Fung, Aeroelasticity §13.4 — so when attached, each node's flow
///               is read a half chord aft of where its force is applied;
///   the SPREAD  attached, the chordwise loading is the bound vortex sheet ∝ √((c−x)/x). Its strength is ONE number
///               set at ONE point, so it does not resist a pitch rate strip by strip; the nodes collapse onto its
///               centroid, which is the quarter chord. Separated, the pressure is spread over the chord and every
///               strip answers for itself. The first case is why a plate autorotates; the second is the damping.
/// Over the four nodes that comes to dM/dω = −k·|v|·(x̄² + σ_c² − lag·x̄), k = ½ρ·Cd·A: exactly +k·|v|·c²/16
/// attached — the classical driving moment of a plate pivoting about mid-chord, whose aerodynamic centre is ahead of
/// its pivot — and exactly −k·|v|·c²/6 broadside, the uniform strip second moment c²/12 doubled because broadside
/// |v|·v_n IS v_n² and the force is quadratic in the rate there. Nothing is tuned between the two: the ends are the
/// two textbook answers and sin α is the only thing in between (DebrisAeroTests measures both). A uniform spread at
/// every angle — the first cut of this file — damps at BOTH ends, and a panel then swung hard to broadside and sat
/// there: flutter, no tumble.
///
/// PLATE OR BOX. The per-axis fineness f = (extent along that axis)/√(face area) already decides the Cd here
/// (<see cref="WaterPhysics.CdForFineness"/>: plate → cube → axial body). The same f decides how much of this is
/// plate behaviour at all: full below <see cref="PlateFineness"/>, none above <see cref="BluffFineness"/>. On real
/// geometry that sorts itself out — a wing-panel box (chord 1.6, span 5, thickness 0.2 m) reads f = 0.07 on its
/// plan face (a plate), 1.6 on its leading-edge face and 8.8 on its tip (bluff, so the old quadratic law and no CP
/// walk, which is what streamwise drag on a panel should be); a fuselage section reads 2.0 along its axis. For a
/// bluff face this returns EXACTLY what the old air drag returned, at the same point, so a chunky piece that was not
/// rotating is untouched.
///
/// DELIBERATELY NOT HERE: air added mass (a plate's is ρ_air·π·(c/2)²·span, about 40 % of a light panel's own mass —
/// it would slow the tumble, not prevent it, and it brings the destabilising Munk couple with it); unsteady wake
/// memory and vortex shedding, the one mechanism a quasi-steady model cannot have and the reason a card dropped
/// exactly flat still flutters.
///
/// KNOWN APPROXIMATION in the node layout: the four nodes sit at the rectangle's exact standard deviation along each
/// flow-frame axis, so both in-plane second moments are right, but their CROSS moment ⟨r_c·r_s⟩ comes out zero where
/// a rectangle at an angle to the flow has one. It costs a little of the coupling between pitch and roll damping on
/// a panel flying diagonally, and nothing at all when the flow is along a box axis.
/// </summary>
public static class DebrisAero
{
    /// <summary>How far forward of the area centroid the centre of pressure sits when the flow is along the plate,
    /// as a fraction of the plate's extent along the flow: mid-chord − quarter-chord = 0.25c. It is multiplied by
    /// (1 − sin α) so it is 0.25c attached and 0 broadside (mid-chord, by symmetry).</summary>
    public const double CpWalk = 0.25;
    /// <summary>At or below this fineness a face behaves as a flat plate (sin law, walking CP, half-chord lag).</summary>
    public const double PlateFineness = 0.2;
    /// <summary>At or above this fineness a face behaves as a bluff box: the old quadratic law at the box centre.</summary>
    public const double BluffFineness = 1.0;
    /// <summary>Two-point Gauss–Legendre node, as a fraction of a half-extent: ±1/√3 integrates a quadratic
    /// exactly, so four strips carry the box's true second moment h²/3.</summary>
    public static readonly double GaussNode = 1.0 / System.Math.Sqrt(3.0);

    /// <summary>
    /// The air's force and moment about <paramref name="cgBody"/> on a piece's elements, body axes. Only the part of
    /// each element that is OUT of the water counts (<paramref name="waterUp"/> NaN = no water, all of it in the
    /// air); the wet part is <see cref="HydroBody"/>'s.
    /// </summary>
    /// <param name="windBody">The air's own velocity in body axes (zero for still air): every velocity below is the
    /// strip's velocity THROUGH THE AIR, so a piece in a wind is carried by it.</param>
    public static (Vec3 Force, Vec3 Moment) Forces(IReadOnlyList<HydroElement> els, Vec3 cgBody, RigidBodyState s,
        double rho, double waterUp, Vec3 windBody)
    {
        bool water = !double.IsNaN(waterUp);
        Vec3 f = Vec3.Zero, m = Vec3.Zero;
        for (int i = 0; i < els.Count; i++)
        {
            HydroElement e = els[i];
            if (e.NoDrag) continue;
            double air = 1.0;
            if (water)
            {
                double depth = s.Position.Z + s.Attitude.Rotate(e.Centre - cgBody).Z + waterUp;
                air = 1.0 - HydroElement.SubmergedFraction(depth, e.VerticalHalf(s.Attitude));
            }
            if (air <= 1e-6) continue;
            Box(e.Centre - cgBody, e.Half, air, s, rho, windBody, 1.0, NoOverride, ref f, ref m);
        }
        return (f, m);
    }

    /// <summary>One box in its own right, for a body that is not a wreck piece: a jettisoned canopy, a pilot's torso
    /// or a limb. <paramref name="centreFromCg"/> is the box centre relative to the CG, body axes.</summary>
    /// <param name="cdScale">A multiplier on every face's Cd·A. 1 uses the box's own
    /// <see cref="WaterPhysics.CdForFineness"/>; anything else is for a body whose drag AREA is measured rather than
    /// derived — a human in free fall, whose areas are back-solved from published terminal speeds
    /// (<see cref="SkydiverAero"/>) and must come out exactly, while the GEOMETRY still decides where the force acts
    /// and so how it tumbles.</param>
    public static (Vec3 Force, Vec3 Moment) Forces(Vec3 centreFromCg, Vec3 half, double airShare, RigidBodyState s,
        double rho, Vec3 windBody, double cdScale = 1.0)
    {
        Vec3 f = Vec3.Zero, m = Vec3.Zero;
        if (airShare > 1e-6) Box(centreFromCg, half, airShare, s, rho, windBody, cdScale, NoOverride, ref f, ref m);
        return (f, m);
    }

    /// <summary>No per-axis drag area given: each face uses the box's own Cd × its own area.</summary>
    public static readonly Vec3 NoOverride = new(-1, -1, -1);

    /// <summary>
    /// One box whose DRAG AREAS ARE MEASURED rather than derived from the box — a canopy shell, whose broadside and
    /// edge-on Cd·A are its own written-down numbers (<see cref="Vr.CanopyFreeBody.DragAreaM2"/>) and whose box is
    /// only a stand-in for WHERE that force acts. The half-extents still set the geometry: the strips, the chord the
    /// centre of pressure walks along, and the fineness that decides how plate-like each face is.
    /// </summary>
    /// <param name="cdAreaM2">Cd·A for motion along body x, y, z, m². A non-positive component falls back to the
    /// box's own Cd × face area.</param>
    public static (Vec3 Force, Vec3 Moment) Forces(Vec3 centreFromCg, Vec3 half, Vec3 cdAreaM2, double airShare,
        RigidBodyState s, double rho, Vec3 windBody)
    {
        Vec3 f = Vec3.Zero, m = Vec3.Zero;
        if (airShare > 1e-6) Box(centreFromCg, half, airShare, s, rho, windBody, 1.0, cdAreaM2, ref f, ref m);
        return (f, m);
    }

    /// <summary>A body made of several boxes about a CG of its own: the <see cref="SegmentBody"/> a pilot is.</summary>
    public static (Vec3 Force, Vec3 Moment) Forces(IReadOnlyList<Segment> segments, RigidBodyState s, double rho,
        Vec3 windBody, double cdScale)
    {
        Vec3 f = Vec3.Zero, m = Vec3.Zero;
        for (int i = 0; i < segments.Count; i++)
        {
            Segment g = segments[i];
            Box(g.Centre, g.Half, 1.0, s, rho, windBody, cdScale, NoOverride, ref f, ref m);
        }
        return (f, m);
    }

    /// <summary>One box of a jointed body: its centre RELATIVE TO THAT BODY'S CG, its half-extents, and its mass
    /// (which <see cref="SegmentBody"/> uses for the CG and the inertia, not this file).</summary>
    public readonly struct Segment
    {
        public readonly Vec3 Centre, Half;
        public readonly double MassKg;
        public Segment(Vec3 centre, Vec3 half, double massKg) { Centre = centre; Half = half; MassKg = massKg; }
        public Segment MovedTo(Vec3 centre) => new(centre, Half, MassKg);
        /// <summary>Frontal area across body axis k, m² — what a drag area is scaled against.</summary>
        public double FaceArea(int k) => k == 0 ? 4 * Half.Y * Half.Z : k == 1 ? 4 * Half.X * Half.Z : 4 * Half.X * Half.Y;
    }

    /// <summary>
    /// One box: four Gauss strips × three faces. Everything in the class summary happens here, and nothing else does.
    /// </summary>
    private static void Box(Vec3 rc, Vec3 half, double airShare, RigidBodyState s, double rho, Vec3 windBody,
        double cdScale, Vec3 cdAreaM2, ref Vec3 force, ref Vec3 moment)
    {
        System.Span<double> h = stackalloc double[3] { half.X, half.Y, half.Z };
        System.Span<double> area = stackalloc double[3];
        System.Span<double> cd = stackalloc double[3];
        System.Span<double> plate = stackalloc double[3];
        for (int k = 0; k < 3; k++)
        {
            int i = (k + 1) % 3, j = (k + 2) % 3;
            area[k] = 4.0 * h[i] * h[j];
            if (area[k] <= 0.0) continue;
            double f = 2.0 * h[k] / System.Math.Sqrt(area[k]);
            // Cd·A: the box's own, unless the caller measured it (a canopy shell). The fineness below is the box's
            // either way — it is geometry, and it decides the CP walk and the strip layout, not the size of the force.
            double given = Comp(cdAreaM2, k);
            cd[k] = given > 0.0 ? given / area[k] : WaterPhysics.CdForFineness(f) * cdScale;
            plate[k] = System.Math.Clamp((BluffFineness - f) / (BluffFineness - PlateFineness), 0.0, 1.0);
        }
        // The plate is the face across the SHORTEST axis; the strips are laid out in that face's own flow frame.
        int t = h[0] <= h[1] ? (h[0] <= h[2] ? 0 : 2) : (h[1] <= h[2] ? 1 : 2);
        int ax = (t + 1) % 3, bx = (t + 2) % 3;
        Vec3 nT = Axis(t);
        Vec3 vC = s.Velocity + Vec3.Cross(s.Rates, rc) - windBody;
        double vCm = vC.Length;
        if (vCm < 1e-6) return;

        // CHORDWISE is along the plate's own motion through the air, SPANWISE across it — so a panel flying sideways
        // has its "chord" along its span, which is what a plate does.
        double vnT = Comp(vC, t);
        Vec3 vip = vC - nT * vnT;
        double vipm = vip.Length;
        Vec3 eC = vipm > 1e-9 ? vip * (1.0 / vipm) : Axis(ax);
        Vec3 eS = Vec3.Cross(nT, eC);
        double eSm = eS.Length;
        eS = eSm > 1e-9 ? eS * (1.0 / eSm) : Axis(bx);
        double ca = Comp(eC, ax), cb = Comp(eC, bx), sa = Comp(eS, ax), sb = Comp(eS, bx);
        // Support width along the chord (what the CP walk is a fraction of) and the exact standard deviation of a
        // uniform rectangle's area along each flow-frame axis, which is what the two Gauss nodes must sit at.
        double chord = 2.0 * (System.Math.Abs(ca) * h[ax] + System.Math.Abs(cb) * h[bx]);
        double sigC0 = System.Math.Sqrt(ca * ca * h[ax] * h[ax] + cb * cb * h[bx] * h[bx]) * GaussNode;
        double sigS = System.Math.Sqrt(sa * sa * h[ax] * h[ax] + sb * sb * h[bx] * h[bx]) * GaussNode;

        // HOW ATTACHED THE PLATE IS. Three things move together with it, and all three are textbook:
        //   the CP          mid-chord when the flow is broadside and separated, quarter chord when it is along the
        //                   plate and attached (CpWalk·(1 − sin α), the measured plate series);
        //   the LAG         the circulation is set by the flow a half chord aft of where the force acts (Theodorsen's
        //                   three-quarter-chord point) when attached, and by the local flow when separated;
        //   the SPREAD      and this is the one that decides whether a plate can autorotate at all. Attached, the
        //                   chordwise loading is the bound vortex sheet ∝ √((c−x)/x), whose strength is one number
        //                   set at one point — so it does NOT resist a pitch rate strip by strip, and collapses onto
        //                   its own centroid, the quarter chord. Separated, the pressure is spread over the chord and
        //                   each strip answers for itself, which is the damping.
        // Written out, dM/dω over the four nodes comes to −k·|v|·(x̄² + σ_c² − lag·x̄): exactly +k|v|c²/16 attached
        // (the classical driving moment of a plate pivoting about mid-chord, its aerodynamic centre being ahead of
        // its pivot) and exactly −k|v|c²/12 broadside (the uniform strip damping). Nothing is tuned between them;
        // the two ends are the two textbook answers and sin α is the only thing in between.
        double sinA = System.Math.Abs(vnT) / vCm;
        double att = (1.0 - sinA) * plate[t];
        double xbar = CpWalk * chord * att;
        double lag = 2.0 * xbar;
        double sigC = sigC0 * (1.0 - att);

        for (int nc = -1; nc <= 1; nc += 2)
            for (int ns = -1; ns <= 1; ns += 2)
            {
                Vec3 at = rc + eC * (xbar + nc * sigC) + eS * (ns * sigS);
                Vec3 vq = s.Velocity + Vec3.Cross(s.Rates, lag > 1e-12 ? at - eC * lag : at) - windBody;
                double vqm = vq.Length;
                if (vqm < 1e-9) continue;
                for (int k = 0; k < 3; k++)
                {
                    if (area[k] <= 0.0 || cd[k] <= 0.0) continue;
                    double vk = Comp(vq, k);
                    // |v|·v_n is the plate's Cn = Cd_max·sin α; v_n·|v_n| is the bluff box's face-on drag.
                    double vref = plate[k] * vqm + (1.0 - plate[k]) * System.Math.Abs(vk);
                    double fk = -0.5 * rho * cd[k] * area[k] * 0.25 * airShare * vref * vk;
                    if (fk == 0.0) continue;
                    Vec3 fv = Axis(k) * fk;
                    force += fv;
                    moment += Vec3.Cross(at, fv);
                }
            }
    }

    private static double Comp(Vec3 v, int k) => k == 0 ? v.X : k == 1 ? v.Y : v.Z;
    private static Vec3 Axis(int k) => k == 0 ? new Vec3(1, 0, 0) : k == 1 ? new Vec3(0, 1, 0) : new Vec3(0, 0, 1);
}
