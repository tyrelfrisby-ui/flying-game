# Pitts S-2B (own model) - v10 fuselage stripes: design notes

References were studied **by eye only** (pixel grid on enlarged crops); nothing traced, sampled or copied into a texture.

## What was wrong in v9
The stripe pack was narrower and sat high on the cowl, so the diamond was well above mid-cowl. On N260BF (Munden)
and Horizons NZ the pack is ~0.17-0.175 m (by eye, scaled to our cowl) and the diamond sits 45-48% down.

## v10 design
* Pack 0.21 m at the diamond (x 1.07), per Ty's target; diamond centre z -0.093 = ~44% down the cowl.
* Each of the four stripe edges is a straight line from x 1.07 to the tail post x -3.275 (pack tapers to ~0.12 m), so the
  stripes stay parallel-ish and run cleanly into the rudder post as on the references.
* Nose bands re-fitted with the same (t, u) tables (t = normalised height off the stripe, u = distance aft of cowl face):
  upper band widened at the top of the side (t 0-0.4), aft edge flattened near the cowl top (u 0.36-0.38) so the side and
  top projections meet without a notch; lower band / chin V narrowed slightly to suit the lower pack.
* Registration moved down to z -0.46..-0.21 (x -1.30 aft) to clear the lower stripe.
* Pants: crescents follow the pant nose curve; outline 9 mm.

## Iterations
1. Pack + diamond position: matched Munden/HNZ; bands too blobby; pant crescents straight bars.
2. Bands slimmed; pant crescents curved; top-view band too wide.
3. Top band forward edge matched; small notch where side band met top band.
4. Upper band aft edge flattened near the top: notch gone, top band closer to Munden. Taken as final.

## Remaining differences (honest)
* The two references disagree slightly (Munden narrower bands, HNZ wider); v10 sits between them, and the pack is
  deliberately a bit taller (0.21 m) than either reads by eye (~0.17 m).
* Cowl-top band is a compromise: still a little wider/further aft at the outer edges than the Munden top view.
* The green-photo perspective camera is approximate; in it the diamond still looks slightly high. Side ortho views are the authority.
* Our pant nose is blunter than the real fairing, so the pant crescents/diamond are only an approximation.
* Chin scoop / cowl louvres not modelled; spinner shape differs from the references.
