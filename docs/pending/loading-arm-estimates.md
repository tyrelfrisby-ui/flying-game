# Loading arm estimates

Generated 2026-11-01 by Codex lane `lane/codex-loading-arms` for Claude. Machine copy: `docs/pending/loading-arm-estimates.json`.

## How to read this

- **Frame:** game body frame, metres: +x forward, +y right, +z down, same frame as `surfaces[].strips[].pos` and `loading.stations[].pos`.
- **Status:**
  - **CONFIRMED** means the x arm comes from a published source (TCDS, POH/AFM, or a real W&B sheet), cited per row. It is converted to the game frame through the anchor listed for the aircraft. y and z still come from the mannequin or the heuristics, because published arms are longitudinal only.
  - **ESTIMATED** means our own value: the CG of a seated 1.778 m de Leva mannequin for seats, or model geometry heuristics for tanks and baggage.
  - **NONE** means the item does not exist (glider fuel, Extra baggage, and so on).
- **Primary** is the value to wire into config. Where a row is CONFIRMED, our estimate sits beside it.
- **Converting a published arm to game x:**
  - Arms positive aft: `game_x = datum_x − arm_in × 0.0254`.
  - Beaver (TCDS A-806 uses + forward): `game_x = datum_x + arm_in × 0.0254`.
- **Δ m** = estimate x − published x in game metres. **+ means our estimate is forward** of the published arm.
- **Δ in** = estimate arm − published arm in the source sign. For an aft-positive datum, + means our estimate is aft.
- **Anchors:** datum_x is located from model geometry. The usual anchor is the inner fixed wing strip LE = `pos.x + 0.25·chord`, or MacGeom LEMAC. The anchor error moves every arm of that aircraft by the same amount. Seat-to-seat spacing (below) is independent of the anchor.
- **Heuristics for ESTIMATED fuel and baggage:**
  - Wing tank: 30% chord of the inboard fixed strip near 30% semispan, at ±y and the strip's z.
  - Header or nose tank: front seat CG +0.55 to 0.75 m.
  - Baggage: rearmost seat CG −0.55 to 0.6 m.
  - Confidence for these is LOW.

## Big discrepancies (|Δ| ≥ 0.15 m on a CONFIRMED seat, or a suspect anchor)

| Aircraft | Item | Published game x | Mannequin x | Δ m (+ = est fwd) | Δ in | Read |
|---|---|---|---|---|---|---|
| c172-like | Pilot (front L) | 0.026 | -0.206 | -0.232 | +9.1 | The mannequin's 46 in is still inside the POH's adjustable 34–46 in range (aft seat). The POH loading-graph arm of 37 is the right primary. |
| c172-like | Front passenger (front R) | 0.026 | -0.206 | -0.232 | +9.1 |  |
| j3-cub-like | Front seat | 0.179 | -0.079 | -0.257 | +10.1 | About 10 in aft on both seats. The J-3 placement is a known compromise. |
| j3-cub-like | Rear seat (solo) | -0.507 | -0.797 | -0.289 | +11.4 |  |
| j3-cub-floats-like | Front seat | 0.179 | -0.079 | -0.257 | +10.1 | same as J-3 |
| j3-cub-floats-like | Rear seat (solo) | -0.507 | -0.797 | -0.289 | +11.4 |  |
| pa18-cub-like | Rear seat | -0.532 | -0.739 | -0.207 | +8.1 | Rear seat 8 in aft. |
| pa18-bush-like | Rear seat | -0.532 | -0.739 | -0.207 | +8.1 | Rear seat 8 in aft. |
| pa18-floats-like | Rear seat | -0.532 | -0.739 | -0.207 | +8.1 | Rear seat 8 in aft. |
| decathlon-8kcab-like | Front seat | 0.090 | 0.501 | +0.411 | -16.2 | Mannequin is 16 in forward of the TCDS arms. The game Decathlon cockpit is forward of its wing. |
| decathlon-8kcab-like | Rear seat (solo) | -0.672 | -0.269 | +0.403 | -15.9 |  |
| extra-300-like | Rear seat (PIC) | -1.280 | -1.444 | -0.164 | +6.4 | The firewall anchor is soft (±0.05 m), but the front-to-rear spacing is also short (0.92 vs 1.09 m). |
| extra-300-like | Front seat | -0.190 | -0.520 | -0.331 | +13.0 |  |
| stearman-pt17-like | Rear seat (PIC) | -1.441 | -1.254 | +0.187 | -7.4 | Rear seat 7 in forward. |
| dhc2-beaver-like | Front seats L/R | 0.102 | 0.993 | +0.892 | +35.1 | Game cabin sits about 0.9 m forward of its wing: the mannequin is 0.57 m ahead of the wing LE, but the TCDS puts the front seats 12.5 in aft of the LE. The MAC matches the TCDS (62.5 in vs game 1.591 m), so the wing is not the problem. |
| dhc2-beaver-floats-like | Front seats L/R | 0.102 | 0.993 | +0.892 | +35.1 | same as Beaver |
| pa28-archer-like | Pilot (front L) | 0.417 | 0.633 | +0.216 | -8.5 | The Archer anchor is flagged as suspect in the repo. The L/R mannequins differ by 0.08 m. |
| glasair3-like | Pilot / pax (side by side) | -0.344 | 0.393 | +0.738 | -29.0 | Crew is 29 in forward of the CAFE arm. Two anchors agree (LEMAC and main gear), so the game cockpit or wing placement is off. |

## Seat spacing check (anchor-independent): front CG minus rear CG

| Aircraft | Published Δ (in) | Published Δ (m) | Mannequin Δ (m) | Diff (m) |
|---|---|---|---|---|
| j3-cub-like | 27.0 | 0.686 | 0.718 | +0.032 |
| j3-cub-floats-like | 27.0 | 0.686 | 0.718 | +0.032 |
| pa18-cub-like | 26.0 | 0.660 | 0.774 | +0.113 |
| pa18-bush-like | 26.0 | 0.660 | 0.774 | +0.113 |
| pa18-floats-like | 26.0 | 0.660 | 0.774 | +0.113 |
| decathlon-8kcab-like | 30.0 | 0.762 | 0.770 | +0.008 |
| glider-2-33-like | 31.0 | 0.787 | 0.834 | +0.046 |
| pitts-s2b-like | 31.3 | 0.796 | 0.788 | -0.008 |
| extra-300-like | 42.9 | 1.090 | 0.923 | -0.167 |
| great-lakes-like | 34.0 | 0.864 | 0.791 | -0.073 |
| stearman-pt17-like | 36.0 | 0.914 | 0.717 | -0.197 |

## Per-aircraft tables

### 172-like Trainer (`c172-like.json`)

- Variants: c172-stock (in game), c172
- Datum: C172N POH datum: front face of firewall (arms in aft). Arms +aft. **datum game x = 0.9658**
- Anchor (HIGH): repo docs/WEIGHT-AND-BALANCE.md: config stations are POH arms (37 in at x 0.026); cross-check game LEMAC 0.3605 + 25.9 in = 1.018 (2 in)
- Source: Cessna 172N POH Sec 6 loading arrangements / loading graph (seats 37, rear 73, fuel 48, bag A1 95, A2 123); TCDS 3A12

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot (front L) | **CONFIRMED** | [0.026, -0.338, -0.021] | 37 | [-0.206, -0.338, -0.021] | 46.1 | -0.232 | +9.1 | HIGH | [0.026,-0.28,0.05] | C172N POH Sec 6 (front seat arm 37, adjustable 34-46); est: seated 1.778 m de Leva mannequin CG, variant c172-stock/left; alt variant c172: x -0.2239 |
| Front passenger (front R) | **CONFIRMED** | [0.026, 0.332, -0.021] | 37 | [-0.206, 0.332, -0.021] | 46.1 | -0.232 | +9.1 | HIGH | [0.026,0.28,0.05] | C172N POH Sec 6; est: seated 1.778 m de Leva mannequin CG, variant c172-stock/right |
| Rear seats L/R | **CONFIRMED** | [-0.888, 0.280, 0.050] | 73 | — |  |  |  | HIGH | [-0.888,±0.28,0.05] | C172N POH Sec 6; no rear-seat eyepoint/mannequin in the eyelines set; no estimate |
| Fuel L/R wing tanks (std 40 gal) | **CONFIRMED** | [-0.253, 1.420, -0.600] | 48 | [-0.061, 1.420, -0.600] | 40.4 | +0.192 | -7.6 | HIGH | [-0.253,∓2.18,-0.55], 54 kg each | C172N POH Sec 6; est: inboard wing 30% chord at 30% semispan |
| Baggage area 1 | **CONFIRMED** | [-1.447, 0.000, 0.100] | 95 | — |  |  |  | HIGH | [-1.447,0,0.1], 54 kg | C172N POH Sec 6 (area 1, 120 lb, sta 82-108) |
| Baggage area 2 | **CONFIRMED** | [-2.158, 0.000, 0.100] | 123 | — |  |  |  | HIGH | not modeled | C172N POH Sec 6 (area 2, 50 lb, sta 108-142) |

### J-3 Cub-like (85 hp) (`j3-cub-like.json`)

- Variants: j3-cub
- Datum: Wing leading edge (TCDS A-691). Arms +aft. **datum game x = 0.4073**
- Anchor (HIGH): wing LE: repo anchor 0.4073 = config inner strip LE 0.407
- Source: FAA TCDS A-691 (J3C-65: seats +9/+36, baggage 20 lb +49, fuel 12 gal -18), https://openairdocs.org/tcds/A-691
- Notes: floats variant: TCDS J3C-65S uses same seat/fuel/baggage arms

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [0.179, -0.001, -0.071] | 9 | [-0.079, -0.001, -0.071] | 19.1 | -0.257 | +10.1 | HIGH | [0.1787,0,-0.05] | TCDS A-691 J3C-65 'Number of Seats 2 (one at +9 and one at +36)'; est: seated 1.778 m de Leva mannequin CG, variant j3-cub/front |
| Rear seat (solo) | **CONFIRMED** | [-0.507, -0.001, 0.034] | 36 | [-0.797, -0.001, 0.034] | 47.4 | -0.289 | +11.4 | HIGH | [-0.5071,0,-0.05] | TCDS A-691; est: seated 1.778 m de Leva mannequin CG, variant j3-cub/rear |
| Nose tank 12 gal | **CONFIRMED** | [0.865, -0.001, -0.421] | -18 | [0.671, -0.001, -0.421] | -10.4 | -0.193 | +7.6 | HIGH | [0.8644,0,-0.25] 45.42 L | TCDS A-691 'Fuel Capacity 12 gallons (-18)'; est: front seat CG +0.75 m fwd, 0.35 m up (behind firewall above the legs) |
| Baggage | **CONFIRMED** | [-0.837, -0.001, 0.034] | 49 | [-1.397, -0.001, 0.034] | 71.0 | -0.559 | +22.0 | HIGH | [-0.8373,0,0] 9.07 kg | TCDS A-691 'Maximum Baggage 20 lb. (+49)'; est: rear seat CG -0.6 m |

### J-3 Cub-like on Floats (`j3-cub-floats-like.json`)

- Variants: j3-cub-floats
- Datum: Wing leading edge (TCDS A-691). Arms +aft. **datum game x = 0.4073**
- Anchor (HIGH): wing LE: repo anchor 0.4073 = config inner strip LE 0.407
- Source: FAA TCDS A-691 (J3C-65: seats +9/+36, baggage 20 lb +49, fuel 12 gal -18), https://openairdocs.org/tcds/A-691
- Notes: floats variant: TCDS J3C-65S uses same seat/fuel/baggage arms

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [0.179, -0.001, 0.029] | 9 | [-0.079, -0.001, 0.029] | 19.1 | -0.257 | +10.1 | HIGH | [0.1787,0,-0.05] | TCDS A-691 J3C-65 'Number of Seats 2 (one at +9 and one at +36)'; est: seated 1.778 m de Leva mannequin CG, variant j3-cub-floats/front |
| Rear seat (solo) | **CONFIRMED** | [-0.507, -0.001, 0.135] | 36 | [-0.797, -0.001, 0.135] | 47.4 | -0.289 | +11.4 | HIGH | [-0.5071,0,-0.05] | TCDS A-691; est: seated 1.778 m de Leva mannequin CG, variant j3-cub-floats/rear |
| Nose tank 12 gal | **CONFIRMED** | [0.865, -0.001, -0.321] | -18 | [0.671, -0.001, -0.321] | -10.4 | -0.193 | +7.6 | HIGH | none (no loading block tanks) | TCDS A-691 'Fuel Capacity 12 gallons (-18)'; est: front seat CG +0.75 m fwd, 0.35 m up (behind firewall above the legs) |
| Baggage | **CONFIRMED** | [-0.837, -0.001, 0.135] | 49 | [-1.397, -0.001, 0.135] | 71.0 | -0.559 | +22.0 | HIGH | none | TCDS A-691 'Maximum Baggage 20 lb. (+49)'; est: rear seat CG -0.6 m |

### Super Cub-like (`pa18-cub-like.json`)

- Variants: pa18
- Datum: Wing leading edge (TCDS 1A2). Arms +aft. **datum game x = 0.408**
- Anchor (MED): config inner wing strip LE (pos.x+0.25c) 0.408; J-3 identical wing anchors at 0.4073
- Source: FAA TCDS 1A2 PA-18 '150' (seats 2 at +11 and +37; baggage 50 lb +57; fuel 36 gal, 2 wing tanks at +24), https://openairdocs.org/tcds/1A2 , https://www.supercub.org/woe/pa18type.pdf
- Notes: some field W&B sheets use a datum 60 in ahead of the LE; the TCDS datum is the LE

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [0.129, 0.001, -0.179] | 11 | [0.035, 0.001, -0.179] | 14.7 | -0.094 | +3.7 | MED | none | TCDS 1A2 'Number of Seats 2 at (+11) and (+37)'; est: seated 1.778 m de Leva mannequin CG, variant pa18/front |
| Rear seat | **CONFIRMED** | [-0.532, 0.001, -0.178] | 37 | [-0.739, 0.001, -0.178] | 45.1 | -0.207 | +8.1 | MED | none | TCDS 1A2; est: seated 1.778 m de Leva mannequin CG, variant pa18/rear |
| Wing tank L (18 gal) | **CONFIRMED** | [-0.202, -1.550, -0.750] | 24 | [-0.057, -1.550, -0.750] | 18.3 | +0.145 | -5.7 | MED | none | TCDS 1A2 '36 gallons at (2 wing tanks at +24)'; est: inboard wing 30% chord |
| Wing tank R (18 gal) | **CONFIRMED** | [-0.202, 1.550, -0.750] | 24 | [-0.057, 1.550, -0.750] | 18.3 | +0.145 | -5.7 | MED | none | TCDS 1A2; est: inboard wing 30% chord |
| Baggage | **CONFIRMED** | [-1.040, 0.001, -0.178] | 57 | [-1.339, 0.001, -0.178] | 68.8 | -0.299 | +11.8 | MED | none | TCDS 1A2 'Maximum Baggage 50 lb. at (+57)'; est: rear seat CG -0.6 m |

### Super Cub-like, Bushwheels (`pa18-bush-like.json`)

- Variants: pa18-bush
- Datum: Wing leading edge (TCDS 1A2). Arms +aft. **datum game x = 0.408**
- Anchor (MED): config inner wing strip LE (pos.x+0.25c) 0.408; J-3 identical wing anchors at 0.4073
- Source: FAA TCDS 1A2 PA-18 '150' (seats 2 at +11 and +37; baggage 50 lb +57; fuel 36 gal, 2 wing tanks at +24), https://openairdocs.org/tcds/1A2 , https://www.supercub.org/woe/pa18type.pdf
- Notes: some field W&B sheets use a datum 60 in ahead of the LE; the TCDS datum is the LE

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [0.129, 0.001, -0.156] | 11 | [0.035, 0.001, -0.156] | 14.7 | -0.094 | +3.7 | MED | none | TCDS 1A2 'Number of Seats 2 at (+11) and (+37)'; est: seated 1.778 m de Leva mannequin CG, variant pa18-bush/front |
| Rear seat | **CONFIRMED** | [-0.532, 0.001, -0.155] | 37 | [-0.739, 0.001, -0.155] | 45.1 | -0.207 | +8.1 | MED | none | TCDS 1A2; est: seated 1.778 m de Leva mannequin CG, variant pa18-bush/rear |
| Wing tank L (18 gal) | **CONFIRMED** | [-0.202, -1.550, -0.750] | 24 | [-0.057, -1.550, -0.750] | 18.3 | +0.145 | -5.7 | MED | none | TCDS 1A2 '36 gallons at (2 wing tanks at +24)'; est: inboard wing 30% chord |
| Wing tank R (18 gal) | **CONFIRMED** | [-0.202, 1.550, -0.750] | 24 | [-0.057, 1.550, -0.750] | 18.3 | +0.145 | -5.7 | MED | none | TCDS 1A2; est: inboard wing 30% chord |
| Baggage | **CONFIRMED** | [-1.040, 0.001, -0.155] | 57 | [-1.339, 0.001, -0.155] | 68.8 | -0.299 | +11.8 | MED | none | TCDS 1A2 'Maximum Baggage 50 lb. at (+57)'; est: rear seat CG -0.6 m |

### Super Cub-like on Floats (`pa18-floats-like.json`)

- Variants: pa18-floats
- Datum: Wing leading edge (TCDS 1A2). Arms +aft. **datum game x = 0.408**
- Anchor (MED): config inner wing strip LE (pos.x+0.25c) 0.408; J-3 identical wing anchors at 0.4073
- Source: FAA TCDS 1A2 PA-18 '150' (seats 2 at +11 and +37; baggage 50 lb +57; fuel 36 gal, 2 wing tanks at +24), https://openairdocs.org/tcds/1A2 , https://www.supercub.org/woe/pa18type.pdf
- Notes: some field W&B sheets use a datum 60 in ahead of the LE; the TCDS datum is the LE

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [0.129, 0.001, -0.156] | 11 | [0.035, 0.001, -0.156] | 14.7 | -0.094 | +3.7 | MED | none | TCDS 1A2 'Number of Seats 2 at (+11) and (+37)'; est: seated 1.778 m de Leva mannequin CG, variant pa18-floats/front |
| Rear seat | **CONFIRMED** | [-0.532, 0.001, -0.155] | 37 | [-0.739, 0.001, -0.155] | 45.1 | -0.207 | +8.1 | MED | none | TCDS 1A2; est: seated 1.778 m de Leva mannequin CG, variant pa18-floats/rear |
| Wing tank L (18 gal) | **CONFIRMED** | [-0.202, -1.550, -0.750] | 24 | [-0.057, -1.550, -0.750] | 18.3 | +0.145 | -5.7 | MED | none | TCDS 1A2 '36 gallons at (2 wing tanks at +24)'; est: inboard wing 30% chord |
| Wing tank R (18 gal) | **CONFIRMED** | [-0.202, 1.550, -0.750] | 24 | [-0.057, 1.550, -0.750] | 18.3 | +0.145 | -5.7 | MED | none | TCDS 1A2; est: inboard wing 30% chord |
| Baggage | **CONFIRMED** | [-1.040, 0.001, -0.155] | 57 | [-1.339, 0.001, -0.155] | 68.8 | -0.299 | +11.8 | MED | none | TCDS 1A2 'Maximum Baggage 50 lb. at (+57)'; est: rear seat CG -0.6 m |

### Decathlon-like Aerobat (`decathlon-8kcab-like.json`)

- Variants: decathlon-8kcab
- Datum: Wing leading edge (TCDS A21CE, all models). Arms +aft. **datum game x = 0.446**
- Anchor (MED): config inner wing strip LE 0.446
- Source: FAA TCDS A21CE Rev 16, Model 8KCAB, https://openairdocs.org/tcds/A21CE

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [0.090, 0.000, -0.226] | 14 | [0.501, 0.000, -0.226] | -2.2 | +0.411 | -16.2 | MED | none | TCDS A21CE 8KCAB 'No. of Seats 2 (1 at +14, 1 at +44)'; est: seated 1.778 m de Leva mannequin CG, variant decathlon-8kcab/front |
| Rear seat (solo) | **CONFIRMED** | [-0.672, 0.000, -0.226] | 44 | [-0.269, 0.000, -0.226] | 28.1 | +0.403 | -15.9 | MED | none | TCDS A21CE (+45 with Front Adjustable Seat); est: seated 1.778 m de Leva mannequin CG, variant decathlon-8kcab/rear |
| Wing tanks 40 gal (L+R) | **CONFIRMED** | [-0.214, 1.410, -0.750] | 26 | [-0.037, 1.410, -0.750] | 19.0 | +0.177 | -7.0 | MED | none | TCDS A21CE 'Wing tanks: 40.0 gal. (+26)'; est: inboard wing 30% chord (±y) |
| Inverted header tank 1.5 gal | **CONFIRMED** | [0.776, 0.000, -0.526] | -13 | [1.251, 0.000, -0.526] | -31.7 | +0.475 | -18.7 | MED | none | TCDS A21CE 'Inverted header tank: 1.5 gal. (-13)'; est: front seat CG +0.75 m |
| Baggage | **CONFIRMED** | [-1.357, 0.000, -0.226] | 71 | [-0.869, 0.000, -0.226] | 51.8 | +0.489 | -19.2 | MED | none | TCDS A21CE 'Maximum Baggage 100 lbs. (+71) (Normal Category Only)'; est: rear seat CG -0.6 m |

### 2-33-like Trainer Glider (`glider-2-33-like.json`)

- Variants: sgs233, sgs233-talk-flying
- Datum: Wing leading edge at root (= Fuselage Sta 66.3) (TCDS G2EA). Arms +aft. **datum game x = 0.45**
- Anchor (MED): config inner wing strip LE 0.450
- Source: FAA TCDS G2EA Rev 4 (seats 1 at -23, 1 at +8), https://www.soarccsc.com/wp-content/uploads/2016/01/G2EA_Rev-4.pdf ; Schweizer W&B form / N2615H sheet (front pilot FS 43.80, rear FS 74.70 => -22.5/+8.4), http://www.soarccsc.com/wp-content/uploads/2016/01/N2615H-Weight-and-Balance.pdf

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [1.034, 0.000, 0.409] | -23 | [1.077, 0.000, 0.409] | -24.7 | +0.042 | -1.7 | MED | [0.971,0,-0.111] (= eye) | TCDS G2EA (FS 43.80 on Schweizer form = -22.5); est: seated 1.778 m de Leva mannequin CG, variant sgs233/front |
| Rear seat | **CONFIRMED** | [0.247, 0.000, 0.471] | 8 | [0.243, 0.000, 0.471] | 8.2 | -0.004 | +0.2 | MED | [0.164,0,-0.06] (= eye) | TCDS G2EA (FS 74.70 = +8.4); est: seated 1.778 m de Leva mannequin CG, variant sgs233/rear |
| Fuel | **NONE** | — |  | — |  |  |  |  | none | glider; none |
| Baggage | **NONE** | — |  | — |  |  |  |  | none | TCDS G2EA: no baggage compartment (ballast 19.5 lb at Sta 14.75 noted on form); none |

### Pitts S-2-like Aerobat (`pitts-s2b-like.json`)

- Variants: pitts-s2b (rear v4), pitts-s2b-factory-unmarked/n31dg/n1191 (rear v3)
- Datum: 91.81 in forward of lower wing LE (TCDS A8SO, S-2B). Arms +aft. **datum game x = 2.332**
- Anchor (HIGH): lower wing LE at config x 0.000 (repo doc + config strip)
- Source: FAA TCDS A8SO, Model S-2B (seats +105.15/+136.50; fuel 24 gal +81.32 & 5 gal +81.75; baggage 20 lb +157.81), https://openairdocs.org/tcds/A8SO , https://pittss2b.com/a8sopitts.pdf

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [-0.339, 0.000, 0.190] | 105.15 | [-0.390, 0.000, 0.190] | 107.2 | -0.051 | +2.0 | HIGH | [-0.564,0,-0.308] (= eye) | TCDS A8SO S-2B 'Number of Seats 2 (1 @ +105.15) (1 @ +136.50)'; est: seated 1.778 m de Leva mannequin CG, variant pitts-s2b/front |
| Rear seat (PIC) | **CONFIRMED** | [-1.135, -0.002, 0.150] | 136.5 | [-1.178, -0.002, 0.150] | 138.2 | -0.043 | +1.7 | HIGH | [-1.325,0,-0.3] (= eye) | TCDS A8SO S-2B; est: seated 1.778 m de Leva mannequin CG, variant pitts-s2b/rear; livery variants rear v3 CG x -1.1481 |
| Main fuselage tank 24 gal | **CONFIRMED** | [0.267, 0.000, -0.060] | 81.32 | [0.310, 0.000, -0.060] | 79.6 | +0.044 | -1.7 | HIGH | none | TCDS A8SO S-2B '24 gal. @ +81.32'; est: front seat CG +0.7 m (tank between firewall and front cockpit) |
| Wing/aux tank 5 gal | **CONFIRMED** | [0.256, 0.390, -0.620] | 81.75 | [0.330, 0.390, -0.620] | 78.8 | +0.074 | -3.0 | HIGH | none | TCDS A8SO S-2B '5 gal @ +81.75'; est: upper wing centre 30% chord |
| Baggage | **CONFIRMED** | [-1.676, -0.002, 0.150] | 157.81 | [-1.728, -0.002, 0.150] | 159.8 | -0.051 | +2.0 | HIGH | none | TCDS A8SO S-2B 'Maximum Baggage 20 pounds (@ +157.81)'; est: rear seat CG -0.55 m |

### Extra 300-like Aerobat (`extra-300-like.json`)

- Variants: extra-300 (rear v4), extra-300-talk-flying / n300uy (rear v3)
- Datum: Plane of firewall (EASA TCDS A.362; POH 300L Sec 6.2), arms in cm aft. Arms +aft. **datum game x = 0.79**
- Anchor (MED): firewall at config x +0.79 ±0.05 (engine aft end); no direct firewall node
- Source: EXTRA 300L POH Sec 6.4 'Loading weights and moments' (pilot rear seat arm 207 cm, copilot front 98 cm; tank arms from moment tables), https://cdn.b12.io/client_media/GuGdX0se/18b04b0c-81aa-11f0-b92c-0242ac110002-300l.pdf ; EASA TCDS A.362 Issue 09 (datum firewall, baggage none), https://www.easa.europa.eu/en/downloads/7174/en

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Rear seat (PIC) | **CONFIRMED** | [-1.280, -0.007, -0.104] | 81.5 | [-1.444, -0.007, -0.104] | 87.9 | -0.164 | +6.4 | MED | [-1.597,0,-0.518] (= eye, placeholder) | EXTRA 300L POH p.6-8 'Arm = 207cm (81.5 inch)'; est: seated 1.778 m de Leva mannequin CG, variant extra-300/rear; livery variants rear v3 CG x -1.3983 |
| Front seat | **CONFIRMED** | [-0.190, -0.007, -0.030] | 38.58 | [-0.520, -0.007, -0.030] | 51.6 | -0.331 | +13.0 | MED | [-0.687,0,-0.529] (= eye, placeholder) | EXTRA 300L POH p.6-8 'Arm = 98 cm (38.4 inch)'; est: seated 1.778 m de Leva mannequin CG, variant extra-300/front |
| Acro & center tank 51 L | **CONFIRMED** | [0.492, -0.007, -0.130] | 11.73 | [0.030, -0.007, -0.130] | 29.9 | -0.463 | +18.2 | MED | none | EXTRA 300L POH p.6-8 moment table: 51 L = 36.7 kg -> 1094 kg·cm => 29.8 cm; est: front seat CG +0.55 m (tank just aft of firewall) |
| Wing tanks 2×60 L | **CONFIRMED** | [0.289, 1.150, -0.070] | 19.72 | [-0.008, 1.150, -0.070] | 31.4 | -0.297 | +11.7 | MED | none | EXTRA 300L POH p.6-8 moment table: 120 L = 86.4 kg -> 4328 kg·cm => 50.1 cm; est: inboard wing 30% chord |
| Baggage | **NONE** | — |  | — |  |  |  |  | none | EASA TCDS A.362 §20 'Baggage/Cargo Compartments: None'; POH placard 'NO BAGGAGE'; none |

### Great Lakes 2T-1A-2-like Biplane (`great-lakes-like.json`)

- Variants: great-lakes (extended-gear corrected placement)
- Datum: Fuselage Sta 00 = centre of most forward lateral fuselage tube (TCDS A18EA). Arms +aft. **datum game x = -0.2**
- Anchor (MED): MAC route (LEMAC Sta 9.0, MAC 46.0) gives -0.200; firewall route -0.175 (±0.025)
- Source: FAA TCDS A18EA Rev 14, Model 2T-1A-2 (seats 1 at 27.5, 1 at 61.5; fuel 26 gal top-wing tank +13.0 & 1.4 gal header +5.0; baggage 40 lb +92.0), https://openairdocs.org/tcds/A18EA

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [-0.898, 0.000, 0.085] | 27.5 | [-1.019, 0.000, 0.085] | 32.2 | -0.120 | +4.7 | MED | [-1.193,0,-0.413] (= eye, placeholder) | TCDS A18EA 2T-1A-2 'No. of seats 2 (1 at 27.5, 1 at 61.5)'; est: seated 1.778 m de Leva mannequin CG, variant great-lakes/front |
| Rear seat (PIC) | **CONFIRMED** | [-1.762, 0.003, 0.001] | 61.5 | [-1.810, 0.003, 0.001] | 63.4 | -0.048 | +1.9 | MED | [-1.96,0,-0.487] (= eye, placeholder) | TCDS A18EA; est: seated 1.778 m de Leva mannequin CG, variant great-lakes/rear |
| Top-wing tank 26 gal | **CONFIRMED** | [-0.530, 0.530, -0.690] | 13.0 | [-0.340, 0.530, -0.690] | 5.5 | +0.190 | -7.5 | MED | none | TCDS A18EA 'one 26.0 gal. tank in top wing at +13.0'; est: upper wing centre 30% chord |
| Fuselage header tank 1.4 gal (IO-360) | **CONFIRMED** | [-0.327, 0.000, -0.215] | 5.0 | [-0.419, 0.000, -0.215] | 8.6 | -0.092 | +3.6 | MED | none | TCDS A18EA 'one 1.4 gal. header tank in fuselage at +5.0'; est: front seat CG +0.6 m |
| Baggage aft compartment | **CONFIRMED** | [-2.537, 0.003, 0.001] | 92.0 | [-2.410, 0.003, 0.001] | 87.0 | +0.127 | -5.0 | MED | none | TCDS A18EA 2T-1A-2 'Maximum baggage (a) 40 lb. (+92.0)' (s/n 701-1199; s/n 1200+: 30 lb +92.0); est: rear seat CG -0.6 m |
| Baggage headrest (s/n 1200+) | **CONFIRMED** | [-2.334, 0.000, 0.000] | 84.0 | — |  |  |  | MED | none | TCDS A18EA '(b) 10 lb (+84.0) pilot's headrest compartment' |

### Stearman-like Trainer Biplane (`stearman-pt17-like.json`)

- Variants: stearman
- Datum: Leading edge of lower wing (TCDS A-743). Arms +aft. **datum game x = -0.044**
- Anchor (MED): config lower wing inner strip LE -0.044
- Source: FAA TCDS A-743 Model A75N1 (PT-17) (seats +19/+55; fuel 46 gal -5; baggage 60 lb +84), https://openairdocs.org/tcds/A-743

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **CONFIRMED** | [-0.527, 0.005, -0.146] | 19 | [-0.537, 0.005, -0.146] | 19.4 | -0.010 | +0.4 | MED | none | TCDS A-743 'No. of Seats 2 (+19) and (+55)'; est: seated 1.778 m de Leva mannequin CG, variant stearman/front |
| Rear seat (PIC) | **CONFIRMED** | [-1.441, 0.000, -0.406] | 55 | [-1.254, 0.000, -0.406] | 47.6 | +0.187 | -7.4 | MED | none | TCDS A-743; est: seated 1.778 m de Leva mannequin CG, variant stearman/rear |
| Centre-section tank 46 gal | **CONFIRMED** | [0.083, 0.630, -0.780] | -5 | [-0.050, 0.630, -0.780] | 0.2 | -0.133 | +5.2 | MED | none | TCDS A-743 'Fuel Capacity 46 gals. (-5)'; est: upper wing centre 30% chord |
| Baggage | **CONFIRMED** | [-2.178, 0.000, -0.406] | 84 | [-1.854, 0.000, -0.406] | 71.2 | +0.324 | -12.8 | MED | none | TCDS A-743 'Maximum Baggage 60 lbs. (+84)'; est: rear seat CG -0.6 m |

### Beaver-like (`dhc2-beaver-like.json`)

- Variants: dhc2-beaver
- Datum: 17.5 in aft of wing LE; arms +forward / -aft (TCDS A-806 Mk I). Arms +fwd. **datum game x = -0.0255**
- Anchor (MED): config inner wing strip LE 0.419 → datum x -0.0255
- Source: TCDS A-806 DHC-2 Mk I (seats 2 front +5, 3 centre -31, 3 rear -66; tanks +4.5 / -19.6 / -40.0 under floor; baggage per approved loading instructions), https://openairdocs.org/tcds/A-806

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seats L/R | **CONFIRMED** | [0.102, -0.300, -0.377] | 5 | [0.993, -0.300, -0.377] | 40.1 | +0.892 | +35.1 | MED | none | TCDS A-806 'Number of seats Eight 2 front (+5)'; est: seated 1.778 m de Leva mannequin CG, variant dhc2-beaver/left |
| Centre bench (3) | **CONFIRMED** | [-0.813, 0.000, -0.377] | -31 | — |  |  |  | MED | none | TCDS A-806 '3 center (-31)'; no mannequin/eyepoint |
| Rear bench (3) | **CONFIRMED** | [-1.702, 0.000, -0.377] | -66 | — |  |  |  | MED | none | TCDS A-806 '3 rear (-66)'; no mannequin/eyepoint |
| Front tank 29 imp gal | **CONFIRMED** | [0.089, 0.000, 0.073] | 4.5 | [0.893, 0.000, 0.073] | 36.2 | +0.805 | +31.7 | MED | none | TCDS A-806; est: under floor below front seats |
| Centre tank 29 imp gal | **CONFIRMED** | [-0.523, 0.000, 0.073] | -19.6 | [0.243, 0.000, 0.073] | 10.6 | +0.767 | +30.2 | MED | none | TCDS A-806; est: under floor, front seat -0.75 m |
| Rear tank 21 imp gal | **CONFIRMED** | [-1.042, 0.000, 0.073] | -40.0 | [-0.307, 0.000, 0.073] | -11.1 | +0.735 | +28.9 | MED | none | TCDS A-806; est: under floor, front seat -1.3 m |
| Baggage (aft cabin) | **ESTIMATED** | [-1.607, 0.000, -0.277] |  | (= primary) | -62.2 |  |  | LOW | none | TCDS A-806 'Maximum baggage: See approved loading instructions' (not published); est: behind rear bench (front seat -2.6 m) |

### Beaver-like on Floats (`dhc2-beaver-floats-like.json`)

- Variants: dhc2-beaver-floats
- Datum: 17.5 in aft of wing LE; arms +forward / -aft (TCDS A-806 Mk I). Arms +fwd. **datum game x = -0.0255**
- Anchor (MED): config inner wing strip LE 0.419 → datum x -0.0255
- Source: TCDS A-806 DHC-2 Mk I (seats 2 front +5, 3 centre -31, 3 rear -66; tanks +4.5 / -19.6 / -40.0 under floor; baggage per approved loading instructions), https://openairdocs.org/tcds/A-806

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seats L/R | **CONFIRMED** | [0.102, -0.300, -0.377] | 5 | [0.993, -0.300, -0.377] | 40.1 | +0.892 | +35.1 | MED | none | TCDS A-806 'Number of seats Eight 2 front (+5)'; est: seated 1.778 m de Leva mannequin CG, variant dhc2-beaver-floats/left |
| Centre bench (3) | **CONFIRMED** | [-0.813, 0.000, -0.377] | -31 | — |  |  |  | MED | none | TCDS A-806 '3 center (-31)'; no mannequin/eyepoint |
| Rear bench (3) | **CONFIRMED** | [-1.702, 0.000, -0.377] | -66 | — |  |  |  | MED | none | TCDS A-806 '3 rear (-66)'; no mannequin/eyepoint |
| Front tank 29 imp gal | **CONFIRMED** | [0.089, 0.000, 0.073] | 4.5 | [0.893, 0.000, 0.073] | 36.2 | +0.805 | +31.7 | MED | none | TCDS A-806; est: under floor below front seats |
| Centre tank 29 imp gal | **CONFIRMED** | [-0.523, 0.000, 0.073] | -19.6 | [0.243, 0.000, 0.073] | 10.6 | +0.767 | +30.2 | MED | none | TCDS A-806; est: under floor, front seat -0.75 m |
| Rear tank 21 imp gal | **CONFIRMED** | [-1.042, 0.000, 0.073] | -40.0 | [-0.307, 0.000, 0.073] | -11.1 | +0.735 | +28.9 | MED | none | TCDS A-806; est: under floor, front seat -1.3 m |
| Baggage (aft cabin) | **ESTIMATED** | [-1.607, 0.000, -0.277] |  | (= primary) | -62.2 |  |  | LOW | none | TCDS A-806 'Maximum baggage: See approved loading instructions' (not published); est: behind rear bench (front seat -2.6 m) |

### Pawnee-like Tow Plane (`pa25-pawnee-like.json`)

- Variants: pa25-pawnee
- Datum: 78 in forward of wing LE (TCDS 2A8, PA-25-235). Arms +aft. **datum game x = 2.4112**
- Anchor (MED): config inner wing strip LE 0.430
- Source: FAA TCDS 2A8 PA-25-235 (pilot +135; fuel 38/42 gal +63; hopper/cargo +93), https://openairdocs.org/tcds/2A8 , https://www.soarcsa.org/uploads/file/aircraft/Pawnee%20TCDS%202A8-23.pdf

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **CONFIRMED** | [-1.018, 0.000, -0.362] | 135 | [-0.907, 0.000, -0.362] | 130.6 | +0.111 | -4.4 | MED | none | TCDS 2A8 PA-25-235 (pilot +135; PA-25 LE datum: +59); est: seated 1.778 m de Leva mannequin CG, variant pa25-pawnee/pilot |
| Fuselage tank 38/42 gal | **CONFIRMED** | [0.811, 0.000, -0.362] | 63 | [0.693, 0.000, -0.362] | 67.6 | -0.118 | +4.6 | MED | none | TCDS 2A8 PA-25-235 '42 gallons at (+63)'; est: pilot CG +1.6 m (tank between hopper and firewall) |
| Hopper / cargo (unused as tow plane) | **CONFIRMED** | [0.049, 0.000, -0.362] | 93 | [-0.007, 0.000, -0.362] | 95.2 | -0.056 | +2.2 | MED | none | TCDS 2A8 hopper station +93; est: pilot CG +0.9 m |

### Archer-like (`pa28-archer-like.json`)

- Variants: archer
- Datum: 78.4 in ahead of wing LE at inboard edge of tapered section (PA-28-181 POH). Arms +aft. **datum game x = 2.4614**
- Anchor (LOW): config inner (straight) strip LE 0.470; repo doc flags the Archer anchor as suspect
- Source: Piper PA-28-181 Archer POH Sec 6 (front 80.5, rear 118.1, fuel 95.0, baggage 142.8), https://fliegen.ch/pdf/afm/Archer_3/ArcherIIISection6.pdf

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot (front L) | **CONFIRMED** | [0.417, -0.253, -0.218] | 80.5 | [0.633, -0.253, -0.218] | 72.0 | +0.216 | -8.5 | LOW | none | PA-28-181 POH Sec 6; est: seated 1.778 m de Leva mannequin CG, variant archer/left |
| Front R | **CONFIRMED** | [0.417, 0.249, -0.230] | 80.5 | [0.550, 0.249, -0.230] | 75.2 | +0.134 | -5.3 | LOW | none | PA-28-181 POH Sec 6; est: seated 1.778 m de Leva mannequin CG, variant archer/right; L/R mannequin CGs differ by 0.08 m (eyepoint asymmetry) |
| Rear seats | **CONFIRMED** | [-0.538, 0.250, -0.220] | 118.1 | — |  |  |  | LOW | none | PA-28-181 POH Sec 6; no rear mannequin |
| Wing tanks 48 gal usable (L+R) | **CONFIRMED** | [0.048, 1.390, 0.450] | 95.0 | [-0.068, 1.390, 0.450] | 99.6 | -0.116 | +4.6 | LOW | none | PA-28-181 POH Sec 6; est: inboard wing 30% chord |
| Baggage | **CONFIRMED** | [-1.166, 0.000, -0.200] | 142.8 | — |  |  |  | LOW | none | PA-28-181 POH Sec 6 (200 lb) |

### Seminole-like Twin (`seminole-like.json`)

- Variants: seminole, seminole-stock (alt frame, CG x -0.0259 z -0.665)
- Datum: 78.4 in forward of wing LE at wing station 106 (TCDS A19SO / PA-44-180 POH). Arms +aft. **datum game x = 2.3224**
- Anchor (MED): config wing LE interpolated at y=2.69 m (WS 106) = 0.331
- Source: Piper PA-44-180 POH Sec 6 loading table (front 80.5, rear 118.1, fuel 95.0, baggage 142.8), https://ou.edu/content/dam/ags/aviation/documents/resources/weights-and-balance/seminole-section-6.pdf ; TCDS A19SO

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot / front pax | **CONFIRMED** | [0.278, -0.297, 0.034] | 80.5 | [0.353, -0.297, 0.034] | 77.6 | +0.075 | -2.9 | MED | none | PA-44-180 POH Sec 6 'Pilot and Front Passenger 80.5'; est: seated 1.778 m de Leva mannequin CG, variant seminole/left |
| Rear seats | **CONFIRMED** | [-0.677, 0.300, 0.034] | 118.1 | — |  |  |  | MED | none | PA-44-180 POH Sec 6; no rear mannequin |
| Nacelle/wing tanks 108 gal (L+R) | **CONFIRMED** | [-0.091, 2.760, 0.200] | 95.0 | [-0.073, 2.760, 0.200] | 94.3 | +0.018 | -0.7 | MED | none | PA-44-180 POH Sec 6 'Fuel (108 Gallon Maximum Usable) 95.0'; est: wing 30% chord at 40% semispan (nacelle tanks) |
| Baggage | **CONFIRMED** | [-1.305, 0.000, 0.034] | 142.8 | [-1.397, 0.000, 0.034] | 146.4 | -0.093 | +3.6 | MED | none | PA-44-180 POH Sec 6 (200 lb); est: front seat CG -1.75 m |

### SR22-like (`cirrus-sr22-like.json`)

- Variants: sr22
- Datum: FS 0, 100 in forward of firewall (SR22 POH). Arms +aft. **datum game x = 3.6845**
- Anchor (MED): game LEMAC (WeightAndBalance MacGeom) 0.3037 + 133.1 in (published LEMAC FS 133.1, MAC 47.7)
- Source: Cirrus SR22 POH Sec 6 (front 143.5, rear 180.0, fuel 154.9, baggage 208.0), https://nova.aero/repository/Manuals/SR22/G3/SR22%20POH%20G3.pdf

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seats L/R | **CONFIRMED** | [0.040, -0.341, -0.464] | 143.5 | [-0.004, -0.341, -0.464] | 145.2 | -0.044 | +1.7 | MED | none | SR22 POH Sec 6; est: seated 1.778 m de Leva mannequin CG, variant sr22/left |
| Rear seats | **CONFIRMED** | [-0.887, 0.340, -0.464] | 180.0 | — |  |  |  | MED | none | SR22 POH Sec 6; no rear mannequin |
| Wing tanks 92 gal (L+R) | **CONFIRMED** | [-0.250, 1.510, 0.400] | 154.9 | [-0.054, 1.510, 0.400] | 147.2 | +0.196 | -7.7 | MED | none | SR22 POH Sec 6; est: inboard wing 30% chord |
| Baggage | **CONFIRMED** | [-1.599, 0.000, -0.264] | 208.0 | [-1.754, 0.000, -0.264] | 214.1 | -0.156 | +6.1 | MED | none | SR22 POH Sec 6 (130 lb); est: front seat CG -1.75 m |

### Glasair III-like (`glasair3-like.json`)

- Variants: glasair3
- Datum: 60 in forward of cowl-to-firewall joint (CAFE report N313CH). Arms +aft. **datum game x = 2.4179**
- Anchor (MED): game LEMAC 0.5078 + 75.20 in (CG range 79.65-87.88 in = 10-28.5 %MAC → LEMAC 75.20, MAC 44.5); main-gear route (arm 95.75) gives 2.388 (1.2 in)
- Source: CAFE Foundation Aircraft Performance Report, Glasair III N313CH (Sport Aviation Feb 1997), W&B data block: crew 108.75, fuel front/rear 65.75/81.35, baggage 132.2, http://acversailles.free.fr/documentation/03~Documentation_Generale_M_Suire/Vol/Performances/Glasair-III.pdf

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot / pax (side by side) | **CONFIRMED** | [-0.344, -0.195, 0.023] | 108.75 | [0.393, -0.195, 0.023] | 79.7 | +0.738 | -29.0 | MED | none | CAFE report 'Crew moment arm 108.75 in'; est: seated 1.778 m de Leva mannequin CG, variant glasair3/left |
| Header tank 5.4 gal | **CONFIRMED** | [0.748, 0.000, -0.277] | 65.75 | [0.993, 0.000, -0.277] | 56.1 | +0.246 | -9.6 | MED | none | CAFE report 'Fuel moment arms front/rear 65.75/81.35'; est: seat CG +0.6 m (fwd of panel) |
| Wing tank 51.8 gal | **CONFIRMED** | [0.352, 0.760, 0.450] | 81.35 | [0.150, 0.760, 0.450] | 89.3 | -0.202 | +8.0 | MED | none | CAFE report; est: inboard wing 30% chord |
| Baggage 100 lb | **CONFIRMED** | [-0.940, 0.000, 0.023] | 132.2 | [-0.207, 0.000, 0.023] | 103.3 | +0.733 | -28.9 | MED | none | CAFE report sample CG (baggage arm 132.2); est: seat CG -0.6 m |

### DC-3-like Airliner (`dc3-like.json`)

- Variants: dc3
- Datum: Leading edge of wing centre section (TCDS A-669). Arms +aft. **datum game x = 2.284**
- Anchor (LOW): config inner wing strip LE 2.284 at y 1.13 (centre section; ±0.36 m if LE taken at y=0)
- Source: FAA TCDS A-669 (fuel main +48, aux +83.5; baggage fwd -63/-45, aft +354), https://douglasdc3.com/faa/a-669.pdf
- Notes: passenger cabin not modeled; no published crew arm

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Crew seats L/R | **ESTIMATED** | [4.627, -0.481, -0.634] |  | (= primary) | -92.3 |  |  | MED | none | TCDS A-669 gives no crew arm; est: mannequin CG |
| Main tanks (2) | **CONFIRMED** | [1.065, 1.130, -0.900] | 48 | [1.056, 1.130, -0.900] | 48.3 | -0.009 | +0.3 | LOW | none | TCDS A-669; est: centre section 30% chord |
| Aux tanks (2) | **CONFIRMED** | [0.163, 1.130, -0.900] | 83.5 | [0.238, 1.130, -0.900] | 80.6 | +0.075 | -2.9 | LOW | none | TCDS A-669; est: centre section 50% chord |
| Fwd baggage R | **CONFIRMED** | [3.884, 0.000, -0.334] | -63 | [3.127, 0.000, -0.334] | -33.2 | -0.757 | +29.8 | LOW | none | TCDS A-669; est: crew CG -1.5 m |
| Fwd baggage L | **CONFIRMED** | [3.427, 0.000, -0.300] | -45 | — |  |  |  | LOW | none | TCDS A-669 |
| Aft baggage | **CONFIRMED** | [-6.708, 0.000, -0.300] | 354 | — |  |  |  | LOW | none | TCDS A-669 |

### P-51D-like Fighter (`p51d-like.json`)

- Variants: p51
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)
- Notes: T.O. 01-60J-5 loading data exists (aircorpslibrary) but arms not openly published

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **ESTIMATED** | [0.420, 0.000, -0.596] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant p51/pilot |
| Wing tanks 2×92 gal | **ESTIMATED** | [0.046, 1.450, 0.250] |  | (= primary) |  |  |  | LOW | none | est: inboard wing 25% chord |
| Fuselage tank 85 gal (behind pilot) | **ESTIMATED** | [-0.380, 0.000, -0.495] |  | (= primary) |  |  |  | LOW | none | est: pilot CG -0.8 m |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none (radio bay); none |

### F-86-like Jet Fighter (`f86-sabre-like.json`)

- Variants: f86
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **ESTIMATED** | [1.856, 0.000, -0.232] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant f86/pilot |
| Fuselage tanks (fwd/aft) | **ESTIMATED** | [-0.342, 0.000, -0.200] |  | (= primary) |  |  |  | LOW | none | est: near MAC quarter chord |
| Wing tanks | **ESTIMATED** | [0.396, 1.310, -0.300] |  | (= primary) |  |  |  | LOW | none | est: inboard wing 30% chord |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none; none |

### 737-like Airliner (`boeing-737-like.json`)

- Variants: 737
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)
- Notes: Boeing W&B manual is not public; cabin passengers not modeled

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Captain / F/O | **ESTIMATED** | [17.424, -0.510, -0.050] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant 737/left |
| Centre tank | **ESTIMATED** | [1.012, 0.000, 0.900] |  | (= primary) |  |  |  | LOW | none | est: wing root 45% chord |
| Wing tanks (main 1/2) | **ESTIMATED** | [0.440, 3.990, 1.000] |  | (= primary) |  |  |  | LOW | none | est: wing 35% chord @30% semispan |
| Fwd cargo hold | **ESTIMATED** | [10.556, 0.000, 1.200] |  | (= primary) |  |  |  | LOW | none | est: midway cockpit-wing LE |
| Aft cargo hold | **ESTIMATED** | [-10.804, 0.000, 1.200] |  | (= primary) |  |  |  | LOW | none | est: ~7 m aft of MAC TE |

### H-4-like Flying Boat (`hughes-h4-like.json`)

- Variants: h4
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Flight deck L/R | **ESTIMATED** | [6.080, -0.576, -2.461] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant h4/left |
| Hull/wing tanks | **ESTIMATED** | [-0.847, 0.000, -2.000] |  | (= primary) |  |  |  | LOW | none | est: MAC 30% |
| Cargo deck | **ESTIMATED** | [-0.313, 0.000, 0.500] |  | (= primary) |  |  |  | LOW | none | est: near quarter-chord MAC |

### Gee Bee R-2-like (`geebee-r2-like.json`)

- Variants: geebee
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **ESTIMATED** | [-1.772, 0.000, -0.459] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant geebee/pilot |
| Fuselage tank (ahead of pilot) | **ESTIMATED** | [-0.772, 0.000, -0.359] |  | (= primary) |  |  |  | LOW | none | est: pilot CG +1.0 m |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none; none |

### Cassutt-like Racer (`cassutt-f1-like.json`)

- Variants: cassutt-f1
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **ESTIMATED** | [0.181, 0.000, 0.117] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant cassutt-f1/pilot |
| Fuselage tank 14.5 gal (behind firewall) | **ESTIMATED** | [0.931, 0.000, -0.133] |  | (= primary) |  |  |  | LOW | none | est: pilot CG +0.75 m |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none; none |

### AirCam-like (`aircam-like.json`)

- Variants: aircam
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **ESTIMATED** | [1.343, 0.000, 0.076] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant aircam/front |
| Rear seat | **ESTIMATED** | [0.493, 0.000, 0.076] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant aircam/rear |
| Fuel tanks (under/behind rear seat) | **ESTIMATED** | [0.143, 0.000, 0.226] |  | (= primary) |  |  |  | LOW | none | est: rear seat CG -0.35 m |
| Baggage | **ESTIMATED** | [-0.107, 0.000, -0.024] |  | (= primary) |  |  |  | LOW | none | est: rear seat CG -0.6 m |

### AirCam-like (amphib) (`aircam-amphib-like.json`)

- Variants: aircam-amphib
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **ESTIMATED** | [1.343, 0.000, 0.076] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant aircam-amphib/front |
| Rear seat | **ESTIMATED** | [0.493, 0.000, 0.076] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant aircam-amphib/rear |
| Fuel tanks (under/behind rear seat) | **ESTIMATED** | [0.143, 0.000, 0.226] |  | (= primary) |  |  |  | LOW | none | est: rear seat CG -0.35 m |
| Baggage | **ESTIMATED** | [-0.107, 0.000, -0.024] |  | (= primary) |  |  |  | LOW | none | est: rear seat CG -0.6 m |

### EB29R-like Open Class (`glider-eb29r-like.json`)

- Variants: eb29r
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)
- Notes: eb29r-stock entry is in a different frame (x 1.3759, z -2.13); use eb29r

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **ESTIMATED** | [0.178, 0.000, 0.130] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant eb29r/pilot |
| Fuel | **NONE** | — |  | — |  |  |  | LOW | none | glider; none |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none; none (EB29R water ballast not modeled) |

### Swift S-1-like Aerobat (`glider-swift-s1-like.json`)

- Variants: swift
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **ESTIMATED** | [0.888, 0.000, 0.128] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant swift/pilot |
| Fuel | **NONE** | — |  | — |  |  |  | LOW | none | glider; none |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none; none (EB29R water ballast not modeled) |

### JN-4D-like Trainer Biplane (`jn4-jenny-like.json`)

- Variants: jenny
- Datum: none published (game body frame only). Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: no published W&B arms found (search: POH/TCDS/W&B sheets)
- Notes: JN-4D spec sheet (NTSB docket) gives weights/fuel only

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Front seat | **ESTIMATED** | [0.012, 0.000, 0.059] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant jenny/front |
| Rear seat (PIC) | **ESTIMATED** | [-0.997, 0.000, 0.059] |  | (= primary) |  |  |  | MED | none | est: seated 1.778 m de Leva mannequin CG, variant jenny/rear |
| Fuselage tank 21 gal | **ESTIMATED** | [0.562, 0.000, -0.041] |  | (= primary) |  |  |  | LOW | none | est: front seat CG +0.55 m |
| Baggage | **NONE** | — |  | — |  |  |  | LOW | none | none; none |

### Hang Glider-like (`hang-glider-like.json`)

- Variants: —
- Datum: n/a. Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: n/a

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **NONE** | — |  | — |  |  |  |  | weightShift pilotMassKg 80 | no seat (weight-shift / suspended pilot / unmanned) |

### Paraglider-like (EN-B) (`paraglider-en-b-like.json`)

- Variants: —
- Datum: n/a. Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: n/a

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **NONE** | — |  | — |  |  |  |  | none | no seat (weight-shift / suspended pilot / unmanned) |

### Target drone (`target-drone-like.json`)

- Variants: —
- Datum: n/a. Arms +aft. **datum game x = n/a**
- Anchor (n/a): n/a
- Source: n/a

| Item | Status | Primary game xyz (m) | Published arm (in) | Our estimate xyz (m) | Est. arm (in) | Δ m (+fwd) | Δ in | Conf. | Current config | Source / method |
|---|---|---|---|---|---|---|---|---|---|---|
| Pilot | **NONE** | — |  | — |  |  |  |  | none | no seat (weight-shift / suspended pilot / unmanned) |

## Wiring notes for Claude

- Use **Primary** for `loading.stations[].pos` and `loading.tanks[].pos`. Keep **y** as given: side-by-side seats get ±y, and centreline seats can be y = 0.
- Rows with no mannequin (C172, Archer, SR22 and Seminole rear seats; Beaver centre and rear benches; some baggage) take their y/z from the nearest seat row. Treat that z as a placeholder.
- Some aircraft have a large Δ (Beaver, Glasair, Decathlon). The published arm is still primary for mass properties. The visual seat or eye mismatch is a separate model-placement issue, so do not move cockpits in this lane.
- Pitts and Extra liveries use the v3 rear seat. Their rear CG x is −1.1481 (Pitts) and −1.3983 (Extra), against −1.1777 and −1.4436 for v4. The published arm is the same for both.
- The C172 stations already match the POH. Leave them unless tests require otherwise.
- `stock` variants (`c172-stock`, `seminole-stock`, `eb29r-stock`):
  - `c172-stock` is the in-game C172 entry used above.
  - `seminole-stock` and `eb29r-stock` are in a different frame. Ignore them.
