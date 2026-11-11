"""THE FABRIC TAIL IS A FLAT PLATE (owner 2026-11-09: "the pitts stabilizer is flat plate all the time not 0012",
and asked which types: "cub great lakes decathlon super cub etc", then "the 2.33 is also flat plate").

Every tail in the fleet flew `naca0012-like` (hStab) and `fin-lowAR` (elevator, fin, rudder). A fabric-and-tube
tail is neither: it is a thin plate on a tube spar, with no camber and no 0012 nose radius.

READ THE NEXT SECTION BEFORE USING THIS TOOL. 2026-11-10: the parameters were SEARCHED rather than sampled, and
the search moved the answer and corrected three things this file used to say.

CONVENTION - the table is a hybrid, and it has to be, because `AeroModel.FiniteSpanSection` only stretches the
ALPHA axis (each point moves by its own lift's induced angle, Cl*(1/a - 1/a0)); it never touches Cl or Cd
magnitudes. So:
  * the LINEAR range is TWO-DIMENSIONAL: Cl = 2*pi*alpha (thin-airfoil theory is exact for a flat plate), and
    FiniteSpanSection rescales it per surface from the surface's own aspect ratio.
  * the POST-STALL range is already FINITE-aspect-ratio, because broadside the induced angle is zero and the
    stretch does nothing there: Cd(90) = Cd_max = 1.11 + 0.018*AR (Viterna & Corrigan, 1981), which at the
    aspect ratios of tail surfaces is 1.14-1.19 - and independently Hoerner, Fluid-Dynamic Drag (1965) ch. 3,
    "plates normal to the flow": AR 1: 1.18, AR 5: 1.20. Both land at ~1.19, and 1.98 (the INFINITE plate) would
    be wrong here by two thirds. This matches the value the two old tables were already fitted to by
    `lowar_plate.py`, so broadside drag does not move: what this table changes is the attached and near-stall
    shape, which is the band the notch was in.

STALL. A thin plate departs the 2*pi line before it stalls. Past that, Viterna & Corrigan's flat-plate extrapolation, matched at the
stall point:
    Cl = A1*sin(2a) + A2*cos^2(a)/sin(a)      A1 = Cd_max/2
    Cd = B1*sin^2(a) + B2*cos(a)              B1 = Cd_max
    A2 = (Cl_s - Cd_max*sin(a_s)*cos(a_s)) * sin(a_s)/cos^2(a_s)
    B2 = (Cd_s - Cd_max*sin^2(a_s)) / cos(a_s)
and NO splice threshold: `lowar_plate.py` rescaled Cd only for |alpha| >= 12 deg and left everything below it
alone, which is the step the notch sat on. This table is generated whole from 0 to 180 deg.

THE FIT: stall 9 deg, Cl_max 0.90, BLEND 44 deg, with SpinAeroConfig.ControlSpoilerOnsetDeg 10.

HOW IT WAS ARRIVED AT, because this is the part that matters. Ty 2026-11-09 asked for a fit "against those three
tests" and was then shown two candidates, 12 deg/1.05 and 9 deg/0.80, and picked 12/1.05 on 2026-11-10 knowing it
put a hole in the elevator at alpha 30. He then withdrew that: "Put an agent on trying to adjust k factors and
slopes as needed to get the flat plate tail to meet our requirements" - and separately "Put the 0012 Pitts tail
back on the app until the flat plate tail work is done". So NOTHING is converted; the numbers above are a
RECOMMENDATION with its map, and his two requirements are the acceptance test:
  1. elevator authority across alpha 20-60 with NO HOLE (12/1.05 collapses to 0.02 of its alpha-0 value at
     alpha 30, which is where an ordinary non-flat spin sits);
  2. NO ANTI-DAMPING anywhere from alpha 10 to 60, alpha 20 above all, because that is where he flies aerobatics
     (9/0.80 was rejected for dM/dq +448 at alpha 20).

1128 candidates were measured on the Pitts and the shortlist on all twelve types (`FlatPlateSearchProbe`,
FLAT_PLATE_SEARCH=<a `--grid` file>), each one swept at 2 DEGREES rather than at the five alphas the two original
candidates were compared at. THAT RESOLUTION IS THE FIRST FINDING: at five points (10/20/30/45/60) the Pitts reads
all-negative dM/dq for the 0012 AND for 12/1.05, and at 2 deg the 0012 that ships today makes +610 at alpha 28 and
12/1.05 makes +2031 at alpha 24. Neither requirement was ever being measured where it fails. So "12/1.05 has no
anti-damping anywhere" was never true; its band simply falls between two samples, and 9/0.80's lands on one.

WHY EACH NUMBER, and none of them is "it made the table look right":
  * STALL 9 deg, Cl_max 0.90 - PUBLISHED SECTION DATA, the thin end of what this tail is. A razor-thin plate
    stalls near Cl 0.72 at 8 deg (Flachsbart; Hoerner, Fluid-Dynamic Lift ch. 4), but a fabric-over-ribs
    stabiliser on a tube spar is a thin SYMMETRIC section with a real nose radius, 6-8 % thick. Thin symmetric
    sections at tail Reynolds numbers (Abbott & von Doenhoff, Re 3e6): 0006 ~0.92 at 9 deg, 0009 ~1.10 at 11,
    0012 ~1.30 at 14. 0.90 at 9 IS the 0006 line. The defensible band is 9-12 deg and 0.90-1.15, and the search
    was run over the whole 8-16 deg, 0.70-1.30 rectangle so that the band could be seen against what lies
    outside it - which matters, because the candidates that beat the 0012 everywhere need Cl_max 0.70-0.80 at
    8-10 deg, i.e. the BARE PLATE value, and this surface is not a bare plate. That is said plainly rather than
    dressed up: the best unconstrained candidate (8 deg/0.70/blend 26) reaches an authority floor of 0.267
    against the 0012's 0.258 with no anti-damping at all, and its Cl_max has no source for a fabric tail.
  * BLEND 44 deg (30-44 all work) - BECAUSE THE SURFACE IS A LOW-ASPECT-RATIO PLATE AND ITS LIFT DOES NOT
    COLLAPSE AT A 2-D STALL. This is the same physics this file already uses to argue the FIN must keep
    `fin-lowAR`: a low-AR plate keeps lifting far past where a 2-D section stalls, on vortex lift, reaching
    Cl 1.0-1.2 at 30-40 deg (Hoerner). `AeroModel.FiniteSpanSection` cannot manufacture that - it moves points
    along alpha and leaves Cl alone - so a gradual hand-over has to be IN THE 2-D TABLE, and the blend width is
    where it lives. The old 10 deg was never measured for a tail; it was inherited from `poststall_plate.py`,
    which blends the fleet's WINGS over 10 deg, and a wing is aspect ratio 6-8 where these tails are 2.6-6.6.
    BLEND IS THE AXIS THAT DECIDES REQUIREMENT 2, and by a long way: of the 1128 candidates, 129 have no
    anti-damping anywhere, and 103 of those have blend >= 26 deg while 8 have blend <= 8. Narrow blends make
    +1700 to +4700 at alpha 16-20. Within 30-44 deg the result barely moves (floor 0.22-0.25, worst dM/dq -38
    to -153), which is what a real answer looks like as against a fitted one.
  * ControlSpoilerOnsetDeg 10 - THE RAMP WAS TIED TO THE WRONG ANGLE. The spoiler term exists because the
    section table stops being able to express a control once the section is on CN = Cd_max*sin(theta); it should
    therefore begin where the table's own flap effect dies, which is the SECTION'S STALL. 30 deg was chosen when
    every horizontal tail in the fleet was a 0012, whose stall is at 14 deg. A plate stalls at 9-12, so 30 left
    a band - alpha 20 to 34 - in which the table has no control effectiveness left and the spoiler has not
    started: that band IS the hole. Moving the onset to the plate's own stall is a mechanism argument, and it is
    worth the whole difference on the Pitts: floor 0.165 at onset 30 against 0.249 at onset 10.

WHAT IT MEASURES, Pitts, Cn clamp off, 2-degree sweep (authority as a fraction of its own alpha-0 value):

               floor (and where)   worst dM/dq        a0 stop-to-stop   a30    a45    a60
  0012 today    0.258 at a36       +610 at a28        3905 N.m          0.33   0.30   0.29
  12/1.05       0.021 at a30       +2031 at a24       2416 N.m          0.02   0.35   0.91
  9/0.90/44/on10 0.249 at a40      -153 at a24        3048 N.m          0.25   0.25   0.73

So: the hole is GONE (0.021 -> 0.249, and the curve is a flat plateau at ~0.25 from alpha 28 to 45 rather than a
dip), the anti-damping the 0012 ITSELF HAS is gone on the Pitts, and alpha 60 is 2.5x the 0012. It does NOT beat
the 0012 at alpha 45 (0.25 against 0.30, and -35 % in absolute N.m), and alpha-0 elevator power is 22 % down,
which is intrinsic: a plate at Cl_max 0.90 cannot make what a 0012 makes at 1.30, and no parameter in this file
changes that.

FLEET-WIDE IT IS BETTER THAN WHAT SHIPS BUT NOT PERFECT, and that is the honest answer to requirement 2. Worst
dM/dq in alpha 10-60, today's 0012 against 9/0.90/blend 44/onset 10:
  pitts +610 -> -153 (clean)   great-lakes +817 -> -60 (clean)   pa25-pawnee +1596 -> -230 (clean)
  jn4-jenny +1442 -> +29       glider-2-33 +327 -> +50           decathlon +1583 -> +69
  stearman +1050 -> +80        pa18-cub/bush +1613 -> +111       j3-cub +1560 -> +120
  pa18-floats +813 -> +203     j3-cub-floats +796 -> +220
Clean on three types including the one he judges by, and 7-50x smaller on the other nine. Every authority floor
rises from 0.00-0.12 (at 12/1.05) to 0.11-0.25. WHERE THE RESIDUE COMES FROM is the tail's own aspect ratio, and
it is why one aeroplane could never settle this: FiniteSpanSection stretches the alpha axis by each point's own
induced angle, so a LOW-AR tail pushes the plate's post-stall collapse out to a higher geometric angle. The Pitts
(hStab AR 2.75) and the Jenny (2.56) are the two lowest in the fleet; AR 3.3-3.9 puts the collapse on alpha 20,
and the 2-33's AR 6.58 puts it at 15-16. The fit was originally chosen on the least representative tail in the
set. Closing the last +29..+220 needs either a per-type blend (defensible - the hand-over width is a function of
aspect ratio) or a knob with no source, and the second is not worth having.

WHAT IT DOES NOT CHANGE, and the hypothesis behind it. The reason for this work was that the 0012's cambered
stall collapse looked like the cause of the Pitts' too-flat left spin and its weak elevator and rudder at high
alpha. Measured, it is not: with the plate the flat spin sits at alpha 92-94 against 91 before (very slightly
FLATTER, the wrong way), and Beggs-Mueller still fails exactly as the owner found in flight - 15 s of idle plus
full opposite rudder decays the yaw rate only 4.5 -> 4.0 with the stick pinned aft. The section was wrong and is
now right; it was not the mechanism.

FIN. The horizontal tail goes through `FiniteSpanSection` (`AeroModel.TailSections` derives a table for
`key == "hStab"`, which `SystemKey` maps the elevator onto too), so it gets this 2-D plate and is rescaled per
type from the surface's own aspect ratio. The FIN AND RUDDER DO NOT - that is staged, pending their own
validation - so their table is used raw, and `fin-lowAR` was accordingly already three-dimensional: slope
3.724/rad against naca0012-like's 6.302, no camber, zero Cm, Cl_max 1.16 held flat from 25 to 30 deg. THAT IS
ALREADY A LOW-ASPECT-RATIO PLATE and it should be left alone: the owner's note was about the stabiliser ("not
0012"), and 0012 is only on the hStab. The fin keeps it, so FLAT_PLATE_ONLY defaults to `hstab`.

Converting the fin as well was tried and is wrong, decisively: with the fin on a plate the Pitts WILL NOT SPIN
AT ALL - alpha 11 at 72 kt where the developed flat spin is alpha 92 at 24 kt - while the stab alone spins
normally. A low-AR surface keeps lifting far past where a 2-D section stalls (vortex lift; Hoerner's low-AR
plates reach Cl 1.0-1.2 at 30-40 deg), and that side force at 25-50 deg is what sustains autorotation. The
alpha-axis mapping cannot manufacture it: it moves points along alpha and leaves Cl alone, so a plate pushed
through it still peaks at the plate's own Cl_max and gives only ~66 % of `fin-lowAR` at 40 deg. `flat-plate-fin`
and FLAT_PLATE_ONLY=fin stay in the tool as the instrument that measured this, not as a thing to ship.

SEPARATELY, AND NOW DONE: `naca0012-like` used to be on the vStab of eleven of the fourteen types - a 2-D table
on a surface that never goes through `FiniteSpanSection`, so those fins flew a 6.30/rad slope where ~3.7 is
right, and their directional stiffness at small sideslip was roughly 70 % too high. All 26 fins moved to
`fin-lowAR` on 2026-11-10 (daf52725), so no vStab in the fleet is on `naca0012-like` any more; finding one there
is now a mistake to report rather than a conversion to make. IT ALSO MEANS THE PITTS' FIN AND ITS STABILISER
WOULD OTHERWISE MOVE IN ONE BUILD - its fin lift slope fell about 40 % that afternoon and its pinned spin tests
have been failing since - so the stabiliser cannot be judged in the air until that is settled or reverted. This
is the reason the conversion is a recommendation here and not a change to the configs.

WHY VITERNA AND NOT `poststall_plate.py`'s RELATION. That tool gives the fleet's WINGS the fully-separated plate,
CN = CD90*sin(a), i.e. CL = CD90*sin(a)*cos(a) - the owner/CFI validated its shape against the extra-sym chart
(secondary peak below the primary, ~zero lift at 90 deg). Taken from the stall it would drop this tail's CN
from 0.80 to 0.19, 77 %, because a surface one degree past its stall is not a plate broadside to the flow;
that is why `poststall_plate.py` blends into it over 10 deg and keeps each wing's own stall drop. Viterna &
Corrigan is the standard way to carry a section from its stall onto the plate instead of starting there, so it
is what the tail gets. Both agree where it matters: a secondary peak below the primary (0.68 at 40 deg against
the pure plate's 0.60 at 45) and zero lift broadside. If the three spin tests want a deeper stall, the pure
relation is the next candidate, not a hand-tuned curve.

WHAT THIS DOES NOT FIX, and a correction to what this file used to claim. The plate still loses NORMAL force at
its stall, and MORE of it than the 0012 did, not less. This paragraph used to read "Cn peaks 0.804 at 8 deg and
falls to 0.655 by 18, a dip of 18.5 %" and conclude the plate made the dip "a THIRD smaller" than the 0012's
0.256 / 21 %. Both numbers are the VITERNA branch (POSTSTALL=viterna) at the old 8 deg / 0.80 fit, and neither is
what ships: with the fully separated plate relation, which is what POSTSTALL defaults to and what Ty validated on
the wings, the 2-D dip in Cn is

      stall / Cl_max / blend      peak            floor            dip
      12 / 1.05 / 10            1.059 at 11.2    0.452 at 22.0    0.607  (57 %)   <- the fit he withdrew
       9 / 0.90 / 10            0.902 at  9.0    0.393 at 19.0    0.509  (56 %)
       9 / 0.90 / 30            0.902 at  9.0    0.664 at 27.6    0.238  (26 %)
       9 / 0.90 / 36            0.902 at  9.0    0.693 at 28.5    0.210  (23 %)
       9 / 0.90 / 44            0.902 at  9.0    0.717 at 29.8    0.185  (21 %)   <- the default
      NACA 0012 (what it replaces) 1.211 at 14   0.955 at 25      0.256  (21 %)

So the OLD fit's dip was 2.4x the section it replaced, not a third of it, and that collapse is the mechanism of
both the alpha-30 hole and the anti-damping. THE BLEND IS WHAT FIXES IT, and this table is why: widening the
hand-over does not merely spread the dip out, it RAISES THE FLOOR, because the fully separated plate it blends
towards is itself climbing (CN = Cd_max*sin(a) is 0.33 at 16 deg and 0.59 at 30). At 44 deg the plate's dip is
0.185 against the 0012's 0.256 - shallower in absolute terms and the same 21 % in relative - which is the first
setting for which this file's old claim is actually true. It is still a dip: a plate is not a surface whose
normal force rises monotonically out of its stall, so `AeroModel.MonotonicPastStall` must NOT be deleted as
redundant on the strength of this change.

Cm is zero throughout: a flat plate's centre of pressure is its aerodynamic centre in the attached range, and
these tails carry their pitching moment through the strip geometry, not the section Cm (the old `fin-lowAR` was
zero Cm for the same reason).

  python3 flat_plate.py --print                      # the table, and its Cn monotonicity
  python3 flat_plate.py configs/aircraft/*.json      # add `flat-plate` and point the fabric tails at it
"""
import io, json, math, os, sys

CD0 = 0.020        # fabric over ribs, a tube spar and a stitched trailing edge (the old tables: 0.008-0.020)
CD_MAX = 1.19      # Viterna & Corrigan 1.11 + 0.018*AR at tail aspect ratios; Hoerner's AR 1-5 plates: 1.18-1.20
# FLAT_PLATE_ASTALL / FLAT_PLATE_CLMAX override these for the fit against the spin tests (see THE FIT below).
A_STALL = 9.0      # deg   } see THE FIT: the 0006 line, the thin end of a 6-8 % fabric-over-ribs tail
CL_STALL = 0.90    #       }

# The alpha stations: dense through the attached range and the stall, coarser broadside - and MIRRORED about
# 90 deg, because the reversed-flow side has the same sharp stall and needs the same resolution to resolve it.
_FWD = [0, 2, 4, 6, 8, 9, 10, 11, 12, 14, 16, 18, 21, 25, 28, 30, 33, 35, 38, 40, 45, 50, 60, 70, 80, 90]
_DEG = _FWD + [180 - d for d in reversed(_FWD[:-1])]


A_STALL = float(os.environ.get('FLAT_PLATE_ASTALL', A_STALL))
CL_STALL = float(os.environ.get('FLAT_PLATE_CLMAX', CL_STALL))
# 44 deg, not the 10 this started with: the 10 was `poststall_plate.py`'s WING number and a wing is AR 6-8 where
# these tails are 2.6-6.6. A low-AR plate's lift does not collapse at a 2-D stall and FiniteSpanSection cannot
# manufacture that, so the gradual hand-over has to be in the 2-D table. See BLEND in THE FIT: this is the axis
# that decides whether the tail has anti-damping, and 30-44 all land in the same place.
BLEND_DEG = float(os.environ.get('FLAT_PLATE_BLEND', 44.0))
POSTSTALL = os.environ.get('FLAT_PLATE_POSTSTALL', 'plate')   # plate | viterna — see WHY THE PLATE RELATION


def _viterna(a_s=None, cl_s=None):
    a_s = A_STALL if a_s is None else a_s
    cl_s = CL_STALL if cl_s is None else cl_s
    a = math.radians(a_s)
    cd_s = CD0 + (cl_s * cl_s) / (math.pi * 4.0)              # the stalling plate's own drag at a_s
    a1, b1 = CD_MAX / 2.0, CD_MAX
    a2 = (cl_s - CD_MAX * math.sin(a) * math.cos(a)) * math.sin(a) / (math.cos(a) ** 2)
    b2 = (cd_s - CD_MAX * math.sin(a) ** 2) / math.cos(a)
    return a1, a2, b1, b2


def coeffs(deg, a_stall=None, cl_stall=None, blend=None):
    """Cl, Cd of the 2-D plate at 0 <= deg <= 180.

    Past 90 deg the plate is simply a plate facing the other way: it has no camber and no distinguished
    leading edge, so Cl(180-a) = -Cl(a) and Cd(180-a) = Cd(a). Taking it from the symmetry rather than from
    Viterna also avoids that fit's cos^2(a)/sin(a) term diverging as sin(a) -> 0 (it gave Cl +1.2 at 175 deg).
    """
    if deg > 90.0:
        cl, cd = coeffs(180.0 - deg, a_stall, cl_stall, blend)
        return -cl, cd
    A_S = A_STALL if a_stall is None else a_stall
    CL_S = CL_STALL if cl_stall is None else cl_stall
    B = BLEND_DEG if blend is None else blend
    a = math.radians(deg)
    if deg >= A_S:
        if POSTSTALL == 'plate':
            # The fully separated plate: the force is normal to the chord and CN = CD_max*sin(a), so
            #   CL = CD_max*sin(a)*cos(a),  CD = CD0 + CD_max*sin^2(a)
            # blended in linearly over BLEND_DEG from the stall point, which is `poststall_plate.py`'s relation
            # for the fleet's wings (Hoerner, Fluid-Dynamic Lift ch. 4 and Drag ch. 3; Ostowari & Naik
            # SERI/STR-217-2559; Critzos & Heyson NACA TN 3361), validated there against the owner's chart.
            cl = CD_MAX * math.sin(a) * math.cos(a)
            # CD0*cos^2 + CD_max*sin^2, not CD0 + CD_max*sin^2, so broadside reads exactly CD_max (1.190) and
            # not 1.210: the 90 deg value is the measured reference the whole table is pinned to.
            cd = CD0 * math.cos(a) ** 2 + CD_MAX * math.sin(a) ** 2
            if B > 0.0 and deg < A_S + B:
                t = (deg - A_S) / B
                cl_s, cd_s = CL_S, CD0 + CL_S * CL_S / (math.pi * 4.0)
                cl, cd = cl_s + (cl - cl_s) * t, cd_s + (cd - cd_s) * t
            return cl, max(CD0, cd)
        a1, a2, b1, b2 = _viterna(A_S, CL_S)
        cl = a1 * math.sin(2 * a) + (a2 * math.cos(a) ** 2 / math.sin(a) if math.sin(a) > 1e-9 else 0.0)
        cd = b1 * math.sin(a) ** 2 + b2 * math.cos(a)
        return cl, max(CD0, cd)
    # Attached: the 2-pi line, eased onto Cl_max over the last third so the slope does not break at the stall.
    lin = 2.0 * math.pi * a
    knee = math.radians(A_S) * 2.0 / 3.0
    if a <= knee: cl = lin
    else:
        t = (a - knee) / (math.radians(A_S) - knee)              # 0..1
        cl = lin + (CL_S - 2.0 * math.pi * math.radians(A_S)) * t * t
    return cl, CD0 + cl * cl / (math.pi * 4.0)


FIN_SLOPE = 3.724   # `fin-lowAR`'s own measured slope: see FIN below


def finite_span(t, a0=2.0 * math.pi, a=None):
    """The same mapping `AeroModel.FiniteSpanSection` applies: each point moves to the geometric angle at which
    the finite surface makes that section lift, alpha = alpha_2D + Cl*(1/a - 1/a0) (nonlinear lifting line,
    Sivells & Neely, NACA Report 865), keeping only the points that advance, outward from zero both ways.

    NOT MonotonicPastStall: that patch is applied inside the sim to tables it derives, and whether it should
    survive this change is an open question (see above), so a table generated here must not bake it in.
    """
    a = FIN_SLOPE if a is None else a
    k = 1.0 / a - 1.0 / a0
    geo = [x + c * k for x, c in zip(t['alphaRad'], t['cl'])]
    z = min(range(len(geo)), key=lambda i: abs(t['alphaRad'][i]))
    keep = [z]
    for i in range(z + 1, len(geo)):
        if geo[i] > geo[keep[-1]]: keep.append(i)
    neg, last = [], geo[z]
    for i in range(z - 1, -1, -1):
        if geo[i] < last: neg.append(i); last = geo[i]
    idx = list(reversed(neg)) + keep
    return {'alphaRad': [round(geo[i], 5) for i in idx], 'cl': [t['cl'][i] for i in idx],
            'cd': [t['cd'][i] for i in idx], 'cm': [0.0] * len(idx)}


def stations(a_stall=None, blend=None):
    """The alpha stations, with THIS table's own breakpoints added.

    `_FWD` is dense through 8-18 deg because that is where the 12 deg fit's stall and blend live. A table is read
    by linear interpolation between stations, so a candidate whose stall is at 9 deg, or whose blend is only 4 deg
    wide, would be measured through a chord across its own corner rather than at it - the search would then be
    comparing station resolution between candidates instead of comparing plates. So every table carries its stall
    angle and the quarter points of its blend as stations of its own. Nothing is removed; this only ever adds.
    """
    a_s = A_STALL if a_stall is None else a_stall
    b = BLEND_DEG if blend is None else blend
    extra = [a_s] + [a_s + b * k / 4.0 for k in range(1, 5)] if b > 0 else [a_s]
    fwd = sorted({round(x, 2) for x in _FWD} | {round(x, 2) for x in extra if 0.0 < x < 90.0})
    return fwd + [round(180.0 - d, 2) for d in reversed(fwd[:-1])]


def table(a_stall=None, cl_stall=None, blend=None):
    _deg = stations(a_stall, blend)
    alpha, cl, cd = [], [], []
    for d in reversed(_deg[1:]):          # -180 .. -2
        c, q = coeffs(d, a_stall, cl_stall, blend)
        alpha.append(round(math.radians(-d), 5)); cl.append(round(-c, 4)); cd.append(round(q, 4))
    for d in _deg:
        c, q = coeffs(d, a_stall, cl_stall, blend)
        alpha.append(round(math.radians(d), 5)); cl.append(round(c, 4)); cd.append(round(q, 4))
    return {'alphaRad': alpha, 'cl': cl, 'cd': cd, 'cm': [0.0] * len(alpha)}


# THE SET, and why each one is in it. Owner 2026-11-09, asked which types: "cub great lakes decathlon super cub
# etc", then "the 2.33 is also flat plate", plus his own Pitts. "etc" is not a list, so the set is worked out from
# what each aeroplane's HORIZONTAL TAIL is actually covered with - not from its fuselage, because `structureType`
# in these configs describes the FUSELAGE and the two do not always agree: the 2-33 has an aluminium WING on a
# fabric-covered steel-tube fuselage and a fabric tail, and the Jenny's fuselage is a wooden truss. Nothing in the
# configs records a TAIL's covering - only `great-lakes-like` and `pitts-s2b-like` carry `construction.covering`
# at all, both "fabric" - so each one below names its own source.
#
#   j3-cub-like, j3-cub-floats-like   fabric over a welded steel-tube stabiliser and elevator (TCDS A-691, and
#                                     the config's own structureTypeSource: "fabric covered"). Ty: "cub".
#   pa18-cub-like, pa18-bush-like,    fabric over steel tube; one tail on all three (TCDS 1A2). Ty: "super cub".
#     pa18-floats-like
#   great-lakes-like                  fabric; `construction.covering: "fabric"` in the config. Ty: "great lakes".
#   decathlon-8kcab-like              fabric over steel tube (TCDS A21CE). Ty: "decathlon".
#   pitts-s2b-like                    fabric; `construction.covering: "fabric"`. HIS OWN AEROPLANE, and the one
#                                     every measurement here was made on: "the pitts stabilizer is flat plate all
#                                     the time not 0012".
#   stearman-pt17-like                fabric over welded steel tube (TCDS A-743).
#   jn4-jenny-like                    fabric over a wooden tail frame. Its FUSELAGE is wood, which is exactly why
#                                     this set cannot be read off `structureType`.
#   pa25-pawnee-like                  fabric over steel tube (TCDS 2A8; the config's structureTypeSource).
#   glider-2-33-like                  fabric over steel tube (TCDS G16EA). Ty: "the 2.33 is also flat plate".
#
# HELD BACK, deliberately: an ambiguous type is left alone rather than guessed at.
#   aircam-like, aircam-amphib-like   WERE in this set when it was first written and are now OUT. The only
#                                     evidence in the repo is the config's own structureTypeSource - "riveted
#                                     6061 aluminium sheet and tube airframe ... no steel truss" - which does not
#                                     say what the TAIL is covered with. It is also the only member of the old
#                                     set whose fuselage is neither steel-tube-and-fabric nor wood, and the only
#                                     type in the fleet whose ELEVATOR strips are on `naca0012-like` instead of
#                                     `fin-lowAR`: both say it was built to a different pattern from the rest.
#                                     One word from Ty puts it back, and the conversion is then one command.
#   cassutt-f1-like                   Formula One racer built from plans: the tail is fabric over steel tube on
#                                     some airframes and aluminium sheet on others, and the config records only
#                                     the fuselage. Genuinely per-airframe, so not decidable here.
#   geebee-r2-like                    fabric over steel tube is very likely (a 1932 racer, and its own
#                                     structureTypeSource says the fuselage is "steel-tube ... and fabric"), but
#                                     no source in the repo states the TAIL and Ty has not named the type.
#   extra-300-like                    steel-tube FUSELAGE, so it reads like one of these, but its tail surfaces
#                                     are not fabric over tube. It keeps its airfoil section.
# Composite and metal types - 172, PA-28, SR22, Beaver, P-51, the twins, the jets, the composite gliders - keep an
# airfoil section and were never candidates.
FABRIC = {'j3-cub-like', 'j3-cub-floats-like', 'pa18-cub-like', 'pa18-bush-like', 'pa18-floats-like',
          'great-lakes-like', 'decathlon-8kcab-like', 'pitts-s2b-like', 'stearman-pt17-like', 'jn4-jenny-like',
          'pa25-pawnee-like', 'glider-2-33-like'}
HSTAB = ('hStab', 'elevator')          # rescaled per surface by AeroModel.FiniteSpanSection: give it the 2-D plate
FIN = ('vStab', 'rudder')              # used raw: give it the pre-rescaled plate
TAILS = HSTAB + FIN


def _fmt(t):
    """One line per array, as the hand-authored arrays in these configs are: 85 alpha stations one-per-line
    would be 680 lines of diff per config for a table nobody reads a number at a time."""
    return '{\n' + ',\n'.join('      "%s": [%s]' % (k, ', '.join(repr(round(x, 5)) for x in t[k]))
                               for k in ('alphaRad', 'cl', 'cd', 'cm')) + '\n    }'


def _entries(text, a, b):
    """The `"name": value` entries of the object whose braces span text[a:b], as (name, verbatim text)."""
    out, k = [], a + 1
    while k < b - 1:
        q = text.find('"', k)
        if q < 0 or q >= b - 1: break
        e = text.index('"', q + 1)
        name = text[q + 1:e]
        c = text.index(':', e) + 1
        while text[c] in ' \n': c += 1
        if text[c] in '{[':
            close = {'{': '}', '[': ']'}[text[c]]
            d, m = 0, c
            while True:
                if text[m] == text[c]: d += 1
                elif text[m] == close:
                    d -= 1
                    if d == 0: break
                m += 1
            m += 1
        else:
            m = c
            while text[m] not in ',\n': m += 1
        out.append((name, text[q:m]))
        k = m
        while k < b and text[k] in ', \n': k += 1
    return out


def apply_text(text):
    """Point the tail strips at `flat-plate` and swap the tables, BY TEXT.

    Never a json round trip: these configs have hand-authored one-line arrays (`"stationX": [-0.3, ...]`) that
    json.dump expands, which churned 92 lines of j3-cub-like for a two-line change. The airfoilTables object is
    rebuilt from its own entries, so every table that stays keeps its text byte for byte.
    """
    if '"flat-plate"' in text: return text, []
    dead = [n for n in ('naca0012-like', 'fin-lowAR') if '"airfoil": "%s"' % n in text]
    if not dead: return text, []
    cfg = json.loads(text)
    # Which table each tail strip should get, by the surface it belongs to.
    want = {}
    for sf in cfg.get('surfaces', []):
        sid = str(sf.get('id', ''))
        only = os.environ.get('FLAT_PLATE_ONLY', 'hstab')    # hstab (ships) | fin | both — see FIN above
        tgt = ('flat-plate' if any(sid == t or sid.startswith(t) for t in HSTAB) and only != 'fin'
               else 'flat-plate-fin' if any(sid == t or sid.startswith(t) for t in FIN) and only != 'hstab' else None)
        if tgt is None: continue
        for st in sf.get('strips', []):
            if st.get('airfoil') in dead: want.setdefault((sf['id'], st.get('airfoil')), tgt)
    # Rewrite surface by surface, so a strip gets the table its own surface needs.
    for (sid, old_af), tgt in want.items():
        h = '      "id": "%s",' % sid
        a = text.index(h)
        b = text.find('\n      "id": "', a + len(h))
        if b < 0: b = len(text)
        text = text[:a] + text[a:b].replace('"airfoil": "%s"' % old_af, '"airfoil": "%s"' % tgt) + text[b:]

    head = '  "airfoilTables": {'
    a = text.index(head) + len(head) - 1
    d, b = 0, a
    while True:
        if text[b] == '{': d += 1
        elif text[b] == '}':
            d -= 1
            if d == 0: break
        b += 1
    b += 1
    body = _fmt(table())
    keep = [('flat-plate', '"flat-plate": ' + body)]
    if '"flat-plate-fin"' in text:
        fb = _fmt(finite_span(table()))
        keep.append(('flat-plate-fin', '"flat-plate-fin": ' + fb))
    if '"airfoil": "flat-plate"' not in text: keep.pop(0)
    live = {st.get('airfoil') for sf in json.loads(text).get('surfaces', []) for st in sf.get('strips', [])}
    keep += [(n, t) for n, t in _entries(text, a, b) if n not in dead or n in live]
    inner = ',\n'.join('    ' + t for _, t in keep)
    return text[:a] + '{\n' + inner + '\n  }' + text[b:], dead


def emit_grid(out, a_stalls, cl_maxes, blends):
    """Every candidate table in one JSON object, keyed "aS<stall>_cl<Cl_max>_b<blend>", for
    `FlatPlateSearchProbe` (FLAT_PLATE_SEARCH=<this file>) to inject one at a time and measure.

    The GENERATOR is the single source of truth: the search measures the same function that writes the configs,
    so a setting that wins here is written out by `--apply` with no second implementation to drift from it.
    Candidates whose Cl_max is above the 2-pi line at their own stall angle are skipped - the attached branch
    cannot reach a lift the thin-airfoil slope does not make by then, and the easing would have to bend UPWARD.
    """
    cand = {}
    for a_s in a_stalls:
        lin = 2.0 * math.pi * math.radians(a_s)
        for cl in cl_maxes:
            if cl > lin + 1e-9: continue
            for b in blends:
                cand['aS%g_cl%g_b%g' % (a_s, cl, b)] = table(a_s, cl, b)
    json.dump(cand, out)
    return len(cand)


if __name__ == '__main__':
    if '--grid' in sys.argv:
        # The swept space, and why each axis has these bounds, is in THE SEARCH in the docstring.
        A = [float(x) for x in os.environ.get('GRID_ASTALL', '8,9,10,11,12,13,14,15,16').split(',')]
        C = [float(x) for x in os.environ.get('GRID_CLMAX', '0.70,0.75,0.80,0.85,0.90,0.95,1.00,1.05,1.10,1.15,1.20,1.25,1.30').split(',')]
        B = [float(x) for x in os.environ.get('GRID_BLEND', '2,4,6,8,10,14,18,22,26,30,36,44').split(',')]
        n = emit_grid(sys.stdout, A, C, B)
        print('', file=sys.stderr); print('%d candidates' % n, file=sys.stderr)
        sys.exit(0)
    if '--scan' in sys.argv: scan(); sys.exit(0)
    if '--print' in sys.argv:
        t = table()
        print('  deg      Cl      Cd      Cn      Ca')
        prev, bad = None, []
        for a, l, d in zip(t['alphaRad'], t['cl'], t['cd']):
            deg = math.degrees(a)
            if deg < -0.5: continue
            cn = l * math.cos(a) + d * math.sin(a); ca = d * math.cos(a) - l * math.sin(a)
            flag = ''
            if prev is not None and cn < prev - 1e-6: flag = '  <-- Cn FALLS'; bad.append(round(deg, 1))
            prev = cn
            print('%5.0f  %+6.3f  %6.3f  %6.3f  %+6.3f%s' % (deg, l, d, cn, ca, flag))
        print('\nCn falls at:', bad if bad else 'nowhere (monotone 0 -> 90 deg)')
        print('slope about 0: %.3f /rad   (2*pi = %.3f)' % ((coeffs(2)[0] - -coeffs(2)[0]) / math.radians(4), 2 * math.pi))
        sys.exit(0)
    dry = '--dry' in sys.argv
    for f in [x for x in sys.argv[1:] if not x.startswith('--')]:
        text = io.open(f, encoding='utf-8').read()
        if json.loads(text).get('id') not in FABRIC: continue
        out, ch = apply_text(text)
        if not ch: continue
        json.loads(out)          # it must still parse
        if not dry: io.open(f, 'w', encoding='utf-8').write(out)
        print('%-26s %s%s' % (json.loads(out)['id'], ' -> flat-plate: ' + ' '.join(ch), '  (dry)' if dry else ''))


def scan():
    """How deep is the plate's own stall dip in NORMAL force, over stall angle and Cl_max?

    dCn/dalpha < 0 is negative pitch damping by definition, so the depth of this dip is the thing that decides
    whether `AeroModel.MonotonicPastStall` is still needed once the tails are plates. For reference the
    NACA 0012 the Pitts flew drops Cn 1.211 (14 deg) -> 0.955 (25 deg): a dip of 0.256, 21 %.
    """
    print(' a_s  Cl_max |  Cn peak   Cn floor   dip     %    | at deg')
    for a_s in (7.0, 8.0, 9.0, 10.0, 11.0, 12.0):
        for cl_s in (0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90):
            cn, best, floor, dtop, dbot = [], -9, 9, 0, 0
            for d in [x * 0.5 for x in range(0, 181)]:
                l, q = coeffs(d, a_s, cl_s); a = math.radians(d)
                v = l * math.cos(a) + q * math.sin(a)
                if v > best and d <= a_s + 1: best, dtop = v, d
                cn.append((d, v))
            for d, v in cn:
                if d >= dtop and v < floor: floor, dbot = v, d
                if d > 60: break
            print('%4.0f   %4.2f   | %7.3f   %7.3f   %6.3f  %5.1f%%  | %.0f -> %.0f'
                  % (a_s, cl_s, best, floor, best - floor, 100 * (best - floor) / best, dtop, dbot))
