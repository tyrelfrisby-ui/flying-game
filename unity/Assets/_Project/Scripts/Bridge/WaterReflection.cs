using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Planar reflection of the aircraft in the water under it (owner request: glassy, reflective water — and
    /// the classic seaplane height cue: the gap between the aircraft and its reflection). A second camera,
    /// mirrored about the water plane, renders ONLY the aircraft layer into a small texture that the
    /// FlyingGame/Water shader samples in screen space. Cheap on mobile: a few dozen meshes at 512².
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class WaterReflection : MonoBehaviour
    {
        public const int AircraftLayer = 8;
        /// <summary>Water-side props drawn into the reflection too: lane buoys, docks, the seaplane hangar (owner 2026-10-07).</summary>
        public const int PropsLayer = 9;
        public FlightSimDriver Driver;
        public int TextureSize = 512;

        private Camera _main, _refl;
        private RenderTexture _rt;
        private static readonly int ReflTexId = Shader.PropertyToID("_ReflectionTex");
        private static readonly int ReflPlaneId = Shader.PropertyToID("_ReflPlaneY");
        private static readonly int ReflOnId = Shader.PropertyToID("_ReflOn");

        private void Awake()
        {
            _main = GetComponent<Camera>();
            var go = new GameObject("WaterReflectionCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            _refl = go.AddComponent<Camera>();
            _refl.enabled = false;
            _refl.clearFlags = CameraClearFlags.SolidColor;
            _refl.backgroundColor = new Color(0, 0, 0, 0);      // alpha 0 = "no aircraft here" → sky
            _refl.cullingMask = (1 << AircraftLayer) | (1 << PropsLayer);
            _rt = new RenderTexture(TextureSize, TextureSize, 16, RenderTextureFormat.ARGB32);
            _refl.targetTexture = _rt;
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
            _refl.clearFlags = CameraClearFlags.SolidColor; _refl.backgroundColor = new Color(0, 0, 0, 0);
            _refl.cullingMask = (1 << AircraftLayer) | (1 << PropsLayer);
            _refl.targetTexture = _rt;
            _refl.worldToCameraMatrix = _main.worldToCameraMatrix * reflection;
            // Oblique near plane at the water so nothing under the surface reflects.
            Vector4 planeCam = CameraSpacePlane(_refl.worldToCameraMatrix, new Vector3(0, planeY, 0), Vector3.up);
            _refl.projectionMatrix = _main.CalculateObliqueMatrix(planeCam);
            GL.invertCulling = true;
            _refl.Render();
            GL.invertCulling = false;
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

        private void OnDestroy()
        {
            if (_refl != null) Destroy(_refl.gameObject);
            if (_rt != null) _rt.Release();
        }
    }
}
