using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// ONE layout for every on-screen element (owner rule 2026-10-03: no text or UI may ever overlap — landing page,
    /// flying, lessons; landscape and portrait; iPhone, iPad, Mac). GUI coordinates (top-left origin).
    ///
    ///   Row 1  TOOLBAR : MENU · [VIEW · REPLAY · CLIP · REC · (END)] · OPTIONS — one row, labels shrink to fit.
    ///   Row 2+ TEXT    : status, event, challenge and lesson lines are STACKED — each asks <see cref="NextLine"/> for its
    ///                    own rect inside the centre BAND (between the pads in landscape), so lines never land on each
    ///                    other or on a button; text that's too long shrinks (<see cref="Fit"/>).
    ///   Below  DIALS   : the analog gauges stay inside the band, under the text stack and above the bottom buttons.
    /// The stack resets every IMGUI event of a new frame; components draw in a fixed order within a frame, so the
    /// Layout and Repaint passes allocate the same rects. <see cref="StackBottomLastFrame"/> tells the dials where the
    /// text ended.
    /// </summary>
    public static class UiLayout
    {
        // Inert unless the hudshots self-test explicitly enables them. Insets are physical pixels.
        public static bool ForceTouchLayoutForTest;
        public static bool IPhoneSafeAreaForTest;
        /// <summary>With <see cref="IPhoneSafeAreaForTest"/>: an iPad mini instead (no notch; 20 pt home indicator at 2x).</summary>
        public static bool IPadSafeAreaForTest;
        public static int MinText => Mathf.Max(22, Mathf.RoundToInt(Unit * 33f / 1206f));
        public static int CaptionText => Mathf.Max(20, Mathf.RoundToInt(Unit * 30f / 1206f));

        // ---- SIZE: the screen's pixels, and the size a finger needs -------------------------------------------
        //
        // Ty 2026-11-08: "The size of the menus and buttons on iPhone is too small." The cause is that every size in
        // the game was a fraction of the PIXEL count on the short side, which says nothing about how big that is in
        // the hand. An iPhone and an iPad have a similar number of pixels across the short side — about 1200 and
        // about 1500 — on 2.8 inches and 5.5 inches of glass. So the same code drew a control half the physical size
        // on the phone, and a 4.4 mm button is below anything Apple would ship (their floor is 44 pt, about 7 mm).
        //
        // So there are two metrics now, and the difference matters:
        //   S     — the screen, in pixels. GEOMETRY: where things are, how wide a panel may be, where the dials end.
        //   Unit  — what a finger needs. SIZE: button heights, margins, gaps, font floors.
        // Widths deliberately stay on S. Growing them is what would push two things into each other, and the owner's
        // 2026-10-03 rule is that no text or UI may ever overlap; growing HEIGHTS cannot collide, because a panel that
        // runs off the bottom scrolls (OptionsPanel) and the toolbar is one row with its own clamps.
        // THE SURFACE BEING DRAWN, not always this device's screen: with the game on a TV the instruments are laid
        // out for the TV's pixels (UiSurface), and on the phone UiSurface IS Screen. Geometry only — Unit below stays
        // on Screen, because what a finger needs is about the glass in the hand and a TV is not touched.
        public static float S => Mathf.Min(UiSurface.Width, UiSurface.Height);

        /// <summary>Physical pixels per inch, with a fallback where the platform will not say (the editor, some Macs).</summary>
        public static float Dpi => Screen.dpi > 1f ? Screen.dpi : 160f;
        /// <summary>A button ought to be about this tall in the hand (in): Apple's floor is 44 pt = 0.27".</summary>
        public const float TargetButtonIn = 0.30f;
        /// <summary>Scale forced by the hudshots self-test so its images do not move with the test machine's DPI.</summary>
        public static float ScaleForTest = -1f;

        /// <summary>
        /// How much bigger than a plain fraction of the screen the controls have to be drawn to reach
        /// <see cref="TargetButtonIn"/>. 1 on a tablet, which is where the original fractions were chosen and where
        /// they are right; about 1.7 on a phone. Never below 1 — nothing should SHRINK from this — and capped, so a
        /// bad DPI reading cannot produce a button the size of the screen.
        /// </summary>
        public static float AutoScale
        {
            get
            {
                float shortIn = Mathf.Min(Screen.width, Screen.height) / Dpi;
                if (shortIn < 0.5f || shortIn > 40f) return 1f;   // a reading that cannot be a screen: leave it alone
                return Mathf.Clamp(TargetButtonIn / (0.062f * shortIn), 1f, 2.2f);
            }
        }

        private static float _userScale = -1f;
        /// <summary>The owner's own multiplier on top of <see cref="AutoScale"/> (OPTIONS → Display), so this is
        /// tunable by eye on the device instead of by another build.</summary>
        public static float UserScale
        {
            get { if (_userScale < 0f) _userScale = Mathf.Clamp(PlayerPrefs.GetFloat("ui.scale", 1f), 0.7f, 1.6f); return _userScale; }
            set { _userScale = Mathf.Clamp(value, 0.7f, 1.6f); PlayerPrefs.SetFloat("ui.scale", _userScale); PlayerPrefs.Save(); }
        }
        /// <summary>
        /// The scale actually used. AutoScale is deliberately NOT in it.
        ///
        /// Ty 2026-11-08: applying the DPI-derived 1.7x by default broke the in-flight screen outright — the toolbar's
        /// fixed ends grew, which squeezed VIEW/REPLAY/CLIP/REC into a few pixels each so their labels came out one
        /// letter per line, and the HUD and engine rows printed over each other because their layouts were measured for
        /// the old font. The sizes were only ever half the problem: every screen's LAYOUT was calibrated at the old
        /// ones, and a global multiplier cannot know that. So the default is 1 — exactly what shipped before — and the
        /// size is raised deliberately, one screen at a time, once that screen can take it. AutoScale stays as the
        /// honest recommendation and OPTIONS shows it.
        /// </summary>
        /// <summary>
        /// The scale for MENUS AND PAGES — Options, Flight Setup, the chart, the credits, the joystick wizard.
        /// Ty 2026-11-08, second pass: "For now just make the menus and options pages bigger for me to test then we
        /// can try the ui on the live play again to see if we can make a slight improvement without breaking it."
        /// A page can afford to grow: it owns the whole screen, its rows wrap, and what runs off the bottom scrolls
        /// with MORE ABOVE / MORE BELOW to say so.
        /// </summary>
        public static float PanelScale => ScaleForTest > 0f ? ScaleForTest : AutoScale * UserScale;

        /// <summary>The unit a MENU OR PAGE sizes its text and rows from.</summary>
        public static float PanelUnit => Mathf.Min(Screen.width, Screen.height) * PanelScale;

        /// <summary>
        /// The scale for the FLYING SCREEN: the toolbar, the HUD, the engine row, the touch controls. One, and
        /// deliberately not a setting yet.
        ///
        /// The first pass raised this with everything else and broke the game: the toolbar's ends grew, which left a
        /// few pixels for VIEW, REPLAY, CLIP and REC, and the HUD rows printed over each other. Those layouts are
        /// measured for this size and have to be taken one at a time before it can move — which is the next pass, and
        /// the reason this is a separate number rather than the same one used twice.
        /// </summary>
        public const float LiveScale = 1f;

        public static float Scale => PanelScale;   // what the Options slider is showing

        /// <summary>The unit the FLYING SCREEN sizes from. <see cref="PanelUnit"/> is the one for menus and pages.</summary>
        public static float Unit => Mathf.Min(Screen.width, Screen.height) * LiveScale;

        public static float Margin => Unit * 0.018f;
        public static float Gap => Unit * 0.012f;
        public static float ButtonH => Unit * 0.062f;

        // ---- THE SAFE AREA (owner 2026-10-31: "the landing page has ui behind the black camera pill on the iPhone
        // screen move it down") ----
        //
        // Unity reports Screen.safeArea in SCREEN pixels with the origin at the BOTTOM left; IMGUI draws with the
        // origin at the TOP left. These four give the inset on each edge in GUI coordinates, so a caller never has to
        // remember which way up the rectangle is. On a device with no notch, pill or home indicator they are all
        // zero and nothing moves.

        /// <summary>Pixels of the top edge the Dynamic Island, notch or status bar is sitting in.</summary>
        public static float SafeTop => UiSurface.IsTv ? TvInset(UiSurface.Height) : IPhoneSafeAreaForTest ? (IPadSafeAreaForTest ? 0f : Screen.height > Screen.width ? 186f : 0f) : Mathf.Max(0f, Screen.height - Screen.safeArea.yMax);
        /// <summary>Pixels of the bottom edge the home indicator is sitting in.</summary>
        public static float SafeBottom => UiSurface.IsTv ? TvInset(UiSurface.Height) : IPhoneSafeAreaForTest ? (IPadSafeAreaForTest ? 40f : 63f) : Mathf.Max(0f, Screen.safeArea.yMin);
        public static float SafeLeft => UiSurface.IsTv ? TvInset(UiSurface.Width) : IPhoneSafeAreaForTest ? (IPadSafeAreaForTest || Screen.height > Screen.width ? 0f : 186f) : Mathf.Max(0f, Screen.safeArea.xMin);
        public static float SafeRight => UiSurface.IsTv ? TvInset(UiSurface.Width) : IPhoneSafeAreaForTest ? (IPadSafeAreaForTest || Screen.height > Screen.width ? 0f : 186f) : Mathf.Max(0f, Screen.width - Screen.safeArea.xMax);

        /// <summary>A TV's title-safe margin (TvSafeArea): the TV pass has no notch, but an HDMI set may overscan.</summary>
        private static float TvInset(float edge) => (float)FlyingGame.Core.TvSafeArea.Inset(edge);

        /// <summary>The toolbar's own top edge: the margin, pushed clear of whatever the hardware is sitting in. The
        /// in-flight row had the same defect the owner reported on the launch page — MENU and OPTIONS were under the
        /// Dynamic Island — and everything that hangs off ToolbarBottom follows this down.</summary>
        private static float ToolbarTop => SafeTop + Margin;

        /// <summary>Width the toolbar actually has to spend, between the safe insets.</summary>
        private static float ToolbarW => Mathf.Max(1f, Screen.width - SafeLeft - SafeRight - 2f * Margin);
        // The two fixed ends grow with the finger like everything else, but never past their share of the row: the
        // toolbar is ONE row (MENU · slots · OPTIONS) and it has to stay one row on a phone in portrait.
        private static float MenuW => Mathf.Min(Unit * 0.13f, ToolbarW * 0.22f);
        private static float OptionsW => Mathf.Min(Unit * 0.17f, ToolbarW * 0.28f);
        public static Rect MenuRect => new Rect(SafeLeft + Margin, ToolbarTop, MenuW, ButtonH);
        public static Rect OptionsRect => new Rect(UiSurface.Width - SafeRight - Margin - OptionsW, ToolbarTop, OptionsW, ButtonH);
        public static float ToolbarBottom => ToolbarTop + ButtonH;

        /// <summary>Toolbar slot <paramref name="i"/> of <paramref name="n"/>, filled right-to-left from just left of OPTIONS.</summary>
        public static Rect ToolbarSlot(int i, int n)
        {
            float left = MenuRect.xMax + Gap, right = OptionsRect.x - Gap;
            float w = Mathf.Min(Unit * 0.17f, (right - left - Gap * (n - 1)) / Mathf.Max(1, n));
            float x0 = right - n * w - (n - 1) * Gap;
            return new Rect(x0 + i * (w + Gap), ToolbarTop, w, ButtonH);
        }

        // ---- centre band (set by the controls layout each time it changes) ----
        public static float BandMin = 0f, BandMax = 99999f;      // GUI x range clear of the pads (landscape)
        public static float BottomLimit = 99999f;               // GUI y above which the dials must stay (buttons / tray below)
        /// <summary>GUI y the LEFT dial column must stay above (the Mac's control indicator and its THR/trim label).</summary>
        public static float LeftBottomLimit = 99999f;
        /// <summary>GUI x range between the analog dial columns (valid while <see cref="DialsShown"/>).</summary>
        public static float DialsLeft, DialsRight; public static int DialsFrame = -10;
        /// <summary>The AERO panel's insets are on (outside views): they use the dials' space.</summary>
        public static bool AeroInsetsShown;
        public static bool DialsShown => Time.frameCount - DialsFrame <= 2;
        public static float BandLeft => UiSurface.IsTv ? SafeLeft + Margin : Mathf.Max(Margin, BandMin);   // the TV has no pads
        public static float BandRight => UiSurface.IsTv ? UiSurface.Width - SafeRight - Margin : Mathf.Min(Screen.width - Margin, BandMax);

        /// <summary>The engine row is drawn under the flight row (and the text stack starts below it). False when the
        /// engine row is below the airplane instead (<see cref="EngineRowRect"/>) or there is no engine.</summary>
        public static bool EngineRowShown;
        /// <summary>GUI-space box (top-left origin) for the ENGINE row when it sits below the airplane (owner 2026-11-02:
        /// "put the engine instruments below the airplane"). Width 0 = the engine row stays under the flight row.
        /// Published by the touch/controller layout, which knows where its buttons and pads are.</summary>
        public static Rect EngineRowRect;
        public static float InstrumentBottom => (float)FlyingGame.Core.HudInstrumentLayout.Bottom(ToolbarBottom, Gap, S, TouchFlightControls.TouchSticksHidden, EngineRowShown);

        // ---- the text stack ----
        private static int _frame = -1; private static EventType _evt;
        private static float _cursor;
        public static float StackBottomLastFrame { get; private set; }
        private static float _stackBottomThisFrame;

        private static void Sync()
        {
            Event e = Event.current;
            EventType t = e != null ? e.type : EventType.Repaint;
            if (Time.frameCount != _frame || t != _evt)
            {
                if (Time.frameCount != _frame) { StackBottomLastFrame = Mathf.Max(ToolbarBottom, _stackBottomThisFrame); _stackBottomThisFrame = ToolbarBottom; }
                _frame = Time.frameCount; _evt = t;
                _cursor = ToolbarBottom + Gap * 0.6f;
                if (SessionSettings.Instruments == SessionSettings.InstrumentMode.Analog && !ChaseCamera.InCockpit && !AeroInsetsShown && !TvOutput.InstrumentsElsewhere)   // the dials are on the TV or the Mac's second monitor
                    _cursor = InstrumentBottom + Gap;
            }
        }

        /// <summary>The next free full-band line of height <paramref name="h"/> (px) below the toolbar.</summary>
        public static Rect NextLine(float h) => NextBlock(h);

        /// <summary>The next free block of height <paramref name="h"/> in the centre band.</summary>
        public static Rect NextBlock(float h)
        {
            Sync();
            var r = new Rect(BandLeft, _cursor, BandRight - BandLeft, h);
            _cursor += h + Gap * 0.3f;
            _stackBottomThisFrame = Mathf.Max(_stackBottomThisFrame, _cursor);
            return r;
        }

        /// <summary>A copy of <paramref name="st"/> whose font shrinks only to the primary reading floor until <paramref name="text"/> fits in
        /// <paramref name="w"/> x <paramref name="h"/> — Label abbreviates excess detail at that floor.</summary>
        public static GUIStyle Fit(GUIStyle st, string text, float w, float h)
        {
            if (st == null) return st;
            var f = new GUIStyle(st) { wordWrap = false, clipping = TextClipping.Clip };
            int minimum = MinText;
            f.fontSize = Mathf.Max(minimum, st.fontSize);
            while (f.fontSize > minimum && f.CalcSize(new GUIContent(text)).x > w) f.fontSize--;
            return f;
        }


        // A modal card (lesson briefing / result) is up: the dials and HUD lines step aside rather than sit under it.
        private static int _modalFrame = -10;
        public static void ModalShown() => _modalFrame = Time.frameCount;
        public static bool Modal => Time.frameCount - _modalFrame <= 1;

        public static void Label(Rect r, string text, GUIStyle st)
        {
            var fit = Fit(st, text, r.width, r.height);
            if (fit == null) return;
            // Drop trailing detail rather than making the entire line unreadable.
            while (text.Length > 1 && fit.CalcSize(new GUIContent(text)).x > r.width)
                text = text.Substring(0, text.Length - 2).TrimEnd() + "…";
            GUI.Label(r, text, fit);
        }
        /// <summary>
        /// How tall <paramref name="text"/> is once WRAPPED into <paramref name="w"/>. A row asks this and takes the
        /// height it gets, so that a bigger font cannot drop a second line onto the row below (Ty 2026-11-08, asking
        /// for bigger menus): <see cref="Label"/> protects a single line by dropping its tail, which is right for a
        /// status line and wrong for a settings label, where the words are the whole point.
        /// </summary>
        public static float WrappedHeight(GUIStyle st, string text, float w)
        {
            if (st == null || string.IsNullOrEmpty(text) || w <= 1f) return 0f;
            var f = new GUIStyle(st) { wordWrap = true, clipping = TextClipping.Clip, fontSize = Mathf.Max(MinText, st.fontSize) };
            return f.CalcHeight(new GUIContent(text), w);
        }

        /// <summary>A label that wraps rather than losing its tail.</summary>
        public static void LabelWrapped(Rect r, string text, GUIStyle st)
        {
            if (st == null) return;
            var f = new GUIStyle(st) { wordWrap = true, clipping = TextClipping.Clip, fontSize = Mathf.Max(MinText, st.fontSize) };
            GUI.Label(r, text, f);
        }

        /// <summary>Absolute floor for a button label: below this nothing is readable anyway, and it exists so that a
        /// label can always keep SHRINKING rather than be wrapped (Ty 2026-11-08: "VIEW" came out as a column of four
        /// letters when the toolbar squeezed its slot). A single word has no break point, so wrapping it does not make
        /// it fit — it stacks it one character per line, which is worse than small and worse than clipped.</summary>
        public const int HardMinText = 13;

        public static GUIStyle ButtonFit(GUIStyle st, string text, float width)
        {
            var fit = new GUIStyle(st) { wordWrap = false, clipping = TextClipping.Clip };
            fit.fontSize = Mathf.Max(CaptionText, st.fontSize);
            while (fit.fontSize > CaptionText && fit.CalcSize(new GUIContent(text)).x > width) fit.fontSize--;
            if (fit.CalcSize(new GUIContent(text)).x > width)
            {
                // Still too wide at the reading floor. Wrapping only helps where there is somewhere to break.
                bool canBreak = text != null && (text.Contains(" ") || text.Contains("\n"));
                if (canBreak) fit.wordWrap = true;
                else while (fit.fontSize > HardMinText && fit.CalcSize(new GUIContent(text)).x > width) fit.fontSize--;
            }
            return fit;
        }
        public static void ButtonLabel(Rect r, string text, GUIStyle st)
        {
            var fit = ButtonFit(st, text, r.width);
            GUI.Label(r, text, fit);
        }
        public static bool Button(Rect r, string text, GUIStyle st)
        {
            var fit = ButtonFit(st, text, r.width - 6f);
            return GUI.Button(r, text, fit);
        }
    }
}
