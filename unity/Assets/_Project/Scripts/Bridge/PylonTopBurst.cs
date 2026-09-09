using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The top of a racing pylon after a wing hits it: the inflated top shoots straight up (compressed air lets go
    /// downward) to 300 ft, then collapses — the fabric deflates and tumbles down, landing crumpled. Reset puts
    /// the top back on its pylon for the next race.
    /// </summary>
    public sealed class PylonTopBurst : MonoBehaviour
    {
        public const float ApexM = 91.44f;   // 300 ft
        private Vector3 _home; private Quaternion _homeRot; private Vector3 _homeScale; private float _groundY;
        private Vector3 _vel; private bool _flying, _landed, _armed; private float _spin;

        private void Awake() { _home = transform.position; _homeRot = transform.rotation; _homeScale = transform.localScale; _groundY = _home.y - 45f; }
        public void SetGround(float groundY) { _groundY = groundY; }

        public void Launch()
        {
            if (_flying) return;
            _armed = true; _flying = true; _landed = false;
            _vel = new Vector3(Random.Range(-1.5f, 1.5f), Mathf.Sqrt(2f * 9.81f * ApexM), Random.Range(-1.5f, 1.5f));
            _spin = Random.Range(-40f, 40f);
        }

        public void ResetTop()
        {
            _flying = false; _landed = false; _armed = false;
            transform.SetPositionAndRotation(_home, _homeRot); transform.localScale = _homeScale;
        }

        private void Update()
        {
            if (!_flying || _landed) return;
            float dt = Time.deltaTime;
            _vel.y -= 9.81f * dt;
            transform.position += _vel * dt;
            if (_vel.y < 0f)
            {
                // Falling: collapse — the shell deflates (thin and short) and tumbles.
                float fall = Mathf.Clamp01(-_vel.y / 30f);
                transform.localScale = new Vector3(_homeScale.x * (1f - 0.55f * fall), _homeScale.y * (1f - 0.8f * fall), _homeScale.z * (1f - 0.55f * fall));
                transform.Rotate(_spin * dt, 0f, _spin * 0.6f * dt, Space.World);
                _vel.x *= 1f - 0.4f * dt; _vel.z *= 1f - 0.4f * dt;
                _vel.y += 4f * dt;   // a deflating shell falls slower than a stone
                if (transform.position.y <= _groundY + 0.5f)
                {
                    transform.position = new Vector3(transform.position.x, _groundY + 0.5f, transform.position.z);
                    transform.rotation = Quaternion.Euler(Random.Range(70f, 100f), Random.Range(0f, 360f), 0f);
                    transform.localScale = new Vector3(_homeScale.x * 0.5f, _homeScale.y * 0.15f, _homeScale.z * 0.5f);
                    _landed = true; _flying = false;
                }
            }
        }
    }
}
