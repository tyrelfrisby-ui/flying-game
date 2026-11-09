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
    /// on the rail would: the slide first (the Pitts: the whole track its jettison frees, past the stop tab), then the swing,
    /// at an arm's pace (E: 0.4 m/s along the track, 150°/s of swing), through CanopyDynamics' own bounded hand servo,
    /// until CanopyDynamics lets it go (past 10° unpinned and unlatched; the Pitts' hinge hooks past their release angle).
    /// In the air the Pitts' jettison is CanopyDynamics' own race between the slide and the swing (v2 stop tab).
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
            if (!Canopy.Held)
            {
                // HIS HAND IS ALWAYS ON IT. This used to be gated on q < DepartMinQ — below the dynamic pressure of 20 kt,
                // on the assumption that anything faster has air enough to take the canopy itself. Not for a canopy that
                // has to run aft on a track before it can swing: between 20 and about 45 kt the Pitts' slide-then-hinge
                // canopy slid its full 152 mm and then sat there, and the pilot - whose hand the gate had switched off -
                // could not get out at all (Ty 2026-11-08: "Bailing out while in an upright flat spin in the pitts doesn't
                // work. I press the button nothing happens"; the device log showed the canopy leaving 12.63 s later at
                // 7 KIAS, which is to say after the aeroplane had hit the ground, and him jumping at 6 m AGL as a ghost).
                // A flat spin sits squarely in that band. Nothing is weakened by always having a hand there: what his hand
                // can do is already capped at CanopyHinge.HandForceN, and when the airflow beats him the canopy is torn out
                // of it (CanopyEvents.TornFromHand) exactly as before - at 100 kt it still goes on the first step.
                //
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
                // As far aft as the track now lets it (the Pitts' jettison frees it past the stop tab), then over as far
                // as it has to go before its hinges let it go (the Pitts' hooks; anything else is free past 10°).
                double track = Canopy.SlideLimitM;
                double slide = Math.Min(track, _grabSlide + SlidePushMs * t);
                double slideSec = track > 0.0 ? Math.Max(0.0, track - _grabSlide) / SlidePushMs : 0.0;
                double to = Canopy.Spec.StopTab != null ? Canopy.Spec.StopTab.HookReleaseDeg + 10.0 : PushToDeg;
                double angle = Math.Min(to, _grabAngle + SwingPushDps * Math.Max(0.0, t - slideSec));
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
