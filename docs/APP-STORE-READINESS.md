# App Store readiness — the technical side

What the client needs in place before a build can go to review, and what is already done. Pricing, agreements,
tax and banking, and the App Store Connect record itself are **not** here: they are owner-only steps, and the
numbers stay out of this repository because it is public.

## Done

| Item | Where |
|---|---|
| Export compliance declared (standard HTTPS/WSS is exempt) | `BuildScript.OnPostProcessBuild` sets `ITSAppUsesNonExemptEncryption = false`, so TestFlight builds do not wait on the compliance question |
| Usage descriptions (microphone, photo library add, local network) | same hook |
| Privacy manifest | `unity/Assets/Plugins/iOS/PrivacyInfo.xcprivacy`, added to the **app** target (Apple reads it from the app bundle, and Unity would otherwise bury it in UnityFramework) |
| StoreKit 2 linked, Swift 5 configured, Swift runtime embedded | same hook — iOS 15 deployment target means the runtime cannot be assumed present |
| Entitlement rules, offline grace, clock-tamper resistance | `src/FlyingGame.Sim/Commerce/Entitlement.cs` |
| Trial, one-time unlock, the unlock's included month, external membership | `src/FlyingGame.Sim/Commerce/ContentAccess.cs` |
| The relay cost counters (connected hours and sessions per pilot, on the device) | `src/FlyingGame.Sim/Commerce/AirtimeMeter.cs` |
| Apple's signed transaction (JWS) carried up to the join check | `Plugins/iOS/AeroStore.swift` → `Bridge/Commerce/StoreKitBridge.cs` → `EntitlementRuntime.MultiplayerJws()` |
| Paywall with the store's own localised prices, and Restore Purchases always reachable | `Bridge/Commerce/Paywall.cs` |
| Product identifiers out of source | `unity/Assets/StreamingAssets/commerce.json` |

## Required reason APIs

The three declared in the manifest are the three this app actually uses: `NSUserDefaults` (CA92.1) for
settings and the airtime counters, file timestamps (C617.1) for listing saved clips, and free disk space (E174.1)
for refusing to start a recording that would fill the device. If a new plugin or package adds another, the
manifest has to grow with it — Apple rejects on the manifest, not on the code.

## Not done, and whose it is

**Owner only.** Pricing and the subscription product set in App Store Connect; the paid-apps agreement, tax
and banking; the age rating questionnaire; the App Store privacy answers (they must match the manifest);
screenshots and the description; and pressing Submit. No build is uploaded without the owner's say-so.

**Blocking, and not an engineering task.** The published privacy policy currently says data is not collected
and there are no accounts. That stays true for everything bought through the App Store — StoreKit handles the
purchase and this app keeps an expiry date and a count of hours flown on the device, sending neither
anywhere. The hours are ours to look at, not anybody's allowance: the pass is unlimited, so no server needs to
know how long a pilot has flown, which is what keeps this answer easy to defend.

It also stays true for the multiplayer join check as designed: the app shows the relay Apple's signed
transaction over TLS, the relay verifies it against Apple's keys and returns a short-lived join token, and
nothing from that transaction is stored or logged on either side. Nothing in it is read by us, and no account
of ours exists. **That has to stay true in the implementation, not just the design** — the moment the relay
keeps so much as the transaction id in a log line, the policy answer changes.

It stops being true the moment external sign-in ships, which is accounts. See `docs/EXTERNAL-ENTITLEMENT.md` §6.

**Still to wire.** The paywall is not attached to a scene object, and nothing is attached to the multiplayer
transport — `EntitlementRuntime.CanJoinMultiplayer`, `MultiplayerJws`, `BeginMultiplayerSession` and
`RecordMultiplayerTime` are written but nothing calls them. Until they are called, nothing is gated and no
hours are counted.

## The product catalogue

Settled with the owner on 2026-10-15 and simplified on 2026-10-16, when the hour cap and the hours top-up
were dropped and the pass became unlimited. Identifiers and the rules that go with them are in
`unity/Assets/StreamingAssets/commerce.json`; prices are not in this repository at all.

| Product | Kind | What it does |
|---|---|---|
| `trial.14day` | $0 non-consumable | Starts the fourteen-day trial. Everything, **single player only**. A purchase rather than an introductory offer, so Apple holds the start date and it survives a reinstall, a new device and a wound-back clock. |
| `unlock` | Non-consumable | Every airplane and lesson, permanently, plus the **first 30 days** of online flying. |
| `multiplayer.monthly`, `multiplayer.yearly` | Auto-renewing, ONE group | The online pass, **unlimited**. Needed by everyone who flies online, external-provider members included. |

There is no consumable and no hours product. The app still counts each pilot's connected hours and sessions on
the device, because the relay is billed by the hour and that number is how we know whether the pass covers
what it costs — but nothing is gated on it and nothing leaves the device.

Rules in code, each with tests: the trial does not include multiplayer; an external membership grants the
unlock's content for as long as the membership lasts and nothing more; the pass is the only question at a join
and the meter has no way to answer "no"; a family-shared copy of any of these is ignored, because Family
Sharing is off for all of them in App Store Connect and "cannot happen" is not a check.

Free, before any purchase: the trainer glider and the C-172, and the lessons and challenges that start you
earning. Everything else is earned with points or bought. **Points buy aircraft and challenges only — a
lesson is passed by flying it to the standard, never bought.** The points table is a draft awaiting the
owner's approval in `configs/progression.json`, with the reasoning and a survey of which named places exist
in `docs/POINTS.md`. No code earns, stores or spends a point yet.

**Owner-only, and not yet done:** creating these five products in App Store Connect. Worth doing once and
carefully — a product identifier cannot be changed after it is created, and Family Sharing must be off for
the unlock and both pass durations.
