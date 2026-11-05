"""Fleet instrument paint, based on the Extra's white-on-black faces.

Only paint belongs here. Callers retain their POH scales, arcs, labels and UVs.
Cream uses the identical bold font and stroke treatment with reversed contrast.
The raster adapter lets old embedded faces share the style without re-exporting
an airframe or changing a needle's calibration. Coloured pixels are preserved.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

BLACK = (8, 8, 8)
WHITE = (255, 255, 255)
CREAM = (245, 232, 195)
INK = (5, 5, 5)

def font(size):
    for path in ('/System/Library/Fonts/Supplemental/Arial Bold.ttf',
                 '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
                 '/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf'):
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    raise FileNotFoundError('Instrument faces require Arial Bold or DejaVu Sans Bold')

def base(size=512, cream=False):
    im = Image.new('RGB', (size, size), CREAM if cream else BLACK)
    return im, ImageDraw.Draw(im)

def finish(image, cream=False):
    """Normalize neutral paint and add one texel of stroke at 512 px.

    Saturated scale arcs, horizon colours and redlines keep their exact pixels.
    Alpha, resolution, positions and angular scale registration are unchanged.
    Cream backgrounds are intentionally excluded from the neutral-ink mask.
    """
    rgb = np.array(image.convert('RGB'))
    hi, lo = rgb.max(2).astype(int), rgb.min(2).astype(int)
    neutral = hi - lo < 38
    if cream:
        background = (rgb[:,:,0] > 170) & (rgb[:,:,1] > 160) & (rgb[:,:,2] > 125)
    else:
        background = neutral & (hi < 115)
    coverage = np.clip((160 - hi) / 155 if cream else (lo - 70) / 150, 0, 1)
    coverage *= neutral
    if cream: coverage *= ~background
    mask = Image.fromarray((coverage * 255).astype('uint8'))
    # 1024 px faces get two pixels; narrow compass bands stay at one.
    radius = max(1, min(image.size) // 512)
    bold = np.array(mask.filter(ImageFilter.MaxFilter(radius * 2 + 1))).astype(float) / 255
    out = rgb.copy()
    out[background] = CREAM if cream else BLACK
    # Do not grow white/black ink over any coloured operating arc.
    allowed = neutral | background
    ink = np.array(INK if cream else WHITE)
    out[allowed] = (out[allowed] * (1 - bold[allowed,None]) + ink * bold[allowed,None]).astype('uint8')
    result = Image.fromarray(out)
    if image.mode == 'RGBA':
        result.putalpha(image.getchannel('A'))
    return result

def cream_from_black(image):
    """A black-faced dial as the cream variant (the PA-18's downloaded faces, Ty 2026-11-04: the Cubs keep cream).

    Neutral paint is re-inked by its lightness: black face -> CREAM, white markings -> INK, greys in between, so
    antialiased edges stay smooth. Saturated arcs and redlines keep their exact pixels, then `finish(cream=True)`
    gives the same bold stroke as every other face.
    """
    rgb = np.array(image.convert('RGB')).astype(float)
    hi, lo = rgb.max(2), rgb.min(2)
    neutral = (hi - lo) < 38
    ink = (hi / 255.0)[:, :, None]
    out = rgb.copy()
    painted = np.array(CREAM, float) * (1 - ink) + np.array(INK, float) * ink
    out[neutral] = painted[neutral]
    out[neutral & (hi < 115)] = CREAM   # the face itself, as finish() reads a black one: dark greys are background too
    result = Image.fromarray(out.astype('uint8'))
    # The re-inking already gives pure ink on pure cream. The bold stroke is one texel at 512 px; on a 256 px map
    # that is twice as heavy and closes up the small print, so a small map keeps its own line weight.
    return finish(result, cream=True) if min(image.size) >= 512 else result

# The PA-18 (Sketchfab "Piper Pa 18" by helijah, CC BY 4.0) carries its dials as JPEG maps beside its OBJ. Not its
# attitude gyro (DefaultWhite_ai): that map is an atlas of horizon card, case and symbol, and no gyro is cream.
PA18_FACES = ['DefaultWhite_%s.png_baseColor.jpg' % k for k in ('alt', 'asi', 'hdg', 'turn', 'vsi')]

def is_face(name):
    return (name.startswith(('Dial_', 'P3b_Dial_', 'Compass_Card', 'Compass_Face', 'FrontPanel_'))
            and ('Face' in name or not name.startswith('FrontPanel_'))) or (
        name.startswith('RearPanel_') and name.endswith('_Face')) or name in (
            'Cockpit_gauge_face', 'Gauge_face', 'Panel_gauge_faces')

def apply_glb(g, cream=False):
    """Style dedicated dial materials only; never process an airframe/avionics atlas."""
    import io
    changed = 0
    images = {}
    for mat in g.doc.get('materials', []):
        name = mat.get('name', '')
        if name in ('172_markings', '172_instrument_black'):
            if mat.get('extras', {}).get('instrumentFaceStyle') != 1:
                rgb = WHITE if name == '172_markings' else BLACK
                mat['pbrMetallicRoughness']['baseColorFactor'] = [v / 255 for v in rgb] + [1]
                mat.setdefault('extras', {})['instrumentFaceStyle'] = 1
                changed += 1
        if not is_face(name):
            continue
        if mat.get('extras', {}).get('instrumentFaceStyle') == 1:
            continue
        pbr = mat.get('pbrMetallicRoughness', {})
        texture = pbr.get('baseColorTexture', {}).get('index')
        if texture is not None:
            source = g.doc['textures'][texture]['source']
            if source not in images:
                im = g.image(texture, mode="RGBA")
                output = io.BytesIO(); finish(im, cream).save(output, format='PNG')
                # Clone the image/texture: other material users may be panel paint.
                g.doc['images'].append(dict(bufferView=g.data(output.getvalue()), mimeType='image/png'))
                tex = dict(g.doc['textures'][texture]); tex['source'] = len(g.doc['images']) - 1
                g.doc['textures'].append(tex); images[source] = len(g.doc['textures']) - 1
            pbr['baseColorTexture'] = dict(pbr['baseColorTexture'], index=images[source])
            pbr['baseColorFactor'] = [1, 1, 1, 1]
        else:
            pbr['baseColorFactor'] = [v / 255 for v in (CREAM if cream else BLACK)] + [1]
        mat.setdefault('extras', {})['instrumentFaceStyle'] = 1
        changed += 1
    # The 172 joins its static lettering into one interior mesh. Select its
    # dedicated white paint primitives, preserving every other interior part.
    for mesh in g.doc.get('meshes', []):
        for prim in mesh['primitives']:
            material = g.doc['materials'][prim['material']] if 'material' in prim else {}
            if material.get('name') != '172_markings' or prim.get('extras', {}).get('instrumentBoldStroke'):
                continue
            attrs = prim['attributes']; positions = g.array(attrs['POSITION'])
            ids = g.array(prim['indices']).reshape(-1) if 'indices' in prim else np.arange(len(positions))
            if not len(ids): continue
            triangles = positions[ids].reshape(-1,3,3)
            delta = triangles - triangles.mean(1)[:,None,:]
            length = np.linalg.norm(delta, axis=2, keepdims=True)
            expanded = triangles + delta / np.maximum(length, 1e-9) * .00012
            for semantic, accessor in list(attrs.items()):
                values = expanded.reshape(-1,3) if semantic == 'POSITION' else g.array(accessor)[ids]
                attrs[semantic] = g.accessor(values, g.doc['accessors'][accessor]['type'])
            prim['indices'] = g.accessor(np.arange(len(ids)).reshape(-1,1), 'SCALAR', 5125)
            prim.setdefault('extras', {})['instrumentBoldStroke'] = True
            changed += 1
    return changed

def write_runtime_palette(root):
    """The floating gauges consume the same paint as the offline face generator."""
    path = root/'src/FlyingGame.Core/InstrumentFaces.cs'
    source = path.read_text()
    start, end = '    // BEGIN GENERATED FACE PAINT', '    // END GENERATED FACE PAINT'
    lines = [start, '    // Generated by tools/own_models/instrument_face_style.py.']
    for name, rgb in [('BlackPaint', BLACK), ('WhitePaint', WHITE), ('CreamPaint', CREAM), ('InkPaint', INK)]:
        lines.append('    public const uint %s = 0x%02X%02X%02X;' % (name, *rgb))
    lines.append(end)
    block = '\n'.join(lines)
    if start in source:
        a, b = source.index(start), source.index(end) + len(end)
        source = source[:a] + block + source[b:]
    else:
        source = source.replace('public static class InstrumentFaces\n{', 'public static class InstrumentFaces\n{\n' + block)
    path.write_text(source)
