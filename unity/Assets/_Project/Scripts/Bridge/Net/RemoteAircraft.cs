using System.Collections.Generic;
using FlyingGame.Core.DataContracts;
using UnityEngine;

namespace FlyingGame.Bridge.Net
{
    /// <summary>
    /// One remote pilot's aircraft: an AirframeBuilder visual of their type, a floating name tag, and
    /// a snapshot buffer rendered ~150 ms behind the newest packet (smooth at 10 Hz). Past the newest
    /// snapshot it dead-reckons with the last velocity for up to 1 s, then holds. Control surfaces
    /// follow the relayed deflections; WingsGone hides the wing meshes (fuselage/tail keep flying).
    /// </summary>
    public sealed class RemoteAircraft : MonoBehaviour
    {
        public string Id { get; private set; }
        public string PilotName { get; private set; }
        public string AircraftId { get; private set; }
        public bool WingsGone { get; private set; }
        public bool PilotOut { get; private set; }

        private const float InterpDelay = 0.15f;   // s behind the newest snapshot
        private const float MaxExtrapolate = 1.0f;  // s of dead-reckoning past the newest snapshot
        private const int MaxSnapshots = 24;

        private struct Snap
        {
            public float T;                 // local receive time (realtime)
            public Vector3 P, V;
            public Quaternion Q;
            public float Ail, Ele, Rud, Spoil, Flap;
            public bool W, O;
        }

        private readonly List<Snap> _buf = new();
        private readonly AirframeBuilder _builder = new();
        private readonly List<GameObject> _wingParts = new();
        private float _halfSpan = 5f;
        private TextMesh _tag;
        private Transform _tagT;
        private bool _hasPose;
        private string _builtAc;

        private static readonly Dictionary<string, AircraftConfig> Configs = new();

        public static RemoteAircraft Create(string id, string name, string ac)
        {
            var go = new GameObject($"Remote-{name}-{id}");
            go.transform.position = new Vector3(0f, -5000f, 0f);   // out of sight until the first state arrives
            var r = go.AddComponent<RemoteAircraft>();
            r.Id = id;
            r.SetName(name);
            r.SetAircraft(ac);
            return r;
        }

        public void SetName(string name)
        {
            PilotName = string.IsNullOrEmpty(name) ? "Pilot" : name;
            if (_tag == null)
            {
                var tgo = new GameObject("NameTag");
                _tagT = tgo.transform;
                _tagT.SetParent(transform, false);
                _tag = tgo.AddComponent<TextMesh>();
                _tag.font = UiFont.Get();
                _tag.GetComponent<MeshRenderer>().sharedMaterial = _tag.font.material;
                _tag.fontSize = 40;
                _tag.anchor = TextAnchor.LowerCenter;
                _tag.alignment = TextAlignment.Center;
                _tag.color = new Color(1f, 1f, 1f, 0.9f);
            }
            _tag.text = PilotName;
        }

        public void SetAircraft(string ac)
        {
            AircraftId = string.IsNullOrEmpty(ac) ? "glider-2-33-like" : ac;
            if (AircraftId == _builtAc) return;
            _builtAc = AircraftId;
            AircraftConfig cfg = LoadConfig(AircraftId) ?? LoadConfig("glider-2-33-like");
            _wingParts.Clear();
            if (cfg == null) return;
            try
            {
                _halfSpan = Mathf.Max(2f, _builder.Build(transform, cfg));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] airframe build failed for {AircraftId}: {e.Message}");
            }
            // Wing meshes by name (AirframeBuilder: "Wing:<id>", control rows by surface id, slats, spoilers).
            foreach (Transform c in transform)
            {
                string n = c.name;
                string l = n.ToLowerInvariant();
                if (n.StartsWith("Wing:") || l.Contains("aileron") || l.Contains("flap") || n == "Slat" || n.StartsWith("Spoiler"))
                {
                    _wingParts.Add(c.gameObject);
                }
            }
            if (_tagT != null) _tagT.localPosition = Vector3.up * (_halfSpan * 0.35f + 1.5f);
            ApplyWings();
        }

        private static AircraftConfig LoadConfig(string id)
        {
            if (Configs.TryGetValue(id, out AircraftConfig c)) return c;
            try { c = UnityAircraftConfigLoader.LoadFromStreamingAssets(id); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] no aircraft config for '{id}': {e.Message}");
                c = null;
            }
            Configs[id] = c;
            return c;
        }

        /// <summary>Queue a received state (main thread).</summary>
        public void Push(NetState s)
        {
            if (s?.p == null || s.p.Length < 3 || s.q == null || s.q.Length < 4) return;
            var snap = new Snap
            {
                T = Time.realtimeSinceStartup,
                P = new Vector3(s.p[0], s.p[1], s.p[2]),
                Q = new Quaternion(s.q[0], s.q[1], s.q[2], s.q[3]),
                V = s.v != null && s.v.Length >= 3 ? new Vector3(s.v[0], s.v[1], s.v[2]) : Vector3.zero,
            };
            if (snap.Q.x == 0 && snap.Q.y == 0 && snap.Q.z == 0 && snap.Q.w == 0) snap.Q = Quaternion.identity;
            if (s.d != null && s.d.Length >= 5) { snap.Ail = s.d[0]; snap.Ele = s.d[1]; snap.Rud = s.d[2]; snap.Spoil = s.d[3]; snap.Flap = s.d[4]; }
            if (s.f != null) { snap.W = s.f.w != 0; snap.O = s.f.o != 0; }

            _buf.Add(snap);
            if (_buf.Count > MaxSnapshots) _buf.RemoveAt(0);

            if (!_hasPose)
            {
                transform.SetPositionAndRotation(snap.P, snap.Q);
                _hasPose = true;
            }
            if (snap.W != WingsGone) { WingsGone = snap.W; ApplyWings(); }
            PilotOut = snap.O;
        }

        private void ApplyWings()
        {
            foreach (GameObject g in _wingParts) if (g != null) g.SetActive(!WingsGone);
        }

        private const float StaleSec = 8f;         // no state for this long: the peer is gone (crashed app, reinstall) — hide it
        private bool _shown = true;
        private void Show(bool on)
        {
            if (on == _shown) return;
            _shown = on;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        private void LateUpdate()
        {
            // A peer that has never sent a state, or has stopped sending, must not stand as a ghost shell at the world
            // origin / its last position (owner 2026-09-13: "green shells around the Pitts and Decathlon" — a stale copy
            // of his own earlier session parked on the runway spawn).
            float now = Time.realtimeSinceStartup;
            if (_buf.Count == 0 || now - _buf[_buf.Count - 1].T > StaleSec) { Show(false); return; }
            Show(true);
            float rt = now - InterpDelay;
            Snap newest = _buf[_buf.Count - 1];

            Vector3 pos; Quaternion rot; Snap surf = newest;
            if (rt >= newest.T)
            {
                // Beyond the buffer: dead-reckon on the newest velocity, capped.
                float dt = Mathf.Min(rt - newest.T, MaxExtrapolate);
                pos = newest.P + newest.V * dt;
                rot = newest.Q;
            }
            else
            {
                int i = _buf.Count - 1;
                while (i > 0 && _buf[i - 1].T > rt) i--;
                if (i == 0)
                {
                    pos = _buf[0].P; rot = _buf[0].Q; surf = _buf[0];
                }
                else
                {
                    Snap a = _buf[i - 1], b = _buf[i];
                    float span = Mathf.Max(1e-3f, b.T - a.T);
                    float u = Mathf.Clamp01((rt - a.T) / span);
                    pos = Vector3.Lerp(a.P, b.P, u);
                    rot = Quaternion.Slerp(a.Q, b.Q, u);
                    surf = new Snap
                    {
                        Ail = Mathf.Lerp(a.Ail, b.Ail, u), Ele = Mathf.Lerp(a.Ele, b.Ele, u), Rud = Mathf.Lerp(a.Rud, b.Rud, u),
                        Spoil = Mathf.Lerp(a.Spoil, b.Spoil, u),
                    };
                }
            }

            transform.SetPositionAndRotation(pos, rot);
            _builder.SetDeflections(surf.Ail, surf.Ele, surf.Rud, surf.Spoil);

            // Name tag: upright, facing the camera, roughly constant on-screen size.
            Camera cam = Camera.main;
            if (cam != null && _tagT != null)
            {
                Vector3 toTag = _tagT.position - cam.transform.position;
                float dist = toTag.magnitude;
                _tagT.rotation = Quaternion.LookRotation(toTag.sqrMagnitude > 1e-4f ? toTag : cam.transform.forward, Vector3.up);
                _tag.characterSize = Mathf.Clamp(dist * 0.012f, 0.4f, 12f);
            }
        }

        private void OnDestroy()
        {
            _builder.Clear();
        }
    }
}
