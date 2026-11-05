using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE WHEELS / SKIS SELECTOR in the cockpit (Ty 2026-11-05): the ONE shared part (<see cref="SkiSelectorPart"/> has its
    /// design and its anchors), built here and nowhere else — no aeroplane's model or builder draws its own
    /// (WheelSkiHingeTests.TheSelectorIsOneSharedPart holds that). A placard plate with a lever in a two-position gate, its
    /// knob a little ski; the lever shows the COMMAND (up WHEELS, down SKIS, like a gear handle) and three lamps along the
    /// top show where the skis ARE (green WHEELS, amber TRANSIT, green SKIS), from <see cref="Aircraft.Skis"/>.
    ///
    /// One path for iOS/Mac and the Quest. AirframeVisual builds it after the airframe (the builder adopts it, so a rebuild
    /// takes it away cleanly and builds it again) and drives it every frame. Every way of moving it ends in
    /// <see cref="Move"/>: a click or a tap on the lever (iOS/Mac), the GEAR slot's SKIS button and the L key
    /// (TouchFlightControls), and the Quest's fingertip poke or a controller grip on the knob (VrCockpitHands).
    /// </summary>
    public sealed class SkiSelectorLever : MonoBehaviour
    {
        private sealed class Built
        {
            public Transform Root, Lever, Knob;
            public Renderer[] Lamps;
            public string Seat;
        }

        private readonly List<Built> _built = new();
        private Aircraft _for;
        private static Material _lampOff, _lampGreen, _lampAmber;
        /// <summary>Radius around the knob that a click or a fingertip takes as "on the lever".</summary>
        public const float HitRadiusM = 0.04f;

        public int Count => _built.Count;

        /// <summary>The knobs, world space (the Quest's hands and the shot tools read them).</summary>
        public IEnumerable<Vector3> KnobsWorld
        {
            get { foreach (Built b in _built) if (b.Knob != null) yield return b.Knob.position; }
        }

        /// <summary>The selector moved by any input: the lever goes to the other position and the hydraulics follow.</summary>
        public static void Move(Aircraft ac, string via)
        {
            if (ac?.Skis == null) return;
            ac.Skis.Selector.Toggle();
            Debug.Log($"[Skis] selector {(ac.Skis.Selector.Commanded == SkiSelector.Position.Skis ? "SKIS (lowering)" : "WHEELS (raising)")} by {via}");
        }

        /// <summary>Build the part on this aeroplane, at each mount <see cref="SkiSelectorPart.MountsFor"/> gives it.</summary>
        public void Build(AirframeBuilder builder, AircraftConfig cfg, Aircraft ac, Vector3 eyeLocalFallback)
        {
            _built.Clear();
            _for = ac;
            if (ac?.Skis == null || cfg == null) return;
            foreach (SkiSelectorPart.Mount m in SkiSelectorPart.MountsFor(cfg, CoordinateMap.ToSim(eyeLocalFallback)))
            {
                Built b = BuildOne(transform, CoordinateMap.ToUnity(m.Pos), CoordinateMap.ToUnity(m.Eye));
                b.Seat = m.SeatId;
                builder?.Adopt(b.Root.gameObject);
                _built.Add(b);
                Debug.Log($"[Skis] selector for seat '{m.SeatId}' at {CoordinateMap.ToUnity(m.Pos)} ({(m.Authored ? "authored anchor" : "default offset from the eye")})");
            }
        }

        public void Forget() { _built.Clear(); _for = null; }

        /// <summary>The one design, put together from SkiSelectorPart's numbers. Local frame: +z out of the plate toward the
        /// pilot, +y up the plate.</summary>
        private static Built BuildOne(Transform air, Vector3 posLocal, Vector3 eyeLocal)
        {
            var root = new GameObject("SkiSelector_Part").transform;
            root.gameObject.layer = WaterReflection.AircraftLayer;
            root.SetParent(air, false);
            root.localPosition = posLocal;
            Vector3 n = eyeLocal - posLocal;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.back;
            root.localRotation = Quaternion.LookRotation(n.normalized, Vector3.up);

            float w = (float)SkiSelectorPart.PlateWidthM, h = (float)SkiSelectorPart.PlateHeightM, t = (float)SkiSelectorPart.PlateThicknessM;
            Part(root, "SkiSelector_Plate", PrimitiveType.Cube, new Vector3(0, 0, -t * 0.5f), Quaternion.identity, new Vector3(w, h, t), new Color(0.12f, 0.12f, 0.13f));
            // The gate: a dark slot the lever runs in, with its two ends.
            Part(root, "SkiSelector_Gate", PrimitiveType.Cube, new Vector3(0, -0.005f, 0.0005f), Quaternion.identity, new Vector3(0.010f, h * 0.55f, 0.001f), new Color(0.02f, 0.02f, 0.02f));

            // The lever and its ski-shaped knob.
            var lever = new GameObject("SkiSelector_Lever").transform;
            lever.SetParent(root, false);
            lever.localPosition = new Vector3(0, -0.005f, 0);
            float len = (float)SkiSelectorPart.LeverLengthM, d = (float)SkiSelectorPart.LeverDiameterM;
            Part(lever, "SkiSelector_Shaft", PrimitiveType.Cylinder, new Vector3(0, 0, len * 0.5f), Quaternion.Euler(90f, 0, 0), new Vector3(d, len * 0.5f, d), new Color(0.75f, 0.76f, 0.78f));
            var knob = new GameObject("SkiSelector_Knob").transform;
            knob.SetParent(lever, false);
            knob.localPosition = new Vector3(0, 0, len);
            float kl = (float)SkiSelectorPart.KnobLengthM, kw = (float)SkiSelectorPart.KnobWidthM, kt = (float)SkiSelectorPart.KnobThicknessM;
            var white = new Color(0.93f, 0.93f, 0.92f);
            // A little ski lying across the end of the lever, its tip turned up: shape-coded like a gear knob is a wheel.
            Part(knob, "SkiSelector_KnobSki", PrimitiveType.Cube, Vector3.zero, Quaternion.identity, new Vector3(kw, kt, kl), white);
            Part(knob, "SkiSelector_KnobTip", PrimitiveType.Cube, new Vector3(0, kt * 0.9f, kl * 0.55f), Quaternion.Euler(-35f, 0, 0), new Vector3(kw, kt, kl * 0.3f), white);

            // The lamps along the top: WHEELS on the pilot's left, TRANSIT, SKIS on his right (the plate's +x is his left).
            EnsureMaterials();
            var lamps = new Renderer[3];
            float lampD = (float)SkiSelectorPart.LampDiameterM;
            for (int i = 0; i < 3; i++)
            {
                float x = (1 - i) * 0.026f;
                lamps[i] = Part(root, "SkiSelector_Lamp" + SkiSelectorPart.LampNames[i], PrimitiveType.Cylinder,
                                new Vector3(x, h * 0.33f, 0.003f), Quaternion.Euler(90f, 0, 0), new Vector3(lampD, 0.003f, lampD), Color.black);
                lamps[i].sharedMaterial = _lampOff;
                Label(root, SkiSelectorPart.LampNames[i], new Vector3(x, h * 0.33f - 0.012f, 0.0012f), 0.0045f);
            }
            // The two lever positions, beside the gate.
            Label(root, "WHEELS", new Vector3(-0.024f, 0.022f, 0.0012f), 0.006f);
            Label(root, "SKIS", new Vector3(-0.024f, -0.036f, 0.0012f), 0.006f);
            return new Built { Root = root, Lever = lever, Knob = knob, Lamps = lamps };
        }

        private static Renderer Part(Transform parent, string name, PrimitiveType prim, Vector3 pos, Quaternion rot, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(prim);
            Collider col = go.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            go.name = name;
            go.layer = WaterReflection.AircraftLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mat(c);
            return r;
        }

        private static void Label(Transform parent, string text, Vector3 pos, float capHeight)
        {
            var go = new GameObject("SkiSelector_Label_" + text);
            go.layer = WaterReflection.AircraftLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            // A TextMesh shows its −z face: turn it so it reads from the pilot's side of the plate.
            go.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.92f, 0.92f, 0.9f);
            if (tm.font != null)
            {
                tm.fontSize = tm.font.fontSize > 0 ? tm.font.fontSize : 16;
                tm.characterSize = capHeight / 1.6f;   // one unit ≈ 1.6 × the cap height (measured, AirframeVisual.Mark)
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = WorldBuilder.DepthTestedText(tm.font);
            }
        }

        private static readonly Dictionary<Color, Material> Mats = new();
        private static Material Mat(Color c)
        {
            if (Mats.TryGetValue(c, out Material m) && m != null) return m;
            m = new Material(Shader.Find("FlyingGame/Lit") ?? Shader.Find("Unlit/Color")) { color = c };
            Mats[c] = m;
            return m;
        }

        private static void EnsureMaterials()
        {
            if (_lampOff != null) return;
            Shader unlit = Shader.Find("Unlit/Color");
            _lampOff = new Material(unlit) { color = new Color(0.08f, 0.10f, 0.08f) };
            _lampGreen = new Material(unlit) { color = new Color(0.25f, 1f, 0.35f) };
            _lampAmber = new Material(unlit) { color = new Color(1f, 0.72f, 0.1f) };
        }

        /// <summary>Lever and lamps from the selector; editor shots call it with a selector of their own.</summary>
        public void Show(SkiSelector sel)
        {
            if (sel == null) return;
            int lit = SkiSelectorPart.LitLamp(sel.Annunciation);
            // The lever is commanded, but a hand pulls it through the gate: ease it there in a fifth of a second.
            float target = (float)SkiSelectorPart.LeverDeg(sel.Commanded);
            foreach (Built b in _built)
            {
                if (b.Root == null) continue;
                Quaternion want = Quaternion.AngleAxis(-target, Vector3.right);
                b.Lever.localRotation = Application.isPlaying ? Quaternion.RotateTowards(b.Lever.localRotation, want, 350f * Time.deltaTime) : want;
                for (int i = 0; i < b.Lamps.Length; i++)
                    if (b.Lamps[i] != null) b.Lamps[i].sharedMaterial = i != lit ? _lampOff : i == 1 ? _lampAmber : _lampGreen;
            }
        }

        private void LateUpdate()
        {
            if (_for?.Skis == null || _built.Count == 0) return;
            if (_built[0].Root == null) { _built.Clear(); return; }   // the airframe was rebuilt: AirframeVisual builds anew
            Show(_for.Skis.Selector);
            if (SessionSettings.MenuOpen || VrMode.HeadTracked) return;
            // A click (Mac) or a tap (iPhone) on the lever moves it.
            Camera cam = Camera.main;
            if (cam == null) return;
            if (Input.GetMouseButtonDown(0)) TryHit(cam, Input.mousePosition, "click");
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began) TryHit(cam, t.position, "tap");
            }
        }

        private void TryHit(Camera cam, Vector2 screen, string via)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            foreach (Built b in _built)
            {
                if (b.Knob == null) continue;
                Vector3 to = b.Knob.position - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0f || along > 3f) continue;   // in the cockpit, within arm's reach of the camera
                if ((to - ray.direction * along).magnitude > HitRadiusM) continue;
                Move(_for, via);
                return;
            }
        }

        /// <summary>The Quest: a fingertip (or a closed controller grip) at a knob moves the lever, once per touch.
        /// <paramref name="knobDistanceM"/> is the hand's distance from the nearest knob.</summary>
        public bool PokeEdge(int hand, float knobDistanceM)
        {
            bool near = knobDistanceM < HitRadiusM * 0.6f;
            bool fired = near && !_poked[hand];
            if (knobDistanceM > HitRadiusM * 1.5f) _poked[hand] = false;
            if (fired) { _poked[hand] = true; Move(_for, hand == 0 ? "left hand" : "right hand"); }
            return fired;
        }
        private readonly bool[] _poked = new bool[2];
    }
}
