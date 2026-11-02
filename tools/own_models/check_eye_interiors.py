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
             ('j3-cub-like', 'j3-cub-like-own.glb')]
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
    if '--json' in sys.argv: print(json.dumps(report, indent=1))
    # The gate: every type the Quest lists (FlyingGame.Bridge.SessionSettings.HeadsetFleet), drawn by the GLB the
    # game places for it, has nothing within 12 cm of either eye. Keep this list in step with HeadsetFleet.
    quest = ['pitts-s2b-like-own-checker-unmarked.glb', 'extra-300-like-own-unmarked.glb', 'great-lakes-like-own.glb',
             'glider-2-33-like-own-tf.glb']
    bad = [(g, r['seat']) for g in quest for r in report[g] if r['skin_within_12cm'] > 0]
    print('QUEST LIST: ' + ('PASS, every eye clear of the skin' if not bad else f'FAIL {bad}'))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
