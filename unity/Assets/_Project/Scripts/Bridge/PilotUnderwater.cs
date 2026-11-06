using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The pilot's view under water (Ty t922u, 2026-11-06; the rule and its numbers are Core <see cref="UnderwaterView"/>).
    /// On the main camera beside <see cref="WaterReflection"/>. Each frame it asks the lake under the eye and, when
    /// <see cref="UnderwaterView.ModeFor"/> says so, draws Hidden/FlyingGame/PilotUnderwater on a small box around the
    /// eye for THIS camera's render only: the renderer is switched on in OnPreCull and off again in OnPostRender, so the
    /// planar reflection, a split-screen second view and the clip recorder (whose camera has no depth texture of its own,
    /// see <c>_WaterSceneDepthOff</c>) never draw it, and no outside view ever does.
    /// Same component on the iPhone, the Mac and the Quest (the shader carries the stereo macros).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PilotUnderwater : MonoBehaviour
    {
        private static readonly int ModeId = Shader.PropertyToID("_PilotWaterMode");
        private static readonly int WaterYId = Shader.PropertyToID("_PilotWaterY");
        private static readonly int KId = Shader.PropertyToID("_PilotWaterK");
        private static readonly int ColorId = Shader.PropertyToID("_PilotWaterColor");
        private static readonly int NearId = Shader.PropertyToID("_PilotWaterNear");
        private static readonly int SunsetId = Shader.PropertyToID("_SunsetOn");

        /// <summary>Lake water lit from above, seen from inside it (day); at sunset it is far darker.</summary>
        public static readonly Color DayColor = new Color(.06f, .19f, .22f), SunsetColor = new Color(.018f, .03f, .038f);

        /// <summary>EDITOR TOOLS ONLY (UnderwaterCockpitShots): the before/after switch. Never set in the game.</summary>
        public static bool DisabledForTesting;
        /// <summary>EDITOR TOOLS ONLY: treat the camera as the seat eye without a ChaseCamera.</summary>
        public static bool? InCockpitForTesting;

        private Camera _cam;
        private MeshRenderer _box;
        private UnderwaterView.Mode _mode;
        public UnderwaterView.Mode CurrentMode => _mode;

        private void Awake() => Ensure();

        private void Ensure()
        {
            if (_box != null) return;
            _cam = GetComponent<Camera>();
            _cam.depthTextureMode |= DepthTextureMode.Depth;
            var shader = Shader.Find("Hidden/FlyingGame/PilotUnderwater");
            if (shader == null) { Debug.LogError("[PilotUnderwater] shader Hidden/FlyingGame/PilotUnderwater not in the build"); enabled = false; return; }
            var go = new GameObject("PilotUnderwaterBox") { hideFlags = HideFlags.DontSave };
            go.layer = WaterReflection.WaterLayer;   // never in the planar mirror's mask
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh();
            _box = go.AddComponent<MeshRenderer>();
            _box.sharedMaterial = new Material(shader) { name = "PilotUnderwater" };
            _box.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _box.receiveShadows = false;
            _box.enabled = false;
            Shader.SetGlobalFloat(ModeId, 0f);
        }

        private static Mesh BoxMesh()
        {
            var m = new Mesh { name = "PilotUnderwaterBox" };
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
            m.vertices = v;
            // Outward-wound faces; the shader culls front faces, so the eye inside sees every face's back.
            m.triangles = new[]
            {
                0, 2, 3, 0, 3, 1,   4, 5, 7, 4, 7, 6,   0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3,   0, 4, 6, 0, 6, 2,   1, 3, 7, 1, 7, 5,
            };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        private void OnPreCull()
        {
            Ensure();
            if (_box == null) return;
            _mode = UnderwaterView.Mode.Off;
            if (!DisabledForTesting)
            {
                Vector3 eye = transform.position;
                // Unity (east, up, north) → sim (north, east, up).
                double? water = FloatHydro.WaterSurfaceAt(eye.z, eye.x, eye.y);
                _mode = UnderwaterView.ModeFor(InCockpitForTesting ?? ChaseCamera.InCockpit, water, eye.y);
                if (_mode != UnderwaterView.Mode.Off)
                {
                    // The box only has to lie between the near and far planes; ZTest Always does the rest.
                    float half = Mathf.Max(.1f, _cam.nearClipPlane * 4f);
                    _box.transform.localScale = Vector3.one * half;
                    _box.transform.localRotation = Quaternion.identity;
                    _box.transform.localPosition = Vector3.zero;
                    Shader.SetGlobalFloat(WaterYId, (float)water.Value);
                    Shader.SetGlobalFloat(KId, (float)UnderwaterView.ExtinctionPerM);
                    Shader.SetGlobalVector(NearId, new Vector4((float)UnderwaterView.NearFieldFullM, (float)UnderwaterView.NearFieldZeroM, 0, 0));
                    Shader.SetGlobalColor(ColorId, Color.Lerp(DayColor, SunsetColor, Shader.GetGlobalFloat(SunsetId)));
                }
            }
            _box.enabled = _mode != UnderwaterView.Mode.Off;
            Shader.SetGlobalFloat(ModeId, (float)(int)_mode);
        }

        private void OnPostRender()
        {
            if (_box != null) _box.enabled = false;
            Shader.SetGlobalFloat(ModeId, 0f);   // the water's underside rule is for this camera alone
        }

        private void OnDisable()
        {
            if (_box != null) _box.enabled = false;
            Shader.SetGlobalFloat(ModeId, 0f);
        }

        /// <summary>EDITOR TOOLS ONLY: Awake does not run on a component added in edit mode.</summary>
        public void SetActiveForTesting() => Ensure();
        /// <summary>EDITOR TOOLS ONLY: the camera's own OnPreCull/OnPostRender as Camera.Render() runs them.</summary>
        public void PreCullForTesting() => OnPreCull();
        public void PostRenderForTesting() => OnPostRender();
    }
}
