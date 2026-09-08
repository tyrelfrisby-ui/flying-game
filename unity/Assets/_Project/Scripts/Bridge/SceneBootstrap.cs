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
        private const float LetterDistanceM = 2500f;
        private const float LetterAltitudeM = 600f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Build()
        {
            WorldBuilder.BuildAll();   // terrain (valley + 3 canyon steps), water, 4 airports, ridge lift
            new GameObject("Soaring").AddComponent<SoaringScenery>();
            BuildCardinalLetters();
            BuildSun();
            GameObject aircraft = BuildAircraft();
            BuildCameraAndHud(aircraft);
        }

        private static void BuildCardinalLetters()
        {
            // Unity +z is sim north (CoordinateMap).
            (string letter, Vector3 dir)[] cards =
            {
                ("N", Vector3.forward), ("S", Vector3.back), ("E", Vector3.right), ("W", Vector3.left),
            };
            foreach ((string letter, Vector3 dir) in cards)
            {
                var go = new GameObject($"Cardinal-{letter}");
                go.transform.position = dir * LetterDistanceM + Vector3.up * LetterAltitudeM;
                go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // face inward text side
                var text = go.AddComponent<TextMesh>();
                text.text = letter;
                text.fontSize = 40;
                text.characterSize = 40f; // ~large enough to read at 2.5 km
                text.anchor = TextAnchor.MiddleCenter;
                text.color = new Color(1f, 1f, 1f, 0.9f);
            }
        }

        private static void BuildSun()
        {
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
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
            root.AddComponent<AirframeVisual>();
            root.AddComponent<GroundShadow>();   // airframe silhouette projected onto the ground (height cue on landing)
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
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f); // clear sky
            var chase = cam.gameObject.AddComponent<ChaseCamera>();
            chase.Target = aircraft.transform;
            chase.Driver = aircraft.GetComponent<FlightSimDriver>();
            chase.SnapBehind();

            var hud = cam.gameObject.AddComponent<FlightHud>();
            hud.Driver = aircraft.GetComponent<FlightSimDriver>();
            hud.Race = aircraft.GetComponent<RaceController>();
            hud.Stol = aircraft.GetComponent<StolController>();
            var hudOverlay = cam.gameObject.AddComponent<HudOverlay>();   // green conformal HUD over the aircraft
            hudOverlay.Driver = aircraft.GetComponent<FlightSimDriver>();

            // The air made visible: bubble field following the aircraft.
            var bubbles = new GameObject("BubbleField").AddComponent<BubbleField>();
            bubbles.Follow = aircraft.transform;

            var chHud = cam.gameObject.AddComponent<ChallengeHud>();
            chHud.Controller = aircraft.GetComponent<ChallengeController>();

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
        }
    }
}
