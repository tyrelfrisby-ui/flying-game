"""Extra 300 rear (PIC) panel top restored after P3b. CPU only, idempotent.

P3b (vr_phase3b.py, 3a9e746b) retired every old panel primitive in Cockpit_Shell, and with them the rear
seat's upper panel hood and its darker glareshield cap ('Cockpit_panel' rear part, 'Cockpit_dash_top'). Ty
2026-11-02: the top of the rear panel "was a good representation and is now gone". This puts the same
hood back: the original cross-section (620 mm wide sides rising to a 400 mm flat top at 0.428 m, 200 mm
deep, panel colour, dark cap over the top and the slants), re-seated on the P3b one-plane panel so its
underside is the P3b panel's top edge (0.360 m) and its aft face sits 3 mm proud of the gauge plane. Nothing
is moved and no gauge, bezel or card is covered: the top-row bezels stop at 0.343 m.
Builder frame: X forward, Y left, Z up (scene glTF = (-x, z, y)).
"""
import numpy as np
from vr_cockpit import Glb, Author, Shape, gl
from drop_card_slot import validate

BOTTOM, TOP = .360, .428          # P3b rear panel top edge; the original hood top
HALF_TOP = .200                   # original flat top half-width
SLOPE = (.310-.200)/(.528-.438)   # original slant: half-width lost per metre of rise
AFT_X, FWD_X = -1.178, -1.002     # builder X of the aft face (3 mm aft of the P3b panel at -1.175) and the front
CAP = .002                        # glareshield cap thickness above the hood

def outline(bottom=BOTTOM, top=TOP):
    half_bottom = HALF_TOP + SLOPE*(top-bottom)
    return [(-half_bottom, bottom), (half_bottom, bottom), (HALF_TOP, top), (-HALF_TOP, top)]

def update(path):
    if path.parent.name != 'extra-300-like': return
    g = Glb(path)
    if g.doc.get('extras', {}).get('vrCockpitPhase3d'): return
    names = {n['name'] for n in g.doc['nodes']}
    assert 'Panel_P3b_R' in names, path
    a = Author(g)
    panel = g.material('P3d_RearPanelTop', (.03433981, .03820436, .04666508))
    cap = g.material('P3d_Glareshield', (.02028856, .02028856, .02121901))
    for i in (panel, cap): g.doc['materials'][i]['doubleSided'] = True
    x, th = (AFT_X+FWD_X)/2, FWD_X-AFT_X
    sh = Shape(); sh.prism(outline(), x, th)
    a.node('RearPanel_Top_P3d', (x, 0, (BOTTOM+TOP)/2), shape=sh, mat=panel)
    # Cap: the two slants and the flat top, CAP thick, on the hood's outer faces.
    (y0, z0), (y1, z1), (y2, z2), (y3, z3) = outline()
    c = Shape()
    for (ya, za), (yb, zb) in [((y1, z1), (y2, z2)), ((y2, z2), (y3, z3)), ((y3, z3), (y0, z0))]:
        if za == zb == BOTTOM: continue
        d = np.array((yb-ya, zb-za)); nrm = np.array((d[1], -d[0]))/np.linalg.norm(d)
        if nrm[1] < 0: nrm = -nrm
        q = [(ya, za), (yb, zb), (yb+nrm[0]*CAP, zb+nrm[1]*CAP), (ya+nrm[0]*CAP, za+nrm[1]*CAP)]
        c.prism(q, x, th)
    a.node('RearPanel_Glareshield_P3d', (x, 0, TOP), shape=c, mat=cap)
    g.doc['extras']['vrCockpitPhase3d'] = dict(
        restored=['RearPanel_Top_P3d', 'RearPanel_Glareshield_P3d'],
        source='original Cockpit_panel rear hood + Cockpit_dash_top (55954abb), re-seated on Panel_P3b_R',
        bottom_m=BOTTOM, top_m=TOP, aft_builder_x=AFT_X, fwd_builder_x=FWD_X)
    validate(g, path); g.save(path); print('EXTRA', path.name, 'rear panel top restored')

if __name__ == '__main__':
    from vr_cockpit import MODELS
    for p in sorted((MODELS/'extra-300-like').glob('*own*.glb')): update(p)
