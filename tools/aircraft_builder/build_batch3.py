import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import builder as B

# ===== PA-18-150 Super Cub (USA 35B cambered, flaps 50deg, tandem front-solo taildragger) =====
def build_cub():
    half=5.365; area=16.58
    flap={'maxDeltaAlphaRad':0.26,'maxCd':0.10,'maxClMax':0.55}  # plain flaps 50deg
    # wing_panel(half, n, chord_fn, z, dihedral_deg, incidence_deg, twist_deg, airfoil, ail_frac, ail_gain, ...)
    wf,wa = B.wing_panel(half, 8, lambda f: 1.55, -0.75, 1.5, 1.8, 0.0, 'clarkY-like',
                         0.60, 1.0, flap_frac=0.55, flap=flap)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-4.4, 2.7, 1.5, 0.9, 0.82, decalage_deg=-2.0)
    # Inertia: Kirschbaum-class estimate, ~794kg high-wing single. slug-ft2 x1.356
    B.write('pa18-cub-like','Bush Taildragger (PA-18)', 794, [-0.05,0,0],
        {'ixx':1450,'iyy':1900,'izz':3000,'ixz':60},
        [{'id':'wing','oswaldE':0.78,'strips':wf},{'id':'wing-aileron','oswaldE':0.78,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.55,'sideForceArea':4.5,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':4,'planCenterX':-0.5,'sideArea':5,'sideCenterX':-0.5,'cd':1.2,'lengthM':6.88}},
        B.prop(112000, 1.88, sign=1, inertia=3.5, eff=0.75, tlz=0.0),  # O-320 150hp
        {'vneMs':68,'gMax':3.8,'gMin':-1.5}, B.controls(0.35,0.44,0.44))

# ===== 8KCAB Super Decathlon (SEMI-symmetric NACA1412-mod, NO flaps, aerobatic +6/-5) =====
def build_decathlon():
    semi = B.semi_symmetric(clmax_up=1.35, clmax_inv=1.05, stall_up=15, stall_inv=13, cl0=0.15)
    half=4.875; area=15.71
    wf,wa = B.wing_panel(half, 8, lambda f: 1.61, -0.75, 1.0, 1.0, 0.0, 'decathlon-wing',
                         0.50, 1.0)  # near full-span ailerons, no flaps
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-4.2, 2.3, 1.4, 0.9, 0.82, decalage_deg=-1.5)
    B.write('decathlon-8kcab-like','Aerobat Taildragger (Decathlon)', 816, [-0.03,0,0],
        {'ixx':1350,'iyy':1800,'izz':2900,'ixz':50},
        [{'id':'wing','oswaldE':0.80,'strips':wf},{'id':'wing-aileron','oswaldE':0.80,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.50,'sideForceArea':4.0,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':3.5,'planCenterX':-0.4,'sideArea':4.5,'sideCenterX':-0.4,'cd':1.2,'lengthM':6.98}},
        B.prop(134000, 1.88, sign=1, inertia=4.5, eff=0.80, tlz=0.0),  # AEIO-360 180hp CS
        {'vneMs':78,'gMax':6.0,'gMin':-5.0}, B.controls(0.35,0.52,0.52),
        extra_tables={'decathlon-wing':semi})

print("Building batch 3:")
build_cub(); build_decathlon()
