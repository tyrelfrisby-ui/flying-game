# Weight and balance

Owner request, 2026-10-30: *"a true weight and balance system where you can load the airplane with occupants,
luggage, or cargo and fuel, and the weight and balance is plotted and the flight characteristics are adjusted
appropriately."*

This page is how it works, where the numbers came from, how to add a type, and the two things found while building
it that are the owner's call rather than mine.

## How it works

`LoadingConfig` on an aircraft config holds the EMPTY aeroplane — its mass, CG and inertia — plus its seats,
baggage holds, tanks and certificated CG envelope. `LoadSheet` holds what is in it right now, adds it all up, and
**writes the result into the aircraft's own `mass` block**. Every one of the ninety-odd places that read the CG then
flies the loaded aeroplane with no change of their own. That is safe because config instances are not shared —
`UnityAircraftConfigLoader.LoadFromStreamingAssets` deserializes a fresh one per call, and the session already
mutates it per flight for bushwheels and skis.

`WeightAndBalance` is the arithmetic: the MAC and where a CG sits on it, the mass/CG/inertia solve (parallel axis,
all three axes, so one full wing tank against one empty one is felt as a rolling moment), and the envelope as a
polygon with point-in-polygon and fore/aft limits at a weight.

A type without a `loading` block flies exactly as it always has, and simply cannot be loaded. Switching weight and
balance on for a type never changes how it flies at its default load — see the rule below.

### The frame: percent MAC

A POH quotes arms and CG limits in inches from that type's own datum — a firewall, a wing leading edge, a point in
space ahead of the propeller. This project's body x has nothing to do with any of them, so the envelope is stored in
**percent MAC**, which is what the limits mean physically and which `WeightAndBalance.PercentMac` measures from the
strips the aeroplane actually flies with.

Mapping a POH station into body x needs exactly **one anchor**, because the scale is already known (an inch is an
inch): `body x = anchor_x − (station − anchor_station) × 0.0254`. The anchor is a point whose station the type
certificate states and whose body x this project's geometry measures. For the 172 that is the leading edge of the
MAC (TCDS 3A12: MAC 58.8 in, its leading edge 25.9 in aft of datum). For the J-3 it is the wing leading edge, which
*is* the datum (TCDS A-691).

**The MAC must be measured from paired sections** — each wing strip plus the aileron hinged behind it. Taking the
strips as they come counts an aileron row as its own narrow little wing and drags the mean chord down: the 172
measured 50 in that way against the 58.8 in on its type certificate, so every percent-MAC figure built on it would
have been out by a sixth. Paired, it measures 59.9 in, which is 1.9 % high and good enough to build on.
`PitchStabilityTests` was computing its own unpaired MAC and so overstated every static margin in the suite by about
8.5 %; it now calls `WeightAndBalance.MeanAerodynamicChord`, and there is one definition of MAC in the tree.

### The rule for adding a type

> The DEFAULT load must reproduce the type's tuned mass, CG and inertia exactly.

Every type was calibrated against its published stall speeds and handling at the mass and CG in its config. If
adding a loading block moved either, it would silently retune a finished aeroplane. So the published figures are the
inputs and the default load is derived backwards from the tuned state:

1. **Empty mass, station arms, tank capacities and the CG envelope are published data.** Take them from the POH
   loading table or the type certificate, never from a calculator website — a third-party calculator gave the
   Archer's front-seat arm as 85.5 in where the factory POH says 80.5.
2. **Where the empty CG is published as a point** (the 172's POH sample airplane: 1454 lb at station 39.6), use it,
   and solve the default station loads — two groups, one forward and one aft — for the tuned mass and CG.
3. **Where only an empty-CG range is published** (the J-3's type certificate gives 8.5 to 20.3 in), make the default
   load the plausible one — equal occupants, tanks full — and derive the empty CG from it. Then **check** that the
   derived empty CG falls inside the published range. The J-3's comes out at station 19.4 in, inside.
4. **Empty inertia is then the tuned inertia with that load's point masses taken back off**, and moved from the
   tuned CG to the empty CG by parallel axis.

`TheDefaultLoadIsTheAeroplaneWeHaveBeenFlying` holds all of that to 10 g and a tenth of a millimetre. If it ever
drifts, the data is wrong, not the physics.

Lateral and vertical arms are **estimated** from the config's own geometry, because no POH publishes them: occupants
level with the aircraft CG, baggage on the cabin floor below it, wing fuel at the wing's own body z and in the
inboard half of the span. They matter to Ixx and Izz and to the vertical CG, and the derived empty inertia depends
on them — the 172's empty Ixx moves by 200 kg·m² if the tanks are placed at half-span instead of 2 m out. Worth
revisiting per type, not worth blocking on.

### Two halves of the data, and why that matters

Adding a type divides cleanly in two, and the halves have very different costs.

- **The envelope half** — max take-off weight and the certificated CG limits — needs only the type certificate, which
  is one document and usually one line. It gives the type real limits in the Aero Widget's weight-and-balance control
  and a real plot to show them on. `LoadSheet.HasEnvelope`.
- **The loading half** — empty weight, and the arm of every seat, hold and tank — needs the type's POH loading table,
  which is a scanned handbook per type and the slow part. It is what lets you actually put people and fuel in.
  `LoadSheet.Loadable`.

A type can have the first without the second, and several now do. The load sheet says so plainly instead of
pretending the type does not exist, and shows the envelope with where the aeroplane is flying on it.

### Types done

| type | source | envelope (%MAC) | loadable |
|---|---|---|---|
| `c172-like` | Cessna 172N POH 1979 §2.13, §6 figs 6-3/6-5; TCDS 3A12 | 21.0–35.7 at gross, 15.2 fwd below 1950 lb | yes — 1454 lb @ sta 39.6, 2300 lb gross |
| `j3-cub-like` | FAA TCDS A-691 (J3C-65) | 14.6–34.4 | yes — **680 lb** (Piper Service Memo 41), 1220 lb gross |
| `j3-cub-floats-like` | FAA A-691 Rev 34 (J3C-65S), **Edo 60-1320** rows | aft limit 3.7 in tighter than the landplane | yes — 805 lb (680 + the 125 lb seaplane exchange), 1300 lb gross |
| `pitts-s2b-like` | FAA TCDS A8SO rev 21, acrobatic category | **20.0–22.2 at 1625 lb** | limits only |
| `great-lakes-like` | FAA TCDS A18EA rev 11 | 25.1–32.5 | limits only |
| `glider-swift-s1-like` | EASA TCDS A.038 | 26.0–41.9 | limits only |

Those three needed no new research: the repo had already located each one's datum for other work
(`docs/CONTROL-SURFACES.md` for the Pitts and the Great Lakes, `assets-incoming/ref-swift-s1/MEASUREMENTS.md` for the
Swift). Their anchors are worth recording, because each is a different kind:

- **Pitts** — the datum is 91.81 in forward of the lower wing leading edge, and this project's model has its origin
  exactly there, so the anchor is body x = 0. Its acrobatic band is **2.2 % MAC wide**, which is what an aerobatic
  biplane's envelope looks like.
- **Great Lakes** — the certificate's datum ("centre of most forward lateral fuselage tube") is not a feature of a
  mesh, but the same sheet gives MAC 46.0 in with its leading edge at Sta. 9.0, which located the datum at body
  x −0.200 by two routes 25 mm apart. The sheet gives one CG range, so it is taken as constant with weight.
- **Swift S-1** — EASA quotes the limits in **percent MAC itself**, so no datum mapping is needed at all. Its MAC of
  984 mm against our measured 970 mm is the whole error.

**Each type's tuned CG lands inside its own certificated envelope**, which is the check that validates the mapping and
the condition for adding one at all — `EveryTypeWithAnEnvelopeIsTunedInsideIt`. Five types agreeing is why the method
is trustworthy, and it is exactly what the Archer fails.

The J-3's two seats are at stations +9 and +36 on a 61-inch chord, so which one the pilot sits in is worth 8 %MAC —
nearly half the whole range. That is why a Cub is soloed from the back seat, and it falls out of the published data
rather than being written in.

### 2026-11-06: the envelopes are read again, as body-x rows (landing page, Ty t880u)

The `envelope` arrays above (percent MAC, kg) stopped being read when `LoadingConfig` was replaced, so the landing
page drew no limits for any of these types. They are now written as `loading.cgLimits` rows in body x metres, straight
from each certificate's stations through the anchors above, and the percent-MAC arrays are left in place, unread. On
today's wing geometry the percent-MAC figures map back to round certificate stations for all four (Pitts 86.35 / 89.58
/ 90.50, J-3 +10.6 / +22.7, Great Lakes +20.0 / +23.8), which is the check that the conversion was right. The Swift
uses EASA's mm-aft-of-datum figures (257 / 414) rather than percent MAC, which differ by 3–6 mm because our MAC is
970 mm against 984. The 172's envelope (above) is NOT re-added: its config's 877 kg derived empty weight would put any
two-person load over the real 2300 lb gross; that is a mass-model decision for the owner. `LandingFlowTests`
(`TheVerifiedEnvelopesAreTheCertificatesOwnNumbers`, `OnlyTheVerifiedTypesHaveAnEnvelope`) hold all of it.

### `pa28-archer-like` is blocked, and why — now with the evidence

The factory POH gives everything except the one thing the mapping needs. Arms (front 80.5, rear 118.1, fuel 95.0,
baggage 142.8), empty weight (1547 lb at 86.4), gross (2550 lb) and the envelope (82.0 in below 2050 lb to 88.6 in
at 2550; aft 93.0 throughout) are all in Section 6 and §2.13. But the datum is defined as *78.4 inches ahead of the
wing leading edge at the inboard intersection of the straight and tapered section*, and anchoring on that point gives
numbers that cannot be right: the published **forward** limit of 82.0 in lands at **0.3 % MAC**, essentially on the
MAC leading edge, and the config's tuned CG falls 5.6 in behind the published aft limit. No aeroplane has a forward
CG limit at the leading edge of its own mean chord.

Four other types hang together under identical treatment, so this is the Archer, not the method — either the config's
wing is positioned differently from the real aeroplane's, or the datum note means something other than the obvious
reading. The number that would settle it is the Archer's MAC and LEMAC station, which FAA TCDS 2A13 does not carry
(it gives the CG range in inches only) and the POH does not either. The UK CAA sheet for the PA-28-181 repeats the
same wing-leading-edge datum without a MAC. Left out rather than fudged.

### 2026-11-11: what `mass.massKg` means — the rule, found on the J-3 and meant for the other 33

The owner, 2026-11-11: *"the j3 empty weight is too high"*. He owns and flies a J-3 on floats and gave three
successive estimates from it over the day (785, 775, 700 lb), then saw the published figures and ruled **"Use
published weights"** and **"Use book for cub too"** — so the Cub takes the book number like the rest of the fleet:

* **680 lb on wheels** — **Piper Service Memo 41** for the J3C-65, empty 680 against gross 1,220, the manufacturer's
  own figure for the type this config models. (A dealer listing says 703; the Piper memo is the better source.) His
  own last estimate was 700, which is the useful check on both.
* **805 lb on floats** — 680 + 125 by the exchange rule below. Not an independent number.

That is the first direct evidence against "**Two things for the owner, 1**" above — every type is authored at max
gross — and the whole fleet is said to be too heavy empty. This section is the rule the J-3 lane worked out so the
other thirty-three can follow it without re-deriving it.

**`mass.massKg` is the aeroplane AS FLOWN at the reference loading. It is not the placard, and it is not the empty
weight.** Three readers disagree if it is anything else, and only one of them goes through `AircraftLoad`:

| reader | what it uses | notes |
|---|---|---|
| the sim | `AircraftLoad.Properties` → `Aircraft.MassProperties` | empty + what is in the stations right now |
| `RigSolver` | `config.Mass.MassKg` | the rig is solved for THIS weight, at 75 % power, 4,000 ft |
| `PracticeScenario` | `config.Mass.MassKg` | Vso, Vx, Vy, ROC, best glide, 75 % cruise — every performance number the lessons quote |

So authoring `loading.emptyMassKg` on its own does not make the aeroplane lighter, it makes the aeroplane and its
rig **two different aeroplanes**. On the J-3, the empty weight alone left the rig solving a 1,220 lb Cub
that the sim flew at 870 lb — a 29 % mass mismatch, which is the same shape of fault as the fitted-blade propeller that
caused the fleet-wide hands-off divergence of 2026-11-10 (`docs/RIGGING.md`, "the rig was solving a propeller the
sim does not fly"). The invariant `AircraftLoad` is built on says it plainly:

```
mass.massKg  ==  loading.emptyMassKg  +  Σ station.referenceKg
```

`AircraftLoad` solves the empty CG and the empty inertia BACKWARDS from `mass.cg` / `mass.inertia` and the
reference stations, so whatever is authored is by definition what the aeroplane weighs when loaded as authored. Break
that sum and the type still flies — it just flies something nobody authored.

**The reference loading is one pilot in the seat the type is soloed from, and NO FUEL.** For a J-3 that is the rear
seat, which `loading.seats` already encoded (rear `referenceKg` 86 kg, front 0 — "Types done" above works out that
which seat the pilot sits in is worth 8 %MAC on this type), with the tanks left at `referenceKg` 0.

**Full fuel in the reference was tried first, and the certificate rejected it.** The argument for it is good: a
reference loading with dry tanks authors an aeroplane that cannot fly and is light by a full fuel load (72 lb on a
Cub, 176 lb on a 172), `c172-like` already carries 40 + 40 kg, `AircraftLoad.BurnFuel` only moves mass when a tank
has something in it so a type with `referenceKg` 0 **never changes weight in flight**, and `FuelGauges` falls
through a "no loading authored, show full" branch when `FuelKg` is 0. All true, and all beaten by one measurement:

> `AircraftLoad` solves the empty CG **backwards** from `mass.cg` and the reference stations. Declaring full tanks
> at the J-3's nose station (−18 in, well forward) therefore drags the solved EMPTY CG **aft**, by 3.2 in. On the
> floats J-3 that puts it at **+16.99 in against A-691's empty-weight CG range of (+8.7) to (+16.8)** for the
> "Solo flying in rear seat only" placard — and the critical loading that range exists to protect, solo in the rear
> with the tanks run dry, then sits **1.6 in behind the +19.0 aft limit**. `LandingFlowTests` caught it the moment
> the float envelope was authored.

| | empty CG | solo, tanks dry | solo, tanks full | certificate |
|---|---|---|---|---|
| floats, full-fuel reference | +16.99 | **+20.61** | +18.00 | empty +8.7…+16.8, loaded +10.6…+19.0 |
| floats, dry reference | +13.77 | +18.00 | +15.57 | — all inside |
| wheels, dry reference | +13.19 | +18.16 | +15.39 | empty +8.5…+20.3, loaded +10.6…+22.7 |

Two things to carry forward. **The nose tank is ahead of the datum, so running it dry is the aft-critical case** —
which is precisely what the certificate's solo-rear placard and its separate empty-weight CG range are about, and
the sim now reproduces it. And **the landplane alone would never have caught this**: its envelope is wide enough
that the full-fuel reference is legal on wheels. It took the float's 3.7 in tighter aft limit to show that the
reference loading was wrong. So `AircraftLoadTests.TheNewTanksAndBaggageAreEmptyAtSpawn`, which pins
`referenceKg` 0 for every authored type but the 172 and calls it an open decision, now has physical evidence behind
it rather than just inertia — and the 172 is the **exception** to copy carefully, not the precedent.

**`loading.emptyCg` and `loading.emptyInertia` are DEAD KEYS. Do not reconcile against them — delete them.**
`LoadingConfig` has no such properties, so neither `System.Text.Json` nor Unity's Newtonsoft loader has ever read
one. They were left by the 2026-10-30 lane, they describe whatever the aeroplane weighed that week, and under
live-looking names beside a corrected `mass` block they are exactly the `_legacyEmptyMassKg` trap that flew the Cub
206 kg light. Removed from `j3-cub-like` here (the floats file never had them), with the old values kept in a
`_`-prefixed note; still present in `great-lakes-like`, `pitts-s2b-like` and `glider-swift-s1-like`.

**`mass.inertia` scales with `mass.massKg` — AS AN INTERIM.** It is the LOADED inertia, and it was authored from
Roskam Part V radii of gyration (`docs/NEW-AIRPLANE-PROTOCOL.md`). The airframe does not change shape when the
belief about how much of it there is changes, so the mass distribution is unchanged and the cheapest defensible
move is `I_new = I_old × massKg_new / massKg_old`, which holds every radius of gyration exactly where it was and
keeps the only check anyone can repeat. That is what the two J-3s carry today. It is **not** what they should carry
— see the next section.

### The inertias want a component build-up, and most of it is already written

Ty, 2026-11-11: *"With the new empty weights all of the inertia calls need to be redone. Start with empty and known
weights like engines and props then estimate structure weight and arm for each item then use the weight and arm
from the loading so inertia is as accurate as possible."*

He is right about why, and the reason is structural rather than a matter of taste. **Today every inertia is authored
as a total and `AircraftLoad` works backwards from it**: it takes `mass.inertia` about the authored CG and subtracts
the reference load to get an empty inertia. Nothing underneath that number is physical, so it cannot survive a
change of empty weight — which is exactly the change now being made to all 34 types. The scaling above is a
placeholder that keeps the radii of gyration honest; it does not know where anything is.

The method, in his order, which is also the order of decreasing certainty:

1. **KNOWN** — looked up, never estimated: the published empty weight (`loading.emptyMassKg`), the engine's
   published dry weight, the propeller's published weight.
2. **ESTIMATED** — a weight *and an arm* for each structural item: wing, fuselage, tail surfaces, gear, floats,
   systems. The residual (empty minus the known items) is what the structure must add up to.
3. **THE LOAD** — occupants, fuel and baggage at the arms this page already establishes, parallel-axis onto the
   empty tensor. `AircraftLoad.Add`/`Subtract` already do this and need no change.

**`src/FlyingGame.Core/Wreck/AirframeHydroModel.cs` ALREADY IS steps 1 and 2, for every type in the fleet.** It was
written for flotation, and nothing has noticed that it is also the mass model this asks for. It builds a list of
`HydroElement`s from the config's own geometry — every wing panel, aileron, stabiliser, elevator, fin, rudder, the
fuselage cut into nose / cabin / tail cone at the same stations the break-up uses, and each gear leg — and gives
each one a **`MassKg`, a `Centre` in the body frame, and a `Half` box size**. Mass comes from Raymer's general
aviation class-II buildup (*Aircraft Design: A Conceptual Approach*, Table 15.2, constants in `WaterPhysics`): wing
12.2 kg/m² of exposed planform, tails 9.76 kg/m², fuselage 6.84 kg/m² of wetted area, gear 5.7 % and "all else"
10 % of design gross, installed engine 1.4 × dry with dry from power, propeller from `propInertia` and the disc
radius. The airframe items are then **scaled together by one factor so the sum lands on `AircraftLoad.EmptyMassKg`**
— which is Ty's residual, already implemented, already called `StructureScale`.

So the inertia build-up is a small tool on top of a model that exists, not a new model:

```
I_empty = Σ over elements [ m·(its own box inertia, from Half) + m·(parallel axis from Centre to the empty CG) ]
I_loaded = I_empty + the reference load by parallel axis      (AircraftLoad already does this half)
```

Whoever builds it should put it in `RigTool`'s shape — env-var gated, printing by default, writing only on request,
scoped by an id list — because that is this project's convention for a fleet-wide rewrite and it is what makes the
result reproducible. Three independent checks exist and all three should be used: the Pitts' part-by-part build-up
already gives a vertical CG of 0.147 against the authored 0.200 (whose own note says "for ground handling, not
certified"), so reproducing 0.147 from components is strong evidence the method works; the Pitts' inertias were
trimmed per axis against Roskam's published ranges, so a build-up should land inside them without being pushed; and
`docs/NEW-AIRPLANE-PROTOCOL.md`'s radii-of-gyration bands are the fleet-wide sanity check. Ty's own propeller rule
(each blade 15 % of net propeller weight, centre of mass at 25 % of DIAMETER, two-blade, +60 % on a three-blade)
belongs in the known tier rather than being re-invented.

One caution found while authoring the J-3: `AirframeHydroModel` takes `DesignGrossKg` from `config.Mass.MassKg`, and
two of Raymer's terms (gear 5.7 %, all-else 10 %) are fractions OF IT. That was harmless while `mass.massKg` was
every type's max gross; now that it is the aeroplane as flown, those two terms shrink with it and the structure
scale absorbs the difference. Whoever does the build-up should decide deliberately whether those fractions should
read the PLACARD (`loading.maxTakeoffMassKg`, which is what Raymer's W_dg means) rather than the flying weight.

**`mass.cg` does NOT move.** Every type's pitch stability is tuned against it, five types are validated by landing
inside their own certificated envelope, and this page already flags moving the tuned CG as the owner's call rather
than a lane's. Holding `mass.cg` fixed and letting `AircraftLoad` re-solve the empty CG backwards is the
minimum-change choice, and it is the one that keeps every existing calibration meaningful. (For the record, on the
J-3 the physically-derived loaded CG would have moved ~4.4 cm FORWARD — about 3 %MAC more stable — which is the
wrong direction against the "the fleet flies slightly nose-heavy" finding above.)

**A floatplane's empty weight is NOT an independent number.** The owner: *"Floats use an exchange weight removing
landing gear and adding floats."* The delta between a wheels config and its floats config is floats-installed MINUS
gear-removed. On the J-3 that is **125 lb**: Edo 1320s run 171–182 lb installed with rigging against about 50 lb of
wheel gear. Wipaire publish the same figure for their own installations and call it the seaplane exchange weight,
and the owner's own pre-publication estimates (700 / 825) used exactly the same 125, which is the cross-check that
this is the rule he means. So a floats config's `loading.emptyMassKg` is its landplane's plus the exchange — here
680 + 125 = 805 — and if the two ever drift apart one of them is wrong. Worth stating because the two files look
independent: nothing mechanical holds them 56.699 kg apart, so it has to be held by whoever edits them. The same
rule is waiting for `pa18-floats-like`, `dhc2-beaver-floats-like` and `aircam-amphib-like`, none of which has a
`loading` block yet.

**What it did to the aeroplane** (`J3WeightProbe`, `J3_WEIGHT=1`; `PracticeScenario`'s own performance solve, which
is where the lessons' numbers come from):

| | wheels, before → after | floats, before → after |
|---|---|---|
| authored mass | 553.0 → 394.4 kg (−28.7 %) | 589.7 → 451.1 kg (−23.5 %) |
| empty weight | 1,029.6 → 680.0 lb | 1,110.5 → 805.0 lb |
| stall Vso | 43.2 → 36.5 mph (−15.5 %) | 43.8 → 38.3 mph (−12.6 %) |
| climb, ROC at Vy | 537 → 991 fpm (+85 %) | 506 → 846 fpm (+67 %) |
| 75 % cruise | 80.7 → 83.2 mph (+3.1 %) | 83.8 → 85.5 mph (+2.0 %) |
| rig point, 75 % | 56.3 → 62.6 KTAS | 59.6 → 65.6 KTAS |

Weight buys **climb**, a useful slice of **stall speed**, and almost nothing at **cruise** — which is what a
power-limited aeroplane does, and worth knowing before the same change is made to thirty-three more types. On the
owner's *"the j3 is noticibly slower"* (2026-11-09): the rig point answers it better than `cruise75` does, because
`PracticeScenario.EstimateCruise75` uses a flat propeller efficiency while `RigSolver` flies the blade-element
propeller. At 75 % the J-3's indicated speed goes **61.1 → 67.9 mph IAS** and its TAS **64.8 → 72.0 mph**, against a
book cruise of about 73 mph — which it now essentially reaches, and could not before.

It also **turned the J-3's three red tests green**: `RigTests.HandsOffLevelAtRigPower(j3-cub-like)` and
`PowerAwayFromTheRigYawsAndRollsTheRightWay` for both J-3s had been failing for days, and the J-3 was the only type
in the fleet still red. `J3HandsOffModeProbe` had traced that mode to the propeller disc's cross-flow terms and
recorded "a real J-3 is directionally stable, so something here is too big". Something was — the aeroplane. At
1,220 lb the rig point is 56.3 KTAS at α 3.51°, where the disc's destabilising cross-flow force beats a fin whose
restoring moment follows ½ρV²; at the weight a J-3 is actually soloed at, the same 75 % gives 62.6 KTAS at α −0.21°
and the fin wins. Nothing in the aero or propeller model was touched. **That is the lesson worth carrying to the
other thirty-three: a handling bug on a type authored at max gross may be the weight, not the physics.**

**While you are in there: the gross was right, and nothing else in the file was.** `j3-cub-floats-like` was authored
at 589.7 kg, which is A-691's 1,300 lb seaplane gross to within 30 grams — that is how you can tell `mass.massKg`
was a placard and not an aeroplane. It carried **no `maxTakeoffMassKg`, no `cgDatumM` and no `cgLimits` at all**, so
nothing read that number as a placard and the landing page showed no limits for the type. All three are now
authored from the certificate, and the landplane's envelope was NOT copied across, because it does not fit:

> **FAA Aircraft Specification A-691 Rev 34, section V, Model J3C-65S** — Maximum Weight *"1160 lb. with McKinley
> floats / 1200 lb. with Edo, Wollam or Heath floats / **1300 lb. with Edo 60-1320**, Edo 92-1400 or Wollam 1200
> floats"*; C.G. Range *"(+10.6) to (+19.0) with Edo 60-1320 or Wollam 1200 floats and 1300 lb. maximum weight"*.

Three things follow, and each is a trap avoided:

1. **There are three seaplane grosses, by float model.** This aeroplane wears Edo 60-1320s, so it is the 1,300 lb
   row. The owner: *"The j3 has several gross weights on floats by serial number and I think the highest is 1300
   just use the highest in the tcds."*
2. **The float aft CG limit is +19.0, 3.7 inches tighter than the landplane's +22.7.** Copying the landplane's
   envelope across — the obvious shortcut, and both files share a datum — would have authored an aeroplane with
   3.7 in of aft travel it does not have.
3. **`LandingFlow.GrossOverrideKg` would have thrown the certificate away.** Floats and amphibians took
   `LandingCatalog.FloatGrossFactor` × the landplane's placard unconditionally; on the J-3 that is 1,464 lb against
   the certificate's 1,300. The factor is a stand-in for a figure nobody has looked up, so it now yields to a float
   config that authors its own — otherwise an authored, sourced number sits unread, which is exactly what
   `loading.emptyCg` was. Only this type authors one today.

The datum mapping was **verified rather than assumed**: run this file's own authored arms back through
`arm = (0.4073 − x) / 0.0254` and they come out as the certificate's own stations to three decimals — seats +9.000
and +36.000, baggage +49.000, fuel −18.000. That is the check that the landplane's anchor carries over.

And the certificate hands back one more check, the sharpest this config has — it is the one that decided the
reference loading above. A-691 also gives an **empty-weight** CG range for this float at 1,300 lb with the
*"Solo flying in rear seat only"* placard: **(+8.7) to (+16.8)**. The empty CG `AircraftLoad` solves backwards from
`mass.cg` and the reference stations is body x +0.0264 = **+13.77 in**, comfortably inside, and every fuel state
follows: solo in the rear the CG runs +18.00 with the tanks dry to +15.57 brim-full, against the +10.6…+19.0 range.
Declare full tanks in the reference instead and the empty CG goes to +16.99, outside — which is how that decision
got made.

Against the 805 lb empty weight the aeroplane is 994.6 lb as authored, 77 % of its 1,300 lb gross, with 305 lb of
useful load left — a passenger and most of the fuel, or full fuel and 230 lb of gear. The landplane for comparison:
869.6 lb against its 1,220 lb placard and 350 lb left, which is the two-up Cub that really does exist.

## Still to build

- **Fuel burn from power.** `LoadSheet.BurnFuelKg` exists and drains the selectable tanks in proportion, so the
  wings stay symmetric; nothing calls it yet. It wants a BSFC per engine, which belongs with the engine systems.
- **The plot.** The envelope and the loaded point are both computed; the diagram is not drawn. Charts in this
  project never show own-ship position, but a weight-and-balance envelope is not a navigation chart and is useless
  without the loaded point on it, so it shows there.
- **A loading screen** before flight, and a per-tank fuel selector.
- **The rest of the fleet.** Each type needs one anchor and, for the loading half, its POH loading table. The cheap
  half is worth doing first and in bulk: a certificate gives the envelope, and the envelope is what the widget and
  the plot need. The gliders want water ballast rather than fuel, which is the same mechanism with a density of 1.0
  and a dump valve.
- **Where it is reached from.** The load sheet is on the landing page's AIRCRAFT chapter, before you fly, which is
  when a pilot does it. There is no in-flight entry yet, and there is not much point in one until fuel burn moves
  the CG during a flight.

## Two things for the owner

**1. Every type is simulated at max gross, and most are a little forward of where a real one balances.**

The 172's config is 1043 kg, which is 2300 lb to the pound — its maximum take-off weight. So is the J-3's 553 kg
against 1220 lb. With the published empty weight and real arms, the tuned CG then implies a specific loading: the
172's works out as two 104 kg occupants in front, two 33 kg children behind, and full tanks. A *typical* gross 172 —
two adults in front, one behind, full fuel and 76 lb of bags — sits about 5 %MAC further aft, at 32 %MAC against the
config's 27 %MAC, and would have about 5 %MAC less static margin.

So the fleet is flying slightly nose-heavy and therefore slightly more stable than the real thing. Closing that gap
means moving the tuned CG, which retunes pitch stability on aeroplanes already calibrated against their published
numbers. It is a real improvement and it is not a change to make quietly. Measured, per type, by
`WhereTheTunedCgSitsInTheRealEnvelope`.

**2. The CG positions look authored by convention, not measured.**

Measured in percent MAC, seven types land within 0.1 % of each other at **27.0 %MAC** — the 172, the Archer, the
SR22, the Cub, the PA-18, the Glasair. Across the fleet the spread is 14 to 33 %MAC. That is the signature of
placing each CG at a fixed fraction of the chord while authoring, which is a perfectly sensible default and is not
the same thing as knowing where the aeroplane balances. Where published data exists it should replace the
convention; the loading blocks above are the first two.

## Sources

- Cessna 172N Pilot's Operating Handbook, 1979 — §2.13 Center of Gravity Limits, §6 Figure 6-3 Loading
  Arrangements, Figure 6-5 Sample Loading Problem.
  <https://topflightlessons.com/wp-content/uploads/2026/05/Cessna-172N-POH-1979-compressed.pdf>
- FAA TCDS 3A12 (Cessna 172), MAC 58.8 in with its leading edge 25.9 in aft of datum; corroborated by EASA
  TCDS IM.A.051. <http://www.aeroelectric.com/Reference_Docs/Cessna/cessna-misc/C172_FAA_typecert_3A12.pdf>
- FAA TCDS A-691 (Piper J3C-65) — datum "wing leading edge", C.G. range (+10.6) to (+22.7), seats "one at +9 and
  one at +36", baggage 20 lb at (+49), fuel 12 gal at (−18), maximum weight 1220 lb.
  <https://upload.wikimedia.org/wikipedia/commons/8/8a/US_FAA_A-691.pdf>
- Piper PA-28-181 Cherokee Archer II POH, Report VB-790, 1976 — §2.13 and §6 Figures 6-9/6-11.
  <https://aacit.org/wp-content/uploads/2016/02/PA28-181-POH.pdf>
- FAA TCDS A8SO (Pitts S-2B) and A18EA (Great Lakes 2T-1A-2), both already read for this project and recorded in
  `docs/CONTROL-SURFACES.md`; EASA TCDS A.038 (Swift S-1), recorded in `assets-incoming/ref-swift-s1/MEASUREMENTS.md`.
- Great Lakes standard empty weight 1230 lb and 1800 lb gross: Plane & Pilot's type specification. A newer build is
  1290 lb, so this is a type figure and not a particular aeroplane's.
  <https://planeandpilotmag.com/article/great-lakes-2t-1a-2>
- J-3 empty weight 765 lb is the type figure, not a particular aeroplane's; an individual Cub's empty weight and CG
  come from its own weight-and-balance record, as the type certificate says.
