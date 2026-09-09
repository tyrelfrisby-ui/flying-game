using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Turns about the world up axis at a fixed rate (the race numbers: one turn every 5 s).</summary>
    public sealed class Spin : MonoBehaviour
    {
        public float DegPerSec = 72f;
        private void Update() { transform.Rotate(0f, DegPerSec * Time.deltaTime, 0f, Space.World); }
    }
}
