# Grob G 103 C Twin III Acro — `glider-grob103-acro-like` — derivation

Lane `lane/claude-grob103`. Built 2026-11-01 as the G 103A Twin II Acro; **re-based on the G 103 C Twin III Acro
2026-11-04** on Ty's call: the Twin III Acro, with its fixed tandem gear (main, nose and tail wheel, none retracting).
The id stays `glider-grob103-acro-like`. Ty's brief still holds: build it **from first principles** — published geometry,
standard aerodynamics, no fudge factors — and use the published performance **for validation only**. Nothing in the
config was adjusted toward a performance figure. Where a number is not published for the Twin III it is either carried
over from the Twin II and **flagged**, derived from a stated design rule, or marked ASSUMED; each is listed under
[Gaps](#gaps).

The config is generated. Do not hand-edit it:

```
XFOIL=/path/to/xfoil python3 tools/aircraft_builder/grob103_xfoil.py   # E583 (+ E603) + FX 71-L-150/30 polars -> data/grob103_xfoil.json
python3 tools/aircraft_builder/grob103_threeview.py                    # the TWIN II manual's three-view, measured -> data/grob103_threeview.json
python3 tools/aircraft_builder/build_grob103.py                        # -> configs/aircraft/glider-grob103-acro-like.json, prints every derivation
cp configs/aircraft/glider-grob103-acro-like.json unity/Assets/StreamingAssets/aircraft/   # the committed ConfigSync mirror
GROB_PROBE=1 dotnet test tools/FlightTests --filter Grob103Probe       # the validation numbers below
GROB_RIG=1 dotnet test tools/FlightTests --filter Grob103Rigging       # the stabiliser-incidence rig point
```

## Sources

| key | document |
|---|---|
| [POH] | Pilot's Operating Handbook **G 103 C TWIN III ACRO**, Burkhart Grob, issue January 1989, revision 6 / 16.10.2003 (the s/n 34197 G-CLXH copy). <https://pilots.scottishglidingcentre.co.uk/_media/aircraft/twinastir/grob103c_poh.pdf>. Where it gives two sets of limits, the config takes the **OSB 315-66 fuselage reinforcement installed** set — the airframe aerobatics are approved for ([POH] 2.11). |
| [TCDS] | EASA TCDS **A.250** Issue 04 (21 Dec 2011), **Section N "GROB G 103 C TWIN III ACRO"**. <https://www.easa.europa.eu/en/downloads/7216/en>. (Ty's brief said A.024; the Twin III Acro is Section N of A.250, the Grob sailplanes sheet.) |
| [UIUC] | UIUC *Incomplete Guide to Airfoil Usage*: "Grob 103C Twin III Acro — Eppler 583 — Eppler 583" (root, tip). Coordinates from the UIUC Airfoil Coordinates Database (`e583.dat`). |
| [N41KP] | Statement of weight and balance, Grob 103C Twin III **N41KP**, 29 Sep 2010 (Norcal Soaring). <https://www.norcalsoaring.org/uploads/1/0/0/6/100675970/kp_weight_and_balance.pdf>. One weighed airframe: empty 969.3 lb / CG 28.69 in, front pilot −44.57 in, rear pilot 1.38 in, baggage 31.89 in, datum to main gear 21.25 in, to tail wheel 192.00 in. |
| [FM-II] | Flight Manual GROB G 103 TWIN II, Sept 1981 (the 2026-11-01 build's source). Used now **only** for what the Twin III has no figure of its own for: the three-view the planform and tail are measured from, tyre sizes. |
| [TCDS-FAA] | FAA TCDS G39EU (G 103 / G 103A — the **Twin II**): control-surface travel, airbrake height. Kept and flagged. |
| [CS-22] | CS 22.147 (roll reversal 45–45 within b/3 s at 1.4 VS1), CS 22.75 (brakes out: no flatter than 1:7 at 1.3 VS0, max weight), CS 22.345 (brakes out −1.5 g). The type's own basis is the LFSM 1975 ([TCDS] N.II.2). |
| [Hoerner], [Raymer] | as before: flat plates, junctions, wheels; fuselage form factor |

## 0. What changed from the Twin II build, and from where

| quantity | Twin II (2026-11-01) | Twin III Acro (2026-11-04) | source |
|---|---|---|---|
| span | 17.5 m | **18.0 m** | [POH] 1.4; [TCDS] N.III.4 |
| wing area | 17.8 m² | **17.5 m²** | [POH] 1.4 |
| aspect ratio | 17.21 | **18.51** (b²/S; [POH] "18.5") | [POH] 1.4 |
| length / height | 8.18 / 1.55 m | 8.18 / 1.55 m (unchanged) | [POH] 1.4 |
| wing section | E603 (19.8 %) | **E583 (16.5 % at 0.41 c)** | [UIUC]; thickness from the coordinates |
| MAC | 1.058 m published, 1.062 modelled | **1.015 m, not published** (planform integral) | — |
| empty mass | 380 kg | **399 kg** | [POH] 5.3.2: D-4279 "flight weight = empty weight + 180 kg = 579 kg" |
| empty CG | 738 mm (mid of 720–757) | **729 mm** | [N41KP] 28.69 in |
| MTOM | 580 kg | **600 kg** | [POH] 2.6; [TCDS] N.III.9 |
| max non-lifting parts | 400 kg | **420 kg** (wings 600 − 420 = 180 kg, unchanged) | [POH] 2.6; [TCDS] N.III.9 |
| aerobatic-category mass | — | **600 kg** (no separate figure: one maximum mass) | [POH] 2.6 |
| CG range | 260–460 mm | **270–480 mm** aft of datum | [POH] 2.7; [TCDS] N.III.10 |
| seat arms | 1150 fwd / 40 aft mm | **1132 fwd / 35 aft mm** | [N41KP] |
| baggage | 10 kg at 450 mm aft (arm unsourced) | **10 kg at 810 mm aft** | [POH] 2.6 / 7.5; [N41KP] 31.89 in |
| trim weights | 5.6 kg at the front seat arm | **up to 2 × 5.6 kg at 1543 mm fwd** (1560 for two) | [POH] 6.4 |
| min / max seat loads | 70 / 110 / 110 kg | 70 / 110 / 110 kg | [POH] 2.10 |
| water ballast | none | **none** ([POH] 7.10.1: removable ballast = trim weights only) | [POH] 7.10 |
| VNE | 250 km/h | **280 km/h** | [POH] 2.2; [TCDS] N.III.7 |
| VNE with altitude | 250/237/225/213/201 (6500–19000 ft, last extrapolated) | **280 to 2000 m, 265 (3000), 240 (5000), 215 (7000), 190 (9000), 165 (11000 m)** — nothing extrapolated | [POH] 2.2, 4.5.7 |
| VA | 180 km/h | **185 km/h** | [POH] 2.2; [TCDS] |
| VRA | 180 | **200 km/h** (not wired) | [POH] 2.2; [TCDS] |
| VT (aerotow) | 170 | **185 km/h** (not wired) | [POH] 2.2 ("185 kts" in the table is a misprint for km/h; [TCDS] 185 km/h) |
| VW (winch) | 120 | **140 km/h** (not wired) | [POH] 2.2, 4.5.1; [TCDS] |
| acrobatic load factors | +6.5/−4.0 at VA, +5.3/−3.0 at VNE (BGA snippet) | **+6.5/−4.0 at VA 185, +5.3/−3.0 at VNE 280** | [POH] 2.9 |
| brakes-out load factor | −1.5/+3.5 (CS 22.345) | **+3.5** [POH] 2.9 / 4.5.9; −1.5 kept from CS 22.345 (flagged) | [POH]; [CS-22] |
| utility category | +5.3/−2.65 at VA 170, +4.0/−1.5 at VNE | **none**: Acrobatic only at 18 m ([TCDS] N.I.3); the Twin II envelope removed | [TCDS] |
| weak link | 630 daN | **845 daN** (record only: the sim does not read `towHook`) | [TCDS] N.III.6; [POH] 4.5.1 |
| approach / spawn speed | 95 km/h | **96 km/h** | [POH] 2.3, 4.5.4 |
| main / tail wheel station | 705 / 5018 mm aft (Twin II drawing) | **540 / 4877 mm aft** | [N41KP] |
| airbrakes | upper surface, "Schempp-Hirth type" | **upper surface only, "Type GROB"**; no flaps | [POH] 1.4, 7.4; [TCDS] N.III.2 |
| airbrake local velocity Ue/V | 1.42 (E603 XFOIL dump) | **1.40** (E583 XFOIL dump, rerun) | XFOIL, §6 |
| rigging: wing incidence | 1.89° (CL 0.60) | **2.78°** (CL 0.761 = 579 kg at 95 km/h) | design rule on [POH] 5.3.2 |
| rigging: stab incidence | −1.53° | **−0.68°** (re-solved at 375 mm, 579 kg, 95 km/h) | design rule, `Grob103Rigging` |
| cd0Area | 0.0563 m² | **0.0523 m²** (thinner root junction, Re at 95 km/h) | build-up §3 |
| default load | 470 kg, CG 377 mm | **489 kg** (399 + 90 kg solo), **CG 386 mm** | build-up §4 |
| display name | "Twin II Acro-like Aerobatic Two-Seater" | **"Twin III Acro-like Aerobatic Two-Seater"** | — |

## 1. Geometry

### Published ([POH] 1.4)

Span 18.0 m, wing area 17.5 m², aspect ratio 18.5 (b²/S 18.51), length 8.18 m, height 1.55 m, max flight weight 600 kg,
max wing loading 34.3 kg/m². Two-seat mid-wing, damped T-tail, "the 2-section wing is triple tapered with airbrakes (Type
GROB) on the upper side", tandem seats, "non-retractable tandem landing gear", main wheel with a hydraulic disc brake,
nose wheel steerable from s/n 34171 ([TCDS] N.III.2: "fixed tandem landing gear with brake and steerable nose wheel").

### Planform — the Twin II's shape, stretched (ASSUMED)

The Twin III's three-view ([POH] 1.6) is a small scanned drawing with no dimension lines, too small to measure the way
the Twin II's was. So the Twin II planform measured off [FM-II] p.5 (`grob103_threeview.py`, unchanged) is used for its
SHAPE: every spanwise station (chords, aileron, airbrake box, strip edges) is stretched by 18.0/17.5 = **1.0286**, and the
chords then scaled to the published area (×0.9692). That gives root 1.251 m, y 4.89 m 1.008, y 8.23 m 0.641, tip
0.524 m; MAC 1.015 m. **The Twin III's own triple-taper break stations are lost** — a gap Ty can close with a
measurement of the real wing or a better drawing.

Carried over from the Twin II drawing unchanged (no Twin III figure): dihedral 2.92°, aileron chord ratio E 0.258
(span now y 5.17–8.68 m), airbrake box at x/c 0.423 (span now y 2.43–3.91 m), straight unswept leading edge, the tail
(stab 2.155 m², elevator 0.259 of it, z −1.20 m; fin 0.734 + rudder 0.504 m²) and the fuselage profile. The tail keeps
the Twin II drawing's own chord correction (×1.0140), not the wing's stretched one.

**Frame.** Body origin = the datum, "wing leading edge at the root rib" ([POH] 2.7; [TCDS] N.III.11), on the root chord
line, +x forward, +y right, +z down. A station s mm aft of the datum is body x = −s/1000.

## 2. Sections — XFOIL 6.99, run here

XFOIL 6.99 built from the MIT source (Homebrew gfortran 16, plot driver stubbed), checked again 2026-11-04 on NACA 0012
at Re 10⁶, α 4°: CL 0.4278, CD 0.00728 (the known values). Settings unchanged: 200 panels, free transition, **Ncrit 9**,
Mach 0, α swept out from 0 in 0.25° steps (`grob103_xfoil.py`, scratch files in a temp dir).

**Wing: Eppler E583** ([UIUC]: root and tip of the Twin III Acro), **16.5 % thick at 0.41 c** (from the coordinates).

| Re | a₀ /rad | α₀ | cl_max (at α) | cd_min | cm₀ |
|---|---|---|---|---|---|
| 0.6 M | 6.300 | −4.62° | 1.422 (15.8°) | 0.00825 | −0.104 |
| 0.8 M | 6.387 | −4.67° | 1.439 (18.0°) | 0.00719 | −0.103 |
| 1.0 M | 6.435 | −4.68° | 1.461 (18.2°) | 0.00660 | −0.103 |
| 1.25 M | 6.447 | −4.71° | 1.483 (18.5°) | 0.00606 | −0.103 |
| 1.5 M | 6.465 | −4.73° | 1.501 (19.0°) | 0.00569 | −0.103 |
| 2.0 M | 6.490 | −4.76° | 1.528 (19.0°) | 0.00520 | −0.103 |

The **2.5 M sweep did not converge past 13.5°** (its "cl_max" 1.457 is where XFOIL stopped, not a stall), so the family
ends at 2.0 M and is held there above it; the 2.5 M points stay in `data/grob103_xfoil.json` for the record. The E603
polars stay in the file too but are no longer flown.

**Tail: Wortmann FX 71-L-150/30, ASSUMED** (unchanged; no Twin II or Twin III tail section is published).

### From section to wing

Glauert lifting line on the stretched planform with its washout: a_3D **5.782/rad = 0.894 a₀**, span efficiency
**e 0.982**; the first section to reach cl_max is at y 1.20 m at 1.056 × CL. Everything else (Reynolds family per
strip, flat-plate post-stall, the fin's pre-scaled table) is as in the Twin II build.

### Rigging (unpublished; design rules, not fits)

- **Washout 1.5° linear — ASSUMED** (unchanged).
- **Wing incidence 2.78°**: the wing's own α at the published best-glide point — [POH] 5.3.2, 579 kg at 95 km/h,
  CL = 2W/(ρV²S) = **0.761** — so the fuselage meets the air nose-on at best glide.
- **Stabiliser incidence −0.68°**: the elevator streamlined at the mid-envelope CG ((270 + 480)/2 = 375 mm) at that
  best-glide point. Solved in the sim (`GROB_RIG=1 Grob103Rigging`), stepping stab += 0.53 × elevator from the Twin
  II's −1.53°: elevator +1.29° → −0.85°; +0.33° → −0.68°; residual +0.09°.

## 3. Parasite drag

The build-up is the Twin II's, re-run: fuselage skin friction 0.0318 m² (S_wet 11.83, Re_L at the [POH] best-glide 95
km/h, transition at the canopy frame), wing–fuselage junction **0.0048** (E583's t/c 0.165 on the 1.251 m root, halved
for the fillet), tail junctions 0.0026, wheels 0.0115 (Hoerner, half in a well — the fixed wheels in their fairings),
misc 0.0016 → **cd0Area 0.0523 m²** (was 0.0563).

## 4. Mass, CG and inertia

Wings **180 kg** = MTOM 600 − max non-lifting 420 ([POH] 2.6), the same rule as before. Tail, concentrated items: as in
the Twin II build, with the main and tail wheel masses moved to the [N41KP] stations. The shell takes the rest of the
**399 kg** ([POH] 5.3.2 polar airframe) and its areal-density ramp is solved onto the [N41KP] empty CG, **729 mm**:
spread uniformly it would sit at 660 mm; k = 0.70 (tail-end laminate 1.64 × the nose's). **Flag**: 399 kg is D-4279's
empty mass and 729 mm is N41KP's empty CG (a 440 kg airframe with equipment); no single document gives both for one
airframe, and the [POH] gives no type empty-CG range (it is in the Maintenance Manual, Sec. 7, not available).

| loading | mass | CG mm aft | %MAC (1.015) | |
|---|---|---|---|---|
| empty | 399 | 729 | — | [N41KP] 729 |
| 70 kg solo (min front load) | 469 | 451 | 44.4 | inside 480 ✓; 55 kg solo is aft of it, and 2 trim weights bring it back ✓ |
| **90 kg solo — the default** | **489** | **386** | 38.1 | |
| 110 kg solo | 509 | 327 | 32.2 | inside ✓ |
| 2 × 100 kg | 599 | 302 | 29.8 | inside ✓, under MTOM ✓ |
| 2 × 110 kg | 619 | 275 | 27.1 | inside the CG range, 19 kg over MTOM |

Default-load inertia **Ixx 3296, Iyy 1260, Izz 4458, Ixz 114 kg·m²**.

## 5. Controls — the Twin II's travels, KEPT

[TCDS] N.III.13 refers the Twin III's control-surface deflections to its Maintenance Manual, which is not available. The
Twin II's [TCDS-FAA] travels, read as the chord of the arc, are kept: aileron 25.0 up / 13.8 down (flown as their 19.4°
mean), elevator 23.3 up / 18.2 down, rudder ±30.0°. [POH] 4.5.9 confirms the Twin III has differential ailerons ("the
aileron differentiation"); the sim still flies the mean. Wheel brake on the end of the airbrake travel ([POH] 7.4 "With
the airbrakes fully extended, the wheel brake is activated"): `wheelBrakeFromSpoiler` 0.9.

## 6. Airbrakes

"Airbrake system of type GROB … mounted on the upper side of the wing" ([POH] 7.4); "airbrakes on upper wing surface"
([TCDS] N.III.2). **No flaps** (the [POH] 4.5.3 sentence "up to VNE the flaps may be extended" is boilerplate about the
airbrakes; there are none). Ty's brief said "Schempp-Hirth type upper/lower airbrakes": the [POH] and [TCDS] both say
upper surface only and Grob's own type, so the model is upper-surface only — **question for Ty** below.

The physical model is unchanged (lift aft of the plate lost on the strips under it, plate C_D 1.2 in the local
dynamic pressure). The local velocity was re-measured on the E583, as CLAUDE.md requires of any number credited to a
tool: XFOIL DUMP at Re 1.5 M, nearest panel x/c 0.434, **Ue/V 1.326 / 1.398 / 1.464 / 1.531 at CL 0.21 / 0.57 / 0.92 /
1.25**; the middle, **1.40**, so the plate sees 1.96 q. Height 170 mm and one blade: the Twin II's, flagged.

## 7. Limits

| | value | source |
|---|---|---|
| category | **Acrobatic** (18 m); aerobatics only with OSB 315-66 | [TCDS] N.I.3; [POH] 2.8, 2.11 |
| approved manoeuvres | positive loop, turn, lazy eight, chandelle, spin, slow roll, Immelmann, split S, inverted flight, inverted spin | [POH] 2.8 |
| load factors | **+6.5/−4.0 at VA 185, +5.3/−3.0 at VNE 280**, straight line between | [POH] 2.9 |
| brakes out | **+3.5**; −1.5 | [POH] 2.9, 4.5.9; [CS-22] 22.345 (flagged) |
| mass | full limits to 420 kg non-lifting; above, × 420/(m − 180) | [POH] 2.6 |
| VNE | 280 to 2000 m, 265 / 240 / 215 / 190 / 165 at 3000 / 5000 / 7000 / 9000 / 11000 m | [POH] 2.2, 4.5.7 |
| VRA / VT / VW | 200 / 185 / 140 km/h (recorded in `limits._comment`, not wired) | [POH] 2.2; [TCDS] N.III.7 |
| without OSB 315-66 | VNE 250, VA = VRA = VT 170, VW 140; no aerobatics, no cloud | [POH] 2.2A, 2.11 (not modelled) |
| weak link | 845 daN | [TCDS] N.III.6 |

## Validation (predicted vs published — nothing fitted)

Sea level ISA, the trim solver with the elevator balancing pitch; loadings through the load sheet; "aft"/"forward" CG
rows flown at the [POH] 2.7 limits.

| quantity | published | sim | error | test band | |
|---|---|---|---|---|---|
| best L/D, 579 kg | 37.5 at 95 km/h [POH] 5.3.2 | **35.8 at 94.5** | −4.5 % / −0.5 % | ±12 % / +5 −15 % | pass |
| min sink, 579 kg | 0.64 m/s at 80 km/h | **0.656 at 75.6** | +2.5 % / −5.5 % | ±12 % | pass |
| V(best L/D) 579/469 | √(579/469) = 1.111 | 1.094 | −1.5 % | ±4 % | pass |
| stall, 450 kg aft | 61 / 66 km/h (clean / brakes) [POH] 5.2.2 | **59.6 / 62.8** | −2 % / −5 % | +5 −15 / +5 −20 % | pass |
| stall, 530 kg aft | 66 / 72 | **64.6 / 68.0** | −2 % / −6 % | | pass |
| stall, 600 kg forward | 72 / 80 | **70.2 / 73.8** | −2.5 % / −8 % | | pass |
| stall, 470 kg solo | 62 / 68 [POH] 4.5.3 | **61.0 / 64.3** | −1.6 % / −5 % | | pass |
| full brakes, 96 km/h | 1:6.6 [POH] 4.5.4 | **1:7.27** (489 kg) | +10 % flatter | (printed) | **NOT MET** |
| full brakes, 1.3 VS0, 600 kg | 1:7 or steeper [CS-22] 22.75 | **1:8.84** | | (printed) | **NOT MET** |
| 54° dive, full brakes, 600 kg | terminal = VNE 280 km/h [POH] 4.5.3 | **266 km/h** | −5 % | ≤ 280, > 238 | pass |
| 45–45 reversal, 1.4 Vs | 4.5 s max [POH] 4.5.3; CS 22.147 ≤ 6.0 s | **4.64 s** (489 kg), **4.32 s** (579 kg) | +3 % / −4 % | 4.5 ±15 % | pass |
| spin, aft CG (70 kg solo) | 2–6 s/turn, 80–120 m/turn [POH] 4.5.9 | **3.5 s, 61 m** | height −24 % | 2–6 s; 52–120 m | rate pass, **height NOT MET** |
| spin, forward limit (270 mm) | "will hardly spin" / no stationary spin [POH] 4.5.9, 3.5 | 0.69 turns in 12 s | | < 1 turn | pass |
| spin recovery | "terminated" by the standard recovery | 1.17 → 0.32 rad/s in 6 s | | slows ≥ ⅓ | **GAP** (not a prompt stop) |

### What the results say

- **The polar and the clean stall are close** (best L/D −4.5 %, min sink +2.5 %, clean stalls 1.6–2.5 % low), much
  closer than the Twin II build was against its own manual. The E583 is a better match than the Twin II's E603 was for
  that manual, and the [POH]'s 1986 polar is a measured flight test of a 579 kg airframe — the same loading the model
  is tested at.
- **The brakes' drag is right and their lift loss too small, as before**: the dive is held 5 % under the book's
  point, but the brakes raise the stall 5 % where the [POH] says 8–11 %, and the approach glide is 10 % flatter than
  1:6.6. The chord-ahead-of-the-plate lift model is still the suspect.
- **Spin height per turn 24 % low** — the developed spin is flatter than the real one (the fleet's open spin item).
  Asserted at what the model gives, with the book printed beside it.

## Gaps

- **Planform**: the Twin II's measured shape, stretched to 18.0 m and scaled to 17.5 m² — the Twin III's own triple-taper
  break stations are not modelled. Aileron and airbrake spans are stretched with it.
- **Carried over from the Twin II, unverified for the Twin III**: dihedral, aileron chord ratio, airbrake x/c, height
  (170 mm) and blade count, the whole tail (tailplane, fin, rudder) and the tail section, the fuselage profile and the
  datum's place on it, the nose-wheel station, tyre sizes, control travel, every concentrated mass, the spring trim.
- **Design rule, not published**: wing and stab incidence, the shell density ramp, washout (1.5° ASSUMED).
- **Mixed-airframe mass data**: 399 kg empty (D-4279) with N41KP's 729 mm empty CG and arms.
- **No MAC published**: %MAC is of the planform's own integral.
- **Airbrakes**: −1.5 g brakes-out limit from CS 22.345 (the [POH] gives none); lift loss too small (above).
- **Not wired**: VRA, VT, VW; the weak link (the sim does not read `towHook`); the without-OSB 315-66 envelope; the 20 m
  SB 315-59 wingtips; the steerable nose wheel (s/n 34171 on).
- **Spin height and recovery** (above). **Differential ailerons** flown as their mean. **Inverted flight** is not
  validated (the [POH] gives inverted stalls of 112–125 km/h, 4.5.9 / 5.2.2 — a test to add).
- **Viscosity** sea level at every height; **flutter** not modelled (the VNE table is the protection); wet wing not
  modelled.
- **Eye points**: still the own model's (Codex) unapproved proposals at the Twin II seat arms.
