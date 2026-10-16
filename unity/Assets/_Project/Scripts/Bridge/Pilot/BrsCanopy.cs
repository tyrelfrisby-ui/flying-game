using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The whole-aircraft parachute you can SEE (owner 2026-10-16). The physics lives in
    /// <see cref="BallisticRecovery"/> in Core; this only draws what that model says is happening — the
    /// rocket going up and away, the bag, the lines paying out, and a 55 ft canopy filling above the
    /// aeroplane, with risers down to the bridle on the airframe.
    ///
    /// It hangs from the WORLD-UP side of the aircraft rather than from its own nose, because the aeroplane
    /// underneath is swinging and the canopy is not: a canopy is a drag device at the top of the risers, and
    /// it goes where the air puts it. The size on screen is the real one — the Cirrus canopy is 2,400 ft²
    /// projected, which is a 55 ft disc, and it should look alarmingly big over a 38 ft wingspan.
    /// </summary>
    public sealed class BrsCanopy : MonoBehaviour
    {
        public FlightSimDriver Driver;

        private const int Risers = 6;
        private static Mesh _dome;
        private GameObject _root, _canopy, _rocket, _bag;
        private readonly GameObject[] _risers = new GameObject[Risers];
        private float _projectedRadiusM = 8.4f;   // 55 ft across
        private float _riserLenM = 18f;

        private void Update()
        {
            var ac = Driver != null ? Driver.Sim?.Aircraft : null;
            if (ac?.Config?.Brs is null) { Hide(); return; }
            BallisticRecovery brs = ac.Brs;
            if (!brs.Out) { Hide(); return; }

            Build();
            var cfg = ac.Config.Brs;
            _projectedRadiusM = Mathf.Sqrt((float)cfg.CanopyProjectedAreaM2 / Mathf.PI);
            _riserLenM = _projectedRadiusM * 2.2f;     // CAPS risers are about a canopy diameter long

            // The bridle, on the airframe, in world space.
            Vector3 bridle = transform.TransformPoint(Body(cfg.BridleVec()));
            // The canopy rides above, leaning into the airflow — the way the aeroplane is actually moving.
            Vector3 vW = Driver.WorldVelocityUnity;
            Vector3 up = vW.sqrMagnitude > 4f ? -vW.normalized : Vector3.up;
            up = Vector3.Slerp(Vector3.up, up, 0.75f).normalized;

            float fill = Fill(brs);
            float deployed = Mathf.Clamp01((float)brs.TimeSec / (float)BallisticRecovery.RocketSec);
            float lineOut = brs.Current == BallisticRecovery.Phase.Rocket ? deployed * 0.35f
                          : brs.Current == BallisticRecovery.Phase.LineStretch
                            ? 0.35f + 0.65f * Mathf.Clamp01(((float)brs.TimeSec - (float)BallisticRecovery.RocketSec)
                                                            / (float)(BallisticRecovery.LineStretchSec - BallisticRecovery.RocketSec))
                            : 1f;
            Vector3 apex = bridle + up * (_riserLenM * lineOut);

            bool torn = brs.Current == BallisticRecovery.Phase.Failed;
            // A streamer is a long thin rag, not a canopy: it never opens, and it should look like it.
            float r = torn ? _projectedRadiusM * 0.08f : _projectedRadiusM * Mathf.Max(0.04f, fill);
            _canopy.SetActive(fill > 0.02f || torn);
            _canopy.transform.position = apex;
            _canopy.transform.up = up;
            _canopy.transform.localScale = new Vector3(r, torn ? _projectedRadiusM * 0.9f : r * 0.62f, r);

            // The rocket is gone in the first second; the bag rides up with it while the lines pay out.
            bool early = brs.Current == BallisticRecovery.Phase.Rocket || brs.Current == BallisticRecovery.Phase.LineStretch;
            _rocket.SetActive(brs.Current == BallisticRecovery.Phase.Rocket);
            _rocket.transform.position = bridle + up * (_riserLenM * (0.4f + deployed * 1.3f));
            _bag.SetActive(early);
            _bag.transform.position = apex;

            for (int i = 0; i < Risers; i++)
            {
                float a = Mathf.PI * 2f * i / Risers;
                Vector3 skirt = apex + _canopy.transform.right * (Mathf.Cos(a) * r * 0.92f)
                                     + _canopy.transform.forward * (Mathf.Sin(a) * r * 0.92f);
                Place(_risers[i], bridle, skirt, 0.05f);
            }
        }

        private static float Fill(BallisticRecovery brs) => brs.Current switch
        {
            BallisticRecovery.Phase.Full => 1f,
            BallisticRecovery.Phase.Inflating =>
                Mathf.Clamp01(((float)brs.TimeSec - (float)BallisticRecovery.LineStretchSec)
                              / (float)(BallisticRecovery.FullSec - BallisticRecovery.LineStretchSec)),
            _ => 0f,
        };

        /// <summary>Sim body axes (x fore, y right, z DOWN) to Unity local (x right, y up, z fore).</summary>
        private static Vector3 Body(Core.MathTypes.Vec3 v) => new((float)v.Y, -(float)v.Z, (float)v.X);

        private void Place(GameObject go, Vector3 a, Vector3 b, float thick)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) { go.SetActive(false); return; }
            go.SetActive(true);
            go.transform.position = (a + b) * 0.5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            go.transform.localScale = new Vector3(thick, len * 0.5f, thick);
        }

        private void Build()
        {
            if (_root != null) { _root.SetActive(true); return; }
            _dome ??= EgressAir.Dome(28, 8);
            _root = new GameObject("BrsCanopy");
            ReplayActors.Register(_root);              // it has to be there in the replay too
            Material fabric = EgressAir.Unlit(new Color(0.93f, 0.40f, 0.12f));   // CAPS canopies are orange and white
            Material line = EgressAir.Unlit(new Color(0.10f, 0.10f, 0.12f));
            _canopy = EgressAir.MeshObject("Canopy", _dome, fabric, _root.transform);
            _rocket = EgressAir.Prim(PrimitiveType.Capsule, "Rocket", _root.transform, Vector3.zero,
                                     new Vector3(0.12f, 0.35f, 0.12f), EgressAir.Unlit(new Color(0.85f, 0.85f, 0.88f)));
            _bag = EgressAir.Prim(PrimitiveType.Cube, "Bag", _root.transform, Vector3.zero,
                                  new Vector3(0.5f, 0.9f, 0.5f), EgressAir.Unlit(new Color(0.80f, 0.78f, 0.72f)));
            for (int i = 0; i < Risers; i++)
                _risers[i] = EgressAir.Prim(PrimitiveType.Cylinder, $"Riser{i}", _root.transform, Vector3.zero, Vector3.one, line);
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }
    }
}
