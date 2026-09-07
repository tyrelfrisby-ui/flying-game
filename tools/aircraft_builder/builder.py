"""Shared aircraft-config builder. Encodes the modeling patterns validated across the
glider/C172/Pitts campaign: split control surfaces, per-strip flaps/sweep/slats,
component-mass inertia, measured airfoil tables. Each aircraft script imports this."""
import json, math, os

REPO = os.path.expanduser('~/Documents/flying-game')
CFG = os.path.join(REPO, 'configs/aircraft')

def load_tables():
    g = json.load(open(os.path.join(CFG, 'glider-2-33-like.json')))
    return g['airfoilTables'], g['stallDynamics']

TABLES, DYN = load_tables()

def wing_panel(half_span, n, chord_fn, z, dihedral_deg, incidence_deg, twist_deg, airfoil,
               ail_frac, ail_gain, sweep_deg=0, flap_frac=0, flap=None, slat=None, x0=0.1):
    """One wing (both sides): returns (fixed_strips, aileron_strips, flap_strips)."""
    fixed, ails, flaps = [], [], []
    sweep = math.radians(sweep_deg)
    for side in (-1, 1):
        edges = [half_span*math.sin(math.pi/2*i/n) for i in range(n+1)]
        for i in range(n):
            y0, y1 = edges[i], edges[i+1]; yc = side*0.5*(y0+y1); w = y1-y0; frac = abs(yc)/half_span
            ch = chord_fn(frac)
            inc = math.radians(incidence_deg + twist_deg*frac)
            x = x0 - yc*math.tan(sweep)*side*0 + abs(yc)*math.tan(sweep)  # sweep sets x aft outboard
            has_ail = frac > ail_frac
            has_flap = flap is not None and frac < flap_frac
            st = {'pos':[round(x,3), round(yc,3), z], 'chord':round(ch*(0.75 if has_ail else 1.0),3),
                  'area':round(ch*w*(0.75 if has_ail else 1.0),4), 'incidenceRad':round(inc,5),
                  'dihedralRad':round(math.radians(dihedral_deg),5), 'airfoil':airfoil,
                  'control':({'surface':'aileron','gain':round(-ail_gain*side*0.45,3)} if has_ail else None),
                  'sweepRad':round(sweep,5)}
            if slat is not None: st['slat']=slat
            if has_flap and flap is not None: st['flap']=flap
            fixed.append(st)
            if has_ail:
                ails.append({'pos':[round(x-0.8*ch,3), round(yc,3), z], 'chord':round(ch*0.25,3),
                    'area':round(ch*w*0.25,4), 'incidenceRad':round(inc,5),
                    'dihedralRad':round(math.radians(dihedral_deg),5), 'airfoil':airfoil,
                    'control':{'surface':'aileron','gain':round(-ail_gain*side,2)}, 'sweepRad':round(sweep,5)})
    return fixed, ails

def tail(x, stab_area, elev_area, fin_area, rud_area, stab_z=0.0, decalage_deg=-3.0,
         fin_zs=None, rud_zs=None, sweep_deg=0):
    fin_zs = fin_zs or [-0.3,-0.7,-1.1]; rud_zs = rud_zs or [0.15,-0.3,-0.7,-1.1]
    sw = round(math.radians(sweep_deg),5)
    n=8
    stab=[{'pos':[x, round(-1.2+2.4*i/(n-1),2), stab_z],'chord':0.7,'area':round(stab_area/n,4),
        'incidenceRad':round(math.radians(decalage_deg),5),'dihedralRad':0.0,'airfoil':'naca0012-like',
        'control':{'surface':'elevator','gain':0.45},'sweepRad':sw} for i in range(n)]
    elev=[{'pos':[x-0.5, round(-1.2+2.4*i/(n-1),2), stab_z],'chord':0.4,'area':round(elev_area/n,4),
        'incidenceRad':0.0,'dihedralRad':0.0,'airfoil':'fin-lowAR','control':{'surface':'elevator','gain':1.0},'sweepRad':sw} for i in range(n)]
    fin=[{'pos':[x,0,z],'chord':0.6,'area':round(fin_area/len(fin_zs),4),'incidenceRad':0.0,
        'dihedralRad':0.0,'airfoil':'naca0012-like','control':{'surface':'rudder','gain':0.45},'sweepRad':sw} for z in fin_zs]
    rud=[{'pos':[x-0.4,0,z],'chord':0.4,'area':round(rud_area/len(rud_zs),4),'incidenceRad':0.0,
        'dihedralRad':0.0,'airfoil':'fin-lowAR','control':{'surface':'rudder','gain':1.0},'sweepRad':sw} for z in rud_zs]
    return stab, elev, fin, rud

def controls(ail, elev, rud):
    return {'aileron':{'maxDeflRad':ail,'rateRadPerSec':3.0,'expo':0.3,'deadZone':0.03},
        'elevator':{'maxDeflRad':elev,'rateRadPerSec':3.0,'expo':0.3,'deadZone':0.03},
        'rudder':{'maxDeflRad':rud,'rateRadPerSec':4.0,'expo':0.2,'deadZone':0.03},
        'spoiler':{'maxDeflRad':0.0,'dragOnly':True,'axis':'throttleLever','axisMap':'aftOnly'}}

def prop(power_w, dia, sign=1, inertia=6.0, eff=0.80, tlz=0.0):
    return {'maxPowerW':power_w,'propDiameterM':dia,'idleRpm':700,'maxRpm':2700,'propInertia':inertia,
        'rotationSign':sign,'efficiency':eff,'thrustLineZ':tlz,'pFactorK':0.35,'slipstreamK':0.12}

def write(idname, disp, mass_kg, cg, inertia, surfaces, fuselage, propulsion, limits, ctl,
          extra_tables=None, vbf=1.0):
    tabs = dict(TABLES)
    if extra_tables: tabs.update(extra_tables)
    c = {'schemaVersion':1,'id':idname,'displayName':disp,
        'mass':{'massKg':mass_kg,'cg':cg,'inertia':inertia},
        'surfaces':surfaces,'airfoilTables':tabs,'controls':ctl,'fuselage':fuselage,
        'propulsion':propulsion,'stallDynamics':DYN,'wakeBlanketMaxLoss':0.7,
        'verticalBlanketFactor':vbf,'gear':[],'limits':limits}
    json.dump(c, open(os.path.join(CFG, idname+'.json'),'w'), indent=2)
    warea = sum(sum(s['area'] for s in sf['strips']) for sf in surfaces if 'wing' in sf['id'])
    print(f"  {idname}: wing {warea:.1f} m2, mass {mass_kg} kg, Ixx {inertia['ixx']}")

def flat_plate_symmetric(clmax=1.1, stall_deg=14):
    """Generic symmetric ±180 table for a given stall angle (jets/thin sections)."""
    import math
    deg=[-180,-160,-140,-120,-100,-90,-75,-60,-45,-30,-20,-15,-10,-5,0,5,10,15,20,30,45,60,75,90,100,120,140,160,180]
    cl,cd,cm=[],[],[]
    for d in deg:
        a=abs(d); s=math.copysign(1,d) if d else 1
        if a<=stall_deg: c=clmax*a/stall_deg
        elif a<=90: c=clmax*0.7*(90-a)/(90-stall_deg)+ (0 if a>60 else 0)
        else: c=-clmax*0.5*(a-90)/90
        cl.append(round(s*c,3))
        cd.append(round(0.008+1.3*math.sin(math.radians(min(a,90)))**2,3))
        cm.append(round(-0.25*(0.25*min(1,(a-stall_deg)/45) if a>stall_deg else 0)*abs(s*c),4) if a<=90 else 0.0)
    return {'alphaRad':[round(math.radians(d),5) for d in deg],'cl':cl,'cd':cd,'cm':cm}

def semi_symmetric(clmax_up=1.35, clmax_inv=1.05, stall_up=15, stall_inv=13, cl0=0.15):
    """Semi-symmetric section (Decathlon NACA 1412-mod): flies inverted but with LESS lift and
    EARLIER stall inverted than upright — cl0>0, asymmetric stall angles/clmax."""
    import math
    deg=[-180,-160,-140,-120,-100,-90,-75,-60,-45,-30,-20,-16,-13,-10,-5,0,5,10,13,15,16,20,30,45,60,75,90,100,120,140,160,180]
    cl,cd,cm=[],[],[]
    for d in deg:
        if d>=0:
            a=d; sm=stall_up; cm_=clmax_up
            if a<=sm: c=cl0+(cm_-cl0)*a/sm
            elif a<=sm+3: c=cm_-(cm_-cm_*0.55)*(a-sm)/3
            elif a<=90: c=cm_*0.55*(90-a)/(90-sm-3)
            else: c=-0.5*(a-90)/90
        else:
            a=-d; sm=stall_inv; cm_=clmax_inv
            if a<=sm: c=-(-cl0+(cm_+cl0)*a/sm)  # inverted: shifted so 0-lift alpha is negative
            elif a<=sm+3: c=-(cm_-(cm_-cm_*0.55)*(a-sm)/3)
            elif a<=90: c=-(cm_*0.55*(90-a)/(90-sm-3))
            else: c=0.5*(a-90)/90
        cl.append(round(c,3))
        cd.append(round(0.008+1.3*math.sin(math.radians(min(abs(d),90)))**2,4))
        aa=abs(d); cp=0.25 if aa<=15 else 0.25+0.2*min(1,(aa-15)/40)
        cn=c*math.cos(math.radians(d))
        cm.append(round(-(cp-0.25)*abs(cn)*(1 if d>0 else -1),4) if aa<=90 else 0.0)
    return {'alphaRad':[round(math.radians(d),5) for d in deg],'cl':cl,'cd':cd,'cm':cm}
