using System;
using System.Collections.Generic;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>How a harness fitting is opened by hand.</summary>
    public enum HarnessReleaseKind
    {
        /// <summary>A lift-lever latch (Hooker-style lever buckle, an aircraft lap-belt buckle): the lever is lifted away
        /// from the body. Gesture: close the hand on it and lift it <see cref="HarnessRelease.LiftM"/> along its lift axis.</summary>
        LiftLever = 0,
        /// <summary>A rotary cam-lock (5-point buckle): the dial is turned. Gesture: close the hand on it and twist the
        /// wrist <see cref="HarnessRelease.TwistDeg"/> about the dial's axis, either way.</summary>
        RotaryTurn = 1,
    }

    /// <summary>
    /// ONE RELEASE FITTING. State: latched → released, one way (refastening five tongues into a cam-lock is a two-handed
    /// job on the ground, not something done in flight; E — a question for Ty). Harness01 is the rig's `harness_latch`
    /// (CONTRACT: 0 latched .. 1 released).
    ///
    ///   lift lever:  released when the hand has carried the lever ≥ <see cref="LiftM"/> along its lift axis (4 cm, E:
    ///                about 60° of a 4 cm lever, the point where a lever buckle's hook clears the tongue)
    ///   rotary:      released when the hand has turned ≥ <see cref="TwistDeg"/> about the dial axis (60°, E; cam-lock
    ///                dials are turned a quarter turn or so, either direction)
    ///
    /// A controller measures no force, so both are travel thresholds, like the canopy latch's detent.
    /// </summary>
    public sealed class HarnessRelease
    {
        public const double LiftM = 0.04, TwistDeg = 60.0;

        public readonly string Name;
        public readonly HarnessReleaseKind Kind;
        /// <summary>Lift lever: the direction the lever lifts (unit, cockpit frame). Rotary: the dial's axis.</summary>
        public readonly Vec3 Axis;

        public bool Released { get; private set; }
        public bool Held { get; private set; }
        /// <summary>0..1 how far toward release the fitting has been moved (drawn; 1 = released).</summary>
        public double Travel01 { get; private set; }

        private Vec3 _grabPos;
        private Quat _grabRot;

        public HarnessRelease(string name, HarnessReleaseKind kind, Vec3 axis)
        {
            Name = name; Kind = kind;
            Axis = axis.LengthSquared > 1e-12 ? axis.Normalized() : new Vec3(0, 0, 1);
        }

        public void Reset() { Released = false; Held = false; Travel01 = 0.0; }

        public void Grab(Vec3 hand, Quat handRot) { Held = true; _grabPos = hand; _grabRot = handRot; }

        public void Release()
        {
            Held = false;
            if (!Released) Travel01 = 0.0;   // a lever let go snaps back; a dial springs back
        }

        /// <summary>One frame of a held fitting. True on the frame it lets go (a click).</summary>
        public bool Follow(Vec3 hand, Quat handRot)
        {
            if (!Held || Released) return false;
            double t = Kind == HarnessReleaseKind.LiftLever
                ? (hand - _grabPos).Dot(Axis) / LiftM
                : Math.Abs(TwistAboutDeg(_grabRot, handRot, Axis)) / TwistDeg;
            Travel01 = Math.Clamp(t, 0.0, 1.0);
            if (t < 1.0) return false;
            Released = true;
            Travel01 = 1.0;
            return true;
        }

        /// <summary>The twist (swing-twist decomposition) of the rotation from <paramref name="a"/> to <paramref name="b"/>
        /// about <paramref name="axis"/>, degrees, −180..180.</summary>
        public static double TwistAboutDeg(Quat a, Quat b, Vec3 axis)
        {
            Quat r = Quat.Multiply(b, a.Conjugate());
            double proj = r.X * axis.X + r.Y * axis.Y + r.Z * axis.Z;
            double ang = 2.0 * Math.Atan2(proj, r.W) * 180.0 / Math.PI;
            if (ang > 180.0) ang -= 360.0;
            if (ang < -180.0) ang += 360.0;
            return ang;
        }
    }

    /// <summary>
    /// THE PILOT'S BELTS: every independent restraint in the seat. While ANY is latched the pilot cannot leave the seat —
    /// the body stays attached (<see cref="Latched"/>). Aerobatic practice is two independent systems: the Extra 300S
    /// egress card (Patty Wagstaff, ICAS) says "open seat belt (two quick releases: bottom one left, top one right)".
    /// </summary>
    public sealed class SeatHarness
    {
        public readonly List<HarnessRelease> Releases = new();

        public bool Latched
        {
            get { foreach (HarnessRelease r in Releases) if (!r.Released) return true; return false; }
        }

        /// <summary>The rig's `harness_latch`, 0 latched .. 1 released: the main (first) fitting's travel.</summary>
        public double Harness01 => Releases.Count > 0 ? Releases[0].Travel01 : 1.0;

        public void Reset() { foreach (HarnessRelease r in Releases) r.Reset(); }

        /// <summary>
        /// The belts a type carries (E until Codex's nodes and Ty say otherwise). Extra: per the 300S card, TWO — the main
        /// harness's lever release, the TOP one, on the RIGHT, and the lap belt's, the BOTTOM one, on the LEFT. Pitts: one
        /// rotary 5-point release, centred (a second lap belt only if the model carries one). Axes in the cockpit frame:
        /// a lever lifts up and forward, away from the belly; a dial turns about the fore-aft axis out of the belly.
        /// </summary>
        /// <summary>What a pilot in this type is actually strapped in by: nothing, while <see cref="BeltsAndCords.BeltsWorn"/>
        /// is off (Ty 2026-11-05) — an empty harness, never Latched, so the climb-out is never held by a belt. With belts
        /// back on, <see cref="For"/>.</summary>
        public static SeatHarness ForCockpit(string aircraftId) => BeltsAndCords.BeltsWorn ? For(aircraftId) : new SeatHarness();

        public static SeatHarness For(string aircraftId)
        {
            string id = aircraftId ?? "";
            var h = new SeatHarness();
            var liftUpFwd = new Vec3(0.0, 0.6, 0.8);
            if (id.StartsWith("extra"))
            {
                h.Releases.Add(new HarnessRelease("main (top, right)", HarnessReleaseKind.LiftLever, liftUpFwd));
                h.Releases.Add(new HarnessRelease("lap (bottom, left)", HarnessReleaseKind.LiftLever, liftUpFwd));
            }
            else h.Releases.Add(new HarnessRelease("main", HarnessReleaseKind.RotaryTurn, new Vec3(0.0, 0.0, 1.0)));
            return h;
        }
    }
}
