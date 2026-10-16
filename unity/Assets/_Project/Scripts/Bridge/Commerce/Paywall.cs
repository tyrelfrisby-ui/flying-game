using FlyingGame.Sim.Commerce;
using UnityEngine;

namespace FlyingGame.Bridge.Commerce
{
    /// <summary>
    /// The subscription screen and the multiplayer-data screen. IMGUI, like every other panel in this
    /// project (OptionsPanel, StartMenu), so it matches and needs no scene wiring.
    ///
    /// Three rules it follows, because App Review enforces all three:
    ///  - the price shown is the STORE's localised price string, never a number from our side;
    ///  - Restore Purchases is always reachable, not hidden behind a failed purchase;
    ///  - what the subscription includes, its length and its renewal are stated on the screen that takes
    ///    the money, with the terms and privacy links beside them.
    /// And one rule of our own: it never blocks single player. Someone who has paid and is merely offline
    /// gets a line of explanation, not a wall.
    /// </summary>
    public sealed class Paywall : MonoBehaviour
    {
        public bool Open;
        public string TermsUrl = "", PrivacyUrl = "";

        private enum Tab { Subscription, Data }
        private Tab _tab = Tab.Subscription;
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
            if (GUI.Button(new Rect(x, y, bw, _fs * 1.9f), "Subscription", _tab == Tab.Subscription ? _btnOn : _btn)) _tab = Tab.Subscription;
            if (GUI.Button(new Rect(x + bw + pad * 0.5f, y, bw, _fs * 1.9f), "Multiplayer data", _tab == Tab.Data ? _btnOn : _btn)) _tab = Tab.Data;
            y += _fs * 2.6f;

            if (_tab == Tab.Subscription) y = Subscription(x, y, cw);
            else y = Data(x, y, cw);

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

        private float Subscription(float x, float y, float cw)
        {
            var res = Rt.Current;
            // Say where they stand first. Somebody who already pays should not be sold to.
            string standing = res.Trust switch
            {
                EntitlementTrust.Fresh => $"Active — renews {res.ExpiresUtc.ToLocalTime():d}. Thank you.",
                EntitlementTrust.Grace => res.Reason,
                EntitlementTrust.Expired => res.Reason,
                _ => "",
            };
            if (!string.IsNullOrEmpty(standing)) { GUI.Label(new Rect(x, y, cw, _fs * 2.4f), standing, _label); y += _fs * 2.6f; }

            if (res.PaidContent)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 5f),
                    "Everything is unlocked. Online multiplayer also needs data, which is metered separately — see the other tab.",
                    _label);
                return y + _fs * 5f;
            }

            GUI.Label(new Rect(x, y, cw, _fs * 7f),
                "A subscription unlocks every airplane, the full lesson syllabus, the flight recorder and online multiplayer. "
                + "It renews each month until you cancel, and you can cancel any time in your Apple ID settings. "
                + "Already subscribe through a service you pay for elsewhere? Sign in with it below instead — no second monthly fee.",
                _label);
            y += _fs * 7.4f;

            var offer = Rt.SubscriptionOffer;
            if (!StoreKitBridge.Ready)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.4f), "Reaching the App Store…", _label);
                return y + _fs * 2.6f;
            }
            if (!offer.Valid)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.4f), "The App Store is not reachable right now. Single player works offline.", _label);
                return y + _fs * 2.6f;
            }

            bool busy = !string.IsNullOrEmpty(StoreKitBridge.Purchasing);
            // The store's own localised price string — never a number from our side.
            string label = busy ? "Working…" : $"Subscribe — {offer.Price} / month";
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(x, y, cw, _fs * 2.2f), label, _btnOn)) { StoreKitBridge.Purchase(offer.Id); _note = ""; }
            GUI.enabled = true;
            y += _fs * 2.6f;

            if (GUI.Button(new Rect(x, y, cw, _fs * 1.9f), "Sign in with an existing membership", _btn))
                _note = "Not available yet — the provider handshake is still being arranged.";
            return y + _fs * 2.2f;
        }

        private float Data(float x, float y, float cw)
        {
            var m = Rt.Meter;
            GUI.Label(new Rect(x, y, cw, _fs * 6f),
                "Flying online goes through a relay, and that data costs money, so it is metered for everyone — "
                + "subscribers included. Single player, the lessons and everything on your own device are never metered.",
                _label);
            y += _fs * 6.2f;

            string left = m.AvailableBytes <= 0 ? "none left"
                : m.SecondsRemaining == double.PositiveInfinity ? $"{m.AvailableMb:0.#} MB left"
                : $"{m.AvailableMb:0.#} MB left — about {Mathf.RoundToInt((float)m.SecondsRemaining / 60f)} min at your current rate";
            GUI.Label(new Rect(x, y, cw, _fs * 1.8f), left, _label); y += _fs * 2f;
            if (m.PlanAllowanceBytes > 0)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 1.6f),
                    $"Plan: {m.PlanRemainingBytes / (1024.0 * 1024.0):0.#} of {m.PlanAllowanceBytes / (1024.0 * 1024.0):0.#} MB this period, resets {m.PlanPeriodEndUtc.ToLocalTime():d}",
                    _small);
                y += _fs * 1.8f;
            }

            if (!StoreKitBridge.Ready)
            {
                GUI.Label(new Rect(x, y, cw, _fs * 2.2f), "Reaching the App Store…", _label);
                return y + _fs * 2.4f;
            }

            bool busy = !string.IsNullOrEmpty(StoreKitBridge.Purchasing);
            foreach (string id in Rt.DataProductIds)
            {
                var o = StoreKitBridge.Offer(id);
                if (!o.Valid) continue;
                GUI.enabled = !busy;
                if (GUI.Button(new Rect(x, y, cw, _fs * 2.1f), busy ? "Working…" : $"{o.Title} — {o.Price}", _btn))
                { StoreKitBridge.Purchase(o.Id); _note = ""; }
                GUI.enabled = true;
                y += _fs * 2.4f;
            }
            GUI.Label(new Rect(x, y, cw, _fs * 1.6f), "Data you buy does not expire.", _small);
            return y + _fs * 1.8f;
        }
    }
}
