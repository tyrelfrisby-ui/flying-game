using System.Collections.Generic;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.Aero.Wake;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using UnityEngine;

namespace Aero.Wake
{
    /// <summary>One straight piece of a trailing vortex line, Unity world frame. <see cref="gamma"/> is signed for
    /// Unity's left-handed axes: Biot–Savart written with <c>Vector3.Cross</c> and this gamma gives the right velocity
    /// (it is −Γ of the sim's right-handed frame).</summary>
    public struct WakeSegment { public Vector3 a, b; public float gamma, coreRadius, age; }

    /// <summary>
    /// The wake query API of the clouds + wake contract (docs/WAKE-VORTEX.md), on top of the shared physics in
    /// <see cref="VortexWake"/>: Unity's frame in, Unity's frame out. Also the host: <see cref="Tick"/> (called by
    /// <see cref="FlightSimDriver"/> once a frame) registers the player's aeroplane and advances every wake on the sim's
    /// clock. iOS, the Mac and the Quest run exactly this.
    /// </summary>
    public static class WakeField
    {
        /// <summary>Off: nothing sheds, nothing is felt, the visuals see still air.</summary>
        public static bool Enabled = true;

        private static int _tickFrame = -1;
        private static readonly List<VortexSegment> Segs = new();
        private static readonly List<JetSegment> Jets = new();

        /// <summary>Once a frame (later calls in the same frame do nothing): age and move every wake by the frame's
        /// sim time. Every <see cref="FlightSimDriver"/> calls it after <see cref="Ensure"/>-ing its own aeroplane.</summary>
        public static void Tick(float dt)
        {
            if (Time.frameCount == _tickFrame) return;
            _tickFrame = Time.frameCount;
            ReplayActor.ShedsWakes = Enabled;
            if (!Enabled) { if (VortexWake.Active) VortexWake.Clear(); return; }
            if (dt > 0f) VortexWake.Advance(dt);
        }

        /// <summary>Give a simulated aeroplane a wake (once). Its own steps shed into it from then on.</summary>
        public static void Ensure(Aircraft aircraft, string label)
        {
            if (!Enabled || aircraft == null || aircraft.Wake != null) return;
            aircraft.Wake = VortexWake.CreateSource(label, AeroModel.WingSpan(aircraft.Config));
        }

        /// <summary>A wake for something that is only a pose (a network peer). Feed it with <see cref="ShedPose"/>.</summary>
        public static WakeSource CreatePoseSource(string label, double spanM) =>
            Enabled ? VortexWake.CreateSource(label, spanM) : null;

        /// <summary>Shed from a pose seen frame by frame: the lift is what the acceleration says (m (a − g)).</summary>
        public static void ShedPose(WakeSource src, float dt, Vector3 positionUnity, Quaternion rotationUnity, Vector3 velocityUnity, double massKg)
        {
            if (src == null || !Enabled || dt <= 0f) return;
            Vec3 p = CoordinateMap.ToSim(positionUnity);
            Vec3 f = CoordinateMap.ToSim(rotationUnity * Vector3.forward), u = CoordinateMap.ToSim(rotationUnity * Vector3.up);
            Quat q = FromAxes(f, u);
            src.ShedKinematic(dt, p, q, CoordinateMap.ToSim(velocityUnity), Atmosphere.MeanWindAtPosition(p), massKg, Atmosphere.DensityAt(p));
        }

        /// <summary>Body x forward, body z down (NED) from the forward and up directions.</summary>
        private static Quat FromAxes(Vec3 fwd, Vec3 up)
        {
            Vec3 x = fwd.Normalized(), z = (-up).Normalized(), y = Vec3.Cross(z, x);
            // Rotation matrix columns x, y, z → quaternion.
            double m00 = x.X, m01 = y.X, m02 = z.X, m10 = x.Y, m11 = y.Y, m12 = z.Y, m20 = x.Z, m21 = y.Z, m22 = z.Z;
            double tr = m00 + m11 + m22, qw, qx, qy, qz;
            if (tr > 0) { double s = System.Math.Sqrt(tr + 1.0) * 2; qw = 0.25 * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s; }
            else if (m00 > m11 && m00 > m22) { double s = System.Math.Sqrt(1.0 + m00 - m11 - m22) * 2; qw = (m21 - m12) / s; qx = 0.25 * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s; }
            else if (m11 > m22) { double s = System.Math.Sqrt(1.0 + m11 - m00 - m22) * 2; qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25 * s; qz = (m12 + m21) / s; }
            else { double s = System.Math.Sqrt(1.0 + m22 - m00 - m11) * 2; qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25 * s; }
            return new Quat(qx, qy, qz, qw);
        }

        // ------------------------------------------------------------------ the contract's queries

        /// <summary>The wake's air velocity at a Unity world point (m/s, Unity axes).</summary>
        public static Vector3 SampleVelocity(Vector3 worldPos) =>
            VortexWake.Active ? CoordinateMap.ToUnity(VortexWake.SampleVelocity(CoordinateMap.ToSim(worldPos))) : Vector3.zero;

        /// <summary>|ω| at a Unity world point (1/s).</summary>
        public static float SampleVorticityMagnitude(Vector3 worldPos) =>
            VortexWake.Active ? (float)VortexWake.SampleVorticity(CoordinateMap.ToSim(worldPos)).Length : 0f;

        /// <summary>Every live segment, Unity frame (cleared first). Returns the count.</summary>
        public static int GetActiveSegments(List<WakeSegment> output)
        {
            output.Clear();
            if (!VortexWake.Active) return 0;
            VortexWake.GetActiveSegments(Segs);
            foreach (VortexSegment s in Segs)
                output.Add(new WakeSegment
                {
                    a = CoordinateMap.ToUnity(s.A), b = CoordinateMap.ToUnity(s.B),
                    gamma = (float)-s.Gamma, coreRadius = (float)s.CoreRadius, age = (float)s.AgeSec,
                });
            return output.Count;
        }

        // ------------------------------------------------------------------ the texture for the wisps

        public const int MaxTextureSegments = 64, MaxTextureJets = 16;
        private static Material _bake;
        private static bool _bakeMissingLogged;
        private static readonly Vector4[] SegA = new Vector4[MaxTextureSegments], SegB = new Vector4[MaxTextureSegments];
        private static readonly Vector4[] JetA = new Vector4[MaxTextureJets], JetB = new Vector4[MaxTextureJets], JetD = new Vector4[MaxTextureJets];
        private static readonly List<(float key, int idx)> Rank = new();
        /// <summary>Segments that reached the last texture, and how many were in reach of its region.</summary>
        public static int LastTextureSegments { get; private set; }
        public static int LastRegionSegments { get; private set; }

        /// <summary>
        /// The wake's air on the horizontal sheet through <paramref name="region"/>'s centre, over its X/Z extent: R,G =
        /// world X/Z velocity (m/s), B = |ω| (1/s), A = world Y velocity (m/s); UV (0,0) = region minimum X/Z. Evaluated
        /// on the GPU per texel from at most <see cref="MaxTextureSegments"/> segments (the strongest nearest the
        /// sheet) and <see cref="MaxTextureJets"/> slipstream pieces; the CPU only picks them.
        /// </summary>
        public static void WriteToTexture(RenderTexture rt, Bounds region)
        {
            if (rt == null) return;
            int nSeg = 0, nJet = 0;
            Vector3 c = region.center;
            if (VortexWake.Active)
            {
                Vec3 centreSim = CoordinateMap.ToSim(c);
                double radius = Mathf.Sqrt(region.extents.x * region.extents.x + region.extents.z * region.extents.z) + region.extents.y;
                VortexWake.Gather(centreSim, radius, null, 0.0, Segs, Jets);
                LastRegionSegments = Segs.Count;
                Rank.Clear();
                for (int i = 0; i < Segs.Count; i++)
                {
                    VortexSegment s = Segs[i];
                    Vector3 m = CoordinateMap.ToUnity((s.A + s.B) * 0.5);
                    float dy = Mathf.Abs(m.y - c.y);
                    Rank.Add(((float)System.Math.Abs(s.Gamma) / (1f + dy), i));
                }
                Rank.Sort((x, y) => y.key.CompareTo(x.key));
                for (int r = 0; r < Rank.Count && nSeg < MaxTextureSegments; r++)
                {
                    VortexSegment s = Segs[Rank[r].idx];
                    Vector3 a = CoordinateMap.ToUnity(s.A) - c, b = CoordinateMap.ToUnity(s.B) - c;
                    SegA[nSeg] = new Vector4(a.x, a.y, a.z, (float)-s.Gamma);
                    SegB[nSeg] = new Vector4(b.x, b.y, b.z, (float)s.CoreRadius);
                    nSeg++;
                }
                for (int i = 0; i < Jets.Count && nJet < MaxTextureJets; i++)
                {
                    JetSegment j = Jets[i];
                    Vector3 a = CoordinateMap.ToUnity(j.A) - c, b = CoordinateMap.ToUnity(j.B) - c, d = CoordinateMap.ToUnity(j.Dir);
                    JetA[nJet] = new Vector4(a.x, a.y, a.z, (float)(0.5 * (j.DuA + j.DuB)));
                    JetB[nJet] = new Vector4(b.x, b.y, b.z, (float)(0.5 * (j.RA + j.RB)));
                    JetD[nJet] = new Vector4(d.x, d.y, d.z, (float)(0.5 * (j.SwirlA + j.SwirlB)));
                    nJet++;
                }
            }
            LastTextureSegments = nSeg;
            if (nSeg == 0 && nJet == 0)
            {
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = prev;
                return;
            }
            if (_bake == null)
            {
                Shader sh = Shader.Find("Hidden/FlyingGame/WakeFieldBake");
                if (sh == null)
                {
                    if (!_bakeMissingLogged) Debug.LogError("[Wake] Hidden/FlyingGame/WakeFieldBake missing from the build: the wisps see still air");
                    _bakeMissingLogged = true;
                    return;
                }
                _bake = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            _bake.SetVectorArray("_WakeSegA", SegA);
            _bake.SetVectorArray("_WakeSegB", SegB);
            _bake.SetVectorArray("_WakeJetA", JetA);
            _bake.SetVectorArray("_WakeJetB", JetB);
            _bake.SetVectorArray("_WakeJetD", JetD);
            _bake.SetInt("_WakeSegCount", nSeg);
            _bake.SetInt("_WakeJetCount", nJet);
            _bake.SetVector("_WakeRegion", new Vector4(region.extents.x, region.extents.z, 0, 0));
            Graphics.Blit(null, rt, _bake);
        }
    }
}
