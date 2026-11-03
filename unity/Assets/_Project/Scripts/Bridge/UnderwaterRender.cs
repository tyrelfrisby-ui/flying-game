using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE UNDERWATER VIEW (P2d; Core UnderwaterViewMode decides, this draws). On the main camera, after QuestRig has put
    /// it at the eye. Each frame, from the camera's own position against the local water surface (the egress seam's
    /// LivePilotWater):
    ///   * the mode and its 0.25 s blend (Core), published as shader globals: _UwBlend, _UwSurfaceY, _UwSigma (σ = 3.912 /
    ///     6 m), _UwColor (a green-blue lake, E);
    ///   * a camera-centred inverted sphere, 0.3 m, drawn with FlyingGame/Underwater after the opaque world: the fog on the
    ///     underwater part of every ray, the caustics, the sky gone to the water's colour;
    ///   * FlyingGame/Water draws the surface from below (Snell's window) and, in a headset, see-through from above
    ///     (_WaterSeeThrough), so a sinking cockpit stays visible from the surface;
    ///   * the things he has to find stay findable: a small pale-yellow marker (3 cm, E) pulsing at 1 Hz on each exit grab
    ///     point still to be worked — belt releases, the latch, the jettison / release, the door handle — drawn after the
    ///     fog so it is not lost in it. At arm's length the fog alone leaves 72 % of an object's contrast (Core), so the
    ///     marker is a cue, not a necessity: the "slight emissive cue" asked for.
    /// Sound under water is PilotWaterSound's (it reads <see cref="Blend01"/>).
    /// </summary>
    [DefaultExecutionOrder(10010)]
    [RequireComponent(typeof(Camera))]
    public sealed class UnderwaterRender : MonoBehaviour
    {
        public static float Blend01 { get; private set; }
        public static bool EyeUnder { get; private set; }

        /// <summary>The lake's own colour under water (E: a green-blue, temperate lake).</summary>
        public static readonly Color WaterColour = new(0.06f, 0.20f, 0.22f, 1f);
        public const float SphereRadiusM = 0.3f, CueSizeM = 0.03f;

        private static readonly int BlendId = Shader.PropertyToID("_UwBlend"), SurfaceId = Shader.PropertyToID("_UwSurfaceY"),
            SigmaId = Shader.PropertyToID("_UwSigma"), ColourId = Shader.PropertyToID("_UwColor"), SeeThroughId = Shader.PropertyToID("_WaterSeeThrough");

        private readonly UnderwaterViewMode _mode = new();
        private GameObject _sphere;
        private Renderer _sphereR;
        private readonly GameObject[] _cues = new GameObject[8];
        private Material _cueMat;
        private readonly Vector3[] _cuePts = new Vector3[8];
        private bool _shaderMissingLogged, _wasUnder;
        private float _surface;

        private void OnDisable()
        {
            Shader.SetGlobalFloat(BlendId, 0f);
            Blend01 = 0f; EyeUnder = false;
            if (_sphere != null) _sphere.SetActive(false);
            foreach (GameObject c in _cues) if (c != null) c.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_sphere != null) Destroy(_sphere);
            foreach (GameObject c in _cues) if (c != null) Destroy(c);
        }

        private void LateUpdate()
        {
            Vector3 eye = transform.position;
            double? s = LivePilotWater.Instance.SurfaceUpAt(eye.x, eye.z, eye.y);
            _mode.Step(eye.y, s, Time.deltaTime);
            if (s.HasValue) _surface = (float)s.Value;
            Blend01 = (float)_mode.Blend01;
            EyeUnder = _mode.Under;
            if (EyeUnder != _wasUnder)
            {
                _wasUnder = EyeUnder;
                Debug.Log($"[VrEgress] eye {(EyeUnder ? "UNDER" : "out of")} the water ({eye.y - _surface:F2} m) — underwater view {(EyeUnder ? "on" : "off")}");
            }

            Shader.SetGlobalFloat(BlendId, Blend01);
            Shader.SetGlobalFloat(SurfaceId, _surface);
            Shader.SetGlobalFloat(SigmaId, (float)UnderwaterViewMode.FogSigmaPerM);
            Shader.SetGlobalColor(ColourId, WaterColour);
            Shader.SetGlobalFloat(SeeThroughId, XrHeadset.Active ? 1f : 0f);

            bool on = Blend01 > 0.001f;
            if (on && _sphere == null) Build();
            if (_sphere != null)
            {
                _sphere.SetActive(on && _sphereR != null);
                if (on) _sphere.transform.SetPositionAndRotation(eye, Quaternion.identity);
            }
            Cues(on && Blend01 > 0.5f);
        }

        private void Build()
        {
            Shader sh = Shader.Find("FlyingGame/Underwater");
            if (sh == null)
            {
                if (!_shaderMissingLogged) Debug.LogWarning("[VrEgress] FlyingGame/Underwater not in the build — no underwater fog (the water's underside still draws)");
                _shaderMissingLogged = true;
                return;
            }
            _sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere.name = "Underwater view";
            Destroy(_sphere.GetComponent<Collider>());
            _sphere.transform.localScale = Vector3.one * (2f * SphereRadiusM);
            _sphereR = _sphere.GetComponent<MeshRenderer>();
            _sphereR.sharedMaterial = new Material(sh);
            _sphereR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _sphereR.receiveShadows = false;
            // Around the camera always: never culled by its own bounds.
            _sphere.GetComponent<MeshFilter>().mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
        }

        private void Cues(bool on)
        {
            VrCockpitHands hands = VrCockpitHands.Current;
            int n = on && hands != null ? hands.ExitCuePoints(_cuePts) : 0;
            if (n > 0 && _cueMat == null)
            {
                _cueMat = EgressAir.Transparent(new Color(1f, 0.95f, 0.55f, 0.6f));
                _cueMat.renderQueue = 3100;   // after the fog (2990) and the water (3000)
            }
            float pulse = 0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2f * Mathf.PI));
            if (_cueMat != null) _cueMat.color = new Color(1f, 0.95f, 0.55f, pulse);
            for (int i = 0; i < _cues.Length; i++)
            {
                if (i >= n) { if (_cues[i] != null && _cues[i].activeSelf) _cues[i].SetActive(false); continue; }
                if (_cues[i] == null) _cues[i] = EgressAir.Prim(PrimitiveType.Sphere, "Exit cue", null, Vector3.zero, Vector3.one * CueSizeM, _cueMat);
                _cues[i].SetActive(true);
                _cues[i].transform.position = _cuePts[i];
            }
        }
    }
}
