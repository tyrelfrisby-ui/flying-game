"""Draws the Great Lakes 2T-1A-2 instrument faces (our own original artwork, generic markings, no brand names, no
registration, no placard text) into art/gl-*.png. Run once on any Python with Pillow:
    python3 tools/own_models/make_gl_dials.py
Scales come from gl_dialmaps.py (shared with the needles in gl_cockpit.py). Layouts follow the greatlakes2t.co.uk
2T-1A-2 panel photos by eye; nothing is traced or sampled."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from gl_dialmaps import DIALS, ang, VNE, VNO, VS_E, RPM_MAX
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
# airspeed (mph)
a = A('ASI'); im, d = base()
arc(d, a(VS_E), a(VNO), 212, 18, GRN); arc(d, a(VNO), a(VNE), 212, 18, YEL); tick(d, a(VNE), 194, 236, 9, RED)
for v in range(40, 201, 5): tick(d, a(v), 236 - (30 if v % 10 == 0 else 16), 236, 5 if v % 10 == 0 else 3)
for v in range(40, 201, 20): label(d, a(v), 160, str(v), 36)
label(d, 180, 72, 'MPH', 30); label(d, 0, 92, 'AIRSPEED', 22); save(im, 'gl-dial-asi.png')
# altimeter (100 ft hand; 1000 ft hand), Kollsman window
im, d = base()
for i in range(50): tick(d, i * 7.2, 236 - (32 if i % 5 == 0 else 16), 236, 5 if i % 5 == 0 else 3)
for i in range(10): label(d, i * 36, 166, str(i), 46)
label(d, 180, 82, 'ALT', 28); label(d, 0, 84, '100 FT', 22)
d.rectangle((C + 70, C - 20, C + 142, C + 20), fill=(0, 0, 0), outline=(120, 120, 120), width=2); label(d, 90, 106, '29.9', 24)
save(im, 'gl-dial-alt.png')
# VSI (x100 fpm)
a = A('VSI'); im, d = base()
for v in range(-2000, 2001, 100): tick(d, a(v), 236 - (32 if v % 500 == 0 else 16), 236, 5 if v % 500 == 0 else 3)
for v in (-1500, -1000, -500, 0, 500, 1000, 1500, 2000): label(d, a(v), 168, str(abs(v) // 100), 38)
label(d, 20, 84, 'UP', 26); label(d, 160, 84, 'DN', 26); label(d, 90, 70, 'FPM x100', 20); save(im, 'gl-dial-vsi.png')
# tachometer (x100 rpm)
a = A('TACH'); im, d = base()
arc(d, a(2000), a(RPM_MAX), 212, 18, GRN); tick(d, a(RPM_MAX), 194, 236, 9, RED)
for v in range(0, 3501, 100): tick(d, a(v), 236 - (32 if v % 500 == 0 else 16), 236, 5 if v % 500 == 0 else 3)
for v in range(0, 3501, 500): label(d, a(v), 162, str(v // 100), 38)
label(d, 180, 80, 'RPM x100', 22); save(im, 'gl-dial-tach.png')
# manifold pressure (inHg)
a = A('MP'); im, d = base()
for v in range(10, 36): tick(d, a(v), 236 - (32 if v % 5 == 0 else 16), 236, 5 if v % 5 == 0 else 3)
for v in range(10, 36, 5): label(d, a(v), 164, str(v), 40)
label(d, 180, 80, 'MAN PRESS', 20); label(d, 180, 112, 'IN HG', 20); save(im, 'gl-dial-mp.png')
# dual engine gauge: oil pressure (upper) + oil temperature (lower)
ap, at = A('OILP'), A('OILT'); im, d = base()
arc(d, ap(60), ap(90), 212, 20, GRN); arc(d, ap(25), ap(60), 212, 20, YEL); tick(d, ap(25), 190, 236, 9, RED); tick(d, ap(115), 190, 236, 9, RED)
for v in range(0, 116, 10): tick(d, ap(v), 236 - (30 if v % 50 == 0 else 16), 236, 6 if v % 50 == 0 else 4)
for v in (0, 50, 100): label(d, ap(v), 160, str(v), 40)
arc(d, at(165), at(245), 212, 20, GRN); tick(d, at(245), 190, 236, 9, RED)
for v in range(100, 251, 10): tick(d, at(v), 236 - (30 if v % 50 == 0 else 16), 236, 6 if v % 50 == 0 else 4)
for v in (100, 150, 200, 250): label(d, at(v), 160, str(v), 36)
d.line([pol(-90, 236), pol(90, 236)], fill=(70, 70, 74), width=4)
label(d, 0, 72, 'OIL PSI', 26); label(d, 180, 72, 'OIL F', 26); save(im, 'gl-dial-oil.png')
# CHT (F)
a = A('CHT'); im, d = base()
arc(d, a(200), a(435), 212, 20, GRN); arc(d, a(435), a(500), 212, 20, YEL); tick(d, a(500), 190, 236, 10, RED)
for v in range(200, 501, 25): tick(d, a(v), 236 - (32 if v % 100 == 0 else 16), 236, 6 if v % 100 == 0 else 4)
for v in (200, 300, 400, 500): label(d, a(v), 160, str(v), 40)
label(d, 180, 88, 'CYL F', 30); save(im, 'gl-dial-cht.png')
# accelerometer (g)
a = A('G'); im, d = base()
tick(d, a(5.4), 190, 236, 9, RED); tick(d, a(-4.0), 190, 236, 9, RED)   # TCDS A18EA NOTE 6: +5.40 / -4.00 g
for v in range(-4, 11): tick(d, a(v), 236 - 32, 236, 6)
for k in range(-8, 21): tick(d, a(k / 2.0), 220, 236, 3)
for v in range(-4, 11, 2): label(d, a(v), 166, ('+' if v > 0 else '') + str(v), 38)
label(d, 180, 74, 'G UNITS', 24); save(im, 'gl-dial-g.png')
# turn coordinator: static dial (L / R standard-rate indices, horizon index); airplane symbol + ball are separate meshes
im, d = base()
for th in (-90, 90): tick(d, th, 170, 232, 8)
for th in (-90 - 20, 90 + 20): tick(d, th, 170, 232, 8)
label(d, -118, 150, 'L', 36); label(d, 118, 150, 'R', 36)
TCB = (C - 200, C - 240, C + 200, C + 160)    # ball tube: arc about a centre 40 px ABOVE the dial centre, radius 200 px
d.arc(TCB, 62, 118, fill=(214, 214, 206), width=44); d.arc(TCB, 62, 118, fill=(60, 60, 64), width=3)
d.line([(C - 26, C + 132), (C - 26, C + 186)], fill=(40, 40, 40), width=5); d.line([(C + 26, C + 132), (C + 26, C + 186)], fill=(40, 40, 40), width=5)
label(d, 0, 110, 'TURN COORD', 22); label(d, 0, 74, '2 MIN', 22); save(im, 'gl-dial-tc.png')
# Hobbs hour meter (static)
im, d = base((26, 26, 28))
d.rectangle((C - 150, C - 40, C + 150, C + 40), fill=(8, 8, 8), outline=(120, 120, 120), width=3)
f = font(64)
for i, ch in enumerate('01234'):
    x = C - 140 + i * 58; d.rectangle((x, C - 34, x + 52, C + 34), fill=(236, 236, 230) if i == 4 else (12, 12, 12))
    d.text((x + 9, C - 36), ch, fill=(10, 10, 10) if i == 4 else WH, font=f)
label(d, 180, 120, 'HOURS', 34); save(im, 'gl-dial-hobbs.png')
# ignition switch face (key is a mesh)
a = A('MAG'); im, d = base((30, 30, 32))
for v, s_ in zip(range(5), ('OFF', 'R', 'L', 'BOTH', 'START')):
    tick(d, a(v), 200, 236, 6); label(d, a(v), 160, s_, 34 if len(s_) < 4 else 28)
d.ellipse((C - 70, C - 70, C + 70, C + 70), fill=(150, 152, 156), outline=(90, 90, 92), width=6)
save(im, 'gl-dial-mag.png')
# compass card band (wet compass, reverse-sensing card as seen from aft: higher headings to the LEFT)
W_, H_ = 1024, 96; im = Image.new('RGB', (W_, H_), (24, 24, 26)); d = ImageDraw.Draw(im); f = font(34)
names = {0: 'N', 90: 'E', 180: 'S', 270: 'W'}
for h in range(0, 360, 5):
    for x in ((W_ * (1 - h / 360),) if h else (0, W_)):
        d.line([(x, 0), (x, 22 if h % 10 == 0 else 12)], fill=WH, width=3 if h % 30 == 0 else 2)
        if h % 30 == 0:
            s_ = names.get(h, str(h // 10)); bb = d.textbbox((0, 0), s_, font=f); d.text((x - (bb[2] - bb[0]) / 2, 34), s_, fill=WH, font=f)
im.save(os.path.join(ART, 'gl-compass-band.png')); print('wrote gl-compass-band.png')
