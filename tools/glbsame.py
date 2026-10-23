"""Is this built livery actually stale, or did the build just drift?

RAW sha1 IS NOT A VALID STALENESS TEST ACROSS BUILD GROUPINGS, and believing it was cost a wrong answer:
the Stearman was reported stale on a hash a later seven-model run reproduced exactly, all forty digits.
Two builds of the same aeroplane at the same commit, grouped differently, differ in two places only:

  - Mark.* vertex positions, by under a micron (9.5e-07 m on the 172).
  - a handful of texels at the edge of a painted decal -- 9 channel samples of 50,331,648 on a 4096x4096
    texture. A boundary ray that hit on one run and missed on the next.

The trigger is the BUILD GROUPING, not the clock: one id per blender process reproduces a final exactly,
and naming seven ids in one process does not. It is deterministic per grouping, so it is leakage between
models in one process rather than anything threaded -- see main() in talkflying_livery.py. A hash
comparison across two different groupings therefore reports "stale" for a file that is nothing of the kind,
which is a mistake this tool exists to stop repeating.

Neither difference is visible and neither is a change. So compare what a real change would move:

    materials (names, factors, alphaMode)   mesh names and vertex counts
    image dimensions and content, by sample  mark geometry, to a tolerance

usage: python3 -I tools/glbsame.py A.glb B.glb [pos_tol_m] [img_tol_frac]
exit 0 = same aeroplane, 1 = genuinely different
"""
import json, struct, sys, math, zlib

def load(p):
    with open(p, "rb") as f:
        struct.unpack("<III", f.read(12))
        ln, k = struct.unpack("<II", f.read(8))
        js = f.read(ln)
        bl, bk = struct.unpack("<II", f.read(8))
        return json.loads(js), f.read(bl)

def view(g, b, vi):
    v = g["bufferViews"][vi]
    o = v.get("byteOffset", 0)
    return b[o:o + v["byteLength"]]

def png_raw(d):
    i, idat, hdr = 8, [], None
    while i < len(d):
        ln, typ = struct.unpack_from(">I4s", d, i); i += 8
        body = d[i:i+ln]; i += ln + 4
        if typ == b"IHDR": hdr = struct.unpack(">IIBBBBB", body)
        elif typ == b"IDAT": idat.append(body)
        elif typ == b"IEND": break
    w, h, depth, ctype, _, _, inter = hdr
    nch = {0:1, 2:3, 3:1, 4:2, 6:4}[ctype]
    if depth != 8 or inter: return (w, h, nch, None)
    raw = zlib.decompress(b"".join(idat)); stride = w * nch
    out = bytearray(h * stride); prev = bytearray(stride); pos = 0
    for y in range(h):
        f = raw[pos]; pos += 1
        line = bytearray(raw[pos:pos+stride]); pos += stride
        if f == 1:
            for x in range(nch, stride): line[x] = (line[x] + line[x-nch]) & 255
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                a = line[x-nch] if x >= nch else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x-nch] if x >= nch else 0
                bb = prev[x]; c = prev[x-nch] if x >= nch else 0
                p = a + bb - c
                pa, pb, pc = abs(p-a), abs(p-bb), abs(p-c)
                pr = a if (pa <= pb and pa <= pc) else (bb if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        out[y*stride:(y+1)*stride] = line; prev = line
    return (w, h, nch, bytes(out))

def positions(g, b, ai):
    a = g["accessors"][ai]
    v = g["bufferViews"][a["bufferView"]]
    off = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    st = v.get("byteStride") or 12
    return [struct.unpack_from("<fff", b, off + i*st) for i in range(a["count"])]

A, BA = load(sys.argv[1]); B, BB = load(sys.argv[2])
ptol = float(sys.argv[3]) if len(sys.argv) > 3 else 1e-4      # 0.1 mm
itol = float(sys.argv[4]) if len(sys.argv) > 4 else 1e-4      # 0.01% of samples
bad = []

# ---- materials -------------------------------------------------------------------------------------
def mats(g):
    return {m.get("name"): json.dumps({k: v for k, v in m.items() if k != "name"}, sort_keys=True)
            for m in g.get("materials", [])}
ma, mb = mats(A), mats(B)
if set(ma) != set(mb):
    bad.append("material names differ: only in A %s / only in B %s"
               % (sorted(set(ma)-set(mb)), sorted(set(mb)-set(ma))))
for k in sorted(set(ma) & set(mb)):
    if ma[k] != mb[k]:
        bad.append("material %s changed\n      A %s\n      B %s" % (k, ma[k], mb[k]))

# ---- meshes ---------------------------------------------------------------------------------------
def meshes(g):
    out = {}
    for m in g["meshes"]:
        n = 0
        for pr in m["primitives"]:
            ai = pr.get("attributes", {}).get("POSITION")
            if ai is not None: n += g["accessors"][ai]["count"]
        out[m.get("name","?")] = n
    return out
sa, sb = meshes(A), meshes(B)
if set(sa) != set(sb):
    bad.append("mesh names differ: only in A %s / only in B %s"
               % (sorted(set(sa)-set(sb))[:8], sorted(set(sb)-set(sa))[:8]))
for k in sorted(set(sa) & set(sb)):
    if sa[k] != sb[k]:
        bad.append("mesh %s vertex count %d -> %d" % (k, sa[k], sb[k]))

# ---- geometry, to a tolerance ---------------------------------------------------------------------
print("%-22s %10s %10s" % ("mesh", "maxdev(m)", "verts"))
worst, worstname = 0.0, "-"
pa = {m.get("name","?"): m["primitives"][0]["attributes"].get("POSITION")
      for m in A["meshes"] if m["primitives"]}
pb = {m.get("name","?"): m["primitives"][0]["attributes"].get("POSITION")
      for m in B["meshes"] if m["primitives"]}
shown = 0
for k in sorted(set(pa) & set(pb)):
    if pa[k] is None or pb[k] is None: continue
    va, vb = positions(A, BA, pa[k]), positions(B, BB, pb[k])
    if len(va) != len(vb): continue
    dev = max((math.dist(p, q) for p, q in zip(va, vb)), default=0.0)
    if dev > worst: worst, worstname = dev, k
    if dev > 0 or k.startswith("Mark."):
        if shown < 14:
            print("%-22s %10.2e %10d%s" % (k, dev, len(va), "   OVER TOL" if dev > ptol else ""))
            shown += 1
    if dev > ptol:
        bad.append("%s geometry moved %.3e m (tol %.0e)" % (k, dev, ptol))
print("worst: %s at %.2e m" % (worstname, worst))

# ---- images ---------------------------------------------------------------------------------------
print("\n%-5s %-14s %-12s %s" % ("img", "size", "dims", "difference"))
if len(A.get("images", [])) != len(B.get("images", [])):
    bad.append("image count %d -> %d" % (len(A.get("images",[])), len(B.get("images",[]))))
for i, (ia, ib) in enumerate(zip(A.get("images", []), B.get("images", []))):
    da, db = view(A, BA, ia["bufferView"]), view(B, BB, ib["bufferView"])
    if da == db:
        continue
    wa, ha, ca, ra = png_raw(da)
    wb, hb, cb, rb = png_raw(db)
    if (wa, ha, ca) != (wb, hb, cb):
        bad.append("image %d dimensions %sx%sx%s -> %sx%sx%s" % (i, wa, ha, ca, wb, hb, cb))
        print("%-5d %-14s %-12s DIMENSIONS DIFFER" % (i, len(da), "%dx%d" % (wa, ha)))
        continue
    if ra is None or rb is None:
        bad.append("image %d: cannot decode to compare (depth/interlace)" % i)
        continue
    nd = sum(1 for x, y in zip(ra, rb) if x != y)
    mx = max((abs(x-y) for x, y in zip(ra, rb) if x != y), default=0)
    frac = nd / float(len(ra))
    over = frac > itol
    print("%-5d %-14s %-12s %d of %d samples (%.6f%%), worst delta %d/255%s"
          % (i, "%d/%d" % (len(da), len(db)), "%dx%d" % (wa, ha), nd, len(ra), 100*frac, mx,
             "   OVER TOL" if over else ""))
    if over:
        bad.append("image %d differs in %.4f%% of samples (tol %.4f%%)" % (i, 100*frac, 100*itol))

print()
if bad:
    print("DIFFERENT -- a real change:")
    for x in bad: print("  - " + x)
    sys.exit(1)
print("SAME AEROPLANE: differs only by build drift (marks < %.0e m, texels < %.4f%% of samples)"
      % (ptol, 100*itol))
