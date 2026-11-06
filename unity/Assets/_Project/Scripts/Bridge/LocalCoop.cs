using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// LOCAL 2-PLAYER, SPLIT SCREEN (Mode 1, Ty 2026-11-05: "two 8BitDo controllers, split screen, one view per plane",
    /// for combat practice and formation flying). Builds the second player's aeroplane and camera when the first
    /// player's flight starts with <see cref="LocalSplit.Enabled"/> on, and takes them away when it starts with it off.
    ///
    /// What the second player gets: a full aeroplane of the same type (its own FlightSimDriver, airframe, shadow, damage,
    /// sound, smoke and seat) flown by their own pad (<see cref="LocalPadControls"/>); their own chase camera on their half
    /// of the screen with its own views; the green HUD on their half when the instruments are set to HUD, and their line
    /// (<see cref="LocalPlayerHud"/>). He starts abreast on the right, 30 m tip to tip (on a runway or the water: in
    /// trail; FormationSlot).
    ///
    /// What stays the first player's: the world clock (Atmosphere.ClockOwner), the touch controls, the menus, the
    /// main camera (clouds, recorder, everything that asks Camera.main), the one AudioListener (both engines are heard
    /// through it, each at <see cref="MixGainEach"/>), the combat zone's guns and score, the network session.
    ///
    /// Free flight and the combat-zone start only: a challenge, lesson or event places the first player itself and has
    /// no second slot, and an online / LAN room has no seat for a second local pilot yet. The reason is shown beside
    /// the option (<see cref="LocalSplit.NotStartedReason"/>) rather than silently flying alone.
    /// </summary>
    public sealed class LocalCoop : MonoBehaviour
    {
        public FlightSimDriver P1;
        public Camera P1Camera;

        /// <summary>Each engine's share of the one mix while two are heard (−2.9 dB each).</summary>
        public const float MixGainEach = 0.72f;
        /// <summary>The [Perf] line's period, seconds of real time.</summary>
        public const float PerfPeriodS = 5f;

        private GameObject _p2Root, _p2CamGo, _backdropGo;
        private FlightSimDriver _p2;
        private ChaseCamera _p2Chase;
        private LocalPlayerView _v1;

        public FlightSimDriver P2 => _p2;

        /// <summary>The second player's RESET (the FLIGHT panel's P2 column): back to its start slot beside the session's
        /// start, its own flight only. The first player is not touched.</summary>
        public void ResetP2()
        {
            if (!LocalSplit.Active || _p2 == null || _p2.Sim == null) return;
            _p2.ResetFlight();   // AircraftChanged → LocalPadControls.Preset (trim and power of the start)
            if (_p2Chase != null) _p2Chase.SnapBehind();
            Debug.Log("[LocalMP] P2 reset: P2's flight only");
        }

        private void Start()
        {
            if (P1 != null) P1.AircraftChanged += OnP1Spawned;
        }

        private void OnDestroy()
        {
            if (P1 != null) P1.AircraftChanged -= OnP1Spawned;
        }

        // Set by the first player's own RESET just before its ResetFlight (TouchFlightControls.DoReset), read once by
        // the AircraftChanged it raises.
        private static bool _p1OwnReset;

        /// <summary>The first player's next AircraftChanged is its own RESET, not a new session: the second player
        /// keeps flying (<see cref="FlyingGame.Core.LocalMp.SplitControls.RestartsP2"/>).</summary>
        public static void MarkP1OwnReset() => _p1OwnReset = true;

        /// <summary>Every FLY, restart, RESET and aircraft change of the first player passes here.</summary>
        private void OnP1Spawned()
        {
            var cause = _p1OwnReset ? FlyingGame.Core.LocalMp.SplitControls.P1Spawn.OwnReset : FlyingGame.Core.LocalMp.SplitControls.P1Spawn.Session;
            _p1OwnReset = false;
            string why = WhyNot();
            LocalSplit.NotStartedReason = LocalSplit.Enabled ? why : null;
            bool p2Flying = LocalSplit.Active && _p2 != null && _p2.Sim != null;
            if (LocalSplit.Enabled && why == null && !FlyingGame.Core.LocalMp.SplitControls.RestartsP2(cause, p2Flying))
                Debug.Log("[LocalMP] P1 reset: P1's flight only, P2 flies on");
            else if (LocalSplit.Enabled && why == null) StartP2();
            else
            {
                if (LocalSplit.Enabled) Debug.Log($"[LocalMP] split screen is on but the second player was not started: {why}");
                TearDown();
            }
        }

        private static string WhyNot()
        {
            if (!LocalSplit.Allowed) return "not in a headset (two headsets: the LAN room)";
            string ch = SessionSettings.ChallengeId;
            if (ch != null && ch != "event:combat") return "free flight or the combat zone only";
            if (SessionSettings.Multiplayer != SessionSettings.MultiplayerMode.Solo) return "multiplayer must be Solo";
            return null;
        }

        private void Update()
        {
            if (LocalSplit.Active && (!LocalSplit.Enabled || !LocalSplit.Allowed)) TearDown();
            if (LocalSplit.Active) PerfTick();
        }

        // ---- the second player ------------------------------------------------------------------------------------

        private void StartP2()
        {
            if (P1 == null || P1.Sim == null || P1Camera == null) return;
            bool fresh = _p2Root == null;
            if (fresh) BuildP2();
            EnsureViews();
            var cfg = P1.Sim.Aircraft.Config;
            var (span, length) = FlyingGame.Core.LocalMp.FormationSlot.SizeOf(cfg);
            _p2.SpawnSlot = FlyingGame.Core.LocalMp.FormationSlot.For(1, span, length, onSurface: P1.GroundStart);
            if (fresh) _p2Root.SetActive(true);   // Awake spawns once; ApplySession below is the real start
            _p2.ApplySession();
            _p2Root.GetComponent<LocalPadControls>().Preset();
            _p2Chase.SnapBehind();
            LocalSplit.Active = true;
            WaterReflection.SuppressedForSplit = true;
            SetMix(MixGainEach);
            _perfT = 0; _perfFrames = 0; _perfMax = 0; _perfSimTicks0 = FlightSimDriver.SimAdvanceTicks;
            var o = _p2.SpawnSlot;
            Debug.Log($"[LocalMP] split screen: P2 {_p2.AircraftName} {o.ForwardM:F0} m ahead, {o.RightM:F0} m right of P1 "
                    + $"(span {span:F1} m, {(P1.GroundStart ? "surface start, in trail" : "air start, abreast")}); "
                    + $"pads P1 {GamepadInput.NameFor(0) ?? "none"}, P2 {GamepadInput.NameFor(1) ?? "none (parked)"}");
        }

        private void BuildP2()
        {
            _p2Root = new GameObject("Aircraft P2");
            _p2Root.SetActive(false);   // so the components see their fields set before Awake
            _p2 = _p2Root.AddComponent<FlightSimDriver>();
            _p2.PlayerIndex = 1;
            var pad = _p2Root.AddComponent<LocalPadControls>();
            pad.Driver = _p2;
            _p2Root.AddComponent<AirframeVisual>();
            _p2Root.AddComponent<GroundShadow>();
            _p2Root.AddComponent<StructuralDamage>();
            _p2Root.AddComponent<CockpitView>().Driver = _p2;
            _p2Root.AddComponent<SmokeController>().Driver = _p2;
            _p2Root.AddComponent<FlightAudio>();

            // His camera: a copy of the first player's (clip planes, sky, culling), without its listener, recorder,
            // headset rigs or menus. Rendered after the first so the shared OnGUI order is P1, P2.
            _p2CamGo = new GameObject("P2 Camera");
            var cam = _p2CamGo.AddComponent<Camera>();
            cam.CopyFrom(P1Camera);
            cam.targetTexture = null;
            cam.depth = P1Camera.depth + 1f;
            _p2Chase = _p2CamGo.AddComponent<ChaseCamera>();
            _p2Chase.PlayerIndex = 1;
            _p2Chase.Target = _p2Root.transform;
            _p2Chase.Driver = _p2;
            _p2Chase.Cockpit = _p2Root.GetComponent<CockpitView>();
            pad.Chase = _p2Chase;
            var v2 = _p2CamGo.AddComponent<LocalPlayerView>();
            v2.PlayerIndex = 1; v2.Driver = _p2; v2.Chase = _p2Chase; v2.Camera = cam;
            _p2CamGo.AddComponent<HudOverlay>().Driver = _p2;
            _p2CamGo.AddComponent<LocalPlayerHud>().View = v2;

            // The thin gap between the halves: something must clear it, or it shows whatever the last frame left there.
            _backdropGo = new GameObject("Split Backdrop");
            var bd = _backdropGo.AddComponent<Camera>();
            bd.depth = P1Camera.depth - 10f; bd.cullingMask = 0; bd.clearFlags = CameraClearFlags.SolidColor;
            bd.backgroundColor = Color.black; bd.rect = new Rect(0, 0, 1, 1);
        }

        private void EnsureViews()
        {
            if (_v1 == null)
            {
                _v1 = P1Camera.gameObject.AddComponent<LocalPlayerView>();
                _v1.PlayerIndex = 0; _v1.Driver = P1; _v1.Camera = P1Camera; _v1.Chase = P1Camera.GetComponent<ChaseCamera>();
            }
            var lh = P1Camera.GetComponent<LocalPlayerHud>() ?? P1Camera.gameObject.AddComponent<LocalPlayerHud>();
            lh.View = _v1;
        }

        /// <summary>Back to one player: nothing of the second remains, so the single-player game is the one it always was.</summary>
        private void TearDown()
        {
            bool was = LocalSplit.Active;
            LocalSplit.Active = false;
            WaterReflection.SuppressedForSplit = false;
            SetMix(1f);
            if (_p2Root != null) Destroy(_p2Root);
            if (_p2CamGo != null) Destroy(_p2CamGo);
            if (_backdropGo != null) Destroy(_backdropGo);
            _p2Root = _p2CamGo = _backdropGo = null; _p2 = null; _p2Chase = null;
            if (_v1 != null) Destroy(_v1);
            _v1 = null;
            if (P1Camera != null && P1Camera.TryGetComponent(out LocalPlayerHud h)) Destroy(h);
            if (was) Debug.Log("[LocalMP] split screen off: one player");
        }

        private void SetMix(float g)
        {
            if (P1 != null && P1.TryGetComponent(out FlightAudio a1)) a1.MixGain = g;
            if (_p2Root != null && _p2Root.TryGetComponent(out FlightAudio a2)) a2.MixGain = g;
        }

        // ---- [Perf] -------------------------------------------------------------------------------------------------

        private float _perfT, _perfMax;
        private int _perfFrames;
        private long _perfSimTicks0;

        private void PerfTick()
        {
            float dt = Time.unscaledDeltaTime;
            _perfT += dt; _perfFrames++; _perfMax = Mathf.Max(_perfMax, dt);
            if (_perfT < PerfPeriodS) return;
            long ticks = FlightSimDriver.SimAdvanceTicks - _perfSimTicks0;
            double simMs = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, _perfFrames);
            Debug.Log($"[Perf] split 2P: {_perfFrames / _perfT:F1} fps, frame mean {_perfT * 1000f / _perfFrames:F1} ms max {_perfMax * 1000f:F1} ms, "
                    + $"sim (both drivers) {simMs:F2} ms/frame, {Screen.width}x{Screen.height}, reflection off");
            _perfT = 0; _perfFrames = 0; _perfMax = 0; _perfSimTicks0 = FlightSimDriver.SimAdvanceTicks;
        }
    }
}
