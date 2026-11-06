using System.IO;
using System.Linq;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ContactPoint = FlyingGame.Core.ContactPoint;

namespace FlyingGame.EditorTools
{
    /// <summary>
    /// Ty 2026-11-05 Q028b / t867u: canopy, windshield and window glass shatters only on a direct hit, jagged, on every
    /// aeroplane. For each case the airframe the game builds is drawn, its glass is measured by GlassShatter (the panes the
    /// sim is given), a shatter is raised — a REAL one for the Extra (on its back onto flat ground at 14 m/s, as
    /// tools/FlightTests/GlazingTests does; the Pitts' upper wing would meet the ground first), else the sim's event at the pane's own first sensor point — and the glass is
    /// drawn before and after, outside and from the seat eye.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.GlassShatterShots.Render
    /// Images to GLASS_RENDER_DIR (default build/glass-shatter/).
    /// </summary>
    public static class GlassShatterShots
    {
        private sealed class Case { public string Id, Pane; public bool Real; }

        public static void Render()
        {
            string outDir = System.Environment.GetEnvironmentVariable("GLASS_RENDER_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/glass-shatter"));
            Directory.CreateDirectory(outDir);
            int failures = 0;
            var cases = new[]
            {
                new Case { Id = "pitts-s2b-like", Pane = "Canopy_Glass" },   // on its back the upper wing meets the ground first
                new Case { Id = "extra-300-like", Pane = "Canopy_Glass", Real = true },
                new Case { Id = "c172-like", Pane = "Glass.Windscreen" },
                new Case { Id = "glider-2-33-like", Pane = "Canopy_Glass" },
                new Case { Id = "glider-grob103-acro-like", Pane = "Canopy_Glass" },
                new Case { Id = "great-lakes-like", Pane = "Windscreen_Front" },
                new Case { Id = "j3-cub-like", Pane = "Glass" },
                new Case { Id = "cirrus-sr22-like", Pane = "Object_30" },
            };
            try
            {
                foreach (Case c in cases)
                {
                    try { failures += RenderCase(c, outDir); }
                    catch (System.Exception e) { failures++; Debug.LogException(e); }
                }
            }
            finally
            {
                RenderTexture.active = null;
                Debug.Log($"[GlassShots] {(failures == 0 ? "PASS" : "FAIL")} ({failures} failures); images in {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static int RenderCase(Case c, string outDir)
        {
            WorldTerrain.Active = null;
            AircraftConfig cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(c.Id);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionSettings.UseAirframeModels = true;
            SessionSettings.UseOwnModels = true;
            var cam = new GameObject("RenderCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.7f, 0.92f);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 300f;
            var rt = new RenderTexture(1400, 1000, 24); cam.targetTexture = rt;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(40, 60, 0);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            // A dark ground plane under the aeroplane, so the clear glass and the pale breaks read against it.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 4f; ground.transform.position = new Vector3(0, -2.2f, 0);
            ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.22f, 0.3f, 0.2f) };

            var root = new GameObject(c.Id);
            var builder = new AirframeBuilder();
            builder.Build(root.transform, cfg);
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0, 0, -100), Quat.Identity, new Vec3(30, 0, 0), Vec3.Zero), ControlDeflections.Neutral);
            var gs = root.AddComponent<GlassShatter>();
            int measured = gs.SetActiveForTesting(ac, builder.ModelRoot);
            Vec3 cg = cfg.Mass.CgVec();
            Debug.Log($"[GlassShots] {c.Id}: model {(builder.ModelRoot != null ? builder.ModelRoot.name : "(shell)")}, {measured} pane(s) measured; sim panes: "
                + string.Join("; ", ac.GlazingPanes.Select(p => $"{p.Id} {p.Kind} [{string.Join(" ", p.Points.Select(q => $"({q.X:F2},{q.Y:F2},{q.Z:F2})"))}]")));
            int fails = 0;
            if (measured == 0) { Debug.LogError($"[GlassShots] {c.Id}: no glass found on the drawn airframe"); fails++; }

            // Where to look: the panes' points (body, config origin) in the root's frame (CG-relative body axes).
            Vector3 L(Vec3 body) => CoordinateMap.ToUnity(body - cg);
            GlazingPane target = c.Pane == null ? ac.GlazingPanes.FirstOrDefault() : ac.GlazingPanes.FirstOrDefault(p => p.Id == c.Pane) ?? ac.GlazingPanes.FirstOrDefault(p => p.Id.StartsWith(c.Pane));
            if (target == null) { Debug.LogError($"[GlassShots] {c.Id}: no pane '{c.Pane}'"); Object.DestroyImmediate(root); return fails + 1; }
            Vector3 centre = Vector3.zero; foreach (Vec3 q in target.Points) centre += L(q); centre /= target.Points.Length;
            Vector3 eye = cfg.Loading.Seats.Any(s => s.HasEye) ? CoordinateMap.ToUnity(cfg.Loading.Seats.Where(s => s.HasEye).OrderByDescending(s => s.EyeVec().X).First().EyeVec()) :   // eyes are CG-rooted
                centre + new Vector3(0, -0.15f, -0.5f);
            Vector3 outside = (new Vector3(centre.x, centre.y, 0f).normalized + Vector3.forward * 0.6f + Vector3.left * 0.5f).normalized;
            void Shots(string tag)
            {
                cam.fieldOfView = 28f;
                Snap(cam, rt, centre, centre + outside * 3.2f + Vector3.up * 0.6f, outDir, $"{c.Id}-{tag}-outside");
                Snap(cam, rt, centre, centre + new Vector3(-2.6f, 1.4f, -1.2f), outDir, $"{c.Id}-{tag}-left-aft");
                cam.fieldOfView = 70f;
                Snap(cam, rt, eye + Vector3.forward * 2f + Vector3.up * 0.15f, eye, outDir, $"{c.Id}-{tag}-seat-eye");
            }
            Shots("before");

            if (c.Real)
            {
                // On its back onto flat ground at 14 m/s, as GlazingTests.ADirectHitOnTheCanopyShattersItAndItStaysOn.
                var inv = new Quat(1, 0, 0, 0);
                double low = double.NegativeInfinity;
                foreach (ContactPoint p in ac.ContactPoints) if (!p.PropDisc) low = System.Math.Max(low, inv.Rotate(p.Body - cg).Z);
                ac.State = new RigidBodyState(new Vec3(0, 0, -(low + 0.02)), inv, inv.Conjugate().Rotate(new Vec3(0, 0, 14.0)), Vec3.Zero);
                for (double t = 0; t < 1.0 && ac.GlazingLog.Count == 0; t += 0.005) ac.Step(new ControlInputs(0, 0, 0, -1), 0.005);
                Debug.Log($"[GlassShots] {c.Id}: real strike shattered [{string.Join(", ", ac.GlazingLog.Select(p => $"{p.Id} at {p.ShatterClosingMs:F1} m/s by {p.ShatterBy}"))}]");
                if (ac.GlazingLog.Count == 0) { Debug.LogError($"[GlassShots] {c.Id}: the strike broke no glass"); fails++; }
            }
            else
            {
                RigidBodyState s = ac.State;
                Vec3 w = s.Position + s.Attitude.Rotate(target.Points[0] - cg);
                gs.ShatterForTesting(new ContactEvent(ContactEventKind.GlazingShattered, target.Id, AirframeComponent.Cabin, ContactPoint.SurfaceNone, 9.0, 6.0, w, s.Attitude.Rotate(s.Velocity)));
            }
            // What was drawn: the remnant beside the pane it replaced.
            int remnants = 0, keptTris = 0, edgeTris = 0;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name == "GlassRemnant" && mf.sharedMesh != null) { remnants++; keptTris += mf.sharedMesh.triangles.Length / 3; }
                if (mf.name == "GlassFractureEdges" && mf.sharedMesh != null) edgeTris += mf.sharedMesh.triangles.Length / 3;
            }
            Debug.Log($"[GlassShots] {c.Id}: {target.Id} broken: {remnants} remnant mesh(es), {keptTris} triangles kept, {edgeTris} edge triangles; panes now "
                + string.Join(", ", gs.PanesForTesting().Select(p => $"{p.Id}{(p.Broken ? " BROKEN" : "")}")));
            if (measured > 0 && remnants == 0) { Debug.LogError($"[GlassShots] {c.Id}: no remnant drawn"); fails++; }
            gs.StepShardsForTesting(0.35f);
            Shots("after");
            Object.DestroyImmediate(root);
            foreach (GameObject g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)) if (g.name == "GlassShard") Object.DestroyImmediate(g);
            return fails;
        }

        private static void Snap(Camera cam, RenderTexture rt, Vector3 target, Vector3 from, string outDir, string name)
        {
            cam.transform.position = from;
            cam.transform.LookAt(target, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
