using System.Collections.Generic;
using System.Linq;
using FlyingGame.Core.Landing;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The landing flow on a flat screen (iPhone, iPad, Mac), IMGUI like every menu here (iOS strips the default skin, so
    /// styles are built from scratch). This is the FUNCTIONAL BASELINE: every control the flow offers, laid out so nothing
    /// overlaps at the four shapes the self-test shoots. lane/codex-landing-v2 owns the look (cards, icons, carousels,
    /// light/dark); its changes belong in the Draw* methods here, while every call into <see cref="LandingFlow"/> is the
    /// binding and stays.
    ///
    ///  PAGE 1 — left: four menus (Aircraft, Configuration, Location, Viewpoint), each a tile showing the current card;
    ///    centre: the live aeroplane (<see cref="LandingPreview"/>); right: Challenges and Lessons. A tile opens its
    ///    thumbnail strip over the page; drag it sideways, tap a card. NEXT.
    ///  PAGE 2 — left: seats, fuel, baggage; centre: the aeroplane again and the weight-and-balance plot; right: time of
    ///    day, weather, reliability. BACK, LAUNCH.
    /// "More settings" opens the classic chapters (instruments, multiplayer, runway, livery, tow plane, the exact wind):
    /// nothing that page could do is lost.
    /// </summary>
    public sealed class LandingView
    {
        private readonly StartMenu _host;
        public LandingView(StartMenu host) { _host = host; }

        private GUIStyle _title, _head, _label, _small, _tiny, _btn, _btnOn, _btnBig, _card, _cardOn, _cardOff;
        private Texture2D _bg, _panel, _panelLight, _btnBg, _btnOnBg, _cardBg, _cardOnBg, _white, _dim;
        private int _fs;
        private float _stripScroll, _dragX, _dragScroll; private bool _dragPossible, _dragging;

        public LandingFlow Flow => LandingSession.Flow;

        // ================================================================ styles

        private static Texture2D Solid(Color c) { var t = new Texture2D(2, 2, TextureFormat.ARGB32, false); t.SetPixels(new[] { c, c, c, c }); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.Max(13, Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f));
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.94f));
            _panel ??= Solid(new Color(0.09f, 0.13f, 0.19f, 0.96f));
            _panelLight ??= Solid(new Color(0.80f, 0.86f, 0.90f, 0.96f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.97f));
            _btnOnBg ??= Solid(new Color(0.20f, 0.62f, 0.35f, 0.97f));
            _cardBg ??= Solid(new Color(0.13f, 0.18f, 0.25f, 0.98f));
            _cardOnBg ??= Solid(new Color(1.00f, 0.82f, 0.36f, 1f));
            _white ??= Solid(Color.white);
            _dim ??= Solid(new Color(0f, 0f, 0f, 0.55f));
            Font f = UiFont.Get();
            GUIStyle S(int size, FontStyle st, TextAnchor a, Color c) => new GUIStyle { font = f, fontSize = size, fontStyle = st, alignment = a, normal = { textColor = c }, wordWrap = false, clipping = TextClipping.Clip };
            _title = S(Mathf.RoundToInt(fs * 1.35f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            _head = S(Mathf.RoundToInt(fs * 0.85f), FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.55f, 0.95f, 1f));
            _label = S(fs, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            _small = S(Mathf.RoundToInt(fs * 0.8f), FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.82f, 0.87f, 0.92f));
            _tiny = S(Mathf.RoundToInt(fs * 0.68f), FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.80f, 0.85f, 0.9f));
            _tiny.wordWrap = true;
            _btn = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(6, 6, 4, 4) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _btnBig = new GUIStyle(_btnOn) { fontSize = Mathf.RoundToInt(fs * 1.2f) };
            _card = new GUIStyle { normal = { background = _cardBg } };
            _cardOn = new GUIStyle { normal = { background = _cardOnBg } };
            _cardOff = new GUIStyle { normal = { background = _btnBg } };
        }

        // ================================================================ frame

        private float _m, _gap, _bh;

        public void Draw()
        {
            EnsureStyles();
            var flow = Flow;
            float s = Mathf.Min(Screen.width, Screen.height);
            _m = s * 0.025f; _gap = s * 0.014f; _bh = _fs * 2.0f;
            float left = Mathf.Max(_m, UiLayout.SafeLeft + _m * 0.4f), right = Screen.width - Mathf.Max(_m, UiLayout.SafeRight + _m * 0.4f);
            float top = UiLayout.SafeTop + _m * 0.5f, bottom = Screen.height - UiLayout.SafeBottom - _m * 0.5f;
            var safe = Rect.MinMaxRect(left, top, right, bottom);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);

            bool menuOpen = flow.OpenPicker != PickerKind.None;
            bool confirming = flow.LaunchConfirmPending;
            GUI.enabled = !menuOpen && !confirming;
            float headH = _bh * 1.1f, footH = _bh * 1.35f;
            DrawHeader(new Rect(safe.x, safe.y, safe.width, headH), flow);
            var body = new Rect(safe.x, safe.y + headH + _gap, safe.width, safe.height - headH - footH - 2 * _gap);
            if (flow.Page == LandingPage.Choose) DrawChoose(body, flow); else DrawLoad(body, flow);
            DrawFooter(new Rect(safe.x, safe.yMax - footH, safe.width, footH), flow);
            GUI.enabled = true;
            if (menuOpen) DrawStrip(safe, flow);
            if (confirming) DrawLaunchConfirm(safe, flow);
        }

        private void DrawHeader(Rect r, LandingFlow flow)
        {
            float stepW = Mathf.Min(r.width * 0.30f, _fs * 13f);
            UiLayout.Label(new Rect(r.x, r.y, r.width - 2 * stepW - _gap, r.height), flow.Page == LandingPage.Choose ? "CHOOSE YOUR FLIGHT" : "LOAD AND LAUNCH", _title);
            if (UiLayout.Button(new Rect(r.xMax - 2 * stepW - _gap * 0.5f, r.y + r.height * 0.12f, stepW, r.height * 0.76f), "1  Aircraft & place", flow.Page == LandingPage.Choose ? _btnOn : _btn)) flow.GoTo(LandingPage.Choose);
            if (UiLayout.Button(new Rect(r.xMax - stepW, r.y + r.height * 0.12f, stepW, r.height * 0.76f), "2  Load & launch", flow.Page == LandingPage.Load ? _btnOn : _btn)) flow.GoTo(LandingPage.Load);
        }

        private void DrawFooter(Rect r, LandingFlow flow)
        {
            float bw = Mathf.Min(r.width * 0.26f, _fs * 12f);
            float sw = Mathf.Min(r.width * 0.16f, _fs * 8f);
            if (flow.Page == LandingPage.Choose)
            {
                if (UiLayout.Button(new Rect(r.xMax - bw, r.y, bw, r.height), "NEXT  ›", _btnBig)) flow.Next();
            }
            else
            {
                if (UiLayout.Button(new Rect(r.x, r.y, sw, r.height), "‹  BACK", _btn)) flow.Back();
                if (UiLayout.Button(new Rect(r.xMax - bw, r.y, bw, r.height), "LAUNCH", _btnBig)) flow.RequestLaunch();
            }
            float x0 = flow.Page == LandingPage.Load ? r.x + sw + _gap : r.x;
            float small = r.height * 0.62f, sbw = Mathf.Min(_fs * 7.5f, (r.xMax - bw - _gap - x0) / 3.2f);
            if (UiLayout.Button(new Rect(x0, r.yMax - small, sbw * 1.25f, small), "More settings", _btn)) _host.ShowClassic(null);
            if (UiLayout.Button(new Rect(x0 + sbw * 1.25f + _gap * 0.5f, r.yMax - small, sbw, small), "HELP", _btn)) SupportDesk.OpenHelp();
            if (UiLayout.Button(new Rect(x0 + sbw * 2.25f + _gap, r.yMax - small, sbw, small), "CHART", _btn)) ChartPanel.Toggle();
        }

        // ================================================================ page 1

        private void DrawChoose(Rect r, LandingFlow flow)
        {
            bool portrait = Screen.height > Screen.width;
            Rect leftCol, hero, rightCol;
            if (portrait)
            {
                hero = new Rect(r.x, r.y, r.width, r.height * 0.38f);
                float below = r.height - hero.height - _gap, half = (r.width - _gap) * 0.5f;
                leftCol = new Rect(r.x, hero.yMax + _gap, half, below);
                rightCol = new Rect(r.x + half + _gap, hero.yMax + _gap, half, below);
            }
            else
            {
                float lw = r.width * 0.27f, rw = r.width * 0.23f;
                leftCol = new Rect(r.x, r.y, lw, r.height);
                rightCol = new Rect(r.xMax - rw, r.y, rw, r.height);
                hero = new Rect(leftCol.xMax + _gap, r.y, r.width - lw - rw - 2 * _gap, r.height);
            }
            DrawHero(hero, flow, true);

            var kinds = new[] { PickerKind.Aircraft, PickerKind.Configuration, PickerKind.Location, PickerKind.Viewpoint };
            string[] heads = { "AIRCRAFT", "CONFIGURATION", "LOCATION", "VIEWPOINT" };
            float th = (leftCol.height - 3 * _gap) / 4f;
            for (int i = 0; i < 4; i++) DrawTile(new Rect(leftCol.x, leftCol.y + i * (th + _gap), leftCol.width, th), heads[i], kinds[i], flow);

            float rh = (rightCol.height - 2 * _gap) / 3f;
            DrawTile(new Rect(rightCol.x, rightCol.y, rightCol.width, rh), "CHALLENGES", PickerKind.Challenge, flow);
            DrawTile(new Rect(rightCol.x, rightCol.y + rh + _gap, rightCol.width, rh), "LESSONS", PickerKind.Lesson, flow);
            var note = new Rect(rightCol.x, rightCol.y + 2 * (rh + _gap), rightCol.width, rh);
            GUI.DrawTexture(note, _panel);
            var inner = Inset(note, _gap * 0.6f);
            var rules = flow.Rules;
            string text = flow.Activity == null ? "Free flight: every aeroplane, every place, every start."
                : string.Join("\n", new[] { flow.Activity.Name, rules.AircraftNote, rules.FixedViewpointNote, rules.ConditionsLocked ? "Scored: the conditions are the contest's" : null }.Where(x => x != null));
            GUI.Label(new Rect(inner.x, inner.y, inner.width, inner.height - _bh), text, _tiny);
            bool hasOptions = flow.ActivityKind == ActivityKind.Lesson || flow.ActivityId == "event:dogfight";
            if (hasOptions && UiLayout.Button(new Rect(inner.x, inner.yMax - _bh * 0.85f, inner.width, _bh * 0.85f), flow.ActivityKind == ActivityKind.Lesson ? "Lesson options" : "Opponent", _btn))
                _host.ShowClassic("Lessons");
        }

        /// <summary>One menu tile: its heading, the current card's picture and name. Tap to open its strip.</summary>
        private void DrawTile(Rect r, string head, PickerKind kind, LandingFlow flow)
        {
            bool fixedOne = flow.IsFixed(kind);
            GUI.DrawTexture(r, flow.OpenPicker == kind ? _btnOnBg : _panel);
            var items = flow.Items(kind);
            var cur = items.FirstOrDefault(i => i.Selected) ?? items.FirstOrDefault();
            float pad = _gap * 0.5f;
            float picH = r.height - 2 * pad, picW = Mathf.Min(picH * 16f / 9f, r.width * 0.45f);
            var pic = new Rect(r.x + pad, r.y + pad, picW, picH);
            DrawPicture(pic, cur, kind);
            float tx = pic.xMax + pad, tw = r.xMax - tx - pad;
            UiLayout.Label(new Rect(tx, r.y + pad, tw, r.height * 0.3f), head, _head);
            UiLayout.Label(new Rect(tx, r.y + r.height * 0.32f, tw, r.height * 0.34f), cur?.Title ?? "-", _label);
            UiLayout.Label(new Rect(tx, r.y + r.height * 0.64f, tw, r.height * 0.3f), fixedOne && kind == PickerKind.Viewpoint && flow.Rules.FixedViewpointNote != null ? "set by the activity" : cur?.Subtitle ?? "", _small);
            if (!fixedOne && GUI.Button(r, GUIContent.none, GUIStyle.none)) { flow.Open(kind); _stripScroll = 0f; ScrollToSelected(flow); }
        }

        /// <summary>The live aeroplane, with the configuration's feedback drawn over it (fuel, bags, people).</summary>
        private void DrawHero(Rect r, LandingFlow flow, bool titles)
        {
            GUI.DrawTexture(r, _panel);
            var preview = LandingPreview.Instance;
            if (preview != null) preview.Framing = flow.Page == LandingPage.Choose ? LandingPreview.FramingMode.Hero : LandingPreview.FramingMode.Load;
            Texture tex = preview != null && preview.Texture != null ? preview.Texture : (Texture)LandingThumbnails.Get(flow.HeroThumbnails);
            if (tex != null) GUI.DrawTexture(Inset(r, 2f), tex, ScaleMode.ScaleAndCrop);
            if (!titles) return;
            var band = new Rect(r.x, r.yMax - _bh * 1.6f, r.width, _bh * 1.6f);
            GUI.DrawTexture(band, _dim);
            UiLayout.Label(new Rect(band.x + _gap, band.y + _gap * 0.2f, band.width - 2 * _gap, _bh * 0.85f), flow.HeroTitle, _title);
            UiLayout.Label(new Rect(band.x + _gap, band.y + _bh * 0.85f, band.width - 2 * _gap, _bh * 0.6f), flow.HeroSubtitle, _small);
        }

        private void DrawPicture(Rect r, PickerItem item, PickerKind kind)
        {
            Texture2D t = LandingThumbnails.Get(item);
            if (t != null) { GUI.DrawTexture(r, t, ScaleMode.ScaleAndCrop); return; }
            // No picture yet: a plain card with its initial, until the pipeline captures one (the visual lane's icons
            // go here: item.IconKey).
            GUI.DrawTexture(r, _btnBg);
            var st = new GUIStyle(_title) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(r.height * 0.42f), normal = { textColor = new Color(1f, 1f, 1f, 0.55f) } };
            string glyph = string.IsNullOrEmpty(item?.Title) ? "?" : item.Title.Substring(0, 1).ToUpperInvariant();
            GUI.Label(r, glyph, st);
        }

        // ================================================================ the thumbnail strip (an open menu)

        private void ScrollToSelected(LandingFlow flow)
        {
            var items = flow.Items(flow.OpenPicker);
            int i = items.FindIndex(x => x.Selected);
            _stripScrollTarget = i;
        }
        private int _stripScrollTarget = -1;

        private void DrawStrip(Rect safe, LandingFlow flow)
        {
            Event e = Event.current;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);
            float h = Mathf.Min(safe.height * 0.62f, safe.width * 0.42f);
            var panel = new Rect(safe.x, safe.yMax - h, safe.width, h);
            // A tap outside closes the menu and does nothing else.
            if (e.type == EventType.MouseDown && !panel.Contains(e.mousePosition)) { flow.ClosePicker(); e.Use(); return; }
            GUI.DrawTexture(panel, _panel);
            string title = flow.OpenPicker switch
            {
                PickerKind.Aircraft => flow.Rules.AircraftNote != null ? $"AIRCRAFT  ·  {flow.Rules.AircraftNote}" : "AIRCRAFT",
                PickerKind.Configuration => "CONFIGURATION  ·  " + flow.Family.Name, PickerKind.Location => "LOCATION  ·  seen from your viewpoint",
                PickerKind.Viewpoint => "VIEWPOINT  ·  what you will see first", PickerKind.Challenge => "CHALLENGES", PickerKind.Lesson => "LESSONS", _ => "",
            };
            UiLayout.Label(new Rect(panel.x + _gap, panel.y + _gap * 0.4f, panel.width - _bh * 2.5f, _bh), title, _head);
            if (UiLayout.Button(new Rect(panel.xMax - _bh * 1.6f - _gap, panel.y + _gap * 0.4f, _bh * 1.6f, _bh * 0.9f), "Close", _btn)) { flow.ClosePicker(); return; }

            var items = flow.Items(flow.OpenPicker);
            var strip = new Rect(panel.x + _gap, panel.y + _bh * 1.3f, panel.width - 2 * _gap, panel.height - _bh * 1.3f - _gap);
            float cardH = strip.height, cardW = Mathf.Min(cardH * 1.25f, strip.width * 0.46f), step = cardW + _gap;
            float maxScroll = Mathf.Max(0f, items.Count * step - _gap - strip.width);
            if (_stripScrollTarget >= 0) { _stripScroll = Mathf.Clamp(_stripScrollTarget * step - (strip.width - cardW) * 0.5f, 0, maxScroll); _stripScrollTarget = -1; }
            switch (e.type)
            {
                case EventType.ScrollWheel: if (strip.Contains(e.mousePosition)) { _stripScroll += (e.delta.y + e.delta.x) * cardW * 0.08f; e.Use(); } break;
                case EventType.MouseDown: if (strip.Contains(e.mousePosition)) { _dragPossible = true; _dragging = false; _dragX = e.mousePosition.x; _dragScroll = _stripScroll; } break;
                case EventType.MouseDrag:
                    if (_dragPossible) { float dx = e.mousePosition.x - _dragX; if (!_dragging && Mathf.Abs(dx) > _fs * 0.6f) _dragging = true; if (_dragging) { _stripScroll = _dragScroll - dx; e.Use(); } }
                    break;
                case EventType.MouseUp: if (_dragging) { e.Use(); GUIUtility.hotControl = 0; } _dragPossible = false; _dragging = false; break;
            }
            _stripScroll = Mathf.Clamp(_stripScroll, 0f, maxScroll);
            GUI.BeginGroup(strip);
            for (int i = 0; i < items.Count; i++)
            {
                float x = i * step - _stripScroll;
                if (x + cardW < 0 || x > strip.width) continue;
                if (DrawCard(new Rect(x, 0, cardW, cardH), items[i])) { flow.Pick(flow.OpenPicker == PickerKind.None ? items[i].Kind : flow.OpenPicker, items[i].Id); break; }
            }
            GUI.EndGroup();
            if (maxScroll > 1f)
            {
                float frac = strip.width / (maxScroll + strip.width), tw = strip.width * frac, tx = strip.x + (strip.width - tw) * (_stripScroll / maxScroll);
                GUI.DrawTexture(new Rect(tx, panel.yMax - _gap * 0.55f, tw, _gap * 0.3f), _btnOnBg);
            }
        }

        /// <summary>A card: picture on top, title and subtitle under it; the selected card is outlined in the accent.
        /// Returns true when tapped.</summary>
        private bool DrawCard(Rect r, PickerItem it)
        {
            GUI.Box(r, GUIContent.none, it.Selected ? _cardOn : it.Enabled ? _card : _cardOff);
            var inner = Inset(r, it.Selected ? _gap * 0.35f : 2f);
            GUI.DrawTexture(inner, _cardBg);
            float picH = Mathf.Min(inner.height * 0.66f, inner.width * 9f / 16f);
            DrawPicture(new Rect(inner.x, inner.y, inner.width, picH), it, it.Kind);
            var c = GUI.color; if (!it.Enabled) GUI.color = new Color(1, 1, 1, 0.45f);
            float ty = inner.y + picH + _gap * 0.3f, rest = inner.yMax - ty;
            UiLayout.Label(new Rect(inner.x + _gap * 0.4f, ty, inner.width - _gap * 0.8f, Mathf.Min(_bh * 0.75f, rest * 0.5f)), it.Title, _label);
            GUI.Label(new Rect(inner.x + _gap * 0.4f, ty + Mathf.Min(_bh * 0.75f, rest * 0.5f), inner.width - _gap * 0.8f, rest * 0.5f), it.Enabled ? it.Subtitle : it.DisabledReason ?? "", _tiny);
            GUI.color = c;
            return it.Enabled && !_dragging && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ================================================================ page 2

        private void DrawLoad(Rect r, LandingFlow flow)
        {
            bool portrait = Screen.height > Screen.width;
            Rect leftCol, centre, rightCol;
            if (portrait)
            {
                centre = new Rect(r.x, r.y, r.width, r.height * 0.36f);
                float below = r.height - centre.height - _gap, half = (r.width - _gap) * 0.5f;
                leftCol = new Rect(r.x, centre.yMax + _gap, half, below);
                rightCol = new Rect(r.x + half + _gap, centre.yMax + _gap, half, below);
            }
            else
            {
                float lw = r.width * 0.32f, rw = r.width * 0.30f;
                leftCol = new Rect(r.x, r.y, lw, r.height);
                rightCol = new Rect(r.xMax - rw, r.y, rw, r.height);
                centre = new Rect(leftCol.xMax + _gap, r.y, r.width - lw - rw - 2 * _gap, r.height);
            }
            if (portrait)
            {
                float hw = (centre.width - _gap) * 0.5f;
                DrawHero(new Rect(centre.x, centre.y, hw, centre.height), flow, false);
                DrawWeightBalance(new Rect(centre.x + hw + _gap, centre.y, hw, centre.height), flow);
            }
            else
            {
                float heroH = centre.height * 0.5f;
                DrawHero(new Rect(centre.x, centre.y, centre.width, heroH), flow, false);
                DrawWeightBalance(new Rect(centre.x, centre.y + heroH + _gap, centre.width, centre.height - heroH - _gap), flow);
            }
            DrawPeopleAndLoad(leftCol, flow);
            DrawSettings(rightCol, flow);
        }

        private void DrawPeopleAndLoad(Rect r, LandingFlow flow)
        {
            GUI.DrawTexture(r, _panel);
            var inner = Inset(r, _gap * 0.6f);
            float y = inner.y, row = _bh * 0.95f, stepW = _bh * 0.95f;
            UiLayout.Label(new Rect(inner.x, y, inner.width, row * 0.8f), "PEOPLE  ·  tap a seat", _head); y += row * 0.8f;
            foreach (var s in flow.Seats)
            {
                string who = s.Occupant switch
                {
                    Occupant.Me => "You",
                    Occupant.DefaultPerson => "Passenger",
                    Occupant.InvitedPlayer => (s.InviteName ?? "Friend") + (s.Role == SeatRole.DualControl ? " · flying" : " · riding"),
                    _ => "Empty",
                };
                var seatR = new Rect(inner.x, y, inner.width - 2 * stepW - _gap * 0.6f, row);
                GUI.DrawTexture(seatR, s.Occupant == Occupant.Me ? _btnOnBg : s.Occupant == Occupant.Empty ? _btnBg : _cardBg);
                // The seat's own name, short ("Rear seat (solo)" → "Rear"), then who sits there.
                string seatName = s.Label.Split(' ')[0];
                UiLayout.Label(new Rect(seatR.x + _gap * 0.4f, seatR.y, seatR.width * 0.26f, row), seatName, _small);
                UiLayout.Label(new Rect(seatR.x + seatR.width * 0.28f, seatR.y, seatR.width * 0.72f - _gap * 0.3f, row), s.Occupant == Occupant.Empty ? who : $"{who}  {flow.MassText(s.Kg)}", _label);
                if (s.Occupant != Occupant.Me && GUI.Button(seatR, GUIContent.none, GUIStyle.none)) flow.CycleSeat(s.SeatId);
                if (s.Occupant != Occupant.Empty)
                {
                    if (UiLayout.Button(new Rect(seatR.xMax + _gap * 0.3f, y, stepW, row), "−", _btn)) flow.SetSeatKg(s.SeatId, s.Kg - flow.MassStepKg);
                    if (UiLayout.Button(new Rect(seatR.xMax + _gap * 0.3f + stepW, y, stepW, row), "+", _btn)) flow.SetSeatKg(s.SeatId, s.Kg + flow.MassStepKg);
                }
                y += row + _gap * 0.3f;
            }
            if (flow.Seats.Count > 1 && flow.Seats.Any(s => s.HasControls && s.Occupant != Occupant.Me))
            { GUI.Label(new Rect(inner.x, y, inner.width, row * 0.7f), "An invited player can ride along or take the other set of controls.", _tiny); y += row * 0.7f; }

            var fuel = flow.Stations.Where(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Fuel).ToList();
            var bags = flow.Stations.Where(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Baggage).ToList();
            if (fuel.Count > 0 && y + row * 2 < inner.yMax)
            {
                y += _gap * 0.3f;
                UiLayout.Label(new Rect(inner.x, y, inner.width, row * 0.8f), $"FUEL  ·  {flow.FuelFraction * 100:0} %", _head); y += row * 0.8f;
                float q = (inner.width - 3 * _gap * 0.4f) / 4f;
                (string n, double f)[] presets = { ("Empty", 0), ("¼", 0.25), ("Tabs", 0.5), ("Full", 1) };
                for (int i = 0; i < 4; i++)
                    if (UiLayout.Button(new Rect(inner.x + i * (q + _gap * 0.4f), y, q, row), presets[i].n, System.Math.Abs(flow.FuelFraction - presets[i].f) < 0.02 ? _btnOn : _btn)) flow.SetFuelFraction(presets[i].f);
                y += row + _gap * 0.3f;
                var bar = new Rect(inner.x, y, inner.width, row * 0.3f);
                GUI.DrawTexture(bar, _btnBg);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (float)flow.FuelFraction, bar.height), _btnOnBg);
                y += row * 0.3f + _gap * 0.5f;
            }
            foreach (var b in bags)
            {
                if (y + row > inner.yMax) break;
                UiLayout.Label(new Rect(inner.x, y, inner.width - 2 * stepW - _gap, row), $"{b.Label}  {flow.MassNumber(b.Kg)} / {flow.MassText(b.MaxKg)}", _label);
                if (UiLayout.Button(new Rect(inner.xMax - 2 * stepW, y, stepW, row), "−", _btn)) flow.SetStationKg(b.StationId, b.Kg - flow.MassStepKg);
                if (UiLayout.Button(new Rect(inner.xMax - stepW, y, stepW, row), "+", _btn)) flow.SetStationKg(b.StationId, b.Kg + flow.MassStepKg);
                y += row + _gap * 0.3f;
            }
            if (flow.Load == null || flow.Load.Slots.Count == 0)
                GUI.Label(new Rect(inner.x, y, inner.width, inner.yMax - y), "This type has no loading stations authored yet: it flies at its authored weight and balance.", _tiny);
        }

        /// <summary>The W&amp;B plot, laid out like a handbook's chart (CG across, weight up): the type's verified envelope
        /// with its limit lines named, the takeoff point, and the line the CG walks to the zero-fuel point as the fuel
        /// burns (Ty t880u). Factual labels only (t881u). The visual lane restyles this; the data is WeightBalance's.</summary>
        private void DrawWeightBalance(Rect r, LandingFlow flow)
        {
            GUI.DrawTexture(r, _panel);
            var inner = Inset(r, _gap * 0.6f);
            var wb = flow.WeightBalance;
            if (wb == null) { UiLayout.Label(inner, "No loading data for this type", _small); return; }
            string head = $"WEIGHT & BALANCE  ·  {flow.MassText(wb.TotalKg)}" + (wb.HasGross ? $" of {flow.MassNumber(wb.MaxGrossKg)}" : "");
            UiLayout.Label(new Rect(inner.x, inner.y, inner.width, _bh * 0.8f), head, _head);
            float warnH = _bh * 0.7f;
            float yGutter = _fs * 2.6f, xGutter = _fs * 2.1f;
            var plot = new Rect(inner.x + yGutter, inner.y + _bh * 0.85f, inner.width - yGutter, inner.height - _bh * 0.85f - warnH - xGutter);
            GUI.DrawTexture(plot, _btnBg);
            Vector2 P(double x, double y) => new Vector2(plot.x + (float)x * plot.width, plot.yMax - (float)y * plot.height);

            // Axes: ticks in the chosen units, and what each axis measures.
            var tickR = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            var tickC = new GUIStyle(_tiny) { alignment = TextAnchor.UpperCenter, wordWrap = false };
            foreach (var t in wb.YTicks)
            {
                Vector2 a = P(0, t.At);
                Line(a, P(1, t.At), new Color(1f, 1f, 1f, 0.08f), 1f);
                GUI.Label(new Rect(inner.x, a.y - _fs * 0.5f, yGutter - 4, _fs), t.Label, tickR);
            }
            foreach (var t in wb.XTicks)
            {
                Vector2 a = P(t.At, 0);
                Line(a, P(t.At, 1), new Color(1f, 1f, 1f, 0.08f), 1f);
                GUI.Label(new Rect(a.x - _fs * 1.6f, plot.yMax + 1, _fs * 3.2f, _fs), t.Label, tickC);
            }
            GUI.Label(new Rect(plot.x, plot.yMax + _fs * 1.0f, plot.width, _fs), wb.XAxisTitle, tickC);
            GUI.Label(new Rect(inner.x, plot.y - _fs * 0.1f, yGutter + _fs * 4f, _fs), DisplayUnits.MassUnit(flow.Units), _tiny);

            // The envelope and its named limits; a gross with no CG limits is still drawn.
            if (wb.Envelope.Count > 1)
                for (int i = 1; i < wb.Envelope.Count; i++) Line(P(wb.Envelope[i - 1].x, wb.Envelope[i - 1].y), P(wb.Envelope[i].x, wb.Envelope[i].y), new Color(0.55f, 0.95f, 1f), 2f);
            foreach (var lim in wb.Limits)
            {
                var col = lim.Label == WeightBalance.MaxGrossLineLabel ? new Color(1f, 0.6f, 0.3f, 0.85f) : new Color(0.55f, 0.95f, 1f);
                if (!wb.HasEnvelope) for (int i = 1; i < lim.Points.Count; i++) Line(P(lim.Points[i - 1].x, lim.Points[i - 1].y), P(lim.Points[i].x, lim.Points[i].y), col, 1.5f);
                Vector2 m = P((lim.Points[0].x + lim.Points[lim.Points.Count - 1].x) * 0.5, (lim.Points[0].y + lim.Points[lim.Points.Count - 1].y) * 0.5);
                var st = new GUIStyle(_tiny) { wordWrap = false, normal = { textColor = col } };
                GUI.Label(new Rect(m.x - _fs * 3f, m.y - _fs * (lim.Label == WeightBalance.MaxGrossLineLabel ? 1.1f : 0.5f), _fs * 6f, _fs), lim.Label, st);
            }
            if (!wb.HasEnvelope) GUI.Label(new Rect(plot.x + 3, plot.yMax - _fs * 1.1f, plot.width, _fs), WeightBalance.NoEnvelopeLabel, _tiny);

            Line(P(wb.ZeroFuelX, wb.ZeroFuelY), P(wb.PointX, wb.PointY), new Color(1f, 1f, 1f, 0.6f), 2f);
            Dot(P(wb.ZeroFuelX, wb.ZeroFuelY), _fs * 0.35f, wb.ZeroFuelInside ? new Color(0.8f, 0.8f, 0.8f) : new Color(1f, 0.35f, 0.25f));
            Dot(P(wb.PointX, wb.PointY), _fs * 0.6f, wb.TakeoffInside ? new Color(0.3f, 0.9f, 0.4f) : new Color(1f, 0.35f, 0.25f));

            var warn = new GUIStyle(_tiny) { normal = { textColor = wb.InLimits ? new Color(0.6f, 0.95f, 0.6f) : new Color(1f, 0.7f, 0.3f) } };
            string status = wb.InLimits
                ? $"In limits.  {WeightBalance.TakeoffPointLabel} {wb.TakeoffText}  ·  {WeightBalance.ZeroFuelPointLabel} {wb.ZeroFuelText}"
                : string.Join("  ·  ", wb.Warnings);
            GUI.Label(new Rect(inner.x, inner.yMax - warnH, inner.width, warnH), status, warn);
        }

        /// <summary>LAUNCH out of limits (Ty t881u / t882u): the exact message and Go back | Fly, over the page.</summary>
        private void DrawLaunchConfirm(Rect safe, LandingFlow flow)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);
            float w = Mathf.Min(safe.width * 0.8f, _fs * 30f), h = _bh * 4.2f;
            var box = new Rect(safe.center.x - w * 0.5f, safe.center.y - h * 0.5f, w, h);
            GUI.DrawTexture(box, _panel);
            var inner = Inset(box, _gap);
            var msg = new GUIStyle(_label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(inner.x, inner.y, inner.width, inner.height - _bh * 1.3f), LandingFlow.WbOutOfLimitsMessage, msg);
            float bw = (inner.width - _gap) * 0.5f, by = inner.yMax - _bh * 1.1f;
            if (UiLayout.Button(new Rect(inner.x, by, bw, _bh * 1.1f), LandingFlow.WbGoBackLabel, _btn)) flow.ConfirmLaunch(fly: false);
            if (UiLayout.Button(new Rect(inner.x + bw + _gap, by, bw, _bh * 1.1f), LandingFlow.WbFlyLabel, _btnBig)) flow.ConfirmLaunch(fly: true);
        }

        private void DrawSettings(Rect r, LandingFlow flow)
        {
            // Time of day lights the column itself: the light/dark feedback (the visual lane refines it).
            float light = (float)flow.Light;
            GUI.DrawTexture(r, _panel);
            var c = GUI.color; GUI.color = new Color(1f, 0.92f, 0.7f, 0.18f * light); GUI.DrawTexture(r, _white); GUI.color = c;
            var inner = Inset(r, _gap * 0.6f);
            float y = inner.y, headH = _bh * 0.75f;
            // UNITS (Ty 2026-11-05 10:56): one row of two cards, Standard | Metric, under the conditions.
            float unitsH = _bh * 1.25f;
            float block = (inner.height - 4 * headH - unitsH - _gap * 0.3f) / 3f;
            y = Choices(inner.x, y, inner.width, headH, block, "TIME OF DAY", flow.TimeItems(), id => flow.SetTimeOfDay((TimeOfDay)System.Enum.Parse(typeof(TimeOfDay), id)), timeTint: true);
            y = Choices(inner.x, y, inner.width, headH, block, flow.ConditionsLocked ? "WEATHER  ·  set by the contest" : "WEATHER", flow.WeatherItems(), flow.SetWeather);
            y = Choices(inner.x, y, inner.width, headH, block, "RELIABILITY", flow.ReliabilityItems(), id => flow.SetReliability((Reliability)System.Enum.Parse(typeof(Reliability), id)));
            Choices(inner.x, y, inner.width, headH, unitsH, "UNITS", flow.UnitItems(), id => flow.SetUnits((UnitSystem)System.Enum.Parse(typeof(UnitSystem), id)));
        }

        /// <summary>A heading and a grid of small cards. Returns the next y.</summary>
        private float Choices(float x, float y, float w, float headH, float h, string head, List<PickerItem> items, System.Action<string> pick, bool timeTint = false)
        {
            UiLayout.Label(new Rect(x, y, w, headH), head, _head);
            y += headH;
            int cols = items.Count > 4 ? 3 : 2, rows = Mathf.CeilToInt(items.Count / (float)cols);
            float cw = (w - (cols - 1) * _gap * 0.4f) / cols, ch = (h - (rows - 1) * _gap * 0.4f) / rows;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var cr = new Rect(x + (i % cols) * (cw + _gap * 0.4f), y + (i / cols) * (ch + _gap * 0.4f), cw, ch);
                GUI.DrawTexture(cr, it.Selected ? _cardOnBg : it.Enabled ? _cardBg : _btnBg);
                if (timeTint)
                {
                    double lt = LandingCatalog.TimesOfDay.First(t => t.t.ToString() == it.Id).light;
                    var col = Color.Lerp(new Color(0.15f, 0.12f, 0.30f), new Color(0.55f, 0.78f, 1f), (float)lt);
                    var g = GUI.color; GUI.color = col; GUI.DrawTexture(Inset(cr, it.Selected ? _gap * 0.3f : 1f), _white); GUI.color = g;
                }
                // Dark text on a light card (a selected card, a bright time of day), white on a dark one.
                bool lightCard = (it.Selected && !timeTint) || (timeTint && LandingCatalog.TimesOfDay.First(t => t.t.ToString() == it.Id).light > 0.75);
                var ts = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter, fontStyle = it.Selected ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = lightCard ? new Color(0.1f, 0.1f, 0.1f) : Color.white } };
                UiLayout.Label(new Rect(cr.x, cr.y, cr.width, cr.height * 0.6f), it.Title, ts);
                var ss = new GUIStyle(_tiny) { alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = lightCard ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.85f, 0.88f, 0.92f) } };
                GUI.Label(new Rect(cr.x, cr.y + cr.height * 0.55f, cr.width, cr.height * 0.45f), it.Subtitle, ss);
                if (it.Enabled && GUI.Button(cr, GUIContent.none, GUIStyle.none)) pick(it.Id);
            }
            return y + h + _gap * 0.3f;
        }

        // ================================================================ drawing helpers

        private static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);

        private void Line(Vector2 a, Vector2 b, Color col, float width)
        {
            var m = GUI.matrix; var c = GUI.color;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, len = (b - a).magnitude;
            GUI.color = col;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _white);
            GUI.matrix = m; GUI.color = c;
        }

        private void Dot(Vector2 p, float rad, Color col)
        {
            var c = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(p.x - rad, p.y - rad, 2 * rad, 2 * rad), _white);
            GUI.color = c;
        }
    }
}
