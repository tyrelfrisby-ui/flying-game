using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>
    /// WHERE THE HEADSET'S ORIGIN GOES IN AN OUTSIDE VIEW (Ty 2026-11-04, Quest 3S: "no views are available except
    /// 'outside' which is actually cockpit"). The desktop chase camera follows the flight path EXACTLY and turns with it;
    /// on a screen that is the point, in a headset it is the quickest way to make someone ill — every heading change of
    /// the aeroplane would turn the world round his head, and every swing of the path would throw him sideways.
    ///
    /// Frame: Unity world, +y up. The origin only ever has a HEADING (never pitch or roll: the horizon stays level), and:
    ///  - it moves WITH the aeroplane exactly (a steady translation is comfortable), while its OFFSET from it eases to the
    ///    camera's wanted offset with time constant <see cref="OffsetTauSec"/>, so a swinging flight path is a slow drift;
    ///  - its heading does not follow the camera's at all until they differ by more than <see cref="SnapDeg"/>, then it
    ///    SNAPS to it in one frame (a snap turn, the standard comfort turn in VR, not a smooth one);
    ///  - a world-fixed view (the fly-by) is not eased at all: the camera stands still in the world, and when it moves to
    ///    a new spot that is a teleport with the heading set toward the aeroplane;
    ///  - a new view, or the wanted position jumping by more than <see cref="JumpM"/>, starts afresh.
    /// The head moves freely on top (QuestRig); nothing here ever rotates it.
    /// </summary>
    public sealed class QuestOutsideView
    {
        public const double OffsetTauSec = 0.8;
        public const double SnapDeg = 45.0;
        public const double JumpM = 25.0;

        private bool _valid;
        private int _view = int.MinValue;
        private Vec3 _offset, _lastWant, _lastTarget;
        public Vec3 Position { get; private set; }
        /// <summary>Heading of the origin, degrees, Unity convention (0 = +z, 90 = +x).</summary>
        public double YawDeg { get; private set; }
        public int Snaps { get; private set; }

        public void Forget() { _valid = false; }

        /// <summary>One frame. <paramref name="want"/> is where the chase camera put itself and <paramref name="lookDir"/>
        /// where it looks; <paramref name="target"/> is the aeroplane (or the pilot it follows).</summary>
        public void Step(int view, bool worldFixed, Vec3 target, Vec3 want, Vec3 lookDir, double dt)
        {
            double wantYaw = YawOf(lookDir, YawDeg);
            bool jump = _valid && ((want - target) - (_lastWant - _lastTarget)).Length > JumpM && !worldFixed;
            bool moved = _valid && worldFixed && (want - _lastWant).Length > 0.5;   // the fly-by re-parked
            if (!_valid || view != _view || jump || moved)
            {
                _valid = true; _view = view;
                _offset = want - target;
                Position = want;
                YawDeg = wantYaw;
            }
            else if (worldFixed) Position = want;
            else
            {
                double k = dt > 0 ? 1.0 - Math.Exp(-dt / OffsetTauSec) : 0.0;
                _offset += ((want - target) - _offset) * k;
                Position = target + _offset;
                if (Math.Abs(Wrap(wantYaw - YawDeg)) > SnapDeg) { YawDeg = wantYaw; Snaps++; }
            }
            _lastWant = want; _lastTarget = target;
        }

        public static double YawOf(Vec3 d, double fallback)
        {
            double h = Math.Sqrt(d.X * d.X + d.Z * d.Z);
            return h < 1e-6 ? fallback : Math.Atan2(d.X, d.Z) * 180.0 / Math.PI;
        }

        public static double Wrap(double deg)
        {
            deg %= 360.0;
            if (deg > 180.0) deg -= 360.0;
            if (deg < -180.0) deg += 360.0;
            return deg;
        }
    }
}
