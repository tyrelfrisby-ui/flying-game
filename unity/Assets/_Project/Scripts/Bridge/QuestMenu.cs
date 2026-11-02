using System.Collections.Generic;
using FlyingGame.Core.Vr;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE MENU INSIDE THE HEADSET. Every menu in this game is OnGUI, and OnGUI draws to the phone's / Mac's
    /// screen — on a Quest that is a buffer nobody sees. Without this the app would open on a start page the
    /// pilot cannot see, paused, with no way out. So a small world-space panel, built from the game's own font,
    /// carries what a pilot needs from inside the cockpit:
    ///
    ///   FLY / RESUME · RESTART FLIGHT · Aircraft · Start · Seat · View · Display 72 / 90 Hz · Recentre ·
    ///   Reset flight · Bail out / Deploy BRS / Eject (asks twice)
    ///
    /// It is deliberately NOT the whole landing page: lessons, challenges, weather and multiplayer have OnGUI
    /// cards of their own that would be just as invisible, so a flight started from here is FREE FLIGHT
    /// (<see cref="SessionSettings.ChallengeId"/> = null). Bringing those across is Codex's VR-UI work.
    ///
    /// Point with either hand (controller or tracked hand) and pull the trigger / press A or X / pinch; or move
    /// the highlight with a thumbstick and push it left / right to change a value. B, Y or the menu button
    /// closes it in flight. It opens by itself whenever the landing page is up, and pauses the sim while it is
    /// open in flight, exactly as the Options panel does.
    ///
    /// Drawn with FlyingGame/XrOverlay (force-included), which ignores depth, so the panel shows through the
    /// instrument panel it is floating in front of.
    /// </summary>
    public sealed class QuestMenu
    {
        private sealed class Row
        {
            public System.Func<string> Label;
            public System.Action Activate;
            public System.Action<int> Cycle;
            public System.Func<bool> Visible;
            public TextMesh Text;
        }

        public const float Distance = 0.8f, RowHeight = 0.042f, Width = 0.62f, CharHeight = 0.024f;

        private readonly List<Row> _rows = new();
        private readonly List<Row> _shown = new();
        private GameObject _root;
        private Transform _highlight;
        private LineRenderer _laser;
        private TextMesh _title, _hint;
        private bool _open, _built;
        private int _sel, _openedFrame;
        private float _navHeld, _cycleHeld;
        private bool _prevTrig, _prevA, _prevX, _prevB, _prevY, _prevPinch;
        private float _confirmUntil;
        private Material _textMat, _panelMat, _barMat, _laserMat;
        private StartMenu _start;
        private QuestRig _rig;

        public bool IsOpen => _open;

        private ChaseCamera Chase => _rig != null ? _rig.GetComponent<ChaseCamera>() : null;
        private CockpitView Cockpit => Chase != null ? Chase.Cockpit : null;
        private FlightSimDriver Driver => Chase != null ? Chase.Driver : null;
        private TouchFlightControls Controls => Driver != null ? Driver.GetComponent<TouchFlightControls>() : null;
        private StartMenu Landing => _start != null ? _start : _start = Object.FindFirstObjectByType<StartMenu>();
        private bool OnLanding => SessionSettings.MenuOpen && Landing != null && Landing.IsOpen;

        private static readonly (SessionSettings.Start mode, string name)[] Starts =
        {
            (SessionSettings.Start.InTheAir, "In the air"), (SessionSettings.Start.OnTheRunway, "On the runway"),
            (SessionSettings.Start.OnFinal, "On final, 300 ft"), (SessionSettings.Start.InAeroBox, "Aerobatic box"),
            (SessionSettings.Start.InCombatZone, "Combat zone"), (SessionSettings.Start.InThermal, "In a thermal"),
        };

        // ---- open / close ----

        public void Toggle(QuestRig rig)
        {
            if (_open) Close(force: false);
            else Open(rig);
        }

        public void Open(QuestRig rig)
        {
            _rig = rig;
            if (!_built) Build();
            _open = true;
            _openedFrame = Time.frameCount;
            _root.SetActive(true);
            XrHeadset.MenuOpen = true;
            if (!OnLanding) { SessionSettings.PanelPause = true; SessionSettings.ApplyTimeScale(); }
            _sel = 0;
            _confirmUntil = 0f;
            PlaceInFrontOfHead();
        }

        /// <summary>Close it. On the landing page it cannot be closed except by flying — there is nothing behind it
        /// the pilot could see.</summary>
        public void Close(bool force)
        {
            if (!_open) return;
            if (!force && OnLanding) return;
            _open = false;
            XrHeadset.MenuOpen = false;
            if (_root != null) _root.SetActive(false);
            if (!OnLanding) { SessionSettings.PanelPause = false; SessionSettings.ApplyTimeScale(); }
        }

        /// <summary>Editor shot tool (QuestMenuShots): build, open and lay the menu out with no headset and no Update.</summary>
        public GameObject OpenForTesting(QuestRig rig)
        {
            Open(rig);
            Refresh();
            return _root;
        }

        /// <summary>Every frame from <see cref="QuestRig"/>.</summary>
        public void Tick(QuestRig rig)
        {
            _rig = rig;
            if (!_open && OnLanding) Open(rig);
            if (!_open) return;
            Refresh();
            if (Time.frameCount != _openedFrame) HandleInput();
        }

        private void PlaceInFrontOfHead()
        {
            Transform head = _rig.transform;
            Vector3 f = head.forward;
            Vector3 flat = new Vector3(f.x, 0f, f.z);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 pos = head.position + flat * Distance - Vector3.up * 0.05f;
            Transform air = Chase != null ? Chase.Target : null;
            _root.transform.SetParent(air, true);
            _root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flat, Vector3.up));
        }

        // ---- the rows ----

        private void Build()
        {
            _built = true;
            Font font = UiFont.Get();
            Shader sh = Shader.Find("FlyingGame/XrOverlay");   // ours: ZTest Always, force-included (BuildScript)
            if (sh == null) { Debug.LogError("[Quest] FlyingGame/XrOverlay shader missing from the build: no VR menu"); sh = Shader.Find("Unlit/Color"); }
            _textMat = new Material(sh) { mainTexture = font.material.mainTexture, renderQueue = 4002 };
            Font.textureRebuilt += f => { if (f == font && _textMat != null) _textMat.mainTexture = font.material.mainTexture; };
            _panelMat = Solid(sh, new Color(0.05f, 0.08f, 0.12f, 0.92f), 4000);
            _barMat = Solid(sh, new Color(0.16f, 0.34f, 0.46f, 0.98f), 4001);
            _laserMat = Solid(sh, new Color(0.5f, 0.95f, 1f, 0.8f), 4003);

            _root = new GameObject("VR Menu");
            if (Application.isPlaying) Object.DontDestroyOnLoad(_root);

            AddRow(() => OnLanding ? "FLY" : "RESUME", () => { if (OnLanding) FlyNow(); else Close(force: false); });
            AddRow(() => "RESTART FLIGHT", FlyNow, visible: () => !OnLanding);
            AddRow(() => $"Aircraft:  {AircraftName(SessionSettings.AircraftId)}" + (!OnLanding && Driver != null && Driver.AircraftId != SessionSettings.AircraftId ? "   (restart to fly it)" : ""),
                () => CycleAircraft(1), CycleAircraft);
            AddRow(() => $"Start:  {StartName()}", () => CycleStart(1), CycleStart);
            AddRow(() => $"Seat:  {(Cockpit != null ? Cockpit.SeatLabel : "-")}", () => CycleSeat(1), CycleSeat, () => Cockpit != null && Cockpit.SeatCount > 1);
            AddRow(() => $"View:  {(ChaseCamera.InCockpit ? "Cockpit" : "Outside")}", ToggleView, _ => ToggleView());
            AddRow(() => $"Display:  {(XrHeadset.Prefer90Hz ? "90 Hz" : "72 Hz")}" + (XrHeadset.CurrentRefreshHz > 0f ? $"   (running {XrHeadset.CurrentRefreshHz:F0})" : ""),
                () => XrHeadset.Prefer90Hz = !XrHeadset.Prefer90Hz, _ => XrHeadset.Prefer90Hz = !XrHeadset.Prefer90Hz);
            AddRow(() => "Recentre the view", () => { XrHeadset.Recentre(); });
            AddRow(() => "Reset flight", () => { Controls?.MenuReset(); Close(force: false); }, visible: () => !OnLanding && Controls != null);
            AddRow(() => Time.unscaledTime < _confirmUntil ? $"PRESS AGAIN: {HandleName()}" : HandleName(), PullHandle,
                visible: () => !OnLanding && Controls != null && Controls.MenuHandleAvailable);

            var panel = Quad("Panel", _panelMat);
            panel.SetParent(_root.transform, false);
            _highlight = Quad("Highlight", _barMat);
            _highlight.SetParent(_root.transform, false);
            _title = Text("Title", "AERO PLAYGROUND", CharHeight * 1.2f);
            _hint = Text("Hint", "", CharHeight * 0.7f);
            foreach (Row r in _rows) r.Text = Text("Row", "", CharHeight);

            _laser = new GameObject("Pointer").AddComponent<LineRenderer>();
            _laser.transform.SetParent(_root.transform, false);
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.startWidth = _laser.endWidth = 0.003f;
            _laser.sharedMaterial = _laserMat;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
        }

        private void AddRow(System.Func<string> label, System.Action activate, System.Action<int> cycle = null, System.Func<bool> visible = null) =>
            _rows.Add(new Row { Label = label, Activate = activate, Cycle = cycle, Visible = visible ?? (() => true) });

        /// <summary>Lay the visible rows out top to bottom and refresh their text (only when it changes).</summary>
        private void Refresh()
        {
            _shown.Clear();
            foreach (Row r in _rows)
            {
                bool v = r.Visible();
                r.Text.gameObject.SetActive(v);
                if (v) _shown.Add(r);
            }
            _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, _shown.Count - 1));
            float h = (_shown.Count + 2.6f) * RowHeight;
            Transform panel = _root.transform.Find("Panel");
            panel.localPosition = new Vector3(0f, 0f, 0.002f);
            panel.localScale = new Vector3(Width, h, 1f);
            float top = h * 0.5f;
            Place(_title, top - RowHeight * 0.75f);
            for (int i = 0; i < _shown.Count; i++)
            {
                Row r = _shown[i];
                string s = (r.Cycle != null ? "<  " : "    ") + r.Label() + (r.Cycle != null ? "  >" : "");
                if (r.Text.text != s) r.Text.text = s;
                Place(r.Text, top - RowHeight * (i + 1.75f));
            }
            _hint.text = XrHeadset.HandsOnly ? "point and pinch  -  hold a left pinch to close" : "point + trigger / A   stick < > changes   B closes";
            Place(_hint, -top + RowHeight * 0.55f);
            _highlight.localPosition = new Vector3(0f, top - RowHeight * (_sel + 1.75f), 0.001f);
            _highlight.localScale = new Vector3(Width - 0.02f, RowHeight * 0.9f, 1f);
        }

        private void Place(TextMesh t, float y) => t.transform.localPosition = new Vector3(-Width * 0.5f + 0.03f, y, 0f);

        // ---- input ----

        private void HandleInput()
        {
            QuestControllerFrame f = XrControllers.Read(out _);
            float dt = Time.unscaledDeltaTime;

            // Pointing: the right hand if it is there, else the left.
            int pointed = -1;
            bool aimed = Aim(false, out Vector3 o, out Vector3 d) || Aim(true, out o, out d);
            if (aimed && HitRow(o, d, out pointed, out Vector3 hit))
            {
                _laser.enabled = true;
                _laser.SetPosition(0, o); _laser.SetPosition(1, hit);
                if (pointed >= 0) _sel = pointed;
            }
            else if (aimed)
            {
                _laser.enabled = true;
                _laser.SetPosition(0, o); _laser.SetPosition(1, o + d * 1.5f);
            }
            else _laser.enabled = false;

            // Thumbsticks: up / down moves the highlight, left / right changes a value (with key repeat).
            double sy = System.Math.Abs(f.RightY) > System.Math.Abs(f.LeftY) ? f.RightY : f.LeftY;
            double sx = System.Math.Abs(f.RightX) > System.Math.Abs(f.LeftX) ? f.RightX : f.LeftX;
            if (Repeat(System.Math.Abs(sy) > 0.6 && System.Math.Abs(sy) > System.Math.Abs(sx), ref _navHeld, dt))
                _sel = Mathf.Clamp(_sel + (sy > 0 ? -1 : 1), 0, _shown.Count - 1);
            if (Repeat(System.Math.Abs(sx) > 0.6 && System.Math.Abs(sx) >= System.Math.Abs(sy), ref _cycleHeld, dt) && _sel < _shown.Count)
                _shown[_sel].Cycle?.Invoke(sx > 0 ? 1 : -1);

            bool trig = f.RightTrigger > 0.6 || f.LeftTrigger > 0.6;
            bool pinch = f.HandsOnly && (f.RightPinch > 0.8 || f.LeftPinch > 0.8);
            bool select = (trig && !_prevTrig) || (f.A && !_prevA) || (f.X && !_prevX) || (pinch && !_prevPinch);
            bool back = (f.B && !_prevB) || (f.Y && !_prevY);
            _prevTrig = trig; _prevA = f.A; _prevX = f.X; _prevB = f.B; _prevY = f.Y; _prevPinch = pinch;

            if (select && _sel < _shown.Count) _shown[_sel].Activate?.Invoke();
            else if (back) Close(force: false);
        }

        private static bool Repeat(bool held, ref float t, float dt)
        {
            if (!held) { t = 0f; return false; }
            bool fire = t == 0f;
            t += dt;
            if (t > 0.45f) { t -= 0.18f; fire = true; }
            return fire;
        }

        private bool Aim(bool left, out Vector3 origin, out Vector3 dir)
        {
            origin = default; dir = Vector3.forward;
            if (!XrControllers.TryPointer(left, out Vector3 p, out Quaternion q)) return false;
            if (!_rig.TrackingToWorld(p, q, out origin, out Quaternion wq)) return false;
            dir = wq * Vector3.forward;
            return true;
        }

        private bool HitRow(Vector3 o, Vector3 d, out int row, out Vector3 hit)
        {
            row = -1; hit = default;
            Transform t = _root.transform;
            Vector3 n = t.forward;
            float den = Vector3.Dot(d, n);
            if (Mathf.Abs(den) < 1e-4f) return false;
            float dist = Vector3.Dot(t.position - o, n) / den;
            if (dist <= 0f || dist > 5f) return false;
            hit = o + d * dist;
            Vector3 local = t.InverseTransformPoint(hit);
            float h = (_shown.Count + 2.6f) * RowHeight;
            if (Mathf.Abs(local.x) > Width * 0.5f || Mathf.Abs(local.y) > h * 0.5f) return false;
            float fromTop = h * 0.5f - local.y;
            int i = Mathf.FloorToInt(fromTop / RowHeight - 1.25f);
            row = i >= 0 && i < _shown.Count ? i : -1;
            return true;
        }

        // ---- actions ----

        private void FlyNow()
        {
            SessionSettings.ChallengeId = null;     // free flight: a lesson's OnGUI cards could not be seen in here
            if (Landing != null) Landing.Fly();
            Close(force: true);
            if (Cockpit != null && Chase != null) Chase.SetView(ChaseCamera.View.Cockpit);
            XrHeadset.Recentre();
        }

        private static string AircraftName(string id)
        {
            foreach (var f in SessionSettings.Fleet) if (f.id == id) return f.name;
            return id;
        }

        private static void CycleAircraft(int step)
        {
            var fleet = SessionSettings.Fleet;
            int i = System.Array.FindIndex(fleet, f => f.id == SessionSettings.AircraftId);
            SessionSettings.AircraftId = fleet[((i < 0 ? 0 : i) + step + fleet.Length) % fleet.Length].id;
        }

        private static string StartName()
        {
            foreach (var s in Starts) if (s.mode == SessionSettings.StartMode) return s.name;
            return SessionSettings.StartMode.ToString();
        }

        private static void CycleStart(int step)
        {
            int i = System.Array.FindIndex(Starts, s => s.mode == SessionSettings.StartMode);
            SessionSettings.StartMode = Starts[((i < 0 ? 0 : i) + step + Starts.Length) % Starts.Length].mode;
        }

        private void CycleSeat(int step)
        {
            if (Cockpit == null) return;
            int n = Cockpit.SeatCount;
            Cockpit.SelectSeat((Cockpit.SeatIndex + step + n) % n);   // QuestRig recentres on the seat change
        }

        private void ToggleView()
        {
            if (Chase == null) return;
            Chase.SetView(ChaseCamera.InCockpit ? ChaseCamera.View.RelativeWind : ChaseCamera.View.Cockpit);
        }

        private string HandleName() => Controls == null ? "" : Controls.MenuHandleLabel;

        /// <summary>Bail out / BRS / eject asks twice: the first press arms it for 3 s.</summary>
        private void PullHandle()
        {
            if (Time.unscaledTime >= _confirmUntil) { _confirmUntil = Time.unscaledTime + 3f; return; }
            _confirmUntil = 0f;
            Controls?.MenuPullHandle();
            Close(force: false);
        }

        // ---- building blocks ----

        private static Material Solid(Shader sh, Color c, int queue) =>
            new Material(sh) { mainTexture = Texture2D.whiteTexture, color = c, renderQueue = queue };

        private static Transform Quad(string name, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>());
            else Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // A quad is seen from its -z side, and the pilot sits on the panel's -z side looking along its forward:
            // no turn needed (and the text shader does not cull, so it would show either way).
            return go.transform;
        }

        private TextMesh Text(string name, string s, float charHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            // Like the quad: a TextMesh reads left to right from its -z side, which is where the pilot is.
            var t = go.AddComponent<TextMesh>();
            t.font = UiFont.Get();
            t.fontSize = 64;
            // A TextMesh line is about fontSize x characterSize / 10 world units tall.
            t.characterSize = charHeight * 10f / t.fontSize;
            t.anchor = TextAnchor.MiddleLeft;
            t.alignment = TextAlignment.Left;
            t.color = new Color(0.92f, 0.95f, 1f, 1f);
            t.text = s;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _textMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }
    }
}
