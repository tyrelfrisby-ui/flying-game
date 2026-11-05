"""Apply shared face paint to existing own aircraft assets; no Unity/Blender build.
Run after any geometry patch; scales, rigs and all non-instrument buffers survive.
"""
from pathlib import Path
import hashlib, json
from PIL import Image
from vr_cockpit import MODELS, ROOT, Glb
from instrument_face_style import apply_glb, finish, write_runtime_palette

def main():
    write_runtime_palette(ROOT)
    for path in sorted(MODELS.glob('*/*-own*.glb')):
        g = Glb(path)
        count = apply_glb(g, cream=path.parent.name.startswith(('j3-', 'pa18-')))
        if count:
            g.save(path)
            print(path.name, count)
    manifest = Path(__file__).with_name('instrument-style-manifest.json')
    hashes = json.loads(manifest.read_text()) if manifest.exists() else {}
    for path in sorted((ROOT/'unity/Assets/_Project/Resources/CockpitDials').glob('*.png')):
        key = path.name
        if hashes.get(key) != hashlib.sha256(path.read_bytes()).hexdigest():
            finish(Image.open(path)).save(path)
            hashes[key] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest.write_text(json.dumps(hashes, indent=2, sort_keys=True) + '\n')

if __name__ == '__main__':
    main()
