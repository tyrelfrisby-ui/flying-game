using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// A JAMMED EXIT UNDER WATER: why a cabin door will not open until the cabin is nearly full. Hydrostatics, nothing
    /// else (gauge pressures). Outside, at a height z: ρg·max(0, s_o − z). Inside: the air above the inside water at
    /// p_air, and below the inside water level s_i, p_air + ρg(s_i − z). The air leaves through the cabin's highest gap
    /// (E: the vents and seals — crash-water's HydroElement computes this flow properly), so it is at the pressure THERE:
    ///
    ///     cabin top above the outside surface:   p_air = 0                       (vented to the atmosphere)
    ///     cabin top under it:                    p_air = ρg(s_o − z_top)         (bubbling out at the top)
    ///
    /// So a cabin still riding with its roof out is held shut by the head difference s_o − s_i, and a cabin fully under
    /// by the AIR GAP above its water, z_top − s_i (not by the depth: below s_i the two sides balance). The net load,
    /// pressing the opening shut, is
    ///
    ///     F = ∫∫ [p_out(z) − p_in(z)] dA
    ///
    /// and what the pilot must beat is its MOMENT about the hinge, through his hand at the handle:
    ///
    ///     forward hinge (vertical axis):   M = (W²/2) ∫ Δp(z) dz          the load acts at half the width
    ///     top / bottom hinge:              M = W ∫ Δp(z) · (distance of z from the hinge) dz
    ///     canopy (a near-horizontal shell): F = A · Δp(z_canopy), M = F · (hinge → plan centroid)
    ///     push needed at the handle  = M / (hinge → handle)
    ///
    /// He can push <see cref="PushAvailableN"/> (400 N, E: two hands or a braced shoulder from the seat — twice the 200 N
    /// one-hand figure the canopy uses, CanopyDynamics.HandForceN). For a 172 door (0.90 × 1.00 m, forward hinge, handle at
    /// 0.85 of the width) fully under water on both sides, Δp is uniform and F = ρ g Δh A, M = F·W/2, so the door opens
    /// only once the levels differ by less than
    ///
    ///     Δh = P · 0.85 W / (ρ g A · W/2) = 400 · 0.85 / (1000 · 9.80665 · 0.90 · 0.5) = 0.0770 m
    ///
    /// — 8 cm: the cabin has to be all but full (fully under, the same 8 cm is the air gap left under its roof), which is what every ditching brief says (open the door BEFORE the water
    /// reaches it, or wait for the cabin to fill).
    ///
    /// THE CANOPY RELEASE IS NOT GATED (assumption, stated): the water presses the canopy DOWN onto its sill, so the latch
    /// pins and the jettison's hinge pins are unloaded in shear and slide out as easily as in air. What the water does
    /// gate is lifting the canopy away afterwards — the same moment rule as a door, on the canopy's plan area.
    /// </summary>
    public static class DoorWaterLoad
    {
        public const double PushAvailableN = 400.0;
        private const int Slices = 400;

        /// <summary>Net load pressing the opening shut (N, ≥ 0 when the outside is higher) and its moment about the hinge
        /// (N·m). Heights in m up: the opening's centre, the outside surface, the inside water level (NaN = dry inside),
        /// the cabin's top (its highest gap, where the air leaves; +∞ = always vented).</summary>
        public static (double ForceN, double MomentNm) Load(in ExitOpening o, double centreUp, double outsideUp, double insideUp, double rho,
                                                             double cabinTopUp = double.PositiveInfinity)
        {
            double g = 9.80665;
            double si = double.IsNaN(insideUp) ? double.NegativeInfinity : insideUp;
            double pAir = rho * g * Math.Max(0.0, outsideUp - cabinTopUp);
            double Dp(double z) => rho * g * Math.Max(0.0, outsideUp - z) - (pAir + rho * g * Math.Max(0.0, si - z));
            if (o.Hinge == HingeEdge.CanopySide)
            {
                double f = o.AreaM2 * Dp(centreUp);
                return (f, f * o.CanopyLoadArmM);
            }
            double zb = centreUp - 0.5 * o.HeightM, zt = centreUp + 0.5 * o.HeightM;
            double dz = o.HeightM / Slices, force = 0.0, moment = 0.0;
            for (int i = 0; i < Slices; i++)
            {
                double z = zb + (i + 0.5) * dz;
                double strip = Dp(z) * o.WidthM * dz;   // N on this horizontal strip
                force += strip;
                double arm = o.Hinge == HingeEdge.Forward ? 0.5 * o.WidthM
                           : o.Hinge == HingeEdge.Top ? zt - z
                           : z - zb;
                moment += strip * arm;
            }
            return (force, moment);
        }

        /// <summary>Hinge → handle, m.</summary>
        public static double HandleArmM(in ExitOpening o) => o.Hinge switch
        {
            HingeEdge.Forward => o.HandleArmFrac * o.WidthM,
            HingeEdge.CanopySide => o.HandleArmFrac * o.CanopyLoadArmM,
            _ => o.HandleArmFrac * o.HeightM,
        };

        /// <summary>The push the pilot needs at the handle, N (0 when the water is not holding it shut).</summary>
        public static double PushNeededN(in ExitOpening o, double centreUp, double outsideUp, double insideUp, double rho,
                                         double cabinTopUp = double.PositiveInfinity)
        {
            var (_, m) = Load(o, centreUp, outsideUp, insideUp, rho, cabinTopUp);
            double arm = HandleArmM(o);
            return m <= 0.0 || arm <= 1e-6 ? 0.0 : m / arm;
        }

        public static bool CanOpen(in ExitOpening o, double centreUp, double outsideUp, double insideUp, double rho,
                                   double cabinTopUp = double.PositiveInfinity) =>
            PushNeededN(o, centreUp, outsideUp, insideUp, rho, cabinTopUp) <= PushAvailableN;
    }
}
