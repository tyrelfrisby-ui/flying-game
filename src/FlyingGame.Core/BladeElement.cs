using System;

namespace FlyingGame.Core;

/// <summary>
/// A PROPELLER, INSTEAD OF A THRUST SCALAR WITH CORRECTIONS BOLTED ON (Ty 2026-11-09: "I'd like to make each of these
/// real science not lump sums if possible"). Blade-element momentum theory: the blade is cut into annular elements,
/// each element's own inflow is solved against the momentum it must impart to its annulus, and thrust and torque are
/// what the elements add up to. Glauert's method; Leishman, <i>Principles of Helicopter Aerodynamics</i> ch. 2-3, with
/// Prandtl's tip loss.
///
/// WHAT THIS REPLACES, and why it is one job and not five. The old model was thrust = eta*P/V capped by a static
/// figure of merit, with P-factor, the in-plane force, the spiral slipstream's yaw and the slipstream's swirl each
/// added afterwards as its own tuned coefficient. They are not separate effects: they are all consequences of a disc
/// of blades meeting the air at an angle, and they all fall out of the same integral.
///
///   thrust, torque    the axial and tangential sums over the elements — no efficiency constant, and no static
///                     figure of merit, because at V = 0 the solution is simply the one with no forward speed
///   efficiency        TV/(Q*Omega), a CURVE against advance ratio instead of the flat 0.75 every type carried
///   swirl             the torque IS the angular momentum in the slipstream, which is what the traced jet wants
///   P-factor          integrate around the azimuth as well as the radius: at alpha the down-going blade meets the
///                     air faster and at a higher angle, and the thrust centre moves off the hub by itself
///   in-plane force    the same azimuthal integral's tangential component — NormalForceK with the K removed
///   windmilling       negative torque, not a separate branch
///
/// The one thing it needs that a thrust scalar did not is BLADE GEOMETRY, and no config has it. Rather than invent
/// chord and twist per type, this takes a standard blade family: an activity-factor chord distribution and a constant
/// geometric pitch set at three-quarter radius. Two numbers per propeller, both of which have published values for
/// real blades, and both of which can be fitted to a type's own book figures (static thrust, climb, cruise at power).
/// </summary>
public static class BladeElement
{
    /// <summary>One propeller's geometry. <see cref="PitchAt75Deg"/> is the blade angle at 0.75 R, which with a
    /// constant geometric pitch sets the twist everywhere else.</summary>
    public readonly struct Geometry
    {
        public readonly int Blades;
        public readonly double RadiusM, HubFraction, ActivityFactor, PitchAt75Deg;
        public Geometry(int blades, double radiusM, double pitchAt75Deg, double activityFactor = 100.0, double hubFraction = 0.15)
        {
            Blades = Math.Max(1, blades); RadiusM = radiusM; PitchAt75Deg = pitchAt75Deg;
            ActivityFactor = activityFactor; HubFraction = Math.Clamp(hubFraction, 0.05, 0.5);
        }

        /// <summary>
        /// Chord at radius fraction x, from the activity factor. AF = (100000/16) * integral of (c/D) x^3 dx over the
        /// blade, so a shape function fixes the distribution and the AF fixes its scale: one published number per
        /// blade instead of a table nobody has. The shape is a conventional round-tipped blade — widest a little
        /// inboard of mid-span, tapering to the tip.
        /// </summary>
        public double ChordM(double x)
        {
            double s = ShapeAt(x);
            return ActivityFactor / (100000.0 / 16.0) / ShapeIntegral * s * (2.0 * RadiusM);
        }

        private static double ShapeAt(double x) => x <= 0.0 || x >= 1.0 ? 0.0 : Math.Pow(Math.Sin(Math.PI * Math.Pow(x, 0.75)), 0.85);

        /// <summary>The shape's own integral of s(x)*x^3, so ChordM scales to the requested activity factor.</summary>
        private static double ShapeIntegral
        {
            get
            {
                if (_shapeIntegral > 0) return _shapeIntegral;
                double sum = 0; const int N = 2000;
                for (int i = 0; i < N; i++) { double x = (i + 0.5) / N; sum += ShapeAt(x) * x * x * x / N; }
                return _shapeIntegral = sum;
            }
        }
        private static double _shapeIntegral;

        /// <summary>Blade angle at x, from a constant geometric pitch through 0.75 R.</summary>
        public double BladeAngleRad(double x)
        {
            double p = 2.0 * Math.PI * 0.75 * RadiusM * Math.Tan(PitchAt75Deg * Math.PI / 180.0);   // advance per turn, m
            return Math.Atan2(p, 2.0 * Math.PI * Math.Max(1e-4, x) * RadiusM);
        }
    }

    /// <summary>What the disc did to the air, and what the air did back.</summary>
    public readonly struct Result
    {
        public readonly double ThrustN, TorqueNm, PowerW, Efficiency, SwirlC, NormalN, SideN;
        public Result(double t, double q, double p, double eta, double swirlC, double normal, double side)
        { ThrustN = t; TorqueNm = q; PowerW = p; Efficiency = eta; SwirlC = swirlC; NormalN = normal; SideN = side; }
    }

    private const int Elements = 24;
    private const int Azimuths = 16;

    /// <summary>
    /// Solve the disc at this airspeed, angle of attack and rpm. <paramref name="alphaRad"/> is the angle between the
    /// free stream and the shaft; at zero the azimuthal loop collapses to the axial case and the in-plane forces are
    /// zero by symmetry.
    /// </summary>
    public static Result Solve(in Geometry g, double vAxialMs, double vCrossMs, double rpm, double rhoKgM3)
    {
        double omega = rpm * 2.0 * Math.PI / 60.0;
        if (omega <= 1e-3 || g.RadiusM <= 0.0) return new Result(0, 0, 0, 0, 0, 0, 0);
        double thrust = 0, torque = 0, normal = 0, angMom = 0;
        double dx = (1.0 - g.HubFraction) / Elements;
        bool axial = Math.Abs(vCrossMs) < 1e-6;
        int nAz = axial ? 1 : Azimuths;

        for (int i = 0; i < Elements; i++)
        {
            double x = g.HubFraction + (i + 0.5) * dx;
            double r = x * g.RadiusM, chord = g.ChordM(x), theta = g.BladeAngleRad(x);
            double sigma = g.Blades * chord / (2.0 * Math.PI * r);
            for (int k = 0; k < nAz; k++)
            {
                // Azimuth 0 is the blade going DOWN on the right of a right-hand prop, which is where a positive
                // angle of attack adds velocity and incidence — the whole of P-factor, from geometry.
                double psi = 2.0 * Math.PI * (k + 0.5) / nAz;
                double vTanLocal = omega * r + vCrossMs * Math.Cos(psi);
                // INDUCED VELOCITY, ABSOLUTE — not a fraction of the forward speed. Writing it as V(1+a), which is
                // the aeroplane-propeller convention, is degenerate at V = 0: the flow through the disc is zero
                // whatever a does, so the static case collapses (it gave 24 N where this propeller makes over a
                // thousand). Rotor analysis never writes it that way because hover IS V = 0. With vi and wi as
                // velocities, the annulus momentum balance is
                //     0.5 W^2 sigma Cn = 2 F (V + vi) vi          ->  vi = ( -V + sqrt(V^2 + W^2 sigma Cn / F) ) / 2
                //     0.5 W^2 sigma Ct = 2 F (V + vi) wi          ->  wi = W^2 sigma Ct / (4 F (V + vi))
                // and V = 0 is just another solution.
                double vi = 0.5 * Math.Sqrt(Math.Max(1.0, vTanLocal * 0.05)), wi = 0.0;
                double w2 = 0, cn = 0, ct = 0, phi = 0;
                for (int it = 0; it < 60; it++)
                {
                    double va = vAxialMs + vi, vt = Math.Max(1e-6, vTanLocal - wi);
                    phi = Math.Atan2(va, vt);
                    double alpha = theta - phi;
                    (double cl, double cd) = Section(alpha);
                    double sp = Math.Sin(phi), cp = Math.Cos(phi);
                    cn = cl * cp - cd * sp;          // along the shaft
                    ct = cl * sp + cd * cp;          // against the rotation
                    w2 = va * va + vt * vt;
                    // Prandtl tip (and root) loss.
                    double f = g.Blades * 0.5 * (1.0 - x) / Math.Max(1e-3, x * Math.Sin(Math.Abs(phi)));
                    double ftip = 2.0 / Math.PI * Math.Acos(Math.Clamp(Math.Exp(-f), -1, 1));
                    double fr = g.Blades * 0.5 * (x - g.HubFraction) / Math.Max(1e-3, x * Math.Sin(Math.Abs(phi)));
                    double froot = 2.0 / Math.PI * Math.Acos(Math.Clamp(Math.Exp(-fr), -1, 1));
                    double F = Math.Max(0.05, ftip * froot);
                    double viNew, wiNew;
                    double load = w2 * sigma * cn / F;
                    if (load >= 0.0)
                        viNew = 0.5 * (-vAxialMs + Math.Sqrt(Math.Max(0.0, vAxialMs * vAxialMs + load)));
                    else
                        viNew = 0.5 * (-vAxialMs + Math.Sign(vAxialMs) * Math.Sqrt(Math.Max(0.0, vAxialMs * vAxialMs + load)));
                    viNew = Math.Clamp(viNew, -0.9 * Math.Abs(vAxialMs) - 30.0, 60.0);
                    wiNew = w2 * sigma * ct / Math.Max(1e-6, 4.0 * F * Math.Max(1e-3, vAxialMs + viNew));
                    wiNew = Math.Clamp(wiNew, -0.5 * vTanLocal, 0.5 * vTanLocal);
                    double nvi = vi + 0.4 * (viNew - vi), nwi = wi + 0.4 * (wiNew - wi);
                    if (Math.Abs(nvi - vi) < 1e-6 && Math.Abs(nwi - wi) < 1e-6) { vi = nvi; wi = nwi; break; }
                    vi = nvi; wi = nwi;
                }
                double q = 0.5 * rhoKgM3 * w2 * g.Blades * chord * dx * g.RadiusM / nAz;
                double dT = q * cn, dQ = q * ct * r;
                thrust += dT; torque += dQ;
                angMom += dQ;
                if (!axial) normal += ct * q * Math.Cos(psi);   // the in-plane force, from the same elements
            }
        }
        double power = torque * omega;
        double eta = power > 1e-6 ? thrust * vAxialMs / power : 0.0;
        // The swirl the slipstream carries: C = Q / (rho * A * V_disc), the vortex strength the traced jet wants.
        double disc = Math.PI * g.RadiusM * g.RadiusM;
        double vDisc = Math.Max(1.0, vAxialMs + 0.5 * Math.Max(0.0, Math.Sqrt(Math.Max(0.0, vAxialMs * vAxialMs + 2.0 * thrust / Math.Max(1e-6, rhoKgM3 * disc))) - vAxialMs));
        double swirlC = angMom / Math.Max(1e-6, rhoKgM3 * disc * vDisc);
        return new Result(thrust, torque, power, eta, swirlC, normal, 0.0);
    }

    /// <summary>
    /// A propeller blade section: Clark-Y-like, which is what most of this fleet's blades are. Linear to the stall,
    /// then a flat-plate post-stall so a stalled root (which every propeller has at low speed) behaves instead of
    /// running away. Not a table because a propeller blade spends its life between 0 and 10 degrees.
    /// </summary>
    private static (double cl, double cd) Section(double alphaRad)
    {
        const double A0 = -2.0 * Math.PI / 180.0, Slope = 5.9, ClMax = 1.25;
        double a = alphaRad - A0;
        double clLin = Slope * a;
        double cl, cd;
        if (Math.Abs(clLin) <= ClMax)
        {
            cl = clLin;
            cd = 0.008 + 0.020 * cl * cl;
        }
        else
        {
            double sign = Math.Sign(clLin);
            double over = Math.Abs(a) - ClMax / Slope;
            cl = sign * Math.Max(0.6, ClMax - 0.7 * over);              // post-stall decay toward a plate
            cd = 0.030 + 1.1 * Math.Sin(Math.Min(Math.PI / 2, Math.Abs(over))) * Math.Sin(Math.Min(Math.PI / 2, Math.Abs(over)));
        }
        return (cl, cd);
    }
}
