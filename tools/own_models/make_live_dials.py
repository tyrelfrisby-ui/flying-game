"""Dial faces for the LIVE rear panels (FlyingGame.Core.CockpitLive, Bridge/CockpitLiveParts.cs).

rear_panel.py paints every PIC dial with a fixed needle and a fixed reading. In a headset those pictures are the
only instruments the pilot has, so the game swaps each face's texture for one of these (no needle painted on) and
turns needles of its own over it. Output: unity/Assets/_Project/Resources/CockpitDials/<name>.png.

 - the EA 300 / Great Lakes dials from art/ (make_ea_dials.py, make_gl_dials.py), COPIED unchanged: their scales
   are the ones the needle maps in CockpitLive.cs were taken from (docs/models/ea300-own-rig.json);
 - two faces that turn instead of a needle, drawn here in the same style (512 px, face (18,18,20), rim (62,62,66)):
   live-compass-card  a compass card, N at 12 o'clock, 5 deg ticks, labels every 30 deg, read at the top;

Run with the system Python that has Pillow: /usr/bin/python3 tools/own_models/make_live_dials.py
"""
import math
import os
import shutil
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, 'art')
OUT = os.path.join(HERE, '..', '..', 'unity', 'Assets', '_Project', 'Resources', 'CockpitDials')
S = 512
C = S / 2
WH = (236, 236, 230)
# No airspeed, altimeter, attitude or g-meter faces here: those are the SHARED INSTRUMENT LIBRARY'S (tools/own_models/instruments).
COPY = ['ea-dial-tach', 'ea-dial-oil', 'ea-dial-mp', 'ea-dial-fuel']


from instrument_face_style import font, finish

def pol(th, r):
    t = math.radians(th)
    return (C + r * math.sin(t), C - r * math.cos(t))


def base(fill=(18, 18, 20)):
    im = Image.new('RGB', (S, S), (10, 10, 11))
    d = ImageDraw.Draw(im)
    d.ellipse((4, 4, S - 4, S - 4), fill=fill, outline=(62, 62, 66), width=6)
    return im, d


def radial_label(im, th, r, s, sz, col):
    """Text whose top faces the rim, so it reads upright when the card brings it to 12 o'clock."""
    f = font(sz)
    bb = ImageDraw.Draw(im).textbbox((0, 0), s, font=f)
    w, h = bb[2] - bb[0] + 8, bb[3] - bb[1] + 8
    tile = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(tile).text((4 - bb[0], 4 - bb[1]), s, fill=col, font=f)
    tile = tile.rotate(-th, resample=Image.BICUBIC, expand=True)
    x, y = pol(th, r)
    im.paste(tile, (int(x - tile.width / 2), int(y - tile.height / 2)), tile)


def compass_card():
    im, d = base()
    for deg in range(0, 360, 5):
        major = deg % 10 == 0
        d.line([pol(deg, 240), pol(deg, 214 if major else 226)], fill=WH, width=4 if major else 2)
    names = {0: 'N', 90: 'E', 180: 'S', 270: 'W'}
    for deg in range(0, 360, 30):
        s = names.get(deg, str(deg // 10))
        radial_label(im, deg, 178, s, 46 if deg in names else 36, (255, 140, 40) if deg == 0 else WH)
    d.ellipse((C - 10, C - 10, C + 10, C + 10), fill=(62, 62, 66))
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    for n in COPY:
        finish(Image.open(os.path.join(ART, n + '.png'))).save(os.path.join(OUT, n + '.png'))
        print('copied', n)
    finish(compass_card()).save(os.path.join(OUT, 'live-compass-card.png'))
    print('wrote live-compass-card ->', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
