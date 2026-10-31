# Weight and balance, engine systems, engine reliability

Three owner requests (2026-10-30) that turn out to share one foundation. This page is the plan, the decisions, and
what was found while checking the ground before building on it.

## Why they are one piece of work

Searching the tree for engine state returned **nothing**: no oil pressure, no oil temperature, no manifold pressure,
no CHT, no mixture input, no fuel quantity anywhere in `src/` or the Unity scripts. That is why the Extra 300's four
gauges read dead — there is nothing behind them to read. "Oil pressure falling while temperature rises" has nothing
to fall.

And for weight and balance: mass and inertia are already runtime-mutable on `Aircraft` (there is precedent — losing
a wing rescales inertia at `Aircraft.cs:473`), but **CG is not**. It is `Config.Mass.CgVec()`, static JSON, read in
**93 places across 41 files**. CG position is exactly what changes the handling the owner wants adjusted.

So all three requests sit on one substrate: per-flight mutable mass, fuel and engine state. Fuel is the hinge — W&B
needs tanks to load, engine systems need fuel flow, and fuel burn is what makes the CG move in flight.

**Order (owner's call, 2026-10-30): weight and balance first, then engine systems, then the failure modes on top.**

## 1. Engine reliability — DONE (`29fe6bb`)

`src/FlyingGame.Core/Engine/EngineReliability.cs`. Built first because it is needed in every ordering and depends on
nothing. See the file's own comments for the full reasoning; in short:

- A hazard rate, not a dice roll per cause: `lambda = m/3700` per hour, `P(step) = 1 - exp(-lambda*dt)`.
- Each cause owns a **share** of that one rate, weights summing to exactly 1. Adding, removing or reweighting causes
  changes *what* breaks, never *how often*. Measured MTBF over 74 million simulated hours: **3700.6 h against 3700**.
- `m` clamped to `[1, 1000]`: worse, never better than the fleet.
- Carb ice, vapour lock, fuel exhaustion and detonation are **deliberately not in the pool** — they are conditional,
  not random. Putting them in would stop them happening when conditions warranted *and* steal probability from the
  mechanical causes, making the engine quietly more reliable than 1-in-3700 in the ways that matter.
- **Armed in free flight only by default** (owner's call): a scripted lesson should not be ruined by a 1-in-3700 draw.

Still to come, on top of the engine systems: carb ice (conditions table, needs induction type per aircraft), vapour
lock, the surge model, and the symptom plumbing.

## 2. Weight and balance — NEXT

### Shape

New config block per aircraft:

- `empty`: empty mass, empty CG, empty inertia. Everything else is built up from it.
- `stations[]`: pilot, passengers, baggage, cargo — id, display name, arm (body axes), max kg, default kg.
- `tanks[]`: id, name, arm, capacity, fuel density, selectable.
- `envelope[]`: the CG limit polygon as (cg arm, mass) vertices, plus max take-off mass.

Runtime load sheet: what is in each station and tank now. It computes total mass, CG in all three axes (lateral too,
so asymmetric fuel is felt), and inertia by parallel axis from the empty aircraft plus point masses.

### How the computed CG reaches the 93 read sites

`AircraftConfig` instances are **not shared**: `UnityAircraftConfigLoader.LoadFromStreamingAssets` deserializes fresh
on every call, and there is already precedent for mutating the instance per session (`config.Bushwheels`,
`config.Skis`). So the load sheet writes computed mass, CG and inertia into the aircraft's own config, and all 93
read sites pick it up with no change. That is a far smaller and safer diff than threading a CG argument through
41 files.

### The one hazard, found by checking

Seven per-config caches are keyed on the instance (`ConditionalWeakTable<AircraftConfig, …>`), so a mutated config
could leave a stale cached value. Six do not touch mass at all. **One does**: `AeroModel._span` caches
`[span, meanWingZ - cg.Z]` — the wing's height above the CG, which is the **ground-effect** geometry. Move the CG
vertically (wing fuel against low baggage) and that cached height goes stale, silently, in ground effect only.

Fix, before anything else in this phase: cache `meanWingZ` alone and subtract the live CG z at the point of use. One
line, and the staleness cannot come back. Noted here because it is the kind of thing that would otherwise be found
as a strange landing-flare bug months later.

### What else needs doing

- Fuel burn from power (BSFC), reducing tank mass and moving the CG in flight.
- Envelope test and the plotted diagram. Charts in this project never show own-ship position, but a W&B envelope plot
  is not a navigation chart — it must show the loaded point, or it is useless.
- A loading screen before flight, and the handling consequences: static margin, stall speed, climb, and the
  aft-CG spin behaviour that makes a loading mistake interesting rather than cosmetic.

## 3. Engine systems — AFTER W&B

Oil (quantity, pressure, temperature), CHT, manifold pressure, mixture, fuel flow, induction type per aircraft. This
is what backs the Extra's four dead gauges, and what the failure symptoms act on.

Induction type has to be recorded per type, because carb ice can only happen to a carburettor and vapour lock is
mostly an injected engine's problem. The fleet splits roughly: carburetted — J-3, PA-18, PA-28, the radials
(Stearman, Jenny, Beaver, DC-3, Gee Bee), Cassutt, Pawnee, Seminole, AirCam; injected — Extra, Pitts, Decathlon,
Great Lakes, SR22, Glasair; neither — the jets, the gliders and the soft wings. **Each of those wants checking against
the type's own engine designation before it is written down**, not taken from this paragraph.

## Visual effects

Oil on the windscreen, engine smoke and cockpit electrical smoke are art, and art belongs to Codex (owner's call,
2026-10-30). This lane publishes the state and the handoff spec — oil-on-screen fraction, smoke density and source,
vibration amplitude — and Codex draws it.

## Sources for the reliability figures

The weights in `EngineReliability.Table` are an allocation, not a measurement, and the file says so. Background read
while sizing them:

- NTSB/AOPA accident record, via [AVweb's summary of engine-failure causes](http://avweb.com/flight-safety/accidents-ntsb/why-engines-quit-failures-are-avoidable/)
  and [Flying's "When Engines Fail"](https://www.flyingmag.com/when-engines-fail/) — fuel-related is the largest
  single bucket (27–43 % across studies), catastrophic mechanical failure about a quarter.
- [A 1989–2023 fatal-accident study summary](https://generalaviationnews.com/2025/12/18/study-finds-equipment-failures-continue-driving-ga-fatal-accidents/)
  — 34 % of failed components were crankshaft, connecting rod or piston.
- Carburettor icing envelope: the FAA chart in **SAIB CE-09-35**, also PHAK fig. 7-12, mirrored by
  [CASA](https://www.casa.gov.au/sites/default/files/2022-10/carburettor-icing-probability-chart.pdf) and
  [the ATSB](https://www.atsb.gov.au/sites/default/files/2025-06/carburettor-icing-probability-chart.pdf).
  **Not yet read**: the fetch timed out, and a reproduced version circulating online is known to be wrong (it shows
  dew point above ambient temperature, which is impossible). The envelope table must be built from the SAIB itself.

These count **accidents**, not failures, and most of their fuel share is exhaustion and mismanagement, which the
random pool excludes. A service-difficulty-report or warranty-claim distribution would be the right source and has
not been found.
