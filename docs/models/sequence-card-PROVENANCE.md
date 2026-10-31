# Shared aerobatic sequence card and holder — round 3

2026-10-31, lane/codex-iac-pitts. Extra placement supersedes rounds 1–2 (38e8746 / 1a6a4d7); Pitts/Great Lakes retain round 2.
All three Extra, all four Pitts and the one Great Lakes own GLB on this lane are updated.
Ty's steering: **flush in the centre of every rear/PIC panel; on Extra use the UPPER panel, with no stick-up.**

## Source and artwork

Default: **2026 International Aerobatic Club (Power) Sportsman Known**, official four-page PDF: https://www.iac.org/sites/default/files/Sequences/SPTP-Known-2026.pdf. Local source supplied by Ty: `~/Developer/agent-lanes/aero-playground/refs/SPTP-Known-2026.pdf`. SHA-256: `4f110bf7a3574fe7b9d317f136195007f21bb8ccbe2bb1d76f5536f0ec0da8f6`. The PDF itself is not committed.

`make_sequence_card.py` rasterizes the **fourth page (pilot card, same Aresti sequence as Form B)** at 1275 × 1650 px / 150 dpi, letter portrait. All 11 figures, wind arrow, red/dotted lines and roll markings remain. Added header and a catalogue/K column transcribe Form A (page 1): figure K = 127; presentation K = 10; total = 137. Source diagram strokes are preserved. No diagram is redrawn or invented. Sequence graphics/catalogue content are attributed to the IAC's published OpenAero sheet; they are external source art, not claimed as original project artwork.

Adapted from the existing Extra cockpit lane's `77d6e2c` generator, inspected with `git show`. This shared version uses Liberation Sans Regular/Bold (**SIL Open Font License 1.1**, Red Hat/Liberation Fonts; only baked pixels ship), retains the original diagram strokes and writes a fleet-wide texture. `IAC_FONT_DIR` identifies an installed Liberation Sans directory. The panel-compass image is our own procedural dial, drawn by the same generator with the same fonts. No third-party dial image or mesh is used.

- Authoring texture: `tools/own_models/art/sequence-card-sportsman-known-2026.png`.
- Runtime default/reset: `unity/Assets/_Project/Resources/SequenceCards/sportsman-known-2026.png` (identical bytes, clamp, sRGB, preserve NPOT aspect, max 2048).
- Compass art: `tools/own_models/art/panel-compass-static.png`, 512 × 512.

## Flush holder and panel placement

Card: **4.25 × 5.5 in = 0.10795 × 0.13970 m** (rounded 0.108 × 0.140 m), exact 8.5:11 aspect (0.772727).
Each dimension is half letter size. Outer backing/frame: **0.11395 × 0.14570 m**; 3 mm rails/lip,
22 mm clip. Paper face is 1.2 mm aft of the panel surface to avoid z-fighting; the outermost frame/clip is
3 mm aft. This is a low-profile flush surface mount, with no raised foot or above-panel structure.
All card and hardware vertices are within the rear panel outline; above-panel protrusion is **0 for every type**.
The source/reset texture remains byte-identical 1275 × 1650; it already has letter aspect and all 11 figures,
so cropping or re-letterboxing would remove content without improving the aspect. Physical size does not
change texture resolution. Review the flat image at full resolution for the small catalogue/roll text.

| Aircraft | Rear panel face (builder X) | Face centre raw glTF (X,Y,Z) | Card top | Frame top | Panel/glareshield top | Protrusion |
|---|---:|---|---:|---:|---:|---:|
| Extra 300, UPPER | -1.2020 | (1.2032, 0.3500, 0) | 0.41985 | 0.42285 | 0.428 | 0 |
| Pitts S-2B (2026-10-31 lowered) | -0.8075 | (0.8087, -0.0350, 0) | 0.03485 | 0.03785 | 0.158 | 0 |
| Great Lakes | -1.6395 | (1.6407, 0.0050, 0) | 0.07485 | 0.07785 | 0.136 | 0 |

**Extra round 3:** Ty selected the **upper rear-panel/glareshield face**. Its original centre height was
**0.120 m**, from model up 0.308 to 0.428; width 0.620 m at its lower edge, 0.400 m at the top.
The card (0.13970 m high) exceeds that height by **19.70 mm**, and its frame (0.14570 m) by **25.70 mm**.
It cannot fit unchanged. `RearPanel_UpperExtension` extends the **bottom downward 36 mm** to 0.272,
matching the original face colour; top/glareshield stay at 0.428. The resulting centre height is 0.156 m.
Card/frame are centred at up=0.350 with 5.15 mm frame clearance above/below, zero stick-up.
This is a documented enlargement of the upper face, not a claim the original upper face could contain the card.

The round-2 `RearPanel_FlushPanel` **is removed**: its only purpose was lower-card visibility.
The original lower slab/recess at builder X=-1.162 is restored, 40 mm forward of the upper face.
Upper voltmeter/compass/fuel-quantity slots already clear the central 0.11395 m frame and retain their positions.
Lower flight instruments use a row of 57.15 mm bezels (ASI -0.24, alt -0.16, attitude -0.08, G 0 at v=0.201);
engine instruments use 40 mm bezels in two rows (tach/oil pressure at u=+0.13/+0.23,v=0.220;
oil temperature/fuel pressure at the same u,v=0.174). This fits below the enlarged upper lip and remains
visible from the rear eye despite the restored recess. The G-meter fills the former lower-card centre;
there is no hole. Lower radio, front cockpit, seats and original aircraft buffers/geometry remain unchanged.

Raw glTF: nose -X, up +Y, pilot's right -Z. Builder: nose +X, port +Y, up +Z; conversion (-x,z,y).
Nodes have baked positions/identity transforms. `SequenceCard_Face` node and material, baseColorTexture /
TEXCOORD_0 contract, upright 0..1 UVs and aft normal +glTF X are retained. Find the renderer by name after
importer handedness/aircraft placement, rather than treating raw coordinates as Unity world coordinates.

Reusable `Models/sequence-card/sequence-card-holder` is now the same flush holder, reference panel X=0,
face centre raw glTF (0.0012,0,0), no raised foot. Align it to an actual future Decathlon panel before use.
There is no Decathlon panel in this base.

## Rear instrument layout and artwork

Panel coordinates are metres: **u=pilot's right, v=model up**, origin on the panel centreline at model Z=0.
Flight instruments occupy the left group; engine instruments the right group. The G-meter sits at the top
of the left group next to the card, where it is prominent during aerobatics. The compass occupies a separate
right-side slot. Extra uses the upper card/three auxiliary gauges and the restored lower panel layout described above, retaining attitude and the lower radio face.
Pitts/Great Lakes have seven suitably spaced dials each. Everything fits; no gauge was squeezed under the card.
These are visual arrangements, not a claim to reproduce a particular serial number's installed equipment.

The old builder had anonymous plain dial faces/needles: it did not identify ASI, altimeter, engine gauges or
G-meter functions. Round 2 assigns explicit functions to replacement dials and adds a visibly labelled **G-meter**
on all three types. Names and exact before/after coordinates/diameters are in `sequence-card-spec.json` and
the round-2 `AGENT-LOG.md` tables. Front panels retain every original instrument and triangle; round 1 did not
displace any front-panel instrument, so no front restoration is needed.

Original procedural dial art is in `tools/own_models/art/rear-panel/`, generated by `rear_panel.py` with
Liberation Sans Bold (existing SIL OFL 1.1 attribution/license). No third-party dial image/mesh is copied.
All new dials are **static visual placeholders**, including the compass; runtime animation is Claude's work.
The Extra's obsolete `PanelCompass_Housing` / `PanelCompass_Face` nodes are removed; there is exactly one
`RearPanel_Compass_Face`, on its PIC panel. When reconciling the other cockpit lanes, retain one card and one
compass, adopt their working rig as appropriate, and apply this flush size/layout requirement.

## Rear eye points and render evidence

Source: Ty's `refs/eyepoints-v2-aerobats.json` (v2 subset). Invert each entry's native-model-to-body matrix,
including translation and span scale; do not use body coordinates directly as GLB coordinates.
Exact inputs, inverse results and source commits are snapshotted in the spec's `rear_eye` fields.

| Type | Raw glTF rear eye | Panel ahead by | Verification on this lane |
|---|---|---:|---|
| Extra | (1.593487, 0.546503, 0) | 0.391 m | eye X=1.593 in rear seat-pan range 1.42–1.76 |
| Pitts | (1.407662, 0.340660, 0) | 0.600 m | eye X=1.408 at rear seat, ahead of reclined rear back |
| Great Lakes | (1.823660, 0.274322, 0) | 0.184 m | eye X=1.824 in rear opening 1.64–2.08 and pan range 1.74–2.06 |

**No eye-position adjustments.** Extra source model was 073f113 and Great Lakes 4a45232; those have newer
cockpit interiors than this lane. Extra's seat stations remain the same; Great Lakes uses different seat/back
geometry, but the supplied eye remains inside this lane's rear opening, above the pan and aft of the rear panel.
Round-3 review qualifies that round-2 footprint check: the source and this lane have materially different rear seat backs. The native horizontal distance is 184 mm (181 mm after span placement). The eye is retained unchanged; its correctness relative to this older seat is for Ty to judge. See the round-3 Great Lakes review in its provenance and AGENT-LOG; a footprint is not an adult-fit validation.

Straight-on renders are orthographic, normal to the actual rear panel, 2400×1800 with a 0.90 m horizontal field.
They isolate the rear slab/hood and instruments from seats/tub. Before renders load 38e8746 GLBs via `git show`
into temporary storage and use the **same projection, centre and scale** as after.
Rear-eye renders are true perspective at 1600×1200, 86° horizontal FOV, yaw forward along body +X,
pitched down to centre the panel, full opaque scene retained, glass omitted. glTF hierarchy/TRS is evaluated
(including Great Lakes oleo nodes); perspective-correct depth and UV interpolation are used.
CPU rendering is review evidence, not a Unity screenshot. No pilot occupants are modelled.

All 20 PNGs and `render-manifest.json` are untracked under `renders/iac-pitts/v2/`.
The manifest records each livery GLB, camera inputs and render names. See the round-2 log for every path.

## Build and validation

The CPU builders rebuild from the immutable round-1 GLB at **38e8746**, preserving its entire original binary
buffer, original airframe geometry, rig and paint (including round-1 checker undersides). Rear anonymous dial
triangles alone are removed by new index lists; original position/normal/UV data stays intact. Named card meshes
are replaced and the new rear dials appended. Repeating an offline update does not accumulate meshes/buffers.
Full Blender builders skip the old rear gauge loops and use the same shared frame/dial geometry. Baked dial PNGs
are committed, so Blender does not need Pillow to load them. Full Blender execution remains unverified here.

```sh
# CPU Python with NumPy/Pillow; TMPDIR holds caches/temp files.
python3 tools/own_models/build_ea300.py --offline-update  # EA_LIVERY=tf|n300uy|unmarked
python3 tools/own_models/build_pitts_s2b.py --offline-update  # PITTS_LIVERY=N31DG|N1191|CHECKER-UNMARKED|FACTORY-UNMARKED
python3 tools/own_models/build_great_lakes.py --offline-update  # GL_LIVERY=v1
python3 tools/own_models/sequence_card.py
python3 tools/own_models/check_iac_assets.py
python3 tools/own_models/render_iac_visuals.py --v2
# Only when changing original dial artwork:
IAC_FONT_DIR=/path/to/liberation-sans python3 tools/own_models/rear_panel.py
```

Checks cover all nine GLBs (eight aircraft + shared holder): exact dimensions/aspect, face/UV/material/image
identity, flush offsets, zero protrusion, full holder containment inside the panel outline, bezel-circle clearance
against the card/frame rectangle, separating axes for actual rear-eye projected bezel/face hulls, and **99 full
opaque-scene visibility rays across each printable card**. They also check original buffer/airframe/paint and
front-panel triangle preservation against round 1. Existing OwnPittsModelTests: **17/17 pass** by direct invocation,
including all Extra livery/propeller/sibling checks. Existing `GearMatchesTheModels`: **7/7 pass** across Pitts/Extra liveries (24/24 total direct checks). No separate offline Great Lakes model test class exists in this
base; its asset, hierarchy and rear-panel checks run through `check_iac_assets.py`.

No Unity, Blender, deploy, installation or upload was attempted in round 2, per Ty's rules. Round 1 established
Blender's Metal-init crash and standard dotnet-test sandbox socket failure. The direct harness uses the existing
compiled test DLL and reads this worktree's current GLBs. Device import/render and a full Blender build remain
external checks. No Tests/ or app logic was edited.

## Round-3 review renders and checks

`python3 tools/own_models/render_iac_round3.py` writes untracked `renders/iac-pitts/v3/`:
all three Extra upper-panel straight/rear-eye views and matched **1a6a4d7** before; Great Lakes right-side,
3/4 and top cutaways, forward -4°/86° eye views, plus picked seat-shell isolation. Great Lakes is rendered
from both this lane and source **4a45232**, without changing either the eye point or Great Lakes GLB.
Manifest records model hashes, source selectors, transformed points, measurements and render paths.
The scale proxy is a wire sphere of body radius 0.105 m, centre 0.080 m aft / 0.050 m above the eye;
markers/dimensions are x-ray overlays. Cutaways clip the right fuselage skin at centreline and omit wings/gear;
forward eye views retain opaque geometry (glass omitted), with the specified pitch rather than aiming down at the card.

The asset checker verifies Extra against its **upper** outline and actual original face + downward extension,
with no `RearPanel_FlushPanel`, original recessed lower gauge stations, zero protrusion, bezel/projected-hull
clearance and full-scene card visibility. Pitts/Great Lakes/shared holder stay unchanged from round 2.

## 2026-10-31 — Pitts card down on the panel

Ty: “the sight position looks good”, but the Pitts card should be down on the panel. The four Pitts livery face centres move from builder (-0.8087,0,+0.015) to (-0.8087,0,-0.035), exactly **50 mm down**. Raw GLB centre becomes (0.8087,-0.035,0). Frame bottom -0.10785 has 12.15 mm clearance above the panel bottom. The card remains 4.25 × 5.5 inches, centred, flush, with original art/UV/material contract. No instrument relocation was needed; existing seven bezels clear the holder and each other. Original front-panel geometry is retained.

Review: `docs/pending/sights-renders/pitts-panel-rear-eye.png` (Ty’s specified sight eye), `pitts-panel-closeup.png`. Existing `check_iac_assets.py` passes all original flush/size/visibility/preservation checks. The shared sequence helper now also calls the parametric aerobatic sight helper for both offline and full Blender builds; see [sights-PROVENANCE.md](sights-PROVENANCE.md) for scope, exact transforms, the separate unresolved Great Lakes visibility gate, and external rebuild commands. No eyepoint status or approval gate changed.
