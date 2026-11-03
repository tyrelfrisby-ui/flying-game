using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE RIPCORD HANDLE on a pilot's back-type emergency rig. Source: Strong Enterprises Para-Cushion Owner's Manual,
    /// Rev. H (11/2000), "How to open your parachute":
    ///   "The ripcord handle is located near the chest strap on the wearer's LEFT front of the harness. The key is to LOOK
    ///    at the ripcord handle … REACH over and grab it with both hands (or typically with your right hand and left
    ///    thumb), and PULL … The Para-Cushion Back and Para-Cushion Chair both have housings coming over your shoulder, so
    ///    push the handle DOWN toward your feet."
    /// So: on the left main lift web at the chest strap (Codex's Grip_Ripcord overrides the place), pulled DOWN.
    ///
    /// PULL. The cable has to draw its pins out of the closing loops: <see cref="PullM"/> = 0.10 m (4 in, E — a pin's
    /// engaged length plus the cable's slack; the manual's instruction is to pull it right out). The force a real one
    /// needs is up to 22 lbf (98 N) — TSO-C23's maximum manual pull force for an emergency parachute (as quoted by the
    /// USPA and by APF RAC 502B; the TSO text itself was not re-read for this). A controller measures no force, so the
    /// trigger is the distance along the pull direction since the grab; a partial pull stays partial (no spring).
    /// </summary>
    public sealed class Ripcord
    {
        public const double PullM = 0.10;
        public const double RealPullForceMaxN = 97.9;   // 22 lbf

        public bool Pulled { get; private set; }
        public bool Held { get; private set; }
        /// <summary>0..1 of the pull distance done so far (drawn: the handle comes out of its pocket that far).</summary>
        public double Travel01 { get; private set; }
        /// <summary>Unit pull direction in the body frame: down toward the feet (+ a little outboard is fine).</summary>
        public readonly Vec3 PullDir;

        private Vec3 _grab;
        private double _grabTravel;

        public Ripcord(Vec3 pullDir) { PullDir = pullDir.LengthSquared > 1e-12 ? pullDir.Normalized() : new Vec3(0, -1, 0); }

        public static Ripcord BackRig() => new(new Vec3(0, -1, 0));

        public void Reset() { Pulled = Held = false; Travel01 = 0.0; }

        public void Grab(Vec3 hand) { if (Pulled) return; Held = true; _grab = hand; _grabTravel = Travel01; }

        public void Release() => Held = false;

        /// <summary>One frame of a hand on the handle: true on the frame the pins clear (the pack opens).</summary>
        public bool Follow(Vec3 hand)
        {
            if (!Held || Pulled) return false;
            double d = (hand - _grab).Dot(PullDir) / PullM + _grabTravel;
            Travel01 = Math.Clamp(Math.Max(Travel01, d), 0.0, 1.0);
            if (d < 1.0) return false;
            Pulled = true;
            Travel01 = 1.0;
            return true;
        }
    }

    public enum OpeningPhase { Packed = 0, PilotChute = 1, Inflating = 2, Open = 3 }

    /// <summary>
    /// THE EMERGENCY CANOPY OPENING, from the pins clearing to a full canopy. Its numbers:
    ///
    ///   canopy        26 ft (7.925 m) nominal diameter, round, low-porosity — the Strong Para-Cushion's ("fitted with a
    ///                 round 26-foot diameter, steerable canopy", Owner's Manual Rev. H); Codex states the drawn one.
    ///   Cd0           0.80 on the nominal area S0 = π D0²/4 (E, inside the 0.75–0.90 band usually quoted for conical
    ///                 canopies; a low-porosity cloth sits at the high side of a solid canopy's range)
    ///   pilot chute   spring-launched 8 m/s (E) out of the pack; 0.76 m (30 in, E), Cd 0.9 (E): CdS 0.41 m², 0.25 kg;
    ///                 after the 1.5 m bridle it extracts the 3 kg bag (E)
    ///   line stretch  when the pilot chute has separated bridle + lines = 1.5 + 7.0 m (E; 7 m is the existing Parachute)
    ///   inflation     fill time t_f = n·D0 / V_ls, the canopy filling over a constant DISTANCE of n diameters (Knacke,
    ///                 Parachute Recovery Systems Design Manual, NWC TP 6575 — n ≈ 8 for solid canopies; quoted from the
    ///                 existing Parachute.cs, not re-read for this). Drag area CdS(t) = CdS_pc + (CdS0 − CdS_pc)·(t/t_f)²
    ///                 (E: the quadratic drag-area-time form of the Pflanz method for a solid canopy)
    ///
    /// The OPENING SHOCK is not a number put in: it comes out of m·dv/dt = −½ρv²(CdS(t) + CdA_body) + m·g with the drag
    /// area growing while the speed is still high. From 53.6 m/s at sea level with 105 kg that is ≈ 6.7 g peak, 0.23 s
    /// to line stretch, t_f 1.22 s, 3.2 s and 64 m (210 ft) to steady descent (at 3000 m from 62 m/s: 8.1 g) — the manual: "opening times are from 2
    /// to 3 seconds and the distance fallen would be from 150 feet to 300 feet". Steady:
    ///
    ///   v = √(2 m g / (ρ (Cd0·S0 + CdA_hanging))) = √(2·105·9.80665 / (1.225·(39.46 + 0.5))) = 6.49 m/s at sea level.
    /// </summary>
    public sealed class CanopyOpening
    {
        public const double NominalDiameterM = 7.925, Cd0 = 0.80, FillConstant = 8.0;
        public const double PilotChuteLaunchMs = 8.0, PilotChuteCdS = 0.41, PilotChuteMassKg = 0.25, BagMassKg = 3.0;
        public const double BridleM = 1.5, LinesM = 7.0;
        /// <summary>Pilot hanging upright under the canopy (the existing PilotBody's figure, E).</summary>
        public const double HangingCdA = 0.5;

        public static double NominalAreaM2 => Math.PI * NominalDiameterM * NominalDiameterM / 4.0;
        public static double FullCdS => Cd0 * NominalAreaM2;

        public OpeningPhase Phase { get; private set; } = OpeningPhase.Packed;
        public double TimeSec { get; private set; }
        public double LineStretchSec { get; private set; }
        public double LineStretchSpeedMs { get; private set; }
        public double FillTimeSec { get; private set; }
        /// <summary>Separation of the pilot chute from the pilot, m (0 .. bridle + lines).</summary>
        public double SeparationM { get; private set; }
        /// <summary>The canopy's current drag area, m² (the pilot's own body is NOT in it).</summary>
        public double CdS { get; private set; }
        /// <summary>0..1 time through the inflation: the shape key "Inflate" (and the drawn radius).</summary>
        public double Inflate01 { get; private set; }

        private double _pcSpeed, _tFill;

        /// <summary>Pins clear: the spring throws the pilot chute out.</summary>
        public void Deploy(double airspeedMs)
        {
            if (Phase != OpeningPhase.Packed) return;
            Phase = OpeningPhase.PilotChute;
            TimeSec = 0.0; SeparationM = 0.0;
            _pcSpeed = Math.Max(0.0, airspeedMs - PilotChuteLaunchMs);   // its own airspeed, along the pilot's path
            CdS = PilotChuteCdS;
        }

        /// <summary>Filling time for a line-stretch speed: n·D0 / V.</summary>
        public static double FillTime(double lineStretchSpeedMs) => FillConstant * NominalDiameterM / Math.Max(1.0, lineStretchSpeedMs);

        /// <summary>One step at the pilot's current airspeed and air density.</summary>
        public void Step(double dt, double airspeedMs, double rho)
        {
            if (Phase == OpeningPhase.Packed || dt <= 0.0) return;
            TimeSec += dt;
            if (Phase == OpeningPhase.PilotChute)
            {
                // The pilot chute decelerates on its own drag (the bag joins it once the bridle is out); the gap grows by
                // the difference in speed.
                double m = PilotChuteMassKg + (SeparationM >= BridleM ? BagMassKg : 0.0);
                double k = 0.5 * rho * PilotChuteCdS / m;
                _pcSpeed = _pcSpeed / (1.0 + k * Math.Max(0.0, _pcSpeed) * dt);   // implicit quadratic drag
                SeparationM += Math.Max(0.0, airspeedMs - _pcSpeed) * dt;
                CdS = PilotChuteCdS;
                if (SeparationM < BridleM + LinesM && TimeSec < 10.0) return;
                Phase = OpeningPhase.Inflating;
                LineStretchSec = TimeSec;
                LineStretchSpeedMs = airspeedMs;
                FillTimeSec = FillTime(airspeedMs);
                _tFill = 0.0;
            }
            if (Phase == OpeningPhase.Inflating)
            {
                _tFill += dt;
                double s = Math.Clamp(_tFill / FillTimeSec, 0.0, 1.0);
                Inflate01 = s;
                CdS = PilotChuteCdS + (FullCdS - PilotChuteCdS) * s * s;
                if (s >= 1.0) Phase = OpeningPhase.Open;
                return;
            }
            Inflate01 = 1.0;
            CdS = FullCdS;
        }

        /// <summary>Steady sink (m/s) under the full canopy.</summary>
        public static double SteadySinkMs(double massKg, double rho) =>
            Math.Sqrt(2.0 * massKg * 9.80665 / (rho * (FullCdS + HangingCdA)));

        public readonly struct Result
        {
            public readonly double LineStretchSec, FillTimeSec, PeakG, SteadySec, LostHeightM, FinalSinkMs;
            public Result(double ls, double tf, double g, double t, double h, double v)
            { LineStretchSec = ls; FillTimeSec = tf; PeakG = g; SteadySec = t; LostHeightM = h; FinalSinkMs = v; }
        }

        /// <summary>
        /// A vertical opening, integrated: what the pilot feels (peak drag / weight, in g) and how long and how far it
        /// takes. Body drag area blends from <paramref name="freeCdA"/> to <see cref="HangingCdA"/> with the inflation,
        /// as the Bridge's PilotBody does.
        /// </summary>
        public static Result Simulate(double v0, double rho, double massKg, double freeCdA, double dt = 1e-3)
        {
            var o = new CanopyOpening();
            o.Deploy(v0);
            double v = v0, t = 0, h = 0, peak = 0;
            while (t < 30.0)
            {
                o.Step(dt, v, rho);
                double body = freeCdA + (HangingCdA - freeCdA) * o.Inflate01;
                double drag = 0.5 * rho * v * v * (o.CdS + body);
                peak = Math.Max(peak, drag / (massKg * 9.80665));
                double a = 9.80665 - drag / massKg;
                v += a * dt; h += v * dt; t += dt;
                if (o.Phase == OpeningPhase.Open && Math.Abs(a) < 0.05) break;
            }
            return new Result(o.LineStretchSec, o.FillTimeSec, peak, t, h, v);
        }
    }
}
