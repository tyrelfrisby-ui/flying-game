# Agent log — codex/own-ea300

## NEEDS YOU
- **GPL question on the EA 300 v2 (Ty's decision, for Claude to assess).** Ty asked for the EA 300 to be redrawn to match the approved FlightGear-based Extra (codex/fg-models v9, 5fdacad, wing raised about 10 in) to within about 1 in. That model comes from the FlightGear EA300 by Patrizio Melis, which is GPL. Ty was told about the GPL-derivative risk before he decided. v2 copies no vertices, faces, UVs or textures: the FG model was measured into numeric tables (tools/own_models/ea300_ref_tables.json), and all geometry was drawn fresh from those tables. However, the shape was fitted on purpose to match the GPL model to within a few centimetres. **Claude: please assess whether this counts as a GPL derivative for our licensing.** One option is to ask Patrizio Melis for written permission or a relicence. Until that's decided, treat this asset as possibly GPL-encumbered. Do not ship or merge it.

## HANDOFF
- [codex → Claude] codex/unmarked-liveries · Ty (2026-10-28): unmarked liveries, make them the game DEFAULTS · details:
  - New GLBs (same geometry/props as the approved v10.1 Pitts and v12 Extra; only markings removed, removed areas left in the plain underlying paint):
    - pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb: id 'checker-unmarked' (N31DG checkered scheme, Hartzell 2-blade prop grey with white tips, no registration). **Pitts DEFAULT.**
    - pitts-s2b-like/pitts-s2b-like-own-factory-unmarked.glb: id 'factory-unmarked' (N1191 factory diamonds scheme, MT 3-blade prop white with red tips, no registration).
    - extra-300-like/extra-300-like-own-unmarked.glb: id 'unmarked' (no N-number, no Talk Flying / TALKFLYING.COM / TF badge / Sunrise Aviation; fin stars and stripes kept; red underwing bar like N300UY). **Extra DEFAULT.**
  - Task (lane/claude-livery-picker, src/FlyingGame.Core/LiveryChoice.cs): add the ids to Options (pitts-s2b-like: ("checker-unmarked","Checkered"), ("factory-unmarked","Factory"); extra-300-like: ("unmarked","Extra")), map OwnSuffix pitts checker-unmarked→"-own-checker-unmarked", factory-unmarked→"-own-factory-unmarked", extra unmarked→"-own-unmarked", and make them the per-type defaults (Pitts = checker-unmarked, Extra = unmarked). Keep the marked liveries (n31dg, n1191, talk-flying, n300uy) selectable. New GLBs have no .meta yet (Unity generates it).
  - Build: PITTS_LIVERY=CHECKER-UNMARKED|FACTORY-UNMARKED tools/own_models/build_pitts_s2b.py; EA_LIVERY=unmarked tools/own_models/build_ea300.py. Renders (agent box): /workspace/unmarked-v1/{pitts-checker-unmarked,pitts-factory-unmarked,extra-unmarked}-{side,top,front,bottom}.png.
- [codex → Claude] codex/own-pitts-s2b · APPROVED by Ty (2026-10-27, v10 c8b90bc: "send them to aero"; v10.1 removes the fin "Pitts S-2B" script on Ty's request — use v10.1) as DONE: own clean-room Pitts S-2B in two liveries · wire into Aero Playground as the in-game Pitts S-2B · details:
  - Models: unity/Assets/_Project/Resources/Models/pitts-s2b-like/pitts-s2b-like-own.glb (N31DG: red/white sunburst, checkered rudder, Hartzell 2-blade prop grey/white tips) and pitts-s2b-like-own-n1191.glb (N1191: factory sunburst with diamonds, MT 3-blade prop white/red tips). The n1191 GLB has no .meta yet (Unity generates it).
  - Task: make these the in-game Pitts S-2B (existing flight config configs/aircraft/pitts-s2b-like.json). N31DG (-own) is the default; N1191 (-own-n1191) is selectable the same way as the Extra's N300UY (AirframeModels suffix / AERO_LIVERY), or a picker if easy. Check scale/orientation, US prop rotation (clockwise from the cockpit), control-surface binding.
  - Credits/licensing (docs/models/pitts-s2b-own-PROVENANCE.md): fully clean-room procedural Blender build (tools/own_models/build_pitts_s2b.py); no third-party mesh used or measured; dimensions from Aviat/Jane's/TCDS A8SO; paint from reference photos only; fonts SIL OFL 1.1. No GPL question.
- [codex → Claude] codex/own-ea300 · APPROVED by Ty (2026-10-27, v12 d0c6130) as DONE: own EA300 in two liveries · swap in as the in-game Extra 300, review provenance · details:
  - Models: unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own.glb (Talk Flying: TF fin badge, TALKFLYING.COM sides) and extra-300-like-own-n300uy.glb (plain N300UY, red tapered underwing bar, no TF branding). Both have a 3-blade prop (1/1 propellers bound), cockpit, D-shaped cowl inlets and a simplified AEIO-540 engine. Built by tools/own_models/build_ea300.py (EA_LIVERY=tf|n300uy) + tools/own_models/paint.py, art in tools/own_models/art/.
  - Task: replace the current Extra (the FlightGear-based EA300 from codex/fg-models) with these as the in-game Extra 300. Make Talk Flying (-own) the default; make N300UY (-own-n300uy) selectable through the AirframeModels.cs suffix naming (as with the 2-33's OwnSuffix/PickOwn). Retire the FG EA300 from shipping use (no FG EA300 asset referenced by any shipping livery).
  - Credits/licensing (docs/models/ea300-own-PROVENANCE.md): from v2 on, the geometry was measured from the GPL FlightGear EA300 by Patrizio Melis (codex/fg-models 5fdacad), by Ty's explicit choice. No FG mesh, UVs or textures are copied, but the shape may count as a derivative work. Fonts: Liberation Sans Bold and Archivo Black, both SIL OFL 1.1. Before App Store release, Claude MUST complete a GPL derivative review: keep the GPL credit, get Patrizio Melis's permission, or fall back to the clean-room v1 (638c05a). Update docs/CREDITS.md to match.
  - Four-views (agent box): /workspace/ea300-own-v12/ea300-tf-4view.png, /workspace/ea300-own-v12/ea300-n300uy-4view.png (plus ea300-front-inlets.png, ea300-front-3q.png).
- [codex → Claude] codex/own-ea300 · decide the GPL status of extra-300-like-own.glb v2 (see NEEDS YOU and docs/models/ea300-own-PROVENANCE.md). Possible outcomes:
  - Accept it.
  - Ask Patrizio Melis for permission.
  - Revert to the v1 clean-room commit 638c05a.

## Log
### 2026-10-28 — Grok Bot executor · codex/unmarked-liveries · unmarked liveries (Ty): Pitts checker-unmarked + factory-unmarked (no registration), Extra unmarked (no N-number, no Talk Flying / Sunrise Aviation); built from 8fea251 with livery switches only; handed to Claude as the new defaults (see HANDOFF). Local commit only.
### 2026-10-28 — Grok Bot executor · codex/unmarked-liveries · HANDOFF prop text corrected (was swapped): N31DG/checker = Hartzell 2-blade, N1191/factory = MT 3-blade. GLBs verified already correct (checker 2-blade grey/white tips, factory 3-blade white/red tips, matching own.glb / own-n1191.glb); not rebuilt. Local commit only.
### 2026-10-27 — Grok Bot executor · codex/own-pitts-s2b · v10.1: fin "Pitts"/"S-2B" script removed from both liveries (Ty); GLBs rebuilt; renders /workspace/pitts-own-v10-1 (box). Local commit only.
### 2026-10-27 — Grok Bot executor · codex/own-pitts-s2b · Ty approved Pitts S-2B v10 (c8b90bc) in N31DG + N1191; handed to Claude (see HANDOFF). Local commit only.

### 2026-10-26 — Codex · codex/own-ea300 · EA 300 v2 redraw to the FG v9 shape
- Added tools/own_models/ea300_measure.py, which slices the v9 FG GLB into tables.
- Rewrote build_ea300.py to loft all-new geometry from those tables, with the v9 N300UY paint rules unchanged.
- Added ea300_compare.py, which measures the 3-D and silhouette deviation.
- Visible-surface p99 is ≤ 3.7 cm in every part. Silhouette outlines are within 25 mm on 99.3–99.9% of the outline. Details are in PROVENANCE.
- Unity rig binds 4/4 surfaces and 1/1 prop.
- Local commit only: no push and no merge.

## 2026-10-26 EA 300 own model v3 (codex/own-ea300)
Did the v3 shape pass and visual QA (see docs/models/ea300-own-PROVENANCE.md v3). Outputs are in the box at /workspace/ea300-own-v3/. Not pushed. The GPL NEEDS YOU note from v2 still stands.
- 2026-10-27 Chief/executor: EA300 own model v4 — added clean-room cockpit interior (seats, harness, sticks, front + rear panels, tub, tube frame); exterior unchanged; renders in /workspace/ea300-own-v4 (box).
- 2026-10-27 Chief/executor: EA300 own v5 — smaller/lower seats+sticks, front seat fwd 0.16 m, angular rear dash top 0.20 m deep with panel under; renders /workspace/ea300-own-v5 (box).

- 2026-10-27 Grok Bot executor: EA300 own v6 cockpit layout (seats/panel/sticks moved, black dash top, collision fixes). Local commit only.
- 2026-10-27 Grok Bot executor: EA300 own v7 cockpit layout (pans -6in, panel/front seat/stick fwd 6in, sticks +4in). Local commit only.
- 2026-10-27 Grok Bot executor: EA300 own v8 (baggage area, TALKFLYING.COM, TALK FLYING PODCAST). Local commit only.

- 2026-10-27 EA300 own v9: two liveries off one build (EA_LIVERY=tf -> extra-300-like-own.glb with fin TF badge; n300uy -> extra-300-like-own-n300uy.glb, N300UY sides, red tapered underwing bar, no TF branding). Side font now Liberation Sans Bold (OFL 1.1). Awaiting Ty sign-off; not handed to Aero Playground.
- 2026-10-27 EA300 own v10: parallel stab paint, side lettering centred wing TE/stab LE, 2nd thin red stripe, 22% underwing bar (plain), 3-blade prop (1/1 bound), TF fin badge centred. Awaiting Ty sign-off.
- 2026-10-27 EA300 own v11: straight spanwise stab paint lines; open cowl inlets; simplified AEIO-540 flat six (staggered banks, left 0.13 m aft), baffles, left governor. Awaiting Ty sign-off; not handed to Aero Playground.
- 2026-10-27 EA300 own v12: D-shaped cowl inlets (~2:1) with lips draped on the skin; front baffle lowered. Awaiting Ty sign-off; not handed to Aero Playground.
