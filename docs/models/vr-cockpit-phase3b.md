# Extra 300 P3b panel handoff — 2026-11-02

No gate approved. CPU authoring only; Blender/Unity/xcodebuild/deployment not run; no commit. External CONTRACT.md read; operator copies the naming seam below.

One slab per seat, upper/lower sharing builder X depth and normal −X. Rear plane X=−1.175 m; front X=−0.150 m. All dial faces are 0.5 mm toward the pilot from that plane. Panel coordinates u=pilot right, v=up (metres); builder Y=−u. Slab u ±0.220, v 0.075..0.360 m. Both seats carry the full identical 11-gauge layout (E equipment choice), not the previous reduced front panel.

| Gauge | Case inches (E) | u / v metres | Separate nodes (append _R or _F) | Scale / actual clockwise angle from 12 |
|---|---:|---|---|---|
| ASI | 3.125 | -0.150 / 0.300 | Needle_ASI | ASI: [0, 40, 60, 158, 220, 260] kt → [-150, -115, -90, 35, 115, 150]° |
| Altimeter | 3.125 | -0.050 / 0.300 | Needle_Alt100, Needle_Alt1000 | Alt100: [0, 1000] ft → [0, 360]°; Alt1000: [0, 10000] ft → [0, 360]° |
| TC | 3.125 | +0.050 / 0.300 | Needle_TC, Ball_TC | TC: [-6, 0, 6] deg/s → [-40, 0, 40]°; Ball_TC: [-1, 0, 1] normalized → [18, 0, -18]° |
| G | 3.125 | +0.150 / 0.300 | Needle_G, Needle_GMax, Needle_GMin | G: [-10, 0, 10] g → [-150, 0, 150]°; GMax: [-10, 0, 10] g → [-150, 0, 150]°; GMin: [-10, 0, 10] g → [-150, 0, 150]° |
| Tach | 3.125 | -0.150 / 0.200 | Needle_Tach | Tach: [0, 3000] rpm → [-150, 150]° |
| MP | 3.125 | -0.050 / 0.200 | Needle_MP | MP: [10, 35] inHg → [-150, 150]° |
| Oil | 3.125 | +0.050 / 0.200 | Needle_OilP, Needle_OilT | OilP: [0, 120] psi → [-80, 80]°; OilT: [100, 250] degF → [100, 260]° |
| Temps | 3.125 | +0.150 / 0.200 | Needle_EGT, Needle_CHT | EGT: [800, 1800] degF → [-80, 80]°; CHT: [100, 500] degF → [100, 260]° |
| Amps | 2.25 | -0.100 / 0.110 | Needle_Amps | Amps: [-60, 0, 60] A → [-120, 0, 120]° |
| Fuel_Wing | 2.25 | +0.000 / 0.110 | Needle_Fuel_Wing | Fuel_Wing: [0, 31.7] US gal → [-120, 120]° |
| Fuel_Akro | 2.25 | +0.100 / 0.110 | Needle_Fuel_Akro | Fuel_Akro: [0, 2.6, 10, 10.6] US gal → [-120, -120, 120, 120]° |

Nominal cases: 3⅛ in = 79.375 mm, 2¼ in = 57.15 mm. Bezels include a 3 mm radial flange; clearance uses these larger measured circles, not just nominal cases. Original vector/bitmap art, no painted/static hands. Each needle is a separate mesh, pivot exactly at its gauge centre. Altimeter draws two hands only (100-ft marked long hand / 1000-ft short hand, 1000/10000 ft per revolution); no 10000-ft hand. Both extrapolate beyond one revolution. TC is an aircraft symbol rolling on the panel normal, ±3°/s at ±20°, capped ±6°/s at ±40°. Ball_TC rotates about gauge centre on a 25 mm radius lower arc; positive slip moves the ball right.

Rig maps are relative to each modelled rest angle; `extra.rig_dial_ang0_deg` supplies the absolute zero-pose angle. Do not apply both absolute angles and relative maps. The artwork and maps share the same definitions; ASI and AKRO use piecewise interpolation. G/GMax/GMin each have full −10..+10 range; Claude retains/reset telltale extrema in sim state. All 34 moving nodes per asset have unique rig entries and extras.

## Markings and estimates

ASI green 60–158, yellow 158–220, red 220 KIAS. Tach green 700–2400, yellow 2400–2700, red 2700 RPM. MP green 10–25, yellow 25–29.5, red 29.5 inHg. Oil pressure uses the instrument-marking table (green 55–90, yellow 25–55/90–100, red 25/100 psi); this differs from the engine operating allowances. Oil temperature green 140–210, red 245°F; CHT green 150–435, red 500°F. G marks green −5..+8, yellow +8..+10, red +10. These markings follow the [manufacturer-authored EA300 information manual, sections 2.14.3 and 1.6](https://britishaerobaticacademy.com/wp-content/uploads/2020/04/POH-EA-300-CUS-2006-09-20.pdf).

EGT 800–1800°F and ammeter ±60 A are estimated presentation ranges, with no invented EGT redline. WING shows 0–31.7 US gal. AKRO is labeled exactly AKRO and uses E–F: empty indication at/below 2.6 gal, full at/above 10 gal (piecewise plateau out to the 10.6 gal physical capacity), following the manual’s ground-indication placard. Claude should pass actual tank gallons, not a shared total fuel fraction.

## Controls and retired geometry

Removed all original upper/lower panel geometry in Cockpit_Shell, all old bezel/face/static instrument primitives in Cockpit_Interior, all RearPanel_* meshes, old needle/symbol/ball/compass meshes, radio/transponder faces and legacy panel controls. Old nodes retain `Legacy_P3b_` names as inert, meshless records; unused meshes/accessors are removed from GLB, so no hidden instrument duplicates remain. P3a Ctl_* names/grips/pokes are unchanged. Both side-console sets are lowered 450 mm (to builder Z=0); rig pivots, interaction metadata and mount positions match. Radio_Display_R/Xpdr_Display_R are independent UV display quads on a sub-panel centred at Z=.035, below the instrument rectangle; display metadata updated. Sequence card and complete holder shifted 285 mm right / 150 mm down, clear of gauges.

Naming seam for operator to copy to external CONTRACT: `Needle_ASI`, `Needle_Alt100`, `Needle_Alt1000`, `Needle_TC`, `Ball_TC`, `Needle_G`, `Needle_GMax`, `Needle_GMin`, `Needle_Tach`, `Needle_MP`, `Needle_OilP`, `Needle_OilT`, `Needle_EGT`, `Needle_CHT`, `Needle_Amps`, `Needle_Fuel_Wing`, `Needle_Fuel_Akro`, all suffixed `_R` AND `_F`. New TC names replace TC_Symbol_R/TC_Ball_R. Removed legacy Needle_VSI_R/Needle_Fuel_R/Compass_Card_R have no new gauge equivalents.

## Claude wiring

Reuse: `airspeed_kias`, `altitude_ft`, `g_load`, `g_max_seen`, `g_min_seen`, `rpm`, `manifold_inhg`, `oil_press_psi`, `oil_temp_f`, `cht_f`. New/binding additions requested: `egt_f`, `amps`, `turn_rate_dps`, `slip_ball`, `fuel_wing_gal`, `fuel_akro_gal`. Positive amps = charging, positive turn = right; positive slip = ball right; both seats share values. This lane supplies live rig targets/maps, not runtime sim wiring.

## Checks and review

check_vr_phase3.py passes 10 assets / 440 P3a controls. Three Extra liveries: each 2 panels × 11 gauges × 17 moving nodes; minimum measured bezel-circle clearance 14.625 mm, maximum face distance from slab plane 0.500035 mm. Actual face normals all match the panel normal. Checker rejects injected 10 mm face-depth and shifted-bezel overlap faults. Original-base CPU rebuild from 0a1a7b4 passed on all three Extras; per-node geometry counts match current assets and repeat builds are byte-identical. Current GLBs + rig also byte-identical on repeat entrypoint.

Blender presets use Eye_Rear (−1.700,0,.550) and Eye_Front (−.800,0,.550), 75° horizontal review FOV and 45° tighter FOV; both perspective views keep the same actual eye position. FOV choices E, since eye rig carries no FOV. Also straight-on ortho per panel. Six outputs prefixed after3b_. Representative pose is applied from the authored rig maps. No Blender renders performed; final pilot-eye legibility and device review remain unapproved.

## Operator commands

```sh
cd ~/Developer/flying-game-worktrees/codex-vr-cockpit
export PYTHONDONTWRITEBYTECODE=1
# NumPy + Pillow environment used for this CPU run:
/tmp/topo-venv/bin/python tools/own_models/vr_cockpit.py
/tmp/topo-venv/bin/python tools/own_models/check_vr_phase3.py
# Operator only:
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/own_models/render_vr_phase3b.py -- renders/phase3b
```
