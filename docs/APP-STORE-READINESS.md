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
| Entitlement rules, offline grace, clock-tamper resistance | `src/FlyingGame.Sim/Commerce/Entitlement.cs`, 11 tests |
| Multiplayer data metering and purchase crediting | `src/FlyingGame.Sim/Commerce/DataMeter.cs`, 11 tests |
| Paywall with the store's own localised prices, and Restore Purchases always reachable | `Bridge/Commerce/Paywall.cs` |
| Product identifiers out of source | `unity/Assets/StreamingAssets/commerce.json` |

## Required reason APIs

The three declared in the manifest are the three this app actually uses: `NSUserDefaults` (CA92.1) for
settings and the data balance, file timestamps (C617.1) for listing saved clips, and free disk space (E174.1)
for refusing to start a recording that would fill the device. If a new plugin or package adds another, the
manifest has to grow with it — Apple rejects on the manifest, not on the code.

## Not done, and whose it is

**Owner only.** Pricing and the subscription product set in App Store Connect; the paid-apps agreement, tax
and banking; the age rating questionnaire; the App Store privacy answers (they must match the manifest);
screenshots and the description; and pressing Submit. No build is uploaded without the owner's say-so.

**Blocking, and not an engineering task.** The published privacy policy currently says data is not collected
and there are no accounts. That stays true for the subscription on its own — StoreKit handles the purchase and
this app keeps an expiry date and a byte count on the device, sending neither anywhere. It stops being true
the moment external sign-in ships. See `docs/EXTERNAL-ENTITLEMENT.md` §6.

**Still to wire.** The paywall is not yet attached to a scene object, and the metering is not yet attached to
the multiplayer transport — `EntitlementRuntime.CanJoinMultiplayer` and `RecordMultiplayerTraffic` are written
and tested but nothing calls them. Until they are called, nothing is gated and nothing is counted. That is the
next piece of work, and it is deliberately the piece that touches the networking code.

## Age rating inputs (for the questionnaire)

Factual answers for whoever fills the form; the rating follows from them. No violence against persons —
the combat content is aircraft-vs-aircraft with no depiction of injury. No blood or gore, no sexual content,
no profanity, no gambling, no alcohol, tobacco or drug references, no horror. Unrestricted web access: no
(the only outbound links are the terms and privacy pages, opened in Safari). User-generated content: no
sharing of user content between players. Contests: no. The voice intercom carries live voice between players
in a shared room, which the form treats as user-to-user communication, so that one is yes and it is the only
yes in the social group.

## Order of work from here

1. Attach the paywall and the gate to the scene and to the transport (above).
2. Sandbox-test every purchase path on a device: buy, cancel, restore, Ask-to-Buy pending, refund/revoke, and
   a renewal while offline. The resolver's rules are unit-tested, but the StoreKit plumbing is not and cannot be.
3. Only then a TestFlight build.
