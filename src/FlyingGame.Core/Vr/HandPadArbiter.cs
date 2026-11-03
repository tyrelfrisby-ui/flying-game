using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// WHO FLIES: a tracked hand on the cockpit's own stick, or a thumbstick (Touch or a Bluetooth pad).
    ///
    /// A hand that holds the stick owns pitch and roll. A thumbstick takes them back the moment it is pushed past its
    /// dead zone (<see cref="QuestControlMapper.StickDeadZone"/>) — a pad left lying on a knee with its stick centred
    /// never fights the hand, a pilot who reaches for the pad does not have to let go first — and of course when the
    /// hand lets go. Rudder and throttle stay with the pad, except that a hand holding the throttle lever owns the
    /// throttle.
    /// </summary>
    public static class HandPadArbiter
    {
        /// <summary>True when the pad's flying stick is out of its dead zone (it wants pitch and roll).</summary>
        public static bool PadStickActive(double rightX, double rightY) =>
            Math.Abs(rightX) > QuestControlMapper.StickDeadZone || Math.Abs(rightY) > QuestControlMapper.StickDeadZone;

        /// <summary>Does the hand's stick drive pitch and roll this frame?</summary>
        public static bool HandOwnsStick(bool handHoldsStick, double padRightX, double padRightY) =>
            handHoldsStick && !PadStickActive(padRightX, padRightY);

        /// <summary>Does the hand's lever drive the throttle this frame? (The pad's throttle only moves it when the
        /// hand is not on the lever.)</summary>
        public static bool HandOwnsThrottle(bool handHoldsThrottle) => handHoldsThrottle;
    }
}
