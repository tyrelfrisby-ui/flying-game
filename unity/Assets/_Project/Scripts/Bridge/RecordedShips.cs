using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Combat;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using FlyingGame.Sim.Replay;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Flight recorder, Unity side (docs/FLIGHT-RECORDER.md). PHASE 1: built and compiled, NOT attached to any scene or
    /// menu until Ty has approved the design and the UI placement. Everything that decides anything is in
    /// FlyingGame.Sim.Replay (shared, engine-free, tested); this draws the recorded ships, feeds the live aircraft into
    /// a take, and exposes the ships to the combat code as gun targets. No platform code: iOS, the Mac and the Quest
    /// run exactly this.
    /// </summary>
    public sealed class RecordedShips : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public ReplayDirector Director { get; private set; }
        public RecordingLibrary Library { get; private set; }
        /// <summary>The take being recorded, or null.</summary>
        public RecordingTake Take { get; private set; }
        /// <summary>The playback that runs alongside the take (the other tracks), or null.</summary>
        public SessionPlayback TakePlayback { get; private set; }

        /// <summary>Set by the smoke and lights code when this is wired in; recorded as frame flags.</summary>
        public System.Func<RecordFlags> LiveFlags;
        public System.Func<int> LiveSmokeRgb;

        private static readonly Dictionary<string, AircraftConfig> Configs = new();

        private sealed class Visual
        {
            public GameObject Go; public AirframeBuilder Builder; public DamageFx Fx; public int Generation; public Aircraft BuiltFor;
        }
        private readonly Dictionary<ReplayActor, Visual> _visuals = new();
        private readonly List<ReplayActor> _gone = new();
        private readonly HashSet<ReplayActor> _present = new();

        public static string LibraryFolder => System.IO.Path.Combine(Application.persistentDataPath, "Recordings");

        private void Awake()
        {
            Driver ??= GetComponent<FlightSimDriver>();
            Director = new ReplayDirector(ConfigFor);
            Library = new RecordingLibrary(LibraryFolder);
        }

        private static AircraftConfig ConfigFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Configs.TryGetValue(id, out AircraftConfig c)) return c;
            try { c = UnityAircraftConfigLoader.LoadFromStreamingAssets(id); }
            catch (System.Exception e) { Debug.LogWarning($"[Recorder] no aircraft config for '{id}': {e.Message}"); c = null; }
            Configs[id] = c;
            return c;
        }

        // ---------------------------------------------------------------- recording

        /// <summary>Begin a take that adds a ship to <paramref name="session"/> (a new session for a first recording).
        /// The other tracks play from the punch-in; the live aircraft is put in its slot for the count-in.</summary>
        public RecordingTake BeginNewTrack(FlightSession session, double punchInSec = 0.0)
        {
            Aircraft live = Driver?.Sim?.Aircraft;
            if (live == null) return null;
            float span = 2f * Mathf.Max(3f, (float)WingPanels.Semispan(live.Config));
            Take = RecordingTake.NewTrack(session, Driver.AircraftId, null, LiveSmokeRgb?.Invoke() ?? 0xFFFFFF, punchInSec, FlightSession.DefaultSlot(span));
            StartTakePlayback();
            return Take;
        }

        /// <summary>Begin re-flying <paramref name="track"/> from <paramref name="punchInSec"/>.</summary>
        public RecordingTake BeginOverdub(FlightSession session, FlightTrack track, double punchInSec)
        {
            if (Driver?.Sim?.Aircraft == null) return null;
            Take = RecordingTake.Overdub(session, track, punchInSec);
            StartTakePlayback();
            return Take;
        }

        private void StartTakePlayback()
        {
            if (TakePlayback != null) Director.Stop(TakePlayback);
            TakePlayback = Take.Session.Tracks.Count == 0 ? null : Director.PlayAlong(Take);   // on the take's clock
        }

        /// <summary>Stop the take: commit it into its session and save the session to the library.</summary>
        public FlightTrack EndTake(bool keep = true)
        {
            if (Take == null) return null;
            FlightTrack t = null;
            if (keep) t = Take.Commit(); else Take.Cancel();
            if (t != null)
            {
                if (string.IsNullOrEmpty(Take.Session.Name) || Take.Session.Name == "Recording") Take.Session.Name = Library.NextName();
                Take.Session.WorldId = WorldTerrain.Active != null ? "island" : "legacy";
                try { Library.Save(Take.Session); }
                catch (System.Exception e) { Debug.LogError($"[Recorder] save failed: {e.Message}"); }
            }
            if (TakePlayback != null) Director.Stop(TakePlayback);
            TakePlayback = null; Take = null;
            return t;
        }

        // ---------------------------------------------------------------- the frame

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool running = Time.timeScale > 0f && !SessionSettings.MenuOpen;
            Aircraft live = Driver?.Sim?.Aircraft;
            if (running && Take != null && live != null)
            {
                Vec3 wind = Atmosphere.WindAtPosition(live.State.Position);
                Take.Tick(dt, live, wind, LiveFlags?.Invoke() ?? RecordFlags.None, LiveSmokeRgb?.Invoke() ?? 0xFFFFFF);
            }
            Director.Tick(running ? dt : 0.0);
            Draw(dt);
        }

        private void Draw(float dt)
        {
            _present.Clear();
            foreach (ReplayActor a in Director.Present()) _present.Add(a);
            foreach (KeyValuePair<ReplayActor, Visual> kv in _visuals)
                if (!_present.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (ReplayActor a in _gone)
            {
                bool stillPlaying = false;
                foreach (SessionPlayback p in Director.Playing) if (p.Actors.Contains(a)) { stillPlaying = true; break; }
                Visual v = _visuals[a];
                if (stillPlaying) { if (v.Go != null) v.Go.SetActive(false); continue; }
                if (v.Go != null) Destroy(v.Go);
                _visuals.Remove(a);
            }
            _gone.Clear();

            foreach (ReplayActor a in _present)
            {
                if (!_visuals.TryGetValue(a, out Visual v) || v.Generation != a.Generation || v.BuiltFor != a.Aircraft)
                {
                    if (v?.Go != null) Destroy(v.Go);
                    v = Build(a);
                    _visuals[a] = v;
                }
                if (!v.Go.activeSelf) v.Go.SetActive(true);
                RigidBodyState s = a.Aircraft.State;
                v.Go.transform.SetPositionAndRotation(CoordinateMap.ToUnity(s.Position), CoordinateMap.ToUnity(s.Attitude));
                Vector3 vel = CoordinateMap.ToUnity(s.Attitude.Rotate(s.Velocity));
                if (a.Mode == ActorMode.Kinematic)
                {
                    TrackSample c = a.Current;
                    v.Builder.SetDeflections((float)c.Deflections.AileronRad, (float)c.Deflections.ElevatorRad, (float)c.Deflections.RudderRad, (float)c.Deflections.SpoilerFraction);
                    v.Builder.SetGearExtension((float)c.GearExtension);
                    float rpm = (float)c.EngineRpm;
                    v.Builder.SpinProps(_ => rpm, dt);
                }
                v.Fx.Update(a.Aircraft.Damage, vel);
            }
        }

        private Visual Build(ReplayActor a)
        {
            var v = new Visual { Generation = a.Generation, BuiltFor = a.Aircraft };
            v.Go = new GameObject($"Recorded-{a.Track.Name}-{a.Id}");
            v.Builder = new AirframeBuilder();
            try { v.Builder.Build(v.Go.transform, a.Config); }
            catch (System.Exception e) { Debug.LogWarning($"[Recorder] airframe build failed for {a.Track.AircraftId}: {e.Message}"); }
            v.Fx = new DamageFx(v.Go.transform, a.Config);
            // Parts already gone (a recorded loss before this point, or a seek) come off now; later ones as they go.
            foreach (AirframeComponent c in a.Aircraft.LostComponents) AirframeVisual.DetachStatic(v.Builder, v.Go.transform, a.Config, c, Vector3.zero);
            Visual vv = v; Aircraft built = a.Aircraft;
            built.ComponentLost += c =>
            {
                if (vv.Go == null) return;
                RigidBodyState s = built.State;
                AirframeVisual.DetachStatic(vv.Builder, vv.Go.transform, a.Config, c, CoordinateMap.ToUnity(s.Attitude.Rotate(s.Velocity)));
            };
            return v;
        }

        // ---------------------------------------------------------------- combat

        /// <summary>For CombatController (when wired): the recorded ships bullets can hit this step.</summary>
        public IReadOnlyList<GunTarget> GunTargets() => Director.GunTargets();

        /// <summary>For CombatController (when wired): a Gunnery hit; true when it was on a recorded ship.</summary>
        public bool OnHit(in HitEvent hit) => Director.OnHit(hit) != null;

        private void OnDestroy()
        {
            foreach (Visual v in _visuals.Values) if (v.Go != null) Destroy(v.Go);
            _visuals.Clear();
        }
    }
}
