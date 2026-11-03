"""Island terrain palette from the satellite reference (tools/island/topo/palette.json, made by imagery.py).

Each material colour in FlyingGame/IslandTerrain is the MEDIAN SURFACE REFLECTANCE of one land-cover class in
the real places (Sentinel-2 L2A, atmospherically corrected), encoded as sRGB because the project renders in
gamma space and the shader's lighting is ~1.0 in full sun, so an albedo of 1.0 is white paper. Where a class
appears in several areas the areas that the island material stands for are averaged in linear reflectance.
Output: the float3 constants pasted into the shader, with the classes and areas they came from.
Run: python3 -I tools/island/topo/shader_palette.py   (standard library only)
"""
import json, os

P = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'palette.json')))['areas']


def srgb(x):
    x = max(0.0, min(1.0, x))
    return 12.92 * x if x <= .0031308 else 1.055 * x ** (1 / 2.4) - .055


# shader constant -> (class, areas it stands for)
USE = {
    'conifer_wet':   ('conifer_forest', ['sitka', 'fairweather']),
    'conifer_dry':   ('conifer_forest', ['johnsoncreek', 'knik']),
    'broadleaf':     ('broadleaf_tropical_veg', ['dillingham']),
    'meadow':        ('grass_meadow', ['sitka', 'johnsoncreek', 'knik']),
    'dry_grass':     ('dry_grass_shrub', ['grandcanyon', 'dillingham']),
    'heath':         ('grass_meadow', ['fairweather']),
    'rock_crystalline': ('bare_rock', ['fairweather', 'knik']),
    'rock_light':    ('bare_rock', ['grandcanyon']),
    'red_rock':      ('red_rock', ['grandcanyon']),
    'gravel_bars':   ('sediment_gravel', ['knik', 'fairweather']),
    'sand':          ('sand', ['dillingham']),   # dedicated shoreline sample (imagery.py SAND_SAMPLES)
    'snow':          ('snow', ['fairweather', 'knik']),
    'glacier_ice':   ('glacier_ice', ['fairweather', 'knik']),
    'glacial_water': ('water', ['fairweather']),
}


def main():
    for name, (cls, areas) in USE.items():
        refl, n = [0.0, 0.0, 0.0], 0
        for a in areas:
            c = P[a]['classes'].get(cls)
            if not c or (c.get('frac', 1) < .002 and cls != 'sand'):
                continue
            refl = [r + v for r, v in zip(refl, c['refl_p50'])]; n += 1
        if not n:
            print(f'{name:18s} (no data)'); continue
        refl = [r / n for r in refl]
        s = [srgb(r) for r in refl]
        print(f'{name:18s} float3({s[0]:.2f}, {s[1]:.2f}, {s[2]:.2f})   reflectance {refl[0]:.3f} {refl[1]:.3f} {refl[2]:.3f}   {cls} @ {",".join(areas)}')


if __name__ == '__main__':
    main()
