using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// What the pilot wears in the water: a mass, its solid volume, and the air it traps (free-air volume at one
    /// atmosphere, leaking out with a time constant). Each line is an estimate (E) unless it names a source.
    /// </summary>
    public readonly struct PilotKit
    {
        public readonly string Name;
        public readonly double MassKg, SolidM3, AirM3, AirLeakTauSec;
        /// <summary>Worn on the feet (in the water once the soles are) rather than spread over the body.</summary>
        public readonly bool OnFeet;
        public PilotKit(string name, double massKg, double densityKgM3, double airM3, double airTauSec, bool onFeet = false)
        { Name = name; MassKg = massKg; SolidM3 = massKg / densityKgM3; AirM3 = airM3; AirLeakTauSec = airTauSec; OnFeet = onFeet; }

        /// <summary>A light Nomex flight suit: 1.0 kg of aramid (ρ 1380), and "a little air" trapped in it, 2.0 L, leaking
        /// out of collar and cuffs with τ 90 s (E).</summary>
        public static readonly PilotKit FlightSuit = new("flight suit", 1.0, 1380.0, 2.0e-3, 90.0);
        /// <summary>Boots, the pair: 1.6 kg, effective density 1150 (leather and rubber; E).</summary>
        public static readonly PilotKit Boots = new("boots", 1.6, 1150.0, 0.0, 1.0, onFeet: true);
        /// <summary>Emergency parachute after it has opened: the harness and the empty container, 3.0 kg, effective density
        /// 1400 (nylon webbing 1140 and its steel hardware; E). The canopy is out on its risers (CanopyOnWater).</summary>
        public static readonly PilotKit ParachuteOpened = new("parachute harness + empty container", 3.0, 1400.0, 0.0, 1.0);
        /// <summary>Emergency parachute still packed: 7.0 kg (the Para-Cushion's 14½–15¾ lb) at an effective solid density
        /// of 1250 (nylon canopy and webbing, steel), and 6 L of air in the pack, leaking with τ 120 s as it soaks (E).</summary>
        public static readonly PilotKit ParachutePacked = new("parachute (packed)", 7.0, 1250.0, 6.0e-3, 120.0);
    }

    /// <summary>
    /// THE PILOT'S BODY IN THE WATER: a 50th-percentile man as a stack of segment volumes, upright. One body, Archimedes
    /// plus drag — pilot physics, not a second water model (the water itself, its surface and density, are the seam's).
    ///
    ///   mass        85 kg (E, inside Ty's 80–90 kg)
    ///   tissue      density 1053 kg/m³ = Siri's 495/(%fat + 450) at 20 % body fat (E): the body's density with the lung
    ///               gas taken out, which is what hydrostatic weighing measures. Tissue volume 85 / 1053 = 80.7 L.
    ///   segments    de Leva's mass fractions (uniform density): head + neck 6.94 %, trunk 43.46 %, arms 9.88 %, legs 39.72 %.
    ///               Heights from the eye (ANSUR II 50th man standing; E for the head's split): top of head +0.112, chin
    ///               −0.115, suprasternal notch −0.20, crotch −0.80, soles −1.641. The head (73 % of head + neck) is an
    ///               ellipsoid top → chin; the neck, trunk and legs uniform in their bands; the arms, sculling under water
    ///               while treading, spread over −0.25 … −0.60.
    ///   lungs       gas in the chest band −0.22 … −0.50, compressed by depth (Boyle, at the band's centre): a held breath
    ///               5.0 L (E: between FRC 3.0 and TLC 6.5 L — a deep breath before the water closes over), treading 4.0 L
    ///               (E: breathing high in the vital capacity, as people treading water do).
    ///   kit         PilotKit (suit, boots, harness or packed parachute), its trapped air in the chest band too.
    /// </summary>
    public static class SwimmerBody
    {
        public const double TissueKg = 85.0, FatPercent = 20.0;
        public static readonly double TissueDensity = 495.0 / (FatPercent + 450.0) * 1000.0;
        public static readonly double TissueM3 = TissueKg / TissueDensity;
        public const double FracHeadNeck = 0.0694, FracTrunk = 0.4346, FracArms = 0.0988, FracLegs = 0.3972, HeadOfHeadNeck = 0.73;
        public const double TopM = 0.112, ChinM = -0.115, NotchM = -0.20, CrotchM = -0.80, SolesM = -ClimbPath.StandingEyeM;
        public const double ArmsTopM = -0.25, ArmsBottomM = -0.60, ChestTopM = -0.22, ChestBottomM = -0.50;
        public const double MouthM = -0.07;
        public const double HeldBreathM3 = 5.0e-3, BreathingLungsM3 = 4.0e-3;

        /// <summary>Tissue volume ABOVE a level, the level given relative to the eye (m, up +).</summary>
        public static double TissueAboveM3(double level)
        {
            double v = 0.0, hn = TissueM3 * FracHeadNeck;
            // Head: an ellipsoid along the vertical, top → chin; the fraction of it above u (−1..1) is (1 − u)²(2 + u)/4.
            if (level < TopM)
            {
                if (level <= ChinM) v += hn * HeadOfHeadNeck;
                else { double c = 0.5 * (TopM + ChinM), a = 0.5 * (TopM - ChinM), u = (level - c) / a; v += hn * HeadOfHeadNeck * (1 - u) * (1 - u) * (2 + u) / 4.0; }
            }
            v += Band(level, ChinM, NotchM, hn * (1.0 - HeadOfHeadNeck));
            v += Band(level, NotchM, CrotchM, TissueM3 * FracTrunk);
            v += Band(level, ArmsTopM, ArmsBottomM, TissueM3 * FracArms);
            v += Band(level, CrotchM, SolesM, TissueM3 * FracLegs);
            return v;
        }

        /// <summary>Fraction of a uniform band (top..bottom, relative to the eye) that lies above a level.</summary>
        public static double BandFractionAbove(double level, double top, double bottom) =>
            level >= top ? 0.0 : level <= bottom ? 1.0 : (top - level) / (top - bottom);

        private static double Band(double level, double top, double bottom, double vol) => vol * BandFractionAbove(level, top, bottom);

        /// <summary>A gas volume (free air at the surface, or a breath taken there) at a depth below the surface: Boyle.</summary>
        public static double AtDepth(double v0, double depthM, double rho = FloatHydro.WaterDensity) =>
            v0 * 101325.0 / (101325.0 + rho * 9.80665 * Math.Max(0.0, depthM));
    }

    public enum SwimPhase { Plunge = 0, SwimUp = 1, Tread = 2 }

    /// <summary>
    /// THE PILOT IN THE WATER, first person: plunge, swim up, tread water. Vertical dynamics of one body (y up):
    ///
    ///   (m + m_a) dv/dt = ρ g V_sub(eye − surface) − m g + F_swim − ½ ρ CdA |v| v − F_canopy
    ///
    ///   m_a          added mass, 10 % of m (E: a slender body moving along its axis)
    ///   CdA          0.12 m² upright, along the body (E: clothed; a streamlined swimmer's passive drag is ~0.07 m²)
    ///                0.40 m² with the limbs spread to brake a plunge (E: starfish)
    ///   F_swim       plunge: nothing for the first <see cref="ReactionSec"/> after the soles touch (0.4 s, E), then the
    ///                limbs spread and he strokes for the surface; SwimUp: <see cref="StrokeN"/> = 50 N up (E: a clothed
    ///                breast-stroke / flutter, about the drag of a swimmer at 1 m/s)
    ///                tread: <see cref="TreadN"/> = 60 N (E: a sustainable recreational tread; a water-polo eggbeater holds
    ///                more) pulsing ±30 % at the kick's 0.8 Hz (E) — the gentle bob — minus c·v, the sculling limbs'
    ///                damping: c = ½ ρ S CLα u = ½·1000·0.15 m²·2 /rad·1.5 m/s = 225 N·s/m (E: hands, feet and forearms
    ///                0.15 m², a low-aspect paddle's lift slope 2 /rad, the sweep 1.5 m/s)
    ///   F_canopy     a soaked canopy hanging from the risers (CanopyOnWater.LoadOnPilotN)
    ///
    /// Horizontal: the entry's drift dies on a broadside drag area of 0.6 m² (E) toward the water's current; on the
    /// surface the stick swims him at up to <see cref="SurfaceSwimMs"/> (0.6 m/s, E). Breath: held under water, breathing
    /// once the mouth is clear. Integrated internally in 2 ms steps.
    /// </summary>
    public sealed class PilotSwim
    {
        public const double AddedMassFrac = 0.10, CdAUpright = 0.12, CdASpread = 0.40, CdABroadside = 0.60;
        public const double ReactionSec = 0.4, StrokeN = 50.0, TreadN = 60.0, TreadPulse = 0.30, TreadHz = 0.8, TreadDampNsM = 225.0;
        public const double SurfaceSwimMs = 0.6, SurfaceSwimAccel = 0.8, SurfacedM = -0.05;
        private const double SubstepSec = 0.002;

        private readonly IPilotWater _water;
        private readonly PilotKit[] _kit;
        private readonly double[] _kitAir;
        private Vec3 _vh;

        public Vec3 Eye { get; private set; }
        public double Vy { get; private set; }
        public SwimPhase Phase { get; private set; }
        public double TimeSec { get; private set; }
        public double LungsM3 { get; private set; }
        /// <summary>The deepest the eye has been below the surface, m (positive = below).</summary>
        public double DeepestEyeM { get; private set; }
        /// <summary>Time from the start to the eye reaching the surface (SwimUp → Tread), s; NaN until it has.</summary>
        public double SurfacedSec { get; private set; } = double.NaN;
        public double CanopyLoadN;
        public double SurfaceUp { get; private set; }
        public double EyeAboveSurfaceM => Eye.Y - SurfaceUp;
        public double MassKg { get; }
        private double _wetT = double.NaN;

        public PilotSwim(IPilotWater water, Vec3 eye, Vec3 velocity, SwimPhase phase, params PilotKit[] kit)
        {
            _water = water ?? LivePilotWater.Instance;
            _kit = kit ?? Array.Empty<PilotKit>();
            _kitAir = new double[_kit.Length];
            double m = SwimmerBody.TissueKg;
            for (int i = 0; i < _kit.Length; i++) { m += _kit[i].MassKg; _kitAir[i] = _kit[i].AirM3; }
            MassKg = m;
            Eye = eye; Vy = velocity.Y; _vh = new Vec3(velocity.X, 0, velocity.Z);
            Phase = phase;
            LungsM3 = SwimmerBody.HeldBreathM3;
            SurfaceUp = _water.SurfaceUpAt(eye.X, eye.Z, eye.Y) ?? eye.Y;
            DeepestEyeM = Math.Max(0.0, SurfaceUp - eye.Y);
            if (phase != SwimPhase.Plunge) _wetT = 0.0;
        }

        public double TrappedAirM3 { get { double s = 0; foreach (double a in _kitAir) s += a; return s; } }

        /// <summary>The kit for an exit: the suit and boots always; the parachute packed (still in the cockpit) or opened.</summary>
        public static PilotKit[] KitFor(bool parachute, bool opened) =>
            !parachute ? new[] { PilotKit.FlightSuit, PilotKit.Boots }
                       : new[] { PilotKit.FlightSuit, PilotKit.Boots, opened ? PilotKit.ParachuteOpened : PilotKit.ParachutePacked };

        /// <summary>Net upward force (N) with the eye <paramref name="eyeAbove"/> above the surface (negative = below),
        /// before any swimming and drag: buoyancy − weight − canopy.</summary>
        public double NetBuoyancyN(double eyeAbove)
        {
            double rho = _water.Density, g = 9.80665;
            double level = -eyeAbove;                      // the waterline, relative to the eye
            if (level <= SwimmerBody.SolesM) return -(MassKg * g + CanopyLoadN);
            double tissueSub = SwimmerBody.TissueM3 - SwimmerBody.TissueAboveM3(level);
            double subFrac = tissueSub / SwimmerBody.TissueM3;
            double chestDepth = Math.Max(0.0, -(eyeAbove + 0.5 * (SwimmerBody.ChestTopM + SwimmerBody.ChestBottomM)));
            double chestUnder = 1.0 - SwimmerBody.BandFractionAbove(level, SwimmerBody.ChestTopM, SwimmerBody.ChestBottomM);
            double gas = SwimmerBody.AtDepth(LungsM3 + TrappedAirM3, chestDepth, rho) * chestUnder;
            double solids = 0.0;
            foreach (PilotKit k in _kit) solids += k.OnFeet ? k.SolidM3 : k.SolidM3 * subFrac;
            return rho * g * (tissueSub + gas + solids) - MassKg * g - CanopyLoadN;
        }

        /// <summary>The eye height above the surface where the body floats still (net force 0) with a steady upward
        /// push <paramref name="liftN"/> (the tread, or 0): the highest level at which the net force changes sign going
        /// down, which is the stable one (deeper, Boyle squeezes the gas and the body only gets heavier). NaN if he is
        /// heavier than the water at every level down to 2 m.</summary>
        public double EquilibriumEyeAboveM(double liftN)
        {
            double hi = 0.5;
            if (NetBuoyancyN(hi) + liftN >= 0.0) return hi;
            for (double lo = hi - 0.01; lo >= -2.0; lo -= 0.01)
            {
                if (NetBuoyancyN(lo) + liftN < 0.0) { hi = lo; continue; }
                for (int i = 0; i < 60; i++) { double m = 0.5 * (lo + hi); if (NetBuoyancyN(m) + liftN > 0.0) lo = m; else hi = m; }
                return lo;
            }
            return double.NaN;
        }

        /// <summary>One frame. The stick swims him along the surface (head-relative, as on foot) once he is treading.</summary>
        public void Step(double dt, double stickX = 0.0, double stickY = 0.0, double headYawDeg = 0.0)
        {
            int n = Math.Max(1, (int)Math.Ceiling(dt / SubstepSec));
            double h = dt / n;
            for (int i = 0; i < n; i++) Sub(h, stickX, stickY, headYawDeg);
        }

        private void Sub(double h, double sx, double sy, double headYawDeg)
        {
            double rho = _water.Density, g = 9.80665;
            TimeSec += h;
            SurfaceUp = _water.SurfaceUpAt(Eye.X, Eye.Z, Eye.Y) ?? SurfaceUp;
            double above = Eye.Y - SurfaceUp;
            bool wet = above < ClimbPath.StandingEyeM;
            if (wet && double.IsNaN(_wetT)) _wetT = 0.0;
            if (!double.IsNaN(_wetT)) _wetT += h;
            for (int k = 0; k < _kit.Length; k++)
                if (wet && _kitAir[k] > 0.0) _kitAir[k] *= Math.Exp(-h / Math.Max(1.0, _kit[k].AirLeakTauSec));

            // Phase changes.
            if (Phase == SwimPhase.Plunge && !double.IsNaN(_wetT) && _wetT > ReactionSec && Vy >= 0.0)
                Phase = above < SurfacedM ? SwimPhase.SwimUp : SwimPhase.Tread;
            if (Phase == SwimPhase.SwimUp && above >= SurfacedM) { Phase = SwimPhase.Tread; if (double.IsNaN(SurfacedSec)) SurfacedSec = TimeSec; }
            if (Phase == SwimPhase.Plunge && above >= SurfacedM && Vy >= 0.0 && !double.IsNaN(_wetT) && _wetT > ReactionSec) Phase = SwimPhase.Tread;
            // Breathing once the mouth is out; holding it under.
            LungsM3 = above + SwimmerBody.MouthM > 0.0 && Phase == SwimPhase.Tread ? SwimmerBody.BreathingLungsM3 : LungsM3;

            double f = wet ? NetBuoyancyN(above) : -MassKg * g;
            double cda = CdAUpright;
            switch (Phase)
            {
                case SwimPhase.Plunge:
                    if (!double.IsNaN(_wetT) && _wetT > ReactionSec) { cda = CdASpread; f += StrokeN; }
                    break;
                case SwimPhase.SwimUp: f += StrokeN; break;
                case SwimPhase.Tread:
                    f += TreadN * (1.0 + TreadPulse * Math.Sin(2.0 * Math.PI * TreadHz * TimeSec)) - TreadDampNsM * Vy;
                    break;
            }
            if (wet) f -= 0.5 * rho * cda * Math.Abs(Vy) * Vy;
            double mEff = MassKg * (1.0 + (wet ? AddedMassFrac : 0.0));
            Vy += f / mEff * h;

            // Horizontal: drift dies toward the current; on the surface the stick swims.
            Vec3 cur = _water.CurrentAt(Eye.X, Eye.Z, Eye.Y);
            Vec3 rel = _vh - new Vec3(cur.X, 0, cur.Z);
            double s = rel.Length;
            if (wet && s > 1e-6) { double k = 0.5 * rho * CdABroadside / mEff; rel = rel / (1.0 + k * s * h); }
            _vh = rel + new Vec3(cur.X, 0, cur.Z);
            if (Phase == SwimPhase.Tread)
            {
                double mag = Math.Sqrt(sx * sx + sy * sy);
                if (mag > GroundWalker.DeadZone)
                {
                    double a = headYawDeg * Math.PI / 180.0;
                    var fwd = new Vec3(Math.Sin(a), 0, Math.Cos(a));
                    var right = new Vec3(Math.Cos(a), 0, -Math.Sin(a));
                    Vec3 want = (fwd * sy + right * sx) * (Math.Min(1.0, mag) * SurfaceSwimMs / mag) + new Vec3(cur.X, 0, cur.Z);
                    Vec3 dv = want - _vh;
                    double dvMax = SurfaceSwimAccel * h;
                    if (dv.Length > dvMax) dv = dv.Normalized() * dvMax;
                    _vh += dv;
                }
            }
            Eye += new Vec3(_vh.X * h, Vy * h, _vh.Z * h);
            // The bed: he cannot go through it (he would stand up there; the Bridge hands him to the walker).
            double bed = _water.GroundUpAt(Eye.X, Eye.Z) + ClimbPath.StandingEyeM * 0.25;
            if (Eye.Y < bed) { Eye = new Vec3(Eye.X, bed, Eye.Z); if (Vy < 0) Vy = 0; }
            DeepestEyeM = Math.Max(DeepestEyeM, SurfaceUp - Eye.Y);
        }

        /// <summary>The bed is near enough to stand on with the head clear: water no higher than mid-chest (the eye 0.36 m
        /// above it, E) — the Bridge hands him to the walker.</summary>
        public bool CanStand
        {
            get
            {
                double bed = _water.GroundUpAt(Eye.X, Eye.Z);
                return Phase == SwimPhase.Tread && bed + ClimbPath.StandingEyeM - SurfaceUp >= 0.36;
            }
        }
    }

    /// <summary>
    /// THE PARACHUTE ON THE WATER after he lands in it (Ty, 2026-11-02 14:30). Once his weight is off it the canopy loses
    /// its inflation, the skirt falls and the fabric comes down over the water DOWNWIND of him — no longer carrying load,
    /// it moves with the air — lines slack; it floats, then sinks slowly as it soaks.
    ///
    ///   falls      from the line length above him, <see cref="CanopyOpening.LinesM"/> + 0.6 m of risers = 7.6 m, at the
    ///              terminal speed of a collapsing canopy, v = √(2 m g / (ρ CdA)) = √(2·3.6·9.80665 / (1.225·12)) = 2.19 m/s
    ///              (3.6 kg canopy and lines, E; a crumpling canopy's drag area 12 m², E, against 39.5 m² full): 3.47 s
    ///   drifts     the wind times that fall, along the wind (in calm air, along his canopy's heading, carried 0.3 m by
    ///              what is left of its drive, E); its centre no further from him than lines + risers + the flat canopy's
    ///              radius (7.6 + 3.96 = 11.56 m), where the lines are straight
    ///   the rule   it lies over his head only if its centre lands within its own flat radius (3.96 m, half the 26 ft): wind
    ///              below 3.96 / 3.47 = 1.14 m/s (≈ 2 kt). That is "calm"; in any real breeze it is a few metres off.
    ///   floats     <see cref="SoakSec"/> (45 s, E) while the cloth wets and its air escapes, then sinks at
    ///              v = √(2 W' / (ρ_w CdA)), W' = 3.6·g·(1 − 1000/1140) = 4.34 N wet weight (nylon ρ 1140), CdA 2 m² of
    ///              crumpled cloth (E): 0.066 m/s. Once it has sunk until lines + its own radius (11.56 m) are taut below him it hangs from his harness: 4.34 N.
    /// </summary>
    public sealed class CanopyOnWater
    {
        public const double MassKg = 3.6, CollapsingCdA = 12.0, FallHeightM = CanopyOpening.LinesM + 0.6, DriveCarryM = 0.3;
        public const double SoakSec = 45.0, NylonDensity = 1140.0, SunkCdA = 2.0, CalmMs = 0.2;
        public static double FlatRadiusM => 0.5 * CanopyOpening.NominalDiameterM;
        public static double MaxReachM => FallHeightM + FlatRadiusM;
        public static double FallSpeedMs(double rhoAir = 1.225) => Math.Sqrt(2.0 * MassKg * 9.80665 / (rhoAir * CollapsingCdA));
        public static double FallSec(double rhoAir = 1.225) => FallHeightM / FallSpeedMs(rhoAir);
        public static double WetWeightN(double rhoW = FloatHydro.WaterDensity) => MassKg * 9.80665 * (1.0 - rhoW / NylonDensity);
        public static double SinkMs(double rhoW = FloatHydro.WaterDensity) => Math.Sqrt(2.0 * WetWeightN(rhoW) / (rhoW * SunkCdA));
        /// <summary>The wind below which the canopy comes down over him (m/s).</summary>
        public static double CoverWindMs(double rhoAir = 1.225) => FlatRadiusM / FallSec(rhoAir);

        /// <summary>Where its centre comes to rest on the water (world, y = the surface).</summary>
        public readonly Vec3 Centre;
        /// <summary>Unit, horizontal: the way it went (downwind).</summary>
        public readonly Vec3 LayDir;
        public readonly double DistanceM;
        public readonly bool CoversPilot;
        private readonly Vec3 _start;

        /// <summary><paramref name="pilot"/>: where he went in (y = the surface); <paramref name="wind"/>: the wind
        /// (horizontal used); <paramref name="heading"/>: his canopy's heading; <paramref name="startApex"/>: where the
        /// canopy was at touchdown.</summary>
        public CanopyOnWater(Vec3 pilot, Vec3 wind, Vec3 heading, Vec3 startApex, double rhoAir = 1.225)
        {
            var wh = new Vec3(wind.X, 0, wind.Z);
            var hh = new Vec3(heading.X, 0, heading.Z);
            double w = wh.Length;
            if (w >= CalmMs) { LayDir = wh / w; DistanceM = Math.Min(MaxReachM, w * FallSec(rhoAir) + DriveCarryM); }
            else { LayDir = hh.LengthSquared > 1e-9 ? hh.Normalized() : new Vec3(0, 0, 1); DistanceM = DriveCarryM; }
            Centre = pilot + LayDir * DistanceM;
            CoversPilot = DistanceM < FlatRadiusM;
            _start = startApex;
        }

        /// <summary>0 at touchdown → 1 lying on the water, after the fall time.</summary>
        public double Settle01(double t, double rhoAir = 1.225) => Math.Clamp(t / FallSec(rhoAir), 0.0, 1.0);

        /// <summary>The canopy's centre at time t after touchdown: from where it was, falling and drifting to its rest,
        /// then sinking below the surface once soaked (never further than the lines reach from the pilot).</summary>
        public Vec3 CentreAt(double t, Vec3 pilotNow, double rhoAir = 1.225, double rhoW = FloatHydro.WaterDensity)
        {
            double s = Settle01(t, rhoAir);
            Vec3 p = _start + (Centre - _start) * s;
            if (s < 1.0) return p;
            return Centre - new Vec3(0, DepthM(t, pilotNow, rhoAir, rhoW), 0);
        }

        /// <summary>Depth of the canopy below the surface, m: 0 while it floats, then sinking until it hangs on its lines.</summary>
        public double DepthM(double t, Vec3 pilotNow, double rhoAir = 1.225, double rhoW = FloatHydro.WaterDensity)
        {
            double sinkT = t - FallSec(rhoAir) - SoakSec;
            if (sinkT <= 0.0) return 0.0;
            double d = SinkMs(rhoW) * sinkT;
            double maxD = HangDepthM(pilotNow);
            return Math.Min(d, maxD);
        }

        /// <summary>How deep it can sink before its lines (and its own radius) are taut below him: √(R² − d²), R the reach.</summary>
        public double HangDepthM(Vec3 pilotNow)
        {
            double horiz = Math.Sqrt((Centre.X - pilotNow.X) * (Centre.X - pilotNow.X) + (Centre.Z - pilotNow.Z) * (Centre.Z - pilotNow.Z));
            return horiz >= MaxReachM ? 0.0 : Math.Sqrt(MaxReachM * MaxReachM - horiz * horiz);
        }

        /// <summary>The pull on his harness, N: its wet weight, once it hangs on taut lines below him.</summary>
        public double LoadOnPilotN(double t, Vec3 pilotNow, double rhoAir = 1.225, double rhoW = FloatHydro.WaterDensity)
        {
            double sinkT = t - FallSec(rhoAir) - SoakSec;
            if (sinkT <= 0.0) return 0.0;
            double maxD = HangDepthM(pilotNow);
            return SinkMs(rhoW) * sinkT >= maxD ? WetWeightN(rhoW) : 0.0;
        }
    }
}
