#!/usr/bin/env python3
"""Standard-library reader for src/FlyingGame.Core/IslandRelief.Generated.cs (the generated natural relief).
Same decode and the same Catmull-Rom arithmetic, in the same order, as FlyingGame.Core.IslandRelief, so the
authoring evaluator and the compiled one agree to rounding. The C# file is the only copy of the data.
Format IRL2 adds four surface channels; the glacier-surface detail (ice undulation, crevasses, seracs,
boulders: IslandRelief.Surfaces.cs) is mirrored here term for term."""
import base64, math, re, struct
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
    assert data[:4] in (b'IRL1', b'IRL2'); surfaces = data[:4] == b'IRL2'; o = 4
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
        chans = []
        for _ in range(8 if surfaces else 4):
            c = bytearray(size); k = 0
            while k < size:
                val = data[o]; o += 1; run = v(); c[k:k + run] = bytes([val]) * run; k += run
            chans.append(c)
        while len(chans) < 8: chans.append(bytearray(size))
        ice, debris, _wet, _ela, snow, rough, crev, _ledge = chans
        flag = [rough[k] > 0 or crev[k] > 0 or (debris[k] > 0 and ice[k] > 0) for k in range(size)]
        active = bytearray(size)
        for j in range(h):
            for i in range(w):
                any_ = False
                for b in (-1, 0, 1):
                    jj = j + b
                    if jj < 0 or jj >= h: continue
                    for a in (-1, 0, 1):
                        ii = i + a
                        if 0 <= ii < w and flag[jj * w + ii]: any_ = True; break
                    if any_: break
                active[j * w + i] = 1 if any_ else 0
        out.append(dict(e0=e0, n0=n0, cell=cell, inv=1 / cell, w=w, h=h, height=hf, ice=ice, debris=debris,
                        rough=rough, crev=crev, active=active))
    return out

LATTICES = _decode()

def _weights(t):
    t2 = t * t; t3 = t2 * t
    return (.5 * (-t3 + 2 * t2 - t), .5 * (3 * t3 - 5 * t2 + 2), .5 * (-3 * t3 + 4 * t2 + t), .5 * (t3 - t2))

def _dweights(t):
    t2 = t * t
    return (.5 * (-3 * t2 + 4 * t - 1), .5 * (9 * t2 - 10 * t), .5 * (-9 * t2 + 8 * t + 1), .5 * (3 * t2 - 2 * t))

def height(e, n):
    for l in LATTICES:
        x = (e - l['e0']) * l['inv']; y = (n - l['n0']) * l['inv']
        if x < 0 or y < 0 or x > l['w'] - 1 or y > l['h'] - 1: continue
        w, hh, H = l['w'], l['h'], l['height']
        i = min(int(x), w - 2); j = min(int(y), hh - 2); tx = x - i; ty = y - j
        a0, a1, a2, a3 = _weights(tx); b0, b1, b2, b3 = _weights(ty)
        da0, da1, da2, da3 = _dweights(tx); db0, db1, db2, db3 = _dweights(ty)
        i0 = i - 1 if i > 0 else 0; i3 = i + 2 if i + 2 < w else w - 1
        j0 = j - 1 if j > 0 else 0; j3 = j + 2 if j + 2 < hh else hh - 1
        def row(r):
            o = r * w; p0, p1, p2, p3 = H[o + i0], H[o + i], H[o + i + 1], H[o + i3]
            return p0 * a0 + p1 * a1 + p2 * a2 + p3 * a3, p0 * da0 + p1 * da1 + p2 * da2 + p3 * da3
        (r0, x0), (r1, x1), (r2, x2), (r3, x3) = row(j0), row(j), row(j + 1), row(j3)
        gx = x0 * b0 + x1 * b1 + x2 * b2 + x3 * b3
        gy = r0 * db0 + r1 * db1 + r2 * db2 + r3 * db3
        hb = r0 * b0 + r1 * b1 + r2 * b2 + r3 * b3
        return hb + _detail(l, x, y, hb, gx, gy)
    return SEA_BED

# ---- glacier surface detail: IslandRelief.Surfaces.cs, same constants and order -----------------------------
UNDULATION_M, UNDULATION_CELL, PATCH_CELL, FLOOR = 1.5, 55.0, 320.0, .2
FALL_M, WARP_CELL, WARP, DEPTH_M, SERAC_M, SERAC_CELL = 24.0, 45.0, .5, 22.0, 3.0, 15.0
HALF0, HALFK = .2, .15
BOULDER_CELL, BOULDER_CHANCE, ONSET = 100.0, .35, .15
SEED_UND, SEED_PATCH, SEED_WARP, SEED_SERAC, SEED_BOULDER = 0x51a7, 0x9e37, 0x2c1b, 0x77f1, 0x4b0d
M32 = 0xffffffff

def _u(x, y, seed):
    h = ((x & M32) * 0x8da6b343 & M32) ^ ((y & M32) * 0xd8163841 & M32) ^ ((seed & M32) * 0xcb1ab31f & M32)
    h ^= h >> 13; h = h * 0x5bd1e995 & M32; h ^= h >> 15
    return (h & 0xffffff) / 16777215.0

def _value(em, nm, cell, seed):
    X = em / cell; Y = nm / cell; fx0 = math.floor(X); fy0 = math.floor(Y)
    ix = int(fx0); iy = int(fy0); fx = X - fx0; fy = Y - fy0
    u = fx * fx * fx * (fx * (fx * 6 - 15) + 10); v = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
    a = 2 * _u(ix, iy, seed) - 1; b = 2 * _u(ix + 1, iy, seed) - 1; c = 2 * _u(ix, iy + 1, seed) - 1; d = 2 * _u(ix + 1, iy + 1, seed) - 1
    k = a - b - c + d
    return a + (b - a) * u + (c - a) * v + k * u * v

def _ch(ch, k, w, sx, sy):
    c00 = ch[k] / 255.0; c10 = ch[k + 1] / 255.0; c01 = ch[k + w] / 255.0; c11 = ch[k + w + 1] / 255.0
    a = c00 + (c10 - c00) * sx; b = c01 + (c11 - c01) * sx
    return a + (b - a) * sy

def _step(t):
    t = 0.0 if t < 0 else 1.0 if t > 1 else t
    return t * t * (3 - 2 * t)

def _boulder_in(l, bi, bj):
    s = SEED_BOULDER
    r = 5 + 7 * _u(bi, bj, s)
    margin = r + 2; span = BOULDER_CELL - 2 * margin
    ce = bi * BOULDER_CELL + margin + span * _u(bi, bj, s + 1)
    cn = bj * BOULDER_CELL + margin + span * _u(bi, bj, s + 2)
    ax = _u(bi, bj, s + 3) - .5; ay = _u(bi, bj, s + 4) - .5; al = math.sqrt(ax * ax + ay * ay)
    ca = ax / al if al > 1e-6 else 1.0; sa = ay / al if al > 1e-6 else 0.0
    q = .65 + .35 * _u(bi, bj, s + 5)
    hb = (.55 + .25 * _u(bi, bj, s + 6)) * r
    x = (ce / 1000 - l['e0']) * l['inv']; y = (cn / 1000 - l['n0']) * l['inv']
    if x < 0 or y < 0 or x > l['w'] - 1 or y > l['h'] - 1: return None
    w = l['w']; i = min(w - 2, int(x)); j = min(l['h'] - 2, int(y)); tx = x - i; ty = y - j; k = j * w + i
    def B(c): return ((c[k] * (1 - tx) + c[k + 1] * tx) * (1 - ty) + (c[k + w] * (1 - tx) + c[k + w + 1] * tx) * ty) / 255.0
    debris = B(l['debris'])
    if debris <= .35: return None
    ice = B(l['ice']); rough = B(l['rough']); crev = B(l['crev'])
    if rough <= .15 or crev >= .3: return None
    chance = BOULDER_CHANCE * _step((debris - .35) / .3) * _step((ice - .5) / .3)
    if not _u(bi, bj, s + 7) < chance: return None
    return ce, cn, r, ca, sa, q, hb

def _detail(l, x, y, h, gx, gy):
    w = l['w']; i = int(x); j = int(y)
    if i > w - 2: i = w - 2
    if j > l['h'] - 2: j = l['h'] - 2
    k = j * w + i; act = l['active']
    if not (act[k] or act[k + 1] or act[k + w] or act[k + w + 1]): return 0.0
    tx = x - i; ty = y - j
    sx = tx * tx * (3 - 2 * tx); sy = ty * ty * (3 - 2 * ty)
    em = (l['e0'] + x * l['cell']) * 1000; nm = (l['n0'] + y * l['cell']) * 1000
    v = 0.0
    r = _ch(l['rough'], k, w, sx, sy)
    if r > 0:
        n1 = _value(em, nm, UNDULATION_CELL, SEED_UND); n2 = _value(em, nm, PATCH_CELL, SEED_PATCH)
        t = (n2 + .25) / .5
        s = 1.0 if t >= 1 else t * t * (3 - 2 * t) if t > 0 else 0.0
        mod = FLOOR + (1 - FLOOR) * s
        v += UNDULATION_M * r * mod * n1
    c = _ch(l['crev'], k, w, sx, sy)
    if c > ONSET:
        ct = (c - ONSET) / (.5 - ONSET)
        c = 1.0 if ct >= 1 else ct * ct * (3 - 2 * ct)
        wv = _value(em, nm, WARP_CELL, SEED_WARP)
        phi = h / FALL_M + WARP * wv
        f = phi - math.floor(phi) - .5; half = HALF0 + HALFK * c; tt = f / half
        if -1 < tt < 1: v -= DEPTH_M * c * (1 - tt * tt)
        v += SERAC_M * c * _value(em, nm, SERAC_CELL, SEED_SERAC)
    bi = int(math.floor(em / BOULDER_CELL)); bj = int(math.floor(nm / BOULDER_CELL))
    bd = _boulder_in(l, bi, bj)
    if bd:
        ce, cn, rr, ca, sa, q, hb = bd
        ex = em - ce; ny = nm - cn
        if ex * ex + ny * ny < rr * rr:
            du = ex * ca + ny * sa; dv = -ex * sa + ny * ca; r2 = rr * rr; qr2 = q * q * r2
            rho2 = du * du / r2 + dv * dv / qr2
            if rho2 < 1: v += hb * (1 - rho2 * rho2)
    return v
