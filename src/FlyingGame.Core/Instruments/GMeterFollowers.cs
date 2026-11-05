using System;

namespace FlyingGame.Core.Instruments
{
    /// <summary>
    /// THE ACCELEROMETER'S TWO FOLLOWERS (Ty 2026-11-05: "Every g-meter records max/min with a push-to-reset button").
    /// The main pointer reads the load factor now; the max follower is pushed up by it and stays at the highest value
    /// seen, the min follower is pushed down and stays at the lowest. Pushing the reset knob lets both spring back to the
    /// main pointer — on the ground, or in level flight, that is 1 g. They start there too (a freshly reset instrument).
    /// Pointer and followers stop at the dial's ends.
    /// </summary>
    public sealed class GMeterFollowers
    {
        public readonly double DialMinG, DialMaxG;
        public double Max { get; private set; } = 1.0;
        public double Min { get; private set; } = 1.0;

        public GMeterFollowers(double dialMinG = -10.0, double dialMaxG = 10.0) { DialMinG = dialMinG; DialMaxG = dialMaxG; }

        /// <summary>One sample of the load factor the instrument feels.</summary>
        public void Step(double g)
        {
            if (double.IsNaN(g)) return;
            double p = Math.Clamp(g, DialMinG, DialMaxG);
            if (p > Max) Max = p;
            if (p < Min) Min = p;
        }

        /// <summary>The push-to-reset knob: both followers back to the main pointer.</summary>
        public void Reset(double gNow)
        {
            double p = double.IsNaN(gNow) ? 1.0 : Math.Clamp(gNow, DialMinG, DialMaxG);
            Max = Min = p;
        }
    }
}
