using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE BAIL-OUT WORKS THE REAL CANOPY (Ty 2026-11-05 06:38: "canopy movement/latch/hinge/shatter physics ALREADY
    /// EXIST … bailout must REUSE that existing real-canopy system"). Nothing here moves the canopy: the pilot does what
    /// the headset's hands do — the latch open, and the jettison pulled IF there is one and it is worth pulling — and
    /// <see cref="CanopyDynamics.Step"/> decides the rest. His hand then shoves the free edge up and over the way a hand
    /// on the rail would: the slide first (the Pitts: the whole track its jettison frees, past the stop tab), then the
    /// swing, at an arm's pace (E: 0.4 m/s along the track, 150°/s of swing), through CanopyDynamics' own bounded hand
    /// servo. The air can take it out of his hand at any point (<see cref="CanopyEvents.TornFromHand"/>) and that is
    /// fine — it is what the air does.
    ///
    /// TWO THINGS DECIDE WHETHER ANYTHING LEAVES THE AEROPLANE, and until 2026-11-11 neither was asked (this
    /// constructor pulled the hinge pins of every canopy in the fleet, always):
    ///
    ///  • HAS THE TYPE A JETTISON AT ALL? It is a fitting, authored per type in the config
    ///    (<see cref="CanopySpec.HasJettison"/>). Ty, 2026-11-11: *"The extra has no jettison feature."* Bailing out of
    ///    a type without one OPENS the canopy and leaves it on its hinges: on the ground it stands there at its stop
    ///    and he climbs out past it; in the air it is flung to the stop and flails there, and if the airload at the
    ///    stop beats <see cref="CanopySpec.FittingStrengthN"/> the hinge fittings fail and it departs
    ///    (<see cref="CanopyDeparture.HingesFailed"/>) — torn off, which is what happens in life, and not jettisoned.
    ///    The Extra's fittings are calibrated to let go at 80 KIAS, so at any speed you would really be leaving at,
    ///    the canopy goes anyway; it just goes because the air took it.
    ///  • IS THE AEROPLANE ON THE GROUND? Then nothing is jettisoned whatever the type has. Ty, same message: *"on the
    ///    ground it should not fall off it should stay open."* Nobody pulls a jettison to climb out of a stopped
    ///    aeroplane, and this is the whole of his bug: a canopy let go on the ground had nowhere to go but straight
    ///    down onto the turtledeck, which counted as a strike, which broke the glass on an aeroplane that was
    ///    "intact and upright".
    ///
    /// Cockpit frame as CanopyDynamics: +x right, +y up, +z nose; the hinge line sits <see cref="CanopySpec.HingeOutboardM"/>
    /// right of the canopy's centre-line, so with the centre-line at x = 0 the hinge is at (d, 0, 0).
    /// </summary>
    public sealed class CanopyBailout
    {
        public const double SlidePushMs = 0.4, SwingPushDps = 150.0, PushToDeg = 45.0;
        public readonly CanopyDynamics Canopy;
        /// <summary>The jettison was pulled as he got out: the type has one AND the aeroplane was flying. False means
        /// the canopy is on its hinges and only the air can take it.</summary>
        public readonly bool JettisonPulled;
        public double TimeSec { get; private set; }
        private bool _grabbed, _handsOff;
        private double _grabT, _grabZ, _grabSlide, _grabAngle;

        /// <summary>
        /// The latch is open from here on, and the jettison is pulled if the type has one
        /// (<see cref="CanopySpec.HasJettison"/>) and <paramref name="onGround"/> is false. On the ground nothing is
        /// pulled: the canopy opens and stays on the aeroplane.
        /// </summary>
        public CanopyBailout(CanopyDynamics canopy, bool onGround)
        {
            Canopy = canopy ?? throw new ArgumentNullException(nameof(canopy));
            JettisonPulled = canopy.Spec.HasJettison && !onGround;
            if (JettisonPulled) Canopy.ReleaseHingePins();
        }

        public bool Departed => Canopy.Departed;

        /// <summary>He can climb out past it: it has gone, or it is open far enough to go by
        /// (<see cref="CanopyDynamics.ClearToLeave"/>). Not "it has departed" — a canopy that cannot be jettisoned
        /// never departs on the ground, and he still gets out.</summary>
        public bool ClearToLeave => Canopy.ClearToLeave;

        /// <summary>
        /// HE HAS GONE OVER THE SIDE: there is no hand on the canopy any more, and every step from here is the
        /// airload's alone. It matters now that a canopy can stay on the aeroplane — before 2026-11-11 the bail-out's
        /// canopy always departed, which ended the stepping by itself; a canopy that never departs would otherwise go
        /// on being shoved by a pilot who is a hundred feet behind it.
        /// </summary>
        public void HandsOff()
        {
            _handsOff = true;
            _grabbed = false;
            if (Canopy.Held) Canopy.Release();
        }

        /// <summary>His hand is off it for good (<see cref="HandsOff"/>).</summary>
        public bool Unhanded => _handsOff;

        /// <summary>One step: <paramref name="q"/> the dynamic pressure, Pa; <paramref name="gApp"/> apparent gravity in the
        /// cockpit frame (level, 1 g: (0, −9.81, 0)). The latch is open for good.</summary>
        public CanopyEvents Step(double dt, double q, Vec3 gApp)
        {
            if (dt <= 0.0 || Canopy.Departed) return CanopyEvents.None;
            TimeSec += dt;
            Vec3 hinge = Hinge();
            if (!Canopy.Held && !_handsOff)
            {
                // HIS HAND IS ALWAYS ON IT, until he lets go of the aeroplane altogether (HandsOff). This used to be gated on q < DepartMinQ — below the dynamic pressure of 20 kt,
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
            if (_grabbed && !_handsOff)
            {
                double t = TimeSec - _grabT;
                // As far aft as the track now lets it (the Pitts' jettison frees it past the stop tab), then over as far
                // as he means to push it (PushTargetDeg).
                double track = Canopy.SlideLimitM;
                double slide = Math.Min(track, _grabSlide + SlidePushMs * t);
                double slideSec = track > 0.0 ? Math.Max(0.0, track - _grabSlide) / SlidePushMs : 0.0;
                double angle = Math.Min(PushTargetDeg, _grabAngle + SwingPushDps * Math.Max(0.0, t - slideSec));
                Vec3 at = Edge(angle, slide);
                hand = new Vec3(at.X, at.Y, _grabZ - (slide - _grabSlide));
            }
            return Canopy.Step(dt, q, gApp, false, hand, hinge);
        }

        /// <summary>
        /// HOW FAR HIS HAND MEANS TO SHOVE IT, degrees.
        ///
        ///  • jettison pulled, Pitts: past where the hinge hooks come off their pins — he is getting rid of it;
        ///  • jettison pulled, plain hinge: <see cref="PushToDeg"/> is enough, because an unpinned, unlatched canopy
        ///    is gone the moment it is off the sill (10°) and the rest of the push never happens;
        ///  • NO JETTISON (the Extra, and most of the fleet): all the way open, because the canopy is staying on the
        ///    aeroplane and he has to climb out past it. The Pitts' frame comes down on its stop tab
        ///    (<see cref="CanopyStopTab.RestAngleDeg"/>, about 100° at the normal 4 in of slide); anything else goes
        ///    to its hinge stop. Both are well past <see cref="CanopyDynamics.ClearToLeaveDeg"/>, which is the point.
        /// </summary>
        public double PushTargetDeg
        {
            get
            {
                CanopyStopTab? tab = Canopy.Spec.StopTab;
                if (JettisonPulled) return tab != null ? tab.HookReleaseDeg + 10.0 : PushToDeg;
                if (tab == null) return Canopy.Spec.StopDeg;
                double rest = tab.RestAngleDeg(Canopy.Spec.SlideM);
                return double.IsNaN(rest) ? Canopy.Spec.StopDeg : rest;
            }
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
