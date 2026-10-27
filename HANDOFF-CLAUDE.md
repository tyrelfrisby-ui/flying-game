# HANDOFF — Claude lanes wrap-up (2026-10-27, Mac Studio)

Ty is moving Claude work to his own Claude setup. Codex work continues unchanged. Everything below is **local only**:
nothing was pushed or merged by the Claude lanes. Times are PDT. "split-surfaces" = flying-game base ed2cc7c.

## 0. TL;DR — what Ty needs to do
1. **GPL decision on the own EA300 before App Store release** (not a TestFlight blocker) — §1.1.
2. **Regenerate the Glass Overlay MCP automation token** (it was printed once in an agent log on 2026-10-27) — §2.1.
3. **Merge decisions**: flying-game `lane/claude-ea300-swap` + `lane/claude-regional-atmosphere` (both fast-forward onto
   split-surfaces; together they conflict only in AGENT-LOG.md). Glass `lane/claude-qa-2026-10-27` (⚠️ its AGENT-LOG.md
   commit wiped the shared log — see §2.1 before merging).
4. Answer the atmosphere NEEDS YOU questions (1–13, all have working defaults) — §1.2.
5. The Glass Overlay launchd Claude worker is installed but has been failing (exit 127) — keep, fix or unload — §3.
6. Stray uncommitted `unity/ProjectSettings/ProjectSettings.asset` in the flying-game main checkout — §1.5.

---

## 1. flying-game (~/Developer/flying-game)

| branch | last commit | vs split-surfaces | worktree | tests |
|---|---|---|---|---|
| lane/claude-ea300-swap | d03cb13 (10:37) | +15 / −0, unmerged | (worktree now on the OTA branch, see 1.4) | covered by 1.4 run: 729 pass / 3 skip / 0 fail |
| lane/claude-regional-atmosphere | 4434ed9 (14:36) | +6 / −0, unmerged | ~/Developer/flying-game-worktrees/claude-regional-atmosphere | 736 pass / 3 skip / 0 fail (Claude, full suite); 11 atmosphere/roughness/calm tests re-run by Grok Bot: pass. Unity batchmode compile clean |
| lane/claude-ota-ea300-atmos | 068db23 (14:17) | +20 / −0, local build branch | ~/Developer/flying-game-worktrees/claude-ea300-swap | 729 pass / 3 skip / 0 fail (full suite, Grok Bot) |
| lane/claude-handoff | this file | +1 | ~/Developer/flying-game-worktrees/claude-handoff | docs only |

### 1.1 EA300 swap — lane/claude-ea300-swap (d03cb13)
- Contains the codex/own-ea300 chain 638c05a…37bd66d (own Extra EA300 v1–v12 + HANDOFF entry) plus Claude's swap d03cb13.
- What changed in the game: our own EA300 (`Models/extra-300-like/extra-300-like-own.glb`, Talk Flying livery) is the Extra
  ("Aerobat Monoplane") for **every** livery; `n300uy` livery loads `extra-300-like-own-n300uy.glb` via `AirframeModels.OwnSuffix`.
  Paint baked, no tints/decals (Livery.cs). Old Sunrise.* node toggle removed (AirframeVisual). AircraftRender accepts `-livery n300uy`.
  Renders of talk-flying / n300uy / sunrise / type: 4/4 control surfaces, 1/1 propellers bound.
- FG EA300 retired: it only ever existed as the earlier `-own.glb` on codex/fg-models; v12 replaced that file, so no FG mesh is in the branch.
  The downloaded Sketchfab Extra (`extra-300-like.glb`, CC-BY, ~0.7 MB) is still in Resources and ships, but is never loaded.
- CREDITS (docs/CREDITS.md + StreamingAssets/credits.md): Patrizio Melis GPL FlightGear EA300 as shape reference (Ty's choice), fonts SIL OFL 1.1, GPL review pending.
- **GPL review (Claude, not legal advice):** shell v2–v12 was fitted closely to the GPL mesh → likely a derivative. Cockpit, engine, inlets,
  prop and paint are ours. GPL content is not safe on the App Store. **Ty decides:** (a) ask Patrizio Melis (github.com/cobe571) for written
  permission (draft in AGENT-LOG), or (b) move the v12 details onto the clean-room v1 shell 638c05a. Not a blocker for TestFlight/OTA.
- **No livery picker exists** — N300UY is only selectable via the `AERO_LIVERY` env var (`Liveries.Available(id)` added for a future picker). On a phone you only get Talk Flying.
- To merge: fast-forward split-surfaces → d03cb13 (brings the 15 EA300 commits). Resolve the GPL question before any App Store build.
- Shipped: TestFlight 2026.1027.1038 (build 202610271038), uploaded 10:45.

### 1.2 Regional atmosphere + mountain roughness — lane/claude-regional-atmosphere (4434ed9)
Commits: 61fe42d field/terrain/airport winds/turbulence/config · 9c97ed7 per-surface sampling (CG, both tips, tail) + 19 tests ·
55920df Unity: menu "Air: REGIONAL/uniform", thermals, effects read local air, `J` debug overlay · 1a53a90 AGENT-LOG ·
a7e1c96 mountain roughness + lake smoothing · 4434ed9 AGENT-LOG.
- Config: `configs/atmosphere/regional-atmosphere.json` (+ StreamingAssets copy). Only free flight uses regional air; challenges/contests/lessons/events use uniform air.
- **Calm zone `home-airport-calm`:** anchored to `WorldTerrain.DefaultAirportIndex` (Valley, 1,000 ft); stadium round the main runway centreline,
  1,800 m past each threshold, radius 1,200 m; ceiling 1,500 ft AGL (762 m MSL); blend 600 m lateral / 150 m vertical. Inside it returns a fixed
  ISA sample with every wind/gust/turbulence/thermal value exactly 0 (tests: 128 points × 4 times; 1,897 samples over its lakes).
- Ridge/bluff: crest speed-up (15→~24 kt), V·∇h slope lift (0→8.3 m/s), lee sink + rotor (not in light wind). Turbulence: von Kármán (default)/Dryden,
  MIL-spec intensities, varies with height/roughness/terrain/region. Orographic turbulence scales with wind/slope/roughness, decays above ridges, lee-wave bands in stable air.
- Airport 2 (Bench): ~5 kt, seeded smooth 360° wander (mean 4.8 kt, ≤6°/s). Airport 3 (Mesa): 060/5G15 with smooth gust onset/decay (mean 5.03, peak 14.97 kt).
- **Roughness (a7e1c96):** factor scales terrain turbulence, rotor, lee sink and shortens/lengthens the turbulence scale (not crest speed-up, slope lift or waves).
  Rough: The High One 1.85, Huntington 1.75, Alaska range 1.6, Mesa→Summit wall 1.5, Frank Church canyon 1.45, Foraker/Dragontail 1.35, Bench→Mesa wall 1.3, Reno mesas 1.25, Cathedral 1.15.
  Smooth: Alpine (lake) range 0.85, Valley bluff 0.8, Colchuck dome 0.7, sea cliffs 0.6, rolling hills 0.5. Unlisted = 1.0.
- **Seaplane lakes:** detected from terrain lake data (landable Cascades lakes Outlet/Shelf/Larch, Knik + Colony glacier lakes, each airport's lake, canyon lake; harbour excluded;
  Hidden/Cirque/Crown not landable → not smoothed). Zone = water + 300 m shore + 1,500 m approach corridors off both ends (and an alpine lake's outlet valley), ±250 m, fading over 600 m.
  Inside: terrain turbulence/rotor/lee sink ×0.35 (on top of the mountain factor), general low-level turbulence ×0.7, length scale ×1.3 (`lakeSmoothing` block).
  Worst point at 25 kt: Outlet turb 19.6→6.9 m/s, rotor 52.9→18.5, sink −8.7→−3.0; Knik 26.5→9.3, 73.8→25.8, −16.0→−5.6.
- Tests: MountainRoughnessTests 7/7 (rough vs smooth exactly 1.6/0.6; lakes exactly ×0.35 over 928 samples; max roughness gradient 0.0036/m; calm field still exactly 0).
- **Not yet flown** in the editor or on a device (J overlay, moved thermals, feel).
- To merge: fast-forward split-surfaces → 4434ed9. If the EA300 swap is merged too, AGENT-LOG.md is an add/add conflict — keep both sides (union), as in 068db23.
- **NEEDS YOU (AGENT-LOG 1–13, defaults in place):** 1 calm zone covers the bluff's lower face — keep or narrow? · 2 should events fly regional air? ·
  3 should "Turbulence: Calm" disable regional turbulence? · 4 hot-day spawn trim vs ISA home field (tiny) OK? · 5 altimeter: pressure altitude + local QNH? ·
  6 one owner for the sim clock (AI traffic speeds gusts; pre-existing)? · 7 Mesa wind 060 OK? · 8 mountain magnitudes harsh (−3.5 m/s sink, σ 4.7 in 23 kt) — tune `orographicScale`/`leeSinkFraction`? ·
  9 not flown yet · 10 Valley thermals moved 350–600 m out — OK? · 11 rotor speeds too strong in big ranges (74 m/s at 25 kt) — cap/`rotorStrength`? ·
  12 glacier lakes only ~×0.6 (multiplicative) — absolute cap e.g. ≤×0.4? · 13 non-landable lakes stay rough — right?

### 1.3 Combined OTA — lane/claude-ota-ea300-atmos (068db23)
- Local build-only branch: merge of d03cb13 + atmosphere 1a53a90 (AGENT-LOG union). **Does not include roughness (a7e1c96/4434ed9).** Do not merge it; merge the two lanes instead.
- Built OTA **2026.1027.1418 (202610271418)** at 14:18, development export, hosted at https://mac-studio.tail9aad2a.ts.net/ota/ (tailnet only;
  python http.server :8792 serving ~/Developer/flying-game/build/ota via `tailscale serve /ota`). IPA verified to contain
  `models/extra-300-like/extra-300-like-own` and `-own-n300uy` and `atmosphere/regional-atmosphere.json`; profile includes iPhone 17 Pro 00008150-001629A83662401C (expires 2027-04-28).
- Why Ty saw the old red Extra: an 11:01 build (2026.1027.1101) from the main checkout on split-surfaces (no swap) overwrote the OTA IPA. Backup: /tmp/FlyingGame-2026.1027.1101-backup.ipa.
  Anything that runs deploy/testflight from the main checkout will overwrite build/ota again.

### 1.4 Worktrees / stray work
- ~/Developer/flying-game-worktrees/claude-ea300-swap → checked out on lane/claude-ota-ea300-atmos (clean).
- ~/Developer/flying-game-worktrees/claude-regional-atmosphere → lane/claude-regional-atmosphere (clean). Also holds a copy of the gitignored `scripts/claude-run.sh`.
- ~/Developer/flying-game-worktrees/claude-handoff → this branch.
- Older Claude Code subagent worktrees in `~/Developer/flying-game/.claude/worktrees/` (2026-10-13 spin work): `spin-autorotation-fix` 288af7d (+4 unmerged:
  3c7bf54, ffa15a8, ebf875c, 288af7d), `spin-step3-hysteresis` 91c3cef and `spin-rotary-autorotation` 49af347 (both already contained in split-surfaces). Review or prune.
- Runner: `scripts/claude-run.sh` (gitignored; `scripts/claude-lane-run.sh` never existed), lock ~/Library/Logs/FlyingGame/claude-run.lock, log claude-run.log. No run active.

### 1.5 Stray ProjectSettings change (main checkout)
`~/Developer/flying-game` is on split-surfaces with one uncommitted change, **not made by the Claude lanes**: `unity/ProjectSettings/ProjectSettings.asset`
bundleVersion 2026.1011.1303 → 2026.1027.1101 and iPhone buildNumber → 202610271101 (mtime 11:01, from that 11:01 iOS build). Left untouched;
safe to discard with `git checkout -- unity/ProjectSettings/ProjectSettings.asset` if nobody needs it.

---

## 2. Glass Overlay (~/Documents/overlay-studio-dev, ~/Documents/overlay-qa-wt)

### 2.1 QA lane — lane/claude-qa-2026-10-27 (543730c4, 10:22), worktree ~/Documents/overlay-qa-wt
- Based on develop/v2 707c1731; +18 / −0, unmerged, not pushed, worktree clean.
- Fixes QA-1…QA-17 (each its own commit): QA-1 MCP Origin substring check + QA-2 negative/huge Content-Length (6e73d13f, security/crash) ·
  QA-3 Int(Double) traps (0117d66e) · QA-4/5 out-of-range program times, strict bools (64e303f1) · QA-6 NaN in aero_widget args (75abd904) ·
  QA-7 hide VPAUAggregateAudioDevice (c057129f) · QA-8 live_draw pen colour validation (fb127edd) · QA-9 stdio bridge hang/CRLF (1c1d2cf3) ·
  QA-10 bundled glass-overlay-mcp refresh (6b31585f) · QA-11 media/voicechain self-tests build (323f86c7) · QA-12 string "false" turned widgets ON (00209c56) ·
  QA-13 handoff sync bumped recording folder dates (a8f8bfae) · QA-14 capture_window status-item crash (8309c31b) · QA-15 open_settings panes (f499e463) ·
  QA-16 start_recording keeps the session title (5bba062c) · QA-17 negative timeline on scene-only recordings (9058f61d).
- Tests (as reported by Claude, not re-run by Grok Bot): 39/39 offline self-tests pass; new scripts/mcphttp-selftest; 126-tool garbage-arg sweep, no crashes;
  Round 2 recording/export/calls OK. Not tested: real mouse/keyboard UI (no Accessibility/Screen Recording grant), going live (needs a human click; public YouTube), iOS.
- ⚠️ **Merge blocker:** commit 5296e3eb replaced AGENT-LOG.md wholesale (+45 / −1073 lines — the whole Studio-worker journal, NEEDS YOU and HANDOFF sections).
  Before merging, restore develop/v2's AGENT-LOG.md and re-add the QA report as a new section (or drop 5296e3eb/2487ffee/543730c4 and keep the QA report in a separate file).
  Code commits merge as a fast-forward otherwise.
- **NEEDS YOU (from the QA log):** 1 **regenerate the MCP automation token** (printed once in an agent log; redacted since) — the studio-worker's `--mcp-config` uses it ·
  2 grant Accessibility + Screen Recording to the agent shell for real UI testing · 3 "Mac Studio Speakers"/DELL monitor listed as mic inputs (unresolved) ·
  4 ~607 Debug build warnings · 5 eight recording folders already have bumped dates (pre-QA-13) · 6 live.glassoverlay.app 404s for bundle files (Channel-2.mov, Glass-Packed.mov) ·
  7 Settings sheet window has no title (capture_window works by "#N") · 8 review/merge the lane. Also open: scene-only recording program export renders black; joining a nonexistent call room reports "connected, 0 participants".

### 2.2 Other Claude items in Glass Overlay
- `claude/grok-plan` bc485e6a (2026-10-26, worktree ~/Documents/overlay-studio-claude-grok-plan): plan doc "Glass Overlay for AI agents (MCP auth, scopes, confirmations, events, remote control, Grok provider)"; +1 / −3 vs develop/v2, unmerged.
- develop/v2 AGENT-LOG open items addressed to Claude: HANDOFF `[codex → claude] codex/audit-and-look` — review A-D commits 776f9690 / 00a74d23 / bed083f2 and resolve the
  export-queue free-space margin (needs max(10 GB, 10 %) free when the clone fails → blocks external-drive exports; proposed fix: big margin only for copies > 64 MB). **Still open.**
  NEEDS YOU `[claude, 10-26]` main checkout was on codex/audit-and-look — now on develop/v2 (resolved).
- ~/Documents/overlay-studio/.claude/worktrees/ holds 47 old Claude Code subagent worktrees (one locked). Review/prune when convenient.
- ~/Documents/overlay-studio-dev (develop/v2 707c1731) has 14 uncommitted files (BACKLOG.md, legal/*, …) that are not from the Claude lanes — untouched.

---

## 3. Running Claude processes / schedulers (reported, nothing disabled)
- **launchd `com.glassoverlay.studio-worker`** (~/Library/LaunchAgents/com.glassoverlay.studio-worker.plist): runs
  `~/Documents/overlay-studio-dev/scripts/studio-worker.sh` (`claude -p` with the MCP automation token) at 00:07 / 06:07 / 12:07 / 18:07. Loaded, **last exit 127**:
  every run since ~Oct 21 fails with `zsh: can't open input file …/studio-worker.sh` (launchd zsh can't read ~/Documents — likely a privacy/Full Disk Access permission).
  Last successful run 2026-10-21 00:12 (idle). Still loaded. To stop: `launchctl bootout gui/$(id -u)/com.glassoverlay.studio-worker`.
- Interactive Claude Code sessions alive since Fri 2026-10-23 07:53: pid 5682 `claude --resume` (cwd overlay-studio-dev), 5758 `claude --continue` (cwd flying-game),
  5817 and 5864 `claude --resume` (cwd ~). Plus the Claude desktop app and `claude --chrome-native-host`.
- No crontab. No `claude -p` lane runs active.
