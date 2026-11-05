using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// A slave strut between two hinged surfaces: the Great Lakes' Strut_Slave_L/R, pinned to a horn on the upper
/// aileron (Strut_Slave_&lt;s&gt;_PinUpper) and one on the lower (_PinLower). The pins ride on their ailerons, so where
/// the two ends are is already known once the rig has moved the surfaces; the strut itself has to be put back
/// between them: carried with the upper pin and turned, by the shortest arc, until its long axis points at the
/// lower pin again (tools/own_models/gl_aileronpose.py does the same in Blender).
///
/// Engine-free so it is tested (tools/FlightTests/SlaveStrutTests.cs) and the Unity follower in AirframeRig is a
/// thin call: the maths here is pure algebra on vectors and Hamilton quaternions, so it gives the same answer in
/// glTF's right-handed frame and in Unity's left-handed one, as long as both ends are expressed in the same frame.
/// </summary>
public static class SlaveStrut
{
    /// <summary>Where the strut goes: the rotation to apply on top of its rest rotation (parent frame, applied
    /// on the left: <c>rot = Turn * restRot</c>) and its new origin.</summary>
    public readonly struct Pose
    {
        public readonly Quat Turn;
        public readonly Vec3 Origin;
        /// <summary>Change in pin-to-pin distance from rest, metres (the upper aileron's solved table keeps it near 0).</summary>
        public readonly double LengthError;
        public Pose(Quat turn, Vec3 origin, double lengthError) { Turn = turn; Origin = origin; LengthError = lengthError; }
    }

    /// <summary>The strut's pose for the pins at <paramref name="upper"/> / <paramref name="lower"/>, given where the
    /// pins and the strut's origin were at rest. All points in the strut's PARENT frame.</summary>
    public static Pose Solve(Vec3 restUpper, Vec3 restLower, Vec3 restOrigin, Vec3 upper, Vec3 lower)
    {
        Vec3 a = restLower - restUpper, b = lower - upper;
        Quat q = FromTo(a, b);
        // The origin sits on the upper pin in our GLB; carry any offset from it with the turn, so a model whose
        // strut origin is elsewhere still lands with its pin end on the pin.
        Vec3 origin = upper + q.Rotate(restOrigin - restUpper);
        return new Pose(q, origin, b.Length - a.Length);
    }

    /// <summary>The shortest-arc rotation taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static Quat FromTo(Vec3 from, Vec3 to)
    {
        double la = from.Length, lb = to.Length;
        if (la < 1e-12 || lb < 1e-12) return Quat.Identity;
        Vec3 u = from / la, v = to / lb;
        double d = u.Dot(v);
        if (d < -0.999999)
        {
            // Opposite: any axis across `from` will do.
            Vec3 ax = Vec3.Cross(u, new Vec3(1, 0, 0));
            if (ax.LengthSquared < 1e-8) ax = Vec3.Cross(u, new Vec3(0, 1, 0));
            ax = ax / ax.Length;
            return new Quat(ax.X, ax.Y, ax.Z, 0);
        }
        Vec3 c = Vec3.Cross(u, v);
        return new Quat(c.X, c.Y, c.Z, 1 + d).Normalized();
    }

    /// <summary>Rotate <paramref name="p"/> by <paramref name="deg"/> about the line through <paramref name="pivot"/>
    /// along <paramref name="axis"/>, right-hand rule (a rig row's "+deg about axis_gltf" in the glTF frame).</summary>
    public static Vec3 AboutHinge(Vec3 p, Vec3 pivot, Vec3 axis, double deg)
    {
        Vec3 u = axis / axis.Length;
        double h = 0.5 * deg * System.Math.PI / 180.0, s = System.Math.Sin(h);
        var q = new Quat(u.X * s, u.Y * s, u.Z * s, System.Math.Cos(h));
        return pivot + q.Rotate(p - pivot);
    }
}
