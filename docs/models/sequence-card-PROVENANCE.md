# Shared aerobatic sequence card and holder

2026-10-31, lane/codex-iac-pitts. Applies to every own Extra 300, Pitts S-2B and Great Lakes GLB in this base, with a reusable holder for the future Decathlon. Aircraft paint is preserved except for the separately requested unmarked Checkered Pitts underside extension.

## Source and artwork

Default: **2026 International Aerobatic Club (Power) Sportsman Known**, official four-page PDF: https://www.iac.org/sites/default/files/Sequences/SPTP-Known-2026.pdf. Local source supplied by Ty: `~/Developer/agent-lanes/aero-playground/refs/SPTP-Known-2026.pdf`. SHA-256: `4f110bf7a3574fe7b9d317f136195007f21bb8ccbe2bb1d76f5536f0ec0da8f6`. The PDF itself is not committed.

`make_sequence_card.py` rasterizes the **fourth page (pilot card, same Aresti sequence as Form B)** at 1275 × 1650 px / 150 dpi, letter portrait. All 11 figures, wind arrow, red/dotted lines and roll markings remain. Added header and a catalogue/K column transcribe Form A (page 1): figure K = 127; presentation K = 10; total = 137. Source diagram strokes are preserved. No diagram is redrawn or invented. Sequence graphics/catalogue content are attributed to the IAC's published OpenAero sheet; they are external source art, not claimed as original project artwork.

Adapted from the existing Extra cockpit lane's `77d6e2c` generator, inspected with `git show`. This shared version uses Liberation Sans Regular/Bold (**SIL Open Font License 1.1**, Red Hat/Liberation Fonts; only baked pixels ship), retains the original diagram strokes and writes a fleet-wide texture. `IAC_FONT_DIR` identifies an installed Liberation Sans directory. The panel-compass image is our own procedural dial, drawn by the same generator with the same fonts. No third-party dial image or mesh is used.

- Authoring texture: `tools/own_models/art/sequence-card-sportsman-known-2026.png`.
- Runtime default/reset: `unity/Assets/_Project/Resources/SequenceCards/sportsman-known-2026.png` (identical bytes, clamp, sRGB, preserve NPOT aspect, max 2048).
- Compass art: `tools/own_models/art/panel-compass-static.png`, 512 × 512.

## Holder and transforms

Original matte backing plate, 6 mm rails, card stock, bottom lip, metal spring clip and mounting foot are generated once from shared dimensions by `tools/own_models/sequence_card.py`. No reference mesh is imported. Physical card: **0.120 × 0.155294 m** (8.5:11 portrait); outer frame: 0.132 × 0.167294 m. Card plane is 12 mm aft of the panel face, on its centreline.

Each aircraft has independent nodes `SequenceCard_Holder`, `SequenceCard_Clip`, `SequenceCard_Stock`, and **`SequenceCard_Face`**. The face has one material named **`SequenceCard_Face`**, glTF `baseColorTexture` on `TEXCOORD_0`; no aircraft paint atlas shares this material. Face normal points aft toward the pilot. UVs cover exactly 0..1: the pilot's left/right map to U=0/1; glTF V=0 is the top. The Blender builder UV convention is V up, converted by its glTF exporter. No mirroring or runtime mesh rebuilding is needed.

Exact centres, dimensions and panel/holder heights are machine-readable in `docs/models/sequence-card-spec.json`. Coordinates there are metres before model placement, with aircraft rotation baked into positions and identity node transforms:

| Aircraft | Face centre in raw glTF (X,Y,Z) | Panel top | Holder top | Above panel |
|---|---|---|---|---|
| Extra 300 | (1.214, 0.394353, 0) | 0.428 | 0.478 | 0.050 |
| Pitts S-2B | (0.8195, 0.236353, 0) | 0.150 | 0.320 | 0.170 |
| Great Lakes | (1.6515, 0.222353, 0) | 0.136 | 0.306 | 0.170 |

Raw glTF axes: nose -X, up +Y; card faces +X. Builder axes: nose +X, port +Y, up +Z; conversion is (-x,z,y). Unity/glTFast handedness conversion and the existing aircraft placement scale/rotation are subsequent transforms; find the named renderer rather than applying raw coordinates as world coordinates. Pitts/Great Lakes cards stand above their existing gauge rows; Extra uses the requested 50 mm protrusion over the rear glareshield and sits on its blank centre area.

Reusable asset: `unity/Assets/_Project/Resources/Models/sequence-card/sequence-card-holder.glb` (Resources key `Models/sequence-card/sequence-card-holder`). It uses a local reference panel face/top at zero and the same card/slot names, with a 0.170 m holder top; face centre raw glTF (0.012, 0.086353, 0). **Decathlon:** call `blender_add(P, aircraft)` with a new placement entry when its panel exists, or attach this standalone asset to its panel; the panel reference must be aligned and translated to the Decathlon panel. There is no Decathlon cockpit on this base, so no guessed Decathlon placement ships.

## Extra compass and other-lane integration

The base `1b67dd5` has no compass. Inspected `lane/codex-extra-cockpit` (`e431e36`, rigged compass + sequence holder). That lane is not merged here. This base now has a static, original panel compass in the blank right glareshield face, centre builder (-1.202, -0.145, 0.366), 66 mm outside diameter, heading N. Nodes: `PanelCompass_Housing`, `PanelCompass_Face`; material `PanelCompass_Face`. It does not cover a gauge. Runtime heading update is handed off.

When reconciling the newer cockpit lane, keep **one** holder and compass: its existing `SequenceCard_Face` slot matches this contract, but it uses `ea-seqcard-default.png` and slightly different frame dimensions/centre. Its rigged `Compass_Card_R` should supersede this base's static compass. Do not append another shared holder after that lane's cockpit is installed. No app logic or existing cockpit rig was copied into this base.

## Building and checking

Full Blender builds call the shared holder helper for every livery. Full Blender execution was **not possible here**: Blender 5.2.2 exits 139 in `gpu::supports_barycentric_whitelist` / Metal initialization before any Python script runs (including a trivial factory-startup smoke command).

The builders' `--offline-update` mode is an incremental GLB rebuild using existing committed airframe geometry. It appends the holder geometry/textures and updates the requested checker underside projections; it does not re-export the original meshes. Existing .meta GUIDs are retained; new standalone assets/folders have matching .meta files. Repeated incremental runs keep the node count stable and refresh default image bytes. It is not a substitute for a full Blender geometry rebuild if the airframe or holder dimensions are subsequently changed.

```sh
IAC_FONT_DIR=/path/to/liberation-sans python3 tools/own_models/make_sequence_card.py /path/to/SPTP-Known-2026.pdf
EA_LIVERY=unmarked python3 tools/own_models/build_ea300.py --offline-update
PITTS_LIVERY=CHECKER-UNMARKED python3 tools/own_models/build_pitts_s2b.py --offline-update
GL_LIVERY=v1 python3 tools/own_models/build_great_lakes.py --offline-update
python3 tools/own_models/sequence_card.py
python3 tools/own_models/check_iac_assets.py
python3 tools/own_models/render_iac_visuals.py --undersides --cockpits
```

Repeat aircraft commands for Extra `tf|n300uy|unmarked`, Pitts `N31DG|N1191|CHECKER-UNMARKED|FACTORY-UNMARKED`. Python offline tooling requires NumPy/Pillow; all caches/temp files stayed in temporary storage. `check_iac_assets.py` checks all nine GLBs, slot/image identity, face normals/UV orientation, frame heights, and the byte-for-byte preservation of original positions/indices/nodes and other-livery paint against `1b67dd5`.

Existing `OwnPittsModelTests`: **17 cases passed** by direct reflection invocation of the compiled tests in a temporary console harness; Tests/ source untouched. `dotnet test` itself cannot open its socket/pipe in this sandbox. Compilation succeeded with one MSBuild worker and shared compilation disabled. Review renders are CPU rasterizations of actual GLB meshes and embedded textures, not Unity screenshots; Unity import/render/build still requires Ty's check.
