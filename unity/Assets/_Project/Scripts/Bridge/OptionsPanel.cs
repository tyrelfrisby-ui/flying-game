using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// In-flight OPTIONS button (top right) opening the settings panel.
    ///
    /// Owner 2026-10-28: "overhaul the landing page and the menu so it takes up less space, put more things into
    /// pull downs or buttons that open pages, maybe use a chapter/page setup." The old panel was one long list
    /// that had grown taller than an iPhone, which put its own Close button off the bottom of the screen. So:
    /// a row of chapter tabs and ONE page at a time. Three rules keep it closable for good.
    ///
    ///   1. OPTIONS and Close are drawn BEFORE the scroll view, so they are on screen whatever the page does.
    ///   2. The panel is clamped to the screen; a page taller than that scrolls inside it.
    ///   3. A page's height is MEASURED from where its last row actually ended on the previous pass, never
    ///      counted by hand — adding a row to a page cannot make the panel the wrong size again.
    ///
    /// The world stands still while this is open (<see cref="SessionSettings.PauseWhenMenuOpen"/>, on the Game
    /// page) so that reaching in to switch the bubbles off is not a way to crash: owner 2026-10-28, "i keep
    /// crashing because i'm turning off bubbles etc". Multiplayer is the exception — the other aeroplanes are
    /// real people, so there time keeps running.
    /// </summary>
    public sealed class OptionsPanel : MonoBehaviour
    {
        public FlightSimDriver Driver;
        private bool _open;
        public bool IsOpen => _open;
        public void Close() { _open = false; Unpause(); }
        private GUIStyle _btn, _btnOn, _tab, _tabOn, _head, _label, _note;
        private Texture2D _bg, _btnBg, _btnOnBg, _tabBg, _tabOnBg;
        private int _fs;

        private enum Page { Display, Aids, Controls, Audio, Clips, Badges, Game, Help }
        private static readonly string[] PageNames = { "Display", "Aids", "Controls", "Audio", "Clips", "Badges", "Game", "Help" };
        private Page _page = Page.Display;
        private Vector2 _scroll;
        /// <summary>Where each page's last row ended, measured on the previous pass (0 = not drawn yet).</summary>
        private readonly float[] _measured = new float[8];

        /// <summary>Self-test: open the panel and show a given chapter by name ("display", "controls", …).</summary>
        public void ShowPageForTest(string name)
        {
            Open();
            for (int i = 0; i < PageNames.Length; i++)
                if (string.Equals(PageNames[i], name, System.StringComparison.OrdinalIgnoreCase)) { _page = (Page)i; _scroll = Vector2.zero; }
        }
        /// <summary>Self-test: the chapter names, in tab order.</summary>
        public static string[] PageNamesForTest => PageNames;

        private static bool FlightControlsDesk => TouchFlightControls.DeskMode;
        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void Open()
        {
            _open = true;
            Widget.GameAeroPanel.Close();
            SessionSettings.PanelPause = true;
            SessionSettings.ApplyTimeScale();
        }
        private void Unpause()
        {
            SessionSettings.PanelPause = false;
            SessionSettings.ApplyTimeScale();
        }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.92f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            _tabBg ??= Solid(new Color(0.10f, 0.14f, 0.20f, 0.95f));
            _tabOnBg ??= Solid(new Color(0.16f, 0.34f, 0.46f, 0.98f));
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _tab = new GUIStyle(_btn) { normal = { textColor = new Color(0.72f, 0.80f, 0.88f), background = _tabBg }, active = { textColor = Color.white, background = _tabBg } };
            _tabOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.75f, 0.98f, 1f), background = _tabOnBg }, active = { textColor = Color.white, background = _tabOnBg } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.05f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.88f, 0.92f) } };
            _note = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(fs * 0.85f), normal = { textColor = new Color(0.62f, 0.68f, 0.76f) } };
        }

        private void ApplyFeelLive()
        {
            var cfg = Driver?.Sim?.Aircraft?.Config;
            if (cfg == null) return;
            // Re-load the type's own values first so switching a setting back to "aircraft" restores them.
            try
            {
                var fresh = UnityAircraftConfigLoader.LoadFromStreamingAssets(cfg.Id);
                cfg.Controls.Aileron.Expo = fresh.Controls.Aileron.Expo; cfg.Controls.Aileron.DeadZone = fresh.Controls.Aileron.DeadZone;
                cfg.Controls.Elevator.Expo = fresh.Controls.Elevator.Expo; cfg.Controls.Elevator.DeadZone = fresh.Controls.Elevator.DeadZone;
                cfg.Controls.Rudder.Expo = fresh.Controls.Rudder.Expo; cfg.Controls.Rudder.DeadZone = fresh.Controls.Rudder.DeadZone;
            }
            catch (System.Exception) { }
            SessionSettings.ApplyFeel(cfg);
        }

        // ---- row helpers. Each advances y and reports whether the player changed anything, so the caller
        // ---- can write the choice out straight away: nothing on this panel is forgotten on the next launch.
        private float _x, _cw, _lh, _gap;

        private bool BoolRow(string name, ref bool v, ref float y)
        {
            UiLayout.Label(new Rect(_x, y, _cw * 0.56f, _lh), name, _label);
            float bw = (_cw * 0.4f - _gap * 0.4f) / 2f, bx = _x + _cw * 0.58f;
            bool hit = false;
            if (UiLayout.Button(new Rect(bx, y, bw, _lh), "On", v ? _btnOn : _btn)) { hit = !v; v = true; }
            if (UiLayout.Button(new Rect(bx + bw + _gap * 0.4f, y, bw, _lh), "Off", !v ? _btnOn : _btn)) { hit = v; v = false; }
            y += _lh + _gap * 0.4f;
            return hit;
        }

        /// <summary>A labelled row of mutually exclusive choices. Returns the index picked this frame, or -1.</summary>
        private int PickRow(string name, string[] names, int cur, ref float y)
        {
            UiLayout.Label(new Rect(_x, y, _cw * 0.38f, _lh), name, _label);
            int n = names.Length;
            float bw = (_cw * 0.6f - (n - 1) * _gap * 0.4f) / n, bx = _x + _cw * 0.4f;
            int hit = -1;
            for (int i = 0; i < n; i++)
                if (UiLayout.Button(new Rect(bx + i * (bw + _gap * 0.4f), y, bw, _lh), names[i], cur == i ? _btnOn : _btn)) hit = i;
            y += _lh + _gap * 0.4f;
            return hit;
        }

        private void Note(string text, ref float y)
        {
            UiLayout.Label(new Rect(_x, y, _cw, _lh * 0.8f), text, _note);
            y += _lh * 0.8f;
        }

        private bool Check(string name, bool v, ref float y)
        {
            bool hit = false;
            if (UiLayout.Button(new Rect(_x, y, _lh, _lh), v ? "X" : "", v ? _btnOn : _btn)) hit = true;
            if (UiLayout.Button(new Rect(_x + _lh + _gap, y, _cw - _lh - _gap, _lh), name, _label)) hit = true;
            y += _lh + _gap * 0.4f;
            return hit;
        }

        private void OnGUI()
        {
            if (SessionSettings.MenuOpen) { if (_open) { _open = false; Unpause(); } return; }
            EnsureStyles();
            if (!_open)
            {
                if (UiLayout.Button(UiLayout.OptionsRect, "OPTIONS", _btn)) Open();   // the shared toolbar row
                return;
            }
            UiLayout.ModalShown();          // the dials and the HUD text step aside rather than sit under the panel

            // A page is short now, so the panel is narrow in landscape too — no two-column mode to keep track of.
            float s = Mathf.Min(Screen.width, Screen.height);
            bool wide = Screen.width > Screen.height * 1.3f;
            _lh = _fs * 1.8f; _gap = _fs * 0.5f;
            float w = wide ? Mathf.Min(Screen.width * 0.62f, s * 1.45f) : Mathf.Min(Screen.width * 0.94f, s * 0.94f);
            float top = UiLayout.ToolbarBottom + _gap;

            // The header: title, the paused note, Close. Drawn before anything can scroll.
            int tabRows = wide ? 1 : 2;
            float headH = _lh + _gap * 0.5f + tabRows * (_lh + _gap * 0.35f);
            float bodyWant = _measured[(int)_page] > 0f ? _measured[(int)_page] : Screen.height;   // first pass: clamp to screen
            float h = Mathf.Min(headH + bodyWant + _gap, Screen.height - top - s * 0.03f);
            var panel = new Rect((Screen.width - w) * 0.5f, top, w, h);
            GUI.DrawTexture(panel, _bg);

            float hx = panel.x + _gap, hw = panel.width - 2 * _gap;
            UiLayout.Label(new Rect(hx, panel.y + _gap * 0.3f, hw * 0.46f, _lh), "OPTIONS", _head);
            if (SessionSettings.WorldFrozen)
                UiLayout.Label(new Rect(hx + hw * 0.46f, panel.y + _gap * 0.3f, hw * 0.3f, _lh), "world paused", _note);
            else if (SessionSettings.MultiplayerLive)
                UiLayout.Label(new Rect(hx + hw * 0.46f, panel.y + _gap * 0.3f, hw * 0.3f, _lh), "flying on — others are up", _note);
            if (UiLayout.Button(new Rect(panel.xMax - _gap - hw * 0.22f, panel.y + _gap * 0.3f, hw * 0.22f, _lh), "Close", _btnOn)) { _open = false; Unpause(); return; }

            // The chapter tabs: one row across in landscape, two rows of four on a phone in portrait.
            {
                int per = PageNames.Length / tabRows;
                float ty = panel.y + _lh + _gap * 0.5f;
                for (int r = 0; r < tabRows; r++)
                {
                    float tw = (hw - (per - 1) * _gap * 0.3f) / per;
                    for (int i = 0; i < per; i++)
                    {
                        int pi = r * per + i;
                        if (UiLayout.Button(new Rect(hx + i * (tw + _gap * 0.3f), ty, tw, _lh), PageNames[pi], (int)_page == pi ? _tabOn : _tab))
                        { _page = (Page)pi; _scroll = Vector2.zero; }
                    }
                    ty += _lh + _gap * 0.35f;
                }
            }

            // The body. Content origin sits on the view origin, so the page can be drawn in absolute coords.
            var view = new Rect(panel.x, panel.y + headH, panel.width, panel.height - headH - _gap * 0.5f);
            float sbw = bodyWant > view.height ? _fs * 1.2f : 0f;
            var content = new Rect(panel.x, view.y, panel.width - sbw, Mathf.Max(bodyWant, view.height));
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            _x = content.x + _gap; _cw = content.width - 2 * _gap;
            float y0 = view.y, y = y0;
            switch (_page)
            {
                case Page.Display: DisplayPage(ref y); break;
                case Page.Aids: AidsPage(ref y); break;
                case Page.Controls: ControlsPage(ref y); break;
                case Page.Audio: AudioPage(ref y); break;
                case Page.Clips: ClipsPage(ref y); break;
                case Page.Badges: BadgesPage(ref y); break;
                case Page.Game: GamePage(ref y); break;
                case Page.Help: HelpPage(ref y); break;
            }
            GUI.EndScrollView();
            _measured[(int)_page] = y - y0 + _gap;
        }

        private void DisplayPage(ref float y)
        {
            var modes = new[] { SessionSettings.InstrumentMode.Analog, SessionSettings.InstrumentMode.Hud, SessionSettings.InstrumentMode.None };
            int im = PickRow("Instruments", new[] { "Dials", "HUD", "None" }, System.Array.IndexOf(modes, SessionSettings.Instruments), ref y);
            if (im >= 0) { SessionSettings.Instruments = modes[im]; SessionSettings.SavePrefs(); }

            // The radio stack and the dead-reckoning instruments (owner 2026-10-18). On by default: the
            // chart shows no position, so these are the only way to find out where you are.
            bool nav = NavRadios.Shown;
            if (BoolRow("Nav radios, heading card and leg clock", ref nav, ref y)) { NavRadios.Shown = nav; SessionSettings.SavePrefs(); }

            // One choice, three ways to draw the aeroplane, and it takes effect where you are sitting --
            // AirframeVisual rebuilds on the change, so you can fly the same aeroplane three times over.
            int cur = !SessionSettings.UseAirframeModels ? 0 : SessionSettings.UseOwnModels ? 2 : 1;
            int af = PickRow("Airframe", new[] { "Drawn", "Downloaded", "Ours" }, cur, ref y);
            if (af >= 0) { SessionSettings.UseAirframeModels = af > 0; SessionSettings.UseOwnModels = af == 2; SessionSettings.SavePrefs(); }
            Note(AirframeModels.HasOwn(SessionSettings.AircraftId)
                     ? "Ours: built here — falls back to the downloaded one elsewhere"
                     : "Ours: none for this aeroplane yet — the 172 has one", ref y);

            var tri = new[] { SessionSettings.Tri.Auto, SessionSettings.Tri.On, SessionSettings.Tri.Off };
            int lm = PickRow("Show lift", new[] { "Auto", "On", "Off" }, System.Array.IndexOf(tri, SessionSettings.LiftMarkers), ref y);
            if (lm >= 0) { SessionSettings.LiftMarkers = tri[lm]; SessionSettings.SavePrefs(); }
            Note("Auto = gliders only", ref y);

            if (BoolRow("Air bubbles", ref SessionSettings.BubblesOn, ref y)) SessionSettings.SavePrefs();
            if (SessionSettings.BubblesOn)
            {
                if (BoolRow("Dense bubbles (GPU instancing)", ref SessionSettings.BubbleInstancing, ref y)) SessionSettings.SavePrefs();
                var st = new[] { SessionSettings.Streaks.Off, SessionSettings.Streaks.Normal, SessionSettings.Streaks.Long };
                int bs = PickRow("Bubble streaks", new[] { "Off", "Normal", "Long" }, System.Array.IndexOf(st, SessionSettings.BubbleStreaks), ref y);
                if (bs >= 0) { SessionSettings.BubbleStreaks = st[bs]; SessionSettings.SavePrefs(); }
                Note("Long = read α and β off the streak angle", ref y);
            }
        }

        private void AidsPage(ref float y)
        {
            if (BoolRow("Flight path vector (3 s, air-relative)", ref SessionSettings.ShowFlightPath, ref y)) SessionSettings.SavePrefs();
            if (BoolRow("Force vectors", ref SessionSettings.ShowForceVectors, ref y)) SessionSettings.SavePrefs();
            Note("lift green · drag red · moment yellow · thrust magenta · wheels cyan", ref y);
            // Owner 2026-10-07: an optional flight-test data block (alpha, beta, q, TAS/EAS/Mach, pressure and
            // density altitude, OAT, load factors, rates, path angle, control positions...).
            if (BoolRow("Flight test data", ref SessionSettings.FlightTestData, ref y)) SessionSettings.SavePrefs();
            if (BoolRow("Atmosphere map", ref SessionSettings.ShowAtmosphereMap, ref y)) SessionSettings.SavePrefs();
        }

        private void ControlsPage(ref float y)
        {
            // Control feel (owner 2026-09-15): per-axis expo and dead zone, tuned by the user. Applied live to the
            // current aircraft and to every aircraft loaded from now on; "aircraft" = that type's own value.
            SessionSettings.LoadFeel();
            float bw = (_cw - _gap * 0.4f) / 2f;
            // JOYSTICK SETUP (owner 2026-10-11): the guided calibration + button assignment, any joystick (Mac).
            if (FlightControlsDesk || JoystickInput.ConnectedName != null)
            {
                if (UiLayout.Button(new Rect(_x, y, bw, _lh), JoystickInput.ConnectedName == null ? "No joystick" : JoystickInput.Profile == null ? "Joystick setup" : "Joystick (redo)", _btn)
                    && JoystickInput.ConnectedName != null)
                { _open = false; Unpause(); JoystickSetupPanel.Open(); return; }
            }
            if (UiLayout.Button(new Rect(_x + bw + _gap * 0.4f, y, bw, _lh), "Aircraft defaults", _btn))
            {
                for (int i = 0; i < 3; i++) { SessionSettings.FeelExpo[i] = -1f; SessionSettings.FeelDeadZone[i] = -1f; }
                SessionSettings.SaveFeel(); ApplyFeelLive();
            }
            y += _lh + _gap * 0.4f;
            for (int i = 0; i < 3; i++)
            {
                var axis = Driver?.Sim?.Aircraft?.Config?.Controls == null ? null : new[] { Driver.Sim.Aircraft.Config.Controls.Aileron, Driver.Sim.Aircraft.Config.Controls.Elevator, Driver.Sim.Aircraft.Config.Controls.Rudder }[i];
                float expoNow = SessionSettings.FeelExpo[i] >= 0f ? SessionSettings.FeelExpo[i] : (float)(axis?.Expo ?? 0.3);
                float dzNow = SessionSettings.FeelDeadZone[i] >= 0f ? SessionSettings.FeelDeadZone[i] : (float)(axis?.DeadZone ?? 0.05);
                UiLayout.Label(new Rect(_x, y, _cw * 0.48f, _lh), $"{SessionSettings.FeelAxisNames[i]} expo  {expoNow * 100f:F0} %{(SessionSettings.FeelExpo[i] < 0f ? " (aircraft)" : "")}", _label);
                float e = GUI.HorizontalSlider(new Rect(_x + _cw * 0.5f, y + _lh * 0.35f, _cw * 0.5f, _lh * 0.4f), expoNow, 0f, 1f);
                if (Mathf.Abs(e - expoNow) > 0.004f) { SessionSettings.FeelExpo[i] = Mathf.Round(e * 20f) / 20f; SessionSettings.SaveFeel(); ApplyFeelLive(); }
                y += _lh * 0.95f;
                UiLayout.Label(new Rect(_x, y, _cw * 0.48f, _lh), $"{SessionSettings.FeelAxisNames[i]} dead zone  {dzNow * 100f:F0} %{(SessionSettings.FeelDeadZone[i] < 0f ? " (aircraft)" : "")}", _label);
                float d = GUI.HorizontalSlider(new Rect(_x + _cw * 0.5f, y + _lh * 0.35f, _cw * 0.5f, _lh * 0.4f), dzNow, 0f, 0.3f);
                if (Mathf.Abs(d - dzNow) > 0.002f) { SessionSettings.FeelDeadZone[i] = Mathf.Round(d * 100f) / 100f; SessionSettings.SaveFeel(); ApplyFeelLive(); }
                y += _lh * 0.95f;
            }
            // JOYSTICK LINK (owner 2026-10-12): the Mac's joystick / RC radio flies the iPad and iPhone over the local network.
            y += _gap * 0.5f;
            {
                bool on = JoyLink.On;
                string what = JoyLink.IsSender ? "Share this joystick with iPad / iPhone" : "Fly with the Mac's joystick";
                string st = JoyLink.StatusText;
                if (Check(st.Length > 0 ? $"{what}: {st}" : $"{what} (same Wi-Fi)", on, ref y)) JoyLink.On = !on;
            }
        }

        private void AudioPage(ref float y)
        {
            UiLayout.Label(new Rect(_x, y, _cw * 0.38f, _lh), "Sound volume", _label);
            FlightAudio.MasterVolume = GUI.HorizontalSlider(new Rect(_x + _cw * 0.4f, y + _lh * 0.35f, _cw * 0.6f, _lh * 0.4f), FlightAudio.MasterVolume, 0f, 1f);
            y += _lh + _gap * 0.4f;
            // Intercom + radio (owner 2026-10-01): intercom = voice-activated, heard in headphones; radio = hold TALK.
            if (Check("Intercom: talk and hear yourself (headphones)", VoiceComms.IntercomOn, ref y)) VoiceComms.IntercomOn = !VoiceComms.IntercomOn;
            UiLayout.Label(new Rect(_x, y, _cw * 0.38f, _lh), "Radio frequency", _label);
            int nf = VoiceComms.Frequencies.Length;
            float bw = (_cw * 0.6f - (nf - 1) * _gap * 0.4f) / nf, bx = _x + _cw * 0.4f;
            for (int i = 0; i < nf; i++)
            {
                string f = VoiceComms.Frequencies[i];
                if (UiLayout.Button(new Rect(bx + i * (bw + _gap * 0.4f), y, bw, _lh), f, VoiceComms.Frequency == f ? _btnOn : _btn)) VoiceComms.Frequency = f;
            }
            y += _lh + _gap * 0.4f;
        }

        private void ClipsPage(ref float y)
        {
            // Quick clips (owner 2026-10-01): how much the CLIP button saves, and whether the instruments are in the picture.
            int sec = ClipRecorder.ClipSeconds;
            UiLayout.Label(new Rect(_x, y, _cw * 0.38f, _lh), $"Clip length  {ClipRecorder.Length(sec)}", _label);
            float v = GUI.HorizontalSlider(new Rect(_x + _cw * 0.4f, y + _lh * 0.35f, _cw * 0.6f, _lh * 0.4f), sec, ClipRecorder.MinSeconds, ClipRecorder.MaxSeconds);
            if (Mathf.Abs(v - sec) >= ClipRecorder.StepSeconds * 0.5f) ClipRecorder.ClipSeconds = Mathf.RoundToInt(v);
            y += _lh + _gap * 0.4f;
            if (Check("Include instruments in clips (dials or HUD) — applies from when you tick it", ClipRecorder.IncludeInstruments, ref y))
                ClipRecorder.IncludeInstruments = !ClipRecorder.IncludeInstruments;
        }

        private void BadgesPage(ref float y)
        {
            // BADGES AND SCORES (owner 2026-10-18): the viewer's choice, both ways round. The HANDLE is not
            // on this list and never will be — you always get to know who is in the circuit with you.
            if (BoolRow("Show other pilots' badges and scores", ref SessionSettings.ShowOtherBadges, ref y)) SessionSettings.SavePrefs();
            if (BoolRow("Show my own badge on screen", ref SessionSettings.ShowOwnBadge, ref y)) SessionSettings.SavePrefs();
            // The third switch is a different kind: the two above are about this screen, this one is
            // about what leaves the device. Off means other pilots get the handle and nothing else.
            if (BoolRow("Let other pilots see my badge and score", ref SessionSettings.PublishBadge, ref y)) SessionSettings.SaveBadgePrefs();
            Note(SessionSettings.PublishBadge
                     ? "Others see your handle, badge and score."
                     : "Others see your handle only. It stays on your own profile.", ref y);
            // Your own profile, which your publishing choice never hides — it is your profile.
            var held = PilotRecord.Held();
            string badge = FlyingGame.Sim.Ratings.Certificate.Describe(held);
            double sc = PilotRecord.OwnScore();
            UiLayout.Label(new Rect(_x, y, _cw, _lh * 0.9f), $"You are: {badge}{(sc > 0 ? $"   ·   {FlyingGame.Sim.Ratings.PilotScore.Format(sc)} points" : "")}", _label);
            y += _lh * 0.9f;
            if (PilotRecord.Awards.Count > 0) Note("Awards: " + string.Join(" · ", PilotRecord.Awards), ref y);
            Note("Your lesson scores are yours. Only the badge and the leaderboard score are sent.", ref y);
        }

        private void GamePage(ref float y)
        {
            if (BoolRow("Pause while a menu is open", ref SessionSettings.PauseWhenMenuOpen, ref y)) { SessionSettings.SavePrefs(); SessionSettings.ApplyTimeScale(); }
            Note(SessionSettings.MultiplayerLive
                     ? "Not while others are flying with you — the world keeps going."
                     : "The world stands still while this panel is up, so changing a setting cannot cost you the aeroplane.", ref y);
            y += _gap * 0.5f;
            // SLOW MOTION (owner 2026-10-11): practise slow and work up to speed. Off (100 %) in multiplayer.
            var sp = SessionSettings.SimSpeeds;
            string[] spn = new string[sp.Length];
            for (int i = 0; i < sp.Length; i++) spn[i] = sp[i] >= 0.999f ? "100%" : $"{sp[i] * 100f:0}%";
            int si = PickRow("Speed of time", spn, System.Array.IndexOf(sp, SessionSettings.SimSpeed), ref y);
            if (si >= 0) { SessionSettings.SimSpeed = sp[si]; SessionSettings.SavePrefs(); SessionSettings.ApplyTimeScale(); }
            y += _gap * 0.5f;
            Note("Everything on these pages is remembered for the next launch.", ref y);
            if (UiLayout.Button(new Rect(_x, y, _cw * 0.52f, _lh), "Reset all settings", _btn)) SessionSettings.ResetPrefs();
            y += _lh + _gap * 0.4f;
        }

        private void HelpPage(ref float y)
        {
            // HELP (handoff 2026-10-17): a web page, opened in the browser. No chat in the app, and the app
            // sends nothing — the URL carries the build and the platform so the page knows which version is
            // asking. The diagnostics block goes on the CLIPBOARD and nowhere else: the customer reads it and
            // pastes it, or doesn't. See docs/support/ and SupportDesk.
            float bw = (_cw - _gap * 0.4f) / 2f;
            if (UiLayout.Button(new Rect(_x, y, bw, _lh), "Help (opens the web)", _btn)) SupportDesk.OpenHelp();
            if (UiLayout.Button(new Rect(_x + bw + _gap * 0.4f, y, bw, _lh), "Copy diagnostics", _btn)) SupportDesk.CopyDiagnostics();
            y += _lh + _gap * 0.4f;
            Note(SupportDesk.Note.Length > 0 ? SupportDesk.Note
                     : "Diagnostics = build, device, aircraft and the last warnings. No pilot name, nothing bought.", ref y);
        }
    }
}
