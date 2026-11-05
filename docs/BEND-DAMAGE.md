# Bends before it breaks (Ty 2026-11-05)

> "The wing hit relatively slowly and immediately snapped off … the airplanes are all like porcelain dolls … make them
> more resilient in the sense that they stay together but still use realistic damage thresholds for bending surfaces and
> model the bent surfaces reduced strength and aerodynamic impacts if it continues flying."

## What it was

- A wing panel (outboard of its structural station) came off the **first step** its tip's contact moment passed the
  ultimate. The contact is a stiff spring-damper, so a 5 m/s tip arrival on the J-3 rang to 9.5 kN·m for a step against
  an ultimate of 6.2 kN·m: gone.
- A strike between limit and ultimate left a fixed 5° set; a bent panel then snapped at **2 × its 1 g moment**, a fifth
  of its ultimate (Ty's 2026-11-03 rule, replaced by this lane).
- Tail surfaces, the wing's mid-span point, nacelles and gear legs came off at the first arrival over their break speed.

## What it is now

**Wing panels: two plastic hinges at the station** (`src/FlyingGame.Core/PlasticHinge.cs`, `WingStructure`).

- Bending (tip up/down): yield = the limit moment (14 CFR 23.305), peak = the ultimate. Torsion (nose up/down about the
  flexural axis at 0.35 c): fabric over two spars `T_u = M_u·b/(2L)` (differential spar bending, b = 0.45 c); stressed
  skin `T_u = 2·F_su/F_tu·M_u = 1.28 M_u` (2024-T3).
- Backbone in accumulated plastic rotation κ, from `θ_p ≈ 2·ε_p` (plastic hinge length ≈ section depth):

  | construction | peak at | falls to | by | tears at | fleet |
  |---|---|---|---|---|---|
  | wood / fabric (spruce) | 0.007 rad | 35 % | 0.05 | 0.40 (fabric 20 %) | J-3, Pitts, Great Lakes, Stearman, Decathlon, Cassutt, Jenny, Gee Bee |
  | aluminium / fabric | 0.02 | 50 % | 0.10 | 0.36 (2024 18 %) | PA-18 ×3, PA-25 |
  | aluminium skin | 0.02 | 50 % | 0.10 | 0.36 | C172, PA-28, Beaver, 2-33, twins, jets |
  | composite | 0.005 | 15 % | 0.02 | 0.10 | SR22, Extra 300, composite gliders |

  New config key `wingSpar` (+ `wingSparSource`) on the 8 types that differ from what the fuselage implies.
- **The ground cannot push a tip harder than the hinges carry** (`AirframeContact.CapPanels`): the panel's points are
  scaled together to the cap. What they asked for, with Σ(k + c/dt)·r² of the push, goes to `PlasticHinge.Load`, which
  turns the hinge until the relieved push meets the backbone. The strike's energy becomes plastic work, not a moment spike.
- Every yield adds to κ; past the peak each new yield leaves it weaker. Off only at the rupture rotation (`PanelBroken`).
- The set is geometry (`WingPanel.Deform`: twist then bend): hard points, strip load points, dihedral (+bend), incidence
  (+twist × span fraction, `IncidenceDeltaRad`) and a buckled-skin damage `D = (κ − peak)/(rupture − peak)` through the
  branch-strike surface law (1 − 0.4 D lift, +0.06 D profile drag; the worse of the two damages counts).
- In flight a damaged panel carries `n_aero·M1` against what is left: a J-3 panel bent 5° holds to 3.5 g.
- The drawn mesh: `AirframeVisual.ShowPanelBend` puts every vertex where `Deform` puts it.

**Other members crush before they break** (`MemberMaterials.CrushCapacityM`, the fuselage's δ₀·(v²/v_b² − 1) rule):
tail surfaces / rudder steel 0.20 m, aluminium 0.15, wood 0.08, composite 0.05; mid-span by wing construction; nacelles
and gear legs 0.15 m. Untagged airframes keep the old first-overload rule. Event `MemberBent`.

## Measured (BendDamageTests, ContactPatchTests)

| J-3, right tip | result |
|---|---|
| 20 m/s, tip down at 5 m/s | twist −2.4°, bend 0.4°, cracked in torsion, **stays on** (old: snapped) |
| 25 m/s, tip 10 m/s, sink 3 | bend 8.8°, twist 3.4°, stays on; in flight 1.1° of aileron to hold level, +18 % drag |
| 30 m/s, tip 15 m/s, sink 6 | torsion tears after 712 J: **separates** |
| 35 m/s, 25° bank, sink 6 | folds 20°, then separates |
| Gear drop 5 m/s / 7 m/s | legs bend and stay / legs go |

Pitts lower panel, 35° bank at 20 m/s: sink 0.8 → set < 0.4°; sink 3 → folds ~21° and hangs on; sink 6 → off.

## Not done

- A bent gear leg absorbs and stays, but its geometry does not change (the aeroplane does not sit lower on it).
- No fatigue below the backbone: a cracked member under loads it can carry does not get weaker.
- Ailerons on a bent panel are not redrawn bent (placeholder, as before).
