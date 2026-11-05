using System.Collections.Generic;
using FlyingGame.Core;
using FlyingGame.Core.Instruments;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// TOUCHING THE PANEL (Ty 2026-11-05): tap / click / poke
    ///   a sequence card   → the next card in the deck (both seats' cards change together: SequenceCardLibrary);
    ///   an altimeter      → the Kollsman popup: type the setting, or step it, or set field elevation / zero; values outside
    ///                       the instrument's window are refused (Core KollsmanAltimeter). In a headset the poke opens the
    ///                       menu on its altimeter row instead (OnGUI does not exist inside a headset);
    ///   a g-meter         → its push-to-reset: the followers spring back to the pointer.
    /// Every altimeter also gets a knob (lower left of the bezel) and a Kollsman window (3 o'clock) when the model has none
    /// (contract: &lt;prefix&gt;Altimeter_Knob, &lt;prefix&gt;Altimeter_Kollsman), and every g-meter a reset knob
    /// (&lt;prefix&gt;G_meter_Reset).
    ///
    /// Finding them: Core PanelInstrumentNames classifies the model's nodes; the parts of one instrument (face, bezel,
    /// needles) are pooled per seat and the instrument is the disc around them. A touch that lands on a stick pad, a trim
    /// slider or a button (TouchFlightControls.IsControlAt) is the control's, never the panel's.
    /// </summary>
    public sealed class CockpitTaps : MonoBehaviour
    {
        private enum Kind { Card, Altimeter, GMeter }

        private sealed class Target
        {
            public Kind Kind; public PanelSeat Seat;
            public Transform Anchor;            // a model node the instrument moves with
            public Vector3 LocalCentre;         // its centre in the anchor's frame
            public float RadiusM;
            public Renderer Card;               // cards: the face, tested as a rectangle
            public Transform Knob;              // altimeter knob / g reset (made or the model's)
            public Vector3 KnobRest;
            public float KnobPushT = -1f;
            public Renderer Window;             // the Kollsman window quad
            public Texture2D WindowTex;
            public string WindowShown = "";
            public Vector3 Centre => Anchor != null ? Anchor.TransformPoint(LocalCentre) : Vector3.zero;
        }

        private readonly List<Target> _targets = new();
        private readonly List<Object> _owned = new();
        private AirframeVisual _visual;
        private Transform _boundRoot;
        private TouchFlightControls _touch;
        private Material _knobMat, _windowMat;

        // a touch / click in progress
        private int _downId = int.MinValue;
        private Vector2 _downPos;
        private float _downT;
        private readonly bool[] _pokeArmed = { true, true };
        private Target _pokeLast0, _pokeLast1;

        // the popup
        private bool _popup;
        private string _typed = "", _popupMsg = "";
        private GUIStyle _box, _btn, _big, _lab, _field;
        private Texture2D _bg, _btnBg;
        private int _fs;

        /// <summary>The one on the flying aeroplane (VrCockpitHands feeds it fingertips).</summary>
        public static CockpitTaps Current { get; private set; }

        public bool PopupOpen => _popup;
        private CockpitRigState Cockpit => _visual != null ? _visual.Cockpit : null;

        private void Awake() { _visual = GetComponent<AirframeVisual>(); _touch = GetComponent<TouchFlightControls>(); Current = this; }
        private void OnDestroy() { if (Current == this) Current = null; Forget(); }

        // ---- binding ----

        /// <summary>Called once per placed model (AirframeVisual) with its lookup.</summary>
        public void Bind(Transform modelRoot, System.Func<string, Transform> find, Transform air)
        {
            if (modelRoot == null || modelRoot == _boundRoot) return;
            Forget();
            _boundRoot = modelRoot;
            Build(modelRoot, find, air, _targets, _owned, ref _knobMat, ref _windowMat);
            int alt = 0, g = 0, cards = 0;
            foreach (Target t in _targets) { if (t.Kind == Kind.Altimeter) alt++; else if (t.Kind == Kind.GMeter) g++; else cards++; }
            Debug.Log($"[CockpitTaps] {modelRoot.name}: {cards} card(s), {alt} altimeter(s), {g} g-meter(s) can be touched");
        }

        public void Forget()
        {
            _targets.Clear();
            foreach (Object o in _owned) if (o != null) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
            _owned.Clear();
            _boundRoot = null;
            _popup = false;
        }

        private static void Build(Transform root, System.Func<string, Transform> find, Transform air, List<Target> into, List<Object> owned,
                                  ref Material knobMat, ref Material windowMat)
        {
            // Cards: each face is its own target.
            foreach (string n in new[] { SequenceCard.FaceNode, SequenceCard.FrontFaceNode })
            {
                Transform t = find(n);
                if (t == null || !t.TryGetComponent(out Renderer r)) continue;
                into.Add(new Target { Kind = Kind.Card, Seat = n == SequenceCard.FaceNode ? PanelSeat.Rear : PanelSeat.Front, Anchor = t, Card = r,
                                      LocalCentre = t.InverseTransformPoint(r.bounds.center), RadiusM = r.bounds.extents.magnitude });
            }

            // Instruments: pool every classified node's drawn bounds per (kind, seat).
            var parts = new Dictionary<(Kind, PanelSeat), List<Transform>>();
            var knobs = new Dictionary<(Kind, PanelSeat), Transform>();
            var windows = new Dictionary<PanelSeat, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                var (k, seat) = PanelInstrumentNames.Classify(t.name);
                if (k == PanelInstrument.None) continue;
                Kind kind = k == PanelInstrument.GMeter || k == PanelInstrument.GMeterReset ? Kind.GMeter : Kind.Altimeter;
                if (k == PanelInstrument.AltimeterKnob || k == PanelInstrument.GMeterReset) { knobs[(kind, seat)] = t; continue; }
                if (k == PanelInstrument.AltimeterWindow) { windows[seat] = t; continue; }
                if (!parts.TryGetValue((kind, seat), out var l)) parts[(kind, seat)] = l = new List<Transform>();
                l.Add(t);
            }
            foreach (var kv in parts)
            {
                Bounds? b = null, face = null;
                Transform anchor = null;
                foreach (Transform t in kv.Value)
                {
                    // Faces, dials and bezels give the disc; needles only where there is nothing else (the J-3).
                    string tn = t.name.StartsWith("Model:") ? t.name.Substring(6) : t.name;
                    bool needle = tn.StartsWith("Needle", System.StringComparison.OrdinalIgnoreCase);
                    if (needle) continue;
                    bool isFace = tn.IndexOf("Face", System.StringComparison.OrdinalIgnoreCase) >= 0 || tn.StartsWith("Dial", System.StringComparison.OrdinalIgnoreCase);
                    foreach (Renderer r in t.GetComponents<Renderer>())
                    {
                        if (!r.enabled) continue;
                        if (b == null) { b = r.bounds; anchor = t; } else { Bounds bb = b.Value; bb.Encapsulate(r.bounds); b = bb; }
                        if (isFace) { if (face == null) face = r.bounds; else { Bounds fb = face.Value; fb.Encapsulate(r.bounds); face = fb; } }
                    }
                }
                Vector3 centre; float radius;
                if (b != null)
                {
                    centre = b.Value.center;
                    // The disc's radius: its two largest extents (a face is thin across the panel).
                    Vector3 e = b.Value.extents;
                    float a1 = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
                    radius = Mathf.Clamp(a1, 0.015f, 0.065f);
                }
                else
                {
                    // Needles only: the hands turn about the dial's centre, which is their pivot.
                    anchor = kv.Value[0];
                    centre = anchor.position;
                    radius = 0.038f;    // a 3-1/8 in case (E)
                }
                if (anchor == null) continue;
                var tgt = new Target { Kind = kv.Key.Item1, Seat = kv.Key.Item2, Anchor = anchor, LocalCentre = anchor.InverseTransformPoint(centre), RadiusM = radius };
                Vector3 aft = air != null ? -air.forward : -anchor.forward;
                // The window and knob go on the FACE's front plane, not the bezel's centre: a deep bezel (the Pitts') would
                // otherwise hide them behind the dial. The face's own bounds where it has one, else the whole instrument's.
                Bounds front = face ?? b ?? new Bounds(centre, Vector3.zero);
                Vector3 fe = front.extents;
                float depth = Mathf.Abs(fe.x * aft.x) + Mathf.Abs(fe.y * aft.y) + Mathf.Abs(fe.z * aft.z);
                centre += aft * (Vector3.Dot(front.center - centre, aft) + depth);
                Vector3 right = air != null ? air.right : anchor.right, up = air != null ? air.up : anchor.up;
                if (knobs.TryGetValue(kv.Key, out Transform knob)) { tgt.Knob = knob; }
                else tgt.Knob = MakeKnob(anchor, centre, radius, aft, right, up, kv.Key.Item1 == Kind.GMeter, owned, ref knobMat);
                if (tgt.Knob != null) tgt.KnobRest = tgt.Knob.localPosition;
                if (tgt.Kind == Kind.Altimeter)
                {
                    windows.TryGetValue(tgt.Seat, out Transform w);
                    MakeWindow(tgt, w, centre, radius, aft, right, up, owned, ref windowMat);
                }
                into.Add(tgt);
            }
        }

        /// <summary>The knob a model lacks: a small black cylinder at the bezel's lower left (where an altimeter's setting
        /// knob and an accelerometer's push-to-reset sit), standing proud toward the pilot. Parented to the instrument.</summary>
        private static Transform MakeKnob(Transform anchor, Vector3 centre, float r, Vector3 aft, Vector3 right, Vector3 up, bool reset,
                                          List<Object> owned, ref Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = reset ? "Live:G_meter_Reset" : "Live:Altimeter_Knob";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            float kr = Mathf.Clamp(r * 0.17f, 0.004f, 0.0075f), len = 0.010f;
            Vector3 dir = (-right - up).normalized;
            go.transform.position = centre + dir * (r * 0.92f) + aft * (len * 0.5f);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, aft);
            go.transform.localScale = new Vector3(kr * 2f, len * 0.5f, kr * 2f);
            go.transform.SetParent(anchor, true);
            if (mat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(s) { name = "Live knob", color = new Color(0.05f, 0.05f, 0.05f) };
                owned.Add(mat);
            }
            go.GetComponent<Renderer>().sharedMaterial = mat;
            owned.Add(go);
            return go.transform;
        }

        /// <summary>The Kollsman window: the model's own quad if it has one, else a small one at 3 o'clock inside the
        /// dial, a millimetre proud of the face. Digits drawn into a texture (CockpitDisplays' font: no font to lose).</summary>
        private static void MakeWindow(Target t, Transform model, Vector3 centre, float r, Vector3 aft, Vector3 right, Vector3 up,
                                       List<Object> owned, ref Material mat)
        {
            Renderer rend;
            if (model != null && model.TryGetComponent(out rend)) { }
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Live:Altimeter_Kollsman";
                Object.DestroyImmediate(go.GetComponent<Collider>());
                float w = r * 0.42f, h = r * 0.16f;
                go.transform.position = centre + right * (r * 0.56f) + aft * 0.0012f;
                go.transform.rotation = Quaternion.LookRotation(-aft, up);   // a Quad faces -z: its front toward the pilot
                go.transform.localScale = new Vector3(w, h, 1f);
                go.transform.SetParent(t.Anchor, true);
                rend = go.GetComponent<Renderer>();
                owned.Add(go);
            }
            if (mat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                mat = new Material(s) { name = "Kollsman window" };
                owned.Add(mat);
            }
            var inst = new Material(mat);
            owned.Add(inst);
            t.WindowTex = new Texture2D(96, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Kollsman" };
            owned.Add(t.WindowTex);
            SequenceCard.SetMainTexture(inst, t.WindowTex);
            rend.sharedMaterial = inst;
            t.Window = rend;
        }

        /// <summary>Editor shots (CockpitEyeShots, no play mode): draw every Kollsman window at <paramref name="inHg"/>.</summary>
        public void ShowWindowsForShots(double inHg)
        {
            string w = KollsmanAltimeter.Window(inHg);
            foreach (Target t in _targets) if (t.WindowTex != null) { t.WindowShown = w; DrawDigits(t.WindowTex, w); }
        }

        // ---- every frame ----

        private void Update()
        {
            if (_visual == null) return;
            if (_visual.ModelRoot != null) Bind(_visual.ModelRoot, _visual.FindInModel, transform);
            if (_targets.Count == 0) return;
            UpdateWindows();
            AnimateKnobs();
            if (XrHeadset.Active) return;          // the headset pokes (VrCockpitHands → Poke)
            if (!_popup) Taps();
        }

        private void UpdateWindows()
        {
            CockpitRigState c = Cockpit;
            if (c == null) return;
            string s = KollsmanAltimeter.Window(c.Altimeter.InHg);
            foreach (Target t in _targets)
            {
                if (t.WindowTex == null || t.WindowShown == s) continue;
                t.WindowShown = s;
                DrawDigits(t.WindowTex, s);
            }
        }

        private static void DrawDigits(Texture2D tex, string s)
        {
            var px = new Color32[tex.width * tex.height];
            var bg = new Color32(18, 18, 18, 255); var fg = new Color32(240, 236, 220, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            const int scale = 3;                         // 5x7 glyphs → 15 x 21 px
            int adv = 6 * scale, x0 = (tex.width - s.Length * adv + scale) / 2, y0 = (tex.height - 7 * scale) / 2;
            for (int ci = 0; ci < s.Length; ci++)
            {
                byte[] rows = LcdFont.Rows(s[ci]);
                for (int ry = 0; ry < 7; ry++)
                    for (int rx = 0; rx < 5; rx++)
                    {
                        if ((rows[ry] & (1 << (4 - rx))) == 0) continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int x = x0 + ci * adv + rx * scale + sx, y = y0 + (6 - ry) * scale + sy;
                                if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) px[y * tex.width + x] = fg;
                            }
                    }
            }
            tex.SetPixels32(px); tex.Apply(false);
        }

        private void AnimateKnobs()
        {
            foreach (Target t in _targets)
            {
                if (t.Knob == null || t.KnobPushT < 0f) continue;
                float k = Mathf.Clamp01((Time.unscaledTime - t.KnobPushT) / 0.25f);
                float depth = Mathf.Sin(k * Mathf.PI) * 0.004f;   // 4 mm in and back
                Vector3 aftLocal = t.Knob.parent != null ? t.Knob.parent.InverseTransformDirection(-transform.forward) : Vector3.back;
                t.Knob.localPosition = t.KnobRest - aftLocal * (depth / Mathf.Max(1e-4f, t.Knob.parent != null ? t.Knob.parent.lossyScale.x : 1f));
                if (k >= 1f) { t.KnobPushT = -1f; t.Knob.localPosition = t.KnobRest; }
            }
        }

        // ---- glass and mouse ----

        private void Taps()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began && _downId == int.MinValue) { _downId = t.fingerId; _downPos = t.position; _downT = Time.unscaledTime; }
                    else if (t.fingerId == _downId && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                    {
                        _downId = int.MinValue;
                        if (t.phase == TouchPhase.Ended) TryTap(cam, _downPos, t.position);
                    }
                }
                return;
            }
            if (Input.GetMouseButtonDown(0)) { _downId = -1; _downPos = Input.mousePosition; _downT = Time.unscaledTime; }
            else if (Input.GetMouseButtonUp(0) && _downId == -1) { _downId = int.MinValue; TryTap(cam, _downPos, Input.mousePosition); }
        }

        private void TryTap(Camera cam, Vector2 down, Vector2 up)
        {
            // A tap, not a drag or a hold, and not on a control the glass already owns.
            if ((up - down).magnitude > UiLayout.S * 0.02f || Time.unscaledTime - _downT > 0.6f) return;
            if (_touch != null && _touch.IsControlAt(down)) return;
            if (SessionSettings.MenuOpen || SessionSettings.PanelPause) return;
            Ray ray = cam.ScreenPointToRay(down);
            Target best = null; float bestT = 3f;
            foreach (Target t in _targets)
            {
                float d = Hit(t, ray);
                if (d >= 0f && d < bestT) { bestT = d; best = t; }
            }
            if (best != null) Act(best, viaHeadset: false);
        }

        /// <summary>Distance along the ray to the target, or −1 for a miss.</summary>
        private static float Hit(Target t, Ray ray)
        {
            if (t.Anchor == null) return -1f;
            if (t.Card != null)
            {
                // The card as the rectangle it is: the ray in the face's own frame against its mesh bounds.
                Transform f = t.Card.transform;
                MeshFilter mf = f.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return -1f;
                var local = new Ray(f.InverseTransformPoint(ray.origin), f.InverseTransformDirection(ray.direction));
                Bounds b = mf.sharedMesh.bounds; b.Expand(0.004f / Mathf.Max(1e-4f, f.lossyScale.x));
                if (!b.IntersectRay(local, out float dl)) return -1f;
                return (f.TransformPoint(local.GetPoint(dl)) - ray.origin).magnitude;
            }
            Vector3 c = t.Centre;
            float along = Vector3.Dot(c - ray.origin, ray.direction);
            if (along < 0f) return -1f;
            float miss = (ray.origin + ray.direction * along - c).magnitude;
            return miss <= t.RadiusM * 1.1f ? along : -1f;
        }

        // ---- the headset's fingertip ----

        /// <summary>A pointing fingertip (world) this frame, hand 0 left / 1 right; called by VrCockpitHands. A poke fires
        /// once as the tip comes within reach of an instrument and re-arms when it has gone 2 cm further away.</summary>
        public void Poke(int hand, Vector3 tip)
        {
            if (_targets.Count == 0 || hand < 0 || hand > 1) return;
            Target near = null; float bestD = float.MaxValue;
            foreach (Target t in _targets)
            {
                float d = PokeDistance(t, tip);
                if (d < bestD) { bestD = d; near = t; }
            }
            Target last = hand == 0 ? _pokeLast0 : _pokeLast1;
            if (_pokeArmed[hand] && near != null && bestD <= 0f)
            {
                _pokeArmed[hand] = false;
                if (hand == 0) _pokeLast0 = near; else _pokeLast1 = near;
                Act(near, viaHeadset: true);
                XrHandInput.Tick(hand == 0, 0.5f, 0.03f);
            }
            else if (!_pokeArmed[hand] && (last == null || PokeDistance(last, tip) > 0.02f)) _pokeArmed[hand] = true;
        }

        /// <summary>How far the tip is outside the instrument's touch volume (≤ 0 = touching): a disc of its radius, 1.5 cm
        /// deep in front of the face.</summary>
        private float PokeDistance(Target t, Vector3 tip)
        {
            if (t.Anchor == null) return float.MaxValue;
            Vector3 aft = -transform.forward;
            Vector3 c = t.Card != null ? t.Card.bounds.center : t.Centre;
            float r = t.Card != null ? Mathf.Min(t.Card.bounds.extents.x, t.Card.bounds.extents.y) + 0.01f : t.RadiusM;
            Vector3 d = tip - c;
            float depth = Vector3.Dot(d, aft);
            float lateral = (d - aft * depth).magnitude;
            return Mathf.Max(lateral - r, Mathf.Abs(depth - 0.005f) - 0.015f);
        }

        // ---- what a touch does ----

        private void Act(Target t, bool viaHeadset)
        {
            switch (t.Kind)
            {
                case Kind.Card:
                    SequenceCardLibrary.Next();
                    break;
                case Kind.GMeter:
                    t.KnobPushT = Time.unscaledTime;
                    Cockpit?.ResetGTellTales();
                    break;
                case Kind.Altimeter:
                    t.KnobPushT = Time.unscaledTime;
                    if (viaHeadset) QuestRig.OpenMenuAtRow?.Invoke("altimeter");
                    else OpenPopup();
                    break;
            }
        }

        // ---- the Kollsman popup (glass and Mac) ----

        public void OpenPopup()
        {
            CockpitRigState c = Cockpit;
            if (c == null) return;
            _popup = true;
            _typed = KollsmanAltimeter.Window(c.Altimeter.InHg);
            _popupMsg = "";
        }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(UiLayout.S * 0.028f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= MakeTex(new Color(0.04f, 0.06f, 0.09f, 0.95f));
            _btnBg ??= MakeTex(new Color(0.18f, 0.24f, 0.32f, 0.97f));
            Font f = UiFont.Get();
            _box = new GUIStyle { normal = { background = _bg } };
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg } };
            _big = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 2.0f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.93f, 0.85f) } };
            _lab = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.8f), alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.75f, 0.82f, 0.9f) } };
            _field = new GUIStyle(GUI.skin.textField) { font = f, fontSize = Mathf.RoundToInt(fs * 1.2f), alignment = TextAnchor.MiddleCenter };
        }

        private static Texture2D MakeTex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void OnGUI()
        {
            if (!_popup) return;
            CockpitRigState c = Cockpit;
            if (c == null || XrHeadset.Active) { _popup = false; return; }
            EnsureStyles();
            AltimeterSetting a = c.Altimeter;
            float w = Mathf.Min(Screen.width * 0.9f, UiLayout.S * 1.05f), lh = UiLayout.ButtonH, gap = UiLayout.Gap;
            float h = lh * 7.6f + gap * 8f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(box, GUIContent.none, _box);
            float x = box.x + gap, y = box.y + gap, cw = w - 2f * gap;

            // The scroll wheel over the popup turns the knob, a hundredth a click.
            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && box.Contains(e.mousePosition))
            {
                a.Step(e.delta.y < 0 ? +1 : -1);
                _typed = KollsmanAltimeter.Window(a.InHg); _popupMsg = "";
                e.Use();
            }

            GUI.Label(new Rect(x, y, cw, lh * 0.7f), "ALTIMETER  —  Kollsman window", _lab); y += lh * 0.7f;
            GUI.Label(new Rect(x, y, cw, lh * 1.3f), $"{KollsmanAltimeter.Window(a.InHg)} inHg", _big); y += lh * 1.3f;
            GUI.Label(new Rect(x, y, cw, lh * 0.7f), $"{KollsmanAltimeter.Hpa(a.InHg):F0} hPa   ·   reads {c.Altimeter.IndicatedFt(c.StaticPa):N0} ft   ·   window {a.Range.MinInHg:F2}–{a.Range.MaxInHg:F2}", _lab);
            y += lh * 0.7f + gap;

            float bw = (cw - 3f * gap) / 4f;
            int step = 0;
            if (GUI.Button(new Rect(x, y, bw, lh), "−0.10", _btn)) step = -10;
            if (GUI.Button(new Rect(x + (bw + gap), y, bw, lh), "−0.01", _btn)) step = -1;
            if (GUI.Button(new Rect(x + 2f * (bw + gap), y, bw, lh), "+0.01", _btn)) step = +1;
            if (GUI.Button(new Rect(x + 3f * (bw + gap), y, bw, lh), "+0.10", _btn)) step = +10;
            if (step != 0) { a.Step(step); _typed = KollsmanAltimeter.Window(a.InHg); _popupMsg = ""; }
            y += lh + gap;

            float fw = cw * 0.62f;
            GUI.SetNextControlName("kollsman");
            _typed = GUI.TextField(new Rect(x, y, fw, lh), _typed, 12, _field);
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (GUI.Button(new Rect(x + fw + gap, y, cw - fw - gap, lh), "Set", _btn) || enter)
            {
                if (a.TrySet(_typed, out string why)) { _popupMsg = $"set {KollsmanAltimeter.Window(a.InHg)}"; _typed = KollsmanAltimeter.Window(a.InHg); }
                else _popupMsg = "refused: " + why;
            }
            y += lh + gap;

            float hw = (cw - gap) * 0.5f;
            if (GUI.Button(new Rect(x, y, hw, lh), "Field elevation (QNH)", _btn))
            {
                _popupMsg = c.SetAltimeterToRead(c.TrueAltitudeFt) ? "set to read the elevation here" : $"the window stops at {KollsmanAltimeter.Window(a.InHg)}";
                _typed = KollsmanAltimeter.Window(a.InHg);
            }
            if (GUI.Button(new Rect(x + hw + gap, y, hw, lh), "Zero here (QFE)", _btn))
            {
                _popupMsg = c.SetAltimeterToRead(0.0) ? "zeroed" : $"cannot zero here: the window stops at {KollsmanAltimeter.Window(a.InHg)} ({a.Range.Why})";
                _typed = KollsmanAltimeter.Window(a.InHg);
            }
            y += lh + gap;

            GUI.Label(new Rect(x, y, cw, lh * 0.8f), _popupMsg, _lab); y += lh * 0.8f + gap;
            if (GUI.Button(new Rect(x + cw * 0.3f, y, cw * 0.4f, lh), "Done", _btn) || (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)) _popup = false;
        }
    }
}
