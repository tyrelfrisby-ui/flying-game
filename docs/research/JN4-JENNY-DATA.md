# Curtiss JN-4D "Jenny" — book data and three-view derivation

Research date: 2026-10-20. Prepared for `configs/aircraft/jn4-jenny-like.json`
(`tools/aircraft_builder/build_jenny.py`), step 1 and 1b–1c of `docs/NEW-AIRPLANE-PROTOCOL.md`.

**[E] marks an estimate** — a number not found in a source and derived instead from the three-view, from the
protocol's own component rules, or from the aeroplane's class. Every one says where it came from. Nothing here is a
factor chosen to make a test pass.

## 1. The variant

The **JN-4D** of 1917, the Curtiss-built American trainer with the **control stick** (the JN-4Can "Canuck" has a
different wing, ailerons on both wings and a wheel). This matters twice:

- **Ailerons on the UPPER wing only.** The spec sheet counts "Ailerons: 17.6 sq ft each, 35.20 sq ft total" — two
  surfaces, not four. The JN-4Can and the JN-4H had four. [1][2]
- Wikipedia's specification block is labelled JN-4D but its own caption attributes the data to the **JN-4B**, which is
  why the empty weight there (1,390 lb) disagrees with the 1,430 lb on the JN-4D sheet. Where they differ this
  document follows the JN-4D sheet. [2]

## 2. Dimensions (source [1], a transcribed JN-4D erection-and-maintenance spec sheet)

| | Imperial | Metric |
|---|---|---|
| Span, upper plane | 43 ft 7⅜ in | **13.294 m** |
| Span, lower plane | 33 ft 11¼ in | **10.344 m** |
| Chord (both planes, constant) | 59½ in | **1.5113 m** |
| Gap between planes | 60 in | 1.524 m |
| Stagger (upper forward) | 16 in | 0.4064 m |
| Dihedral | 1° | 1° |
| Incidence of panels | 2° | 2° |
| Sweepback | 0° | 0° |
| Horizontal stabilizer incidence | 0° | 0° |
| Length overall | 27 ft 4 in | 8.331 m |
| Height overall | 9 ft 10⅝ in | 3.013 m |

### Areas

| Surface | sq ft | m² |
|---|---|---|
| Upper planes (less ailerons) | 167.94 | 15.602 |
| Lower planes | 149.42 | 13.882 |
| Ailerons (2 × 17.6) | 35.20 | 3.270 |
| **Total supporting surface** | **352.56** | **32.754** |
| Horizontal stabilizer | 28.70 | 2.666 |
| Elevators (2 × 11.0) | 22.00 | 2.044 |
| Vertical stabilizer (fin) | 3.80 | 0.353 |
| Rudder | 12.00 | 1.115 |

The three area lines sum exactly to the published total (167.94 + 149.42 + 35.20 = 352.56), so the aileron area is
**additional to** the "upper planes" line and the upper wing including its ailerons is 203.14 sq ft = **18.872 m²**.
The config is built to these three numbers, not to span × chord: the ailerons **overhang the upper tip and the
trailing edge**, so span × chord does not reproduce the published areas.

## 3. Weights (source [1])

| | lb | kg |
|---|---|---|
| Empty (net) | 1,430 | 648.6 |
| Gross (loaded) | **1,920** | **870.9** |
| Useful load | 490 | 222.3 |
| — fuel (21 US gal) | 130 | 59.0 |
| — oil (4 gal) | 30 | 13.6 |
| — pilot | 165 | 74.8 |
| — passenger | 165 | 74.8 |

Wing loading 5.45 lb/sq ft; 21.35 lb per rated horsepower.

**We fly it at gross, 871 kg.** Every published performance figure (75 mph maximum, 45 mph minimum, the climb) is
quoted at 1,920 lb, so flying at gross is what makes the performance test a check against the book rather than
against an interpolation. It is also the training case the aeroplane exists for.

## 4. Engine, propeller, fuel

- **Curtiss OX-5**, V-8, four-stroke, water-cooled; **90 hp (67 kW) at 1,400 rpm**; bore and stroke 4 in × 5 in,
  500 cu in. Weight per rated horsepower 4.33 lb → **dry weight 390 lb (177 kg)**. [1][3]
- **Propeller**: two-blade fixed-pitch wood. No diameter in any source consulted. An original OX-5 propeller offered
  as "the same type used on the JN-4" measures **95 in = 2.413 m**, which is the figure used. [4] **[E]** on the
  attribution; the length itself is measured from the artefact.
- Fuel 21 US gal, gravity feed; consumption 9 gal/hr → about 2 h 15 endurance. Oil 4 gal.
- **Propeller weight 30 lb [E]**: the dealer's "approximately 40–50 pounds" [4] is a shipping estimate for a
  crated propeller; a one-piece wood blade of 95 in is nearer 30 lb bare.
- **Propeller inertia 10.0 kg·m² [E]**: that blade treated as a rod, m·L²/12 = 13.6 × 5.82/12 = 6.6, plus roughly
  3.4 for an OX-5 crank and flywheel. It is the largest prop inertia in the fleet relative to power, which is the
  point: a big slow wood propeller at 1,400 rpm is a flywheel, and the gyroscopic term should show it.
- **Efficiency 0.70 [E]**: a 1917 fixed-pitch wood propeller at 60–75 mph. For comparison the fleet uses 0.75 for
  the Stearman's and 0.80 for the Extra's.

## 5. Airfoil — **Eiffel 36**, and what the config actually uses

The JN-4's section is **Eiffel 36** at root and tip, confirmed by two independent sources. [2][5] It is a thin,
strongly cambered 1917 section.

**The config uses the repo's measured `clarkY-like` table.** No measured Eiffel 36 lift and drag table was found —
NTRS has no Eiffel 36 record, and the section's published polars are in Eiffel's own 1919 volume, which was not
available. `clarkY-like` is the nearest **measured** cambered table already in the repo and is what the Stearman
uses for its NACA 2213. This is a **table selection**, the same kind of choice as the TR-379 column in
`docs/SPIN-ROTARY-DATA.md`, not a tuned number.

What it costs, stated plainly: `clarkY-like` peaks at **C_Lmax 1.48 at 17°**, where a thin high-camber 1917 section
on a wire-braced fabric wing would peak lower and earlier. At 871 kg over 32.754 m² that difference is worth about
3 mph of minimum speed. **Open item: an Eiffel 36 table from Eiffel's measurements.** Until then the published
45 mph minimum level speed is the thing to check the config against, and §9 records what it came out at.

## 6. Where the stations come from — the mesh is the three-view

The spec sheet gives spans, chord, areas, gap, stagger and weights. It says nothing about **where along the
fuselage** anything sits, and those stations are what set the CG, the tail arm, the inertias and whether the
aeroplane noses over. The first pass took them off a published three-view by eye and got them wrong by up to half a
metre — the Jenny's wings are much further forward than they look.

**helijah's model is a dimensioned three-view that happens to be a mesh**, and it is the same mesh the game draws.
It is faithful: span 13.310 m against a published 13.294, chord 1.499 against 1.5113, length 8.253 against 8.331,
height 2.982 against 3.013, and 26-inch wheels. Its meshes are named in French, which places every part without
ambiguity — `helice` is the propeller, `direction` the rudder, `profondeur` the elevator, `ailes2` the stabilizer,
`aileH`/`aileB` the upper and lower wings, `essieu` the axle, `roueG`/`roueD` the wheels, `pate` the tail skid.

Re-measure any of it with:

```
python3 -I -c "
import collections
NOSE=-4.118   # the spinner's front face, so s = x - NOSE is the station aft of the nose
cur=None; box=collections.defaultdict(lambda:[1e9,-1e9,1e9,-1e9,1e9,-1e9])
for line in open('assets-incoming/jenny/source/jenny.obj'):
    if line.startswith('o '): cur=line[2:].strip().split('_')[0]
    elif line.startswith('v ') and cur:
        p=[float(v) for v in line.split()[1:4]]; b=box[cur]
        for i in range(3): b[2*i]=min(b[2*i],p[i]); b[2*i+1]=max(b[2*i+1],p[i])
for k,b in box.items(): print('%-26s s %6.3f..%6.3f  y %6.3f..%6.3f  z %6.3f..%6.3f' % (k,b[0]-NOSE,b[1]-NOSE,b[2],b[3],b[4],b[5]))"
```

x is the station **aft of the spinner's front face**; y is **up**, with the propeller axis at y 0.025.

| Station | mesh object | s (m) | y (m) |
|---|---|---|---|
| Propeller disc | `helice` 0.004–0.108 | **0.056** | 0.025 |
| Radiator | (between `bol` and `moteurs`) | 0.35 **[E]** | 0.20 **[E]** |
| OX-5 | `moteurs` 0.549–1.208 | **0.879** | **0.285** (a V-8's banks sit above the crank) |
| **Upper wing leading edge** | `aileH` 1.279–2.778 | **1.279** | **1.033** |
| Main wheel axle | `essieu` 1.495–1.567 | **1.531** | **−1.165** |
| Wheel radius / track | `roueG` y −1.498…−0.831, z 0.726…0.880 | **0.334** | **1.606 m track** |
| Front seat | `interieur` 1.553–3.634; `panel` clusters at 1.70 and 2.74 | 2.10 **[E]** | 0.025 |
| Rear seat | as above | 3.15 **[E]** | 0.025 |
| Stabilizer | `ailes2` 6.256–7.296, z ±1.463 | **6.256**, chord **1.040**, span **2.926** |
| Elevator | `profondeur` 7.295–8.206, z ±1.596 | **7.295**, chord **0.911**, span **3.192** |
| Rudder | `direction` 7.292–8.253, y −0.152…1.452 | **7.292**, chord **0.961**, height **1.604** |
| Tail skid | `pate`, lowest point | **7.099** | **−0.510** |

Two places where the mesh and the sheet disagree, and **the sheet wins** because it states them: the **gap**
(sheet 60 in = 1.524 m, mesh 1.606) and the **stagger** (sheet 16 in = 0.4064 m, mesh 0.328). The lower wing is
therefore placed from the upper wing's measured position using the published gap and stagger.

One place the mesh wins outright: the **ailerons**. They start at z 3.112, which is **47 % of the half-span** —
they run over the outer *half* of the wing, not the outer third a modern aeroplane uses. Their chord fraction then
comes from the published area rather than the mesh (which draws them deflected): 1.635 m² over 1.5113 m of chord
and 3.536 m of span is **0.306 of the chord**. The builder prints the area back out and it lands on 1.635 m².

Area-weighted combined wing aerodynamic centre: **1.829 m**. Tail arm to the area-weighted horizontal tail
quarter-chord: **5.12 m**.

## 7. Mass breakdown, CG and inertia (protocol §1c)

Computed, not typed: `B.mass_properties(ITEMS)` in the builder takes the table below and produces the mass, the CG
and the whole inertia tensor, so moving a component moves all of them. Structure fractions are **heavier than the
protocol's modern guide bands** and deliberately so: the Jenny's empty weight is **74 % of its gross**, where a
modern light aeroplane is near 60 %, and the protocol's 10–14 % wing / 10–12 % fuselage bands cannot reach
1,430 lb. The **struts and wires get their own line** — on a two-bay biplane they are real structure sitting
between the wings, and burying them in a wing line puts them in the wrong place.

| Item | lb | kg | s (m) | x from CG | z from CG | note |
|---|---|---|---|---|---|---|
| Propeller, wood 95 in | 30 | 13.6 | 0.056 | +1.989 | +0.128 | rod of 2.413 m |
| Radiator and coolant | 65 | 29.5 | 0.35 | +1.695 | −0.047 | **[E]** |
| OX-5, dry | 390 | 177.0 | 0.879 | +1.166 | −0.132 | 4.33 lb/hp × 90 [1] |
| Bearers, mount, accessories | 70 | 31.8 | 1.05 | +0.995 | −0.047 | **[E]** |
| Landing gear, bungee axle | 100 | 45.4 | 1.531 | +0.514 | +1.318 | split at ±0.803 |
| Upper wing, ailerons, cabane | 230 | 104.3 | 1.879 | +0.162 | −0.880 | rod of 13.294 m |
| Lower wing | 165 | 74.8 | 2.292 | −0.245 | +0.644 | rod of 10.344 m |
| Interplane struts and wires | 65 | 29.5 | 2.04 | +0.005 | −0.077 | **[E]**, rod of 9.1 m |
| Cockpits, seats, instruments | 70 | 31.8 | 2.40 | −0.355 | +0.153 | **[E]** |
| Fuselage, fabric, controls | 185 | 83.9 | 3.35 | −1.305 | +0.003 | **[E]**, rod of 5.9 m — a tapering wooden box girder carries its mass forward of its midpoint |
| Tail group and skid | 60 | 27.2 | 7.10 | −5.055 | +0.053 | **[E]** |
| **Empty** | **1,430** | **648.6** | | | | **matches [1] exactly** |
| Oil, 4 gal | 30 | 13.6 | 1.20 | +0.845 | +0.353 | [1] |
| Fuel, 21 US gal | 130 | 59.0 | 2.10 | −0.055 | −0.397 | [1], gravity tank under the centre section |
| Front occupant | 165 | 74.8 | 2.10 | −0.055 | +0.153 | [1] |
| Rear occupant | 165 | 74.8 | 3.15 | −1.105 | +0.153 | [1] |
| **Gross** | **1,920** | **870.9** | | | | **matches [1] exactly** |

### The CG, and the check that says the layout is right

| | lb | CG (m aft of the nose) | % of the lower-wing chord |
|---|---|---|---|
| Empty | 1,430 | 1.924 | 16 % |
| **Gross, both seats** | **1,920** | **2.045** | **24 %** |
| **Rear-seat solo** (empty + fuel + oil + one pilot aft) | **1,755** | **2.040** | **23 %** |

**A JN-4D is soloed from the REAR seat, and the rear-seat-solo CG comes out 5 mm from the gross CG.** That was not
arranged — it falls out of stations measured off the mesh and a weight breakdown constrained only to hit the
published empty and gross weights. It is the reason the aeroplane is flown from the back, and it is the strongest
evidence available that the layout is right. (Soloed from the front it would sit at 1.932 m, 21 % of chord —
flyable but nose-heavy, and not how it was done.)

The gross CG is 216 mm aft of the combined wing AC. With V_h 0.488 the neutral point lands near 2.21 m, so the
static margin is about **11 % of the mean chord** — positive, and slim, which is a 1917 trainer.

### Inertias

| | kg·m² | dominated by |
|---|---|---|
| Ixx (roll) | **2,718** | the wings: 1,617 upper + 698 lower, then 204 from the interplane structure and 108 from the gear |
| Iyy (pitch) | **1,859** | the tail aft (696), the engine group forward (424), the fuselage rod (386) |
| Izz (yaw) | **4,116** | Σm(x²+y²); Ixx + Iyy is 4,577, so it is the "less a small amount" the protocol expects |
| Ixz | **−41** | Σm·x·z largely cancels: the gear below and forward (+) against the upper wing and engine above and forward (−). 1.5 % of Ixx either way |

**Radii of gyration** (protocol §1c, R = 2√(I/m)/reference): R̄x **0.266** (band 0.25–0.30 ✓),
R̄y **0.351** (0.35–0.40 ✓), R̄z **0.402** (0.38–0.45 ✓). All three in band. The first pass, on the
eyeballed stations, had R̄x out of band at 0.243 — the mesh fixed it without anything being tuned.

**Tail volumes**: V_h = 4.710 × 5.12/(32.754 × 1.5113) = **0.488**, at the bottom edge of the protocol's 0.5–0.7
light-aircraft band. V_v = 1.468 × 5.95/(32.754 × 13.294) = **0.0192**, which is **half** the protocol's
0.03–0.05 band. That is not an error in the measurement: the JN-4D's fin is **3.80 sq ft**, and the aeroplane's
reputation for needing a bootful of rudder and for wallowing in yaw is exactly what a V_v of 0.019 predicts.

**On the ground**: the mesh's wheel contact (s 1.531, y −1.498) and skid (s 7.099, y −0.510) give a three-point
attitude of **10.1° nose-up**, and the CG sits **17.3°** behind the main wheels. The second number is the one that
decides whether it noses over, and 17° is a well-behaved taildragger. The first is a cross-check on the whole
layout: 10.1° of body attitude plus 2° of wing incidence is 12° of wing angle of attack on the ground, and the
spec sheet's landing note is that the machine should be "just grazing the ground with its wheels and tail skid at
minimum flying speed" — 45 mph, which needs C_L 1.05, which a cambered section reaches at about 10–11°. The two
agree.

## 8. Controls (for `docs/research/CONTROLS-REVERSIBILITY-AND-SPRINGS.md`)

- **All three axes reversible**, cables, control **stick** in both cockpits (the JN-4D took the stick from the
  JN-4Can; earlier JNs used a shoulder yoke or a wheel). [2]
- **Ailerons on the upper wing only**, large and deep (0.44 of the chord) and **overhung** past the tip and the
  trailing edge, cable-connected across the centre section.
- **No trim tab, no trim spring, no centering spring on any axis.** Stabilizer incidence is 0° and ground-set. The
  config therefore uses `trimType: "tab"` — the repo's default, which sets the elevator's free-float angle, the
  same choice the Super Cub and the Stearman use for their adjustable stabilizers. **[E]** on there being no
  in-flight adjustment.
- **Unbalanced plain surfaces** — no horn balances, no mass balances. Hinge-moment coefficients are set one step
  heavier than the horn-balanced Stearman's for that reason (aileron Ch_δ −0.55 against the Stearman's −0.45,
  elevator and rudder −0.50 against −0.40): **[E]**, a class estimate from the documented absence of balance, which
  is how every other type in that document was set.
- **No wheel brakes.** A 1917 trainer stops on a **tail skid** in grass. `brake: false` on both mains, and the skid
  is unsteerable with a high scrub coefficient and a high rolling resistance. This is a real constraint on the
  aeroplane, not an omission: it cannot be held against power and it cannot be stopped on pavement.

## 9. Performance: what it was calibrated on, and what it measured

| | Source figure | What the config does | Where it is held |
|---|---|---|---|
| **Maximum level speed** | **75 mph** [1][2] | **75 mph** | `JennyPerformanceTests.MaximumLevelSpeedIsTheBookSeventyFive` |
| Climb | 2,000 ft in 10 min = **200 ft/min** [1]; in 7 min 30 s = **267** [2] | **282 ft/min** at Vy 46 mph | `ClimbsLikeNinetyHorsepower` (band 200–400: the suite reads ROC high — the C172 gives 1,274 fpm against a book 730) |
| Minimum level speed | **45 mph** [1] | level flight possible down to **37 mph** | §9.1 below |
| Best glide | none published | **8.2:1 at 44 mph** | printed by `BOOK_SPEEDS=1` |
| Cruise | **60 mph** [2] | 75 % power gives **67 mph** | not asserted — see below |
| Service ceiling | **6,500 ft** [2]; 10,000–11,000 [7] | not measured | the source spread is unresolved |

**fuselage.cd0Area = 1.42 m²** is the one number calibrated rather than measured, exactly as the protocol directs,
and it is calibrated on the **maximum level speed alone**. That is deliberate: maximum level flight is drag against
power and nothing else, where a period "cruise 60 mph" is somebody's habit at some unrecorded throttle setting and
a rate of climb depends on the propeller. `BOOK_SPEEDS=1` sweeps it and prints the whole curve — 1.28 m² gives
78 mph, 1.42 gives 75, 1.56 gives 73. 1.42 m² is **15.3 sq ft of equivalent flat plate**, four times the Extra's
and twice the Stearman's, which is what a radiator, an uncowled V-8, two open cockpits, eight interplane struts, a
cabane, a forest of flying and landing wires, a bungee axle and a tail skid add up to.

The book cruise is **not** asserted, because 60 mph against a 75 mph maximum is about 51 % of power, not 75 %. The
model's 75 %-power speed of 67 mph and a habitual cruise of 60 mph are consistent with each other; they are not the
same quantity, and testing one against the other would be a false check.

### 9.1 The open gap: it stalls 7 mph slower than the book says it can fly

The aerodynamic stall comes out at **37.8 mph**, where the sheet's **minimum level flight speed is 45 mph**. The
stall has to be below the minimum level speed — an aeroplane cannot have a minimum level speed below its stall —
and it is, so nothing is inverted. But 7 mph is more than it should be: 45 mph at 1,920 lb over 352.56 sq ft needs
a system **C_L of only 1.05**, and the config's wing reaches **1.495**, which is exactly the peak of the
`clarkY-like` table it borrows. The wing makes 1.42× the lift the book asks for.

Two candidates, and the measurement rules one of them out.

- **`clarkY-like` standing in for Eiffel 36** (§5). A thin, strongly cambered 1917 section on a wire-braced fabric
  wing would peak lower and earlier than a Clark Y. **Unresolved** — no measured Eiffel 36 table was found.
- **Biplane interference, which the strip model does not have at all.** Every strip sees the free stream, so two
  wings one chord apart make as much lift as two wings a long way apart. This was the first suspect, and
  `BOOK_SPEEDS=1` on the other two biplanes does **not** support it being the whole story:

  | | sim Vso | book stall | |
  |---|---|---|---|
  | Stearman PT-17 | 53 mph | 52 mph | on the nose |
  | Pitts S-2B | 68 mph | 56 mph | reads 21 % **high** |
  | Jenny JN-4D | 38 mph | 45 mph (minimum level) | reads 16 % **low** |
  | C172 — the monoplane control | 62 mph | 55 mph | reads 13 % **high** |

  There is no biplane-wide bias to find: one biplane matches, one reads high, one reads low, and the monoplane
  control reads high too. If the missing interference term were doing the work, all three biplanes would read low.

  The Stearman and the Pitts are both limited by **elevator authority** before their wings stall (the known item:
  full aft stick pins the Pitts at α 13° and the Stearman at α 10°), so neither of them is actually measuring its
  wing. The Jenny has the elevator to reach its wing's real peak, which is why it is the type that exposes this.

Pinned by `JennyPerformanceTests.StallsBelowItsPublishedMinimumLevelSpeed`, which asserts the stall stays below
45 mph and inside 34–45, so the gap cannot widen unnoticed in either direction.

**What would settle it:** a measured Eiffel 36 polar (§5), and separately a biplane-interference term (Munk /
Prandtl) in the strip model. The second is a real aero-model change that would touch the Stearman and the Pitts as
well as the Jenny, so it is not something to fold into adding an aeroplane.

- **Vne 44.7 m/s (100 mph) [E]** and **+3.5/−1.5 g [E]**. No placard was found. The aeroplane's maximum level speed
  is 75 mph; it was not stressed for aerobatics, and barnstormers who looped them lost wings. The limits are set to
  make that true in the game rather than to be generous.

## 10. Sources

1. "JN4 spec" — transcribed JN-4D specification sheet (dimensions, areas, weights, engine, performance).
   <https://airminded.net/jenny/jn4_spec.html> — the page states it is unofficial and not for operating or
   maintaining a real aeroplane; treated here as a secondary transcription of a period sheet, and the figure it
   gives that can be cross-checked (gross 1,920 lb, length, height, engine) agrees with [2] and [3].
2. "Curtiss JN Jenny", Wikipedia (airfoil Eiffel 36; cruise 60 mph; ceiling 6,500 ft; 2,000 ft in 7 min 30 s;
   aileron and control-system history by variant; the specification block's JN-4B attribution).
   <https://en.wikipedia.org/wiki/Curtiss_JN_Jenny>
3. Aeropedia, "Curtiss JN-4 Jenny" (OX-5 90 hp at 1,400 rpm, 500 cu in, V-8 water-cooled; loaded 871 kg; tail skid
   on the extended rudder post). <https://aeropedia.com.au/content/curtiss-jn-4-jenny/>
4. International Military Antiques listing, original OX-5 wood propeller, **95 in**, described as the type used on
   the JN-4. <https://www.ima-usa.com/products/original-u-s-wwi-curtiss-jn-4-jenny-early-sawtooth-clad-scimitar-shaped-lightweight-speed-ox-5b-wood-propeller-95-inches>
   — a dealer listing, not a technical reference; used only for the propeller length.
5. The Aerodrome forum, WWI-era airfoil usage (JN-4 root and tip Eiffel 36; the JN-4H wing used Eiffel 36 while the
   N-9 used RAF 6). <https://theaerodrome.com/forum/showthread.php?t=70634>
6. Museum of Flight, JN-4D reproduction (wingspan 43.58 ft, wing area 352.00 sq ft, empty 1,390 lb).
   <https://www.museumofflight.org/exhibits-and-events/aircraft/curtiss-jn-4d-jenny-reproduction>
7. Aerospace Heritage Foundation of Utah, JN-4D pages (ceiling 10,000–11,000 ft; lower wingspan 33 ft 11¼ in).
   <https://www.aerospaceutah.org/curtiss-jn-4d-jenny/>
8. EAA Museum, 1918 Curtiss JN-4D (empty 1,430 lb).
   <https://www.eaa.org/eaa-museum/museum-collection/aircraft-collection-folder/1918-curtiss-jn4d-jenny>

### Resolved since the first pass

- **Wheel track** is **1.606 m**, measured off the mesh (`roueG` z 0.726…0.880), not the 1.68 m first estimated.
- **Wheel diameter** 26 in, measured (`roueG` y spans 0.667 m).

### Not found, and wanted

- A **measured Eiffel 36** lift/drag/moment table (§5). Eiffel's 1919 *Nouvelles recherches* is the likely home.
- A **placarded Vne and load factor** for the JN-4D (§9).
- The **service-ceiling** disagreement between 6,500 ft and 10,000–11,000 ft.
