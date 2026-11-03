using FlyingGame.Core;
using FlyingGame.Core.Wreck;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Bridge
{
    /// <summary>Picture of CrashRelay's single slick. The shared sim clock makes growth/drift pause safe.
    /// Installed on StructuralDamage's object, whose relay is replaced on every repair/new flight.</summary>
    public sealed class FuelSlickVisual : MonoBehaviour
    {
        private StructuralDamage _damage;
        private GameObject _sheet;
        private Material _material;
        private Mesh _mesh;
        private MeshRenderer _renderer;

        private void Awake() { _damage = GetComponent<StructuralDamage>(); }
        private void LateUpdate()
        {
            var relay = _damage != null ? _damage.Relay : null;
            Show(relay != null ? relay.Slick : null,
                relay != null ? Atmosphere.SimTimeSec - relay.SlickCreatedSimS : 0);
        }

        /// <summary>Also used by the deterministic editor render fixture, with explicit simulation ages.</summary>
        public void Show(FuelSlick slick, double elapsedSimS)
        {
            if (slick == null || slick.RadiusAt(elapsedSimS) <= 0)
            { if (_sheet != null) _sheet.SetActive(false); return; }
            if (_sheet == null)
            {
                Shader shader = Shader.Find("FlyingGame/FuelSlick");
                if (shader == null) throw new System.InvalidOperationException("FuelSlick shader missing from build");
                _sheet = new GameObject("Fuel sheen");
                _sheet.transform.SetParent(transform, false);
                _mesh = new Mesh { name = "Fuel sheen quad" };
                _mesh.vertices = new[] { new Vector3(-1,0,-1), new Vector3(-1,0,1), new Vector3(1,0,1), new Vector3(1,0,-1) };
                _mesh.uv = new[] { new Vector2(-1,-1), new Vector2(-1,1), new Vector2(1,1), new Vector2(1,-1) };
                _mesh.triangles = new[] { 0,1,2,0,2,3 }; _mesh.RecalculateBounds();
                _sheet.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _sheet.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
                _material = new Material(shader); _renderer.sharedMaterial = _material;
            }
            _sheet.SetActive(true);
            Vector3 centre = CoordinateMap.ToUnity(slick.CentreAt(elapsedSimS));
            // Same flat surface datum as the real water mesh; depth bias is in the shader, not a raised disc.
            _sheet.transform.position = centre;
            _sheet.transform.rotation = Quaternion.identity;
            _sheet.transform.localScale = Vector3.one * (float)slick.RadiusAt(elapsedSimS);
            _material.SetFloat("_SurfaceY", centre.y);
        }
        private void OnDisable() { if (_sheet != null) _sheet.SetActive(false); }
        private static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        private void OnDestroy()
        {
            Release(_sheet); Release(_material); Release(_mesh);
        }
    }
}
