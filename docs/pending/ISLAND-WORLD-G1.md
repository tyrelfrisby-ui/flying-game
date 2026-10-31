# Island world — G1 review

Phase1B implementation on `lane/codex-island-world`, pending Ty. G1a approval at `e23bb64` is preserved verbatim in AGENT-LOG. This session makes local commits only and stops at G1.

The [plan and complete changes ledger](ISLAND-WORLD-PLAN.md), [spec](island-spec.json), [map](island-map.png), [elevation](island-elevation.png), [fit evidence](island-fit-check.json), [runtime probes](island-runtime-checks.json), [terrain parity](island-runtime-parity.json), [built-solid screening](island-runtime-solids.json), and [concept gallery](island-concepts/README.md) accompany the code. Final existing-test comparison is being completed and will be recorded before the gate handoff.

The world includes51 fields/strips/lanes/beaches,14 airparks, five outer runway islands, Sitka's volcano and islets, six hard-sand beaches, three IAC boxes and four combat zones (CZ4 dropped). Drake has exactly one north portal, with runway17 arrival and departure through that same portal. Its reverse final and exemption are absent. Both cavity shells have height-aware interior floor routing and solid roofs.

New source files: `src/FlyingGame.Core/Island{Value,Data.Generated,Terrain,Water,Geometry,Cavities,Sites}.cs` and their metadata; `unity/Assets/_Project/Scripts/Bridge/IslandWorldBuilder.cs` and metadata. Existing world entry points updated in WorldTerrain, FrankChurch, Forest, Seaside, WorldBuilder and SeasideBuilder; fixed WorldRender cameras updated. Reproduction/verification tools are under `tools/island/` and `tools/IslandCheck/`. No new runtime StreamingAssets kind is read.

The known geometry checks pass:854 flat-field pairs, maximum mean grade34.954359%;113 operational fans including12 supplied finals, zero terrain/pad/tower/wire failures;23 known airway/perimeter swaths, minimum2025ft; no authored ring/spacing/shore failures; AB/CZ known protections pass. Missing T201/missed/hold/circling volumes and flight/zoom/turnback validation remain unverified.

The .NET core/sim/test-source builds and the limited Unity world-source check pass. The latter uses installed Unity engine assemblies with external adapters stubbed; it is not a Unity player build. Unity batch startup fails on the sandbox's denied PackageManager socket (`listen EPERM ... Unity-Upm-37487.sock`). Fixed WorldRender screenshots and Unity/iOS BUILD SUCCEEDED cannot be produced here. No device build, installation, export or deployment was attempted.

Open for Ty at G1: local Unity/player build and VFR review, terrain/material readability and loading performance, cave entry/contact/roof/skylight behavior, float-base ramps and beach landings, Page mesa landings and each lesson box canyon. Approved TY1–TY8 remain accepted assumptions, including provisional survey values. No new locked-decision exception is requested.

Claude integration handoffs are in AGENT-LOG: event/AI/scoring/engagement and chart/ATIS/menu hooks; runway-choice/ApproachSpawn and lesson arena bindings; IFR/weather reconciliation and missing protection geometry; later legacy tunnel/test cleanup. These bindings remain pending and are not represented as complete in this world-geometry gate.

`[codex -> Ty] build check needed: run tools/deploy/deploy.sh`
