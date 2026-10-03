using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Side-view lessons in 2-D (owner 2026-10-03: "the 3D simulation actually detracts from the lesson"). While a side-view
    /// lesson runs, the 3-D world (terrain, scenery, clouds, compass markers, the far lift field) is hidden, the main camera
    /// goes ORTHOGRAPHIC looking straight along the side axis, and a flat backdrop is drawn behind the aircraft: sky, a ground
    /// band with stripes every 25 m (so speed and distance still read), the runway as a strip on the ground line. The
    /// aircraft (seen exactly side-on), the lesson's vectors and the glideslope line stay. Everything is restored on exit.
    /// </summary>
    public static class Side2DView
    {
        private static readonly List<GameObject> _hidden = new();
        private static GameObject _backdrop;
        private static bool _active, _wasOrtho, _fog;
        private static float _wasSize;
        private static CameraClearFlags _wasClear;
        private static Color _wasBg;
        public static bool Active => _active;
        /// <summary>Horizontal unit vector toward the camera (the side axis) while 2-D is active.</summary>
        public static Vector3 SideAxis { get; private set; }
        /// <summary>2-D shows a SLICE of the air: only bubbles within this distance of the aircraft's own plane (a flat
        /// camera would otherwise stack every bubble in the 150 m volume into one curtain).</summary>
        public const float AirSliceHalfM = 4f;

        public static readonly Color Sky = new(0.62f, 0.78f, 0.95f), Ground = new(0.42f, 0.55f, 0.32f), GroundStripe = new(0.37f, 0.5f, 0.28f),
                                     Runway = new(0.32f, 0.33f, 0.36f), RunwayEdge = new(0.92f, 0.92f, 0.9f);

        /// <summary>Enter 2-D: <paramref name="sideRight"/> = the camera's side (Unity, horizontal), <paramref name="along"/> =
        /// the runway direction (Unity, horizontal), <paramref name="groundY"/> = ground height, threshold/length = the runway.</summary>
        public static void Enter(Camera cam, Vector3 sideRight, Vector3 along, float groundY, Vector3 threshold, float runwayLength)
        {
            if (_active || cam == null) return;
            _active = true;
            SideAxis = new Vector3(sideRight.x, 0f, sideRight.z).normalized;
            foreach (string n in new[] { "World", "Soaring", "LiftField" })
            {
                GameObject g = GameObject.Find(n);
                if (g != null && g.activeSelf) { g.SetActive(false); _hidden.Add(g); }
            }
            foreach (GameObject g in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (g.name.StartsWith("Cardinal-") && g.activeSelf) { g.SetActive(false); _hidden.Add(g); }

            _wasOrtho = cam.orthographic; _wasSize = cam.orthographicSize; _wasClear = cam.clearFlags; _wasBg = cam.backgroundColor;
            _fog = RenderSettings.fog; RenderSettings.fog = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sky;

            // Backdrop: a vertical plane 200 m BEHIND the aircraft's track (on the far side from the camera), facing the camera.
            _backdrop = new GameObject("Side2DBackdrop");
            Vector3 a = new Vector3(along.x, 0f, along.z).normalized, r = new Vector3(sideRight.x, 0f, sideRight.z).normalized;
            Vector3 basePt = new Vector3(threshold.x, groundY, threshold.z) - r * 200f;
            const float halfLen = 12000f;
            // Ground band: 3 km deep below the ground line, striped every 25 m along the track.
            Quad(_backdrop.transform, basePt + Vector3.down * 1500f, a, halfLen, 1500f, r, StripeTexture(), halfLen * 2f / 50f);
            // Runway: a strip just in front of the ground band, along the ground line, edge lines on top.
            Vector3 mid = basePt + a * (runwayLength * 0.5f) + r * 1f;
            Quad(_backdrop.transform, mid + Vector3.down * 1.2f, a, runwayLength * 0.5f, 1.2f, r, Solid(Runway), 1f);
            Quad(_backdrop.transform, mid + r * 0.5f + Vector3.down * 0.1f, a, runwayLength * 0.5f, 0.1f, r, Solid(RunwayEdge), 1f);
        }

        public static void Exit(Camera cam)
        {
            if (!_active) return;
            _active = false;
            foreach (GameObject g in _hidden) if (g != null) g.SetActive(true);
            _hidden.Clear();
            if (_backdrop != null) Object.Destroy(_backdrop);
            _backdrop = null;
            RenderSettings.fog = _fog;
            if (cam != null) { cam.orthographic = _wasOrtho; cam.orthographicSize = _wasSize; cam.clearFlags = _wasClear; cam.backgroundColor = _wasBg; }
        }

        /// <summary>Keep the orthographic frame matched to the side distance the 3-D side view used (same framing).</summary>
        public static void Frame(Camera cam, float sideDistance)
        {
            if (!_active || cam == null) return;
            cam.orthographicSize = sideDistance * Mathf.Tan(Mathf.Max(20f, cam.fieldOfView) * 0.5f * Mathf.Deg2Rad);
        }

        // A camera-facing rectangle: centre c, half-length hl along a, half-height hh vertically, facing -n (toward the camera).
        private static void Quad(Transform parent, Vector3 c, Vector3 a, float hl, float hh, Vector3 n, Texture2D tex, float tileU)
        {
            var go = new GameObject("Band");
            go.transform.SetParent(parent, false);
            var mesh = new Mesh();
            Vector3 up = Vector3.up * hh, ax = a * hl;
            mesh.vertices = new[] { c - ax - up, c + ax - up, c + ax + up, c - ax + up };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(tileU, 0), new Vector2(tileU, 1), new Vector2(0, 1) };
            // Wind toward the camera side (+n): both windings so it shows regardless.
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Unlit/Color")) { mainTexture = tex };
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Texture2D StripeTexture()
        {
            var t = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            t.SetPixels(new[] { Ground, GroundStripe }); t.Apply();
            return t;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false); t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}
