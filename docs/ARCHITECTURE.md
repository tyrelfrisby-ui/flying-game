# Flying Game — Architecture

Working title only. iOS (iPhone + iPad), Unreal Engine first, aero core engine-agnostic. Planning doc — nothing here is built yet.

## The one big decision

**The flight model does not use Unreal's physics.** All aero and rigid-body math lives in a pure C# assembly with zero engine references (no `UnrealEngine`/UE API usage). Unreal is just the renderer, input surface, and scene host: each frame it hands stick positions to the sim and copies the sim's position/attitude onto the airplane's Actor transform.

Because Core/Sim stay pure C# but Unreal's native language is C++ (with Blueprints on top), the two sides need an interop plan. The simplest credible path for an iOS target: compile `FlyingGame.Core` + `FlyingGame.Sim` with **.NET NativeAOT** into a static library exposing a thin C ABI (`[UnmanagedCallersOnly]` exports — step, set-controls, read-state). A UE C++ module (`FlyingGame.Bridge`) links that static library directly and calls it every tick. This avoids embedding Mono/CoreCLR (which needs JIT — disallowed by Apple on iOS) and keeps Core/Sim's source completely engine-free; only the Bridge module knows the C ABI exists. This is a plan, not yet built — see "Out of scope this session" below.

Why this matters for this project specifically:

1. **The physics north star is testable without a screen.** Because the core is plain C#, we can run automated "flight tests" as unit tests: trim the glider, command full aileron, and assert the nose yaws *away* from the turn (adverse yaw); hold full aft stick + rudder and assert autorotation develops with the right rotation rate sign. Spin fidelity gets verified in CI, not by eyeballing the chase cam.
2. **Engine-agnostic for real.** Core/Sim have zero engine references; if the target engine ever changes again, only the Bridge/interop layer moves, not the physics.
3. **Precision control.** We choose our own integrator and timestep instead of fighting the engine's built-in rigid-body physics (Chaos), which was never built for post-stall aerodynamics.

## Modules

```
FlyingGame.Core     pure C#, zero engine references. Compiled via .NET
                    NativeAOT into a static lib with a C ABI (iOS has no
                    JIT, so this is the interop path, not Mono/CoreCLR
                    embedding). Math, atmosphere, RigidBody6DOF, AeroModel,
                    PropModel (stub), data contracts.
FlyingGame.Sim      pure C#, same NativeAOT target as Core. Fixed-timestep
                    sim loop, aircraft assembly (airframe + surfaces +
                    engine), flight-state snapshot API.
FlyingGame.Bridge   Unreal C++ module. Only module that calls the Core/Sim
                    C ABI. Adapters: sim→Actor transform, touch input→sim
                    controls, camera rig, bubble field renderer,
                    ground/crash detection.
FlyingGame.Game     Unreal C++ + Blueprints. Challenge runner, arenas,
                    scoring, progression, economy (stubs until needed),
                    save/load.
FlyingGame.UI       Unreal UMG/Slate + Blueprints. Dual touchpads, gauges,
                    HUD, menus, callouts.
```

Dependency direction is strictly downward: `UI → Game → Bridge → Sim → Core`. Core and Sim never reference anything Unreal; Bridge is the only module that crosses the C#/C++ boundary.

## Folder layout

```
flying-game/
  docs/                      planning + Grok prompt log
  unreal/                    Unreal project (TODO: not yet scaffolded)
    Source/FlyingGame/
      Core/  Sim/            pure C# sources, built via NativeAOT into a
                              static lib the Bridge module links against
      Bridge/  Game/  UI/    Unreal C++ modules
    Content/                 Blueprints, maps, art, audio
    Config/                  AircraftConfig + ChallengeDefinition JSON
  tools/FlightTests/         dotnet test project compiling the SAME
                             Core+Sim source files for headless flight tests
```

Unreal target: UE 5.x, mobile forward renderer, landscape only, Metal.

## Core systems

### RigidBody6DOF (Core)
State: world position, attitude quaternion, body-frame velocity (u,v,w), body rates (p,q,r). Full inertia tensor including Ixz product (matters for spin — it couples roll and yaw). Integrator: RK4 at a fixed small step (target 200 Hz sim substeps inside a fixed 50 Hz host tick), quaternion renormalized each step. Double precision internally if float proves noisy post-stall (see Risks).

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
No Unreal project, no code, no aero implementation, no IAP, no branding (a placeholder `unreal/` folder marked TODO is fine; no scaffolded content). Next step when Ty approves: **"build the slice."**
