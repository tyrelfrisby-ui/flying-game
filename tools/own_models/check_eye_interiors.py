"""Is each seat's eye in a cockpit, or inside the aeroplane's skin? Engine-free, from the committed GLBs and configs.

Quest 3S, 2026-11-02: the 2-33 and the J-3 opened with the eye "inside geometry", although both have approved eye
points (docs/models/eyepoints.json, carried into configs/aircraft/*.json loading.seats[].eye). This answers, per
aircraft and seat, for the model the game actually DRAWS:

  * our own GLB, placed the way AirframeModels.Place places it (sights.placement): how much of the head's
    surroundings is skin (rays from the eye that hit non-glass geometry within 0.12 m), and how much of the sky
    is blocked within 3 m (rays above the horizon; 100 % from inside a closed body);
  * the procedural airframe (AirframeVisual.StyleFor), a CLOSED body of revolution with no cockpit cut into it: is
    the eye inside the tube? Profiles are copied by hand from AirframeVisual.cs below (a test that read the file
    it checks would prove nothing).

For the J-3 (Ty 2026-11-02: "get the eye location fixed on the j3 and add it to quest too both on wheels and
floats") it also answers, for wheels AND floats:

  * is the eye in the CABIN, not merely clear of it: rays to each side, glass counted, must meet the cabin's side
    (wall or window) within 0.5 m, the floor must be under it within 1.5 m and the roof over it;
  * what the pilot sees: the first solid (non-glass) thing straight ahead, 10 deg down over the nose, out to each
    side and straight up, and the head room to the roof;
  * that the floats airframe sits where the landplane's does under the CG (AirframeModels' sitHighM), so the one
    approved eye means the same place in both cabins;
  * how the DRAWN floats line up with the PHYSICS floats (configs floats block and its float-keel contact points).
    Reported, not gated: that geometry belongs to the flight model.

Run with a Python that has NumPy (Blender's will do):
  /Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 -I tools/own_models/check_eye_interiors.py
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    import PIL  # noqa: F401  (glb_visuals imports it for textures, which this check never reads)
except ImportError:
    import types
    sys.modules['PIL'] = types.ModuleType('PIL'); sys.modules['PIL'].Image = None
import numpy as np
from glb_visuals import Glb
from sights import parts, placement, ray_hits, MODELS, ROOT

GLASS = ('canopy', 'glass', 'windshield', 'windscreen', 'skylight', 'window', 'sight_', 'decal', 'yawstring')

# AirframeVisual.StyleFor, copied by hand 2026-11-02: (BodyAxisZ, [(x, radius)...]) in the config datum frame,
# +x forward, +z down. Only the types the question is about.
PROCEDURAL = {
    'glider-2-33-like': (0.15, [(2.2, .05), (1.9, .25), (1.2, .45), (.3, .48), (-.8, .38), (-2.0, .24), (-3.5, .14), (-5.0, .09), (-5.7, .06)]),
    'j3-cub-like': (-0.25, [(1.95, .09), (1.65, .34), (1.0, .45), (.2, .47), (-.8, .42), (-1.8, .30), (-3.1, .19), (-4.4, .11), (-4.95, .075)]),
}


def sphere(n=400):
    i = np.arange(n) + .5
    phi = np.arccos(1 - 2 * i / n); th = math.pi * (1 + 5 ** .5) * i
    return np.column_stack((np.cos(th) * np.sin(phi), np.sin(th) * np.sin(phi), np.cos(phi)))


def cone(axis, half_deg, n=300):
    axis = axis / np.linalg.norm(axis)
    d = sphere(4000)
    return d[d @ axis >= math.cos(math.radians(half_deg))][:n]


def own_report(aircraft, glb_name, seats):
    g = Glb(MODELS / aircraft / glb_name)
    m, tr = placement(g, aircraft)
    tris = []
    for name, p, a, ids in parts(g):
        if any(k in name.lower() for k in GLASS): continue
        b = a @ m.T + tr
        tris.append(b[ids])
    tris = np.concatenate(tris)
    out = []
    for s in seats:
        eye = np.array(s['eye'], dtype=float)
        # AirframeModels.Place sits the model in the aeroplane's root frame, which is at the CG, using the config's
        # datum numbers (gear depth, outer-wing station) as they stand: the procedural parts are shifted by -CG
        # (AirframeVisual.Build), the model is not. The eye points were measured on that placement, so the eye is
        # used as it is. (Adding the CG put the Great Lakes rear eye 7 cm under its own turtle deck.)
        e = eye
        near = sum(1 for d in sphere() if (ray_hits(tris, e, d) < .12).any()) / 400
        # Can the pilot see out? Rays into the upper hemisphere (above the horizon), glass excluded: from inside a
        # closed body every one of them is blocked; from a cockpit most reach the sky.
        sky = [d for d in sphere() if d[2] < -0.05]
        blocked = sum(1 for d in sky if (ray_hits(tris, e, d) < 3.0).any())
        total = len(sky)
        up = ray_hits(tris, e, np.array([0, 0, -1.]))
        out.append(dict(seat=s['id'], eye=eye.tolist(), skin_within_12cm=round(near, 3),
                        sky_blocked=round(blocked / total, 3),
                        overhead_m=round(float(up.min()), 3) if len(up) else None))
    return out


def solid_tris(g, aircraft, glass=False):
    m, tr = placement(g, aircraft)
    out = []
    for name, p, a, ids in parts(g):
        if not glass and any(k in name.lower() for k in GLASS): continue
        out.append((a @ m.T + tr)[ids])
    return np.concatenate(out)


def first_hit(tris, e, d):
    d = np.asarray(d, dtype=float); d = d / np.linalg.norm(d)
    h = ray_hits(tris, e, d)
    return round(float(h.min()), 3) if len(h) else None


# Body axes: +x forward, +y right, +z down.
VIEWS = {'ahead': (1, 0, 0), 'over_nose_10_down': (math.cos(math.radians(10)), 0, math.sin(math.radians(10))),
         'left': (0, -1, 0), 'right': (0, 1, 0), 'up': (0, 0, -1), 'down': (0, 0, 1)}


def cabin_report(aircraft, glb_name, seats):
    """Is each eye inside the cabin (glass counted), and what does it see out of it (glass not counted)?"""
    g = Glb(MODELS / aircraft / glb_name)
    shell, solid = solid_tris(g, aircraft, glass=True), solid_tris(g, aircraft)
    out = []
    for s in seats:
        e = np.array(s['eye'], dtype=float)
        walls = {k: first_hit(shell, e, VIEWS[k]) for k in ('left', 'right', 'up', 'down')}
        inside = all(walls[k] is not None and walls[k] < 0.5 for k in ('left', 'right')) \
            and walls['up'] is not None and walls['down'] is not None and walls['down'] < 1.5
        out.append(dict(seat=s['id'], inside_cabin=inside, shell=walls,
                        sees={k: first_hit(solid, e, d) for k, d in VIEWS.items()}))
    return out


def airframe_offset(wheels, floats):
    """Where each GLB's origin (the config datum, the same airframe in both) lands under the CG, as placed."""
    (mw, tw), (mf, tf) = (placement(Glb(MODELS / a / f'{a}-own.glb'), a) for a in (wheels, floats))
    return float(np.abs(tf - tw).max()), float(abs(mf[0, 0] - mw[0, 0])), tw, tf


def floats_report(aircraft):
    """The drawn floats (Float_L / Float_R, as placed) against the physics floats block and its keel contact points."""
    g = Glb(MODELS / aircraft / f'{aircraft}-own.glb')
    m, tr = placement(g, aircraft)
    cfg = json.loads((ROOT / 'configs/aircraft' / f'{aircraft}.json').read_text())
    f, cg = cfg['floats'], np.array(cfg['mass']['cg'], dtype=float)
    side = {}
    for name, p, a, ids in parts(g):
        if name in ('Float_L', 'Float_R'): side.setdefault(name, []).append((a @ m.T + tr)[ids])
    rep = {}
    for name, tl in side.items():
        t = np.concatenate(tl); v = t.reshape(-1, 3)
        yc = float((v[:, 1].min() + v[:, 1].max()) / 2)
        def keel(x):
            h = ray_hits(t, np.array([x, yc, 4.0]), np.array([0, 0, -1.]))
            return round(4.0 - float(h.min()), 3) if len(h) else None   # the first hit from below is the keel
        keels = [(round(k['pos'][0] - cg[0], 3), round(k['pos'][2] - cg[2], 3), keel(k['pos'][0] - cg[0]))
                 for k in cfg['gear'] if k.get('gearType') == 'float-keel' and (k['pos'][1] < 0) == (name == 'Float_L')]
        rep[name] = dict(centre_y=round(yc, 3), bow_x=round(float(v[:, 0].max()), 3), transom_x=round(float(v[:, 0].min()), 3),
                         beam=round(float(np.ptp(v[:, 1])), 3), deepest_z=round(float(v[:, 2].max()), 3),
                         keel_at_contact_points=[dict(x=x, physics_z=pz, drawn_z=dz) for x, pz, dz in keels])
    phys = dict(centre_y=f['spreadM'] / 2, bow_x=round(f['bowX'] - cg[0], 3), transom_x=round(f['bowX'] - f['lengthM'] - cg[0], 3),
                beam=f['beamM'], keel_z=f['keelZ'], step_x=round(f['bowX'] - f['stepFraction'] * f['lengthM'] - cg[0], 3))
    return dict(drawn=rep, physics=phys)


def procedural_report(aircraft, seats):
    axis, prof = PROCEDURAL[aircraft]
    cfg = json.loads((ROOT / 'configs/aircraft' / f'{aircraft}.json').read_text())
    cg = np.array(cfg['mass']['cg'], dtype=float)
    xs = [p[0] for p in prof][::-1]; rs = [p[1] for p in prof][::-1]
    out = []
    for s in seats:
        e = np.array(s['eye'], dtype=float) + cg
        r = float(np.interp(e[0], xs, rs)); d = math.hypot(e[1], e[2] - axis)
        out.append(dict(seat=s['id'], radius_m=round(r, 3), eye_from_axis_m=round(d, 3), inside_closed_tube=d < r))
    return out


def main():
    cases = [('pitts-s2b-like', 'pitts-s2b-like-own-checker-unmarked.glb'),
             ('extra-300-like', 'extra-300-like-own-unmarked.glb'),
             ('great-lakes-like', 'great-lakes-like-own.glb'),
             ('glider-2-33-like', 'glider-2-33-like-own-tf.glb'),
             ('glider-2-33-like', 'glider-2-33-like-own.glb'),
             ('j3-cub-like', 'j3-cub-like-own.glb'),
             ('j3-cub-floats-like', 'j3-cub-floats-like-own.glb')]
    report = {}
    for aircraft, glb in cases:
        seats = json.loads((ROOT / 'configs/aircraft' / f'{aircraft}.json').read_text())['loading']['seats']
        report[glb] = own_report(aircraft, glb, seats)
        for r in report[glb]:
            print(f'{glb:42s} {r["seat"]:5s} skin within 12 cm {r["skin_within_12cm"]:5.1%}  sky blocked {r["sky_blocked"]:5.1%}  overhead {r["overhead_m"]}')
    for aircraft in PROCEDURAL:
        seats = json.loads((ROOT / 'configs/aircraft' / f'{aircraft}.json').read_text())['loading']['seats']
        report['procedural ' + aircraft] = procedural_report(aircraft, seats)
        for r in report['procedural ' + aircraft]:
            print(f'procedural {aircraft:31s} {r["seat"]:5s} tube radius {r["radius_m"]:.3f} m, eye {r["eye_from_axis_m"]:.3f} m from its axis -> {"INSIDE the closed tube" if r["inside_closed_tube"] else "outside"}')
    bad = []
    # The J-3: in the cabin, what it sees, the two airframes coincide, and the floats against the physics.
    for aircraft in ('j3-cub-like', 'j3-cub-floats-like'):
        seats = json.loads((ROOT / 'configs/aircraft' / f'{aircraft}.json').read_text())['loading']['seats']
        report['cabin ' + aircraft] = cabin_report(aircraft, f'{aircraft}-own.glb', seats)
        for r in report['cabin ' + aircraft]:
            print(f'cabin {aircraft:28s} {r["seat"]:5s} {"IN the cabin" if r["inside_cabin"] else "NOT inside the cabin"}; '
                  f'walls (glass counted) {r["shell"]}; first solid thing seen {r["sees"]}')
            if r['seat'] == 'rear' and not r['inside_cabin']: bad.append((aircraft, 'rear not inside the cabin'))
    d, ds, tw, tf = airframe_offset('j3-cub-like', 'j3-cub-floats-like')
    print(f'J-3 airframe under the CG: wheels datum at {np.round(tw, 4)}, floats at {np.round(tf, 4)} -> '
          f'{"COINCIDE" if d < 0.001 and ds < 1e-6 else "DIFFER"} ({d * 1000:.1f} mm, scale diff {ds:.2e})')
    if d >= 0.001 or ds >= 1e-6: bad.append(('j3-cub-floats-like', f'airframe {d * 1000:.1f} mm off the landplane: update sitHighM'))
    fl = floats_report('j3-cub-floats-like'); report['floats j3-cub-floats-like'] = fl
    ph = fl['physics']
    print(f'J-3 floats, physics: centre y +-{ph["centre_y"]:.3f}, bow x {ph["bow_x"]:+.3f}, step x {ph["step_x"]:+.3f}, '
          f'transom x {ph["transom_x"]:+.3f}, beam {ph["beam"]:.3f}, keel at the step z {ph["keel_z"]:.3f}')
    for name, r in fl['drawn'].items():
        print(f'J-3 floats, drawn {name}: centre y {r["centre_y"]:+.3f}, bow x {r["bow_x"]:+.3f}, transom x {r["transom_x"]:+.3f}, '
              f'beam {r["beam"]:.3f}, deepest z {r["deepest_z"]:.3f}; keel at the physics contact points '
              + ', '.join(f'x {k["x"]:+.2f}: physics {k["physics_z"]:.3f} drawn {k["drawn_z"]}' for k in r['keel_at_contact_points']))
    off_y = max(abs(abs(r['centre_y']) - ph['centre_y']) for r in fl['drawn'].values())
    print(f'J-3 FLOATS vs PHYSICS: {"MISMATCH" if off_y > 0.05 else "lined up"} -- each drawn float is {off_y:.3f} m '
          f'inboard of the physics float (reported, not gated: the float block is the flight model\'s)')
    if '--json' in sys.argv: print(json.dumps(report, indent=1, default=str))
    # The gate: every type the Quest lists (FlyingGame.Core.Vr.QuestFleet.Listed), drawn by the GLB the game places
    # for it, has nothing within 12 cm of the eye of every seat the headset offers (QuestFleet.SeatsOnly: the J-3 is
    # flown from the rear only, its front eye has a cabin tube 9 cm away). Keep this list in step with QuestFleet.
    quest = {'pitts-s2b-like-own-checker-unmarked.glb': None, 'extra-300-like-own-unmarked.glb': None,
             'great-lakes-like-own.glb': None, 'glider-2-33-like-own-tf.glb': None,
             'j3-cub-like-own.glb': ['rear'], 'j3-cub-floats-like-own.glb': ['rear']}
    bad += [(g, r['seat']) for g, only in quest.items() for r in report[g]
            if (only is None or r['seat'] in only) and r['skin_within_12cm'] > 0]
    print('QUEST LIST: ' + ('PASS, every offered eye clear of the skin' if not bad else f'FAIL {bad}'))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
