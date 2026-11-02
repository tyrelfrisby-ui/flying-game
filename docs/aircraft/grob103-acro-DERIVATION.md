# Grob G 103A Twin II Acro — `glider-grob103-acro-like` — derivation

Lane `lane/claude-grob103`, 2026-11-01. Ty's brief: build the aerobatic two-seat trainer **from first principles** —
published geometry, standard aerodynamics, no fudge factors — and use the published performance **for validation
only**. Nothing in the config was adjusted toward a performance figure. Where a number is not published it is
measured off the type's own three-view, derived from a stated design rule, or marked ASSUMED; each is listed under
[Gaps](#gaps).

The config is generated. Do not hand-edit it:

```
XFOIL=/path/to/xfoil python3 tools/aircraft_builder/grob103_xfoil.py   # E603 + FX 71-L-150/30 polars -> data/grob103_xfoil.json
python3 tools/aircraft_builder/grob103_threeview.py                    # the manual's three-view, measured -> data/grob103_threeview.json (needs Pillow, pdftoppm)
python3 tools/aircraft_builder/build_grob103.py                        # -> configs/aircraft/glider-grob103-acro-like.json, prints every derivation
GROB_PROBE=1 dotnet test tools/FlightTests --filter Grob103Probe       # the validation numbers below
```

## Sources

| key | document |
|---|---|
| [FM] | Flight Manual GROB G 103 TWIN II, FAA-English edition Sept 1981, LBA revisions to March 1982. <https://esgc.co.uk/wp-content/uploads/2014/01/Grob103_Flight_Manual.pdf> |
| [TCDS-FAA] | FAA TCDS G39EU (Grob G 103 / G 103A): datum, CG range, seat arms, control-surface travel |
| [TCDS-EASA] | EASA TCDS A.250 Issue 04 (21 Dec 2011), Section L "G 103 A Twin II Acro": category Utility and Acrobatic, VA 170 / 180, VNE 250, VT 170, VW 120, max mass 580 kg, NLP 400 kg, "airbrakes on upper wing surface". <https://www.easa.europa.eu/en/downloads/7216/en> |
| [MSB] | Grob MSB 315-65/1 (9 Feb 2006): restricts Twin II Acros to "simple aerobatics" (loop, turn, lazy eight, chandelle, spin); the acrobatics handbook becomes invalid. <https://ltb-lindner.com/wp-content/uploads/bsk-pdf-manager/2021/04/315-65-1-new-limits.pdf> |
| [OTM] | Grob OTM 315-66/1: fuselage stiffeners restore full aerobatics, placard VA 180, VRA 180, VFE 250. <https://ltb-lindner.com/wp-content/uploads/bsk-pdf-manager/2021/04/315-66-1-betriebsgrenzen-modofikation.pdf> |
| [BGA] | BGA data sheet, Grob 103 Twin II Acro (snippet only; the PDF returns 403): "+6.5 & -4 up to 97 knots, +5.3 & -3 to VNE. Otherwise semi-aerobatic." |
| [CS-22] | CS 22.147 (roll reversal 45-45 within b/3 s at 1.4 VS1), CS 22.75 (brakes out: glide no flatter than 1:7 at 1.3 VS0, max weight), CS 22.345 (brakes out: -1.5 to +3.5 g), UK CAA regulatory library |
| [UIUC] | UIUC Airfoil Coordinates Database (E603, FX 71-L-150/30 coordinates); UIUC "Incomplete Guide to Airfoil Usage" lists E603 root and tip for the Twin II |
| [Hoerner] | S. F. Hoerner, *Fluid-Dynamic Drag* (1965): flat plates normal to the flow (ch. 3), junctions (8-11), wheels (13) |
| [Raymer] | D. Raymer, *Aircraft Design: A Conceptual Approach*: fuselage form factor (eq. 12.31) |

## 1. Geometry

### Published

Span 17.5 m, wing area 17.8 m², length 8.18 m, height 1.55 m, max 580 kg, "Aspect Ratio 17.1" ([FM] I.5). b²/S is
**17.21**; the manual's 17.1 is its own rounding. Mid wing, T-tail, upper-surface Schempp-Hirth-type airbrakes,
fixed tandem gear (main 6.00-6, nose 260×85, tail 210×65, [FM] p.10). MAC: [FM] II.7 puts 260–460 mm aft of the
datum at "24.7 % to 43.6 % of the M.A.C." — 200 mm / 0.189 = **1.058 m**, with its leading edge at the datum.

### Measured off the manual's three-view ([FM] p.5)

The only three-view of the type found free; it has no dimension lines. `grob103_threeview.py` renders it at 400 dpi
and reads every edge at the **centre** of its drawn line. Scales: plan and front views by the 17.5 m span (both
2523 px, 6.936 mm/px); side view by the 8.18 m length (1172 px, 6.980 mm/px). The plan view's fuselage is drawn
2.7 % short of 8.18 m, so stations **along the fuselage** come from the side view (and plan-view x stations are
stretched by 8.18/7.956) and the plan view gives only the planform shapes.

| item | measured | how |
|---|---|---|
| wing planform | root 1.309, y 4.75: 1.055, y 8.0: 0.671, tip 0.548 m (double trapezoid, break ~4.8 m) | chord at 0.25 m bins, both halves averaged; area 17.554 m² → chords ×1.0140 to the published 17.8 (protocol: keep the distribution, scale to the published total) |
| MAC | **1.062 m** (published 1.058, +0.4 %) | ∫c²dy/S of the scaled planform — the independent check that the measured planform is right |
| leading edge | straight, unswept (±0.03 m tip to root once the drawing's tilt cancels) | LE line centres |
| dihedral | **2.92°** | front view, upper-surface line fitted on each half; the drawing is rotated 0.64°, which the two halves' average removes |
| ailerons | y 5.03 → 8.44 m, **E = c_a/c = 0.258** | the hinge line drawn on the plan view |
| airbrake box | y 2.36 → 3.80 m (1.44 m a side), at **x/c 0.423** | the slot line drawn on the plan view |
| tailplane | span 3.35 m, root 0.78 m, tip ~0.43 m; area **2.155 m²**; straight hinge; elevator **0.259** of the area | plan view; chords × the wing's 1.0140 |
| fin / rudder | fin LE 6.80 → 7.30 m from the nose (22.6° sweep), hinge 7.55 → 7.82 m, rudder TE 7.95 → 8.15 m, rudder foot z +0.21 to stab z −1.17; **fin 0.734 m², rudder 0.504 m²** | side view row scans |
| tailplane height | z **−1.20 m** (on the fin top) | side view |
| wheels | main **3.022 m** from the nose (Ø 0.42 drawn = the 6.00-6), nose **0.858 m** (Ø 0.31 ≈ 260×85), tail **7.335 m** (Ø 0.21 = 210×65); main and nose bottoms level | side view; the diameters match the published tyres, which checks the scale |
| datum | wing-root LE **2.317 m** aft of the nose | plan view, stretched |
| fuselage | widths and depths at 22 stations (`PROFILE` in the builder): max 0.73 m wide × 1.04 m deep | plan widths less one line width, side depths keel to canopy |

**Frame.** Body origin = the [FM]/[TCDS] datum (wing-root LE) on the root chord line, +x forward, +y right, +z down.
A station s mm aft of the datum is body x = −s/1000, so the published arms go in unconverted, and the own model
(Codex lane), whose origin is the same point, needs no offset.

Tail volumes: V_h = S_h·l_h/(S·c̄) = 2.155 × 5.00 / (17.8 × 1.058) = **0.57**; V_v ≈ 1.24 × 5.4 / (17.8 × 17.5) = **0.021**.

## 2. Sections — XFOIL 6.99, run here

XFOIL is not in Homebrew; it was built from the MIT source (`xfoil6.99.tgz`) with Homebrew gfortran 16, its X11
plot driver stubbed (no plotting is used), and checked against NACA 0012 at Re 10⁶, α 4° (CL 0.428, CD 0.0073 —
the known values). Settings: 200 panels, free transition, **Ncrit 9**, Mach 0, α swept out from 0 in 0.25° steps.

**Wing: Eppler E603** ([UIUC]: root and tip of the Twin II), 19.8 % thick.

| Re | a₀ /rad | α₀ | cl_max (at α) | cd_min | cm₀ |
|---|---|---|---|---|---|
| 0.6 M | 6.645 | −3.70° | 1.406 (17.5°) | 0.00876 | −0.082 |
| 1.0 M | 6.714 | −3.80° | 1.454 (19.2°) | 0.00709 | −0.082 |
| 1.5 M | 6.769 | −3.83° | 1.503 (20.0°) | 0.00606 | −0.083 |
| 2.0 M | 6.802 | −3.86° | 1.538 (20.0°) | 0.00551 | −0.083 |
| 2.5 M | 6.818 | −3.90° | 1.563 (20.0°) | 0.00511 | −0.084 |

(0.8 and 1.25 M are in `data/grob103_xfoil.json` too.)

**Tail: Wortmann FX 71-L-150/30, ASSUMED** — the Twin II's tail section is not published; this is the flapped
15 % tail section of its period. Symmetric: a₀ 6.51/rad, α₀ 0, cl_max 1.17 at Re 0.8 M.

### From section to wing

- **Lift-curve slope**: Glauert's monoplane equation (41 odd terms) on the measured planform with its washout:
  a_3D = **5.977/rad = 0.883 × a₀**. Each wing table is XFOIL's, with α stretched about the zero-lift angle by
  1/0.883 (the whole wing's downwash), XFOIL's profile cd and cm unchanged.
- **Induced drag** is the sim's own per-strip CL²/(π·AR·e), with **e = 0.984** — the lifting line's span efficiency
  of this planform (0.983–0.984 over CL 0.54–0.96). It is the planform's, not an Oswald factor: the lift-dependent
  PROFILE drag is already in XFOIL's polar.
- **Reynolds number**: the seven E603 tables are one family (`AirfoilTableData.ReynoldsFamily`, new in
  `AeroModel.SampleAtReynolds`). Each strip flies the family at its own Re = V·c/ν, interpolated in log Re, so
  profile drag falls with speed and the short tip chords have their own drag and stall. Held at the ends (0.6 and
  2.5 M); ν from μ = 1.789×10⁻⁵ Pa·s and the local density.
- **Post-stall**: blended over 12° into a flat plate, CN = CD90·sin α with CD90 = 1.20 (Hoerner, AR 10–20), and the
  fleet's reversed-flow treatment (`reversed_flow.py`).
- The lifting line puts the first section to reach cl_max at y = 1.17 m, with local cl 1.062 × CL: the wing's
  CL_max is below the section's. Strip theory loads every strip at the wing's α, so the sim does not see this
  (see Validation, stall).
- Tail: the tailplane takes the 2-D table and `AeroModel.TailSections` rescales it to its finite span itself
  (DATCOM/Helmbold). The fin is not rescaled by the code yet, so its table is pre-scaled the same way: effective AR
  1.38²/1.24 × 1.55 (end plate) = **2.38**, slope **2.97/rad = 0.456 × a₀**.

### Rigging (unpublished; design rules, not fits)

- **Washout 1.5° linear, root to tip — ASSUMED** (the middle of the 1–2° glass two-seaters of the period carry).
- **Wing incidence 1.89°**: the wing's own α at CL 0.60 (580 kg at the published best-glide speed), so the fuselage
  meets the air nose-on at best glide, which is what a sailplane's rigging is set for.
- **Stabiliser incidence −1.53°**: the elevator streamlined at the mid-envelope CG (360 mm) at best glide (580 kg,
  105 km/h), so the spring trim's travel is shared out either side. Solved in the sim (`GROB_RIG=1 Grob103Rigging`):
  at 0° the elevator trims at −2.31°; ×0.53 (= 0.85·τ(0.259), the elevator's section effectiveness) → −1.22°, which
  re-trims at −0.59° (the downwash moves with it); one more step → −1.53°, residual −0.16°.

## 3. Parasite drag — build-up, each term stated

| term | drag area m² | arithmetic |
|---|---|---|
| fuselage skin friction | 0.0311 | S_wet 11.79 m² (elliptical sections through the 22 measured stations, less the wing and fin footprints); Re_L = 29.2 × 8.18 / 1.46×10⁻⁵ = 1.63×10⁷; Prandtl–Schlichting C_F 0.00278 turbulent, Schlichting mixed-flow correction for transition at the canopy's front frame (1.0 m) → **0.00241**; form factor (Raymer 12.31) at fineness 8.18/0.867 = 9.43 → **1.095** |
| wing–fuselage junction | 0.0095 | Hoerner 8-11, per side ΔC_D·t² = 0.75 t/c − 0.0003/(t/c)², t/c 0.198, t = 0.259 m; **halved** for the moulded root fairing |
| fin–tailplane and fin–fuselage junctions | 0.0026 | same formula, t/c 0.15, no fillets drawn |
| wheels | 0.0115 | Hoerner 13: a wheel half in a well, C_D ≈ 0.25 on the exposed frontal area — main 0.15 × 0.21, nose 0.085 × 0.10, tail 0.065 × 0.09 m |
| canopy frames, vents, hooks, pitot, gaps | 0.0016 | Thomas's ~5 % of the fuselage term |
| **cd0Area** | **0.0563** | (wing and tail profile drag are the strips' own XFOIL tables) |

## 4. Mass, CG and inertia — component build-up

| component | kg | where | why |
|---|---|---|---|
| wings | 180 (90 a side) | spanwise ∝ c², chordwise at 0.40 c | [FM]: max non-lifting parts 400 kg is the wing-root bending design point n·(W − W_wing) at MTOM, so W_wing = 580 − 400 |
| tailplane + elevators | 10.8 | tail MAC, z −1.20 | 5.0 kg/m² of GFRP sandwich planform |
| fin + rudder | 6.8 | fin centroid | 5.5 kg/m² |
| main wheel, brake, axle, mount | 12 | main wheel | |
| nose wheel / tail wheel | 3 / 2 | wheels | |
| canopies | 9 + 9 | each canopy | |
| panels + instruments | 6 + 5 | each panel | |
| seats, harnesses, sticks, pedals | 8 + 7 | each seat | |
| wing-root fittings + main frame | 15 | x −0.35 | |
| control runs, airbrake and trim linkage | 6 | 3.6 m from the nose | |
| fuselage shell | 100.4 | over the wetted area, see below | the rest of the published 380 kg |

**The empty CG is published** — [FM] p.31: 720–757 mm aft of the datum at 380 kg. Spreading the shell **uniformly**
over its wetted area puts it at **668 mm, 52 mm forward of the range**, and then a legal 2 × 100 kg crew would sit
forward of the 260 mm limit — so uniform is wrong (protocol 1c: if the CG misses, the positions are wrong). A boom
carries the whole tail's bending and torsion in a small perimeter, so its laminate is heavier per m² than the
cockpit's. The shell's areal density is modelled as rising linearly along the fuselage and its slope **solved** for
the middle of the published range (738 mm): **k = 0.85, tail-end laminate 1.78 × the nose's** — a believable ratio.

Checks that are NOT inputs:

| loading | mass | CG mm aft | %MAC | |
|---|---|---|---|---|
| empty | 380 | 738 | — | [FM] p.31: 720–757 ✓ |
| 70 kg solo (the minimum front-seat load) | 450 | 445 | 42.0 | inside the 460 mm aft limit ✓ — and 55 kg solo is aft of it, which is what the placard is for |
| **90 kg solo — the default** | **470** | **377** | **35.6** | the [FM]'s "single seater" stall-speed weight |
| 110 kg solo | 490 | 315 | 29.7 | inside ✓ |
| 2 × 100 kg | 580 | 292 | 27.6 | inside ✓ |
| 2 × 110 kg | 600 | 264 | 25.0 | inside the CG range, but 20 kg over MTOM |

Seat arms ([FM] V.2, [TCDS-FAA]): front 1150 mm ahead of the datum, rear 40 mm behind; pilots at z +0.25/+0.24
(hips ~0.2 m above the keel). The 5.6 kg front trim ballast ([FM] II.8) is a baggage station at the front seat.

Inertia, default load: **Ixx 3116, Iyy 1198, Izz 4220, Ixz 114 kg·m²**; radii of gyration R̄x 0.294, R̄y 0.390,
R̄z 0.467 (the protocol's bands are for powered light aircraft; a sailplane's R̄z runs high because the wing mass is
far out). The wing is the roll inertia: 180 kg spread ∝ c² gives 3,056 of the 3,116.

## 5. Controls

[TCDS-FAA] gives travel as a distance at a radius. Read as the **chord of the arc**, 2·asin(d/2r):

| surface | published | angle | sim |
|---|---|---|---|
| aileron | 90 up / 50 down mm at 208 mm (inboard end) | **25.0° / 13.8°** | ±19.4° (the mean: the aileron axis is symmetric; GAP) |
| elevator | 3.82 in up / 2.99 in down at 9.45 in | **23.3° / 18.2°** | `maxDeflNegRad` 23.3° up, `maxDeflRad` 18.2° down |
| rudder | ±233 mm at 450 mm | **±30.0°** | ±30.0° |

The chord-of-arc reading is the one that gives the round numbers a designer specifies (25.0° and 30.0°); asin(d/r)
gives 25.6° and 31.2°. Control power from the measured chord ratios through τ(E) (docs/CONTROL-SURFACES.md):
aileron E 0.258 → τ 0.618 → fixed-strip gain 0.360; elevator E 0.259 → 0.360; rudder E 0.407 → 0.393. Trim: spring
([TCDS-EASA] Note 8). Wheel brake on the end of the airbrake lever ([FM] IV.1 "Airbrake lever and wheelbrake"; IV.9
"strong effect of the wheel-brake" at full extension): `wheelBrakeFromSpoiler` 0.9, as on the 2-33.

## 6. Airbrakes — a physical model on the strips (new)

The sim's spoiler was one flat-plate area hung on the aeroplane, drag only. `SpoilerConfig.Physical`
(`dragOnly: false` + plate geometry) flies the brakes on the wing strips they sit on:

- **Lift**: a plate at x_b = 0.423 c separates the flow behind it; the upper surface aft of it sits at the wake's
  base pressure, so the section keeps the circulation of the chord **ahead** of the plate: Cl × x_b, i.e. 58 % of
  the lift of those strips gone at full extension, in proportion at part extension. The lost lift also takes its
  own induced drag with it; the rest of the wing flies at a higher α to make it up and pays that instead.
- **Drag**: a flat plate normal to the flow, C_D **1.2** on its own frontal area (Hoerner, plate of aspect ratio
  5–10: span 1.44 m over twice the 0.17 m height, the wing being its reflection plane), in the local dynamic
  pressure over the upper surface. **Ue/V = 1.42** from XFOIL's boundary-layer dump at x/c 0.428 (1.351 at CL 0.21,
  1.422 at CL 0.57, 1.489 at CL 0.92, 1.554 at CL 1.25): the middle of the dive-to-approach range, so the plate sees
  2.02 q.
- Brake span and position from the three-view (2.36–3.80 m, 0.423 c); height 170 mm [TCDS-FAA]; **one blade,
  upper surface — the blade count is not published (GAP)**.

## 7. Limits (new optional fields in `LimitsConfig`, wired into `StructuralState`)

| | value | source |
|---|---|---|
| category flown | **acrobatic** (OTM 315-66 airframe) | [TCDS-EASA] category U and A; [OTM] |
| acrobatic | **+6.5 / −4.0 to VA 180 km/h, +5.3 / −3.0 at VNE**, straight line between | [BGA] snippet (no mass given); VA 180 [TCDS-EASA]/[OTM] |
| utility (`categories[utility]`) | +5.3 / −2.65 at VA 170, +4.0 / −1.5 at VNE | [FM] II.5 |
| brakes out | −1.5 to +3.5 | [CS-22] 22.345 (the manual quotes brakes-closed only) |
| mass | full limits to 400 kg of non-lifting parts; above it × 400/(m − 180) | [FM] II.6; wing-root bending is n × that weight |
| VNE | 250 km/h to 6500 ft, 237 (10 000), 225 (13 000), 213 (16 500), **201 (19 000, extrapolated)** | [FM] II.4 (the 19 000 ft column is cut off in the scan after "20") |
| VFE | 250 (= VNE) | [OTM] placard |
| VT / VW | 170 / 120 km/h | [TCDS-EASA] (not wired: the sim has no tow-speed limit) |
| weak link | 6300 N | [TCDS-EASA] maximum 630 daN (845 with SB 315-19) |

The groan and the wing failure stay on the VA limits × the owner's 1.5 / 2.0 factors (bending strength does not
depend on speed); `StructuralState.LimitAt(ias, brakes)` / `OverLimitAt` give the speed- and brake-dependent
envelope for grading exceedances. **Approved manoeuvres** [FM] IV.8: loop 180 km/h (~3 g), stall turn 180, spin 80,
chandelle 170, lazy eight 140. **[MSB] 315-65 restricts many Twin II Acros to "simple aerobatics"** (loop, turn,
lazy eight, chandelle, spin) and invalidates the acrobatics handbook until [OTM] 315-66 is embodied.

## Validation (predicted vs published — nothing fitted)

Sea level ISA, the trim solver with the elevator balancing pitch; loadings through the load sheet.

| quantity | published | sim | error | test band | |
|---|---|---|---|---|---|
| best L/D, 580 kg | 37 at 105 km/h | **33.1 at 96.3** | −10.5 % / −8.3 % | ±12 % / +5 −15 % | pass, low |
| min sink, 580 kg | 0.70 m/s at 85 km/h | **0.706 at 75.6** | +0.9 % / −11 % | ±12 % | pass |
| best L/D, 450 kg | 36.5 at 95 km/h | **33.5 at 84.6** | −8.2 % / −11 % | ±12 % / +5 −15 % | pass, low |
| min sink, 450 kg | 0.64 m/s at 80 km/h | **0.604 at 64.8** | −5.6 % / −19 % | ±12 % | pass (speed not banded) |
| V(best L/D) 580/450 | √(580/450) = 1.135 | 1.138 | +0.3 % | ±4 % | pass |
| stall, 470 kg clean | 66 km/h | **59.9** | −9.2 % | +5 −15 % | pass, low |
| stall, 580 kg clean | 75 km/h | **68.9** | −8.1 % | +5 −15 % | pass, low |
| stall, 470 kg brakes | 75 km/h | **63.0** | −16 % | +5 −20 % | pass, LOW |
| stall, 580 kg brakes | 85 km/h | **72.2** | −15 % | +5 −20 % | pass, LOW |
| 45° dive, brakes, 580 kg | under VNE 250 km/h [FM] IV.7 | **248** | — | < 250 | **pass** |
| brakes, 1.3 VS0, 580 kg | 1:7 or steeper [CS-22] 22.75 | **1:7.98** | — | (printed) | **NOT MET** |
| brakes, 95 km/h, 470 kg | "sufficiently powerful for steep approaches" | 1:7.04 (clean 1:32.7) | — | | |
| 45–45 reversal, 1.4 Vs, 470 kg | ≤ b/3 = 5.83 s [CS-22] 22.147 | **4.70 s** | — | 3.6–5.83 | pass |
| 45–45 reversal, 1.4 Vs, 580 kg | ≤ 5.83 s | **4.24 s** | — | 3.6–5.83 | pass |
| spin, aft CG (70 kg solo) | "aft C.G. only … one turn every 3 s … about 60 m per turn" | **3.3 s/turn, 55 m/turn** | | 2–4 s, 35–85 m | pass |
| spin, forward CG (110 kg solo) | does not spin | 0.5 turns in 12 s, no spin | | < 1 turn | pass |
| spin recovery | "standard recovery" | rotation 1.17 → 0.32 rad/s after 6 s | | slows ≥ ⅓ | **GAP**: not a prompt stop |

Roll floor (strip-theory bound): Cl_δa = 2·a·τ_eff·∫c·y dy/(S·b) = 2 × 5.98 × 0.525 × 18.5 / (17.8 × 17.5) = 0.37/rad;
Cl_p = −(a/12)(1+3λ)/(1+λ) = −0.79 (λ 0.42); pb/2V = 0.37 × 0.34 / 0.79 = 0.16 → 25°/s at 23.3 m/s, 90° in 3.6 s.

### What the misses say

- **Best L/D 8–10 % low, and L/D at almost the same CL for both masses.** The drag build-up's unpublished terms
  (fuselage transition, the junction, wheels: ±25 % on cd0Area) and XFOIL at Ncrit 9 against a real wing (±8 %)
  span the gap; the derivation did not choose the favourable end of any of them.
- **Stall 8–9 % slow, min-sink speed 11–19 % slow, best-glide speed 8–11 % slow** — all one thing: the model reaches
  a trimmed CL_max of 1.42–1.53 (580 / 470 kg) where the manual's stall speeds imply 1.20–1.26. XFOIL's cl_max for a 20 % laminar
  section is optimistic, strip theory loads every strip at the wing's α (the lifting line has the first section at
  1.06 × CL), and the thick root's junction separates early on the real wing.
- **Brakes**: the plates' DRAG is right (the 45° dive is held under VNE as the manual says); their LIFT loss is too
  small (stall rises 5 % against 13 %; CS 22.75's 1:7 not met). The chord-ahead-of-the-plate lift model is the
  suspect; a measured spoiler ΔCl(h/c, x/c) table would replace it.
- **Spin**: entry, rate and height per turn match the manual; the developed mode is flat (α 60–100°) and slow to
  stop under the manual's recovery. That is the fleet's open spin-model item (docs/CALIBRATION-2-33.md,
  SPIN-ROTARY-DATA.md), not a Twin II number to tune.

## Gaps

- **Not published, measured off the drawing**: planform, ailerons, brake box, tail and fin geometry, wheel stations,
  datum position — one undimensioned 1981 three-view, ±1–2 %.
- **Not published, ASSUMED**: tail section (FX 71-L-150/30), washout (1.5°), airbrake blade count (one), tail and
  fin areal masses, every concentrated mass in §4.
- **Design rule, not published**: wing incidence (fuselage nose-on at best glide), stab incidence (elevator
  streamlined at mid-CG best glide), shell density slope (solved for the published empty-CG range).
- **Enlarged tailplane**: [TCDS-EASA] Note 8 fits an enlarged tailplane from s/n 33879 — the airframes VA 180 is
  quoted for — and [TCDS-FAA]'s 9.45 in elevator radius exceeds the drawn elevator's ~0.20 m chord. The drawn
  (original) tail is modelled; the enlarged one's size is not published.
- **Aerobatic g limits**: +6.5/−4.0 → +5.3/−3.0 is from the BGA card's snippet; the Feb 1984 *Flight handbook for
  acrobatics* is not online, and no aerobatic mass limit was found (none in the TCDS: 580 kg for both categories).
- **Differential ailerons** fly as their ±19.4° mean: the rolling moment is right, the differential's adverse-yaw
  benefit is lost (`ControlAxisConfig` has one symmetric aileron travel).
- **Airbrake lift loss** (above). **Spin recovery** (above). **CL_max** (above).
- **VNE above 16 500 ft**: the 19 000 ft value (201 km/h) is an extrapolation; the scan is cut off.
- **Wet / iced wing**: [FM] III.3 "raises the stall speed by about 6 knots" — not modelled.
- **Flutter**: not modelled (the VNE altitude table is the manual's protection against it; it is wired for grading).
- **Viscosity** is the sea-level value at every height (≈ −2 % per 1000 m on ν·ρ); density carries the altitude.
- **Winch hook / CG hook**: only the nose (aerotow) hook is modelled; VW is not wired.
- **Eye points**: the own model's proposals (Codex) at the seat arms, z −0.40/−0.45 — unapproved; the [FM] arms are
  the pilots' mass stations and a reclined pilot's eye is normally 0.3–0.4 m aft of that.
