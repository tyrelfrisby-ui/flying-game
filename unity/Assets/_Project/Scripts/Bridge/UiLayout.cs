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
        public static float S => Mathf.Min(Screen.width, Screen.height);
        public static float Margin => S * 0.018f;
        public static float Gap => S * 0.012f;
        public static float ButtonH => S * 0.062f;

        public static Rect MenuRect => new Rect(Margin, Margin, S * 0.13f, ButtonH);
        public static Rect OptionsRect => new Rect(Screen.width - Margin - S * 0.17f, Margin, S * 0.17f, ButtonH);
        public static float ToolbarBottom => Margin + ButtonH;

        /// <summary>Toolbar slot <paramref name="i"/> of <paramref name="n"/>, filled right-to-left from just left of OPTIONS.</summary>
        public static Rect ToolbarSlot(int i, int n)
        {
            float left = MenuRect.xMax + Gap, right = OptionsRect.x - Gap;
            float w = Mathf.Min(S * 0.17f, (right - left - Gap * (n - 1)) / Mathf.Max(1, n));
            float x0 = right - n * w - (n - 1) * Gap;
            return new Rect(x0 + i * (w + Gap), Margin, w, ButtonH);
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
        public static float BandLeft => Mathf.Max(Margin, BandMin);
        public static float BandRight => Mathf.Min(Screen.width - Margin, BandMax);

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

        /// <summary>A copy of <paramref name="st"/> whose font shrinks (down to 45 %) until <paramref name="text"/> fits in
        /// <paramref name="w"/> x <paramref name="h"/> — long labels never spill onto their neighbours.</summary>
        public static GUIStyle Fit(GUIStyle st, string text, float w, float h)
        {
            if (st == null || string.IsNullOrEmpty(text)) return st;
            var c = new GUIContent(text);
            Vector2 sz = st.CalcSize(c);
            if (sz.x <= w && sz.y <= h * 1.05f) return st;
            var f = new GUIStyle(st) { wordWrap = false, clipping = TextClipping.Clip };
            int min = Mathf.Max(6, Mathf.RoundToInt(st.fontSize * 0.45f));
            for (int fs = st.fontSize - 1; fs >= min; fs--)
            {
                f.fontSize = fs;
                sz = f.CalcSize(c);
                if (sz.x <= w && sz.y <= h * 1.05f) break;
            }
            return f;
        }

        // A modal card (lesson briefing / result) is up: the dials and HUD lines step aside rather than sit under it.
        private static int _modalFrame = -10;
        public static void ModalShown() => _modalFrame = Time.frameCount;
        public static bool Modal => Time.frameCount - _modalFrame <= 1;

        public static void Label(Rect r, string text, GUIStyle st) => GUI.Label(r, text, Fit(st, text, r.width, r.height));
        public static bool Button(Rect r, string text, GUIStyle st) => GUI.Button(r, text, Fit(st, text, r.width - 6f, r.height));
    }
}
