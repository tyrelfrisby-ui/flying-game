# Flying Game — Architecture

Working title only. iOS (iPhone + iPad), Unity first, aero core engine-agnostic. Planning doc — nothing here is built yet.

## The one big decision

**The flight model does not use Unity's physics.** All aero and rigid-body math lives in a pure C# assembly with zero UnityEngine references. Unity is just the renderer, input surface, and scene host: each frame it hands stick positions to the sim and copies the sim's position/attitude onto the airplane's Transform.

Why this matters for this project specifically:

1. **The physics north star is testable without a screen.** Because the core is plain C#, we can run automated "flight tests" as unit tests: trim the glider, command full aileron, and assert the nose yaws *away* from the turn (adverse yaw); hold full aft stick + rudder and assert autorotation develops with the right rotation rate sign. Spin fidelity gets verified in CI, not by eyeballing the chase cam.
2. **Engine-agnostic for real.** If Unreal ever happens, the aero core moves unchanged.
3. **Precision control.** We choose our own integrator and timestep instead of fighting PhysX, which was never built for post-stall aerodynamics.

## Assemblies (Unity asmdefs)

```
FlyingGame.Core     pure C#, noEngineReferences=true. Math, atmosphere,
                    RigidBody6DOF, AeroModel, PropModel (stub), data contracts.
FlyingGame.Sim      pure C#. Fixed-timestep sim loop, aircraft assembly
                    (airframe + surfaces + engine), flight-state snapshot API.
FlyingGame.Bridge   Unity. Adapters: sim→Transform, touch input→sim controls,
                    camera rig, bubble field renderer, ground/crash detection.
FlyingGame.Game     Unity. Challenge runner, arenas, scoring, progression,
                    economy (stubs until needed), save/load.
FlyingGame.UI       Unity. Dual touchpads, gauges, HUD, menus, callouts.
```

Dependency direction is strictly downward: `UI → Game → Bridge → Sim → Core`. Core and Sim never reference anything Unity.

## Folder layout

```
flying-game/
  docs/                      planning + Grok prompt log
  unity/                     Unity project (created when slice is built)
    Assets/_Project/
      Scripts/{Core,Sim,Bridge,Game,UI}/   one asmdef each
      Configs/               AircraftConfig + ChallengeDefinition JSON
      Art/  Audio/  Prefabs/  Scenes/  Settings/
  tools/FlightTests/         dotnet test project compiling the SAME
                             Core+Sim source files for headless flight tests
```

Unity target: current Unity 6 LTS, URP (mobile render pipeline), landscape only, Metal.

## Core systems

### RigidBody6DOF (Core)
State: world position, attitude quaternion, body-frame velocity (u,v,w), body rates (p,q,r). Full inertia tensor including Ixz product (matters for spin — it couples roll and yaw). Integrator: RK4 at a fixed small step (target 200 Hz sim substeps inside Unity's 50 Hz FixedUpdate), quaternion renormalized each step. Double precision internally if float proves noisy post-stall (see Risks).

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
Two virtual RC touchpads. LEFT pad: vertical = throttle (spring-loaded off for glider spoiler variant TBD), horizontal = rudder. RIGHT pad: vertical = elevator, horizontal = aileron. Per-axis dead zone, expo curve, and rate limits live in config, not code — feel tuning is data. Controller support later.

### BubbleField (Bridge)
An infinite-feeling, evenly spaced 3D grid of small bubbles, **fixed in the air mass** (they translate with wind relative to terrain). Implementation: only a local block around the aircraft exists; GPU-instanced meshes; when the aircraft moves a grid cell, bubbles wrap modulo grid spacing so the field never ends. Distance fade by shrinking the mesh (avoid transparent overdraw — see Risks). v0 is passive flow visualization: relative streaming past the canopy IS the AoA/sideslip display. A later pass adds local deflection near the wing (upwash/downwash) driven by the same aero state.

### Camera (Bridge)
Chase cam with velocity-vector-aware lag so sideslip and AoA are visible as the nose pointing away from the flight path. Alternate views later.

### Challenge/Arena (Game)
A `ChallengeRunner` loads a `ChallengeDefinition` (data, not code): spawn state, live tolerance bands, scoring mode (accuracy % or points), pass/fail rules, callouts. Arena = a scene + a list of challenges. New challenges should mostly be new JSON, not new C#.

### Progression/Economy (Game)
`ProgressSave` (local JSON, versioned) tracks per-challenge best scores, unlocks, credits, lives. Interfaces defined now; lives/IAP logic and StoreKit are **explicitly out of scope** until the game earns it.

## Data contracts
See [DATA-CONTRACTS.md](DATA-CONTRACTS.md) for `AircraftConfig`, `ChallengeDefinition`, `ProgressSave` field-level specs.

## Risks & mitigations

1. **Bubble field mobile perf** (the #1 risk). Thousands of camera-facing bubbles = transparent overdraw, which kills mobile GPUs on fill rate. Mitigation: GPU instancing, hard count budget (~2–4k), opaque or alpha-tested bubbles with size-fade instead of alpha-fade, no soft particles. Build a stress scene FIRST in the slice and profile on the oldest supported iPhone before anything else depends on the look.
2. **6DOF numerical stability.** Post-stall strip aero is stiff; large rates + big table gradients can blow up an integrator. Mitigation: small fixed step + RK4, table smoothing at the stall break, rate clamps as a last-resort safety net (logged when hit, never silently), energy-drift check in the flight-test harness.
3. **Spin fidelity is a tuning project, not a code feature.** Strip theory will spin; making it spin *like a 2-33* (spanwise washout, tail power, Ixz) takes iteration. Mitigation: the headless flight-test harness + Ty's UPRT experience as the acceptance test. Spin challenge is deliberately later in the ladder (matches the "design park" note).
4. **Touch feel.** RC dual-stick on glass is unforgiving; bad expo/dead zones will read as "bad physics." Mitigation: feel parameters in data, tuned in the slice on-device from day one.
5. **Scope.** Six arenas + economy is a big game. Mitigation: the vertical-slice contract in [VERTICAL-SLICE.md](VERTICAL-SLICE.md) — nothing outside it gets built until it ships.

## Out of scope this session
No Unity project, no code, no aero implementation, no IAP, no branding. Next step when Ty approves: **"build the slice."**
