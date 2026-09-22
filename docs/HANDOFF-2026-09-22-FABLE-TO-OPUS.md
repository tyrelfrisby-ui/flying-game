# Aero Playground — handoff (2026-09-22, Fable 5.1 session → Opus 5.5 session)

Read this first, then `docs/memory/*.md` (verbatim copies of the previous session's memory: rules, gotchas, physics decisions).

## What this is
iOS flight-training game "Aero Playground" (bundle `com.tyrelfrisby.aeroplayground`). Unity 6000.3.23f1 front end + an engine-agnostic C# flight sim.
Owner: Tyrel Frisby, CFI, Cub/glider pilot, decisive, limited hands-on time — **build, don't survey**; ship every change to his phone/TestFlight and report plainly.

- Repo: `~/Documents/flying-game` (private GitHub `tyrelfrisby-ui/flying-game`), branch **`split-surfaces`**, HEAD `03e1795`. Working tree clean.
- Sim: `src/FlyingGame.Core` (aero, gear, prop, terrain) and `src/FlyingGame.Sim` (Aircraft, TrimSolver, TugPilot, DronePilot, `Practice/PracticeScenario.cs`). Compiled into Unity from source.
- Unity: `unity/Assets/_Project/Scripts/Bridge/**` (FlightSimDriver, TouchFlightControls, ChaseCamera, StartMenu, OptionsPanel, PracticeController/PracticeHud/LessonIllustrator under `Bridge/Practice/`, Net/, Combat/, Pilot/).
- Aircraft configs: `configs/aircraft/*.json` — **copy to `unity/Assets/StreamingAssets/aircraft/` after every edit** (`cp configs/aircraft/*.json unity/Assets/StreamingAssets/aircraft/`); it is a copy, not a symlink.
- Tests: `tools/FlightTests` (xUnit, 248 passing). Run: `~/.dotnet/dotnet test tools/FlightTests` (~3 min). Practice/lesson tests: `PracticeScenarioTests.cs` (has a `TraceSTurns` scratch test with a 1 Hz trace facility — reuse it for any tuning).

## Build / ship (all tracked in `tools/deploy/`)
- `zsh tools/deploy/deploy.sh` — clean Unity export → xcodebuild (generic iOS) → install+launch on the **iPhone** (UDID in script). Log `build/deploy.log`; C# errors in `build/ios.log` (`grep 'error CS'`).
- `zsh tools/deploy/install-devices.sh` — installs the last build on every connected device (iPhone `757C3F5F-…`, iPad Air 11 `5BE5CD6C-…`, iPad mini `E7648C42-…`). "unable to locate device" = asleep/unplugged.
- `zsh tools/deploy/testflight.sh` — archive → App Store Connect upload → OTA dev ipa to `build/ota/FlyingGame.ipa` (served at https://mac-studio.tail9aad2a.ts.net/ota/ over Tailscale; the iPad Air is NOT on Tailscale). Verify with `grep 'Upload succeeded' build/testflight.log` and `== OTA ipa refreshed`.
- Version stamp = `bundleVersion` in `unity/ProjectSettings/ProjectSettings.asset` (date-time), shown in green at the top of the in-game screen.
- Current: phone has **2026.0921.0648** (latest code). TestFlight/OTA have 2026.0916.0632 (same features) — refresh them if anything ships.
- Commit after each shipped change; end commit messages with the Co-Authored-By / Claude-Session trailer the harness gives you.

## State of the product (what exists)
- Fleet of ~20 types, real USDZ models for some (`Bridge/AirframeModels.cs`), procedural airframes for the rest.
- Ground handling: castoring sprung tailwheel with 45° unlock (see memory), per-type springs, nosewheel steering sign fixed, brakes −25 %, red BRAKES ON readout, FLAP 0/50/100 buttons.
- Aero realism (2026-09-15): Clark Y wing table with flat-plate post-stall lift/drag and CP shift, real tail lift slope, 30° elevator, **Cub/172/bush CG at 27 % MAC with the gear moved with it** (other types still have the old nose-heavy placement — same fix if the owner reports them), flaps add tail downwash, propeller efficiency falls off below design speed, realistic fuselage drag/Oswald on the fixed-gear types. Cub held stall ≈ 1,300 fpm mush; 172 Vy ROC ≈ 1,000 fpm (book ~850).
- FLYING LESSONS (setup screen, column 3) + practice exercises: crosswind rudder/aileron (level, landing), flare, flare side view (weight-on-wheels arrows), approach side view (glideslope), S-turns practice (game rolls at ≤1 s stop-to-stop aileron, 45°/s, reversing at 45°; user rudder) + scored test, stalls (side view with lift/tail/relative-wind arrows; rudder; elevator), Straight (wings level, rudder neutral), Climb/level/descend, Glide speed-to-fly (rear + side), Vy and Vx climbs. YOU FLY axis toggles; illustrated spoken briefings rendered live from the game (`LessonIllustrator`); sim frozen during briefing, GO → 3-2-1 countdown → live.
- Chase camera sits **exactly on the flight path** (no lag): air-relative aloft, **ground track** in ground-reference mode (runway lessons and < 100 m AGL). Owner: "the foundation of the app".
- Side-view exercises are longitudinal-only (`ConstrainLongitudinal` after each step).
- Control feel: per-axis expo/dead-zone in OPTIONS (PlayerPrefs).

## Owner's standing rules (from memory — read `docs/memory/`)
- Never read `mesh.vertices` in a player build; instancing "Keep All"; configs copied not linked.
- No instrument/button may cover the aircraft; don't move dials with bank.
- Tests must reset `Atmosphere.SteadyWind` (practice scenarios drive it).
- He judges by feel on the device; expect tuning requests phrased as pilot observations — measure with a trace before changing gains.

## Open / likely next
1. Owner has not yet flown: the lessons on the phone with the frozen briefing + countdown, the exact-path camera, the S-turn ball readout — expect tuning.
2. Other types' CG placement (Decathlon, Pitts, Pawnee, Stearman, Extra, Gee Bee, P-51…) still nose-heavy like the Cub was; check trim probe (`TrimProbeElevatorNeededVsSpeed`, `PitchMomentBreakdown` tests) before touching.
3. Sim still ~15 % optimistic on climb; 172 wing area in config 14.3 m² vs real 16.2.
4. iPad Air 11 needs Tailscale for the OTA link (or use TestFlight / USB).
5. TestFlight external group already set up; public link exists (see memory).
