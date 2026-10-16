using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlyingGame.Bridge.Commerce
{
    /// <summary>One thing the App Store is offering, with the price IT says — localised, current, and never
    /// hardcoded anywhere in this project.</summary>
    public readonly struct StoreOffer
    {
        public readonly string Id, Title, Detail, Price;
        public StoreOffer(string id, string title, string detail, string price) { Id = id; Title = title; Detail = detail; Price = price; }
        public bool Valid => !string.IsNullOrEmpty(Id);
    }

    /// <summary>A verified entitlement StoreKit is currently reporting.</summary>
    public readonly struct StoreEntitlement
    {
        public readonly string Id;
        public readonly DateTime ExpiresUtc;      // DateTime.MaxValue for a non-expiring purchase
        public readonly bool Revoked;

        /// <summary>Apple's own signed transaction (JWS) for this entitlement, exactly as StoreKit handed it
        /// over. It is what the relay is shown at join to prove the Multiplayer Pass without us keeping an
        /// account: Apple signed it, so anyone holding Apple's public keys can check it, and we never have to
        /// store it. Treat it as a bearer token — hand it to the relay over TLS, never log it, never persist
        /// it. Empty off device.</summary>
        public readonly string Jws;

        public StoreEntitlement(string id, DateTime expiresUtc, bool revoked, string jws = "")
        { Id = id; ExpiresUtc = expiresUtc; Revoked = revoked; Jws = jws ?? ""; }
    }

    /// <summary>
    /// The C# side of Plugins/iOS/AeroStore.swift. StoreKit 2 is asynchronous and this interop is not, so
    /// nothing is awaited here: the plugin keeps a JSON snapshot and <see cref="Poll"/> reads the latest one.
    /// A purchase is therefore fire-and-forget — the result turns up in a later snapshot, which is also what
    /// happens when a purchase completes hours later after an Ask-to-Buy approval.
    ///
    /// Off iOS (the editor, the Mac build, the widget) every call goes to an in-memory FAKE instead, so the
    /// paywall and the entitlement rules can be built and tested on the Mac. The fake grants nothing unless
    /// a developer explicitly asks it to.
    /// </summary>
    public static class StoreKitBridge
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void AeroStoreStart(string idsCsv);
        [DllImport("__Internal")] private static extern void AeroStorePurchase(string productId);
        [DllImport("__Internal")] private static extern void AeroStoreRestore();
        [DllImport("__Internal")] private static extern void AeroStoreFinish(string txnId);
        [DllImport("__Internal")] private static extern int AeroStoreState(byte[] dst, int max);
        private const bool Native = true;
#else
        private static void AeroStoreStart(string idsCsv) => Fake.Start(idsCsv);
        private static void AeroStorePurchase(string productId) => Fake.Purchase(productId);
        private static void AeroStoreRestore() => Fake.Restore();
        private static void AeroStoreFinish(string txnId) => Fake.Finish(txnId);
        private static int AeroStoreState(byte[] dst, int max) => Fake.State(dst, max);
        private const bool Native = false;
#endif

        private static byte[] _buf = new byte[4096];

        public static bool IsNative => Native;

        /// <summary>True once the store has answered at all. Until then the paywall shows its loading state
        /// rather than claiming there is nothing for sale.</summary>
        public static bool Ready { get; private set; }

        /// <summary>The product currently being bought, or empty. Disables the buttons.</summary>
        public static string Purchasing { get; private set; } = "";

        /// <summary>The last thing that went wrong, in the store's own words, or empty.</summary>
        public static string Error { get; private set; } = "";

        public static IReadOnlyList<StoreOffer> Offers => _offers;
        public static IReadOnlyList<StoreEntitlement> Entitlements => _entitlements;

        /// <summary>Consumables that have arrived and not yet been consumed. Nothing in the app sells a
        /// consumable any more (both products are subscriptions), so this should always be empty; the
        /// entitlement layer finishes anything that does turn up so a stray transaction cannot sit unfinished
        /// for ever. Kept because it is generic store plumbing, not a business model.</summary>
        public static IReadOnlyList<(string productId, string txnId)> PendingConsumables => _pending;

        private static readonly List<StoreOffer> _offers = new();
        private static readonly List<StoreEntitlement> _entitlements = new();
        private static readonly List<(string, string)> _pending = new();

        public static void Start(IEnumerable<string> productIds) => AeroStoreStart(string.Join(",", productIds));
        public static void Purchase(string productId) { Error = ""; AeroStorePurchase(productId); }
        public static void Restore() { Error = ""; AeroStoreRestore(); }
        public static void Finish(string txnId) => AeroStoreFinish(txnId);

        public static StoreOffer Offer(string id)
        {
            foreach (var o in _offers) if (o.Id == id) return o;
            return default;
        }

        /// <summary>Reads the plugin's latest snapshot. Cheap enough to call once a second; there is no reason
        /// to call it per frame.</summary>
        public static void Poll()
        {
            int need = AeroStoreState(_buf, _buf.Length);
            if (need > _buf.Length) { _buf = new byte[need + 1024]; need = AeroStoreState(_buf, _buf.Length); }
            if (need <= 0) return;
            string json = Encoding.UTF8.GetString(_buf, 0, need);
            try
            {
                var o = JObject.Parse(json);
                Ready = o.Value<bool?>("ready") ?? false;
                Purchasing = o.Value<string>("purchasing") ?? "";
                Error = o.Value<string>("error") ?? "";

                _offers.Clear();
                if (o["offers"] is JArray offs)
                    foreach (var x in offs)
                        _offers.Add(new StoreOffer(x.Value<string>("id"), x.Value<string>("title"), x.Value<string>("detail"), x.Value<string>("price")));

                _entitlements.Clear();
                if (o["entitlements"] is JArray ents)
                    foreach (var x in ents)
                    {
                        string exp = x.Value<string>("expiresUtc");
                        DateTime when = string.IsNullOrEmpty(exp)
                            ? DateTime.MaxValue        // a non-expiring purchase
                            : DateTime.TryParse(exp, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
                        _entitlements.Add(new StoreEntitlement(x.Value<string>("id"), when, x.Value<bool?>("revoked") ?? false, x.Value<string>("jws") ?? ""));
                    }

                _pending.Clear();
                if (o["pendingConsumables"] is JArray pend)
                    foreach (var x in pend) _pending.Add((x.Value<string>("id"), x.Value<string>("txn")));
            }
            catch (Exception e)
            {
                // A snapshot we cannot read must not take the game down, and must not silently grant
                // anything either — Ready stays as it was and the entitlement layer keeps its cached answer.
                Debug.LogWarning($"StoreKit snapshot unreadable: {e.Message}");
            }
        }

#if !(UNITY_IOS && !UNITY_EDITOR)
        /// <summary>The off-device stand-in. It grants NOTHING by default: a developer calls
        /// <see cref="GrantForTesting"/> deliberately, so nobody ever ships code that assumed a free pass.</summary>
        public static class Fake
        {
            private static readonly List<string> _ids = new();
            private static string _state = "{\"ready\":false}";
            private static readonly List<(string id, DateTime exp)> _granted = new();

            public static void Start(string idsCsv)
            {
                _ids.Clear();
                foreach (string s in (idsCsv ?? "").Split(',')) if (!string.IsNullOrWhiteSpace(s)) _ids.Add(s.Trim());
                Rebuild();
            }

            /// <summary>Pretend the store sold us something, for development and for the paywall's own tests.</summary>
            public static void GrantForTesting(string productId, DateTime expiresUtc)
            {
                _granted.RemoveAll(g => g.id == productId);
                _granted.Add((productId, expiresUtc));
                Rebuild();
            }

            public static void ClearForTesting() { _granted.Clear(); Rebuild(); }

            public static void Purchase(string productId) => GrantForTesting(productId, DateTime.UtcNow.AddDays(30));
            public static void Restore() => Rebuild();
            public static void Finish(string txnId) { }

            public static int State(byte[] dst, int max)
            {
                byte[] b = Encoding.UTF8.GetBytes(_state);
                if (dst == null || max < b.Length) return b.Length;
                Array.Copy(b, dst, b.Length);
                return b.Length;
            }

            private static void Rebuild()
            {
                var offers = new JArray();
                foreach (string id in _ids)
                    offers.Add(new JObject { ["id"] = id, ["title"] = id, ["detail"] = "(editor stand-in — no real store)", ["price"] = "—" });
                var ents = new JArray();
                foreach (var g in _granted)
                    ents.Add(new JObject { ["id"] = g.id, ["expiresUtc"] = g.exp.ToString("o"), ["revoked"] = false, ["jws"] = "" });
                // No jws off device: there is no Apple signature to hand over, and inventing a plausible-looking
                // one would let a relay check pass in the editor that must fail everywhere real.
                _state = new JObject
                {
                    ["ready"] = true,
                    ["purchasing"] = "",
                    ["error"] = "",
                    ["nowUtc"] = DateTime.UtcNow.ToString("o"),
                    ["entitlements"] = ents,
                    ["pendingConsumables"] = new JArray(),
                    ["offers"] = offers,
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
        }
#endif
    }
}
