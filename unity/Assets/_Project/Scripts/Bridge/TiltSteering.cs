using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Flying by tilting the phone (Ty 2026-11-08, for a beta tester: "Aileron of roll rudder is yaw and elevator is
    /// pitch"). ONLY those three axes. The throttle, flaps, gear, spoiler and water rudder stay on the glass, where a
    /// tilt could not reach them anyway without turning every lever into a gesture.
    ///
    /// THE SECOND READER OF THE DEVICE'S MOTION. CockpitView was the first and left the rule for this one: each takes
    /// its OWN zero, "feeding the same attitude to both would make looking out of the left window also roll the
    /// aeroplane left". They also never both apply it — <see cref="CockpitView.DeviceLook"/> is false while this is on,
    /// so turning the phone flies the aeroplane and the view is looked around with the on-screen pad instead, exactly
    /// as it is when move-to-look is switched off.
    ///
    /// Roll and pitch are honest, absolute angles: gravity says which way is down. YAW IS NOT. Nothing can sense
    /// rotation about gravity from gravity, so yaw comes from the fused gyro, and it wanders over minutes however good
    /// the sensor. That is why the rudder is its own switch, why its deadzone is wider, and why CENTRE exists.
    /// </summary>
    internal static class TiltSteering
    {
        /// <summary>
        /// Only where the aeroplane is flown by tilting the thing you are holding. A gyroscope is not enough to say
        /// that: a Quest reports one and is strapped to your head, where "tilt to roll" would mean looking down rolls
        /// you inverted, and the headset already owns the pose. A Mac has none, so it excludes itself.
        /// </summary>
        public static bool Available => SystemInfo.supportsGyroscope
                                        && SystemInfo.deviceType == DeviceType.Handheld
                                        && !VrMode.HeadTracked;

        private const string KeyOn = "tilt.on", KeyRudder = "tilt.rudder", KeySens = "tilt.sens";
        private const string KeyInvA = "tilt.inv.a", KeyInvE = "tilt.inv.e", KeyInvR = "tilt.inv.r";

        private static int _on = -1, _rudder = -1, _invA = -1, _invE = -1, _invR = -1;
        private static float _sens = -1f;

        /// <summary>Tilt flies the aeroplane. Off by default: it takes the phone away from looking around.</summary>
        public static bool Enabled
        {
            get { if (_on < 0) _on = PlayerPrefs.GetInt(KeyOn, 0); return _on == 1 && Available; }
            set { _on = value ? 1 : 0; PlayerPrefs.SetInt(KeyOn, _on); PlayerPrefs.Save(); Centre(); }
        }

        /// <summary>Yaw drives the rudder as well. Separate because yaw is the axis that drifts.</summary>
        public static bool RudderFromYaw
        {
            get { if (_rudder < 0) _rudder = PlayerPrefs.GetInt(KeyRudder, 1); return _rudder == 1; }
            set { _rudder = value ? 1 : 0; PlayerPrefs.SetInt(KeyRudder, _rudder); PlayerPrefs.Save(); }
        }

        /// <summary>0 = least sensitive (a big turn of the phone for full deflection), 1 = most.</summary>
        public static float Sensitivity01
        {
            get { if (_sens < 0f) _sens = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySens, 0.5f)); return _sens; }
            set { _sens = Mathf.Clamp01(value); PlayerPrefs.SetFloat(KeySens, _sens); PlayerPrefs.Save(); }
        }

        // A phone's axes are not something this can be sure of from here, and a mirrored control is unflyable, so each
        // axis can be turned round from Options rather than from another build.
        public static bool InvertAileron { get { if (_invA < 0) _invA = PlayerPrefs.GetInt(KeyInvA, 0); return _invA == 1; } set { _invA = value ? 1 : 0; PlayerPrefs.SetInt(KeyInvA, _invA); PlayerPrefs.Save(); } }
        public static bool InvertElevator { get { if (_invE < 0) _invE = PlayerPrefs.GetInt(KeyInvE, 0); return _invE == 1; } set { _invE = value ? 1 : 0; PlayerPrefs.SetInt(KeyInvE, _invE); PlayerPrefs.Save(); } }
        public static bool InvertRudder { get { if (_invR < 0) _invR = PlayerPrefs.GetInt(KeyInvR, 0); return _invR == 1; } set { _invR = value ? 1 : 0; PlayerPrefs.SetInt(KeyInvR, _invR); PlayerPrefs.Save(); } }

        public static double FullScaleDeg => FlyingGame.Core.TiltControls.FullScaleForSensitivity(Sensitivity01);

        private static Quaternion _zero = Quaternion.identity;
        private static bool _haveZero;

        /// <summary>Hold the phone however you mean to hold it and call this: that attitude becomes the stick's centre.
        /// Taken again whenever the option is switched on, because the pilot has just picked the phone up.</summary>
        public static void Centre() => _haveZero = false;

        /// <summary>Live angles away from the centred hold, for the readout in Options. NaN when unavailable.</summary>
        public static Vector3 LastAnglesDeg { get; private set; }

        /// <summary>
        /// The three axes as stick commands, or false when tilt is not flying (switched off, no gyroscope, or the
        /// sensor has not produced a reading yet). Call once per frame: it also takes the zero on the first reading.
        /// </summary>
        public static bool TryAxes(out double aileron, out double elevator, out double rudder)
        {
            aileron = elevator = rudder = 0.0;
            if (!Enabled) { LastAnglesDeg = new Vector3(float.NaN, float.NaN, float.NaN); return false; }
            Input.gyro.enabled = true;   // this reader turns it on for itself: there may be no CockpitView at all
            if (!Input.gyro.enabled) return false;

            // CoreMotion's right-handed attitude into Unity's left-handed frame. The zero is taken in the SAME frame,
            // and everything below is relative to it, so a wrong handedness here can only mirror an axis — which is
            // what the invert switches are for — and cannot make the response nonlinear or unstable.
            Quaternion q = Input.gyro.attitude;
            Quaternion u = new Quaternion(q.x, q.y, -q.z, -q.w);
            if (!_haveZero) { _zero = u; _haveZero = true; }

            Quaternion rel = Quaternion.Inverse(_zero) * u;
            Vector3 fwd = rel * Vector3.forward, right = rel * Vector3.right;
            // Asin of how far each axis has tipped out of the plane it started in: no Euler order to get wrong, and
            // no wrap-around at 90°, because both are clamped before the asin.
            float pitchDeg = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float rollDeg = -Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yawDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            LastAnglesDeg = new Vector3(rollDeg, pitchDeg, yawDeg);

            double full = FullScaleDeg;
            aileron = FlyingGame.Core.TiltControls.Axis(rollDeg, full);
            elevator = FlyingGame.Core.TiltControls.Axis(pitchDeg, full);
            rudder = RudderFromYaw
                ? FlyingGame.Core.TiltControls.Axis(yawDeg, full, FlyingGame.Core.TiltControls.DefaultYawDeadzoneDeg)
                : 0.0;
            if (InvertAileron) aileron = -aileron;
            if (InvertElevator) elevator = -elevator;
            if (InvertRudder) rudder = -rudder;
            return true;
        }
    }
}
