using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>Which kind of canopy latch a type has, for the grab system to offer.</summary>
    public enum CanopyLatchKind
    {
        /// <summary>No latch to grab: an open cockpit (Great Lakes), a door (Cub, 172), or not built yet.</summary>
        None = 0,
        /// <summary>The Pitts S-2: a black ball on top of the rear panel that slides LEFT / RIGHT; fully right = locked.</summary>
        SlideBall = 1,
        /// <summary>The Extra 300: two red-and-white handles squeezed TOGETHER to unlock; spring-loaded back to locked.</summary>
        Squeeze = 2,
    }

    /// <summary>The state every latch shows the rest of the game: locked or not, and the rig's `canopy_latch`
    /// param (0 locked .. 1 fully open / squeezed, CONTRACT.md). Phase 2's canopy opening reads <see cref="Locked"/>.</summary>
    public interface ICanopyLatch
    {
        CanopyLatchKind Kind { get; }
        bool Locked { get; }
        double Latch01 { get; }
        /// <summary>A fresh aeroplane: closed and locked.</summary>
        void Reset();
    }

    /// <summary>
    /// THE PITTS S-2 CANOPY LATCH (owner Ty: "the pitts has a black ball that slides left/right"). The ball rides a
    /// slot ~3 in long on top of the rear panel; its pin drops into the block that captures the canopy bows.
    /// Fully RIGHT = LOCKED (CONTRACT.md), with a DETENT at that end.
    ///
    ///   position s, metres from the full-LEFT (open) end, 0 .. <see cref="TravelM"/>
    ///   held:     s follows the hand's lateral travel since the grab (1:1)
    ///   detent:   reaching within <see cref="DetentCaptureM"/> of the right end snaps it home → LOCKED (a click);
    ///             once home it stays home until the hand pulls <see cref="DetentBreakoutM"/> to the left of it —
    ///             that breakout is the detent's spring, in hand travel, since a controller has no force to measure
    ///   released: it stays where it was left (a sliding pin in a slot has friction, no spring)
    ///   Latch01 = 1 − s / travel  (0 locked .. 1 fully open)
    ///
    /// Numbers: travel 3 in = 0.0762 m (Ty, "~3 in"); capture 4 mm and breakout 10 mm are estimates (E) of a
    /// spring-ball detent's feel — tune on the headset.
    /// </summary>
    public sealed class PittsCanopyLatch : ICanopyLatch
    {
        public const double TravelM = 0.0762, DetentCaptureM = 0.004, DetentBreakoutM = 0.010;

        public CanopyLatchKind Kind => CanopyLatchKind.SlideBall;
        /// <summary>The ball's position, metres from the full-left end.</summary>
        public double PositionM { get; private set; } = TravelM;
        public bool Locked { get; private set; } = true;
        public double Latch01 => 1.0 - PositionM / TravelM;

        private bool _held;
        private double _grabHandX, _grabPos;

        public void Reset() { PositionM = TravelM; Locked = true; _held = false; }

        /// <summary>The hand closes on the ball. <paramref name="handRightM"/> is the hand's position along the
        /// aeroplane's lateral axis (+ right), any origin.</summary>
        public void Grab(double handRightM) { _held = true; _grabHandX = handRightM; _grabPos = PositionM; }

        public void Release() => _held = false;

        /// <summary>One frame of a held ball. Returns true on the frame it clicks into the detent (a haptic click).</summary>
        public bool Follow(double handRightM)
        {
            if (!_held) return false;
            double want = Math.Clamp(_grabPos + (handRightM - _grabHandX), 0.0, TravelM);
            if (Locked)
            {
                // Home in the detent: the hand has to pull past the breakout to get it out.
                if (want > TravelM - DetentBreakoutM) { PositionM = TravelM; return false; }
                Locked = false;
                PositionM = want;
                return false;
            }
            if (want >= TravelM - DetentCaptureM)
            {
                PositionM = TravelM;
                Locked = true;
                return true;
            }
            PositionM = want;
            return false;
        }
    }

    /// <summary>
    /// THE EXTRA 300 CANOPY LATCH (owner Ty: "the extra has two red and white handles that squeeze together").
    /// Each handle turns about its own pivot toward the other; squeezing them withdraws the locking pins, and their
    /// springs push them back out.
    ///
    ///   squeeze per handle 0..1 (1 = touching). One hand on the pair with the trigger squeezes BOTH; two hands
    ///   each squeeze their own.
    ///   unlock:  both handles past <see cref="UnlockAt"/> (both pins out) → unlocked
    ///   release: the handles spring back. With the canopy still SHUT the pins snap back in → LOCKED again; with it
    ///            lifted off the sill (Phase 2: <c>canopyOpen01</c> &gt; 0) they spring out against nothing → it
    ///            stays unlocked until it is shut, when it latches by itself (the pins are bevelled, like a door).
    ///   Latch01 = mean squeeze (what the rig draws the two handles at)
    ///
    /// UnlockAt 0.85 (E): near the end of the travel, so a half-hearted squeeze does not open it.
    /// </summary>
    public sealed class ExtraCanopyLatch : ICanopyLatch
    {
        public const double UnlockAt = 0.85, RelockBelow = 0.5, ShutBelow = 0.01;

        public CanopyLatchKind Kind => CanopyLatchKind.Squeeze;
        public double SqueezeLeft { get; private set; }
        public double SqueezeRight { get; private set; }
        public bool Locked { get; private set; } = true;
        public double Latch01 => 0.5 * (SqueezeLeft + SqueezeRight);

        public void Reset() { SqueezeLeft = SqueezeRight = 0.0; Locked = true; }

        /// <summary>One frame. Each squeeze is 0 when no hand holds that handle (it springs back). Returns true on
        /// the frame the pins come out (unlock click) or snap back in (lock click).</summary>
        public bool Step(double squeezeLeft, double squeezeRight, double canopyOpen01)
        {
            SqueezeLeft = Math.Clamp(squeezeLeft, 0.0, 1.0);
            SqueezeRight = Math.Clamp(squeezeRight, 0.0, 1.0);
            bool was = Locked;
            double both = Math.Min(SqueezeLeft, SqueezeRight);
            if (Locked && both >= UnlockAt) Locked = false;
            else if (!Locked && both < RelockBelow && canopyOpen01 < ShutBelow) Locked = true;
            return was != Locked;
        }
    }
}
