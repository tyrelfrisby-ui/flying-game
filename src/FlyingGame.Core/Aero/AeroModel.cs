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
        ControlDeflections controls,
        double wakeStalledFracOverride = -1.0,
        StripFlowState? flowState = null)
    {
        int stripIndex = 0;
        Vec3 cg = config.Mass.CgVec();
        Vec3 totalForce = Vec3.Zero;
        Vec3 totalMoment = Vec3.Zero;

        // wakeStalledFracOverride >= 0 supplies a LAGGED separation state from the caller (stall
        // hysteresis — separated wakes develop fast and wash out slowly). Negative = instantaneous.
        WingWake wake = ComputeWingWake(config, bodyVelocity, windBody, bodyRates, cg, wakeStalledFracOverride);

        // NACA TN-1045/1329 stab-wake rudder shielding: in steep/vertical flow the horizontal tail
        // sheds a wake wedge (60-deg line from its LE, 30-deg from its TE); fin/rudder area inside is
        // blanked. Precompute stab position/chord for the per-strip test on vertical surfaces.
        double stabX = 0, stabZ = 0, stabChord = 0; int stabN = 0;
        foreach (SurfaceConfig sf in config.Surfaces)
        {
            if (sf.Id.Contains("hStab", StringComparison.OrdinalIgnoreCase))
            {
                foreach (StripConfig st in sf.Strips) { stabX += st.PosVec().X; stabZ += st.PosVec().Z; stabChord += st.Chord; stabN++; }
            }
        }
        if (stabN > 0) { stabX /= stabN; stabZ /= stabN; stabChord /= stabN; }

        foreach (SurfaceConfig surface in config.Surfaces)
        {
            bool isWing = surface.Id.Contains("wing", StringComparison.OrdinalIgnoreCase);
            bool isVertical = surface.Id.Contains("vstab", StringComparison.OrdinalIgnoreCase)
                               || surface.Id.Contains("vertical", StringComparison.OrdinalIgnoreCase);

            double aspectRatio = GeometricAspectRatio(surface, isVertical);

            foreach (StripConfig strip in surface.Strips)
            {
                int idx = stripIndex++; // counted for EVERY strip, including low-speed skips
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

                    // Wing DOWNWASH at the tail: horizontal tail surfaces fly in reduced local alpha
                    // while the wing is attached (eps ~ 0.4*alpha_wing), and the downwash COLLAPSES
                    // as the wing separates — so the tail wakes up nose-down exactly when the spin
                    // pitches deep. This attached/stalled asymmetry is what lets a strong elevator
                    // trim slow flight without tumbling the spin.
                    if (!isWing && strip.PosVec().X < -1.0)
                    {
                        double eps = 0.4 * Math.Clamp(wake.FlowAlpha, -0.5, 0.5) * (1.0 - wake.StalledFraction);
                        alphaBase -= eps;
                    }

                    double dx = uu / planeSpeed, dz = ww / planeSpeed;
                    dragDir = new Vec3(dx, 0, dz);
                    liftDir = new Vec3(dz, 0, -dx);
                    momentAxis = new Vec3(0, 1, 0);
                }

                double controlDeflRad = strip.Control is null ? 0.0 : controls.GetDeflection(strip.Control.Surface);
                if (!airfoilTables.TryGetValue(strip.Airfoil, out AirfoilTable? table))
                {
                    throw new KeyNotFoundException($"Strip references unknown airfoil '{strip.Airfoil}'.");
                }

                // SPLIT-SURFACE MODEL (owner-directed): fixed surfaces (wing, stab, fin) and their
                // hinged controls (ailerons, elevator, rudder) are SEPARATE strip rows. A control
                // strip's incidence follows its deflection (gain ±1), it samples its own low-AR
                // table, and it carries its own separation memory — so "the stab stalls but the
                // elevator doesn't" is emergent physics, replacing the old effectiveness-fade and
                // flat-plate approximations that lived here.
                // Control response scales with cos(flow angle): full when flow is chordwise, zero at 90,
                // REVERSED in tail-first flow (a TE-down deflection acts LE-down when the flow comes
                // from behind). Without this the deep-gyration cycle gets wrong-signed control forces.
                // Reversal factor uses the CHORDWISE flow component vs total 3D speed: pure vertical
                // crossflow neutralizes controls (factor ~0) rather than reversing them — the planar
                // angle misreads flow-from-below as tail-first and commanded the rudder BACKWARD
                // during deep phases (found via spin direction-reversal hunt).
                double chordwiseFactor = Math.Clamp(2.0 * vLocal.X / Math.Max(vLocal.Length, MinSpeedMs), -1.0, 1.0);
                double controlDeltaAlpha = strip.Control is null ? 0.0 : strip.Control.Gain * controlDeflRad * chordwiseFactor;

                // AILERONS in separated flow (owner directive): a deflected aileron on a stalled wing
                // section stops commanding lift but KEEPS its pressure drag — so roll authority fades
                // with this strip's own separation memory while adverse yaw grows. Attached flight
                // keeps full roll power plus a modest deflection-drag increment (real adverse yaw).
                bool isAileronStrip = strip.Control is not null && strip.Control.Surface == "aileron";
                double cdDeflection = 0.0;
                if (isAileronStrip && Math.Abs(controlDeflRad) > 1e-9)
                {
                    double sep = flowState is not null && idx < flowState.Separation.Length ? flowState.Separation[idx] : 0.0;
                    controlDeltaAlpha *= 1.0 - 0.7 * sep;
                    double deltaGeom = strip.Control!.Gain * controlDeflRad;
                    cdDeflection = 1.2 * Math.Sin(deltaGeom) * Math.Sin(deltaGeom) * (1.0 + 2.0 * sep);
                }

                double alpha = alphaBase + strip.IncidenceRad + controlDeltaAlpha;

                AeroCoefficients coeffs = table.Sample(alpha);

                // Two-branch stall: blend the ATTACHED branch (linear lift carried past the static
                // stall — capped dynamic overshoot) with the SEPARATED branch (the table's post-stall
                // data) by this strip's own separation memory. Only meaningful in the transition band;
                // deep post-stall the strip is always separated and the table rules.
                if (flowState is not null && idx < flowState.Separation.Length)
                {
                    flowState.LocalAlphaRad[idx] = alpha;
                    double sep = flowState.Separation[idx];
                    double aClMax = table.AlphaClMaxRad;
                    if (sep < 1.0 && Math.Abs(alpha) > aClMax && Math.Abs(alpha) < aClMax + 20.0 * Math.PI / 180.0)
                    {
                        double sign = Math.Sign(alpha);
                        AeroCoefficients atStall = table.Sample(sign * aClMax);
                        // Attached branch: hold ~Clmax with a gentle continued rise, capped at 1.15x.
                        double clAttached = sign * Math.Min(Math.Abs(atStall.Cl) * 1.15,
                            Math.Abs(atStall.Cl) + 0.8 * (Math.Abs(alpha) - aClMax));
                        double cl = (1.0 - sep) * clAttached + sep * coeffs.Cl;
                        double cd = (1.0 - sep) * atStall.Cd + sep * coeffs.Cd;
                        double cm = (1.0 - sep) * atStall.Cm + sep * coeffs.Cm;
                        coeffs = new AeroCoefficients(cl, cd, cm);
                    }
                }

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

                // Stab-wake shielding of the fin/rudder (NACA spin-recovery geometry). Ramps in as
                // the tail-region flow steepens (none below 30-deg flow, full by 60-deg — the NACA
                // construction assumes near-vertical spin flow). A strip ABOVE the stab plane is
                // inside the wedge unless it lies aft of the 30-deg trailing-edge line; strips
                // BELOW the stab (the 2-33's low rudder) are never shielded.
                if (isVertical && stabN > 0)
                {
                    double flowSteep = Math.Clamp((Math.Abs(wake.FlowAlpha) - 30.0 * Math.PI / 180.0) / (30.0 * Math.PI / 180.0), 0.0, 1.0);
                    if (flowSteep > 0)
                    {
                        double up = stabZ - strip.PosVec().Z;                   // + when strip is above the stab (z down)
                        double aftOfTe = (stabX - 0.75 * stabChord) - strip.PosVec().X; // + when strip is aft of stab TE
                        bool inWedge = up > 0 && (aftOfTe < up / Math.Tan(30.0 * Math.PI / 180.0));
                        if (inWedge)
                        {
                            qFactor *= 1.0 - 0.85 * flowSteep;
                        }
                    }
                }

                double q = 0.5 * airDensity * planeSpeed * planeSpeed * qFactor;
                double lift = q * strip.Area * coeffs.Cl;
                double drag = q * strip.Area * (coeffs.Cd + cdInduced + cdDeflection);
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
        private readonly double _spreadRad;

        private readonly Vec3 _origin;        // area-weighted wing quarter-chord position
        private readonly double _flowAlpha;   // wake convection elevation (freestream alpha)
        private readonly double _stalledFrac; // stalled wing area / total wing area

        public WingWake(Vec3 origin, double flowAlpha, double stalledFrac, double spreadRad)
        {
            _origin = origin;
            _flowAlpha = flowAlpha;
            _stalledFrac = stalledFrac;
            _spreadRad = spreadRad;
        }

        public static double StallAlpha => StallAlphaRad;
        public double StalledFraction => _stalledFrac;
        public double FlowAlpha => _flowAlpha;

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
            // The separated wake thickens with stall depth: the lower boundary grows below the chord
            // plane, blanketing the stab (which sits just below the wing plane). Stable ONLY with the
            // hysteretic (lagged) stalled fraction supplied by Aircraft — with the instantaneous
            // fraction this band flickers and drives a relaxation limit cycle (owner goal: 200 ft/turn).
            double lo = Math.Min(0.0, _flowAlpha) - _spreadRad - 0.65 * Math.Abs(_flowAlpha) * _stalledFrac;
            double hi = Math.Max(0.0, _flowAlpha) + _spreadRad;

            // Smooth edge falloff over the spread margin.
            double inside;
            if (elevation <= lo || elevation >= hi)
            {
                inside = 0.0;
            }
            else
            {
                double edgeDist = Math.Min(elevation - lo, hi - elevation);
                inside = Math.Clamp(edgeDist / _spreadRad, 0.0, 1.0);
            }

            return 1.0 - maxLoss * _stalledFrac * inside;
        }
    }

    /// <summary>Instantaneous stalled fraction of wing area — callers integrate this with an
    /// asymmetric lag (fast separation, slow reattachment) to get the hysteretic wake state.</summary>
    public static double InstantStalledFraction(AircraftConfig config, Vec3 bodyVelocity, Vec3 bodyRates, Vec3 windBody)
    {
        WingWake w = ComputeWingWake(config, bodyVelocity, windBody, bodyRates, config.Mass.CgVec(), -1.0);
        return w.StalledFraction;
    }

    private static WingWake ComputeWingWake(AircraftConfig config, Vec3 bodyVelocity, Vec3 windBody, Vec3 bodyRates, Vec3 cg, double stalledFracOverride)
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
            return new WingWake(Vec3.Zero, 0.0, 0.0, config.StallDynamics.WakeSpreadDeg * Math.PI / 180.0);
        }

        double frac = stalledFracOverride >= 0.0 ? stalledFracOverride : stalledArea / totalArea;
        return new WingWake(new Vec3(sumX / totalArea, 0, sumZ / totalArea), flowAlpha, frac, config.StallDynamics.WakeSpreadDeg * Math.PI / 180.0);
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
