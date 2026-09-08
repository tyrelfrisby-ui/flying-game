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
            BuildRunway();
            BuildHangar();
            new GameObject("Soaring").AddComponent<SoaringScenery>();
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

        private static void BuildRunway()
        {
            // A 1500 m x 30 m asphalt strip along +x (sim north-ish), centered near origin at ground z=0.
            var rw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rw.name = "Runway";
            Object.Destroy(rw.GetComponent<Collider>());
            rw.transform.position = new Vector3(0, 0.02f, 400f);   // slightly above ground plane
            rw.transform.localScale = new Vector3(30f, 0.05f, 1500f);
            rw.GetComponent<MeshRenderer>().material = UnlitMat(new Color(0.22f, 0.22f, 0.24f));
            // Centerline stripes.
            for (int i = 0; i < 40; i++)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(stripe.GetComponent<Collider>());
                stripe.transform.position = new Vector3(0, 0.06f, -300f + i * 36f);
                stripe.transform.localScale = new Vector3(0.6f, 0.05f, 16f);
                stripe.GetComponent<MeshRenderer>().material = UnlitMat(Color.white);
            }
        }

        /// <summary>
        /// A hangar beside the runway with its sliding doors pushed open at BOTH ends (owner request), so
        /// it can be flown straight through. Its long axis is parallel to the runway (Unity +z), on the
        /// east side, joined to the strip by a concrete apron. Big enough for the glider's 15 m span
        /// with room to spare: 44 m wide, 16 m clear height, 50 m long.
        /// </summary>
        private static void BuildHangar()
        {
            const float halfW = 22f, height = 16f, halfL = 25f, wall = 0.6f;
            var center = new Vector3(78f, 0f, 250f); // 63 m east of the runway edge, abeam its first third
            var wallCol = new Color(0.80f, 0.78f, 0.72f);
            var roofCol = new Color(0.42f, 0.44f, 0.48f);
            var trimCol = new Color(0.55f, 0.57f, 0.60f);
            var doorCol = new Color(0.22f, 0.34f, 0.56f);
            var apronCol = new Color(0.62f, 0.62f, 0.60f);

            var root = new GameObject("Hangar");
            root.transform.position = center;

            // Concrete apron under the hangar, plus a taxiway strip joining it to the runway edge.
            Slab(root, "Apron", new Vector3(0f, 0.015f, 0f), new Vector3(2f * halfW + 16f, 0.03f, 2f * halfL + 30f), apronCol);
            Slab(root, "Taxiway", new Vector3(-(halfW + 8f + (center.x - halfW - 8f - 15f) * 0.5f), 0.015f, 0f),
                new Vector3(center.x - halfW - 8f - 15f, 0.03f, 20f), apronCol);

            // Side walls (long axis along z).
            Slab(root, "WallWest", new Vector3(-halfW, height * 0.5f, 0f), new Vector3(wall, height, 2f * halfL), wallCol);
            Slab(root, "WallEast", new Vector3(halfW, height * 0.5f, 0f), new Vector3(wall, height, 2f * halfL), wallCol);

            // Flat roof with a small overhang, and a fascia beam across each open end.
            Slab(root, "Roof", new Vector3(0f, height + wall * 0.5f, 0f), new Vector3(2f * halfW + 2f, wall, 2f * halfL + 2f), roofCol);
            Slab(root, "FasciaN", new Vector3(0f, height - 0.8f, halfL), new Vector3(2f * halfW + 2f, 1.6f, wall), trimCol);
            Slab(root, "FasciaS", new Vector3(0f, height - 0.8f, -halfL), new Vector3(2f * halfW + 2f, 1.6f, wall), trimCol);

            // Sliding doors at each end, slid fully OPEN past the side walls (one leaf each side), riding
            // a rail beam along the end face. The opening between them is the full hangar width.
            const float doorW = 13f, doorH = height - 1.2f;
            foreach (float zEnd in new[] { halfL, -halfL })
            {
                float zDoor = zEnd + Mathf.Sign(zEnd) * (wall + 0.35f); // just outside the end face
                foreach (float side in new[] { -1f, 1f })
                {
                    float xDoor = side * (halfW + wall + doorW * 0.5f + 0.4f);
                    Slab(root, "Door", new Vector3(xDoor, doorH * 0.5f, zDoor), new Vector3(doorW, doorH, 0.3f), doorCol);
                    // A lighter stripe so the leaf reads as a panel, not a slab.
                    Slab(root, "DoorStripe", new Vector3(xDoor, doorH * 0.5f, zDoor + Mathf.Sign(zEnd) * 0.2f),
                        new Vector3(doorW * 0.12f, doorH * 0.9f, 0.1f), new Color(0.85f, 0.88f, 0.92f));
                }
                // Door rail along the whole end face (spans walls + both open leaves).
                Slab(root, "DoorRail", new Vector3(0f, doorH + 0.5f, zDoor), new Vector3(2f * (halfW + wall + doorW + 0.8f), 0.6f, 0.5f), trimCol);
            }
        }

        private static void Slab(GameObject parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().material = UnlitMat(color);
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
            var hudOverlay = cam.gameObject.AddComponent<HudOverlay>();   // green conformal HUD over the aircraft
            hudOverlay.Driver = aircraft.GetComponent<FlightSimDriver>();

            // The air made visible: bubble field following the aircraft.
            var bubbles = new GameObject("BubbleField").AddComponent<BubbleField>();
            bubbles.Follow = aircraft.transform;

            var chHud = cam.gameObject.AddComponent<ChallengeHud>();
            chHud.Controller = aircraft.GetComponent<ChallengeController>();

            var weather = aircraft.AddComponent<WeatherController>();
            weather.Bubbles = bubbles;
        }
    }
}
