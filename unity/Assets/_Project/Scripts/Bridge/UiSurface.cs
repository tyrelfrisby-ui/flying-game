using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The screen IMGUI is laying out for (TV MODE, docs/TV-COMPANION-PLAN.md §1.2). On iOS <see cref="Screen"/> is always
    /// the phone; while <see cref="TvOutput"/> draws the instruments into the TV's picture it brackets that pass with
    /// <see cref="Begin"/> / <see cref="End"/>, and the layout code the pass runs (UiLayout, AnalogGauges, HudOverlay,
    /// ScreenLayout's keep-out) reads its sizes from here instead. Outside a TV pass this IS <see cref="Screen"/>, so a
    /// handheld frame is laid out exactly as before.
    /// </summary>
    public static class UiSurface
    {
        private static int _w, _h, _depth;
        private static bool _panel;

        /// <summary>A TV pass is drawing: sizes are the TV's, there are no pads, no notch and no toolbar taps.</summary>
        public static bool IsTv => _depth > 0;
        /// <summary>The Mac's second-monitor pass (Q8): instruments only, laid out as an exterior row whatever the view,
        /// on a square reference surface that TvOutput then scales to the monitor (Core MonitorPanel).</summary>
        public static bool IsPanel => _depth > 0 && _panel;
        public static int Width => _depth > 0 ? _w : Screen.width;
        public static int Height => _depth > 0 ? _h : Screen.height;

        /// <summary>Lay out for a <paramref name="w"/> × <paramref name="h"/> TV picture until the matching <see cref="End"/>.</summary>
        public static void Begin(int w, int h, bool panel = false)
        {
            _w = Mathf.Max(1, w); _h = Mathf.Max(1, h);
            _panel = panel;
            _depth++;
        }

        public static void End() { if (_depth > 0) _depth--; if (_depth == 0) _panel = false; }
    }
}
