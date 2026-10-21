#!/usr/bin/env python3
"""Average the measured cross-sections of several reference models into one table of stations.

    python3 tools/average_sections.py <true-span> <model> [<model> ...]

THIS IS THE POINT OF THE WHOLE EXERCISE. Averaging the MESHES would be a derivative of every source at once --
infringing each instead of one -- and would mush the sharp features that make an aeroplane read as itself.
Averaging the MEASUREMENTS converges on the real aeroplane, because the shape of a real Cessna 172 is a fact
about a machine Cessna built, not any modeller's expression. Three people measuring the same aircraft get the
same sections; the places they DISAGREE are where one of them took a liberty, and the average rejects it.

Each reference is normalised by its own length before averaging, so a model drawn 2 % long does not smear the
cabin. The spread between references is printed for every station: a small spread means the three agree and the
number is trustworthy; a large one means they do not, and that station deserves a look.
"""
import subprocess, sys, os, statistics

BLENDER = "/opt/homebrew/bin/blender"
MEASURE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "measure_model.py")
N = 18


def measure(path, span):
    out = subprocess.run([BLENDER, "--background", "--python", MEASURE, "--", path, str(N), str(span)],
                         capture_output=True, text=True).stdout
    rows, length = [], None
    for line in out.splitlines():
        if line.startswith("MEASURE scale="):
            length = float(line.split("length")[1].split()[0])
        # the per-station table: "MEASURE  s(m)  frac  halfwidth  bottom  top"
        parts = line.split()
        if len(parts) == 6 and parts[0] == "MEASURE":
            try:
                rows.append(tuple(float(x) for x in parts[1:]))
            except ValueError:
                pass
    return length, rows


def main():
    span = float(sys.argv[1])
    paths = sys.argv[2:]
    tables, lengths = [], []
    for p in paths:
        L, rows = measure(p, span)
        if not rows:
            print("  (no sections from %s)" % os.path.basename(p)); continue
        # key on the PRINTED precision (3 dp): the measure script formats frac with %5.3f, so a
        # 4-dp key here matches only the fractions that happen to be exact (2026-10-21: 3 of 19).
        tables.append({round(r[1], 3): r[2] for r in rows})       # frac -> half-width
        lengths.append(L)
        print("  %-46s length %.3f m, %d sections" % (os.path.basename(p)[:46], L, len(rows)))
    if not tables:
        return 1
    L = statistics.mean(lengths)
    print("\n  averaged over %d references, mean length %.3f m\n" % (len(tables), L))
    print("  %6s %8s %9s %9s  %s" % ("frac", "s(m)", "median", "spread", "per-reference"))
    out = []
    for i in range(N + 1):
        f = round(i / N, 3)
        vals = [t[f] for t in tables if f in t]
        if len(vals) < 2:
            continue
        # MEDIAN, not mean. With three references one bad slice drags a mean badly: at station 0.46 m two
        # references said 0.404 and 0.335 and the third said 0.097 (its cut fell in the gap between spinner and
        # cowl), and the mean reported 0.279 -- a number none of them measured. The median returns 0.335 and
        # the outlier is simply ignored. The disagreements cluster where other structure sits near the
        # centreline: the spinner and nose gear forward, the tailplane root aft.
        mean = statistics.median(vals)
        spread = max(vals) - min(vals)
        flag = "  <-- references disagree" if spread > 0.12 else ""
        print("  %6.3f %8.2f %9.3f %9.3f  %s%s" % (f, f * L, mean, spread,
              " ".join("%.3f" % v for v in vals), flag))
        out.append((f * L, mean, spread))
    print("\n# paste into the model script: (station from the nose, half-width)")
    print("MEASURED = [")
    for s, w, sp in out:
        print("    (%.2f, %.3f),%s" % (s, w, "   # references disagree by %.2f" % sp if sp > 0.12 else ""))
    print("]")
    return 0


sys.exit(main())
