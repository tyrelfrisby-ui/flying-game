# Schweizer SGS 2-33 — own clean-room model (glider-2-33-like-own.glb)

**Owner:** Talk Flying / Ty Frisby. All geometry was written from scratch by `tools/own_models/build_sgs233.py`.
No GPL or third-party mesh was used as input.

## Clean-room statement
- No geometry, vertices, UVs, slices or traces were taken from the FlightGear `sgs233` or `EA300` models.
  Those models are kept only on `codex/fg-models`, where they were used for side-by-side comparison renders.
- Ty suggested taking cross-sections of the existing model. We used the stricter rule instead, so nothing at all is derived from GPL sources.
- `tools/own_models/paint.py` and `art/n2033a.png` are our own code and artwork, copied from `tools/fg_models/`.

## References (public, viewed only; nothing traced into the mesh)
- Wikimedia Commons, "Schweizer SGS 2-33A two-seat glider colour 3-view drawing" (by DM, 2019):
  https://commons.wikimedia.org/wiki/File:Schweizer_SGS_2-33A_two-seat_glider_colour_3-view_drawing.jpg
- Commons photos: Royal_Canadian_Air_Cadets_Schweizer_233_A.jpg, SGS233AwingsMuseum.jpg, Schweizer2-33C-GRVS.JPG,
  Sgs_2-33b_sn286_5_20231004_1274531989.jpg (all https://commons.wikimedia.org/wiki/File:<name>)
- Published specs: span 15.54 m (51 ft), length 7.85 m (25 ft 9 in), aspect ratio 11.85.

## Method
1. Station positions and heights were measured by eye from the side view of the 3-view at 0.01548 m/px, then typed in as numbers.
2. 18 superellipse fuselage stations were lofted. The canopy uses 7 sections. The wing reuses our own planform loft from the earlier model.
3. Separate meshes were built for Aileron_L/R, Spoiler_L/R, Elevator_L/R and Rudder.
4. Paint: factory scheme (white fuselage, yellow wings and tail, yellow band with a navy pinstripe, N2033A). No part is black.

## Known differences from the real aircraft
- The stab span (2.35 m) was scaled from the drawing, not taken from a published figure.
- The airfoil is a simplified NACA-like section, not the real 2-33 section.
- Each side has a single streamlined lift strut plus simple cabane poles. The real strut and jury-strut arrangement is more detailed.
- The wing pylon is a simple box fairing.
- There is no cockpit interior.
- Fuselage sections are smooth superellipses. The real fuselage is fabric over a steel tube frame, with stringer facets.
- Spoilers are flat plates.
- The tail surfaces are flat thin slabs, not true airfoils.
