"""Draws the 2-33 instrument faces (our own original artwork, generic markings, no brand names) into art/.
Run once on any Python with Pillow:  python3 tools/own_models/make_sgs_dials.py
Angles are clockwise from 12 o'clock as seen by the pilot; the needles in build_sgs233.py use the same maps."""
import math, os
from PIL import Image, ImageDraw, ImageFont
ART = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'art'); S = 512; C = S / 2
def font(sz):
    for p in ('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', '/System/Library/Fonts/Helvetica.ttc'):
        if os.path.exists(p): return ImageFont.truetype(p, sz)
    return ImageFont.load_default()
def pol(th, r): t = math.radians(th); return (C + r * math.sin(t), C - r * math.cos(t))
def base():
    im = Image.new('RGB', (S, S), (14, 14, 16)); d = ImageDraw.Draw(im)
    d.ellipse((4, 4, S - 4, S - 4), fill=(20, 20, 22), outline=(70, 70, 74), width=6); return im, d
def tick(d, th, r0, r1, w=4, col=(240, 240, 240)): d.line([pol(th, r0), pol(th, r1)], fill=col, width=w)
def label(d, th, r, s, sz=34):
    f = font(sz); x, y = pol(th, r); bb = d.textbbox((0, 0), s, font=f)
    d.text((x - (bb[2] - bb[0]) / 2, y - (bb[3] - bb[1]) / 2 - bb[1]), s, fill=(240, 240, 240), font=f)
def arc(d, th0, th1, r, w, col):
    d.arc((C - r, C - r, C + r, C + r), th0 - 90, th1 - 90, fill=col, width=w)
# airspeed, mph: 20..140 over 10..310 deg (needle rests at 0 below 20)
asi = lambda v: 10 + 2.5 * (v - 20)
im, d = base()
arc(d, asi(38), asi(65), 214, 18, (40, 170, 60)); arc(d, asi(65), asi(98), 214, 18, (230, 190, 30))   # FAA AFM 1040 lb markings
tick(d, asi(98), 196, 236, 8, (220, 40, 40))
for v in range(20, 141, 5): tick(d, asi(v), 236 - (30 if v % 10 == 0 else 16), 236, 5 if v % 10 == 0 else 3)
for v in range(20, 141, 20): label(d, asi(v), 160, str(v))
label(d, 180, 70, 'MPH', 30); im.save(os.path.join(ART, 'sgs-dial-asi.png'))
# altimeter: 0..9 (x100 ft), long hand 1 turn / 1000 ft, short hand 1 turn / 10000 ft
im, d = base()
for i in range(50): tick(d, i * 7.2, 236 - (32 if i % 5 == 0 else 16), 236, 5 if i % 5 == 0 else 3)
for i in range(10): label(d, i * 36, 166, str(i), 44)
label(d, 180, 80, 'ALT', 30); label(d, 0, 80, '100 FT', 22)
d.rectangle((C + 70, C - 18, C + 140, C + 18), fill=(0, 0, 0), outline=(120, 120, 120), width=2); label(d, 90, 105, '29.9', 24)
im.save(os.path.join(ART, 'sgs-dial-alt.png'))
# variometer, knots: 0 at 9 o'clock, climb clockwise (up), +-10 kt over +-150 deg
va = lambda v: -90 + 15 * v
im, d = base()
for v in range(-10, 11): tick(d, va(v), 236 - (32 if v % 5 == 0 else 16), 236, 5 if v % 5 == 0 else 3)
for v in (-10, -5, 0, 5, 10): label(d, va(v), 168, str(abs(v)), 40)
label(d, 0, 70, 'UP', 28); label(d, 180, 70, 'DN', 28); label(d, 90, 70, 'KTS', 24)
im.save(os.path.join(ART, 'sgs-dial-vario.png'))
# compass card band: heading runs right-to-left (u = 1 - h/360) so the aft face of the drum reads like a real
# wet compass: heading under the lubber line, the next-higher numbers to the LEFT, glyphs upright
W_, H_ = 1024, 96; im = Image.new('RGB', (W_, H_), (24, 24, 26)); d = ImageDraw.Draw(im); f = font(34)
names = {0: 'N', 90: 'E', 180: 'S', 270: 'W'}
for h in range(0, 360, 5):
  for x in ((W_ * (1 - h / 360),) if h else (0, W_)):
    d.line([(x, 0), (x, 22 if h % 10 == 0 else 12)], fill=(240, 240, 240), width=3 if h % 30 == 0 else 2)
    if h % 30 == 0:
        s = names.get(h, str(h // 10)); bb = d.textbbox((0, 0), s, font=f); d.text((x - (bb[2] - bb[0]) / 2, 34), s, fill=(240, 240, 240), font=f)
im.save(os.path.join(ART, 'sgs-compass-band.png'))
# placard + boot / seat fabric are flat colours in the build; one small white placard texture
im = Image.new('RGB', (256, 64), (235, 235, 230)); d = ImageDraw.Draw(im); f = font(13)
d.text((8, 6), 'MANEUVERING SPEED 65 MPH', fill=(20, 20, 20), font=f); d.text((8, 25), 'MAX AERO TOW 98  WINCH 69', fill=(20, 20, 20), font=f)
d.text((8, 44), 'TOW RELEASE  PULL', fill=(20, 20, 20), font=f); im.save(os.path.join(ART, 'sgs-placard.png'))
print('dials written to', ART)
