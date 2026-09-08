using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// A guaranteed GUI font. On iOS/IL2CPP the default IMGUI skin font can be stripped from the build,
    /// so the first GUI.Label/Button that draws text throws NullReferenceException in GUI.DoLabel and
    /// aborts the rest of that frame's OnGUI (symptom: only non-text GUI — e.g. a pad's circle texture —
    /// renders, everything after the first label vanishes). Every GUIStyle we draw text with must have an
    /// explicit .font from here so it never depends on the default skin font.
    /// </summary>
    public static class UiFont
    {
        private static Font _font;

        public static Font Get()
        {
            if (_font != null)
            {
                return _font;
            }

            // Unity's built-in runtime font (never stripped when fetched explicitly).
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");        // older editors
            if (_font == null)
            {
                // Last resort: a dynamic font backed by a system typeface.
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica", "Arial", ".SF UI Text" }, 16);
            }
            return _font;
        }

        /// <summary>Stamp the guaranteed font onto a style (returns it for chaining).</summary>
        public static GUIStyle Apply(GUIStyle style)
        {
            style.font = Get();
            return style;
        }
    }
}
