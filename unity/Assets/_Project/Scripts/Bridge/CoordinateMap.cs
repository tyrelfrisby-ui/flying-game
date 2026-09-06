using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Sim ↔ Unity frame conversion. Sim world is NED-style right-handed (x north/forward, y east/right,
    /// z DOWN; altitude = -z). Unity is left-handed y-up (x right/east, y up, z forward/north).
    /// Mapping: unity = (sim.y, -sim.z, sim.x). Rotations are converted via basis vectors rather than
    /// quaternion algebra — immune to handedness sign mistakes.
    /// </summary>
    public static class CoordinateMap
    {
        public static Vector3 ToUnity(Vec3 sim) =>
            new((float)sim.Y, (float)-sim.Z, (float)sim.X);

        public static Quaternion ToUnity(Quat simAttitude)
        {
            Vector3 forward = ToUnity(simAttitude.Rotate(new Vec3(1, 0, 0)));
            Vector3 up = ToUnity(simAttitude.Rotate(new Vec3(0, 0, -1)));
            return Quaternion.LookRotation(forward, up);
        }
    }
}
