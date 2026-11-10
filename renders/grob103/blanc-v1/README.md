# Blanc Twin III Acro v1 - NEEDS YOU: Ty visual approval

Replacement for the previous analytic Grob GLBs, adapted from Florian Blanc's
CC BY-SA print plate. Attribution and licence: `CREDITS.md` beside the assets.
All images are rendered from the exported GLB reimported into Blender 5.2.2,
CPU Cycles, 32 samples. No Unity/device validation is claimed.

- `side.png`, `three-quarter.png`, `front.png`, `top.png`: assembled exterior.
- `gear-main.png`, `gear-nose.png`, `gear-tail.png`: rounded fixed tires and
  skin fillets; main .440 x .150 m, nose .260 x .085 m, tail .210 x .065 m.
- `canopy.png`: separate transparent glass and white frame, tandem cockpit.
- `side-poh-overlay.png`: cyan source-model silhouette and red contour over
  Twin III POH PDF p14 / printed 1.6. Uniform 854 px : 18 m scale, level axes,
  approximate nose registration, wings omitted. No independent axis stretching
  or contour fitting. Wing-root omission leaves local gaps in this mask;
  the complete aircraft exterior participates in cockpit containment checks.
  Reference: https://www.norcalsoaring.org/uploads/1/0/0/6/100675970/grob_g-103-c-twiniii-acro-poh.pdf
  POH reference pixels are review evidence, not part of the CC BY-SA mesh.
- Manifests record source/model/reference hashes, assembly scales and cameras.
- `validation.json`: exported mesh, graph, UV, rig, control motion, dimensions,
  2,016 independent cockpit-part pairs per asset and 1,608 shell-envelope samples.

Measured span 18.000045 m, length 8.179798 m, fin top 1.710 m above the level
belly datum; deviations from 18.00/8.18 are under 0.3 mm after decimation/export.
69,765 triangles including cockpit, instruments and wheels; about 3.1 MB/GLB.
The fixed tire bottoms expose .10/.12/.07 m at the modeled local nose/main/tail
keels (-.48/-.48/-.19 m). No wheel pants, doors or retraction.
Both compatibility filenames currently use the same white/red registration-style
finish. D-103C is synthetic review lettering, not a real registration claim.

All 29 driven node names, original parameter/travel maps and cockpit hierarchy
remain. Surface pivots/markers follow the replacement geometry. Proposed visual
eyes are native glTF front (-1.15,.16,0), rear (.04,.22,0), with head clearance
75.4/90.9 mm under the same envelope test as before. These are pending review;
application eyepoints, physics and CG were not changed. Canopy remains static.

The legacy analytic checker also passes a fresh temporary legacy asset. The
.NET suite could not run because dotnet is absent; nothing was installed.
Ty approval remains PENDING. The overlay shows remaining scan/model contour
mismatch and does not establish type-perfect geometry or production tolerances.
