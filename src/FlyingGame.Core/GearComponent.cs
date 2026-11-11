using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>How well a component's mass is known. It exists so that "which inputs did you invent?" is answerable
/// MECHANICALLY rather than by reading comments: a test asserts every <see cref="MassTier.Known"/> component carries a
/// non-empty <see cref="GearComponent.MassSource"/>.</summary>
public enum MassTier
{
    /// <summary>A published or weighed figure. <see cref="GearComponent.MassSource"/> is REQUIRED and names it.</summary>
    Known,
    /// <summary>Derived from a rule fitted to known figures, or from a shape and a material. The source says which.</summary>
    Estimated,
    /// <summary>What is left when everything known is subtracted from a measured empty weight. Nothing in the gear
    /// catalogue is this; it is here because the airframe build-up's components are, and both read this type.</summary>
    Residual,
}

/// <summary>
/// The solid a component's inertia is DERIVED from. Never hand-set: a wheel really is a disc and a ski really is a
/// slab, and a fitted tensor would have nothing to check it against.
/// </summary>
public enum InertiaShape
{
    /// <summary>No inertia of its own — a strut, a bracket, a pedestal. Honest for a slender thing whose own tensor
    /// is small next to m·r² at a metre from the CG.</summary>
    Point,
    /// <summary>A uniform rectangular solid of the given full extents along the body axes.</summary>
    Box,
    /// <summary>A WHEEL: a disc (or short cylinder) spinning about the body y axis. Its polar inertia comes from
    /// <see cref="TireSpec.InertiaKgM2"/>, which already builds it part by part, so the spin-up model and the mass
    /// build-up cannot drift apart.</summary>
    Disc,
    /// <summary>A flat slab of chord, span and thickness, pitched nose-up by its own incidence — a ski.</summary>
    Slab,
}

/// <summary>
/// WHO COMPUTES THIS COMPONENT'S AERODYNAMICS. The single most dangerous failure in a component build-up is not drag
/// living apart from mass: it is drag COUNTED TWICE, because nothing fails loudly when it is.
/// <see cref="Aero.FloatAero"/> already returns a force AND a moment at the floats' own position; a catalogue that
/// also handed a float a drag area for something else to sum would double it in silence.
/// </summary>
public enum AeroOwner
{
    /// <summary>Nothing. A brake disc inside a wheel, a retracted leg.</summary>
    None,
    /// <summary>THIS catalogue: <see cref="GearAero"/> computes it from the component's own area and coefficients and
    /// applies it at <see cref="GearComponent.AeroRefPointBody"/>.</summary>
    Components,
    /// <summary>The wing/tail strip model. Nothing here may add to it.</summary>
    StripModel,
    /// <summary><see cref="Aero.FloatAero"/>, which already does lift, drag, side force and the moments at the floats'
    /// own position. Float components therefore carry mass and inertia and a drag area of ZERO.</summary>
    FloatAero,
    /// <summary>The fuselage's own cd0Area.</summary>
    FuselageCd0,
}

/// <summary>
/// ONE PHYSICAL THING BOLTED TO THE AEROPLANE — a wheel, a fairing, a ski, a float — described ONCE, for everyone.
///
/// Ty 2026-11-11: "the lift drag moment at the actual location need to be accurate and the inertia of the component
/// needs to be added". So a fitment is not a mass and an arm. It is a mass, the position of ITS OWN centre of mass in
/// three dimensions, ITS OWN inertia derived from its shape, and — where it owns them — aerodynamics applied at its
/// own reference point, which is what makes a wheel hanging below and ahead of the CG a pitching moment rather than
/// a number taken off the thrust.
///
/// THERE IS ONE DESCRIPTION AND BOTH CONSUMERS READ IT. The mass build-up reads this class through
/// <see cref="GearMass"/>; the aerodynamics reads the SAME instance through <see cref="GearAero"/>, and only where
/// <see cref="AeroOwnedBy"/> says <see cref="AeroOwner.Components"/>. This project lost a night to the rig solver and
/// the sim evaluating different propellers because one question had two implementations, and the fix was to put the
/// predicate in one place (<see cref="PropModel.IntegratesShaft"/>). The same discipline, with the sharper edge that
/// a double-counted drag area fails silently where a disagreeing propeller at least flew strangely.
///
/// WHAT THIS CLASS DOES NOT REINVENT. <see cref="TireSpec"/> already carries a tyre's mass, radius, section width,
/// part-by-part own inertia and a <see cref="TireSpec.Source"/> with its estimates labelled; a wheel component is
/// built FROM it, not beside it. <see cref="SkiGear"/> already derives a ski's size, mass, centre and drag area from
/// the leg's geometry; a ski component is built FROM it. Every number that already exists keeps its one home.
///
/// TWO POSITIONS, NOT ONE. <see cref="CmBody"/> is where the mass is; <see cref="AeroRefPointBody"/> is where the
/// force acts. On a tyre they are the same point (the axle). On a float they are not — the mass is near mid-float and
/// the centre of pressure is forward of it and moves with incidence — which is why the two are separate fields and
/// not one.
///
/// FRAME. Both positions are in the config datum frame — x forward, y right, z DOWN — the same frame as
/// <see cref="DataContracts.GearConfig.Pos"/> and <see cref="DataContracts.FloatsConfig.KeelZ"/>, so a component's
/// position comes off geometry already in the config. Inertia terms are about the component's OWN centre of mass in
/// BODY axes, so summing them needs only the parallel-axis shift and no rotations.
/// </summary>
public sealed class GearComponent
{
    // ---- identity ------------------------------------------------------------------------------------------------
    public string Id = "", Name = "";

    /// <summary>The airframe component this leaves with — "fuselage", "wing", "tail". A gear leg's bracketry goes
    /// when its structure goes, and the breakup model needs to know which piece carries it.</summary>
    public string AttachTo = "fuselage";

    // ---- mass ----------------------------------------------------------------------------------------------------
    public double MassKg;
    public MassTier MassTier = MassTier.Estimated;

    /// <summary>Where the mass figure came from. REQUIRED when <see cref="MassTier"/> is <see cref="MassTier.Known"/>
    /// and checked by a test; on an Estimated component it says which rule produced it and what the rule was fitted
    /// to.</summary>
    public string MassSource = "";

    // ---- placement and inertia -----------------------------------------------------------------------------------
    /// <summary>This component's own centre of mass, config datum frame (x forward, y right, z DOWN). It TRACKS THE
    /// GEOMETRY — <see cref="DataContracts.GearConfig.Pos"/> is the CONTACT POINT, not the centre of mass, and
    /// fitting a bigger tyre moves both.</summary>
    public Vec3 CmBody;

    public InertiaShape Shape = InertiaShape.Point;

    /// <summary>The shape's dimensions, metres: Box = full extents (x, y, z); Disc = (radius, width, unused), about
    /// body y; Slab = (chord, span, thickness) pitched by <see cref="IncidenceRad"/>; Point = unused.</summary>
    public double DimX, DimY, DimZ;

    /// <summary>DISC ONLY: polar inertia about the axle, kg·m², from <see cref="TireSpec.InertiaKgM2"/>. Zero makes
    /// it a uniform solid cylinder instead — correct for a wheel with no catalogue entry, wrong for a tyre, whose
    /// mass is nearly all at the tread.</summary>
    public double DiscPolarKgM2;

    /// <summary>SLAB ONLY: how it is rigged, radians, positive nose up. Carried because a pitched slab makes a real
    /// Ixz, which is small on one ski and is not small on a pair of 2.4 m Beaver skis.</summary>
    public double IncidenceRad;

    /// <summary>Does this component spin? A wheel's own Iyy is then its POLAR inertia about the axle, and
    /// <see cref="WheelSpin"/> owns the spin state — from the same <see cref="TireSpec.InertiaKgM2"/> this component
    /// is built from, so the two cannot disagree.</summary>
    public bool Spins;

    // ---- aerodynamics --------------------------------------------------------------------------------------------
    public AeroOwner AeroOwnedBy = AeroOwner.None;

    /// <summary>Where lift, drag and therefore the moment act. NOT necessarily <see cref="CmBody"/>.</summary>
    public Vec3 AeroRefPointBody;

    /// <summary>Equivalent flat-plate area, m² (Cd × reference area, already multiplied out, including
    /// <see cref="InterferenceFactor"/>). Zero unless <see cref="AeroOwnedBy"/> is <see cref="AeroOwner.Components"/>
    /// — a float's is ZERO because <see cref="Aero.FloatAero"/> owns it.</summary>
    public double DragAreaM2;

    /// <summary>LIFTING COMPONENTS ONLY (a ski): plan area, aspect ratio and rig angle for the plate relation. A
    /// bluff body leaves these at zero and makes drag only.</summary>
    public double PlanAreaM2, AspectRatio;

    /// <summary>What the junction costs on top of the parts: a leg into a fuselage, a leg into a wheel. 1.0 = none.
    /// It is folded into <see cref="DragAreaM2"/> when the component is built, and kept here so the factor is
    /// visible and can be checked rather than being buried in a product.</summary>
    public double InterferenceFactor = 1.0;

    /// <summary>Source for the aerodynamic figures, with the same discipline as <see cref="MassSource"/>.</summary>
    public string AeroSource = "";

    // ---- derived -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inertia about THIS COMPONENT'S OWN centre of mass, body axes, DERIVED from <see cref="Shape"/> — never
    /// hand-set. Ixz = Σm·x·z and the rest likewise, matching <see cref="MassProperties"/>: the tensor is
    /// [[Ixx, −Ixy, −Ixz], [−Ixy, Iyy, −Iyz], [−Ixz, −Iyz, Izz]].
    /// </summary>
    public MassProperties InertiaAboutCm
    {
        get
        {
            double m = MassKg;
            switch (Shape)
            {
                case InertiaShape.Box:
                    return new MassProperties(m,
                        m * (DimY * DimY + DimZ * DimZ) / 12.0,
                        m * (DimX * DimX + DimZ * DimZ) / 12.0,
                        m * (DimX * DimX + DimY * DimY) / 12.0, 0);

                case InertiaShape.Disc:
                {
                    // A wheel on an axle along body y. The POLAR inertia (about y) is the tyre catalogue's own
                    // part-by-part figure where there is one; the two TRANSVERSE inertias are half of it — the
                    // perpendicular-axis theorem for a body whose mass lies in a plane — plus the m·w²/12 the real
                    // width adds. Ixz is zero by symmetry.
                    double r = DimX, w = DimY;
                    double polar = DiscPolarKgM2 > 0 ? DiscPolarKgM2 : 0.5 * m * r * r;
                    double transverse = 0.5 * polar + m * w * w / 12.0;
                    return new MassProperties(m, transverse, polar, transverse, 0);
                }

                case InertiaShape.Slab:
                {
                    // Chord along the slab's own x, span along y, thickness along z, then pitched nose-up by
                    // IncidenceRad. Rotating the principal values into body axes gives Ixz = sinθcosθ(Ia − Ic).
                    double c = DimX, b = DimY, t = DimZ;
                    double ia = m * (b * b + t * t) / 12.0;   // about its own chordwise axis
                    double ib = m * (c * c + t * t) / 12.0;   // about its span
                    double ic = m * (c * c + b * b) / 12.0;   // about its normal
                    double s = System.Math.Sin(IncidenceRad), co = System.Math.Cos(IncidenceRad);
                    return new MassProperties(m, ia * co * co + ic * s * s, ib, ia * s * s + ic * co * co,
                                              s * co * (ia - ic));
                }

                default:
                    return new MassProperties(m, 0, 0, 0, 0);
            }
        }
    }
}

/// <summary>
/// The MASS side of a set of gear components — one of the two readers of <see cref="GearComponent"/>.
///
/// EXCHANGE, NOT ADDITION. A float set REPLACES the wheel legs; it does not join them. So every fitment returns the
/// COMPLETE undercarriage and the arithmetic is set against set (<see cref="Exchange"/>), never a sum of two
/// fitments. That is the shape <see cref="FloatExchange"/>'s scalar rule already had — floats installed minus gear
/// removed — now carrying the whole tensor instead of one number, which is the point of describing a component
/// rather than a weight.
/// </summary>
public static class GearMass
{
    public static double TotalKg(IEnumerable<GearComponent> set)
    {
        double m = 0;
        foreach (GearComponent g in set) m += g.MassKg;
        return m;
    }

    /// <summary>The set's own combined centre of mass, config datum frame.</summary>
    public static Vec3 CenterOfMass(IEnumerable<GearComponent> set)
    {
        Vec3 sum = Vec3.Zero; double m = 0;
        foreach (GearComponent g in set) { sum += g.CmBody * g.MassKg; m += g.MassKg; }
        return m > 0 ? sum / m : Vec3.Zero;
    }

    /// <summary>
    /// The set's mass properties about <paramref name="aboutBody"/> (pass the airframe's CG), by the parallel-axis
    /// theorem on each component's own derived tensor. Ixy and Iyz come out non-zero as soon as the set is not
    /// symmetric — a glider with one main wheel, an aeroplane that has lost a leg — and
    /// <see cref="MassProperties"/> carries them, so nothing is quietly dropped.
    /// </summary>
    public static MassProperties About(IEnumerable<GearComponent> set, Vec3 aboutBody)
    {
        double m = 0, ixx = 0, iyy = 0, izz = 0, ixz = 0, ixy = 0, iyz = 0;
        foreach (GearComponent g in set)
        {
            MassProperties own = g.InertiaAboutCm;
            Vec3 r = g.CmBody - aboutBody;
            m += g.MassKg;
            ixx += own.Ixx + g.MassKg * (r.Y * r.Y + r.Z * r.Z);
            iyy += own.Iyy + g.MassKg * (r.X * r.X + r.Z * r.Z);
            izz += own.Izz + g.MassKg * (r.X * r.X + r.Y * r.Y);
            ixz += own.Ixz + g.MassKg * r.X * r.Z;
            ixy += own.Ixy + g.MassKg * r.X * r.Y;
            iyz += own.Iyz + g.MassKg * r.Y * r.Z;
        }
        return new MassProperties(m, ixx, iyy, izz, ixz, ixy, iyz);
    }

    /// <summary>
    /// What swapping one COMPLETE undercarriage for another does to the airframe, about the OLD CG: fitted minus
    /// removed, mass and the whole tensor. The CG itself also moves, and that shift is <see cref="CombinedCg"/>'s,
    /// because it needs the airframe's own mass to compute and this function does not have it.
    /// </summary>
    public static MassProperties Exchange(IEnumerable<GearComponent> fitted, IEnumerable<GearComponent> removed,
                                          Vec3 aboutBody)
    {
        MassProperties a = About(fitted, aboutBody), b = About(removed, aboutBody);
        return new MassProperties(a.MassKg - b.MassKg, a.Ixx - b.Ixx, a.Iyy - b.Iyy, a.Izz - b.Izz,
                                  a.Ixz - b.Ixz, a.Ixy - b.Ixy, a.Iyz - b.Iyz);
    }

    /// <summary>Where the combined CG lands when an airframe of <paramref name="airframeKg"/> at
    /// <paramref name="airframeCg"/> carries this set — the whole point of keeping three dimensions of position,
    /// because a pair of floats moves the CG forward AND down.</summary>
    public static Vec3 CombinedCg(double airframeKg, Vec3 airframeCg, IEnumerable<GearComponent> set)
    {
        double m = TotalKg(set);
        double total = airframeKg + m;
        return total > 0 ? (airframeCg * airframeKg + CenterOfMass(set) * m) / total : airframeCg;
    }
}

/// <summary>
/// The AERO side of the same components, and ONLY of those whose <see cref="GearComponent.AeroOwnedBy"/> says
/// <see cref="AeroOwner.Components"/>. Everything else is somebody else's and is skipped here, which is the
/// mechanism that stops a float's drag being counted twice.
///
/// Ty 2026-11-11: "the lift drag moment at the actual location need to be accurate". Every force is applied at the
/// component's own <see cref="GearComponent.AeroRefPointBody"/> and the moment is the cross product — the pattern
/// <see cref="Aero.FloatAero"/> already uses, followed rather than reinvented.
///
/// WHY IT IS NOT A SCALAR. A main wheel on a Cub hangs about 0.9 m below the CG and a little ahead of it, so its drag
/// pitches the aeroplane nose down in proportion to q, and a 35 in bushwheel does it several times as hard as the
/// stock tyre. Subtract the drag from the thrust instead and the aeroplane loses the same speed and none of the trim
/// change — which is the half a pilot feels.
///
/// THE SKI USES THE FLOATS' OWN RELATION. A ski is a flat bottom with a hard edge all round at an aspect ratio near
/// 0.2 — the same body the float model is written for — so it uses <see cref="Aero.FloatAero.Coefficients"/>
/// unchanged (Jones' slender-wing term plus Polhamus vortex lift) with its own profile drag added. One
/// implementation, two users.
/// </summary>
public static class GearAero
{
    /// <summary>
    /// Force and moment about <paramref name="cgBody"/> from a set of components, body axes.
    /// <paramref name="bodyVelocity"/> is the air-relative velocity in body axes.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(IEnumerable<GearComponent> set, Vec3 bodyVelocity,
                                                    double airDensity, Vec3 cgBody)
    {
        double speed = bodyVelocity.Length;
        if (speed < 1.0) return (Vec3.Zero, Vec3.Zero);
        double q = 0.5 * airDensity * speed * speed;
        double alphaBody = System.Math.Atan2(bodyVelocity.Z, System.Math.Max(1e-6, bodyVelocity.X));
        double ca = System.Math.Cos(alphaBody), sa = System.Math.Sin(alphaBody);

        Vec3 force = Vec3.Zero, moment = Vec3.Zero;
        foreach (GearComponent g in set)
        {
            if (g.AeroOwnedBy != AeroOwner.Components) continue;
            double lift = 0, drag = q * g.DragAreaM2;
            if (g.PlanAreaM2 > 0)
            {
                double alpha = alphaBody + g.IncidenceRad;
                // A ski is a PLAIN plate, so its vortex constant is 1.8, not the chined float's pi.
                (double cl, double cdi) = Aero.FloatAero.Coefficients(g.AspectRatio, alpha, Aero.FloatAero.PlateVortexK);
                lift = q * g.PlanAreaM2 * cl;
                drag += q * g.PlanAreaM2 * cdi;
            }
            if (lift == 0 && drag == 0) continue;
            // Lift perpendicular to the free stream, drag along it, both in the body's x-z plane - as FloatAero does.
            Vec3 f = new(-drag * ca + lift * sa, 0, -drag * sa - lift * ca);
            force += f;
            moment += Vec3.Cross(g.AeroRefPointBody - cgBody, f);
        }
        return (force, moment);
    }

    /// <summary>The set's equivalent flat-plate area at zero incidence, m² — the number a drag polar is quoted in,
    /// and the one to compare an installation against a published cruise-speed penalty with. Components owned by
    /// somebody else contribute nothing, by construction.</summary>
    public static double FlatPlateAreaM2(IEnumerable<GearComponent> set)
    {
        double f = 0;
        foreach (GearComponent g in set) if (g.AeroOwnedBy == AeroOwner.Components) f += g.DragAreaM2;
        return f;
    }
}
