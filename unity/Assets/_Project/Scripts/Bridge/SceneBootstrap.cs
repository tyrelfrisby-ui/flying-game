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
        private const float GroundSizeM = 12000f;
        private const float LetterDistanceM = 2500f;
        private const float LetterAltitudeM = 600f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Build()
        {
            BuildGround();
            BuildCardinalLetters();
            BuildSun();
            GameObject aircraft = BuildAircraft();
            BuildCameraAndHud(aircraft);
        }

        private static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "FarmGrid";
            ground.transform.localScale = Vector3.one * (GroundSizeM / 10f); // Unity plane is 10x10 m
            var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = MakeGridTexture() };
            mat.mainTextureScale = new Vector2(GroundSizeM / 200f, GroundSizeM / 200f); // 200 m "fields"
            ground.GetComponent<MeshRenderer>().material = mat;
        }

        /// <summary>One 200 m farm field: green with darker fence-line edges, subtle checker shading.</summary>
        private static Texture2D MakeGridTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
            var field = new Color(0.42f, 0.55f, 0.28f);
            var fieldAlt = new Color(0.48f, 0.58f, 0.30f);
            var line = new Color(0.28f, 0.36f, 0.20f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    bool edge = x < 2 || y < 2 || x >= n - 2 || y >= n - 2;
                    tex.SetPixel(x, y, edge ? line : ((x / 64 + y / 64) % 2 == 0 ? field : fieldAlt));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
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
            var root = new GameObject("Glider");

            // Placeholder airframe from primitives, roughly 2-33 proportions. Real model later.
            AddPart(root, "Fuselage", PrimitiveType.Capsule, new Vector3(0, 0, 0.6f),
                Quaternion.Euler(90, 0, 0), new Vector3(0.7f, 3.9f, 0.7f), new Color(0.85f, 0.1f, 0.1f));
            AddPart(root, "Wing", PrimitiveType.Cube, new Vector3(0, 0.35f, 0.6f),
                Quaternion.identity, new Vector3(15.2f, 0.12f, 1.25f), new Color(0.92f, 0.9f, 0.85f));
            AddPart(root, "HStab", PrimitiveType.Cube, new Vector3(0, 0.1f, -3.2f),
                Quaternion.identity, new Vector3(3.6f, 0.08f, 0.9f), new Color(0.92f, 0.9f, 0.85f));
            AddPart(root, "VStab", PrimitiveType.Cube, new Vector3(0, 0.8f, -3.3f),
                Quaternion.identity, new Vector3(0.08f, 1.5f, 1.0f), new Color(0.85f, 0.1f, 0.1f));

            var driver = root.AddComponent<FlightSimDriver>();
            root.AddComponent<KeyboardTestControls>();
            root.AddComponent<ChallengeController>();
            _ = driver;
            return root;
        }

        private static void AddPart(GameObject parent, string name, PrimitiveType type,
            Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.Destroy(part.GetComponent<Collider>()); // no Unity physics on the airframe
            part.transform.SetParent(parent.transform, false);
            part.transform.SetLocalPositionAndRotation(pos, rot);
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().material.color = color;
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
            chase.SnapBehind();

            var hud = cam.gameObject.AddComponent<FlightHud>();
            hud.Driver = aircraft.GetComponent<FlightSimDriver>();

            // The air made visible: bubble field following the aircraft.
            var bubbles = new GameObject("BubbleField").AddComponent<BubbleField>();
            bubbles.Follow = aircraft.transform;

            var chHud = cam.gameObject.AddComponent<ChallengeHud>();
            chHud.Controller = aircraft.GetComponent<ChallengeController>();
        }
    }
}
