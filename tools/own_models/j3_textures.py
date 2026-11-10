"""J-3 own model v5 textures (Pillow + DejaVu fonts; generated, nothing traced or copied).
  python3 j3_textures.py OUTDIR    -> j3_panel_atlas.png (gauge faces + placard), j3_prop_wood.png (grain, red tips, sheath)
Gauge layout / colours read by eye off Ty's cockpit photo (refs/j3-cub/ty-panel.png): cream faces, black markings, no
maker names.
Ty t934u: Cub exception — remove ALL colored bands/arcs; red limit lines ONLY for max oil temp, min AND max oil
pressure, max RPM, max airspeed. Ty chose KEEP 85-hp C-85 physics/config; match instrument redlines to C-85 (TCDS E-233),
not A-65/E-205. Airframe ASI max from A-691 glide/dive 122 mph CAS (mph face). Never dial scale ends.
t935: oil mid-line removed; otherwise J3 face styling approved (visual only).  Prop: laminated-birch look, varnish tint, red tip band (Ty) on both faces, brass LE sheath + rivet line
(Ty's Sensenich-type photo refs/j3-cub/ty-prop.png; the maker's decal is a trademark and is NOT reproduced)."""
import sys, os, math, numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
OUT = sys.argv[1] if len(sys.argv) > 1 else '.'; os.makedirs(OUT, exist_ok=True)
FD = '/usr/share/fonts/truetype/dejavu/'
from instrument_face_style import font as F, finish
def FC(sz): return F(sz)
CREAM, INK, RED, GRN = (245, 232, 195), (5, 5, 5), (196, 36, 26), (40, 150, 60)
S = 512                                   # one gauge cell (face disc fills the cell); atlas 5 cells + placard row
A = Image.new('RGB', (S * 5, S + 128), (20, 20, 20)); D = ImageDraw.Draw(A)
def polar(cx, cy, r, deg):                 # deg clockwise from 12 o'clock
    t = math.radians(deg); return cx + r * math.sin(t), cy - r * math.cos(t)
def face(i, col=CREAM):
    cx, cy = i * S + S / 2, S / 2; D.ellipse((cx - S / 2, cy - S / 2, cx + S / 2, cy + S / 2), fill=col)
    # faint ageing
    return cx, cy
def ticks(cx, cy, r0, r1, a0, a1, n, w, col=INK):
    for k in range(n + 1):
        a = a0 + (a1 - a0) * k / n; D.line([polar(cx, cy, r0, a), polar(cx, cy, r1, a)], fill=col, width=w)
def label(cx, cy, r, a, txt, f, col=INK):
    x, y = polar(cx, cy, r, a); D.text((x, y), txt, font=f, fill=col, anchor='mm')
def arc(cx, cy, r, a0, a1, w, col):
    D.arc((cx - r, cy - r, cx + r, cy + r), a0 - 90, a1 - 90, fill=col, width=w)
# v15 rig: the needles and the compass card are separate animated meshes in the GLB (build_j3_cub.py), so the faces
# are painted WITHOUT needles; PAINT_NEEDLES=1 restores the old painted needles (reference only).
PAINT_NEEDLES = os.environ.get('PAINT_NEEDLES') == '1'
def needle(cx, cy, a, L, col=RED, w=9, tail=40):
    if not PAINT_NEEDLES: return
    D.line([polar(cx, cy, -tail, a), polar(cx, cy, L, a)], fill=col, width=w); D.ellipse((cx - 16, cy - 16, cx + 16, cy + 16), fill=(30, 30, 30))
# Rear-seat lettering: radial/text offsets clear the original ticks; scale angles and arcs stay fixed.
# 0 tachometer (x100 RPM): 5..35 over 270 deg
cx, cy = face(0); a0, a1 = -135, 135
ticks(cx, cy, 200, 238, a0, a1, 30, 4); ticks(cx, cy, 185, 238, a0, a1, 6, 11)
for k, v in enumerate(range(5, 40, 5)): label(cx, cy, 138, a0 + 270 * k / 6, str(v), F(72))
# Max RPM: Continental C-85 TCDS E-233 — max continuous 85 hp at 2575 rpm (matches config propulsion.maxRpm).
# (Ty: keep 85-hp C-85; match instrument limits to it — not A-65 2350.)
arc(cx, cy, 226, a0 + 270 * (25.75 - 5) / 30 - 1.5, a0 + 270 * (25.75 - 5) / 30 + 1.5, 26, RED)
D.text((cx, cy - 30), 'R P M', font=F(52), fill=INK, anchor='mm'); D.text((cx, cy + 150), 'X 100', font=F(36), fill=INK, anchor='mm')
needle(cx, cy, a0 + 270 * (18 - 5) / 30, 200, (200, 70, 30))
# 1 airspeed (MPH): 40 at 1 o'clock clockwise to 140 at ~11 o'clock
cx, cy = face(1)
def asd(v): return 30 + (v - 40) * 300 / 100.0
arc(cx, cy, 214, asd(38), asd(122) , 0, INK)
# t934u: no color band. Max airspeed red = FAA Spec A-691 Airspeed Limits (CAS) "Glide or dive 122 mph (106 knots)".
# (Level flight or climb 90 mph is a separate limit — not painted as green here per t934.)
# config limits.vneMs 54.5 = 122 mph matches A-691 glide/dive; build_j3.py had flagged secondary until TCDS check.
D.line([polar(cx, cy, 196, asd(122)), polar(cx, cy, 250, asd(122))], fill=RED, width=12)
ticks(cx, cy, 205, 238, asd(40), asd(140), 20, 4); ticks(cx, cy, 190, 238, asd(40), asd(140), 5, 11)
for v in range(40, 160, 20): label(cx, cy, 128, asd(v), str(v), F(68))
D.text((cx, cy - 44), 'AIR SPEED', font=F(40), fill=INK, anchor='mm'); D.text((cx, cy + 160), 'M P H', font=F(36), fill=INK, anchor='mm')
needle(cx, cy, asd(40) - 12, 205)
# 2 compass ball: black window; v15: the card is a separate drum mesh (Compass_Card) textured from the strip below,
# the lubber line is a white bar in front of it (Cabin mesh)
cx, cy = face(2, (14, 14, 16))
if PAINT_NEEDLES: D.rectangle((cx - 200, cy - 70, cx + 200, cy + 70), fill=(26, 26, 30))
for k, t in enumerate(['30', '33', 'N', '3', '6'] if PAINT_NEEDLES else []):
    x = cx - 176 + k * 88
    D.text((x, cy - 6), t, font=F(62 if t == 'N' else 44), fill=(240, 240, 236), anchor='mm')
if PAINT_NEEDLES:
    for k in range(42): x = cx - 205 + k * 10; D.line([(x, cy + 52), (x, cy + 70 - (8 if k % 3 else 0))], fill=(230, 230, 230), width=3)
    D.line([(cx + 8, cy - 140), (cx + 8, cy + 150)], fill=(245, 245, 240), width=7)
D.line([(cx - 120, cy + 120), (cx + 120, cy + 120)], fill=(200, 200, 196), width=3)
# v15 compass card strip (atlas x 640..2560, y 512..640): u = heading label / 360 across the full drum circumference.
# Wet-compass card: seen from the pilot the numbers run right-to-left (reverse sensing) -- the drum UVs handle that; here
# the labels are drawn left to right 0..360. The drum is 0.016 m radius x 0.016 m tall, so a drum mm is 19.1 px across but
# 8 px up: each label is drawn upright then stretched x2.39 so it reads with normal proportions on the drum.
CS0, CSW, CSH = 640, 1920, 128; CR, CHT = 0.016, 0.016; KX = (CSW / (2 * math.pi * CR)) / (CSH / CHT)
D.rectangle((CS0, S, CS0 + CSW, S + CSH), fill=(26, 26, 30))
for deg in range(0, 360, 5):
    x = CS0 + CSW * deg / 360.0; tall = deg % 30 == 0; mid = deg % 10 == 0
    D.line([(x, S + CSH - 6), (x, S + CSH - (34 if tall else 24 if mid else 15))], fill=(235, 235, 230), width=3)
NAMES = {0: 'N', 90: 'E', 180: 'S', 270: 'W'}
for deg in range(0, 360, 30):
    t = NAMES.get(deg, str(deg // 10)); f = F(66 if deg in NAMES else 50)
    tw = int(D.textlength(t, font=f)) + 8; L = Image.new('RGB', (tw, 82), (26, 26, 30)); LD = ImageDraw.Draw(L)
    LD.text((tw / 2, 41), t, font=f, fill=(255, 255, 250), anchor='mm')
    L = L.resize((max(1, int(tw * KX)), 82), Image.LANCZOS)
    L = L.transpose(Image.FLIP_LEFT_RIGHT)                       # strip u runs right-to-left on the drum face seen from the seat
    for xo in (0, -CSW, CSW):                                     # wrap at 0/360
        x = int(CS0 + CSW * deg / 360.0 - L.width / 2) + xo
        if x + L.width > CS0 and x < CS0 + CSW:
            cl = max(0, CS0 - x); cr = min(L.width, CS0 + CSW - x); A.paste(L.crop((cl, 0, cr, 82)), (x + cl, S + 10))
# 3 altimeter: 0..9 x 1000 ft, one long + one short hand
cx, cy = face(3); ticks(cx, cy, 205, 238, 0, 360, 50, 4); ticks(cx, cy, 185, 238, 0, 360, 10, 11)
# Pull only the bottom numeral inward to clear the existing setting-window oval.
for v in range(10): label(cx, cy, 94 if v == 5 else 152, v * 36, str(v), F(76))
D.text((cx, cy - 72), 'ALTITUDE', font=F(38), fill=INK, anchor='mm'); D.text((cx, cy + 40), 'FEET', font=F(32), fill=INK, anchor='mm')
if PAINT_NEEDLES: D.line([(cx, cy), polar(cx, cy, 120, 10)], fill=INK, width=16)
needle(cx, cy, 3, 210, INK, 8)
D.ellipse((cx - 46, cy + 128, cx + 46, cy + 160), outline=INK, width=3)
# 4 oil temperature / pressure combination: two arc dials, top TEMP, bottom PRESSURE
cx, cy = face(4)
for yo, lab, vals, red_at in ((-95, 'TEMPERATURE', ['100', '150', '200', '250'], 1.0), (115, 'OIL PRESSURE', ['0', '20', '40', '60', '80'], None)):
    oc = cy + yo + 60; D.arc((cx - 190, oc - 190, cx + 190, oc + 190), -150, -30, fill=INK, width=5)
    n = len(vals) - 1
    for k, v in enumerate(vals):
        a = -60 + 120 * k / n; D.line([polar(cx, oc, 175, a), polar(cx, oc, 200, a)], fill=INK, width=9)
        label(cx, oc, 120, a, v, F(40))
    # t934/t935: no color bands; no mid-line. Red lines = C-85 certified limits (TCDS E-233), NOT dial scale ends.
    # Oil temp max 225 F: E-233 NOTE 1 C85 series max oil inlet temperature 225 F (dial to 250 ≠ limit).
    # Oil press min 30 / max 40 psig: E-233 NOTE 1 "oil pressure limits 30 to 40 p.s.i." (not scale-end 80; not A-65 30-60).
    if red_at is not None:
        a_tmax = -60 + 120 * (225 - 100) / (250 - 100)
        D.line([polar(cx, oc, 170, a_tmax), polar(cx, oc, 215, a_tmax)], fill=RED, width=10)
    else:
        a_pmin = -60 + 120 * (30 / 80.0)
        a_pmax = -60 + 120 * (40 / 80.0)
        D.line([polar(cx, oc, 170, a_pmin), polar(cx, oc, 215, a_pmin)], fill=RED, width=10)
        D.line([polar(cx, oc, 170, a_pmax), polar(cx, oc, 215, a_pmax)], fill=RED, width=10)
    D.text((cx, oc - 25), lab, font=F(30), fill=INK, anchor='mm')
    if PAINT_NEEDLES: D.line([(cx, oc), polar(cx, oc, 190, -10 if red_at else 8)], fill=INK, width=8)
# t935u: Remove the mid line on the oil temp pressure otherwise approved for j3.
# (horizontal divider across dual oil temp/pressure face ONLY — do not remove scales/ticks/fonts/redlines)
# placard (aluminium, black letters)
P0 = (0, S, 640, S + 128); D.rectangle(P0, fill=(178, 182, 186)); D.rectangle((8, S + 8, 632, S + 120), outline=INK, width=5)
D.text((320, S + 42), 'REAR SEAT FOR', font=FC(46), fill=INK, anchor='mm'); D.text((320, S + 90), 'SOLO FLYING', font=FC(46), fill=INK, anchor='mm')
# subtle face grain / ageing so the cream is not flat
arr = np.asarray(A).astype(np.float32); rng = np.random.default_rng(3)
arr += rng.normal(0, 2.5, arr.shape[:2])[..., None]; A = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
finish(A, cream=True).save(os.path.join(OUT, 'j3_panel_atlas.png'))
# ---- prop wood: u = blade radial coordinate y in [-R, R], v = chordwise z in [-ZH, ZH] (planar projection, both faces) ----
R, ZH, IN = 36 * 0.0254, 0.10, 0.0254
W, H = 4096, 512
ys = np.linspace(-R, R, W)[None, :]; zs = np.linspace(ZH, -ZH, H)[:, None]
r = np.abs(ys); rng = np.random.default_rng(7)
# birch laminations: bands follow the blade's thickness contours -> seen on the faces as lines running along the blade,
# converging toward the hub; plus fine long grain
lw = np.array(Image.fromarray((rng.random((8, 64)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)) / 255.0
warp = 0.006 * np.sin(ys * 7.0) + 0.003 * np.sin(ys * 19 + 1.3) + 0.012 * (lw - 0.5)
lam = np.sin((zs + warp) / (0.0105 + 0.010 * np.clip(r / R, 0, 1)) * math.pi)
fine = np.array(Image.fromarray((rng.random((H // 4, W)) * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR)) / 255.0
fine = np.array(Image.fromarray((fine * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur((6, 0.6)))) / 255.0
hub = np.exp(-((np.hypot(ys, zs) / 0.09) ** 4))                                   # end-grain rings on the hub boss
rings = np.sin(np.hypot(ys * 1.15, zs) * 180 + 6 * (lw - 0.5)) * hub
lam = np.sign(lam) * np.abs(lam) ** 3
t = 0.62 + 0.12 * lam * (1 - hub) + 0.35 * (fine - 0.5) + 0.10 * rings - 0.15 * hub + 0.10 * (lw - 0.5)
light, dark = np.array((236, 196, 132)), np.array((182, 120, 62))                  # varnished birch (amber cast)
img = (light[None, None] * t[..., None] + dark[None, None] * (1 - t[..., None]))
# red tip band (Ty): 12.4 in from the tip along the blade (measured on ty-prop.png), edge only slightly oblique across the chord,
# the band ends further out on the leading-edge side as in the photo; sheath LE side is +z for y>0, -z for y<0
sgn = np.sign(ys); chordpos = np.clip(zs * sgn / 0.06, -1, 1)
edge = R - 12.4 * IN + 0.35 * IN * chordpos   # v5b: near-square edge as on ty-prop.png
img[np.broadcast_to(r > edge, img.shape[:2])] = (176, 24, 22)
# generic, TEXT-FREE blade decal (the maker's winged decal on Ty's photo is a trademark and is not reproduced): a small
# dark-green oval with a thin gold rim at ~0.48 R, centred on the chord, same place as the photo's decal
for sg_ in (1, -1):
    dd = ((ys - sg_ * 0.48 * R) / 0.030) ** 2 + (zs / 0.013) ** 2
    img[np.broadcast_to(dd < 1.0, img.shape[:2])] = (196, 160, 70)
    img[np.broadcast_to(dd < 0.70, img.shape[:2])] = (34, 60, 40)
Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(os.path.join(OUT, 'j3_prop_wood.png'))
# ---- fin emblem (Ty: refs/j3-cub/ty-tail-bear.png for size / placement / colours only): an ORIGINAL cartoon bear drawn
# here from circles and ellipses, holding a red sign that reads only "CUB" (cream block letters, thin cream border).
# No maker name, no city line, nothing traced from the photo.
BW, BH = 900, 1066
E = Image.new('RGBA', (BW, BH), (0, 0, 0, 0)); G = ImageDraw.Draw(E)
FUR, FURD, LINE, CRM, SRED = (100, 60, 32, 255), (80, 46, 24, 255), (46, 26, 12, 255), (240, 226, 192, 255), (176, 30, 28, 255)
def ell(c, rx, ry, fill, w=8, line=LINE): G.ellipse((c[0] - rx, c[1] - ry, c[0] + rx, c[1] + ry), fill=fill, outline=line, width=w)
for ex in (292, 608):                                                   # ears
    ell((ex, 112), 74, 70, FUR); ell((ex, 118), 38, 34, (198, 142, 98, 255), 0)
ell((450, 600), 250, 210, FUR)                                          # body (mostly behind the sign)
for fx_ in (318, 582):                                                  # feet below the sign
    ell((fx_, 868), 104, 62, FUR); ell((fx_, 878), 52, 28, (198, 142, 98, 255), 0)
    for k in (-34, 0, 34): ell((fx_ + k, 836), 13, 11, (198, 142, 98, 255), 0)
SG = [(60, 470), (110, 420), (790, 420), (840, 470), (840, 760), (790, 810), (110, 810), (60, 760)]
G.polygon(SG, fill=SRED, outline=LINE); G.line(SG + [SG[0]], fill=LINE, width=8)
IN_ = [(x_ + (24 if x_ < 450 else -24), y_ + (24 if y_ < 615 else -24)) for x_, y_ in SG]
G.line(IN_ + [IN_[0]], fill=CRM, width=7)
G.text((450, 622), 'CUB', font=F(290), fill=CRM, anchor='mm')
ell((450, 262), 196, 182, FUR)                                          # head
ell((450, 340), 118, 82, CRM, 6)                                        # muzzle
ell((450, 298), 42, 28, (20, 16, 14, 255), 0)                           # nose
G.ellipse((440, 288, 452, 296), fill=(230, 230, 230, 255))
G.line([(450, 322), (450, 350)], fill=LINE, width=6)
G.arc((392, 300, 508, 392), 20, 160, fill=LINE, width=7)                 # grin
G.chord((420, 360, 480, 404), 0, 180, fill=(204, 70, 70, 255))           # tongue
for ex_ in (382, 518):                                                  # eyes + brows
    G.ellipse((ex_ - 30, 192, ex_ + 30, 252), fill=(250, 250, 246, 255), outline=LINE, width=5)
    G.ellipse((ex_ - 12 + 6, 212, ex_ + 12 + 6, 240), fill=(18, 14, 12, 255)); G.ellipse((ex_ + 4, 214, ex_ + 12, 222), fill=(255, 255, 255, 255))
    G.arc((ex_ - 34, 158, ex_ + 34, 196), 200, 340, fill=LINE, width=7)
for px_, sg_ in ((178, -1), (722, 1)):                                  # paws gripping the sign's top corners
    G.line([(450 + sg_ * 150, 470), (px_ - sg_ * 10, 448)], fill=FUR, width=70)
    ell((px_, 446), 62, 50, FUR)
    for k in (-24, 0, 24): G.line([(px_ + k, 470), (px_ + k, 492)], fill=LINE, width=5)
# sticker edge: a thin cream margin around the whole emblem
al = E.split()[3]; edge = al.filter(ImageFilter.MaxFilter(23))
base = Image.new('RGBA', (BW, BH), (236, 228, 204, 0)); base.putalpha(edge)
base = Image.alpha_composite(base, E); base.save(os.path.join(OUT, 'j3_fin_bear.png'))
print('wrote', OUT)
