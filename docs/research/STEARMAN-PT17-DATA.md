# Boeing-Stearman Model 75 (PT-17 / N2S) — source data, and why the model ran out of elevator before its wing stalled

Written 2026-11-10 on `lane/claude-stearman-elev`, answering Ty's instruction about the Stearman's stall speed:
**"Investigate it properly"** — explicitly over *"just give it more travel"*, with the reasoning he gave and
accepted: *something is wrong — either the elevator is too small, the travel too limited, the CG too far forward,
or the tail volume is off — and raising the stop would hide whichever of the four it is.*

He also said, in the same breath, that it is legitimate for the answer to be that **the aeroplane is right and the
test is measuring an elevator limit**, because a real aeroplane at a forward CG genuinely does run out of elevator
before the wing stalls — that is what a forward CG limit is for.

**It is not that.** It is the third of his four, the CG — and it has a second, sourced cause sitting underneath it
that is not on his list at all: the **stagger is 9 inches short**. Both come out of one primary document.

Everything in § 1 is **[P]** (read from the document named) or **[S]** (secondary) or **[NF]** (not found, and
*not* interpolated). § 2 onwards is measured in this repo and says which probe measured it.

---

## 1. The sources

### FAA TCDS A-743, Revision 16, 25 April 2023 — the primary document **[P]**

Holder: The Boeing Company. Certification basis Type Certificate No. 743 / CAR 4a. Covers 75 (PT-13), A75
(PT-13A/-13B/-13C), A75J1 (PT-18), A75L3, A75L300, **A75N1 (PT-17/-17A, N2S-1/-4)**, B75 (N2S-5), B75N1 (N2S-3),
D75N1 (PT-27), E75 (PT-13D/N2S-5), E75N1, IB75A. Read page by page out of the FAA Dynamic Regulatory System:
`https://drs.faa.gov/browse/excelExternalWindow/DRSDOCID131948451020230425205902.0001?modalOpened=true`
(`rgl.faa.gov` is dead — no DNS, nothing in the Wayback Machine).

Verbatim, under *Specifications Pertinent to All Models*:

> Datum — Leading edge of lower wing
> Leveling Means — Leveling lugs provided in front cockpit

Verbatim, Section I (A75L3, 75, A75, B75, E75) **and** Section II (A75N1 = PT-17, B75N1, D75N1) — the two sections
give the same figures:

> C.G. Range — (-1.0) to (+7.1)
>
> Empty Weight C.G. Range — (-1.5) to (+0.5) solo from either seat
> (-4.4) to (+0.5) placard in front cockpit: "Solo from rear seat only."
> When empty weight C.G. falls within this range, computation of critical fore and aft C.G. positions is
> unnecessary. Ranges are not valid for non-standard arrangement or for crop duster or sprayer installations.

Inches aft (+) or forward (−) of the lower wing's leading edge. **The flight envelope is (−1.0) to (+7.1) in, 8.1
inches wide.** The (−1.5)…(+0.5) and (−4.4)…(+0.5) figures are *empty-weight* ranges used as a shortcut to avoid
computing the loaded extremes — the "(−1.5) to (+0.5)" that circulates on forums as "the Stearman's CG range" is
that shortcut, not the envelope.

> Maximum Weight — 2950 lbs. See NOTE 6 for 3200 lbs. maximum weight.

NOTE 6 tightens the aft limit with weight: `(-1.0) to (+4.5) at 3200 lbs. / (-1.0) to (+7.1) at 2950 lbs. or less /
Straight line variation of aft C.G. limit between 2950 lbs. and 3200 lbs.` This model flies at 2646 lb, so the full
+7.1 in applies.

**Control Surface Movements**, verbatim, Sections I and II alike:

> Elevator trim tabs — Up 15° — Down 15°
> **Elevator — Up 24° — Down 22°**
> Aileron — Up 23° — Down 18°
> Rudder — Right 30° — Left 30°

**NOTE 5, rigging specifications**, verbatim excerpt:

> Center section centering — … This dimension is normally 23" plus or minus 1/32" …
> Dihedral — Upper wing 1/2°, lower wing 1-1/2°
> Incidence — Upper wing 4°, lower wing 3°
> Stagger — 26-13/16" plus or minus 1/8" at strut point (spoilers on both wings or on neither wing) —
> 26-9/16" plus or minus 1/8" at strut point (spoilers on lower wing only)
> Sweepback — 0°

Loading arms, all **[P]**, inches from the datum: engine W-670-6A 465 lb **(−55)**; seats **(+19)** front and
**(+55)** rear; baggage 60 lb **(+84)**; fuel 46 gal **(−5)** (one tank in the upper wing centre section); oil
4.4 gal **(−26)**. Airspeed limits (TIAS) level or climb **125 mph**, glide or dive **186 mph**. Engine limit
A75N1 `2075 rpm (220 hp)`. NOTE 7 load factors **+6.67 to −2.67**. Empty weight: **[NF]** — TCDS of this era give
none; NOTE 1 requires a current weight-and-balance report in the aeroplane. M.A.C. or chord: **[NF]**, the
document has no such entry.

### Pilot's Handbook for Stearman Airplanes N2S-1, N2S-2, N2S-3 — the stall speed **[P]**

Report No. A75NI-9001, 20 January 1941, Stearman Aircraft Division of Boeing, released by the Bureau of
Aeronautics. Hosted in an NTSB docket:
`https://data.ntsb.gov/Docket/Document/docBLOB?ID=40464026&FileExtension=.PDF&FileName=Pilot's+Handbook+for+Stearman-Master.PDF`

Section I-D, Table of Characteristics, verbatim row:

> *Stalling Speed at Sea Level (M.P.H.) — 53 / 54 / 54 — (Knots) 46 / 46.9 / 46.9
> *With 50% Fuel and Oil Consumed.

with Normal Gross Weights **2682.7 / 2755.8 / 2726.7 lb** and wing loadings 9.03 / 9.26 / 9.16 lb/sq ft, and wing
area **297.6 sq ft**.

**This is the figure the model must be compared against, and the comparison in the repo was wrong.** The "book 52"
the Stearman has been measured against (and which `JN4-JENNY-DATA.md` § biplane-interference records as "on the
nose") is **Jane's 1945/6 LANDING speed**, not a stall. And the handbook's 53 mph is at 50 % fuel and oil: 50 % of
46 gal at 6 lb and 4.4 gal at 7.5 lb is 154 lb, so that stall is at about **2529 lb**, where this model flies at
2646 lb. The comparable figure is 53 · √(2646 / 2529) = **54.2 mph**.

Also **[P]** from the same handbook, and it corrects two other files in this repo:

> ELEVATOR TRIM TAB: Horizontal trim of the airplane is effected by trimming tabs located in the trailing edge of
> the elevators. These tabs are cable operated by an irreversible mechanism located near the front spar of the
> stabilizer. … require a total movement of 270° to obtain the full tab range of 15 up and 15 down.

and `A. BALANCE — Longitudinal balance is maintained by the use of the elevator trim tab.` **The Stearman has no
in-flight adjustable stabiliser.** A-743's NOTE 5 rigging list contains no stabiliser incidence entry and its
Control Surface Movements table lists the ±15° elevator trim tabs; the AirCorps *Stearman Airframe Parts Catalog*
index has sections 64–67 for the elevator tab control front, rear and non-reversing assembly and no
adjustable-stabiliser jackscrew; Jane's says "trimming-tab in elevator".
`docs/research/CONTROLS-REVERSIBILITY-AND-SPRINGS.md` (reference 25, "no primary source found; flagged") and
`docs/research/JN4-JENNY-DATA.md` both say adjustable stabiliser. Corrected in both.

### Secondary, and what is genuinely unpublished

- **Wing area 297.4 sq ft** — Jane's 1945/6 p. 215c **[S]**, against the handbook's 297.6 **[P]**. Agree to 0.2.
- **Airfoil NACA 2213** — Jane's **[S]**. No source names a different section upper versus lower; A-743's rigging
  note has them differ only in incidence, so it is 2213 on both wings.
- **Span upper 32 ft 2 in, lower 31 ft 2 in** — handbook **[P]**.
- **Chord 60 in both wings** — National Stearman Fly-In **[S]** only; **[NF]** in any primary document. Any
  "% MAC" figure for this type therefore rests on a type-club number, which is why § 2 below works in the
  certificate's own inches instead.
- **Horizontal stabiliser area, elevator area, total horizontal tail area, tail moment arm — [NF].** Not in A-743,
  not in the handbook, not in Jane's, not in the parts catalog, and there is no NACA/NTRS report on the Model 75.
  The likely holder is **AN 01-70AC-4** (*Erection & Maintenance Instructions, Army PT-13D / Navy N2S-5*; note
  AN 01-70AC-2 is the parts catalog, not the E&M manual), which is sold as a scan by AirCorps Library and Avialogs
  and is not free. **These four numbers are the gap in this investigation and they are not guessed anywhere in it.**
- **Horizontal tail span 12 ft 6 in** — National Stearman Fly-In **[S]**, low-to-medium confidence (it says
  "horizontal stabilizer (elevator) width", which is ambiguous). The model has 11 ft 10 in.
- **The real aeroplane is routinely stalled fully onto the ground.** *Stearman Wheel Landings*, CAF Utah Wing,
  2012 **[P as a technique article]**, treats the "full stall three point landing" as the normal Stearman landing
  and describes holding "full up elevator to keep the tail firmly grounded". Budd Davisson's pirep (*Air Progress
  Vintage Guide* 1989) **[S]**: "If you force and yank real hard, yes, the stall will break." So the wing does
  reach its stall on the elevator, and it takes a firm pull. **No published statement that a PT-17 hits the
  elevator stop before the wing stalls was found [NF]** — the documented forward-CG constraint is the loading
  placard, not a loss of pitch authority.

---

## 2. What the model does, measured

`StearmanElevatorLimitProbe` (`STEARMAN_ELEV=1`) measures all of this. It reloads the config from disk for every
variant, because the finite-span section table, the system aspect ratio and the Vso alpha cap are each cached per
`AircraftConfig` instance.

As configured, at 1200 kg (2646 lb), power off, flaps up, sea level:

| | |
|---|---|
| Vso, the game's 0.5 m/s scan | 24.50 m/s = **54.8 mph** |
| Vso, a 0.1 m/s scan | 24.30 m/s = **54.4 mph** |
| elevator there | **−29.07° of a 29.79° stop = 98 % of travel** |
| body alpha there | **9.02°**, where the wing reaches CLmax at **13.50°** |
| `StallAuthorityTests` full-aft balance | **9.2° against a 16.5° stall** — the only type in the fleet whose full-aft balance is BELOW its own stall |

The 92 % quoted when this was opened is the same thing at the coarser scan step: the 0.5 m/s grid happens to land
just short of the stop. It is 98 %, and the trim ladder shows the aeroplane pinned at the stop from 24.0 m/s down.

**The tail is not short of air, and the downwash is not eating it** — the fifth thing Ty asked to be checked, and
it is ruled out. At the limiting condition every hStab strip reads local alpha **2.74°**, downwash **3.49°**,
**η_t = 0.823**. dε/dα ≈ 0.40 and η_t 0.82 are both textbook for a tail behind a biplane cellule.

### The four, one at a time

| Ty's candidate | what the sources say | what the model does | verdict |
|---|---|---|---|
| **travel too limited** | A-743: **up 24°, down 22°** | 29.8° symmetric | **ruled out, and backwards.** The model has 24 % MORE up travel than the certificate allows. Setting the real stops makes it worse: Vso 55.9 mph at 97 % of 24°. "Give it more travel" would have taken it further from the aeroplane, not nearer. |
| **elevator too small** | areas **[NF]** | elevator is 32.0 % of the horizontal tail's area; its contemporaries here are at 43.4 % (Great Lakes, Jenny) and the Cubs at 38.6 % | **not the cause.** Moving chord from the hStab row to the elevator row at constant tail area, 32 % → 43 %, changes the deflection needed from 27.5° to 23.7° and **does not move Vso at all** (24.50 both). It changes how much stick, not what speed. Open question on its own merits, unresolvable without AN 01-70AC-4. |
| **tail volume off** | areas and arm **[NF]**; tail span 12 ft 6 in **[S]** against the model's 11 ft 10 in | Vh = Sh·l/(S·c) = **0.383**, the lowest of the powered fleet (next 0.425, the rest 0.44–0.48); Sh/Sw 0.132, also lowest | **not the cause.** Scaling the tail 1.3× does reach the book speed, but the 2-33 flies at Vh 0.316 using 28 % of its travel, so Vh is not what sets the margin. Probably modestly undersized; an open item, not the answer. |
| **CG too far forward** | A-743: datum = **lower-wing LE**, flight range **−1.0 … +7.1 in** | the model's CG sits at **+0.24 in**, i.e. **15 % of the way into an 8.1-inch envelope** | **THIS IS IT.** |

### The CG, in the certificate's own inches

The model's lower-wing leading edge — A-743's datum — is at model station x −0.044 m, and the CG is at x −0.050 m,
so the CG is **+0.24 in aft of datum**. The envelope maps to model x −0.019 m (forward limit) to −0.224 m (aft
limit).

A-743 gives no empty weight, but it gives every loading arm, so the loaded CG can be computed the way the
certificate intends. With a 170 lb occupant per seat, full fuel (276 lb) and full oil (33 lb):

| loading | empty wt / empty CG | loaded wt | loaded CG |
|---|---|---|---|
| solo rear seat | 1936 lb @ −2.0 in | 2415 lb | **+1.34 in** |
| solo rear seat | 1936 lb @ −0.5 in | 2415 lb | **+2.54 in** |
| **dual, both seats** | **2000 lb @ −0.5 in** | **2649 lb — this model's own mass** | **+3.53 in** |
| dual, both seats | 1936 lb @ +0.5 in | 2585 lb | **+4.38 in** |

(Empty weight is **[NF]** in A-743; 1936 lb is the commonly quoted PT-17 figure **[S]** and 2000 lb is the one that
reproduces this model's 1200 kg exactly. The loaded CG moves only 0.1 in between the two, because the empty weight
sits essentially at the datum — so the answer does not depend on it.)

The model's 1200 kg *is* a dual, full-fuel load, and on A-743's own arms that load sits at about **+3.5 in** — so
the model's CG is **3.3 inches (0.084 m) too far forward**, and only an aeroplane at the extreme −4.4 in empty CG
would sit as far forward as the config does.

| CG, in aft of datum | Vso (0.1 m/s scan) | elevator | body alpha | static margin |
|---|---|---|---|---|
| **−1.00** — A-743 forward limit | 25.10 m/s, 56.1 mph | 98 % | 8.05° | 27.5 % |
| **+0.24** — as configured | 24.30 m/s, **54.4 mph** | **98 %** | 9.02° | **25.4 %** |
| +1.34 | 23.60 m/s, 52.8 mph | 98 % | 10.00° | 23.6 % |
| +2.54 | 23.00 m/s, 51.4 mph | 88 % | 10.93° | 21.6 % |
| **+3.53** — this mass on A-743's arms | 22.50 m/s, **50.3 mph** | **89 %** | 11.80° | 19.9 % |
| +5.00 | 22.10 m/s, 49.4 mph | 85 % | 12.52° | 17.5 % |
| **+7.10** — A-743 aft limit | 22.00 m/s, **49.2 mph** | **71 %** | 12.38° | **14.0 %** |

**That is the aft-CG number Ty asked for: at the certificate's aft limit the Stearman stalls at 49.2 mph using
71 % of its travel, and the stall is the wing's.**

### And the thing underneath it: the stagger is 9 inches short

A-743 NOTE 5: **stagger 26-13/16 in ± 1/8** (0.681 m). The model's leading-edge stagger is upper x +0.406 against
lower x −0.044 = 0.450 m = **17.7 in**. The upper wing is **0.231 m too far aft**, which drags the cellule's
area-weighted lift 0.117 m aft of where it belongs. The certificate's own loading arms corroborate the 26-13/16:
the fuel tank is in the upper-wing centre section at (−5) in, and a tank at ~40 % of a 60 in chord puts the upper
leading edge about 29 in forward of the lower one.

Which wing is misplaced is decided by the certificate too: if the LOWER wing were moved aft 0.231 m instead, the
datum moves with it and the CG lands **8.9 in FORWARD of datum**, far outside the −1.0 in forward limit — and
the cellule's lift centroid moves aft as well, so the static margin gets worse, not better. So it is the upper
wing, forward.

And A-743 NOTE 5 has the **incidence the other way round from the config**: the certificate says upper 4°, lower
3° (1° of decalage on the upper wing); the config has upper 3.00°, lower 4.00°.

The measured consequence of the two sourced geometry errors and the correct loading, each alone and together:

| variant | Vso | elevator | body alpha | static margin |
|---|---|---|---|---|
| as shipped | 24.30 m/s, 54.4 mph | 98 % | 9.02° | **25.4 %** |
| decalage to A-743 (upper 4 / lower 3) | 23.70 m/s, 53.0 mph | 98 % | 9.88° | 25.7 % |
| **stagger to 26-13/16 in** | 22.20 m/s, 49.7 mph | 87 % | 12.28° | **18.6 %** |
| decalage + stagger | 22.10 m/s, 49.4 mph | 81 % | 12.37° | 18.9 % |
| CG to +3.53 in only | 22.50 m/s, 50.3 mph | 89 % | 11.80° | 19.9 % |
| **decalage + stagger + CG** | 21.90 m/s, **49.0 mph** | **60 %** | 12.33° | **13.4 %** |

The static margin is the tell. **25.4 % of chord as shipped is the highest in the fleet** — PA-18 17.7, Pitts 11.2,
Great Lakes 11.1, Jenny 6.3 — and `AeroModel`'s own note on the hinged-section rework calls 25–70 % the symptom of
the 2-D-slope bug it fixed and **5–20 % the real light-aeroplane band**. The Stearman was left outside that band,
and 25.4 % of static margin at 8.1 inches of CG envelope is what eats 98 % of the elevator at the stall.

---

## 3. What this does NOT fix, and must be said in the same breath

**Correcting the CG and the stagger would take Vso the other way past the book.** The weight-corrected book stall
is 54.2 mph; as shipped the model reads 54.4 mph (**+0.4 %**) and the corrected airframe reads 49.0 mph
(**−9.6 %**). The trimmed CLmax goes from 1.20 (what the book speed needs at this weight and 27.3 m²) to **1.47** —
**22 % high**.

So the forward CG and the aft wing have been **masking a cellule CLmax that is too high**, and the right stall
speed has been coming out of two compensating errors. That is exactly the open item
`docs/research/JN4-JENNY-DATA.md` records as *"biplane interference, which the strip model does not have at all"* —
and the same file's reason for doubting it was that *"the Stearman and the Pitts are both limited by elevator
authority before their wings stall … so neither of them is actually measuring its wing"*. It isn't; now we know
what it reads when it is, and it reads 22 % of CLmax high, the same direction as the missing interference term.

**Nothing in § 2 or § 3 has been changed in the configs on this lane.** Moving the CG and the stagger is a change
to the whole aeroplane — static margin, trim, the rig, the glide table, the STOL spawn, the approach speeds, the
taildragger stance angle — and it needs Ty's call on whether to do it together with the cellule CLmax or to leave
the two compensating errors alone until the interference term exists. The parity lock also allows this lane only
one leading shared commit, which `controlSpoilerK` has taken.

## 4. Other discrepancies found against A-743 while looking, not acted on

- **Load factors**: A-743 NOTE 7 gives **+6.67 / −2.67**; the config's `limits` are +5.5 / −2.5.
- **Elevator travel**: A-743 gives **up 24 / down 22**; the config has 29.8 symmetric, which is this repo's generic
  default value (0.52 rad appears on 21 of 35 types). Lowering it to the certificate's figures on its own, without
  the CG and stagger, would make the stall speed worse, so it must not be done alone.
- **Wing area**: 27.30 m² modelled against 297.6 sq ft = 27.65 m² **[P]**, −1.3 %.
- **Horizontal tail span**: 3.60 m modelled against 12 ft 6 in = 3.81 m **[S]**, −5 %.
