#!/usr/bin/env python3
"""Build the flight configs for the two soft wings: a paraglider and a hang glider.

METHOD. Neither type publishes a section or a weight-and-balance sheet, but both publish a POLAR -- best
glide, the speed it happens at, minimum sink, trim speed, stall and (for the hang glider) Vne. So the
derivation runs BACKWARDS, the same way Ty asked the Great Lakes' CLmax to be back-solved from its
published Vso:

    L/D_max = 0.5 * sqrt(pi * AR * e / CD0)      ->   CD0 = pi * AR * e / (4 * (L/D_max)^2)

with CD0 referred to the wing area. CD0 is then SPLIT into the part that is section profile drag (carried
by the airfoil table) and the part that is not wing at all -- the pilot's body and, on a paraglider, 300-odd
metres of line -- which is carried by `fuselage.cd0Area`, a true drag AREA in m^2 in this sim
(AeroModel: drag = q * Cd0Area). Line drag is the reason a paraglider glides at 9 and a sailplane at 40.

What this file does NOT invent: the mass, area, span and published polar points are at the top of each
entry with their source. Everything else is derived here, and the test
`tools/FlightTests/SoftWingPolarTests.cs` flies the result and checks it against those same published
points -- so if this arithmetic is wrong, that test says so rather than this comment.
"""
import json, math, os, sys

RHO = 1.225
G = 9.80665

# --------------------------------------------------------------------------------------------------
# The two wings, as published. Sources in docs/SOFT-WINGS.md.
# --------------------------------------------------------------------------------------------------
PARAGLIDER = dict(
    ident="paraglider-en-b-like", name="Paraglider-like (EN-B)",
    # Mass: an 85 kg pilot, a 10 kg harness/reserve, a 5 kg wing -- an EN-B wing's certified
    # all-up range is about 85-105 kg, so 100 kg sits mid-range.
    pilot_kg=85.0, gear_kg=10.0, wing_kg=5.0,
    area=25.0, span=11.2,            # a mid EN-B: 25 m^2 flat, 11.2 m flat span -> AR 5.02
    hang_m=6.5,                      # pilot below the wing: riser + line length
    ld_max=9.0,                      # published best glide for the class
    v_trim=10.0,                      # 36 km/h hands-up
    v_stall=7.0,                      # ~25 km/h
    v_max=14.0,                       # ~50 km/h on full bar
    sink_min=1.05,
    oswald=0.90,
    # A paraglider's parasite drag is NOT the wing: ~0.30 m^2 of line and ~0.40 m^2 of harnessed pilot.
    parasite_cda=0.70,
    section=dict(cl0=0.55, cl_alpha=5.4, cl_max=1.35, a_stall_deg=13.0, cd_min=0.018,
                 cl_at_cd_min=0.60, cm=-0.085, k_cd=0.055),
    control="brakes",
    g_max=5.5, g_min=-1.0,          # an EN-B wing is load-tested to 8 g; -1 is where it goes soft
    shift_lat=0.0, shift_long=0.0,
    # BRAKES. Pulling one toggle deflects that side's trailing edge: drag and lift up on that side, the
    # wing yaws and rolls into it. Pulling BOTH adds camber and angle of attack across the span, which is
    # the only pitch control a tailless pendulum wing has -- so it is what "elevator" means here. The
    # speed bar is the opposite: it pulls the leading edge down and takes angle of attack away.
    controls_json={
        "aileron": {"maxDeflRad": 0.52, "rateRadPerSec": 2.0, "expo": 0.2, "deadZone": 0.04,
                    "reversible": False, "centeringSpringKt": 0, "trimType": "none"},
        "elevator": {"maxDeflRad": 0.42, "rateRadPerSec": 1.8, "expo": 0.15, "deadZone": 0.03,
                     "reversible": False, "centeringSpringKt": 0, "trimType": "none"},
    },
    # A paraglider lands on the pilot's legs: soft, heavily damped, and it does not roll.
    gear_json=[{"pos": [0.0, 0.0, 0.95], "springN": 9000, "dampNs": 3500, "brake": True,
                "isTailwheel": False, "isSteerable": False, "maxSteerRad": 0.0, "tireMu": 0.9,
                "corneringStiffnessN": 1200, "rollResistN": 900}],
)

HANG_GLIDER = dict(
    ident="hang-glider-like", name="Hang Glider-like (intermediate)",
    # Mass: an 80 kg pilot in a pod harness and a 30 kg flex-wing.
    pilot_kg=80.0, gear_kg=0.0, wing_kg=30.0,
    area=14.4, span=10.0,            # 155 sq ft, 10 m span -> AR 6.94 (Sport-3 class)
    hang_m=1.5,                      # the pilot hangs just under the keel
    ld_max=13.0,
    v_trim=11.0,                      # ~40 km/h
    v_stall=8.3,                      # ~30 km/h
    v_max=23.6,                       # Vne 85 km/h, published for this class
    sink_min=0.85,
    oswald=0.88,
    # A prone pilot in a pod plus the control frame and wires.
    parasite_cda=0.195,
    # CLmax is BACK-SOLVED from the published stall, not assumed: 2*W/(rho*S*V_stall^2) at 110 kg over
    # 14.4 m^2 at 8.3 m/s wants 1.78, which no sail section gives. A flex wing's quoted "stall" is a mush
    # with the tips washed out and already separated, so the honest reading is that the published figure is
    # a MINIMUM CONTROLLABLE speed, not a CL break. CLmax 1.55 is the top of the credible range for a
    # cambered double-surface sail, and it puts the break at 8.9 m/s (32 km/h) -- 7 % above the published
    # 30 km/h. That gap is reported by SoftWingPolarTests rather than tuned away.
    section=dict(cl0=0.35, cl_alpha=5.0, cl_max=1.55, a_stall_deg=16.0, cd_min=0.014,
                 cl_at_cd_min=0.45, cm=-0.055, k_cd=0.016),
    control="weightshift",
    g_max=6.0, g_min=-2.0,          # HGMA load-tests to +6/-3; -2 is where a flex wing tucks
    shift_lat=0.45, shift_long=0.40,  # how far a prone pilot can swing on the hang strap
    # WEIGHT SHIFT. A flex wing has no moving surface, so these axes are not deflections at all -- they are
    # the angle the pilot swings on the hang strap, and the limit is atan(maxShift / hangHeight): how far a
    # prone pilot can actually move. build() fills them in once the hang height is known. The rate is how
    # fast a body moves, not how fast a pushrod does. No rudder at all: a flex wing has no yaw control.
    controls_json="swing",
    # A hang glider lands on its wheels or the pilot's feet; the control frame takes the rest.
    gear_json=[{"pos": [0.35, 0.0, 0.75], "springN": 14000, "dampNs": 3000, "brake": True,
                "isTailwheel": False, "isSteerable": False, "maxSteerRad": 0.0, "tireMu": 0.7,
                "corneringStiffnessN": 1500, "rollResistN": 600}],
)


def derive(w):
    """CD0 from the published best glide, then split it into section and parasite."""
    ar = w["span"] ** 2 / w["area"]
    mass = w["pilot_kg"] + w["gear_kg"] + w["wing_kg"]
    cd0_total = math.pi * ar * w["oswald"] / (4.0 * w["ld_max"] ** 2)
    cd0_parasite = w["parasite_cda"] / w["area"]
    cd0_section = cd0_total - cd0_parasite
    out = dict(ar=ar, mass=mass, cd0_total=cd0_total,
               cd0_parasite=cd0_parasite, cd0_section=cd0_section,
               wing_loading=mass * G / w["area"])
    # Where the published points land on the derived polar.
    out["cl_bg"] = math.sqrt(cd0_total * math.pi * ar * w["oswald"])
    # The section's minimum drag is DERIVED, not guessed. The table carries a drag bucket round
    # cl_at_cd_min, so at the best-glide CL the section is already paying k_cd*(cl_bg - cl_at_cd_min)^2 on
    # top of cd_min; cd_min is whatever is left once that and the parasite area are taken off the CD0 the
    # published glide demands. Guessing it instead cost the paraglider 8 % of its glide, because the bucket
    # was being charged twice.
    sec = w["section"]
    out["cd_min_derived"] = (cd0_total - cd0_parasite
                             - sec["k_cd"] * (out["cl_bg"] - sec["cl_at_cd_min"]) ** 2)
    out["v_bg"] = math.sqrt(2 * out["wing_loading"] / (RHO * out["cl_bg"]))
    out["cl_trim"] = 2 * out["wing_loading"] / (RHO * w["v_trim"] ** 2)
    out["cl_stall_needed"] = 2 * out["wing_loading"] / (RHO * w["v_stall"] ** 2)
    return out


def polar(w, d, v):
    """Sink rate and glide ratio at airspeed v on the derived polar."""
    cl = 2 * d["wing_loading"] / (RHO * v * v)
    cd = d["cd0_total"] + cl * cl / (math.pi * d["ar"] * w["oswald"])
    ld = cl / cd
    return v / ld, ld, cl


def inertia(w, d):
    """About the COMBINED cg. The wing is a span-wise bar; the pilot is a point mass below it."""
    m_w, m_p = w["wing_kg"], w["pilot_kg"] + w["gear_kg"]
    h = w["hang_m"]
    # z measured UP from the pilot; the sim's body z is negative up, handled by the caller.
    z_cg = h * m_w / d["mass"]
    chord_mean = w["area"] / w["span"]
    keel = 2.2 * chord_mean                      # fore-and-aft extent of the wing's mass
    ixx = m_p * z_cg ** 2 + m_w * (h - z_cg) ** 2 + m_w * w["span"] ** 2 / 12.0
    iyy = m_p * z_cg ** 2 + m_w * (h - z_cg) ** 2 + m_w * keel ** 2 / 12.0
    izz = m_w * (w["span"] ** 2 + keel ** 2) / 12.0 + 0.35 * m_p
    return z_cg, ixx, iyy, izz


def airfoil_table(s):
    """A 2-D cambered section: linear lift to the stall, a cosine decay past it, and a drag bucket.
    The sim adds induced drag itself (per surface, cl^2 / (pi AR e)), so this is section data only."""
    a_stall = math.radians(s["a_stall_deg"])
    alphas, cls, cds, cms = [], [], [], []
    deg = list(range(-180, -24, 15)) + [-20, -16, -12, -8, -4, 0, 2, 4, 6, 8, 10, 11, 12, 13, 14, 15,
                                        16, 18, 20, 24, 30, 40, 60, 90, 120, 150, 180]
    for dd in deg:
        a = math.radians(dd)
        lin = s["cl0"] + s["cl_alpha"] * a
        if abs(a) <= a_stall:
            cl = max(-s["cl_max"], min(s["cl_max"], lin))
        else:
            # Past the stall: fall back toward a flat plate, 2 sin a cos a.
            sep = min(1.0, (abs(a) - a_stall) / math.radians(18.0))
            plate = 2.0 * math.sin(a) * math.cos(a)
            edge = math.copysign(s["cl_max"], a if a > 0 else a)
            cl = edge * (1 - sep) + plate * sep
        # Drag: a bucket round cl_at_cd_min, plus full separation drag past the stall.
        cd = s["cd_min"] + s["k_cd"] * (cl - s["cl_at_cd_min"]) ** 2
        if abs(a) > a_stall:
            sep = min(1.0, (abs(a) - a_stall) / math.radians(18.0))
            cd += sep * (1.2 * math.sin(abs(a)) ** 2)
        alphas.append(round(a, 5)); cls.append(round(cl, 4))
        cds.append(round(cd, 5)); cms.append(round(s["cm"] * (1 - 0.5 * min(1.0, abs(a) / a_stall)), 4))
    return dict(alphaRad=alphas, cl=cls, cd=cds, cm=cms)


def strips(w, d, z_cg, n_per_side=9):
    """An elliptical-ish planform. z is NEGATIVE UP in this sim's body frame, and the whole wing sits
    `hang_m` above the pilot, so its strips sit (hang_m - z_cg) above the COMBINED cg."""
    b2 = w["span"] / 2.0
    z_wing = -(w["hang_m"] - z_cg)
    # Elliptical chord, scaled so the integral is exactly the published area.
    ys = [b2 * (i + 0.5) / n_per_side for i in range(n_per_side)]
    raw = [math.sqrt(max(0.0, 1 - (y / b2) ** 2)) for y in ys]
    dy = b2 / n_per_side
    scale = (w["area"] / 2.0) / (sum(raw) * dy)
    out = []
    for side in (-1, 1):
        for y, r in zip(ys, raw):
            chord = r * scale
            # Sweep: a paraglider's leading edge is almost straight, a flex wing is strongly swept.
            sweep = 0.30 if w["control"] == "brakes" else 0.95
            x_qc = -sweep * (abs(y) / b2) * (w["area"] / w["span"])
            # Brakes live on the outboard trailing edge; a flex wing has no moving surface at all.
            ctl = None
            if w["control"] == "brakes":
                f = abs(y) / b2
                if f > 0.45:
                    ctl = dict(surface="aileron", gain=round(0.60 * f ** 1.5, 4))
                else:
                    ctl = dict(surface="elevator", gain=round(0.50 * (1 - f), 4))
            out.append(dict(pos=[round(x_qc, 4), round(side * y, 4), round(z_wing, 4)],
                            chord=round(chord, 5), area=round(chord * dy, 5),
                            incidenceRad=0.0,
                            dihedralRad=round(math.radians(4.0 if w["control"] == "brakes" else 2.0) * side * 0, 4),
                            airfoil=w["ident"] + "-sec",
                            **({"control": ctl} if ctl else {})))
    return out, z_wing


def swing_axes(w, z_cg):
    """The flex wing's axes: the pilot's swing angle on the strap, limited by how far he reaches."""
    h = w["hang_m"] - z_cg
    return {
        "aileron": {"maxDeflRad": round(math.atan2(w["shift_lat"], h), 4), "rateRadPerSec": 2.0,
                    "expo": 0.1, "deadZone": 0.02, "reversible": False, "centeringSpringKt": 0,
                    "trimType": "none"},
        "elevator": {"maxDeflRad": round(math.atan2(w["shift_long"], h), 4), "rateRadPerSec": 2.0,
                     "expo": 0.1, "deadZone": 0.02, "reversible": False, "centeringSpringKt": 0,
                     "trimType": "none"},
    }


def balance(w, d, st):
    """Put the hang point where a real one goes: at the BALANCE point.

    A hang glider hangs from a single point and a paraglider from its risers, so neither has a tail to trim
    against -- the only thing that sets trim is where that point sits fore and aft of the wing's own centre
    of lift. Taking the mean aerodynamic centre as the area-weighted quarter chord, pitch equilibrium at the
    design trim speed wants

        x_cg = x_ac - q*S*c_bar*Cm / W

    which for a nose-down section puts the cg AHEAD of the ac -- and that offset is also the static margin,
    so a wing balanced this way is stable for the same reason it trims. The first hang glider was built with
    its cg at the origin and its ac 0.68 m aft, which is a 734 N m nose-down moment against 314 N m of
    available swing: it could not be trimmed at any speed, and the polar sweep found zero flyable points.

    The strips are translated rather than the cg moved, so the cg stays at the origin where the inertia was
    computed about it."""
    area = sum(x["area"] for x in st)
    x_ac = sum(x["pos"][0] * x["area"] for x in st) / area
    c_bar = w["area"] / w["span"]
    q = 0.5 * RHO * w["v_trim"] ** 2
    weight = d["mass"] * G
    offset = -q * w["area"] * c_bar * w["section"]["cm"] / weight     # cg ahead of the ac, metres
    want_ac = -offset
    shift = want_ac - x_ac
    for x in st:
        x["pos"][0] = round(x["pos"][0] + shift, 4)
    return x_ac, want_ac, offset, offset / c_bar


def build(w):
    d = derive(w)
    w["section"] = dict(w["section"], cd_min=round(d["cd_min_derived"], 5))
    z_cg, ixx, iyy, izz = inertia(w, d)
    st, z_wing = strips(w, d, z_cg)
    bal = balance(w, d, st)
    cfg = {
        "schemaVersion": 1,
        "id": w["ident"],
        "displayName": w["name"],
        "spawnIasMs": round(w["v_trim"], 1),
        "mass": {
            "massKg": round(d["mass"], 1),
            "cg": [0.0, 0.0, 0.0],
            "inertia": {"ixx": round(ixx, 1), "iyy": round(iyy, 1), "izz": round(izz, 1), "ixz": 0.0},
        },
        "surfaces": [{"id": "wing", "heightAboveBodyAxisM": round(-z_wing, 3),
                      "oswaldE": w["oswald"], "strips": st}],
        "airfoilTables": {w["ident"] + "-sec": airfoil_table(w["section"])},
        "fuselage": {"cd0Area": round(w["parasite_cda"], 4), "sideForceArea": 0.25,
                     "munkScale": 0.0},
        "stallDynamics": {"stripSepTau": 0.2, "stripReattachTau": 0.8, "wakeGrowTau": 0.3,
                          "wakeDecayTau": 1.2, "wakeSpreadDeg": 8.0},
        "limits": {"vneMs": round(w["v_max"], 1), "gMax": w["g_max"], "gMin": w["g_min"]},
        "controls": swing_axes(w, z_cg) if w["controls_json"] == "swing" else w["controls_json"],
        "gear": w["gear_json"],
    }
    if w["control"] == "weightshift":
        cfg["weightShift"] = {
            "pilotMassKg": w["pilot_kg"], "hangHeightM": round(w["hang_m"] - z_cg, 3),
            "maxLateralM": w["shift_lat"], "maxLongitudinalM": w["shift_long"],
        }
    return cfg, d, (z_cg, ixx, iyy, izz), bal


def report(w, d, inert, cfg, bal):
    z_cg, ixx, iyy, izz = inert
    print(f"=== {w['ident']}")
    print(f"    mass {d['mass']:.1f} kg   area {w['area']:.2f} m2   AR {d['ar']:.2f}   "
          f"wing loading {d['wing_loading']/G:.2f} kg/m2")
    print(f"    section cd_min derived {d['cd_min_derived']:.5f} (was a guess)")
    print(f"    CD0 total {d['cd0_total']:.4f} = section {d['cd0_section']:.4f} + parasite "
          f"{d['cd0_parasite']:.4f} ({w['parasite_cda']:.3f} m2 / {w['area']:.1f} m2)")
    print(f"    cg {z_cg:.2f} m above the pilot, wing {w['hang_m']-z_cg:.2f} m above the cg")
    print(f"    Ixx {ixx:.0f}  Iyy {iyy:.0f}  Izz {izz:.0f} kg m2")
    print(f"    derived best glide {d['cl_bg']/ (d['cd0_total'] + d['cl_bg']**2/(math.pi*d['ar']*w['oswald'])):.2f} "
          f"at {d['v_bg']:.1f} m/s   (published {w['ld_max']:.1f})")
    print(f"    CL needed at the published stall {w['v_stall']:.1f} m/s: {d['cl_stall_needed']:.2f} "
          f"(section CLmax {w['section']['cl_max']:.2f})")
    print("    v(m/s)  sink   glide")
    for v in [w["v_stall"], w["v_trim"], d["v_bg"], w["v_max"]]:
        s, ld, cl = polar(w, d, v)
        print(f"    {v:6.1f} {s:6.2f} {ld:7.2f}   CL {cl:.3f}")
    print(f"    balance: ac was {bal[0]:+.3f} m, moved to {bal[1]:+.3f} m -> cg {bal[2]*1000:.0f} mm ahead "
          f"of the ac = {bal[3]*100:.1f} % MAC static margin")
    print(f"    strips {len(cfg['surfaces'][0]['strips'])}  area sum "
          f"{sum(s['area'] for s in cfg['surfaces'][0]['strips']):.3f} m2")


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    for w in (PARAGLIDER, HANG_GLIDER):
        cfg, d, inert, bal = build(w)
        report(w, d, inert, cfg, bal)
        p = os.path.join(root, "configs", "aircraft", w["ident"] + ".json")
        with open(p, "w") as f:
            json.dump(cfg, f, indent=2)
            f.write("\n")
        print(f"    -> {os.path.relpath(p, root)}")
