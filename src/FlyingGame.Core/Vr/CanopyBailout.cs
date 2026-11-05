using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE BAIL-OUT WORKS THE REAL CANOPY (Ty 2026-11-05 06:38: "canopy movement/latch/hinge/shatter physics ALREADY
    /// EXIST … bailout must REUSE that existing real-canopy system"). Nothing here moves the canopy: the pilot does what
    /// the headset's hands do — the latch open, the jettison pulled (<see cref="CanopyDynamics.ReleaseHingePins"/>) — and
    /// <see cref="CanopyDynamics.Step"/> decides when it goes. In the air (q above that of 20 kt) that is the first step.
    /// On the ground (a wreck at rest) nothing pulls it off, so his hand shoves the free edge up and over the way a hand
    /// on the rail would: the Pitts' slide first, then the swing, at an arm's pace (E: 0.4 m/s along the track, 150°/s
    /// of swing), through CanopyDynamics' own bounded hand servo. Past 10° unpinned and unlatched it is free and departs.
    ///
    /// Cockpit frame as CanopyDynamics: +x right, +y up, +z nose; the hinge line sits <see cref="CanopySpec.HingeOutboardM"/>
    /// right of the canopy's centre-line, so with the centre-line at x = 0 the hinge is at (d, 0, 0).
    /// </summary>
    public sealed class CanopyBailout
    {
        public const double SlidePushMs = 0.4, SwingPushDps = 150.0, PushToDeg = 45.0;
        public readonly CanopyDynamics Canopy;
        public double TimeSec { get; private set; }
        private bool _grabbed;
        private double _grabT, _grabZ, _grabSlide, _grabAngle;

        /// <summary>The latch has been opened and the jettison pulled: the hinge pins are out from here on.</summary>
        public CanopyBailout(CanopyDynamics canopy)
        {
            Canopy = canopy ?? throw new ArgumentNullException(nameof(canopy));
            Canopy.ReleaseHingePins();
        }

        public bool Departed => Canopy.Departed;

        /// <summary>One step: <paramref name="q"/> the dynamic pressure, Pa; <paramref name="gApp"/> apparent gravity in the
        /// cockpit frame (level, 1 g: (0, −9.81, 0)). The latch is open for good.</summary>
        public CanopyEvents Step(double dt, double q, Vec3 gApp)
        {
            if (dt <= 0.0 || Canopy.Departed) return CanopyEvents.None;
            TimeSec += dt;
            Vec3 hinge = Hinge();
            if (!Canopy.Held && q < CanopyDynamics.DepartMinQ)
            {
                // His hand on the free edge, where it is now (again, if the canopy got away from it).
                _grabbed = true; _grabT = TimeSec;
                Vec3 edge = Edge(Canopy.AngleDeg, Canopy.SlideM);
                _grabZ = edge.Z; _grabSlide = Canopy.SlideM; _grabAngle = Canopy.AngleDeg;
                Canopy.Grab(edge, hinge);
            }
            Vec3 hand = Vec3.Zero;
            if (_grabbed)
            {
                double t = TimeSec - _grabT;
                double slide = Math.Min(Canopy.Spec.SlideM, _grabSlide + SlidePushMs * t);
                double slideSec = Canopy.Spec.SlideM > 0.0 ? Math.Max(0.0, Canopy.Spec.SlideM - _grabSlide) / SlidePushMs : 0.0;
                double angle = Math.Min(PushToDeg, _grabAngle + SwingPushDps * Math.Max(0.0, t - slideSec));
                Vec3 at = Edge(angle, slide);
                hand = new Vec3(at.X, at.Y, _grabZ - (slide - _grabSlide));
            }
            return Canopy.Step(dt, q, gApp, false, hand, hinge);
        }

        /// <summary>The hinge where it is now (slid aft with the Pitts' canopy).</summary>
        private Vec3 Hinge() => new(Canopy.Spec.HingeOutboardM, 0.0, -Canopy.SlideM);

        /// <summary>The free edge, opened to <paramref name="angleDeg"/> and slid <paramref name="slideM"/> aft.</summary>
        private Vec3 Edge(double angleDeg, double slideM)
        {
            Vec3 u = CanopyDynamics.Inboard(angleDeg) * Canopy.Spec.FreeEdgeM;
            return new Vec3(Canopy.Spec.HingeOutboardM + u.X, u.Y, -slideM);
        }
    }
}
