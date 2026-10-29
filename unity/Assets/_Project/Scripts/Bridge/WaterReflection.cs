using UnityEngine;

namespace FlyingGame.Bridge
{
    // One planar camera, with terrain/shoreline on balanced and high tiers. Water is excluded.
    [RequireComponent(typeof(Camera))]
    public sealed class WaterReflection : MonoBehaviour
    {
        public const int AircraftLayer = 8;
        /// <summary>Water-side props drawn into the reflection too: lane buoys, docks, the seaplane hangar (owner 2026-10-07).</summary>
        public const int PropsLayer = 9;
        public FlightSimDriver Driver;
        public enum ReflectionQuality { Low, Balanced, High }
        public ReflectionQuality Quality = ReflectionQuality.Balanced;
        // 0 = tier default; explicit override is useful for comparison captures.
        public int TextureSize = 0;
        public const int WaterLayer = 4;
        public bool ReflectScenery = true;
        private int Mask => ((Quality == ReflectionQuality.Low || !ReflectScenery)
            ? (1 << AircraftLayer) | (1 << PropsLayer)
            : _main.cullingMask) & ~((1 << WaterLayer) | (1 << 5) | (1 << 2));

        private void EnsureTexture()
        {
            int size = TextureSize > 0 ? Mathf.Clamp(TextureSize, 128, 2048)
                : Quality == ReflectionQuality.High ? 1536 : Quality == ReflectionQuality.Low ? 384 : 768;
            int width = Mathf.Max(128, Mathf.RoundToInt(size * Mathf.Min(1, _main.aspect)));
            int height = Mathf.Max(128, Mathf.RoundToInt(size / Mathf.Max(1, _main.aspect)));
            if (_rt != null && _rt.width == width && _rt.height == height) return;
            if (_rt != null) { _rt.Release(); Dispose(_rt); }
            _rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
            _rt.name = "Planar water reflection"; _rt.wrapMode = TextureWrapMode.Clamp;
            _rt.filterMode = FilterMode.Bilinear; _rt.Create();
            _refl.targetTexture = _rt;
        }

        private Camera _main, _refl;
        private Renderer[] _zonePanes;
        private readonly System.Collections.Generic.List<Renderer> _hiddenPanes = new();
        private RenderTexture _rt;
        private static readonly int ReflTexId = Shader.PropertyToID("_ReflectionTex");
        private static readonly int ReflPlaneId = Shader.PropertyToID("_ReflPlaneY");
        private static readonly int ReflOnId = Shader.PropertyToID("_ReflOn");

        private void Awake()
        {
            if (_refl != null) return;
            _main = GetComponent<Camera>();
            _main.depthTextureMode |= DepthTextureMode.Depth;
            var go = new GameObject("WaterReflectionCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            _refl = go.AddComponent<Camera>();
            _refl.enabled = false;
            _refl.clearFlags = CameraClearFlags.SolidColor;
            _refl.backgroundColor = new Color(0, 0, 0, 0);      // alpha 0 = "no aircraft here" → sky
            EnsureTexture();
            Shader.SetGlobalFloat(ReflOnId, 0f);
        }

        private void OnPreCull()
        {
            if (Driver == null || _refl == null) { Shader.SetGlobalFloat(ReflOnId, 0f); return; }
            // Water plane under the aircraft (nearest lake / river surface); none → no reflection.
            var simPos = CoordinateMap.ToSim(Driver.transform.position);
            double? water = FlyingGame.Core.FloatHydro.WaterSurfaceAt(simPos.X, simPos.Y);
            if (!water.HasValue)
            {
                // Not over water: use the nearest lake if it's close (so the reflection is ready as you arrive).
                double best = double.MaxValue; double? h = null;
                foreach (var l in FlyingGame.Core.WorldTerrain.Lakes)
                {
                    double d = l.Inside(simPos.X, simPos.Y);
                    if (d < best) { best = d; h = l.SurfaceM; }
                }
                if (best > 2.5) { Shader.SetGlobalFloat(ReflOnId, 0f); return; }
                water = h;
            }
            float planeY = (float)water.Value;
            // Mirror the main camera about the plane y = planeY.
            Vector3 pos = _main.transform.position;
            if (pos.y < planeY + 0.2f) { Shader.SetGlobalFloat(ReflOnId, 0f); return; }
            var reflection = Matrix4x4.identity;
            reflection.m11 = -1f; reflection.m13 = 2f * planeY;   // y' = -y + 2*planeY
            _refl.CopyFrom(_main);
            _refl.enabled = false;
            _refl.clearFlags = CameraClearFlags.SolidColor; _refl.backgroundColor = new Color(0, 0, 0, 0);
            EnsureTexture();
            _refl.cullingMask = Mask;
            _refl.depthTextureMode = DepthTextureMode.None;
            _refl.allowMSAA = false;
            // A shorter reflection frustum cuts distant cliffs off above their shoreline.
            // Resolution/masks carry the tier budget; reflected geometry uses the same reach.
            _refl.farClipPlane = _main.farClipPlane;
            // Transform is needed by skybox and world-space specular, in addition to the view matrix.
            _refl.transform.position = reflection.MultiplyPoint(pos);
            _refl.transform.rotation = Quaternion.LookRotation(reflection.MultiplyVector(_main.transform.forward), reflection.MultiplyVector(_main.transform.up));
            _refl.targetTexture = _rt;
            _refl.worldToCameraMatrix = _main.worldToCameraMatrix * reflection;
            // Keep a conventional finite frustum. At a grazing water angle an oblique
            // near plane also tilts the far plane, slicing distant cliffs into slivers.
            // The scenery/aircraft shaders clip the water half-space in world space;
            // unlike an oblique far plane that is independent of camera pitch/distance.
            _refl.projectionMatrix = _main.projectionMatrix;
            _refl.cullingMatrix = _refl.projectionMatrix * _refl.worldToCameraMatrix;
            // Glass zone panes are navigation aids, not physical reflectors. Restore
            // their renderers after this camera so gameplay visibility is unchanged.
            if (_zonePanes == null)
            {
                var found = new System.Collections.Generic.List<Renderer>();
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    var material = renderer.sharedMaterial;
                    if (material != null && material.shader.name == "FlyingGame/Glass"
                        && renderer.gameObject.layer != AircraftLayer) found.Add(renderer);
                }
                _zonePanes = found.ToArray();
            }
            _hiddenPanes.Clear();
            foreach (var pane in _zonePanes)
                if (pane != null && pane.enabled) { _hiddenPanes.Add(pane); pane.enabled = false; }
            bool previousCulling = GL.invertCulling;
            Shader.SetGlobalVector("_ReflectionClip", new Vector4(0, 1, 0, -planeY - .03f));
            Shader.SetGlobalFloat("_ReflectionRendering", 1);
            try { GL.invertCulling = !previousCulling; _refl.Render(); }
            finally
            {
                GL.invertCulling = previousCulling; Shader.SetGlobalFloat("_ReflectionRendering", 0);
                foreach (var pane in _hiddenPanes) if (pane != null) pane.enabled = true;
            }
            Shader.SetGlobalTexture(ReflTexId, _rt);
            Shader.SetGlobalFloat(ReflPlaneId, planeY);
            Shader.SetGlobalFloat(ReflOnId, 1f);
        }

        private static void Dispose(Object obj)
        {
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        private void OnDestroy()
        {
            if (_refl != null) Dispose(_refl.gameObject);
            if (_rt != null) { _rt.Release(); Dispose(_rt); }
            Shader.SetGlobalFloat(ReflOnId, 0);
        }
    }
}
