using System;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Can a hand in the headset actually take the 2-33's handles, and do they go the right way? Builds the aeroplane
    /// exactly as the game does (as SgsRigCheck does), binds the rig, and MEASURES each handle with the same code the
    /// headset uses (<see cref="RigHandle"/>: the knob from the node's own meshes, its two ends from the rig's own
    /// rotation), in the aeroplane's frame, against the seat's eye:
    ///   - DiveBrake_Lever_F / _R: 33° of swing, the knob moving AFT from stowed to full, on the LEFT, within reach;
    ///   - Trim_Lever: 40° of swing, AFT = nose up (+1), on the left, within reach of the front seat;
    ///   - TowRelease_F / _R: 50 mm of pull, straight AFT, within reach.
    ///
    ///   Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.VrHandleCheck.Run
    ///
    /// Exit 0 = every check passed; the log lines start "HANDLE".
    /// </summary>
    public static class VrHandleCheck
    {
        private const string Type = "glider-2-33-like";
        /// <summary>A seated pilot's comfortable reach from the eye to a knob, metres (shoulder 0.2 m below and 0.2 m to
        /// the side of the eye, a 0.65 m arm): anything past it a pilot would have to lean for.</summary>
        private const float ReachM = 0.85f;

        private static int _fails;

        public static void Run()
        {
            int exit = 1;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                SessionSettings.UseAirframeModels = true;
                SessionSettings.UseOwnModels = true;
                var root = new GameObject(Type);
                var builder = new AirframeBuilder();
                AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(Type);
                builder.Build(root.transform, cfg);
                if (!builder.HasOwnRig) throw new InvalidOperationException("the 2-33's own rig did not bind — nothing to check.");

                Vector3 eyeF = Eye(cfg, "front"), eyeR = Eye(cfg, "rear");
                Debug.Log($"HANDLE eyes (aeroplane frame, m): front {eyeF:F3}, rear {eyeR:F3}");
                Lever(builder.Rig, root.transform, "DiveBrake_Lever_F", eyeF, 33f, wantAft: true);
                Lever(builder.Rig, root.transform, "DiveBrake_Lever_R", eyeR, 33f, wantAft: true);
                Lever(builder.Rig, root.transform, "Trim_Lever", eyeF, 40f, wantAft: true);
                Slide(builder.Rig, root.transform, "TowRelease_F", eyeF, 0.05f);
                Slide(builder.Rig, root.transform, "TowRelease_R", eyeR, 0.05f);
                exit = _fails == 0 ? 0 : 1;
                Debug.Log(exit == 0 ? "HANDLE OK" : $"HANDLE PROBLEMS: {_fails} — see the errors above");
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(exit);
        }

        private static Vector3 Eye(AircraftConfig cfg, string seat)
        {
            foreach (StationConfig s in cfg.Loading.Seats)
                if (s.Id == seat) return CoordinateMap.ToUnity(s.EyeVec());
            Fail($"no '{seat}' seat in {Type}");
            return Vector3.zero;
        }

        private static void Fail(string m) { _fails++; Debug.LogError("HANDLE FAIL " + m); }

        /// <summary>"12 cm fwd, 30 cm down, 25 cm left" — the aeroplane's own frame, from the eye.</summary>
        private static string From(Vector3 eye, Vector3 p)
        {
            Vector3 d = (p - eye) * 100f;
            return $"{Mathf.Abs(d.z):F0} cm {(d.z >= 0 ? "fwd" : "aft")}, {Mathf.Abs(d.y):F0} cm {(d.y >= 0 ? "up" : "down")}, {Mathf.Abs(d.x):F0} cm {(d.x >= 0 ? "right" : "left")} of the eye ({(p - eye).magnitude * 100f:F0} cm)";
        }

        private static void Lever(AirframeRig rig, Transform air, string node, Vector3 eye, float wantSweep, bool wantAft)
        {
            RigHandle h = RigHandle.Find(rig, node);
            if (h == null) { Fail($"{node} is not bound"); return; }
            int verts = RigHandle.VertexCount(h.T);
            Vector3 pivot = air.InverseTransformPoint(h.PivotWorld);
            Vector3 k0 = air.InverseTransformPoint(h.KnobWorld(h.V0)), k1 = air.InverseTransformPoint(h.KnobWorld(h.V1));
            var lever = new FlyingGame.Core.Vr.HingedLever(C(pivot), C(k0), C(k1), C(air.InverseTransformDirection(h.HingeWorld)));
            Debug.Log($"HANDLE {node}: {verts} vertices; pivot {From(eye, pivot)}; knob at {h.P0:+0.##;-0.##;0} {From(eye, k0)}; " +
                      $"at {h.P1:+0.##;-0.##;0} {From(eye, k1)}; arm {lever.ArmM * 100:F1} cm ({lever.AxialM * 100:F1} cm along the hinge), sweep {lever.SweepDeg:F1}° about ({lever.Axis.X:F2}, {lever.Axis.Y:F2}, {lever.Axis.Z:F2})");
            if (verts == 0) Fail($"{node}: no readable mesh — the knob fell back to the pivot");
            if (System.Math.Abs(lever.SweepDeg - wantSweep) > 1.0) Fail($"{node}: sweeps {lever.SweepDeg:F1}°, the rig says {wantSweep}°");
            if (lever.ArmM < 0.05 || lever.ArmM > 0.45) Fail($"{node}: a {lever.ArmM * 100:F0} cm arm is not a hand lever");
            float dz = k1.z - k0.z;
            if (wantAft && dz > -0.02f) Fail($"{node}: the knob moves {dz * 100:F1} cm fore-aft from {h.P0} to {h.P1}; it must go AFT");
            if (System.Math.Abs(lever.Axis.X) < 0.9) Fail($"{node}: hinge axis ({lever.Axis.X:F2}, {lever.Axis.Y:F2}, {lever.Axis.Z:F2}) is not lateral");
            if (k0.x > eye.x || k1.x > eye.x) Fail($"{node}: the knob is RIGHT of the eye; the 2-33's levers are on the left wall");
            float reach = Mathf.Max((k0 - eye).magnitude, (k1 - eye).magnitude);
            if (reach > ReachM) Fail($"{node}: {reach * 100:F0} cm from the eye, out of reach");
        }

        private static void Slide(AirframeRig rig, Transform air, string node, Vector3 eye, float wantTravel)
        {
            RigHandle h = RigHandle.Find(rig, node);
            if (h == null) { Fail($"{node} is not bound"); return; }
            int verts = RigHandle.VertexCount(h.T);
            Vector3 home = air.InverseTransformPoint(h.KnobWorld(h.V0)), out1 = air.InverseTransformPoint(h.KnobWorld(h.V1));
            Vector3 d = out1 - home;
            Debug.Log($"HANDLE {node}: {verts} vertices; knob home {From(eye, home)}; pulled ({d.x * 1000:F0}, {d.y * 1000:F0}, {d.z * 1000:F0}) mm (x right, y up, z fwd)");
            if (verts == 0) Fail($"{node}: no readable mesh");
            if (Mathf.Abs(d.magnitude - wantTravel) > 0.002f) Fail($"{node}: pulls {d.magnitude * 1000:F0} mm, the rig says {wantTravel * 1000:F0}");
            if (d.z > -0.9f * d.magnitude) Fail($"{node}: the pull is not straight aft");
            if ((home - eye).magnitude > ReachM) Fail($"{node}: {(home - eye).magnitude * 100:F0} cm from the eye, out of reach");
        }

        private static FlyingGame.Core.MathTypes.Vec3 C(Vector3 v) => new(v.x, v.y, v.z);
    }
}
