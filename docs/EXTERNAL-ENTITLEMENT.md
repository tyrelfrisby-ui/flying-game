# External entitlement providers

**Status: design only. Nothing here is built, agreed, or announced.** No API exists yet, no contact has been
made, and every open question below is the owner's to answer. This document exists so the client-side shape is
decided before anyone talks to anyone — it is a technical specification, not a plan of record.

Commercial terms, prices and anything about a business relationship are deliberately **not** in this file. The
repository is public. Those live in the owner's own notes and in the bridge log.

## 1. Why this is an abstraction rather than one provider's integration

Aero Playground needs to answer one question at runtime: **may this person fly the paid content right now?**
The answer can come from several places, and the places differ in latency, failure mode and trust:

| Provider | Where the truth lives | Can it fail offline? |
|---|---|---|
| `AppStoreSubscription` | StoreKit 2 on the device | No — the receipt is local |
| `External` | a partner's server, via ours | Yes |
| `Comp` | a signed grant we issue (press, testers, the owner) | No — the grant is local |

So the client never asks "is this a member of some particular service?" It asks the **entitlement layer**,
which consults providers in order and returns a single resolved state. An external provider becomes one
implementation of `IEntitlementProvider`, and adding a second one later is a new class, not a new concept.

The handshake below is what we would need from **any** subscription service whose members get access; the only
provider-specific part is which of the three shapes a given service can actually offer.

> This document deliberately does not name the provider. Naming a prospective partner in a public repository
> discloses business intent before any approach has been made, and the engineering is identical either way.
> The real name lives in the owner's own notes. (Owner's decision, 2026-10-15.)

## 2. The handshake, in order of preference

What we need is small: **a trustworthy statement that a person is a member in good standing, and when that
statement expires.** We do not want their name, their email, their viewing history, or any other profile data,
and we should refuse it if offered — see §6.

### Option A — Sign in with the provider (OAuth 2.0 / OIDC). Preferred.

```
app ──ASWebAuthenticationSession──> provider authorize endpoint
                                      (user signs in there, never in our app)
app <──── redirect: aeroplayground://auth?code=…&state=… ────┘
app ──── code + PKCE verifier ────> OUR account service
                                      exchanges the code provider-side,
                                      reads the entitlement claim,
                                      mints OUR OWN signed entitlement
app <──── our signed entitlement (sub, ent, exp, iat) ────┘
```

Why this one: the password is only ever typed into the provider's own web view, PKCE means a stolen redirect is
useless, and the code exchange happens server-side so a client secret never ships in the app. It is also the
only option that gives us a revocation story without a webhook.

Requirements on the provider: a standard authorization-code endpoint with PKCE (S256), a token endpoint, a
JWKS endpoint, a registered custom-scheme or universal-link redirect, and one claim — call it
`aero_playground` — plus its expiry. An `offline_access` refresh token is useful but not required; our own
entitlement carries the offline window instead.

### Option B — A signed entitlement token on a deep link.

```
provider app ──> aeroplayground://link?token=<JWT>
```

The JWT carries `sub` (an **opaque** member id, not an email), the entitlement claim, `iat`, `exp`, and a
`jti`. We verify it against the provider's JWKS (`RS256` or `ES256`; never `HS256` with a shared secret, and
never `alg: none`), reject anything older than a few minutes, and remember the `jti` until `exp` so a copied
link cannot be replayed. Short expiry — minutes, not days — because a deep link lands in logs, pasteboards and
screenshots.

Simpler for them, weaker for us: no revocation, and the token is only as fresh as the last launch.

### Option C — Webhook plus a one-time linking code.

The person reads a short code in the provider's app and types it into ours; we redeem it server-side, and the
provider posts subscription changes to a signed webhook endpoint. Acceptable as a fallback, and the only one
that works if they have no OAuth surface at all. The linking code must be single-use, rate-limited and
shorter-lived than a minute of patience — six characters for ten minutes.

### What all three share

- **We mint our own entitlement.** Whatever the provider gives us is converted, server-side, into a short
  signed token of our own with a known shape. The client only ever verifies ours. This is what lets the three
  options share one code path, and it keeps the provider's key material off the device.
- **Re-check every 24 h, or on webhook.** Sooner if the app is launched after the cached entitlement expires.
- **Signed for offline use**, so a flight on a plane with no signal still works. See §4.

## 3. Account linking, and unlinking

Linking is per-Apple-ID-install, stored in the keychain, and **reversible from inside the app**. The settings
screen shows which provider is currently granting access, when it was last confirmed, and an Unlink button.

In-app account deletion is **required** by App Review 5.1.1(v) once an account exists at all. If we sign people
in, we must also let them delete — not just unlink, but delete the account on our account service, with the
provider link removed. That is a server endpoint and a confirmation screen, and it is not optional.

A person may be entitled by more than one provider at once (an App Store subscriber who also links a
membership). The layer resolves to the **longest-lived** grant and says which one it used, so nobody is told
they have no access while holding two.

## 4. Offline, and failure

The rule: **a provider that cannot be reached must never cost someone a flight they have paid for.**

- A verified entitlement is cached, signed, with its own `exp` and a grace window beyond it. Inside the window,
  single player behaves exactly as if online.
- Past the window, with no reachable provider: single player keeps working in full. This is a flight trainer;
  taking the airplane away from someone over a network error would be the wrong call every time.
- Multiplayer is different, because it runs through a relay we rent. It is not part of what an external
  provider's statement grants, and whether it ever should be is an open owner question (§7). It is checked
  live — the join needs a signed transaction verified at that moment — so it is withheld when nothing can be
  checked, with a clear reason rather than a silent failure.
- The cached entitlement is signed and clock-checked, so moving the device clock forward does not extend it,
  and moving it backward does not either.

## 5. What a tile in their app would need

A "Games" tile deep-linking to `aeroplayground://link?...` (option B's shape) or to an App Store product
page with a campaign token. From our side this needs: a universal-link domain with an
`apple-app-site-association`, a fallback web page for anyone without the app installed, and artwork at whatever
sizes they ask for. Nothing in the client is specific to the tile; it is the same link handler as option B.

## 6. Privacy, and the thing that must change first

**The live privacy policy currently says data is not collected and that there are no accounts. Both stop being
true the moment any of this ships.** The policy, the App Store privacy answers and the `PrivacyInfo.xcprivacy`
manifest all have to be updated **before** a build with sign-in goes to review, not after. This is a release
blocker, and it belongs to whoever owns the published policy.

What we should collect: an opaque member id, an entitlement flag, an expiry. That is the whole list. Not an
email address, not a name, not a viewing history — if the provider's token carries extra claims we drop them
server-side and never store them, because data we do not hold cannot leak and does not need disclosing.

No third-party analytics or attribution SDK rides along with this work.

## 7. The stub provider

Behind a compile flag and off by default, so the entitlement layer can be exercised end to end before any
partner exists:

- `AERO_EXTERNAL_STUB` — an `IEntitlementProvider` that reads a local JSON file (entitled, expiry, provider
  name) and answers from it, including the failure cases: expired, revoked, unreachable, malformed, clock-skewed.
- It must be impossible to enable in a release build. The flag is not in the shipping build configuration, and
  there is a test that asserts the stub is absent from a release assembly.

That ordering matters: the offline behaviour in §4 and the resolution rules in §3 are the parts most likely to
be wrong, and both can be driven from the stub without a line of partner code.

## 8. Still the owner's call

- Whether to approach a partner at all, and which handshake to ask for.
- Whether this file names the provider publicly (§1).
- Whether multiplayer stays withheld offline, or gets a short courtesy window.
- What an external provider's members get for multiplayer, which the business side has flagged as undecided:
  nothing extra (they buy it from us like everyone else), or some arrangement that is part of the deal.

---

*See also: `docs/DATA-CONTRACTS.md` for the shape of our own signed entitlement, and `docs/ARCHITECTURE.md`
for where the entitlement layer sits relative to the sim.*
