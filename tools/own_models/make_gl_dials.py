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
# Separate front-panel oil pressure. Face only: Claude supplies the chrome bezel,
# glass, hub and moving needle. OILP2 shares oil_press_psi with the combined gauge.
# Bands/limits are typical estimates (E), not an assertion of this aircraft's limits.
a = A('OILP2'); im, d = base()
arc(d, a(25), a(60), 212, 18, YEL); arc(d, a(60), a(90), 212, 18, GRN)
for v in range(0, 121, 5):
    tick(d, a(v), 236 - (32 if v % 20 == 0 else 22 if v % 10 == 0 else 13), 236,
         5 if v % 20 == 0 else 3)
for v in range(0, 121, 20): label(d, a(v), 164, str(v), 36)
for v in (25, 115): tick(d, a(v), 190, 238, 9, RED)
label(d, 0, 76, 'OIL PRESS', 27); label(d, 180, 92, 'PSI', 27)
save(im, 'gl-dial-oilpress.png')
# CHT (F)
a = A('CHT'); im, d = base()
arc(d, a(200), a(435), 212, 20, GRN); arc(d, a(435), a(500), 212, 20, YEL); tick(d, a(500), 190, 236, 10, RED)
for v in range(200, 501, 25): tick(d, a(v), 236 - (32 if v % 100 == 0 else 16), 236, 6 if v % 100 == 0 else 4)
for v in (200, 300, 400, 500): label(d, a(v), 160, str(v), 40)
label(d, 180, 88, 'CYL F', 30); save(im, 'gl-dial-cht.png')
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
# FRONT wet compass: independent assets; leave the existing rear card unchanged.
# gl-front-compass-band.png is the MOVING, horizontally repeating card (no lubber
# line). 2048 x 160; one full 360-degree turn, 2048/360 px per degree.
# Image x increases to pilot's right; h(x) = (-360*x/2048) mod 360.
# N at x=0 (wrap seam), E=1536, S=1024, W=512. For a scrolling flat window,
# sample x = (-heading_deg*2048/360 + local_x) mod 2048, where local_x=0
# at the fixed lubber line. Thus increasing heading moves the card RIGHT.
# Equivalently center U=(-heading_deg/360) mod 1, U increasing to screen right.
# Use repeat U / clamp V, and image-top=UV v=1; do not reverse U a second time.
# A drum can use the same mapping; check N/E/S/W at the line before wiring.
FW, FH = 2048, 160
im = Image.new('RGB', (FW, FH), (24, 24, 26)); d = ImageDraw.Draw(im)
for h in range(0, 360, 5):
    x0 = (-h * FW / 360) % FW
    for x in (x0 - FW, x0, x0 + FW):
        d.line([(x, 12), (x, 50 if h % 30 == 0 else 38 if h % 10 == 0 else 28)],
               fill=WH, width=4 if h % 30 == 0 else 2)
        if h % 30 == 0:
            s_ = names.get(h, str(h // 10)); f = font(48)
            bb = d.textbbox((0, 0), s_, font=f)
            d.text((x - (bb[2] - bb[0]) / 2, 65 - bb[1]), s_, fill=WH, font=f)
save(im, 'gl-front-compass-band.png')
# gl-front-compass-face.png is a STATIC RGBA overlay, never driven by heading.
# Transparent exterior and rounded card window (66,202)..(446,310); the thin
# ivory lubber line stays in front of the card. Housing silhouette is a compact
# rectangular wet-compass case, not a circular accelerometer. Put the card behind
# this window on a separate surface, mask it to the window and keep glass separate.
# At native card scale the 380 px window spans 66.796875 degrees. To fit all 160
# card rows into its 108 px height, scale vertically only (or use equivalent UVs).
im = Image.new('RGBA', (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
d.rounded_rectangle((28, 130, 484, 386), 48, fill=(18, 18, 20, 255), outline=(62, 62, 66, 255), width=6)
d.rounded_rectangle((54, 190, 458, 322), 22, fill=(8, 8, 9, 255), outline=(90, 90, 92, 255), width=4)
d.rounded_rectangle((66, 202, 446, 310), 12, fill=(0, 0, 0, 0))
d.line([(256, 204), (256, 308)], fill=WH + (255,), width=3)
for x in (66, 446):
    d.ellipse((x - 8, 348, x + 8, 364), fill=(90, 90, 92, 255))
    d.line([(x - 5, 356), (x + 5, 356)], fill=(18, 18, 20, 255), width=2)
save(im, 'gl-front-compass-face.png')
# v13.1 compact panel-mount avionics faces (Ty, 2026-10-30): plain faces, no maker text, no legends. 10 px per mm over
# the real 63 x 44 mm control-head face (TY91/TY96-style radio head, TT21/TT22-style transponder head: Trig datasheets);
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
for k, v in AVX.items(): avx_face(v['lcd'], v['seats'], v['buttons'], 'gl-avx-%s.png' % k)
