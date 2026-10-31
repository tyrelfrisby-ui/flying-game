# Island world — G1 review

**Phase 1B built; G1 PENDING Ty.** Branch `lane/codex-island-world`. Implementation commit `e1d13ca`; the following local validation/handoff commit contains the continuity repair and final report. G1a approval at `e23bb64` is preserved verbatim in the first session commit. No push, merge, rebase, deployment, device install/export/upload, or later phase was performed.

## World and files

The world contains 51 authored fields/strips/lanes/beaches, eight start-airport sites, 14 airparks, five outer runway islands, Sitka’s symmetric cratered volcano and surrounding islets, six hard-sand beaches, three IAC boxes and four combat zones: CZ1, CZ2, CZ3 and CZ5. CZ4 is dropped. McCall is the only high-DA box. AB1/AB2 each have nine marker rafts, a separate judge raft and four corner buoys.

The runtime includes the terrain/coast/water, flat or explicitly sloped pads, airport and airpark buildings/markings/parked aircraft, all six towers and five wires, seeded forest and driftwood, rivers/bars/glaciers, OC city/rings/skybridges/landing pad, the offshore race course/carrier, Catalina/Avalon/sea arch, Powell dam/arches/spires/marina, Page mesa landing planes, lesson/practice terrain, and both cavity shells. Drake has exactly one north portal and the full 2,992 ft strip, including its indoor markings. Land runway 17 and depart through that same north portal; no reverse final, exemption or second portal exists.

New shared sources are `src/FlyingGame.Core/IslandValue.cs`, `IslandData.Generated.cs`, `IslandTerrain.cs`, `IslandWater.cs`, `IslandGeometry.cs`, `IslandCavities.cs` and `IslandSites.cs`, with tracked metadata. Existing world entry points changed in WorldTerrain, FrankChurch, Forest and Seaside. Unity uses `IslandWorldBuilder.cs`, WorldBuilder and SeasideBuilder; fixed cameras are in WorldRender. Tools are under `tools/island/` and `tools/IslandCheck/`. No new runtime asset kind is read from StreamingAssets. No StreamingAssets file or test source was changed.

[Approved plan and full changes ledger](ISLAND-WORLD-PLAN.md) · [Spec](island-spec.json) · [Map PNG](island-map.png) · [Elevation PNG](island-elevation.png) · [Concept gallery](island-concepts/README.md) · [Saved image_gen prompts](island-concepts/prompts.json)

## Local validation

| Check | Result |
| --- | --- |
| Shared C# / Python terrain | PASS at 8,042 points; maximum error 2.96e-12 m |
| Runtime pads/cavities/surfaces/starts/zones | PASS, 554 probes |
| Built blocks/ramps/arches/rings | PASS, 4,934 known final/airway checks; no Drake exemption |
| Pad/peak/lake boundary continuity | PASS, 53,656 sampled probes outside the authored Drake cliff; largest 10 cm step 3.2114 m |
| Determinism | PASS: all nine generated review outputs byte-identical on full regeneration |
| .NET Core/Sim/test-source compile | PASS; existing warnings retained |
| Unity world/camera source check | PASS against installed Unity 6000.3.23f1 engine/editor assemblies, with external adapters stubbed |
| Unity player / iOS build | UNVERIFIED: Unity PackageManager socket denied in sandbox |

[Fit evidence](island-fit-check.json) · [Runtime probes](island-runtime-checks.json) · [Terrain parity](island-runtime-parity.json) · [Solids](island-runtime-solids.json) · [Boundary checks](island-continuity-check.json) · [Legacy continuity sweep output](island-continuity-legacy-sweep.json) · [Compile/build limits](island-build-checks.json)

## Fit results

| Rule | Current result |
| --- | --- |
| 1 — Pads/cavity detail | PASS design geometry |
| 2 — Mean grade between flat fields | PASS |
| 3 — Approach clearance | UNVERIFIED |
| 4 — Airway room | PASS known geometry / UNVERIFIED T201 |
| 5 — 20 km ring | UNVERIFIED |
| 6 — Spacing/shore setbacks | PASS |
| 7 — Box canyons | PASS profile / UNVERIFIED flight |
| 8 — Boxes/combat zones | PASS known geometry / UNVERIFIED missing IFR |
| 9 — Determinism | PASS deterministic algorithm |
| 10 — Shared height field | PASS shared runtime evaluator / UNVERIFIED player |

Measured: **854** flat-field pairs, maximum mean grade **34.954359%**; **113** operational fans including all 12 supplied finals, zero terrain/pad/tower/wire failures; **23** known airway/perimeter swaths with minimum **2022.35 ft** clearance; **378** authored ring enclosures, all inside 20 km. Missing T201/missed/hold/circling geometry and loaded flight/zoom/turnback certification remain unverified. The independent sampled continuity probes are not a whole-domain interval proof.

## Changes since approved 1A.2

- Apply Ty’s single-portal Drake ruling and remove end B from approach generation/validation.
- Move the Gateway arch from (0.5, −6.7) to (0.3, −6.5) km to clear AP14.
- Allocate city heights/roof rings inside the known protection budgets; place Powell docks on the actual lake footprint.
- Build the off-airport Page mesa detail planes at (5, −3.7), 4,475 ft and (5.4, −3.7), 4,075 ft. These are natural landing tops in F12, not additional airport starts.
- Repair a real 238 m terrain step at the outer airpark pad-blend boundary: clearance correction now fades with pad influence and peak release; the airway budget is a continuous global layer. Exact field/summit datums remain unchanged.
- Complete height-aware cave floors/paved surfaces and the throat/chamber roof junction; reuse the existing reflective water/wake shader.
- Remove contradictory ignore rules for already-tracked legacy generated files so the mandatory pre-commit ignore check prints nothing. No mirror file was edited or untracked; Claude restores generated-file ignores with his mirror cleanup.

Every design delta has old/new/reason in the spec ledger. Locked field elevations/lengths, lake/glacier datums, Catalina placement, variation and ring remain intact. The 1A.2 wider mainland and provisional survey values remain approved TY1–TY8 assumptions.

## Existing tests and Claude handoff

Unchanged-source xUnit baseline: **765 passed / 15 failed / 3 skipped**. Frozen repaired-height world run: **670 passed / 110 failed / 3 skipped**. **97 failing cases are absent from baseline**; 13 also fail in baseline. This is not a green integration suite.

[Every failing case and message](ISLAND-WORLD-BROKEN-TESTS.md) · [Machine-readable comparison](island-test-results.json). The baseline has two temporary CREDITS fixture artefacts and eight sandbox-denied LAN cases. Known aircraft and GreatLakes mirror failures are retained. The old continuity test now reports only the two Drake north-cliff cells; its legacy allow-list has no Drake location. No test source was edited. Final cavity surface changes are separately covered by the runtime probes.

Claude owns the pending event/AI/scoring, chart/ATIS/menu, runway-choice/ApproachSpawn, lesson/arena/tow and weather bindings. `IslandSites`, `IslandGeometry`, `IslandCombatZone` and `IslandZoneView` expose the new placement/state/visibility data. Targets and drones are initially hidden; COLD forces them hidden. Claude must connect HOT active-volume entry and AI, reconcile IFR/world coordinates, supply missing protection polygons, and later delete the legacy waterfall tunnel/tests. Existing legacy spawn/arena/chart paths still need that reconciliation and are not flight-verified. All handoffs are in AGENT-LOG.

## Screenshots and Open for Ty

**No fixed WorldRender screenshots are available.** Unity batch startup exits with `Error: listen EPERM: operation not permitted /tmp/Unity-Upm-37487.sock`. Eighteen fixed cameras are authored in WorldRender for overview, regions, both caves, airpark, all boxes and CZ5. The nine inspected concept images were generated with the built-in image_gen tool and are explicitly references, not game screenshots.

G1 remains pending: local Unity/player build and fixed-camera capture; Ty’s VFR review of terrain/material readability and loading performance; runway/start/arena reconciliation; cave entry/contact/roof/skylight, float ramps, beach/mesa landings and lesson canyons. TY1–TY8 are already approved; no new locked-decision exception is requested. Full IFR/weather/flight certification remains outside this session.

`[codex -> Ty] build check needed: run tools/deploy/deploy.sh`

The session ends at G1 with `[codex -> Claude] island world built, ready for IFR + weather - PENDING Ty`.
