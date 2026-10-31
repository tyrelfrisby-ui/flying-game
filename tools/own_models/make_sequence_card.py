"""Default aerobatic sequence card for the rear-panel card holder (SequenceCard_Face): the 2026 IAC Power Sportsman
Known, pilot's card page (page 4) of the IAC's published OpenAero sheet, rasterised at letter size (8.5 x 11 in,
portrait) with a figure / Aresti catalogue / K list added in the empty right-hand column. The PDF is NOT committed:
    curl -o build/refs/SPTP-Known-2026.pdf https://www.iac.org/sites/default/files/Sequences/SPTP-Known-2026.pdf
    python3 tools/own_models/make_sequence_card.py [build/refs/SPTP-Known-2026.pdf]
Needs poppler (pdftoppm), Pillow and Liberation Sans (IAC_FONT_DIR). Writes art/sequence-card-sportsman-known-2026.png (1275 x 1650 = 150 dpi). The page carries no
IAC logo; the figure list below is copied from the sheet's Form A (catalogue numbers and K are the IAC's)."""
import os, sys, subprocess, tempfile
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
PDF = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'build', 'refs', 'SPTP-Known-2026.pdf')
OUT = os.path.join(HERE, 'art', 'sequence-card-sportsman-known-2026.png'); W, H = 1275, 1650
# Form A of SPTP-Known-2026.pdf (IAC): figure, Aresti catalogue numbers, K per element
FIGS = [(1, '1.1.6.1', '10'), (2, '1.1.6.3 + 9.11.1.6', '10+3'), (3, '8.5.6.1 + 9.1.4.2', '10+4'), (4, '7.2.2.1 + 9.1.3.2', '6+4'),
        (5, '7.2.3.3 + 9.4.3.2', '6+5'), (6, '7.4.1.1', '10'), (7, '2.2.1.1', '4'), (8, '5.2.1.1', '17'), (9, '1.1.1.1 + 9.1.3.4', '2+8'),
        (10, '1.2.7.1 + 9.1.4.2', '13+4'), (11, '7.2.2.1 + 9.4.3.2', '6+5')]
def font(sz, bold=False):
    filename = 'LiberationSans-Bold.ttf' if bold else 'LiberationSans-Regular.ttf'
    for directory in (os.environ.get('IAC_FONT_DIR', ''), '/usr/share/fonts/truetype/liberation2', '/usr/share/fonts/truetype/liberation'):
        p = os.path.join(directory, filename)
        if os.path.isfile(p): return ImageFont.truetype(p, sz)
    raise FileNotFoundError('Set IAC_FONT_DIR to Liberation Sans (SIL OFL 1.1) font directory')
with tempfile.TemporaryDirectory(prefix='iac-pdf-') as td:
    subprocess.run(['pdftoppm', '-f', '4', '-l', '4', '-png', '-scale-to-x', str(W), '-scale-to-y', str(H), PDF, os.path.join(td, 'p')], check=True)
    im = Image.open(os.path.join(td, sorted(os.listdir(td))[0])).convert('RGB').resize((W, H))
# Preserve the official diagram strokes, figure numerals and dotted/red line styles.
d = ImageDraw.Draw(im)
d.text((40, 34), '2026 IAC POWER SPORTSMAN', fill=(0, 0, 0), font=font(40, True))
d.text((40, 84), 'KNOWN', fill=(200, 20, 20), font=font(40, True))
x0, y0 = 905, 190; d.text((x0, y0), 'No  Aresti cat. No.', fill=(0, 0, 0), font=font(22, True)); d.text((x0 + 300, y0), 'K', fill=(0, 0, 0), font=font(22, True))
d.line((x0, y0 + 32, W - 30, y0 + 32), fill=(0, 0, 0), width=2)
for i, (n, cat, k) in enumerate(FIGS):
    y = y0 + 44 + i * 58; parts = cat.split(' + '); ks = k.split('+')
    d.text((x0, y), '%d' % n, fill=(0, 0, 0), font=font(26, True))
    for j, (c, kk) in enumerate(zip(parts, ks)):
        d.text((x0 + 44, y + j * 25), c, fill=(0, 0, 0), font=font(21)); d.text((x0 + 300, y + j * 25), kk, fill=(0, 0, 0), font=font(21))
yt = y0 + 44 + len(FIGS) * 58 + 6; d.line((x0, yt, W - 30, yt), fill=(0, 0, 0), width=2)
d.text((x0, yt + 10), 'Total K 127', fill=(0, 0, 0), font=font(26, True)); d.text((x0, yt + 46), '+ presentation 10 = 137', fill=(0, 0, 0), font=font(20))
d.rectangle((2, 2, W - 3, H - 3), outline=(0, 0, 0), width=4)
im.save(OUT, optimize=True); print('WROTE', OUT, im.size, os.path.getsize(OUT))

# Standalone Resources texture for runtime default/reset; GLBs also embed this same PNG.
import shutil
resource = os.path.join(ROOT, "unity/Assets/_Project/Resources/SequenceCards/sportsman-known-2026.png")
os.makedirs(os.path.dirname(resource), exist_ok=True)
shutil.copyfile(OUT, resource)

# Original static magnetic-compass artwork for the base Extra panel. No external dial art.
im = Image.new('RGB', (512, 512), (26, 28, 31)); d = ImageDraw.Draw(im)
d.ellipse((8, 8, 504, 504), fill=(54, 57, 61), outline=(172, 175, 178), width=8)
d.ellipse((28, 28, 484, 484), fill=(18, 20, 22), outline=(8, 9, 10), width=14)
d.rounded_rectangle((45, 172, 467, 326), radius=30, fill=(32, 33, 33), outline=(100, 102, 100), width=4)
for x in range(76, 450, 30): d.line((x, 190, x, 208 if (x - 76) % 90 else 223), fill=(235, 234, 215), width=4)
for x, text in ((80, '33'), (233, 'N'), (390, '03')): d.text((x, 237), text, font=font(52), fill=(241, 237, 214))
d.line((256, 165, 256, 334), fill=(238, 136, 71), width=5)
d.text((158, 98), 'COMPASS', font=font(32), fill=(205, 207, 205))
d.text((173, 366), 'MAGNETIC', font=font(25), fill=(180, 184, 183))
im.save(os.path.join(HERE, 'art', 'panel-compass-static.png'), optimize=True)
