import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import builder as B
S2K = 1.35582  # slug-ft2 -> kg-m2

# ===== P-51D Mustang (straight laminar wing, plain flaps 50deg, V-1650 nose) =====
def build_p51():
    lam = B.flat_plate_symmetric(clmax=1.45, stall_deg=15)  # laminar, cambered-ish -> use clarkY actually
    half=5.64; area=21.83
    flap={'maxDeltaAlphaRad':0.30,'maxCd':0.11,'maxClMax':0.7}
    wf,wa = B.wing_panel(half, 9, lambda f: 2.58-(2.58-1.18)*f, -0.4, 5.0, 1.0, -2.0, 'clarkY-like',
                         0.65, 1.0, flap_frac=0.55, flap=flap)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-4.9, 4.2, 1.5, 0.9, 0.82, decalage_deg=-1.5)
    B.write('p51d-like','WWII Fighter (P-51D)', 4600, [-0.05,0,0],
        {'ixx':13000,'iyy':15000,'izz':23500,'ixz':300},  # DATCOM est scaled to loaded
        [{'id':'wing','oswaldE':0.82,'strips':wf},{'id':'wing-aileron','oswaldE':0.82,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.55,'sideForceArea':4.5,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':5,'planCenterX':-0.5,'sideArea':6,'sideCenterX':-0.5,'cd':1.2,'lengthM':9.8}},
        B.prop(1100000, 3.40, sign=1, inertia=14.0, eff=0.80, tlz=0.1),  # 1490hp Merlin
        {'vneMs':226,'gMax':8.0,'gMin':-4.0}, B.controls(0.35,0.44,0.35))

# ===== F-86F Sabre (35deg swept, slats, all-flying stab, jet, NACA F-86A inertia) =====
def build_f86():
    swp = B.flat_plate_symmetric(clmax=1.05, stall_deg=13)
    half=5.65; area=26.75
    flap={'maxDeltaAlphaRad':0.22,'maxCd':0.09,'maxClMax':0.5}   # plain inboard flaps
    slat={'stallExtensionRad':0.18,'clIncrement':0.20}            # automatic LE slats
    wf,wa = B.wing_panel(half, 10, lambda f: 3.14-(3.14-1.55)*f, -0.3, 3.0, 1.0, -2.0, 'sabre',
                         0.60, 1.0, sweep_deg=35, flap_frac=0.55, flap=flap, slat=slat)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-5.3, 3.3, 2.0, 2.0, 1.9, decalage_deg=-1.0, sweep_deg=35)  # all-flying: big elev gain
    for e in el: e['control']['gain']=1.0
    B.write('f86-sabre-like','Jet Fighter (F-86 Sabre)', 6900, [-0.3,0,0],
        {'ixx':9831,'iyy':27120,'izz':31337,'ixz':963},  # NACA RM L53J01 (Iyy est)
        [{'id':'wing','oswaldE':0.78,'strips':wf},{'id':'wing-aileron','oswaldE':0.78,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.9,'sideForceArea':6,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':7,'planCenterX':-1,'sideArea':9,'sideCenterX':-1.5,'cd':1.2,'lengthM':11.4}},
        {'maxPowerW':26600,'propDiameterM':0.0,'idleRpm':0,'maxRpm':0,'propInertia':0,
         'rotationSign':1,'efficiency':1.0,'thrustLineZ':0.0,'pFactorK':0,'slipstreamK':0},  # J47 26.6kN jet
        {'vneMs':300,'gMax':7.3,'gMin':-3.0}, B.controls(0.30,0.30,0.30),
        extra_tables={'sabre':swp}, vbf=0.5)

# ===== Piper Seminole (light twin, slotted flaps 40, laminar 65-415; centerline thrust for now) =====
def build_seminole():
    half=5.88; area=17.08
    flap={'maxDeltaAlphaRad':0.24,'maxCd':0.08,'maxClMax':0.6}
    wf,wa = B.wing_panel(half, 8, lambda f: 1.6-(1.6-1.0)*f, -0.3, 7.0, 2.0, -3.0, 'clarkY-like',
                         0.58, 1.0, sweep_deg=5, flap_frac=0.5, flap=flap)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-4.6, 2.1, 1.4, 0.9, 0.82, decalage_deg=-2.5)
    B.write('seminole-like','Twin Trainer (PA-44)', 1724, [-0.05,0,0],
        {'ixx':2034,'iyy':2576,'izz':4204,'ixz':80},
        [{'id':'wing','oswaldE':0.80,'strips':wf},{'id':'wing-aileron','oswaldE':0.80,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':0.42,'sideForceArea':4,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':4,'planCenterX':-0.5,'sideArea':5,'sideCenterX':-0.5,'cd':1.2,'lengthM':8.4}},
        B.prop(268000, 1.88, sign=1, inertia=3.5, eff=0.78, tlz=0.0),  # 2x180hp (counter-rot, no critical eng)
        {'vneMs':104,'gMax':3.8,'gMin':-1.5}, B.controls(0.35,0.44,0.28))

# ===== DC-3 (two-panel wing, split flaps 45, radials; REAL NACA inertia) =====
def build_dc3():
    half=14.48; area=91.87
    flap={'maxDeltaAlphaRad':0.20,'maxCd':0.12,'maxClMax':0.7}   # split flap
    wf,wa = B.wing_panel(half, 10, lambda f: 4.32-(4.32-1.42)*f, -0.9, 5.0, 2.0, 0.0, 'clarkY-like',
                         0.62, 1.0, sweep_deg=15, flap_frac=0.55, flap=flap)
    k=area/(sum(s['area'] for s in wf)+sum(s['area'] for s in wa))
    for s in wf+wa: s['area']=round(s['area']*k,3)
    st,el,fn,rd = B.tail(-9.1, 16.6, 6.0, 4.5, 4.3, decalage_deg=-2.0,
        fin_zs=[-0.8,-1.8,-2.8], rud_zs=[-0.4,-1.4,-2.4])
    B.write('dc3-like','Radial Airliner (DC-3)', 11591, [-0.1,0,0],
        {'ixx':round(66670*S2K),'iyy':round(91690*S2K),'izz':round(150400*S2K),'ixz':2000},  # NACA MR 1942
        [{'id':'wing','oswaldE':0.82,'strips':wf},{'id':'wing-aileron','oswaldE':0.82,'strips':wa},
         {'id':'hStab','oswaldE':0.85,'strips':st},{'id':'elevator','oswaldE':0.85,'strips':el},
         {'id':'vStab','oswaldE':0.85,'strips':fn},{'id':'rudder-vstab','oswaldE':0.85,'strips':rd}],
        {'cd0Area':2.5,'sideForceArea':14,'damping':{'p':0,'q':0,'r':0},
         'crossflow':{'planArea':18,'planCenterX':-1.5,'sideArea':22,'sideCenterX':-2,'cd':1.2,'lengthM':19.6}},
        B.prop(1789000, 3.53, sign=1, inertia=16.0, eff=0.80, tlz=0.0),  # 2x1200hp radials
        {'vneMs':117,'gMax':3.0,'gMin':-1.0}, B.controls(0.30,0.35,0.30))

print("Building batch 2:")
build_p51(); build_f86(); build_seminole(); build_dc3()
