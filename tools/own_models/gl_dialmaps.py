"""Great Lakes 2T-1A-2 cockpit dial maps, shared by make_gl_dials.py (the artwork) and gl_cockpit.py / the rig
extras (the needles), so the drawn scales and the needle maps can never drift apart.

Angles are degrees CLOCKWISE FROM 12 O'CLOCK as seen from the seat. Each needle is modelled pointing at ang0;
the rig map gives the rotation RELATIVE TO THE MODELLED POSE (rig_map_deg), so param -> ang0 + interp(param).
Limits: TCDS A18EA rev 11, Model 2T-1A-2 (IO-360-B1F6 / AEIO-360-B1G6): Vne 153 mph, Va / Vno 120 mph,
2700 rpm. Values marked (E) are our estimates (dial layouts are read off the greatlakes2t.co.uk panel photos by
eye; the green-arc floor, Lycoming oil / CHT bands and the G limits are typical values, not 2T-1A documents)."""
VNE, VNO, VS_E = 153.0, 120.0, 58.0            # mph; VS_E (green-arc floor) is an estimate (E)
RPM_MAX = 2700.0
DIALS = {
    # key: (param, ps, rel_deg, ang0, unit, description, extra)
    'ASI':     ('airspeed_mph', [0, 40, 200], [0, 30, 330], 0.0, 'mph',
                'ASI 40..200 mph over 30..330 deg (E layout), rests at 0 below 40; arcs green %g(E)-%g, yellow %g-%g, red line %g (TCDS)' % (VS_E, VNO, VNO, VNE, VNE), {}),
    'ALT100':  ('altitude_ft', [0, 1000], [0, 360], 0.0, 'ft', 'altimeter long hand, 1 turn per 1000 ft (unbounded)', {'rig_map_extrapolate': 1}),
    'ALT1000': ('altitude_ft', [0, 10000], [0, 360], 0.0, 'ft', 'altimeter short hand, 1 turn per 10000 ft (unbounded)', {'rig_map_extrapolate': 1}),
    'VSI':     ('vsi_fpm', [-2000, 0, 2000], [-170, 0, 170], -90.0, 'ft/min', 'VSI, modelled at 0 (9 o\'clock), +2000 fpm climb = 170 deg clockwise (E layout, photo shows a 0-2000 dial)', {}),
    'TACH':    ('rpm', [0, 3500], [0, 300], -150.0, 'rpm', 'tachometer 0..3500 rpm over 300 deg from 7 o\'clock (E layout); green 2000(E)-2700, red line 2700 (TCDS)', {}),
    'MP':      ('manifold_inhg', [10, 35], [0, 300], -120.0, 'inHg', 'manifold pressure 10..35 inHg, 12 deg per inHg from 8 o\'clock (photo layout, E); pinned at 10 below', {}),
    'OILP':    ('oil_press_psi', [0, 115], [0, 160], -80.0, 'psi', 'dual engine gauge upper scale: oil pressure 0..115 psi over the top half (E); green 60-90, red 25 / 115 (Lycoming typical, E)', {}),
    'OILT':    ('oil_temp_f', [100, 250], [0, 160], 100.0, 'degF', 'dual engine gauge lower scale: oil temp 100..250 F over the bottom half, clockwise (E); red 245 (Lycoming typical, E)', {}),
    'CHT':     ('cht_f', [200, 500], [0, 180], -90.0, 'degF', 'small top-right gauge read as CHT (identity E), 200..500 F, 9 -> 3 o\'clock; red 500 (Lycoming typical, E)', {}),
    'G':       ('g_load', [-4, 0, 10], [-92, 0, 230], 0.0, 'g', 'accelerometer -4..+10 g, 0 g at 12 o\'clock, 23 deg/g (photo layout, E); red +5.4 / -4.0 (TCDS A18EA rev 11 NOTE 6 load factors)', {}),
    'GMAX':    ('g_max_seen', [0, 10], [0, 230], 0.0, 'g', 'accelerometer max tell-tale (follows g_load up, stays; reset knob not rigged)', {}),
    'GMIN':    ('g_min_seen', [-4, 0], [-92, 0], 0.0, 'g', 'accelerometer min tell-tale (follows g_load down, stays)', {}),
    'TC':      ('turn_rate_dps', [-6, 0, 6], [-40, 0, 40], 0.0, 'deg/s', 'turn coordinator airplane symbol, standard rate (3 deg/s) = the 20 deg L/R index (E)', {}),
    'MAG':     ('magneto', [0, 1, 2, 3, 4], [0, 30, 60, 90, 130], -60.0, 'OFF/R/L/BOTH/START', 'ignition key OFF, R, L, BOTH, START (spring back to BOTH); detent angles E', {'rig_detents': [0, 1, 2, 3, 4]}),
}
SLIP_DEG = 18.0                                  # (E) ball swing at slip_ball = +-1 (ball tube radius ~ 50 mm)


def interp(v, xs, ys, extrapolate=False):
    if v <= xs[0]:
        return ys[0] + ((v - xs[0]) * (ys[1] - ys[0]) / (xs[1] - xs[0]) if extrapolate else 0.0)
    if v >= xs[-1]:
        return ys[-1] + ((v - xs[-1]) * (ys[-1] - ys[-2]) / (xs[-1] - xs[-2]) if extrapolate else 0.0)
    for (x0, y0), (x1, y1) in zip(zip(xs, ys), zip(xs[1:], ys[1:])):
        if x0 <= v <= x1:
            return y0 + (v - x0) * (y1 - y0) / (x1 - x0) if x1 > x0 else y1
    return ys[-1]


def ang(key, v):
    """absolute dial angle (deg clockwise from 12) for value v"""
    p, ps, ds, a0, *_ = DIALS[key]
    return a0 + interp(v, ps, ds, extrapolate=bool(DIALS[key][6].get('rig_map_extrapolate')))
