using System;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE PITTS' BLUE PROP LEVER (Ty 2026-11-04 19:27): "it turns from down to horizontal to unlock it and then slides
    /// forward/aft to control rpm then rotates back down again." Two motions on one handle: the blade's turn about its
    /// spindle (<see cref="Lock01"/>: 0 hanging DOWN, locked .. 1 HORIZONTAL, released) and the slide along its slot
    /// (<see cref="Slide01"/>: 0 aft, full coarse .. 1 forward, full fine), which is the prop lever the sim reads
    /// (Aircraft.PropLever). The slide moves only while the blade is released, so the rpm cannot creep with the lever
    /// down. Engine-free; the hand code feeds it both motions every frame and draws what it says.
    /// </summary>
    public sealed class PropLockLever
    {
        /// <summary>The blade counts as released this close to horizontal: the slot's teeth clear at about 80 deg of 90.</summary>
        public const double ReleasedAt = 0.85;

        public double Lock01 { get; private set; }
        public double Slide01 { get; private set; }
        public bool Released => Lock01 >= ReleasedAt;

        public PropLockLever(double slide01 = 1.0, double lock01 = 0.0)
        {
            Slide01 = Math.Clamp(slide01, 0.0, 1.0);
            Lock01 = Math.Clamp(lock01, 0.0, 1.0);
        }

        /// <summary>The hand's turn of the blade and its slide along the slot this frame; returns true if the slide moved.
        /// The turn always follows; the slide follows only once the blade is released (checked after this frame's turn,
        /// so turning up and pulling back in one motion works).</summary>
        public bool Move(double lock01, double slide01)
        {
            Lock01 = Math.Clamp(lock01, 0.0, 1.0);
            if (!Released) return false;
            double s = Math.Clamp(slide01, 0.0, 1.0);
            bool moved = Math.Abs(s - Slide01) > 1e-12;
            Slide01 = s;
            return moved;
        }

        /// <summary>The sim's own lever moved (the other seat's, or a new flight): follow it, whatever the blade.</summary>
        public void SetFromSim(double slide01) => Slide01 = Math.Clamp(slide01, 0.0, 1.0);
    }
}
