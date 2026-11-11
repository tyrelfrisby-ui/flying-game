using System;
using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    public enum ExitSide { Left = 0, Right = 1 }

    [Flags]
    public enum ClimbEvents { None = 0, Blocked = 1, Exposed = 2, Left = 4, PulledOut = 8 }

    /// <summary>
    /// CLIMBING OUT. A seated player cannot really stand up, so the climb is a hand gesture, the one VR climbing uses:
    /// close a hand on the cockpit SILL (left or right) and push it DOWN — the body rises by what the hand pushes, 1:1,
    /// exactly as pushing yourself up on a sill does. The view moves only because the player's own hand moved it.
    ///
    ///   strapped in (any belt latched)   the rise stops at <see cref="StrappedRiseM"/> (2 cm of webbing stretch)
    ///   canopy in the way (not open past <see cref="CanopyDynamics.ClearToLeaveDeg"/>, nor gone)   stops at
    ///                                    <see cref="CanopyRiseM"/>: the head is against it
    ///   exposed                          at <see cref="ExposedRiseM"/> the shoulders are above the rail: the slipstream
    ///                                    now pulls on the torso, F = q·<see cref="TorsoCdA"/>, aft and outboard
    ///   pulled out                       when F exceeds what a hand holds on the sill (<see cref="CanopyDynamics.HandForceN"/>)
    ///                                    the airstream takes him: he leaves on the side he was holding
    ///   over the side                    or at <see cref="OverRiseM"/> (hips at the rail) he lets go of the sill: out
    ///
    /// Leave to the LEFT (Extra 300 POH and the 300S card); in the Pitts the canopy hinges over to the RIGHT, so left again.
    /// Numbers (E): exposed 0.30 m, over 0.50 m, exposed torso CdA 0.35 m² (half a standing body's ~0.7).
    /// </summary>
    public sealed class ClimbOut
    {
        public const double StrappedRiseM = 0.02, CanopyRiseM = 0.08, ExposedRiseM = 0.30, OverRiseM = 0.50, MaxRiseM = 0.60;
        /// <summary>How far he gets with a FOLDED WING lying across the opening (Ty 2026-11-10: "Negative g wing folds
        /// prevent escape/bailout unless the wing actually departs if a door is blocked"). Less than the canopy allows,
        /// because a canopy he is pressing on is above his head and a wing across the door is in front of his shoulders:
        /// he can get out of the seat and no further. Only the wing DEPARTING clears it.</summary>
        public const double WingBlockedRiseM = 0.05;
        public const double TorsoCdA = 0.35;

        public double RiseM { get; private set; }
        public bool Held { get; private set; }
        public ExitSide Side { get; private set; } = ExitSide.Left;
        public bool Out { get; private set; }
        public bool PulledOut { get; private set; }
        private double _grabY, _grabRise;
        private bool _exposed;

        public void Reset() { RiseM = 0.0; Held = false; Out = PulledOut = false; _exposed = false; Side = ExitSide.Left; }

        public void Grab(Vec3 hand, ExitSide side) { Held = true; Side = side; _grabY = hand.Y; _grabRise = RiseM; }

        /// <summary>Let go of the sill. True if that put him over the side.</summary>
        public bool Release()
        {
            if (!Held) return false;
            Held = false;
            if (RiseM >= OverRiseM && !Out) { Out = true; return true; }
            if (!Out) RiseM = Math.Min(RiseM, ExposedRiseM * 0.5);   // let go short of the rail: he sits back down
            return false;
        }

        /// <summary>One frame. <paramref name="hand"/>: the hand on the sill, cockpit frame, measured from where the seat
        /// is NOW (i.e. without the rise already applied — the body rises, the hand stays on the sill).</summary>
        /// <param name="exitClear">Is the way out of the opening clear of the AEROPLANE? False when a folded wing lies
        /// across it (EgressClearance.From with the panels, then Hit at the opening). A folded wing is still attached,
        /// so nothing else will move it: he is trapped until it departs.</param>
        public ClimbEvents Step(Vec3 hand, bool strapped, bool canopyClear, double q, bool exitClear = true)
        {
            ClimbEvents ev = ClimbEvents.None;
            if (Out || !Held) return ev;
            double want = Math.Clamp(_grabRise + (_grabY - hand.Y), 0.0, MaxRiseM);
            double cap = strapped ? StrappedRiseM : canopyClear ? MaxRiseM : CanopyRiseM;
            if (!exitClear) cap = Math.Min(cap, WingBlockedRiseM);
            if (want > cap + 1e-4) ev |= ClimbEvents.Blocked;
            RiseM = Math.Min(want, cap);
            if (RiseM >= ExposedRiseM && !_exposed) { _exposed = true; ev |= ClimbEvents.Exposed; }
            if (RiseM >= ExposedRiseM && SlipstreamN(q, RiseM) > CanopyDynamics.HandForceN)
            {
                Out = PulledOut = true;
                Held = false;
                ev |= ClimbEvents.PulledOut | ClimbEvents.Left;
            }
            return ev;
        }

        /// <summary>Slipstream force on the exposed torso, N: q·CdA scaled by how much of him is above the rail.</summary>
        public static double SlipstreamN(double q, double riseM)
        {
            double f = Math.Clamp((riseM - ExposedRiseM * 0.5) / (MaxRiseM - ExposedRiseM * 0.5), 0.0, 1.0);
            return q * TorsoCdA * f;
        }

        /// <summary>The speed above which the slipstream pulls an exposed pilot off the sill (m/s TAS at this density),
        /// at the point of first exposure.</summary>
        public static double PullOutSpeedMs(double rho)
        {
            double f = (ExposedRiseM - ExposedRiseM * 0.5) / (MaxRiseM - ExposedRiseM * 0.5);
            return Math.Sqrt(2.0 * CanopyDynamics.HandForceN / (rho * TorsoCdA * f));
        }
    }

    /// <summary>The pilot's state the moment he leaves, and the body he becomes.</summary>
    public static class JumperAero
    {
        /// <summary>Mass the drag areas are solved at (SkydiverAero): 90 kg pilot + 15 kg kit, which includes the rig
        /// (the Para-Cushion weighs 14½–15¾ lb, ~7 kg).</summary>
        public const double MassKg = SkydiverAero.ReferenceMassKg;
        /// <summary>Tumbling, unstable: 0.7 m² (E — the existing PilotBody's figure; draggier than a stable belly-to-earth
        /// body's 0.585 m² because limbs flail broadside).</summary>
        public const double TumblingCdA = 0.7;
        /// <summary>An untrained body leaving an aeroplane tumbles; it settles belly-to-earth after about this long (E).</summary>
        public const double StabiliseSec = 3.0;
        /// <summary>The push off the sill, m/s: outboard and up (E).</summary>
        public const double PushOutMs = 1.0, PushUpMs = 0.5;

        /// <summary>Drag area at a time since exit: tumbling, then stable belly to earth (SkydiverAero.CdABelly = 0.585 m²,
        /// back-solved from 120 mph = 53.6 m/s at 105 kg, sea level).</summary>
        public static double CdA(double secSinceExit) =>
            secSinceExit < StabiliseSec ? TumblingCdA : SkydiverAero.CdABelly;

        /// <summary>v_t = √(2 m g / (ρ CdA)).</summary>
        public static double TerminalSpeed(double cdA, double massKg, double rho) =>
            Math.Sqrt(2.0 * massKg * 9.80665 / (Math.Max(1e-6, rho) * Math.Max(1e-6, cdA)));

        /// <summary>
        /// The pilot's velocity the instant he lets go: the aeroplane's velocity at that point, v_cg + ω × r (r from the
        /// aeroplane's reference to him, ω its rates; any one frame), plus the push off the sill, outboard on his exit side
        /// and up (the frame's right and up unit vectors).
        /// </summary>
        public static Vec3 ExitVelocity(Vec3 vAircraft, Vec3 omega, Vec3 r, Vec3 right, Vec3 up, ExitSide side) =>
            vAircraft + omega.Cross(r) + right * (side == ExitSide.Left ? -PushOutMs : PushOutMs) + up * PushUpMs;
    }

    /// <summary>
    /// DID HE HIT THE TAIL? The aeroplane's lifting surfaces as boxes in its own body frame (x forward, y right, z down —
    /// the sim's), built from the config's strips: each strip spans [x − ¾c, x + ¼c] fore-aft (its position is the
    /// quarter chord), its share of the span either side of its y, ±<see cref="ThicknessM"/> about its z. A body of
    /// radius r inside any box (grown by r) has hit that surface. Reported, not resolved: the jumper is a point mass
    /// (Bridge) and the aeroplane flies on.
    /// </summary>
    public sealed class EgressClearance
    {
        public const double ThicknessM = 0.12;
        /// <summary>Radius of the falling body (E: a curled-up adult's half-width).</summary>
        public const double BodyRadiusM = 0.30;

        public readonly struct Box
        {
            public readonly string Surface;
            public readonly Vec3 Min, Max;
            public Box(string s, Vec3 min, Vec3 max) { Surface = s; Min = min; Max = max; }
        }

        public readonly List<Box> Boxes = new();

        public static EgressClearance From(AircraftConfig cfg) => From(cfg, null);

        /// <summary>
        /// The same, with the WING WHERE IT ACTUALLY IS. Each wing strip's box is placed through its panel's
        /// <see cref="WingPanel.Shape"/> — the strike's kink, the aerodynamic curve and, the reason this exists, the
        /// FOLD — so a wing that has folded over the cockpit is a box over the cockpit.
        ///
        /// Ty 2026-11-10: "Negative g wing folds prevent escape/bailout unless the wing actually departs if a door is
        /// blocked." A push folds a wing DOWN (the set a negative overstress leaves is tips down), and on a high-wing
        /// aeroplane like the Cub — whose door is in the side of the cabin, under the wing root — a wing folded down
        /// about its root sweeps straight across that door. Nothing in the straight-strip boxes could ever say so,
        /// because they are the aeroplane as authored.
        ///
        /// <paramref name="panels"/> null, or a panel whose wing is straight, gives exactly the boxes this always
        /// gave: Shape is the identity on an undamaged wing.
        /// </summary>
        public static EgressClearance From(AircraftConfig cfg, IReadOnlyList<WingPanel> panels)
        {
            var c = new EgressClearance();
            if (cfg?.Surfaces == null) return c;
            // Which panel owns each strip (AeroModel strip order), so a wing row can be put where its panel is.
            WingPanel[] owner = null;
            if (panels is { Count: > 0 })
            {
                int total = 0; foreach (SurfaceConfig sf in cfg.Surfaces) total += sf.Strips?.Count ?? 0;
                owner = new WingPanel[total];
                foreach (WingPanel p in panels)
                    foreach (int i in p.SideStrips)
                        if (i >= 0 && i < total) owner[i] = p;
            }
            int idx = -1;
            foreach (SurfaceConfig s in cfg.Surfaces)
            {
                if (s.Strips == null) continue;
                foreach (StripConfig st in s.Strips)
                {
                    idx++;
                    if (st.Pos == null || st.Pos.Length < 3) continue;
                    double chord = Math.Max(0.05, st.Chord);
                    double halfSpan = chord > 1e-6 ? 0.5 * st.Area / chord : 0.1;
                    double x = st.Pos[0], y = st.Pos[1], z = st.Pos[2];
                    bool vertical = s.Id.IndexOf("vstab", StringComparison.OrdinalIgnoreCase) >= 0
                                    || s.Id.IndexOf("fin", StringComparison.OrdinalIgnoreCase) >= 0
                                    || s.Id.IndexOf("rudder", StringComparison.OrdinalIgnoreCase) >= 0;
                    Vec3 half = vertical ? new Vec3(0, ThicknessM, halfSpan) : new Vec3(0, halfSpan, ThicknessM);
                    // Where this row's section actually is. A shaped wing's box is taken about the MOVED centre and
                    // grown to hold the span it sweeps through, which is what a folded wing does to a doorway: it is
                    // the box being in the way that blocks the door, not its exact corners.
                    WingPanel own = owner is not null && idx < owner.Length ? owner[idx] : null;
                    if (own is not null)
                    {
                        Vec3 at = own.Shape(new Vec3(x, y, z));
                        Vec3 outer = own.Shape(new Vec3(x, y + Math.Sign(y == 0 ? 1 : y) * halfSpan, z));
                        y = at.Y; z = at.Z;
                        double dy = Math.Abs(outer.Y - at.Y), dz = Math.Abs(outer.Z - at.Z);
                        half = new Vec3(0, Math.Max(ThicknessM, dy), Math.Max(ThicknessM, dz));
                    }
                    var min = new Vec3(x - 0.75 * chord, y - half.Y, z - half.Z);
                    var max = new Vec3(x + 0.25 * chord, y + half.Y, z + half.Z);
                    c.Boxes.Add(new Box(s.Id, min, max));   // the configs list both halves strip by strip
                }
            }
            return c;
        }

        /// <summary>The surface a body at <paramref name="bodyPos"/> (aeroplane body frame) touches, or null.</summary>
        public string Hit(Vec3 bodyPos, double radius = BodyRadiusM)
        {
            foreach (Box b in Boxes)
            {
                if (bodyPos.X < b.Min.X - radius || bodyPos.X > b.Max.X + radius) continue;
                if (bodyPos.Y < b.Min.Y - radius || bodyPos.Y > b.Max.Y + radius) continue;
                if (bodyPos.Z < b.Min.Z - radius || bodyPos.Z > b.Max.Z + radius) continue;
                return b.Surface;
            }
            return null;
        }
    }
}
