using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Single binocular surface 1.5 m ahead of the head, within the central 20 degrees. No touch controls.
    /// HudMode: off = hidden; minimal = IAS kt, altitude ft MSL, true heading. Unknown = hidden.</summary>
    public sealed class VrWorldHud : MonoBehaviour
    {
        private GameObject _root;
        private TextMesh _text;
        private MeshRenderer _textRenderer;
        private Transform _backing;
        private FlightSimDriver _driver;
        private void LateUpdate() => Refresh();

        /// <summary>Also called explicitly before edit-mode eye renders; no lifecycle messages required.</summary>
        public void Refresh()
        {
            bool visible = VrMode.On && VrDistortion.MinimalHud(VrMode.Lens.HudMode);
            if (!visible) { if (_root != null) _root.SetActive(false); return; }
            if (_driver == null)
            {
                var chase = GetComponent<ChaseCamera>();
                _driver = chase != null ? chase.Driver : null;
            }
            if (_root == null)
            {
                // new Material(null) throws, and from LateUpdate that would be every frame (CLAUDE.md, "A shader
                // name in a list is not a shader in the build"): say so once and stay hidden.
                Shader textShader = Resources.Load<Shader>("VrHudText"), backingShader = Resources.Load<Shader>("VrHudBacking");
                if (textShader == null || backingShader == null)
                {
                    if (!_missingLogged) Debug.LogError("[VR] missing Resources/VrHudText or VrHudBacking shader; no VR HUD.");
                    _missingLogged = true;
                    return;
                }
                _root = new GameObject("VR minimal HUD");
                _root.transform.SetParent(transform, false);
                _root.transform.localPosition = new Vector3(0, -.24f, 1.5f);
                _text = _root.AddComponent<TextMesh>();
                _text.anchor = TextAnchor.MiddleCenter; _text.alignment = TextAlignment.Center;
                _text.font = UiFont.Get(); _text.fontSize = 64; _text.characterSize = .01f;
                _text.color = Color.white;
                _textMaterial = new Material(textShader);
                _textMaterial.mainTexture = _text.font.material.mainTexture;
                _textRenderer = _text.GetComponent<MeshRenderer>();
                _textRenderer.sharedMaterial = _textMaterial;
                // Both surfaces overlay cockpit depth inside each eye, before lens correction.
                var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backing.name = "HUD contrast"; backing.transform.SetParent(_root.transform, false);
                _backing = backing.transform;
                var collider = backing.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
                _backingMaterial = new Material(backingShader);
                backing.GetComponent<MeshRenderer>().sharedMaterial = _backingMaterial;
            }
            _root.SetActive(_driver != null && _driver.Sim != null);
            if (_driver == null || _driver.Sim == null) return;
            Vector3 nose = _driver.transform.forward;
            float heading = (Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg + 360) % 360;
            string readout = $"{_driver.IasMs * 1.9438:F0} kt  {_driver.AltitudeM * 3.28084:F0} ft  {heading:000}°";
            if (_text.text == readout) return;
            _text.characterSize = .01f;
            _text.text = readout;
            // Text and renderer share the root transform, so localBounds are root-local metres.
            Bounds bounds = _textRenderer.localBounds;
            // Reserve 1 cm padding on each side within a 0.5 m total width (~±9.5°).
            if (bounds.size.x > .48f)
            {
                float scale = .48f / bounds.size.x;
                _text.characterSize *= scale;
                bounds = new Bounds(bounds.center * scale, bounds.size * scale);
            }
            _backing.localPosition = new Vector3(bounds.center.x, bounds.center.y, .003f);
            _backing.localScale = new Vector3(bounds.size.x + .02f, bounds.size.y + .012f, 1);
        }
        private Material _backingMaterial, _textMaterial;
        private bool _missingLogged;
        private void OnDestroy()
        {
            if (Application.isPlaying) { Destroy(_root); Destroy(_backingMaterial); Destroy(_textMaterial); }
            else { DestroyImmediate(_root); DestroyImmediate(_backingMaterial); DestroyImmediate(_textMaterial); }
        }
    }
}
