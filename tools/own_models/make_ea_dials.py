"""Draws the Extra 300/L instrument faces (our own original artwork, generic markings, no brand names, no registration,
no placard text) into art/ea-*.png. Run once on any Python with Pillow:
    python3 tools/own_models/make_ea_dials.py
Scales and coloured arcs come from ea_dialmaps.py (POH section 2 markings; layouts E). Nothing is traced or sampled."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from ea_dialmaps import DIALS, ang, VS0, VNO, VNE, RPM_G0, RPM_Y, RPM_MAX
ART = os.path.join(HERE, 'art'); S = 512; C = S / 2
WH, GRN, YEL, RED = (238, 238, 232), (40, 170, 60), (232, 190, 30), (220, 40, 40)
from instrument_face_style import font, finish

def pol(th, r): t = math.radians(th); return (C + r * math.sin(t), C - r * math.cos(t))
def base(bg=(18, 18, 20)):
    im = Image.new('RGB', (S, S), (10, 10, 11)); d = ImageDraw.Draw(im)
    d.ellipse((4, 4, S - 4, S - 4), fill=bg, outline=(62, 62, 66), width=6); return im, d
def tick(d, th, r0, r1, w=4, col=WH): d.line([pol(th, r0), pol(th, r1)], fill=col, width=w)
def label(d, th, r, s, sz=34, col=WH):
    f = font(sz); x, y = pol(th, r); bb = d.textbbox((0, 0), s, font=f)
    d.text((x - (bb[2] - bb[0]) / 2, y - (bb[3] - bb[1]) / 2 - bb[1]), s, fill=col, font=f)
def arc(d, th0, th1, r, w, col):
    a0, a1 = sorted((th0, th1)); d.arc((C - r, C - r, C + r, C + r), a0 - 90, a1 - 90, fill=col, width=w)
def save(im, n): finish(im).save(os.path.join(ART, n)); print('wrote', n)
A = lambda k: (lambda v: ang(k, v))
# The airspeed, altimeter, VSI, turn-coordinator and g-meter faces are the SHARED INSTRUMENT LIBRARY'S (tools/own_models/instruments,
# Ty 2026-11-05: one of each, used by every aircraft); this script draws only this aeroplane's other dials.
# tachometer (x100 rpm)
a = A('TACH'); im, d = base()
arc(d, a(RPM_G0), a(RPM_Y), 212, 18, GRN); arc(d, a(RPM_Y), a(RPM_MAX), 212, 18, YEL); tick(d, a(RPM_MAX), 194, 236, 9, RED)
for v in range(0, 3501, 100): tick(d, a(v), 236 - (32 if v % 500 == 0 else 16), 236, 5 if v % 500 == 0 else 3)
for v in range(0, 3501, 500): label(d, a(v), 162, str(v // 100), 38)
label(d, 180, 80, 'RPM x100', 22); save(im, 'ea-dial-tach.png')
# manifold pressure
a = A('MP'); im, d = base()
arc(d, a(10), a(25), 212, 18, GRN); arc(d, a(25), a(29.5), 212, 18, YEL); tick(d, a(29.5), 194, 236, 9, RED)
for v in range(10, 36): tick(d, a(v), 236 - (32 if v % 5 == 0 else 16), 236, 5 if v % 5 == 0 else 3)
for v in range(10, 36, 5): label(d, a(v), 164, str(v), 40)
label(d, 180, 80, 'MAN PRESS', 20); label(d, 180, 112, 'IN HG', 20); save(im, 'ea-dial-mp.png')
# dual engine gauge: oil pressure (upper) + oil temperature (lower)
ap, at = A('OILP'), A('OILT'); im, d = base()
arc(d, ap(55), ap(95), 212, 20, GRN); tick(d, ap(25), 190, 236, 9, RED); tick(d, ap(115), 190, 236, 9, RED)
for v in range(0, 121, 10): tick(d, ap(v), 236 - (30 if v % 50 == 0 else 16), 236, 6 if v % 50 == 0 else 4)
for v in (0, 50, 100): label(d, ap(v), 160, str(v), 40)
arc(d, at(140), at(210), 212, 20, GRN); tick(d, at(245), 190, 236, 9, RED)
for v in range(100, 251, 10): tick(d, at(v), 236 - (30 if v % 50 == 0 else 16), 236, 6 if v % 50 == 0 else 4)
for v in (100, 150, 200, 250): label(d, at(v), 160, str(v), 36)
d.line([pol(-90, 236), pol(90, 236)], fill=(70, 70, 74), width=4)
label(d, 0, 72, 'OIL PSI', 26); label(d, 180, 72, 'OIL F', 26); save(im, 'ea-dial-oil.png')
# CHT
a = A('CHT'); im, d = base()
arc(d, a(200), a(435), 212, 20, GRN); arc(d, a(435), a(500), 212, 20, YEL); tick(d, a(500), 190, 236, 10, RED)
for v in range(200, 501, 25): tick(d, a(v), 236 - (32 if v % 100 == 0 else 16), 236, 6 if v % 100 == 0 else 4)
for v in (200, 300, 400, 500): label(d, a(v), 160, str(v), 40)
label(d, 180, 88, 'CYL F', 30); save(im, 'ea-dial-cht.png')
# fuel quantity
a = A('FUEL'); im, d = base()
tick(d, a(0), 190, 236, 9, RED)
for k in range(0, 9): tick(d, a(k / 8.0), 236 - (32 if k % 2 == 0 else 16), 236, 6 if k % 2 == 0 else 4)
for v, s_ in ((0, 'E'), (0.25, '1/4'), (0.5, '1/2'), (0.75, '3/4'), (1, 'F')): label(d, a(v), 160, s_, 38 if len(s_) == 1 else 30)
label(d, 180, 80, 'FUEL', 30); save(im, 'ea-dial-fuel.png')
# ignition switch face (key is a mesh)
a = A('MAG'); im, d = base((30, 30, 32))
for v, s_ in zip(range(5), ('OFF', 'R', 'L', 'BOTH', 'START')):
    tick(d, a(v), 200, 236, 6); label(d, a(v), 160, s_, 34 if len(s_) < 4 else 28)
d.ellipse((C - 70, C - 70, C + 70, C + 70), fill=(150, 152, 156), outline=(90, 90, 92), width=6)
save(im, 'ea-dial-mag.png')
# fuel selector plate (handle is a mesh): OFF / ACRO+CTR / WING at -90 / 0 / +90 deg (E)
im, d = base((30, 30, 32))
for th, s_ in ((-90, 'OFF'), (0, 'ACRO'), (90, 'WING')):
    tick(d, th, 196, 236, 8, RED if s_ == 'OFF' else WH); label(d, th, 150, s_, 40)
save(im, 'ea-dial-fuelsel.png')
# compass card band (reverse-sensing card as seen from aft: higher headings to the LEFT)
W_, H_ = 1024, 96; im = Image.new('RGB', (W_, H_), (24, 24, 26)); d = ImageDraw.Draw(im); f = font(34)
names = {0: 'N', 90: 'E', 180: 'S', 270: 'W'}
for h in range(0, 360, 5):
    for x in ((W_ * (1 - h / 360),) if h else (0, W_)):
        d.line([(x, 0), (x, 22 if h % 10 == 0 else 12)], fill=WH, width=3 if h % 30 == 0 else 2)
        if h % 30 == 0:
            s_ = names.get(h, str(h // 10)); bb = d.textbbox((0, 0), s_, font=f); d.text((x - (bb[2] - bb[0]) / 2, 34), s_, fill=WH, font=f)
im.save(os.path.join(ART, 'ea-compass-band.png')); print('wrote ea-compass-band.png')
# compact panel-mount avionics faces (Ty, 2026-10-30): plain faces, no maker text, no legends. 10 px per mm over the
# real 63 x 44 mm control-head face (TY91/TY96-style radio head, TT21/TT22-style transponder head: Trig datasheets);
# the layouts (display window, knob seats, button row) are our estimates (E). The LCD itself is a separate mesh node.
def avx_face(lcd, seats, buttons, name):
    im = Image.new('RGB', (630, 440), (34, 34, 38)); d = ImageDraw.Draw(im)
    d.rectangle((3, 3, 626, 436), outline=(70, 70, 76), width=6)
    d.rectangle(lcd, fill=(10, 12, 12), outline=(84, 84, 90), width=5)
    for cx, cy, r in seats: d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(22, 22, 24), outline=(84, 84, 90), width=4)
    for x0, y0, x1, y1 in buttons: d.rounded_rectangle((x0, y0, x1, y1), 8, fill=(64, 64, 70), outline=(96, 96, 102), width=3)
    save(im, name)
AVX = {'com': dict(lcd=(150, 50, 480, 250), seats=[(75, 200, 50), (555, 200, 72)], buttons=[(165 + 82 * i, 300, 225 + 82 * i, 370) for i in range(4)]),
       'xpdr': dict(lcd=(40, 50, 440, 220), seats=[(540, 200, 62)], buttons=[(40 + 68 * i, 285, 92 + 68 * i, 355) for i in range(6)])}
for k, v in AVX.items(): avx_face(v['lcd'], v['seats'], v['buttons'], 'ea-avx-%s.png' % k)
