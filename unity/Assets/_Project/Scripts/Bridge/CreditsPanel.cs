using System.IO;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Credits, and specifically the ATTRIBUTION the model licences require.
    ///
    /// 15 of the fleet's 17 downloaded models are CC Attribution 4.0 and 2 are Sketchfab Free Standard. CC BY
    /// permits commercial use — that was never the problem — but it requires the credit to reach the person
    /// USING the work, and until 2026-10-21 the only place a credit existed was `docs/CREDITS.md`, a file in a
    /// source repository that no player will ever open. That is a live compliance gap, not a tidiness one, and
    /// it stays live for as long as a downloaded model ships (owner decision 2026-10-21: the generated fleet
    /// becomes the shipping look, but each type switches over only when it is good enough, so the models are in
    /// the build for a while yet — see docs/pending/OWN-AIRFRAMES-PLAN.md).
    ///
    /// The text is `StreamingAssets/credits.md`, copied from `docs/CREDITS.md` and checked byte-for-byte by
    /// `GlideTableTests.CreditsReachTheUser` — the same copy-and-gate pattern the aircraft configs use, so the
    /// shipped credit cannot drift from the one in the repo.
    ///
    /// Deliberately plain: a scrolling monospace wall of text with a CLOSE button. Attribution is a legal
    /// obligation to discharge clearly, not a screen to design.
    /// </summary>
    public sealed class CreditsPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        public static void Toggle() => IsOpen = !IsOpen;
        public static void Close() => IsOpen = false;

        private string _text;
        private Vector2 _scroll;
        private GUIStyle _btn, _title, _body;
        private Texture2D _bg;
        private float _fs;

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        private void EnsureStyles()
        {
            float s = Mathf.Min(Screen.width, Screen.height);
            float fs = Mathf.Round(s * 0.028f);
            if (_body != null && Mathf.Approximately(fs, _fs)) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.02f, 0.03f, 0.05f, 0.97f));
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(fs * 1.5f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white },
            };
            // Monospace so the markdown tables in CREDITS.md line up instead of turning to mush.
            _body = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(fs * 0.82f), alignment = TextAnchor.UpperLeft, wordWrap = true,
                richText = false, font = Font.CreateDynamicFontFromOSFont("Courier", Mathf.RoundToInt(fs * 0.82f)),
                normal = { textColor = new Color(0.86f, 0.88f, 0.92f) },
            };
            _btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(fs), fontStyle = FontStyle.Bold };
        }

        /// <summary>Read once, and never let a missing file hide the obligation: if the credits cannot be shown,
        /// say so on the screen rather than showing an empty panel that looks like there is nothing to credit.</summary>
        private void Load()
        {
            if (_text != null) return;
            string path = Path.Combine(Application.streamingAssetsPath, "credits.md");
            try
            {
                _text = File.ReadAllText(path);
            }
            catch (System.Exception e)
            {
                _text = "The credits file could not be read, which is a bug — every 3-D model in this game is used "
                        + "under a licence that requires its author to be credited here.\n\nThe full list is in "
                        + "docs/CREDITS.md in the project, and in a LICENSE.txt beside each model.\n\n(" + path + ": " + e.Message + ")";
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();
            Load();

            float s = Mathf.Min(Screen.width, Screen.height);
            float m = s * 0.03f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bg);

            float lh = _fs * 1.6f;
            GUI.Label(new Rect(m, m * 0.7f, Screen.width - 2 * m, lh * 1.3f), "CREDITS", _title);

            float bw = s * 0.2f, bh = lh * 1.1f;
            if (GUI.Button(new Rect(Screen.width - m - bw, m * 0.7f, bw, bh), "CLOSE", _btn)) Close();

            float top = m * 0.7f + lh * 1.6f;
            var view = new Rect(m, top, Screen.width - 2 * m, Screen.height - top - m);
            float contentW = view.width - s * 0.05f;
            float contentH = _body.CalcHeight(new GUIContent(_text), contentW);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, contentW, contentH));
            GUI.Label(new Rect(0, 0, contentW, contentH), _text, _body);
            GUI.EndScrollView();
        }
    }
}
