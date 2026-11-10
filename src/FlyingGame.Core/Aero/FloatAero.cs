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
/// THE INSTALLATION ANGLE IS THE ONE THING GEOMETRY DOES NOT GIVE US. A float is rigged to the airframe at an angle
/// chosen by the installer, and it is what decides how much of their own weight the floats carry in cruise. It is
/// therefore a per-type number (floats.incidenceDeg) rather than a constant, and it is the only tunable here —
/// everything else is the float's measured length, beam and position.
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

    /// <summary>Lift and drag coefficients of ONE float at this angle of attack (rad), referenced to its planform.</summary>
    public static (double cl, double cd) Coefficients(double aspectRatio, double alphaRad)
    {
        double s = Math.Sin(alphaRad), c = Math.Cos(alphaRad);
        double kp = 0.5 * Math.PI * aspectRatio;
        double cl = kp * s * c * c + VortexK * c * s * Math.Abs(s);
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
        // At the floats' own centroid: half a length aft of the bow, on the keel.
        Vec3 at = new(f.BowX - 0.5 * f.LengthM, 0, f.KeelZ);
        Vec3 moment = Vec3.Cross(at - c.Mass.CgVec(), force);
        return (force, moment);
    }
}
