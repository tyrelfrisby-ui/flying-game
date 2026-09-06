# Grok Prompts Log

Prompts from Grok bot (relayed by Tyrel) that drive design/build work, newest first.

## 2026-09-06 — Build the slice, steps 1–2 (Core/Sim + flight tests)

Issue #3, `@claude`, labeled `build-slice`: execute VERTICAL-SLICE.md build order steps 1–2 only
(headless Core/Sim + flight-test harness), no Unity project.

**Shipped:**
- `src/FlyingGame.Core` (pure C#, no UnityEngine refs): `Vec3`/`Quat` (double precision), `Atmosphere`
  (ISA troposphere density stub, still-air wind), `RigidBody6DOF` (full inertia incl. Ixz product term,
  fixed-step RK4, quaternion kinematics), `DataContracts` (`AircraftConfig` + JSON loader via
  `System.Text.Json`), `Aero` (`AirfoilTable` with ±180° linear interpolation, strip-theory `AeroModel`).
- `src/FlyingGame.Sim` (pure C#): `Aircraft` (assembles config + rigid body + actuators with dead
  zone/expo/rate-limit shaping), `SimLoop` (200 Hz fixed-timestep loop), `TrimSolver` (damped
  Newton solve for wings-level glide trim).
- `configs/aircraft/glider-2-33-like.json`: 2-33-like `AircraftConfig` (~25:1 target glide, 19 strips
  across wing/hStab/vStab, hand-authored ±180° `clarkY-like` and `naca0012-like` airfoil tables with a
  genuine stall break), left lever = speed brake (`axisMap: aftOnly`, drag-only spoiler) per Ty's
  refinement.
- `tools/FlightTests` (xUnit, references Core+Sim directly): trim convergence (Newton solve + a 5s
  sim-loop hold that must stay bounded), static stability signs (pitch dCm/dalpha<0, yaw weathercock
  dN/dbeta>0), roll-rate sanity (roll damping dMx/dp<0), adverse-yaw sign (roll/yaw moments opposite
  sign under aileron alone), stall-break behavior (Cl rises then breaks past stall, Cd keeps climbing).

**Verification:** could not run `dotnet test` inside this sandboxed Action run (`--allowedTools` permits
git/read-only shell only, not `dotnet`/`python3`). Local/CI command: `dotnet test tools/FlightTests`
(uses `ProjectReference`s to Core+Sim, no solution file needed). Physics signs were hand-derived and
cross-checked against the strip-theory geometry before writing the assertions — see the PR description
for the derivation — but this run has not executed the suite. Flagged for Grok/Ty to run and confirm green
before treating steps 3–6 (Unity project/Bridge) as unblocked.

## 2026-09-06 — Architecture + skeleton planning (FULL)

> You are starting architecture + skeleton planning for a new iOS project: working title "flying game" (final name TBD — sky/level-up theme under discussion; do NOT brand or rename yet).
>
> OWNER: Ty Frisby — aerobatic / UPRT / aerospace background; vibecodes with Claude; little formal coding experience. Glass Overlay is his primary shipping product; this is a SEPARATE future product. Do NOT touch Glass Overlay / overlay-studio-dev.
>
> GOAL: An iPhone/iPad flight TRAINER disguised as a game. Players unknowingly learn real envelope skills (including accurate spins) via visuals + challenges — not FS2024-style huge margins.
>
> PLATFORM & FEEL
> - iOS (iPhone + iPad)
> - Mostly chase-cam
> - RC-style dual touchpads: LEFT = throttle + rudder; RIGHT = elevator + aileron
> - Primary visual: airplane flying through an evenly spaced BUBBLE MATRIX (air mass). Bubbles stationary in still air / move vs terrain; stream over the airframe so AoA and sideslip are VISIBLE. Gauges (IAS, alt, etc.) exist but stay subtle.
> - Engine lean: Unity first (owner rejected Godot; Unreal still optional later). Architecture should keep aero/physics engine-agnostic where practical.
>
> PHYSICS NORTH STAR (non-negotiable)
> - Full rigid-body inertia (mass + Ixx/Iyy/Izz + products) → control response from moments, not "aileron = roll rate"
> - Spanwise kinematic AoA from roll/yaw rates (ω × r): low-AoA damping; post-stall autorotation / wing drop / spin as EMERGENT modes (not canned animations)
> - Accurate adverse yaw from aileron deflection
> - Prop effects when powered: P-factor, spiral slipstream, torque
> - Multi-engine later: asymmetric thrust on failure
> - Bubble field must read these effects visually
>
> CURRICULUM (arenas)
> 1) Flat farm GRID + floating N/S/E/W letters. Aircraft: Schweizer 2-33–like glider (~25:1), spoilers only (no flaps).
>    Ladder: (1) wings-level straight heading (2) pitch for best glide / min sink (3) random cardinal turn callouts (e.g. west → "turn left to north" = 270° left) with adverse yaw (4) stall+recover (5) SPIN — design park; deep pass later (6) ballistic roll (7) loop (8) altitude-holding slow roll (9) runway landing (10) strong headwind landing (11) crosswind landing
> 2) Same grid + Cessna 172–like: prop effects, Vx/Vy, landings then takeoffs; repeat skills with power
> 3) Pitts S-2B–like: rolls/slow rolls → combo aerobatics → hangar / bridge stunts → pylon race
> 4) WWII fighter: qualify airframe → gunnery / strafe / bomb / BFM (practice scoring only — NO return fire; ground-reference teaching; e.g. shoot during a roll for points)
> 5) Jet: similar + high altitude / thin air
> 6) Multi: asymmetric thrust — Seminole-like → King Air-like → airliner
> FREE PLAY landscape: pylons, open hangar, runway, ridge-lift hill, thermals — any unlocked aircraft
>
> META / MONETIZATION
> - Pass: % accuracy on most challenges; points on some (flexible per challenge)
> - Fail: early bail/eject = free restart; crash = lose a life (gliders may full-reset from start). Lives = IAP; excellent play earns points/credits to buy lives/unlocks (skill path so spending isn't forced)
> - Unlock free-play aircraft by clearing that type's levels, OR pay / spend earned credits
> - Combat = scoring practice only
>
> YOUR JOB THIS SESSION (planning only — owner said not ready to full-code yet)
> 1. Propose a clean Unity (or engine-agnostic) architecture: folders, assemblies, core systems (Input, RigidBody6DOF, AeroModel, BubbleField, Challenge/Arena, Progression/Economy, Camera).
> 2. Define the minimal VERTICAL SLICE for Arena 1 Challenge 1–2 only (straight + best glide) — what ships in v0 skeleton.
> 3. Spec data contracts: AircraftConfig (mass, inertia tensor, aero tables), ChallengeDefinition, ProgressSave.
> 4. Call out risks (mobile perf for bubble field, numerical stability of 6DOF, spin later).
> 5. Write a short ARCHITECTURE.md + VERTICAL-SLICE.md the owner can review.
> 6. Do NOT implement full aero, spin, App Store IAP, or all arenas yet. Do NOT create a giant unfinished game. Skeleton + docs only unless owner says "build the slice."
>
> SUCCESS: Owner can read the docs and say "build the vertical slice" next. Ask clarifying questions only if they block the architecture.

**Outcome:** ARCHITECTURE.md, VERTICAL-SLICE.md, DATA-CONTRACTS.md written same day. Next trigger phrase: **"build the slice"**.

## Workflow update 2026-09-06 — direct Grok→Claude (no Ty relay)

Ty asked not to be monkey-in-the-middle. Grok Bot (Chief) now runs Claude Code
headlessly on the Mac Studio via:

```
/Users/tyfrisby/Documents/flying-game/scripts/claude-run.sh
# optional: --bg for background agent
```

Prompt body lives in `scripts/NEXT-PROMPT.md` (Grok overwrites per task).
Logs: `~/Library/Logs/FlyingGame/claude-run.log`
Auth: existing claude.ai Max OAuth (do not use `--bare`).
Ty still approves big triggers (e.g. "build the slice") in chat with Grok;
Grok then invokes Claude directly and verifies via git + docs/.

