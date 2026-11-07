using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>Visual sampling only: two leading spray sheets per wet float, using the existing hull roots.
/// Does not feed forces, sound, wake forcing or the parcel field.</summary>
public static class FloatSprayVisual
{
    public const int MaxArms = 8; // FloatHydro supports four float slots, two chines each.
    public const int Samples = 12;
    public const double SheetLife = 0.28;

    // Presentation limits, not droplet diameters or a hydrodynamic model. Water.shader's
    // existing wake envelope grows by .354 m per metre aft. Stay inside that envelope.
    public const double VisibleLife = 0.65;
    public const double WakeInnerSlope = 0.30;
    public const double MaxFootprintM = 0.024;

    /// <summary>The existing FloatHandlingTests step criteria, applied per float.
    /// Ventilation alone is not enough: a nose-high float can ventilate while its heel drags.</summary>
    public const double HeelClearanceM = .005; // existing FloatHandlingTests contact tolerance

    public static bool IsOnStep(FloatHydro.FloatState f) => f.Wet &&
        f.VentilatedFraction >= .99 && f.SternDraftM < HeelClearanceM && f.PlaningLiftN > f.BuoyancyN;

    /// <summary>Continuous presentation weight from actual support, ventilation and heel release.
    /// No speed cutoff: deep stern contact or buoyancy-dominated support retains broad plow spray.
    /// This only blends the drawing; it changes no force or hydro classification.</summary>
    public static double StepFraction(FloatHydro.FloatState f)
    {
        if (!f.Wet || f.PlaningLiftN <= 0) return 0;
        double support = System.Math.Min(1, f.PlaningLiftN / System.Math.Max(1e-6, f.BuoyancyN));
        double release = 1 - System.Math.Clamp(System.Math.Max(0, f.SternDraftM) /
            HeelClearanceM, 0, 1);
        return System.Math.Clamp(f.VentilatedFraction, 0, 1) * support * release;
    }

    public static double CloudVisibility(Vec3 point, Vec3 forward, double beam,
        ReadOnlySpan<FloatHydro.FloatState> floats)
    {
        double norm = System.Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        if (norm < 1e-6) return 0;
        int nearest = -1; double distance = double.PositiveInfinity;
        for (int i = 0; i < floats.Length; i++)
        {
            if (!floats[i].Wet) continue;
            Vec3 d = point - floats[i].StepWorld;
            double side = System.Math.Abs(-d.X * forward.Y + d.Y * forward.X) / norm;
            if (side < distance) { distance = side; nearest = i; }
        }
        if (nearest < 0) return 0;
        var f = floats[nearest];
        return InsideWake(point, f.StepWorld, forward, beam, MaxFootprintM * .5) ? 1 : 1 - StepFraction(f);
    }

    public static bool InsideWake(Vec3 point, Vec3 step, Vec3 forward, double beam, double radius = 0)
    {
        double norm = System.Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        if (norm < 1e-6) return false;
        double x = forward.X / norm, y = forward.Y / norm;
        Vec3 d = point - step;
        double aft = -(d.X * x + d.Y * y);
        double side = System.Math.Abs(-d.X * y + d.Y * x);
        return aft >= 0 && aft <= 12 && side + radius <= beam * .5 + aft * WakeInnerSlope;
    }

    /// <summary>Keep the drawn contact sheet narrow and low. Only render geometry is bounded;
    /// the source jet, parcel trajectories and forces are never changed.</summary>
    public static Vec3 BoundSheet(Vec3 point, Vec3 contact, Vec3 forward)
    {
        double norm = System.Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        if (norm < 1e-6) return contact;
        Vec3 f = new Vec3(forward.X / norm, forward.Y / norm, 0), side = new Vec3(-f.Y, f.X, 0);
        Vec3 d = point - contact;
        double aft = System.Math.Max(0, -(d.X * f.X + d.Y * f.Y));
        double lateral = d.X * side.X + d.Y * side.Y;
        double width = .18 * aft;
        lateral = System.Math.Max(-width, System.Math.Min(width, lateral));
        double rise = System.Math.Min(.30, System.Math.Max(0, -d.Z));
        return contact - f * aft + side * lateral + new Vec3(0, 0, -rise);
    }

    /// <summary>Pick the foremost upward, outward jet on each chine, independently for each float.
    /// Returning root indices preserves the actual contact points; never average the two floats.</summary>
    public static void SelectRoots(ReadOnlySpan<FloatHydro.SprayRoot> roots, Quat attitude, int[] selected)
    {
        for (int i = 0; i < MaxArms; i++) selected[i] = -1;
        Quat inverse = attitude.Conjugate();
        for (int i = 0; i < roots.Length; i++)
        {
            var r = roots[i];
            if (r.Slot < 0 || r.Slot >= MaxArms / 2 || r.MassFlux <= 0 || r.JetVelocity.Z >= 0) continue;
            Vec3 jet = inverse.Rotate(r.JetVelocity);
            if (System.Math.Abs(jet.Y) < 0.05) continue;
            int arm = r.Slot * 2 + (jet.Y > 0 ? 1 : 0);
            int previous = selected[arm];
            if (previous < 0 || inverse.Rotate(r.Position - roots[previous].Position).X > 0)
                selected[arm] = i;
        }
    }

    /// <summary>A short steady sheet: drops emitted from the moving root at successive past times.
    /// Integrates the same droplet drag/gravity as SprayField, without changing any simulated parcel.</summary>
    public static void SampleSheet(FloatHydro.SprayRoot root, Vec3 aircraftWorldVelocity, Vec3 wind, Vec3[] points, double life = SheetLife)
    {
        points[0] = root.Position;
        Vec3 offset = Vec3.Zero, v = root.JetVelocity;
        double time = 0;
        const double dt = 1.0 / 240;
        for (int i = 1; i < Samples; i++)
        {
            double age = System.Math.Min(SheetLife, System.Math.Max(0, life)) * i / (Samples - 1);
            while (time < age)
            {
                double step = System.Math.Min(dt, age - time);
                v += SprayField.DropAcceleration(v, wind, 0.002) * step;
                offset += v * step;
                time += step;
            }
            points[i] = root.Position - aircraftWorldVelocity * age + offset;
        }
    }
}
