#!/usr/bin/env python3
"""Find freely-licensed reference photos on Wikipedia/Wikimedia Commons for a fleet type.

Owner's decision, 2026-10-25: "For the modeling of our own aircraft go to the Wikipedia page for
each type and find unlicensed photos you can use to judge your design against for accuracy."

A photo is the only reference that shows an aeroplane as it actually sits -- the fillet, the
canopy line, the gear rake, the stance. Drawings idealise and meshes are somebody's reading.
This finds them, records what licence each one carries, and leaves the files in
assets-incoming/ (gitignored) where every other reference lives: measured, compared, never
shipped. See docs/MODEL-PIPELINE.md.

  python3 tools/ref_photos.py --check                 verify every title/category resolves
  python3 tools/ref_photos.py <id> [<id> ...]         list candidates, write PHOTOS.md
  python3 tools/ref_photos.py <id> --download [N]     also fetch the top N (default 6)

Stdlib only, so it runs anywhere python3 does.
"""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

# The repo root from THIS FILE's location rather than a hardcoded path: realpath resolves the
# ~/Documents/flying-game symlink, so a run through either path lands on the real directory.
REPO = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
REF = os.path.join(REPO, "assets-incoming")

UA = "flying-game-reference/1.0 (https://github.com/tyfrisby/flying-game; measurement reference only)"

# config id -> (reference dir, Wikipedia article, Commons category).
# Several configs share one real aeroplane -- floats and bushwheel variants are the same airframe,
# so they share a reference directory rather than downloading the same photos three times.
TYPES = {
    "aircam-like":             ("aircam",        "Lockwood Aircam",                "Lockwood Aircam"),
    "aircam-amphib-like":      ("aircam",        "Lockwood Aircam",                "Lockwood Aircam"),
    "boeing-737-like":         ("b737",          "Boeing 737",                     "Boeing 737"),
    "c172-like":               ("c172",          "Cessna 172",                     "Cessna 172"),
    "cassutt-f1-like":         ("cassutt",       "Cassutt Special",                "Cassutt Racer"),
    "cirrus-sr22-like":        ("sr22",          "Cirrus SR22",                    "Cirrus SR22"),
    "dc3-like":                ("dc3",           "Douglas DC-3",                   "Douglas DC-3"),
    "decathlon-8kcab-like":    ("decathlon",     "American Champion Decathlon",    "American Champion Decathlon"),
    "dhc2-beaver-like":        ("dhc2-beaver",   "De Havilland Canada DHC-2 Beaver", "De Havilland Canada DHC-2 Beaver"),
    "dhc2-beaver-floats-like": ("dhc2-beaver",   "De Havilland Canada DHC-2 Beaver", "De Havilland Canada DHC-2 Beaver"),
    "extra-300-like":          ("extra300",      "Extra EA-300",                   "Extra 300"),
    "f86-sabre-like":          ("f86",           "North American F-86 Sabre",      "North American F-86 Sabre"),
    "geebee-r2-like":          ("geebee",        "Gee Bee Model R",                "Granville Gee Bee Model R Super Sportster"),
    "glasair3-like":           ("glasair3",      "Glasair III",                    "Stoddard-Hamilton Glasair"),
    "glider-2-33-like":        ("glider233",     "Schweizer SGS 2-33",             "Schweizer SGS 2-33"),
    "glider-eb29r-like":       ("eb29",          "Binder EB29",                    "Binder EB29"),
    "glider-swift-s1-like":    ("swift-s1",      "Swift S-1",                      "Swift S-1"),
    "hughes-h4-like":          ("h4",            "Hughes H-4 Hercules",            "Hughes H-4 Hercules"),
    "j3-cub-like":             ("j3-cub",        "Piper J-3 Cub",                  "Piper J-3 Cub"),
    "j3-cub-floats-like":      ("j3-cub",        "Piper J-3 Cub",                  "Piper J-3 Cub"),
    "jn4-jenny-like":          ("jn4",           "Curtiss JN-4",                   "Curtiss JN-4"),
    "p51d-like":               ("p51d",          "North American P-51 Mustang",    "North American P-51 Mustang"),
    "pa18-cub-like":           ("pa18",          "Piper PA-18 Super Cub",          "Piper PA-18 Super Cub"),
    "pa18-bush-like":          ("pa18",          "Piper PA-18 Super Cub",          "Piper PA-18 Super Cub"),
    "pa18-floats-like":        ("pa18",          "Piper PA-18 Super Cub",          "Piper PA-18 Super Cub"),
    "pa25-pawnee-like":        ("pa25",          "Piper PA-25 Pawnee",             "Piper PA-25 Pawnee"),
    "pa28-archer-like":        ("pa28",          "Piper PA-28 Cherokee",           "Piper PA-28 Cherokee"),
    "pitts-s2b-like":          ("pitts-s2",      "Pitts Special",                  "Pitts S-2 Special"),
    "seminole-like":           ("pa44",          "Piper PA-44 Seminole",           "Piper PA-44 Seminole"),
    "stearman-pt17-like":      ("stearman",      "Boeing-Stearman Model 75",       "Boeing-Stearman Model 75"),
    # target-drone-like has no real counterpart -- nothing to measure it against.
}

# A licence is acceptable only if its short name matches one of these. Everything else is
# REJECTED, including anything unrecognised: the default has to be "no", or one fair-use still
# frame slips in among forty free photos and nobody notices which.
FREE = ("public domain", "pd-", "cc0", "cc by", "cc-by", "attribution")
NOT_FREE = ("fair use", "non-free", "nonfree", "all rights")

# Icons, flags and maps ride along on every article. Skip by name, not by hoping.
JUNK = ("commons-logo", "wikidata-logo", "wiki-logo", "edit-ltr", "folder_", "symbol_",
        "flag_of", "flag of", "red_pog", "question_book", "ambox", "crystal",
        "gnome-", "nuvola", "loudspeaker", "sound-icon", "disambig")
PHOTO_EXT = (".jpg", ".jpeg", ".png", ".tif", ".tiff")

# Names that mean the picture answers a shape question rather than being a nice snapshot.
VIEW_WORDS = {
    "3-view": 60, "three-view": 60, "3 view": 60, "drawing": 25, "silhouette": 20,
    "side view": 30, "side-view": 30, "profile": 12, "front view": 25, "nose": 8,
    "planform": 25, "top view": 25, "cockpit": 10, "panel": 6, "cutaway": 20,
    "landing": 6, "takeoff": 4, "taxi": 8, "parked": 10, "static": 8, "ground": 4,
}


def api(host, params, tries=6):
    """One API call, with backoff. Wikimedia answers 429 to a tight loop -- a --check run over
    thirty types hit it in under a minute -- and it means slow down, not stop."""
    params = dict(params, format="json", formatversion="2")
    url = "https://%s/w/api.php?%s" % (host, urllib.parse.urlencode(params))
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    wait = 5.0
    for attempt in range(tries):
        try:
            with urllib.request.urlopen(req, timeout=40) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code not in (429, 503) or attempt == tries - 1:
                raise
            # Retry-After is authoritative when they send one.
            hdr = e.headers.get("Retry-After") if e.headers else None
            delay = float(hdr) if (hdr or "").isdigit() else wait
            print("   (%s %d, waiting %.0fs)" % (host.split(".")[0], e.code, delay),
                  file=sys.stderr)
            time.sleep(delay)
            wait = min(wait * 2, 120.0)
    raise RuntimeError("unreachable")


def licence_of(meta):
    """The licence short name, the author and whether we may use it."""
    def val(k):
        v = meta.get(k)
        if isinstance(v, dict):
            v = v.get("value")
        return (v or "").strip()
    lic = val("LicenseShortName") or val("License")
    artist = val("Artist")
    # Artist arrives as HTML; a link round the name is the common case.
    for tag in ("<", ">"):
        if tag in artist:
            import re
            artist = re.sub(r"<[^>]+>", "", artist)
            break
    artist = " ".join(artist.split())[:80]
    low = lic.lower()
    ok = any(f in low for f in FREE) and not any(n in low for n in NOT_FREE)
    return lic or "(none stated)", artist or "(not stated)", ok


def score(title, width, height):
    """Rank candidates: a three-view beats a photo, and a big photo beats a small one."""
    low = title.lower()
    s = 0
    for word, pts in VIEW_WORDS.items():
        if word in low:
            s += pts
    px = (width or 0) * (height or 0)
    s += min(px / 2.0e6, 12.0)          # pixels help, but cannot outweigh a drawing
    if (width or 0) < 800:
        s -= 30                          # too small to judge a fillet against
    return s


def gather(article, category):
    """Every image on the article plus every file in the Commons category, deduplicated."""
    titles = {}
    try:
        d = api("en.wikipedia.org", {"action": "query", "titles": article,
                                     "generator": "images", "gimlimit": "100",
                                     "redirects": "1"})
        for p in d.get("query", {}).get("pages", []):
            titles[p["title"]] = "article"
    except Exception as e:
        print("  ! wikipedia article %r: %s" % (article, e), file=sys.stderr)
    direct, subcats = category_members(category)
    for t in direct:
        titles.setdefault(t, "category")
    # Many type categories are containers: "Granville Gee Bee Model R Super Sportster" and
    # "Binder EB29" hold NO files at all, only "... by registration" and "... by location"
    # subcategories. One level down is where the photos are. Stop at one level -- two reaches
    # airshows and airports, which are full of other people's aeroplanes.
    if len(direct) < 40:
        for sc in subcats:
            more, _ = category_members(sc[len("Category:"):])
            for t in more:
                titles.setdefault(t, "subcategory")
            time.sleep(0.3)
    return titles


def category_members(category):
    """(files, subcategories) directly in a Commons category."""
    files, subs, cont = [], [], {}
    try:
        while True:
            d = api("commons.wikimedia.org", dict({
                "action": "query", "list": "categorymembers",
                "cmtitle": "Category:" + category, "cmtype": "file|subcat",
                "cmlimit": "500"}, **cont))
            for m in d.get("query", {}).get("categorymembers", []):
                (subs if m["title"].startswith("Category:") else files).append(m["title"])
            cont = d.get("continue") or {}
            if not cont:
                break
    except Exception as e:
        print("  ! commons category %r: %s" % (category, e), file=sys.stderr)
    return files, subs


def describe(titles, thumb_width):
    """imageinfo for each file, in batches of 50, filtered to free photos."""
    out = []
    names = [t for t in titles
             if t.lower().endswith(PHOTO_EXT) and not any(j in t.lower() for j in JUNK)]
    for i in range(0, len(names), 50):
        batch = names[i:i + 50]
        d = api("commons.wikimedia.org", {
            "action": "query", "titles": "|".join(batch), "prop": "imageinfo",
            "iiprop": "url|size|extmetadata", "iiurlwidth": str(thumb_width),
            "iiextmetadatafilter": "LicenseShortName|License|Artist|LicenseUrl|Credit"})
        for p in d.get("query", {}).get("pages", []):
            ii = (p.get("imageinfo") or [{}])[0]
            if not ii:
                continue
            lic, artist, ok = licence_of(ii.get("extmetadata", {}))
            if not ok:
                print("  skip (licence %s): %s" % (lic, p["title"]), file=sys.stderr)
                continue
            out.append({
                "title": p["title"],
                "w": ii.get("width"), "h": ii.get("height"),
                "url": ii.get("thumburl") or ii.get("url"),
                "page": ii.get("descriptionurl"),
                "licence": lic, "artist": artist,
                "source": titles[p["title"]],
                "score": score(p["title"], ii.get("width"), ii.get("height")),
            })
        time.sleep(0.2)
    out.sort(key=lambda c: -c["score"])
    return out


def safe_name(title, taken=None):
    """A filesystem name that keeps its extension and does not collide.

    Truncating the whole name cut the extension off and made four different Jenny photographs share one
    filename -- the second one found the first already there, reported "(have)", and three photographs were
    lost silently. Truncate the STEM, keep the suffix, and number a genuine clash."""
    stem = title[5:] if title.startswith("File:") else title
    root, dot, ext = stem.rpartition(".")
    if not dot:
        root, ext = stem, ""
    def clean(t):
        t = "".join(c if (c.isalnum() or c in "-._") else "-" for c in t)
        while "--" in t:
            t = t.replace("--", "-")
        return t.strip("-")
    name = clean(root)[:80] + ("." + clean(ext).lower() if ext else "")
    if taken is None:
        return name
    base, n = name, 2
    while name in taken:
        root2, dot2, ext2 = base.rpartition(".")
        name = "%s-%d.%s" % (root2, n, ext2) if dot2 else "%s-%d" % (base, n)
        n += 1
    taken.add(name)
    return name


def run(cid, n_download, thumb_width):
    if cid not in TYPES:
        print("no reference mapping for %r" % cid, file=sys.stderr)
        return 1
    slug, article, category = TYPES[cid]
    outdir = os.path.join(REF, "ref-" + slug, "photos")
    print("== %s  ->  assets-incoming/ref-%s/photos" % (cid, slug))
    print("   article  en.wikipedia.org/wiki/%s" % article.replace(" ", "_"))
    print("   category commons.wikimedia.org/wiki/Category:%s" % category.replace(" ", "_"))
    cands = describe(gather(article, category), thumb_width)
    if not cands:
        print("   nothing freely licensed found", file=sys.stderr)
        return 1
    print("   %d freely-licensed candidates" % len(cands))

    os.makedirs(outdir, exist_ok=True)
    lines = ["# Reference photos: %s" % cid, "",
             "Wikipedia: <https://en.wikipedia.org/wiki/%s>" % article.replace(" ", "_"),
             "Commons:   <https://commons.wikimedia.org/wiki/Category:%s>" % category.replace(" ", "_"),
             "",
             "Gathered %s by `tools/ref_photos.py`. Every file below is public domain or",
             "Creative Commons, taken as a **measurement and comparison reference only** --",
             "nothing here ships, and `assets-incoming/` is gitignored.",
             "", "| # | file | px | licence | author | Commons page |", "|---|---|---|---|---|---|"]
    lines[5] = lines[5] % time.strftime("%Y-%m-%d")

    taken = set()
    for i, c in enumerate(cands, 1):
        fn = safe_name(c["title"], taken)
        got = ""
        if i <= n_download:
            dest = os.path.join(outdir, fn)
            if os.path.exists(dest):
                got = " (have)"
            else:
                req = urllib.request.Request(c["url"], headers={"User-Agent": UA})
                with urllib.request.urlopen(req, timeout=60) as r, open(dest, "wb") as f:
                    f.write(r.read())
                got = " (%.1f MB)" % (os.path.getsize(dest) / 1e6)
            print("   %2d. %s%s" % (i, fn, got))
        lines.append("| %d | %s | %sx%s | %s | %s | <%s> |" % (
            i, ("`%s`" % fn) if i <= n_download else fn,
            c["w"], c["h"], c["licence"], c["artist"], c["page"]))

    man = os.path.join(outdir, "PHOTOS.md")
    with open(man, "w") as f:
        f.write("\n".join(lines) + "\n")
    print("   wrote %s" % man)
    return 0


def check():
    """Every article and category resolves, before anyone trusts the table above."""
    bad = 0
    seen = set()
    for cid, (slug, article, category) in sorted(TYPES.items()):
        key = (article, category)
        if key in seen:
            continue
        seen.add(key)
        a = api("en.wikipedia.org", {"action": "query", "titles": article, "redirects": "1"})
        apage = (a.get("query", {}).get("pages") or [{}])[0]
        amiss = apage.get("missing", False)
        files, subs = category_members(category)
        if not files and subs:
            for sc in subs[:4]:
                more, _ = category_members(sc[len("Category:"):])
                files = files + more
                time.sleep(0.3)
        flag = ""
        if amiss:
            flag += " ARTICLE MISSING"
        if not files:
            flag += " CATEGORY EMPTY/MISSING"
        if flag:
            bad += 1
        print("%-26s %-34s %-34s %3d files%s" % (
            slug, apage.get("title", article)[:33], category[:33], len(files), flag))
        time.sleep(1.5)
    print("\n%d mapping(s) need fixing" % bad)
    return 1 if bad else 0


FIELDS = ["length", "span", "upper span", "lower span", "height", "wing area",
          "gross weight", "empty weight", "max takeoff weight"]

# Articles that cover several marks carry one {{Aircraft specs}} per mark, and the first is not always ours.
# The value is a section heading that must appear just before the block we want.
SPEC_BLOCK = {"pitts-s2": "Pitts S-2B"}


def dimensions(article, prefer=None):
    """Span, length and height from the article's {{Aircraft specs}} template.

    The config cannot supply these. `fuselage.crossflow.lengthM` is the body's crossflow length, not the
    aeroplane's: a 172 reads 5.8 m there and is 8.28 m long. The template is the published figure with the
    article's own citation behind it."""
    d = api("en.wikipedia.org", {"action": "query", "prop": "revisions", "rvprop": "content",
                                 "rvslots": "main", "titles": article, "redirects": "1"})
    pages = d.get("query", {}).get("pages") or [{}]
    revs = pages[0].get("revisions")
    if not revs:
        return {}
    text = revs[0]["slots"]["main"]["content"]
    starts = [m.start() for m in re.finditer(r"\{\{\s*aircraft specs", text, re.I)]
    if not starts:
        return {}
    i = starts[0]
    if prefer:
        for st in starts:
            if prefer.lower() in text[max(0, st - 300):st].lower():
                i = st
                break
    # walk to the matching }} so a nested {{cite}} does not end it early
    depth, j = 0, i
    while j < len(text) - 1:
        if text[j:j+2] == "{{":
            depth += 1; j += 2; continue
        if text[j:j+2] == "}}":
            depth -= 1; j += 2
            if depth == 0:
                break
            continue
        j += 1
    body = text[i:j]
    out = {}
    # Pipes are often indented ("\n    |ref = ..."), so split on the newline-pipe pattern, not the literal.
    for line in re.split(r"\n\s*\|", body):
        if "=" not in line:
            continue
        key, _, val = line.partition("=")
        key = re.sub(r"<!--.*?-->", "", key, flags=re.S).strip().lower()
        val = re.sub(r"<!--.*?-->", "", val, flags=re.S).split("<ref")[0].strip().strip("|").strip()
        for f in FIELDS:
            if key.startswith(f) and val and not key.endswith("note"):
                out[key] = val
    return out


def dims_report():
    """One DIMENSIONS.md per reference directory, plus a fleet table on stdout."""
    rows = []
    done = set()
    for cid, (slug, article, _cat) in sorted(TYPES.items()):
        if slug in done:
            continue
        done.add(slug)
        fields = dimensions(article, SPEC_BLOCK.get(slug))
        m = {}
        for k, v in fields.items():
            for f in ("length", "span", "upper span", "height"):
                if k.startswith(f + " m") and k[len(f)+1:].strip() in ("m", "m)"):
                    m[f] = v
        # prefer the explicit metre field; fall back to ft/in
        def metres(f, _alt=None):
            if f not in m and _alt and _alt in m:
                f = _alt
            if f in m:
                try:
                    return float(m[f].split()[0].replace(",", "."))
                except ValueError:
                    return None
            # The template writes inches as mixed fractions: "2+1/2", "7+3/4", "10+1/2".
            def num(t):
                if not t:
                    return None
                tot = 0.0
                for part in str(t).replace("&nbsp;", " ").split("+"):
                    part = part.strip()
                    if not part:
                        continue
                    try:
                        if "/" in part:
                            a, b = part.split("/", 1)
                            tot += float(a) / float(b)
                        else:
                            tot += float(part.replace(",", "."))
                    except (ValueError, ZeroDivisionError):
                        return None
                return tot
            ft, inch = num(fields.get(f + " ft")), num(fields.get(f + " in"))
            if ft is None:
                return None
            return ft * 0.3048 + (inch or 0.0) * 0.0254
        sp = metres("span", "upper span")
        if sp is None:                      # biplanes: the template writes "upper span ft/in"
            fields.setdefault("span ft", fields.get("upper span ft"))
            fields.setdefault("span in", fields.get("upper span in"))
            sp = metres("span")
        ln, ht = metres("length"), metres("height")
        rows.append((slug, article, sp, ln, ht, fields))
        out = os.path.join(REF, "ref-" + slug, "DIMENSIONS.md")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(out, "w") as f:
            f.write("# Published dimensions: %s\n\n" % slug)
            f.write("From the {{Aircraft specs}} template of <https://en.wikipedia.org/wiki/%s>, read %s.\n"
                    % (article.replace(" ", "_"), time.strftime("%Y-%m-%d")))
            f.write("The article carries its own citations; this is a convenience copy, not a source.\n\n")
            f.write("| field | value |\n|---|---|\n")
            for k in sorted(fields):
                f.write("| %s | %s |\n" % (k, fields[k]))
            f.write("\nDerived: span %s m, length %s m, height %s m\n" % (sp, ln, ht))
        print("%-14s span=%-8s length=%-8s height=%-8s  %s" % (
            slug,
            "%.3f" % sp if sp else "-", "%.3f" % ln if ln else "-", "%.3f" % ht if ht else "-",
            "" if (sp and ln) else "INCOMPLETE"))
        time.sleep(1.0)
    return 0


def main(argv):
    if "--check" in argv:
        return check()
    if "--dims" in argv:
        return dims_report()
    ids, n, width = [], 0, 2000
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--download":
            n = 6
            if i + 1 < len(argv) and argv[i + 1].isdigit():
                n = int(argv[i + 1]); i += 1
        elif a == "--max-width":
            width = int(argv[i + 1]); i += 1
        elif a == "--all":
            ids = sorted(TYPES)
        else:
            ids.append(a)
        i += 1
    if not ids:
        print(__doc__)
        return 2
    rc = 0
    done = set()
    for cid in ids:
        slug = TYPES.get(cid, (None,))[0]
        if slug in done:
            print("== %s shares ref-%s, already done" % (cid, slug))
            continue
        done.add(slug)
        rc |= run(cid, n, width)
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
