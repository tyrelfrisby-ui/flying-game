using FlyingGame.Core.Combat;
using FlyingGame.Core.MathTypes;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Compact instruments for same-device iPhone/iPad split views only.</summary>
    public sealed class LocalPlayerHud : MonoBehaviour
    {
        public LocalPlayerView View;
        private GUIStyle _text;
        private Texture2D _ring;

        public static bool CompactActive
        {
            get
            {
#if UNITY_IOS || UNITY_EDITOR
                return LocalSplit.Active && !VrMode.SuppressFlatOverlays;
#else
                return false;
#endif
            }
        }

        // Both rectangles use GUI's top-left origin. Intersect with the safe area rather than adding
        // the notch inset to BOTH halves: the inside seam needs no physical-screen inset.
        public static Rect SafeRect(Rect viewport, Rect safe)
        {
            float x = Mathf.Max(viewport.xMin, safe.xMin), y = Mathf.Max(viewport.yMin, safe.yMin);
            return new Rect(x, y, Mathf.Max(0, Mathf.Min(viewport.xMax, safe.xMax) - x),
                Mathf.Max(0, Mathf.Min(viewport.yMax, safe.yMax) - y));
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !CompactActive || View?.Driver?.Sim?.Aircraft == null || SessionSettings.MenuOpen ||
                SessionSettings.ReplayActive || UiLayout.Modal || !PilotEgress.HudAllowed(View.Driver)) return;
            var d = View.Driver;
            Rect sa = Screen.safeArea;
            sa.y = Screen.height - sa.yMax;
            Rect r = SafeRect(View.GuiRect, sa);
            if (r.width < 80 || r.height < 80) return;
            Vector2 p = View.GuiRect.center - r.position;
            bool armed = d.Sim.Aircraft.Guns?.Guns.Count > 0;
            bool visible = true;
            if (armed && View.Camera != null)
            {
                var ac = d.Sim.Aircraft;
                (Vec3 aim, _) = Gunnery.PredictRound(ac.Guns, ac.Config, ac.State, 300);
                Vector3 sp = View.Camera.WorldToScreenPoint(CoordinateMap.ToUnity(aim));
                p = new Vector2(sp.x - r.x, Screen.height - sp.y - r.y);
                visible = sp.z > 0;
            }
            Draw(r, View.PlayerIndex, GamepadInput.NameFor(View.PlayerIndex) != null,
                d.IasMs * 1.943844, d.AltitudeM * 3.28084, d.HeadingRad * Mathf.Rad2Deg,
                d.LoadFactorG, p, armed, visible);
        }

        /// <summary>Shared live/review drawing entry; pipper is local to the safe viewport.</summary>
        public void Draw(Rect r, int player, bool connected, double knots, double feet, double degrees,
            double g, Vector2 p, bool armed, bool visible)
        {
            // 430 x 390 points is the small landscape half. Scale from this view, never the full screen.
            float scale = Mathf.Min(r.width / 430f, r.height / 390f);
            float fs = 18f * scale, margin = 12f * scale;
            Color tint = player == 0 ? new Color(.35f, .9f, 1f) : new Color(1f, .78f, .3f);
            var oldColor = GUI.color;
            GUI.BeginGroup(r); // clips every stroke and projected pipper to its own safe half
            _text ??= new GUIStyle { font = UiFont.Get(), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _text.alignment = TextAnchor.MiddleLeft;
            _text.fontSize = Mathf.Max(12, Mathf.RoundToInt(fs));
            _text.normal.textColor = tint;
            float w = r.width - 2 * margin, row = fs * 1.8f;
            Box(new Rect(margin, margin, w, row), .72f);
            GUI.Label(new Rect(margin + fs * .5f, margin, w - fs, row),
                $"P{player + 1}" + (!connected ? "   NO CONTROLLER" : ""), _text);
            float heading = Mathf.Repeat((float)degrees, 360f);
            _text.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(margin, margin, w - fs * .5f, row), $"{Mathf.RoundToInt(heading) % 360:000}°", _text);
            _text.alignment = TextAnchor.MiddleLeft;
            float y = r.height - margin - row;
            Box(new Rect(margin, y, w, row), .72f);
            float cell = w / 3f;
            GUI.Label(new Rect(margin + fs * .5f, y, cell, row), $"{knots:F0} KT", _text);
            _text.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(margin + cell, y, cell, row), $"{feet:F0} FT", _text);
            _text.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(margin + 2 * cell, y, cell - fs * .5f, row), $"{g:F1} G", _text);

            if (visible)
            {
                float radius = 13 * scale;
                GUI.color = tint;
                if (armed)
                {
                    _ring ??= MakeRing();
                    GUI.DrawTexture(new Rect(p.x - radius, p.y - radius, radius * 2, radius * 2), _ring);
                }
                GUI.DrawTexture(new Rect(p.x - 5 * scale, p.y - scale, 10 * scale, 2 * scale), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(p.x - scale, p.y - 5 * scale, 2 * scale, 10 * scale), Texture2D.whiteTexture);
            }
            GUI.EndGroup();
            GUI.color = oldColor;
        }

        private static void Box(Rect r, float alpha)
        {
            GUI.color = new Color(.015f, .025f, .04f, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static Texture2D MakeRing()
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1.5f - Mathf.Abs(distance - 29f))));
            }
            t.Apply();
            return t;
        }

        private void OnDestroy() { if (_ring != null) Destroy(_ring); }
    }
}
