"""THE FABRIC TAIL IS A FLAT PLATE (owner 2026-11-09: "the pitts stabilizer is flat plate all the time not 0012",
and asked which types: "cub great lakes decathlon super cub etc", then "the 2.33 is also flat plate").

Every tail in the fleet flew `naca0012-like` (hStab) and `fin-lowAR` (elevator, fin, rudder). A fabric-and-tube
tail is neither: it is a thin plate on a tube spar, with no camber and no 0012 nose radius, so it has no cambered
stall break to collapse through. That collapse is what put a 12-14 deg notch in the tail's force and damping and
made dM/dq go POSITIVE (anti-damping) at alpha 30.

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

THE FIT (stall 12 deg, Cl_max 1.05), owner 2026-11-09: "fit the flat plate table against those three tests".
Bounded from both sides, which is why it is not a tuned number:
  * PHYSICS. A razor-thin plate stalls near Cl 0.72 at 8 deg (Flachsbart; Hoerner ch. 4), but a Pitts stabiliser
    is fabric over ribs on a tube spar - a thin SYMMETRIC section with a real nose radius, 6-8 % thick. Thin
    symmetric sections at tail Reynolds numbers: 0006 ~0.92 at 9 deg, 0009 ~1.1 at 11, 0012 ~1.3 at 14. 1.05 at
    12 sits between 0006 and 0009. 0.80 would be a bare plate, which this is not.
  * FLIGHT. Below ~1.05 the aeroplane will not ENTER its flat spin: `BeggsMuellerProbe` (BEGGS=1) holds the
    developed spin at alpha 92, r -4.5 at 1.05/12, and at 0.95/11 the entry collapses to alpha 11 and 73 kt -
    full aft stick no longer holds it stalled. The real aeroplane enters that spin, so the entry working is an
    acceptance test, not a preference.

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

SEPARATELY, AND NOT FIXED HERE: on eleven of the fourteen types `naca0012-like` is on the vStab as well - a 2-D
table on a surface that never goes through `FiniteSpanSection`, so those fins fly a 6.30/rad slope where ~3.7 is
right. Their directional stiffness at small sideslip is therefore roughly 70 % too high. Moving them to
`fin-lowAR` is the fix and it moves every one of those types' handling, so it wants its own change and its own
flight test.

WHY VITERNA AND NOT `poststall_plate.py`'s RELATION. That tool gives the fleet's WINGS the fully-separated plate,
CN = CD90*sin(a), i.e. CL = CD90*sin(a)*cos(a) - the owner/CFI validated its shape against the extra-sym chart
(secondary peak below the primary, ~zero lift at 90 deg). Taken from the stall it would drop this tail's CN
from 0.80 to 0.19, 77 %, because a surface one degree past its stall is not a plate broadside to the flow;
that is why `poststall_plate.py` blends into it over 10 deg and keeps each wing's own stall drop. Viterna &
Corrigan is the standard way to carry a section from its stall onto the plate instead of starting there, so it
is what the tail gets. Both agree where it matters: a secondary peak below the primary (0.68 at 40 deg against
the pure plate's 0.60 at 45) and zero lift broadside. If the three spin tests want a deeper stall, the pure
relation is the next candidate, not a hand-tuned curve.

WHAT THIS DOES NOT FIX - and the plan said it would. The plate still loses NORMAL force at its stall: Cn peaks
0.804 at 8 deg and falls to 0.655 by 18 deg, a dip of 18.5 %, and `scan()` shows NO plausible (stall angle,
Cl_max) pair in 7-12 deg / 0.60-0.90 removes it - the shallowest anywhere on that grid is 17.7 %. The NACA 0012
it replaces dips 21 % (Cn 1.211 at 14 deg -> 0.955 at 25). So a plate is not a surface whose normal force rises
monotonically out of its stall, and `AeroModel.MonotonicPastStall` must NOT be deleted as redundant on the
strength of this change. What the plate does do is make the dip a THIRD smaller in absolute terms (0.149 against
0.256) and move it from 14-25 deg down to 8-18 deg - below the 30-45 deg a spin sits at, which is where the
anti-damping mattered.

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
A_STALL = 12.0     # deg   } see THE FIT
CL_STALL = 1.05    #       }

# The alpha stations: dense through the attached range and the stall, coarser broadside - and MIRRORED about
# 90 deg, because the reversed-flow side has the same sharp stall and needs the same resolution to resolve it.
_FWD = [0, 2, 4, 6, 8, 9, 10, 11, 12, 14, 16, 18, 21, 25, 28, 30, 33, 35, 38, 40, 45, 50, 60, 70, 80, 90]
_DEG = _FWD + [180 - d for d in reversed(_FWD[:-1])]


A_STALL = float(os.environ.get('FLAT_PLATE_ASTALL', A_STALL))
CL_STALL = float(os.environ.get('FLAT_PLATE_CLMAX', CL_STALL))
BLEND_DEG = float(os.environ.get('FLAT_PLATE_BLEND', 10.0))
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


def coeffs(deg, a_stall=None, cl_stall=None):
    """Cl, Cd of the 2-D plate at 0 <= deg <= 180.

    Past 90 deg the plate is simply a plate facing the other way: it has no camber and no distinguished
    leading edge, so Cl(180-a) = -Cl(a) and Cd(180-a) = Cd(a). Taking it from the symmetry rather than from
    Viterna also avoids that fit's cos^2(a)/sin(a) term diverging as sin(a) -> 0 (it gave Cl +1.2 at 175 deg).
    """
    if deg > 90.0:
        cl, cd = coeffs(180.0 - deg, a_stall, cl_stall)
        return -cl, cd
    A_S = A_STALL if a_stall is None else a_stall
    CL_S = CL_STALL if cl_stall is None else cl_stall
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
            if deg < A_S + BLEND_DEG:
                t = (deg - A_S) / BLEND_DEG
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


def table():
    alpha, cl, cd = [], [], []
    for d in reversed(_DEG[1:]):          # -180 .. -2
        c, q = coeffs(d); alpha.append(round(math.radians(-d), 5)); cl.append(round(-c, 4)); cd.append(round(q, 4))
    for d in _DEG:
        c, q = coeffs(d); alpha.append(round(math.radians(d), 5)); cl.append(round(c, 4)); cd.append(round(q, 4))
    return {'alphaRad': alpha, 'cl': cl, 'cd': cd, 'cm': [0.0] * len(alpha)}


# The fabric-and-tube types (owner 2026-11-09). Composite and metal types keep an airfoil section.
FABRIC = {'j3-cub-like', 'j3-cub-floats-like', 'pa18-cub-like', 'pa18-bush-like', 'pa18-floats-like',
          'great-lakes-like', 'decathlon-8kcab-like', 'pitts-s2b-like', 'stearman-pt17-like', 'jn4-jenny-like',
          'pa25-pawnee-like', 'aircam-like', 'aircam-amphib-like', 'glider-2-33-like'}
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


if __name__ == '__main__':
    if '--scan' in sys.argv: scan(); sys.exit(0)

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
