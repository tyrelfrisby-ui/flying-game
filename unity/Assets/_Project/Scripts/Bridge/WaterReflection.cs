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
            _refl.farClipPlane = Mathf.Min(_main.farClipPlane, Quality == ReflectionQuality.High ? 6000 : 3000);
            // Transform is needed by skybox and world-space specular, in addition to the view matrix.
            _refl.transform.position = reflection.MultiplyPoint(pos);
            _refl.transform.rotation = Quaternion.LookRotation(reflection.MultiplyVector(_main.transform.forward), reflection.MultiplyVector(_main.transform.up));
            _refl.targetTexture = _rt;
            _refl.worldToCameraMatrix = _main.worldToCameraMatrix * reflection;
            // Oblique near plane at the water so nothing under the surface reflects.
            Vector4 planeCam = CameraSpacePlane(_refl.worldToCameraMatrix, new Vector3(0, planeY, 0), Vector3.up);
            _refl.projectionMatrix = Matrix4x4.Perspective(_main.fieldOfView, _main.aspect, _main.nearClipPlane, _refl.farClipPlane);
            _refl.projectionMatrix = _refl.CalculateObliqueMatrix(planeCam);
            bool previousCulling = GL.invertCulling;
            try { GL.invertCulling = !previousCulling; _refl.Render(); }
            finally { GL.invertCulling = previousCulling; }
            Shader.SetGlobalTexture(ReflTexId, _rt);
            Shader.SetGlobalFloat(ReflPlaneId, planeY);
            Shader.SetGlobalFloat(ReflOnId, 1f);
        }

        private static Vector4 CameraSpacePlane(Matrix4x4 worldToCam, Vector3 pos, Vector3 normal)
        {
            Vector3 cpos = worldToCam.MultiplyPoint(pos);
            Vector3 cnormal = worldToCam.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
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
