# DHC-2 Beaver (own) — dhc2-beaver-like-own.glb

- Source: `~/Developer/flying-game-reference/out/beaver-v2/codex-dhc2-beaver-reference.glb` (Codex, 2026-10-25),
  built procedurally in Blender by `build_beaver_reference.py`. The geometry is ours: traced from the dimensioned
  U.S. Air Force L-20A three-view (public domain). Secondary drawings and photos were used only as visual checks.
  No third-party mesh was copied. Ty 2026-11-05 approved the swap into the game ("Swap").
- Rig: `tools/own_models/rig_beaver_reference.py` (Codex, lane/codex-beaver-cockpit) hinges the ailerons, flaps,
  elevator, trim tab and rudder (TCDS A-806 travels), plus the swivelling tailwheel and a Prop empty. The table is
  `docs/models/dhc2-beaver-own-rig.json`, written by `tools/own_models/beaver_rig_json.py`.
- Rebuild: `blender -b --python tools/own_models/rig_beaver_reference.py -- --ref <beaver-v2 GLB> --out /tmp/x.glb`.
  The script refuses an in-repo output path from the old licence assumption, so copy the result in, then re-run
  beaver_rig_json.py.
- Known disagreements (not fixed here; the flight config is untouched):
  - The drawn main wheels are at x +0.70 and the tailwheel at x -6.15 from the model datum. The config's contacts are
    at +0.156 and -5.378 from the CG, so the model is placed by span and outer wing, not at the datum.
  - The flaps are drawn up because ModelRig has no flap input.
  - The model's own LICENSE.txt in the reference pack still says CC BY-SA 4.0 (Codex's label). Ty to confirm the
    licence line for the shipped model.
