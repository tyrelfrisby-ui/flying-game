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
        public static int MinText => Mathf.Max(22, Mathf.RoundToInt(S * 33f / 1206f));
        public static int CaptionText => Mathf.Max(20, Mathf.RoundToInt(S * 30f / 1206f));
        public static float S => Mathf.Min(UiSurface.Width, UiSurface.Height);
        public static float Margin => S * 0.018f;
        public static float Gap => S * 0.012f;
        public static float ButtonH => S * 0.062f;

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

        public static Rect MenuRect => new Rect(SafeLeft + Margin, ToolbarTop, S * 0.13f, ButtonH);
        public static Rect OptionsRect => new Rect(UiSurface.Width - SafeRight - Margin - S * 0.17f, ToolbarTop, S * 0.17f, ButtonH);
        public static float ToolbarBottom => ToolbarTop + ButtonH;

        /// <summary>Toolbar slot <paramref name="i"/> of <paramref name="n"/>, filled right-to-left from just left of OPTIONS.</summary>
        public static Rect ToolbarSlot(int i, int n)
        {
            float left = MenuRect.xMax + Gap, right = OptionsRect.x - Gap;
            float w = Mathf.Min(S * 0.17f, (right - left - Gap * (n - 1)) / Mathf.Max(1, n));
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
        public static GUIStyle ButtonFit(GUIStyle st, string text, float width)
        {
            var fit = new GUIStyle(st) { wordWrap = false, clipping = TextClipping.Clip };
            fit.fontSize = Mathf.Max(CaptionText, st.fontSize);
            while (fit.fontSize > CaptionText && fit.CalcSize(new GUIContent(text)).x > width) fit.fontSize--;
            fit.wordWrap = fit.CalcSize(new GUIContent(text)).x > width;
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
