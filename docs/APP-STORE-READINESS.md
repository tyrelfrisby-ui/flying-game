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
| The layer running in the game: a `Commerce` object with the runtime and the store screen, reachable from the landing page | `SceneBootstrap`, `StartMenu.Store` |
| Online hours counted where they are actually spent | `Bridge/Net/NetSession.cs` — `BeginCounting` on the relay's `hello`, wall-clock seconds while connected, ended on any disconnect |
| The join question asked in the one place that can reach the relay | `NetSession.Join` → `EntitlementRuntime.CanJoinMultiplayer` |

## Required reason APIs

The three declared in the manifest are the three this app actually uses: `NSUserDefaults` (CA92.1) for
settings and the airtime counters, file timestamps (C617.1) for listing saved clips, and free disk space (E174.1)
for refusing to start a recording that would fill the device. If a new plugin or package adds another, the
manifest has to grow with it — Apple rejects on the manifest, not on the code.

## Not done, and whose it is

**Owner only.** The commercial setup in App Store Connect, which is kept out of this public repo (see the
bridge log); the age rating questionnaire; the App Store privacy answers (they must match the manifest);
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

**Wired, but deliberately not enforcing.** The entitlement layer runs in the scene, the store screen opens
from the landing page, and `NetSession` now counts every connected second and asks `CanJoinMultiplayer`
before it opens a socket. It does not act on the answer: `commerce.json` keeps `"gateMultiplayer": false`,
because the products do not exist in App Store Connect yet and switching it on today would refuse everybody —
there would be no pass in the world to hold. Flipping that flag is the same change as making the relay
require a join token; doing one without the other leaves the two halves disagreeing.

**Still to build.** The relay asks for nothing: `EntitlementRuntime.MultiplayerJws()` returns Apple's signed
transaction and nothing sends it, because the relay side is an open decision (see the privacy paragraph
above, and `server/relay/` has no auth at all today). After that, every purchase path has to be walked on a
device against the sandbox — buy, cancel, restore, Ask-to-Buy pending, refund and revoke, and a renewal that
lands while the phone is offline — before any of this goes to TestFlight. The fleet picker also still has no
locks of its own; that is the points work, and it should ask the same layer rather than grow a second answer.

## The product catalogue

The product set, the trial design and the commercial reasoning behind them are NOT in this public
repository -- they are in the private bridge log. What is here is the code and the tests that hold it: the
identifiers and the rules that go with them live in `unity/Assets/StreamingAssets/commerce.json`.

There is no consumable and no hours product. The app counts each pilot's connected hours and sessions on the
device, but nothing is gated on that count and nothing leaves the device.

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
