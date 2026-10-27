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
- Published specs (Schweizer flight manual): span 15.54 m (51 ft), length 7.85 m (25 ft 9 in), aspect ratio 11.85, dihedral 1.5°.

## Method
1. Station positions and heights were measured by eye from the side view of the 3-view at 0.01548 m/px, then typed in as numbers.
2. 18 superellipse fuselage stations were lofted. The canopy uses 7 sections. The wing reuses our own planform loft from the earlier model.
3. Separate meshes were built for Aileron_L/R, Spoiler_L/R, Elevator_L/R and Rudder.
4. Paint: all yellow, matching the Commons 3-view. The yellow RGB (255, 204, 0) was sampled from the drawing. The canopy is clear with yellow frame bows, and the wheel and skids are grey. The N2033A registration is kept because the 3-view shows no markings. No part is black.
5. Revisions:
   - v2: the stab and elevator were measured on the 3-view (span 2.30 m, chord 0.85 m, elevator 45% of chord, leading edge 5.0 m behind the wing leading edge), and the rudder hinge is now raked.
   - v3: dihedral is 1.5°, per the flight manual. The outer panels were measured on the 3-view: the taper starts 3.43 m from the centreline, the tip chord is 0.77 m, and the tip leading edge is 0.19 m aft of the root leading edge.
   - v4: the nose stations were re-measured from the side and top views of the 3-view, giving a drooped, tapered nose. The canopy now springs from the fuselage sides and follows the 3-view crown line. The box pylon was replaced with a lofted fairing that runs into the wing root.

## Known differences from the real aircraft
- The stab span (2.35 m) was scaled from the drawing, not taken from a published figure.
- The airfoil is a simplified NACA-like section, not the real 2-33 section.
- Each side has a single streamlined lift strut plus simple cabane poles. The real strut and jury-strut arrangement is more detailed.
- The wing-root pylon is a smooth lofted fairing. On the real airplane it is a framed structure with a rear window.
- There is no cockpit interior.
- Fuselage sections are smooth superellipses. The real fuselage is fabric over a steel tube frame, with stringer facets.
- Spoilers are flat plates.
- The tail surfaces are flat thin slabs, not true airfoils.

## Talk Flying livery (v5)
`SGS_LIVERY=talk-flying` builds `glider-2-33-like-own-tf.glb`: the same yellow base (255,204,0) with a black cheat line + silver pinstripe, TALKFLYING in place of the N-number, the Talk Flying logo on a black/silver plate, and talkflying.com, all baked into the fuselage side texture. Lettering art made with Be Vietnam Pro Bold (OFL); the logo is the project's own Decals/talk-flying-logo.png. No third-party geometry or artwork.

## v6 wing marks
Top: TALK FLYING (port) / PODCAST (starboard), reading from above nose-up, and the wings-and-headset emblem over the fuselage. Underside: TALK FLYING / PODCAST reading from below nose-up. Baked into the wing via planar top/bottom projections. The emblem is cropped from our own Decals/talk-flying-logo.png (wordmark removed) on a black outline; lettering is Be Vietnam Pro Bold (OFL).
