"""Run XFOIL 6.99 (Drela, MIT; built headless from web.mit.edu/drela/Public/web/xfoil/xfoil6.99.tgz) on the Extra's two
candidate wings (extra_sections.py: A = Eppler E473 root / E472 tip unscaled, B = the lane owner's "ice cream cone"
reshape of the same), one case per strip station per candidate, and keep everything it says in
tools/aircraft_builder/data/xfoil/, so the table can be rebuilt without XFOIL (extra_wing_table.py reads only these files).
lane/claude-inertia run 5d, 2026-11-02. Settings, identical for A and B, all stated in
docs/aircraft/extra-300-airfoil-DERIVATION.md:
  section   extra_sections.coords_w(w, cand), 160 cosine panels a surface (also written to data/extra300-sections/)
  paneling  PPAR N 240 (tier 1; see RETRIES)
  viscous   VISC <Re>, MACH 0.083, ITER 300 (tier 1)
  transition free, e^N with NCRIT 9 (a smooth composite wing in calm air).
  Re        c x 1.937e6: the station's chord at the 1-g stall, 55 KIAS at 820 kg (sec. 5.4, ACRO I) = 28.29 m/s EAS at sea
            level, nu 1.4607e-5 m2/s (ISA).
  UP        one XFOIL process: alpha 0 -> 26 deg in 0.25 deg steps, each point started from the previous one's boundary
            layer, with the boundary-layer DUMP (x, y, Ue, Dstar, Theta, Cf) taken ON that sweep at each DUMP_ALPHAS
            (so the dumps are the polar's own solutions), summarised into <case>.sep.txt; raw dumps stay in XFOIL_WORK.
  DOWN      a second, separate process: 0 -> 26 deg unrecorded, then 26 -> 0 recorded, both in the tier's step
            recorded (a different solution path; where up and down disagree XFOIL itself is hysteretic).
  TIMEOUT   every XFOIL process is killed after its tier's limit; a hung XFOIL cannot block the other cases (they run in
            a process pool) and its polar so far is kept (XFOIL appends each converged point as it goes).
  RETRIES   a sweep is accepted when it ran to the end and its converged points leave no gap wider than 1.0 deg in 0..26.
            Otherwise it is re-run at the next tier: 2 = PPAR N 300, 0.10 deg steps, ITER 500; 3 = PPAR N 360, 0.05 deg,
            ITER 800. The best attempt is kept and its tier recorded. Then, so A and B stay on identical settings, the
            other candidate at the same station is re-run at the higher tier too.
  log       data/xfoil/xfoil-ab-runlog.json: per case and sweep the tier, whether it timed out, the alphas that did not
            converge.
The NACA 0012 Ladson validation polars (run 4, M 0.15) are kept and not re-run here.
Usage: XFOIL=/path/to/xfoil python3 extra_xfoil.py [--jobs N] [case ...]   (default: every A_ and B_ case)
"""
import json, os, shutil, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import extra_sections as S  # noqa: E402

OUT = os.path.join(HERE, 'data', 'xfoil')
XFOIL = os.environ.get('XFOIL', 'xfoil')
WORK = os.environ.get('XFOIL_WORK', '/private/tmp/extra_xfoil_work')
RUNLOG = os.path.join(OUT, 'xfoil-ab-runlog.json')

STATION_Y = S.STATION_Y
VS_MS, NU = 55 * 0.514444, 1.4607e-5
MACH, NCRIT, XTR = 0.083, 9, 1.0
AMAX = 26.0
DUMP_ALPHAS = [8, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26]
TIERS = {1: dict(npan=240, step=0.25, iters=300, timeout=900),
         2: dict(npan=300, step=0.10, iters=500, timeout=1800),
         3: dict(npan=360, step=0.05, iters=800, timeout=2700)}


def station(y):
    c, t, w = S.station(y)
    return c, t, w, round(c * VS_MS / NU, -3)


# name -> spec: section ('A'|'B' blended at w, or 'naca0012' = XFOIL's own NACA 0012 generator), Re, Mach, NCRIT, XTR.
def spec(section, re, w=None, mach=MACH, ncrit=NCRIT, xtr=XTR):
    return dict(section=section, w=w, re=re, mach=mach, ncrit=ncrit, xtr=xtr)


CASES = {}
for cand in S.CANDIDATES:
    for i, y in enumerate(STATION_Y):
        c, t, w, re = station(y)
        CASES[f'{cand}_st{i + 1}_y{y:.3f}'] = spec(cand, re, w)
# The Extra's B stations at the NCRIT the run-5e correction uses (derivation sec. 1e).
NCRIT_FLIGHT = 9
# VALIDATION: Ladson, NASA TM 4074 (1988), NACA 0012, M 0.15. Free transition (Table I) at Ladson's Reynolds numbers and
# NCRIT 9 / 6 / 4 / 2 (the tunnel's turbulence was unknown and elevated, TM 4074 p. 1); transition fixed at 0.05 c on
# both surfaces (Table IX, No. 80 grit) as XTR 0.05.
LADSON_FREE_RE = [2.00e6, 3.94e6, 5.97e6, 8.86e6, 11.90e6]
LADSON_TRIP_RE = [2.00e6, 3.99e6, 5.95e6]
VALIDATION = {}
for re in LADSON_FREE_RE:
    for n in (9, 6, 4, 2):
        VALIDATION[f'v0012_re{re / 1e6:.2f}_m15_n{n}'] = spec('naca0012', re, mach=0.15, ncrit=n)
for re in LADSON_TRIP_RE:
    VALIDATION[f'v0012_re{re / 1e6:.2f}_m15_x05'] = spec('naca0012', re, mach=0.15, ncrit=9, xtr=0.05)
CASES.update(VALIDATION)


def _xfoil(cmd, cwd, timeout):
    t0 = time.time()
    try:
        r = subprocess.run([XFOIL], input=cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout)
        return r.stdout, False, time.time() - t0
    except subprocess.TimeoutExpired as e:
        out = e.stdout.decode(errors='replace') if isinstance(e.stdout, bytes) else (e.stdout or '')
        return out, True, time.time() - t0


def read_polar(path):
    rows = []
    if not os.path.exists(path): return rows
    for l in open(path).readlines()[12:]:
        p = l.split()
        if len(p) >= 5:
            try: rows.append(tuple(map(float, p[:5])))
            except ValueError: pass
    return rows


def _gaps(rows, step):
    """Grid alphas in 0..AMAX with no converged point within half a step, and the widest gap between converged points."""
    al = sorted(r[0] for r in rows)
    if not al: return [0.0], AMAX
    pts = [0.0] + al + [AMAX] if al[0] > 0 else al + [AMAX]
    widest = max(b - a for a, b in zip(pts, pts[1:]))
    n = int(round(AMAX / step))
    missing = [round(k * step, 3) for k in range(n + 1) if min(abs(a - k * step) for a in al) > 0.5 * step + 1e-6]
    return missing, widest


def _prep(name, work):
    """XFOIL commands that load the case's section (writing its .dat first for the Eppler candidates)."""
    sp = CASES[name]
    if sp['section'] == 'naca0012':
        return 'PLOP\nG F\n\nNACA 0012\n'
    S.write_dat(os.path.join(work, name + '.dat'), name, S.coords_w(sp['w'], sp['section']))
    return f'PLOP\nG F\n\nLOAD {name}.dat\n'


def _oper(name, T):
    sp = CASES[name]
    return (f'PPAR\nN {T["npan"]}\n\n\nOPER\nVISC {sp["re"]:.6g}\nMACH {sp["mach"]}\nVPAR\nN {sp["ncrit"]}\n'
            f'XTR {sp["xtr"]} {sp["xtr"]}\n\nITER {T["iters"]}\n')


def sweep_up(name, tier):
    T = TIERS[tier]
    work = os.path.join(WORK, f'{name}.up.t{tier}')
    shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    pol = 'up.pol'
    cmd = _prep(name, work) + _oper(name, T) + f'PACC\n{pol}\n\n'
    a = 0.0
    for d in DUMP_ALPHAS + [AMAX]:
        if d > a:
            a0 = a + T['step'] if a > 0 else 0.0     # each segment starts one step past the last one's end
            cmd += f'ASEQ {a0:g} {d:g} {T["step"]:g}\n'
            a = d
        if d in DUMP_ALPHAS: cmd += f'DUMP a{int(d):02d}.bl\n'
    cmd += 'PACC\n\nQUIT\n'
    out, timed_out, dt = _xfoil(cmd, work, T['timeout'])
    rows = read_polar(os.path.join(work, pol))
    missing, widest = _gaps(rows, T['step'])
    return dict(tier=tier, work=work, timed_out=timed_out, seconds=round(dt), n=len(rows), missing=missing,
                widest_gap=round(widest, 3), failed_msgs=out.count('Convergence failed'),
                ok=(not timed_out) and widest <= 1.0 + 1e-6)


def sweep_down(name, tier):
    T = TIERS[tier]
    work = os.path.join(WORK, f'{name}.down.t{tier}')
    shutil.rmtree(work, ignore_errors=True); os.makedirs(work)
    cmd = (_prep(name, work) + _oper(name, T)
           + f'ASEQ 0 {AMAX:g} {T["step"]:g}\nPACC\ndown.pol\n\nASEQ {AMAX:g} 0 -{T["step"]:g}\nPACC\n\nQUIT\n')
    out, timed_out, dt = _xfoil(cmd, work, T['timeout'])
    rows = read_polar(os.path.join(work, 'down.pol'))
    missing, widest = _gaps(rows, T['step'])
    return dict(tier=tier, work=work, timed_out=timed_out, seconds=round(dt), n=len(rows), missing=missing,
                widest_gap=round(widest, 3), failed_msgs=out.count('Convergence failed'),
                ok=(not timed_out) and widest <= 1.0 + 1e-6)


def _best(fn, name, start=1):
    tries = []
    for tier in range(start, max(TIERS) + 1):
        r = fn(name, tier)
        tries.append(r)
        print(f'  {name} {fn.__name__} tier {tier}: n {r["n"]} widest gap {r["widest_gap"]} timed out {r["timed_out"]} '
              f'{r["seconds"]} s', flush=True)
        if r['ok']: break
    best = max(tries, key=lambda r: (r['ok'], -r['widest_gap'], r['n']))
    best['tries'] = [{k: v for k, v in t.items() if k != 'work'} for t in tries]
    return best


def keep(name, up, down):
    """Copy the accepted polars and summarise the up-sweep dumps into data/xfoil/."""
    os.makedirs(OUT, exist_ok=True)
    shutil.copy(os.path.join(up['work'], 'up.pol'), os.path.join(OUT, name + '.up.pol'))
    if down and os.path.exists(os.path.join(down['work'], 'down.pol')):
        shutil.copy(os.path.join(down['work'], 'down.pol'), os.path.join(OUT, name + '.down.pol'))
    conv = {r[0] for r in read_polar(os.path.join(up['work'], 'up.pol'))}
    summarise(name, CASES[name]['re'], up['work'], conv, TIERS[up['tier']]['step'])


def upper_surface(path):
    """Rows (x, Ue, Dstar, Theta, Cf) of the upper surface, leading edge to trailing edge, from an XFOIL DUMP."""
    rows = []
    for l in open(path):
        r = l.split()
        if l.startswith('#') or len(r) < 8: continue
        try: rows.append(list(map(float, r[:8])))
        except ValueError: pass   # a Fortran field overflow runs two numbers together: drop that row
    up = []
    for r in rows:
        if up and r[1] > up[-1][1]: break
        up.append(r)
    return [(r[1], r[3], r[4], r[5], r[6]) for r in reversed(up)]


def summarise(name, re, work, converged=None, step=0.25):
    """The boundary-layer facts the table needs, one line per dumped alpha (the raw dumps stay out of the repo):
    te_sep  fraction of chord separated (Cf < 0) at the trailing edge, upper surface;
    bubble  the leading-edge laminar separation bubble's start and end x/c (Cf < 0 forward of 0.2 c), '-' if none;
    Re_d*s  Ue * delta* * Re at the bubble's start (Owen & Klanfer: short bubble above ~400-500, bursting below);
    conv    1 if the polar has a converged point at that alpha (the dump is the converged solution), 0 if not."""
    with open(os.path.join(OUT, name + '.sep.txt'), 'w') as f:
        f.write('# alpha  te_sep  bubble_x0  bubble_x1  Re_dstar_s  conv   (XFOIL DUMP, upper surface; extra_xfoil.summarise)\n')
        for a in DUMP_ALPHAS:
            p = os.path.join(work, f'a{a:02d}.bl')
            if not os.path.exists(p): p = os.path.join(work, f'{name}.a{a:02d}.bl')
            if not os.path.exists(p): continue
            ok = 1 if converged is None or any(abs(c - a) < 0.5 * step + 1e-6 for c in converged) else 0
            up = upper_surface(p)
            te = [x for x, ue, ds, th, cf in up if cf < 0 and x > 0.2]
            sep = 1.0 - (min(te) if te else 1.0)
            i0 = next((i for i, r in enumerate(up) if r[4] < 0 and r[0] < 0.2), None)
            if i0 is None:
                f.write(f'{a:5d}  {sep:6.3f}  -  -  -  {ok}\n')
            else:
                i1 = next((i for i in range(i0, len(up)) if up[i][4] > 0), i0)
                f.write(f'{a:5d}  {sep:6.3f}  {up[i0][0]:.4f}  {up[i1][0]:.4f}  {up[i0][1] * up[i0][2] * re:.0f}  {ok}\n')


def run_case(name, start=1):
    up = _best(sweep_up, name, start)
    down = None if name in VALIDATION else _best(sweep_down, name, start)   # the validation needs the up sweep only
    keep(name, up, down)
    strip = lambda r: {k: v for k, v in r.items() if k != 'work'} if r else None
    return name, dict(up=strip(up), down=strip(down))


if __name__ == '__main__':
    args = sys.argv[1:]
    jobs = 8
    if args[:1] == ['--jobs']: jobs, args = int(args[1]), args[2:]
    want = args or [n for n in CASES if n not in VALIDATION]
    log = json.load(open(RUNLOG)) if os.path.exists(RUNLOG) else {}
    with ThreadPoolExecutor(jobs) as ex:
        for name, rec in ex.map(run_case, want):
            log[name] = rec
            json.dump(log, open(RUNLOG, 'w'), indent=1)
    # identical settings for A and B at a station: re-run the lower-tier partner at the higher tier
    for i, y in enumerate(STATION_Y):
        a, b = f'A_st{i + 1}_y{y:.3f}', f'B_st{i + 1}_y{y:.3f}'
        if a not in log or b not in log: continue
        ta, tb = log[a]['up']['tier'], log[b]['up']['tier']
        if ta != tb:
            lo, hi = (a, tb) if ta < tb else (b, ta)
            print(f'tier mismatch at st{i + 1}: re-running {lo} from tier {hi}', flush=True)
            name, rec = run_case(lo, hi)
            log[name] = rec
            json.dump(log, open(RUNLOG, 'w'), indent=1)
    for n in want:
        u, d = log[n]['up'], log[n]['down']
        print(f'{n}: up tier {u["tier"]} ok {u["ok"]} missing {len(u["missing"])} widest {u["widest_gap"]}'
              + (f'; down tier {d["tier"]} ok {d["ok"]} widest {d["widest_gap"]}' if d else ''))
