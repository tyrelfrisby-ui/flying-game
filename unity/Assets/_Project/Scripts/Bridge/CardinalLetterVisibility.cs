using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// One floating cardinal letter (Ty 2026-11-03): shown only when <see cref="SessionSettings.ShowCardinalLetters"/>
    /// is on (off by default), and drawn as if at infinity — just before the main camera culls, the letter is moved to
    /// <see cref="CardinalLetters.Centre"/> for that camera's position, so it rides with the camera and can never be
    /// flown up to or through. Its rotation is fixed to face back along its own direction, never the camera's.
    /// It switches the RENDERER, not the GameObject, so it never fights Side2DView, which hides the letters by
    /// deactivating them and turns back on only the ones it turned off.
    /// </summary>
    public sealed class CardinalLetterVisibility : MonoBehaviour
    {
        public Vector3 Direction = Vector3.forward;

        private MeshRenderer _renderer;
        private bool _scaled;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            transform.rotation = Quaternion.LookRotation(Direction, Vector3.up);
        }

        private void OnEnable() => Camera.onPreCull += Place;
        private void OnDisable() => Camera.onPreCull -= Place;

        private void LateUpdate()
        {
            bool show = SessionSettings.ShowCardinalLetters;
            if (_renderer != null && _renderer.enabled != show) _renderer.enabled = show;
        }

        private void Place(Camera cam)
        {
            if (cam == null || cam != Camera.main || _renderer == null || !_renderer.enabled) return;
            // Scale once the TextMesh has built its mesh, so the glyph itself spans CardHeightM, not its font box.
            if (!_scaled && _renderer.localBounds.size.y > 0f)
            {
                float k = (float)CardinalLetters.CardHeightM / _renderer.localBounds.size.y;
                transform.localScale = new Vector3(k, k, k);
                _scaled = true;
            }
            Vector3 p = cam.transform.position;
            var c = CardinalLetters.Centre(new FlyingGame.Core.MathTypes.Vec3(p.x, p.y, p.z),
                                           new FlyingGame.Core.MathTypes.Vec3(Direction.x, Direction.y, Direction.z));
            transform.position = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
        }
    }
}
