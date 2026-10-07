#!/usr/bin/env python3
"""Lesson regimen report: reads the per-aircraft CSVs written by LessonRegimenTests.FlyTheWholeSyllabus and flags the
anomalies (owner 2026-10-06: "put the aircraft through a test regimen overnight, test every lesson in the syllabus").

usage: report.py <dir-of-csvs> > report.md
"""
import csv, glob, os, sys
from collections import defaultdict

LANDING = {"LandingRudder", "LandingAileron", "Flare", "FlareSideView", "ApproachSideView"}
BRAKES = {"Flare", "FlareSideView"}                     # the user's wheel brakes (idle-power landings)
ENDLESS = {"STurns", "StallSideView", "StallRudder", "StallElevator", "Straight"}
CRASH = {"hit the ground", "airframe failed", "off the side"}


def f(r, k):
    try:
        return float(r[k])
    except (ValueError, KeyError):
        return float("nan")


def flags(r):
    out = []
    k, p, end = r["lesson"], r["pilot"], r["end"]
    if r["phase"] == "Exception": return [f"EXCEPTION {r['error']}"]
    if r["nan"] == "True": out.append("NaN state")
    if p == "ace":
        if k not in ENDLESS and r["phase"] != "Finished": out.append(f"did not finish ({f(r, 'sim_s'):.0f} s)")
        if end in CRASH: out.append(f"ace: {end}")
        if r["wings_failed"] == "True": out.append("wings failed")
        if int(r["lost"] or 0) > 0: out.append(f"lost {r['lost']} components")
        if k in LANDING:
            if r["touched"] != "True": out.append("no touchdown")
            else:
                if f(r, "td_sink_ms") > 2.0: out.append(f"hard touchdown {f(r, 'td_sink_ms'):.1f} m/s")
                if int(r["bounces"] or 0) > 0: out.append(f"{r['bounces']} bounce(s)")
                if int(r["nose_first"] or 0) > 0: out.append("nose wheel first")
                if f(r, "max_swing_deg") > 10: out.append(f"roll-out swing {f(r, 'max_swing_deg'):.0f}°")
                fl = f(r, "float_m")
                if fl == fl and fl > 460: out.append(f"long float {fl / 0.3048:.0f} ft from 50 ft")
            if end == "departure end": out.append("ran off the departure end")
            if k in BRAKES and r["braking"] and r["braking"] != "Green": out.append(f"braking {r['braking']} (avg {f(r, 'avg_decel_g'):.2f} g, peak {f(r, 'peak_decel_g'):.2f} g, tail {r['tail_lifted']})")
        if k not in ENDLESS and r["phase"] == "Finished" and f(r, "score") < 70 and end not in CRASH: out.append(f"low score {f(r, 'score'):.0f}")
    if p == "handsoff" and k in LANDING and f(r, "score") > 60: out.append(f"hands-off scored {f(r, 'score'):.0f} — judge too lenient? ({r['verdict']})")
    return out


def main():
    d = sys.argv[1]
    rows = []
    for path in sorted(glob.glob(os.path.join(d, "*.csv"))):
        with open(path) as fh: rows += list(csv.DictReader(fh))
    if not rows: print("no rows"); return
    by_ac = defaultdict(list)
    for r in rows: by_ac[r["aircraft"]].append(r)
    print(f"# Lesson regimen — {len(rows)} runs, {len(by_ac)} aircraft\n")
    print("| aircraft | runs | ace flagged | ace crashes | ace mean score | student crashes | hands-off > 60 |")
    print("|---|---|---|---|---|---|---|")
    for ac, rs in by_ac.items():
        ace = [r for r in rs if r["pilot"] == "ace"]
        sc = [f(r, "score") for r in ace if r["lesson"] not in ENDLESS and r["phase"] == "Finished"]
        print(f"| {ac} | {len(rs)} | {sum(1 for r in ace if flags(r))} | {sum(1 for r in ace if r['end'] in CRASH)} | "
              f"{(sum(sc) / len(sc)) if sc else 0:.0f} | {sum(1 for r in rs if r['pilot'] == 'student' and r['end'] in CRASH)} | "
              f"{sum(1 for r in rs if r['pilot'] == 'handsoff' and r['lesson'] in LANDING and f(r, 'score') > 60)} |")
    # Flags grouped by (lesson, flag kind) so a systemic problem shows as one line with many aircraft.
    print("\n## Flags by lesson\n")
    grouped = defaultdict(list)
    for r in rows:
        for fl in flags(r):
            key = fl.split(" ")[0] if not fl.startswith("ace:") else fl
            grouped[(r["lesson"], r["pilot"], key)].append((r, fl))
    for (lesson, pilot, key), items in sorted(grouped.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        acs = sorted({r["aircraft"] for r, _ in items})
        print(f"- **{lesson}** / {pilot} / `{key}` ×{len(items)} — {', '.join(acs)}")
        for r, fl in items[:6]:
            print(f"    - {r['aircraft']} {r['wind']} flaps {r['flaps']}: {fl}")
    # Float distance table (ace, calm Flare) against expectations.
    print("\n## Flare (calm, ace): 50 ft → touchdown, touchdown speed, roll-out\n")
    print("| aircraft | flaps | 50 ft kt | td kt | air ft | roll ft | braking | score |")
    print("|---|---|---|---|---|---|---|---|")
    for r in rows:
        if r["pilot"] == "ace" and r["lesson"] == "Flare" and r["wind"] == "Calm":
            fl, ro = f(r, "float_m"), f(r, "roll_m")
            print(f"| {r['aircraft']} | {r['flaps']} | {f(r, 'v50_kt'):.0f} | {f(r, 'td_kt'):.0f} | {fl / 0.3048:.0f} | {ro / 0.3048:.0f} | {r['braking']} | {f(r, 'score'):.0f} |")


if __name__ == "__main__":
    main()
