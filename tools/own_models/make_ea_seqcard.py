"""Default aerobatic sequence card for the rear-panel card holder (SequenceCard_Face): the 2026 IAC Power Sportsman
Known, pilot's card page (page 4) of the IAC's published OpenAero sheet, rasterised at letter size (8.5 x 11 in,
portrait) with a figure / Aresti catalogue / K list added in the empty right-hand column. The PDF is NOT committed:
    curl -o build/refs/SPTP-Known-2026.pdf https://www.iac.org/sites/default/files/Sequences/SPTP-Known-2026.pdf
    python3 tools/own_models/make_ea_seqcard.py [build/refs/SPTP-Known-2026.pdf]
Needs poppler (pdftoppm) and Pillow. Writes art/ea-seqcard-default.png (1275 x 1650 = 150 dpi). The page carries no
IAC logo; the figure list below is copied from the sheet's Form A (catalogue numbers and K are the IAC's)."""
import os, sys, subprocess, tempfile
from PIL import Image, ImageDraw, ImageFont, ImageFilter
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
PDF = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'build', 'refs', 'SPTP-Known-2026.pdf')
OUT = os.path.join(HERE, 'art', 'ea-seqcard-default.png'); W, H = 1275, 1650
# Form A of SPTP-Known-2026.pdf (IAC, published 2025-11-20): figure, Aresti catalogue numbers, K per element
FIGS = [(1, '1.1.6.1', '10'), (2, '1.1.6.3 + 9.11.1.6', '10+3'), (3, '8.5.6.1 + 9.1.4.2', '10+4'), (4, '7.2.2.1 + 9.1.3.2', '6+4'),
        (5, '7.2.3.3 + 9.4.3.2', '6+5'), (6, '7.4.1.1', '10'), (7, '2.2.1.1', '4'), (8, '5.2.1.1', '17'), (9, '1.1.1.1 + 9.1.3.4', '2+8'),
        (10, '1.2.7.1 + 9.1.4.2', '13+4'), (11, '7.2.2.1 + 9.4.3.2', '6+5')]
def font(sz, bold=False):
    for p in (('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf' if bold else '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'),
              '/System/Library/Fonts/Helvetica.ttc'):
        if os.path.exists(p): return ImageFont.truetype(p, sz)
    return ImageFont.load_default()
with tempfile.TemporaryDirectory() as td:
    subprocess.run(['pdftoppm', '-f', '4', '-l', '4', '-png', '-scale-to-x', str(W), '-scale-to-y', str(H), PDF, os.path.join(td, 'p')], check=True)
    im = Image.open(os.path.join(td, sorted(os.listdir(td))[0])).convert('RGB').resize((W, H))
im = im.filter(ImageFilter.MinFilter(3))   # thicken the 1 px OpenAero strokes so the card reads at cockpit distance
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
