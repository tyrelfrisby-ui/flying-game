using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Orientation-aware screen split (owner request 2026-09-08: flip the phone vertical and the game goes
    /// portrait — the two touchpads move to the BOTTOM and the flying "area" with the HUD and the aircraft
    /// sits ABOVE them).
    ///
    ///   LANDSCAPE : the 3D view fills the screen; the pads float at mid-height at the sides (unchanged).
    ///   PORTRAIT  : a control TRAY (pads + trim + buttons) owns the bottom <see cref="TrayHeightPx"/> px;
    ///               the chase camera's viewport is the rest, so the HUD/aircraft are never under a thumb.
    ///
    /// Everything is derived from Screen.width/height each call, so an autorotation mid-flight re-lays
    /// itself the next frame. All rects are screen px, bottom-left origin (Unity screen space).
    /// </summary>
    public static class ScreenLayout
    {
        // Portrait tray geometry, as fractions of the screen WIDTH (the short edge in portrait).
        public const float PortraitPadHalf = 0.20f;   // pad half-size → two pads + trim fit across a phone
        public const float Margin = 0.035f;
        public const float Gap = 0.02f;
        public const float ButtonHeight = PortraitPadHalf * 0.26f;
        public const float EgressRowHeight = ButtonHeight * 0.85f;   // second (short) row: BAIL OUT · EJECT
        public const float TrimBarHeight = 0.045f;                    // aileron / rudder trim bars (RC-transmitter style)
        public const float FontFrac = 0.028f;         // TouchFlightControls' label font, min(w,h) fraction

        public static bool Portrait => Screen.height > Screen.width;

        /// <summary>Pad label block above each pad: two label lines (name + value).</summary>
        public static float LabelBlockPx => Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * FontFrac) * 1.5f * 2f;

        /// <summary>Height of the bottom control tray in px (0 in landscape: the pads float over the view).</summary>
        public static float TrayHeightPx
        {
            get
            {
                if (!Portrait) return 0f;
                float w = Screen.width;
                // margin + pads + labels + gap + button row + gap + egress row (BAIL OUT · EJECT) + gap
                return w * Margin + 2f * w * PortraitPadHalf + w * TrimBarHeight + w * Gap * 0.5f + LabelBlockPx + w * Gap + w * ButtonHeight + w * Gap
                       + w * EgressRowHeight + w * Gap;
            }
        }

        /// <summary>The flying area (3D view + HUD) in screen px.</summary>
        public static Rect ViewRect => new Rect(0f, TrayHeightPx, Screen.width, Screen.height - TrayHeightPx);

        /// <summary>The control tray in screen px (empty in landscape).</summary>
        public static Rect TrayRect => new Rect(0f, 0f, Screen.width, TrayHeightPx);

        /// <summary>RULE (owner 2026-09-10): no instrument or button may cover the aircraft. This is the aircraft's
        /// on-screen rectangle (screen px, bottom-left origin, padded), refreshed every frame by AirframeVisual;
        /// layouts push their elements out of it.</summary>
        public static Rect AircraftKeepOut { get; private set; } = new Rect(-1f, -1f, 0f, 0f);
        public static bool HasAircraftKeepOut => AircraftKeepOut.width > 0f;

        public static void UpdateAircraftKeepOut(Camera cam, Transform aircraft)
        {
            if (cam == null || aircraft == null) { AircraftKeepOut = new Rect(-1f, -1f, 0f, 0f); return; }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            foreach (Renderer r in aircraft.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 sp = cam.WorldToScreenPoint(corner);
                    if (sp.z <= 0f) continue;
                    any = true;
                    if (sp.x < minX) minX = sp.x; if (sp.x > maxX) maxX = sp.x;
                    if (sp.y < minY) minY = sp.y; if (sp.y > maxY) maxY = sp.y;
                }
            }
            if (!any) { AircraftKeepOut = new Rect(-1f, -1f, 0f, 0f); return; }
            float pad = Mathf.Min(Screen.width, Screen.height) * 0.04f;
            AircraftKeepOut = Rect.MinMaxRect(minX - pad, minY - pad, maxX + pad, maxY + pad);
        }

        /// <summary>Normalized camera viewport for <see cref="Camera.rect"/>.</summary>
        public static Rect CameraViewport
        {
            get
            {
                float f = Screen.height > 0 ? TrayHeightPx / Screen.height : 0f;
                return new Rect(0f, f, 1f, 1f - f);
            }
        }
    }
}
