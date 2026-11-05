"""The shared instruments: geometry, faces and the needle maps, one function per instrument.

Every map here is copied by hand into FlyingGame.Core.Instruments.InstrumentLibrary (C#), and InstrumentLibraryTests
holds both to the same literals; build.py also writes them to docs/models/instrument-library.json for reading.
Map values are DEGREES CLOCKWISE AS THE PILOT SEES THE DIAL (rot) or FACE RADII along the face's up (slide).
"""
import math

from PIL import Image, ImageDraw

from . import faces as F
from .geometry import Mesh, Writer

# ---- shared paint and parts -------------------------------------------------------------------------------------

BEZEL_RGB = (0.045, 0.045, 0.05)
NEEDLE_RGB = (0.93, 0.93, 0.90)
HUB_RGB = (0.03, 0.03, 0.03)
ORANGE_RGB = (1.0, 0.45, 0.05)

BEZEL_OUTER = 1.10      # bezel ring outer radius, face radii (a 3-1/8 in case: 79.4 mm face in an 87 mm ring)
BEZEL_FRONT = 0.20      # bezel lip, face radii proud of the face: every needle sits below it
CASE_BACK = -0.30       # the case's side wall runs back into the panel this far


def materials(w):
    return dict(bezel=w.material('Library_Bezel', BEZEL_RGB, rough=0.45),
                needle=w.material('Needle_Library_White', NEEDLE_RGB, rough=0.5),
                hub=w.material('Needle_Library_Hub', HUB_RGB, rough=0.4),
                orange=w.material('Needle_Library_Orange', ORANGE_RGB, rough=0.5))


def bezel(w, root, prefix, mats):
    m = Mesh()
    m.annulus(1.0, BEZEL_OUTER, BEZEL_FRONT)
    m.wall(1.0, 0.0, BEZEL_FRONT, outward=False)
    m.wall(BEZEL_OUTER, CASE_BACK, BEZEL_FRONT)
    w.node(prefix + '_Bezel', root, m, mats['bezel'])


def face(w, root, prefix, image, name=None):
    m = Mesh().disc(1.0, 0.0, uv_r=1.0)
    w.node(prefix + '_Face', root, m, w.material(name or ('Dial_Library_' + prefix), (1, 1, 1), png=image, rough=0.7))


def pointer(length, root_w, tip_w, tail=0.18, z0=0.10, z1=0.115):
    """A pointer at 12 o'clock: a tapered shaft from the tail to `length`, a short point beyond. Face radii."""
    m = Mesh()
    m.plate([(-root_w / 2, -tail), (root_w / 2, -tail), (tip_w / 2, length - 0.06), (-tip_w / 2, length - 0.06)], z0, z1)
    m.plate([(-tip_w / 2, length - 0.06), (tip_w / 2, length - 0.06), (0.0, length)], z0, z1)
    return m


def hub(r=0.075, z=0.13):
    return Mesh().disc(r, z, n=32).wall(r, z - 0.03, z, n=32)


# ---- turn coordinator -------------------------------------------------------------------------------------------
# The one generic TC (Ty 2026-11-05 06:53). Wings: the thin tapered wings of lane/codex-turn-coord f2e544ee (Ty
# 05:55): each wing tapers from the root to a tip exactly as wide as the standard-rate index painted on the face.

TC_INDEX_W = 0.040          # standard-rate index width, face radii (f2e544ee: 8 px + strokes on a 0.492*512 px face)
TC_INDEX_R = (0.66, 0.86)   # the indices' radial extent
TC_RATE_DEG = 20.0          # standard rate (3 deg/s) = the wings on the 20 deg index
TC_WING_TIP = 0.62          # wing span from the centre (f2e544ee: 0.62 rf)
TC_WING_ROOT_W = 0.080      # wing chord at the root (f2e544ee GL: 2.0 mm on 25.74 mm)
TC_BALL_RHO, TC_BALL_R = 0.57, 0.062   # ball on an arc about the dial centre; ball radius
TC_TUBE_HALF_DEG = 26.0
TC_BALL_SWING = 18.0        # ball travel at slip_ball = +-1 (the GL / Extra rigs' 18 deg)

TC_PARTS = {
    'TC_Airplane': dict(param='turn_rate_dps', kind='rot', map=[[-6, -40], [0, 0], [6, 40]]),
    # +1 = ball to the pilot's RIGHT: a ball at 6 o'clock moving right goes ANTI-clockwise.
    'TC_Ball': dict(param='slip_ball', kind='rot', map=[[-1, TC_BALL_SWING], [0, 0], [1, -TC_BALL_SWING]]),
}


def tc_face():
    im, d = F.base()
    for th in (90.0, 270.0):                                     # wings-level indices
        F.bar(d, th, *TC_INDEX_R, F.px(TC_INDEX_W))
    for th in (90.0 + TC_RATE_DEG, 270.0 - TC_RATE_DEG):         # standard-rate indices
        F.bar(d, th, *TC_INDEX_R, F.px(TC_INDEX_W))
    F.text_at(d, -0.74, -0.47, 'L', 84); F.text_at(d, 0.74, -0.47, 'R', 84)
    F.text_at(d, 0.0, 0.56, 'TURN COORDINATOR', 56)
    F.text_at(d, 0.0, 0.40, 'NO PITCH INFORMATION', 34)
    F.text_at(d, 0.0, -0.84, '2 MIN', 50)
    F.text_at(d, -0.42, -0.84, 'D.C.', 30); F.text_at(d, 0.42, -0.84, 'ELEC', 30)
    # the inclinometer tube: a light band on an arc about the centre, the ball's track
    R0, R1 = F.px(TC_BALL_RHO - 0.075), F.px(TC_BALL_RHO + 0.075)
    a0, a1 = 180 - TC_TUBE_HALF_DEG, 180 + TC_TUBE_HALF_DEG
    d.pieslice((F.C - R1, F.C - R1, F.C + R1, F.C + R1), a0 - 90, a1 - 90, fill=(214, 214, 206))
    d.pieslice((F.C - R0, F.C - R0, F.C + R0, F.C + R0), a0 - 90, a1 - 90, fill=F.FACE)
    d.arc((F.C - R1, F.C - R1, F.C + R1, F.C + R1), a0 - 90, a1 - 90, fill=(60, 60, 64), width=5)
    return F.done(im)


def tc_airplane():
    """Rear view of a small aeroplane at 12 o'clock-up: tapered wings, round fuselage, fin."""
    m = Mesh(); z0, z1 = 0.135, 0.15
    hr, ht = TC_WING_ROOT_W / 2, TC_INDEX_W / 2
    m.plate([(0.05, -hr), (TC_WING_TIP, -ht), (TC_WING_TIP, ht), (0.05, hr)], z0, z1)
    m.plate([(-0.05, hr), (-TC_WING_TIP, ht), (-TC_WING_TIP, -ht), (-0.05, -hr)], z0, z1)
    circ = [(0.085 * math.cos(a), 0.085 * math.sin(a)) for a in [i * math.tau / 24 for i in range(24)]]
    m.plate(circ, z0, z1 + 0.004)
    m.plate([(-0.022, 0.0), (0.022, 0.0), (0.014, 0.23), (-0.014, 0.23)], z0, z1)
    m.plate([(-0.09, 0.20), (0.09, 0.20), (0.09, 0.235), (-0.09, 0.235)], z0, z1)   # the tailplane, seen end on
    return m


def turn_coordinator():
    w = Writer(); mats = materials(w)
    root = w.node('TurnCoordinator', extras=dict(library='turn_coordinator', parts=TC_PARTS))
    bezel(w, root, 'TC', mats)
    face(w, root, 'TC', tc_face())
    ball = Mesh().sphere((0.0, -TC_BALL_RHO, 0.07), TC_BALL_R)
    w.node('TC_Ball', root, ball, w.material('Needle_Library_Ball', (0.02, 0.02, 0.02), rough=0.25))
    wires = Mesh()
    for s in (-1, 1):
        x = s * (TC_BALL_R + 0.012)
        wires.plate([(x - 0.008, -TC_BALL_RHO - 0.085), (x + 0.008, -TC_BALL_RHO - 0.085), (x + 0.008, -TC_BALL_RHO + 0.085), (x - 0.008, -TC_BALL_RHO + 0.085)], 0.125, 0.13)
    w.node('TC_Wires', root, wires, mats['hub'])
    w.node('TC_Airplane', root, tc_airplane(), mats['needle'])
    return w


# ---- airspeed indicator -----------------------------------------------------------------------------------------
# One law for every generic ASI: 0 at 12 o'clock, the scale floor at 30 deg, the scale top at 330 deg, linear between
# (the Great Lakes law, Ty-approved panel). An aeroplane gives only its units, floor, top, arcs and red line
# (configs/aircraft/<id>.json "instruments.airspeed").

def asi_law(lo, hi):
    return [[0, 0], [lo, 30], [hi, 330]]


def asi_angle(v, lo, hi):
    if v <= lo: return 30.0 * max(v, 0.0) / lo
    return 30.0 + 300.0 * (min(v, hi) - lo) / (hi - lo)


def asi_param(units):
    return {'mph': 'airspeed_mph', 'kt': 'airspeed_kias'}[units]


def asi_face(spec):
    lo, hi = spec['scaleMin'], spec['scaleMax']
    a = lambda v: asi_angle(v, lo, hi)
    im, d = F.base()
    arcs = spec.get('arcs', {})
    if 'white' in arcs: F.arc(d, a(arcs['white'][0]), a(arcs['white'][1]), 0.80, 22, F.ARC_WHITE)
    if 'green' in arcs: F.arc(d, a(arcs['green'][0]), a(arcs['green'][1]), 0.865, 30, F.GREEN)
    if 'yellow' in arcs: F.arc(d, a(arcs['yellow'][0]), a(arcs['yellow'][1]), 0.865, 30, F.YELLOW)
    if 'redline' in spec: F.bar(d, a(spec['redline']), 0.80, 0.94, 16, F.RED)
    minor = spec.get('minorTick', 5 if hi - lo <= 200 else 10)
    major = spec.get('majorTick', 10 if hi - lo <= 200 else 20)
    lab = spec.get('labelEvery', 20 if hi - lo <= 200 else 40)
    v = lo
    while v <= hi + 1e-6:
        big = abs(v / major - round(v / major)) < 1e-6
        F.tick(d, a(v), 0.94 - (0.12 if big else 0.06), 0.94, 10 if big else 6)
        if abs(v / lab - round(v / lab)) < 1e-6: F.label(d, a(v), 0.64, '%g' % v, 74)
        v += minor
    F.text_at(d, 0.0, -0.30, {'mph': 'MPH', 'kt': 'KNOTS'}[spec['units']], 58)
    F.text_at(d, 0.0, 0.34, 'AIRSPEED', 44)
    return F.done(im)


GENERIC_ASI = dict(units='kt', scaleMin=40, scaleMax=200)


def airspeed():
    w = Writer(); mats = materials(w)
    parts = {'ASI_Needle': dict(param='airspeed_kias', kind='rot', map=asi_law(40, 200), note='per-aircraft: units/floor/top')}
    root = w.node('Airspeed', extras=dict(library='airspeed', parts=parts))
    bezel(w, root, 'ASI', mats)
    face(w, root, 'ASI', asi_face(GENERIC_ASI), name='Dial_Library_ASI')
    w.node('ASI_Needle', root, pointer(0.86, 0.06, 0.03), mats['needle'])
    w.node('ASI_Hub', root, hub(), mats['hub'])
    return w


# ---- altimeter ------------------------------------------------------------------------------------------------------

ALT_PARTS = {
    'ALT_Hand10000': dict(param='altitude_ft', kind='rot', map=[[0, 0], [100000, 360]], unbounded=True),
    'ALT_Hand1000': dict(param='altitude_ft', kind='rot', map=[[0, 0], [10000, 360]], unbounded=True),
    'ALT_Hand100': dict(param='altitude_ft', kind='rot', map=[[0, 0], [1000, 360]], unbounded=True),
}


def alt_face():
    im, d = F.base()
    for i in range(50): F.tick(d, i * 7.2, 0.94 - (0.13 if i % 5 == 0 else 0.065), 0.94, 10 if i % 5 == 0 else 6)
    for i in range(10): F.label(d, i * 36, 0.66, str(i), 92)
    F.text_at(d, 0.0, 0.34, 'ALT', 50)
    F.text_at(d, 0.0, -0.36, '100 FEET', 40)
    # the Kollsman window at 3 o'clock, where CockpitTaps draws the setting (x 0.35..0.77, y +-0.08)
    d.rectangle((F.C + F.px(0.35), F.C - F.px(0.085), F.C + F.px(0.77), F.C + F.px(0.085)), fill=(0, 0, 0), outline=(120, 120, 120), width=4)
    return F.done(im)


def altimeter():
    w = Writer(); mats = materials(w)
    root = w.node('Altimeter', extras=dict(library='altimeter', parts=ALT_PARTS))
    bezel(w, root, 'ALT', mats)
    face(w, root, 'ALT', alt_face())
    # 10,000 ft: a thin pointer to the rim with a broad triangle at its end (the "low altitude" index pointer)
    # Every hand rides above 0.11 face radii: a panel's own Kollsman window quad sits up to 3.8 mm proud (the Great Lakes').
    m = Mesh(); m.plate([(-0.012, 0.0), (0.012, 0.0), (0.012, 0.82), (-0.012, 0.82)], 0.115, 0.125)
    m.plate([(-0.05, 0.82), (0.05, 0.82), (0.0, 0.97)], 0.115, 0.125)
    w.node('ALT_Hand10000', root, m, mats['needle'])
    m = Mesh(); m.plate([(-0.05, -0.12), (0.05, -0.12), (0.05, 0.40), (-0.05, 0.40)], 0.135, 0.145)
    m.plate([(-0.05, 0.40), (0.05, 0.40), (0.0, 0.56)], 0.135, 0.145)
    w.node('ALT_Hand1000', root, m, mats['needle'])
    w.node('ALT_Hand100', root, pointer(0.88, 0.05, 0.026, z0=0.155, z1=0.17), mats['needle'])
    w.node('ALT_Hub', root, hub(z=0.185), mats['hub'])
    return w


# ---- vertical speed indicator --------------------------------------------------------------------------------------
# Zero at 9 o'clock; 0.09 deg/fpm to 1,000 (at 12 / 6 o'clock), 0.08 deg/fpm beyond, 2,000 at 80 / 100 deg.

def vsi_angle(fpm):
    v = min(abs(fpm), 2000.0)
    a = v * 0.09 if v <= 1000 else 90.0 + (v - 1000.0) * 0.08
    return -90.0 + math.copysign(a, fpm)


VSI_PARTS = {'VSI_Needle': dict(param='vsi_fpm', kind='rot',
                                map=[[-2000, -260], [-1000, -180], [0, -90], [1000, 0], [2000, 80]])}


def vsi_face():
    im, d = F.base()
    for fpm in range(-2000, 2001, 100):
        big = fpm % 500 == 0
        F.tick(d, vsi_angle(fpm), 0.94 - (0.13 if big else 0.065), 0.94, 10 if big else 6)
    for fpm in (-1500, -1000, -500, 0, 500, 1000, 1500):
        F.label(d, vsi_angle(fpm), 0.66, str(abs(fpm) // 100), 80)
    F.label(d, 90.0, 0.66, '20', 80)                  # +-2,000 share the 3 o'clock label (80 / 100 deg)
    F.text_at(d, 0.30, 0.30, 'UP', 44); F.text_at(d, 0.30, -0.30, 'DOWN', 44)
    F.text_at(d, 0.0, 0.44, 'VERTICAL SPEED', 34); F.text_at(d, 0.0, -0.44, '100 FT PER MIN', 30)
    return F.done(im)


def vertical_speed():
    w = Writer(); mats = materials(w)
    root = w.node('VerticalSpeed', extras=dict(library='vertical_speed', parts=VSI_PARTS))
    bezel(w, root, 'VSI', mats)
    face(w, root, 'VSI', vsi_face())
    w.node('VSI_Needle', root, pointer(0.88, 0.06, 0.03), mats['needle'])
    w.node('VSI_Hub', root, hub(), mats['hub'])
    return w


# ---- attitude indicator ----------------------------------------------------------------------------------------------

AI_APERTURE = 0.74
AI_PITCH_PER_DEG = 0.015    # face radii of card travel per degree of pitch
AI_PITCH_LIMIT = 20.0
AI_CARD_R = AI_APERTURE + AI_PITCH_PER_DEG * AI_PITCH_LIMIT + 0.02
AI_PARTS = {
    # the roll assembly turns OPPOSITE the bank (right bank: horizon tilts anticlockwise)
    'AI_Roll': dict(param='bank_deg', kind='rot', map=[[-180, 180], [180, -180]]),
    # pitch up: the horizon goes DOWN
    'AI_Pitch': dict(param='pitch_deg', kind='slide',
                     map=[[-AI_PITCH_LIMIT, AI_PITCH_PER_DEG * AI_PITCH_LIMIT], [AI_PITCH_LIMIT, -AI_PITCH_PER_DEG * AI_PITCH_LIMIT]]),
}


def ai_card():
    S = F.S; im = Image.new('RGB', (S, S), (10, 10, 11)); d = ImageDraw.Draw(im)
    k = F.C / AI_CARD_R                                      # px per face radius on this texture
    d.rectangle((0, 0, S, F.C), fill=(38, 110, 196))         # sky
    d.rectangle((0, F.C, S, S), fill=(120, 74, 38))          # ground
    d.rectangle((0, F.C - 4, S, F.C + 4), fill=F.WHITE)      # horizon
    for deg in (5, 10, 15, 20):
        y = deg * AI_PITCH_PER_DEG * k; half = (0.16 if deg % 10 == 0 else 0.08) * k
        for s in (-1, 1):
            d.rectangle((F.C - half, F.C - s * y - 3, F.C + half, F.C - s * y + 3), fill=F.WHITE)
            if deg % 10 == 0:
                for x in (-1, 1):
                    F_ = F.font(40); txt = str(deg); bb = d.textbbox((0, 0), txt, font=F_)
                    cx = F.C + x * (half + 0.08 * k)
                    d.text((cx - (bb[2] - bb[0]) / 2 - bb[0], F.C - s * y - (bb[3] - bb[1]) / 2 - bb[1]), txt, fill=F.WHITE, font=F_)
    return F.done(im)


def ai_mask_face():
    im, d = F.base()
    for deg in (-90, -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60, 90):
        big = abs(deg) in (0, 30, 60, 90)
        if deg == 0: continue
        if abs(deg) == 45: d.polygon([F.pol(deg, 0.80), F.pol(deg - 3, 0.90), F.pol(deg + 3, 0.90)], fill=F.WHITE); continue
        F.tick(d, deg, 0.80, 0.95 if big else 0.90, 14 if big else 9)
    d.polygon([F.pol(0, 0.80), F.pol(-5, 0.95), F.pol(5, 0.95)], fill=F.WHITE)      # the zero-bank index
    return F.done(im)


def attitude():
    w = Writer(); mats = materials(w)
    root = w.node('Attitude', extras=dict(library='attitude', parts=AI_PARTS))
    bezel(w, root, 'AI', mats)
    face(w, root, 'AI', Image.new('RGB', (64, 64), F.FACE))   # the dark case floor behind the card
    roll = w.node('AI_Roll', root)
    card = Mesh().disc(AI_CARD_R, 0.02, n=96, uv_r=AI_CARD_R)
    pitch = w.node('AI_Pitch', roll)
    w.node('AI_PitchCard', pitch, card, w.material('Dial_Library_AI_Card', (1, 1, 1), png=ai_card(), rough=0.7))
    sky = Mesh().plate([(0.0, AI_APERTURE + 0.005), (-0.05, AI_APERTURE + 0.10), (0.05, AI_APERTURE + 0.10)], 0.085, 0.095)
    w.node('AI_SkyPointer', roll, sky, mats['orange'])
    mask = Mesh().annulus(AI_APERTURE, 1.0, 0.06, uv_r=1.0).wall(AI_APERTURE, 0.0, 0.06, outward=False)
    w.node('AI_Mask', root, mask, w.material('Dial_Library_AI_Mask', (1, 1, 1), png=ai_mask_face(), rough=0.7))
    fixed = Mesh()
    fixed.plate([(-0.52, -0.022), (-0.13, -0.022), (-0.13, 0.022), (-0.52, 0.022)], 0.11, 0.125)
    fixed.plate([(0.13, -0.022), (0.52, -0.022), (0.52, 0.022), (0.13, 0.022)], 0.11, 0.125)
    fixed.plate([(-0.13, 0.022), (0.0, -0.09), (0.13, 0.022)], 0.11, 0.125)
    circ = [(0.03 * math.cos(a), 0.03 * math.sin(a)) for a in [i * math.tau / 16 for i in range(16)]]
    fixed.plate(circ, 0.11, 0.13)
    w.node('AI_Airplane', root, fixed, mats['orange'])
    return w


# ---- heading indicator (directional gyro) ----------------------------------------------------------------------------

DG_CARD_R = 0.86
DG_PARTS = {'DG_Card': dict(param='heading_deg', kind='rot', map=[[0, 0], [360, -360]], wrap=360)}


def dg_card():
    S = F.S; im = Image.new('RGB', (S, S), (10, 10, 11)); d = ImageDraw.Draw(im)
    k = 1.0 / DG_CARD_R                                       # this texture's radius is the card's
    d.ellipse((0, 0, S - 1, S - 1), fill=F.FACE)
    for deg in range(0, 360, 5):
        big = deg % 10 == 0
        F.tick(d, deg, (0.98 - (0.13 if big else 0.07)), 0.98, 10 if big else 6)
    for i, t in enumerate(['N', '3', '6', 'E', '12', '15', 'S', '21', '24', 'W', '30', '33']):
        F.label(d, i * 30, 0.70, t, 86 if t in 'NESW' else 70)
    return F.done(im)


def dg_ring():
    im, d = F.base()
    for deg in range(0, 360, 45):
        if deg == 0: continue
        d.polygon([F.pol(deg, DG_CARD_R + 0.01), F.pol(deg - 2.5, 0.97), F.pol(deg + 2.5, 0.97)], fill=F.WHITE)
    return F.done(im)


def heading():
    w = Writer(); mats = materials(w)
    root = w.node('Heading', extras=dict(library='heading', parts=DG_PARTS))
    bezel(w, root, 'DG', mats)
    face(w, root, 'DG', dg_ring())
    card = Mesh().disc(DG_CARD_R, 0.02, n=96, uv_r=DG_CARD_R)
    w.node('DG_Card', root, card, w.material('Dial_Library_DG_Card', (1, 1, 1), png=dg_card(), rough=0.7))
    lub = Mesh().plate([(0.0, DG_CARD_R - 0.04), (-0.04, 0.99), (0.04, 0.99)], 0.06, 0.07)
    w.node('DG_Lubber', root, lub, mats['orange'])
    ac = Mesh()
    ac.plate([(-0.025, -0.30), (0.025, -0.30), (0.025, 0.28), (-0.025, 0.28)], 0.08, 0.095)
    ac.plate([(-0.30, -0.02), (0.30, -0.02), (0.30, 0.04), (-0.30, 0.04)], 0.08, 0.095)
    ac.plate([(-0.11, -0.30), (0.11, -0.30), (0.11, -0.26), (-0.11, -0.26)], 0.08, 0.095)
    ac.plate([(-0.025, 0.28), (0.025, 0.28), (0.0, 0.34)], 0.08, 0.095)
    w.node('DG_Airplane', root, ac, mats['orange'])
    return w



# ---- accelerometer (g-meter) -----------------------------------------------------------------------------------------
# Ty 2026-11-05 07:26: "G meter should be standardized too". One law: 0 g at 9 o'clock, 12 deg per g, -10..+10 (+10 at
# 1 o'clock, -10 at 5 o'clock; the Extra's and the Pitts' face since 2026-11-02). The aeroplane gives only its red lines,
# which are its structural limits (configs/aircraft/<id>.json limits.gMax / gMin). Max and min followers ride with the
# main needle and stay until reset (Core GMeterFollowers; the reset knob is the panel's, or CockpitTaps').

GM_LAW = [[-10, -210], [0, -90], [10, 30]]
GM_PARTS = {
    'GM_Min': dict(param='g_min_seen', kind='rot', map=GM_LAW),
    'GM_Max': dict(param='g_max_seen', kind='rot', map=GM_LAW),
    'GM_Needle': dict(param='g_load', kind='rot', map=GM_LAW),
}


def gm_angle(g):
    return -90.0 + 12.0 * max(-10.0, min(10.0, g))


def gm_face(g_max=None, g_min=None):
    im, d = F.base()
    for g in range(-10, 11):
        big = g % 2 == 0
        F.tick(d, gm_angle(g), 0.94 - (0.13 if big else 0.065), 0.94, 10 if big else 6)
        if big: F.label(d, gm_angle(g), 0.68, ('+%d' % g) if g > 0 else str(g), 70 if abs(g) < 10 else 62)
    if g_max is not None and g_min is not None:
        F.arc(d, gm_angle(g_min), gm_angle(g_max), 0.865, 30, F.GREEN)
        for g in (g_min, g_max): F.bar(d, gm_angle(g), 0.78, 0.95, 16, F.RED)
    F.text_at(d, 0.20, 0.0, 'G', 110)
    F.text_at(d, 0.20, -0.30, 'ACCEL', 36)
    return F.done(im)


def g_meter():
    w = Writer(); mats = materials(w)
    root = w.node('GMeter', extras=dict(library='g_meter', parts=GM_PARTS))
    bezel(w, root, 'GM', mats)
    face(w, root, 'GM', gm_face(), name='Dial_Library_GM')
    tell = w.material('Needle_Library_TellTale', (0.98, 0.55, 0.12), rough=0.5)
    w.node('GM_Min', root, pointer(0.80, 0.03, 0.02, tail=0.0, z0=0.07, z1=0.08), tell)
    w.node('GM_Max', root, pointer(0.80, 0.03, 0.02, tail=0.0, z0=0.085, z1=0.095), tell)
    w.node('GM_Needle', root, pointer(0.86, 0.06, 0.03), mats['needle'])
    w.node('GM_Hub', root, hub(), mats['hub'])
    return w

INSTRUMENTS = {
    'turn-coordinator': turn_coordinator,
    'airspeed': airspeed,
    'altimeter': altimeter,
    'vertical-speed': vertical_speed,
    'attitude': attitude,
    'heading': heading,
    'g-meter': g_meter,
}

PARTS = {
    'turn-coordinator': TC_PARTS,
    'altimeter': ALT_PARTS,
    'vertical-speed': VSI_PARTS,
    'attitude': AI_PARTS,
    'heading': DG_PARTS,
    'g-meter': GM_PARTS,
}
