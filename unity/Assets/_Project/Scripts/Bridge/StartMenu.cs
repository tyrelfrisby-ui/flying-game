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
        private Texture2D _bg, _btnBg, _btnOnBg;
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

        private void OnGUI()
        {
            EnsureStyles();
            float s = Mathf.Min(Screen.width, Screen.height);
            if (!IsOpen)
            {
                // Small MENU button top-left.
                if (UiLayout.Button(UiLayout.MenuRect, "MENU", _btn)) Open();   // the shared toolbar row (no-overlap rule)
                return;
            }

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);
            float m = s * 0.03f, w = Screen.width - 2 * m;
            float lh = _fs * 1.7f, gap = _fs * 0.5f;
            UiLayout.Label(new Rect(m, m * 0.6f, w, lh * 1.3f), "FLIGHT SETUP", _title);
            float pageTop = m * 0.6f + lh * 1.5f;
            // Portrait (no-overlap rule): two wide columns, two rows of sections; landscape: four columns.
            bool port = Screen.height > Screen.width;
            float colW = port ? (w - gap) / 2f : (w - 3 * gap) / 4f;

            // SCROLLING (owner 2026-10-03: "so big now with all of the options i cannot see them all"): the four columns live
            // in a scroll area between the title and the FLY button — drag (touch / mouse) or scroll wheel / trackpad.
            float fw0 = s * 0.26f, fh0 = s * 0.09f;
            var viewport = new Rect(0f, pageTop, Screen.width, Screen.height - pageTop - fh0 - m * 1.5f);
            ScrollInput(viewport, lh);
            _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _contentH - viewport.height));
            GUI.BeginGroup(viewport);
            float top = -_scrollY, maxY = 0f;

            // ---- column 1: aircraft
            float x = m, y = top;
            UiLayout.Label(new Rect(x, y, colW, lh), "AIRCRAFT", _head); y += lh;
            float bh = lh * 1.05f;
            foreach ((string id, string name) in SessionSettings.Fleet)
            {
                bool on = SessionSettings.AircraftId == id;
                if (UiLayout.Button(new Rect(x, y, colW, bh), name, on ? _btnOn : _btn)) SessionSettings.AircraftId = id;
                y += bh + gap * 0.4f;
            }

            // ---- column 2: start
            maxY = Mathf.Max(maxY, y);
            x = m + colW + gap; y = top;
            UiLayout.Label(new Rect(x, y, colW, lh), "START", _head); y += lh;
            if (UiLayout.Button(new Rect(x, y, colW, bh), "In the air", SessionSettings.StartMode == SessionSettings.Start.InTheAir ? _btnOn : _btn)) SessionSettings.StartMode = SessionSettings.Start.InTheAir;
            y += bh + gap * 0.4f;
            if (UiLayout.Button(new Rect(x, y, colW, bh), "On the runway", SessionSettings.StartMode == SessionSettings.Start.OnTheRunway ? _btnOn : _btn)) SessionSettings.StartMode = SessionSettings.Start.OnTheRunway;
            y += bh + gap * 0.4f;
            if (UiLayout.Button(new Rect(x, y, colW, bh), "On final, 300 ft", SessionSettings.StartMode == SessionSettings.Start.OnFinal ? _btnOn : _btn)) SessionSettings.StartMode = SessionSettings.Start.OnFinal;
            y += bh + gap * 0.4f;
            {
                // Owner 2026-10-01: start in the aerobatic box, the combat zone, or already circling in a thermal.
                float third = (colW - 2 * gap * 0.3f) / 3f;
                var extra = new[] { (SessionSettings.Start.InAeroBox, "Aero box"), (SessionSettings.Start.InCombatZone, "Combat"), (SessionSettings.Start.InThermal, "Thermal") };
                for (int i = 0; i < extra.Length; i++)
                    if (UiLayout.Button(new Rect(x + i * (third + gap * 0.3f), y, third, bh), extra[i].Item2, SessionSettings.StartMode == extra[i].Item1 ? _btnOn : _btn)) SessionSettings.StartMode = extra[i].Item1;
                y += bh + gap * 0.4f;
            }
            if (SessionSettings.StartMode == SessionSettings.Start.OnTheRunway || SessionSettings.StartMode == SessionSettings.Start.OnFinal)
            {
                // Runway choice (owner 2026-09-10): into the wind on the 09/27, or the crosswind main.
                float half = (colW - gap * 0.3f) / 2f;
                if (UiLayout.Button(new Rect(x, y, half, bh), "Headwind", SessionSettings.Runway == SessionSettings.RunwayPick.Headwind ? _btnOn : _btn)) SessionSettings.Runway = SessionSettings.RunwayPick.Headwind;
                if (UiLayout.Button(new Rect(x + half + gap * 0.3f, y, half, bh), "Crosswind", SessionSettings.Runway == SessionSettings.RunwayPick.Crosswind ? _btnOn : _btn)) SessionSettings.Runway = SessionSettings.RunwayPick.Crosswind;
                y += bh + gap * 0.4f;
            }
            y += gap * 0.6f;
            UiLayout.Label(new Rect(x, y, colW, lh), "AIRPORT", _head); y += lh;
            for (int i = 0; i < FlyingGame.Core.WorldTerrain.Airports.Length; i++)
            {
                var a = FlyingGame.Core.WorldTerrain.Airports[i];
                string txt = $"{a.Name}  {a.ElevationM * 3.28084:N0} ft";
                if (UiLayout.Button(new Rect(x, y, colW, bh), txt, SessionSettings.AirportIndex == i ? _btnOn : _btn)) SessionSettings.AirportIndex = i;
                y += bh + gap * 0.4f;
            }
            if (SessionSettings.AircraftId.StartsWith("glider"))
            {
                y += gap;
                UiLayout.Label(new Rect(x, y, colW, lh), "TOW PLANE", _head); y += lh;
                float half = (colW - gap * 0.4f) / 2f;
                if (UiLayout.Button(new Rect(x, y, half, bh), "Pawnee", SessionSettings.TugId == "pa25-pawnee-like" ? _btnOn : _btn)) SessionSettings.TugId = "pa25-pawnee-like";
                if (UiLayout.Button(new Rect(x + half + gap * 0.4f, y, half, bh), "Super Cub", SessionSettings.TugId == "pa18-cub-like" ? _btnOn : _btn)) SessionSettings.TugId = "pa18-cub-like";
                y += bh + gap * 0.4f;
            }
            y += gap;
            UiLayout.Label(new Rect(x, y, colW, lh), "INSTRUMENTS", _head); y += lh;
            {
                float third = (colW - 2 * gap * 0.4f) / 3f;
                var modes = new[] { (SessionSettings.InstrumentMode.Analog, "Dials"), (SessionSettings.InstrumentMode.Hud, "HUD"), (SessionSettings.InstrumentMode.None, "None") };
                for (int i = 0; i < modes.Length; i++)
                {
                    bool on = SessionSettings.Instruments == modes[i].Item1;
                    if (UiLayout.Button(new Rect(x + i * (third + gap * 0.4f), y, third, bh), modes[i].Item2, on ? _btnOn : _btn)) SessionSettings.Instruments = modes[i].Item1;
                }
                y += bh + gap * 0.4f;
            }
            y += gap;
            UiLayout.Label(new Rect(x, y, colW, lh * 2f), SessionSettings.StartMode == SessionSettings.Start.InTheAir
                ? "Airborne 2,000 ft over the field, trimmed."
                : SessionSettings.StartMode == SessionSettings.Start.InAeroBox ? "Running in to the aerobatic box, 2,300 ft above the ground."
                : SessionSettings.StartMode == SessionSettings.Start.InCombatZone ? "Entering the combat zone, guns hot."
                : SessionSettings.StartMode == SessionSettings.Start.InThermal ? "Circling right in a thermal at min-sink speed, trimmed. Hold the bank and climb."
                : SessionSettings.StartMode == SessionSettings.Start.OnFinal ? "300 ft on final, idle, trimmed at best glide."
                : (SessionSettings.AircraftId.StartsWith("glider") ? "At the threshold. Tap TOW for the aerotow."
                   : SessionSettings.AircraftId == "pa18-floats-like" ? "Afloat on the field's lake, engine idling." : "At the threshold, engine idling."), _small);

            // ---- column 3: challenge
            maxY = Mathf.Max(maxY, y);
            float row2Top = maxY + gap * 1.5f;
            if (port) { x = m; y = row2Top; } else { x = m + 2 * (colW + gap); y = top; }
            UiLayout.Label(new Rect(x, y, colW, lh), "CHALLENGE", _head); y += lh;
            foreach ((string id, string name) in SessionSettings.Challenges)
            {
                bool on = SessionSettings.ChallengeId == id;
                if (UiLayout.Button(new Rect(x, y, colW, bh), name, on ? _btnOn : _btn)) SessionSettings.ChallengeId = id;
                y += bh + gap * 0.4f;
                if (on && id == "event:dogfight")
                {
                    // Opponent (tap to cycle through the powered fleet) and its skill.
                    string oppName = SessionSettings.DogfightOpponentId;
                    foreach (var f in SessionSettings.Fleet) if (f.id == SessionSettings.DogfightOpponentId) oppName = f.name;
                    if (UiLayout.Button(new Rect(x, y, colW, bh), $"vs  {oppName}  >", _btn)) SessionSettings.DogfightOpponentId = NextOpponent(SessionSettings.DogfightOpponentId);
                    y += bh + gap * 0.4f;
                    float third = (colW - 2 * gap * 0.3f) / 3f;
                    for (int k = 0; k < 3; k++)
                        if (UiLayout.Button(new Rect(x + k * (third + gap * 0.3f), y, third, bh), SessionSettings.SkillNames[k], SessionSettings.DogfightSkill == k ? _btnOn : _btn)) SessionSettings.DogfightSkill = k;
                    y += bh + gap * 0.4f;
                }
            }
            // ---- FLYING LESSONS (owner 2026-09-15): the game flies every axis but the one being learned.
            y += gap * 0.5f;
            UiLayout.Label(new Rect(x, y, colW, lh), "FLYING LESSONS", _head); y += lh;
            float lbh = bh * 0.9f;
            foreach ((string id, string name) in SessionSettings.Lessons)
            {
                bool on = SessionSettings.ChallengeId == id;
                if (UiLayout.Button(new Rect(x, y, colW, lbh), name, on ? _btnOn : _btn))
                {
                    SessionSettings.ChallengeId = id;
                    bool xw = SessionSettings.PracticeIsCrosswind(id);
                    bool ok = false;
                    foreach (var c in xw ? SessionSettings.CrosswindChoices : SessionSettings.AlongWindChoices) if (c.w == SessionSettings.PracticeWindChoice) ok = true;
                    if (!ok || (SessionSettings.PracticeIsAirwork(id) && !SessionSettings.PracticeHasWindChoice(id))) SessionSettings.PracticeWindChoice = xw ? FlyingGame.Sim.Practice.PracticeWind.Steady : FlyingGame.Sim.Practice.PracticeWind.Calm;
                    SessionSettings.LessonUserAxes = SessionSettings.LessonDefaultAxes(id);
                }
                y += lbh + gap * 0.3f;
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
                y += bh + gap * 0.4f;
            }
            if (SessionSettings.LessonHasFlapChoice(SessionSettings.ChallengeId) && SelectedHasFlaps())
            {
                // Flaps for the landing lessons (owner 2026-10-03): the lesson starts on the power-off glide for this setting.
                UiLayout.Label(new Rect(x, y, colW, lh), "FLAPS", _head); y += lh;
                (float f, string n)[] fl = { (0f, "UP"), (0.5f, "HALF"), (1f, "FULL") };
                float flw = (colW - 2 * gap * 0.3f) / 3f;
                for (int i = 0; i < 3; i++)
                    if (UiLayout.Button(new Rect(x + i * (flw + gap * 0.3f), y, flw, bh), fl[i].n, Mathf.Approximately(SessionSettings.LessonFlaps, fl[i].f) ? _btnOn : _btn)) SessionSettings.LessonFlaps = fl[i].f;
                y += bh + gap * 0.4f;
            }
            if (SessionSettings.IsPractice(SessionSettings.ChallengeId) && SessionSettings.PracticeHasWindChoice(SessionSettings.ChallengeId))
            {
                // Practice wind (owner 2026-09-15): steady / gusty / shifting crosswind, or calm / head / tail winds with gusts.
                UiLayout.Label(new Rect(x, y, colW, lh), "PRACTICE WIND", _head); y += lh;
                var choices = SessionSettings.PracticeIsCrosswind(SessionSettings.ChallengeId) ? SessionSettings.CrosswindChoices : SessionSettings.AlongWindChoices;
                foreach (var (wnd, wname) in choices)
                {
                    if (UiLayout.Button(new Rect(x, y, colW, bh), wname, SessionSettings.PracticeWindChoice == wnd ? _btnOn : _btn)) SessionSettings.PracticeWindChoice = wnd;
                    y += bh + gap * 0.4f;
                }
            }

            // ---- column 4: conditions
            maxY = Mathf.Max(maxY, y);
            if (port) { x = m + colW + gap; y = row2Top; } else { x = m + 3 * (colW + gap); y = top; }
            UiLayout.Label(new Rect(x, y, colW, lh), "CONDITIONS", _head); y += lh;
            y = Slider(x, y, colW, "Wind from", $"{SessionSettings.WindFromDeg:000}°", ref SessionSettings.WindFromDeg, 0f, 359f, 15f);
            y = Slider(x, y, colW, "Wind speed", $"{SessionSettings.WindSpeedMs * 1.944f:F0} kt", ref SessionSettings.WindSpeedMs, 0f, 20f, 1f);
            float turb = SessionSettings.TurbulenceLevel;
            y = Slider(x, y, colW, "Turbulence", new[] { "Calm", "Light", "Moderate", "Severe" }[SessionSettings.TurbulenceLevel], ref turb, 0f, 3f, 1f);
            SessionSettings.TurbulenceLevel = Mathf.RoundToInt(turb);
            float surfF = (15f + SessionSettings.IsaDeviationC) * 1.8f + 32f;
            y = Slider(x, y, colW, "Temperature", $"{surfF:F0}°F at sea level", ref SessionSettings.IsaDeviationC, -30f, 30f, 1f);
            y = Slider(x, y, colW, "Thermals", SessionSettings.ThermalScale <= 0.01f ? "Off" : $"{SessionSettings.ThermalScale:F1}×", ref SessionSettings.ThermalScale, 0f, 2f, 0.25f);

            // ---- multiplayer (free play only)
            y += gap * 0.5f;
            UiLayout.Label(new Rect(x, y, colW, lh), "MULTIPLAYER", _head); y += lh;
            bool freePlay = SessionSettings.ChallengeId == null;
            if (!freePlay)
            {
                SessionSettings.Multiplayer = SessionSettings.MultiplayerMode.Solo;
                UiLayout.Label(new Rect(x, y, colW, lh), "Free flight only — challenges fly solo.", _small);
            }
            else
            {
                float third = (colW - 2 * gap * 0.4f) / 3f;
                var modes = new[] { (SessionSettings.MultiplayerMode.Solo, "Solo"), (SessionSettings.MultiplayerMode.FreeForAll, "Free-for-all"), (SessionSettings.MultiplayerMode.PrivateRoom, "Private room") };
                for (int i = 0; i < modes.Length; i++)
                {
                    bool on = SessionSettings.Multiplayer == modes[i].Item1;
                    if (UiLayout.Button(new Rect(x + i * (third + gap * 0.4f), y, third, bh), modes[i].Item2, on ? _btnOn : _btn)) SessionSettings.Multiplayer = modes[i].Item1;
                }
                y += bh + gap * 0.4f;
                if (SessionSettings.Multiplayer != SessionSettings.MultiplayerMode.Solo)
                {
                    float lw = colW * 0.3f;
                    UiLayout.Label(new Rect(x, y, lw, bh), "Pilot", _label);
                    string name = GUI.TextField(new Rect(x + lw, y, colW - lw, bh), SessionSettings.PilotName ?? "", 16, _field);
                    SessionSettings.PilotName = Ascii(name, false);
                    y += bh + gap * 0.4f;
                }
                if (SessionSettings.Multiplayer == SessionSettings.MultiplayerMode.PrivateRoom)
                {
                    float lw = colW * 0.3f, cw = colW * 0.42f;
                    UiLayout.Label(new Rect(x, y, lw, bh), "Code", _label);
                    string code = GUI.TextField(new Rect(x + lw, y, cw, bh), SessionSettings.RoomCode ?? "", 6, _field);
                    SessionSettings.RoomCode = Ascii(code, true);
                    if (UiLayout.Button(new Rect(x + lw + cw + gap * 0.4f, y, colW - lw - cw - gap * 0.4f, bh), "Create", _btn)) SessionSettings.RoomCode = SessionSettings.NewRoomCode();
                    y += bh + gap * 0.4f;
                    UiLayout.Label(new Rect(x, y, colW, lh), SessionSettings.IsValidRoomCode(SessionSettings.RoomCode) ? "Share the code — friends type it to join." : "6 letters/digits: type a friend's code or Create.", _small);
                }
            }

            maxY = Mathf.Max(maxY, y);
            GUI.EndGroup();
            _contentH = maxY - top + gap;
            if (_contentH > viewport.height + 1f)
            {
                // Scroll bar: a thin thumb on the right edge showing where the view is.
                float frac = viewport.height / _contentH, thumbH = Mathf.Max(lh, viewport.height * frac);
                float thumbY = viewport.y + (viewport.height - thumbH) * (_scrollY / Mathf.Max(1f, _contentH - viewport.height));
                GUI.DrawTexture(new Rect(Screen.width - m * 0.5f, viewport.y, m * 0.18f, viewport.height), _btnBg);
                GUI.DrawTexture(new Rect(Screen.width - m * 0.5f, thumbY, m * 0.18f, thumbH), _btnOnBg);
                if (_scrollY < _contentH - viewport.height - 1f)
                    UiLayout.Label(new Rect(m, viewport.yMax - lh * 0.1f, w * 0.5f, lh * 0.9f), "▼ more below — drag or scroll", _small);
            }

            // ---- FLY
            float fw = s * 0.26f, fh = s * 0.09f;
            if (UiLayout.Button(new Rect(Screen.width - m - fw, Screen.height - m - fh, fw, fh), "FLY", _btnOn)) Fly();
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

        /// <summary>A labelled -/+ stepper (touch-friendly; IMGUI sliders are fiddly on a phone).</summary>
        private float Slider(float x, float y, float w, string label, string value, ref float v, float min, float max, float step)
        {
            float lh = _fs * 1.7f, bw = lh * 1.2f;
            UiLayout.Label(new Rect(x, y, w, lh), label, _label);
            y += lh;
            if (UiLayout.Button(new Rect(x, y, bw, lh), "−", _btn)) v = Mathf.Clamp(v - step, min, max);
            UiLayout.Label(new Rect(x + bw + _fs * 0.4f, y, w - 2 * bw - _fs * 0.8f, lh), value, new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
            if (UiLayout.Button(new Rect(x + w - bw, y, bw, lh), "+", _btn)) v = Mathf.Clamp(v + step, min, max);
            if (label == "Wind from" && v >= 359f) v = 0f;
            return y + lh + _fs * 0.5f;
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
