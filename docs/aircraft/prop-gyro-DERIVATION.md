# Propeller-and-crank gyroscopics — Extra 300L (with the Pitts and Great Lakes for consistency)

lane/claude-inertia, run 3 (Claude, 2026-11-02). Owner Ty, flying 5608cc9: *"not much gyroscopic swerve in yaw when
I pitch up and down"*. Tests: `tools/FlightTests/PropGyroTests.cs`; probe: `tools/FlightTests/GyroSwerveProbe.cs`
(`GYRO_PROBE=1`). Run 2's sign derivation and the Euler-equation check are in `extra-300-endo-DERIVATION.md` §3a/§3c
and are not repeated here.

**Result.** The gyroscopic term is wired at full value with the right sign and axis. The Extra's polar moment
was **2.5 kg·m²**, which is **20 % above** what its propeller and engine can have. It is now **2.1**, derived from
Hartzell's *published* polar moment for the same-class propeller. Ty's "not much swerve" is therefore not a missing
or throttled gyro. In a flyable pitch manoeuvre the gyro's yaw is a few degrees, and the aeroplane's other
propeller yaw terms are ten times larger (§5).

## 1. What turns at propeller speed on an Extra 300L

The configuration is the EA 300/L (two-seat, 8.00 m span). EASA TCDS **EASA.A.362** issue 09, Section C, lists the
engine as AEIO-540-L1B5 (300 hp at 2700 rpm). It lists the propeller as Model 1, **MT-Propeller MTV-9-B-C/C200-15**:
3 blades, 200 cm, right-hand tractor, constant speed. Model 2 is the 4-blade MTV-14-B-C/C190-17. Model 1 is the
one modelled here.

### 1a. Propeller and spinner: 1.95 ± 0.15 kg·m²

No MT document I could get publishes a polar moment for the MTV-9. EASA TCDS **P.096** (MTV-9 series, issue 03)
gives only "Constant Speed: approx. 24 kg". The nearest **published** polar moment is Hartzell's, from the
aerobatic composite-propeller brochure (pittss2b.com/hartzellbrochure.pdf, specification table):

| Hartzell | blades | diameter | basic prop | spinner | system | **polar moment** | engine |
|---|---|---|---|---|---|---|---|
| HC-C3YR-1A / 7690C | 3 composite | 78 in | 60.0 lb | 5.2 lb | 70.2 lb | **17.4 in·lb·s² = 1.966 kg·m²** | IO-540 |
| HC-C2YR-1A / 7690C | 2 composite | 76 in | 40.5 lb | 5.5 lb | 51.0 lb | 10.6 in·lb·s² = 1.198 kg·m² | IO-360 |

(1 in·lb·s² = 0.1129848 kg·m².) Hartzell's own Extra 300/L kit sheet (hartzellprop.com, *Extra300LCERTIFIED.pdf*)
gives the MT it replaces as **62 lb, 78.74 in**.

The 3-blade Hartzell is the same class: 3 composite blades on an aluminium hub, IO-540, the same diameter to 1 %.
So scale its I ∝ m·D², keeping the mass distribution:

| basis for the MT's mass | m ratio | (D ratio)² | I |
|---|---|---|---|
| 62 lb against Hartzell's 60.0 lb basic prop | 1.0333 | (78.74/78)² = 1.0190 | 1.966 × 1.053 = **2.07** |
| 62 lb includes the MT spinner: against 60.0 + 5.2 lb | 0.9509 | 1.0190 | 1.966 × 0.969 = **1.91** |
| EASA P.096's "approx. 24 kg" against 27.2 kg | 0.8818 | 1.0190 | 1.966 × 0.899 = **1.77** |

A spinner shell (Hartzell's is 5.2 lb = 2.36 kg at Ø 14.3 in, so R 0.182 m, I ≈ m·R²/2 = 0.04) is inside this
spread whichever way the brochure counts it. **Prop + spinner = 1.95 ± 0.15 kg·m².**

Cross-check on the blade distribution: Hartzell's 60 lb with roughly 3 × 3.6 kg of blades gives
k = √((1.966 − 0.05)/10.9) = 0.42 m = **0.42 R**. That is the bottom of run 2's 0.42–0.53 R band for blades with
root hardware, so the scaling assumes nothing unusual.

### 1b. Engine parts turning with the crank: 0.14 kg·m² (band 0.09–0.20)

Lycoming publishes neither part masses nor a rotating-assembly inertia for the 540. Run 2's 0.2–0.3 is refined
here; the masses marked ASSUMED are estimates.

- **Crankshaft.** 27 kg (ASSUMED). k ≈ 0.045 m: journals Ø 60 mm, so r 0.030; pins at the 0.0556 m throw (stroke
  4.375 in, Lycoming TCDS E-295); webs out to about 0.085 m. Giving 27 × 0.045² = **0.055**.
- **Dynamic counterweights.** 2 × LW-19210, in positions 1 and 2 on the -L1B5 (Lycoming SI 1012L, Table 6).
  1.3 kg each (ASSUMED) at 0.08 m: **0.017**.
- **Connecting-rod big ends, the rotating share.** 6 × 0.6 kg (ASSUMED) at the 0.0556 m throw: **0.011**.
- **Starter ring gear and support.** 3.13 kg, weighed by an owner on an IO-540 (aviationstop.com, 2009). Ring
  1.5 kg at 0.165 m gives 0.041; hub and Ø 9.75 in alternator pulley 1.63 kg at k 0.08 gives 0.010. **0.051**.
- **Alternator rotor.** 2.5 kg at k 0.035 (ASSUMED), so I = 0.0031. It is belt-driven at **3.25:1** in the same
  sense (Lycoming IO-540-AG1A5 I&O manual 60297-45, assumed the same for the -L1B5). Its angular momentum is
  I·3.25·Ω, so it counts as **+0.010**. Angular momentum scales with the ratio, not the ratio squared.
- **Camshaft.** About 4 kg at k 0.015, so I = 0.0009. It is gear-driven at ½ speed, *opposite* sense: **−0.0005**.
  The magnetos (1.5× speed, small rotors) give **+0.0005**.
- **Pistons.** They reciprocate along axes through the crank, so they carry no mean angular momentum about it: **0**.

**Engine total ≈ 0.143.** The crank mass is the big unknown (±30 %), so the band is 0.09–0.20.

### 1c. Total

**I_rot = 1.95 + 0.14 = 2.09 kg·m², band 1.86–2.31. Config: 2.5 → 2.1.** The propeller is 93 % of it.
At 2700 rpm, Ω = 2700 × 2π/60 = **282.74 rad/s**, so:

| | I_rot | H = I·Ω | N at q = 100 deg/s (1.7453 rad/s) | ṙ if nothing resisted (Izz 2575) |
|---|---|---|---|---|
| before (5608cc9) | 2.5 | 706.9 N·m·s | 1233.7 N·m | 0.479 rad/s² = 27.5 deg/s² |
| **after (derived)** | **2.1** | **593.8 N·m·s** | **1036.3 N·m** | 0.402 rad/s² = 23.1 deg/s² |

## 2. The code path, end to end: full value, no gate

1. `configs/aircraft/extra-300-like.json` `propulsion.propInertia` → `PropulsionConfig.PropInertia`. The loader
   reads it verbatim; the `1.7` default applies only when the key is absent.
2. `PropModel.GyroMoment` (`src/FlyingGame.Core/PropModel.cs`):
   - rpm = IdleRpm + (MaxRpm − IdleRpm)·throttle, which is 2700 at full throttle;
   - h = RotationSign·Ip·rpm·2π/60;
   - it returns **(0, −r·h, +q·h)**, which is −ω × H with H = (h, 0, 0) in body axes (x fwd, y right, z down).
     A Lycoming turns clockwise seen from the seat, which is +x, so RotationSign = +1.
3. `PropModel.Compute` adds it to the propeller's moment **unscaled**, the last thing before return. Thrust, torque,
   P-factor and slipstream are separate terms; none multiplies it.
4. `Aircraft.ForceMoment` (`src/FlyingGame.Sim/Aircraft.cs`), single-engine branch: `totalM += pM`. This is
   evaluated at every RK4 stage with the stage's own rates `s.Rates`, so it is not lagged or smoothed. It is
   skipped only if the nose (and with it the prop) has been lost.
5. `RigidBody6DOF.IntegrateRK4` with the full inertia tensor (run 2 §3c).

I searched `src/` and `unity/Assets/_Project/Scripts/` for an assist, rate damper or stability-augmentation layer
("assist", rate damping, clamps on `totalM`) and found none. Signs: a pull (q > 0) gives N = +q·h, nose **right**; a
push gives nose **left**. A left yaw (r < 0) gives M = −r·h > 0, nose **up**. This agrees with Hepperle (mh-aerotools,
*propeller precession*): "a pitch-down motion ('push') produces a tendency to turn left".

**One gap, already noted in run 2.** The gyro takes its rpm from the throttle law, not from `Aircraft.EngineRpm`'s
governor. At full throttle both give 2700. At cruise power, e.g. 80 % throttle, the gyro uses 2300 where the
governor holds 2500, so it is 8 % low there. This does not affect Ty's full-power manoeuvres and is not changed
here.

## 3. Tests (hand-copied literals)

`PropGyroTests`:
- the Extra's propInertia lies inside 1.86–2.31;
- `GyroMoment` at ±100 deg/s gives N = ±1036.3 N·m (±2), with zero roll and pitch;
- `PropModel.Compute` with q minus without q equals that moment, so the gyro reaches the total unscaled;
- flying the §5 pull and push with and without the gyro: the gyro swings the nose right in the pull and left in the
  push, by 2–15°.

`EndoTests`' hand-copied h for the Extra moved 706.86 → 593.76, a deliberate edit in two places.

## 4. Pitts S-2B and Great Lakes: reported, not changed

The same engine-parts estimate applies to the Pitts' AEIO-540-D4A5: about 0.14 against run 2's 0.2–0.3. That
lowers the Pitts' band by about 0.1, from 3.9–6.0 to **3.8–5.9**, so **5.0 stays**.

Hartzell's published 2-blade composite figure (1.20 kg·m² for 40.5 lb at 76 in) is *not* the Pitts' propeller,
which has aluminium FC8477A-4 blades. Scaled with a composite mass distribution to 80 lb and 78 in, it would give
1.20 × (80/40.5) × (78/76)² = 2.50. Solid aluminium blades carry far more of their mass outboard. The 80-lb figure
is the owner's, so it is not a like-for-like comparison. It does say the Pitts' 5.0 should not be thought of as
conservative: **flagged, not changed** (no rotating-parts argument moves it).

The Great Lakes' AEIO-360 has four cylinders, so roughly 0.09 for the engine. Its 5.0 remains above any band that
the ~50 lb propeller run 2 assumed can give (2.5–3.8). It is **still flagged for Ty, not changed**: the propeller's
mass is the unknown, not the crank.

## 5. What the gyro does in the air (GyroSwerveProbe)

The probe flies straight and level at 100 KIAS, 1000 m ISA, reference loading, full power (2700 rpm). Feet and
ailerons are neutral and held, then **full** aft or forward stick is held for 3 s. Ty's "~100 deg/s for 3 s" cannot
be flown: q = n·g/V means 100 deg/s at 100 KIAS needs 9 g. What the aeroplane actually reaches is below.

∫r is the body yaw angle over the 3 s. "Gyro share" is the same flight's ∫r minus the no-gyro flight's.

| 100 KIAS, 3 s | q mean / peak (deg/s) | gyro N mean / peak (N·m) | r peak (deg/s) | ∫r (deg) | gyro share of ∫r |
|---|---|---|---|---|---|
| **pull**, before (2.5) | 38.9 / 79.1 | +480 / +976 | −55.5 | −74.7 | **+6.7°** (right) |
| pull, **after (2.1)** | 39.3 / 80.2 | +408 / +832 | −57.3 | −76.1 | **+5.6°** (right) |
| pull, no gyro | 38.6 / 77.2 | 0 | −60.2 | −81.7 | — |
| **push**, before (2.5) | −50.2 / −98.4 | −619 / −1214 | +65.6 | +33.2 | **−7.1°** (left) |
| push, **after (2.1)** | −49.4 / −98.5 | −512 / −1020 | +62.4 | +33.3 | **−5.9°** (left) |
| push, no gyro | −49.2 / −98.8 | 0 | +65.2 | +39.2 | — |

At 140 KIAS the pull reaches q 51 deg/s mean (101 peak) at 6.4 g. The gyro share is +10.2° before and +8.6° after.
The push shares (−32° before, −14° after) are not comparable with each other: the two flights diverge, with mean q
−57.8 against −61.9 deg/s, so the flight path, not Ip, sets the difference.

**Why the swerve feels small.** Run in reverse, the gyro moment ṙ = 1036 N·m / 2575 = 23 deg/s² at a full
100 deg/s. But the aeroplane only averages 40–60 deg/s of pitch for 3 s, and over that time the fin takes most of
the yaw back. Its net share is **6–9° of yaw in 3 s**. Meanwhile, with the feet flat, the same pulls and pushes yaw
the aeroplane **55–100 deg/s** through the other propeller terms: P-factor at high and negative α, and the
slipstream on the fin. Those are an order of magnitude larger than the gyro's share and in the opposite direction
in the pull. What Ty reads as "the yaw in a pull" is mostly those. The gyroscopic part is right-sized, and if
anything it was 20 % strong.
