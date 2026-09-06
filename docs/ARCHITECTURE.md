# Flying Game — Architecture

Working title only. iOS (iPhone + iPad), Unreal Engine, aero core dual-implemented (C# design oracle + C++ shipping runtime). Planning doc — nothing here is built yet.

## The one big decision

**The flight model does not use Unreal's built-in physics.** Aero and rigid-body math is dual-implemented on purpose:

- **`Core`/`Sim` (C#, zero engine references)** — where the physics is designed, iterated on, and proven headlessly. This is the **oracle**: it never ships.
- **A ported C++ Unreal module** — the same physics, re-implemented in native C++, which is what actually runs in the shipped game.

Unreal is just the renderer, input surface, and scene host: each frame it hands stick positions to the C++ `Sim` and copies the result onto the airplane's Actor transform.

### Why C# *and* C++, not just one

- **Shipping needs C++.** Unreal on iOS can't embed a JIT-ing CLR — Apple disallows it on the App Store — so a managed aero core can't run inside the shipped app. We considered **UnrealSharp** (C# scripting for Unreal) and **rejected it for shipping physics**: it still leans on Mono/CoreCLR plumbing that's fragile and out of our control on iOS. Physics ships as native C++.
- **Design needs C#.** Iterating on the physics is fastest in plain C# — quick to write, quick to test headlessly, zero engine coupling. That's what `Core`/`Sim` stay for.

Why this matters for this project specifically:

1. **The physics north star is testable without a screen — and testable twice.** The C# oracle runs automated "flight tests" as unit tests: trim the glider, command full aileron, and assert the nose yaws *away* from the turn (adverse yaw); hold full aft stick + rudder and assert autorotation develops with the right rotation rate sign. The **same scenarios and assertions are replayed against the C++ port**; a behavior only counts as shipped once both sides agree. Spin fidelity gets verified in CI on both sides, not by eyeballing the chase cam.
2. **Deliberate, verified porting instead of hand-waved "engine-agnostic."** The C# reference isn't assumed to "just work" if dropped into an engine — every behavior it defines is explicitly ported to C++ and checked for parity before the game trusts it.
3. **Precision control.** We choose our own integrator and timestep instead of fighting Chaos (Unreal's physics), which was never built for post-stall aerodynamics.

## Modules (C# oracle + Unreal C++ runtime)

```
FlyingGame.Core         C# oracle, pure C#, zero engine references. Math, atmosphere,
                        RigidBody6DOF, AeroModel, PropModel (stub), data contracts.
                        Design + correctness tool — does not ship.
FlyingGame.Sim          C# oracle. Fixed-timestep sim loop, aircraft assembly
                        (airframe + surfaces + engine), flight-state snapshot API.
                        Does not ship.
FlyingGame.FlightTests  dotnet test project: the oracle's assertions (trim,
                        stability signs, adverse yaw, stall-break, later spin).

FlyingGame::Core        C++ Unreal module, ported from FlyingGame.Core. Plain C++,
                        no UE types, so it stays testable outside the engine too.
FlyingGame::Sim         C++ Unreal module, ported from FlyingGame.Sim. This is
                        what actually runs in the shipped game.
FlyingGame::Bridge      Unreal C++/Blueprints. Adapters: Sim→Actor transform,
                        touch input→sim controls, camera rig, bubble field
                        renderer, ground/crash detection.
FlyingGame::Game        Unreal C++/Blueprints. Challenge runner, arenas, scoring,
                        progression, economy (stubs until needed), save/load.
FlyingGame::UI          Unreal UMG. Dual touchpads, gauges, HUD, menus, callouts.
```

Dependency direction on the shipping side is strictly downward: `UI → Game → Bridge → Sim → Core` (all C++). The C# oracle has no runtime dependency on Unreal — it's connected to the shipping side only by parity tests that replay its scenarios/assertions against the C++ port.

## Folder layout

```
flying-game/
  docs/                      planning + Grok prompt log
  unreal/                    Unreal project (created when slice is built)
    Source/
      Core/                  C++ port: math, RigidBody6DOF, AeroModel, PropModel
      Sim/                   C++ port: sim loop, aircraft assembly
      Bridge/                Sim→Actor transform, touch input, camera rig, bubbles
      Game/                  ChallengeRunner, arenas, scoring, progression
      UI/                    UMG widgets: touchpads, gauges, HUD, menus
    Content/
      Config/                AircraftConfig + ChallengeDefinition JSON
      Art/  Audio/  Maps/    (Unreal calls scenes "Maps")
  tools/
    FlightTests/             dotnet test project: the C# oracle + its assertions
                             (parity harness that replays these against the C++
                             port lands alongside the port itself)
```

Unreal target: current Unreal Engine 5 LTS, mobile renderer, landscape only, Metal (iOS).

## Core systems

Each system below is designed and specified once, then exists twice: first proven in the C# oracle (`Core`/`Sim`), then ported to the shipping C++ module. The section labels (Core, Bridge, Game...) name the layer, not the language.

### RigidBody6DOF (Core)
State: world position, attitude quaternion, body-frame velocity (u,v,w), body rates (p,q,r). Full inertia tensor including Ixz product (matters for spin — it couples roll and yaw). Integrator: RK4 at a fixed small step (target 200 Hz sim substeps inside a fixed-tick loop decoupled from Unreal's variable frame rate), quaternion renormalized each step. Double precision internally if float proves noisy post-stall (see Risks).

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
An infinite-feeling, evenly spaced 3D grid of small bubbles, **fixed in the air mass** (they translate with wind relative to terrain). Implementation: only a local block around the aircraft exists; GPU-instanced meshes; when the aircraft moves a grid cell, bubbles wrap modulo grid spacing so the field never ends. Distance fade by shrinking the mesh (avoid transparent overdraw — see Risks). v0 is passive flow visualization: relative streaming past the canopy IS the AoA/sideslip display. A later pass adds local deflection near the wing (upwash/downwash) driven by the same aero state.

### Camera (Bridge)
Chase cam with velocity-vector-aware lag so sideslip and AoA are visible as the nose pointing away from the flight path. Alternate views later.

### Challenge/Arena (Game)
A `ChallengeRunner` loads a `ChallengeDefinition` (data, not code): spawn state, live tolerance bands, scoring mode (accuracy % or points), pass/fail rules, callouts. Arena = a map + a list of challenges. New challenges should mostly be new JSON, not new code.

### Progression/Economy (Game)
`ProgressSave` (local JSON, versioned) tracks per-challenge best scores, unlocks, credits, lives. Interfaces defined now; lives/IAP logic and StoreKit are **explicitly out of scope** until the game earns it.

## Data contracts
See [DATA-CONTRACTS.md](DATA-CONTRACTS.md) for `AircraftConfig`, `ChallengeDefinition`, `ProgressSave` field-level specs.

## Risks & mitigations

1. **Bubble field mobile perf** (the #1 risk). Thousands of camera-facing bubbles = transparent overdraw, which kills mobile GPUs on fill rate. Mitigation: GPU instancing, hard count budget (~2–4k), opaque or alpha-tested bubbles with size-fade instead of alpha-fade, no soft particles. Build a stress scene FIRST in the slice and profile on the oldest supported iPhone before anything else depends on the look.
2. **6DOF numerical stability.** Post-stall strip aero is stiff; large rates + big table gradients can blow up an integrator. Mitigation: small fixed step + RK4, table smoothing at the stall break, rate clamps as a last-resort safety net (logged when hit, never silently), energy-drift check in the flight-test harness (checked in both the C# oracle and the C++ port).
3. **Spin fidelity is a tuning project, not a code feature.** Strip theory will spin; making it spin *like a 2-33* (spanwise washout, tail power, Ixz) takes iteration. Mitigation: the C# oracle's headless flight-test harness, replayed against the C++ port for parity, plus Ty's UPRT experience as the acceptance test. Spin challenge is deliberately later in the ladder (matches the "design park" note).
4. **Porting drift.** A C++ port can silently diverge from the C# oracle it was ported from. Mitigation: the port is only trusted once it reproduces the oracle's flight-test assertions bit-for-sign (trim, stability signs, adverse yaw, stall-break); no gameplay ships on unverified C++ physics.
5. **Touch feel.** RC dual-stick on glass is unforgiving; bad expo/dead zones will read as "bad physics." Mitigation: feel parameters in data, tuned in the slice on-device from day one.
6. **Scope.** Six arenas + economy is a big game. Mitigation: the vertical-slice contract in [VERTICAL-SLICE.md](VERTICAL-SLICE.md) — nothing outside it gets built until it ships.

## Out of scope this session
No Unreal project, no C++ port yet, no aero implementation, no IAP, no branding. Next step when Ty approves: **"build the slice."**
