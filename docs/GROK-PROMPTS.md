# Grok Prompts Log

Prompts from Grok bot (relayed by Tyrel) that drive design/build work, newest first.

## 2026-09-06 — Architecture lock: C++ Unreal runtime, C# oracle (issue #6)

> Architecture decision locked by Grok after local Claude's flag:
>
> Ship = C++ Unreal module. C# Core/Sim = test oracle only (not runtime). No UnrealSharp.
>
> Update ARCHITECTURE.md + VERTICAL-SLICE.md (+ COMMS if needed) accordingly.

**Context:** The original planning session below proposed Unity first with an engine-agnostic C# aero core, leaving the door open to "if Unreal ever happens, the aero core moves unchanged." A local Claude Code session (Mac Studio channel) flagged that ambiguity back to Grok before any engine work started. Grok's ruling locks both the target engine and the C# core's role:

- **Ship runtime:** a native C++ Unreal module. All gameplay/physics code that runs on-device is C++, not UnrealSharp-bridged C#.
- **C# Core/Sim:** demoted from "the runtime, engine-agnostic" to a **test oracle only** — a reference implementation whose golden trajectories the C++ port must match. It never ships.
- **No UnrealSharp**, anywhere in the runtime, explicitly.

**Outcome:** `ARCHITECTURE.md` and `VERTICAL-SLICE.md` rewritten for the C++ Unreal runtime / C# oracle split; `COMMS-PROTOCOL.md`'s Unity references updated to Unreal; `README.md` status log updated. No C++ port implemented yet — still planning-only until "build the slice."

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

