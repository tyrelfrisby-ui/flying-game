# Pitts S-2B (own model) - v9 nose and wheel-pant stripes: design notes

Written **before any v9 paint code**, from enlarged crops of the references (in `refcrops/`). The references were
studied **by eye only**: positions were read off with a pixel grid, turned into a handful of numbers, and rebuilt as
our own procedural curves. Nothing is traced, sampled, or copied into a texture.

## What v8 got wrong (Ty: "Not even close")
v8 ran both stripes straight to a vertical white bar just behind the nose ring and put small red "tongues" above and
below them. The diamond was at x 1.27, right up near the nose. None of the references look like that.

## What the references actually show (all of them agree)
* **Fork:** the two stripes run forward along the fuselage and **end in a big white diamond about mid-cowl**. The
  diamond is as tall as the whole stripe pack (top of upper stripe to bottom of lower stripe) and a bit wider than tall.
* **Upper band:** from the diamond's **upper-forward edge**, a white band sweeps **up and forward** to the cowl top,
  getting wider as it goes, and crosses the cowl top as a straight band (Munden top view, HNZ oblique, Horizon photo).
  * Its **forward edge** leaves the diamond's upper-left edge and curves up to the top line, concave toward the nose.
  * Its **aft edge** is the front of the upper red panel: it leaves the stripe's top edge tangentially, at about the
    diamond's aft tip, and rounds up into the top line. On the top view this edge bows aft at the shoulders.
* **Lower band:** the mirror image, sweeping **down and forward** to the chin. It is wider than the upper band, and
  the lower red panel's front is a rounder bulge. Under the cowl the two lower bands join in a **straight band across
  the chin**. **A white V runs aft from the band along the centre line** between two rounded red tongues (Munden and
  HNZ bottom views, green photo).
* **Red wedge:** between the two bands, forward of the diamond, a **red wedge** points aft into the diamond's forward
  tip. Ahead of both bands' forward edges the whole **nose bowl is red**, including the nose ring. Munden and the
  Horizon photo have a red spinner; HNZ draws it grey. v9 leaves each livery's spinner as it is.
* **Wheel pants:** the same design in miniature. Two thin stripes sit near mid-height and converge on the pointed
  tail, ending in a diamond near the nose. A small band sweeps up-forward over the pant top and down-forward under it.
  There is a red nose cap ahead, and a red wedge between the bands into the diamond tip.

## Measurements (grid read-offs)
Conventions: **u** = distance aft of the cowl front face (model x 1.50), in metres. The image scale comes from cowl
height and spinner length: Munden side x5 crop ~460 px/m, HNZ side x5 crop ~610 px/m, Munden top/bottom x6 crop
~435-440 px/m.

| feature | HNZ side | Munden side | Munden top / bottom |
|---|---|---|---|
| diamond centre u | 0.40 | 0.48 | - |
| diamond width x height (m) | 0.20 x 0.20 | 0.21 x 0.16 | - |
| upper band at cowl top, u from-to | 0.15-0.30 | 0.22-0.49 (blob) | top band 0.18-0.31 (0.37 at the shoulders) |
| lower band at cowl bottom, u from-to | 0.15-0.39 | 0.22-0.44 | chin band 0.19-0.34, then the V |
| band forward-edge u at stripe level | ~0.34 | ~0.41 | - |
| V under the cowl | apex about mid-cowl | - | ~45% of the cowl width at the band, apex u ~0.75 |

Pants (fractions of the pant length L from the nose; Munden x10 / HNZ x6):
* diamond centre at 0.16-0.17 L. Its half-width is about 0.04 L and its height is about 28% of the local pant height.
* stripes at 40-48% and 58-65% of the local height from the top, converging to the tail tip.
* upper band at the pant top spans 0.03-0.11 L, with the forward edge at about 0.06 L. The lower band at the bottom
  spans 0.05-0.12 L.

## The model's proportions differ, so the design is normalised
Our fuselage stripes, kept from v7, sit higher on the cowl than in the drawings. Upper stripe top to cowl top is only
about 0.13 m, against about 0.25 m scaled in HNZ. So each band is described in a **normalised height t**, in the side
projection:
* upper: t = (z - top of upper stripe) / (cowl top - top of upper stripe), so t = 0 is the stripe edge and t = 1 the
  cowl top line.
* lower: t = (bottom of lower stripe - z) / (bottom of lower stripe - cowl bottom).

Each band edge is a smooth curve u(t), interpolated with Catmull-Rom through the points below and extended beyond
t = 1 so it carries over the top and under the chin.

## v9 spec (starting values; iterations recorded below)
* Diamond: centre x 1.10 (u 0.40), z = middle of the stripe pack; half-width 0.080, half-height 0.065. Own navy
  outline drawn over the white so it reads separately from the bands.
* Stripes end at the diamond. No stripe forward of it: that is the red wedge.
* Upper band, points (t, u): forward edge (-0.35, .345) (0, .325) (.25, .27) (.55, .22) (1, .17) (1.5, .16). Aft edge
  (0, .50) (.1, .43) (.25, .39) (.55, .355) (1, .345) (1.5, .345). This gives a top band of u ~.17-.35 (x 1.33-1.15).
* Lower band: forward edge (-.1, .357) (0, .34) (.2, .275) (.5, .21) (1, .165) (1.5, .16). Aft edge (0, .53)
  (.12, .45) (.3, .405) (.6, .39) (1, .40) (1.5, .41). Chin band u ~.165-.40.
* Top and belly projections use the **same 3-D rule**: the surface height at (x, |y|) comes from the cowl superellipse,
  then the side rule is applied. The band therefore carries straight over the top and under the chin, and meets the
  side paint at the seams.
* Belly V: from the chin band's aft edge (x ~1.10), half-width 0.20, to an apex at x 0.80.
* Nose bowl and ring: red forward of the forward edges, about the front 0.16 m of the cowl at the top and bottom, and
  down to the red wedge between the bands.
* Pants: diamond centre at 0.165 L (x 0.638), on the pant's mid-height (fraction 0.52). Its half-width is 0.040 and its
  half-height 28% of the local height / 2.
  * The stripes are at fractions .40-.48 and .58-.65 from the top, from the diamond to the tail tip.
  * Bands as on the cowl, in pant-normalised t (stripe edge to pant top / bottom). Upper: forward (-.3, .14)
    (0, .125) (.4, .09) (1, .06) (1.6, .05); aft (0, .21) (.15, .165) (.45, .13) (1, .11) (1.6, .11). Lower: forward
    (-.3, .14) (0, .125) (.4, .09) (1, .065) (1.6, .06); aft (0, .21) (.15, .17) (.45, .14) (1, .12) (1.6, .12).
    Here u is a fraction of L, measured from the pant nose (x 0.80).

## Corrected measurements (re-read of the N260BF / Munden crops on a grid, after iter2)
* Side: each stripe widens forward of the diamond into a large white "blob" with a rounded red panel front behind it.
  Upper blob forward edge (Munden t, u): (-.17,.34) (.1,.28) (.37,.235) (.63,.22) (.83,.235) (.95,.25); aft edge (0,.62)
  (.1,.55) (.37,.535) (.63,.535) (.87,.54) (1,.55). Lower blob forward edge: wedge tip u .40 -> .30 halfway down the
  lower stripe -> .235 at its bottom -> ~.22; aft edge (0,.55) (.11,.49) (.31,.47) (.6,.46) (.89,.46). Diamond u .50, ~.20 x .16 m.
* Top: band forward edge .21 (centre) to ~.26 (sides); aft edge .36 centre, .38 at r .75, .43 at the edges.
* Bottom: band forward edge straight at .215; red tongues top at .39 (|y| .2-.46), rounding to .46 at the outer edges;
  V half-width ~.13-.20 at its top (two read-offs), apex u .82.
* Conflicts: Munden side shoulder aft edge (.535) vs Munden top (.38-.43); HNZ side puts the top crossing at .15-.30;
  Horizon photo puts it at ~.10-.33; green photo has the chin white reaching nearer the nose lip than the drawing.
  Priority used: side = Munden (shape), cowl top = Horizon photo / Munden top, under the chin = green photo / Munden bottom.
* Stripe pack height: in Munden and HNZ the pack is ~.21 m tall with the diamond ~40-45 % down the cowl; ours (v7
  fuselage stripes, kept by instruction) is ~.12 m tall and ~25 % down. This is the largest remaining difference.

## Method
Band edges are tables of (t, u): t < 0 is inside the stripe (the forward wedge), t = 0 at the stripe's outer edge,
t = 1 at the cowl top (upper) or cowl bottom (lower), linear in z; u = distance aft of the cowl face (m). One 3-D rule
paints the side projection (z from the texel), the top view and the bottom view (z from the superellipse surface at
(x, |y|)), so the views join. The superellipse-angle variant (tau) was worked out but not needed: with the cowl's flat
shoulders the top/bottom faces all sit at t ~ .85-1, where the tables are flat.

## Iteration log (all kept under iterN/, each with comparisons + renders/ + the build file used)
* iter1 (rejected): thin diagonal bands, diamond x 1.10 - read as the v8 hooks, far too narrow vs Munden.
* iter2 (rejected): wider bands, diamond x 1.07, chin band + V. Upper band still narrow; V apex blunt (outline eats it).
* iter3 (rejected): chin white nearer the nose lip; pant stripes given a red gap; pant bands narrowed. Found the
  perspective cameras had not changed (fixed) and measured the stripe-pack height difference.
* iter4 (rejected): band tables re-fit to the Munden blob read-offs (upper aft edge .56->.37, lower aft edge held at
  ~.46 down the side before rounding to .39 under the chin, lower forward wedge from u .40). Camera variants g1-g4 / h1-h4
  rendered and the closest picked. Pant red gap still swallowed by the outlines.
* iter5 (accepted -> final): pant stripes (.30-.40)/(.58-.68) of local height (gap = one stripe width), pant diamond spans the
  pack, crescents narrowed (aft edge .11-.12 L at the top/bottom); chin band forward edge .19; V apex x .68, half-width .14.

## Final parameters
DM_X, DM_HA, DM_HB = 1.07, .090, .070
BU_F (-.35,.37) (0,.30) (.3,.245) (.6,.215) (.8,.20) (1,.19);  BU_A (0,.56) (.12,.50) (.35,.46) (.6,.42) (.8,.385) (1,.37)
BL_F (-.10,.40) (-.067,.36) (0,.27) (.1,.235) (.3,.215) (.6,.20) (1,.19);  BL_A (0,.57) (.1,.49) (.3,.47) (.6,.46) (.8,.44) (.92,.41) (1,.39)
X_VA, HV = .68, .14; X_V0 = 1.11
Pants: PS_U (.30,.40), PS_L (.58,.68); PD_U .165, PD_HA .042, PD_HF .19
PU_F = PL_F (-.3,.115) (0,.085) (.4,.05) (1,.03) (1.6,.025); PU_A (0,.21) (.15,.165) (.45,.13) (1,.11); PL_A (0,.21) (.15,.17) (.45,.14) (1,.12)
Perspective cameras (model frame): green loc (2.3,1.7,-.95) look (.5,0,-.45) lens 27 pitch 8;
horizon loc (2.0,-6.5,1.3) look (-.4,0,-.1) lens 45 roll -20.
