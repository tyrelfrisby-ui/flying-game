using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Practice;
using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>The controls an autopilot (or a lesson) flies through — the widget's WidgetControls, or the game's adapter.</summary>
    public interface IAeroControls
    {
        double Aileron { get; set; }
        double Elevator { get; set; }
        double Rudder { get; set; }
        double Throttle01 { get; set; }
        bool ElevatorFree { get; set; }
    }

    /// <summary>
    /// What the shared teaching layers need from whoever hosts them (owner rule 2026-10-09: the GAME and the WIDGET stay in
    /// PARITY — one implementation, two hosts). The Aero Widget (AeroWidget: its own sim, review history, Syphon/NDI frame) and
    /// the game (GameAeroHost: FlightSimDriver + the chase camera, drawn on the screen) both derive from this. The vectors,
    /// labels, insets, curves, weight &amp; balance and the autopilot use only these members.
    /// </summary>
    public abstract class AeroHost : MonoBehaviour
    {
        public string AircraftId { get; protected set; } = "c172-like";
        public bool Paused;
        public AircraftConfig Config { get; protected set; }
        public Aircraft Ac { get; protected set; }
        public float SpanM { get; protected set; } = 11f;
        public float LengthM { get; protected set; } = 8f;
        public Camera Cam { get; protected set; }
        protected WidgetVectors _vectors;
        public WidgetVectors Vectors => _vectors;
        protected WidgetControlsDisplay _display;
        public WidgetControlsDisplay Display => _display;
        /// <summary>The airplane drawn larger than life when the view is too wide to see it (1 = true scale).</summary>
        public float VisualScale { get; protected set; } = 1f;
        public WidgetLoading Loading { get; protected set; }
        public WidgetAutopilot Autopilot { get; protected set; }
        public WidgetCurves Curves { get; protected set; }
        public readonly HashSet<string> Insets = new();
        public string Lesson;
        public double BallDeg => Vectors?.Current?.BallDeg ?? 0;

        /// <summary>The controls the autopilot flies through.</summary>
        public abstract IAeroControls AeroControls { get; }
        /// <summary>The widget (review, lessons, its own world references) vs the game.</summary>
        public virtual bool IsWidgetHost => false;
        /// <summary>The reviewed frame (widget review mode), null = live.</summary>
        public virtual WidgetHistory.Snap Shown => null;
        public virtual WidgetReview Review => null;
        public virtual WidgetLessons Lessons => null;
        /// <summary>What the picture shows: the reviewed frame's state, or the live aircraft's.</summary>
        public RigidBodyState ShownState => Shown != null ? Shown.State : Ac.State;
        public IReadOnlyList<FlyingGame.Core.Aero.ForceSample> ShownForces => Shown != null ? Shown.Forces : Ac.LastForces;
        public double ShownNz => Shown != null ? Shown.Nz : Ac.LoadFactorZ;

        /// <summary>PROTOCOL 6: "ntsb" (default — the airplane above, the instrument plates below, on transparency) or
        /// "classic" (the picture + the protocol-4 control panel). Split = the panel's fraction of the frame's height.</summary>
        public string DisplayMode = "ntsb";
        public float Split = 0.45f;
        /// <summary>The fleet's name for the type, in capitals ("PITTS S-2").</summary>
        public virtual string AircraftName => AircraftId.Replace("-like", "").ToUpperInvariant();

        private readonly Dictionary<string, double> _critAlpha = new(), _stallKias = new();
        private readonly Dictionary<string, Vec3> _np = new();
        /// <summary>The neutral point (body, config coords): where an increment of lift acts — dM/dL from two angles of attack
        /// on the aero model. TOTAL AERO is drawn from here.</summary>
        public Vec3 NeutralPointBody()
        {
            double flaps = Ac.FlapFraction; string key = $"{AircraftId}|{System.Math.Round(flaps, 2)}";
            Vec3 cg = Config.Mass.CgVec();
            if (_np.TryGetValue(key, out var v)) return new Vec3(v.X, 0, cg.Z);   // (an absolute position: independent of the loaded CG)
            var tables = Aircraft.BuildAirfoilTables(Config);
            // The aero model's own totals (moment about the CG): x_np = x_cg + ΔM / ΔL. (Summing the overlay's samples missed
            // the fuselage's destabilising moment and put the C172's NP ~20 % MAC too far aft.)
            (double L, double M) At(double aDeg)
            {
                double ar = aDeg * System.Math.PI / 180, V = 40;
                var keep = FlyingGame.Core.Aero.ForceDebug.Samples; FlyingGame.Core.Aero.ForceDebug.Samples = null;
                var (F, Mo) = FlyingGame.Core.Aero.AeroModel.Compute(Config, tables, new Vec3(V * System.Math.Cos(ar), 0, V * System.Math.Sin(ar)), Vec3.Zero, Vec3.Zero, 1.225, new FlyingGame.Core.Aero.ControlDeflections(0, 0, 0, 0, flaps));
                FlyingGame.Core.Aero.ForceDebug.Samples = keep;
                return (F.X * System.Math.Sin(ar) - F.Z * System.Math.Cos(ar), Mo.Y);
            }
            var (l1, m1) = At(2); var (l2, m2) = At(6);
            double x = System.Math.Abs(l2 - l1) > 1 ? cg.X + (m2 - m1) / (l2 - l1) : cg.X;
            _np[key] = new Vec3(x, 0, cg.Z);
            return new Vec3(x, 0, cg.Z);
        }
        private readonly Dictionary<string, Vec3> _midSpan = new();
        /// <summary>Mid-semispan point of the left (−1) or right (+1) wing (body, config coords).</summary>
        public Vec3 WingMidSpan(int side)
        {
            string key = AircraftId + side;
            if (_midSpan.TryGetValue(key, out var v)) return v;
            double maxY = 0;
            foreach (var sf in Config.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (var st in sf.Strips) maxY = System.Math.Max(maxY, side * st.PosVec().Y);
            Vec3 best = new(Config.Mass.CgVec().X, side * SpanM * 0.25, Config.Mass.CgVec().Z); double bd = double.MaxValue;
            foreach (var sf in Config.Surfaces) if (sf.Id.ToLowerInvariant().Contains("wing")) foreach (var st in sf.Strips)
            { var p = st.PosVec(); if (side * p.Y <= 0) continue; double d = System.Math.Abs(side * p.Y - maxY * 0.5); if (d < bd) { bd = d; best = p; } }
            _midSpan[key] = best;
            return best;
        }
        /// <summary>The type's critical (stall) angle of attack with these flaps: the α of peak lift, swept on the aero model.</summary>
        public double CriticalAlphaDeg(double flaps)
        {
            string key = $"{AircraftId}|{System.Math.Round(flaps, 2)}";
            if (_critAlpha.TryGetValue(key, out double v)) return v;
            var tables = Aircraft.BuildAirfoilTables(Config);
            double best = double.MinValue, bestA = 15;
            for (double a = 0; a <= 35; a += 0.25)
            {
                double ar = a * System.Math.PI / 180, V = 40;
                var (F, _) = FlyingGame.Core.Aero.AeroModel.Compute(Config, tables, new Vec3(V * System.Math.Cos(ar), 0, V * System.Math.Sin(ar)), Vec3.Zero, Vec3.Zero, 1.225,
                    new FlyingGame.Core.Aero.ControlDeflections(0, 0, 0, 0, flaps));
                double lift = F.X * System.Math.Sin(ar) - F.Z * System.Math.Cos(ar);
                if (lift > best) { best = lift; bestA = a; }
            }
            _critAlpha[key] = bestA;
            return bestA;
        }
        /// <summary>The 1-g stall speed (KIAS) with these flaps — the red band on the airspeed tape.</summary>
        public double StallKias(double flaps)
        {
            string key = $"{AircraftId}|{System.Math.Round(flaps, 2)}|{Config.Mass.MassKg:0}";
            if (_stallKias.TryGetValue(key, out double v)) return v;
            v = PracticeScenario.EstimateVso(Config, 0, flaps) * 1.943844;
            _stallKias[key] = v;
            return v;
        }


        public readonly Dictionary<string, bool> Show = new()
        {
            ["lift"] = false, ["drag"] = false, ["weight"] = false, ["thrust"] = true, ["wind"] = false, ["total"] = true,
            ["axis"] = false, ["wheels"] = true, ["labels"] = true, ["readout"] = true, ["strips"] = false, ["review"] = true,
            ["controlsDisplay"] = false, ["controlTraces"] = false, ["horizon"] = true, ["vectors"] = false,
            ["wingWind"] = true, ["tailWind"] = true, ["inertial"] = true, ["tail"] = true, ["moments"] = true,
            ["yawMoment"] = false, ["finForce"] = false, ["bodyAxes"] = false, ["cgnp"] = false,
            ["liftComponents"] = false, ["wingDrag"] = false, ["groundTrack"] = false, ["aimPoint"] = false, ["propEffects"] = false,
        };


        // ---- readouts for the labels and the state stream ----
        public struct Readout
        {
            public double Kias, Ktas, AlphaDeg, BetaDeg, PitchDeg, RollDeg, HeadingDeg, SinkFpm, HeightFt, YawRateDps, RollRateDps, PitchRateDps, LoadFactor, Q;
            public double LeftAlphaDeg, RightAlphaDeg; public bool LeftStalled, RightStalled, OnGround;
        }

        public Readout Read() => Read(ShownState, ShownNz);

        public Readout Read(RigidBodyState s, double nz)
        {
            var r = new Readout();
            Vec3 vAir = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position));
            double tas = vAir.Length, rho = Atmosphere.DensityAtAltitude(-s.Position.Z);
            r.Ktas = tas * 1.943844; r.Kias = tas * System.Math.Sqrt(rho / 1.225) * 1.943844; r.Q = 0.5 * rho * tas * tas;
            r.AlphaDeg = tas > 1 ? System.Math.Atan2(vAir.Z, vAir.X) * 57.2958 : 0;
            r.BetaDeg = tas > 1 ? System.Math.Asin(System.Math.Clamp(vAir.Y / tas, -1, 1)) * 57.2958 : 0;
            var q = s.Attitude;
            r.RollDeg = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 57.2958;
            r.PitchDeg = System.Math.Asin(System.Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)) * 57.2958;
            r.HeadingDeg = (System.Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 57.2958 + 360) % 360;
            Vec3 vW = q.Rotate(s.Velocity);
            r.SinkFpm = vW.Z * 196.85; r.HeightFt = -s.Position.Z * 3.28084;
            r.RollRateDps = s.Rates.X * 57.2958; r.PitchRateDps = s.Rates.Y * 57.2958; r.YawRateDps = s.Rates.Z * 57.2958;
            r.LoadFactor = nz;
            // Each wing's local α at mid-semispan: the airflow there includes the rotation (ω × r) — the heart of the spin.
            double half = SpanM * 0.5;
            Vec3 Local(double y) { Vec3 rr = new(0, y, 0); return vAir + Vec3.Cross(s.Rates, rr); }
            Vec3 vl = Local(-half * 0.5), vr = Local(half * 0.5);
            r.LeftAlphaDeg = System.Math.Atan2(vl.Z, System.Math.Max(0.1, vl.X)) * 57.2958;
            r.RightAlphaDeg = System.Math.Atan2(vr.Z, System.Math.Max(0.1, vr.X)) * 57.2958;
            double stallDeg = 15.0;
            r.LeftStalled = r.LeftAlphaDeg > stallDeg; r.RightStalled = r.RightAlphaDeg > stallDeg;
            r.OnGround = LandingGear.AnyMainWheelOnGround(Config, s);
            return r;
        }

    }
}
