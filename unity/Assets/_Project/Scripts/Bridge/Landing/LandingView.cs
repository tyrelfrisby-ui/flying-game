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
    ///  PAGE 1 — the STACK (Aircraft, Configuration, Location, Start, Viewpoint), the live aeroplane
    ///    (<see cref="LandingPreview"/>) and the ACTIVITIES (Challenges, Lessons). Portrait: stack on top, aeroplane in
    ///    the middle, activities at the bottom. Landscape: stack left, aeroplane middle, activities right (Ty
    ///    2026-11-11). A tile opens its card strip over the page; drag it sideways, tap a card. NEXT.
    ///  PAGE 2 — ONE SCROLLING SURFACE of section cards, each as tall as its own content (Ty 2026-11-11; the long
    ///    comment above <see cref="DrawLoad"/> has the four defects it replaced and what each of them measured): the
    ///    aeroplane, PEOPLE, FUEL, BAGGAGE, WEIGHT &amp; BALANCE, the conditions (time, weather, reliability, units)
    ///    and WHAT LAUNCH WILL DO. One column of them in portrait, two in landscape, three on a tablet or a Mac.
    ///    BACK, LAUNCH.
    /// "More settings" opens the classic chapters (instruments, multiplayer, runway, livery, tow plane, the exact wind):
    /// nothing that page could do is lost.
    ///
    /// TWO RULES ABOUT SPACE, both Ty's, both 2026-11-11:
    ///  * NO PICTURE ON A MENU TILE ("don't show the airplane on every button it takes up too much space"). The hero in
    ///    the middle is the aeroplane; four more thumbnails of the same aeroplane down the side bought nothing and ate
    ///    half of every tile. Where there is no picture there is no PLACEHOLDER either: a challenge has no thumbnail
    ///    and never will, and the grey "F" the old card drew for "Free flight" was a letter the size of a thumb.
    ///  * THE PAGE SCROLLS ("I suspect it is just too low and the list is not scrollable"). Everything is laid out at a
    ///    size that can be read first; if that does not fit, the page scrolls. The old rule — divide the height by the
    ///    number of rows and live with it — never put anything below a fold, but it shrank rows until they could not be
    ///    read, which is the same thing as missing.
    /// </summary>
    public sealed class LandingView
    {
        private readonly StartMenu _host;
        public LandingView(StartMenu host) { _host = host; }

        private GUIStyle _title, _head, _label, _labelBold, _small, _tiny, _btn, _btnOn, _btnBig, _card, _cardOn, _cardOff;
        private Texture2D _bg, _panel, _panelLight, _btnBg, _btnOnBg, _cardBg, _cardOnBg, _white, _dim;
        private int _fs;
        private float _stripScroll, _dragX, _dragScroll; private bool _dragPossible, _dragging;
        // The page's own vertical scroll (see the class notes). _pageContentH is last frame's measurement, which is
        // what the clamp has to go on: IMGUI lays out and draws in one pass, so the height is not known until after.
        private float _pageScroll, _pageContentH, _pageDragY, _pageDragScroll;
        private bool _pageDragPossible, _pageDragging;
        private LandingPage _scrolledPage = LandingPage.Choose;

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
            _labelBold = S(fs, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);   // a card with no picture leads with its name
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

        /// <summary>
        /// ONE READABLE LINE of text, in pixels — the unit every row on this page is a multiple of.
        ///
        /// It is not <c>_fs</c>: <see cref="UiLayout.Label"/> will not draw below <see cref="UiLayout.MinText"/>
        /// (Ty 2026-11-08 asked for bigger menus and that is the floor it put in), so on a phone the text is often
        /// LARGER than the font this page asked for, and a row sized from <c>_fs</c> is a row whose own letters do not
        /// fit in it — they get clipped top and bottom, which reads as a smudge. The 1.35 is the leading that keeps a
        /// descender inside its rectangle.
        /// </summary>
        private float TextLine => Mathf.Max(_fs, UiLayout.MinText) * 1.35f;

        /// <summary>The least a menu tile can be: its padding, plus a caption line, a name line and a line of what
        /// that name means (0.85 + 1.0 + 0.85 of <see cref="TextLine"/>). <see cref="DrawTile"/> splits whatever
        /// height it is given in those same proportions.</summary>
        private float TileMin => 2 * (_gap * 0.55f) + TextLine * 2.7f;

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
            DrawBody(body, flow);
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
            // The three small buttons SPEND sbw * 3.25 plus a gap (More settings is 1.25 wide, and there are two
            // half-gaps between them), so that is what the budget has to be. It used to divide the space by 3.2 and
            // reserve nothing for the gap, which on page 2 in portrait — where BACK has already taken sw off the left
            // — asked for 643 px of the 617 px that was free and drew CHART across the left 26 px of LAUNCH. CHART is
            // drawn last, so it took the tap there: the bottom-right button on the page was partly covered by another
            // one. Page 1 never showed it, because with no BACK the row fits with 40 px to spare.
            float small = r.height * 0.62f, sbw = Mathf.Min(_fs * 7.5f, (r.xMax - bw - _gap * 2f - x0) / 3.25f);
            if (UiLayout.Button(new Rect(x0, r.yMax - small, sbw * 1.25f, small), "More settings", _btn)) _host.ShowClassic(null);
            if (UiLayout.Button(new Rect(x0 + sbw * 1.25f + _gap * 0.5f, r.yMax - small, sbw, small), "HELP", _btn)) SupportDesk.OpenHelp();
            if (UiLayout.Button(new Rect(x0 + sbw * 2.25f + _gap, r.yMax - small, sbw, small), "CHART", _btn)) ChartPanel.Toggle();
        }

        // ================================================================ the body, and the scroll it sits in

        /// <summary>
        /// Whichever page is showing, inside a scroll viewport. The page lays itself out as if it had the viewport's
        /// height and reports what it actually used; anything over that scrolls, with a drag or the wheel.
        ///
        /// The scroll input is read BEFORE the group, in screen coordinates, because <see cref="GUI.BeginGroup"/>
        /// moves the GUI matrix and with it <c>Event.current.mousePosition</c>. It is read only while the page itself
        /// is live: with a menu strip or the weight-and-balance question over the page, the finger belongs to them.
        /// </summary>
        private void DrawBody(Rect r, LandingFlow flow)
        {
            // PAGE 2 BRINGS ITS OWN SCROLL (<see cref="DrawLoad"/>: one surface of section cards, with its own
            // viewport and its own MORE ABOVE / MORE BELOW), so it is drawn straight into the body rect. Putting it
            // inside this viewport as well would scroll it twice, and worse: this viewport is a GUI.BeginGroup, which
            // moves the GUI matrix and with it Event.current.mousePosition, so page 2's drag would be measured
            // against a rect that is no longer where the finger is. The outer scroll stays for page 1, which has no
            // scroll of its own and reports the height it used.
            if (flow.Page == LandingPage.Load)
            {
                _pageDragPossible = false; _pageDragging = false;
                _scrolledPage = flow.Page; _pageScroll = 0f; _pageContentH = r.height;
                DrawLoad(r, flow);
                return;
            }
            if (_scrolledPage != flow.Page) { _scrolledPage = flow.Page; _pageScroll = 0f; _pageContentH = r.height; }
            // A menu opening mid-drag would otherwise leave the drag latched (its MouseUp goes to the strip, not to
            // us) and the next tap on a tile would be swallowed as the end of a drag that finished long ago.
            if (GUI.enabled) PageScrollInput(r); else { _pageDragPossible = false; _pageDragging = false; }
            float over = Mathf.Max(0f, _pageContentH - r.height);
            _pageScroll = Mathf.Clamp(_pageScroll, 0f, over);
            GUI.BeginGroup(r);
            var inner = new Rect(0f, -_pageScroll, r.width, r.height);
            _pageContentH = DrawChoose(inner, flow);
            GUI.EndGroup();
            if (over <= 1f) return;
            // A bar only when there IS something below the fold, so the page never claims to scroll when it does not.
            float frac = r.height / _pageContentH, thumbH = Mathf.Max(_bh * 0.8f, r.height * frac);
            float thumbY = r.y + (r.height - thumbH) * (_pageScroll / over);
            float bw = _gap * 0.35f;
            GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), _btnBg);
            GUI.DrawTexture(new Rect(r.xMax - bw, thumbY, bw, thumbH), _btnOnBg);
        }

        /// <summary>Drag / wheel scrolling, the same handling as the classic chapters (StartMenu.ScrollInput): a drag
        /// past a few pixels scrolls and swallows the release, so the tile under the finger does not fire; a tap still
        /// presses it.</summary>
        private void PageScrollInput(Rect viewport)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (viewport.Contains(e.mousePosition)) { _pageScroll += e.delta.y * _bh * 0.6f; e.Use(); }
                    break;
                case EventType.MouseDown:
                    if (viewport.Contains(e.mousePosition)) { _pageDragPossible = true; _pageDragging = false; _pageDragY = e.mousePosition.y; _pageDragScroll = _pageScroll; }
                    break;
                case EventType.MouseDrag:
                    if (_pageDragPossible)
                    {
                        float dy = e.mousePosition.y - _pageDragY;
                        if (!_pageDragging && Mathf.Abs(dy) > _bh * 0.35f) _pageDragging = true;
                        if (_pageDragging) { _pageScroll = _pageDragScroll - dy; e.Use(); }
                    }
                    break;
                case EventType.MouseUp:
                    if (_pageDragging) { e.Use(); GUIUtility.hotControl = 0; }
                    _pageDragPossible = false; _pageDragging = false;
                    break;
            }
        }

        // ================================================================ page 1

        /// <summary>The five menus of the stack, in the order they are read. START is where the flight begins (it wore
        /// the label VIEWPOINT until 2026-11-11 while it offered "On the runway"); VIEWPOINT is the camera.</summary>
        private static readonly (PickerKind kind, string head)[] Stack =
        {
            (PickerKind.Aircraft, "AIRCRAFT"), (PickerKind.Configuration, "CONFIGURATION"), (PickerKind.Location, "LOCATION"),
            (PickerKind.Start, "START"), (PickerKind.CameraView, "VIEWPOINT"),
        };

        /// <summary>
        /// Page 1 (Ty 2026-11-11, verbatim): "put the current left stack at the top in vertical or left landscape and
        /// the challenges or lessons at the bottom with the airplane in the middle. On landscape airplane middle
        /// challenge and lessons to the right."
        ///
        ///   PORTRAIT     stack (5 rows, full width) / aeroplane / Challenges | Lessons / the activity's note
        ///   LANDSCAPE    stack | aeroplane | Challenges, Lessons, the note
        ///
        /// The tiles and the activity pair take a readable height first and the aeroplane takes what is left, with a
        /// floor under it; where that floor bites, the page scrolls rather than squeezing the rows. That is the right
        /// way round — a letterboxed aeroplane is a smaller loss than a row of words too short for its own letters.
        /// Returns the height the page used.
        /// </summary>
        private float DrawChoose(Rect r, LandingFlow flow)
        {
            bool portrait = Screen.height > Screen.width;
            float used;
            if (portrait)
            {
                float tileH = TileMin;
                float stackH = Stack.Length * tileH + (Stack.Length - 1) * _gap;
                float actH = tileH * 1.15f, noteH = TextLine * 3.4f;
                float bottomH = actH + _gap + noteH;
                // The aeroplane gets what is left, down to a floor: below about four lines it stops being a picture of
                // an aeroplane and becomes a letterbox with a title band in it. Past that floor the page scrolls.
                float heroH = Mathf.Max(TextLine * 4.5f, r.height - stackH - bottomH - 2 * _gap);

                float y = r.y;
                for (int i = 0; i < Stack.Length; i++) DrawTile(new Rect(r.x, y + i * (tileH + _gap), r.width, tileH), Stack[i].head, Stack[i].kind, flow);
                y += stackH + _gap;
                DrawHero(new Rect(r.x, y, r.width, heroH), flow, true);
                y += heroH + _gap;
                float half = (r.width - _gap) * 0.5f;
                DrawTile(new Rect(r.x, y, half, actH), "CHALLENGES", PickerKind.Challenge, flow);
                DrawTile(new Rect(r.x + half + _gap, y, half, actH), "LESSONS", PickerKind.Lesson, flow);
                y += actH + _gap;
                DrawActivityNote(new Rect(r.x, y, r.width, noteH), flow);
                used = stackH + heroH + bottomH + 2 * _gap;   // stack · gap · hero · gap · (activities, which hold the third)
            }
            else
            {
                float lw = r.width * 0.27f, rw = r.width * 0.25f;
                // Stretch the tiles to fill the column, but never past a comfortable thumb and never under the three
                // readable lines TileMin is; whichever column needs more than the viewport is what makes the page scroll.
                float tileH = Mathf.Clamp((r.height - (Stack.Length - 1) * _gap) / Stack.Length, TileMin, TileMin * 1.6f);
                float stackH = Stack.Length * tileH + (Stack.Length - 1) * _gap;
                float actH = Mathf.Clamp(r.height * 0.26f, TileMin, TileMin * 1.8f);
                float noteH = Mathf.Max(TextLine * 3.4f, r.height - 2 * actH - 2 * _gap);
                float rightH = 2 * actH + noteH + 2 * _gap;
                float colH = Mathf.Max(r.height, Mathf.Max(stackH, rightH));

                for (int i = 0; i < Stack.Length; i++) DrawTile(new Rect(r.x, r.y + i * (tileH + _gap), lw, tileH), Stack[i].head, Stack[i].kind, flow);
                DrawHero(new Rect(r.x + lw + _gap, r.y, r.width - lw - rw - 2 * _gap, colH), flow, true);
                float rx = r.xMax - rw;
                DrawTile(new Rect(rx, r.y, rw, actH), "CHALLENGES", PickerKind.Challenge, flow);
                DrawTile(new Rect(rx, r.y + actH + _gap, rw, actH), "LESSONS", PickerKind.Lesson, flow);
                DrawActivityNote(new Rect(rx, r.y + 2 * (actH + _gap), rw, noteH), flow);
                used = colH;
            }
            return used;
        }

        /// <summary>What the chosen challenge or lesson restricts, and its own options where it has any.</summary>
        private void DrawActivityNote(Rect r, LandingFlow flow)
        {
            GUI.DrawTexture(r, _panel);
            var inner = Inset(r, _gap * 0.6f);
            var rules = flow.Rules;
            string text = flow.Activity == null ? "Free flight: every aeroplane, every place, every start."
                : string.Join("\n", new[] { flow.Activity.Name, rules.AircraftNote, rules.FixedViewpointNote, rules.ConditionsLocked ? "Scored: the conditions are the contest's" : null }.Where(x => x != null));
            bool hasOptions = flow.ActivityKind == ActivityKind.Lesson || flow.ActivityId == "event:dogfight";
            float btnH = hasOptions ? _bh * 0.85f : 0f;
            GUI.Label(new Rect(inner.x, inner.y, inner.width, Mathf.Max(0f, inner.height - btnH)), text, _tiny);
            if (hasOptions && UiLayout.Button(new Rect(inner.x, inner.yMax - btnH, inner.width, btnH), flow.ActivityKind == ActivityKind.Lesson ? "Lesson options" : "Opponent", _btn))
                _host.ShowClassic("Lessons");
        }

        /// <summary>
        /// One menu tile: its heading, the current card's name and what that card is. NO PICTURE — see the class
        /// notes. Three lines of words need about half the height the old picture tile did (the picture was 16:9 and
        /// set the whole tile's height), which is what makes five menus fit where four did.
        ///
        /// ▼ says the tile opens a menu. A tile with nothing to choose (a glider's one configuration, the start a
        /// lesson sets) has no ▼ and does not answer a tap: it is a line that reports, not a button that lies.
        /// </summary>
        private void DrawTile(Rect r, string head, PickerKind kind, LandingFlow flow)
        {
            bool fixedOne = flow.IsFixed(kind);
            GUI.DrawTexture(r, flow.OpenPicker == kind ? _btnOnBg : _panel);
            var items = flow.Items(kind);
            var cur = items.FirstOrDefault(i => i.Selected) ?? items.FirstOrDefault();
            var inner = Inset(r, _gap * 0.55f);
            float caret = fixedOne ? 0f : TextLine * 0.9f;
            // 0.85 : 1.0 : 0.85 of a line — the same split TileMin is built from, so the tile never promises a row
            // less height than its letters need, however much or little the column gives it.
            float headH = inner.height * (0.85f / 2.7f), rest = inner.height - headH;
            UiLayout.Label(new Rect(inner.x, inner.y, inner.width - caret, headH), head, _head);
            if (!fixedOne) UiLayout.Label(new Rect(inner.xMax - caret, inner.y, caret, headH), "▼", _head);
            UiLayout.Label(new Rect(inner.x, inner.y + headH, inner.width, rest * (1f / 1.85f)), cur?.Title ?? "-", _label);
            string sub = fixedOne && kind == PickerKind.Start && flow.Rules.FixedViewpointNote != null ? "set by the activity" : cur?.Subtitle ?? "";
            UiLayout.Label(new Rect(inner.x, inner.yMax - rest * (0.85f / 1.85f), inner.width, rest * (0.85f / 1.85f)), sub, _small);
            if (!fixedOne && !_pageDragging && GUI.Button(r, GUIContent.none, GUIStyle.none)) { flow.Open(kind); _stripScroll = 0f; ScrollToSelected(flow); }
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

        /// <summary>The card's picture if there is one, and NOTHING if there is not (Ty 2026-11-11: "remove the giant
        /// 'f' with challenges or lessons"). There used to be a placeholder here — a half-transparent capital of the
        /// card's first letter, filling the picture box — on the assumption that every card would get a thumbnail
        /// eventually. Aircraft and locations did; challenges, lessons and camera views never will, because there is
        /// nothing to photograph, so the placeholder was permanent and a letter the size of a thumb said nothing the
        /// card's own title did not. Returns false when the caller should give the whole card to its words.</summary>
        private static bool DrawPicture(Rect r, PickerItem item)
        {
            Texture2D t = LandingThumbnails.Get(item);
            if (t == null) return false;
            GUI.DrawTexture(r, t, ScaleMode.ScaleAndCrop);
            return true;
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
                PickerKind.Configuration => "CONFIGURATION  ·  " + flow.Family.Name, PickerKind.Location => "LOCATION  ·  seen from your start",
                PickerKind.Start => "START  ·  where the flight begins",
                PickerKind.CameraView => "VIEWPOINT  ·  the camera you fly behind",
                PickerKind.Challenge => "CHALLENGES", PickerKind.Lesson => "LESSONS", _ => "",
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

        /// <summary>A card: picture on top where there is one, title and subtitle under it; the selected card is
        /// outlined in the accent. A card with no picture (a challenge, a lesson, a camera view) is all words, and the
        /// title gets the size the picture would have had. Returns true when tapped.</summary>
        private bool DrawCard(Rect r, PickerItem it)
        {
            GUI.Box(r, GUIContent.none, it.Selected ? _cardOn : it.Enabled ? _card : _cardOff);
            var inner = Inset(r, it.Selected ? _gap * 0.35f : 2f);
            GUI.DrawTexture(inner, _cardBg);
            float picH = Mathf.Min(inner.height * 0.66f, inner.width * 9f / 16f);
            if (!DrawPicture(new Rect(inner.x, inner.y, inner.width, picH), it)) picH = 0f;
            var c = GUI.color; if (!it.Enabled) GUI.color = new Color(1, 1, 1, 0.45f);
            float ty = inner.y + picH + _gap * 0.3f, rest = inner.yMax - ty;
            float titleH = picH > 0f ? Mathf.Min(_bh * 0.75f, rest * 0.5f) : Mathf.Min(_bh * 1.1f, rest * 0.35f);
            var titleStyle = picH > 0f ? _label : _labelBold;
            UiLayout.Label(new Rect(inner.x + _gap * 0.4f, ty, inner.width - _gap * 0.8f, titleH), it.Title, titleStyle);
            GUI.Label(new Rect(inner.x + _gap * 0.4f, ty + titleH, inner.width - _gap * 0.8f, rest - titleH), it.Enabled ? it.Subtitle : it.DisabledReason ?? "", _tiny);
            GUI.color = c;
            return it.Enabled && !_dragging && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ================================================================ page 2 — LOAD AND LAUNCH
        //
        // Ty 2026-11-11: "Page 2 of the page is not scrollable either and needs a facelift too there is dead area
        // bottom left and hidden ui bottom right and the text is unreadable give it an overhaul and let me know what
        // you come up with all ui accessible and easy to read."
        //
        // What the four complaints turned out to be, measured at the phone shape the self-test shoots (iPhone 15/16
        // Pro class, 1179 x 2556 px of 460 ppi glass, 3 px to the point) before anything was changed:
        //
        //  NOT SCROLLABLE — true, and structural rather than a missing feature. The old DrawLoad cut `body` into three
        //    columns by FRACTION and placed every row inside them by fraction, so there was never any content to
        //    scroll: whatever did not fit was simply not drawn. Two lines did that silently — FUEL only appeared
        //    `if (fuel.Count > 0 && y + row * 2 < inner.yMax)` and baggage stations ran `if (y + row > inner.yMax)
        //    break;`. A type with enough seats lost its tanks and its bags with no mark on the page to say so.
        //
        //  DEAD AREA BOTTOM LEFT — true, and the largest single defect on the page. The PEOPLE / FUEL / BAGGAGE column
        //    was given the full height of the body (1323 px in portrait) while a J-3's content stacks to 432 px of it.
        //    881 px left over: 294 pt, 1.9 INCHES of empty navy panel, 68 % of that column. In landscape it was 460 px
        //    (52 %). The cause is the fraction: a column sized by the screen and filled from the top.
        //
        //  HIDDEN UI BOTTOM RIGHT — two separate things, both real, neither of them off the screen.
        //    (1) The settings column's budget line, `block = (inner.height - 4 * headH - unitsH - _gap * 0.3f) / 3f`,
        //        subtracted ONE inter-group gap where the layout then spends three, so the stack overran its own panel
        //        by exactly 0.6 x gap = 10 px and the last control on the page — UNITS, Standard | Metric — ended up
        //        jammed on the body's bottom edge, 78 px = 26 pt tall against Apple's 44 pt floor.
        //    (2) In PORTRAIT the footer's three small buttons want 643 px where 617 px is free, so CHART is drawn
        //        across the left 26 px of LAUNCH and, being drawn later, wins the tap there. The bottom-right control
        //        on the page is partly covered by another button. Fixed in DrawFooter (shared with page 1, where the
        //        same row fits with 40 px to spare because page 1 has no BACK button to pay for).
        //
        //  TEXT UNREADABLE — the cause is SIZE, and it is not the dark theme. Measured contrast on the navy is 16.2:1
        //    for body text, 11.2:1 for captions and 7.7:1 for the numbers on the weight-and-balance plot: all far past
        //    WCAG AA's 4.5:1. What is wrong is that EnsureStyles takes 2.6 % of the short side IN PIXELS, which is
        //    31 px = 10.3 pt here, and the derived styles land at _small 25 px = 8.3 pt and _tiny 21 px = 7.0 pt.
        //    _tiny carries every number on the W&B plot, every settings-card subtitle and both of the page's notes,
        //    and all of them are drawn with a raw GUI.Label, so nothing floors them. SEVEN POINTS, where Apple's
        //    minimum is 11 pt and body text is 17 pt, and where docs/landing-v2/DESIGN.md asked for "16-point body
        //    text ... compact secondary captions in these boards are 11-14 points". This is the same defect UiLayout
        //    already documents (Ty 2026-11-08, "the size of the menus and buttons on iPhone is too small"): a fraction
        //    of a pixel count says nothing about how big a thing is in the hand.
        //    Contrast WAS the fault in exactly two places, and both are fixed below. White on the Morning time-of-day
        //    tint measures 3.10:1 (its subtitle 2.34:1) because the ink was picked by a hand-set `light > 0.75` rule
        //    and Morning is 0.70. White on the green "this is my seat" fill measures 3.40:1.
        //
        // The overhaul. One scrolling surface — thumb drag, MORE ABOVE / MORE BELOW, the idiom OptionsPanel already
        // uses — built out of section cards that are as tall as THEIR CONTENT instead of columns that are a fraction
        // of the screen. One column of sections in portrait, two in landscape, three on a tablet or a Mac. Running out
        // of room now means scrolling, so nothing is dropped for want of it, and a short list no longer leaves a foot
        // of empty panel under it. Page 2 sizes its own text from UiLayout.PanelUnit (see EnsureLoadStyles).
        //
        // DELIBERATELY NOT TOUCHED: EnsureStyles, DrawHeader, DrawChoose, DrawTile, DrawStrip, DrawCard. Another lane
        // is overhauling page 1 at the same time, and the one-line PanelUnit change belongs there, in its hands.

        private GUIStyle _p2Title, _p2Head, _p2Label, _p2Small, _p2Body, _p2Tiny, _p2Btn, _p2BtnOn, _p2More;
        private Texture2D _p2Accent, _p2MoreBg;
        private int _p2Fs;
        private float _p2Bh, _p2Gap, _p2Pad;
        private Vector2 _loadScroll;
        private float _loadContentH;
        private bool _loadDragPossible, _loadDragging;
        private float _loadDragY, _loadDragScroll;

        /// <summary>
        /// Page 2's text and row sizes. These come from <see cref="UiLayout.PanelUnit"/> — the screen's short side
        /// times AutoScale, the DPI-derived "how big is this in the hand" factor (1.89 on the phone above, 1.00 on an
        /// iPad, where the original fractions were chosen and are right), times the owner's OPTIONS → Display → "Menu
        /// &amp; page size" slider. Not from Screen's raw pixel count the way <see cref="EnsureStyles"/> does it.
        ///
        /// Two things follow. The page becomes legible: fs 58 px = 19.3 pt body, 50 px = 16.6 pt card titles, 43 px =
        /// 14.3 pt captions, rows 138 px = 46 pt against Apple's 44 pt target — the sizes DESIGN.md asked for. And the
        /// OPTIONS size slider now MOVES page 2, which it never did: that slider only ever changed PanelUnit, and this
        /// page did not read it, so turning it up did nothing here and there was no way to fix the page from the
        /// device.
        /// </summary>
        private void EnsureLoadStyles()
        {
            int fs = Mathf.Max(13, Mathf.RoundToInt(UiLayout.PanelUnit * 0.026f));
            if (_p2Label != null && fs == _p2Fs) return;
            _p2Fs = fs;
            _p2Gap = UiLayout.PanelUnit * 0.014f;
            _p2Pad = _p2Gap * 0.6f;
            // A row is never smaller than a finger: UiLayout's own button fraction (6.2 % of the unit), floored
            // against the text so that a large OPTIONS scale can never print a 19 pt label into a 15 pt row.
            _p2Bh = Mathf.Max(fs * 1.9f, UiLayout.PanelUnit * 0.062f);
            // White on this green measures 8.6:1. White on the page's existing accent (0.20, 0.62, 0.35) measures
            // 3.4:1, which is what the "You" seat row was drawing text on. The hue is kept; only the value moves.
            _p2Accent ??= Solid(new Color(0.10f, 0.34f, 0.20f, 0.98f));
            _p2MoreBg ??= Solid(new Color(0.10f, 0.42f, 0.62f, 0.96f));
            Font f = UiFont.Get();
            GUIStyle S(int size, FontStyle st, TextAnchor a, Color c) => new GUIStyle { font = f, fontSize = size, fontStyle = st, alignment = a, normal = { textColor = c }, wordWrap = false, clipping = TextClipping.Clip };
            var caption = new Color(0.84f, 0.88f, 0.93f);
            _p2Title = S(Mathf.RoundToInt(fs * 1.15f), FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            _p2Head = S(Mathf.RoundToInt(fs * 0.92f), FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.55f, 0.95f, 1f));
            _p2Label = S(fs, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            _p2Small = S(Mathf.RoundToInt(fs * 0.86f), FontStyle.Normal, TextAnchor.MiddleLeft, caption);
            // Sentences WRAP. UiLayout.Label drops the tail of a line that will not fit, which is right for a status
            // line and wrong for a sentence, where the words are the whole point (OptionsPanel learnt the same thing:
            // Ty 2026-11-08, "I can't read this"). Anything drawn with these two goes through LabelWrapped.
            _p2Body = S(Mathf.RoundToInt(fs * 0.86f), FontStyle.Normal, TextAnchor.UpperLeft, caption);
            _p2Body.wordWrap = true;
            _p2Tiny = S(Mathf.RoundToInt(fs * 0.74f), FontStyle.Normal, TextAnchor.UpperLeft, caption);
            _p2Tiny.wordWrap = true;
            _p2Btn = new GUIStyle(_p2Label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(8, 8, 4, 4) };
            _p2BtnOn = new GUIStyle(_p2Btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _p2Accent }, active = { textColor = Color.white, background = _p2Accent } };
            _p2More = new GUIStyle(_p2Small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _p2MoreBg } };
        }

        /// <summary>
        /// The whole page in one scrolling viewport. The sections are laid out as independent vertical stacks, one per
        /// column, and the scroll runs over all of them together.
        ///
        /// The column assignment is fixed rather than packed by height, because a reader needs the loading and its
        /// consequence next to each other: wherever there are two columns the aeroplane, the people, the fuel, the
        /// bags and the weight-and-balance plot are ONE stack (what you loaded, and what it did), and the conditions
        /// and the summary are the other. Measured at the phone shapes that leaves about 600 px of imbalance in
        /// landscape, which is a sixth of a column rather than the two-thirds of one the old layout wasted, and it is
        /// at the very bottom of a page that scrolls instead of in the middle of the page you first see.
        /// </summary>
        private void DrawLoad(Rect r, LandingFlow flow)
        {
            EnsureLoadStyles();
            bool portrait = Screen.height > Screen.width;
            int cols = portrait ? 1 : r.width > _p2Bh * 20f ? 3 : 2;

            // The content height is the one measured on the PREVIOUS pass. IMGUI wants the content rect before the
            // sections have had a chance to say how tall they are, and the alternative — a silent dry run of every
            // section — doubles the code and can drift from what is actually drawn. The cost is one frame of wrong
            // scroll extent when the page opens or the aeroplane changes, which is the trade OptionsPanel makes too.
            float hidden = Mathf.Max(0f, _loadContentH - r.height);
            LoadScrollInput(r, hidden);
            _loadScroll.y = Mathf.Clamp(_loadScroll.y, 0f, hidden);
            float sbw = hidden > 1f ? _p2Fs * 1.1f : 0f;
            var content = new Rect(r.x, r.y, r.width - sbw, Mathf.Max(_loadContentH, r.height));
            _loadScroll = GUI.BeginScrollView(r, _loadScroll, content);

            float colW = (content.width - (cols - 1) * _p2Gap) / cols;
            float X(int i) => content.x + i * (colW + _p2Gap);
            float yA = content.y, yB = content.y, yC = content.y;
            yA = SecHero(X(0), yA, colW, flow);
            yA = SecPeople(X(0), yA, colW, flow);
            yA = SecFuel(X(0), yA, colW, flow);
            yA = SecBaggage(X(0), yA, colW, flow);
            if (cols == 1)
            {
                yA = SecWeightBalance(X(0), yA, colW, flow);
                yA = SecConditions(X(0), yA, colW, flow);
                yA = SecSummary(X(0), yA, colW, flow);
            }
            else if (cols == 2)
            {
                yA = SecWeightBalance(X(0), yA, colW, flow);
                yB = SecConditions(X(1), yB, colW, flow);
                yB = SecSummary(X(1), yB, colW, flow);
            }
            else
            {
                yB = SecWeightBalance(X(1), yB, colW, flow);
                yB = SecSummary(X(1), yB, colW, flow);
                yC = SecConditions(X(2), yC, colW, flow);
            }
            GUI.EndScrollView();
            _loadContentH = Mathf.Max(yA, Mathf.Max(yB, yC)) - content.y;

            // MORE ABOVE / MORE BELOW, in words and across the edge the content runs off (Ty 2026-11-08: "an obvious
            // 'more below/above' when not at the top/bottom of a scrollable menu"). A three-pixel scrollbar at the
            // edge of a moving list is not something a thumb finds on a phone.
            if (hidden > 1f)
            {
                if (_loadScroll.y > 1f) GUI.Label(new Rect(r.x, r.y, r.width - sbw, _p2Bh * 0.62f), "▲  MORE ABOVE", _p2More);
                if (_loadScroll.y < hidden - 1f) GUI.Label(new Rect(r.x, r.yMax - _p2Bh * 0.62f, r.width - sbw, _p2Bh * 0.62f), "▼  MORE BELOW", _p2More);
            }
        }

        /// <summary>Wheel and thumb-drag scrolling for page 2. A drag only becomes a scroll past a third of a row, so
        /// a tap on a seat or a − / + still lands; once it is a scroll the MouseUp is eaten and hotControl cleared, so
        /// the button under the finger does not also fire. Same shape as OptionsPanel's.</summary>
        private void LoadScrollInput(Rect viewport, float hidden)
        {
            // GUI.enabled is false while the out-of-limits question is up: the page behind a modal does not scroll
            // under the finger that is reaching for Go back.
            if (hidden <= 1f || !GUI.enabled) { _loadDragPossible = _loadDragging = false; return; }
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (viewport.Contains(e.mousePosition)) { _loadScroll.y = Mathf.Clamp(_loadScroll.y + e.delta.y * _p2Bh * 0.5f, 0f, hidden); e.Use(); }
                    break;
                case EventType.MouseDown:
                    if (viewport.Contains(e.mousePosition)) { _loadDragPossible = true; _loadDragging = false; _loadDragY = e.mousePosition.y; _loadDragScroll = _loadScroll.y; }
                    break;
                case EventType.MouseDrag:
                    if (_loadDragPossible)
                    {
                        float dy = e.mousePosition.y - _loadDragY;
                        if (!_loadDragging && Mathf.Abs(dy) > _p2Bh * 0.33f) _loadDragging = true;
                        if (_loadDragging) { _loadScroll.y = Mathf.Clamp(_loadDragScroll - dy, 0f, hidden); e.Use(); }
                    }
                    break;
                case EventType.MouseUp:
                    if (_loadDragging) { e.Use(); GUIUtility.hotControl = 0; }
                    _loadDragPossible = false; _loadDragging = false;
                    break;
            }
        }

        /// <summary>A section card: its panel, its heading if it has one, and the inner rect its rows go in. Every
        /// section works out its own height from its own row count first, draws this, and then fills it — which is the
        /// whole of the fix for the dead area: a card is as tall as what is in it.</summary>
        private Rect SecFrame(float x, float y, float w, float h, string head)
        {
            var box = new Rect(x, y, w, h);
            GUI.DrawTexture(box, _panel);
            var inner = Inset(box, _p2Pad);
            if (head != null) UiLayout.Label(new Rect(inner.x, inner.y, inner.width, _p2Bh * 0.8f), head, _p2Head);
            return inner;
        }

        /// <summary>The live aeroplane and what it is: the same preview page 1 shows, in a strip, with the name under
        /// it. DrawHero also sets <see cref="LandingPreview.Framing"/>, so page 2 has to go through it.</summary>
        private float SecHero(float x, float y, float w, LandingFlow flow)
        {
            float inW = w - 2 * _p2Pad;
            float picH = Mathf.Min(inW * 0.42f, _p2Bh * 4.5f);
            float h = 2 * _p2Pad + picH + _p2Gap * 0.4f + _p2Bh * 0.78f + _p2Bh * 0.62f;
            var inner = SecFrame(x, y, w, h, null);
            DrawHero(new Rect(inner.x, inner.y, inner.width, picH), flow, false);
            float ty = inner.y + picH + _p2Gap * 0.4f;
            UiLayout.Label(new Rect(inner.x, ty, inner.width, _p2Bh * 0.78f), flow.HeroTitle, _p2Title);
            UiLayout.Label(new Rect(inner.x, ty + _p2Bh * 0.78f, inner.width, _p2Bh * 0.62f), flow.HeroSubtitle, _p2Small);
            return y + h + _p2Gap;
        }

        private float SecPeople(float x, float y, float w, LandingFlow flow)
        {
            float inW = w - 2 * _p2Pad;
            bool shared = flow.Seats.Count > 1 && flow.Seats.Any(s => s.HasControls && s.Occupant != Occupant.Me);
            string note = "Tap a seat to change who is in it; − and + set what they weigh."
                        + (shared ? "  An invited player can ride along or take the other set of controls." : "");
            float noteH = UiLayout.WrappedHeight(_p2Tiny, note, inW);
            float h = 2 * _p2Pad + _p2Bh * 0.8f + flow.Seats.Count * (_p2Bh + _p2Gap * 0.35f) + _p2Gap * 0.2f + noteH;
            var inner = SecFrame(x, y, w, h, "PEOPLE");
            float yy = inner.y + _p2Bh * 0.8f, step = _p2Bh;
            foreach (var s in flow.Seats)
            {
                string who = s.Occupant switch
                {
                    Occupant.Me => "You",
                    Occupant.DefaultPerson => "Passenger",
                    Occupant.InvitedPlayer => (s.InviteName ?? "Friend") + (s.Role == SeatRole.DualControl ? " · flying" : " · riding"),
                    _ => "Empty",
                };
                bool mine = s.Occupant == Occupant.Me;
                var seatR = new Rect(inner.x, yy, inner.width - 2 * step - _p2Gap * 0.4f, _p2Bh);
                GUI.DrawTexture(seatR, mine ? _p2Accent : s.Occupant == Occupant.Empty ? _btnBg : _cardBg);
                // The seat's own name, short ("Rear seat (solo)" → "Rear"), then who sits there.
                UiLayout.Label(new Rect(seatR.x + _p2Gap * 0.4f, seatR.y, seatR.width * 0.30f, seatR.height), s.Label.Split(' ')[0], _p2Small);
                UiLayout.Label(new Rect(seatR.x + seatR.width * 0.32f, seatR.y, seatR.width * 0.68f - _p2Gap * 0.4f, seatR.height),
                               s.Occupant == Occupant.Empty ? who : $"{who}   {flow.MassText(s.Kg)}", _p2Label);
                if (!mine && GUI.Button(seatR, GUIContent.none, GUIStyle.none)) flow.CycleSeat(s.SeatId);
                if (s.Occupant != Occupant.Empty)
                {
                    if (UiLayout.Button(new Rect(seatR.xMax + _p2Gap * 0.4f, yy, step, _p2Bh), "−", _p2Btn)) flow.SetSeatKg(s.SeatId, s.Kg - flow.MassStepKg);
                    if (UiLayout.Button(new Rect(seatR.xMax + _p2Gap * 0.4f + step, yy, step, _p2Bh), "+", _p2Btn)) flow.SetSeatKg(s.SeatId, s.Kg + flow.MassStepKg);
                }
                yy += _p2Bh + _p2Gap * 0.35f;
            }
            UiLayout.LabelWrapped(new Rect(inner.x, yy + _p2Gap * 0.2f, inner.width, noteH), note, _p2Tiny);
            return y + h + _p2Gap;
        }

        private float SecFuel(float x, float y, float w, LandingFlow flow)
        {
            // No tanks (every glider) means no section at all, rather than the old silent "only if it happens to fit".
            if (!flow.Stations.Any(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Fuel)) return y;
            float h = 2 * _p2Pad + _p2Bh * 0.8f + _p2Bh + _p2Gap * 0.4f + _p2Bh * 0.34f;
            var inner = SecFrame(x, y, w, h, $"FUEL  ·  {flow.FuelFraction * 100:0} %");
            float yy = inner.y + _p2Bh * 0.8f;
            float q = (inner.width - 3 * _p2Gap * 0.4f) / 4f;
            (string n, double f)[] presets = { ("Empty", 0), ("¼", 0.25), ("Tabs", 0.5), ("Full", 1) };
            for (int i = 0; i < 4; i++)
                if (UiLayout.Button(new Rect(inner.x + i * (q + _p2Gap * 0.4f), yy, q, _p2Bh), presets[i].n,
                                    System.Math.Abs(flow.FuelFraction - presets[i].f) < 0.02 ? _p2BtnOn : _p2Btn)) flow.SetFuelFraction(presets[i].f);
            yy += _p2Bh + _p2Gap * 0.4f;
            var bar = new Rect(inner.x, yy, inner.width, _p2Bh * 0.34f);
            GUI.DrawTexture(bar, _btnBg);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (float)flow.FuelFraction, bar.height), _p2Accent);
            return y + h + _p2Gap;
        }

        private float SecBaggage(float x, float y, float w, LandingFlow flow)
        {
            var bags = flow.Stations.Where(s => s.Kind == FlyingGame.Core.AircraftLoad.StationKind.Baggage).ToList();
            float inW = w - 2 * _p2Pad;
            if (bags.Count == 0)
            {
                // A type with no authored stations says so once, in a sentence, instead of leaving the reader to guess
                // why there is nothing to load. A type that HAS stations but no baggage simply has no section.
                if (flow.Load != null && flow.Load.Slots.Count > 0) return y;
                const string none = "This type has no loading stations authored yet: it flies at its authored weight and balance.";
                float nh = UiLayout.WrappedHeight(_p2Body, none, inW);
                float h0 = 2 * _p2Pad + _p2Bh * 0.8f + nh;
                var i0 = SecFrame(x, y, w, h0, "LOADING");
                UiLayout.LabelWrapped(new Rect(i0.x, i0.y + _p2Bh * 0.8f, i0.width, nh), none, _p2Body);
                return y + h0 + _p2Gap;
            }
            float h = 2 * _p2Pad + _p2Bh * 0.8f + bags.Count * (_p2Bh + _p2Gap * 0.35f);
            var inner = SecFrame(x, y, w, h, "BAGGAGE");
            float yy = inner.y + _p2Bh * 0.8f, step = _p2Bh;
            foreach (var b in bags)
            {
                var rowR = new Rect(inner.x, yy, inner.width - 2 * step - _p2Gap * 0.4f, _p2Bh);
                GUI.DrawTexture(rowR, _cardBg);
                UiLayout.Label(new Rect(rowR.x + _p2Gap * 0.4f, rowR.y, rowR.width * 0.52f, rowR.height), b.Label, _p2Small);
                UiLayout.Label(new Rect(rowR.x + rowR.width * 0.54f, rowR.y, rowR.width * 0.46f - _p2Gap * 0.4f, rowR.height),
                               $"{flow.MassNumber(b.Kg)} / {flow.MassText(b.MaxKg)}", _p2Label);
                if (UiLayout.Button(new Rect(rowR.xMax + _p2Gap * 0.4f, yy, step, _p2Bh), "−", _p2Btn)) flow.SetStationKg(b.StationId, b.Kg - flow.MassStepKg);
                if (UiLayout.Button(new Rect(rowR.xMax + _p2Gap * 0.4f + step, yy, step, _p2Bh), "+", _p2Btn)) flow.SetStationKg(b.StationId, b.Kg + flow.MassStepKg);
                yy += _p2Bh + _p2Gap * 0.35f;
            }
            return y + h + _p2Gap;
        }

        /// <summary>
        /// Time of day, weather, reliability and units — what used to be the right-hand column and, before that, a page
        /// of its own in docs/landing-v2/DESIGN.md. Its three defects are all gone here: the grid is two-up at every
        /// size (see <see cref="ChoiceGrid"/>), the card is as tall as its tallest subtitle rather than a third of
        /// whatever was left over, and the warm time-of-day wash that used to lie over the whole column at
        /// 0.18 x light — which cost the captions a third of their contrast, 11.2:1 down to 6.7:1 at midday — is now
        /// only on the time cards themselves, where it is the feedback, and not under the text that has to be read.
        /// </summary>
        private float SecConditions(float x, float y, float w, LandingFlow flow)
        {
            var groups = new (string head, List<PickerItem> items, System.Action<string> pick, bool tint)[]
            {
                ("TIME OF DAY", flow.TimeItems(), id => flow.SetTimeOfDay((TimeOfDay)System.Enum.Parse(typeof(TimeOfDay), id)), true),
                (flow.ConditionsLocked ? "WEATHER  ·  set by the contest" : "WEATHER", flow.WeatherItems(), flow.SetWeather, false),
                ("RELIABILITY", flow.ReliabilityItems(), id => flow.SetReliability((Reliability)System.Enum.Parse(typeof(Reliability), id)), false),
                ("UNITS", flow.UnitItems(), id => flow.SetUnits((UnitSystem)System.Enum.Parse(typeof(UnitSystem), id)), false),
            };
            float inW = w - 2 * _p2Pad;
            float h = 2 * _p2Pad;
            foreach (var g in groups) h += _p2Bh * 0.8f + GridHeight(inW, g.items) + _p2Gap * 0.5f;
            h -= _p2Gap * 0.5f;
            var inner = SecFrame(x, y, w, h, null);
            float yy = inner.y;
            foreach (var g in groups)
            {
                UiLayout.Label(new Rect(inner.x, yy, inner.width, _p2Bh * 0.8f), g.head, _p2Head);
                yy += _p2Bh * 0.8f;
                ChoiceGrid(inner.x, yy, inner.width, g.items, g.pick, g.tint);
                yy += GridHeight(inner.width, g.items) + _p2Gap * 0.5f;
            }
            return y + h + _p2Gap;
        }

        private float GridCardW(float w) => (w - _p2Gap * 0.4f) * 0.5f;

        /// <summary>How tall a choice card has to be for its own longest subtitle, wrapped, plus its title row.</summary>
        private float GridCardH(float w, List<PickerItem> items)
        {
            float tw = GridCardW(w) - _p2Gap * 0.6f, sub = 0f;
            foreach (var it in items) sub = Mathf.Max(sub, UiLayout.WrappedHeight(_p2Tiny, it.Enabled ? it.Subtitle : it.DisabledReason ?? "", tw));
            return Mathf.Max(_p2Bh * 1.25f, _p2Bh * 0.62f + sub + _p2Gap * 0.6f);
        }

        private float GridHeight(float w, List<PickerItem> items)
        {
            int rows = Mathf.CeilToInt(items.Count / 2f);
            return rows * GridCardH(w, items) + (rows - 1) * _p2Gap * 0.4f;
        }

        /// <summary>
        /// A two-up grid of choice cards: title, and the subtitle WRAPPED under it.
        ///
        /// Two columns at every size, deliberately. The old grid went to three the moment a list had more than four
        /// entries, which is exactly what WEATHER has (five presets plus Custom), and that put its cards in 173 px =
        /// 58 pt boxes on the phone. The subtitle style there was wordWrap = false with TextClipping.Clip and
        /// UpperCenter alignment, so an over-long caption was not ellipsised — it was sliced off at BOTH ends.
        /// "Expect a failure in an hour or two" needs about 357 px at the old 21 px font and had 263 px, so what the
        /// reader actually saw was a fragment out of the middle of a sentence.
        /// </summary>
        private void ChoiceGrid(float x, float y, float w, List<PickerItem> items, System.Action<string> pick, bool timeTint)
        {
            float cw = GridCardW(w), ch = GridCardH(w, items);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var cr = new Rect(x + (i % 2) * (cw + _p2Gap * 0.4f), y + (i / 2) * (ch + _p2Gap * 0.4f), cw, ch);
                Color fill = it.Selected ? new Color(1.00f, 0.82f, 0.36f) : it.Enabled ? new Color(0.13f, 0.18f, 0.25f) : new Color(0.18f, 0.24f, 0.32f);
                if (timeTint)
                {
                    double lt = LandingCatalog.TimesOfDay.First(t => t.t.ToString() == it.Id).light;
                    fill = Color.Lerp(new Color(0.15f, 0.12f, 0.30f), new Color(0.55f, 0.78f, 1f), (float)lt);
                }
                var g0 = GUI.color;
                if (it.Selected) { GUI.color = new Color(1.00f, 0.82f, 0.36f); GUI.DrawTexture(cr, _white); }
                GUI.color = fill;
                GUI.DrawTexture(it.Selected ? Inset(cr, _p2Gap * 0.22f) : cr, _white);
                GUI.color = g0;

                // Dark ink on a light card, white on a dark one, decided by the fill's OWN relative luminance instead
                // of by a hand-set threshold on the time of day. The old rule was `light > 0.75`, and Morning is 0.70,
                // so Morning got white text on a mid-blue card: 3.10:1, and its subtitle 2.34:1, against WCAG AA's
                // 4.5:1 — the worst text anywhere on the page. 0.179 is where black and white give equal contrast; by
                // it every card here measures at least 6.0:1 (Morning 6.05, Afternoon 7.35, Sunset 6.75, Midday 10.5).
                var lin = fill.linear;
                bool lightCard = 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b > 0.179f;
                Color ink = lightCard ? new Color(0.06f, 0.07f, 0.09f) : Color.white;
                Color sub = lightCard ? new Color(0.14f, 0.16f, 0.19f) : new Color(0.84f, 0.88f, 0.93f);

                float tx = cr.x + _p2Gap * 0.3f, tw = cr.width - _p2Gap * 0.6f;
                var ts = new GUIStyle(_p2Small) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(_p2Fs * 0.94f), fontStyle = it.Selected ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = ink } };
                UiLayout.Label(new Rect(tx, cr.y + _p2Gap * 0.25f, tw, _p2Bh * 0.62f), it.Title, ts);
                var ss = new GUIStyle(_p2Tiny) { alignment = TextAnchor.UpperCenter, normal = { textColor = sub } };
                UiLayout.LabelWrapped(new Rect(tx, cr.y + _p2Bh * 0.62f, tw, cr.height - _p2Bh * 0.62f - _p2Gap * 0.3f), it.Enabled ? it.Subtitle : it.DisabledReason ?? "", ss);
                if (it.Enabled && !_loadDragging && GUI.Button(cr, GUIContent.none, GUIStyle.none)) pick(it.Id);
            }
        }

        /// <summary>
        /// The W&amp;B plot, laid out like a handbook's chart (CG across, weight up): the type's verified envelope with
        /// its limit lines named, the takeoff point, and the line the CG walks to the zero-fuel point as the fuel burns
        /// (Ty t880u). Factual labels only (t881u). The data is WeightBalance's.
        ///
        /// Changed here: the ANSWER comes first. In limits or not, and why, used to be the last of three lines of 7 pt
        /// grey under the plot; it is now a wrapped line of 19 pt in its own colour directly under the heading, and it
        /// reads WeightBalance.Details (the warnings WITH their amounts — "Over gross by 47 lb") rather than Warnings
        /// (the bare label), which the flat page had never shown at all. The plot keeps its own space below that and
        /// its numbers are 14 pt instead of 7.
        /// </summary>
        private float SecWeightBalance(float x, float y, float w, LandingFlow flow)
        {
            var wb = flow.WeightBalance;
            float inW = w - 2 * _p2Pad;
            if (wb == null)
            {
                const string none = "No loading data for this type.";
                float nh0 = UiLayout.WrappedHeight(_p2Body, none, inW);
                float h0 = 2 * _p2Pad + _p2Bh * 0.8f + nh0;
                var i0 = SecFrame(x, y, w, h0, "WEIGHT & BALANCE");
                UiLayout.LabelWrapped(new Rect(i0.x, i0.y + _p2Bh * 0.8f, i0.width, nh0), none, _p2Body);
                return y + h0 + _p2Gap;
            }

            string head = $"WEIGHT & BALANCE  ·  {flow.MassText(wb.TotalKg)}" + (wb.HasGross ? $" of {flow.MassNumber(wb.MaxGrossKg)}" : "");
            string status = wb.InLimits
                ? (wb.HasEnvelope && wb.HasGross ? "In limits" : "In limits, as far as this type's limits are authored")
                : string.Join("  ·  ", wb.Details.Count > 0 ? wb.Details : wb.Warnings);
            var statusStyle = new GUIStyle(_p2Body) { fontSize = _p2Fs, normal = { textColor = wb.InLimits ? new Color(0.55f, 0.95f, 0.6f) : new Color(1f, 0.75f, 0.35f) } };
            float statusH = UiLayout.WrappedHeight(statusStyle, status, inW);
            float axisTop = _p2Fs * 1.35f;                    // the "Weight (lb)" line, on its own row above the plot
            float xGutter = _p2Fs * 2.9f;                     // the CG ticks and the axis title, under the plot
            float yGutter = _p2Fs * 3.0f;                     // the weight ticks, left of it
            float plotH = Mathf.Clamp(inW * 0.62f, _p2Bh * 3.2f, _p2Bh * 6.5f);
            float legendH = _p2Bh * 0.62f * 2f;
            float h = 2 * _p2Pad + _p2Bh * 0.8f + statusH + _p2Gap * 0.5f + axisTop + plotH + xGutter + _p2Gap * 0.4f + legendH;
            var inner = SecFrame(x, y, w, h, head);

            float yy = inner.y + _p2Bh * 0.8f;
            UiLayout.LabelWrapped(new Rect(inner.x, yy, inner.width, statusH), status, statusStyle);
            yy += statusH + _p2Gap * 0.5f;
            UiLayout.Label(new Rect(inner.x, yy, inner.width, axisTop), wb.YAxisTitle, _p2Tiny);
            yy += axisTop;

            var plot = new Rect(inner.x + yGutter, yy, inner.width - yGutter - _p2Fs * 1.4f, plotH);
            GUI.DrawTexture(plot, _btnBg);
            Vector2 P(double px, double py) => new Vector2(plot.x + (float)px * plot.width, plot.yMax - (float)py * plot.height);

            var tickR = new GUIStyle(_p2Tiny) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            var tickC = new GUIStyle(_p2Tiny) { alignment = TextAnchor.UpperCenter, wordWrap = false };
            foreach (var t in wb.YTicks)
            {
                Vector2 a = P(0, t.At);
                Line(a, P(1, t.At), new Color(1f, 1f, 1f, 0.10f), 1f);
                GUI.Label(new Rect(inner.x, a.y - _p2Fs * 0.5f, yGutter - _p2Fs * 0.25f, _p2Fs), t.Label, tickR);
            }
            foreach (var t in wb.XTicks)
            {
                Vector2 a = P(t.At, 0);
                Line(a, P(t.At, 1), new Color(1f, 1f, 1f, 0.10f), 1f);
                GUI.Label(new Rect(a.x - _p2Fs * 1.8f, plot.yMax + _p2Fs * 0.15f, _p2Fs * 3.6f, _p2Fs * 1.2f), t.Label, tickC);
            }
            GUI.Label(new Rect(plot.x, plot.yMax + _p2Fs * 1.5f, plot.width, _p2Fs * 1.2f), wb.XAxisTitle, tickC);

            // The envelope and its named limits; a gross with no CG limits is still drawn.
            if (wb.Envelope.Count > 1)
                for (int i = 1; i < wb.Envelope.Count; i++) Line(P(wb.Envelope[i - 1].x, wb.Envelope[i - 1].y), P(wb.Envelope[i].x, wb.Envelope[i].y), new Color(0.55f, 0.95f, 1f), 2.5f);
            foreach (var lim in wb.Limits)
            {
                var col = lim.Label == WeightBalance.MaxGrossLineLabel ? new Color(1f, 0.6f, 0.3f, 0.9f) : new Color(0.55f, 0.95f, 1f);
                if (!wb.HasEnvelope) for (int i = 1; i < lim.Points.Count; i++) Line(P(lim.Points[i - 1].x, lim.Points[i - 1].y), P(lim.Points[i].x, lim.Points[i].y), col, 2f);
                Vector2 m = P((lim.Points[0].x + lim.Points[lim.Points.Count - 1].x) * 0.5, (lim.Points[0].y + lim.Points[lim.Points.Count - 1].y) * 0.5);
                var st = new GUIStyle(_p2Tiny) { wordWrap = false, normal = { textColor = col } };
                if (lim.Label == WeightBalance.MaxGrossLineLabel)
                {
                    st.alignment = TextAnchor.LowerCenter;
                    GUI.Label(new Rect(m.x - _p2Fs * 3.4f, m.y - _p2Fs * 1.3f, _p2Fs * 6.8f, _p2Fs * 1.1f), lim.Label, st);
                }
                else
                {
                    // Follow the limit edge so the two names never run across the live points.
                    var matrix = GUI.matrix;
                    GUIUtility.RotateAroundPivot(-90, m);
                    st.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(new Rect(m.x - _p2Fs * 5f, m.y - _p2Fs * 0.55f, _p2Fs * 10f, _p2Fs * 1.1f), lim.Label, st);
                    GUI.matrix = matrix;
                }
            }
            if (!wb.HasEnvelope) GUI.Label(new Rect(plot.x + _p2Gap * 0.3f, plot.yMax - _p2Fs * 1.3f, plot.width, _p2Fs * 1.2f), WeightBalance.NoEnvelopeLabel, _p2Tiny);

            Line(P(wb.ZeroFuelX, wb.ZeroFuelY), P(wb.PointX, wb.PointY), new Color(1f, 1f, 1f, 0.6f), 2.5f);
            var zeroCol = wb.ZeroFuelInside ? new Color(0.85f, 0.85f, 0.85f) : new Color(1f, 0.35f, 0.25f);
            var takeoffCol = wb.TakeoffInside ? new Color(0.35f, 0.95f, 0.45f) : new Color(1f, 0.35f, 0.25f);
            Dot(P(wb.ZeroFuelX, wb.ZeroFuelY), _p2Fs * 0.32f, zeroCol);
            Dot(P(wb.PointX, wb.PointY), _p2Fs * 0.55f, takeoffCol);

            // A fixed legend identifies both markers even when their plotted positions overlap.
            float ly = plot.yMax + xGutter + _p2Gap * 0.4f, lr = _p2Bh * 0.62f;
            Dot(new Vector2(inner.x + _p2Fs * 0.45f, ly + lr * 0.5f), _p2Fs * 0.3f, takeoffCol);
            UiLayout.Label(new Rect(inner.x + _p2Fs * 1.1f, ly, inner.width - _p2Fs * 1.1f, lr), $"{WeightBalance.TakeoffPointLabel}  {wb.TakeoffText}", _p2Small);
            Dot(new Vector2(inner.x + _p2Fs * 0.45f, ly + lr * 1.5f), _p2Fs * 0.22f, zeroCol);
            UiLayout.Label(new Rect(inner.x + _p2Fs * 1.1f, ly + lr, inner.width - _p2Fs * 1.1f, lr), $"{WeightBalance.ZeroFuelPointLabel}  {wb.ZeroFuelText}", _p2Small);
            return y + h + _p2Gap;
        }

        /// <summary>
        /// What LAUNCH will do, a line each. docs/landing-v2/DESIGN.md: "Always show the aircraft/location/load summary
        /// from the host before enabling Launch." <see cref="LandingFlow.Summary"/> has existed since the flow was
        /// written and the Quest panel has always shown it; the flat page never did, so on iPhone, iPad and Mac the one
        /// list of what the button is about to commit to was unreachable. It is also the page's own answer to "did it
        /// take the invite / the fuel / the weather I just set" without flying to find out.
        /// </summary>
        private float SecSummary(float x, float y, float w, LandingFlow flow)
        {
            var lines = flow.Summary().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            float inW = w - 2 * _p2Pad;
            var hs = new float[lines.Count];
            float h = 2 * _p2Pad + _p2Bh * 0.8f;
            for (int i = 0; i < lines.Count; i++) { hs[i] = UiLayout.WrappedHeight(_p2Body, lines[i], inW) + _p2Gap * 0.25f; h += hs[i]; }
            var inner = SecFrame(x, y, w, h, "WHAT LAUNCH WILL DO");
            float yy = inner.y + _p2Bh * 0.8f;
            for (int i = 0; i < lines.Count; i++) { UiLayout.LabelWrapped(new Rect(inner.x, yy, inner.width, hs[i]), lines[i], _p2Body); yy += hs[i]; }
            return y + h + _p2Gap;
        }

        /// <summary>LAUNCH out of limits (Ty t881u / t882u): the exact message and Go back | Fly, over the page.</summary>
        private void DrawLaunchConfirm(Rect safe, LandingFlow flow)
        {
            EnsureLoadStyles();
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);
            float w = Mathf.Min(safe.width * 0.86f, _p2Fs * 26f);
            var msg = new GUIStyle(_p2Body) { fontSize = _p2Fs, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            float msgH = UiLayout.WrappedHeight(msg, LandingFlow.WbOutOfLimitsMessage, w - 2 * _p2Gap);
            float h = 2 * _p2Gap + msgH + _p2Gap + _p2Bh * 1.15f;
            var box = new Rect(safe.center.x - w * 0.5f, safe.center.y - h * 0.5f, w, h);
            GUI.DrawTexture(box, _panel);
            var inner = Inset(box, _p2Gap);
            UiLayout.LabelWrapped(new Rect(inner.x, inner.y, inner.width, msgH), LandingFlow.WbOutOfLimitsMessage, msg);
            float bw = (inner.width - _p2Gap) * 0.5f, by = inner.yMax - _p2Bh * 1.15f;
            if (UiLayout.Button(new Rect(inner.x, by, bw, _p2Bh * 1.15f), LandingFlow.WbGoBackLabel, _p2Btn)) flow.ConfirmLaunch(fly: false);
            if (UiLayout.Button(new Rect(inner.x + bw + _p2Gap, by, bw, _p2Bh * 1.15f), LandingFlow.WbFlyLabel, _p2BtnOn)) flow.ConfirmLaunch(fly: true);
        }

        /// <summary>The grid a block of <paramref name="n"/> cards is laid out in (<see cref="Choices"/>), and the
        /// least height those rows can be and still be read: a card is two lines, its name and what it means.</summary>
        private static int ChoiceColumns(int n) => n > 4 ? 3 : 2;
        private float ChoiceRows(int n) => Mathf.CeilToInt(n / (float)ChoiceColumns(n));
        private float BlockFloor(int n) => ChoiceRows(n) * TextLine * 1.85f + (ChoiceRows(n) - 1) * _gap * 0.4f;

        /// <summary>
        /// The least height the settings column can be drawn in. The column used to take whatever height it was given
        /// and divide it by three, which is why WEATHER could end up as a row of cards too small to read on a phone
        /// (Ty 2026-11-11: "The weather is missing on the landing page"). Now the floor is what a card needs, the page
        /// scrolls if the screen will not give it that, and the extra — when there IS extra — still goes to the cards.
        /// </summary>
        private float SettingsHeight(LandingFlow flow)
        {
            float headH = _bh * 0.75f;
            float h = 2 * (_gap * 0.6f) + 4 * headH + 4 * (_gap * 0.3f);
            h += BlockFloor(flow.TimeItems().Count) + BlockFloor(flow.WeatherItems().Count)
               + BlockFloor(flow.ReliabilityItems().Count) + BlockFloor(flow.UnitItems().Count);
            return h;
        }

        private void DrawSettings(Rect r, LandingFlow flow)
        {
            // Time of day lights the column itself: the light/dark feedback (the visual lane refines it).
            float light = (float)flow.Light;
            GUI.DrawTexture(r, _panel);
            var c = GUI.color; GUI.color = new Color(1f, 0.92f, 0.7f, 0.18f * light); GUI.DrawTexture(r, _white); GUI.color = c;
            var inner = Inset(r, _gap * 0.6f);
            float y = inner.y, headH = _bh * 0.75f;
            var times = flow.TimeItems(); var weather = flow.WeatherItems(); var rel = flow.ReliabilityItems(); var units = flow.UnitItems();
            // UNITS (Ty 2026-11-05 10:56): one row of two cards, Standard | Metric, under the conditions. It is the
            // one block that gains nothing from being taller, so the three CONDITIONS blocks share whatever is spare.
            float unitsH = BlockFloor(units.Count);
            float spare = (inner.height - 4 * headH - 4 * (_gap * 0.3f) - unitsH) / 3f;
            float timeH = Mathf.Max(BlockFloor(times.Count), spare);
            float weatherH = Mathf.Max(BlockFloor(weather.Count), spare);
            float relH = Mathf.Max(BlockFloor(rel.Count), spare);
            y = Choices(inner.x, y, inner.width, headH, timeH, "TIME OF DAY", times, id => flow.SetTimeOfDay((TimeOfDay)System.Enum.Parse(typeof(TimeOfDay), id)), timeTint: true);
            y = Choices(inner.x, y, inner.width, headH, weatherH, flow.ConditionsLocked ? "WEATHER  ·  set by the contest" : "WEATHER", weather, flow.SetWeather);
            y = Choices(inner.x, y, inner.width, headH, relH, "RELIABILITY", rel, id => flow.SetReliability((Reliability)System.Enum.Parse(typeof(Reliability), id)));
            Choices(inner.x, y, inner.width, headH, unitsH, "UNITS", units, id => flow.SetUnits((UnitSystem)System.Enum.Parse(typeof(UnitSystem), id)));
        }

        /// <summary>A heading and a grid of small cards. Returns the next y.</summary>
        private float Choices(float x, float y, float w, float headH, float h, string head, List<PickerItem> items, System.Action<string> pick, bool timeTint = false)
        {
            UiLayout.Label(new Rect(x, y, w, headH), head, _head);
            y += headH;
            int cols = ChoiceColumns(items.Count), rows = Mathf.CeilToInt(items.Count / (float)cols);
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
