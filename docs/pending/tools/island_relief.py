#!/usr/bin/env python3
"""Standard-library reader for src/FlyingGame.Core/IslandRelief.Generated.cs (the generated natural relief).
Same decode and the same Catmull-Rom arithmetic, in the same order, as FlyingGame.Core.IslandRelief, so the
authoring evaluator and the compiled one agree to rounding. The C# file is the only copy of the data."""
import base64, re, struct
from pathlib import Path
SOURCE = Path(__file__).resolve().parents[3] / 'src/FlyingGame.Core/IslandRelief.Generated.cs'
SEA_BED = -30.0

def _decode():
    text = SOURCE.read_text()
    body = text[text.index('Base64 ='):]
    data = base64.b64decode(''.join(re.findall(r'"([A-Za-z0-9+/=]*)"', body)))
    o = 0
    def v():
        nonlocal o
        r = s = 0
        while True:
            b = data[o]; o += 1; r |= (b & 0x7f) << s
            if b < 0x80: return r
            s += 7
    def d():
        nonlocal o
        x = struct.unpack_from('<d', data, o)[0]; o += 8; return x
    assert data[:4] == b'IRL1'; o = 4
    out = []
    for _ in range(v()):
        e0, n0, cell = d(), d(), d(); w, h = v(), v(); size = w * h; q = [0] * size
        for j in range(h):
            for i in range(w):
                k = j * w + i
                pred = q[k-1] + q[k-w] - q[k-w-1] if i and j else q[k-1] if i else q[k-w] if j else 0
                z = v(); q[k] = pred + ((z >> 1) ^ -(z & 1))
        # float32 storage, exactly as the runtime holds it
        hf = list(struct.unpack('<%df' % size, struct.pack('<%df' % size, *[x * .05 - 30 for x in q])))
        for _ in range(4):
            k = 0
            while k < size:
                o += 1; run = v(); k += run
        out.append(dict(e0=e0, n0=n0, cell=cell, inv=1 / cell, w=w, h=h, height=hf))
    return out

LATTICES = _decode()

def _weights(t):
    t2 = t * t; t3 = t2 * t
    return (.5 * (-t3 + 2 * t2 - t), .5 * (3 * t3 - 5 * t2 + 2), .5 * (-3 * t3 + 4 * t2 + t), .5 * (t3 - t2))

def height(e, n):
    for l in LATTICES:
        x = (e - l['e0']) * l['inv']; y = (n - l['n0']) * l['inv']
        if x < 0 or y < 0 or x > l['w'] - 1 or y > l['h'] - 1: continue
        w, hh, H = l['w'], l['h'], l['height']
        i = min(int(x), w - 2); j = min(int(y), hh - 2); tx = x - i; ty = y - j
        a0, a1, a2, a3 = _weights(tx); b0, b1, b2, b3 = _weights(ty)
        i0 = i - 1 if i > 0 else 0; i3 = i + 2 if i + 2 < w else w - 1
        j0 = j - 1 if j > 0 else 0; j3 = j + 2 if j + 2 < hh else hh - 1
        def row(r):
            o = r * w
            return H[o + i0] * a0 + H[o + i] * a1 + H[o + i + 1] * a2 + H[o + i3] * a3
        return row(j0) * b0 + row(j) * b1 + row(j + 1) * b2 + row(j3) * b3
    return SEA_BED
