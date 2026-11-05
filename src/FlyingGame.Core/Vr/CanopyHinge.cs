using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>How a canopy opens. Both aerobatic types hinge on the RIGHT sill; the Pitts first slides aft.</summary>
    public enum CanopyMotion
    {
        /// <summary>Extra 300: up and over to the right about a hinge line on the right sill.</summary>
        HingeRight = 0,
        /// <summary>Pitts S-2: slides AFT on its left mushroom-button track, then hinges up and over to the right.</summary>
        SlideThenHingeRight = 1,
    }

    /// <summary>
    /// One canopy's numbers. The bubble's size is measured off Codex's own models (lane/codex-vr-cockpit 45d2654, the
    /// `Canopy` subtree's bounds and the hinge-axis marker); the masses, the stop and the fittings are estimates (E).
    /// </summary>
    public readonly struct CanopySpec
    {
        public readonly CanopyMotion Motion;
        /// <summary>Canopy mass, glass and frame (E).</summary>
        public readonly double MassKg;
        /// <summary>The bubble: length along the flow, span rim to rim, height above the sill.</summary>
        public readonly double LengthM, SpanM, HeightM;
        /// <summary>The hinge line's distance to the RIGHT of the canopy's centre-line (on the right sill).</summary>
        public readonly double HingeOutboardM;
        public readonly double SlideM, StopDeg;
        /// <summary>The hinge fittings and open-stop together fail at this load at the stop lug (calibrated: see
        /// <see cref="CanopyDynamics"/>).</summary>
        public readonly double FittingStrengthN;
        /// <summary>A jettison control the pilot can pull (Pitts knob; Extra emergency release). The Bridge clears it
        /// when the model provides none.</summary>
        public readonly bool HasJettison;
        /// <summary>The Pitts: its frame, the stop tab on the right upper wing and the wing's skin, which together stop the
        /// swing (<see cref="CanopyStopTab"/>); <see cref="StopDeg"/> is then only the hinge's own limit, never reached
        /// while the tab catches it. Null (the Extra): the swing stops at <see cref="StopDeg"/>.</summary>
        public readonly CanopyStopTab? StopTab;

        public CanopySpec(CanopyMotion motion, double massKg, double lengthM, double spanM, double heightM, double hingeOutboardM,
                          double slideM, double stopDeg, double fittingStrengthN, bool hasJettison, CanopyStopTab? stopTab = null)
        {
            Motion = motion; MassKg = massKg; LengthM = lengthM; SpanM = spanM; HeightM = heightM; HingeOutboardM = hingeOutboardM;
            SlideM = slideM; StopDeg = stopDeg; FittingStrengthN = fittingStrengthN; HasJettison = hasJettison; StopTab = stopTab;
        }

        /// <summary>Hinge line to the free (left) edge, where a hand holds it.</summary>
        public double FreeEdgeM => HingeOutboardM + 0.5 * SpanM;

        public CanopySpec WithJettison(bool has) =>
            new(Motion, MassKg, LengthM, SpanM, HeightM, HingeOutboardM, SlideM, StopDeg, FittingStrengthN, has, StopTab);

        public CanopySpec WithStopTab(CanopyStopTab? tab) =>
            new(Motion, MassKg, LengthM, SpanM, HeightM, HingeOutboardM, SlideM, StopDeg, FittingStrengthN, HasJettison, tab);

        public CanopySpec WithFittingStrength(double n) =>
            new(Motion, MassKg, LengthM, SpanM, HeightM, HingeOutboardM, SlideM, StopDeg, n, HasJettison, StopTab);

        /// <summary>The whole track the canopy can run aft on with the jettison pulled (Pitts), else the slide.</summary>
        public double TrackM => SlideM + (StopTab?.JettisonExtraSlideM ?? 0.0);

        /// <summary>
        /// Extra 300 (one canopy over both seats): 2.12 m long, 0.78 m rim to rim, 0.48 m high, hinge line 0.343 m right
        /// of the centre-line (Codex's Canopy bounds and Canopy_HingeAxis); 14 kg (E); 100° stop (the rig, E); fittings
        /// 15,100 N (calibrated to let go at 80 KIAS); emergency release CanopyRelease_F/R (the model's).
        /// Pitts S-2B: 1.48 × 0.716 × 0.406 m, hinge 0.36 m right (Codex's Canopy origin); 9 kg (E); 4 in of aft slide
        /// (Codex's rig, E); the swing stopped by the frame landing on the stop tab (<see cref="CanopyStopTab"/>; the hinge's
        /// own limit 175°, E, is never reached while it does); fittings calibrated to 80 mph = 69.5 KIAS; the jettison.
        /// </summary>
        public static CanopySpec For(string aircraftId)
        {
            string id = aircraftId ?? "";
            if (id.StartsWith("pitts"))
                return new CanopySpec(CanopyMotion.SlideThenHingeRight, 9.0, 1.48, 0.716, 0.406, 0.36, 0.1016, 175.0, PittsFittingN, true,
                                      CanopyStopTab.Pitts());
            return new CanopySpec(CanopyMotion.HingeRight, 14.0, 2.12, 0.78, 0.48, 0.343, 0.0, 100.0, ExtraFittingN, true);
        }

        /// <summary>Fitting strengths, N, from <see cref="CanopyDynamics.PeakStopLoadN"/> at the failure speeds Ty gave
        /// (80 KIAS Extra; 80 mph = 69.5 KIAS Pitts), rounded to 100 N. Re-derive if any canopy number changes. The Pitts'
        /// is the peak HINGE reaction when the frame slams onto the stop tab (v2, 2026-11-05: 11,481 N); the Extra's the
        /// load at its stop lug.</summary>
        public const double ExtraFittingN = 15100.0, PittsFittingN = 11500.0;
    }

    /// <summary>
    /// THE BUBBLE IN THE AIRFLOW, from its shape. A canopy is modelled as the top half of an ellipsoid with semi-axes
    /// a = L/2 along the flow, b = W/2 across it and c = h up, sitting on a flat fuselage top. A flat wall in a flow
    /// along it is a plane of symmetry, so the attached potential flow over the half-ellipsoid is exactly half of the
    /// flow over the whole one, and that is a classical result (Lamb, Hydrodynamics §114): the velocity on the surface of
    /// an ellipsoid in a uniform stream U along a is uniform-in-form,
    ///
    ///   V_s = C · U · √(1 − n_a²),   C = 2 / (2 − α₀),   α₀ = a b c ∫₀^∞ dλ / ((a² + λ) √((a²+λ)(b²+λ)(c²+λ)))
    ///
    /// where n_a is the surface normal's component along the flow (a sphere has α₀ = 2/3, C = 1.5; a long body C → 1).
    /// So the outside pressure is Cp(n) = 1 − C²(1 − n_a²): stagnation at the nose and tail, suction everywhere the
    /// surface runs along the flow, most over the crown. The CURVATURE is all in C: a tall, short bubble (Pitts) has a
    /// bigger C and more suction than a long, flat one (Extra).
    ///
    /// The cockpit inside sits at free-stream static, Cp_in = 0 (assumption: the vents' ram air raises it a little and
    /// NACA RM L50B03 measured "slightly less than free stream" on a closed canopy; the two are taken to cancel). The net
    /// outward load is then
    ///
    ///   L = q · A_plan · ΔCp,   ΔCp = Cp_in − (1/A_plan) ∫ Cp dA_plan      (each surface element's vertical projection)
    ///
    /// and, the distribution being symmetric about the centre-line, it acts on the centre-line: lever = the hinge's
    /// distance outboard of it. Measured on Codex's models: Extra C = 1.160, ΔCp = 0.227; Pitts C = 1.219, ΔCp = 0.309.
    /// What it leaves out: the fuselage's own supervelocity (more suction), the windscreen's wake on the Pitts (less),
    /// and separation over the aft slope (real bubbles do not recover to Cp = 1 at the tail, so the true net suction
    /// is somewhat LARGER than this — the opening loads here are on the low side).
    ///
    /// The same surface, with the canopy's mass spread uniformly over it, gives its centre of mass (height y_c above the
    /// sill) and its moment of inertia about the hinge line.
    /// </summary>
    public sealed class CanopyBubble
    {
        public const double CpInside = 0.0;
        public readonly double Alpha0, SpeedRatio, PlanAreaM2, NetCp, ComHeightM, InertiaKgM2, FrontalAreaM2;

        public CanopyBubble(in CanopySpec s)
        {
            double a = 0.5 * s.LengthM, b = 0.5 * s.SpanM, c = s.HeightM;
            Alpha0 = LambAlpha(a, b, c);
            SpeedRatio = 2.0 / (2.0 - Alpha0);
            PlanAreaM2 = Math.PI * a * b;
            FrontalAreaM2 = 0.5 * Math.PI * b * c;

            const int n = 160;
            double dx = 2.0 * a / n, dz = 2.0 * b / n, dA = dx * dz, C2 = SpeedRatio * SpeedRatio;
            double area = 0.0, cpSum = 0.0, surf = 0.0, ySum = 0.0, r2Sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double x = -a + (i + 0.5) * dx;
                for (int k = 0; k < n; k++)
                {
                    double z = -b + (k + 0.5) * dz;
                    double r = 1.0 - x * x / (a * a) - z * z / (b * b);
                    if (r <= 0.0) continue;
                    double y = c * Math.Sqrt(r);
                    double gx = x / (a * a), gy = y / (c * c), gz = z / (b * b);
                    double g = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    double na = gx / g, ny = gy / g;
                    area += dA;
                    cpSum += (1.0 - C2 * (1.0 - na * na)) * dA;
                    // The surface element over this plan element: dS = dA / n_y (capped at the rim, where it runs vertical).
                    double dS = dA / Math.Max(ny, 0.05);
                    surf += dS;
                    ySum += y * dS;
                    double lat = z - s.HingeOutboardM;   // z + right; the hinge is HingeOutboardM to the right
                    r2Sum += (lat * lat + y * y) * dS;
                }
            }
            NetCp = CpInside - cpSum / area;
            ComHeightM = ySum / surf;
            InertiaKgM2 = s.MassKg * r2Sum / surf;
        }

        /// <summary>Lamb's α₀ for flow along the a axis, by the substitution λ = a² t / (1 − t) and the midpoint rule.</summary>
        public static double LambAlpha(double a, double b, double c)
        {
            const int n = 4000;
            double sum = 0.0, a2 = a * a, b2 = b * b, c2 = c * c;
            for (int i = 0; i < n; i++)
            {
                double t = (i + 0.5) / n;
                double lam = a2 * t / (1.0 - t);
                double dl = a2 / ((1.0 - t) * (1.0 - t)) / n;
                sum += dl / ((a2 + lam) * Math.Sqrt((a2 + lam) * (b2 + lam) * (c2 + lam)));
            }
            return a * b * c * sum;
        }
    }

    /// <summary>
    /// THE CANOPY ON ITS HINGE, IN THE AIR, AND WHEN IT LEAVES. Cockpit frame (+x right, +y up, +z nose). The canopy
    /// turns about a hinge line on the RIGHT sill, along the flow; β is its opening angle (0 shut, + = left edge up and
    /// over to the right). Unit vectors in the plane of rotation:
    ///
    ///   u(β) = (−cos β, sin β, 0)   hinge → inboard (toward the free edge)     n(β) = (sin β, cos β, 0)   "up" of the canopy
    ///
    /// with du/dβ = n and dn/dβ = −u. Its centre of mass is d = <see cref="CanopySpec.HingeOutboardM"/> inboard of the
    /// hinge and y_c up (<see cref="CanopyBubble"/>): r = d·u + y_c·n, so a force F there opens it by F·(d·n − y_c·u).
    ///
    ///   AIRLOAD   M_a = d · ΔCp(shape) · q · A_plan  (CanopyBubble: potential flow on the bubble, q = ½ρV², the sim's
    ///             TAS and density; the Bridge passes ½ρ₀·IAS², the same q). Normal to the canopy, so its lever is d.
    ///             Opened, the canopy stands in the free stream with its convex face still in suction and its concave
    ///             face near static (E), so ΔCp is held as it opens. The hinge axis is ALONG the flow, so the drag on
    ///             the open canopy has no moment about it: it loads the fittings and the aeroplane, not the swing.
    ///   GRAVITY   apparent (load factor included): M_g = m · g_app · (d·n − y_c·u); level and shut, −m g d.
    ///   HAND      at the free edge (lever d + W/2), up to <see cref="HandForceN"/> (E); beaten, it is torn out of it.
    ///   SHEDDING  open past <see cref="ExposedDeg"/>, the canopy sheds vortices at St = V f / h = 0.2 and its load
    ///             fluctuates ±<see cref="SheddingAmp"/> (E): what flaps it against its stop.
    ///   STOP      the open-stop and hinge fittings are a stiff one-sided spring at the stop angle, K =
    ///             <see cref="StopStiffnessNmPerRad"/> (E: a 1° give per 175 N·m of canopy frame, limiter and lug) with
    ///             ζ = <see cref="StopDampingRatio"/> (E, so it bounces back about a third of its speed). The stop lug is
    ///             <see cref="StopLeverM"/> from the hinge pins, so the FITTING LOAD is F = M_stop / e.
    ///
    ///   I β¨ = M_a + M_g + M_hand − c β˙ + M_stop,   I about the hinge line from the bubble's surface.
    ///
    /// HINGE FAILURE. Unlatched in flight the suction flings it open; it reaches the stop with E = ∫ (M_a + M_g) dβ of
    /// kinetic energy and the stop has to take that out in a few degrees: the peak stop moment is about
    /// M_s + √(M_s² + 2 K E) (M_s the static moment there). Ty's rule — the canopy departs above 80 KIAS (Extra) and
    /// 80 mph (Pitts) when flung open, and stays on below — fixes the fittings' strength: it is the peak load at the
    /// stop at exactly that speed (<see cref="PeakStopLoadN"/>, which runs THIS dynamics), rounded:
    /// Extra 15,100 N, Pitts 11,500 N (at its hinges, v2). A steady load at the stop (flying on faster with it open) breaks it too, at
    /// the speed where M_a + flutter alone reaches that.
    ///
    /// THE PITTS' SLIDE. It must slide 4 in aft before it can hinge (the mushroom buttons hold it in the track). By hand
    /// the slide follows the hand 1:1; unlatched in flight the canopy's own drag (q · Cd 0.07 · frontal area, E for a
    /// faired bubble) pushes it aft against the track's friction μ = 0.05 (E, nylon buttons in an aluminium track)
    /// times the net load on the buttons |lift − weight|. With these numbers it slides and flings at any flying speed
    /// — a question for Ty (does an unlatched S-2B canopy really run aft by itself?).
    ///
    /// THE PITTS' STOP (v2, Ty 2026-11-05). No stop angle: the frame lands on the tab on the right upper wing
    /// (<see cref="CanopyStopTab"/>), a one-sided spring-damper in the swing at the angle the geometry gives for the
    /// present slide (96.8° after the normal 4 in). Its load on the hinges is the reaction R = m(α t_c − ω² r_c) − F_air −
    /// m g − F_tab (the canopy as a body on its hinge line), and the fittings fail when R passes their strength, which is
    /// calibrated to Ty's 80 mph exactly as before. JETTISONED, the Pitts' pull frees the track: the canopy runs on aft
    /// (its drag, or a hand) up to <see cref="CanopySpec.TrackM"/>, against the carriage's friction and the rubber's
    /// while it presses the tab; past 4.75 in its front hoop no longer meets the tab, so nothing stops the swing, and past
    /// <see cref="CanopyStopTab.HookReleaseDeg"/> the hinges let go. Whether it gets there before it lands on the tab is a
    /// race between the slide and the swing, not a rule.
    ///
    /// JETTISON (Extra). <see cref="ReleaseHingePins"/> pulls the hinge pins. With the latch still LOCKED the canopy is held at
    /// the latch and stays on (its hinge side would lift by the latch's play; not drawn). Once the latch is open as well
    /// it is FREE: in the air (q above that of 20 kt) it departs at once at any speed; on the ground it sits loose
    /// until a hand lifts it 10°.
    ///
    /// DRAG into the aeroplane (<see cref="DragAreaM2"/>): an open cockpit, <see cref="OpenCockpitCdA"/>, ramping in
    /// over the first 30° of opening, plus the canopy standing in the stream, Cd 0.4 (E, a hollow half-shell edge-on)
    /// on its arch's frontal area π b h / 2, ramping in to 45°. Departed: the open cockpit only.
    /// </summary>
    public sealed class CanopyDynamics
    {
        public const double HandForceN = 200.0;
        public const double DampingNmsPerRad = 2.0;
        public const double StopStiffnessNmPerRad = 10000.0, StopDampingRatio = 0.3, StopLeverM = 0.10;
        public const double ExposedDeg = 20.0, Strouhal = 0.2, SheddingAmp = 0.15;
        public const double ShutCd = 0.07, TrackFriction = 0.05;
        public const double OpenShellCd = 0.4;
        /// <summary>Above this q a free (jettisoned and unlatched) canopy leaves by itself: ½ρ₀(20 kt)² = 64.8 Pa.</summary>
        public const double DepartMinQ = 64.8;
        /// <summary>Angle above which the pilot can climb out past it (E: the frame is clear of his shoulders).</summary>
        public const double ClearToLeaveDeg = 60.0;
        private const double Rho0 = 1.225, SubstepSec = 0.001;

        public readonly CanopySpec Spec;
        public readonly CanopyBubble Bubble;
        /// <summary>Metres slid aft (Pitts), 0 .. SlideM, and its speed.</summary>
        public double SlideM { get; private set; }
        public double SlideSpeedMs { get; private set; }
        public double AngleDeg { get; private set; }
        public double RateDps { get; private set; }
        public bool Held { get; private set; }
        /// <summary>The jettison has pulled the hinge pins (one-way).</summary>
        public bool HingePinsOut { get; private set; }
        public bool Departed { get; private set; }
        public CanopyDeparture DepartedBy { get; private set; }
        /// <summary>The largest load the stop lug has seen, N (this flight), and the last contact's.</summary>
        public double PeakFittingLoadN { get; private set; }
        public double FittingLoadN { get; private set; }

        private double _grabZ, _grabSlide, _grabAngleOff, _t;

        public CanopyDynamics(CanopySpec spec) { Spec = spec; Bubble = new CanopyBubble(spec); }

        public void Reset()
        {
            SlideM = 0.0; SlideSpeedMs = 0.0; AngleDeg = 0.0; RateDps = 0.0; Held = false;
            HingePinsOut = false; Departed = false; DepartedBy = CanopyDeparture.None;
            PeakFittingLoadN = 0.0; FittingLoadN = 0.0; _t = 0.0;
            TabForceN = 0.0; PeakTabForceN = 0.0;
        }

        public bool SlidComplete => Spec.Motion == CanopyMotion.HingeRight || SlideM >= Spec.SlideM - 1e-6;
        public bool Shut => !Departed && AngleDeg < 0.5 && SlideM < 1e-3;
        public bool ClearToLeave => Departed || AngleDeg >= ClearToLeaveDeg;

        /// <summary>The rig's `canopy_open` (CONTRACT / Codex's rigs): Extra 0..1 = 0..stop°. Pitts with its stop tab
        /// (v2): 0..1 = 0..the hinge's own limit (175°), the slide being `canopy_slide` (<see cref="Slide01"/>); a Pitts
        /// without one keeps the v1 map, 0..0.2 = the slide, 0.2..1 = 0..stop°.</summary>
        public double Open01 => Spec.Motion == CanopyMotion.HingeRight || Spec.StopTab != null
            ? Math.Clamp(AngleDeg / Spec.StopDeg, 0.0, 1.0)
            : (SlideM < Spec.SlideM - 1e-6 ? 0.2 * SlideM / Spec.SlideM : 0.2 + 0.8 * Math.Clamp(AngleDeg / Spec.StopDeg, 0.0, 1.0));

        /// <summary>The rig's `canopy_slide`: 0..1 = 0..the whole track (<see cref="CanopySpec.TrackM"/>: the normal 4 in
        /// plus what the jettison frees).</summary>
        public double Slide01 => Spec.TrackM > 1e-9 ? Math.Clamp(SlideM / Spec.TrackM, 0.0, 1.0) : 0.0;

        /// <summary>
        /// The rig params for a canopy NOT stepped by this class (iOS / Mac, no hands: CockpitRigState's 1.6 s open/shut
        /// command, 0..1). Extra: the command is the swing. Pitts v2: the first fifth slides it the normal 4 in, the rest
        /// swings it to where its frame comes down on the stop tab (the geometry's own answer, <see cref="CanopyStopTab"/>),
        /// so the scripted canopy rests where the physical one does.
        /// </summary>
        public static (double Slide01, double Open01) ScriptedRig(in CanopySpec s, double command01)
        {
            double c = Math.Clamp(command01, 0.0, 1.0);
            if (s.StopTab == null) return (0.0, c);
            double rest = s.StopTab.RestAngleDeg(s.SlideM);
            if (double.IsNaN(rest)) rest = s.StopDeg;
            double slide = Math.Min(1.0, c / 0.2) * s.SlideM, deg = Math.Max(0.0, (c - 0.2) / 0.8) * rest;
            return (slide / s.TrackM, deg / s.StopDeg);
        }

        /// <summary>How far the track lets it run aft now: the normal stop, or the whole track once jettisoned.</summary>
        public double SlideLimitM => HingePinsOut && Spec.StopTab != null ? Spec.TrackM : Spec.SlideM;

        /// <summary>The canopy's frame on the stop tab this step (resting or slamming into it): the contact force, N.</summary>
        public double TabForceN { get; private set; }
        public double PeakTabForceN { get; private set; }

        /// <summary>Open-cockpit drag area, m² (E): Extra 0.15 (two seats under one opening), Pitts 0.12.</summary>
        public double OpenCockpitCdA => Spec.Motion == CanopyMotion.HingeRight ? 0.15 : 0.12;

        /// <summary>Added drag area, m², into the flight model (Aircraft.ExtraDragAreaM2).</summary>
        public double DragAreaM2 => Departed
            ? OpenCockpitCdA
            : OpenCockpitCdA * Math.Min(1.0, AngleDeg / 30.0) + OpenShellCd * Bubble.FrontalAreaM2 * Math.Min(1.0, AngleDeg / 45.0);

        /// <summary>How open the cockpit is to the airflow, 0 sealed .. 1 no canopy at all (for the wind noise): an
        /// attached canopy shelters some of it even fully open.</summary>
        public double Exposure01 => Departed ? 1.0 : 0.8 * Math.Min(1.0, AngleDeg / 30.0);

        public static Vec3 Normal(double angleDeg)
        {
            double b = angleDeg * Math.PI / 180.0;
            return new Vec3(Math.Sin(b), Math.Cos(b), 0.0);
        }

        public static Vec3 Inboard(double angleDeg)
        {
            double b = angleDeg * Math.PI / 180.0;
            return new Vec3(-Math.Cos(b), Math.Sin(b), 0.0);
        }

        /// <summary>Where a point fixed to the canopy is now, given where it is with the canopy shut (cockpit frame):
        /// slid aft by <see cref="SlideM"/>, then turned by β about the hinge line through <paramref name="hinge"/>
        /// (the hinge's position with the canopy shut). The grips, the latch handles and the jettison knob all ride it.</summary>
        public Vec3 PointOnCanopy(Vec3 shutPoint, Vec3 hinge) => PointOnCanopy(shutPoint, hinge, SlideM, AngleDeg);

        public static Vec3 PointOnCanopy(Vec3 shutPoint, Vec3 hinge, double slideM, double angleDeg)
        {
            Vec3 r = shutPoint - hinge;
            double inboard = -r.X, up = r.Y;
            Vec3 p = Inboard(angleDeg) * inboard + Normal(angleDeg) * up;
            return new Vec3(hinge.X + p.X, hinge.Y + p.Y, shutPoint.Z - slideM);
        }

        /// <summary>The net outward load of the airflow, N.</summary>
        public double AeroNormalN(double q) => Bubble.NetCp * q * Bubble.PlanAreaM2;

        /// <summary>Opening moment of the airload, N·m (positive opens).</summary>
        public double AeroMomentNm(double q) => Spec.HingeOutboardM * AeroNormalN(q);

        /// <summary>Opening moment of (apparent) gravity, N·m; level 1 g and shut: −m g d.</summary>
        public double GravityMomentNm(Vec3 gApp, double angleDeg) =>
            Spec.MassKg * gApp.Dot(Normal(angleDeg) * Spec.HingeOutboardM - Inboard(angleDeg) * Bubble.ComHeightM);

        /// <summary>Dynamic pressure at which a shut, unlatched canopy lifts off against level 1 g and a hand holding
        /// <paramref name="holdN"/> at its free edge: q = (m g d + F (d + W/2)) / (d ΔCp A).</summary>
        public double FlipOpenQ(double holdN, double g = 9.80665) =>
            (Spec.MassKg * g * Spec.HingeOutboardM + holdN * Spec.FreeEdgeM) / AeroMomentNm(1.0);

        /// <summary>The canopy's own velocity relative to the aeroplane at its centre of mass, cockpit frame, m/s: the
        /// swing (and the Pitts' slide). What it leaves with, on top of the aeroplane's velocity there.</summary>
        public Vec3 ComVelocityLocal()
        {
            double w = RateDps * Math.PI / 180.0;
            Vec3 swing = (Normal(AngleDeg) * Spec.HingeOutboardM - Inboard(AngleDeg) * Bubble.ComHeightM) * w;
            return swing + new Vec3(0, 0, -SlideSpeedMs);
        }

        /// <summary>The centre of mass relative to the shut hinge position, cockpit frame.</summary>
        public Vec3 ComLocal(Vec3 hinge) =>
            PointOnCanopy(hinge + new Vec3(-Spec.HingeOutboardM, Bubble.ComHeightM, 0.0), hinge);

        /// <summary>The jettison pulls the hinge pins (one-way, until the aeroplane is reset).</summary>
        public void ReleaseHingePins() => HingePinsOut = true;

        /// <summary>The hand closes on the frame. <paramref name="hand"/>.Z = fore-aft (for the slide); the angle the
        /// hand points the free edge at is kept as an offset, so closing on it never jumps it.</summary>
        public void Grab(Vec3 hand, Vec3 hinge)
        {
            Held = true;
            _grabZ = hand.Z; _grabSlide = SlideM;
            _grabAngleOff = AngleDeg - HandAngle(hand, hinge);
        }

        public void Release() => Held = false;

        /// <summary>The angle the hand would hold the free edge at: atan2 of its height over the hinge and its distance
        /// inboard (left) of it.</summary>
        public static double HandAngle(Vec3 hand, Vec3 hinge) =>
            Math.Atan2(hand.Y - hinge.Y, hinge.X - hand.X) * 180.0 / Math.PI;

        /// <summary>
        /// One step. <paramref name="locked"/>: the latch holds it shut (Extra: pins engaged; Pitts: the ball home).
        /// <paramref name="hand"/> / <paramref name="hinge"/> are used while <see cref="Held"/> (the hinge where it is
        /// NOW, i.e. slid). Runs internally in 1 ms steps (the stop is stiff). Returns events for haptics and the log.
        /// </summary>
        public CanopyEvents Step(double dt, double q, Vec3 gApp, bool locked, Vec3 hand, Vec3 hinge)
        {
            CanopyEvents ev = CanopyEvents.None;
            if (dt <= 0.0 || Departed) return ev;
            if (locked && Shut) { RateDps = 0.0; SlideSpeedMs = 0.0; return ev; }

            CanopyStopTab? tab = Spec.StopTab;
            // Jettisoned and unlatched (Extra): nothing holds it. It goes in the air, or when a hand lifts it on the ground.
            // The Pitts' jettison instead frees the track; its hinges let go only once it has opened past the tab (below).
            if (tab == null && HingePinsOut && !locked && (q >= DepartMinQ || AngleDeg >= 10.0))
                return Depart(CanopyDeparture.Jettisoned);

            // The Pitts' slide, while it is down on its track.
            if (Spec.Motion == CanopyMotion.SlideThenHingeRight && AngleDeg < 0.5)
            {
                bool was = SlidComplete;
                if (Held && !locked)
                {
                    double s = Math.Clamp(_grabSlide + (_grabZ - hand.Z), 0.0, SlideLimitM);
                    SlideSpeedMs = (s - SlideM) / dt;
                    SlideM = s;
                }
                else if (!locked) SlideInTheAir(dt, q, gApp);
                if (!SlidComplete) { AngleDeg = 0.0; RateDps = 0.0; return ev; }
                // At the normal stop it stops; jettisoned it runs on aft as it starts to swing.
                if (!(tab != null && HingePinsOut) || Held) SlideSpeedMs = 0.0;
                // Just reached the end of the track: re-take the hinge offset so the swing starts where the hand is.
                if (!was && Held) _grabAngleOff = AngleDeg - HandAngle(hand, hinge);
            }

            double I = Bubble.InertiaKgM2;
            double cStop = 2.0 * StopDampingRatio * Math.Sqrt(StopStiffnessNmPerRad * I);
            double rate = RateDps * Math.PI / 180.0, angle = AngleDeg * Math.PI / 180.0, stop = Spec.StopDeg * Math.PI / 180.0;
            double vTrue = Math.Sqrt(2.0 * Math.Max(0.0, q) / Rho0);
            double shedHz = Strouhal * vTrue / Math.Max(0.1, Spec.HeightM);
            bool wasOpen = AngleDeg >= FlungOpenDeg, inContact = AngleDeg > Spec.StopDeg, onTab = TabForceN > 0.0;
            // The tab is a much stiffer contact than the hinge's stop: a quarter of the step.
            int n = Math.Max(1, (int)Math.Ceiling(dt / (tab != null ? TabSubstepSec : SubstepSec)));
            double h = dt / n;
            FittingLoadN = 0.0;
            for (int i = 0; i < n; i++)
            {
                _t += h;
                double deg = angle * 180.0 / Math.PI;
                double shed = deg > ExposedDeg ? 1.0 + SheddingAmp * Math.Sin(2.0 * Math.PI * shedHz * _t) : 1.0;
                double ma = AeroMomentNm(q) * shed;
                double m = ma + GravityMomentNm(gApp, deg);
                double mHand = 0.0;
                if (Held)
                {
                    // The hand is a stiff, bounded servo on the angle it points the edge at: critically damped at 12 rad/s
                    // (E, an arm's bandwidth), capped at what one arm can push or pull at the free edge.
                    double want = Math.Clamp(HandAngle(hand, hinge) + _grabAngleOff, 0.0, Spec.StopDeg) * Math.PI / 180.0;
                    const double wn = 12.0;
                    double need = I * (wn * wn * (want - angle) - 2.0 * wn * rate) - m;
                    double cap = HandForceN * Spec.FreeEdgeM;
                    if (Math.Abs(need) > cap)
                    {
                        Held = false;
                        ev |= CanopyEvents.TornFromHand;
                        mHand = Math.Sign(need) * cap;   // he was still pushing this step
                    }
                    else mHand = need;
                    m += mHand;
                }
                m -= DampingNmsPerRad * rate;

                // The frame on the tab (Pitts): the contact force, its moment about the hinge line, its push along the
                // track, and the load it puts through the hinges.
                Vec3 fc = Vec3.Zero;
                double nTab = 0.0;
                if (tab != null)
                {
                    CanopyStopTab.Touch t = tab.TouchAt(SlideM);
                    if (t.Hits && deg > t.Deg)
                    {
                        // Past the touch by (β − β_t): the frame is that far into the rubber and bar along its swing.
                        var hingeNow = new Vec3(0, 0, -SlideM);
                        Vec3 r = t.Point - hingeNow;
                        Vec3 tang = new Vec3(r.Y, -r.X, 0.0);   // d(point)/dβ
                        double arm = tang.Length;
                        double depth = (deg - t.Deg) * Math.PI / 180.0 * arm;
                        double meff = I / Math.Max(0.0025, arm * arm);
                        double f = CanopyStopTab.StiffnessNPerM * depth
                                   + 2.0 * CanopyStopTab.DampingRatio * Math.Sqrt(CanopyStopTab.StiffnessNPerM * meff) * rate * arm;
                        if (f > 0.0)
                        {
                            // Along the contact normal, whose moment about the hinge resists the swing (it is the swing's
                            // own direction, reversed, for a head-on touch).
                            Vec3 nc = t.Normal;
                            double back = -nc.Dot(tang) / arm;
                            if (back < 0.2) nc = -tang / arm;   // a grazing normal still has to stop it
                            Vec3 fv = nc * f;
                            fc += fv;
                            m += fv.X * r.Y - fv.Y * r.X;
                            nTab = f;
                        }
                    }
                    if (nTab > 0.0 && !onTab && rate * 180.0 / Math.PI > 20.0) ev |= CanopyEvents.HitStop;
                    onTab = nTab > 0.0;
                    if (nTab > PeakTabForceN) PeakTabForceN = nTab;
                }

                // The open-stop: a one-sided spring-damper, and the load it puts through the lug.
                double over = angle - stop;
                if (over > 0.0)
                {
                    double ms = Math.Max(0.0, StopStiffnessNmPerRad * over + cStop * rate);
                    m -= ms;
                    double load = ms / StopLeverM;
                    if (load > FittingLoadN) FittingLoadN = load;
                    if (load > PeakFittingLoadN) PeakFittingLoadN = load;
                    if (!inContact)
                    {
                        inContact = true;
                        if (rate * 180.0 / Math.PI > 20.0) ev |= CanopyEvents.HitStop;
                    }
                    if (load > Spec.FittingStrengthN)
                    {
                        AngleDeg = angle * 180.0 / Math.PI; RateDps = rate * 180.0 / Math.PI;
                        return ev | Depart(CanopyDeparture.HingesFailed);
                    }
                }
                else inContact = false;

                double alpha = m / I;
                // Down on the sill, pressed onto it: the sill takes it, not the hinges.
                bool onSill = angle <= 0.0 && alpha <= 0.0;
                if (onSill) alpha = 0.0;
                if (tab != null && !onSill)
                {
                    // The hinges' load: what the canopy's own acceleration needs beyond the forces on it (the airload on
                    // its centre-line, its weight, the contacts; the hand's share left out — it is on the frame, not the
                    // hinges): R = m (α t_c − ω² r_c) − F_air − m g − F_contact, in the plane of the swing.
                    Vec3 nrm = Normal(deg), rc = Inboard(deg) * Spec.HingeOutboardM + nrm * Bubble.ComHeightM;
                    double aNoHand = (m - mHand) / I;
                    Vec3 acc = new Vec3(rc.Y, -rc.X, 0.0) * aNoHand - rc * (rate * rate);
                    Vec3 rxn = acc * Spec.MassKg - nrm * (AeroNormalN(q) * shed) - new Vec3(gApp.X, gApp.Y, 0.0) * Spec.MassKg
                               - new Vec3(fc.X, fc.Y, 0.0);
                    double load = rxn.Length;
                    if (load > FittingLoadN) FittingLoadN = load;
                    if (load > PeakFittingLoadN) PeakFittingLoadN = load;
                    TabForceN = nTab;
                    if (load > Spec.FittingStrengthN)
                    {
                        AngleDeg = deg; RateDps = rate * 180.0 / Math.PI;
                        return ev | Depart(CanopyDeparture.HingesFailed);
                    }
                    // Jettisoned, it runs on aft along the track while it swings: its drag against the carriage's and the
                    // rubber's friction, and whatever the contacts push along it.
                    if (HingePinsOut && !Held) SlideOpen(h, q, gApp, deg, nrm, shed, fc.Z, nTab);
                }
                else if (tab != null) TabForceN = 0.0;

                rate += alpha * h;
                angle += rate * h;
                if (angle <= 0.0)
                {
                    angle = 0.0;
                    if (rate < 0.0) rate = 0.0;
                }
                // Opened past the tab with the jettison pulled: the hinge hooks come off their pins.
                if (tab != null && HingePinsOut && !locked && angle * 180.0 / Math.PI >= tab.HookReleaseDeg)
                {
                    AngleDeg = angle * 180.0 / Math.PI; RateDps = rate * 180.0 / Math.PI;
                    return ev | Depart(CanopyDeparture.Jettisoned);
                }
            }
            AngleDeg = angle * 180.0 / Math.PI;
            RateDps = rate * 180.0 / Math.PI;
            if (!wasOpen && AngleDeg >= FlungOpenDeg && !Held) ev |= CanopyEvents.FlippedOpen;
            return ev;
        }

        /// <summary>"Flung open": 90 % of the stop (Extra), over the top past vertical (Pitts, whose tab is further).</summary>
        private double FlungOpenDeg => Spec.StopTab != null ? 90.0 : 0.9 * Spec.StopDeg;

        private const double TabSubstepSec = 0.00025;

        /// <summary>The open, jettisoned Pitts canopy on its freed track, one substep: drag (shut bubble's plus the open
        /// shell's as it stands up) aft; the carriage's friction on the net of airload and weight, and the rubber's on what
        /// presses the tab; the contacts' own push along the track. It cannot run forward past the normal stop.</summary>
        private void SlideOpen(double h, double q, Vec3 gApp, double deg, Vec3 nrm, double shed, double contactFwdN, double contactN)
        {
            double drive = q * (ShutCd + OpenShellCd * Math.Min(1.0, deg / 45.0)) * Bubble.FrontalAreaM2 - contactFwdN;
            Vec3 press = nrm * (AeroNormalN(q) * shed) + new Vec3(gApp.X, gApp.Y, 0.0) * Spec.MassKg;
            double friction = TrackFriction * press.Length + CanopyStopTab.RubberFriction * contactN;
            double v = SlideSpeedMs, f;
            if (Math.Abs(v) > 1e-6) f = drive - friction * Math.Sign(v);
            else f = Math.Abs(drive) > friction ? drive - friction * Math.Sign(drive) : 0.0;
            double vn = v + f / Spec.MassKg * h;
            if (Math.Abs(v) > 1e-6 && Math.Sign(vn) != Math.Sign(v) && Math.Abs(drive) <= friction) vn = 0.0;   // friction stops it
            double s = SlideM + vn * h;
            if (s >= Spec.TrackM) { s = Spec.TrackM; vn = Math.Min(0.0, vn); }
            if (s <= Spec.SlideM) { s = Spec.SlideM; vn = Math.Max(0.0, vn); }
            SlideM = s;
            SlideSpeedMs = vn;
        }

        private CanopyEvents Depart(CanopyDeparture why)
        {
            Departed = true;
            DepartedBy = why;
            Held = false;
            return CanopyEvents.Departed;
        }

        /// <summary>The Pitts' canopy on its track in the airload: its drag pushes it aft against the friction of the
        /// buttons, which are pressed by the net of suction and weight.</summary>
        private void SlideInTheAir(double dt, double q, Vec3 gApp)
        {
            double drive = q * ShutCd * Bubble.FrontalAreaM2;
            double press = Math.Abs(AeroNormalN(q) + Spec.MassKg * gApp.Y);
            double friction = TrackFriction * press;
            double v = SlideSpeedMs;
            double f = v > 1e-6 ? drive - friction : drive > friction ? drive - friction : 0.0;
            v = Math.Max(0.0, v + f / Spec.MassKg * dt);
            double s = SlideM + v * dt;
            double end = SlideLimitM;
            // At the normal stop it stops; on a freed track it reaches the normal stop still moving and runs on.
            if (s >= end) { s = end; v = 0.0; }
            SlideM = s;
            SlideSpeedMs = v;
        }

        /// <summary>
        /// THE CALIBRATION. Fling a shut, unlatched canopy open at this q (level 1 g, no hand, fittings that never
        /// break) and return the largest load its stop lug sees, N. The fitting strengths in <see cref="CanopySpec"/>
        /// are this at Ty's failure speeds.
        /// </summary>
        public static double PeakStopLoadN(CanopySpec s, double q)
        {
            CanopySpec strong = s.WithFittingStrength(double.MaxValue);
            var c = new CanopyDynamics(strong);
            var g = new Vec3(0, -9.80665, 0);
            for (int i = 0; i < 600; i++) c.Step(0.005, q, g, false, Vec3.Zero, Vec3.Zero);   // 3 s: slide, fling, slam, settle
            return c.PeakFittingLoadN;
        }
    }

    public enum CanopyDeparture { None = 0, HingesFailed = 1, Jettisoned = 2 }

    [Flags]
    public enum CanopyEvents { None = 0, FlippedOpen = 1, HitStop = 2, TornFromHand = 4, Departed = 8 }

    /// <summary>
    /// THE WIND IN THE COCKPIT. The pressure fluctuations of a turbulent flow scale with its dynamic pressure, so the
    /// noise AMPLITUDE goes as q (+6 dB per doubling of q, +12 dB per doubling of speed); how much of it reaches the
    /// pilot depends on how open the cockpit is. Relative amplitude, 1 = an open cockpit at 100 KIAS:
    ///
    ///   A = (q / q₁₀₀) · (0.12 + 0.88 · exposure)     (0.12 = what a shut canopy lets through: E, ≈ −18 dB)
    /// </summary>
    public static class CockpitWind
    {
        /// <summary>½ρ₀ (100 kt)² = 1620.8 Pa.</summary>
        public const double Q100Kt = 1620.8, SealedLeak = 0.12;

        public static double Amplitude(double q, double exposure01) =>
            Math.Max(0.0, q) / Q100Kt * (SealedLeak + (1.0 - SealedLeak) * Math.Clamp(exposure01, 0.0, 1.0));
    }
}
