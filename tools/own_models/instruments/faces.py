"""The ONE set of dial-painting primitives (every per-aircraft dial script used to carry its own pol/base/tick/label/arc).
Image = the face disc: S px across, radius S/2 = one face radius. Angles: degrees clockwise from 12 o'clock as the pilot
sees the dial. Paint follows instrument_face_style (white on black, Arial Bold, the fleet's stroke)."""
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from instrument_face_style import font, finish  # noqa: E402

S = 1024
C = S / 2
FACE = (18, 18, 20)
RIM = (62, 62, 66)
WHITE = (238, 238, 232)
GREEN, YELLOW, RED = (40, 170, 60), (232, 190, 30), (220, 40, 40)
ARC_WHITE = (236, 236, 230)


def px(r):
    """Face radii -> pixels."""
    return r * C


def pol(th, r):
    t = math.radians(th)
    return (C + px(r) * math.sin(t), C - px(r) * math.cos(t))


def base(fill=FACE):
    im = Image.new('RGB', (S, S), (10, 10, 11))
    d = ImageDraw.Draw(im)
    d.ellipse((0, 0, S - 1, S - 1), fill=fill)
    d.ellipse((6, 6, S - 7, S - 7), outline=RIM, width=10)
    return im, d


def tick(d, th, r0, r1, w=8, col=WHITE):
    d.line([pol(th, r0), pol(th, r1)], fill=col, width=int(w))


def bar(d, th, r0, r1, w, col=WHITE):
    """A radial bar of exact width w (px), squared ends: what the TC's rate index is."""
    t = math.radians(th); ux, uy = math.sin(t), -math.cos(t); nx, ny = -uy, ux
    a, b = pol(th, r0), pol(th, r1); h = w / 2
    d.polygon([(a[0] + nx * h, a[1] + ny * h), (b[0] + nx * h, b[1] + ny * h), (b[0] - nx * h, b[1] - ny * h), (a[0] - nx * h, a[1] - ny * h)], fill=col)


def label(d, th, r, s, size=60, col=WHITE):
    f = font(int(size)); x, y = pol(th, r); bb = d.textbbox((0, 0), s, font=f)
    d.text((x - (bb[2] - bb[0]) / 2 - bb[0], y - (bb[3] - bb[1]) / 2 - bb[1]), s, fill=col, font=f)


def text_at(d, x, y, s, size=40, col=WHITE):
    """Text centred on (x, y) in face radii (+y up)."""
    f = font(int(size)); bb = d.textbbox((0, 0), s, font=f)
    X, Y = C + px(x), C - px(y)
    d.text((X - (bb[2] - bb[0]) / 2 - bb[0], Y - (bb[3] - bb[1]) / 2 - bb[1]), s, fill=col, font=f)


def arc(d, th0, th1, r, w, col):
    """A coloured band centred on radius r (face radii), w px wide, from th0 to th1 clockwise."""
    a0, a1 = sorted((th0, th1)); R = px(r)
    d.arc((C - R - w / 2, C - R - w / 2, C + R + w / 2, C + R + w / 2), a0 - 90, a1 - 90, fill=col, width=int(w))


def done(im):
    return finish(im)
