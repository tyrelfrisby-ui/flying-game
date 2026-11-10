using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Builds the Arena-1 skeleton world in code at play start — no hand-authored scene content, so the
    /// whole world is reviewable in git. Creates: farm-grid ground, floating N/S/E/W letters, sun,
    /// placeholder glider driven by FlightSimDriver, chase cam, HUD.
    /// </summary>
    public static class SceneBootstrap
    {

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Build()
        {
            // iOS defaults to 30 fps when no target is set (owner 2026-10-04: "the refresh rate on the iPad is really slow").
            // 60 everywhere — the sim steps at its own fixed rate, so this is smoothness only.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            // A flight sim must never let the screen sleep. Hands-off at altitude an aeroplane needs no input for
            // minutes at a time, and iOS auto-lock can be set as low as THIRTY SECONDS: the screen locks, the app is
            // suspended, and what the pilot sees is a frozen picture with dead controls that comes back when he taps.
            // Nothing in this project had ever set it, so every flight was on the device's auto-lock timer.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            // The Aero Widget build (owner 2026-10-07): no world, no game — two transparent scenarios for Glass Overlay.
            if (FlyingGame.Bridge.Widget.AeroWidget.IsWidget) { FlyingGame.Bridge.Widget.AeroWidget.Create(); return; }
            // The first frame on the Quest is one 3.5–7.4 s SLOW FRAME, and the log could only bracket it by the
            // timestamps of unrelated lines (docs/quest-performance.md, *The startup frame*): say where it goes.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            WorldBuilder.BuildAll();   // terrain (valley + 3 canyon steps), water, 4 airports, ridge lift
            double tWorld = sw.Elapsed.TotalMilliseconds;
            new GameObject("Soaring").AddComponent<SoaringScenery>();
            BuildCardinalLetters();
            BuildSun();
            double tScenery = sw.Elapsed.TotalMilliseconds;
            GameObject aircraft = BuildAircraft();
            double tAircraft = sw.Elapsed.TotalMilliseconds;
            BuildCameraAndHud(aircraft);
            double tAll = sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[Startup] scene built in {tAll:F0} ms: world {tWorld:F0}, soaring + sun {tScenery - tWorld:F0}, "
                    + $"aircraft {tAircraft - tScenery:F0} (its airframe: see [Airframe] … model ready), camera + HUD {tAll - tAircraft:F0}");
        }

        private static void BuildCardinalLetters()
        {
            // Placement, size and default live in Core CardinalLetters: drawn as if at infinity (they ride with the
            // camera's position, a few degrees above the horizon) and only when the setting is on (off by default).
            foreach ((string letter, FlyingGame.Core.MathTypes.Vec3 d) in FlyingGame.Core.CardinalLetters.Cards)
            {
                var dir = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
                var go = new GameObject($"Cardinal-{letter}");
                go.transform.position = dir * (float)FlyingGame.Core.CardinalLetters.RangeM;
                go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // face inward text side
                var text = go.AddComponent<TextMesh>();
                text.text = letter;
                text.fontSize = 40;
                text.characterSize = 1f;   // the glyph is scaled to CardinalLetters.CardHeightM when first drawn
                text.anchor = TextAnchor.MiddleCenter;
                text.color = new Color(1f, 1f, 1f, 0.9f);
                go.GetComponent<MeshRenderer>().enabled = SessionSettings.ShowCardinalLetters;
                go.AddComponent<CardinalLetterVisibility>().Direction = dir;
            }
        }

        private static void BuildSun()
        {
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            if (System.Environment.GetEnvironmentVariable("AERO_SUNSET") == "1") SunsetLighting.Apply(light, true);
        }

        private static GameObject BuildAircraft()
        {
            var root = new GameObject("Aircraft");

            // Airframe visuals are built per type by AirframeVisual (wings/tails lofted from the config's
            // strip geometry + 3-view fuselage/canopy/prop/gear tables) and rebuilt on aircraft switch.
            var driver = root.AddComponent<FlightSimDriver>();
            root.AddComponent<TouchFlightControls>(); // RC dual-touchpad (folds in editor keyboard fallback)
            root.AddComponent<ChallengeController>();
            root.AddComponent<TowController>();
            root.AddComponent<RaceController>().Driver = driver;
            root.AddComponent<StolController>().Driver = driver;
            root.AddComponent<CropDustController>().Driver = driver;
            root.AddComponent<Net.NetSession>();
            root.AddComponent<VoiceComms>();   // intercom (VOX, headphones) + radio (hold TALK) over the relay
            root.AddComponent<AirframeVisual>();
            // The whole-aircraft parachute, on the types that carry one (owner 2026-10-16).
            root.AddComponent<BrsCanopy>().Driver = root.GetComponent<FlightSimDriver>();
            root.AddComponent<PilotEgress>();   // BAIL OUT / EJECT sequencer + pilot/parachute bodies
            root.AddComponent<GroundShadow>();   // airframe silhouette projected onto the ground (height cue on landing)
            root.AddComponent<StructuralDamage>();
            root.AddComponent<CombatController>().Driver = driver;   // guns, target drones, the combat zone
            root.AddComponent<CockpitView>().Driver = driver;        // which seat, and where the pilot is looking
            root.AddComponent<SmokeController>().Driver = driver;     // the smoke oil, on the SMOKE button
            root.AddComponent<FlightPathVector>().Driver = driver;   // magenta 5 s predicted path with a cone tip
            root.AddComponent<FloatSplash>().Driver = driver;   // per-float water spray (floatplane only)
            root.AddComponent<WakeWaves>().Driver = driver;     // the wake on the water: V from each float/hull, 5 min, reflects off the shore
            root.AddComponent<FlightAudio>();
            UnityGlideTableLoader.Load();
            root.AddComponent<Practice.PracticeController>().Driver = driver;   // crosswind / flare / approach practice
            root.AddComponent<Practice.LessonDebrief>().Controller = root.GetComponent<Practice.PracticeController>();   // screenshots + replay commentary for the debrief
            root.AddComponent<FlightReplay>().Driver = driver;   // last 10 min of flight, played back on the real airframe
            root.AddComponent<TunnelHazards>().Driver = driver;   // the falling rock and the sheet of water in the maze behind the falls
            _ = driver;
            return root;
        }

        // CreatePrimitive's default material uses the built-in Standard shader, which is stripped from the
        // iOS player build (renders magenta on device). Unlit/Color is force-included, so route flat-color
        // primitives through it — matches the current untextured look and is cheaper on mobile.
        private static Material UnlitMat(Color color) =>
            new Material(Shader.Find("Unlit/Color")) { color = color };

        private static void AddPart(GameObject parent, string name, PrimitiveType type,
            Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>()); // no Unity physics on the airframe
            part.transform.SetParent(parent.transform, false);
            part.transform.SetLocalPositionAndRotation(pos, rot);
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().material = UnlitMat(color);
        }

        private static void BuildCameraAndHud(GameObject aircraft)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.gameObject.tag = "MainCamera";
            }

            cam.farClipPlane = 20000f;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f); // clear sky
            var chase = cam.gameObject.AddComponent<ChaseCamera>();
            // The stereo rig rides the same camera and only wakes up when VR is switched on; off, it
            // costs one disabled component and the mono view is untouched.
            cam.gameObject.AddComponent<VrRig>();
            // And the native-headset rig (Meta Quest through OpenXR): inert unless an XR runtime is up, which only an
            // Android build with the OpenXR loader can start. It makes this same camera the XR camera.
            cam.gameObject.AddComponent<QuestRig>();
            // The pilot's hands on the aeroplane's own controls, and his visible body: inert unless the headset is on.
            cam.gameObject.AddComponent<VrCockpitHands>();
            cam.gameObject.AddComponent<AudioListener>();
            chase.Target = aircraft.transform;
            chase.Driver = aircraft.GetComponent<FlightSimDriver>();
            chase.SnapBehind();

            // Says what the frame was doing when it stopped (Ty's Pitts freeze, 2026-10-31). One comparison a frame
            // when nothing is wrong, so it stays in: a freeze nobody can reproduce on demand needs the evidence to
            // already be being written when it happens.
            cam.gameObject.AddComponent<FreezeWatch>().Driver = aircraft.GetComponent<FlightSimDriver>();

            // Says WHICH AEROPLANE the flight model is flying, and whether it is the one being drawn (Ty 2026-11-09:
            // the Pitts flew with a Cub's dihedral effect for a minute and then went back to normal — "make the build
            // log or display which config the flight model is actually using, so the next time it happens we know for
            // certain rather than inferring"). One line on screen, the full record on a tap and in the log at every
            // aircraft change. Like FreezeWatch above it stays in: a transient nobody can reproduce on demand needs
            // the evidence to already be being written when it happens.
            cam.gameObject.AddComponent<ModelIdentityBadge>().Driver = aircraft.GetComponent<FlightSimDriver>();

            var hud = cam.gameObject.AddComponent<FlightHud>();
            hud.Driver = aircraft.GetComponent<FlightSimDriver>();
            hud.Race = aircraft.GetComponent<RaceController>();
            hud.Stol = aircraft.GetComponent<StolController>();
            hud.Dust = aircraft.GetComponent<CropDustController>();
            hud.Tow = aircraft.GetComponent<TowController>();
            hud.Combat = aircraft.GetComponent<CombatController>();
            chase.Cockpit = aircraft.GetComponent<CockpitView>();   // `cam` is Unity's Camera; the ChaseCamera is `chase`
            cam.gameObject.AddComponent<TracerOverlay>().Combat = aircraft.GetComponent<CombatController>();
            var reflection = cam.gameObject.AddComponent<WaterReflection>();   // glassy water: aircraft mirrored in the lake
            reflection.Driver = aircraft.GetComponent<FlightSimDriver>();
            cam.gameObject.AddComponent<PilotUnderwater>();   // the seat view with the head in the lake (Ty t922u)
            // Instruments: round analog dials (default), the green conformal HUD, or none — SessionSettings.Instruments.
            var gauges = cam.gameObject.AddComponent<AnalogGauges>();
            gauges.Driver = aircraft.GetComponent<FlightSimDriver>();
            var hudOverlay = cam.gameObject.AddComponent<HudOverlay>();
            hudOverlay.Driver = aircraft.GetComponent<FlightSimDriver>();
            // Quick clips: films a hidden copy of this camera (no buttons) + the game sound; instruments optional.
            var clips = cam.gameObject.AddComponent<ClipRecorder>();
            clips.Hud = hudOverlay; clips.Gauges = gauges; clips.Combat = aircraft.GetComponent<CombatController>();

            // The air made visible: bubble field following the aircraft.
            var bubbles = new GameObject("BubbleField").AddComponent<BubbleField>();
            bubbles.Follow = aircraft.transform;
            // "Show lift" (owner 2026-10-03): rising bubbles at 10x the air's vertical speed, bigger in stronger lift — on top
            // of the snow (or alone in a clear sky when the snow is off). Replaces the old green/orange lift-sink markers.
            var lift = new GameObject("LiftBubbles").AddComponent<LiftBubbles>();
            lift.Follow = aircraft.transform;
            var options = cam.gameObject.AddComponent<OptionsPanel>();          // in-flight OPTIONS: markers, bubbles, instruments, volume
            var views = cam.gameObject.AddComponent<ViewPanel>();               // VIEW / REPLAY / CLIP / REC + the replay bar
            views.Chase = chase; views.Replay = aircraft.GetComponent<FlightReplay>(); views.Options = options;
            // THE DEBRIEF (Ty 2026-11-10): what failed, when, what you did, how long you took to notice. On the
            // replay bar, one tap from the replay, because the live toolbar is already seven buttons wide and his
            // standing complaint is menus. All of its words come from FlyingGame.Sim.Replay.FlightDebrief.
            var debrief = cam.gameObject.AddComponent<DebriefPanel>();
            debrief.Replay = views.Replay; debrief.Driver = aircraft.GetComponent<FlightSimDriver>();
            views.Debrief = debrief;
            options.Driver = aircraft.GetComponent<FlightSimDriver>();
            // THE FIRE POTS (owner 2026-10-18): the lighted mail route up the staircase, Valley to Summit.
            new GameObject("FirePots").AddComponent<FirePotLights>();
            // THE CHART (owner 2026-10-18): VFR sectional / IFR low / satellite, from the CHART button or the
            // pause menu. It is given no reference to the aircraft, on purpose: there is no own-ship symbol.
            cam.gameObject.AddComponent<ChartPanel>();
            // CREDITS (2026-10-21): the model licences require the credit to reach the PLAYER, and a file in the
            // source repo does not. See CreditsPanel.
            cam.gameObject.AddComponent<CreditsPanel>();
            // THE LOAD SHEET (owner 2026-10-30): occupants, bags, cargo and fuel, plotted on the type's own CG
            // envelope, from the landing page's AIRCRAFT chapter.
            // And the half that lets you work out where you are instead: two VOR/ILS receivers, a heading
            // card and a leg clock. These DO read the position, because that is what a receiver does.
            cam.gameObject.AddComponent<NavRadios>().Driver = aircraft.GetComponent<FlightSimDriver>();
            var forces = cam.gameObject.AddComponent<ForceVectorOverlay>();     // physics forces drawn on the airframe (OPTIONS)
            forces.Driver = aircraft.GetComponent<FlightSimDriver>();
            var airMap = cam.gameObject.AddComponent<AtmosphereOverlay>();     // the regional air made visible (debug: J)
            airMap.Driver = aircraft.GetComponent<FlightSimDriver>();
            // The Aero Widget's teaching layers in the game (owner rule 2026-10-09: game and widget in parity): protocol-7
            // vectors and moments, the insets, the autopilot and weight & balance — the AERO toolbar button.
            var aero = cam.gameObject.AddComponent<Widget.GameAeroHost>();
            aero.Driver = aircraft.GetComponent<FlightSimDriver>();
            cam.gameObject.AddComponent<Widget.GameAeroPanel>().Host = aero;

            var chHud = cam.gameObject.AddComponent<ChallengeHud>();
            chHud.Controller = aircraft.GetComponent<ChallengeController>();
            var wheels = cam.gameObject.AddComponent<Practice.WheelForceOverlay>();   // weight-on-wheels arrows (side-view practice)
            wheels.Driver = aircraft.GetComponent<FlightSimDriver>();
            wheels.Practice = aircraft.GetComponent<Practice.PracticeController>();
            var stallVec = cam.gameObject.AddComponent<Practice.StallVectorOverlay>();   // lift / tail force / relative wind (stall side view)
            stallVec.Driver = aircraft.GetComponent<FlightSimDriver>();
            stallVec.Practice = aircraft.GetComponent<Practice.PracticeController>();
            var pHud = cam.gameObject.AddComponent<Practice.PracticeHud>();
            pHud.Controller = aircraft.GetComponent<Practice.PracticeController>();
            pHud.Driver = aircraft.GetComponent<FlightSimDriver>();
            pHud.Debrief = aircraft.GetComponent<Practice.LessonDebrief>();

            // LOCAL 2-PLAYER, split screen (off by default: with it off this object builds nothing).
            var coop = new GameObject("LocalCoop").AddComponent<LocalCoop>();
            coop.P1 = aircraft.GetComponent<FlightSimDriver>(); coop.P1Camera = cam;

            var weather = aircraft.AddComponent<WeatherController>();
            weather.Bubbles = bubbles;

            // Landing page: aircraft / start / challenge / conditions. Opens on launch (sim paused).
            var menu = cam.gameObject.AddComponent<StartMenu>();
            menu.Driver = aircraft.GetComponent<FlightSimDriver>();
            menu.Weather = weather;
            menu.Challenges = aircraft.GetComponent<ChallengeController>();
            menu.Tow = aircraft.GetComponent<TowController>();
            menu.Race = aircraft.GetComponent<RaceController>();
            menu.Stol = aircraft.GetComponent<StolController>();
            menu.Dust = aircraft.GetComponent<CropDustController>();
            menu.Practice = aircraft.GetComponent<Practice.PracticeController>();
            menu.Combat = aircraft.GetComponent<CombatController>();
            cam.gameObject.AddComponent<CombatHud>().Combat = aircraft.GetComponent<CombatController>();   // target indicator + gunsight
            pHud.Menu = menu;

            // Commerce: what this person may fly, and whether they hold the online pass. One object, reached
            // everywhere else through EntitlementRuntime.Instance, plus the store screen it drives. Nothing
            // is refused while commerce.json keeps "gateMultiplayer" false — the layer resolves, counts the
            // online hours and sells what the store offers, and the refusal waits for the products to exist.
            var commerce = new GameObject("Commerce");
            var ent = commerce.AddComponent<Commerce.EntitlementRuntime>();
            var store = commerce.AddComponent<Commerce.Paywall>();
            store.TermsUrl = ent.TermsUrl;
            store.PrivacyUrl = ent.PrivacyUrl;
            menu.Store = store;
            // Multiplayer test (2026-10-11): AERO_MP_TEST=ffa (or a 6-char room code) picks that room and flies — for a
            // terminal-launched end-to-end check against the platform (mp.aeroplayground.app).
            string mpTest = System.Environment.GetEnvironmentVariable("AERO_MP_TEST");
            if (!string.IsNullOrEmpty(mpTest)) menu.StartCoroutine(MpTest(menu, mpTest));
            // Device self-test of replay + clips, only when launched with AERO_SELFTEST set (devicectl --environment-variables).
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AERO_SELFTEST")))
            {
                Application.runInBackground = true;   // Mac: a test launched from the terminal isn't focused — don't pause
                var st = cam.gameObject.AddComponent<ClipSelfTest>();
                st.Menu = menu; st.Replay = aircraft.GetComponent<FlightReplay>(); st.Egress = aircraft.GetComponent<PilotEgress>(); st.Chase = chase;
                st.Combat = aircraft.GetComponent<CombatController>();
            }
        }

        private static System.Collections.IEnumerator MpTest(StartMenu menu, string room)
        {
            Application.runInBackground = true;
            yield return new WaitForSecondsRealtime(2f);
            SessionSettings.Multiplayer = room == SessionSettings.FfaRoom ? SessionSettings.MultiplayerMode.FreeForAll : SessionSettings.MultiplayerMode.CodeRoom;
            if (room != SessionSettings.FfaRoom) SessionSettings.RoomCode = room.ToUpperInvariant();
            if (string.IsNullOrEmpty(SessionSettings.PilotName)) SessionSettings.PilotName = "MacTest";
            Debug.Log($"[MpTest] flying in {SessionSettings.EffectiveRoom}");
            menu.Fly();
        }
    }
}
