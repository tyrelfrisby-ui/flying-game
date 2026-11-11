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
    /// models; fuel and baggage are drawn by the page over the picture.
    ///
    /// IT FLIES WHILE YOU CHOOSE (Ty 2026-11-11: "Keep the preview of the plane in the air circling the whole time").
    /// The aeroplane is still SPAWNED where it would start — LAUNCH and the launch-view card below both depend on that —
    /// but once a spawn has settled the page lifts it off and flies it round a slow level circle for the picture,
    /// banked into the turn, with the camera on a fixed bearing and range so one lap shows every side of it. Display
    /// only: the pose is written straight onto the driver's transform (the world is paused while the menu is open, so
    /// nothing is simulating it), and the sim's own state is never touched — which is why LAUNCH still puts you on the
    /// threshold when the threshold is what you picked.
    ///
    /// NO SEATED FIGURES (Ty 2026-11-11: "Remove the phony pilot from the plane on the landing page it looks silly").
    /// The page's seat rows already say who is aboard in words — "You", "Passenger", "Friend · flying" (LandingView) —
    /// so the sphere-and-capsule occupants this used to park in the cockpit said nothing the page was not saying
    /// better. The cockpit's own pilot and the headset's body are elsewhere and are untouched.
    ///
    /// Also the thumbnail pipeline's camera (<see cref="LandingThumbnails"/>): once a rebuilt aeroplane has settled, any
    /// card picture that does not exist yet for it — the aeroplane, and the view from the launch pose — is captured.
    /// That capture is the reason the circle waits for the settle: the launch-view card is the MAIN camera's picture of
    /// the aeroplane where it will start, and it would be a picture of the circle instead.
    ///
    /// Owns its objects (the camera) on its own root, never on the aircraft, which rebuilds and destroys children
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

        /// <summary>One lap of the display circle. 40 s is slow enough to read the aeroplane and fast enough that a
        /// pilot glancing at the page sees it turning rather than hanging there.</summary>
        public const float CircleSeconds = 40f;
        /// <summary>Bank angle in the circle. A gentle sightseeing turn: enough to show the top of the wing from the
        /// outside, not so much that a high-wing aeroplane hides its own cabin.</summary>
        public const float CircleBankDeg = 14f;
        /// <summary>How far above a surface spawn the circle is flown. Only applied when the aeroplane was spawned on
        /// the ground or on the water (<see cref="FlightSimDriver.GroundStart"/>); an air start already has height.</summary>
        public const float CircleLiftM = 220f;

        /// <summary>Hold the aeroplane at the pose the driver spawned it in — no display circle. For the thumbnail
        /// generator (<see cref="LandingSelfTest"/>), which captures the launch view SECONDS after the settle rather
        /// than at it, and must photograph the aeroplane where it will start rather than out on the circle.</summary>
        public static bool HoldAtStart;

        private Camera _cam;
        private RenderTexture _rt;
        private string _spawnSig, _loadSig;
        private float _settleAt = -1f;
        private int _seenVersion = -1;
        private float _radius = 6f;
        private Vector3 _spawnPos;        // where the driver left it, before the circle moves it (see FlyPose)
        private bool _circling;

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
            // rebuild), but no second camera, and no display circle flying his own cockpit round the sky.
            bool flat = !XrHeadset.Active;
            if (!open) { _circling = false; return; }   // reopened mid-flight, the circle starts from wherever it is now
            EnsureCamera();
            var flow = LandingSession.Flow;
            if (flow.Version != _seenVersion) { _seenVersion = flow.Version; Sync(flow); }
            if (!flat) { _circling = false; return; }
            EnsureTexture();

            Transform t = Driver.transform;
            // Fly it. FlightSimDriver.Update re-applies the sim's own pose to this transform every frame (even paused,
            // where the step does nothing), so the circle is recomputed from the stored spawn each frame rather than
            // integrated — it cannot drift, and the frame after LAUNCH the driver simply wins.
            if (_settleAt < 0f && !HoldAtStart)
            {
                if (!_circling) { _spawnPos = t.position; _circling = true; }   // the pose the driver left: the circle's centre
                FlyPose(t);
            }
            else _circling = false;   // settling after a respawn: it stands at its real start for the launch-view card

            // The camera holds a FIXED bearing and range, so the aspect that changes is the aeroplane's own as it goes
            // round (before the circle this orbited the camera instead, because the aeroplane was standing still).
            float dist = _radius * (Framing == FramingMode.Hero ? 2.6f : 3.0f);
            Quaternion orbit = Quaternion.Euler(14f, 210f, 0f);
            Vector3 centre = t.position + Vector3.up * (_radius * 0.08f);
            _cam.transform.SetPositionAndRotation(centre - orbit * Vector3.forward * dist, orbit);
            _cam.targetTexture = _rt;
            _cam.aspect = (float)_rt.width / _rt.height;
            // NO GHOST AEROPLANE IN THE WATER (Ty 2026-11-11, with a video; "weird ghost plane that was rotating of
            // axis to it about 90 degrees" on 11-09). This camera copies Camera.main's culling mask, so it draws the
            // water, and the water reads the planar reflection in SCREEN SPACE — the main camera's mirror image at
            // this camera's coordinates. WaterReflection.RenderWithoutMirror is the guard SuppressedForSplit already
            // gives the split screen, and it covers the shallows' scene depth for the same reason; it is also why this
            // never calls _cam.Render() directly.
            WaterReflection.RenderWithoutMirror(_cam);

            if (_settleAt > 0f && Time.unscaledTime >= _settleAt) { _settleAt = -1f; _radius = Radius(t); CaptureMissing(flow); }
        }

        /// <summary>
        /// The display circle: a level turn at <see cref="CircleBankDeg"/> round a point over where the aeroplane
        /// spawned, one lap in <see cref="CircleSeconds"/>, on unscaled time because the world is paused.
        ///
        /// The attitude is built from vectors rather than Euler angles so the bank cannot come out the wrong way
        /// round: the nose is on the tangent, and the aeroplane's own up is tilted off vertical TOWARDS the centre of
        /// the turn, which is where a banked aeroplane's lift points.
        /// </summary>
        private void FlyPose(Transform t)
        {
            // Clear of the ground when the spawn was on it; an air start keeps the height it was given.
            Vector3 centre = _spawnPos + Vector3.up * (Driver.GroundStart ? CircleLiftM : 0f);
            float radius = Mathf.Max(40f, _radius * 6f);
            float phase = Time.unscaledTime * (2f * Mathf.PI / CircleSeconds);
            var outward = new Vector3(Mathf.Sin(phase), 0f, Mathf.Cos(phase));
            var tangent = new Vector3(Mathf.Cos(phase), 0f, -Mathf.Sin(phase));   // d(outward)/d(phase): the way it is going
            Vector3 pos = centre + outward * radius;
            float bank = CircleBankDeg * Mathf.Deg2Rad;
            Vector3 up = (Vector3.up * Mathf.Cos(bank) - outward * Mathf.Sin(bank)).normalized;   // lift tilted inward
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent, up));
        }

        /// <summary>Bring the aeroplane in line with the flow: rebuild it when what would fly changed, otherwise only the
        /// saved load.</summary>
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
            // Time of day lights the real scene at once, so the preview shows it (lighting only).
            TimeOfDayLighting.Apply(flow.TimeOfDay);
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

        // ---------------------------------------------------------------- thumbnails

        /// <summary>Capture the pictures this combination has no capture for yet: the aeroplane (preview camera) and the
        /// view from the launch pose (main camera). Both are taken at the settle, before the display circle starts, so
        /// the launch-view card is a picture of the real start and not of the circle.</summary>
        private void CaptureMissing(LandingFlow flow)
        {
            string ac = ThumbnailKeys.Aircraft(flow.Family.Id, flow.GearId);
            if (!LandingThumbnails.Captured(ac)) LandingThumbnails.Capture(ac, _cam);
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
