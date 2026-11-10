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

        /// <summary>LOCAL 2-PLAYER: off while the screen is split. The reflection is one texture, mirrored from the first
        /// player's camera and read by the water shader in screen space, so the second view would show the first view's
        /// mirror image; and a second planar camera is the most expensive thing that could be doubled on a phone.</summary>
        public static bool SuppressedForSplit;

        private void OnPreCull()
        {
            if (Driver == null || _refl == null || SuppressedForSplit) { Shader.SetGlobalFloat(ReflOnId, 0f); return; }
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
                ExcludeStrayWater();
            }
            _hiddenPanes.Clear();
            foreach (var pane in _zonePanes)
                if (pane != null && pane.enabled) { _hiddenPanes.Add(pane); pane.enabled = false; }
            bool previousCulling = GL.invertCulling;
            Shader.SetGlobalVector("_ReflectionClip", new Vector4(0, 1, 0, -planeY - .03f));
            Shader.SetGlobalVector("_ReflectionEye", _refl.transform.position);
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
            if (Application.isPlaying && Time.unscaledTime >= _nextAudit) Audit();
        }

        // ---- the mirror's own health check (Ty 2026-11-05: magenta over the water after a crash) ------------------------
        // Every AuditSec (30 s) while reflecting: read the mirror target back (async, one frame late, ~1.5 MB) and count texels
        // that are Unity's error / missing-material pink; when there are any, name every reflected renderer whose material
        // is missing, destroyed or has a shader this GPU cannot run. Costs nothing between audits.
        private const float AuditSec = 30f;
        private float _nextAudit, _nextWaterScan = 60f;
        private void Audit()
        {
            _nextAudit = Time.unscaledTime + AuditSec;
            // Water built after the first frame (a region, an event) gets the same check, once a minute: a scan of every
            // renderer is a millisecond or two, too much for each audit on a headset.
            if (Time.unscaledTime >= _nextWaterScan) { _nextWaterScan = Time.unscaledTime + 60f; ExcludeStrayWater(); }
            if (!SystemInfo.supportsAsyncGPUReadback) return;
            int mask = _refl.cullingMask;
            Vector3 eye = _refl.transform.position;
            UnityEngine.Rendering.AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError) return;
                var px = req.GetData<Color32>();
                int pink = 0;
                for (int i = 0; i < px.Length; i++) if (IsErrorPink(px[i])) pink++;
                if (pink == 0) return;
                var sb = new System.Text.StringBuilder();
                sb.Append($"[WaterReflection] MIRROR PINK: {pink} of {px.Length} texels ({100f * pink / px.Length:F1} %), mirror eye {eye}. Suspects:");
                int n = 0;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || ((1 << r.gameObject.layer) & mask) == 0) continue;
                    string why = Suspect(r);
                    if (why == null) continue;
                    if (n++ < 12) sb.Append($"\n  {Path(r.transform)} [{r.GetType().Name}, layer {r.gameObject.layer}, {r.bounds.size.magnitude:F0} m]: {why}");
                }
                sb.Append($"\n  ({n} suspect renderers)");
                Debug.LogWarning(sb.ToString());
            });
        }

        /// <summary>
        /// Water must not be drawn into its own reflection. Ty 2026-11-05 (iPad mini A17, 2026.1105.0804): magenta blocks
        /// over the island's water. The island builder (SeasideBuilder.B.Build) left "Ocean-lakes-water-lanes" on the
        /// default layer, so this camera drew the water, and the water's shader then sampled _ReflectionTex — the target
        /// being rendered — and _CameraDepthTexture, which this camera does not make. Reading the bound colour target is
        /// undefined: on the A17 it came back magenta, on a Mac it came back stale and looked right. The builders now set
        /// the layer; this puts anything that slips through on it, and says so, so it cannot come back silently.
        /// </summary>
        public static int ExcludeStrayWater()
        {
            int moved = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.layer == WaterLayer) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name != "FlyingGame/Water") continue;
                    Debug.LogError($"[WaterReflection] '{Path(r.transform)}' draws FlyingGame/Water on layer {r.gameObject.layer}, " +
                                   $"so the mirror camera would draw it into its own reflection. Moved to layer {WaterLayer}; fix its builder.");
                    r.gameObject.layer = WaterLayer; moved++;
                    break;
                }
            }
            return moved;
        }

        /// <summary>Unity's error and missing-material colour is (1, 0, 1); anything near it, with green well below both.</summary>
        public static bool IsErrorPink(Color32 c) => c.r > 200 && c.b > 200 && c.g < 60;

        /// <summary>Why this renderer would draw pink, or null.</summary>
        public static string Suspect(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats.Length == 0) return "no materials";
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (ReferenceEquals(m, null)) return $"material {i} missing";
                if (m == null) return $"material {i} destroyed";
                if (m.shader == null) return $"material {i} '{m.name}' has no shader";
                if (!m.shader.isSupported) return $"material {i} '{m.name}': shader '{m.shader.name}' not supported";
                if (m.shader.name.Contains("InternalErrorShader")) return $"material {i} '{m.name}': error shader";
                if (m.shader.name == "FlyingGame/Water") return $"material {i} '{m.name}': water drawn into its own reflection";
            }
            if (r is MeshRenderer && r.TryGetComponent(out MeshFilter f) && f.sharedMesh != null && f.sharedMesh.subMeshCount > mats.Length)
                return $"{f.sharedMesh.subMeshCount} submeshes, {mats.Length} materials";
            return null;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
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
