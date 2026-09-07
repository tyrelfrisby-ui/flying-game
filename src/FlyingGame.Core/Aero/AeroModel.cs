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
    private const double MinInducedAspectRatio = 0.5; // below this, treat as a low-AR body: table drag only

    /// <summary>
    /// Geometric aspect ratio b²/S from the strip layout: span from strip-center extent plus half a
    /// strip width (area/chord) at each tip, area as the strip sum. Single-strip surfaces get AR from
    /// that strip alone (width²/area).
    /// </summary>
    /// <summary>1.0 while the local flow is attached (|alpha| &lt; 15°), fading linearly to 0.3 by 30° and holding there.</summary>
    private static double ControlEffectiveness(double localAlphaRad)
    {
        double a = Math.Abs(localAlphaRad);
        const double attached = 15.0 * Math.PI / 180.0, deep = 30.0 * Math.PI / 180.0;
        if (a <= attached) return 1.0;
        if (a >= deep) return 0.3;
        return 1.0 - 0.7 * (a - attached) / (deep - attached);
    }

    private static double GeometricAspectRatio(SurfaceConfig surface, bool isVertical)
    {
        double area = 0.0, min = double.MaxValue, max = double.MinValue;
        StripConfig? first = null, last = null;
        foreach (StripConfig strip in surface.Strips)
        {
            area += strip.Area;
            double spanCoord = isVertical ? strip.PosVec().Z : strip.PosVec().Y;
            if (spanCoord < min) { min = spanCoord; first = strip; }
            if (spanCoord > max) { max = spanCoord; last = strip; }
        }

        if (area <= 0.0 || first is null || last is null)
        {
            return 0.0;
        }

        double endWidths = (WidthOf(first) + WidthOf(last)) * 0.5;
        double span = (max - min) + endWidths;
        return span * span / area;

        static double WidthOf(StripConfig s) => s.Chord > 1e-9 ? s.Area / s.Chord : 0.0;
    }

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

        WingWake wake = ComputeWingWake(config, bodyVelocity, windBody, bodyRates, cg);

        foreach (SurfaceConfig surface in config.Surfaces)
        {
            bool isWing = surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            bool isVertical = surface.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                               || surface.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

            double aspectRatio = GeometricAspectRatio(surface, isVertical);

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
                    // Dihedral effect: a panel with dihedral Γ presents its surface to lateral flow,
                    // so sideslip shifts the strip's local AoA — upwind panel gains α, downwind loses
                    // it (Δα ≈ β·Γ). This is THE roll-from-sideslip mechanism (banks a spin into the
                    // rotation); it emerges per strip and keeps working post-stall via the tables.
                    double dihedral = strip.DihedralRad;
                    double sideSign = Math.Sign(strip.PosVec().Y);
                    double uu = vLocal.X;
                    double ww = vLocal.Z * Math.Cos(dihedral) + vLocal.Y * Math.Sin(dihedral) * sideSign;
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
                // Hinged surfaces lose grip in separated flow: full effectiveness while attached,
                // fading to ~30% once the strip is deep-stalled (they keep their drag — adverse yaw
                // survives — but stop commanding lift). Without this, held aileron overpowers a spin
                // it could never overpower in the real aircraft.
                double controlEffectiveness = ControlEffectiveness(alphaBase + strip.IncidenceRad);
                double controlDeltaAlpha = strip.Control is null ? 0.0 : strip.Control.Gain * controlDeflRad * controlEffectiveness;
                double alpha = alphaBase + strip.IncidenceRad + controlDeltaAlpha;

                if (!airfoilTables.TryGetValue(strip.Airfoil, out AirfoilTable? table))
                {
                    throw new KeyNotFoundException($"Strip references unknown airfoil '{strip.Airfoil}'.");
                }

                AeroCoefficients coeffs = table.Sample(alpha);

                // Drag polar: airfoil tables carry PROFILE drag only; induced drag is added here
                // per strip as Cl²/(π·AR·e) so it varies with local alpha (and therefore with
                // control deflection — this is where adverse yaw's drag asymmetry comes from).
                // The cos² taper retires the lifting-line term post-stall, where it is invalid
                // and the table's flat-plate drag takes over.
                double cdInduced = 0.0;
                if (aspectRatio > MinInducedAspectRatio)
                {
                    double cosAlpha = Math.Cos(alpha);
                    double attachedTaper = cosAlpha > 0.0 ? cosAlpha * cosAlpha : 0.0;
                    cdInduced = coeffs.Cl * coeffs.Cl / (Math.PI * aspectRatio * surface.OswaldE) * attachedTaper;
                }

                // Tail blanketing: strips of non-wing surfaces sitting inside the stalled wing's
                // separated wake lose dynamic pressure. In a spin this is what stops the tail from
                // producing near-CLmax upload and lets the nose ride high.
                double qFactor = isWing ? 1.0 : wake.DynamicPressureFactor(strip.PosVec(), config.WakeBlanketMaxLoss);

                double q = 0.5 * airDensity * planeSpeed * planeSpeed * qFactor;
                double lift = q * strip.Area * coeffs.Cl;
                double drag = q * strip.Area * (coeffs.Cd + cdInduced);
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

    /// <summary>
    /// Geometry of the stalled wing's separated wake (NACA spin-research style). The wake sheds from
    /// the wing and convects downstream with the flow: in body axes it occupies the angular band
    /// between the wing chord plane (elevation ~0) and the freestream direction (elevation ~alpha),
    /// growing with a spread margin. Strength scales with the stalled fraction of wing area.
    /// </summary>
    private readonly struct WingWake
    {
        private const double StallAlphaRad = 15.0 * Math.PI / 180.0;
        private const double SpreadRad = 6.0 * Math.PI / 180.0;

        private readonly Vec3 _origin;        // area-weighted wing quarter-chord position
        private readonly double _flowAlpha;   // wake convection elevation (freestream alpha)
        private readonly double _stalledFrac; // stalled wing area / total wing area

        public WingWake(Vec3 origin, double flowAlpha, double stalledFrac)
        {
            _origin = origin;
            _flowAlpha = flowAlpha;
            _stalledFrac = stalledFrac;
        }

        public static double StallAlpha => StallAlphaRad;

        /// <summary>1 = clean air; down to (1 - maxLoss) fully inside a fully-stalled wing's wake.</summary>
        public double DynamicPressureFactor(Vec3 stripPos, double maxLoss)
        {
            if (_stalledFrac <= 0.0 || maxLoss <= 0.0)
            {
                return 1.0;
            }

            double aft = _origin.X - stripPos.X;   // distance behind the wing (+x forward)
            if (aft < 0.1)
            {
                return 1.0;                        // ahead of / on the wing: no wake
            }

            // Elevation of the strip above the wing plane, seen from the wake origin (+z is down,
            // so "up" is -z). The wake band spans elevation [0, flowAlpha] +/- spread.
            double elevation = Math.Atan2(-(stripPos.Z - _origin.Z), aft);
            // NOTE (2026-09-06 experiment): growing the wake's LOWER boundary with stall depth
            // (blanketing the stab, which sits just below the wing plane) produced target-beating
            // rotation peaks (2.8 s/turn, 222 ft/turn) but a relaxation limit cycle — the spin
            // repeatedly fell out and rebuilt (net turns ~0.1). Likely needs stall HYSTERESIS
            // (separation at ~15 deg, reattachment lower) to damp the cycle before this returns.
            double lo = Math.Min(0.0, _flowAlpha) - SpreadRad;
            double hi = Math.Max(0.0, _flowAlpha) + SpreadRad;

            // Smooth edge falloff over the spread margin.
            double inside;
            if (elevation <= lo || elevation >= hi)
            {
                inside = 0.0;
            }
            else
            {
                double edgeDist = Math.Min(elevation - lo, hi - elevation);
                inside = Math.Clamp(edgeDist / SpreadRad, 0.0, 1.0);
            }

            return 1.0 - maxLoss * _stalledFrac * inside;
        }
    }

    private static WingWake ComputeWingWake(AircraftConfig config, Vec3 bodyVelocity, Vec3 windBody, Vec3 bodyRates, Vec3 cg)
    {
        Vec3 freestream = bodyVelocity - windBody;
        double flowAlpha = Math.Abs(freestream.X) > MinSpeedMs || Math.Abs(freestream.Z) > MinSpeedMs
            ? Math.Atan2(freestream.Z, freestream.X)
            : 0.0;

        double totalArea = 0.0, stalledArea = 0.0, sumX = 0.0, sumZ = 0.0;
        foreach (SurfaceConfig surface in config.Surfaces)
        {
            if (!surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (StripConfig strip in surface.Strips)
            {
                Vec3 pos = strip.PosVec();
                Vec3 vLocal = freestream + Vec3.Cross(bodyRates, pos - cg);
                double localAlpha = Math.Atan2(vLocal.Z, vLocal.X) + strip.IncidenceRad;
                totalArea += strip.Area;
                sumX += pos.X * strip.Area;
                sumZ += pos.Z * strip.Area;
                if (Math.Abs(localAlpha) > WingWake.StallAlpha)
                {
                    stalledArea += strip.Area;
                }
            }
        }

        if (totalArea <= 0.0)
        {
            return new WingWake(Vec3.Zero, 0.0, 0.0);
        }

        return new WingWake(new Vec3(sumX / totalArea, 0, sumZ / totalArea), flowAlpha, stalledArea / totalArea);
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

            // Slender-body crossflow drag (quadratic, acts at area centers so it makes MOMENTS):
            // plan-view normal force arrests spin flattening; side-view force weathervanes the nose
            // when the fin is stalled/blanketed at big beta.
            CrossflowConfig cf = config.Fuselage.Crossflow;
            if (cf.PlanArea > 0.0)
            {
                double w = bodyVelocity.Z;
                double fz = -0.5 * airDensity * cf.PlanArea * cf.Cd * w * Math.Abs(w);
                totalForce += new Vec3(0, 0, fz);
                totalMoment += Vec3.Cross(new Vec3(cf.PlanCenterX, 0, 0) - config.Mass.CgVec(), new Vec3(0, 0, fz));
            }

            if (cf.SideArea > 0.0)
            {
                double v = bodyVelocity.Y;
                double fy = -0.5 * airDensity * cf.SideArea * cf.Cd * v * Math.Abs(v);
                totalForce += new Vec3(0, fy, 0);
                totalMoment += Vec3.Cross(new Vec3(cf.SideCenterX, 0, 0) - config.Mass.CgVec(), new Vec3(0, fy, 0));
            }
        }

        totalMoment -= new Vec3(
            config.Fuselage.Damping.P * bodyRates.X,
            config.Fuselage.Damping.Q * bodyRates.Y,
            config.Fuselage.Damping.R * bodyRates.Z);
    }
}
