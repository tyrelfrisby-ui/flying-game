r"""THE FIN AND RUDDER SECTION: HIGH AT 20-40 DEGREES AND STILL RISING (2026-11-09).

Owner's report: "the elevator and rudder authority in the spin are both significantly weaker than the real
thing", and after three builds of other work, "same flat spin problems". Measured cause (SpinAuthorityProbe,
SPIN_AUTH=1; commit e7cd404c): side force on a vertical strip IS the normal-force coefficient,
Fy/qS = Cl*cos a + Cd*sin a, and `fin-lowAR` holds that NORMAL FORCE FLAT from 25 to 90 degrees -
1.14, 1.16, 1.10, 1.07, 1.03, 1.08, 1.14, 1.19 - with dCN/da of +-0.006 per degree and NEGATIVE from 30 to 55.
A flat CN is a dead control: the whole authority a +-30 deg rudder can command is

    local a   10     20     30     45     53     60     70     90
    dCN     2.17   1.70   1.03   0.22  0.047  0.032  0.076  0.149

and in a developed spin the fin sits at 37-90 deg of local angle, because its in-plane flow is dominated by
r x arm rather than by the descent. Hence 0.2 deg/s2 of yaw at alpha 91 where stopping the rotation wants ~135.

WHY THE FLATNESS IS FORCED, and why this cannot be fixed by adding force. CN on a flat surface cannot exceed
the broadside plate value, about 1.19 (Hoerner, Fluid-Dynamic Drag ch. 3, plates normal to the flow at these
aspect ratios; Viterna & Corrigan's 1.11 + 0.018*AR). `fin-lowAR` is already at 1.06 by 20 degrees - 89 % of its
own ceiling at a fifth of the angle - so there is only 0.13 of headroom left across the remaining 70 degrees.
The curve has nowhere to go. **To give the rudder slope, CN at 20-40 degrees has to come DOWN.** That is
arithmetic, not a modelling choice.

AND IT CANNOT COME DOWN TO THE BARE PLATE, which was tried on 2026-11-09 and is decisively wrong: with the fin
on a 2-D plate the Pitts WILL NOT SPIN AT ALL - alpha 11 at 72 kt where its flat spin is alpha 80-92 at 24 kt -
because the plate drops CN at 20-50 deg by 2.6x (0.41 against 1.06 at 20) and that side force IS the
autorotation drive. So the fin wants a curve that keeps most of the drive and still rises.

THE CONSTRUCTION. Three-dimensional throughout, because the fin and rudder are used RAW - `AeroModel.TailSections`
derives a finite-span table only for `key == "hStab"`, the vertical tail being staged out of that path - so this
table IS the surface, not a section to be rescaled. Hence:
  * the LINEAR SLOPE is kept at `fin-lowAR`'s own 3.724/rad exactly. It sets directional stiffness in ordinary
    flight and must not move: a 2-D 6.3/rad here would be a 69 % authority change dressed up as a section fix.
  * Cl_max at `CL_MAX` and `A_STALL`, eased on over the last third so the slope does not break.
  * past the stall, blended over `BLEND_DEG` into the fully separated plate, CN = CD_MAX*sin(a), i.e.
    Cl = CD_MAX*sin(a)*cos(a) and Cd = CD0*cos^2(a) + CD_MAX*sin^2(a) - the same relation `poststall_plate.py`
    gives the fleet's wings and the owner validated there against his own chart, and the one `flat_plate.py`
    gives the stabiliser. Past the blend CN rises monotonically to CD_MAX, so the rudder always has slope.
  * Cm zero throughout, as `fin-lowAR` was: a fin carries its moment through the strip geometry.

RESULT: BUILT, MEASURED, AND NOT SHIPPED. The table below does what it was designed to do - CN monotone from 35
deg, and 5-15x the rudder authority where the fin actually sits (dCN at a local 53 deg 0.320 against 0.047, at 60
deg 0.487 against 0.032) - and it still fails, because of a constraint that no single curve can escape:

  * APPLIED TO THE WHOLE VERTICAL TAIL: the forceful entry still spins (alpha 80.2) but the Beggs-Mueller entry
    collapses to alpha 11 at 72 kt. CN at 20-40 deg IS the autorotation drive and this costs 8-39 % of it.
  * APPLIED TO THE RUDDER STRIPS ONLY (leaving vStab and the elevator on `fin-lowAR`, so the drive and the
    elevator are untouched): the flat spin survives exactly, alpha 80.0 at 1.24 s a turn, and the rudder gains
    4-12x at alpha 60-80 with alpha 30 unchanged at 14.6 against 14.9. But the entry still collapses, because the
    rudder's CN at 30 deg of deflection halves (0.597 against 1.162) and THAT is the ordinary rudder power that
    initiates the spin.
  * A RUNTIME SUBSTITUTION instead of a table (SeparatedControlPlateK, reverted): the plate relation is the fully
    separated limit, so imposing it at moderate angles made the ELEVATOR weaker (table CN 1.14 against the
    plate's 0.54 at 27 deg) and the aeroplane never reached the stall at all - 142 kt.

THE WALL, stated once so it is not rediscovered. Authority at a local angle A is CN(A+30) - CN(A-30). CN cannot
exceed the broadside plate, about 1.19. `fin-lowAR` is at 97 % of that ceiling by 30 deg. So authority at A
REQUIRES a low CN at A-30 - and CN at 20-40 deg is simultaneously the autorotation drive AND the rudder power
that enters the spin. The three demands are in direct arithmetic conflict in one curve, and the owner's
"the rudder is much more effective in real life during spin recovery" cannot be met by reshaping it.

WHAT IS LEFT, and it is a new TERM rather than a tuning: a rudder deflected into a strong crossflow redirects the
flow over the whole aft fuselage, acting on the fuselage's crossflow rather than only on its own area. That is
the dominant rudder effect in a developed spin and a strip model has no representation of it - each strip sees
only its own local flow, so no table on the rudder's own 0.87 m2 can carry it.

  python3 fin_section.py --print
  python3 fin_section.py --sweep
  python3 fin_section.py [--dry] configs/aircraft/*.json
"""
import io, json, math, os, sys

SLOPE = 3.724                                               # fin-lowAR's own measured slope, per rad: do not move
CD0 = 0.008
CD_MAX = float(os.environ.get('FIN_CDMAX', 1.194))          # fin-lowAR's own broadside value, kept
A_STALL = float(os.environ.get('FIN_ASTALL', 20.0))         # deg
CL_MAX = float(os.environ.get('FIN_CLMAX', 1.00))
BLEND_DEG = float(os.environ.get('FIN_BLEND', 15.0))
AR_E = 3.65 * 0.85                                          # effective AR x span efficiency, for the induced drag

_FWD = [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 30, 33, 35, 40, 45, 50, 55, 60, 70, 80, 90]
_DEG = _FWD + [180 - d for d in reversed(_FWD[:-1])]


def coeffs(deg):
    """Cl, Cd of the fin at 0 <= deg <= 180 (three-dimensional: this table is the surface)."""
    if deg > 90.0:
        cl, cd = coeffs(180.0 - deg)
        return -cl, cd                                      # a fin reversed is a fin: no camber, no fixed LE
    a = math.radians(deg)
    plate_cl = CD_MAX * math.sin(a) * math.cos(a)
    plate_cd = CD0 * math.cos(a) ** 2 + CD_MAX * math.sin(a) ** 2
    if deg >= A_STALL + BLEND_DEG:
        return plate_cl, plate_cd
    # Attached: the fin's own slope, eased onto Cl_max over the last third so the slope does not break at stall.
    # HELD at Cl_max past the stall rather than extrapolated: the easing term is quadratic, so continuing it
    # into the blend dives (Cl 1.000 at 20 deg to 0.707 at 25), and blending a diving curve into the plate put a
    # 57 % hole in CN at 30 deg. The attached branch's job inside the blend is to be the UNSTALLED reference.
    a_s = math.radians(A_STALL)
    knee = a_s * 2.0 / 3.0
    if a <= knee:
        cl = SLOPE * a
    elif deg <= A_STALL:
        cl = SLOPE * a + (CL_MAX - SLOPE * a_s) * ((a - knee) / (a_s - knee)) ** 2
    else:
        cl = CL_MAX
    cd = CD0 + cl * cl / (math.pi * AR_E)
    if deg <= A_STALL:
        return cl, cd
    t = (deg - A_STALL) / BLEND_DEG                          # 0..1 through the blend
    return cl + (plate_cl - cl) * t, cd + (plate_cd - cd) * t


def table():
    al, cl, cd = [], [], []
    for d in reversed(_DEG[1:]):
        c, q = coeffs(d); al.append(round(math.radians(-d), 5)); cl.append(round(-c, 4)); cd.append(round(q, 4))
    for d in _DEG:
        c, q = coeffs(d); al.append(round(math.radians(d), 5)); cl.append(round(c, 4)); cd.append(round(q, 4))
    return {'alphaRad': al, 'cl': cl, 'cd': cd, 'cm': [0.0] * len(al)}


def CN(deg):
    l, d = coeffs(deg); a = math.radians(deg)
    return l * math.cos(a) + d * math.sin(a)


def _old(cfg='configs/aircraft/pitts-s2b-like.json'):
    t = json.load(open(cfg))['airfoilTables']['fin-lowAR']
    a, cl, cd = t['alphaRad'], t['cl'], t['cd']
    def s(deg):
        x = math.radians(deg)
        for i in range(len(a) - 1):
            if a[i] <= x <= a[i + 1]:
                g = (x - a[i]) / (a[i + 1] - a[i])
                return cl[i] + g * (cl[i + 1] - cl[i]), cd[i] + g * (cd[i + 1] - cd[i])
        return 0.0, 0.0
    def cn(deg):
        l, d = s(deg); x = math.radians(deg); return l * math.cos(x) + d * math.sin(x)
    return s, cn


if __name__ == '__main__':
    if '--print' in sys.argv or '--sweep' in sys.argv:
        s_old, cn_old = _old()
        print('slope about 0: %.3f /rad   (fin-lowAR 3.724)' % ((coeffs(2)[0] + coeffs(2)[0]) / math.radians(4)))
        print('\n  deg     Cl      Cd      CN    | fin-lowAR CN |  dCN a+-30: new   old')
        prev = None
        for d in (0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 53, 60, 70, 80, 90):
            auth_new = abs(CN(d + 30) - CN(d - 30)) if d >= 30 else float('nan')
            auth_old = abs(cn_old(d + 30) - cn_old(d - 30)) if 30 <= d <= 90 else float('nan')
            l, q = coeffs(d)
            flag = '  <- CN falls' if prev is not None and CN(d) < prev - 1e-6 else ''
            prev = CN(d)
            print('  %4d  %+6.3f  %6.3f  %6.3f  |    %6.3f    |     %6.3f  %6.3f%s'
                  % (d, l, q, CN(d), cn_old(d), auth_new, auth_old, flag))
        sys.exit(0)
    dry = '--dry' in sys.argv
    body = '{\n' + ',\n'.join('      "%s": [%s]' % (k, ', '.join(repr(x) for x in table()[k]))
                              for k in ('alphaRad', 'cl', 'cd', 'cm')) + '\n    }'
    for f in [x for x in sys.argv[1:] if not x.startswith('--')]:
        text = io.open(f, encoding='utf-8').read()
        if '"fin-lowAR": {' not in text: continue
        a = text.index('    "fin-lowAR": {')
        d, b = 0, a + len('    "fin-lowAR": ')
        while True:
            if text[b] == '{': d += 1
            elif text[b] == '}':
                d -= 1
                if d == 0: break
            b += 1
        out = text[:a] + '    "fin-lowAR": ' + body + text[b + 1:]
        json.loads(out)
        print('%-26s fin-lowAR replaced%s' % (json.loads(out)['id'], '  (dry)' if dry else ''))
        if not dry: io.open(f, 'w', encoding='utf-8').write(out)
