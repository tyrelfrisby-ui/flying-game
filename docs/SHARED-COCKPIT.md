# One cockpit for iOS/Mac and the Quest

Ty, 2026-11-04, after flying the Extra on the Quest: *"these fixes need to apply to both quest and ios/mac so i don't
get two different lanes."*

## Why there were two

Not platform code: **lane history**. The VR-cockpit work (P3a controls, P3b one-plane panel, P3d hood, P3e throttle and
canopy handles, P3f switch row and shorter sticks) was done on the Quest lineage (lane/claude-vr-cockpit →
lane/claude-quest-islandnav → lane/claude-quest-cockpit-switches) and edited the Extra GLBs in place. island-nav kept the
pre-VR Extra (130 nodes), and P3f (60f3ff48) never reached either island-nav or the Quest lane Ty flies, so the iPhone and
the headset each showed a different Extra, and neither showed the newest one.

Nothing in the GLB, the rig json or the config is platform-specific. A cockpit diverges only when one lane edits these
files and the other does not get the commit.

## The rule

The cockpit is **these files, and only these**, and they are edited on a `lane/claude-*` / `lane/codex-*` branch off
island-nav, merged into island-nav, and island-nav is then merged into the Quest lane. Never edited on the Quest lane alone.

| what | where | read by |
|---|---|---|
| The model, every livery | `unity/Assets/_Project/Resources/Models/<id>/<id>-own*.glb` | `AirframeModels.Place` (both) |
| Its rig (moving parts, needles, switches, displays) | `docs/models/<model>-rig.json` → `StreamingAssets/rigs/` (ConfigSync, generated, gitignored) | `AirframeRig` (both); the Quest's hands read the same file's `controls` |
| Flight config (seats, eyes, controls, hinge set-back) | `configs/aircraft/<id>.json` → `StreamingAssets/aircraft/` (ConfigSync) | the sim (both) |
| The offline passes that build the GLB | `tools/own_models/` (below) | people, not the game |
| Shared runtime | `Scripts/Bridge/`: AirframeVisual, AirframeRig, CockpitRigState, CockpitDisplays, InstrumentLighting, CockpitSights; `src/FlyingGame.Core/ModelRig.cs`, `InstrumentFaces.cs` | both |

Quest-only code (VrCockpitHands, VrPilotBody, VrControlBinding, XrHeadset, QuestRig, Scripts/Quest/, the XR packages and
ProjectSettings) stays on the Quest lane. It reads the same GLB and rig: the hands bind `Grip_*`, `Poke_*`, `Ctl_*`,
`Mount_*` nodes and the rig's `controls`; iOS ignores those nodes (they are meshes or empties like any other) and drives
the same `Ctl_*` from the sim where the rig gives them a param it knows.

## What each platform does with the same Extra GLB

| | iOS / Mac | Quest |
|---|---|---|
| Model, paint, panel, card, sights | `AirframeModels.Place` → `AirframeVisual.TryPlaceModel` | same |
| Instrument lighting | `InstrumentLighting.Apply` in TryPlaceModel | same call; the Quest's CockpitLiveParts must pass the dial materials it makes through `InstrumentLighting.Lit` (merge note below) |
| Needles, ball, sticks, pedals, throttle, switches | `AirframeRig` from the rig json, fed by `CockpitRigState.Fill` | same, plus the hands override grabbed controls |
| COM / XPDR / fuel computer LCDs | `CockpitDisplays` (`Radio_Display_R`, `Xpdr_Display_R`, `Totalizer_Display_R`) | same |
| Flat IMGUI dials | `AnalogGauges` (screen overlay, never scene-lit) | suppressed (`VrMode.SuppressFlatOverlays`) |
| Eye | `seats[].eye` of the config, CockpitView | same eye anchors the headset |

Rig params a model names but the sim does not supply are reported once at bind and stay at rest — that is how the
Extra's ammeter, EGT and fuel needles sat dead on BOTH platforms until 2026-11-04 (now `amps`, `egt_f`, `fuel_wing_gal`,
`fuel_akro_gal`, plus `fuel_press_psi`, `fuel_flow_gph`, `fuel_used_gal`; `SharedPanelRigTests` fails if an instrument
node's param is unknown). The switch params (`nav_lights`, `strobe`, `smoke`, …) are still unknown on iOS: those
toggles sit where the model draws them; on the Quest the hands move them.

## Instrument lighting (both platforms)

`FlyingGame/Instrument` (unity/Assets/_Project/Shaders/Instrument.shader), applied by `InstrumentLighting` to every
material `InstrumentFaces.IsInstrument` names (dial faces, needles, tell-tales, lubber lines, compass cards, placard
text; never bezels, the panel, knobs or LCDs): `colour = albedo × (max(sun + sky, 0.32) + 0.30)`. The floor keeps a face
in shadow or at dusk at open-shade brightness; the glow is proportional to the paint, so white numerals glow and black
faces stay black (contrast > 8:1 in the dark, nothing neon). The shader reaches every player through
`Resources/Materials/Instrument.mat` — no always-included list to keep in step between the iOS and Quest builds. Why not
the glTF emission: glTFast compiles it behind `shader_feature _EMISSION`, which no imported material enables, so it is
stripped from a player and only works in the editor.

## The Extra GLB: pass order

The committed GLBs are the product of a full build plus offline passes, each editing the GLBs in place (all three
liveries). A full rebuild alone DROPS the passes (CLAUDE.md: "A full procedural rebuild can revive content removed by an
offline pass"). To regenerate reproducibly, from the approved GLBs, NumPy/Pillow Python (Blender's bundled
`python3.13` has both):

| # | pass | what it adds | lane / commit |
|---|---|---|---|
| 0 | `build_ea300.py` (Blender; `-- --wing-only` swaps only the three wing meshes) | airframe, airfoil-B wing (318ff1ac), panel, paint | island-nav |
| 1 | sights (`sights.py`), card holder (`drop_card_slot.py`, a8f6d6a9), floor windows (8c88bb74) | IAC sights, one sequence card | island-nav |
| 2 | `vr_cockpit.py` | phase-1 visuals: squeeze latch, pilot body anchors | bad3a452 |
| 3 | `vr_phase2.py`, `vr_phase2d.py`, `vr_phase2e.py` (`rebuild_vr_phase2.sh`) | harnesses, jacks, canopy latch v2, detachable canopy, Quest triangle budget | 0c0d882f, 45d26542, e14b800a, 911fb2f1 |
| 4 | `vr_phase3.py` | every switch / knob / push-pull a moving `Ctl_*` with `Mount_*`, `Grip_*`, `Poke_*` | 55954abb, 74cb309f |
| 5 | `vr_phase3b.py` | the one-plane panel (`Panel_P3b_*`, `Face/Bezel_P3b_*`, needles), legacy nodes renamed `Legacy_P3b_*` and emptied | 3a9e746b |
| 6 | `cockpit_controls_v2.py` | every `Ctl_`/`Mount_` onto a real panel face | 2576bec6, 74cb309f |
| 7 | `vr_phase3d.py` | rear panel top (hood + glareshield) back | 5c96a4ff |
| 8 | `vr_phase3e.py` | rear throttle on the left wall with a gate, canopy handles | d0517239 |
| 9 | island-nav wing transplant: `ea300_wing_export.replace_wings` | airfoil-B `Wing`/`Aileron_L/R` into the VR GLBs | 2be16316 |
| 10 | `extra_panel_p3f.py` | panels out to the walls, 0.275 m tall, one switch row, radio subpanel, sticks −0.06 m | 60f3ff48 |
| 11 | (lane/codex-extra-cockpit-shared) the stick, switch row, panel layout, aileron hinge and junction per `_xfer/extra-cockpit-shared/CODEX-SPEC.md` | — | next |

After any pass: `drop_card_slot.validate` on the node graph, `check_vr_phase3.py`, `check_aileron_junction.py`, then
`dotnet test --filter "CockpitControlInteriorTests|ExtraRearPanelTopTests|SharedPanelRigTests|InstrumentFacesTests|ModelRigTests"`,
then a seat-eye render (`CockpitEyeShots`, COCKPIT_SHOTS) — and look at it.

## Merging this into the Quest lane

- Files brought from the Quest lane by whole-file checkout are byte-identical there: no conflict.
- `CockpitControlInteriorTests.cs`: this lane has the Extra rows only (the Pitts/GL VR GLBs are not on island-nav). Take
  the Quest file, or keep its Pitts/GL rows plus whatever Codex changes in the Extra rows.
- `ModelRig.cs`: the new params sit after `XpdrKnobClicks` and at the end of `TryGet`, away from the Quest's edits.
- `CockpitDisplays.Update`: the Quest's early-out compares (COM, standby, volume, mode, squawk); add
  `(FuelFlowGph, FuelUsedGal, Master)` to that tuple or the fuel computer freezes.
- `CockpitLiveParts` (Quest lane only): route each `FaceMat` and needle material it creates through
  `InstrumentLighting.Lit` so the Pitts' live dials get the same lighting.
