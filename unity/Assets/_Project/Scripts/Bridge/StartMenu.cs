using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Landing page (owner request): pick the aircraft, where to start (in the air, or on the runway at
    /// any of the four airports), a challenge or free flight, and the conditions (wind, turbulence,
    /// temperature, thermals). Shown at launch and from the MENU button; the sim is paused while open.
    /// OnGUI, DPI-scaled, styles built from scratch (iOS strips the default skin).
    /// </summary>
    public sealed class StartMenu : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public WeatherController Weather;
        public ChallengeController Challenges;
        public TowController Tow;
        public RaceController Race;
        public StolController Stol;
        public CropDustController Dust;
        public Practice.PracticeController Practice;

        public bool IsOpen { get; private set; } = true;

        private GUIStyle _title, _head, _btn, _btnOn, _label, _small, _field;
        private const string PrefPilotName = "net.pilotName", PrefRoomCode = "net.roomCode", PrefMode = "net.mode";
        private string _flapsFor; private bool _flapsHas;
        /// <summary>Does the aircraft picked on this page have flaps? (its config, cached per id)</summary>
        private bool SelectedHasFlaps()
        {
            if (_flapsFor == SessionSettings.AircraftId) return _flapsHas;
            _flapsFor = SessionSettings.AircraftId; _flapsHas = false;
            try
            {
                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(SessionSettings.AircraftId);
                foreach (var sf in cfg.Surfaces) foreach (var st in sf.Strips) if (st.Flap != null) _flapsHas = true;
            }
            catch (System.Exception) { }
            return _flapsHas;
        }

        private Net.NetSession Net => Driver != null ? Driver.GetComponent<Net.NetSession>() : null;
        private Texture2D _bg, _btnBg, _btnOnBg, _ddBg;
        private int _fs;
        private Vector2 _fleetScroll;

        private void Start()
        {
            // Multiplayer identity persists across launches (name/code/mode).
            SessionSettings.PilotName = PlayerPrefs.GetString(PrefPilotName, "");
            if (string.IsNullOrEmpty(SessionSettings.PilotName)) SessionSettings.PilotName = "Pilot" + Random.Range(100, 1000);
            SessionSettings.RoomCode = PlayerPrefs.GetString(PrefRoomCode, "");
            SessionSettings.Multiplayer = (SessionSettings.MultiplayerMode)PlayerPrefs.GetInt(PrefMode, 0);
            Open();
        }

        public void Open()
        {
            IsOpen = true;
            SessionSettings.MenuOpen = true;
            Time.timeScale = 0f;
            Net?.Leave();
        }

        /// <summary>The next powered type after <paramref name="id"/> (gliders can't fight).</summary>
        private static string NextOpponent(string id)
        {
            var fleet = SessionSettings.Fleet;
            int i = System.Array.FindIndex(fleet, f => f.id == id);
            for (int k = 1; k <= fleet.Length; k++)
            {
                string c = fleet[(i + k + fleet.Length) % fleet.Length].id;
                if (!c.StartsWith("glider") && c != "pa18-floats-like") return c;
            }
            return id;
        }

        public CombatController Combat;

        public void Fly()
        {
            IsOpen = false;
            SessionSettings.MenuOpen = false;
            Time.timeScale = 1f;
            SessionSettings.ApplyWeather();
            if (Weather != null) Weather.SyncFromSession();
            Driver.ApplySession();               // aircraft + start position (fires AircraftChanged)
            Race?.End(); Stol?.End(); Dust?.End(); Practice?.End();
            if (SessionSettings.ChallengeId == "event:dogfight") Combat?.BeginDogfight(SessionSettings.DogfightOpponentId, SessionSettings.DogfightSkill);
            else Combat?.EndDogfight();
            string ch = SessionSettings.ChallengeId;
            if (SessionSettings.IsPractice(ch) && Practice != null && SessionSettings.PracticeKindFor(ch) is FlyingGame.Sim.Practice.PracticeKind pk)
            {
                Practice.Begin(pk, SessionSettings.PracticeWindChoice, SessionSettings.LessonHasAxisChoice(ch) ? SessionSettings.LessonUserAxes : -1);
                return;
            }
            if (ch == "event:race" && Race != null) Race.Begin();
            else if (ch == "event:stol" && Stol != null) Stol.Begin(SessionSettings.Airport);
            else if (ch == "event:dust" && Dust != null) Dust.Begin();
            else if (ch != null && !SessionSettings.IsEvent(ch) && Challenges != null) Challenges.StartById(ch);

            // Multiplayer is free play only: a challenge/event flies solo (EffectiveRoom returns null).
            if (ch != null) SessionSettings.Multiplayer = SessionSettings.MultiplayerMode.Solo;
            PlayerPrefs.SetString(PrefPilotName, SessionSettings.PilotName);
            PlayerPrefs.SetString(PrefRoomCode, SessionSettings.RoomCode);
            PlayerPrefs.SetInt(PrefMode, (int)SessionSettings.Multiplayer);
            PlayerPrefs.Save();
            Net?.Join();
        }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            Font f = UiFont.Get();
            _title = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.05f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _small = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 0.8f), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.8f, 0.85f, 0.9f) } };
            _field = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white, background = _btnBg }, focused = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(8, 8, 4, 4) };
        }

        // ---- drop-down lists (owner 2026-10-06: "give the landing page pull down menus for aircraft, challenge, flying lessons
        // etc to make it fit on a single page"). A field shows the current choice ▼; tapping it opens the list OVER the page
        // (the page dims and ignores touches while it is open); a choice closes it, so does a tap outside. Long lists scroll.
        private string _dd;                                   // the open list's key, or null
        private Rect _ddAnchor, _ddRect;                      // the field it hangs from / the list panel (screen coords)
        private readonly System.Collections.Generic.List<(string label, bool on, System.Action pick)> _ddItems = new();
        private float _ddScroll, _ddDragStartY, _ddDragStartScroll; private bool _ddDragPossible, _ddDragging;
        private Vector2 _groupOffset;                         // the page's group origin (fields are drawn inside the scroll group)
        private string _ddForce;
        /// <summary>Self-test: open / close a list as if its field were tapped.</summary>
        public void OpenListForTest(string key) => _ddForce = key;
        public void CloseListForTest() => _dd = null;

        private void Dropdown(string key, Rect r, string current, System.Collections.Generic.IEnumerable<(string label, bool on, System.Action pick)> items)
        {
            bool forced = _ddForce == key; if (forced) _ddForce = null;
            if ((UiLayout.Button(r, current + "  ▼", _dd == key ? _btnOn : _btn) || forced) && _dd == null)
            {
                _dd = key; _ddScroll = 0f;
                _ddAnchor = new Rect(r.x + _groupOffset.x, r.y + _groupOffset.y, r.width, r.height);
                _ddItems.Clear(); _ddItems.AddRange(items);
            }
        }

        /// <summary>The open list, drawn last (on top). Returns true while open.</summary>
        private void DrawDropdown(float bh, float gap)
        {
            if (_dd == null) return;
            Event e = Event.current;
            float rowH = bh * 1.05f, pad = gap * 0.5f;
            float wantH = _ddItems.Count * rowH + 2 * pad;
            float below = Screen.height - _ddAnchor.yMax - gap, above = _ddAnchor.y - gap;
            bool down = below >= Mathf.Min(wantH, Screen.height * 0.45f) || below >= above;
            float h = Mathf.Min(wantH, down ? below : above);
            float w = Mathf.Max(_ddAnchor.width, Screen.width * 0.26f);
            float x = Mathf.Clamp(_ddAnchor.x, gap, Screen.width - w - gap);
            _ddRect = new Rect(x, down ? _ddAnchor.yMax + gap * 0.3f : _ddAnchor.y - gap * 0.3f - h, w, h);
            // A tap outside closes the list (and does nothing else).
            if (e.type == EventType.MouseDown && !_ddRect.Contains(e.mousePosition) && !_ddAnchor.Contains(e.mousePosition)) { _dd = null; e.Use(); return; }
            // Scroll a long list: wheel or drag (a drag swallows its release so the row under the finger doesn't fire).
            float maxScroll = Mathf.Max(0f, wantH - h);
            switch (e.type)
            {
                case EventType.ScrollWheel: if (_ddRect.Contains(e.mousePosition)) { _ddScroll += e.delta.y * rowH * 0.6f; e.Use(); } break;
                case EventType.MouseDown: if (_ddRect.Contains(e.mousePosition)) { _ddDragPossible = true; _ddDragging = false; _ddDragStartY = e.mousePosition.y; _ddDragStartScroll = _ddScroll; } break;
                case EventType.MouseDrag:
                    if (_ddDragPossible) { float dy = e.mousePosition.y - _ddDragStartY; if (!_ddDragging && Mathf.Abs(dy) > rowH * 0.3f) _ddDragging = true; if (_ddDragging) { _ddScroll = _ddDragStartScroll - dy; e.Use(); } }
                    break;
                case EventType.MouseUp: if (_ddDragging) { e.Use(); GUIUtility.hotControl = 0; } _ddDragPossible = false; _ddDragging = false; break;
            }
            _ddScroll = Mathf.Clamp(_ddScroll, 0f, maxScroll);
            GUI.DrawTexture(new Rect(_ddRect.x - pad * 0.5f, _ddRect.y - pad * 0.5f, _ddRect.width + pad, _ddRect.height + pad), _ddBg);
            GUI.BeginGroup(_ddRect);
            float y = pad - _ddScroll;
            for (int i = 0; i < _ddItems.Count; i++)
            {
                var it = _ddItems[i];
                var row = new Rect(pad, y, _ddRect.width - 2 * pad, rowH - gap * 0.25f);
                if (row.yMax > 0 && row.y < _ddRect.height && UiLayout.Button(row, it.label, it.on ? _btnOn : _btn)) { it.pick(); _dd = null; }
                y += rowH;
            }
            GUI.EndGroup();
            if (maxScroll > 1f)
            {
                float frac = h / wantH, th = Mathf.Max(rowH, h * frac), ty = _ddRect.y + (h - th) * (_ddScroll / maxScroll);
                GUI.DrawTexture(new Rect(_ddRect.xMax - gap * 0.35f, ty, gap * 0.25f, th), _btnOnBg);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            _ddBg ??= Solid(new Color(0.02f, 0.03f, 0.05f, 0.97f));
            float s = Mathf.Min(Screen.width, Screen.height);
            if (!IsOpen)
            {
                // Small MENU button top-left.
                if (UiLayout.Button(UiLayout.MenuRect, "MENU", _btn)) Open();   // the shared toolbar row (no-overlap rule)
                return;
            }

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);
            float m = s * 0.03f, w = Screen.width - 2 * m;
            float lh = _fs * 1.55f, gap = _fs * 0.45f;
            float bh = lh * 1.05f, row = bh + gap * 0.4f;
            bool listOpen = _dd != null;
            // An open list has the page's touches: the page underneath dims and ignores them.
            GUI.enabled = !listOpen;
            UiLayout.Label(new Rect(m, m * 0.6f, w, lh * 1.3f), "FLIGHT SETUP", _title);
            float pageTop = m * 0.6f + lh * 1.5f;
            // One page (owner 2026-10-06): landscape three columns, portrait two (no-overlap rule) — long lists are drop-downs.
            bool port = Screen.height > Screen.width;
            int cols = port ? 2 : 3;
            float colW = (w - (cols - 1) * gap) / cols;

            float fw0 = s * 0.26f, fh0 = s * 0.09f;
            var viewport = new Rect(0f, pageTop, Screen.width, Screen.height - pageTop - fh0 - m * 1.5f);
            if (!listOpen) ScrollInput(viewport, lh);   // (kept for very small screens; the page normally fits)
            _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _contentH - viewport.height));
            GUI.BeginGroup(viewport);
            _groupOffset = new Vector2(viewport.x, viewport.y);
            float top = -_scrollY, maxY = 0f;
            float colX(int c) => m + c * (colW + gap);

            // ---- column 1: aircraft · start · airport · instruments
            float x = colX(0), y = top;
            UiLayout.Label(new Rect(x, y, colW, lh), "AIRCRAFT", _head); y += lh;
            string acName = SessionSettings.AircraftId;
            foreach (var f in SessionSettings.Fleet) if (f.id == SessionSettings.AircraftId) acName = f.name;
            Dropdown("aircraft", new Rect(x, y, colW, bh), acName, System.Linq.Enumerable.Select(SessionSettings.Fleet, f => (f.name, f.id == SessionSettings.AircraftId, (System.Action)(() => SessionSettings.AircraftId = f.id))));
            y += row;
            if (SessionSettings.AircraftId.StartsWith("glider"))
            {
                float half = (colW - gap * 0.4f) / 2f;
                UiLayout.Label(new Rect(x, y, colW, lh), "TOW PLANE", _head); y += lh;
                if (UiLayout.Button(new Rect(x, y, half, bh), "Pawnee", SessionSettings.TugId == "pa25-pawnee-like" ? _btnOn : _btn)) SessionSettings.TugId = "pa25-pawnee-like";
                if (UiLayout.Button(new Rect(x + half + gap * 0.4f, y, half, bh), "Super Cub", SessionSettings.TugId == "pa18-cub-like" ? _btnOn : _btn)) SessionSettings.TugId = "pa18-cub-like";
                y += row;
            }
            y += gap * 0.4f;
            UiLayout.Label(new Rect(x, y, colW, lh), "START", _head); y += lh;
            var starts = new[] { (SessionSettings.Start.InTheAir, "In the air"), (SessionSettings.Start.OnTheRunway, "On the runway"), (SessionSettings.Start.OnFinal, "On final, 300 ft"),
                                 (SessionSettings.Start.InAeroBox, "Aerobatic box"), (SessionSettings.Start.InCombatZone, "Combat zone"), (SessionSettings.Start.InThermal, "In a thermal") };
            string startName = "";
            foreach (var st in starts) if (st.Item1 == SessionSettings.StartMode) startName = st.Item2;
            Dropdown("start", new Rect(x, y, colW, bh), startName, System.Linq.Enumerable.Select(starts, st => (st.Item2, st.Item1 == SessionSettings.StartMode, (System.Action)(() => SessionSettings.StartMode = st.Item1))));
            y += row;
            if (SessionSettings.StartMode == SessionSettings.Start.OnTheRunway || SessionSettings.StartMode == SessionSettings.Start.OnFinal)
            {
                // Runway choice (owner 2026-09-10): into the wind on the 09/27, or the crosswind main.
                float half = (colW - gap * 0.3f) / 2f;
                if (UiLayout.Button(new Rect(x, y, half, bh), "Headwind", SessionSettings.Runway == SessionSettings.RunwayPick.Headwind ? _btnOn : _btn)) SessionSettings.Runway = SessionSettings.RunwayPick.Headwind;
                if (UiLayout.Button(new Rect(x + half + gap * 0.3f, y, half, bh), "Crosswind", SessionSettings.Runway == SessionSettings.RunwayPick.Crosswind ? _btnOn : _btn)) SessionSettings.Runway = SessionSettings.RunwayPick.Crosswind;
                y += row;
            }
            y += gap * 0.4f;
            UiLayout.Label(new Rect(x, y, colW, lh), "AIRPORT", _head); y += lh;
            {
                var aps = FlyingGame.Core.WorldTerrain.Airports;
                string Ap(int i) => $"{aps[i].Name}  {aps[i].ElevationM * 3.28084:N0} ft";
                int cur = Mathf.Clamp(SessionSettings.AirportIndex, 0, aps.Length - 1);
                Dropdown("airport", new Rect(x, y, colW, bh), Ap(cur), System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, aps.Length), i => (Ap(i), i == SessionSettings.AirportIndex, (System.Action)(() => SessionSettings.AirportIndex = i))));
                y += row;
            }
            y += gap * 0.4f;
            UiLayout.Label(new Rect(x, y, colW, lh), "INSTRUMENTS", _head); y += lh;
            {
                float third = (colW - 2 * gap * 0.4f) / 3f;
                var modes = new[] { (SessionSettings.InstrumentMode.Analog, "Dials"), (SessionSettings.InstrumentMode.Hud, "HUD"), (SessionSettings.InstrumentMode.None, "None") };
                for (int i = 0; i < modes.Length; i++)
                    if (UiLayout.Button(new Rect(x + i * (third + gap * 0.4f), y, third, bh), modes[i].Item2, SessionSettings.Instruments == modes[i].Item1 ? _btnOn : _btn)) SessionSettings.Instruments = modes[i].Item1;
                y += row;
            }
            UiLayout.Label(new Rect(x, y, colW, lh * 2f), SessionSettings.StartMode == SessionSettings.Start.InTheAir
                ? "Airborne 2,000 ft over the field, trimmed."
                : SessionSettings.StartMode == SessionSettings.Start.InAeroBox ? "Running in to the aerobatic box, 2,300 ft above the ground."
                : SessionSettings.StartMode == SessionSettings.Start.InCombatZone ? "Entering the combat zone, guns hot."
                : SessionSettings.StartMode == SessionSettings.Start.InThermal ? "Circling right in a thermal at min-sink speed, trimmed. Hold the bank and climb."
                : SessionSettings.StartMode == SessionSettings.Start.OnFinal ? "300 ft on final, idle, trimmed at best glide."
                : (SessionSettings.AircraftId.StartsWith("glider") ? "At the threshold. Tap TOW for the aerotow."
                   : SessionSettings.AircraftId == "pa18-floats-like" ? "Afloat on the field's lake, engine idling." : "At the threshold, engine idling."), _small);
            y += lh * 2f;

            // ---- column 2: challenge · flying lesson (+ its options)
            maxY = Mathf.Max(maxY, y);
            x = colX(1); y = top;
            UiLayout.Label(new Rect(x, y, colW, lh), "CHALLENGE", _head); y += lh;
            {
                bool isLesson = SessionSettings.IsPractice(SessionSettings.ChallengeId);
                string chName = isLesson ? "—  (a lesson is picked)" : "Free flight";
                foreach (var c in SessionSettings.Challenges) if (!isLesson && c.id == SessionSettings.ChallengeId) chName = c.name;
                Dropdown("challenge", new Rect(x, y, colW, bh), chName, System.Linq.Enumerable.Select(SessionSettings.Challenges, c => (c.name, !isLesson && c.id == SessionSettings.ChallengeId, (System.Action)(() => SessionSettings.ChallengeId = c.id))));
                y += row;
            }
            if (SessionSettings.ChallengeId == "event:dogfight")
            {
                // Opponent (from the powered fleet) and its skill.
                string oppName = SessionSettings.DogfightOpponentId;
                foreach (var f in SessionSettings.Fleet) if (f.id == SessionSettings.DogfightOpponentId) oppName = f.name;
                var powered = System.Linq.Enumerable.Where(SessionSettings.Fleet, f => !f.id.StartsWith("glider") && f.id != "pa18-floats-like");
                Dropdown("opponent", new Rect(x, y, colW, bh), "vs  " + oppName, System.Linq.Enumerable.Select(powered, f => (f.name, f.id == SessionSettings.DogfightOpponentId, (System.Action)(() => SessionSettings.DogfightOpponentId = f.id))));
                y += row;
                float third = (colW - 2 * gap * 0.3f) / 3f;
                for (int k = 0; k < 3; k++)
                    if (UiLayout.Button(new Rect(x + k * (third + gap * 0.3f), y, third, bh), SessionSettings.SkillNames[k], SessionSettings.DogfightSkill == k ? _btnOn : _btn)) SessionSettings.DogfightSkill = k;
                y += row;
            }
            y += gap * 0.4f;
            UiLayout.Label(new Rect(x, y, colW, lh), "FLYING LESSON", _head); y += lh;
            {
                bool gliderPicked = SessionSettings.AircraftId.StartsWith("glider");
                if (gliderPicked && SessionSettings.LessonNeedsEngine(SessionSettings.ChallengeId)) SessionSettings.ChallengeId = null;
                string lName = "—  none";
                foreach (var l in SessionSettings.Lessons) if (l.id == SessionSettings.ChallengeId) lName = l.name;
                var offered = System.Linq.Enumerable.Where(SessionSettings.Lessons, l => !(gliderPicked && SessionSettings.LessonNeedsEngine(l.id)));
                Dropdown("lesson", new Rect(x, y, colW, bh), lName, System.Linq.Enumerable.Select(offered, l => (l.name, l.id == SessionSettings.ChallengeId, (System.Action)(() => PickLesson(l.id)))));
                y += row;
            }
            if (SessionSettings.LessonHasAxisChoice(SessionSettings.ChallengeId))
            {
                // Which controls YOU fly (the game takes the rest) — owner 2026-09-15.
                UiLayout.Label(new Rect(x, y, colW, lh), "YOU FLY", _head); y += lh;
                if (SessionSettings.LessonUserAxes < 0) SessionSettings.LessonUserAxes = SessionSettings.LessonDefaultAxes(SessionSettings.ChallengeId);
                string[] names = { "AIL", "ELE", "RUD", SessionSettings.AircraftId.StartsWith("glider") ? "SPOIL" : "THR" };
                float aw = (colW - 3 * gap * 0.3f) / 4f;
                for (int i = 0; i < 4; i++)
                {
                    bool on = (SessionSettings.LessonUserAxes & (1 << i)) != 0;
                    if (UiLayout.Button(new Rect(x + i * (aw + gap * 0.3f), y, aw, bh), names[i], on ? _btnOn : _btn)) SessionSettings.LessonUserAxes ^= (1 << i);
                }
                y += row;
            }
            if (SessionSettings.LessonHasFlapChoice(SessionSettings.ChallengeId) && SelectedHasFlaps())
            {
                // Flaps for the landing lessons (owner 2026-10-03): the lesson starts on the power-off glide for this setting.
                UiLayout.Label(new Rect(x, y, colW, lh), "FLAPS", _head); y += lh;
                (float f, string n)[] fl = { (0f, "UP"), (0.5f, "HALF"), (1f, "FULL") };
                float flw = (colW - 2 * gap * 0.3f) / 3f;
                for (int i = 0; i < 3; i++)
                    if (UiLayout.Button(new Rect(x + i * (flw + gap * 0.3f), y, flw, bh), fl[i].n, Mathf.Approximately(SessionSettings.LessonFlaps, fl[i].f) ? _btnOn : _btn)) SessionSettings.LessonFlaps = fl[i].f;
                y += row;
            }
            if (SessionSettings.IsPractice(SessionSettings.ChallengeId) && SessionSettings.PracticeHasWindChoice(SessionSettings.ChallengeId))
            {
                // Practice wind (owner 2026-09-15): steady / gusty / shifting crosswind, or calm / head / tail winds with gusts.
                UiLayout.Label(new Rect(x, y, colW, lh), "PRACTICE WIND", _head); y += lh;
                var choices = SessionSettings.PracticeIsCrosswind(SessionSettings.ChallengeId) ? SessionSettings.CrosswindChoices : SessionSettings.AlongWindChoices;
                string wName = null;
                foreach (var c in choices) if (c.w == SessionSettings.PracticeWindChoice) wName = c.name;
                if (wName == null) { SessionSettings.PracticeWindChoice = choices[0].w; wName = choices[0].name; }   // a wind this lesson doesn't offer → its first
                Dropdown("pwind", new Rect(x, y, colW, bh), wName, System.Linq.Enumerable.Select(choices, c => (c.name, c.w == SessionSettings.PracticeWindChoice, (System.Action)(() => SessionSettings.PracticeWindChoice = c.w))));
                y += row;
            }

            // ---- column 3 (portrait: under column 1): conditions · multiplayer
            maxY = Mathf.Max(maxY, y);
            if (port) { x = colX(0); y = maxY + gap; } else { x = colX(2); y = top; }
            UiLayout.Label(new Rect(x, y, colW, lh), "CONDITIONS", _head); y += lh;
            y = Stepper(x, y, colW, "Wind from", $"{SessionSettings.WindFromDeg:000}°", ref SessionSettings.WindFromDeg, 0f, 359f, 15f);
            y = Stepper(x, y, colW, "Wind", $"{SessionSettings.WindSpeedMs * 1.944f:F0} kt", ref SessionSettings.WindSpeedMs, 0f, 20f, 1f);
            float turb = SessionSettings.TurbulenceLevel;
            y = Stepper(x, y, colW, "Turbulence", new[] { "Calm", "Light", "Moderate", "Severe" }[SessionSettings.TurbulenceLevel], ref turb, 0f, 3f, 1f);
            SessionSettings.TurbulenceLevel = Mathf.RoundToInt(turb);
            float surfF = (15f + SessionSettings.IsaDeviationC) * 1.8f + 32f;
            y = Stepper(x, y, colW, "Temperature", $"{surfF:F0}°F", ref SessionSettings.IsaDeviationC, -30f, 30f, 1f);
            y = Stepper(x, y, colW, "Thermals", SessionSettings.ThermalScale <= 0.01f ? "Off" : $"{SessionSettings.ThermalScale:F1}×", ref SessionSettings.ThermalScale, 0f, 2f, 0.25f);

            if (port) { maxY = Mathf.Max(maxY, y); x = colX(1); }
            else y += gap * 0.4f;
            UiLayout.Label(new Rect(x, y, colW, lh), "MULTIPLAYER", _head); y += lh;
            bool freePlay = SessionSettings.ChallengeId == null;
            if (!freePlay)
            {
                SessionSettings.Multiplayer = SessionSettings.MultiplayerMode.Solo;
                UiLayout.Label(new Rect(x, y, colW, lh), "Free flight only — challenges fly solo.", _small);
                y += lh;
            }
            else
            {
                float third = (colW - 2 * gap * 0.4f) / 3f;
                var modes = new[] { (SessionSettings.MultiplayerMode.Solo, "Solo"), (SessionSettings.MultiplayerMode.FreeForAll, "Free-for-all"), (SessionSettings.MultiplayerMode.PrivateRoom, "Private room") };
                for (int i = 0; i < modes.Length; i++)
                    if (UiLayout.Button(new Rect(x + i * (third + gap * 0.4f), y, third, bh), modes[i].Item2, SessionSettings.Multiplayer == modes[i].Item1 ? _btnOn : _btn)) SessionSettings.Multiplayer = modes[i].Item1;
                y += row;
                if (SessionSettings.Multiplayer != SessionSettings.MultiplayerMode.Solo)
                {
                    float lw = colW * 0.3f;
                    UiLayout.Label(new Rect(x, y, lw, bh), "Pilot", _label);
                    string name = GUI.TextField(new Rect(x + lw, y, colW - lw, bh), SessionSettings.PilotName ?? "", 16, _field);
                    SessionSettings.PilotName = Ascii(name, false);
                    y += row;
                }
                if (SessionSettings.Multiplayer == SessionSettings.MultiplayerMode.PrivateRoom)
                {
                    float lw = colW * 0.3f, cw = colW * 0.42f;
                    UiLayout.Label(new Rect(x, y, lw, bh), "Code", _label);
                    string code = GUI.TextField(new Rect(x + lw, y, cw, bh), SessionSettings.RoomCode ?? "", 6, _field);
                    SessionSettings.RoomCode = Ascii(code, true);
                    if (UiLayout.Button(new Rect(x + lw + cw + gap * 0.4f, y, colW - lw - cw - gap * 0.4f, bh), "Create", _btn)) SessionSettings.RoomCode = SessionSettings.NewRoomCode();
                    y += row;
                    UiLayout.Label(new Rect(x, y, colW, lh), SessionSettings.IsValidRoomCode(SessionSettings.RoomCode) ? "Share the code — friends type it to join." : "6 letters/digits: type a friend's code or Create.", _small);
                    y += lh;
                }
            }

            maxY = Mathf.Max(maxY, y);
            GUI.EndGroup();
            _contentH = maxY - top + gap;
            if (_contentH > viewport.height + 1f)
            {
                // Scroll bar: only on a screen too small for the page.
                float frac = viewport.height / _contentH, thumbH = Mathf.Max(lh, viewport.height * frac);
                float thumbY = viewport.y + (viewport.height - thumbH) * (_scrollY / Mathf.Max(1f, _contentH - viewport.height));
                GUI.DrawTexture(new Rect(Screen.width - m * 0.5f, viewport.y, m * 0.18f, viewport.height), _btnBg);
                GUI.DrawTexture(new Rect(Screen.width - m * 0.5f, thumbY, m * 0.18f, thumbH), _btnOnBg);
            }

            // ---- FLY
            float fw = s * 0.26f, fh = s * 0.09f;
            if (UiLayout.Button(new Rect(Screen.width - m - fw, Screen.height - m - fh, fw, fh), "FLY", _btnOn)) Fly();

            GUI.enabled = true;
            DrawDropdown(bh, gap);
        }

        /// <summary>Picking a lesson resets its wind to one it offers, and its axes to the lesson's own.</summary>
        private static void PickLesson(string id)
        {
            SessionSettings.ChallengeId = id;
            bool xw = SessionSettings.PracticeIsCrosswind(id);
            bool ok = false;
            foreach (var c in xw ? SessionSettings.CrosswindChoices : SessionSettings.AlongWindChoices) if (c.w == SessionSettings.PracticeWindChoice) ok = true;
            if (!ok || (SessionSettings.PracticeIsAirwork(id) && !SessionSettings.PracticeHasWindChoice(id))) SessionSettings.PracticeWindChoice = xw ? FlyingGame.Sim.Practice.PracticeWind.Steady : FlyingGame.Sim.Practice.PracticeWind.Calm;
            SessionSettings.LessonUserAxes = SessionSettings.LessonDefaultAxes(id);
        }

        /// <summary>A one-line −/+ stepper: label · − value + (touch-friendly; IMGUI sliders are fiddly on a phone).</summary>
        private float Stepper(float x, float y, float w, string label, string value, ref float v, float min, float max, float step)
        {
            float bh = _fs * 1.55f * 1.05f, bw = bh * 1.15f, lw = w * 0.38f;
            UiLayout.Label(new Rect(x, y, lw, bh), label, _label);
            float rx = x + lw, rw = w - lw;
            if (UiLayout.Button(new Rect(rx, y, bw, bh), "−", _btn)) v = Mathf.Clamp(v - step, min, max);
            UiLayout.Label(new Rect(rx + bw, y, rw - 2 * bw, bh), value, new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
            if (UiLayout.Button(new Rect(rx + rw - bw, y, bw, bh), "+", _btn)) v = Mathf.Clamp(v + step, min, max);
            if (label == "Wind from" && v >= 359f) v = 0f;
            return y + bh + _fs * 0.35f;
        }

        private float _scrollY, _contentH, _dragStartY, _dragStartScroll;
        /// <summary>Self-test / tools: jump the setup page to its top or bottom.</summary>
        public void ScrollTo(bool end) => _scrollY = end ? 1e6f : 0f;
        private bool _dragPossible, _dragging;

        /// <summary>Drag / wheel scrolling for the setup page. A drag past a few pixels scrolls and swallows the release, so
        /// the button under the finger doesn't fire; a tap still presses buttons normally.</summary>
        private void ScrollInput(Rect viewport, float lh)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (viewport.Contains(e.mousePosition)) { _scrollY += e.delta.y * lh * 0.6f; e.Use(); }
                    break;
                case EventType.MouseDown:
                    if (viewport.Contains(e.mousePosition)) { _dragPossible = true; _dragging = false; _dragStartY = e.mousePosition.y; _dragStartScroll = _scrollY; }
                    break;
                case EventType.MouseDrag:
                    if (_dragPossible)
                    {
                        float dy = e.mousePosition.y - _dragStartY;
                        if (!_dragging && Mathf.Abs(dy) > lh * 0.35f) _dragging = true;
                        if (_dragging) { _scrollY = _dragStartScroll - dy; e.Use(); }
                    }
                    break;
                case EventType.MouseUp:
                    if (_dragging) { e.Use(); GUIUtility.hotControl = 0; }
                    _dragPossible = false; _dragging = false;
                    break;
            }
        }

        /// <summary>Printable ASCII only (the wire format is plain JSON); room codes are upper-case [A-Z0-9].</summary>
        private static string Ascii(string s, bool codeOnly)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
            {
                char c = codeOnly ? char.ToUpperInvariant(ch) : ch;
                bool ok = codeOnly ? (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') : c >= ' ' && c <= '~';
                if (ok) sb.Append(c);
            }
            return sb.ToString();
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            t.SetPixels(new[] { c, c, c, c }); t.Apply();
            return t;
        }
    }
}
