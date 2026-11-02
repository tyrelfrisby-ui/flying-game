using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Wreck;

/// <summary>The parts an airframe can come apart into. Each <see cref="HydroElement"/> belongs to exactly one; a
/// broken-off <see cref="AirframeComponent"/> takes the atoms <see cref="WreckAtoms.Of"/> lists for it.</summary>
public enum WreckAtom
{
    Cabin, Nose, TailCone,
    WingLInner, WingLOuter, WingRInner, WingROuter, AileronL, AileronR,
    HStab, ElevatorL, ElevatorR, VStab, Rudder,
    GearLeft, GearRight, GearNose, GearTail,
    NacelleL, NacelleR,
    /// <summary>A float compartment (FloatFlooding) — never breaks off.</summary>
    Float,
}

/// <summary>A hole in an element's skin: a point in the airframe body frame and an area. A torn face or a door
/// edge that spans a height is two openings, one at its top and one at its bottom, each with half the area —
/// whichever is deeper at the moment takes water in, the shallower lets air out.</summary>
public sealed class Opening
{
    public Vec3 Body;
    public double AreaM2;
    public string Why = "";
    public Opening(Vec3 body, double areaM2, string why) { Body = body; AreaM2 = areaM2; Why = why; }
}

/// <summary>
/// One closed volume of an airframe (a wing panel, the cabin, a fuel tank, an engine block): a box in the airframe
/// body frame carrying a displaced envelope volume, a mass, a floodable void, openings to the outside, and its flood
/// state (the air still trapped inside).
///
/// HYDROSTATICS. The skin displaces its whole envelope whatever is inside it, so buoyancy is ρ_w·g·(submerged part of
/// the envelope), and water that has got in is carried as added weight ρ_w·g·W at the centroid of the water. For an
/// element open to the sea and floating, the water inside stands at the outside level, and the two cancel to leave
/// exactly the buoyancy of its solid parts — Archimedes either way, but this way the flooding has a state.
///
/// FLOODING (quasi-steady orifice flow). Water comes in through the deepest openings and air leaves through the
/// shallowest, in series:
///     Q = Cd·sqrt(2·g·h) / sqrt(1/A_in² + (ρ_air/ρ_w)/A_out²)
/// from Δp_water + Δp_air = ρ_w·g·h with Δp = ρ·Q²/(2·Cd²·A²) through each opening (Bernoulli through a
/// sharp-edged orifice, Cd = <see cref="WaterPhysics.OrificeCd"/>). The head h is how far the inside free surface
/// stands below the outside surface, or below the air outlet when the outlet itself is under water:
///     h = d_inside − max(d_outlet, 0).
/// So an element floating with its outlet in the air fills until the inside level meets the outside level and then
/// stops — it settles lower, its outlet goes under, and the air bubbles out of it. With no outlet the air cannot
/// leave and only compresses (isothermal, p·V = const, p = atmosphere + ρ_w·g·d_inside). Trapped air is tracked as
/// FREE-AIR volume (at one atmosphere) so a body that dives has its pocket squeezed and one that rises has it expand
/// — and bubble out of the openings when it no longer fits.
/// </summary>
public sealed class HydroElement
{
    public string Name;
    public WreckAtom Atom;
    /// <summary>Box centre in the airframe body frame (config frame: x fwd, y right, z down), m.</summary>
    public Vec3 Centre;
    /// <summary>Box half-sizes along body x, y, z, m.</summary>
    public Vec3 Half;
    /// <summary>Outer displaced volume when fully submerged, m^3 (≤ the box: an airfoil fills 0.685 of its box).</summary>
    public double EnvelopeM3;
    /// <summary>Structure plus contents, kg.</summary>
    public double MassKg;
    /// <summary>Floodable volume inside the envelope, m^3 (envelope less structure and contents).</summary>
    public double VoidM3;
    /// <summary>A float compartment: the hull's displacement is FloatHydro's, so this element only adds the weight
    /// of the water that has got in.</summary>
    public bool BuoyancyElsewhere;
    /// <summary>No drag or added mass from this element (a float compartment; FloatHydro has the hull's).</summary>
    public bool NoDrag;
    public readonly List<Opening> Openings = new();

    /// <summary>Trapped air as free-air volume at one atmosphere, m^3.</summary>
    public double AirFreeM3;
    /// <summary>Trapped air as it is now (compressed at depth), m^3.</summary>
    public double AirM3 { get; private set; }
    /// <summary>Water inside, m^3.</summary>
    public double WaterM3 => System.Math.Max(0.0, VoidM3 - AirM3);
    /// <summary>Free-air volume that has left since the last <see cref="TakeEscaped"/>, m^3.</summary>
    public double EscapedFreeM3 { get; private set; }
    /// <summary>Free air let back IN since birth (an element out of the water drains and air takes the water's place), m^3.
    /// Escaped − admitted = the air lost, exactly.</summary>
    public double AdmittedFreeM3 { get; private set; }
    /// <summary>Last flow, m^3/s of water: + coming in, − draining out.</summary>
    public double LastFlowM3s { get; private set; }

    public HydroElement(string name, WreckAtom atom, Vec3 centre, Vec3 half, double envelopeM3, double massKg, double solidM3)
    {
        Name = name; Atom = atom; Centre = centre; Half = half; EnvelopeM3 = envelopeM3; MassKg = massKg;
        VoidM3 = System.Math.Max(0.0, envelopeM3 - solidM3);
        AirFreeM3 = VoidM3;
        AirM3 = VoidM3;
    }

    public HydroElement Clone()
    {
        var e = new HydroElement(Name, Atom, Centre, Half, EnvelopeM3, MassKg, 0)
        {
            VoidM3 = VoidM3, BuoyancyElsewhere = BuoyancyElsewhere, NoDrag = NoDrag, AirFreeM3 = AirFreeM3,
        };
        e.AirM3 = AirM3;
        e.AdmittedFreeM3 = AdmittedFreeM3;
        foreach (Opening o in Openings) e.Openings.Add(new Opening(o.Body, o.AreaM2, o.Why));
        return e;
    }

    public double TakeEscaped() { double e = EscapedFreeM3; EscapedFreeM3 = 0; return e; }

    public double TotalOpeningM2 { get { double a = 0; foreach (Opening o in Openings) a += o.AreaM2; return a; } }

    /// <summary>Add a hole that spans the box's height at <paramref name="at"/> (body frame): half at the top, half at
    /// the bottom of the element there.</summary>
    public void AddSpanningOpening(Vec3 at, double areaM2, string why)
    {
        if (areaM2 <= 0) return;
        Openings.Add(new Opening(new Vec3(at.X, at.Y, Centre.Z - Half.Z), areaM2 * 0.5, why));
        Openings.Add(new Opening(new Vec3(at.X, at.Y, Centre.Z + Half.Z), areaM2 * 0.5, why));
    }

    /// <summary>
    /// Leakage spread over the whole skin (drain holes in every rib bay, seams, rivet lines): lumped at the box's eight
    /// corners, an eighth each, so whatever way up the element floats there is an opening at its highest point to let
    /// air out and one at its lowest to let water in. Lumped at the centre instead, a panel that tipped on end kept a
    /// diving bell of air above its "drain holes" for ever.
    /// </summary>
    public void AddDistributedOpening(double areaM2, string why)
    {
        if (areaM2 <= 0) return;
        for (int i = 0; i < 8; i++)
            Openings.Add(new Opening(Centre + new Vec3((i & 1) == 0 ? -Half.X : Half.X, (i & 2) == 0 ? -Half.Y : Half.Y, (i & 4) == 0 ? -Half.Z : Half.Z), areaM2 / 8, why));
    }

    /// <summary>Half the box's extent along world vertical for this attitude, m.</summary>
    public double VerticalHalf(Quat q)
    {
        double zx = q.Rotate(new Vec3(1, 0, 0)).Z, zy = q.Rotate(new Vec3(0, 1, 0)).Z, zz = q.Rotate(new Vec3(0, 0, 1)).Z;
        return System.Math.Max(1e-4, System.Math.Abs(zx) * Half.X + System.Math.Abs(zy) * Half.Y + System.Math.Abs(zz) * Half.Z);
    }

    /// <summary>Fraction of the envelope below the surface (0..1) — the box's vertical extent against the depth of its
    /// centre. <paramref name="depthCentre"/> is + below the surface.</summary>
    public static double SubmergedFraction(double depthCentre, double halfH) =>
        System.Math.Clamp((depthCentre + halfH) / (2 * halfH), 0.0, 1.0);

    /// <summary>
    /// Advance the flooding by dt. <paramref name="waterUp"/> is the surface height (m, up) or NaN for no water;
    /// the body's state is the rigid body carrying this element and <paramref name="cgBody"/> its CG in body frame.
    /// </summary>
    public void Flood(Vec3 cgBody, RigidBodyState s, double waterUp, double dt)
    {
        LastFlowM3s = 0;
        if (VoidM3 <= 1e-9) { AirM3 = 0; return; }
        double rho = WaterPhysics.RhoWater, g = WaterPhysics.G, p0 = WaterPhysics.AtmospherePa;
        bool water = !double.IsNaN(waterUp);
        // Depths are + below the surface; with no water anywhere every depth is "above".
        double Depth(Vec3 body) => water ? (s.Position + s.Attitude.Rotate(body - cgBody)).Z + waterUp : -1e6;
        double halfH = VerticalHalf(s.Attitude), hTot = 2 * halfH;
        double dTop = Depth(Centre) - halfH;

        // Inlet (deepest openings) and outlet (shallowest). A single opening, or a face lying level, exchanges both
        // ways through itself: half its area each way.
        double aIn = 0, aOut = 0, dIn = double.NegativeInfinity, dOut = double.PositiveInfinity;
        if (Openings.Count > 0)
        {
            double dMin = double.PositiveInfinity, dMax = double.NegativeInfinity, aTot = 0;
            for (int i = 0; i < Openings.Count; i++)
            {
                double d = Depth(Openings[i].Body);
                dMin = System.Math.Min(dMin, d); dMax = System.Math.Max(dMax, d);
                aTot += Openings[i].AreaM2;
            }
            if (dMax - dMin < 0.01) { aIn = aOut = 0.5 * aTot; dIn = dMax; dOut = dMin; }
            else
            {
                double mid = 0.5 * (dMin + dMax);
                for (int i = 0; i < Openings.Count; i++)
                {
                    double d = Depth(Openings[i].Body);
                    if (d >= mid) { aIn += Openings[i].AreaM2; dIn = System.Math.Max(dIn, d); }
                    else { aOut += Openings[i].AreaM2; dOut = System.Math.Min(dOut, d); }
                }
            }
        }
        bool vented = aOut > 0;
        bool inletWet = aIn > 0 && dIn > 0;

        // Pocket pressure and volume (fixed point: the volume sets the inside level, the level sets the pressure).
        double n = AirFreeM3, pAir = p0, vAir = System.Math.Min(VoidM3, n);
        for (int k = 0; k < 4; k++)
        {
            double dInside = dTop + hTot * vAir / VoidM3;
            if (vented && dOut < dInside) pAir = p0 + rho * g * System.Math.Max(dOut, 0.0);            // open to the outlet
            else if (inletWet || (vented && dOut > 0)) pAir = p0 + rho * g * System.Math.Max(dInside, 0.0); // squeezed by the sea
            else pAir = p0;
            vAir = System.Math.Min(VoidM3, n * p0 / pAir);
        }
        // Air that no longer fits (the body rose, the pocket expanded) bubbles out — if there is any way out.
        double nMax = VoidM3 * pAir / p0;
        if (n > nMax && Openings.Count > 0) { EscapedFreeM3 += n - nMax; n = nMax; }

        double dIn2 = dTop + hTot * vAir / VoidM3;
        double rhoAir = WaterPhysics.RhoAirSeaLevel * pAir / p0;
        double q = 0;
        if (vented && aIn > 0)
        {
            double series = System.Math.Sqrt(1.0 / (aIn * aIn) + (rhoAir / rho) / (aOut * aOut));
            double outletLevel = System.Math.Max(dOut, 0.0);
            if (inletWet && dIn2 > outletLevel + 1e-9)
            {
                double h = dIn2 - outletLevel;
                q = WaterPhysics.OrificeCd * System.Math.Sqrt(2 * g * h) / series;
                // No overshoot: stop where the inside level reaches the outlet / outside level.
                double vEq = System.Math.Clamp(VoidM3 * (outletLevel - dTop) / hTot, 0.0, VoidM3);
                q = System.Math.Min(q, System.Math.Max(0.0, vAir - vEq) / dt);
            }
            else if (vAir < VoidM3)
            {
                // Draining: the inside level stands above the outside level (or above an inlet that is in the air).
                double target = System.Math.Min(dIn, 0.0);
                double h = target - dIn2;
                if (h > 1e-9)
                {
                    q = -WaterPhysics.OrificeCd * System.Math.Sqrt(2 * g * h) / series;
                    double vEq = System.Math.Clamp(VoidM3 * (target - dTop) / hTot, 0.0, VoidM3);
                    q = -System.Math.Min(-q, System.Math.Max(0.0, vEq - vAir) / dt);
                }
            }
        }
        double dn = -q * dt * pAir / p0;
        double nNew = System.Math.Clamp(n + dn, 0.0, nMax);
        if (nNew < n) EscapedFreeM3 += n - nNew; else AdmittedFreeM3 += nNew - n;
        n = nNew;
        AirFreeM3 = n;
        AirM3 = System.Math.Min(VoidM3, n * p0 / pAir);
        LastFlowM3s = q;
    }

    /// <summary>Re-evaluate the air volume for the current pressure without flowing (used once at birth).</summary>
    public void Settle(Vec3 cgBody, RigidBodyState s, double waterUp) => Flood(cgBody, s, waterUp, 1e-9);

    // ---- drag / added mass -------------------------------------------------------------------------------------

    /// <summary>Drag constant 0.5·ρ·Cd·A for motion along body axis <paramref name="axis"/> (0 x, 1 y, 2 z), with the
    /// projected area the box face across that axis and Cd from its fineness (<see cref="WaterPhysics.CdForFineness"/>).</summary>
    public double DragConstant(int axis, double rho)
    {
        double dx = 2 * Half.X, dy = 2 * Half.Y, dz = 2 * Half.Z;
        (double along, double a, double b) = axis == 0 ? (dx, dy, dz) : axis == 1 ? (dy, dx, dz) : (dz, dx, dy);
        double area = a * b;
        if (area <= 0) return 0;
        double cd = WaterPhysics.CdForFineness(along / System.Math.Sqrt(area));
        return 0.5 * rho * cd * area;
    }

    /// <summary>
    /// Added mass for motion along a body axis when fully submerged, kg: strip theory over the longer of the two
    /// cross dimensions, with the two-dimensional added mass of a flat plate / ellipse moving broadside,
    /// ρ·π·(w/2)² per unit length, w the shorter cross dimension (Newman, Marine Hydrodynamics, 1977, §4.13).
    /// That is exact for a long cylinder and an upper bound for a short plate (DNV-RP-C205 Table A-2 gives 0.58-1.0
    /// of it for plates of aspect 1..∞).
    /// </summary>
    public double AddedMass(int axis)
    {
        double dx = 2 * Half.X, dy = 2 * Half.Y, dz = 2 * Half.Z;
        (double a, double b) = axis == 0 ? (dy, dz) : axis == 1 ? (dx, dz) : (dx, dy);
        double w = System.Math.Min(a, b), len = System.Math.Max(a, b);
        return WaterPhysics.RhoWater * System.Math.PI * 0.25 * w * w * len;
    }
}
