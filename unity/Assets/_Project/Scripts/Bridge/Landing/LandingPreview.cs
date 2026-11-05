using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core.Landing;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE AEROPLANE ON EVERY PAGE (Ty 2026-11-05 addendum: "The airplane remains visible through all of the pages and gets
    /// the configuration added like floats on a Pitts and a full tank and a friend in the front seat etc all visually fed
    /// back"). The real airframe, spawned where it will start, filmed by a camera of its own into <see cref="Texture"/>,
    /// slowly turning. Whenever the flow changes what would fly (type, gear, airport, viewpoint, activity) the session is
    /// re-applied and the aeroplane rebuilt exactly as LAUNCH would build it, so floats, skis and tyres are the real
    /// models; occupied seats get a seated figure; fuel and baggage are drawn by the page over the picture.
    ///
    /// Also the thumbnail pipeline's camera (<see cref="LandingThumbnails"/>): once a rebuilt aeroplane has settled, any
    /// card picture that does not exist yet for it — the aeroplane, and the view from the launch pose — is captured.
    ///
    /// Owns its objects (camera, figures) on its own root, never on the aircraft, which rebuilds and destroys children
    /// it did not make (CLAUDE.md: "Never parent your object to something another system rebuilds").
    /// </summary>
    public sealed class LandingPreview : MonoBehaviour
    {
        public static LandingPreview Instance { get; private set; }
        public FlightSimDriver Driver;

        public enum FramingMode { Hero, Load }
        public FramingMode Framing = FramingMode.Hero;

        /// <summary>The live picture, or null before the first frame / where previews are off.</summary>
        public RenderTexture Texture => _rt;

        /// <summary>Seconds after a rebuild before the model is trusted for a capture (glTF placement is asynchronous).</summary>
        public const float SettleSec = 1.5f;

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _figures;
        private readonly List<GameObject> _figurePool = new();
        private string _spawnSig, _loadSig;
        private float _settleAt = -1f;
        private int _seenVersion = -1;
        private float _radius = 6f;
        private Material _meMat, _personMat, _inviteMat, _dualMat, _skinMat;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; if (_rt != null) _rt.Release(); }

        private void EnsureCamera()
        {
            if (_cam != null) return;
            var go = new GameObject("LandingPreviewCamera");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;            // rendered by hand, only while the menu is open
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 20000f; _cam.fieldOfView = 30f;
            Camera main = Camera.main;
            if (main != null) { _cam.clearFlags = main.clearFlags; _cam.backgroundColor = main.backgroundColor; _cam.cullingMask = main.cullingMask; }
            _figures = new GameObject("LandingOccupants").transform;
            _figures.SetParent(transform, false);
            Shader sh = Shader.Find("Unlit/Color");
            _meMat = new Material(sh) { color = new Color(0.20f, 0.45f, 0.85f) };
            _personMat = new Material(sh) { color = new Color(0.55f, 0.58f, 0.62f) };
            _inviteMat = new Material(sh) { color = new Color(1.00f, 0.80f, 0.25f) };
            _dualMat = new Material(sh) { color = new Color(1.00f, 0.50f, 0.15f) };
            _skinMat = new Material(sh) { color = new Color(0.93f, 0.78f, 0.65f) };
        }

        private void EnsureTexture()
        {
            // 16:9, sized to the screen so the hero is sharp on an iPad and cheap on a phone.
            int w = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(Screen.width, Screen.height) * 0.6f), 480, 1280), h = w * 9 / 16;
            if (_rt != null && _rt.width == w) return;
            if (_rt != null) _rt.Release();
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "LandingPreview", antiAliasing = 2 };
            _rt.Create();
        }

        private void LateUpdate()
        {
            bool open = SessionSettings.MenuOpen && Driver != null;
            // In a headset the pilot is looking at the real aeroplane already: keep it in step with the flow (same
            // rebuild), but no second camera and no figures where his own head is.
            bool flat = !XrHeadset.Active;
            if (_figures != null) _figures.gameObject.SetActive(open && flat);
            if (!open) return;
            EnsureCamera();
            var flow = LandingSession.Flow;
            if (flow.Version != _seenVersion) { _seenVersion = flow.Version; Sync(flow); }
            if (!flat) return;
            EnsureTexture();

            Transform t = Driver.transform;
            // Turn slowly on unscaled time (the world is paused while the menu is open).
            float yaw = 210f + Time.unscaledTime * 9f;
            float dist = _radius * (Framing == FramingMode.Hero ? 2.6f : 3.0f);
            Quaternion orbit = Quaternion.Euler(14f, yaw, 0f);
            Vector3 centre = t.position + Vector3.up * (_radius * 0.08f);
            _cam.transform.SetPositionAndRotation(centre - orbit * Vector3.forward * dist, orbit);
            _cam.targetTexture = _rt;
            _cam.aspect = (float)_rt.width / _rt.height;
            _cam.Render();

            _figures.SetPositionAndRotation(t.position, t.rotation);

            if (_settleAt > 0f && Time.unscaledTime >= _settleAt) { _settleAt = -1f; _radius = Radius(t); CaptureMissing(flow); }
        }

        /// <summary>Bring the aeroplane in line with the flow: rebuild it when what would fly changed, otherwise only the
        /// load and the figures.</summary>
        private void Sync(LandingFlow flow)
        {
            string spawn = $"{flow.AircraftId}|{flow.Gear.Bushwheels}|{flow.Gear.Skis}|{flow.LocationIndex}|{flow.Viewpoint}|{flow.ActivityId}";
            string load = string.Join(",", flow.Seats.Select(s => $"{s.SeatId}:{s.Occupant}:{s.Role}:{s.Kg:0}")) + "|" + string.Join(",", flow.Stations.Select(s => $"{s.StationId}:{s.Kg:0}"));
            if (spawn != _spawnSig)
            {
                _spawnSig = spawn; _loadSig = load;
                LandingSession.ApplyPreview(flow);
                try { Driver.ApplySession(); }
                catch (System.Exception e) { Debug.LogWarning($"[Landing] preview spawn of {flow.AircraftId} failed: {e.Message}"); }
                _settleAt = Time.unscaledTime + SettleSec;
                _radius = Radius(Driver.transform);
            }
            else if (load != _loadSig)
            {
                _loadSig = load;
                LandingSession.ApplyPreview(flow);   // the saved load the next spawn reads
            }
            BuildFigures(flow);
        }

        /// <summary>Half the airframe's size, from its renderers (excluding our own).</summary>
        private float Radius(Transform t)
        {
            Bounds b = new Bounds(t.position, Vector3.one);
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float rad = any ? b.extents.magnitude : 6f;
            return Mathf.Clamp(rad, 2f, 40f);
        }

        // ---------------------------------------------------------------- the people

        private void BuildFigures(LandingFlow flow)
        {
            foreach (var g in _figurePool) g.SetActive(false);
            var cfg = LoadedConfig(flow.AircraftId);
            var seats = cfg?.Loading?.Seats;
            if (seats == null) return;
            int n = 0;
            foreach (var row in flow.Seats)
            {
                if (row.Occupant == Occupant.Empty) continue;
                var st = seats.FirstOrDefault(s => s.Id == row.SeatId);
                if (st == null) continue;
                GameObject fig = n < _figurePool.Count ? _figurePool[n] : NewFigure();
                n++;
                fig.SetActive(true);
                fig.transform.localPosition = CoordinateMap.ToUnity(st.EyeVec());
                fig.transform.localRotation = Quaternion.identity;
                Material body = row.Occupant == Occupant.Me ? _meMat : row.Occupant == Occupant.DefaultPerson ? _personMat
                              : row.Role == SeatRole.DualControl ? _dualMat : _inviteMat;
                fig.transform.GetChild(1).GetComponent<MeshRenderer>().sharedMaterial = body;
            }
        }

        /// <summary>A seated figure, origin at the EYE: a head round the eye and shoulders below it. Simple on purpose: it
        /// says who sits where at a glance; the cockpit's own pilot figure is untouched.</summary>
        private GameObject NewFigure()
        {
            var root = new GameObject("Occupant");
            root.transform.SetParent(_figures, false);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.02f, -0.06f);
            head.transform.localScale = Vector3.one * 0.22f;
            head.GetComponent<MeshRenderer>().sharedMaterial = _skinMat;
            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(torso.GetComponent<Collider>());
            torso.transform.SetParent(root.transform, false);
            torso.transform.localPosition = new Vector3(0f, -0.34f, -0.10f);
            torso.transform.localScale = new Vector3(0.42f, 0.26f, 0.26f);
            _figurePool.Add(root);
            return root;
        }

        private string _cfgFor; private FlyingGame.Core.DataContracts.AircraftConfig _cfg;
        private FlyingGame.Core.DataContracts.AircraftConfig LoadedConfig(string id)
        {
            if (_cfgFor == id) return _cfg;
            _cfgFor = id;
            try { _cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id); } catch (System.Exception) { _cfg = null; }
            return _cfg;
        }

        // ---------------------------------------------------------------- thumbnails

        /// <summary>Capture the pictures this combination has no capture for yet: the aeroplane (preview camera, figures
        /// hidden: a card shows the type, not today's passengers) and the view from the launch pose (main camera).</summary>
        private void CaptureMissing(LandingFlow flow)
        {
            string ac = ThumbnailKeys.Aircraft(flow.Family.Id, flow.GearId);
            if (!LandingThumbnails.Captured(ac))
            {
                bool figs = _figures.gameObject.activeSelf;
                _figures.gameObject.SetActive(false);
                LandingThumbnails.Capture(ac, _cam);
                _figures.gameObject.SetActive(figs);
            }
            if (flow.Rules.ViewpointLocked && flow.Rules.FixedViewpoint == null) return;   // an event's own start: not a viewpoint card
            string loc = ThumbnailKeys.Location(flow.Family.Id, flow.GearId, flow.LocationId, flow.Viewpoint);
            if (!LandingThumbnails.Captured(loc)) CaptureLaunchView(loc);
        }

        /// <summary>The main camera at the launch pose. The chase camera sits behind the aeroplane at its spawn while the
        /// menu is open, which is the first thing the pilot sees after LAUNCH; the IMGUI page is not in a camera render.</summary>
        public static void CaptureLaunchView(string key)
        {
            Camera main = Camera.main;
            if (main == null) return;
            LandingThumbnails.Capture(key, main);
        }

        /// <summary>The first frame after LAUNCH, overwriting the bench / preview picture: what the pilot really saw.</summary>
        public System.Collections.IEnumerator CaptureAfterLaunch(string key)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            if (!SessionSettings.MenuOpen) CaptureLaunchView(key);
        }

        /// <summary>Tools: wait for the next settle.</summary>
        public bool Settling => _settleAt > 0f;
    }
}
