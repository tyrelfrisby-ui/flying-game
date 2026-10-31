"""Extra 300/L cockpit dial maps, shared by make_ea_dials.py (the artwork) and ea_cockpit.py / ea_rig.py (the needles),
so the drawn scales and the needle maps can never drift apart. Same conventions as gl_dialmaps.py.

Angles are degrees CLOCKWISE FROM 12 O'CLOCK as seen from the seat. Each needle is modelled pointing at ang0; the rig
map gives the rotation RELATIVE TO THE MODELLED POSE (rig_map_deg), so param -> ang0 + interp(param).
Markings: EA 300/L POH section 2 (ASI KIAS, RPM, MP, oil, CHT, G). Dial layouts (start angle, degrees per unit) are
our estimates (E); nothing is traced from a photo."""
VS0, VNO, VNE = 60.0, 158.0, 220.0            # KIAS green floor / yellow start / red line (POH)
RPM_G0, RPM_Y, RPM_MAX = 700.0, 2400.0, 2700.0
DIALS = {
    # key: (param, ps, rel_deg, ang0, unit, description, extra)
    'ASI':     ('airspeed_kias', [0, 40, 260], [0, 25, 335], 0.0, 'kt',
                'ASI 40..260 KIAS over 25..335 deg (E layout), rests at 0 below 40; green %g-%g, yellow %g-%g, red %g (POH)' % (VS0, VNO, VNO, VNE, VNE), {}),
    'ALT100':  ('altitude_ft', [0, 1000], [0, 360], 0.0, 'ft', 'altimeter long hand, 1 turn per 1000 ft (unbounded)', {'rig_map_extrapolate': 1}),
    'ALT1000': ('altitude_ft', [0, 10000], [0, 360], 0.0, 'ft', 'altimeter short hand, 1 turn per 10000 ft (unbounded)', {'rig_map_extrapolate': 1}),
    'VSI':     ('vsi_fpm', [-3000, 0, 3000], [-160, 0, 160], -90.0, 'ft/min', 'VSI +-3000 fpm, modelled at 0 (9 o\'clock), climb clockwise (E layout)', {}),
    'TACH':    ('rpm', [0, 3500], [0, 300], -150.0, 'rpm', 'tachometer 0..3500 rpm over 300 deg from 7 o\'clock (E layout); green 700-2400, yellow 2400-2700, red 2700 (POH)', {}),
    'MP':      ('manifold_inhg', [10, 35], [0, 300], -120.0, 'inHg', 'manifold pressure 10..35 inHg, 12 deg per inHg from 8 o\'clock (E layout); green 10-25, yellow 25-29.5, red 29.5 (POH); pinned at 10 below', {}),
    'OILP':    ('oil_press_psi', [0, 120], [0, 160], -80.0, 'psi', 'dual engine gauge upper scale: oil pressure 0..120 psi over the top half (E layout); red 25, green 55-95, red 115 (POH)', {}),
    'OILT':    ('oil_temp_f', [100, 250], [0, 160], 100.0, 'degF', 'dual engine gauge lower scale: oil temp 100..250 F over the bottom half (E layout); green 140-210, red 245 (POH)', {}),
    'CHT':     ('cht_f', [200, 500], [0, 180], -90.0, 'degF', 'CHT 200..500 F, 9 -> 3 o\'clock (E layout); red 500 (POH), green 200-435 (E)', {}),
    'FUEL':    ('fuel_centre_frac', [0, 1], [0, 120], -60.0, '0..1', 'fuel quantity (acro / centre tank, E identity) E..F over 10 -> 2 o\'clock (E)', {}),
    'G':       ('g_load', [-10, 0, 10], [-120, 0, 120], -90.0, 'g', 'accelerometer -10..+10 g, 0 g at 9 o\'clock, 12 deg/g (E layout); green -5..+8, yellow +8..+10, red +-10 (POH, solo)', {}),
    'GMAX':    ('g_max_seen', [0, 10], [0, 120], -90.0, 'g', 'accelerometer max tell-tale (follows g_load up, stays; reset knob not rigged)', {}),
    'GMIN':    ('g_min_seen', [-10, 0], [-120, 0], -90.0, 'g', 'accelerometer min tell-tale (follows g_load down, stays)', {}),
    # compact avionics knobs on the rear panel (Ty, 2026-10-30: Trig-style heads). Detent angles E.
    'COMVOL':  ('com_volume', [0, 1], [0, 270], 0.0, '0..1', 'radio head volume knob (left, on/off + squelch push), 0 = off .. 1 = full over 270 deg (E)', {}),
    'COMOUT':  ('com_freq_mhz', [118, 137], [0, 285], 0.0, 'MHz', 'radio head dual-concentric tuning knob, OUTER (MHz): 15 deg detent per MHz (E; unbounded, wrap 360)', {'rig_map_extrapolate': 1}),
    'COMIN':   ('com_freq_mhz', [118, 119], [0, 600], 0.0, 'MHz', 'radio head dual-concentric tuning knob, INNER (kHz): 15 deg detent per 25 kHz channel = 600 deg per MHz (E; unbounded, wrap 360)', {'rig_map_extrapolate': 1}),
    'XPKNOB':  ('xpdr_knob_clicks', [0, 1], [0, 30], 0.0, 'clicks', 'transponder head rotary knob (squawk / menu entry): 30 deg per click (E; unbounded, wrap 360)', {'rig_map_extrapolate': 1}),
    'TC':      ('turn_rate_dps', [-6, 0, 6], [-40, 0, 40], 0.0, 'deg/s', 'turn coordinator airplane symbol, standard rate (3 deg/s) = the 20 deg L/R index (E)', {}),
    'MAG':     ('magneto', [0, 1, 2, 3, 4], [0, 30, 60, 90, 130], -60.0, 'OFF/R/L/BOTH/START', 'ignition key OFF, R, L, BOTH, START (spring back to BOTH); detent angles E', {'rig_detents': [0, 1, 2, 3, 4]}),
}
SLIP_DEG = 18.0                                  # (E) ball swing at slip_ball = +-1


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
