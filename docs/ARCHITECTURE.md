# Flying Game — Architecture

Working title only. iOS (iPhone + iPad). Unreal Engine (C++) is the shipping runtime. Planning doc — nothing here is built yet.

## The one big decision

**Ship runtime = a native C++ Unreal module. The C# Core/Sim assemblies are a test oracle, not the runtime.** No UnrealSharp — nothing that runs on-device is C#-over-Unreal.

The flight model does not use Unreal's Chaos physics for the aero/rigid-body math. That math is implemented twice, on purpose:

1. **C# oracle** (`FlyingGame.Core` / `FlyingGame.Sim`, pure C#, zero engine references): the reference implementation. Fast to iterate, runs as headless dotnet unit tests with no engine startup cost — trim the glider, command full aileron, and assert the nose yaws *away* from the turn (adverse yaw); hold full aft stick + rudder and assert autorotation develops with the right rotation rate sign. This is where the physics is designed and proven correct.
2. **C++ runtime** (`FlyingGameCore` / `FlyingGameSim`, native Unreal modules): a straight port of the oracle's math, running the actual game on-device. It doesn't diverge from the oracle by feel — it's validated against the oracle's golden trajectories (same control inputs in, same state out, within tolerance) in CI.

Why split it this way instead of shipping the C# oracle directly:

1. **Native C++ is the right choice for a shipping iOS Unreal title.** UnrealSharp (community C#-for-Unreal bindings) is comparatively immature and not something to build a shipping product's core loop on. Native C++ is what Unreal and Apple both actually support and optimize for.
2. **The oracle stays cheap to test.** Pure C#, no engine, no UnrealSharp — the physics north star (spins, adverse yaw, stall break) gets verified in fast headless unit tests, not by eyeballing the chase cam or waiting on Unreal Editor startup.
3. **Porting against a golden reference beats porting from a spec.** The C++ module doesn't reimplement the aero model from the design doc in isolation — it reimplements it against the oracle's recorded outputs, so "does the port match" is a deterministic test, not a judgment call.

**No UnrealSharp.** Explicitly rejected as a runtime dependency. If UnrealSharp (or any C#-in-Unreal bridge) is reconsidered later, that's a separate architecture decision — not implied by the C# oracle's existence. The oracle is a test tool, checked into the repo, never packaged into the app.

## Assemblies / modules

**C# test oracle** (dotnet, no Unreal reference, not shipped):

```
FlyingGame.Core     pure C#, noEngineReferences=true. Math, atmosphere,
                    RigidBody6DOF, AeroModel, PropModel (stub), data contracts.
                    Reference implementation only — ground truth for tests.
FlyingGame.Sim      pure C#. Fixed-timestep sim loop, aircraft assembly
                    (airframe + surfaces + engine), flight-state snapshot API.
```

**C++ runtime** (Unreal modules, ships on-device):

```
FlyingGameCore      native C++ port of Core's math (RigidBody6DOF, AeroModel,
                    PropModel, Atmosphere). Validated against the C# oracle's
                    golden trajectories — not reimplemented from the design
                    doc in isolation.
FlyingGameSim       native C++ port of Sim's fixed-timestep loop and aircraft
                    assembly.
FlyingGameBridge    UE-specific glue: sim→Actor transform, touch input→sim
                    controls, camera rig, bubble field rendering, ground/crash
                    detection.
FlyingGameGame      Challenge runner, arenas, scoring, progression, economy
                    (stubs until needed), save/load.
FlyingGameUI        Dual touchpads, gauges, HUD, menus, callouts (UMG).
```

Dependency direction is strictly downward on both sides: `UI → Game → Bridge → Sim → Core`. The C# oracle never depends on the C++ modules and vice versa — they're compared against each other in tests, never linked together.

## Folder layout

```
flying-game/
  docs/                      planning + Grok prompt log
  oracle/                    C# test-oracle project (Core+Sim), no engine refs,
                             never shipped
  unreal/                    Unreal project (created when slice is built)
    Source/FlyingGame{Core,Sim,Bridge,Game,UI}/   one C++ module each
    Content/
      Configs/               AircraftConfig + ChallengeDefinition JSON
      Art/  Audio/  Blueprints/  Maps/            (Blueprints stay thin — logic is C++)
  tools/FlightTests/         dotnet test project running the oracle headless;
                             also holds golden-trajectory fixtures the C++
                             port's tests replay against
```

Unreal target: current UE5 LTS, Metal (iOS), landscape only, mobile-forward renderer.

## Core systems

Everything below describes math/behavior implemented once in the C# oracle (`FlyingGame.Core`/`FlyingGame.Sim`) and ported to the C++ runtime (`FlyingGameCore`/`FlyingGameSim`). The description is shared; only the implementation language and shipping status differ.

### RigidBody6DOF (Core)
State: world position, attitude quaternion, body-frame velocity (u,v,w), body rates (p,q,r). Full inertia tensor including Ixz product (matters for spin — it couples roll and yaw). Integrator: RK4 at a fixed small step (target 200 Hz sim substeps inside a 50 Hz game tick), quaternion renormalized each step. Double precision internally if float proves noisy post-stall (see Risks).

### AeroModel (Core) — strip theory, because spins must be emergent
Each lifting surface (wing, horizontal tail, vertical tail) is divided into spanwise strips. Per strip, per step:

- local velocity = body velocity + **ω × r** (r = strip position vs CG) + prop wash (later) + wind
- local AoA/sideslip from that velocity → Cl/Cd/Cm looked up in a **full ±180° airfoil table** (linear region, stall break, flat-plate post-stall)
- control surfaces (aileron/elevator/rudder/spoiler) modify the strip's local incidence/camber
- strip forces × arm → summed forces and moments on the body

This one mechanism produces the entire north star without special cases: roll damping at low AoA, autorotation and wing drop past stall, developed spins, and **adverse yaw** (deflected-aileron strips carry more induced drag on the rising wing). Nothing is scripted; a 2-33 spins because its config data says it should.

Fuselage: simple drag + side-force + damping terms (not stripped).

### PropModel (Core, arena 2+)
Interface defined now, implemented later: thrust from a prop table, plus P-factor (asymmetric disc loading at AoA), spiral slipstream (induced sidewash at the vertical tail), and reaction torque. Multi-engine = two instances at offsets → asymmetric thrust for free.

### Atmosphere (Core)
ISA density vs altitude (thin-air arena later), steady wind + gust field interface. Wind is also what the BubbleField reads — bubbles ride the same air mass the wings feel.

### Input (Bridge/UI)
Two virtual RC touchpads. LEFT pad: vertical = throttle on powered aircraft; **on the glider the same lever is the speed brake** — neutral (centered thumb) = fully stowed, full aft = fully deployed, forward of neutral does nothing. Same thumb geometry across every aircraft. LEFT horizontal = rudder. RIGHT pad: vertical = elevator, horizontal = aileron. Per-axis dead zone, expo curve, and rate limits live in config, not code — feel tuning is data. Controller support later.

### BubbleField (Bridge)
An infinite-feeling, evenly spaced 3D grid of small bubbles, **fixed in the air mass** (they translate with wind relative to terrain). Implementation: only a local block around the aircraft exists; GPU-instanced rendering (Niagara particles or Instanced Static Mesh Component); when the aircraft moves a grid cell, bubbles wrap modulo grid spacing so the field never ends. Distance fade by shrinking the mesh (avoid transparent overdraw — see Risks). v0 is passive flow visualization: relative streaming past the canopy IS the AoA/sideslip display. A later pass adds local deflection near the wing (upwash/downwash) driven by the same aero state.

### Camera (Bridge)
Chase cam with velocity-vector-aware lag so sideslip and AoA are visible as the nose pointing away from the flight path. Alternate views later.

### Challenge/Arena (Game)
A `ChallengeRunner` loads a `ChallengeDefinition` (data, not code): spawn state, live tolerance bands, scoring mode (accuracy % or points), pass/fail rules, callouts. Arena = a level + a list of challenges. New challenges should mostly be new JSON, not new C++.

### Progression/Economy (Game)
`ProgressSave` (local JSON, versioned) tracks per-challenge best scores, unlocks, credits, lives. Interfaces defined now; lives/IAP logic and StoreKit are **explicitly out of scope** until the game earns it.

## Data contracts
See [DATA-CONTRACTS.md](DATA-CONTRACTS.md) for `AircraftConfig`, `ChallengeDefinition`, `ProgressSave` field-level specs. Engine-agnostic JSON, read identically by the oracle and the C++ runtime.

## Risks & mitigations

1. **Bubble field mobile perf** (the #1 risk). Thousands of camera-facing bubbles = transparent overdraw, which kills mobile GPUs on fill rate. Mitigation: GPU instancing, hard count budget (~2–4k), opaque or alpha-tested bubbles with size-fade instead of alpha-fade, no soft particles. Build a stress level FIRST in the slice and profile on the oldest supported iPhone before anything else depends on the look.
2. **6DOF numerical stability.** Post-stall strip aero is stiff; large rates + big table gradients can blow up an integrator. Mitigation: small fixed step + RK4, table smoothing at the stall break, rate clamps as a last-resort safety net (logged when hit, never silently), energy-drift check in the flight-test harness. Applies equally to the oracle and the C++ port — same integrator/step choice on both sides.
3. **Oracle/port drift.** Two independent implementations of the same math can silently diverge. Mitigation: golden-trajectory regression tests — record the oracle's state history for a battery of scripted control-input sequences, replay the same sequences through the C++ port, and assert a match within tolerance. Runs in CI on every change to either implementation; a failing diff blocks merge before it ever reaches a device.
4. **Spin fidelity is a tuning project, not a code feature.** Strip theory will spin; making it spin *like a 2-33* (spanwise washout, tail power, Ixz) takes iteration. Mitigation: the headless flight-test harness + Ty's UPRT experience as the acceptance test. Spin challenge is deliberately later in the ladder (matches the "design park" note).
5. **Touch feel.** RC dual-stick on glass is unforgiving; bad expo/dead zones will read as "bad physics." Mitigation: feel parameters in data, tuned in the slice on-device from day one.
6. **Scope.** Six arenas + economy is a big game. Mitigation: the vertical-slice contract in [VERTICAL-SLICE.md](VERTICAL-SLICE.md) — nothing outside it gets built until it ships.

## Out of scope this session
No Unreal project, no C++ port, no aero implementation beyond the oracle, no IAP, no branding. Next step when Ty approves: **"build the slice."**
