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
    "aircam-like":             ("aircam",        "Lockwood Aircraft",              "Lockwood Aircam"),
    "aircam-amphib-like":      ("aircam",        "Lockwood Aircraft",              "Lockwood Aircam"),
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


def safe_name(title):
    stem = title[5:] if title.startswith("File:") else title
    keep = "".join(c if (c.isalnum() or c in "-._") else "-" for c in stem)
    while "--" in keep:
        keep = keep.replace("--", "-")
    return keep.strip("-")[:90]


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

    for i, c in enumerate(cands, 1):
        fn = safe_name(c["title"])
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


def main(argv):
    if "--check" in argv:
        return check()
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
