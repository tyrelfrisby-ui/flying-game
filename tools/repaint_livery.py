"""Repaint a downloaded model's baseColor textures into a livery, in Blender.

    blender --background --python tools/repaint_livery.py

WHY BLENDER. There is no image library on this Mac — no PIL, no ImageMagick — so until Blender went on
(2026-10-21) the model textures could not be touched at all, and the Talk Flying markings had to float as
separate 3-D quads over the skin. Blender's bpy gives full pixel access, which is all this needs.

WHAT IT DOES, and the one idea worth keeping: it REPLACES A HUE WHILE PRESERVING LUMINANCE. The downloaded
texture has shading, panel shadow and dirt baked into it; a flat colour fill would erase all of that and the
aeroplane would go plastic. So each matching pixel keeps its own brightness and only its colour changes —
the sweep stays a sweep, with its highlights and its grime.

Re-runnable: it reads the ORIGINALS from the backup beside it, so running twice does not compound.
"""
import bpy, os, shutil

# The repo root from THIS FILE's location rather than a hardcoded path: realpath resolves the
# ~/Documents/flying-game symlink, so a run through either path lands on the real directory.
REPO = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
MODELS = os.path.join(REPO, "unity/Assets/_Project/Resources/Models")
# Until 2026-10-23 this was a hardcoded path into one session's scratchpad, so it pointed at a directory
# that no longer existed and a run would have died on the seed copy below. It is cached in build/ now.
# Losing the cache is survivable: these textures are tracked in git, so the stock ones come back with
# `git checkout <commit-before-the-repaint> -- unity/Assets/_Project/Resources/Models`.
ORIG = os.path.join(REPO, "build/tex-orig")

# Talk Flying: the mark is monochrome, so the aeroplane is too — graphite where the stock blue was, and the
# whites pushed brighter, which is the "more white" the owner asked for.
BRAND = (0.337, 0.353, 0.384)      # #565A62 graphite, a touch darker than the logo's shadow so it reads as trim
WHITEN = 0.45                       # how far the neutral greys move toward white


def is_blue(r, g, b):
    return b > 0.25 and b > r + 0.12 and b > g + 0.08


def repaint(model, files):
    for fn in files:
        src = os.path.join(ORIG, fn)
        dst = os.path.join(MODELS, model, fn)
        if not os.path.exists(src):                 # first run: seed the backup from the live file
            # SAY SO. Seeding is only correct while the live file is still the STOCK texture; do it when the
            # live file is already repainted and the "original" becomes a repaint, so re-running compounds and
            # the re-runnable promise in the docstring quietly stops holding.
            os.makedirs(ORIG, exist_ok=True)
            print("repaint   seeding backup from the LIVE %s/%s -- correct only if it is still stock" % (model, fn))
            shutil.copy(dst, src)
        img = bpy.data.images.load(src)
        px = list(img.pixels)
        n = len(px) // 4
        changed = 0
        for i in range(n):
            j = i * 4
            r, g, b = px[j], px[j + 1], px[j + 2]
            if is_blue(r, g, b):
                # keep this pixel's own brightness; change only its colour
                lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
                k = min(1.6, lum / 0.30)            # 0.30 ~ the stock navy's luminance
                px[j], px[j + 1], px[j + 2] = (min(1.0, BRAND[0] * k), min(1.0, BRAND[1] * k), min(1.0, BRAND[2] * k))
                changed += 1
            else:
                # neutral? push it toward white. Saturated non-blue (red trim, placards) is left alone.
                mx, mn = max(r, g, b), min(r, g, b)
                if mx - mn < 0.08 and mx > 0.35:
                    px[j] = r + (1.0 - r) * WHITEN
                    px[j + 1] = g + (1.0 - g) * WHITEN
                    px[j + 2] = b + (1.0 - b) * WHITEN
        img.pixels = px
        img.filepath_raw = dst
        img.file_format = 'JPEG'
        img.save()
        print("REPAINT %-40s %d of %d px recoloured" % (fn, changed, n))
        bpy.data.images.remove(img)


repaint("c172-like", ["fuselage.007_baseColor.jpg", "Material.008_baseColor.jpg"])
