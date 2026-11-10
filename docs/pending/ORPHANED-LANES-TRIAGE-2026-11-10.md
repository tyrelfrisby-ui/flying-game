# Triage: the 75 lanes in neither shipping lane (2026-11-10)

Ty, shown that 75 branches carried commits in neither `lane/claude-tv-companion` nor `lane/claude-island-nav` and that
twelve of them ended with a **NEEDS YOU** commit, chose the triage over the merge: *"Triage agent: tell me what each one
is. One agent reads all 75, works out what is finished / what is stale / what conflicts with this week's work, and comes
back with a ranked list and a one-line question for each of the twelve NEEDS YOU. Then you answer twelve questions
instead of opening 75 branches."*

So this is a report, not a merge. **Nothing was merged, nothing was pushed, no lane's ref was moved and no lane's
worktree was entered.** Every number below was read with `git merge-tree --write-tree` (which writes no ref), `git
rev-list`, `git ls-tree` and `git diff` between refs. Reproduce any line with the commands in *Method*, last section.

The headline is not the twelve. It is this: **of the 75, only about 30 hold work that is both unlanded and still worth
something.** Twelve are log entries whose code already shipped — the question is orphaned, not the work. Nine are
superseded, two of them loudly. Three are not orphans at all. That is the shape of the problem, and the ranked table
below is ordered so you can stop reading when the names stop mattering.

---

## 1. The twelve questions

One line each, answerable yes/no or pick-one. The lane name is the place the answer goes; the queue number is the
existing line in the decision queue where there is one. Fuller context under each lane in the table.

| # | Lane | The question, in one line |
|---|---|---|
| 1 | `claude-inertia` | **The Extra's pitch inertia nearly doubled (1100 → 2038 kg·m², built up from its components) and a sharp pull now gives half the pitch acceleration (280 → 169 °/s²) — fly it and tell me whether it now feels sluggish, before anyone tunes anything.** |
| 2 | `claude-crash-cam-occupants` | **The fix that keeps the crash camera on the pilot's section is done and proven (Pitts rear cockpit went 93 % onto the tail; after the fix 100 % stays on target) but it moved one water-breakup test from 1.45 to 0.90 m/s against a 1.0 floor — merge it and re-band that test, or hold until the test is understood? (Q151)** |
| 3 | `claude-j3-skis` | **Three ski band tests are red because the real friction numbers do not reach the AFM's figures: accept the physics and move the test bands to where the measurements put them, or make settled and wet snow stickier than any published source? (Q154)** |
| 4 | `claude-ski-followups` | **The Beaver's hydraulic ski travel (tyre 0.10 m below the ski up, ski 0.05 m below the tyre down, 15 s of pumping) is three estimates with no drawing found — do you have the real numbers, and do you want the PSM's "⅔ for taxiing on runways" position as a third detent?** |
| 5 | `claude-wheel-skis` | **Renders are waiting at `~/Developer/agent-lanes/renders/wheel-skis/README.md` — is that your hinge (links 12° over centre down, 80° forward up, tyre on top of the lowered ski), and is the selector in the right place in the C172, J-3 and Pitts? (Q079–Q083)** |
| 6 | `claude-tree-branch-yaw` | **`CrownGrip = 3` is a feel multiplier, not physics — the derived model gave +0.8° of yaw through a fir crown and the game now gives +7.0°: keep 3, or go up or down? And should a wing come off at less than the current ~16–20 cm of stem? (Q140, Q141)** |
| 7 | `claude-start-altitude` | **For a random clear-air multiplayer start I chose 3 NM or 1,000 ft from every airborne pilot, never inside 1 NM and 500 ft over the first minute, powered 3,000–7,000 ft AGL — are those the numbers?** |
| 8 | `claude-windsocks` | **PAPI at the six island airports with instrument approaches, 2-bar VASI at KAVX and PATK, runway lights at 19 of 39, no aid anywhere else — is that the split you meant, and do the renders in `renders/airport-lighting/` look right? (Q110–Q113)** |
| 9 | `claude-floor-windows` | **The Pitts floor-window sizes are estimates with no dimensioned drawing found (front 22.0 × 14.2 in, rear 18.1 × 14.2 in) — give me the real ones, and does the Pitts get a clear floor in both cockpits or the rear only?** |
| 10 | `codex-landing-v2` | **Five types have a verified W&B envelope drawn and the rest say "No envelope authored" — the C172's is known but its config's empty weight is 877 kg so the real 2300 lb gross would flag every two-person load as over gross: retune the 172's mass, or leave it with no envelope?** |
| 11 | `claude-cockpit-common` | **The Kollsman popup and knob fix (popup drawn over every overlay, knobs off the players' own shaders — URP/Lit drew them magenta on the Mac player) never reached the shipping lane and its screenshots are from the build *before* the fix: do you want it re-shot for approval, or merged on the measurement alone?** |
| 12 | `claude-ski-physics` | **Nothing — this lane is the common base of #3 and #4 and holds no decision of its own.** Answer 3 and 4 and this one is spent. (Verified: `claude-ski-physics` is an ancestor of both `claude-j3-skis` and `claude-ski-followups`.) |

**So it is eleven questions, not twelve.** `claude-ski-physics` is contained whole inside two other lanes on the list;
its "NEEDS YOU (run 4)" is the soft-snow-depth decision you already gave on 2026-11-03 at 21:01, carried forward.

---

## 2. The ranked list

Ranked by what a pilot would feel first, not by size. Size is in the table so you can see where it *isn't* value:
`claude-bent-wings` is one commit and the loudest finding here; `codex-decathlon` is 32 and changes nothing a pilot
flies.

`Merge` is `git merge-tree` against `lane/claude-tv-companion` with `AGENT-LOG.md` discounted (every lane conflicts
there; it is not information).

### Band A — a pilot feels it on the next flight, and it is parked on you

| Lane | Commits | Class | What it is, and what merging it changes for a pilot | Merge |
|---|---|---|---|---|
| `claude-inertia` | 26 | **NEEDS YOU + CONFLICTS** | The Pitts', Great Lakes' and Extra's mass properties built up from components, the tail past the stall, the Extra's real wing section (MA 15 S / MA 12 S through XFOIL), prop polar moments. A pilot feels a different Extra: half the initial pitch acceleration, a late abrupt wing-drop stall, 2.3 s per spin turn. **Six open questions besides #1**, including whether the fleet-wide ε ∝ C_L change lands (it fails `StallAuthorityTests` on every monoplane) and that the Great Lakes CG does not close — the build-up is 5 in aft of A18EA's limit. | 13 files, incl. `AeroModel.cs`, `PropModel.cs`, `Aircraft.cs`, 5 configs, `glide-table.json` |
| `claude-endo` | 31 | **NEEDS YOU + CONFLICTS (worst surface here)** | A superset of `claude-inertia` (verified ancestor) plus the tail at large sideslip: the slipstream drifting with the crossflow, the fuselage's crossflow vortex pair over the tail (Polhamus RM L55E20b), the Endo tumble probe. The honest result is written in its own log: the Extra's Endo **does not tumble** — the push sends α to −25…−60° and it inverted-snaps instead. | **21 files**, incl. 13 aircraft configs, `AeroModel.cs`, `PropModel.cs`, `AircraftConfig.cs`, `Aircraft.cs` |
| `claude-j3-skis` | 10 | **NEEDS YOU** | Ski contact physics: the buried face of a ski sunk past its upturn (Yosida & Huzioka 1950), no wheel under a straight ski, a plank of patches across the ski's width, and the prop meeting the firm base rather than the powder's top. A pilot feels 0.6 m of level powder hold a J-3 at full power, and a full-stall powder landing being a one-way trip. Your Q011 answer ("OK merge after chase + Beaver retractable") is satisfied; only the three band reds stand in the way. | **Clean** (log only) |
| `claude-ski-followups` | 11 | **NEEDS YOU** | `claude-ski-physics` + the Beaver's retractable hydraulic combination wheel-skis, on a selector, starting down on snow and up anywhere else. Brakes do nothing on a lowered ski (measured: 10.5 m rollout from 8 m/s against 106.7 m on plain wheels). Open and awkward: the Beaver in wet new snow **now never breaks out**, fresh or after an hour — the protruding tyres used to carry part of it. | 1 file (`TouchFlightControls.cs`) |
| `claude-wheel-skis` | 5 | **NEEDS YOU** | Hinged wheel-skis on *every* wheel on every aeroplane, with one shared WHEELS/SKIS selector part placed from anchors measured on the GLBs. Whichever of ski bottom or tyre is deeper bears the leg — no surface rule, no per-type pose. Built, Unity-compiled, rendered, `BUILD SUCCEEDED`, parity OK, nothing installed. | 2 files (`Aircraft.cs`, `QuestMenu.cs`) |
| `claude-crash-cam-occupants` | 3 | **NEEDS YOU** | The defect you reported twice (t915u, t919u) and then doubted (t920u) is **proven on the code you were flying**: `AirframeContact.FuselageStations` cut the Pitts' tail at x −0.70 with the solo pilot's rear seat at −1.14, so a bent *or* broken tail boom carries the cockpit off the section the camera follows — 93 % of the rear cockpit, camera keeps 4 %. Intermittent by nature, which is exactly what you saw. Fix: every seat's mass station stays 0.5 m inside the cabin. After: 100 % of every seat on target, every scenario. | 1 file (`CLAUDE.md`) |
| `claude-start-altitude` | 7 | **NEEDS YOU** | Start high for spin testing (Default / 3,000 / 6,000 / 10,000 ft AGL / custom, saved), plus a multiplayer start queue and a random clear-air start so pilots stop spawning on each other. Honest finding inside it: **the wings drift more the higher you start** — the rig is fixed geometry solved at 4,000 ft, so hands off at 10,000 ft AGL the Gee Bee rolls 12.7° in 10 s, the Pitts 5.6°, the Cub 0.5°. Cancelling it needs preset aileron trim, which fights "trims centred every flight". See §4 for the reuse verdict. | 6 files, all menu/net glue |
| `claude-tree-branch-yaw` | 1 | **NEEDS YOU — already shipped** | The code **merged into both lanes** (island-nav `2eb30aab`, Quest `d95d9ef9`); only the log entry is orphaned. So a pilot already has it: a wingtip skimmed through a fir crown yaws +7.0° and slows, treetops cost speed, gear legs and floats are strike edges, a snapped stem dents the leading edge it hit and a wing panel comes off at ~20 cm of stem. What is owed is a fly-check and the two tuning calls in #6. | **Clean** (log only) |
| `claude-landing-v2` / `codex-landing-v2` | 9 / 7 | **NEEDS YOU** | The pre-flight flow: two pages, live preview, Standard/Metric (default Standard, one pref key `landing.units`), and a handbook-style W&B chart with certificated CG/gross limits for the Pitts, J-3, Great Lakes, Swift and Grob. Out-of-limits LAUNCH asks first, in your exact words. The two lanes are the Claude and Codex halves of one job; `codex-landing-v2` carries the UI restyle on top. | 2 and 4 files |
| `claude-windsocks` | 6 | **NEEDS YOU** | Two features in one lane. Windsocks: one shared part, three per runway on all 43 island runways (129 of them), 12 m outside the FAA RSA, swinging and filling from the live wind. Airport lighting: PAPI/VASI only at instrument airports, edge lights at 19 of 39, **colour emergent** from the angle between each lens and the eye every frame. Both built and rendered. | **Clean** (log only) |
| `claude-floor-windows` | 6 | **NEEDS YOU** | Clear Lexan floor windows cut through the belly and floor of the Pitts and Extra — 0.090 in polycarbonate with a smoke tint and a Fresnel sky reflection, one XR-safe pass, drawn after the water and shadow. The Pitts' front footwell is new: the windscreen bulkhead was a solid disc to the belly, so the front pilot could see no floor at all. | 13 files, incl. 7 GLBs and `BuildScript.cs` |
| `claude-cockpit-common` | 2 | **NEEDS YOU + CONFLICTS** | The cockpit-common *feature* already shipped (`b0c35552`, `cab6944d`, `9a3d2011` are all on the shipping lane; its Q057–Q060 are already noted there as open). What is orphaned is the **fix**: the Kollsman popup was being drawn under other overlays and let taps through to the glass controls beneath it, and the added knobs rendered **magenta** in the Mac player because `URP/Lit` is not in a player build. See §3 — this one would fight the shipping lane if merged naively. | 2 files (`TouchFlightControls.cs`, `GraphicsSettings.asset`) |

### Band B — unlanded work a pilot would feel, not parked on a decision, just never merged

| Lane | Commits | Class | What it is | Merge |
|---|---|---|---|---|
| `claude-wake-vortex` | 7 | **READY (large)** | A physical trailing-vortex model with a `WakeField` API fed by the player, tug, drones, replay and peers — plus, merged in from `codex-clouds`, cumulus banks, wake-reactive wispy sheets, low cloud patches and wake-stirred ground fog; and island weather with condition-driven valley fog and airport ASOS/AWOS stations issuing METARs. The biggest unlanded thing a pilot would *see*. One known open item: a mid-band sky seam. | 4 files (`AeroModel.cs`, `Aircraft.cs`, `RemoteAircraft.cs`, `BuildScript.cs`) |
| `claude-vr-cockpit` | 1 | **READY** | *You can always get out*: belt + door/canopy release, exit chosen by situation, then walk, swim, tread water; the parachute water landing; the eye under water. | 11 files — the VR lanes all overlap here |
| `claude-surface-tracks` | 5 | **READY (Part A)** | A per-step `SurfaceContact` buffer from tyres, skis, floats and skids — the foundation the ski lanes emit events into. Absent from the shipping lane entirely, so nothing has superseded it. | **Clean** (log only) |
| `claude-no-position` | 4 | **READY** | Your never-break rule enforced structurally: the crop-dust light bar and the nearest-zone miles removed, `NoPositionRevealTests` added, the rule put at the top of `CLAUDE.md`. Already in memory as a standing rule; this is the lane that made it mechanical. | 1 file (`CLAUDE.md`) |
| `claude-trigger-brakes` | 1 | **READY** | Triggers become left/right toe brakes on the 8BitDo (iOS/Mac/Quest) and Quest Touch; Quest guns move to the right grip. | **Clean** (log only) |
| `claude-input-expo` | 5 | **READY** | One shared `InputExpo` curve applied once after the dead zone — 30 % pitch/roll, 20 % yaw, same on every platform. Its own log says it was never merged because an island-nav merge it waited on never came. | 1 file (`OptionsPanel.cs`) |
| `claude-hydro-quest` | 9 | **MOSTLY LANDED** | Float hydrodynamics from first principles (Savitsky planing, ITTC friction, Michell waves, added mass) with the J-3 floating on the *drawn* EDO 60-1320 hull. 7 of 9 commits are on a shipping lane; what is left is the added-mass field placement and a settle-time test. | 10 files |
| `claude-233-launch` | 9 | **READY** | A glider runway start is on the rope at once, and the Great Lakes tows the 2-33 until the Super Cub is built (your t905u/t906u). | 1 file (`SessionSettings.cs`) |
| `claude-private-ota` | 2 | **READY** | The private Tailscale always-latest OTA publisher (your t908/t909) and an OTA content-type fix. Note: `publish-ota.sh` and `ota-http-server.py` are sitting **untracked** in the main checkout right now — this lane is where they belong. | **Clean** |
| `claude-beaver-swap` | 1 | **READY** | Swaps in the `beaver-v2` airframe (L-20A-traced), rigged, own model always — your "Swap", 2026-11-05. | **Clean** |
| `claude-sgs233-eye-raise` | 1 | **READY** | The 2-33's front eye 2 in higher, per your 2026-11-05 call; eyepoints v7 proposed. | 4 files |
| `claude-shared-instruments-t985` | 1 | **READY** | t985 cockpit repair: knob black (pink only under a pointer), Great Lakes front eye 4 in aft and 2 in down, front COM/XPDR knobs out. | **Clean** |
| `claude-app-icon` | 1 | **READY** | The AP letter app icon for iOS/Mac/Quest. | **Clean** (log only) |
| `claude-q007-pitts-spin-t918` | 4 | **READY, partly landed** | The biplane wing wake averaged over the receiving chord against NACA TR-317/TN-310, and the water mirror filling its target (the portrait reflection was lifted toward the horizon). | 2 files (`AeroModel.cs`, `TailWake.cs`) |
| `claude-test-triage` | 5 | **READY — and relevant to a live problem** | Serialises the tests that *fly*, not just the ones that set the weather; stops one missing attribute failing a fifth of the suite; parks tests for a world that is gone. Two runs agreeing at 1116/7/107. **Worth reading against the "suite runs for hours" problem** — several lanes here report suite coupling that this lane diagnoses. | 3 files (`CLAUDE.md` + 2 tests) |
| `claude-turns-stalls-doc` | 4 | **READY (docs)** | Turns and stalls from the angle of attack out, the four-phase lesson shape, the 75/25 split and the Part 141 hours checked. | **Clean** (log only) |
| `claude-crash-water` | 1 | **READY (tests + docs)** | Remnant mass, rear-panel break pieces and a true set-down ditching, with a before/after probe table. | 1 file (`AirframeContactTests.cs`) |

### Band C — model and art lanes: a pilot sees them, nobody is blocked on a decision

| Lane | Commits | Class | Note |
|---|---|---|---|
| `codex-supercub` | 9 | **READY** | Super Cub PA-18 through v9: helijah fuselage/wing (CC BY 4.0), tail and cowl traced from an N3695Z three-view, N1894A livery. You picked paint B at 17:26 on 11-04 and said "Cub looks good" on the shape. **This is the aeroplane `claude-233-launch` is waiting for** to stop using the Great Lakes as the tug. |
| `codex-own-swift` / `claude-swift-gear` / `codex-swift-gear` | 10 / 6 / 7 | **READY, overlapping** | One job in three lanes. `claude-swift-gear` ⊂ `codex-swift-gear`; `codex-own-swift` carries v6, which **you approved** (eye point still unapproved). Swift S-1 refit to the flight manual's Rys.1 three-view, cruciform tail, retractable main wheel that leaves the skids behind, gear-state HUD tint. Take `codex-own-swift` + `codex-swift-gear`; `claude-swift-gear` is redundant. |
| `claude-decathlon` / `codex-decathlon` | 11 / 32 | **READY (physics) / STALE-ish (paint iteration)** | `claude-decathlon` is the one that matters: the 8KCAB from its type certificate, POH and three-view, a NACA 1412 section polar, 40 US gal, plus your 2026-11-03 21:01 decisions on ground handling, span and aileron. **`codex-decathlon`'s 32 commits are v2→v8.9 paint-and-underside iteration** — 29 of them are a single livery converging, and v8.6 was reverted. Loud note on size: 32 commits, 469 files, and nothing a pilot would feel that the 11-commit lane does not already carry. |
| `codex-beaver-cockpit` | 5 | **READY** | Beaver cockpit v1, plus the EDO 679-4930 float set re-fitted to the true-scale DHC-2 (4 struts/side per an N1018F photo) and the v3 rig script with TCDS A-806 travels. |
| `claude-j3-cockpit` / `claude-quest-hands-install` | 2 / 3 | **READY (identical work)** | `claude-j3-cockpit` ⊂ `claude-quest-hands-install`. The J-3 water rudder reshaped off your photo — black D blade 0.287 × 0.271 m with doubler, bead and hinge fitting, standing wholly behind the post when retracted. Renders made, never approved. |
| `codex-eyepoints` | 7 | **UNCLEAR** | 72 proposed fleet eye points from seat backs and canopy surfaces, with a 1.778 m seated mannequin and a software renderer to check the fit. Never approved, and the eyepoint work has moved several times since (`claude-sgs233-eye-raise`, `t985`, v7 proposed). **What I would need:** whether `docs/models/eyepoints.json` on the shipping lane is already past this — it conflicts, which suggests it is. |
| `claude-front-sight` / `codex-front-sight` | 1 / 3 | **READY** | `claude-front-sight` ⊂ `codex-front-sight`. The front seat gets its own floating sight in its own painted-red material, and each pilot sees only his own. |
| `codex-172-cockpit` | 1 | **READY** | Flush-mount C172 instruments; fixed-pitch controls clarified. |
| `codex-sgs233-panel-flush` | 2 | **READY** | The 2-33's padded panel crown lowered flush with the skin, then another 20 mm for your Q118 review. 4 of its 5 files already match the shipping lane. |
| `codex-turn-coord` | 1 | **READY** | Thinner turn-coordinator wings tapering to standard-rate index width on all aircraft (your 2026-11-05 05:55). |
| `codex-water-visuals` | 1 | **READY** | Better water transmission, a duplicate surface shadow removed, refined spray. |
| `codex-clouds` | 1 | **STALE** | Contained in `claude-wake-vortex`, which merged it (`3fd7e81e`) and replaced its `WakeField` stub with the real one. Take the wake lane, not this. |
| `codex-hud-ladder` | 7 | **READY, needs a look** | A solid fighter-style pitch ladder over the full ±90° with scrolling airspeed/altitude tapes, after your feedback on the perspective skew. Touches the HUD — see §3. |
| `codex-pitts-debris` | 2 | **READY** | A paint-preserving offline debris pipeline rolled across the fleet with serialised validation and a blocker inventory. Merges **clean**, and it is tooling, not flight code. |
| `codex-j3-spray-visual-t923` | 1 | **READY (docs)** | Matched spray validation and the Claude integration handoff. |
| `codex-heaven-wrap` | 1 | **READY (docs)** | Heaven status wrap verification; pending the Q127 device check. |
| `codex-ea300` | 2 | **READY (provenance)** | Records your 2026-11-01 12:10 PT voice decision to ship the EA300 drawing as-is. |
| `claude-233-default` | 2 | **READY** | Defaults the 2-33 to its factory yellow own model. Overlaps the livery picker. |
| `codex-floor-windows` | 1 | **STALE** | Contained in `claude-floor-windows`, which merged it (`7a30d684`). |
| `codex-j3-skis` | 1 | **STALE — fully landed** | Its one commit is on a shipping lane; 7 of its 8 files already match. |
| `vr-cockpit-combined` | 10 | **STALE (integration branch)** | Its own first commit says what it is: *"Combine the VR cockpit lanes (local integration branch)"* — eight of its ten commits are merges of `claude-vr-cockpit` and `codex-vr-cockpit`. It is scaffolding, not work. Take the two source lanes. |

### Band D — not orphans, or nothing left in them

| Lane | Commits | Class | Why it is on the list at all |
|---|---|---|---|
| `claude-quest-islandnav` | 3 | **NOT AN ORPHAN** | This is the **live Quest shipping lane**. Its three commits are the parity merges of `lane/claude-tv-companion`. It appears only because the orphan test asks for commits in neither *named* lane, and the Quest lane's merge commits are its own. |
| `claude-model-identity` | 5 | **LANDED** | `git rev-list --count lane/claude-quest-islandnav..lane/claude-model-identity` = **0**. The four-dial cockpit row and the flight-model identity diagnostic are in the Quest lane and reach the iOS lane from there (`6428b685`). Same definitional artifact. Relevant to the standing "flight model can stick to the previous aircraft" bug — the diagnostic for it has shipped. |
| `claude-bent-wings` | 1 | **STALE — and this is the loud one** | See §3. One commit from today, duplicating work that landed today. |
| `claude-extra-water-ski` | 1 | **LOG ONLY** | The Extra below V_p on water; a missing water-drag moment found and **shelved** (Q137). No code. |
| `claude-final-spawn-crab` | 1 | **LOG ONLY** | Final-spawn crab merged (island-nav `e3e754bc`, Quest `e9afec2c`), installed on the iPad mini. Q133 (crab cap) open. No code. |
| `claude-floats` | 1 | **LOG ONLY** | Prop-damage merged and installed. Quest blocked by Q126 (the Gradle 4 GB limit). No code. |
| `claude-hinge-slavestruts` | 1 | **LOG ONLY** | Hinge + slave struts merged into both lanes and installed on both devices. No code. Merges clean. |
| `claude-hydro` | 2 | **LOG ONLY** | Build and install records for the hydro lane. No code. |
| `claude-water-fix` | 2 | **LOG ONLY** | The magenta grid was the island's water drawn into its own reflection; the Pitts water touch was measured and is not a bug. Fix already on a shipping lane (`a17f94e9`). No code. |
| `claude-pitts-spin-cap` | 1 | **LOG ONLY** | Q007 merged and installed, plus a `CLAUDE.md` lesson: two laws for one quantity. Merges clean. |
| `claude-tf-slim-playable-1106` | 1 | **STALE — landed** | TF slim packaging landed as `b170f8e0` on 2026-11-08. |
| `claude-tf-aircraft-allowlist` | 2 | **STALE — landed** | Same packaging, plus the 3,736,034,720 B measurement that is now history. |
| `claude-tf-slim-b1-t953` | 4 | **STALE — landed** | Same, plus the B1 qualification test-state fixes. Contained in both candidate lanes. |
| `claude-replay-damage-t1001-candidate` | 10 | **STALE — landed under new hashes** | All five replay commits of your pt1001 round are on the shipping lane: the prop turning (`8d5c2f4b`), the fly-by camera (`48a0af1d`), when each part broke (`c65d5b7a`), the prop at flown rpm (`2716e727`), a part riding until it broke (`78cc13ea`). Nothing left. |
| `claude-replay-smoke-t982-candidate` | 5 | **STALE — landed** | Contained in the lane above. |
| `claude-widget-idle` | 2 | **READY — and it is Job 2's subject** | The Aero Widget idles instead of pushing every 1080² frame at 60 fps whether anyone is watching (live 60 / quiet 5 / asleep 4), honours Glass Overlay's `{"cmd":"fps","value":30}`, and quits properly. Measured: asked 30 → 29.6 fps at ~35–37 % of a core. **Its own log flags the trap:** it adds `src/FlyingGame.Core/WidgetPace.cs` and `deploy.sh` was never run — a green suite is not a build. See §5. |

---

## 3. What would fight this week's work

This week landed the staggered wing failure, debris aerodynamics, the folded wing, the curve-from-the-root bend, gear
geometry and flex, all 26 fins onto `fin-lowAR`, tyre cornering stiffness 12 → 7.2/rad, the oil-loss pitch rule,
cranking resistance and compression strokes, TV mode, the canopy's sound and shatter, and the bail-out wiring. That is
225 files. Four lanes genuinely collide with it, and one of them is a duplicate.

### `claude-bent-wings` — STALE, and say it loudly

One commit from **today**, and the shipping lane already has every one of its four claims, quoting you verbatim.

Its commit message: *"Between limit and ultimate the wing now takes a permanent bend… The aileron travel shrinks
linearly from the first bend to failure, about the aileron position when it bent… A load the other way straightens the
wing and gives the travel back, but each straightening divides the remaining fatigue life by 4."*

The shipping lane's `src/FlyingGame.Core/BentAileronTravel.cs` (commit `af09e409`, today) already states the same four
rules in its header, from your own words — *"The ailerons get limited travel with bending linearly with initial bend to
failure and the center of that travel is the aileron position at the time the bending happened… Subsequent over
stresses further limit range but do not move the new center… a bend that reduces bend angle like a hard push after
bending wings up releases the travel limit but is accounted for in fatigue and has a quadruple reduction in fatigue
life."* And `OverstressMember.cs` already carries `PermanentShare = 0.5`, `ReversalFatigueFactor = 4.0` and
`Straightening()`.

So two sessions built the same feature from the same instruction on the same day, and only one merged. The orphan also
carries a known red test (`CombatTests.AerobaticDroneFliesVariedManoeuvresAndSurvives` — the target drone pulls ~7.25 g
against its 6 g limit and drifts out of the zone). **Nothing to merge. It should be abandoned, not resolved.** Worth a
process note: this is the second time this week a lane rebuilt landed work (see the windsock case below), and both
times the duplicate was invisible because neither lane was in either shipping lane.

### `claude-cockpit-common` — CONFLICTS, and a naive merge would revert working code

The orphan's `CockpitTaps.cs` was branched before local multiplayer. The shipping lane has since given that file
split-screen handling: `Awake()` now resolves a `FlightSimDriver` so the second local player's instance never replaces
the first's, and `Taps()` routes through `LocalPlayerView.ForDriver(_driver)` so P2's instruments are not touched
through P1's camera. **The orphan's version deletes both.** Merging it as-is trades the magenta-knob fix for the
split-screen fix. The fix is three small hunks (`BlocksTouch`, the shader lookups, `_popupScreen`) and should be
re-applied by hand onto the current file, not merged.

### `claude-endo` and `claude-inertia` — CONFLICTS, on the two files this week rewrote

They conflict on `AeroModel.cs`, `PropModel.cs`, `Aircraft.cs`, `AircraftConfig.cs`, `configs/glide-table.json` and
13 / 5 aircraft configs — and every aircraft config was rewritten this week when all 26 fins moved to `fin-lowAR`.
These are also the only lanes here that touch the aero tables, so the collision is unavoidable rather than accidental.

The practical consequence: **their value is in the derivations, not the diffs.** Between them they add about 3,200 lines
of `docs/aircraft/*-DERIVATION.md` — the Extra's airfoil, its fuselage camber, prop gyro, prop normal force, the tail
post-stall, and the three inertia build-ups. Four of those derivation files were *already touched this week*, which
means the shipping lane is using some of this reasoning while the lane that produced it sits unmerged. I would read the
derivations and re-derive the numbers against the current tables rather than attempt the merge.

### `wip/claude-jet-swirl-displacement` — CONFLICTS and rotting fast

Conflicts on `AeroModel.cs` and `PropJet.cs`, both of which the shipping lane changed this week. It branched on
2026-11-09 and is already in conflict after one day. See §4 for the verdict.

### Also touching the flagged surfaces, less severely

- **Gear and tyres:** `claude-ski-physics`, `claude-swift-gear`, `codex-swift-gear` and `claude-surface-tracks` all
  touch `src/FlyingGame.Core/LandingGear.cs`, which this week's gear geometry and flex work rewrote. The ski lanes
  conflict there; the surface-tracks lane does not.
- **The HUD:** `codex-hud-ladder` conflicts on `HudOverlay.cs` only — contained, but it is the HUD, so it wants a look
  rather than a merge.
- **Damage and the crash camera:** `claude-crash-cam-occupants` changes `AirframeContact.cs` and `AirframeVisual.cs`,
  both touched this week, yet merges **clean** — this week's changes and its station fix are in different parts of the
  files. That is lucky, not safe: it should be merged soon or it will stop being clean.
- **The bail-out:** `claude-crash-bailout` says in its own log that fatal-only auto-bailout was **superseded by your
  t828–t831**, and this week wired the bail-out properly (`71f14dec`, `e02d8912`). Treat as **STALE**; its only
  conflict is the `BailoutHeavenCheck.cs` editor tool.
- **Propellers:** `claude-inertia`'s prop polar moments (Pitts 9.0 → 5.0, Extra 2.5 → 2.1) predate your blade rule
  (each blade 15 % of net prop weight, CoM at 25 % of *diameter*). The rule is the authority now; these numbers want
  re-deriving under it, not merging.

### And one that is *not* a conflict, against first appearances

`claude-windsocks` looked superseded: `src/FlyingGame.Core/WindsockPlacement.cs` appears among this week's changed
files. It was **deleted**, by `2f56232e` — *"Untrack the crash/damage/windsock work: it is not mine to commit."* So a
windsock placement implementation existed briefly on the shipping lane and was withdrawn. The orphan's
`src/FlyingGame.Core/WindsockPart.cs` is therefore still the only windsock placement in the repo, and the lane merges
clean. **Not stale. Genuinely waiting on you.**

---

## 4. The two specific resolutions

### `lane/claude-start-altitude`: the Core is reusable, the glue is not

**Verdict for the agent rebuilding spawn/queue: take the engine-free files whole, rewrite everything Unity-side.** The
split is clean because the lane put its thinking in new files that do not exist on the shipping lane and therefore
cannot conflict:

**Take as-is — 7 new source files, zero conflict, 171 passing tests:**

| File | What it is |
|---|---|
| `src/FlyingGame.Core/Multiplayer/StartQueue.cs` | `Traffic`, `StartPoint`, `StartQueue.For` (occupants, queued ahead, wait), `Before` (FIFO, pilot-id tie-break so every client computes the same order with no referee), `WaitText` |
| `src/FlyingGame.Core/Multiplayer/RandomAirStart.cs` | `Band`, `Choose` (rejection sampling, area-uniform over the ring, max 600 tries, returns null rather than a bad start), `Why`, `ClosestApproach` (ternary search on the convex max(h/H, v/V) — a fixed time step could step over a fast pair) |
| `src/FlyingGame.Sim/StartAltitude.cs` | The picker's arithmetic: choices, feet AGL, the custom clamp, MSL from the ground under the start, decoding the saved int |
| `tools/FlightTests/MultiplayerStartTests.cs` | **60 tests**, all passing: zones and service times as literals, occupancy edges, wait arithmetic, four pilots computing the same queue, a head-on case rule 1 passes and rule 2 rejects, a stale report widened, and busy skies at every island field (0–40 pilots) with **every returned start re-measured by brute force** — 48/48 found |
| `tools/FlightTests/StartAltitudeTests.cs` | **111 tests**: all 34 rig types × 3,000/6,000/10,000 ft AGL over KMYL, position to the mm, rig converged with no warning, residuals < 1e-6, then 10 s hands off |
| `unity/.../Bridge/StartHold.cs`, `StartSlots.cs` | The roster wait and the field's queue points, with keys every client agrees on |

**Rewrite — 8 modified files, all of them churn-heavy plumbing:** `AirStart.cs` (`ComputeRandom`), `FlightSimDriver.cs`,
`Net/NetProtocol.cs`, `Net/NetSession.cs`, `Net/RemoteAircraft.cs`, `SceneBootstrap.cs`, `SessionSettings.cs`,
`StartMenu.cs`, plus `ClipSelfTest.cs`. Six of these conflict today, and `SessionSettings.cs` and `StartMenu.cs`
conflict with almost every lane in this report — they are not worth a merge from anyone.

**Two things the new agent should inherit rather than rediscover:**

1. **The design choice that needs your blessing first** (question #7): separation at the start is 3 NM or 1,000 ft from
   every airborne pilot; over the first 60 s, flying both straight, never inside 1 NM and 500 ft; occupied fixed starts
   kept 3 NM beyond their zone; 1,000 ft above the highest ground within 2 km; a ring 3–10 km from the field; bands
   powered 3,000–7,000 ft AGL, sailplanes 2,000–5,000, hang/paraglider 1,500–3,000. If nowhere qualifies it starts at
   the field's air start and queues — it never relaxes a rule.
2. **The hole that lane did not close, and must not be re-opened quietly:** two pilots who press FLY for a random start
   within the same few seconds both hold at the same time and cannot see each other (a waiting pilot is not traffic), so
   their picks are independent. With a 3–10 km ring and a random height a clash is unlikely but **not guaranteed out**.
3. **No server change was needed:** waiting pilots add two fields (`f.h` start key, `f.hs` join time) to the state flags
   they already send; the relay forwards `f` untouched (checked in `server/relay/src/index.ts`), and so does the LAN
   path. But **a client on an older build sees a waiting pilot as an aeroplane parked at the start.**

### `wip/claude-jet-swirl-displacement`: worth validating, but only in pieces, and this week

**Verdict: salvage it now, do not merge it, and do not let it sit.** It is one commit from 2026-11-09 whose own message
says *"WIP, NOT VALIDATED"* and is scrupulously honest about why: four changes to the traced propeller jet, each
defensible alone, which together move the Pitts' part-power upright spin for reasons it cannot explain.

**Keep regardless of the physics — two real bugs it found in our own instruments:**

- `PropJetTests` was probing points **inside the fuselage** and asserting full wash there — on the axis at the tail,
  0.3 m off a centreline in a body 0.32 m half-wide. Every tail-wash assertion built on that was measuring nothing.
- `PropJet.Probe` reported `radius + spread * path` — the **undisturbed** radius, not the tube it was describing — so
  every tail-arm diagnostic was being measured against a narrower tube than the model actually had.

Those two are not hypotheses; they are defects in the measuring equipment, and they want extracting into their own
commit whatever happens to the rest.

**Probably keep, on the physics:** the swirl going as 1/r with a Rankine core rather than as solid-body rotation
(Glauert ch. 15, Leishman 2.14 — a propeller's torque enters the air as angular momentum, so `v_theta` is fastest just
outside the hub, not at the tube's outer edge where a biplane's wing roots sit); the fuselage displacing the tube, which
is **your** observation (*"I don't think you are displacing the tube around the fuselage. It has the fuselage
penetrating its core"*) and was not a detail — on the Pitts, `cg.z` 0.2 with `thrustLineZ` −0.2 put the thrust axis
exactly on the body axis, so the tube's core, and after the 1/r change its strongest swirl, was being handed to solid
structure; and integrating the jet across a strip's span by Simpson's rule, because the Pitts' root strip is 0.82 m²
against a tube 0.69 m in radius and was flipping wholly in or out as the tube drifted.

**Why it must not be merged as it stands:** the Pitts' 40 % upright neutral-aileron spin reads α 17.7° against the 34.3°
you flew, and **none of the four changes moves it** (17.2 shipped, 17.7, 17.8, 17.7). Worse, the isolation that
originally found the cause no longer reproduces — suppressing the jet on the wings gave 34.9 on the shipped model and
gives 17.8 here, so the cause has moved off the wing. The lane's own next step is the right one and is not taken: measure
the **stabiliser's** swirl incidence, since the Rankine profile concentrates swirl near the axis, which is where the
stabiliser sits, and the blockage multiplies the jet there too.

**And the clock:** it already conflicts on `AeroModel.cs` and `PropJet.cs`, one day after branching, because this week
rewrote both. Another week and the salvage stops being cheap. This also bears directly on the standing finding that
spin control authority is *absent* rather than weak (rudder making 0.2 °/s² where recovery needs ~135) — which the
lane's own measurements reinforce by ruling out the wing.

---

## 5. Widget parity (the second job, recorded here because it ends in a question too)

Asked to enforce widget parity the way the Quest lanes are enforced — *"Enforce it like the others"* — the honest
finding is that **the literal reading cannot be built, because the widget is not a third target.** It is the same Unity
project, the same scene (`Assets/Scenes/Main.unity`) and the same `src/` as the game, built by
`BuildScript.BuildWidget` with PlayerSettings flipped and chosen at runtime by bundle id. The widget has no branch, so
the thing the Quest rule measures — commit reachability between two branches — has no widget analogue; and there is no
widget-side copy of a config, GLB, shader or physics file for a shared change to skip. **A path regex cannot express
widget parity because the widget shares not *some* paths with the app but all of them**, and a third `SHARED_RE` arm
would have been a check that reads green forever.

What *was* built, and does fire: `tools/check-parity.sh` gained **check 4**, which compares the widget's surface blob
against blob exactly as check 3 does for the GLBs — `Scripts/Bridge/Widget/`, `_Project/Widget/`,
`tools/deploy/widget.sh`, `tools/native-mac/` and `docs/WIDGET.md`. Three of those were **neutral** to the lock, the
worst being `tools/native-mac/`, which is the *source* of the `AeroNative.bundle` whose committed *product* was already
locked. The gap was live: `lane/claude-island-nav` and the Quest lane differ by 98 lines of it today with the gate
green. The check is WARN by default and FAIL under `--strict` (what `deploy-quest.sh` and the lanes run), and those
paths were deliberately **not** added to `SHARED_RE` — the hook treats every `lane/*` branch as the iOS lane, so
widening the regex would have made all ~218 lanes stop and sync before touching a Mac-only build script.

**The remaining question for you,** because it decides whether anything more gets built: your earlier rule (2026-10-09,
quoted in `AeroHost.cs` and `GameAeroPanel.cs`) was *"keep game and widget in parity"* — the same **features** on both
hosts. A capability added to `AeroWidget`/`WidgetServer` and never surfaced in `GameAeroHost`/`GameAeroPanel` is real
drift that **no path rule can catch**, because those files sit side by side in one directory on one branch. Catching it
needs a set-equality check over the capability surface both hosts publish (`WidgetServer`'s `hello` reply against
`GameAeroPanel`'s switch ids, with `Protocol` required to bump when the set changes). That is a test, not a hook, and it
is the expensive reading. `docs/PARITY.md`, *The widget*, has the full write-up.

**Two findings worth knowing independently of the widget:** `docs/PARITY.md`'s claim that the lock *"does nothing on any
branch but `lane/claude-island-nav` and `lane/claude-quest-islandnav`"* has been false since the 2026-11-08 change that
made the iOS lane default to the branch the worktree is on — the lock is live on every `lane/*` branch. And none of the
parity apparatus exists on `main` or `split-surfaces`, so in those checkouts there is no enforcement at all.

---

## 6. Method

Everything above is reproducible, and read-only. The orphan set:

```sh
for b in $(git branch --format='%(refname:short)' | grep -E '^(lane|wip)/'); do
  a=$(git rev-list --count lane/claude-tv-companion..$b)
  c=$(git rev-list --count lane/claude-island-nav..$b)
  [ "$a" -gt 0 ] && [ "$c" -gt 0 ] && echo "$b"
done            # 75 of 218
```

- **Mergeability** — `git merge-tree --write-tree --name-only lane/claude-tv-companion <lane>`, which writes no ref.
  `AGENT-LOG.md` is discounted throughout: every lane appends to it, so it conflicts everywhere and means nothing.
- **Already landed** — a lane's commit subjects against `git log --format='%s'` of both shipping lanes, then
  `git diff --quiet lane/claude-tv-companion <lane> -- <file>` per changed file. The subject test alone is not enough:
  the whole pt1001 replay round landed under **different hashes with suffixed subjects**, so the per-file content test
  is what found it.
- **Containment** — `git merge-base --is-ancestor`. Thirteen pairs, which is why eleven of the lanes above are
  redundant with another on the same list.
- **Collision with this week** — `git log --since=2026-11-08 --name-only lane/claude-tv-companion` (225 files) against
  each lane's own changed set.
- **Log-only lanes** — a lane whose only non-`StreamingAssets`, non-`AGENT-LOG.md` file count is zero. Eight of them.

### What this triage could not settle

- `codex-eyepoints` is the one genuine **UNCLEAR**: I cannot tell from the refs whether the shipping lane's
  `docs/models/eyepoints.json` is already past its proposals. It conflicts, which suggests it is, but confirming it
  means comparing 72 proposed eye points against the current file — a job in itself, and `claude-sgs233-eye-raise`
  proposing "eyepoints v7" implies at least six rounds have happened since.
- **No lane here was built or flown.** Several say `BUILD SUCCEEDED` and `NOINSTALL` in their own logs and I took them
  at their word; none of the physics claims above was re-measured. The build slot is yours.
- **The suite was not run** against any lane. Several report large failure counts that they argue are the suite's
  global coupling rather than their own change (`claude-start-altitude` and `claude-j3-skis` both show the failures
  passing when run alone), and `claude-test-triage` is the lane that diagnosed that coupling. Those claims are
  plausible and unverified.
