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
        /// <summary>A gamepad's Menu button: open the panel, or close it if it is open (never over the landing page).</summary>
        public void Toggle() { if (_open) Close(); else if (!SessionSettings.MenuOpen) Open(); }
        private GUIStyle _btn, _btnOn, _tab, _tabOn, _head, _label, _note;
        private Texture2D _bg, _btnBg, _btnOnBg, _tabBg, _tabOnBg;
        private int _fs;

        private enum Page { Display, Aids, Controls, Audio, Clips, Badges, Game, Help }
        private static readonly string[] PageNames = { "Display", "Aids", "Controls", "Audio", "Clips", "Badges", "Game", "Help" };
        private Page _page = Page.Display;
        private Vector2 _scroll;
        /// <summary>Where each page's last row ended, measured on the previous pass (0 = not drawn yet).</summary>
        private readonly float[] _measured = new float[8];
        // Drag-to-scroll (Ty 2026-11-08: "I could not scroll in options control on the screen"). The panel had only
        // GUI.BeginScrollView's scrollbar — a strip about one character wide down the right edge, which is not a thing
        // a thumb can find on a phone, and nothing else moved the page at all. StartMenu has had its own ScrollInput
        // since it was written, for the same reason; Options never got one.
        private bool _dragPossible, _dragging;
        private float _dragStartY, _dragStartScroll;

        /// <summary>Self-test: open the panel and show a given chapter by name ("display", "controls", …).</summary>
        public void ShowPageForTest(string name)
        {
            Open();
            for (int i = 0; i < PageNames.Length; i++)
                if (string.Equals(PageNames[i], name, System.StringComparison.OrdinalIgnoreCase)) { _page = (Page)i; _scroll = Vector2.zero; }
        }
        /// <summary>Self-test: the chapter names, in tab order.</summary>
        public static string[] PageNamesForTest => PageNames;
        public void ScrollForTest(float y) => _scroll = new Vector2(0f, y);

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
            int fs = Mathf.RoundToInt(UiLayout.PanelUnit * 0.026f);
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

        private void ApplyFeelLive() => SessionSettings.ReapplyFeel(Driver?.Sim?.Aircraft?.Config);

        // ---- row helpers. Each advances y and reports whether the player changed anything, so the caller
        // ---- can write the choice out straight away: nothing on this panel is forgotten on the next launch.
        private float _x, _cw, _lh, _gap;

        private bool BoolRow(string name, ref bool v, ref float y)
        {
            float h = RowH(name, _cw * 0.56f);
            UiLayout.LabelWrapped(new Rect(_x, y, _cw * 0.56f, h), name, _label);
            float bw = (_cw * 0.4f - _gap * 0.4f) / 2f, bx = _x + _cw * 0.58f;
            bool hit = false;
            if (UiLayout.Button(new Rect(bx, y, bw, _lh), "On", v ? _btnOn : _btn)) { hit = !v; v = true; }
            if (UiLayout.Button(new Rect(bx + bw + _gap * 0.4f, y, bw, _lh), "Off", !v ? _btnOn : _btn)) { hit = v; v = false; }
            y += h + _gap * 0.4f;
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

        /// <summary>The rows a MODELLED COCKPIT brings with it: which seat the view sits in, and the sequence
        /// card in the holder. Both are hidden for an aeroplane whose model has neither, so the page does not
        /// grow controls that do nothing.</summary>
        private void CockpitRows(ref float y)
        {
            AirframeVisual visual = Driver != null ? Driver.GetComponent<AirframeVisual>() : null;
            if (visual == null) return;

            // Two eye points means two seats to choose between. Solo aerobatics is flown from the rear, which
            // is why the model marks that one as its default view.
            bool twoSeats = visual.CockpitEye() != null && visual.FindInModel("Eye_Front") != null
                                                        && visual.FindInModel("Eye_Rear") != null;
            if (twoSeats)
            {
                var seats = new[] { SessionSettings.CockpitSeat.Rear, SessionSettings.CockpitSeat.Front };
                int sp = PickRow("Cockpit seat", new[] { "Rear (PIC)", "Front" },
                                 System.Array.IndexOf(seats, SessionSettings.Seat), ref y);
                if (sp >= 0)
                {
                    SessionSettings.Seat = seats[sp]; SessionSettings.SavePrefs();
                    // The cockpit view sits in the seat chosen per aircraft by ID; this row is the same choice.
                    SessionSettings.ChooseSeat(Driver.AircraftId, seats[sp] == SessionSettings.CockpitSeat.Front ? "front" : "rear");
                }
                Note("Solo aerobatics is flown from the rear seat", ref y);
            }

            // The canopy. Mapped by the rig, but a mapping nobody can reach is not a canopy, so here is the
            // handle: open and shut on the ground, and the bail-out still throws it clear in the air.
            CockpitRigState cockpit = visual.Cockpit;
            if (visual.Rig != null && visual.Rig.Find("Canopy") != null && cockpit.CanopyUsable)
            {
                int cp = PickRow("Canopy", new[] { "Shut", "Open" }, cockpit.CanopyOpen01 > 0.5f ? 1 : 0, ref y);
                if (cp == 0) cockpit.SetCanopyLock(true);
                else if (cp == 1) cockpit.CommandCanopy(1f);
                Note($"Opens below {CockpitRigState.CanopyMaxOpenKt:F0} kt; faster than that it shuts and locks", ref y);
            }

            // The 172's failure hooks and its DME switch. Partial panel is a vacuum failure: the pump quits and
            // the two air gyros run down on their own physics while the electric turn coordinator carries on.
            var c172 = CockpitRigState.LiveC172;
            if (c172 != null)
            {
                var f = c172.Failures;
                int vp = PickRow("Vacuum pump", new[] { "Working", "Failed" }, f.VacuumPump ? 1 : 0, ref y);
                if (vp >= 0) f.VacuumPump = vp == 1;
                Note($"Suction {c172.Vacuum.SuctionInHg:0.0} inHg; AI rotor {c172.Ai.Rotor.SpeedFraction * 100:0}%, DG {c172.Dg.Rotor.SpeedFraction * 100:0}%", ref y);
                int el = PickRow("Electrical bus", new[] { "Working", "Failed" }, f.Electrical ? 1 : 0, ref y);
                if (el >= 0) f.Electrical = el == 1;
                int tcf = PickRow("Turn coordinator", new[] { "Working", "Failed" }, f.TurnCoordinator ? 1 : 0, ref y);
                if (tcf >= 0) f.TurnCoordinator = tcf == 1;
                var srcs = new[] { FlyingGame.Core.Instruments.DmeSource.Nav1, FlyingGame.Core.Instruments.DmeSource.Nav2, FlyingGame.Core.Instruments.DmeSource.Hold };
                int ds = PickRow("DME", new[] { "NAV 1", "NAV 2", "HOLD" }, System.Array.IndexOf(srcs, c172.Dme.Source), ref y);
                if (ds >= 0) c172.Dme.Source = srcs[ds];
            }

            CardRows(ref y);
            AltimeterRows(visual, ref y);
        }

        /// <summary>THE IAC CARD (Ty 2026-11-05): one card for every holder and both seats. Tap the card in the cockpit to
        /// go to the next; here, step through, add your own (a PDF: every page a card; on the Mac also a PNG / JPEG) and
        /// remove them. Shown whatever the aeroplane, so the deck can be set up before choosing one.</summary>
        private void CardRows(ref float y)
        {
            SequenceCardPicker.Poll();
            FlyingGame.Core.IacCard cur = SequenceCardLibrary.Current;
            var deck = SequenceCardLibrary.Deck;
            Note($"IAC card: {cur.Label}   ({deck.All.Count} cards, {deck.UserCards.Count} yours)", ref y);
            var actions = new System.Collections.Generic.List<string> { "< Prev", "Next >" };
            if (SequenceCardPicker.Available) actions.Add("Add card…");
            if (cur.IsUser) actions.Add("Remove this");
            int act = PickRow("Card", actions.ToArray(), -1, ref y);
            if (act >= 0)
            {
                string a = actions[act];
                if (a == "< Prev") SequenceCardLibrary.Previous();
                else if (a == "Next >") SequenceCardLibrary.Next();
                else if (a == "Add card…") SequenceCardPicker.Pick();
                else if (a == "Remove this") SequenceCardLibrary.Remove(cur.Id);
            }
            if (SequenceCardPicker.Busy) Note("Choose a file…", ref y);
            else if (SequenceCardPicker.LastMessage.Length > 0) Note(SequenceCardPicker.LastMessage, ref y);
            else Note(SequenceCardPicker.HowTo + "; tap the card in the cockpit to change it", ref y);
        }

        /// <summary>The altimeter's Kollsman setting, for anyone who would rather set it here than touch the instrument.</summary>
        private void AltimeterRows(AirframeVisual visual, ref float y)
        {
            CockpitRigState c = visual.Cockpit;
            var a = c.Altimeter;
            Note($"Altimeter {FlyingGame.Core.Instruments.KollsmanAltimeter.Window(a.InHg)} inHg (window {a.Range.MinInHg:F2}-{a.Range.MaxInHg:F2}); tap the altimeter to type it", ref y);
            int act = PickRow("Altimeter", new[] { "-0.01", "+0.01", "Field elev.", "Zero here" }, -1, ref y);
            if (act == 0) a.Step(-1);
            else if (act == 1) a.Step(+1);
            else if (act == 2) c.SetAltimeterToRead(c.TrueAltitudeFt);
            else if (act == 3 && !c.SetAltimeterToRead(0.0)) Note("this altimeter's window cannot zero here", ref y);
        }

        /// <summary>
        /// Drag the page to scroll it, and the wheel on a desktop. A drag only takes over once the finger has moved
        /// further than a third of a row, so a tap still lands on the control under it and a slightly unsteady tap is
        /// not read as a scroll.
        /// </summary>
        private void ScrollInput(Rect viewport, float hidden)
        {
            if (hidden <= 1f) { _dragPossible = _dragging = false; return; }
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (viewport.Contains(e.mousePosition)) { _scroll.y = Mathf.Clamp(_scroll.y + e.delta.y * _lh * 0.6f, 0f, hidden); e.Use(); }
                    break;
                case EventType.MouseDown:
                    if (viewport.Contains(e.mousePosition))
                    { _dragPossible = true; _dragging = false; _dragStartY = e.mousePosition.y; _dragStartScroll = _scroll.y; }
                    break;
                case EventType.MouseDrag:
                    if (_dragPossible)
                    {
                        float dy = e.mousePosition.y - _dragStartY;
                        if (!_dragging && Mathf.Abs(dy) > _lh * 0.35f) _dragging = true;
                        if (_dragging) { _scroll.y = Mathf.Clamp(_dragStartScroll - dy, 0f, hidden); e.Use(); }
                    }
                    break;
                case EventType.MouseUp:
                    if (_dragging) { e.Use(); GUIUtility.hotControl = 0; }
                    _dragPossible = false; _dragging = false;
                    break;
            }
        }

        /// <summary>The more-above / more-below banner: its own solid band so it reads over whatever it covers, and
        /// not a button — tapping it must not toggle the setting underneath.</summary>
        private void MoreBar(Rect r, string text)
        {
            if (_moreBg == null) _moreBg = Solid(new Color(0.10f, 0.42f, 0.62f, 0.95f));
            if (_more == null)
                _more = new GUIStyle { font = UiFont.Get(), fontSize = Mathf.RoundToInt(_fs * 0.85f), fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _moreBg } };
            GUI.Label(r, text, _more);
        }
        private Texture2D _moreBg;
        private GUIStyle _more;

        private void Note(string text, ref float y)
        {
            // A note is a SENTENCE and wraps. UiLayout.Label drops the tail instead, which on the bigger pages threw
            // away the half that said what to do — "iOS only gives games ga…" (Ty 2026-11-08: "I can't read this").
            float h = Mathf.Max(_lh * 0.8f, UiLayout.WrappedHeight(_note, text, _cw - _fs * 0.5f) + _fs * 0.25f);
            UiLayout.LabelWrapped(new Rect(_x, y, _cw, h), text, _note);
            y += h;
        }

        /// <summary>
        /// A label with a slider beside it, where the LABEL WRAPS and the row takes the height it needs. The labels
        /// here say what the number means — "1° of device = 2.5° of view" — so losing their tail to an ellipsis loses
        /// the only part that was worth reading.
        /// </summary>
        private float SliderRow(string label, float value, float min, float max, ref float y, float labelFrac = 0.5f)
        {
            float lw = _cw * labelFrac;
            float h = RowH(label, lw);
            UiLayout.LabelWrapped(new Rect(_x, y, lw, h), label, _label);
            float v = GUI.HorizontalSlider(new Rect(_x + lw + _gap * 0.5f, y + h * 0.5f - _lh * 0.2f,
                                                    _cw - lw - _gap * 0.5f, _lh * 0.4f), value, min, max);
            y += h + _gap * 0.4f;
            return v;
        }


        /// <summary>A row tall enough for its own label once that label has wrapped — never shorter than one line.</summary>
        private float RowH(string text, float w) => Mathf.Max(_lh, UiLayout.WrappedHeight(_label, text, w - _fs * 0.5f) + _fs * 0.5f);

        private bool Check(string name, bool v, ref float y)
        {
            bool hit = false;
            float lw = _cw - _lh - _gap;
            float h = RowH(name, lw);
            if (UiLayout.Button(new Rect(_x, y, _lh, _lh), v ? "X" : "", v ? _btnOn : _btn)) hit = true;
            if (UiLayout.Button(new Rect(_x + _lh + _gap, y, lw, h), name, _label)) hit = true;
            y += h + _gap * 0.4f;
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
            // One row of tabs only if one row will really HOLD the names. Chosen by aspect alone it put eight across a
            // 508 px panel — about 57 px each — and "Controls" came out clipped at both ends as ":ontrol:" (seen in the
            // self-test shots, Ty 2026-11-08). The minimum is a share of the SCREEN, not of the scaled unit, so raising
            // the page size cannot squeeze the tabs: it adds a row instead.
            int tabRows;
            {
                float hwTabs = w - 2f * _gap, minTab = UiLayout.S * 0.14f;
                int perFit = Mathf.Max(1, Mathf.FloorToInt((hwTabs + _gap * 0.3f) / (minTab + _gap * 0.3f)));
                tabRows = perFit >= PageNames.Length ? 1 : 2;
            }
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
                UiLayout.Label(new Rect(hx + hw * 0.4f, panel.y + _gap * 0.3f, hw * 0.38f, _lh), "flying on", _note);
            if (UiLayout.Button(new Rect(panel.xMax - _gap - hw * 0.22f, panel.y + _gap * 0.3f, hw * 0.22f, _lh), "Close", _btnOn)) { _open = false; Unpause(); return; }

            // The chapter tabs: one row across in landscape, two rows of four on a phone in portrait.
            {
                int per = Mathf.CeilToInt(PageNames.Length / (float)tabRows);
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
            ScrollInput(view, content.height - view.height);
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

            // MORE ABOVE / MORE BELOW (Ty 2026-11-08: "an obvious 'more below/above' when not at the top/bottom of a
            // scrollable menu"). A thin scrollbar on a phone is not an answer — it is three pixels of grey at the edge
            // of a moving list — so the panel says it in words, across the edge the content runs off, and only when
            // there is something that way.
            float hidden = content.height - view.height;
            if (hidden > 1f)
            {
                if (_scroll.y > 1f) MoreBar(new Rect(view.x, view.y, view.width - sbw, _lh * 0.72f), "▲  MORE ABOVE");
                if (_scroll.y < hidden - 1f) MoreBar(new Rect(view.x, view.yMax - _lh * 0.72f, view.width - sbw, _lh * 0.72f), "▼  MORE BELOW");
            }
        }

        private void DisplayPage(ref float y)
        {
            Note($"Build {Application.version}", ref y);

            // UI size (Ty 2026-11-08: "The size of the menus and buttons on iPhone is too small"). The automatic part
            // sizes a button for a finger from the screen's own DPI, which is what the phone was missing; this is his
            // multiplier on top of it, so it can be set by eye on the device instead of by another build.
            float uiNow = UiLayout.UserScale;
            float uiV = SliderRow($"Menu & page size  {uiNow * 100f:F0}%", uiNow, 0.7f, 1.6f, ref y, 0.42f);
            if (Mathf.Abs(uiV - uiNow) > 0.024f) UiLayout.UserScale = Mathf.Round(uiV * 20f) / 20f;
            Note($"Menus and pages only — the flying screen is unchanged for now. Sized for a finger from this screen: {UiLayout.AutoScale * 100f:F0}% before this slider.", ref y);
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

            CockpitRows(ref y);

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
            // Ty 2026-11-03: off by default; when on they sit far off just above the horizon, never in reach.
            if (BoolRow("N / S / E / W letters on the horizon", ref SessionSettings.ShowCardinalLetters, ref y)) SessionSettings.SavePrefs();
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
                float e = SliderRow($"{SessionSettings.FeelAxisNames[i]} expo  {expoNow * 100f:F0} %{(SessionSettings.FeelExpo[i] < 0f ? " (aircraft)" : "")}",
                                    expoNow, 0f, 1f, ref y, 0.5f);
                if (Mathf.Abs(e - expoNow) > 0.004f) { SessionSettings.FeelExpo[i] = Mathf.Round(e * 20f) / 20f; SessionSettings.SaveFeel(); ApplyFeelLive(); }
                float d = SliderRow($"{SessionSettings.FeelAxisNames[i]} dead zone  {dzNow * 100f:F0} %{(SessionSettings.FeelDeadZone[i] < 0f ? " (aircraft)" : "")}",
                                    dzNow, 0f, 0.3f, ref y, 0.5f);
                if (Mathf.Abs(d - dzNow) > 0.002f) { SessionSettings.FeelDeadZone[i] = Mathf.Round(d * 100f) / 100f; SessionSettings.SaveFeel(); ApplyFeelLive(); }
            }
            // STICK-FREE WITH A GAMEPAD (Ty 2026-11-02): shown always, whether or not a pad is connected right now, so it
            // can be set before one is. Off by default; touch play is not affected by it.
            y += _gap * 0.5f;
            if (Check("Gamepad: stick-free (controls float when the stick is centred)", SessionSettings.GamepadStickFree, ref y))
                SessionSettings.GamepadStickFree = !SessionSettings.GamepadStickFree;
            // LOCAL 2-PLAYER, SPLIT SCREEN (Ty 2026-11-05): two gamepads, two aeroplanes, one screen. Off by default; it is
            // a CONTROLS choice (who flies with which pad), so it lives here beside the pad's other option. Not offered in
            // a headset: two Quest players fly in two headsets on the LAN.
            if (LocalSplit.Allowed)
            {
                if (Check("Local 2-player (split screen): two gamepads, from the next FLY", LocalSplit.Enabled, ref y))
                    LocalSplit.Enabled = !LocalSplit.Enabled;
                if (LocalSplit.Enabled && LocalSplit.NotStartedReason != null)
                {
                    UiLayout.Label(new Rect(_x, y, _cw, _lh), $"   second player not started: {LocalSplit.NotStartedReason}", _label);
                    y += _lh;
                }
            }
            // LOOKING AROUND (owner 2026-10-31; moved out of the Audio tab 2026-11-01 after Ty found it there and
            // said it makes no sense in Audio). These are CONTROLS: they decide how the pilot's head is pointed,
            // the same way expo and dead zone decide how his hands reach the stick. The hat on a real stick and the
            // pad on glass need no option; these three do.
            //
            // Shown whatever the current view is and whether or not a cockpit exists right now: the settings live
            // on CockpitView as statics, backed by PlayerPrefs, and apply the moment the cockpit view is active.
            // Before this they were drawn only when FindFirstObjectByType<CockpitView>() found one, so on the menu
            // or in an outside view the options simply were not there.
            y += _gap * 0.5f;
            {
                if (TiltSteering.Available)
                {
                    // Fly by tilting the phone (Ty 2026-11-08, for a beta tester). Roll, pitch and yaw to aileron,
                    // elevator and rudder; everything else stays on the glass.
                    if (Check("Fly by tilting the phone — aileron, elevator and rudder only", TiltSteering.Enabled, ref y))
                        TiltSteering.Enabled = !TiltSteering.Enabled;
                    if (TiltSteering.Enabled)
                    {
                        float sv = TiltSteering.Sensitivity01;
                        float ns = SliderRow($"Tilt sensitivity — {FlyingGame.Core.TiltControls.FullScaleForSensitivity(sv):F0}° for full control",
                                             sv, 0f, 1f, ref y, 0.56f);
                        if (Mathf.Abs(ns - sv) > 0.01f) TiltSteering.Sensitivity01 = ns;

                        if (Check("Yaw turns the rudder (it drifts — use CENTRE TILT)", TiltSteering.RudderFromYaw, ref y))
                            TiltSteering.RudderFromYaw = !TiltSteering.RudderFromYaw;

                        // A mirrored control is unflyable and the phone's axes cannot be known from here, so each one
                        // can be turned round without another build.
                        if (Check("Reverse roll", TiltSteering.InvertAileron, ref y)) TiltSteering.InvertAileron = !TiltSteering.InvertAileron;
                        if (Check("Reverse pitch", TiltSteering.InvertElevator, ref y)) TiltSteering.InvertElevator = !TiltSteering.InvertElevator;
                        if (TiltSteering.RudderFromYaw && Check("Reverse yaw", TiltSteering.InvertRudder, ref y))
                            TiltSteering.InvertRudder = !TiltSteering.InvertRudder;

                        // What the phone is actually reporting, so a reversed axis can be SEEN rather than guessed at.
                        Vector3 a = TiltSteering.LastAnglesDeg;
                        Note(float.IsNaN(a.x) ? "Tilt: no reading yet — hold the phone and leave this page"
                                              : $"Tilt now: roll {a.x:+0;-0}°  pitch {a.y:+0;-0}°  yaw {a.z:+0;-0}°", ref y);
                        if (UiLayout.Button(new Rect(_x, y, _cw * 0.5f, _lh), "CENTRE TILT HERE", _btn)) TiltSteering.Centre();
                        y += _lh + _gap * 0.4f;
                        Note("Hold the phone how you mean to fly, then centre. Also on the look pad in the cockpit.", ref y);
                    }
                    y += _gap * 0.3f;
                }
                if (CockpitView.DeviceAttitudeAvailable)
                {
                    // Not offered while tilt is flying: the phone cannot do both, and tilt has it.
                    if (!TiltSteering.Enabled && Check("Move the device to look around (cockpit view)", CockpitView.DeviceLookEnabled, ref y))
                        CockpitView.DeviceLookEnabled = !CockpitView.DeviceLookEnabled;
                    if (TiltSteering.Enabled)
                        Note("Move-to-look is off while tilt is flying — look around with the pad in the cockpit view.", ref y);
                    if (CockpitView.DeviceLookEnabled && !TiltSteering.Enabled)
                    {
                        // Owner Ty, 2026-11-01: "an adjustable gain from 1x (1 deg of device = 1 deg of view, the
                        // default) up to 10x, continuous". The label says what the number MEANS, because "3.4x" on
                        // its own tells a pilot nothing about how far to turn the phone.
                        float g = CockpitView.DeviceLookGain;
                        float ng = SliderRow($"   Look gain  {g:0.0}x  (1\u00b0 of device = {g:0.0}\u00b0 of view)",
                                             g, (float)FlyingGame.Core.CockpitLook.MinDeviceGain,
                                             (float)FlyingGame.Core.CockpitLook.MaxDeviceGain, ref y, 0.54f);
                        if (Mathf.Abs(ng - g) > 0.02f) CockpitView.DeviceLookGain = ng;
                    }
                }
                else
                {
                    UiLayout.Label(new Rect(_x, y, _cw, _lh), "Move the device to look around: this device has no gyro", _label);
                    y += _lh + _gap * 0.4f;
                }
                if (Check("Look snaps back to straight ahead when you let go", CockpitView.SpringCentreEnabled, ref y))
                    CockpitView.SpringCentreEnabled = !CockpitView.SpringCentreEnabled;

                // VR, next to the look settings because it is the same question -- where the pilot's head is --
                // answered with a headset instead of a hand. Only offered where the phone can track itself.
                if (VrMode.Available)
                {
                    if (Check("VR (Cardboard headset): split screen, full head tracking", VrMode.On, ref y))
                        VrMode.On = !VrMode.On;
                    if (VrMode.On)
                    {
                        float ipd = VrMode.IpdM * 1000f;
                        UiLayout.Label(new Rect(_x, y, _cw * 0.52f, _lh), $"   Eye spacing (IPD)  {ipd:0} mm", _label);
                        float nipd = GUI.HorizontalSlider(new Rect(_x + _cw * 0.54f, y + _lh * 0.35f, _cw * 0.46f, _lh * 0.4f),
                                                          ipd, (float)FlyingGame.Core.Vr.VrLensConfig.MinIpdM * 1000f,
                                                          (float)FlyingGame.Core.Vr.VrLensConfig.MaxIpdM * 1000f);
                        if (Mathf.Abs(nipd - ipd) > 0.4f) VrMode.IpdM = nipd / 1000f;
                        y += _lh + _gap * 0.4f;
                        if (UiLayout.Button(new Rect(_x, y, _cw * 0.5f, _lh), "Recentre the view", _btn)) VrMode.Recentre();
                        y += _lh + _gap * 0.4f;
                        Note("In the headset: press and hold the screen (or the Cardboard trigger) for " +
                             "0.6 s, or click a gamepad's right stick (R3), to recentre. Fly with a joystick or gamepad \u2014 the touch controls are hidden.", ref y);
                    }
                }
            }
            y += _gap * 0.5f;
            // JOYSTICK LINK (owner 2026-10-12): the Mac's joystick / RC radio flies the iPad and iPhone over the local network.
            y += _gap * 0.5f;
            {
                bool on = JoyLink.On;
                string what = JoyLink.IsSender ? "Share this joystick with iPad / iPhone" : "Fly with the Mac's joystick";
                string st = JoyLink.StatusText;
                if (Check(st.Length > 0 ? $"{what}: {st}" : $"{what} (same Wi-Fi)", on, ref y)) JoyLink.On = !on;
            }
            y += _gap * 0.5f;
            RudderPedalsSection(ref y);
        }

        // RUDDER PEDALS (owner Ty, 2026-11-02, item 11): which device, which axis is the rudder and which each toe
        // brake, invert, dead zone, and the guided calibration with its ends learnt live. The same settings as the
        // VR menu's Rudder pedals page (QuestMenu); both edit Core's PedalSession, which saves every change.
        // docs/RUDDER-PEDALS.md.
        private static readonly (FlyingGame.Core.PedalRole role, string name)[] PedalRoles =
            { (FlyingGame.Core.PedalRole.Rudder, "Rudder"), (FlyingGame.Core.PedalRole.LeftBrake, "Left toe brake"), (FlyingGame.Core.PedalRole.RightBrake, "Right toe brake") };

        private void RudderPedalsSection(ref float y)
        {
            RudderPedals.CalibrationShown();
            RudderPedals.Tick();
            FlyingGame.Core.PedalSession s = RudderPedals.Session;
            float sb = _lh * 1.3f, g = _gap * 0.4f;
            UiLayout.Label(new Rect(_x, y, _cw - 2f * (sb + g), _lh), "Rudder pedals:  " + s.DeviceLine(), _label);
            if (s.Devices.Count > 0)
            {
                if (UiLayout.Button(new Rect(_x + _cw - 2f * sb - g, y, sb, _lh), "<", _btn)) s.CycleDevice(-1);
                if (UiLayout.Button(new Rect(_x + _cw - sb, y, sb, _lh), ">", _btn)) s.CycleDevice(1);
            }
            y += _lh + g;
            if (!s.Connected)
            {
                if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    // iOS hands apps gamepads (GameController) but not generic USB HID devices: say so rather than
                    // leave a pilot plugging pedals in and wondering.
                    Note("iPhone / iPad cannot read USB rudder pedals: iOS only gives games gamepads.", ref y);
                    Note("The Mac app and the Quest can. Here the rudder and brakes stay on the pads.", ref y);
                    return;
                }
                Note("Plug USB rudder pedals in (on a Quest through a USB-C OTG adapter). Without them the rudder and brakes", ref y);
                Note("stay on the controls you fly with. < > picks a device the game did not recognise as pedals.", ref y);
                return;
            }
            if (Check("Fly the rudder and the toe brakes with the pedals", s.Profile.Enabled, ref y)) s.SetEnabled(!s.Profile.Enabled);

            FlyingGame.Core.PedalCalibrator cal = s.Calibration;
            if (cal != null)
            {
                UiLayout.Label(new Rect(_x, y, _cw, _lh), "CALIBRATING:  " + cal.Instruction, _label); y += _lh;
                UiLayout.Label(new Rect(_x, y, _cw, _lh), $"   {cal.Status}  {cal.Progress * 100f:0}%", _note); y += _lh;
                for (int i = 0; i < s.Device.Axes.Length; i++)
                {
                    (double lo, double hi) = cal.Seen(i);
                    string mark = i == cal.Focus ? "  <" : "";
                    UiLayout.Label(new Rect(_x, y, _cw, _lh * 0.8f),
                        $"   {s.Device.AxisNames[i].ToUpperInvariant(),-8} {s.Device.Axes[i],6:+0.00;-0.00}   seen {lo:+0.00;-0.00} .. {hi:+0.00;-0.00}{mark}", _note);
                    y += _lh * 0.8f;
                }
                float cw3 = (_cw - 2f * g) / 3f;
                bool sweep = cal.Current == FlyingGame.Core.PedalCalibrator.Step.Sweep;
                if (UiLayout.Button(new Rect(_x, y, cw3, _lh), sweep ? "FINISH" : "Skip", _btnOn)) { if (sweep) s.FinishCalibration(); else s.SkipCalibrationStep(); }
                if (sweep && UiLayout.Button(new Rect(_x + cw3 + g, y, cw3, _lh), "Redo", _btn)) s.StartCalibration();
                if (UiLayout.Button(new Rect(_x + 2f * (cw3 + g), y, cw3, _lh), "Cancel", _btn)) s.CancelCalibration();
                y += _lh + g;
                return;
            }

            // One row per role: name and live value, then < > (axis), Inv, dz - / +.
            float small = _lh * 1.3f, inv = _lh * 1.9f, right = 4f * small + inv + 4f * g;
            foreach (var (role, name) in PedalRoles)
            {
                FlyingGame.Core.PedalAxisCal c = s.Profile[role];
                UiLayout.Label(new Rect(_x, y, _cw - right - g, _lh), $"{name}:  {s.RoleLine(role)}", _label);
                float bx = _x + _cw - right;
                if (UiLayout.Button(new Rect(bx, y, small, _lh), "<", _btn)) s.CycleAxis(role, -1);
                bx += small + g;
                if (UiLayout.Button(new Rect(bx, y, small, _lh), ">", _btn)) s.CycleAxis(role, 1);
                bx += small + g;
                if (UiLayout.Button(new Rect(bx, y, inv, _lh), "Inv", c.Invert ? _btnOn : _btn)) s.ToggleInvert(role);
                bx += inv + g;
                if (UiLayout.Button(new Rect(bx, y, small, _lh), "dz-", _btn)) s.AdjustDead(role, -0.01);
                bx += small + g;
                if (UiLayout.Button(new Rect(bx, y, small, _lh), "dz+", _btn)) s.AdjustDead(role, 0.01);
                y += _lh + g;
            }
            float half = (_cw - g) / 2f;
            if (UiLayout.Button(new Rect(_x, y, half, _lh), "Calibrate (guided)", _btn)) s.StartCalibration();
            if (UiLayout.Button(new Rect(_x + half + g, y, half, _lh), "Reset to detected", _btn)) s.ResetToDefaults();
            y += _lh + g;
            Note("Right pedal forward should read +, each toe brake 0 % released and 100 % on the floor.", ref y);
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
            // Show my screen (Ty 2026-11-08, for his tutorial videos): all or none. On, the clip is the glass —
            // touch pads, these menus, bubbles, vectors, HUD. Off, it is the aeroplane and the world, nothing else.
            if (Check("Show my screen in clips — controls, menus, bubbles and vectors, exactly as you see it", ClipRecorder.ShowMyScreen, ref y))
                ClipRecorder.ShowMyScreen = !ClipRecorder.ShowMyScreen;
            // Narration (Ty 2026-11-08, for talking over a tutorial). Voice-activated, and the microphone stays open
            // while this is ticked — the clip buffer is always rolling, so a voice switched on at REC would be late.
            if (Check("Record my voice in clips — voice-activated; the microphone stays on while this is ticked", VoiceComms.NarrateClips, ref y))
                VoiceComms.NarrateClips = !VoiceComms.NarrateClips;
            if (VoiceComms.NarrateClips && VoiceComms.MicProblem != null)
            {
                UiLayout.Label(new Rect(_x, y, _cw, _lh), VoiceComms.MicProblem, _label);
                y += _lh + _gap * 0.4f;
            }
            if (!ClipRecorder.ShowMyScreen
                && Check("Include instruments in clips (dials or HUD) — applies from when you tick it", ClipRecorder.IncludeInstruments, ref y))
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
            if (held.PilotAircraft != null) Note($"Pilot flown in the {FleetName(held.PilotAircraft)}.", ref y);
            if (held.CarriedOver.Count > 0) Note("Carried over from an earlier version: " + StageTitles(held.CarriedOver) + ".", ref y);
            // What is open next, and how far off it is (owner 2026-11-01: glider solo, Cub solo, then the
            // Decathlon or the Great Lakes, then the aerobatic glider, Blind Flying, Commercial, Instructor).
            foreach (var (stage, missing) in PilotRecord.NextStages())
            {
                string where = stage.Aircraft.Length == 0 ? " in any powered aeroplane"
                             : " in the " + string.Join(" or the ", System.Linq.Enumerable.Select(stage.Aircraft, FleetName))
                               + (stage.Aircraft.Length > 1 ? " (your choice)" : "");
                Note(!FlyingGame.Sim.Ratings.RatingResolver.Flyable(stage)
                         ? $"Next: {stage.Title} — coming soon: its aeroplane is not in the hangar yet."
                         : $"Next: {stage.Title} — {missing.Count} of {stage.Lessons.Length} lessons still to fly to {stage.PassScore:0} %{where}.", ref y);
            }
            if (PilotRecord.Awards.Count > 0) Note("Awards: " + string.Join(" · ", PilotRecord.Awards), ref y);
            Note("Your lesson scores are yours. Only the badge and the leaderboard score are sent.", ref y);
        }

        /// <summary>An aircraft id as the pickers name it; a placeholder by its own name.</summary>
        private static string FleetName(string id)
        {
            foreach (var f in SessionSettings.Fleet) if (f.id == id) return f.name;
            foreach (var p in FlyingGame.Sim.Ratings.Hangar.Placeholders) if (p.Id == id) return p.Name;
            return id;
        }

        private static string StageTitles(System.Collections.Generic.List<string> ids)
        {
            var book = PilotRecord.Book;
            return string.Join(", ", System.Linq.Enumerable.Select(ids, i => book.Stage(i)?.Title ?? i));
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
