using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// THE PILOT'S BODY, as joint-to-joint numbers for the IK: a 50th-percentile adult male in a flight suit.
    ///
    /// Source: ANSUR II (US Army anthropometric survey, 2012), male means, rounded to the centimetre, then turned
    /// from SKIN landmarks into JOINT CENTRES with the usual offsets (shoulder joint ~4 cm below and inboard of the
    /// acromion; knee and hip centres ~5 cm and ~15 cm in from the knee front and buttock):
    ///
    ///   eye height, sitting 0.79      acromial height, sitting 0.60   → shoulder joint 0.23 below the eye
    ///   biacromial breadth 0.42                                        → shoulder joints ±0.17
    ///   acromion–radiale 0.34                                          → upper arm 0.31
    ///   radiale–stylion 0.26                                           → forearm 0.26
    ///   hand length 0.19                                               → wrist to the centre of a closed fist 0.08
    ///   buttock–knee 0.62                                              → thigh 0.42
    ///   knee height, sitting 0.56                                      → shin (knee to ankle) 0.45
    ///   foot length 0.27                                               → ankle to ball of the foot 0.15 fwd, 0.08 down
    ///   hip joints ±0.09, 0.70 below the eye (eye 0.79 above the pan, joint 0.09 above it)
    ///
    /// The fore-aft placements (shoulders 0.08 behind the eye, hips 0.05 behind it) are estimates (E) for an upright
    /// aerobatic seat; a reclined seat moves the hips forward. Everything is relative to the seat's AUTHORED eye, so
    /// it sits right in every type that has one, and a taller or shorter pilot is one scale away.
    /// When Codex's pilot-body-own.glb lands, its own bone lengths replace these (the IK reads whichever is loaded).
    /// </summary>
    public static class SeatedPilot
    {
        public const double UpperArmM = 0.31, ForearmM = 0.26, WristToGripM = 0.08;
        public const double ThighM = 0.42, ShinM = 0.45;
        public const double AnkleToBallFwdM = 0.15, AnkleToBallDownM = 0.08;

        /// <summary>Joint centres relative to the seat's eye, cockpit frame (+x right, +y up, +z nose).</summary>
        public static readonly Vec3 ShoulderL = new(-0.17, -0.23, -0.08), ShoulderR = new(0.17, -0.23, -0.08);
        public static readonly Vec3 HipL = new(-0.09, -0.70, -0.05), HipR = new(0.09, -0.70, -0.05);

        /// <summary>Which way the elbows and knees bend, as pole POINTS relative to the eye: elbows out, down and back
        /// (a pilot's arms hang from the shoulder and bend outboard); knees up and forward.</summary>
        public static readonly Vec3 ElbowPoleL = new(-0.45, -0.70, -0.20), ElbowPoleR = new(0.45, -0.70, -0.20);
        public static readonly Vec3 KneePoleL = new(-0.15, 0.10, 0.60), KneePoleR = new(0.15, 0.10, 0.60);
    }

    /// <summary>
    /// WHERE THE CONTROLS ARE when the model does not say. CONTRACT.md: bind to Codex's named nodes (Grip_Stick_Rear,
    /// Pedal_L_Rear, Canopy_Latch_Knob …) and, where one is missing at runtime, fall back to an authored proxy and log
    /// it once. These are the proxies, as offsets from the seat's AUTHORED EYE in the cockpit frame, so they land in
    /// the right place in either seat of any type. All of them are estimates (E) of a typical tandem aerobatic
    /// cockpit, placed for a pilot of <see cref="SeatedPilot"/>'s size; the model's nodes, when present, win.
    /// </summary>
    public readonly struct CockpitProxy
    {
        public readonly Vec3 StickPivot;
        public readonly double StickLengthM, StickRollMaxDeg, StickAftMaxDeg, StickFwdMaxDeg;
        public readonly Vec3 ThrottlePivot;
        public readonly double ThrottleArmM, ThrottleIdleDeg, ThrottleSweepDeg;
        /// <summary>Ball of each foot on its pedal with the rudder centred, and how far a pedal moves fore-aft at full
        /// rudder (+1 = right pedal forward, left pedal aft).</summary>
        public readonly Vec3 FootL, FootR;
        public readonly double PedalTravelM;
        public readonly CanopyLatchKind Latch;
        /// <summary>The latch's grab point at its LOCKED position (Pitts: the ball fully right; Extra: between the
        /// two handles). For the Pitts it is relative to the REAR seat's eye: the ball is on the rear panel.</summary>
        public readonly Vec3 LatchLocked;

        public CockpitProxy(Vec3 stickPivot, double stickLengthM, double rollMaxDeg, double aftMaxDeg, double fwdMaxDeg,
                            Vec3 throttlePivot, double throttleArmM, double throttleIdleDeg, double throttleSweepDeg,
                            Vec3 footL, Vec3 footR, double pedalTravelM, CanopyLatchKind latch, Vec3 latchLocked)
        {
            StickPivot = stickPivot; StickLengthM = stickLengthM;
            StickRollMaxDeg = rollMaxDeg; StickAftMaxDeg = aftMaxDeg; StickFwdMaxDeg = fwdMaxDeg;
            ThrottlePivot = throttlePivot; ThrottleArmM = throttleArmM; ThrottleIdleDeg = throttleIdleDeg; ThrottleSweepDeg = throttleSweepDeg;
            FootL = footL; FootR = footR; PedalTravelM = pedalTravelM;
            Latch = latch; LatchLocked = latchLocked;
        }

        /// <summary>
        /// The proxy for a type. Stick: grip 0.50 below and 0.42 ahead of the eye on a 0.55 m stick; travel ±18° roll,
        /// 18° aft / 12° forward — the Extra's and the Great Lakes' rig numbers (themselves E), used for every type
        /// until its own rig says otherwise. Throttle on the LEFT (Pitts, Extra, Great Lakes, Cub all have it there),
        /// a 0.20 m lever 20° aft of vertical at idle sweeping 50° forward. Pedals ±0.07 m (E).
        /// </summary>
        public static CockpitProxy For(string aircraftId)
        {
            string id = aircraftId ?? "";
            CanopyLatchKind latch = LatchFor(id);
            // Pitts: the ball on top of the rear panel, centre-line, fully right = locked (+x = right).
            // Extra: the pair of handles on the left canopy frame ahead of the pilot, where the rig's red lock
            // levers are (E: CONTRACT.md does not place Canopy_Latch_Handle_L/R yet).
            Vec3 latchAt = latch == CanopyLatchKind.SlideBall
                ? new Vec3(PittsCanopyLatch.TravelM * 0.5, -0.15, 0.55)
                : new Vec3(-0.20, -0.10, 0.35);
            return new CockpitProxy(
                stickPivot: new Vec3(0.0, -1.05, 0.42), stickLengthM: 0.55, rollMaxDeg: 18.0, aftMaxDeg: 18.0, fwdMaxDeg: 12.0,
                throttlePivot: new Vec3(-0.26, -0.58, 0.20), throttleArmM: 0.20, throttleIdleDeg: -20.0, throttleSweepDeg: 50.0,
                footL: new Vec3(-0.13, -0.85, 0.85), footR: new Vec3(0.13, -0.85, 0.85), pedalTravelM: 0.07,
                latch: latch, latchLocked: latchAt);
        }

        /// <summary>Which latch a type has. Only the two Ty described are built in Phase 1; see docs/vr-cockpit.md for
        /// the rest of the fleet (the Great Lakes is open, the Cub and 172 have doors, the 2-33 is a question).</summary>
        public static CanopyLatchKind LatchFor(string aircraftId)
        {
            string id = aircraftId ?? "";
            if (id.StartsWith("pitts")) return CanopyLatchKind.SlideBall;
            if (id.StartsWith("extra")) return CanopyLatchKind.Squeeze;
            return CanopyLatchKind.None;
        }
    }
}
