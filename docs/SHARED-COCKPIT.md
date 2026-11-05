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
`fuel_akro_gal`, plus `fuel_flow_gph`, `fuel_used_gal`; `SharedPanelRigTests` fails if an instrument
node's param is unknown). The three-in-one's FUEL FLOW needle and the fuel computer both read `fuel_flow_gph` = `RigInputs.GphFromKgPerSec(Aircraft.FuelFlowKgPerSec)`, the sim's own burn (BSFC × MaxPowerW × throttle): 22.5 gph wide open against the POH's 23.3, zero once the engine has stopped. That burn has no altitude, rpm or mixture term and reads 0 at a closed throttle, so it is right at full power low down and not elsewhere. The switch params (`nav_lights`, `strobe`, `smoke`, …) are still unknown on iOS: those
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
| 11 | `extra_cockpit_shared.py` (`/usr/bin/python3`: NumPy + Pillow) | Ty's panel: card centred, ASI/G left, ALT/TC right, Trig-style COM + XPDR heads left, 3-in-1 right (oil P / oil T / FUEL FLOW gph, green 0–35, red radial 35: Ty 2026-11-04 15:23, the real 300L has no fuel pressure gauge), lower row tach / MP / fuel computer / ammeter; panel 0.055–0.360 m; one switch row (z 0.075, none within 85 mm of the stick); radio subpanel gone; aileron hinge at 30 % chord with an arc nose and matching cove; aileron nose skins filled to the wing lip | 8a5737c9 |
| 13 | `extra_front_seat.py` (`/usr/bin/python3`) | Ty 2026-11-04 19:05-19:14. FRONT: a small grey sub-panel with three 2-1/4 in instruments L->R ASI, G, ALT; every other front gauge, head, LCD, switch and knob retired (out of the rig and the hands' `controls`); throttle, stick, pedals kept. REAR: grey panel; only six switches, labelled on placards beneath (MASTER, ALT, FUEL PUMP / NAV, STROBE, SMOKE; the ALT toggle new, param `alternator`); magneto, alternate air, trim, landing light gone; EGT/CHT 2-1/4 in added lower right; grey-padded diagonal tubes from the panel's upper corners to the side rails. Prop / mixture verniers on the left wall beside the rear throttle, outboard of it, knobs aft (drawn IN). Pistol-grip throttles (ribbed, boot, end button) in both seats. FUEL VALVE: the two old fuel-selector rotaries become the Great Lakes' red lever (ON down, OFF 90 deg inboard), right wall, on one rod (`Extra_FuelValve_SharedRod`), param `fuel_valve` = `Aircraft.FuelValveOpen`: a real shut-off (4 s of fuel in the lines, relight 2 s after it reopens) | lane/claude-extra-front-seat |
| 14 | `floor_windows.py` (after 13) | Ty 2026-11-04 19:32: the clear Lexan floor panels in BOTH cockpits (rear x -1.492..-0.950, front -0.520..+0.140), belly skin, old belly glass, tub and footwell floor cut to each outline; `Floor_Window_Glass` (Lexan, BLEND), `Floor_Window_Frame` (Floor_Frame), `Floor_Tubes`. Runtime: `FloorWindowMaterials` (FlyingGame/Lexan, Transparent+6) ported from lane/claude-floor-windows 8890328f. `ExtraFloorWindowTests` | lane/claude-extra-front-seat |
| 15 | `extra_cockpit_v3.py` (`/usr/bin/python3`, after 14) | Ty 2026-11-05 05:07 EXTRA 300 + ADDENDUM 05:08. THROTTLES: both levers moved, geometry and sweep unchanged, so the idle grip sits 0.32 m ahead of, 0.23 m below and 0.10 m outboard of the seat's left shoulder (SeatedPilot): rear lever + quadrant 0.10 m lower; front lever off the top of the left console onto its inboard face (y 0.272) with a quadrant plate (`Throttle_Quadrant_F_V3`), its old box on the console top removed. FRONT KNOB: the pistol grip off, a 38 mm black ball. FUEL ROD: `Extra_FuelValve_SharedRod` from the rear handle forward past the front one to `Extra_FuelValve_Body` on the firewall (x +0.60); same two handles, same `fuel_valve`. FRONT CARD: `FrontPanel_SequenceCard_Face/_Stock/_Holder/_Clip` (the rear holder's parts, 108 x 140 mm) on `FrontPanel_SequenceCard_Plate` right of the front sub-panel; `card_slots` row. CANOPY SEAL: the rim already touched (0.0 mm); the "gap" was exterior livery seen from inside. `Cockpit_SideLiner_L/R` (skin to the glass's lower edge), `Cockpit_DeckLiner`, `Cockpit_AftBulkhead`, `Cockpit_FirewallPlate`, the rails' visible inner faces in `Paint_cabin`, the Wing clipped at the fuselage (inside triangles dropped, straddlers' inside corners onto the skin). `check_extra_canopy_seal.py`, `ExtraCanopySealTests` | lane/claude-extra-cockpit-v3 |
| 16 | `extra_rear_diagonals.py` (`/usr/bin/python3`, after 15) | Ty 2026-11-05 08:37 "the diagonal tubes in the rear seat are in the same plane as the upper longerons ... they go in toward the center". `Tube_P3h_PanelDiagonal_L/R` (pass 13 ran them from the rail at z 0.20 UP to the panel corners at z 0.345) rebuilt in the plane of the upper longerons (the canopy sills, z = the sill centre at each station, ~0.26-0.28): from the sill's inboard face at x -1.55 forward and inboard to the panel face at |y| 0.272, outboard of the oil bezel (|y| 0.258). `ExtraRearDiagonalTests` | lane/claude-extra-cockpit-v3 |
| 12 | `fix_aileron_junctions.py` (other models, not the Extra) | wing/aileron skin break at neutral: 172 bay re-cut, GL / J-3 / 2-33 / Pitts nose skins + gap | db87f77c |

After any pass: `drop_card_slot.validate` on the node graph, `check_vr_phase3.py`, `check_aileron_junction.py`, then
`dotnet test --filter "CockpitControlInteriorTests|ExtraRearPanelTopTests|SharedPanelRigTests|InstrumentFacesTests|ModelRigTests"`,
then a seat-eye render (`CockpitEyeShots`, COCKPIT_SHOTS), `render_aileron_deflect.py` for the hinge — and look at them.

Passes 11-15 edit the GLBs in place (13 after 11, 14 after 13, 15 after 14; 15 needs the GLBs as 14 left them). A full `build_ea300.py`, `model_c172.py`, `build_pitts_s2b.py`, `build_j3_cub.py`, `build_sgs233.py` or Great Lakes rebuild drops them: re-run them after any such rebuild, then `check_aileron_junction.py` must print OK.

## Merging this into the Quest lane

- Files brought from the Quest lane by whole-file checkout are byte-identical there: no conflict.
- `CockpitControlInteriorTests.cs`: this lane has the Extra rows only (the Pitts/GL VR GLBs are not on island-nav). Take
  the Quest file, or keep its Pitts/GL rows plus whatever Codex changes in the Extra rows.
- `ModelRig.cs`: the new params sit after `XpdrKnobClicks` and at the end of `TryGet`, away from the Quest's edits.
- `CockpitDisplays.Update`: the Quest's early-out compares (COM, standby, volume, mode, squawk); add
  `(FuelFlowGph, FuelUsedGal, Master)` to that tuple or the fuel computer freezes.
- `CockpitLiveParts` (Quest lane only): route each `FaceMat` and needle material it creates through
  `InstrumentLighting.Lit` so the Pitts' live dials get the same lighting.
