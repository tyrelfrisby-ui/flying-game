import sys, os, math, json
sys.path.insert(0, os.path.dirname(__file__))
import builder as B

# ============ 737-800 (swept jet, slats+Fowler flaps, Wolfram inertia) ============
def build_737():
    # supercritical wing table: clmax ~1.45, stall ~15; use flat_plate variant
    wtab = B.flat_plate_symmetric(clmax=1.45, stall_deg=15)
    half=17.16; area=124.6
    flap={'maxDeltaAlphaRad':0.28,'maxCd':0.10,'maxClMax':0.9}   # triple-slotted Fowler, big
    slat={'stallExtensionRad':0.20,'clIncrement':0.25}
    wf,wa = B.wing_panel(half, 10, lambda f: 6.3-(6.3-1.77)*f, -1.5, 6.0, 3.2, 0.0, 'sc737',
                         0.72, 1.0, sweep_deg=25, flap_frac=0.65, flap=flap, slat=slat)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-15.0, 32.8, 6.5, 15.0, 6.0, stab_z=0.0, decalage_deg=-1.5,
        fin_zs=[-1,-2.5,-4], rud_zs=[-0.5,-2,-3.5], sweep_deg=30)
    prop={'maxPowerW':214000,'propDiameterM':0.0,'idleRpm':0,'maxRpm':0,'propInertia':0,
          'rotationSign':1,'efficiency':1.0,'thrustLineZ':-1.9,'pFactorK':0,'slipstreamK':0}  # 2x107kN jet, thrust N
    B.write('boeing-737-like','Airliner (737-800)', 65000, [-0.5,0,0],
        {'ixx':1866711,'iyy':3394953,'izz':5097558,'ixz':149140},
        [{'id':'wing','oswaldE':0.80,'strips':wf},{'id':'wing-aileron','oswaldE':0.80,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':6.5,'sideForceArea':30,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':40,'planCenterX':-2,'sideArea':55,'sideCenterX':-3,'cd':1.2,'lengthM':39.5}},
        prop, {'vneMs':180,'gMax':2.5,'gMin':-1.0}, B.controls(0.28,0.35,0.30),
        extra_tables={'sc737':wtab}, vbf=0.5)

# ============ Stearman PT-17 (biplane, radial, ailerons on LOWER wing only, no flaps) ============
def build_stearman():
    up_h, lo_h = 4.90, 4.75
    # NACA 2213 cambered ~ use clarkY-like (cambered). Upper: no ailerons. Lower: ailerons outboard.
    uf,_ = B.wing_panel(up_h, 6, lambda f: 1.52, -0.78, 3.0, 0.0, 0.0, 'clarkY-like', 1.1, 0, x0=0.30)  # no ail (frac>1.1 never)
    lf,la = B.wing_panel(lo_h, 6, lambda f: 1.52, 0.30, 4.0, 0.0, 0.0, 'clarkY-like', 0.5, 1.0, x0=-0.15)
    ku=13.8/sum(s['area'] for s in uf); kl=13.5/(sum(s['area'] for s in lf)+sum(s['area'] for s in la))
    for s in uf: s['area']=round(s['area']*ku,3)
    for s in lf+la: s['area']=round(s['area']*kl,3)
    st,el,fn,rd = B.tail(-4.4, 4.0, 1.5, 0.7, 0.55, decalage_deg=-2.5)
    B.write('stearman-pt17-like','Trainer Biplane (PT-17)', 1200, [-0.05,0,0],
        {'ixx':1650,'iyy':2200,'izz':3700,'ixz':60},
        [{'id':'wing-upper','oswaldE':0.80,'strips':uf},
         {'id':'wing-lower','oswaldE':0.80,'strips':lf},{'id':'wing-lower-aileron','oswaldE':0.80,'strips':la},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.55,'sideForceArea':4.0,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':3.5,'planCenterX':-0.5,'sideArea':4.0,'sideCenterX':-0.4,'cd':1.2,'lengthM':7.5}},
        B.prop(164000, 2.55, sign=1, inertia=4.0, eff=0.75, tlz=0.0),
        {'vneMs':84,'gMax':4.0,'gMin':-2.0}, B.controls(0.35,0.44,0.44))

# ============ Extra 300S (single-seat, symmetric wing, no flaps, ±10g) ============
def build_extra():
    sym = B.flat_plate_symmetric(clmax=1.35, stall_deg=15)
    half=3.75; area=10.44
    wf,wa = B.wing_panel(half, 8, lambda f: 1.6-(1.6-1.1)*f, 0.0, 0.0, 0.0, 0.0, 'extra-sym',
                         0.45, 1.0)  # near full-span ailerons (45%+)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-3.0, 2.56, 0.77, 0.62, 0.77, decalage_deg=0.0)
    B.write('extra-300-like','Aerobat Monoplane (Extra 300S)', 820, [-0.02,0,0],
        {'ixx':800,'iyy':1100,'izz':1800,'ixz':30},
        [{'id':'wing','oswaldE':0.82,'strips':wf},{'id':'wing-aileron','oswaldE':0.82,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.28,'sideForceArea':2.5,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':2.2,'planCenterX':-0.3,'sideArea':2.5,'sideCenterX':-0.3,'cd':1.2,'lengthM':6.65}},
        B.prop(224000, 2.00, sign=1, inertia=5.0, eff=0.80, tlz=0.0),
        {'vneMs':130,'gMax':10.0,'gMin':-10.0}, B.controls(0.52,0.44,0.52),
        extra_tables={'extra-sym':sym})

print("Building batch 1:")
build_737(); build_stearman(); build_extra()
