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
# airspeed, mph: 20..160 over 15..345 deg like the photographed 2-33 ASI (40 at ~2 o'clock, 160 just left of 12); rests at 0 below 20
asi = lambda v: 15 + (v - 20) * 330 / 140
im, d = base()
arc(d, asi(38), asi(65), 214, 18, (40, 170, 60)); arc(d, asi(65), asi(98), 214, 18, (230, 190, 30))   # FAA AFM 1040 lb markings
tick(d, asi(98), 196, 236, 8, (220, 40, 40))
for v in range(20, 161, 5): tick(d, asi(v), 236 - (30 if v % 10 == 0 else 16), 236, 5 if v % 10 == 0 else 3)
for v in range(40, 161, 20): label(d, asi(v), 160, str(v))
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
# ---- v8 panel face (photo-matched black D panel). Texture spans y +PW..-PW (pilot's left -> right) and z_b..z_b+PH.
# Positions (metres from the panel bottom centre, +y = pilot's left) are shared with sgs233_cockpit.py PANEL_LAYOUT.
PW, PH = 0.31, 0.2325; TW, TH = 2048, 768; PPM = TW / (2 * PW)
def P2(y, h): return (TW / 2 - y * PPM, TH - h * PPM)
BLK = (22, 22, 24)
im = Image.new('RGB', (TW, TH), BLK); d = ImageDraw.Draw(im)
def card(y, h, w, ht, bg, lines, fg=(20, 20, 20), sz=22, border=None):
    x0, y0 = P2(y + w / 2, h + ht / 2); x1, y1 = P2(y - w / 2, h - ht / 2)
    d.rectangle((x0, y0, x1, y1), fill=bg, outline=border or (150, 150, 150), width=3)
    yy = y0 + 8
    for ln, fs, bold in lines:
        f = font(fs); bb = d.textbbox((0, 0), ln, font=f)
        d.text(((x0 + x1) / 2 - (bb[2] - bb[0]) / 2, yy), ln, fill=fg, font=f); yy += fs + 6
lim1 = [('FLIGHT LIMITS', 26, 1), ('SOLO FRONT SEAT ONLY', 17, 1), ('MAX WT  1040 LB', 15, 0), ('MIN PILOT WT SOLO', 15, 0),
        ('NEVER EXCEED 98 MPH', 15, 0), ('MAX AERO TOW  98 MPH', 15, 0), ('MAX WINCH TOW  69 MPH', 15, 0), ('MANEUVERING  65 MPH', 15, 0)]
card(+0.136, 0.114, 0.068, 0.062, (232, 232, 226), lim1)
card(+0.132, 0.030, 0.070, 0.032, (226, 226, 220), [('FLIGHT LIMITS  2-33A', 18, 1), ('SAC BALLAST', 17, 1), ('MIN WT  FRONT SEAT', 13, 0)])
card(-0.054, 0.045, 0.046, 0.022, (238, 238, 236), [('PULL TO RELEASE', 14, 0)])
card(-0.124, 0.022, 0.072, 0.040, (196, 214, 236), [('PRE-TAKEOFF CHECK', 16, 1), ('CONTROLS  FREE', 13, 0), ('BALLAST  CHECKED', 13, 0),
     ('STRAPS  TIGHT', 13, 0), ('CANOPY  LOCKED', 13, 0)], fg=(20, 30, 60))
f = font(30); bb = d.textbbox((0, 0), 'RELEASE', font=f); x, y = P2(0, 0.014)   # red, partly behind the ball as photographed
d.text((x - (bb[2] - bb[0]) / 2, y - 18), 'RELEASE', fill=(200, 40, 30), font=f)
for (sy, sh) in ((0.27, 0.02), (-0.27, 0.02), (0.20, 0.19), (-0.20, 0.19), (0.05, 0.205), (-0.05, 0.205), (0.04, 0.12), (-0.04, 0.12),
                 (0.03, 0.05), (-0.03, 0.05), (0.07, 0.003), (-0.07, 0.003)):
    x, y = P2(sy, sh); d.ellipse((x - 7, y - 7, x + 7, y + 7), fill=(150, 150, 152))   # panel screws
im.save(os.path.join(ART, 'sgs-panel-face.png'))
# left-wall air-brake panel: 0.32 x 0.12 m, u = aft -> forward as seen from inside (EXTEND aft, RETRACT forward), kidney slot drawn dark
WW, WH = 1024, 384; LIN = (222, 223, 220)
im = Image.new('RGB', (WW, WH), LIN); d = ImageDraw.Draw(im)
f = font(40); d.text((70, 18), 'EXTEND', fill=(20, 20, 20), font=f); d.line([(260, 44), (720, 44)], fill=(20, 20, 20), width=6)
d.text((740, 18), 'RETRACT', fill=(20, 20, 20), font=font(40))
f = font(34); d.text((60, 150), 'AIR', fill=(20, 20, 20), font=f); d.text((40, 192), 'BRAKE', fill=(20, 20, 20), font=f)
d.rounded_rectangle((250, 170, 1000, 300), radius=60, fill=(208, 209, 206), outline=(150, 150, 150), width=4)     # pressed slot surround
d.rounded_rectangle((285, 205, 975, 268), radius=30, fill=(14, 14, 14))                                         # the slot
im.save(os.path.join(ART, 'sgs-wall-airbrake.png'))
# weight-and-balance placard painted on the rear-seat left wall (as photographed): 0.50 x 0.075 m
im = Image.new('RGB', (1280, 192), LIN); d = ImageDraw.Draw(im); f = font(34)
d.text((10, 8), 'C.G. 78.2-86.1', fill=(20, 20, 20), font=f); d.text((330, 8), 'EMPTY 647.0  61542.5', fill=(20, 20, 20), font=f)
d.text((830, 8), 'W-BALLAST 666.5  61797', fill=(20, 20, 20), font=font(32))
d.text((380, 64), 'FRONT  SEAT  43.8', fill=(20, 20, 20), font=f); d.text((380, 118), 'REAR   SEAT  74.7', fill=(20, 20, 20), font=f)
im.save(os.path.join(ART, 'sgs-wall-wb.png'))
# RELEASE label on the white seat-back cross tube (wraps the tube: u around, v along)
im = Image.new('RGB', (512, 128), (236, 236, 234)); d = ImageDraw.Draw(im); f = font(64)
d.text((40, 24), 'RELEASE', fill=(200, 36, 28), font=f); im.save(os.path.join(ART, 'sgs-release-tube.png'))
# seat cushion stripes: red with cream pin stripes (as photographed)
im = Image.new('RGB', (256, 256), (186, 32, 36)); d = ImageDraw.Draw(im)
for k in range(8): y = 16 + k * 32; d.rectangle((0, y, 256, y + 4), fill=(232, 214, 176))
im.save(os.path.join(ART, 'sgs-cushion-stripe.png'))
print('dials written to', ART)
