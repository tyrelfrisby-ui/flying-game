# J-3 cockpit v17: before / after (2026-11-02, Ty 19:00–19:07)

Ty's words:
- "the ignition switche is abeam the top of the front seat so much further aft."
- "there is also a fuel tank behind the instrument panel with a clear hose that runs to the floor for a drain. the tank
  sort of looks like a drum with vertical striations for stiffening."
- "match the black paint in the front". Then, correcting it: black only in the front area (floor, footwell,
  floorboards, pedals, walls forward of the panel, heel plates), and the sides turn yellow just aft of the panel on a
  curved line.

Every pair uses the **wheels** variant. **Before** is the GLB at b23d2996 (v16). **After** is the GLB on
`lane/claude-j3-cockpit`. The floats variant has the same cockpit.

**Camera.** Shots without "cutaway" in the name sit at the exact approved **rear eye** that the game uses: glTF
(0.7774, 0.5067, 0). This is the same eye and the same 96° wide field of view as v16 (`../README.md`). Close-ups are a
narrow field of view from that same eye. The three cutaways are NOT from the eye.

**How they were made.** `blender -b --python tools/own_models/j3_cockpit_shots.py -- <glb> <dir> <before|after> v17-...`.
The side-by-sides are ffmpeg `hstack`s of Ty's photo and our render.

Nothing here is approved. The headset is the judge.

| file | what it shows |
|---|---|
| `ref-ty-nose-tank-firewall.png`, `ref-ty-panel-tank-behind.png` | Ty's two reference photos, copied unchanged. |
| `compare-tank-photo-vs-cowl-off.png` | Photo (1) beside `v17-07` (after). The tank seen from ahead with the cowling, engine, prop and firewall taken away. |
| `compare-panel-photo-vs-rear-eye.png` | Photo (2) beside `v17-04` (after). The panel from the rear eye at 52°. The photo is taken from further forward, so the floor and the heel plates are hidden here by the front seat. |
| `v17-01-mag-switch-{before,after}` | Forward-left and up. **Before:** the switch on the left root rib just aft of the front spar (station 1.743). **After:** on the same rib at station **2.130**, abeam the top of the front seat back. It now sits on a small black wedge that turns it 20° toward the rear seat. |
| `v17-closeup-mag-switch-{before,after}` | 14° from the rear eye. **After:** OFF, L, R and BOTH and AAF TYPE A are readable. On the flat v16 mount the knob hid BOTH from this eye (the rib is seen 57° off its face). At OFF the lever covers part of the word OFF. |
| `v17-02-nose-tank-{before,after}` | Straight ahead and down. **After:** below the panel, the beaded black face of the tank, the V tubes crossing in front of its lower part, and the clear drain hose next to the stick. |
| `v17-closeup-tank-beads-{before,after}` | 22° from the rear eye. **After:** the vertical stiffening beads on the tank's aft face and its sloping lower aft face, the brass sump and the hose. |
| `v17-closeup-drain-hose-{before,after}` | 18° from the rear eye. **After:** the clear hose from the sump down past the V tubes' firewall fitting to the floor, beside the front right pedal. |
| `v17-03-black-front-{before,after}` | The footwell from the rear eye. **Before:** the walls are yellow all the way forward to the firewall. **After:** semi-gloss black forward of the panel and in the footwell. |
| `v17-04-panel-like-photo-{before,after}` | The panel at 52°. **Before:** tach / ASI / compass / altimeter / oil, unlabelled knob high on the left. **After:** oil / tach / compass / altimeter / ASI as in photo (2), and the knob beside the oil gauge with PULL for HEAT and FRESH AIR PUSH. |
| `v17-05-tank-footwell-cutaway-{before,after}` | **Not the eye.** This is v16's 04b camera, low in the front footwell. It shows the V tubes running under the tank to the firewall fitting, and the hose to the floor. |
| `v17-06-tank-side-cutaway-{before,after}` | **Not the eye.** Taken from the right, with the right skin, liner, door and glass taken away. It shows the tank between the firewall and the panel, its lower aft edge cut parallel to the V tube 3.3 cm above it, the beads on its side, the hose to its floor grommet, and the curved black / yellow parting on the far (left) wall. |
| `v17-07-tank-cowl-off-front-{before,after}` | **Not the eye.** From ahead and above-left with the cowling, engine, prop and firewall taken away, as in photo (1). Shows the rounded top, the vertical beads, the brass rim band and the filler neck up to the red cap. |

## The tank

- **Capacity:** 12 US gal = 45.4 L. Sources: the AOPA J-3 fact sheet ("located in the fuselage just behind the
  fire-wall, and has a capacity of 12 gallons"); Univair U70723-000 (a 12-gallon J-3 tank); owners describe it as
  "between the instrument panel and engine firewall".
- **No dimensioned drawing was found, so the size is a judgement.** The room it fills runs from the firewall (station 0.800) to
  0.11 m ahead of the panel, and from the dash deck and cowl down to Ty's v16 V tubes.
  - Front face at station 0.825, aft face at 1.120.
  - Width 0.46 m.
  - Crown at z 0.170. It is trimmed to stay 1.5 cm inside the cowl skin and under the dash deck.
  - Bottom at z −0.265 at the firewall.
  - The lower aft edge is cut parallel to the V tubes, 3.3 cm above them, so the tubes run under the tank.
- **Volume:** 45.5 L (12.03 gal) for the drawn outline, and **46.3 L (12.22 gal) measured from the GLB's own triangles**
  (the beads included). The test `TheV17CockpitIsWhereTySaid` measures it again.
- **Finish (from the photos):** semi-gloss black, 22 vertical beads 3.5 mm proud on the front and aft faces and on the
  sides, a brass rim band round the front edge, the filler neck up to the existing red cap, and the float-gauge wire
  hanging into the tank.
- **Drain:** a brass sump fitting under the low front corner of the tank, right of centre, clear of the V tubes. From
  it a clear, slightly yellow hose (12 mm OD, alpha 0.5) runs to a black grommet in the floorboards at station 0.950.

## What differs from Ty's photos and was left alone

- **Photo (2) shows no tube in front of the tank.** In ours, Ty's v16 V tubes (from the panel's bottom corners to the
  firewall centre) cross the tank's lower face from the rear seat. Either the real tubes run behind the tank's face, or
  they are hidden in that photo. **For Ty.**
- **Gauge faces.** In the photo the oil gauge has a black face, the ASI a modern-looking white face with a dark lower
  sector, and the compass is a box-type window. Ours are all cream, with a ball compass in a round well. This needs the atlas (`j3_textures.py`), so it was not done here.
- **Placards.** The photo has a two-line placard at the top centre and a long strip under the gauges (it reads like a
  fuel-sump / drain notice), plus a small yellow tag under the heat knob. Ours has a blank top plate and
  "REAR SEAT FOR SOLO FLYING" under the compass. Not changed, because the wording cannot be read reliably off the photo.
- **Filler cap colour.** The cap in photo (1) is blue. Ours stays red, because Ty asked for red in v4.
- **Heel plates.** The photo is taken from further forward, so how far aft they sit is a guess (stations 1.245 to
  1.455, ahead of the front stick). They are hidden from the rear eye by the front seat.
