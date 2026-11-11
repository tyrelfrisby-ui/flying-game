using System;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// FLOATS ARE NOT ONLY A HULL AND A DRAG AREA — THEY FLY TOO. Ty 2026-11-09: "are the floats lift being modeled?
/// they carry their own weight plus some so they don't cause any trim drag."
///
/// They were not. Every number a float carried in this project was hydrodynamic — keel angles, deadrise, the step,
/// beam, the water rudder — and aerodynamically a float installation was mass plus a bigger cd0Area. So the wing
/// carried the floats' whole weight, which costs induced drag, and the floats made no pitching moment at all, so the
/// trim change a real installation produces could not exist in either direction.
///
/// WHAT A FLOAT IS, AERODYNAMICALLY: a very low aspect ratio lifting body. A Cub float is 4.38 m long and 0.54 m in
/// beam, so its aspect ratio is about 0.12 — it is far closer to a slender body than to a wing, and that is exactly
/// the regime where a wing's own theory fails and two terms are needed (Hoerner, Fluid-Dynamic Lift ch. 17-19):
///
///     CL = (pi*AR/2) * sin(a) cos(a)        Jones' slender-wing slope: small, linear, and all there is at low angle
///        + Cdc * sin(a) * |sin(a)|          the crossflow term: the separated flow over a bluff body, growing as
///                                           the square of the angle and dominant by the time it matters
///
/// The lift acts at the float's own centroid, which on a floatplane is well FORWARD of and well BELOW the centre of
/// gravity, so it makes a pitching moment and a drag couple that the old mass-plus-drag treatment could not.
///
/// AND THEY DO IT IN SIDESLIP TOO, which is where it costs the pilot something. Ty 2026-11-11: "make sure the floats
/// inertia lift drag etc is modeled correctly at the correct arm sideslip too", and, on what to do about the result:
/// "If floats decrease directional stability too much add fins on the stabilizers or a ventral fin on the belly ahead
/// of the rudder which are both common float plane mods … The j3 has none so leave it alone."
///
/// THE SIDE PLANE IS THE SAME BODY SEEN THE OTHER WAY. A float in sideslip is still a sharp-edged low-aspect-ratio
/// plate — length by DEPTH instead of length by beam, aspect ratio 0.12 instead of 0.12 — with a hard chine down one
/// side and a deck edge down the other. So <see cref="Coefficients"/> is used unchanged, with the side plane's own
/// aspect ratio and the sideslip angle. One relation, two planes; nothing new was invented for the second.
///
/// AND THAT IS WHY FLOATPLANES GROW FIN AREA. The force lands at the floats' centroid, which is AHEAD of the centre
/// of gravity — a float's bow reaches out past the propeller — so its yawing moment is DESTABILISING, the opposite
/// sign to the fin's. It is also BELOW the CG, so it takes away dihedral effect at the same time, exactly as a
/// ventral fin does. Both moments fall out of applying the force at the right point and would have to be invented if
/// it were applied anywhere else. Nothing here compensates for either: a J-3 on floats comes out less directionally
/// stable than a J-3 on wheels because a J-3 on floats IS, and the real aeroplane carries no finlets and no ventral
/// fin to fix it. Where a type really has those, they belong in its config as authored surfaces alongside its own
/// vertical tail — not as a coefficient here, and never added because a number came out uncomfortable.
///
/// THE INSTALLATION ANGLE IS THE ONE THING GEOMETRY DOES NOT GIVE US. A float is rigged to the airframe at an angle
/// chosen by the installer, and it is what decides how much of their own weight the floats carry in cruise. It is
/// therefore a per-type number (floats.incidenceDeg) rather than a constant, and it is the only tunable here —
/// everything else is the float's measured length, beam, depth and position.
/// </summary>
public static class FloatAero
{
    /// <summary>
    /// THE CHINE SHEDS A VORTEX, and that is where most of a float's lift comes from. Ty 2026-11-09, asked what makes
    /// them carry their weight: "it is the bottom shape they have a curved v then a clean break so the air that they
    /// push out at positive aoa effectively increases their width."
    ///
    /// That is a sharp-edged separation, not a bluff one. A V-bottom with a hard chine fixes the separation line, the
    /// flow rolls up into a leading-edge vortex along it, and the suction under that vortex adds lift well beyond
    /// anything the potential flow gives — the same mechanism that makes slender deltas and chined forebodies work,
    /// and it grows as the SQUARE of the angle, which is the "pushes out more at positive aoa" he describes.
    ///
    /// Polhamus' leading-edge suction analogy (NASA TN D-3767) is the standard way to carry it:
    ///
    ///     CL = Kp * sin(a) cos^2(a)  +  Kv * cos(a) sin^2(a)
    ///
    /// the first term the attached slender-wing lift (Kp = pi*AR/2 at this aspect ratio, and Helmbold's formula
    /// agrees to the digit), the second the vortex lift the sharp edge buys, with Kv near pi for a clean break.
    ///
    /// It matters by a factor of three where it counts: a bluff crossflow term gives a Cub float CL 0.023 at five
    /// degrees, the vortex analogy gives 0.040, and by ten degrees 0.125 against 0.065 — which is the difference
    /// between floats that carry a fifth of their weight and floats that carry all of it.
    /// </summary>
    public const double VortexK = Math.PI;

    /// <summary>
    /// THE VORTEX CONSTANT FOR A PLAIN FLAT PLATE, which is what a SKI is. The same two-term law covers it — Hoerner,
    /// Fluid-Dynamic Lift ch. VII pp. 7-17/7-19, writes a low-aspect-ratio plate's lift as the slender-wing slope
    /// πA/2 (0.0274 per degree per unit aspect ratio, the identical number to <see cref="VortexK"/>'s companion
    /// term above) plus a non-linear k'·sin²α·cos α, with k' = 1.8 fitted to a measured aspect-ratio-0.2 plate. A
    /// float's hard chine buys it π instead; a ski's plain edges buy 1.8. One relation, two constants, and the
    /// difference between them is a measurement rather than a preference.
    /// </summary>
    public const double PlateVortexK = 1.8;

    /// <summary>Lift and drag coefficients of ONE float at this angle of attack (rad), referenced to its planform.</summary>
    public static (double cl, double cd) Coefficients(double aspectRatio, double alphaRad) =>
        Coefficients(aspectRatio, alphaRad, VortexK);

    /// <summary>As above for any sharp-edged low-aspect-ratio body: <paramref name="vortexK"/> is π for a chined
    /// float and <see cref="PlateVortexK"/> for a plain plate such as a ski.</summary>
    public static (double cl, double cd) Coefficients(double aspectRatio, double alphaRad, double vortexK)
    {
        double s = Math.Sin(alphaRad), c = Math.Cos(alphaRad);
        double kp = 0.5 * Math.PI * aspectRatio;
        double cl = kp * s * c * c + vortexK * c * s * Math.Abs(s);
        // Vortex lift is a suction normal to the surface, so ALL of it tilts back with the angle: the drag is the
        // lift times the angle it acts at, which is what makes a slender chined body expensive at large alpha.
        double cd = cl * Math.Abs(s);
        return (cl, cd);
    }

    /// <summary>
    /// The floats' aerodynamic force and moment about the CG, body axes. Zero for a type without floats, and zero
    /// when they are not there to fly.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(AircraftConfig c, Vec3 bodyVelocity, double airDensity)
    {
        FloatsConfig? f = c.Floats;
        if (f is null || f.LengthM <= 0.1 || f.BeamM <= 0.01) return (Vec3.Zero, Vec3.Zero);
        double speed = bodyVelocity.Length;
        if (speed < 1.0) return (Vec3.Zero, Vec3.Zero);

        int count = 2;                                   // a pair; a single-float hull is a different shape entirely
        double areaOne = f.LengthM * f.BeamM;
        double ar = f.BeamM / f.LengthM;
        double alphaBody = Math.Atan2(bodyVelocity.Z, Math.Max(1e-6, bodyVelocity.X));
        double alpha = alphaBody + f.IncidenceDeg * Math.PI / 180.0;
        var (cl, cd) = Coefficients(ar, alpha);
        double q = 0.5 * airDensity * speed * speed;
        double lift = q * areaOne * count * cl, drag = q * areaOne * count * cd;

        // Lift perpendicular to the free stream, drag along it, both in the body's x-z plane.
        double ca = Math.Cos(alphaBody), sa = Math.Sin(alphaBody);
        Vec3 force = new(-drag * ca + lift * sa, 0, -drag * sa - lift * ca);

        // SIDESLIP, the same body in its other plane: length x DEPTH, and the sideslip angle in place of alpha. The
        // pair is treated as two floats working, which is right over the range that decides stability -- the gap
        // between them is about 2 m and each is about 4.4 m long, so one does not get into the other's wake until
        // past atan(2.0/4.4) ~ 24 deg of slip. Beyond that this overestimates, and the shielding is NOT modelled.
        if (f.DepthM > 0.01)
        {
            double betaBody = Math.Atan2(bodyVelocity.Y, Math.Max(1e-6, bodyVelocity.X));
            double areaSide = f.LengthM * f.DepthM;
            var (cly, cdy) = Coefficients(f.DepthM / f.LengthM, betaBody);
            double side = q * areaSide * count * cly, dragY = q * areaSide * count * cdy;
            double cb = Math.Cos(betaBody), sb = Math.Sin(betaBody);
            force += new Vec3(-dragY * cb + side * sb, -dragY * sb - side * cb, 0);
        }

        // At the floats' own centroid: half a length aft of the bow, on the keel. The SIDE force's true centre is the
        // centroid of the float's side AREA, and on a hull deepest at the step and tapering both ways that is within
        // a few per cent of mid-length, so it is applied at the same point rather than at a second one invented for
        // it.
        //
        // MEASURED, AND IT IS NOT WHERE THE ARGUMENT SAYS IT SHOULD BE. On the J-3 that centroid sits 0.15 m BEHIND
        // the centre of gravity, not ahead of it -- a float's bow reaches out past the propeller but its middle does
        // not -- so the crossflow side force's yawing moment comes out weakly STABILISING, about +18 N m at 30 m/s
        // and 3 m/s of slip, against 171 N m of roll. The roll term is the big one and its sign is the expected one:
        // a force below the CG takes away dihedral effect, exactly as a ventral fin does.
        //
        // SO THE TERM THAT REALLY DESTABILISES A FLOATPLANE IS MISSING, AND IT IS NOT A FORCE. It is the MUNK MOMENT
        // (Munk 1924, NACA TR 184; Allen and Perkins, NACA RM A50L07), a pure couple of (k2-k1) q Vol sin 2beta that
        // turns any closed slender body broadside however you apply the forces. At this condition it is of order
        // 120 N m nose-out on a pair of 1320s -- seven times the crossflow term and the opposite sign. It is NOT
        // added here because it needs the float's VOLUME and FloatsConfig carries length, beam and depth but no
        // displacement. That is a real gap, written down rather than papered over, and nothing has been bent to make
        // the numbers above read better. (Diehl's Engineering Aerodynamics has a dedicated "Stabilizing Area
        // Coefficients for Seaplane Floats" chart for sizing the fin needed to cancel this, which is independent
        // confirmation that the effect is real and that fin area is the documented remedy.)
        Vec3 at = new(f.BowX - 0.5 * f.LengthM, 0, f.KeelZ);
        Vec3 moment = Vec3.Cross(at - c.Mass.CgVec(), force);
        return (force, moment);
    }
}
