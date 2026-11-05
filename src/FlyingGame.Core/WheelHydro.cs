using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// A WHEEL on water (owner 2026-10-16: "make sure hydroplaning is accurate so tires can skim on water before
/// reaching land but getting too slow will flip the plane"; Ty 2026-11-03: the J-3 on wheels flipped at once when
/// skimming a pond, and a gentle touch in the Extra stopped it dead).
///
/// THE PHYSICS. The tyre is a cylinder of radius r and width b moving at V over the water with its bottom h below the
/// surface. The water meets the wetted FRONT arc of the tyre (the rear is ventilated — open to the air — at any speed
/// worth the name), from the bottom (angle 0) up to the waterline at φ_w = acos(1 − h/r). The pressure on that arc is
/// the planing pressure ½ρV²·C. Integrating it over the arc, normal to the surface:
///
///     lift  L = ½ρV² C b r sin φ_w = ½ρV² C b l_f      l_f = wetted length ahead of the bottom (the planform)
///     drag  D = ½ρV² C b r (1 − cos φ_w) = ½ρV² C b h   the frontal height in the water
///
/// so D/L = h/l_f = tan(φ_w/2): in planing (h small) the drag is the planing surface's L·tan τ, and as the tyre
/// sinks to its axle and beyond the same integral becomes the ploughing drag of a ventilated bluff body.
///
/// C IS NOT CHOSEN. Horne's total hydroplaning speed, V_p (kt) = 9 √p (NASA TN D-2056, 1963; ESDU 71026; FAA
/// AC 91-6A), is the speed at which the water's pressure on the tyre reaches the tyre's inflation pressure:
/// ½ρV_p²·C = p. Solving with Horne's 9 gives C = 2·6894.76/(1000·(9·0.514444)²) = 0.643, independent of p — which
/// is why the law has that form. (A ventilated circular cylinder in free-streamline theory has C_D ≈ 0.5–0.9;
/// 0.643 sits inside that.) Below V_p the water's pressure is below the inflation pressure and the tyre keeps its
/// shape, so the rigid cylinder is the right body; above V_p the water flattens the tyre, which only adds planform
/// (lift), so the rigid tyre is conservative there.
///
/// WHAT FOLLOWS FROM IT, with no other number: the wheel rides where L carries its share of the weight, so its ride
/// height rises with speed (at V_p the wetted planform is exactly the dry footprint, W/p); the most a wheel can lift
/// is at its axle (l_f = r), so below V_dig = √(2W/(ρ C b r)) it cannot hold itself up, sinks to 2r and ploughs —
/// that is the dig-in, and its drag, applied at the bottom of the tyre well below the centre of gravity, is the
/// nose-over moment, in proportion to the load. Nothing is a step: every term grows from zero with the immersion.
///
/// Also: buoyancy of the immersed segment (Archimedes; small — a wheel cannot float an aeroplane), ITTC-1957 friction
/// on the wetted arc (<see cref="FloatHydro.FrictionCoefficient"/>), and heave damping — the sink rate w changes the
/// incidence of the wetted surface by w/V (planing lift ∝ incidence, Savitsky), plus the cross-flow of a flat plate
/// (C_D 1.2, Hoerner, the same as <see cref="FloatHydro"/>). The spray sheet's momentum IS the pressure drag D (Green
/// 1936: the jet carries a planing plate's ideal drag), so it is not added again.
///
/// Not modelled (stated): the wheel's spin-up (its friction torque is a few newtons), and the gear leg in the water
/// once the tyre is under (a leg has a few square centimetres of frontal area against the tyre's hundreds).
/// </summary>
public static class WheelHydro
{
    public const double WaterDensityKgM3 = 1000.0;
    /// <summary>Tyre width as a fraction of its diameter. A 31×13 bushwheel is 0.42; a 6.00-6 on a light
    /// single is about 0.34; an 8.00-6 is 0.36. One number for all.</summary>
    public const double WidthPerDiameter = 0.38;
    /// <summary>The planing pressure coefficient behind Horne's law: ½ρV_p²·C = p with V_p = 9√p kt (see the class).</summary>
    public static readonly double PressureCoefficient = 2.0 * 6894.76 / (WaterDensityKgM3 * System.Math.Pow(9.0 * 0.514444, 2));
    /// <summary>Cross-flow drag coefficient of the tyre's wetted planform moving up or down through the surface.</summary>
    public const double HeaveDragCd = 1.2;

    /// <summary>
    /// Tyre pressure, in psi. Taken from the type when it is known, and otherwise estimated from the tyre's
    /// size and the aeroplane's weight, which is what sets it in reality: a 35-inch bushwheel on a Cub runs
    /// 8–12 psi, a 172's 6.00-6 runs about 30, and an airliner's runs north of 200.
    /// </summary>
    public static double TirePressurePsi(AircraftConfig c, GearConfig g)
    {
        // The type's own table first — the AFM figure, or the owner's weight rule, and half of it on
        // bushwheels. The size-based estimate below is only the last resort. Whichever it is, it is held to
        // the least pressure that can carry the leg's load at all (FootprintFloorPsi).
        double table = TirePressure.Psi(c, g, c.Bushwheels);
        if (table > 0) return System.Math.Max(table, FootprintFloorPsi(c, g));
        if (g.TirePressurePsi > 0) return System.Math.Max(g.TirePressurePsi, FootprintFloorPsi(c, g));
        double r = LandingGear.TireRadiusOf(c, g);
        double mass = c.Mass != null ? c.Mass.MassKg : 800;
        double width = 2 * r * WidthPerDiameter;
        double patchM2 = width * (0.55 * r);
        double perWheelN = mass * 9.81 / 2.2;                  // the mains carry most of it
        double pa = perWheelN / System.Math.Max(1e-4, patchM2);
        return System.Math.Clamp(pa / 6894.76, 6.0, 260.0);    // Pa → psi, held to what tyres really run
    }

    /// <summary>
    /// The least pressure at which this tyre carries its static share of the weight (<see cref="StaticLoadN"/>):
    /// load over b·r, the most planform it can present (the wetted front arc at its axle, the most this model's tyre
    /// can lift on). A tyre carries its load on a footprint of load / pressure; below this pressure that footprint is
    /// larger than the tyre — it is flat on its rim, and no tyre is run like that.
    ///
    /// Why it matters on water (Ty 2026-11-04: above V_p every aeroplane can ski unless its load exceeds the water's
    /// lift): at V_p, ½ρV_p²·C = p, the most the tyre can lift is p·b·r, so V_p is the speed at which the water can
    /// carry the static load only when the footprint fits — p ≥ load/(b·r). The owner's weight rule gives the Extra
    /// 18 psi on 0.155 m tyres: a 47 in² footprint for each main's 3.75 kN against 28 in² of tyre, so its "V_p" of
    /// 38 kt sat 11 kt below the speed its tyres can hold it up at, and it went over "above V_p". Held to the floor
    /// it is 29.8 psi (V_p 49 kt), and the Pitts 21.4 (42 kt); a 5.00-5 on an aerobatic single is run at 30–40.
    /// Every other type's tyre has room to spare and keeps its book or rule figure.
    /// </summary>
    public static double FootprintFloorPsi(AircraftConfig c, GearConfig g)
    {
        double r = LandingGear.TireRadiusOf(c, g), b = 2 * r * WidthPerDiameter;
        return StaticLoadN(c, g) / (b * r) / 6894.76;
    }

    /// <summary>The leg's share of the weight standing on level ground, from the moment balance of the main legs
    /// against the others (nose or tail) about the CG; the main legs share theirs equally.</summary>
    public static double StaticLoadN(AircraftConfig c, GearConfig g)
    {
        double w = (c.Mass != null ? c.Mass.MassKg : 800) * 9.81;
        double cgX = c.Mass != null ? c.Mass.CgVec().X : 0;
        static bool Main(GearConfig x) => !x.IsTailwheel && System.Math.Abs(x.Pos[1]) > 0.3;
        int nMain = 0, nOther = 0; double xMain = 0, xOther = 0;
        foreach (GearConfig x in c.Gear)
        {
            if (x.GearType == "float-keel" || x.GearType == "skid") continue;
            if (Main(x)) { nMain++; xMain += x.Pos[0]; } else { nOther++; xOther += x.Pos[0]; }
        }
        if (nMain == 0) return w / System.Math.Max(1, nOther);
        xMain /= nMain;
        if (nOther == 0) return w / nMain;
        xOther /= nOther;
        double span = xOther - xMain;
        double mainShare = System.Math.Abs(span) > 1e-6 ? System.Math.Clamp((xOther - cgX) / span, 0.0, 1.0) : 1.0;
        return Main(g) ? w * mainShare / nMain : w * (1 - mainShare) / nOther;
    }

    /// <summary>The speed at which a tyre at this pressure rides up on top of the water.</summary>
    public static double HydroplaneSpeedMs(double pressurePsi) => 9.0 * System.Math.Sqrt(pressurePsi) * 0.514444;

    /// <summary>Below this speed a tyre cannot hold up <paramref name="loadN"/> however deep it sinks (its planing lift
    /// is greatest at the axle, l_f = r, where the half-immersed tyre also displaces ρ g b π r²/2):
    /// √(2(W − ρ g b π r²/2)/(ρ C b r)).</summary>
    public static double DigInSpeedMs(AircraftConfig c, GearConfig g, double loadN)
    {
        double r = LandingGear.TireRadiusOf(c, g), b = 2 * r * WidthPerDiameter;
        double net = System.Math.Max(0.0, loadN - WaterDensityKgM3 * 9.81 * b * System.Math.PI * r * r / 2);
        return System.Math.Sqrt(2 * net / (WaterDensityKgM3 * PressureCoefficient * b * r));
    }

    /// <summary>The parts of the force, for tests and the HUD.</summary>
    public readonly struct Parts
    {
        public readonly double LiftN, BuoyancyN, HeaveN, PressureDragN, FrictionN, ImmersionM, WettedLengthM;
        public Parts(double lift, double buoy, double heave, double dp, double df, double h, double lf)
        { LiftN = lift; BuoyancyN = buoy; HeaveN = heave; PressureDragN = dp; FrictionN = df; ImmersionM = h; WettedLengthM = lf; }
    }

    /// <summary>
    /// The force on one tyre in water, WORLD axes (NED), and where it acts, as a world offset from the bottom of the
    /// tyre (the centre of pressure on the wetted arc). <paramref name="depthM"/> is how far the water surface is
    /// above the bottom of the tyre; <paramref name="contactVelWorld"/> the tyre's velocity relative to the water.
    /// <paramref name="planing"/> comes back true while the water's dynamic pressure is what is holding it up.
    /// </summary>
    public static Vec3 Force(AircraftConfig c, GearConfig g, double depthM, Vec3 contactVelWorld,
                             out bool planing, out Vec3 centreOffsetWorld, out Parts parts)
    {
        planing = false;
        centreOffsetWorld = Vec3.Zero;
        parts = default;
        if (depthM <= 0) return Vec3.Zero;

        double r = LandingGear.TireRadiusOf(c, g);
        double b = 2 * r * WidthPerDiameter;
        double h = System.Math.Min(depthM, 2 * r);
        double cosPhi = System.Math.Clamp(1 - h / r, -1, 1);
        double phi = System.Math.Acos(cosPhi), sinPhi = System.Math.Sin(phi);
        double lf = r * sinPhi;                                      // wetted planform ahead of the bottom

        var horiz = new Vec3(contactVelWorld.X, contactVelWorld.Y, 0);
        double v = horiz.Length;
        double w = contactVelWorld.Z;                                // + = sinking into the water
        const double rho = WaterDensityKgM3;
        double cp = PressureCoefficient;
        double q = 0.5 * rho * v * v;

        // Planing lift on the wetted arc, and its change with the sink rate: the incidence of the wetted surface (its
        // mean slope, φ_w/2) grows by w/V, and planing lift goes with the incidence.
        double lift = q * cp * b * lf;
        double tauE = System.Math.Max(phi / 2, 0.05);
        double heave = v > 0.5 ? lift / (v * tauE) * w : 0;
        heave += 0.5 * rho * HeaveDragCd * b * (2 * lf) * w * System.Math.Abs(w);
        double buoy = rho * 9.81 * b * r * r * (phi - sinPhi * cosPhi);
        double up = System.Math.Max(0.0, lift + heave) + buoy;       // water pushes, never pulls

        double dp = q * cp * b * h;
        double arc = r * phi;
        double df = arc > 1e-6 && v > 1e-3 ? q * b * arc * FloatHydro.FrictionCoefficient(v * arc / FloatHydro.WaterKinematicViscosity) : 0;

        planing = lift > buoy && h < r;
        parts = new Parts(lift, buoy, heave, dp, df, h, lf);

        Vec3 force = new(0, 0, -up);                                 // NED: up is −z
        if (v > 1e-6)
        {
            Vec3 dir = horiz / v;
            force -= dir * (dp + df);
            centreOffsetWorld = dir * (0.5 * lf) + new Vec3(0, 0, -0.5 * h);
        }
        else centreOffsetWorld = new Vec3(0, 0, -0.5 * h);
        return force;
    }
}
