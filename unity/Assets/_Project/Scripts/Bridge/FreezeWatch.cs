using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Says what the frame was doing when it stopped. Ty, 2026-10-31: "the pitts flys for about 30 seconds then
    /// freezes" — with a screenshot showing perfectly ordinary flight values, which means the simulation was fine
    /// and the frame was not. Sixty seconds of the same flight headless cost at most 288 ms of CPU per sim-second
    /// and never threw, so whatever stops the frame is above the sim and cannot be found from the physics side.
    ///
    /// So: every frame longer than <see cref="SlowFrameMs"/> prints one line with the things that could be doing
    /// it — where the aeroplane is, how far the camera is from the ground, how many forest chunks are live and
    /// queued and what the worst chunk build cost, how many bubbles were drawn, and the managed heap. One line per
    /// slow frame, at most <see cref="MaxLines"/> of them, so a genuinely wedged frame leaves evidence instead of
    /// silence. It costs one float comparison a frame when nothing is wrong.
    /// </summary>
    [DefaultExecutionOrder(3000)]   // last: after the camera, the streamers and the recorder have all had the frame
    public sealed class FreezeWatch : MonoBehaviour
    {
        public const float SlowFrameMs = 300f;
        public const int MaxLines = 60;

        public FlightSimDriver Driver;
        private int _lines;
        private float _worstMs;
        private int _frames;
        private float _since;

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _frames++;
            if (ms > _worstMs) _worstMs = ms;
            _since += Time.unscaledDeltaTime;
            if (ms <= SlowFrameMs || _lines >= MaxLines) return;
            _lines++;
            Report(ms, "SLOW FRAME");
        }

        /// <summary>A heartbeat as well as the slow frames: a freeze that never returns prints nothing at all, so
        /// the last heartbeat before the silence is itself the evidence.</summary>
        private void LateUpdate()
        {
            if (_since < 5f) return;
            Report(_worstMs, $"{_frames / _since:F0} fps over 5 s, worst");
            _since = 0; _frames = 0; _worstMs = 0;
        }

        private void Report(float ms, string what)
        {
            var sim = Driver != null ? Driver.Sim : null;
            string plane = "no sim";
            if (sim?.Aircraft != null)
            {
                var p = sim.Aircraft.State.Position;
                double ground = FlyingGame.Core.WorldTerrain.GroundHeightAt(p.X, p.Y);
                plane = $"at ({p.X,8:F0},{p.Y,8:F0}) alt {-p.Z,7:F0} agl {-p.Z - ground,7:F0}";
            }
            string cam = "no camera";
            if (Camera.main != null)
            {
                Vector3 c = Camera.main.transform.position;
                double cg = FlyingGame.Core.WorldTerrain.GroundHeightAt(c.z, c.x);
                cam = $"camera {c.y - (float)cg,7:F0} above ground";
            }
            var forest = Object.FindAnyObjectByType<ForestStreamer>();
            string trees = forest != null ? $"forest live {forest.Depth.live,4} queued {forest.Depth.queued,4} worst {forest.WorstFrameMs,6:F1} ms" : "no forest";
            var bubbles = Object.FindAnyObjectByType<BubbleField>();
            string bub = bubbles != null ? $"bubbles {bubbles.DrawnCount,5}" : "no bubbles";
            Debug.Log($"[Freeze] {what} {ms,7:F0} ms  {plane}  {cam}  {trees}  {bub}  heap {System.GC.GetTotalMemory(false) / 1048576,4} MB");
        }
    }
}
