using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// A native headset's head pose, turned into a pose RELATIVE TO THE SEAT'S AUTHORED EYE.
    ///
    /// The headset reports where the head is in its own tracking space (Unity's axes: +x right, +y up, +z ahead;
    /// "device" / eye-level origin, so the numbers are relative to wherever the runtime last centred). The game
    /// wants the head relative to the pilot's eye point in the aeroplane. Recentring stores the current head as
    /// "the pilot is sitting at the eye, facing the nose"; every later pose is measured from that.
    ///
    /// ONLY THE HEADING IS TAKEN OUT AT A RECENTRE, never pitch or roll. Recentring while glancing down at the
    /// panel must not tilt the whole world: the horizon stays where the headset's own gravity says it is, which
    /// is the one thing that keeps a seated pilot comfortable. (The same trap the Cardboard look fell into with a
    /// sensor-frame delta, CLAUDE.md "A delta in the sensor's own frame is not a measurement of the world": here
    /// the heading is resolved against the tracking space's own up, not the head's.)
    ///
    /// No gain, ever. A head that turns 30° turns the view 30°.
    ///
    /// TRANSLATION IS CLAMPED to a box around the eye (<see cref="Limits"/>), so leaning cannot put the pilot's
    /// head through the canopy, the panel or the side of the fuselage. The box is generous on purpose: inside it
    /// the world is exactly world-locked, and it only bites when the pilot leans much further than a strapped-in
    /// pilot can. Codex owns comfort; the softer alternative (fade to black past the box instead of stopping the
    /// head) is noted in the handoff.
    /// </summary>
    public sealed class XrHeadPose
    {
        /// <summary>How far the head may move from the authored eye, metres, in the SEAT's axes after recentring.
        /// Up is short because most of the fleet has a canopy or a top wing a hand's width above the pilot's head;
        /// aft is short because there is a headrest; sideways and forward allow a good look over the side and at
        /// the panel.</summary>
        public readonly struct Box
        {
            public readonly double Left, Right, Down, Up, Aft, Fore;
            public Box(double left, double right, double down, double up, double aft, double fore)
            { Left = left; Right = right; Down = down; Up = up; Aft = aft; Fore = fore; }
        }

        /// <summary>Default box: ±0.30 m sideways, 0.30 m down / 0.15 m up, 0.15 m aft / 0.30 m forward.</summary>
        public static readonly Box DefaultLimits = new(0.30, 0.30, 0.30, 0.15, 0.15, 0.30);

        public Box Limits = DefaultLimits;

        private bool _centred;
        private double _refYawRad;
        private Vec3 _refPos;

        public bool IsCentred => _centred;

        /// <summary>The heading taken out at the last recentre, degrees, right positive. For the log and tests.</summary>
        public double ReferenceYawDeg => _refYawRad * 180.0 / Math.PI;

        /// <summary>Make THIS head pose the eye point, facing the nose.</summary>
        public void Recentre(Vec3 hmdPos, Quat hmdRot)
        {
            _refYawRad = HeadingOf(hmdRot);
            _refPos = hmdPos;
            _centred = true;
        }

        public void Forget() { _centred = false; _refYawRad = 0; _refPos = Vec3.Zero; }

        /// <summary>The head relative to the eye point: position in metres (clamped to <see cref="Limits"/>) and
        /// orientation, both in the seat's axes (+x right, +y up, +z toward the nose). Before the first recentre the
        /// raw pose is passed through, unclamped in heading but clamped in position.</summary>
        public (Vec3 pos, Quat rot) Relative(Vec3 hmdPos, Quat hmdRot)
        {
            Quat unYaw = AboutUp(-_refYawRad);
            Vec3 d = unYaw.Rotate(hmdPos - _refPos);
            Quat r = Quat.Multiply(unYaw, hmdRot).Normalized();
            return (Clamp(d), r);
        }

        /// <summary>Any tracked point (a hand, a controller) in the seat's axes, UNCLAMPED: only the recentre
        /// position and heading come out. The box is for the head; a hand reaching for a lever may go anywhere.</summary>
        public Vec3 ToSeat(Vec3 trackingPos) => AboutUp(-_refYawRad).Rotate(trackingPos - _refPos);

        /// <summary>Any tracked orientation in the seat's axes.</summary>
        public Quat ToSeat(Quat trackingRot) => Quat.Multiply(AboutUp(-_refYawRad), trackingRot).Normalized();

        /// <summary>True when the head is outside the box and is being held at its edge. Codex's fade hook.</summary>
        public bool Clamped(Vec3 hmdPos)
        {
            Vec3 d = AboutUp(-_refYawRad).Rotate(hmdPos - _refPos);
            Vec3 c = Clamp(d);
            return (d - c).LengthSquared > 1e-12;
        }

        private Vec3 Clamp(Vec3 d) => new(
            Math.Clamp(d.X, -Limits.Left, Limits.Right),
            Math.Clamp(d.Y, -Limits.Down, Limits.Up),
            Math.Clamp(d.Z, -Limits.Aft, Limits.Fore));

        /// <summary>Rotation about the tracking space's up (+y), right positive for a positive angle — Unity's own
        /// convention for a yaw.</summary>
        public static Quat AboutUp(double rad) => new(0, Math.Sin(rad / 2), 0, Math.Cos(rad / 2));

        /// <summary>
        /// Where the head is FACING, as a heading about world up, radians, right positive. Taken from the
        /// horizontal part of the head's forward vector. Looking straight up or down there is no horizontal
        /// forward, so the head's own up vector stands in (looking down, the top of the head points ahead;
        /// looking up, it points behind) — the heading never comes back NaN.
        /// </summary>
        public static double HeadingOf(Quat q)
        {
            Vec3 f = q.Rotate(new Vec3(0, 0, 1));
            double hx = f.X, hz = f.Z;
            if (hx * hx + hz * hz < 0.01)
            {
                Vec3 u = q.Rotate(new Vec3(0, 1, 0));
                double s = f.Y < 0 ? 1.0 : -1.0;
                hx = u.X * s; hz = u.Z * s;
            }
            return Math.Atan2(hx, hz);
        }
    }
}
