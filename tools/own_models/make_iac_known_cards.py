"""The cockpit's built-in IAC card deck (Ty 2026-11-05: "Click/tap the card to cycle IAC categories: wind-left Primary,
wind-right Primary, wind-left Sportsman, wind-right Sportsman, ... up through Unlimited").

The 2026 IAC POWER Known sequences, as published by the IAC (not redrawn): Form B (page 2, the wind-direction arrow
points LEFT) and Form C (page 3, the arrow points RIGHT) of each category's official PDF, rasterised unchanged at letter
portrait 1275 x 1650 (150 dpi), the size the holder's half-letter card is drawn for (Bridge SequenceCard.PixelsW/H).
    mkdir -p build/refs/iac && for c in PRIP SPTP INTP ADVP UNLP; do
      curl -o build/refs/iac/$c-Known-2026.pdf https://www.iac.org/sites/default/files/Sequences/$c-Known-2026.pdf; done
    /usr/bin/python3 tools/own_models/make_iac_known_cards.py build/refs/iac
Needs poppler (pdftoppm) and Pillow. The PDFs are not committed; their SHA-256 on 2026-11-05 are listed in
docs/IAC-CARDS.md. Unlimited's PDF has three pages (A, B, C), the others four (A, B, C, pilot card)."""
import os, sys, subprocess, tempfile, hashlib
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'build', 'refs', 'iac')
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/SequenceCards'); W, H = 1275, 1650
CATS = [('PRIP', 'primary'), ('SPTP', 'sportsman'), ('INTP', 'intermediate'), ('ADVP', 'advanced'), ('UNLP', 'unlimited')]
for code, cat in CATS:
    pdf = os.path.join(SRC, code + '-Known-2026.pdf')
    print(code, hashlib.sha256(open(pdf, 'rb').read()).hexdigest())
    for page, wind in ((2, 'left'), (3, 'right')):
        with tempfile.TemporaryDirectory(prefix='iac-') as td:
            subprocess.run(['pdftoppm', '-f', str(page), '-l', str(page), '-png', '-scale-to-x', str(W), '-scale-to-y', str(H),
                            pdf, os.path.join(td, 'p')], check=True)
            im = Image.open(os.path.join(td, sorted(os.listdir(td))[0])).convert('RGB')
        if im.size != (W, H): im = im.resize((W, H), Image.LANCZOS)
        out = os.path.join(OUT, 'iac-2026-%s-%s.png' % (cat, wind))
        im.save(out, optimize=True); print('WROTE', out, im.size, os.path.getsize(out))
