# Handoff: the crash path, 2026-11-12

Written by Claude Opus 5 at the end of a session that **diagnosed** more than it **built**. Everything
below is measured, not guessed, and every number in it came out of a run in this repo on this date. The
point of this document is that the next agent writes code instead of re-deriving what is already known.

**Read `CLAUDE.md`, `docs/PARITY.md` and this file before touching anything.**

---

## 0. The one-paragraph summary

Four commits landed tonight on `lane/claude-quest-islandnav` and `lane/claude-spin-rudder-tdpf` (both at
`df7d0d25`), and build `2026.1112.0135` went to TestFlight and OTA. They fixed momentum conservation at a
component separation, unified three disagreeing propeller weights into one, made a torn-off gear leg's
stub behave like real structure, and took Ty's decision on a stale test pin. **21 tests are still red, all
of them red before this work started.** They are almost certainly **four or five root causes, not 21** —
Section 3 groups them. The highest-value single task is Section 3.1, and Section 3.1 is *not* the task I
thought it was when I started writing this document; the measurement at the bottom of it changed my mind.

---

## 1. Ground rules — these have all been learned the hard way

| rule | why |
|---|---|
| **NEVER `git stash`** | The stash is shared repo-wide across ~60 worktrees. It has held 8 entries from 5 different lanes at once. Save a patch, `git checkout`, re-apply. |
| **Never `--no-verify`** | The parity hook is load-bearing; see below. |
| **The repo is PUBLIC** | No prices, partner names, email addresses or business terms in commits. Ever. They go in the bridge log. |
| **One commit per fix** | With the reasoning in the message, including the numbers that justify it. Commit messages here are the design record — read a few before writing one. |
| **Never weaken a test to get green** | If a pin looks wrong, measure it, then *ask Ty*. He overturned one tonight in ten seconds when handed the measurement. He will not thank you for editing one quietly. |
| **A green .NET suite is NOT a build** | The tests are .NET 8; the game is Unity compiling Core/Sim/Bridge from source via asmdefs. A suite can be green while the iOS build is broken outright. |
| **`export PATH="$HOME/.dotnet:$PATH"`** | And always `-p:UseSharedCompilation=false` — a wedged VBCSCompiler looks exactly like a hung test (no output, no CPU). |
| **`timeout` does not exist on this macOS** | Use an `until grep -q … ; do sleep N; done` watcher instead. |
| **The suite takes hours** | A 30-class filter is 25 minutes. The whole suite has run 5h15m at 99% CPU and not finished. Always filter, always background it, always log to a file. |

### Parity

`docs/PARITY.md` is a standing rule, not a lane: iOS/Mac and Quest stay in sync at all times. Shared code
lands on a shared lane that is merged into **both** `lane/claude-quest-islandnav` and
`lane/claude-spin-rudder-tdpf`. A commit hook enforces it and will tell you, in plain English, that one
lane now leads the other and that no further shared change may land until you merge. Do what it says
immediately; both lanes must be at the same commit when you stop work.

```
tools/check-parity.sh                                    # from the islandnav worktree
PARITY_IOS_BRANCH=lane/claude-spin-rudder-tdpf tools/check-parity.sh   # when on a *quest* branch
```

### Where things are

| path | what |
|---|---|
| `/Users/tyfrisby/Developer/flying-game` | **main checkout**, on branch `split-surfaces` (OLD — predates the slim packaging). `tools/deploy/publish-ota.sh` is untracked and exists **only here**. |
| `/Users/tyfrisby/Developer/flying-game-worktrees/claude-quest-islandnav` | the Quest/shared lane. **Build and test here.** Has its own `unity/Library`. |
| `/private/tmp/aero-spin-tdpf` | the iOS parity partner lane. |
| `/private/tmp/aero-crashpath` | scratch worktree used tonight, on `wip/claude-crashpath`. Disposable. |

**The build must run from the islandnav worktree.** The main checkout's branch has no `FG_TF_SLIM` define
and no `TestFlightFleet`, so building there silently ships the full fleet and blows App Store Connect's
4 GB limit (a default build measured 4.2 GB; slim is 1.01 GB).

```
FLYINGGAME_TF_SLIM=1 tools/deploy/deploy.sh      # Unity export + xcodebuild; prints app size
tools/deploy/testflight.sh                        # archive + upload (Xcode-managed account; NO tester API)
cd /Users/tyfrisby/Developer/flying-game && tools/deploy/publish-ota.sh --ipa /private/tmp/flyinggame-asc/dev/AeroPlayground.ipa
```

---

## 2. What landed tonight, and the mechanisms behind it

Read these four commit messages in full before working in this area — they contain measurements this
document only summarises.

**`986cfe41` — no propeller leaves the books without a wreck piece to carry it.**
`Aircraft.LoseComponent(Nose)` shed `AirframeContact.PropMassKg` at the prop hub, while the wreck model's
`"engine"` element — tagged `WreckAtom.Nose`, mass `engDry + propKg` — already carried that propeller, so
`ShedMass(Hydro.Detach(Nose))` took it a second time. The Pitts lost 300.349 kg where the piece weighed
277.610: **22.738 kg went nowhere and 3.1 % of the momentum vanished.**

> **The general principle, worth keeping in your head:** with origin `P = Config.Mass.Cg`, momentum before
> is `M·v_P` and after is `(M−m)(v_P + ω×c') + m(v_P + ω×r)`. Those cancel exactly **iff**
> `c' = −m·r/(M−m)` **and** `dm == piece.MassKg`. So **the mass ledger must balance before the momentum
> can.** `NothingLosesSpeedAtASeparation` now asserts the ledger *first*, which is the check that would
> have caught this years earlier.

The same line did a second wrong thing: `LoseComponent(Propeller)` in the live sim is a prop **stoppage**,
not a departure, so a Pitts that stopped its prop on a branch was losing 22.7 kg off a propeller still
bolted on, with no wreck piece anywhere (737 → 714 kg, CG 0.050 m aft, Iyy 1000 → 941). The three paths
where a propeller's weight really does leave are the nose's own volumes, `CrashRelay.SeparateEngine`, and
`CrashRelay.ThrowPropellerBlade` blade by blade.

**`b798736c` — one propeller weight.** `AirframeContact.PropMassKg` is now the single source: authored
figure, else **Ty's blade rule inverted** out of `propInertia` for a two-blade prop, else the disc-area
estimate. The rule is used only where he validated it (two-blade; it runs 60 % high on Hartzell's
three-blade polar moment). `AirframeHydroModel` consumes it instead of its own `3·I/R²`, which assumed an
evenly-spread blade and ran ~4× light on every two-blade type.

**`7ecabcda` — a torn leg's stub belongs to its section, and folds.** It was `AirframeComponent.Fuselage`
with `BreakSpeedMs = 0` — a component no crush section owns and a speed that raises no arrival — so it was
indestructible, and it is the lowest point left on every upright type by 13–24 cm. Belly slams landed on
stubs. It now reports as its own section *and* folds (`ContactPoint.Collapses` →
`Aircraft.CollapseFoldingPoints`) so it charges that section nothing, because the leg already absorbed its
own failure when it tore off.

**`5f99c29a` — Ty's call.** With the stubs out of the way, the C172 tail-cone drop reaches its belly at
12.5 m/s, past an aluminium cabin's `8·√2 = 11.3 m/s` single-blow speed, so it loses its cabin and nose.
`BreakupTests` asserted it did not. Ty overturned his own 2026-11-04 pin when shown the number.

**`df7d0d25` — the Jenny's prop is 18 kg** (Ty), with `propInertia` 10.0 → 2.09 derived from it by his own
blade rule so weight and gyroscopics cannot disagree.

### Numbers you will want and should not re-derive

- `FuselageStructure.CrushTravelM = 0.15`. Crush per arrival is `0.15·((v/breakMs)² − 1)`.
- Break speeds: `TailBoomBreakMs = 5.5`, `CabinBreakMs = 8.0`, `NoseBreakMs = 6.0`, each × `ImpactStrength`.
- Aluminium cabin capacity 0.150 m → single-blow break at `8·√2 = 11.3 m/s`. Steel tube cabin capacity is
  **infinite** (Ty: a steel-tube fuselage does not break apart other than at the firewall), so it crushes
  without ever breaking.
- `FuselageStructure.IsSection` is `Nose | Cabin | TailBoom` **only**. `AirframeComponent.Fuselage` is
  owned by nothing and crushes nowhere — if you see it on a contact point, that is a bug.
- `AircraftFuselageCrush` charges **one arrival per section per step** (the worst), so several points on
  one section in one step is not double-counting. Across *steps* it accumulates.
- Ty's blade rule: each blade is 15 % of net prop weight with its CoM at **25 % of the DIAMETER**; hub
  radius of gyration 0.10 m. `I = M·[(n·0.15)(0.25D)² + (1 − n·0.15)·0.10²]`. **Two-blade only.**

---

## 3. The work, in priority order

### 3.1 The tree strikes — five reds, and probably one cause  ← START HERE

```
CrashTuningTests.AHighEnergyLimbHitCanBreakAWing
StrippedTreesTests.ThePittsSquareOnAStemIsInsideTheDrawnTreeAndTheTreeIsStrippedRoundIt
TreeBranchPassTests.AWingThroughACrownYawsTowardItAndCostsSpeed
TreeBranchPassTests.TheTopFourFeetAreSoftAndTheStemBelowIsSolid
TreeStrikeTests.AHighSpeedTreetopStrikeCostsAModestDeltaVNotAStop
```

Four separate test classes about tree geometry and tree strikes went red together. **Treat that as one
regression in the tree model, not four coincidences.** Nothing in tonight's work touches trees, and all
five were red at the lane tip, so this predates 2026-11-11.

The limb-hit test is the one with a full diagnostic log, and here is its real output (every strike,
`--logger "console;verbosity=detailed"`):

```
limb:wing-upper-panel-L  (WingTipLeft)      StemSnapped at 55.5 m/s  stem 5.5 cm   8.0 kN vs part 85.6 kN
limb:wing-upper-panel-L  (WingTipLeft)      StemSnapped at 54.6 m/s  stem 5.6 cm   8.2 kN vs part 41.0 kN
limb:wing-upper-panel-L  (WingTipLeft)      StemSnapped at 54.0 m/s  stem 5.6 cm   8.2 kN vs part 32.5 kN
limb:wing-lower-panel-L  (WingTipLowerLeft) StemSnapped at 55.0 m/s  stem 5.2 cm   7.0 kN vs part 23.9 kN
limb:gear-left           (GearLeft)         StemSnapped at 60.1 m/s  stem 5.2 cm   7.0 kN vs part 19.1 kN
limb:wing-upper-panel-L  (WingTipLeft)      StemSnapped at 40.6 m/s  stem 7.8 cm  15.8 kN vs part 26.2 kN
```

**Every strike is `StemSnapped`; not one is `PartBroken`.** Three things in that log matter, and the third
is the one I only noticed while writing this:

1. **The limbs are thinner than the test's own arithmetic assumed.** The test comment predicts limbs of
   **0.085–0.105 m** at the stations the wing passes; the log says the limbs actually struck are
   **5.0–5.6 cm**, and later **7.1–7.8 cm**. So the force is low partly because the branch is thin. Check
   the tree's crown geometry (`Forest.Tree`, constructed in the test as
   `new Forest.Tree(20, -(semi + 0.35), 0.0, 40, 0.22 + 40/24.0, 0, 0)`) against what the comment's
   `0.005 + 0.2·0.5` formula assumes. **A limb-diameter law that changed would explain all five reds at
   once** — it is the one quantity every one of these tests depends on.
2. **The predicted force is 2–5× the measured one.** The comment says `F ≈ 19 kN to snap + 22 kN to throw
   at 70 m/s` ≈ 41 kN against a panel's ~30 kN. Measured: 6.5–8.2 kN on the thin limbs, 12.9–15.8 kN on
   the thicker ones. Decide whether the comment's arithmetic or the code's law is wrong **before** you
   change either.
3. **The part strength is already degrading toward the force and nearly gets there.** Watch the same part
   across successive hits: 85.6 → 41.0 → **32.5** kN, and on the lower panel 76.0 → 36.4 → **23.9** kN.
   Progressive damage is working. The closest call in the whole log is **15.8 kN against 26.2 kN** — 1.7×
   short, not 5× short. So this test is much nearer to passing than the headline ratio suggests, and a
   correct limb diameter might be the whole fix.

**Do this, in this order:** (a) `git log` the tree files and **bisect** — do not tune. Find the commit that
moved the limb diameter law, the limb force law, or `Forest.Tree`'s crown geometry. (b) Decide from the
code which of the comment and the law is right. (c) Fix the cause, then re-run all five tests together.
(d) If the fix makes the aeroplane *more* fragile in trees, tell Ty with the numbers before committing —
he flies through trees deliberately and has opinions about it.

**Acceptance:** all five green, with a commit message naming the commit that broke it and the measurement
that proves the fix. If only some go green, say which and why in the message.

### 3.2 The water break-up group — seven reds

```
SoftWaterBreakupTests.AHardWingFirstArrivalOnWaterStillBreaksTheWing
SoftWaterBreakupTests.AJ3WaterSkiNoseOverKeepsItsWingsAndAilerons   (5 parameter cases)
WheelWaterTests.AJ3SkimsAt42KnotsWithPowerAndTheTailLow
```

Six of the seven are the **J-3 on water**, which makes this one aeroplane's hydrodynamics rather than a
general water bug — note `j3-cub-floats-like` got a heavier propeller tonight (5.20 → 20.78 kg of prop in
the wreck model's engine element, nose piece 129.6 → 144.6 kg), **but these were red before that**, so the
prop change is not the cause. Verify that claim yourself at `3b09772a` before going further; it is the
single most useful thing to establish early.

The relevant subtlety: on water whose forces are the airframe's own, break-up is **load-based**
(`Wreck/AirframeFlooding.CheckJoints`), not closing-speed-based — `AirframeContact.Compute` sets
`loadRuled` and deliberately raises no arrival, because a closing speed at first touch says nothing about
the load once the water's added mass has taken it up. A test that expects a break from an arrival speed on
water is testing the wrong mechanism.

### 3.3 WaterCurrent — three reds

```
WaterCurrentTests.AWreckAcrossARiversShearIsTurnedAndInAUniformCurrentIsNot
WaterCurrentTests.AWreckInARiverIsCarriedDownstreamAtTheRiversSpeed
WaterCurrentTests.TaxiingUpstreamTakesPower
```

All three concern a river's current acting on a floating body. One cause, almost certainly. Note Ty's
standing correction: **wrecks SLIDE and that is correct** — do not "fix" sliding into sinking.

### 3.4 ContactPatch and Breakup — five reds

```
ContactPatchTests.ABentPanelFliesOnWhatIsLeftOfItAndGoesPastIt
ContactPatchTests.HardTipStrikeFoldsTheLowerPanelUpAndItHangsOn
ContactPatchTests.LosingThePittsLowerPanelLosesItsAileronAndTheUpperOneTrailsStickFree
BreakupTests.HardDropOnOneMainTearsThatLegOffOnly      -> Assert.Contains GearLeft, set was []
BreakupTests.HardImpactIsReportedForTheCrashSound      -> Assert.Empty, collection was [Propeller]
IslandCombatTests.AWreckOnTheGroundCostsMorePerStepThanAnAeroplaneInTheAir   (performance, likely separate)
```

The two `BreakupTests` failures are the most tractable in the whole list and both involve the **PA-18
Cub**. `HardDropOnOneMainTearsThatLegOffOnly` drops level at 2.8 m/s rolling left at 2.5 rad/s expecting
the left main to arrive at ~5 m/s past the leg's 4 m/s limit — and **no leg breaks at all** (`lost: ` is
empty). `HardImpactIsReportedForTheCrashSound` loses a `Propeller` it should not. Both smell like one
change to gear arrival or to `MemberBreaksOnArrival`. The three `ContactPatchTests` are all the Pitts'
biplane panels and are likely one cause between them.

### 3.5 Flutter — the biggest *build* task, fully specified and not started

`docs/` does not hold this; **Ty's spec is in the agent's memory file `flutter-spec.md`** and it is
complete: armed at 1.2 Vd, initiated by 1.275 Vd or any control surface moving faster than 1 deg/s, the
surface that exceeded the rate is the one that flutters, damage **degrades ultimate strength toward 1 g**
rather than scripting a failure, hard ceiling at 1.3 Vd, calibration point 1.35 Vd for 60 s. Read it in
full before starting.

**It has a hard prerequisite.** Components are identified **per side, not per surface**:
`AirframeComponent.AileronLeft` masks every aileron strip with `y < 0`, and **the Pitts has four
ailerons**, so it cannot lose just its lower-left one. Fix per-surface component identity first. The good
news measured 2026-11-11: `LoseComponent` already removes strips from the aerodynamics via a `_stripMask`
driven by each strip's own `y` station, so a shed surface already loses its lift area geometrically.

### 3.6 Smaller open items

- **Ground penetration is mass-dependent** — DC-3 sinks 65 cm, Pitts 3 cm. A few cm is *right* and stays
  as a scar (Ty); the mass dependence is the bug.
- **Shadow does not scale with height** — logged at Ty's instruction, physics first.
- **Great Lakes Beggs-Mueller recovery at 4.79 turns**, over Ty's 4-turn rule. The **Extra recovers from
  rudder alone in 2.94 turns** where his rule says it must not.
- **Idle does no work in the model** — full power recovers from a spin faster than idle, which is backwards.
- **Six stale Pitts flat-spin pins** want re-pinning; the old `0.2 deg/s²` figure is dead.
- **Nobody has visually inspected the re-cut Pitts exhaust stacks** that shipped tonight.

---

## 4. How Ty works, and what he wants from you

- **Limited hands-on time, decisive.** Build and ship; do not survey. Make the call, report the reasoning
  so he can overturn it cheaply — he does, fast, and that is the system working.
- **His process rules are hypotheses.** He deletes a rule that costs more than it saves. Measure the
  mechanism, then narrow the rule.
- **He is a real pilot and flies the Pitts S-2B.** When he says what an aeroplane does, that is data.
  When the model disagrees with him, the model is usually wrong — but show him the measurement either way,
  because twice tonight a number changed the decision in one message.
- **Push to GitHub without asking** (2026-10-01), tests green first, never force-push.
- **A bug reported twice is usually a fix that never merged.** Check the merge, not the diff.

## 5. Verification command that covers all of this

```
cd /Users/tyfrisby/Developer/flying-game-worktrees/claude-quest-islandnav
export PATH="$HOME/.dotnet:$PATH"
F=$(echo "CrashTuningTests PropDepartureTests ThrownBladeTests BreakupTests ContactPatchTests \
BendDamageTests WaterBreakupTests FloatLandingTests WreckFrictionTests AirframeContactTests \
GroundImprintTests OccupantCabinBreakupTests PerSurfaceLossTests EngineStateTests \
StaggeredWingFailureTests SoftWaterBreakupTests CrashWaterTests FuelSlickTests PropWaterStrikeTests \
WheelWaterTests AircraftLoadTests OverstressPatternTests WingAileronBreakupTests FlutterTests \
FinPoolTests TreeStrikeTests TreeBranchPassTests StrippedTreesTests WaterCurrentTests \
IslandCombatTests" | tr ' ' '\n' | sed 's/^/FullyQualifiedName~/' | paste -sd'|' -)
dotnet test tools/FlightTests/FlightTests.csproj -p:UseSharedCompilation=false --filter "$F" > /tmp/run.log 2>&1
grep -E "Failed!|Passed!" /tmp/run.log
grep -E "^  Failed Flying" /tmp/run.log | sed 's/\[[0-9].*//' | sort -u
```

**Baseline as of `df7d0d25`: 475 passed, 21 failed, 496 total, 25 m 27 s.** Those 21 are exactly the ones
listed in Section 3. Anything else that goes red is yours.
