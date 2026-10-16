using FlyingGame.Sim.Commerce;
using UnityEngine;

namespace FlyingGame.Bridge.Commerce
{
    /// <summary>
    /// The store screen: what unlocks the airplanes, and what lets you fly online. IMGUI, like every other
    /// panel in this project (OptionsPanel, StartMenu), so it matches and needs no scene wiring.
    ///
    /// Three rules it follows, all three enforced by App Review:
    ///  - the price shown is the STORE's localised price string, never a number from our side;
    ///  - Restore Purchases is always reachable, not hidden behind a failed purchase;
    ///  - what each thing includes, its length and its renewal are stated on the screen that takes the money,
    ///    with the terms and privacy links beside them.
    /// And one rule of our own: it never blocks single player. Someone who has paid and is merely offline
    /// gets a line of explanation, not a wall.
    ///
    /// There is nothing here about hours. The pass is unlimited (owner 2026-10-16), so the screen has no
    /// allowance to report and nothing to sell anybody who has already bought it.
    /// </summary>
    public sealed class Paywall : MonoBehaviour
    {
        public bool Open;
        public string TermsUrl = "", PrivacyUrl = "";

        private enum Tab { Unlock, Online }
        private Tab _tab = Tab.Unlock;
        private GUIStyle _btn, _btnOn, _head, _label, _small;
        private Texture2D _bg, _btnBg, _btnOnBg;
        private int _fs;
        private string _note = "";

        private static EntitlementRuntime Rt => EntitlementRuntime.Instance;

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void EnsureStyles()
        {
            int fs = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.026f);
            if (_btn != null && fs == _fs) return;
            _fs = fs;
            _bg ??= Solid(new Color(0.05f, 0.08f, 0.12f, 0.96f));
            _btnBg ??= Solid(new Color(0.18f, 0.24f, 0.32f, 0.95f));
            _btnOnBg ??= Solid(new Color(0.2f, 0.62f, 0.35f, 0.95f));
            Font f = UiFont.Get();
            _btn = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white, background = _btnBg }, active = { textColor = Color.white, background = _btnBg }, padding = new RectOffset(8, 8, 6, 6) };
            _btnOn = new GUIStyle(_btn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = _btnOnBg }, active = { textColor = Color.white, background = _btnOnBg } };
            _head = new GUIStyle { font = f, fontSize = Mathf.RoundToInt(fs * 1.25f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.55f, 0.95f, 1f) } };
            _label = new GUIStyle { font = f, fontSize = fs, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = new Color(0.88f, 0.91f, 0.95f) } };
            _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(fs * 0.82f), normal = { textColor = new Color(0.62f, 0.68f, 0.76f) } };
        }

        private void OnGUI()
        {
            if (!Open || Rt == null) return;
            EnsureStyles();
            float w = Mathf.Min(Screen.width * 0.86f, 720f), h = Mathf.Min(Screen.height * 0.84f, 760f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(panel, _bg);
            float pad = _fs, x = panel.x + pad, y = panel.y + pad, cw = w - pad * 2;

            GUI.Label(new Rect(x, y, cw, _fs * 1.6f), "AERO PLAYGROUND", _head); y += _fs * 2f;

            // Tabs.
            float bw = (cw - pad * 0.5f) / 2f;
            if (GUI.Button(new Rect(x, y, bw, _fs * 1.9f), "Airplanes", _tab == Tab.Unlock ? _btnOn : _btn)) _tab = Tab.Unlock;
            if (GUI.Button(new Rect(x + bw + pad * 0.5f, y, bw, _fs * 1.9f), "Flying online", _tab == Tab.Online ? _btnOn : _btn)) _tab = Tab.Online;
            y += _fs * 2.6f;

            if (_tab == Tab.Unlock) y = UnlockTab(x, y, cw);
            else y = OnlineTab(x, y, cw);

            if (!string.IsNullOrEmpty(StoreKitBridge.Error))
            {
                GUI.Label(new Rect(x, y, cw, _fs * 3f), StoreKitBridge.Error, _label); y += _fs * 3f;
            }
            if (!string.IsNullOrEmpty(_note))
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.4f), _note, _label); y += _fs * 2.4f;
            }

            // Always reachable, both tabs.
            float by = panel.yMax - pad - _fs * 1.9f;
            float rw = (cw - pad) / 2f;
            if (GUI.Button(new Rect(x, by, rw, _fs * 1.9f), "Restore purchases", _btn)) { StoreKitBridge.Restore(); _note = "Asking the App Store for anything you have already bought…"; }
            if (GUI.Button(new Rect(x + rw + pad, by, rw, _fs * 1.9f), "Close", _btn)) Open = false;

            float ly = by - _fs * 1.6f;
            if (!string.IsNullOrEmpty(TermsUrl) || !string.IsNullOrEmpty(PrivacyUrl))
            {
                float lw = (cw - pad) / 2f;
                if (!string.IsNullOrEmpty(TermsUrl) && GUI.Button(new Rect(x, ly, lw, _fs * 1.4f), "Terms of use", _small)) Application.OpenURL(TermsUrl);
                if (!string.IsNullOrEmpty(PrivacyUrl) && GUI.Button(new Rect(x + lw + pad, ly, lw, _fs * 1.4f), "Privacy policy", _small)) Application.OpenURL(PrivacyUrl);
            }
        }

        private float UnlockTab(float x, float y, float cw)
        {
            var c = Rt.Content;
            // Say where they stand first. Somebody who has already paid should not be sold to.
            GUI.Label(new Rect(x, y, cw, _fs * 2.6f), c.Reason, _label); y += _fs * 2.8f;

            if (c.Level == AccessLevel.Unlocked) return y;

            if (c.Level == AccessLevel.Trial)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 3.4f),
                    $"Everything is open until {c.TrialEndsUtc.ToLocalTime():d}. You can unlock it for good any "
                    + "time — and what you have earned with points stays either way.",
                    _label);
                y += _fs * 3.6f;
            }
            else
            {
                GUI.Label(new Rect(x, y, cw, _fs * 5.4f),
                    "Every airplane and every challenge can be earned by flying: points come from landings, gates, "
                    + "altitude gained in the glider, places worth finding. Or unlock the lot at once. Lessons are "
                    + "never bought — a lesson is passed by flying it to the standard.",
                    _label);
                y += _fs * 5.6f;
            }

            if (!StoreKitBridge.Ready)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.2f), "Reaching the App Store…", _label);
                return y + _fs * 2.4f;
            }

            bool busy = !string.IsNullOrEmpty(StoreKitBridge.Purchasing);

            // The free trial, while it has never been started.
            var trial = Rt.TrialOffer;
            if (c.TrialAvailable && trial.Valid)
            {
                GUI.enabled = !busy;
                if (GUI.Button(new Rect(x, y, cw, _fs * 2.2f), busy ? "Working…" : "Try everything free for 14 days", _btnOn))
                { StoreKitBridge.Purchase(trial.Id); _note = ""; }
                GUI.enabled = true;
                y += _fs * 2.4f;
                GUI.Label(new Rect(x, y, cw, _fs * 1.6f), "Single player. Flying online is separate.", _small);
                y += _fs * 1.8f;
            }

            var unlock = Rt.UnlockOffer;
            if (!unlock.Valid)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.4f), "The App Store is not reachable right now. Single player works offline.", _label);
                return y + _fs * 2.6f;
            }

            GUI.enabled = !busy;
            // The store's own localised price string — never a number from our side.
            if (GUI.Button(new Rect(x, y, cw, _fs * 2.2f), busy ? "Working…" : $"Unlock everything — {unlock.Price} once",
                           c.TrialAvailable ? _btn : _btnOn))
            { StoreKitBridge.Purchase(unlock.Id); _note = ""; }
            GUI.enabled = true;
            y += _fs * 2.4f;
            GUI.Label(new Rect(x, y, cw, _fs * 1.6f), "One payment, not a subscription. Includes a month of flying online.", _small);
            y += _fs * 1.8f;

            if (GUI.Button(new Rect(x, y, cw, _fs * 1.9f), "Sign in with an existing membership", _btn))
                _note = "Not available yet — the provider handshake is still being arranged.";
            return y + _fs * 2.2f;
        }

        private float OnlineTab(float x, float y, float cw)
        {
            var o = Rt.Online;

            GUI.Label(new Rect(x, y, cw, _fs * 2.6f), o.Reason, _label); y += _fs * 2.8f;

            GUI.Label(new Rect(x, y, cw, _fs * 4.4f),
                "Flying with other people goes through a relay we rent by the hour, so it is separate from the "
                + "airplanes and everyone who flies online needs it. Fly online as much as you like while it "
                + "runs. Single player, the lessons, and flying together over the same Wi-Fi never need it.",
                _label);
            y += _fs * 4.6f;

            if (!StoreKitBridge.Ready)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.2f), "Reaching the App Store…", _label);
                return y + _fs * 2.4f;
            }

            if (o.Permitted) return y;

            bool busy = !string.IsNullOrEmpty(StoreKitBridge.Purchasing);

            // No pass: offer the durations the store knows about, in the order config lists them.
            foreach (string id in Rt.PassProductIds)
            {
                var p = StoreKitBridge.Offer(id);
                if (!p.Valid) continue;
                GUI.enabled = !busy;
                if (GUI.Button(new Rect(x, y, cw, _fs * 2.2f), busy ? "Working…" : $"{p.Title} — {p.Price}", _btnOn))
                { StoreKitBridge.Purchase(p.Id); _note = ""; }
                GUI.enabled = true;
                y += _fs * 2.4f;
            }
            GUI.Label(new Rect(x, y, cw, _fs * 1.8f),
                "It renews until you cancel, and you can cancel any time in your Apple ID settings.", _small);
            return y + _fs * 2f;
        }
    }
}
