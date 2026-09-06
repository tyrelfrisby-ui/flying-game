using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Aero;

/// <summary>
/// Strip-theory aerodynamic model. Every lifting surface is a list of spanwise strips (from
/// AircraftConfig); each strip gets its own local velocity (body velocity + ω×r + wind), its own local
/// angle of attack (including any control-surface deflection via that strip's gain), and looks up
/// Cl/Cd/Cm from its airfoil's full ±180° table. Nothing about roll damping, adverse yaw, or stall/spin
/// behaviour is special-cased — it all falls out of this per-strip loop, per ARCHITECTURE.md.
/// </summary>
public static class AeroModel
{
    private const double MinSpeedMs = 1e-4;

    /// <summary>
    /// Computes total aerodynamic force and moment (body axes, about the CG) for the aircraft in its
    /// current state. Does NOT include gravity or propulsion — the caller (Aircraft/TrimSolver) adds those.
    /// </summary>
    public static (Vec3 Force, Vec3 Moment) Compute(
        AircraftConfig config,
        IReadOnlyDictionary<string, AirfoilTable> airfoilTables,
        Vec3 bodyVelocity,
        Vec3 bodyRates,
        Vec3 windBody,
        double airDensity,
        ControlDeflections controls)
    {
        Vec3 cg = config.Mass.CgVec();
        Vec3 totalForce = Vec3.Zero;
        Vec3 totalMoment = Vec3.Zero;

        foreach (SurfaceConfig surface in config.Surfaces)
        {
            bool isVertical = surface.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                               || surface.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

            foreach (StripConfig strip in surface.Strips)
            {
                Vec3 r = strip.PosVec() - cg;
                Vec3 vLocal = bodyVelocity - windBody + Vec3.Cross(bodyRates, r);

                double alphaBase;
                Vec3 dragDir, liftDir, momentAxis;
                double planeSpeed;

                if (isVertical)
                {
                    double uu = vLocal.X, vv = vLocal.Y;
                    planeSpeed = Math.Sqrt(uu * uu + vv * vv);
                    if (planeSpeed < MinSpeedMs)
                    {
                        continue;
                    }

                    alphaBase = Math.Atan2(vv, uu);
                    double dx = uu / planeSpeed, dy = vv / planeSpeed;
                    dragDir = new Vec3(dx, dy, 0);
                    liftDir = new Vec3(dy, -dx, 0);
                    momentAxis = new Vec3(0, 0, 1);
                }
                else
                {
                    double uu = vLocal.X, ww = vLocal.Z;
                    planeSpeed = Math.Sqrt(uu * uu + ww * ww);
                    if (planeSpeed < MinSpeedMs)
                    {
                        continue;
                    }

                    alphaBase = Math.Atan2(ww, uu);
                    double dx = uu / planeSpeed, dz = ww / planeSpeed;
                    dragDir = new Vec3(dx, 0, dz);
                    liftDir = new Vec3(dz, 0, -dx);
                    momentAxis = new Vec3(0, 1, 0);
                }

                double controlDeflRad = strip.Control is null ? 0.0 : controls.GetDeflection(strip.Control.Surface);
                double controlDeltaAlpha = strip.Control is null ? 0.0 : strip.Control.Gain * controlDeflRad;
                double alpha = alphaBase + strip.IncidenceRad + controlDeltaAlpha;

                if (!airfoilTables.TryGetValue(strip.Airfoil, out AirfoilTable? table))
                {
                    throw new KeyNotFoundException($"Strip references unknown airfoil '{strip.Airfoil}'.");
                }

                AeroCoefficients coeffs = table.Sample(alpha);
                double q = 0.5 * airDensity * planeSpeed * planeSpeed;
                double lift = q * strip.Area * coeffs.Cl;
                double drag = q * strip.Area * coeffs.Cd;
                double momentC4 = q * strip.Area * strip.Chord * coeffs.Cm;

                Vec3 force = liftDir * lift - dragDir * drag;
                Vec3 momentFromForce = Vec3.Cross(r, force);
                Vec3 momentAero = momentAxis * momentC4;

                totalForce += force;
                totalMoment += momentFromForce + momentAero;
            }
        }

        ApplySpoilerDrag(config, bodyVelocity, airDensity, controls, ref totalForce);
        ApplyFuselage(config, bodyVelocity, bodyRates, airDensity, ref totalForce, ref totalMoment);

        return (totalForce, totalMoment);
    }

    private static void ApplySpoilerDrag(AircraftConfig config, Vec3 bodyVelocity, double airDensity, ControlDeflections controls, ref Vec3 totalForce)
    {
        if (controls.SpoilerFraction <= 0.0)
        {
            return;
        }

        double speed = bodyVelocity.Length;
        if (speed < MinSpeedMs)
        {
            return;
        }

        double wingArea = 0.0;
        foreach (SurfaceConfig surface in config.Surfaces)
        {
            if (!surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (StripConfig strip in surface.Strips)
            {
                wingArea += strip.Area;
            }
        }

        const double deployedPlateCd = 1.6; // flat-plate-like speed brake, fully deployed
        double q = 0.5 * airDensity * speed * speed;
        double extraDrag = q * wingArea * deployedPlateCd * controls.SpoilerFraction;
        totalForce -= (bodyVelocity / speed) * extraDrag;
    }

    private static void ApplyFuselage(AircraftConfig config, Vec3 bodyVelocity, Vec3 bodyRates, double airDensity, ref Vec3 totalForce, ref Vec3 totalMoment)
    {
        double speed = bodyVelocity.Length;
        if (speed >= MinSpeedMs)
        {
            double q = 0.5 * airDensity * speed * speed;
            double drag = q * config.Fuselage.Cd0Area;
            totalForce -= (bodyVelocity / speed) * drag;

            // Crude linear side-force term: damps sideslip velocity, doesn't need to be exact.
            double sideForce = -0.5 * airDensity * speed * bodyVelocity.Y * config.Fuselage.SideForceArea;
            totalForce += new Vec3(0, sideForce, 0);
        }

        totalMoment -= new Vec3(
            config.Fuselage.Damping.P * bodyRates.X,
            config.Fuselage.Damping.Q * bodyRates.Y,
            config.Fuselage.Damping.R * bodyRates.Z);
    }
}
