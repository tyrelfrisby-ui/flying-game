# Grob own model — visual review, not approved

Start with [white three-quarter](white-three-quarter-front.png), [top](white-top.png),
[white side](white-side.png), [Talk Flying side](tf-side.png), [front seat](white-front-seat-eye.png)
and [real rear seat](white-rear-seat-eye.png).

Check blue airbrake / green trim on the pilot's LEFT in [front left](white-front-eye-left.png)
and [rear left](white-rear-eye-left.png); yellow jettison on the RIGHT in
[front right](white-front-eye-right.png) and [rear right](white-rear-eye-right.png).
The manual specifies red jettison handles on both sides; yellow right follows the requested brief.

[Front panel](white-front-panel.png), [rear panel](white-rear-panel.png),
[positive throws](deflected-positive-rear.png), [negative throws](deflected-negative-rear.png).
Both poses have spoilers fully extended 170 mm. Both model liveries have the same cockpit and geometry.

Every PNG is CPU-rendered from an exported GLB. Blender crashed at startup; these are not Unity captures.
`render-manifest.json` records model/image hashes; `validation.json` records the independent check results.
`overlay-manifest.json` gives uniform scale/camera registration and paths to the local reference composites.
Reference composites stay in `assets-incoming/ref-grob103/review-overlays/`, never shipped or tracked.
Model-only masks here contain no reference pixels.

Canopy opening hinge, exact panel layout, root blending, seat posture, simplified fairings/plumbing,
fictional N103TF marking and TF paint need Ty's review. Canopy/pod and tail-height differences remain
visible against the manual; see ../../docs/models/grob103-own-PROVENANCE.md.
No game binding, config, deployment or visual approval is claimed.

## Blender Cycles renders (added by the orchestrator)

`blender/` holds 12 Blender 5 Cycles CPU exterior renders (white-* and tf-*: side, top, front, three-quarter front/rear, underside), made with `tools/own_models/render_grob103_blender.py` run outside the Codex sandbox (Blender crashes only inside the sandbox). The other PNGs come from the CPU fallback renderer.
