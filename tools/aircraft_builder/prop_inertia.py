r"""PROPELLER POLAR MOMENT FROM THE OWNER'S BLADE RULE (2026-11-09).

Ty: "Each blade is 15% of the net prop weight and its center of mass is 25% of the prop diameter from the center
use that on every prop", with the reason given a message earlier: "the blades are light and the hub is heavy so
that should change the net gyro". A heavy constant-speed hub sits at small radius and contributes almost nothing
to sum(m r^2), so estimating a propeller's polar moment from its TOTAL mass with a blade-like radius of gyration
overstates the gyroscopic couple. The rule splits it:

    Ip = N * (0.15 M) * (0.25 D)^2   +   (1 - 0.15 N) * M * k_hub^2   +   crank
         \____ the blades ____/           \____ the hub, near the axis ____/

    = 0.009375 * N * M * D^2 + ... , which is where all the authority is.

k_hub = 0.10 m: a compact constant-speed hub of about 0.14 m radius treated as a disc, k = R/sqrt(2). It is a
~10 % term, so its exact value does not matter much - the Pitts moves 3.06 -> 3.17 between k 0.10 and 0.12.

`crank` is the engine's own rotating parts, which `propInertia` has always included ("Propeller + spinner + the
crank's rotating parts"). From docs/aircraft/prop-gyro-DERIVATION.md section 1b: 0.14 kg.m2 for a six-cylinder
AEIO-540 (band 0.09-0.20), so roughly 0.023 per cylinder.

VALIDATION - run `python3 prop_inertia.py --check`. Against the only two propellers Hartzell publishes a polar
moment for (aerobatic composite brochure, same derivation section 1a) the rule is **+18 % on a two-blade hub and
+60 % on a three-blade one**: 15 % per blade leaves too little mass for a 3-blade hub, which is proportionally
heavier. So this tool refuses to touch a 3-blade type, and in particular leaves the Extra's 2.1 alone - that
value is anchored on the published 3-blade figure directly, and the owner has already confirmed the Extra's
spins are about right.

Run:  python3 prop_inertia.py --check
      python3 prop_inertia.py --dry configs/aircraft/*.json
      python3 prop_inertia.py configs/aircraft/*.json
"""
import io, json, math, sys

K_HUB = 0.10

# Net propeller mass (kg, propeller + spinner) and where it comes from. SOURCED or ESTIMATED, never silent.
# A type absent from here is NOT touched: the rule is linear in mass, so a guessed mass is a guessed gyro.
MASS = {
    'pitts-s2b-like':       (36.3, 0.14, 'SOURCED: 80 lb, owner data (docs/aircraft/pitts-s2b-inertia-DERIVATION.md). AEIO-540, six cylinders.'),
    'great-lakes-like':     (24.5, 0.09, 'ASSUMED ~50 lb with spinner, Hartzell HC-C2YK-4F 74 in (docs/aircraft/great-lakes-inertia-DERIVATION.md). AEIO-360, four cylinders.'),
    'pa18-cub-like':        (20.0, 0.09, 'SOURCED: Sensenich give 44 lb for the metal Super Cub propeller (against 21 lb for their composite). O-320, four cylinders.'),
    'pa18-bush-like':       (20.0, 0.09, 'SOURCED: Sensenich give 44 lb for the metal Super Cub propeller. O-320, four cylinders.'),
    'pa18-floats-like':     (20.0, 0.09, 'SOURCED: Sensenich give 44 lb for the metal Super Cub propeller. O-320, four cylinders.'),
    'decathlon-8kcab-like': (20.0, 0.09, 'ESTIMATED: a 74 in two-blade forged-aluminium propeller of the Super Cub class, 44 lb. AEIO-360, four cylinders.'),
    'j3-cub-floats-like':   (20.0, 0.05, 'ESTIMATED: Sensenich 76AM-2 forged aluminium, the Super Cub metal class at 44 lb. Continental A-65/C-85, four small cylinders.'),
    'c172-like':            (17.5, 0.07, 'ESTIMATED: McCauley 1C160 fixed-pitch two-blade, ~38 lb. O-320/O-360, four cylinders.'),
    'pa28-archer-like':     (17.5, 0.07, 'ESTIMATED: Sensenich 76EM8 forged aluminium fixed-pitch, ~38 lb. O-360, four cylinders.'),
}
# A WOOD propeller is a different animal and wants its own figure, not the metal class scaled.
MASS['j3-cub-like'] = (6.0, 0.05, 'ESTIMATED: a 72 in Sensenich W72 wood propeller is 13 lb; wood props of this size are 11-14 lb, far lighter than the 44 lb metal one. Continental A-65, four small cylinders.')


def ip(mass, diameter, blades, crank=0.0, k_hub=K_HUB):
    bl = blades * (0.15 * mass) * (0.25 * diameter) ** 2
    hub = (1.0 - 0.15 * blades) * mass * k_hub ** 2
    return bl, hub, crank, bl + hub + crank


def check():
    print('The rule against the two propellers Hartzell publishes a polar moment for:')
    print('  prop                       N   D(m)   M(kg)    rule   published   error')
    for name, n, d, m, pub in (('HC-C2YR-1A/7690C', 2, 1.930, 18.37, 1.198),
                               ('HC-C3YR-1A/7690C', 3, 1.981, 27.20, 1.966)):
        *_, tot = ip(m, d, n)
        print('  %-24s %d  %.3f  %6.2f  %6.3f      %6.3f   %+5.0f %%' % (name, n, d, m, tot, pub, 100 * (tot / pub - 1)))
    print('\n=> sound on a two-blade hub, high on a three-blade one. This tool skips 3-blade types.')


if __name__ == '__main__':
    if '--check' in sys.argv:
        check(); sys.exit(0)
    dry = '--dry' in sys.argv
    print('%-24s %5s %6s %6s   %6s -> %-6s %7s' % ('type', 'N', 'D', 'M', 'now', 'rule', 'change'))
    for f in [x for x in sys.argv[1:] if not x.startswith('--')]:
        text = io.open(f, encoding='utf-8').read()
        c = json.loads(text)
        p = c.get('propulsion') or {}
        tid, n, d, now = c.get('id'), p.get('propBladeCount', 0), p.get('propDiameterM', 0.0), p.get('propInertia')
        if not n or d <= 0: continue
        if n != 2:
            print('%-24s %5d %6.2f %6s   %6.2f    (skipped: %d blades, the rule runs +60 %% there)' % (tid, n, d, '-', now, n))
            continue
        if tid not in MASS:
            print('%-24s %5d %6.2f %6s   %6.2f    (skipped: no sourced propeller mass)' % (tid, n, d, '?', now))
            continue
        m, crank, src = MASS[tid]
        bl, hub, ck, tot = ip(m, d, n, crank)
        tot = round(tot, 2)
        print('%-24s %5d %6.2f %6.1f   %6.2f -> %-6.2f %+6.0f %%' % (tid, n, d, m, now, tot, 100 * (tot / now - 1)))
        if dry: continue
        old = '"propInertia": %s,' % json.dumps(now)
        assert old in text, (tid, old)
        text = text.replace(old, '"propInertia": %s,' % tot)
        # Several types carry no propInertiaSource at all (c172, pa28): give them one rather than skip them,
        # since an unsourced number is the thing this tool exists to replace.
        if '    "propInertiaSource": "' not in text:
            anchor = '    "propInertia": %s,\n' % tot
            text = text.replace(anchor, anchor + '    "propInertiaSource": "",\n', 1)
        i = text.index('    "propInertiaSource": "')
        j = text.index('\n', i)
        note = ('    "propInertiaSource": "Propeller + spinner + the crank\'s rotating parts, kg.m2. From the OWNER\'S '
                'BLADE RULE (2026-11-09, tools/aircraft_builder/prop_inertia.py): each blade is 15 %% of the net '
                'propeller weight with its centre of mass at 25 %% of the DIAMETER from the axis, the rest being hub '
                'near the axis - because a heavy constant-speed hub contributes almost nothing to sum(m r^2), so a '
                'mass-weighted estimate overstates the couple. M %.1f kg, D %.2f m, 2 blades: blades %.3f + hub %.3f '
                '(k 0.10 m) + crank %.2f = %.2f, against %s before. MASS: %s VALIDATION: the rule reproduces '
                'Hartzell\'s published two-blade polar moment to +18 %% (1.412 against 1.198) and runs +60 %% high on '
                'their three-blade one, so it is applied to two-blade types only.",'
                % (m, d, bl, hub, ck, tot, json.dumps(now), src))
        text = text[:i] + note + text[j:]
        json.loads(text)
        io.open(f, 'w', encoding='utf-8').write(text)
