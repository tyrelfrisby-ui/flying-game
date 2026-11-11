# The Extra 300L's wing section and its stall

lane/claude-inertia, run 4 (Claude, 2026-11-02); sections replaced in runs 5c and **5d**. Ty: *"I think the extras wing
is not stalling cleanly enough. … it is nothing like a 0012 it is a blunt round leading edge with slightly convex upper
and lower skins to the trailing edge so the separation pattern is leading edge stall immediately not a trailing edge to
leading edge progression."* Ty's clarification: not an early, thin-aerofoil or leading-edge-bubble stall. The lift curve
stays **linear to a high α with a high CLmax**; then **about 85 % of the upper surface separates at once**, so the drop
is **abrupt**. His estimate of the section CLmax, about 1.2, is used here as a check only.

Files:
- **Sections (runs 5d/5e):** `tools/aircraft_builder/extra_sections.py`, candidates A and B (B wired; the rejected
  thinning experiment C via `THIN_K`), from `data/e473-il.dat` (root)
  and `data/e472-il.dat` (tip), published Eppler coordinates. Exactly what XFOIL was given:
  `data/extra300-sections/{A,B}-{root,st1..st8,tip}.dat`, with `A.SOURCE.txt` and `B.SOURCE.txt`.
- **XFOIL validation against Ladson's NACA 0012 (run 5e):** `tools/aircraft_builder/xfoil_0012_validation.py`, polars
  `data/xfoil/v0012_*`.
- **XFOIL runs:** `tools/aircraft_builder/extra_xfoil.py` → `data/xfoil/{A,B}_st*.up.pol`, `.down.pol`, `.sep.txt`,
  and `xfoil-ab-runlog.json`.
- **Tables:** `tools/aircraft_builder/extra_wing_table.py` (`--write` writes `WIRED` = B through `WIRED_METHOD` =
  `measured` into `configs/aircraft/extra-300-like.json`, and its StreamingAssets mirror only if that generated,
  untracked file already exists; `--method r5e` prints run 5e's XFOIL-peak tables; `--cand A --raw` prints the
  comparison tables; `EXTRA_THIN_K` reproduces the rejected thinning experiment of §1d).
- **Tests:** `tools/FlightTests/ExtraWingSectionTests.cs`.
- **Probe:** `tools/FlightTests/StallCharacterProbe.cs` (`STALL_PROBE=1 STALL_CFG_BEFORE=<config>`).
- **Plots (outside the repo):** `~/Developer/agent-lanes/aero-playground/extra-airfoil-AB-xfoil/` (`AB-cl-alpha.png`,
  `AB-separation.png`, `compare_ab.py`); the geometry plots are the lane owner's in `../extra-airfoil-AB/`. Run 5e:
  `../extra-airfoil-v3/` (`ABC-sections.png`, `C-stations.png`, `ABC-cl-alpha.png`, `ABC-separation.png`,
  `compare_abc.py`, `thin_search.py` + `thin_search_log.json`; the correction: `v0012-xfoil-vs-ladson.png`,
  `B-corrected-cl-alpha.png`, `plot_correction.py`).

## 1f. WIRED (Ty, 2026-11-10): cl_max is Ladson's MEASURED 0012 value, and the stall angle follows

Ty, shown §1e's honest result — that the corrected section still carried **0.15–0.30 more cl_max than any measured
symmetric section of its thickness at its Reynolds number**, and that a 0012 cannot tell "the ice-cream-cone really
carries more" from "XFOIL errs more on this shape": *"scale the cl_max to the measured data … XFOIL's optimism on the
ice-cream-cone shape is not evidence."*

So the section's maximum lift is no longer XFOIL's at all.

**The rule** (`extra_wing_table.section_2d`, method `measured`; nothing typed, every constant read from the committed
polars and Ladson's hand-copied table):
1. **The lift slope is still measured** — XFOIL × K(Re), K being Ladson's measured/XFOIL ratio over his linear range
   (4–12.5°). Unchanged from §1e.
2. **cl_max IS Ladson's measured 0012 cl_max at the station's own Reynolds number**, log-interpolated over the three
   free-transition Reynolds numbers whose peak he actually brackets — 2.00 M 1.408, 5.97 M 1.660, 11.90 M 1.617 — and
   held at the 2.00 M value below it. The 3.94 M and 8.86 M rows are **lower bounds** (his run stopped while still
   rising) and are excluded: an interpolation through them would be an interpolation through "at least".
3. **The break angle is where the corrected curve reaches that value**, by bisection on the rising branch. So the
   stall ANGLE is an output of two measured quantities rather than an assertion, and §1e's Δα = 1.287° becomes an
   output too: it comes out **3.47–4.17°** here, i.e. the measured peak is reached that far before XFOIL's own.
4. **After the break, unchanged from §1e**: a trailing-edge station follows XFOIL's own decline moved Δα earlier,
   continuous at the break (Gault types all eight stations trailing-edge).
5. **If the corrected curve never reaches the measured cl_max, the break stays at XFOIL's peak** and the station is
   left at K × XFOIL. The method then says nothing, which is better than scaling a whole polar to force it.

**What this assumes, stated so it can be argued with.** That a 16.2 %-thick ice-cream-cone section cannot carry more
maximum lift than a measured 12 % NACA 0012 at the same Reynolds number. That is a judgement, not a measurement — a
thicker section at these Reynolds numbers usually carries somewhat **more** — so this is the conservative end of the
honest range. It is the end Ty chose; §1e's is the other end, and both are reproducible from the same files.

**Why the 0012 validation could not have found this, and why that matters.** On the 0012 itself the slope correction
alone already lands the peak: K × XFOIL's peak is **1.410 against Ladson's 1.408** at 2.00 M and **1.653 against
1.660** at 5.97 M. On the only section with tunnel data the two methods agree to 0.01 and this one has nothing to
add. The 0.15–0.30 excess is therefore a statement XFOIL makes about *this shape alone*, with nothing to check it
against — which is exactly why believing it was the wrong default.

**The stations** (B, k = 1.0, NCRIT 9, the polars of 90d0e85, unchanged):

| station | Re | XFOIL raw cl_max | Ladson target | **cl_max (= target)** | Δα from XFOIL's peak | α break 2-D | 3-D stall |
|---|---|---|---|---|---|---|---|
| s1 | 3.40 M | 1.844 @ 18.50 | 1.530 | **1.530** | 4.03° | 14.5° | 20.0° |
| s2 | 3.04 M | 1.829 @ 18.25 | 1.505 | **1.505** | 3.97° | 14.3° | 19.0° |
| s3 | 2.70 M | 1.810 @ 18.25 | 1.477 | **1.477** | 4.17° | 14.1° | 18.5° |
| s4 | 2.40 M | 1.789 @ 17.75 | 1.450 | **1.450** | 3.87° | 13.9° | **18.2°** |
| s5 | 2.14 M | 1.771 @ 17.75 | 1.423 | **1.423** | 4.05° | 13.7° | 18.5° |
| s6 | 1.94 M | 1.753 @ 17.50 | 1.408 | **1.408** | 3.86° | 13.6° | 20.3° |
| s7 | 1.80 M | 1.740 @ 17.50 | 1.408 | **1.408** | 3.78° | 13.7° | 26.3° |
| s8 | 1.72 M | 1.730 @ 17.25 | 1.408 | **1.408** | 3.47° | 13.8° | 47.9° |

Every station is now inside the measured band (1.30–1.56), which is what
`SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber` asks. **Mid-span still goes first** — s4 at 18.2°, then s3 18.5°
and s5 18.5°, s2 19.0°, the root s1 20.0°, s6 20.3°, and the tips only at 26.3° and 47.9° — so the spanwise ORDER of
the stall is unchanged from §1e; it has simply moved about four degrees earlier and about 0.17 of cl lower.

**THE WEAKEST LINK IN THIS, named.** The post-break decline is XFOIL's own curve shifted **four** degrees earlier
instead of §1e's 1.3°. Four degrees is a longer reach, and nothing measured says the decline of a section whose peak
is 0.17 lower looks like the decline of XFOIL's optimistic one. It is the same construction as §1e and no worse in
kind, but it is more extrapolation, and if a measured post-stall curve for this shape ever appears this is the part
it should replace.

## 1e. Run 5e result: B at full thickness, with XFOIL's stall corrected against Ladson's 0012 (SUPERSEDED by 1f;
reproducible with `--method r5e`)

Lane owner (run 5e, second instruction): return to the realistic thickness, k = 1.0 (B: 16.19 % root, 12.10 % tip, as
at 90d0e85). Correct XFOIL's late and high cl_max from first principles, validated against Ladson's NACA 0012 (NASA TM
4074): no fudge factor, no cl multiplier, no aiming at the book's Vs.

**1. The validation set** (`tools/aircraft_builder/xfoil_0012_validation.py`; polars `data/xfoil/v0012_*`). XFOIL 6.99,
its own `NACA 0012`, `PPAR N 240`, M 0.15, 0 → 26° in 0.25° steps, with the same harness and retry tiers as the Extra
stations.
- **Free transition:** Ladson's five Reynolds numbers (Table I: 2.00, 3.94, 5.97, 8.86, 11.90 M), each at NCRIT 9, 6,
  4 and 2.
- **Transition fixed at 0.05 c** (`XTR 0.05`) against Table IX (No. 80 grit; 2.00, 3.99, 5.95 M).
- **Ladson's values** are copied by hand into the script as literals. Two are lower bounds: at 3.94 M the table jumps
  from 15.14° to 18.09°, and at 8.86 M the run stops still rising at 18.42°.

**2. What XFOIL gets wrong, and why** (free transition, cl_max and its angle):

| Re | Ladson | XFOIL NCRIT 9 | NCRIT 6 | NCRIT 4 | NCRIT 2 |
|---|---|---|---|---|---|
| 2.00 M | 1.408 @ 15.25° | 1.521 @ 16.50 (+0.113, +1.25°) | 1.549 @ 16.75 | 1.557 @ 16.80 | 1.555 @ 17.00 |
| 3.94 M | ≥ 1.484 @ 15.14° | 1.670 @ 17.75 | 1.679 @ 18.00 | 1.676 @ 18.00 | 1.667 @ 18.00 |
| 5.97 M | 1.660 @ 17.35° | 1.749 @ 18.50 (+0.089, +1.15°) | 1.749 @ 18.50 | 1.744 @ 18.50 | 1.731 @ 18.25 |
| 8.86 M | ≥ 1.693 @ 18.42° | 1.816 @ 19.25 | 1.812 @ 19.00 | 1.808 @ 19.00 | 1.790 @ 18.90 |
| 11.90 M | 1.617 @ 18.04° | 1.865 @ 19.50 (+0.248, +1.46°) | 1.860 @ 19.50 | 1.850 @ 19.25 | 1.836 @ 19.25 |

- **Transition / freestream turbulence is not the cause.**
  - Mack's relation, NCRIT = −8.43 − 2.4 ln(Tu), puts NCRIT 9 at Tu ≈ 0.07 %, NCRIT 6 at 0.25 %, NCRIT 4 at 0.6 % and
    NCRIT 2 at 1.4 %, which runs from XFOIL's "average wind tunnel" (xfoil_doc.txt) to near bypass transition.
  - That whole range moves XFOIL's cl_max by **0.03 or less** at every Re, and never toward Ladson's value at 2 M.
  - It matches Ladson's own result: "fixing transition at the 0.05c location has very little effect on the maximum lift
    coefficient" (TM 4074 p. 4). His tunnel's turbulence "was unknown, but … had increased" (p. 1), so no defensible Tu
    could be put into Mack's relation anyway, and it would not matter.
  - **NCRIT therefore stays 9** for the Extra. That is Mack's value for Tu ≈ 0.07 %, a reasonable calm-air level for a
    GA wing, and cl_max is insensitive to the choice.
- **The tripped runs** (XTR 0.05) stopped converging at 14.0–14.5°, before any stall (tier 1; tiers 2–3 hung and
  were stopped). They give no cl_max and are not used. XFOIL's own documentation warns that forced transition just
  behind a strong suction peak is hard to converge.
- **The cause is XFOIL's boundary-layer closure near separation.**
  - XFOIL's integral boundary layer (lag-dissipation turbulent closure, e^N envelope transition, a short leading-edge
    bubble) keeps the upper surface attached too long. Its trailing-edge separation creeps forward and rounds a peak
    that the real section reaches earlier.
  - At 2 M the real 0012 bursts its leading-edge bubble and loses half its lift in 1°, while XFOIL keeps a short bubble
    through 24° (§3).
  - Drela documents XFOIL's limited validity "just beyond CLmax" (xfoil_doc.txt; Drela, *XFOIL: An Analysis and Design
    System for Low Reynolds Number Airfoils*, 1989).
  - Kallstrom (NASA, 2022, ntrs.nasa.gov 20220000003) reports that XFOIL "cannot accurately model stall and post-stall"
    and over-predicts lift approaching stall.
  - The table above is the quantitative version for this section and these Reynolds numbers.

**3. The correction** (`extra_wing_table.section_2d`, method `r5e`; constants computed by `xfoil_0012_validation.py`
from the committed polars and Ladson's literals, nothing typed in):
- **Break angle:** α_break = α_peak,XFOIL − Δα. **Δα = 1.287°** is the mean of XFOIL's measured lateness over the
  three Ladson cases whose break is tabulated: 2.00 M +1.25°, 5.97 M +1.15°, 11.90 M +1.46°. The standard deviation is
  0.16°.
- **cl at the break:** cl_max = K(Re) · cl_XFOIL(α_break). K is Ladson/XFOIL over Ladson's linear range (4–12.5°):
  0.927 (2.00 M), 0.938 (3.94), 0.945 (5.97), 0.954 (8.86), 0.953 (11.90). This is the measured lift-slope
  over-prediction Ladson reports for theory (7 % at 4 M, 3 % at 12 M), log-interpolated in Re and held at the 2 M value
  below it (st7 and st8 are at 1.80 and 1.72 M).
- **After the break:**
  - A Gault trailing-edge station follows XFOIL's own decline moved Δα earlier: cl(α) = K·[cl_X(α + Δα) − (cl_X,peak −
    cl_X(α_break))], with cd and cm from the same shifted point. The bracket is XFOIL's own rise from the corrected break
    to its late peak (0.01–0.03), subtracted so the curve is continuous. It is not a scale on lift.
  - Any other Gault type falls to Ladson's measured post-break fraction one degree later (run 4, unchanged).
  - **All eight B stations are Gault trailing-edge.**
- **No per-station constant.** Every station uses the same Δα and the same K(Re) table.

**4. Residuals against Ladson** (corrected − Ladson):

| Re | corrected | Ladson | Δcl | Δα | leave-one-out Δα → Δcl, Δα |
|---|---|---|---|---|---|
| 2.00 M | 1.389 @ 15.21° | 1.408 @ 15.25° | **−0.019** | −0.04° | 1.305 → −0.020, −0.05° |
| 3.94 M | 1.539 @ 16.46° | ≥ 1.484 @ 15.14° | (≥ +0.055) | — | consistent with Ladson's tripped 3.99 M: 1.569 @ 17.26° |
| 5.97 M | 1.628 @ 17.21° | 1.660 @ 17.35° | **−0.032** | −0.14° | 1.355 → −0.034, −0.21° |
| 8.86 M | 1.711 @ 17.96° | ≥ 1.693 @ 18.42° | (+0.018) | (stalls ≥ 0.46° early) | — |
| 11.90 M | 1.756 @ 18.21° | 1.617 @ 18.04° | +0.139 | +0.17° | 1.200 → +0.141, +0.26° |

Over the Extra's Reynolds numbers (1.7–3.4 M, bracketed by 2.00 and 3.94 M) the corrected section is within −0.02 of
Ladson in cl_max and within 0.05° in stall angle. At 11.9 M Ladson's peak sits below his own 8.86 M lower bound, a
non-monotonic point the correction does not follow.

**5. Applied to B (k = 1.0, NCRIT 9, the polars of 90d0e85, unchanged):**

| station | Re | XFOIL raw cl_max | raw × K (5d wired) | **corrected cl_max @ α (2-D)** | 3-D stall |
|---|---|---|---|---|---|
| s1 | 3.40 M | 1.844 @ 18.50 | 1.726 | **1.705 @ 17.21°** | 23.4° |
| s2 | 3.04 M | 1.829 @ 18.25 | 1.709 | **1.682 @ 16.96°** | 22.3° |
| s3 | 2.70 M | 1.810 @ 18.25 | 1.689 | **1.667 @ 16.96°** | 22.0° |
| s4 | 2.40 M | 1.789 @ 17.75 | 1.667 | **1.626 @ 16.46°** | 21.3° |
| s5 | 2.14 M | 1.771 @ 17.75 | 1.647 | **1.611 @ 16.46°** | 21.9° |
| s6 | 1.94 M | 1.753 @ 17.50 | 1.629 | **1.585 @ 16.21°** | 23.7° |
| s7 | 1.80 M | 1.740 @ 17.50 | 1.616 | **1.577 @ 16.21°** | 30.3° |
| s8 | 1.72 M | 1.730 @ 17.25 | 1.608 | **1.558 @ 15.96°** | 53.7° |

**The correction barely lowers B's peak** (1–3 % against 5d's raw × K):
- B's XFOIL lift curve is nearly flat over its last 1.3°, and its trailing edge is still fully attached at the
  corrected break (TE separation 0.00 at every station).
- On the 0012 the same 1.3° costs more, because its curve is already rounding over.
- What the correction does move is **where** B stalls: every station breaks 1.3° earlier in 2-D, and the decline
  comes with it.

**6. Whole wing** (strips + lifting line × K):
- **Python strip sum:** A (run-4 rules) 1.446 at 21.5°; B raw × K (5d) 1.642 at 23.3°; **B corrected 1.606 at 22.15°**.
- **Model (StallCharacterProbe, below):** wing-alone CLmax 1.674 at 22.2°.

**7. In the model (StallCharacterProbe; before = run 5d's uncorrected B at 90d0e85):**

| | B uncorrected (5d) | **B corrected (5e, wired)** |
|---|---|---|
| wing static CLmax | 1.719 at 23.35° | **1.674 at 22.20°** |
| lost one / three degrees past | 9 % / 26 % | **15 % / 27 %** |
| steepest 0.05° step | 1.13 /deg | 0.81 /deg (no single-angle cliff) |
| aircraft CL (δe 0) peak | 1.959 at 23.65° | 1.900 at 22.30° |
| **1-g Vs** (`EstimateVso`, trimmed idle, 820 kg) | 50.5 KEAS | **51.5 KEAS** (book 55 KIAS: −6.4 %) |
| 1 kt/s feet flat: break | 54.1 KEAS, n 1.12 (1-g 51.1), α 23.6° | 55.2 KEAS, n 1.14 (1-g 51.7), α 22.2° |
| – CL lost | 24 % in 3.13 s; 0.086 in the first 0.25 s | **18 % in 2.27 s (0.13 /s); 0.027 in the first 0.25 s** |
| – 2 s after: bank, max roll rate, Δθ | +35°, 67 °/s, −24° | **+24°, 41 °/s, −11°** |
| 0.1 rudder held / 0.05 aileron at the break: bank | +15° / +39° | +32° / +27° |
| accelerated (1.8 Vs, full aft) | not measured (detector) | not measured (the detector fired at α 6.4°, n 1.21: tail download, §1c) |
| spin entry: developed, 90 % at | 146 °/s, 4.67 s | 147 °/s, 6.21 s |
| snap at 90 KEAS | 180 °/s at 1.72 s, peak 207 | 180 °/s at 1.66 s, peak 208 |
| developed spin | 148 °/s, 2.43 s/turn, 308 ft | 148 °/s, 2.44 s/turn, 316 ft |
| recovery PARE / BM | 0.4 t / 0.5 t | 0.5 t (1.1 s) / 0.5 t (1.1 s) |
| roll at VA steady / avg 360 | 509 / 414 °/s | 507 / 414 °/s |

**Spanwise:** mid-span first, s4 (y 2.5 m) at 21.3°. s5 follows at 21.9°, s3 at 22.0°, s2 at 22.3°, the root s1 at
23.4° and s6 at 23.8°; the tips go only at 30.3° and 53.8°.

**8. Against the book and the measured band (honest result, nothing aimed at either):**
- **1-g Vs 51.5 KEAS against 55 KIAS (−6.4 %).** `OneGStallSpeedMatchesTheInformationManual` (52.3–57.8) stays
  **red**, by 0.8 kt.
- **Section cl_max 1.558–1.705.** `SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber` (1.30–1.56) stays **red**:
  s8 is inside the band, s1–s7 are above it.
- **What remains:**
  - The correction removes the bias XFOIL shows on the 0012 (Δcl −0.02 at 2 M after correction). B's corrected peak is
    still ~0.15–0.30 above any measured symmetric section of this thickness at this Re.
  - Either the "ice cream cone" shape really does carry a higher cl_max than the 0012-like sections in the band (XFOIL
    says so; no tunnel data exists for it), or XFOIL's error on this shape is larger than on the 0012.
  - The 0012 cannot tell those two apart. **NEEDS YOU.**
- **The break is steep but not a cliff.**
  - 2-D: the trailing-edge separation goes from none to 0.3–0.77 c within one degree past the corrected break.
  - Wing: −15 % one degree on, −27 % three degrees on.
  - It is softer at the first instant than run 4's Ladson cliff or the rejected thin C (−18 % / −41 %), because Gault
    places B's blunt nose in the trailing-edge band.


**9. Tests (run 5e).**
- **Baseline:** 90d0e85 run from a detached temp worktree (removed afterwards), under the no-contention rule: 135
  failed / 1179 passed / 5 skipped (`/private/tmp/run5e-base-90d0e85.trx`).
- **After:** 206 / 1108 / 5 (`/private/tmp/run5e-after.trx`). The count means little: the suite is globally coupled,
  CLAUDE.md.
- **Newly failing against 90d0e85: 83 names, every class re-run solo, all pass:**
  - FloatsOnLand 2/2, MultiEngine 3/3, NoseSkid 1/1;
  - SpawnSpeed 34/34;
  - Structural 40/40 (39 of them newly failing);
  - TipWheel 3/3;
  - GroundTow 1 passed / 2 skipped by the test itself.
- **No real regression.**
- **Newly passing:** LandingGear ×6, SpinTests ×2 (mirror symmetry, TOM recovery), and the `PropGyroTests` push case.
  With the corrected B the 3 s push no longer departs, so the gyro is isolated again: −4.40°, band 2–15°.
- **Still red, unchanged since 90d0e85:**
  - `ExtraWingSectionTests.OneGStallSpeedMatchesTheInformationManual` (51.5 KEAS) and
    `SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber` (s1–s7 above 1.56), as above;
  - the Extra's three ground-handling bands (lift-off 43.9 m/s, rolls 413 / 583 m; run 5d's analysis);
  - the Pitts landings / feet test and `CreditsReachTheUser`, which were red before this lane's run 5.
- The glide table (Extra row: 52 kt Vso, 68 kt, 7.1:1) was regenerated and passes.
- **Build:** Unity iOS export 0 `error CS`; xcodebuild `** BUILD SUCCEEDED **`.

## 1d. EXPERIMENT, REJECTED: thinning B until the 1-g stall is the book's (run 5e, first part)

> **Rejected by the lane owner, not wired.** k = 0.6625 means a 10.7 % root and an 8.0 % tip. An 8 % tip is not a
> realistic Extra section. The wired wing is B at full thickness (k = 1.0) with the Ladson-validated stall correction
> (§1e). The sweep is kept here because it shows how far thickness alone would have to move to reach the book's Vs.
> Its polars and plots are outside the repo, in `~/Developer/agent-lanes/aero-playground/extra-airfoil-v3/`
> (`k-sweep-k0.6625/`, `k-sweep-iterates/`, `thin_search.py`, `thin_search_log.json`, `ABC-*.png`). A fourth iterate,
> k = 0.6884, was started by the search's own 0.3 kt tolerance and stopped by the time limit with no result.

Ty (run 5e, first instruction): thin the B sections until the 1-g stall matches the Information Manual, 55 KIAS at
820 kg (§5.4, ACRO I, idle).

1. **One geometric parameter, chosen to match the book.** The real MA 15 S / MA 12 S coordinates are unpublished, so
   their thickness is unknown. Section **thickness** is therefore the one dimension *chosen* so the trimmed 1-g stall
   matches the POH Vs.
   - It is a geometric choice, recorded as exactly that. It is **not a fudge on the aerodynamics**: no cl or CL
     multiplier, no table edit, no Re / NCRIT / Mach change.
   - The aerodynamics stay XFOIL × K, the Gault classifier and the lifting line, all exactly as in run 4 and run 5d.
2. **C = B × k, y only, one k for root and tip.** That keeps B's shape family exactly: x(t max) 17.0 % c root and
   15.5 % c tip, the 16.19 : 12.10 root-to-tip ratio, the same inflection points and the concave aft skin.
   - **The aft dip scales with k** (0.30 → 0.20 % c) rather than being re-solved at 0.30 % c. A pure y-scale is the
     one thing "one common thickness factor" can mean without a second shape parameter. Re-solving the dip would move
     the shoulder and the inflection point, so B's shape would change as well as its thickness.
   - LE radius scales with k², y(1.25 %) and Δy with k.
   - Stations blend C-root and C-tip with A's weights, so each C station is B's station × k.
3. **The search** (secant on k; each iterate = XFOIL at all 8 stations, tables, config, `EstimateVso`):

   | k | root t/c | tip t/c | section cl_max (×K) | Gault types | wing CLmax @ α | **1-g Vs** |
   |---|---|---|---|---|---|---|
   | 1.0000 (B) | 16.19 % | 12.10 % | 1.608–1.726 | all TE | 1.719 @ 23.35° | 50.5 KEAS |
   | 0.8500 | 13.76 % | 10.29 % | 1.469–1.610 | s1–s3 TE, s4–s8 TE/comb | 1.596 @ 21.70° | 52.5 KEAS |
   | 0.6625 | **10.73 %** | **8.02 %** | **1.357–1.526** | s1–s7 LE/comb, s8 LE | **1.466 @ 19.80°** | **55.4 KEAS** |

   - **Stopped at 55.4 KEAS (+0.4 kt).** The driver's internal tolerance of 0.3 kt had started a fourth iterate, which
     was stopped by the time limit. 0.4 kt is inside Ty's "~1 kt".
   - k = 0.6625 was then re-run cleanly. XFOIL is deterministic: the up polars are byte-identical to the search's.
   - Vs is not linear in k, because the thinner sections cross Gault's boundaries. At 0.85 the outer stations are
     trailing-edge / combined (break at s_b = 0.15 c). At 0.6625 every station is leading-edge / combined or leading-edge
     (break at Ladson's measured 0.071–0.083 c, falling to Ladson's 0.53–0.57 of the peak one degree later).
4. **What k = 0.6625 means.** Root 10.7 %, tip 8.0 %: well under what "MA 15 S / MA 12 S" suggests if the number is the
   thickness (ASSUMED in §1).
   - Read as data: a section *this* shape needs to be that thin for XFOIL and Gault to give the book's stall speed.
   - Equally possible: the real wing loses lift that a smooth 2-D section does not carry (roughness, the fuselage
     junction, the real aerofoil being less optimistic than XFOIL near the peak).
   - The thickness absorbs all of that, so **t/c is a calibrated parameter, not a measurement** (NEEDS YOU).
5. **XFOIL, if anything, makes the break sharper on C than on B.** Raw 2-D, identical settings:

   | | cl_max | lost +1° | lost +3° | worst /deg | Gault (y1.25 % c) | TE separation 16/17/18/19° |
   |---|---|---|---|---|---|---|
   | A st1 / st4 / st8 | 1.578 / 1.579 / 1.583 | 1 / 1 / 1 % | 15 / 17 / 26 % | −0.14 / −0.16 / −0.27 | TE (2.35) / TE (2.28) / TE-comb (2.19) | .04/.19/.43/.65 creeping |
   | B st1 / st4 / st8 | 1.844 / 1.789 / 1.730 | 7 / 2 / 2 % | 28 / 27 / 29 % | −0.32 / −0.30 / −0.34 | TE (2.75 / 2.61 / 2.42) | .00/.00/.00/.28, then 0.7 by 20° |
   | **C** st1 / st4 / st8 | **1.633 / 1.568 / 1.465** | **6 / 12 / 21 %** | **28 / 29 / 29 %** | **−0.29 / −0.31 / −0.34** | **LE-comb (1.82) / LE-comb (1.73) / LE (1.61)** | st1 .00/.19/.79; st4 .00/.80; st8 at 0.8 by 16° |

   (Full 8-station table: `extra-airfoil-v3/compare_abc.py` output.) C's peak comes 1.75–2.25° earlier than B's.
   Its trailing-edge separation goes from nothing to 0.6–0.8 c within one degree. It carries a short leading-edge
   bubble, which XFOIL never bursts.
6. **The whole wing** (strips + lifting line × K; "raw" = XFOIL's own decline at every station, "wired" = the Gault
   pipeline):

   | | CLmax @ α | lost +1° | lost +3° | worst /deg |
   |---|---|---|---|---|
   | A (wired) | 1.446 @ 21.5° | 2 % | 22 % | −0.18 |
   | B (wired = raw) | 1.644 @ 23.3° | 10 % | 28 % | −0.26 |
   | C raw | 1.422 @ 20.1° | 10 % | 26 % | −0.14 |
   | **C (Gault pipeline)** | **1.411 @ 19.75°** | **19 %** | **44 %** | **−0.37** |

7. **In the model, C (StallCharacterProbe; before = run 5d's B at 90d0e85):**

   | | B (5d) | **C (experiment, k 0.6625)** |
   |---|---|---|
   | wing static CLmax | 1.719 at 23.35° | **1.466 at 19.80°** |
   | lost one / three degrees past | 9 % / 26 % | **18 % / 41 %** |
   | steepest 0.05° step | 1.13 /deg | **2.66 /deg** (the stations fold into cliffs) |
   | aircraft CL (δe 0) peak | 1.959 at 23.65° | 1.633 at 19.80° |
   | **1-g Vs** (`EstimateVso`) | 50.5 KEAS | **55.4 KEAS** (book 55 KIAS) |
   | 1 kt/s feet flat: break | 54.1 KEAS, n 1.12, α 23.6° | 59.8 KEAS, n 1.17 (1-g 55.3), α 19.8° |
   | – CL lost | 24 % in 3.13 s | **28 % in 0.94 s** (0.45 /s), 0.067 in the first 0.25 s |
   | – 2 s after: bank, max roll rate, Δθ | +35°, 67 °/s, −24° | **+42°, 63 °/s, −20°** |
   | 0.1 rudder held / 0.05 aileron at the break | +15° / +39° | +49° / +47° |
   | accelerated (1.8 Vs, full aft) | not measured (detector) | **104.4 KEAS at 3.56 g, α 20.2°; 41 % lost in 0.98 s, 0.377 in the first 0.25 s; +24° bank** |
   | spin entry: developed, 90 % at | 146 °/s, 4.67 s | 148 °/s, 6.36 s |
   | snap at 90 KEAS | 180 °/s at 1.72 s, peak 207 | 180 °/s at 1.68 s, peak 202 |
   | developed spin | 148 °/s, 2.43 s/turn, 308 ft | 150 °/s, 2.40 s/turn, 332 ft |
   | recovery PARE / BM | 0.4 t / 0.5 t | 0.3 t (1.2 s) / 0.3 t (0.9 s) |
   | let-go idle / full power peak α | 29° / 46° | 37° / 34° |
   | roll at VA steady / avg 360 | 509 / 414 °/s | 495 / 407 °/s |

   **Spanwise:** the mid-span goes first, s5 (y 3.1 m) at 19.8°, s4 at 20.1°, s3 at 20.2°. s2 follows at 20.8°, s6 at
   21.7° and the root s1 at 22.3°; the lightly loaded tips go only at 27.6° and 49.2°. Each station is a single-angle
   cliff in the finite wing: 52–55 % of its peak is left one degree on, because its 2-D drop is steeper than 1/kInd.
8. **With C (not wired), these checks were green:**
   - `OneGStallSpeedMatchesTheInformationManual`: 55.4 KEAS in 52.3–57.8.
   - `SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber`: 1.357–1.526 in 1.30–1.56.
   - `PropGyroTests` push case: passes again. The push no longer departs.
9. **XFOIL robustness at these thicknesses.** st1–st3 converged at tier 1. st4–st7 needed tier 3 (N 360, 0.05°,
   ITER 800) and st8 tier 2, so their panelling differs from A and B. The break region of every station is fully
   resolved; the gaps that remain are post-stall (the last converged points stop near 23–24°, and st8 has a hole at
   21.5–24.1°). The down sweeps of st5–st8 did not converge (they are not used by the tables). No hysteresis is
   claimed from them.
   - The harness now drops a boundary-layer dump row whose Fortran fields have run together, instead of crashing.
   - The down sweep now climbs at the tier's own step.
   - A's and B's committed polars are unchanged; their down sweeps were run with the older 0.5° climb.

## Run 5d result (B, uncorrected XFOIL; superseded by §1e)

1. **Two candidate wings, both from the published Eppler E473 (root) / E472 (tip), UIUC database (§1).**
   - **A**, "modified Eppler stand-in": both sections unscaled, 16.19 % / 12.10 %, blended per station.
   - **B**, the Extra-style "ice cream cone": the lane owner's geometric reshape of the same two sections (`ab_sections.py`,
     ported). It keeps the thickness, moves the maximum thickness forward to 17.0 % c (root) and 15.5 % c (tip), and
     gives a 0.30 % c concave aft run. **B is not a published section.**
2. **XFOIL agrees with Ty: B stalls more sharply than A, at every station and on the whole wing (§1b).** All settings
   identical (NCRIT 9, free transition, M 0.083, Re = c × 1.937·10⁶). All 16 cases converged at the first tier,
   st8 included.
   - B's trailing-edge separation stays under 0.06 c up to 18° (2-D), then jumps to 0.28–0.77 c within one degree. A's
     creeps forward over three to four degrees.
   - The worst one-degree slope past the peak is −0.29 to −0.34 per degree for B, against −0.14 to −0.27 for A. Three
     degrees past the peak B has lost 27–29 %, A 15–26 %.
   - Whole wing (strips + lifting line, × K): **B CLmax 1.644 at 23.3°, −10 % one degree on, −28 % three degrees on**.
     A is 1.446 at 21.5°, −2 % and −19 %.
   - XFOIL also gives B a higher peak than A (section cl_max 1.73–1.84 against 1.58) and a later one (+0.5 to +1.75°).
3. **Gault's correlation points the other way.** B's blunter nose raises y(1.25 %) to 2.42–2.75 % c, inside Gault's
   *trailing-edge* band at every station. A's outer three stations are trailing-edge / combined. The break in XFOIL is
   still a trailing-edge separation that runs forward fast. **It is not a leading-edge bubble burst**: B carries a short
   bubble from 12° and it never bursts. So XFOIL's "sharper" is a fast trailing-edge collapse, which Gault's figure does
   not resolve.
4. **B is wired** (config + StreamingAssets mirror). Gault types every B station trailing-edge, so the shipped table is
   XFOIL's own peak and decline × K at every station. That is the same as the like-for-like comparison: no break model,
   no fudge factor.
5. **In the model (StallCharacterProbe, §1c)**, compared with the run-4 config:
   - **Static wing:** CLmax 1.719 at 23.35°, against 1.436 at 20.35°.
   - **The drop:** −9 % one degree on and −26 % three degrees on, against −18 % / −21 %. **There is no single-angle
     cliff any more**: the steepest 0.05° step is 1.13 per degree, against run 4's 3.77.
   - **Spanwise order:** s4 (mid-span) goes first at 22.7°; s3 and s5 at 23.3°, s2 at 23.6°, the root s1 at 24.7°,
     s6 at 25.2°, the tips at 32° and 56°.
   - **1 kt/s, feet flat:** breaks at α 23.6°. It loses 24 % of CL over 3.1 s, 0.086 of it in the first quarter second
     (run 4: 0.143). 2 s after the break: **+35° of bank**, 67 °/s.
   - **1-g stall speed: 50.5 KEAS against the book's 55 KIAS (−8 %).**
6. **Red because of B, and why (NEEDS YOU):**
   - `ExtraWingSectionTests.OneGStallSpeedMatchesTheInformationManual`: 50.5 KEAS, outside 52.3–57.8.
   - `ExtraWingSectionTests.SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber`: section cl_max 1.61–1.73, outside the
     measured band 1.30–1.56.
   - XFOIL's cl_max for B is above every measured 12–16 % symmetric section at these Reynolds numbers: Ladson's 0012
     1.41 at 2 M, McCullough & Gault's 12 % and 18 % sections 1.38 at 5.8 M. The book's Vs needs aircraft CL ≈ 1.51.
     Nothing was tuned to close the gap.

## 1. The two candidate sections

| | Source | |
|---|---|---|
| root "MA 15 S", tip "MA 12 S" | Extra 300L Information Manual §1.3.3 (page date 2002-04-20); same pair in the 330LX and Extra 200 manuals; Lednicer, *The Incomplete Guide to Airfoil Usage* (UIUC, 2026-04-04): "Extra 230 / 300 / 330: MA 15S, MA 12S" | verified, source text |
| symmetrical, zero incidence | Jane's 1993 p. 100 via Wikipedia | secondary |
| span 8.00 m; area 10.84 m²; root 1.85 m, tip 0.88 m; MAC 1.404 m | EASA TCDS A.362 iss. 09 (area); Information Manual §1.3.3 (chords; it gives 10.7 m²) | verified |
| **E472 / E473 coordinates** | Eppler aerobatic-aircraft airfoils, UIUC Airfoil Coordinates Database via airfoiltools.com, `seligdatfile?airfoil=e472-il` / `e473-il`, fetched 2026-11-02 | **published**, copied verbatim |
| "ice cream cone": t max ~17 % c, slightly concave aft skin | Ty (owner, Extra pilot), 2026-11-02 | owner |
| B's reshape algorithm and its parameters | the lane owner's `extra-airfoil-AB/ab_sections.py` | owner |

No coordinates for MA 15 S / MA 12 S were found anywhere: UIUC (`ma15s.dat` / `ma12s.dat` 404), airfoiltools, three
Extra manuals, the EASA TCDS. Both data files are byte-identical to the downloads (SHA-256 in each `.SOURCE.txt`) and
symmetric to the last digit. Run 5c's 15 % scaling of the root is **undone** (Ty, run 5d).

**Interpolation.** Each published surface is a natural cubic spline of y against u = √x. A round nose is y ≈ a·u near the
leading edge, so the spline is smooth where y(x) is not. The natural end condition y''(u) = 0 is the circle's own, so the
leading-edge radius is r = a²/2.

**A** is E473 at the root and E472 at the tip, unscaled. **Stations:** the wing is ruled, so chord and *absolute*
thickness are linear in span: t/c(y) = (0.16190·1.85·(1 − e) + 0.12101·0.88·e)/c(y), with e = y/4. Each station is the
blend (1 − w)·h_root + w·h_tip of the half-thickness on a common cosine x grid (160 panels a surface). w is solved so the
blend's own maximum thickness is the ruled t/c.

**B** is the lane owner's algorithm, applied to A's root and tip:
1. **x-warp** x' = x + c·x(1 − x), c = (x_new − x_old)/(x_old(1 − x_old)). This moves the maximum thickness exactly. Near
   the leading edge x' ≈ (1 + c)x, so the LE radius grows by 1/(1 + c).
2. **Aft sag** −A·sin²(πu), u = (x' − x_new)/(1 − x_new), aft of the maximum only. A is solved so the surface dips
   0.30 % c below the straight line from the maximum-thickness point to the trailing edge.
3. **Rescale** to the base t/c.

B's stations blend B-root and B-tip with **A's weights**, so B's station t/c is within 0.015 % of A's.
- **Parameters (lane owner):** x_new 0.170 at the root, 0.155 at the tip; depth 0.0030.
- **Results:** root c = −0.2522, A = 0.00632; tip c = −0.1745, A = 0.00880.
- **Two differences from `ab_sections.py`, so that A and B differ only by the reshape:**
  - the base is A's smooth spline. `ab_sections` interpolates the 61 points linearly, which makes a faceted nose and
    puts E472's maximum at the data point, 17.5 % c, against the spline's 18.1 %;
  - the warped section is evaluated exactly, not re-interpolated on the grid.
- **Effect against the owner's numbers:** root 16.19 % at 17.0 % c, y(1.25 %) 2.77 (owner: 16.18 % at 16.8 %, 2.74);
  tip 12.10 % at 15.5 % c, y(1.25 %) 2.42 (owner: 12.10 % at 15.3 %, 2.32). The tip differs most, because it starts
  further aft (18.1 % against 17.5 %) and so is warped more.

| | w | A t/c | A x(t max) | A LE r | A y(1.25 %) | A Δy | B t/c | B x(t max) | B LE r | B y(1.25 %) | B Δy |
|---|---|---|---|---|---|---|---|---|---|---|---|
| root | 0 | 16.19 % | 21.2 % | 1.76 | 2.36 | 4.72 | 16.19 % | 17.0 % | 2.36 | 2.77 | 5.31 |
| st1 | 0.048 | 15.99 | 21.2 | 1.75 | 2.35 | 4.69 | 15.99 | 17.0 | 2.33 | 2.75 | 5.26 |
| st2 | 0.160 | 15.53 | 21.0 | 1.73 | 2.33 | 4.61 | 15.53 | 16.9 | 2.27 | 2.71 | 5.14 |
| st3 | 0.292 | 14.98 | 20.8 | 1.70 | 2.31 | 4.52 | 14.99 | 16.7 | 2.21 | 2.67 | 5.01 |
| st4 | 0.443 | 14.36 | 20.4 | 1.66 | 2.28 | 4.42 | 14.37 | 16.5 | 2.13 | 2.61 | 4.86 |
| st5 | 0.607 | 13.69 | 20.0 | 1.63 | 2.25 | 4.31 | 13.70 | 16.3 | 2.05 | 2.55 | 4.69 |
| st6 | 0.767 | 13.03 | 19.4 | 1.59 | 2.22 | 4.20 | 13.05 | 16.0 | 1.98 | 2.50 | 4.53 |
| st7 | 0.900 | 12.50 | 18.7 | 1.56 | 2.20 | 4.10 | 12.51 | 15.8 | 1.91 | 2.45 | 4.39 |
| st8 | 0.977 | 12.19 | 18.2 | 1.55 | 2.19 | 4.05 | 12.19 | 15.6 | 1.88 | 2.42 | 4.31 |
| tip | 1 | 12.10 | 18.1 | 1.54 | 2.18 | 4.04 | 12.10 | 15.5 | 1.87 | 2.42 | 4.29 |

LE radius, y(1.25 %) and Δy = y(6 %) − y(0.15 %) are in % c. NACA 0015: 2.48, 2.37, 3.95; NACA 0012: 1.59, 1.89, 3.16.
B's aft run is concave by 0.30 % c at every station (A: 0). B's trailing-edge angle is 10.1–10.4°, A's 11.9–13.0°.

## 1b. A against B in XFOIL

Settings, identical for both: XFOIL 6.99, `PPAR N 240`, `VISC` at the station's Re (c × 1.937·10⁶, the 1-g stall at
55 KIAS and 820 kg), `MACH 0.083`, NCRIT 9, free transition, `ITER 300`.
- **Up sweep:** 0 → 26° in 0.25° steps, each point started from the last, with the boundary-layer `DUMP`s taken on that
  same sweep at 8, 10, 12–26°.
- **Down sweep:** a separate process, 26 → 0°.
- **Robustness:** every XFOIL process has a timeout and runs in a pool, with retry tiers 2 (N 300, 0.1°, ITER 500) and
  3 (N 360, 0.05°, ITER 800) on a hang or a gap > 1°. **No case needed a retry.** The widest gap between converged
  points is 0.75° for A and 1.0° for B st8; the unconverged alphas are listed in `xfoil-ab-runlog.json`.
- **Hysteresis:** none. Up and down agree to 0.018 or better.

| station | Re | A cl_max @ α | B cl_max @ α | A lost +1° / +3° | B lost +1° / +3° | A worst /deg | B worst /deg | A Gault (y1.25) | B Gault (y1.25) | A TE-sep 16/17/18/19° | B TE-sep 16/17/18/19° |
|---|---|---|---|---|---|---|---|---|---|---|---|
| st1 | 3.40 M | 1.578 @ 16.75 | **1.844 @ 18.50** | 0.9 / 15.1 % | 6.9 / 27.9 % | −0.137 | **−0.316** | TE (2.35) | TE (2.75) | .04/.19/.43/.65 | .00/.00/.00/.28 |
| st2 | 3.04 M | 1.578 @ 17.00 | 1.829 @ 18.25 | 1.8 / 17.2 | 4.1 / 27.3 | −0.141 | −0.315 | TE (2.33) | TE (2.71) | .03/.17/.43/.66 | .00/.00/.00/.37 |
| st3 | 2.70 M | 1.577 @ 17.00 | 1.810 @ 18.25 | 1.7 / 17.8 | 6.9 / 27.9 | −0.150 | −0.288 | TE (2.31) | TE (2.67) | .03/.16/.43/.67 | .00/.00/.01/.50 |
| st4 | 2.40 M | 1.579 @ 16.75 | 1.789 @ 17.75 | 1.1 / 17.3 | 1.7 / 26.5 | −0.163 | −0.304 | TE (2.28) | TE (2.61) | .02/.15/.41/.69 | .00/.00/.01/.57 |
| st5 | 2.14 M | 1.580 @ 16.75 | 1.771 @ 17.75 | 1.1 / 19.3 | 3.2 / 27.6 | −0.185 | −0.316 | TE (2.25) | TE (2.55) | .01/.14/.41/.70 | .00/.00/.02/.65 |
| st6 | 1.94 M | 1.581 @ 16.75 | 1.753 @ 17.50 | 1.2 / 22.1 | 2.1 / 27.7 | −0.210 | −0.319 | TE/comb (2.22) | TE (2.50) | .01/.11/.41/.73 | .00/.00/.04/.70 |
| st7 | 1.80 M | 1.583 @ 16.75 | 1.740 @ 17.50 | 1.3 / 24.2 | 4.3 / 29.0 | −0.237 | −0.322 | TE/comb (2.20) | TE (2.45) | .00/.10/.40/.77 | .00/.00/.05/.74 |
| st8 | 1.72 M | 1.583 @ 16.75 | 1.730 @ 17.25 | 1.4 / 25.7 | 1.6 / 28.6 | −0.266 | −0.339 | TE/comb (2.19) | TE (2.42) | .00/.10/.40/.79 | .00/.00/.06/.77 |

The cl values are raw XFOIL (no K). "worst /deg" is the steepest one-degree drop within 5° past the peak.
"TE-sep" is the fraction of the upper surface with Cf < 0 from the trailing edge, measured over x > 0.2 c, so it caps at
0.8. A short leading-edge bubble is present on B from 12° and on both after the break; it never bursts (no Cf < 0
forward of 0.2 c beyond it).

**Whole wing**, the run-4 method with XFOIL's own decline at every station (× K, the lifting-line induced angle of each
station, wing CLα 4.52 /rad from DATCOM, strips area-weighted):

| | CLmax @ α | lost +1° | lost +3° | worst /deg | first station to stall, then … |
|---|---|---|---|---|---|
| A | 1.446 @ 21.50° | 1.7 % | 19.0 % | −0.155 | s4 21.2°, s3 21.4°, s2 21.5°, s1 21.8°, s5 21.8°, s6 23.8°, s7 29.9°, s8 52.5° |
| **B** | **1.644 @ 23.30°** | **10.3 %** | **28.5 %** | **−0.261** | s4 22.7°, s5 23.3°, s3 23.3°, s2 23.6°, s1 24.7°, s6 25.2°, s7 31.9°, s8 56.2° |

**Does XFOIL agree with Ty?** On sharpness, yes. B holds a fully attached trailing edge 1–2° longer, then loses it in
one degree. As a wing it loses 10 % of its lift one degree past the peak, where A loses 2 %. On the mechanism, only in
part. The collapse is a trailing-edge separation running forward fast, not the leading-edge burst Ty described, and by
Gault's measured correlation B's blunter nose belongs further into the trailing-edge band, not out of it. XFOIL cannot
model a bubble burst (§3), so neither result settles whether the real wing's last step is a burst.

**Wired: B.** Gault types every B station trailing-edge, so the table pipeline uses XFOIL's own decline at all eight. The
shipped tables are exactly the like-for-like B above, with no break model.

## 1c. B in the model (StallCharacterProbe; before = the run-4 config at c3580e3)

| | run 4 (invented section, Ladson break model) | **run 5d, candidate B** |
|---|---|---|
| wing static CLmax, α | 1.436 at 20.35° | **1.719 at 23.35°** |
| wing one / three degrees past | −18 % / −21 % | **−9 % / −26 %** |
| steepest 0.05° step | 3.77 /deg (the lifting-line fold: a cliff) | 1.13 /deg (no fold) |
| aircraft CL (δe 0) peak | 1.630 at 20.4° | 1.959 at 23.65° |
| **1-g Vs, trimmed idle (`EstimateVso`)** | 55.4 KEAS | **50.5 KEAS (book 55 KIAS, −8 %)** |
| 1 kt/s feet flat: break | 59.6 KEAS, n 1.14 (1-g 55.8), α 20.4°, CLeff 1.469 | 54.1 KEAS, n 1.12 (1-g 51.1), α 23.6°, CLeff 1.751 |
| – CL lost | 23 % in 1.10 s; 0.143 in the first 0.25 s | 24 % in 3.13 s; **0.086** in the first 0.25 s |
| – 2 s after: bank, max roll rate, Δθ | +41°, 52 °/s, −12° | **+35°, 67 °/s, −24°** |
| 1 kt/s, 0.1 rudder held: bank, roll rate | +37°, 57 °/s | +15°, 34 °/s |
| 0.05 aileron at the break: bank, roll rate | +42°, 52 °/s | +39°, 69 °/s |
| accelerated (1.8 Vs, full aft in 1 s) | 104.4 KEAS at 3.46 g, α 20.6° | **not measured** (below) |
| spin entry from the 1 kt/s break | 155 °/s, 90 % at 5.21 s | 146 °/s, 90 % at 4.67 s |
| snap at 90 KEAS, full power | 180 °/s at 1.79 s, peak 195 °/s | 180 °/s at 1.72 s, peak 207 °/s |
| developed spin | 153 °/s, 2.36 s/turn, 330 ft/turn, α 38° | 148 °/s, 2.43 s/turn, 308 ft/turn, α 44° |
| recovery PARE / Beggs-Mueller | 0.4 t (2.0 s) / 0.3 t (1.2 s) | 0.4 t (1.0 s) / 0.5 t (1.0 s) |
| pitch step q̇₀ (entered at a multiple of Vs) | 159.1 °/s² | 126.7 °/s² (the lower Vs: (50.5/55.4)² × 159 = 132) |
| let-go: idle / full power peak α | 29° / 40° | 29° / 46° |
| full-aileron roll at VA, steady / avg 360 | 504 / 409 °/s | 509 / 414 °/s |

**How the break happens spanwise.** Mid-span goes first (s4, y 2.5 m, 22.7°). s3 and s5 follow within 0.6° and s2 at
23.6°. The root follows a degree later and the outer strips 2.5° later; the lightly loaded tips go only at 32° and 56°.
- **The roll-off** at idle is the prop's asymmetry picking the side: +35° in 2 s with the feet flat. A tenth of rudder
  held cuts it to +15°.
- **Against run 4:** B's wing loses more of its lift three degrees past the peak. But it has no single-angle cliff, and
  in the 1 kt/s entry it sheds lift more slowly in the first quarter second (0.086 against 0.143). Run 4's mid-span cliff
  came from imposing Ladson's measured leading-edge break on Gault-"combined" stations. B's stations are all Gault
  trailing-edge, so they fly XFOIL's own (faster) trailing-edge collapse instead.
- **Accelerated entry, not measured.** The probe calls the break when the aircraft's effective CL falls 10 % while α
  still rises. With B it fired at α 6.7° and n 1.21, 0.49 s into the pull. That is the tail download building faster
  than the wing lift, not a stall: none of the eight tables has a falling segment below 16°. The probe needs a
  wing-only CL for this entry. Left as a GAP.

**The stall speed.** The book's 55 KIAS at 820 kg needs aircraft CL 1.513 (§8). B's XFOIL peaks (×K 1.61–1.73) give
1.75–1.96, hence 50.5 KEAS. No measured section of this thickness reaches those peaks at these Reynolds numbers (§8).
Either XFOIL over-predicts B's peak (it over-predicts the 0012's by 5–8 %, §3), or the real wing's peak is lower for
reasons a smooth 2-D section does not carry. **Nothing was tuned** (NEEDS YOU).

**Tests (run 5d).** Full suite: 172 failed / 1142 passed / 5 skipped of 1319 (`/private/tmp/run5-after.trx`), against
run 4's 156 and run 3's 126. The failure count means little on its own (CLAUDE.md: the suite is globally coupled), so
here are the unique names.
- **Newly failing against run 4: 16.** Each was re-run alone, class by class:
  - **Coupling, pass alone (11):** NoseSkid ×1, ReversibleControl ×5, Soaring ×1, Spin ×2 (mirror symmetry, TOM
    recovery), TipWheel ×2, TrimConvergence ×1.
  - **Glide table, stale by design:** the Extra's row (Vso 56 → 52 kt, 6.9 → 7.2:1). Regenerated
    (`REGEN_GLIDE_TABLE=1`); it passes. `configs/glide-table.json` and `docs/GLIDE-TABLE.md` change in the Extra's row only.
  - **Real, caused by B, left red:**
    - `ExtraWingSectionTests.OneGStallSpeedMatchesTheInformationManual` (50.5 KEAS);
    - `ExtraWingSectionTests.SectionClMaxIsInTheMeasuredBandForItsReynoldsNumber` (1.61–1.73 > 1.56);
    - `PropGyroTests.ThePullSwervesRightAndThePushLeft(stick: 1, sense: -1)`: the push swerve is 1.26° against 2–15°.
      The gyro moment is unchanged (mean −497 N·m; its unit test passes). With B's higher negative peak, the 3 s
      full-forward push at 100 KIAS reaches −5.4 g and departs with ~19 °/s of yaw rate both with and without the gyro.
      The with-minus-without difference is lost inside that departure. The test's flight condition no longer isolates
      the gyro on this wing. Not changed (NEEDS YOU).
- **Newly passing against run 4:** none.
- **Against run 3, also newly failing:**
  - the three Extra ground-handling bands (below);
  - `SpawnSpeedTests` ×34: all pass when run alone;
  - `GroundTowTests.TugReleasesWhenGliderClimbsTooHigh`: `GroundTowTests` passes alone (1 passed, 2 skipped by the
    test's own guards).
- **Against run 3, newly passing:** LandingGearTests ×6.
- **Already failing before run 5d** (run 3 or run 4 lists): Pitts landings ×2, `PittsNeedsQuickFeetTheExtraForgivesRelaxedOnes`,
  `SpinTests.ProSpinDepartsAndSustainsRotation`, `DronePilotTests.AerobaticDroneFliesVariedManoeuvresAndSurvives`,
  `GlideTableTests.CreditsReachTheUser`.

**The three Extra ground-handling failures, on B** (run 4 → run 5d; band):
- lift-off 45.0 → **43.9 m/s** (≤ 40);
- three-point half-brake roll 419 → **413 m** (≤ 400);
- wheel landing 589 → **583 m** (≤ 550).

Not fixed. The cause is not the section, and the physics that would move them is outside this lane:
- **Lift-off.** The test's pilot rotates to 0.5° under the three-point attitude, so the wing flies at ~8.7°, where a
  zero-incidence finite wing gives CL ≈ 0.7. Lifting off at 40 m/s needs about 0.76 out of ground effect.
  - B moves it 1.1 m/s only because its lift slope is 4.7 % higher.
  - **One real omission:** `AeroModel` keeps wing strips out of the propeller slipstream (`!isWing`, AeroModel.cs
    line 384). Momentum theory at full power and 40 m/s gives the inboard strips (|y| < ~0.76 m, s1, ~20 % of the area)
    roughly 1.5–2× the dynamic pressure. That is worth ~2–3 m/s of lift-off speed: not enough alone, and it is a
    fleet-wide change.
  - The rest is the rigging angle against the measured 9.2° three-point attitude (Jane's: zero incidence).
- **Landing rolls.** These are the same as run 4's analysis: less induced drag at the three-point attitude, and a
  smooth-section profile drag (XFOIL cd ~0.010 at 9°) a third of the old feel table's.

> **§2–§12 below are run 4's text, on run 4's invented section.** Their method is still the method. Wherever they give a
> number for the Extra's own sections or wing, §1–§1c supersede it. The NACA 0012 validation (§3) and Gault's figure (§4)
> are unchanged.

## Run 4 result (superseded sections)

1. **The section is published by name only.** It is MA 15 S at the root and MA 12 S at the tip (300L Information
   Manual §1.3.3). No coordinates exist in any source I could find. **Since run 5c the published Eppler E473 (scaled
   to 15 %) and E472 stand in for them (§1). Items 2–7 and §2–§11 were computed on run 4's reconstruction and are
   stale until re-run.**
2. **The shape Ty describes behaves the way he describes, at the Extra's Reynolds number, in the wind tunnel.**
   - Ladson's measured NACA 0012 at Re 2–6 M (NASA TM 4074) is linear to a high cl_max: 1.41 at 15.3°, 1.66 at 17.4°.
   - It then loses 40–47 % of its lift within one degree.
   - McCullough & Gault (NACA TN 2502) call this the **leading-edge stall** of 9–15 % symmetric sections: "little or
     no rounding over of the lift curves near maximum lift… abrupt discontinuities".
   - XFOIL cannot reproduce it. Its 0012 peaks 1.2° late and 5–8 % high, then rounds over (§3).
3. **Which stall the Extra's sections have depends on Gault's ordinate at 1.25 % chord** (NACA TN 3963).
   - With the reconstructed nose, the root sections (y(1.25 %) 2.27–2.32 % c, Re 3.0–3.4 M) fall in Gault's
     **trailing-edge** region: a rounded peak.
   - The mid-span (2.0–2.2 % c) falls in the **combined** band: trailing-edge separation creeps forward a little,
     then the rest lets go at once.
   - The outer wing (1.9 % c) falls in the **leading-edge / combined** band.
   - So Ty's picture holds for the outer two-thirds of the span. The inboard root is the part the evidence disputes,
     and it rests on the one assumed number: where the maximum thickness is (§4).
4. **Section cl_max at the 1-g stall: 1.32 (tip) to 1.50 (root),** at α 15–18° (2-D). That is higher than Ty's 1.2,
   and the published stall speed needs it: 55 KIAS at 820 kg is an aeroplane CL of 1.51 (§8).
5. **The wing now stalls late and at once:**
   - **Before:** the 2026-09-07 feel table stalled at 14.0° with wing CLmax 1.446, losing 0.33 per degree over two
     degrees.
   - **After:** the derived wing is linear to 20.4° with CLmax 1.438.
   - The mid-span stations then fall off a cliff: the steepest 0.05° step loses lift at 3.77 per degree, 11× the old
     table's rate.
   - The drop is smaller, though: −19 % one degree past the peak, against the old −22 % (and −48 % three degrees
     past). The root and the lightly loaded tips are still lifting (§7, §11).
6. **The trimmed 1-g stall speed is 55.4 KEAS against the published 55 KIAS at 820 kg: +0.7 %** (§8).
7. **Hysteresis.**
   - XFOIL shows none: its up and down sweeps agree at every station.
   - The lifting line folds the abrupt sections, which gives 0.8° of static hysteresis between the stall and
     reattachment.
   - No measurement for these sections at these Reynolds numbers was found. The existing 5° reattachment band is
     kept and not claimed (§9).

## 2. Reynolds number

1-g stall, 55 KIAS at 820 kg (Information Manual §5.4, ACRO I, idle, forward CG) → 28.29 m/s EAS, sea-level ν
1.4607·10⁻⁵ m²/s → **Re = c × 1.937·10⁶**. Mach 0.083.

| station | y m | chord m | t/c | Re at 1-g stall | Re at the stall at VA 158 KIAS |
|---|---|---|---|---|---|
| s1 | 0.390 | 1.755 | 14.85 % | 3.40 M | 9.8 M |
| s2 | 1.155 | 1.570 | 14.51 % | 3.04 M | 8.7 M |
| s3 | 1.875 | 1.395 | 14.11 % | 2.70 M | 7.8 M |
| s4 | 2.523 | 1.238 | 13.66 % | 2.40 M | 6.9 M |
| s5 | 3.075 | 1.104 | 13.16 % | 2.14 M | 6.1 M |
| s6 | 3.508 | 0.999 | 12.68 % | 1.94 M | 5.6 M |
| s7 | 3.806 | 0.927 | 12.29 % | 1.80 M | 5.2 M |
| s8 | 3.958 | 0.890 | 12.07 % | 1.72 M | 5.0 M |

The brief expected 3–6 M. **The 1-g stall is 1.7–3.4 M.** 3–6 M is the outer wing in an accelerated stall.

## 3. Lift curve: XFOIL, validated against the tunnel

There are no wind-tunnel data for the MA sections, so XFOIL 6.99 was run. It was built headless from
web.mit.edu/drela/Public/web/xfoil/xfoil6.99.tgz with gfortran 16 and Homebrew libX11; plotting was off (`PLOP G F`).

**Settings:**
- paneling: `PPAR N 240`;
- `VISC Re`, `MACH 0.083`;
- free transition, e^N with **NCRIT 9** (a smooth composite wing in calm air);
- `ITER 300`;
- α 0 → 26° in 0.25° steps, each started from the previous point's boundary layer, then 26 → 0° down;
- boundary-layer `DUMP`s at 8, 10, 12–22 and 24°.

**Sensitivity:** NCRIT 4, which stands in for propwash and dirt, changes the MA 15 S at 2.7 M by 0.012 in cl_max
(1.553 against 1.565). The other sensitivity runs (a 5 % trip, Re 6 and 9 M, x_m 30 %) were started but stopped at the
time limit before finishing. Only the geometry of the x_m 30 % case is used (§4).

**Validation against measurement: NACA 0012, Ladson NASA TM 4074 Table I (M 0.15, free transition), against XFOIL at
the same Re and Mach:**

| Re | measured cl_max | then | XFOIL cl_max | XFOIL after the peak |
|---|---|---|---|---|
| 2.0 M | **1.408 at 15.25°** | **0.753 at 16.25° (−47 % in 1°)** | 1.521 at 16.5° | 1.18 at 20° (−22 % in 3.5°) |
| 3.94 M | 1.484 at 15.14° | 1.095 at 18.09° | 1.670 at 17.75° | 1.32 at 21° |
| 5.97 M | **1.660 at 17.35°** | **1.005 at 18.65° (−39 % in 1.3°)** | 1.749 at 18.5° | 1.35 at 22° |

1. **The linear range** is good to a constant ratio. Measured/XFOIL cl across 4–16° is 0.902–0.948, mean
   **K = 0.929 at 2.0 M** and **0.944 at 5.97 M**. Ladson reports that theory over-predicts the lift slope of this
   very model by ~7 % at R = 4·10⁶. K, log-interpolated in Re, multiplies every XFOIL cl below (0.929–0.936 at the
   stations). With it, XFOIL's cl at the measured break α is 1.39 against 1.408, and 1.63 against 1.660.
2. **The stall is where XFOIL fails.** It is XFOIL's known weakness past cl_max: a steady interacted boundary layer
   with an e^N bubble model.
   - The real 0012 bursts its leading-edge bubble and loses the whole upper surface at once. XFOIL keeps a short
     bubble, from x = 0.005 to 0.013 c, through 24°.
   - Owen & Klanfer's Re_δ*s at its start is 1100–2600, where they put the burst at about 400–500.
   - XFOIL instead lets the trailing-edge separation creep forward, and rounds the peak.
   - Its post-stall cl is not used anywhere in this derivation for a leading-edge or combined station.
3. **What XFOIL does get right** is where the trailing-edge separation has reached at a given α. At the measured
   breaks it had reached **0.071 c (2.0 M)** and **0.097 c (5.97 M)** of the upper surface. The real section lets go
   with **more than 90 % of its upper surface still attached**, the same thing Ty describes.
4. **Measured post-break values.**
   - cl one degree past the break: 0.535 of the peak at 2.0 M, 0.605 at 5.97 M.
   - cd/cl at the first post-break point: 0.208 (3.94 M), 0.210 (11.9 M), 0.245 (18.9 M). **0.21 is used.**
   - cm: −0.106, −0.080, −0.108, −0.134, −0.111 (2.0–18.9 M). **−0.108 is used.**

## 4. Stall type

**McCullough & Gault, NACA TN 2502 (1951).** The tests were at Re 5.8 M:
- the 63₃-018 stalls from the trailing edge;
- the 63₁-012 and 63-009 have the leading-edge stall;
- the 64A006 has the thin-aerofoil stall.

Quotes:
- *"The leading-edge stall is generally inherent to most airfoil sections of moderate thickness (symmetrical sections
  with thickness ratios of approximately 0.09 to 0.15)."*
- On the 63-009: *"smooth flow existed over the entire upper surface"* at the angle for maximum lift. After it, *"no
  evidence of flow reattachment at any station along the surface."*

RM A6L13 (the same authors) gives **cl_max 1.38 at 14.7°** for the 18 % section and **1.38 at 13.7°** for the 12 %
section. Thickness changes the shape of the stall, not the height of the peak.

**Gault, NACA TN 3963 (1957), Fig. 1** correlates about 700 measured stalls with Re and the upper-surface ordinate at
0.0125 c. Two caveats from Gault himself:
- the ordinate "has no apparent physical significance";
- it holds for smooth sections in low turbulence.

His Table I gives 0012 1.89, 0015 2.37 and 63-012 1.52 % c. The boundaries, read off the figure (±0.05 % c), are in
**% c**:

| Re | thin \| LE | LE \| LE+comb | LE+comb \| TE+comb | TE+comb \| TE |
|---|---|---|---|---|
| 1.0 M | 1.62 | 1.66 | 1.95 | 2.20 |
| 1.5 M | 1.42 | 1.66 | 1.93 | 2.28 |
| 2.0 M | 1.33 | 1.60 | 1.92 | 2.25 |
| 3.0 M | 1.20 | 1.52 | 1.86 | 2.22 |
| 6.0 M | 1.00 | 1.32 | 1.68 | 2.12 |
| 9.0 M | 0.92 | 1.20 | 1.57 | 2.06 |

| station | y(1.25 %) % c | at the 1-g stall | with x_m = 30 % | at the stall at VA |
|---|---|---|---|---|
| s1 | 2.32 | **trailing-edge** | TE / combined | trailing-edge |
| s2 | 2.27 | **trailing-edge** | TE / combined | trailing-edge |
| s3 | 2.20 | TE / combined | TE / combined | trailing-edge |
| s4 | 2.13 | TE / combined | TE / combined | trailing-edge |
| s5 | 2.05 | TE / combined | LE / combined | TE / combined |
| s6 | 1.98 | TE / combined | LE / combined | TE / combined |
| s7 | 1.92 | LE / combined | LE / combined | TE / combined |
| s8 | 1.88 | LE / combined | LE / combined | TE / combined |

DATCOM's sharpness parameter Δy is 3.2–4.1 % c. That is far above the ~1.2–2.5 % c where DATCOM treats a section as
sharp-nosed. **None of these sections can have a thin-aerofoil stall.** Ty's correction holds: a blunt nose, not a
bubble stall.

**Reading the table honestly.**
- A larger leading-edge ordinate pushes a section toward the trailing-edge stall, not away from it. A blunter nose
  than my ellipse would make the root rounder still.
- The abrupt, late stall Ty describes is the measured behaviour of the thinner, outer two-thirds of this wing at its
  Reynolds number.
- Whether the inboard third does the same turns on x_m. At 25 % the root is trailing-edge; at 30 % the whole wing is
  in the combined or leading-edge bands. A contour-gauge reading of the root nose would settle it (NEEDS YOU).
- **Reynolds-number dependence.** Higher Re pushes every station toward the trailing-edge stall, which is Gault's
  stated trend. The tables are built at the 1-g Re, so the model's high-speed (accelerated, snap) stall is as abrupt
  as its 1-g stall. By this correlation the real one is rounder, and higher: Ladson's 0012 gains 18 % in cl_max from
  2 to 6 M. **GAP.**

## 5. The station sections (2-D)

For every station:
- **Attached:** XFOIL × K, 0° to the break.
- **Leading-edge / combined stations:** the break comes at the α where XFOIL's upper-surface trailing-edge separation
  reaches **s_b**:
  - s_b is Ladson-calibrated, 0.071–0.097 c (log-interpolated), for "LE / combined";
  - s_b = **0.15 c** for "TE / combined".
- **Where 0.15 c comes from.** No source gives how far a combined stall creeps before it lets go. 0.15 c is **Ty's own
  figure**, "about 85 % of the upper surface separates at once", and it is used as stated. Brackets: Ladson's
  leading-edge breaks at 0.07–0.10 c, and XFOIL's fully progressive peak at 0.31–0.36 c. The choice moves the break α
  by about 1.5° and the peak cl by about 2 %.
- **After the break:** cl falls to Ladson's measured fraction one degree later, with cd = 0.21 cl and cm = −0.108.
- **Trailing-edge stations:** XFOIL's own peak and decline × K, to 6° past the peak.

| | y(1.25 %) | type | K | a₀ /rad (0–10° secant) | XFOIL cl_max | **cl_max** | **α break (2-D)** | attached at break | cl 1° later |
|---|---|---|---|---|---|---|---|---|---|
| s1 | 2.32 | TE | 0.936 | 5.87 | 1.597 @ 18.2° | **1.495** | 18.2° | 69 % | rounded |
| s2 | 2.27 | TE | 0.935 | 5.89 | 1.577 @ 18.5° | **1.474** | 18.5° | 64 % | rounded |
| s3 | 2.20 | TE/comb | 0.933 | 5.95 | 1.558 @ 18.0° | **1.425** | 16.4° | 85 % | 0.55 × |
| s4 | 2.13 | TE/comb | 0.931 | 6.01 | 1.531 @ 17.8° | **1.406** | 16.3° | 85 % | 0.55 × |
| s5 | 2.05 | TE/comb | 0.930 | 6.07 | 1.504 @ 17.0° | **1.382** | 16.1° | 85 % | 0.54 × |
| s6 | 1.98 | TE/comb | 0.929 | 5.99 | 1.480 @ 17.0° | **1.357** | 15.9° | 85 % | 0.53 × |
| s7 | 1.92 | LE/comb | 0.929 | 5.93 | 1.451 @ 16.8° | **1.325** | 15.1° | 93 % | 0.53 × |
| s8 | 1.88 | LE/comb | 0.929 | 5.90 | 1.441 @ 16.8° | **1.315** | 15.1° | 93 % | 0.53 × |

Why the 0–10° secant for a₀: XFOIL's slope near zero is depressed by a long laminar run, which jumps forward at
7–8°. Ladson's measured 0012 secant to 10° is 1.045/10.15° = 5.90 /rad at 2.0 M.

## 6. The finite wing

The aero model flies wing strips directly on their tables. It applies no aspect-ratio correction to a wing's lift
slope; only the tail is mapped. So **the wing table must already be the finite wing's.**

The old `extra-sym` table had a 5.82 /rad slope: a 2-D slope on a wing of aspect ratio 5.9. That was the main
structural error in it.

Each station is carried to the finite wing by its own lifting-line induced angle. This is the same mapping
`AeroModel` uses for the tails (`TailInducedAngleMapping`):

  α₃D = α₂D + c_l (1/a_y − 1/a₀),   a_y = CLα · η(y)

- **η(y) = c_l(y)/CL** comes from a Glauert lifting line of the published planform: 1.85 → 0.88 m over 8.00 m,
  untwisted, 60 odd terms. Values for s1–s8: 0.990, 1.040, 1.065, 1.063, 1.021, 0.914, 0.701, 0.379. The loading
  peaks at mid-semispan, as a λ = 0.48 untwisted wing should.
- **CLα** is DATCOM's 2πA/(2 + √(A²/κ² + 4)), with A = 8.00²/10.84 = 5.904, mean a₀ 5.952, κ = 0.947:
  2π·5.904/(2 + 6.545) = **4.341 /rad**. The lifting line itself gives 4.63 at a₀ = 2π.

Worked example, **s3**:
- kInd = 1/4.62 − 1/5.95 = 0.04838 rad.
- **Break:** α₃D = 16.4° + 1.425 × 0.04838 rad = 16.4 + 3.95 = **20.35°**.
- **One degree later in 2-D:** cl 0.784, α₃D = 17.4 + 0.784 × 0.04838 × 57.3 = **19.57°**, which is *less* than 20.35°.
- **The fold.** The 2-D drop, 36.7 /rad, is steeper than 1/kInd = 20.7 /rad, so the map folds. As the section loses
  lift its own downwash goes, and its angle rises. **The finite wing's drop happens at one angle, a cliff.** This is
  the lifting-line form of "the whole upper surface lets go at once". The table records it as a step of 0.001 rad.
- The tip stations (η 0.70, 0.38) are so lightly loaded that they stall last, at 27° and 48°. They carry 2 % of the
  area.
- **Wing static stall, in order:** s3 and s4 at 20.3°, s5 at 20.9°, s6 at 22.5°, s2 at 22.9°, s1 at 23.6°.
- **Past the drop:** a linear 10° blend into the run-1 flat plate (`poststall_plate.py`: CL = CD90 sin α cos α,
  CD = CD0 + CD90 sin² α, CD90 1.25). That plate is the old table's own rows from there to ±180°, kept in
  `data/extra-sym-f8dc538.json`.
- **Symmetric section:** cl and cm are mirrored with their sign reversed, and cd is mirrored unchanged.

**Rolling check.** A per-strip η was derived for symmetric loading, and a roll is antisymmetric. Strip-theory
derivatives (planform integral):

| mapping | Clp | Clδ | Clδ/Clp (steady pb/2V per unit aileron) |
|---|---|---|---|
| old table (2-D a₀ everywhere) | 0.816 | 1.242 | 1.523 |
| uniform CLα | 0.595 | 0.906 | 1.523 |
| **per-strip CLα·η (shipped)** | 0.559 | 0.882 | **1.579** |
| antisymmetric lifting line (Glauert) | 0.464 | 0.730 | **1.574** |

The shipped mapping reproduces the lifting line's ratio to 0.3 %. The old one was 3.2 % low.

## 7. The whole wing (StallCharacterProbe, static, no rates, elevator neutral)

| | before (extra-sym, f8dc538) | after |
|---|---|---|
| CL at 10° (wing) | 1.027 | 0.750 |
| wing CLmax, α | **1.446 at 14.0°** | **1.438 at 20.4°** |
| one degree past the peak | 1.131 (−22 %) | 1.171 (−19 %) |
| three degrees past | 0.759 (−48 %) | 1.133 (−21 %) |
| steepest drop (0.05° step) | 0.33 /deg | **3.77 /deg** |
| aircraft CL (δe 0) peak | 1.560 at 14.0° | 1.633 at 20.4° |

**Late and abrupt, as Ty describes, but a smaller drop than the old feel table.** The mid-span stations go at once,
while the trailing-edge root and the tips go later. If the root is a combined section too (x_m ≈ 30 %), the wing
would lose all of s1–s6 in the same step.

## 8. CLmax against Ty's 1.2, and Vs against the book

- **Published:** 55 KIAS power idle at 820 kg (Information Manual §5.4; also 57 kt at 870 kg, 60 kt at 950 kg).
  q = ½·1.225·28.29² = 490.2 Pa, so **CL = 820·9.807/(490.2·10.84) = 1.513**. That is the trimmed aeroplane with tail
  download, and the manual's ASI position error near the stall is a graph only.
- **Model:** trimmed idle 1-g stall (`PracticeScenario.EstimateVso`) **55.4 KEAS: +0.7 %**. The before table gave
  55.4 too. The dynamic 1 kt/s entry breaks at 59.6 KEAS at n 1.14, which is 55.8 KEAS 1-g equivalent.
- **Ty's 1.2 is not consistent with the book.** Scaling every section to cl_max 1.2 scales the wing to ~1.21, and the
  stall speed becomes 55.4/√(1.2/1.425) ≈ 60.4 KEAS, 10 % above the manual. That could only hold if the ASI read
  ~9 % low at the stall.
- **What the data give** for a 12–15 % section at Re 1.7–3.4 M is cl_max 1.3–1.5: Ladson's 0012 has 1.41 at 2.0 M;
  McCullough & Gault's 12 % and 18 % sections both have 1.38 at 5.8 M. 1.2 is the right number for a NACA 0012
  around Re 0.5–1 M, or with a rough leading edge: Ladson's wraparound grit costs 25 %.

## 9. Hysteresis

- **XFOIL:** none. Up and down sweeps are identical to 0.001 at every station that converged; st8's down sweep hung.
- **Lifting line:** the fold above leaves a band where both the attached and the separated solutions exist:
  20.35 → 19.57° on s3. That is **0.8° of static hysteresis**, a lower bound.
- **Measurement:** none found at these sections and Reynolds numbers. Ladson and McCullough & Gault ran α upward only.
- **What the model can represent.** Each strip has a separation memory (fast 0.15 s, slow 0.6 s) that blends an
  attached branch carried past the static stall (dynamic stall delay) with the table. Below the static stall the table
  is single-valued.
- **What true static hysteresis would need:** a separated branch cl_sep(α) between α_reattach and α_stall, chosen by
  the memory. With no measurement to set α_reattach it would be a tuning knob, so **it is not added**.
- **What was changed** (`StallDynamicsConfig.SeparationOnPastStallDeg` / `ReattachBelowStallDeg`; Extra only, null for
  every other type):
  - **Before:** the memory switched at a fixed 16° / 11°, calibrated against a ~14° table.
  - **The fault with this wing:** with a 20° stall every strip would be called separated 4° early. That cuts its
    aileron by 85 % and tapers its induced drag while the flow is still attached.
  - **Now:** onset is at each strip's own static stall (+0°); anything past it is only the 0.15 s dynamic delay.
    Reattachment is 5° below, keeping the legacy band's width because nothing measured replaces it.
  - **The same switch** makes the wing-wake "stalled fraction" count each strip from its own stall instead of a fixed
    15°. That fraction collapses the tail downwash and gates the lessons' stall reporting.
  - **What the fixed 15° did here:** it collapsed the downwash five degrees early. The aircraft pitched nose-down at 16°
    (Cm −0.21 → −0.44) and could not be pulled to its own stall at idle.
- **`StallDeg` (tests) and `PracticeScenario.AlphaCapRad`** now take the earliest-stalling strip of the main wing. For
  every other type the main wing has one table, so the result is identical (checked over all configs).

## 10. Other aircraft

Untouched. All the new behaviour is behind `SeparationOnPastStallDeg`, which is null for every other config and
asserted by `ExtraWingSectionTests.EveryOtherTypeKeepsTheFleetStallBand`. The glide table changes in the Extra's row
only (§11).

## 11. Stall-break character, before and after (StallCharacterProbe)

| | before | after |
|---|---|---|
| **1 kt/s, idle, feet flat** | | |
| – break (KEAS, n, 1-g equivalent) | 54.0, 1.00, 53.9 | 59.6, 1.14, 55.8 |
| – CLeff max, α | 1.573 at 15.8° | 1.469 at 20.4° |
| – CL lost, time | 0.736 (47 %) in 2.31 s, 0.32 /s | 0.341 (23 %) in 1.10 s, 0.31 /s |
| – CL lost in the first 0.25 s after the peak | 0.022 | **0.142** |
| – 2 s after the break: bank, max roll rate, Δθ | +12°, 33 °/s, −13° | **+41°, 52 °/s**, −12° |
| 1 kt/s with 0.1 rudder held: bank, max roll rate (2 s) | +11°, 58 °/s | +37°, 57 °/s |
| 0.05 aileron step at the break: bank, max roll rate (2 s) | +6°, 20 °/s | +42°, 52 °/s |
| **accelerated** (1.8 Vs, full aft in 1 s) | | |
| – break | 103.9 KEAS at 3.59 g, α 16.0° | 104.4 KEAS at 3.46 g, α 20.6° |
| – CL lost | 50 % in 0.70 s, 0.555 in the first 0.25 s | 34 % in 1.00 s, 0.189 in the first 0.25 s |
| – 2 s after the break: bank, max roll rate | +80°, 137 °/s | +40°, 53 °/s |
| spin entry from the 1 kt/s break (full aft + left rudder) | 147 °/s developed, 90 % at 4.91 s | 155 °/s, 90 % at 5.21 s |
| snap at 90 KEAS, full power: time to 180 °/s, peak roll rate | 1.60 s, 212 °/s, α 44° | 1.79 s, 195 °/s, α 48° |
| developed spin (FleetSpinSweep) | 150 °/s, 2.39 s/turn, 340 ft/turn, α 34° | 153 °/s, 2.36 s/turn, 330 ft/turn, α 38° |
| recovery: PARE / Beggs-Mueller | 0.3 turn (1.0 s) / 0.2 (0.8 s) | 0.5 (2.1 s) / 0.4 (1.3 s) |
| pitch step: q̇₀, q at 0.5 s, q at 1.0 s | 168.7 °/s², 48.6, −5.9 °/s | 159.1, 45.2, +1.2 |
| let-go, idle: peak α, mean α (last 5 s) | 27°, 23° | 29°, 24° |
| let-go, full power: peak α, time above 30° | 31°, 0.4 s | 40°, 2.6 s |
| full-aileron roll at VA 158 KIAS: steady, avg 360 | 439, 389 °/s | 504, 409 °/s |
| full-aileron roll at 160 KTAS: steady, avg 360 | 437, 383 °/s | 500, 396 °/s |
| idle glide at 1.3 Vso (glide table) | 5.8:1, α 8.2°, 1268 fpm | 6.9:1, α 11.0°, 1068 fpm |
| full-aft static balance against the wing stall | 20.0° against 14.0° | 21.8° against 20.3° |

**Reading it.**
1. **The 1-g break.** It is later (α 20.4° against 15.8°) and faster: 0.14 of CL is gone in the first quarter second
   against 0.02. It is a smaller total drop (23 % against 47 %), with a much stronger wing drop: 41° of bank in 2 s
   with the feet flat.
   - The first stations to go are mid-span; the prop's asymmetry at idle picks the side.
   - The before table lost its lift over two degrees, symmetrically, so the nose fell more and the wing less.
2. **The accelerated stall.**
   - **CL:** the drop is gentler than before, 34 % of CL lost against 50 %, and 0.189 in the first 0.25 s against
     0.555.
   - **Roll-off:** half the old figure, 40° of bank against 80°.
   - **Why:** the old table's 2-D slope made the whole wing reach its 14° cliff at once.
3. **The roll rate rose 15 %.**
   - 3.4 % of it is the corrected ratio of aileron power to roll damping (§6).
   - The rest comes from the aileron's 10–11° of section α reaching the part of XFOIL's curve that steepens through
     its laminar-run jump at 7–8°. That steepening is a modelling artefact: Ladson's measured 0012 does not show it
     (measured/XFOIL there is 0.90, against K = 0.93).
   - Both the old 439 and the new 504 are above the published 300L figures (245–313 °/s; NG 327; 330LX 360). The
     run-2 open question remains.
4. **Spin and recovery** are essentially unchanged. PARE takes 0.2 turn longer.
5. **Full-aft authority.** `StallAuthorityTests`' protocol target for aerobatic types is the stall + 6°, which is now
   26.3°. Full aft balances at 21.8°, 1.5° past the wing stall.
   - The Extra still stalls, spins and snaps.
   - It is not in the Verified set, so the test passes and lists it PENDING.
   - The calibration knob (`saturationDeg` 48.7°) is **not re-run**: the protocol's margin is a handling target, and
     whether to move it is Ty's call (NEEDS YOU).

6. **Ground handling: three test bands now fail on the Extra.** They pass on the before-config, each near its band
   edge.

   | test | before | after | band |
   |---|---|---|---|
   | `TakeoffRollIsTheBooks`: lift-off speed | 38.5 m/s | **45.0 m/s** | 32–40 |
   | `ThreePointAndWheelLandingsAreWorkable`: three-point, half-brake roll | 320 m | **419 m** | ≤ 400 |
   | the same, wheel landing | 538 m | **589 m** | ≤ 550 |

   - **Lift-off.** The test's pilot rotates to 0.5° under the measured three-point attitude (9.2°), so the wing flies
     at about 8.7° and no more.
     - On a finite-wing slope (aircraft ≈ 4.8 /rad) that is CL ≈ 0.7–0.8 even with ground effect, not the 1.05 a
       1.2 Vs lift-off needs (about 12.5°).
     - The old table's 2-D slope gave 34 % more lift at any angle, which is how it flew off at 38.5.
   - **Landing rolls.** They are longer for the same reason. There is less lift, so less induced drag, at the
     three-point attitude, and XFOIL's smooth-section profile drag (cd 0.010 at 9°) is a third of the old table's
     (0.031).
   - **Not tuned.** If the real 300L lifts off at ~1.2 Vs from its three-point attitude, the gap is the wing–fuselage
     rigging angle, the three-point attitude, or in-situ profile drag, not the section (NEEDS YOU).

## 12. Gaps

- **The section is reconstructed.** x_m, the nose shape and t/c are assumed, and x_m decides whether the inboard third
  is trailing-edge or combined.
- **Gault's boundaries were read by eye** off a 1957 figure, to ±0.05 % c.
- **K comes from one section in one tunnel.** Ladson suspected that tunnel's turbulence was higher than usual.
- **The 0.15 c combined-stall extent is Ty's figure**, not a measurement.
- **The tables are built at the 1-g Re.** No Reynolds dependence, so the accelerated stall should be higher and
  rounder than modelled.
- **No measured static hysteresis.**
- **XFOIL's laminar-run kink at 7–8°** is carried by K, which is a constant.
- **The rest of the sensitivity set was not completed:** the trip, Re 6/9 M and x_m 30 % XFOIL runs.
- **Ground effect and propwash over the inner wing** are unchanged; the model leaves wing strips out of the slipstream.
- `AirframeFlow`'s visual wake still uses a fixed 15° stall.
