using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge.Practice
{
    /// <summary>
    /// Renders the lesson pictures live from the game (owner 2026-09-15: "screenshots from the game"): the aircraft is posed,
    /// an off-screen camera renders the scene, and the force arrows for the lesson are drawn over it. Two pictures for
    /// the "Straight" lesson: a rear view with the lift tilted by the bank, and a top view with the relative wind of the
    /// slip, the vertical tail's force and the resulting yaw.
    /// </summary>
    public static class LessonIllustrator
    {
        private sealed class Painter : MonoBehaviour
        {
            public readonly List<(Vector3 from, Vector3 to, Color c)> Lines = new();
            private Material _mat;
            private void OnPostRender()
            {
                _mat ??= new Material(Shader.Find("FlyingGame/HudLine") ?? Shader.Find("Unlit/Color")) { color = Color.white };
                _mat.SetPass(0);
                GL.PushMatrix(); GL.Begin(GL.LINES);
                foreach (var (a, b, c) in Lines) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }
                GL.End(); GL.PopMatrix();
            }
            public void Arrow(Vector3 from, Vector3 v, Color c, float thick = 0.06f)
            {
                Vector3 to = from + v; Vector3 d = v.normalized;
                Vector3 side = Vector3.Cross(d, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(d, Vector3.right); side.Normalize();
                Vector3 up2 = Vector3.Cross(d, side).normalized;
                // a fat shaft: several parallel lines
                for (int i = -2; i <= 2; i++) { Vector3 o = side * (i * thick) ; Lines.Add((from + o, to + o, c)); Vector3 o2 = up2 * (i * thick); Lines.Add((from + o2, to + o2, c)); }
                float h = Mathf.Min(0.9f, v.magnitude * 0.25f);
                for (int i = -2; i <= 2; i++) { Vector3 o = up2 * (i * thick * 0.5f); Lines.Add((to + o, to - d * h + side * h * 0.6f + o, c)); Lines.Add((to + o, to - d * h - side * h * 0.6f + o, c)); }
            }
            public void Arc(Vector3 centre, Vector3 axis, float radius, float fromDeg, float toDeg, Color c)
            {
                Vector3 r0 = Vector3.Cross(axis, Vector3.forward); if (r0.sqrMagnitude < 1e-4f) r0 = Vector3.Cross(axis, Vector3.right); r0.Normalize();
                Vector3 prev = Vector3.zero; int n = 24;
                for (int i = 0; i <= n; i++)
                {
                    float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n);
                    Vector3 p = centre + Quaternion.AngleAxis(a, axis) * r0 * radius;
                    if (i > 0) { Lines.Add((prev, p, c)); Lines.Add((prev + axis * 0.05f, p + axis * 0.05f, c)); }
                    prev = p;
                }
                Vector3 tip = prev, tan = (prev - (centre + Quaternion.AngleAxis(toDeg - 4f, axis) * r0 * radius)).normalized;
                Arrow(tip - tan * 0.6f, tan * 0.6f, c, 0.04f);
            }
        }

        /// <summary>Render one picture: pose the aircraft (bank/yaw in degrees), place the camera, draw the arrows, restore.</summary>
        private static Texture2D Render(Transform aircraft, float bankDeg, float yawDeg, bool topView, System.Action<Painter, Transform> draw, int w = 1024, int h = 640)
        {
            Vector3 pos0 = aircraft.position; Quaternion rot0 = aircraft.rotation;
            aircraft.rotation = Quaternion.AngleAxis(yawDeg, Vector3.up) * rot0 * Quaternion.AngleAxis(-bankDeg, Vector3.forward);
            var go = new GameObject("LessonCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            cam.fieldOfView = 45f; cam.nearClipPlane = 0.5f; cam.farClipPlane = 20000f;
            var painter = go.AddComponent<Painter>();
            float span = 12f;
            var vis = aircraft.GetComponent<AirframeVisual>();
            var main = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (main != null) span = main.Distance / 1.3f;
            if (topView)
            {
                cam.transform.position = aircraft.position + Vector3.up * (span * 2.2f) + aircraft.forward * 1.5f;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.down, aircraft.forward);
            }
            else
            {
                cam.transform.position = aircraft.position - aircraft.forward * (span * 1.6f) + Vector3.up * (span * 0.25f);
                cam.transform.rotation = Quaternion.LookRotation(aircraft.position + Vector3.up * 0.5f - cam.transform.position, Vector3.up);
            }
            draw(painter, aircraft);
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(go);
            aircraft.position = pos0; aircraft.rotation = rot0;
            return tex;
        }

        /// <summary>Page 1: rear view, banked 30°: lift tilted with the wings, its sideways part, and the weight.</summary>
        public static Texture2D RearViewBank(Transform aircraft)
        {
            return Render(aircraft, 30f, 0f, false, (p, ac) =>
            {
                Vector3 wing = ac.position + ac.up * 0.6f;
                float L = 7f;
                Vector3 lift = ac.up * L;
                p.Arrow(wing, lift, new Color(0.3f, 1f, 0.4f));                                  // lift, tilted with the bank
                p.Arrow(wing, Vector3.up * (lift.y), new Color(0.3f, 1f, 0.4f, 0.5f), 0.03f);   // its vertical part
                Vector3 side = new Vector3(lift.x, 0f, lift.z);
                p.Arrow(wing + Vector3.up * lift.y, side, new Color(1f, 0.6f, 0.2f));              // the sideways part: the slip begins
                p.Arrow(ac.position, Vector3.down * L, new Color(0.8f, 0.8f, 0.8f));               // weight
            });
        }

        /// <summary>Page 2: top view, slipping toward the low (right) wing: relative wind from the right, tail pushed left, nose swings right.</summary>
        public static Texture2D TopViewWeathervane(Transform aircraft)
        {
            return Render(aircraft, 0f, 0f, true, (p, ac) =>
            {
                float span = 8f;
                Vector3 windFrom = (ac.forward * 0.85f + ac.right * 0.5f).normalized;                 // the air comes from the front-right in a right slip
                p.Arrow(ac.position + windFrom * (span * 1.6f), -windFrom * (span * 1.2f), Color.white);  // relative wind arriving at the aircraft
                Vector3 tail = ac.position - ac.forward * (span * 0.55f);
                p.Arrow(tail, -ac.right * (span * 0.45f), new Color(1f, 0.9f, 0.2f));               // vertical tail pushes the tail LEFT
                p.Arc(ac.position + Vector3.up * 0.2f, Vector3.up, span * 0.5f, 100f, 20f, new Color(1f, 0.6f, 0.2f));   // nose swings RIGHT
            });
        }
    }
}
