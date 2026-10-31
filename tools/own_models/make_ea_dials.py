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
def font(sz):
    for p in ('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', '/System/Library/Fonts/Helvetica.ttc'):
        if os.path.exists(p): return ImageFont.truetype(p, sz)
    return ImageFont.load_default()
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
def save(im, n): im.save(os.path.join(ART, n)); print('wrote', n)
A = lambda k: (lambda v: ang(k, v))
# airspeed (KIAS)
a = A('ASI'); im, d = base()
arc(d, a(VS0), a(VNO), 212, 18, GRN); arc(d, a(VNO), a(VNE), 212, 18, YEL); tick(d, a(VNE), 194, 236, 9, RED)
for v in range(40, 261, 10): tick(d, a(v), 236 - (30 if v % 20 == 0 else 16), 236, 5 if v % 20 == 0 else 3)
for v in range(40, 261, 40): label(d, a(v), 162, str(v), 36)
label(d, 180, 72, 'KNOTS', 28); label(d, 0, 92, 'AIRSPEED', 22); save(im, 'ea-dial-asi.png')
# altimeter
im, d = base()
for i in range(50): tick(d, i * 7.2, 236 - (32 if i % 5 == 0 else 16), 236, 5 if i % 5 == 0 else 3)
for i in range(10): label(d, i * 36, 166, str(i), 46)
label(d, 180, 82, 'ALT', 28); label(d, 0, 84, '100 FT', 22)
d.rectangle((C + 70, C - 20, C + 142, C + 20), fill=(0, 0, 0), outline=(120, 120, 120), width=2); label(d, 90, 106, '29.9', 24)
save(im, 'ea-dial-alt.png')
# VSI (x1000 fpm)
a = A('VSI'); im, d = base()
for v in range(-3000, 3001, 250): tick(d, a(v), 236 - (32 if v % 1000 == 0 else 16), 236, 5 if v % 1000 == 0 else 3)
for v in (-3000, -2000, -1000, 0, 1000, 2000, 3000): label(d, a(v), 168, str(abs(v) // 1000), 40)
label(d, 20, 84, 'UP', 26); label(d, 160, 84, 'DN', 26); label(d, 90, 70, 'FPM x1000', 20); save(im, 'ea-dial-vsi.png')
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
# accelerometer (g)
a = A('G'); im, d = base()
arc(d, a(-5), a(8), 212, 18, GRN); arc(d, a(8), a(10), 212, 18, YEL); tick(d, a(10), 190, 236, 9, RED); tick(d, a(-10), 190, 236, 9, RED)
for v in range(-10, 11): tick(d, a(v), 236 - (32 if v % 2 == 0 else 18), 236, 6 if v % 2 == 0 else 4)
for v in range(-10, 11, 2): label(d, a(v), 166, ('+' if v > 0 else '') + str(v), 34)
label(d, 90, 70, 'G', 40); save(im, 'ea-dial-g.png')
# turn coordinator (airplane symbol + ball are separate meshes)
im, d = base()
for th in (-90, 90): tick(d, th, 170, 232, 8)
for th in (-90 - 20, 90 + 20): tick(d, th, 170, 232, 8)
label(d, -118, 150, 'L', 36); label(d, 118, 150, 'R', 36)
TCB = (C - 200, C - 240, C + 200, C + 160)
d.arc(TCB, 62, 118, fill=(214, 214, 206), width=44); d.arc(TCB, 62, 118, fill=(60, 60, 64), width=3)
d.line([(C - 26, C + 132), (C - 26, C + 186)], fill=(40, 40, 40), width=5); d.line([(C + 26, C + 132), (C + 26, C + 186)], fill=(40, 40, 40), width=5)
label(d, 0, 110, 'TURN COORD', 22); label(d, 0, 74, '2 MIN', 22); save(im, 'ea-dial-tc.png')
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
