# Current asset: Florian Blanc Twin III Acro conversion (2026-11-03)

The current two GLBs supersede the historical analytic model documented below.
They are adaptations of Florian Blanc's CC BY-SA G103c Twin III Acro print plate,
not original clean-room exterior geometry. See
[asset CREDITS](../../unity/Assets/_Project/Resources/Models/glider-grob103-acro-like/CREDITS.md)
for author, source, licence, changes, source hash and reproduction commands.
`build_grob103_blanc.py` is the current builder; `build_grob103.py` remains the
legacy analytic generator and supplies only the original cockpit, rig maps and
fixed gear through a temporary build. Do not run it alone to replace the current
asset. Geometry checks dispatch by exported source metadata and keep the legacy
analytic checks available. Current review evidence is `renders/grob103/blanc-v1/`.
Ty visual approval remains PENDING. The following sections are historical.

---

# Grob G 103A Twin II Acro — original review model

2026-11-01, lane/codex-grob103. Owner: Talk Flying / Ty Frisby. **Visual acceptance pending.**
The two GLBs, cockpit, dials, UVs and livery are generated from original code. No third-party mesh,
traced topology, reference photograph pixels, manual raster or downloaded texture enters either GLB.
The existing project `glb_visuals.py` writes the GLB. The project Swift CPU renderer was localized as
`grob103_render_cpu.py`; shared renderer/paint behavior and all other aircraft are unchanged.
Pillow draws generic dials and original house lettering with local Helvetica rasterization.
The CPU builder uses the shared GLB helper rather than Blender-only paint APIs; it retains their
sRGB-to-linear color treatment and embedded-image material pattern.

## References actually inspected

- Local `assets-incoming/ref-grob103/grob103-flight-manual.pdf`, 35-page scan, September 1981 manual
  with later revisions: PDF p7 / printed p5 three-view; PDF pp21–24 / printed pp17–20 cockpit;
  PDF p20 / printed p16 emergency jettison. PDF p8 has variant/structural-reinforcement notes.
- Local FAA `faa-tcds-g39eu.pdf`, section III (G103A Twin II Acro), seat stations and control movements.
  The 9.45 in elevator-radius baseline requested by Ty is used, not later serial-specific exceptions.
- All eleven downloaded Commons photos were viewed in a contact sheet. The four OE-5279 G103A photos
  are the variant-specific appearance reference, especially the disassembled fuselage photo ending
  `14294285742_.jpg`: separate front and rear cockpit openings, fixed central bow, white composite
  pod, T-tail and fixed tandem wheels. Other photos are qualitative checks, not orthographic rulers.
  `PHOTOS-list.txt` records titles and retrieval context. Their licensing does not attach to our mesh.
- [UIUC E603 numerical section data](https://m-selig.ae.illinois.edu/ads/coord/e603.dat), accessed
  2026-11-01. Reduced upper/lower numerical ordinate samples are explicitly listed in the builder;
  linear interpolation is an approximation of the real airfoil, not an imported model mesh.
- `bga-datasheet.txt` is a 403 error page, **not usable reference data**.

## Drawing measurement ledger

The manual p7 is rendered at **1200 px page height**, yielding 848 × 1200 pixels. Pixel picks are by
visual line-center estimate, generally ±2–4 px due scan thickness/skew. The top/front span runs
approximately y=148..1070: **922 px / 17.50 m = 52.685714 px/m**, or **0.018980477 m/px**.
A single uniform scale is used in all overlay axes, without outline warping or view-specific stretching.
Source reference pixels remain under assets-incoming, never in shipped art.

| Pick on the 1200 px page | Pixels / conversion | Interpretation and model choice |
|---|---|---|
| Top-view span | y148..1070, 922 px | 17.500 m, exact model tips ±8.750 |
| Top centerline | y610 | Zero span station; scan's two halves differ slightly |
| Top root LE | x693 | Longitudinal datum x=0 |
| Top nose | x823 | 130 px = 2.468 m forward of LE; model +2.500 m |
| Top tail TE | x399 | 294 px = 5.580 m aft; model −5.680 m to retain published length |
| Side nose/tail | y582..151 | 431 px = 8.181 m; agrees with 8.18 m published length |
| Top root TE | x627 | 66 px = 1.253 m chord; chosen 1.3475 m to match 17.8 m² with taper |
| Top taper break | y~345 / ~875 | ~5.03 m half-span; chosen 5.00 m |
| Top chord near break | x~694..640 | ~1.025 m; chosen 1.080 m |
| Top tip LE/TE | x~691..669 | 22 px = .418 m; chosen .430 m |
| Aileron inboard | y~345 / ~875 | ~5.03 m; chosen 5.25 m, full outboard span to tip |
| Airbrake span | y~408..475 / ~745..812 | ~1.27 m each; chosen 1.25 m blade centered ±2.90 m |
| Tailplane span | y~520..698 | 178 px = 3.379 m; chosen 3.20 m |
| Tailplane root/tip chord | x~400..445 / ~408..432 | ~.854 / .456 m; chosen .83 / .54 m |
| Nose wheel station (side) | y~537 | 45 px aft of nose = .854 m; chosen x=+1.65 m |
| Main wheel station (side) | y~425 | 157 px aft of nose = 2.980 m; chosen x=−.44 m |
| Top fuselage width | ~38–40 px | ~.72–.76 m; chosen max .760 m |
| Nose and fin silhouette | qualitative three-view + OE-5279 | Analytic egg sections and tapered slabs; not production formers |

The root/tip/chord choices are not falsely described as exact scan measurements. The analytic planform
integrates to **17.800 m²** (centerline reference area before small control gaps): inner 5 m panel
1.3475→1.08 m, outer 3.75 m panel 1.08→.43 m. Leading edge x=0, −.05, −.12 m at those stations;
the trailing edge moves forward toward the tips. AR = 17.5² / 17.8 = **17.2051**, versus published
rounded 17.1. No planform is distorted to force both inconsistent rounded values simultaneously.
Dihedral **2.5°** is an estimated drawing fit; no certified incidence or washout measurement is claimed.
The modeled E603 section has zero added incidence/washout. Inboard root vertices begin on the
host fuselage half-width curve rather than crossing the centerline/rear cockpit. These are simple
contact curves, not a production root fillet or welded surface union. Stabilizer sections are analytic symmetric
thin sections; elevator and rudder are simple thin lofts.

## Authored station ledger (metres, not claimed surveyed dimensions)

Every numerical station below is an original estimate constrained by the drawing and photos; the uniform
scale above is the reference ruler, not a claim these are directly measured production sections.
Rows are author x, half-width, bottom, upper shoulder. Nose is +X, port +Y, up +Z.

```
 2.50 .008 -.24 -.22     2.40 .120 -.35 -.14     2.20 .240 -.43 -.03
 1.95 .320 -.48  .02     1.70 .360 -.50  .06     1.35 .375 -.51  .09
  .80 .380 -.51  .12      .20 .370 -.49  .15     -.40 .340 -.49  .18
 -.75 .300 -.46  .19    -1.20 .235 -.34  .18    -2.00 .160 -.15  .15
-3.00 .105 -.09  .13    -4.00 .075 -.06  .13    -4.90 .055 -.04  .16
-5.45 .012  .01  .20
```

Lower egg-section loft meets the glass at its side sills. Glass section rows are x, half-width, sill,
crown (all estimated, rather than a traced canopy mesh):

```
-.75 .300 .190 .190
-.55 .260 .175 .560     -.25 .325 .160 .690      .20 .350 .140 .720
 .75 .360 .120 .700     1.30 .350 .095 .610     1.70 .315 .065 .450
1.95 .250 .025 .240     2.10 .270 .000 .000
```
The canopy surface closes vertically at its wide rear sill, not to a needle point. Glass splits at x=.54 with a fixed center bow. Additional bows are
x=−.60 and 1.97; bow tube radius 6 mm, sill rails 8 mm. No opening hinge is animated or certified.

## Cockpit and fit assumptions

TCDS seat stations: front **+1.150 m**, rear **−.040 m** relative to the wing-root-LE datum (rear under
root). Eye x stations use those same nominal stations; eye heights .400 / .450 m are estimated seated
fit, not manufacturer measurements. Seat pans are 350 × 400 × 65 mm, seat backs 65 × 400 × 440 mm;
headrests 90 × 200 × 130 mm. Panel x stations +1.73 / +.53 m are layout estimates. The front
ASI-left / vario-right / altimeter-below / compass-above arrangement retains the manual's primary
instrument intent while omitting optional electrical vario, radio and other optional gauges. Rear has
ASI, altimeter and vario as requested. Bezels have 90 mm overall diameter (front compass 68 mm),
with measured nearest neighbor clearance >92 mm. Simplified blue/green/yellow handles, pedals,
sticks, harnesses and silver buckles are original geometry. No position displays of any kind.

Left wall is +Y in author coordinates; actual eye renders, including left/right views, verify it.
The **yellow canopy jettison handle on the right** follows Ty's explicit requested color; manual
p16 instead specifies **red handles on both sides**. This is a deliberate visible discrepancy for review.
Canopy hinge direction remains unconfirmed from the available views. The static two-piece canopy
replaces the initial one-piece assumption; the OE-5279 open fuselage clearly shows two openings.

Head fit uses 960 samples on a 200 × 210 × 184 mm ellipsoid, center 65 mm aft / 50 mm above each eye.
Vertical clearance is measured against EXPORTED glass triangles, not the generator formula:
front **101.06 mm**, rear **106.97 mm**. These are estimates, not certified anthropometry. The 66 mm
requirement is applied to the entire sampled head envelope, not just the eye-to-roof distance.
Datum-relative body-axis eye proposals are front **(1.15,0,−.40)** and rear **(−.04,0,−.45)**.
Claude must subtract his chosen CG datum position before interpreting these as physics-body eyes.
They are explicitly unapproved in eyepoints.json; the existing approved fleet entries are untouched.

## Gear, paint and other details

Fixed nose wheel: center (+1.65,0,−.51), radius .16 m; main center (−.44,0,−.45), radius .22 m.
Both touch z=−.67 m, wheelbase 2.09 m. Simple upper white fairings, silver hubs, tail skid terminating
near (−5.45,0,−.10). Tow hooks are simplified witnesses at x+2.44 / −.35, fin pitot at x−4.85..−4.58,
z+.68, static ports between seats x+.53. These detail locations are estimates, not plumbing/rigging data.

Factory white has a restrained red stripe and **N103TF**, a fictional project marking (no claim that a
registry has never assigned that combination). TF variant substitutes navy striping and original
**TALK FLYING** lettering. Geometry is identical. No real aircraft registration or photographed sponsor
art was copied. No font files are embedded.

## Checks and review evidence

`check_grob103.py` reopens both GLBs. Exact exported bounds: **17.500 × 8.180 × 1.550 m**
(span × length × height), matching the requested published dimensions within export float precision.
Both assets have 313 nodes, including 29 driven rig nodes.
Graph checks: indices, unique parents/roots, cycles and orphans. Every rig origin/marker pair is measured;
main surface hinge positions and TCDS maps are independently pinned. Alpha is read back as BLEND.
2,145 cockpit mesh pairs per livery are checked for independent-part intersection, with deliberate
stick/grip, handle mounts, tow cables and soft seat/harness interfaces allowed. Instrument disks and colored controls additionally
have explicit separation checks. An additional 1,680 exported vertex samples are required to remain inside the actual fuselage/glass
envelope; deliberate mechanical handle mounts entering the wall are excluded. Shell/liner interfaces,
coincident touching and soft-body deformation are outside the independent-part collision audit; visual inspection remains required. Validation does not mark approval.

Blender `/opt/homebrew/bin/blender` 5.2.2 crashed during factory-startup before running Python (exit139).
`render_grob103_blender.py` provides the explicit CPU/Cycles exterior path for a working Blender installation.
All exports and PNGs use the project-derived CPU path; **no Blender/Cycles/EEVEE render success is claimed**.
Renders are 1000–1400 px wide, in `renders/grob103-own/`: white/TF side, top, front, front/rear
three-quarter, underside, both real seat eyes, seat-eye left/right details, and front/rear panel closeups.
Full positive and negative control poses are separate renders, not a claim of runtime animation testing.

Uniform-scale manual overlays live in **assets-incoming/ref-grob103/review-overlays/** to keep reference
pixels out of tracked/shipped assets. Tracked `overlay-manifest.json` records the PDF hash, pixel picks,
crops, model hash and scale; model-only masks are tracked beside it. Side overlays omit wings to expose
pod/fin/canopy conformance. They show remaining differences: model canopy/pod depth and tailplane
height do not exactly follow the scan, the stabilizer chord differs, and front-view vertical proportions
are inconsistent with forcing the supplied 1.55 m overall height. The overlays have NOT been warped
or called a pass. This is a complete first authored review asset, not approved type-perfect geometry.

## Reproduce

Use Python with NumPy and Pillow (the local Codex runtime has both); set `PYTHONDONTWRITEBYTECODE=1`:

```sh
export PYTHONDONTWRITEBYTECODE=1
python tools/own_models/build_grob103.py
python tools/own_models/check_grob103.py
python tools/own_models/render_grob103.py
python tools/own_models/overlay_grob103.py
```

Only the model, cockpit, source tools, docs, importer metadata and review renders were changed.
No Sim, aircraft config, Unity script, deployment, Unity execution, commit or Glass Overlay work occurred.
Binding remains a documented handoff, not implemented here, per Ty's instruction.

## 2026-11-03 — Twin III Acro correction (supersedes Twin II dimensions above)

Target: G 103 C Twin III Acro, OSB 315-66 stiffened fuselage target per lane log.
Level author belly datum is Z=-0.51 m. Span 18.00 m; length 8.18 m.
Canopy glass runs author X=-0.65..1.95 (2.60 m), crown Z=.485 (0.995 m above
belly), gently rising toward the rear seat. Deck depths at 3.4/4.0 m aft of
nose are .780/.600 m (station top includes the .015 m crown). Fin top Z=1.20,
1.71 m above belly. Published 1.55 m is the wheels/rest attitude dimension,
not this level-axis envelope. Tailplane/elevator and axis witnesses raised .32 m;
outer wings/ailerons and their witnesses extend to Y=±9.00 m. All 29 driven
nodes, params, maps, hierarchy and moving meshes remain; pivots follow geometry.

Fixed wheels, author X=2.5 minus aft distance: nose .80 m aft, .260×.085 m;
main 2.90 m aft, .440×.150 m; tail 7.10 m aft, .210×.065 m. Exposures below
local keel .100/.120/.070 m respectively. Rounded elliptical tire sections;
open-lip fillet lofts enter the host skin and cover the shoulders. No skid,
retraction, wheel doors or detached wheel pants. Hub hardware remains buried.

Cockpit fit required front panel/gauges down .130 m and aft .100 m, with its
pedals following the panel and the front tow cable shortened/routed downward.
Proposed visual eyes now author (1.15,0,.18), (-.04,0,.26); native GLB
(-1.15,.18,0), (.04,.26,0). These supersede earlier visual eye proposals, require
Ty's review, and do not update application/physics CG-relative eyepoints.json.
Head-envelope clearances 83.7/72.8 mm. Instrument/colored control separation,
2145 cockpit-part pairs and shell containment pass in each livery.

Review: `renders/grob/v-fix/` side, three-quarter, front, three gear details,
side-poh-overlay.png and hash/camera manifest. Reproduce with existing NumPy/Pillow
Python: `tools/own_models/build_grob103.py`, `check_grob103.py`, then
`render_grob103_fix.py /path/to/TwinIII-POH.pdf`. CPU GLB views, not Unity validation.
Overlay uses the actual Twin III POH PDF page 14 (printed 1.6), downloaded from
https://www.norcalsoaring.org/uploads/1/0/0/6/100675970/grob_g-103-c-twiniii-acro-poh.pdf .
Uniform 854 pixels per 18 m based on the plan span; side nose registration at
(762,708) in the 1600-pixel page. No per-axis fitting or contour matching;
level model axes, reference scan registration approximate. Wings omitted only
from the overlay to expose the fuselage contour. Raw PDF stays outside git.
Visual status PENDING; no approval or runtime import claim.
