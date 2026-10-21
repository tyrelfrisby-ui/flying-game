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
               ail_frac, ail_gain, sweep_deg=0, flap_frac=0, flap=None, slat=None, x0=0.1,
               ail_chord_frac=0.25):
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
            fixed_frac = 1.0 - ail_chord_frac   # the aileron takes ail_chord_frac of the chord (0.25 unless given)
            st = {'pos':[round(x,3), round(yc,3), z], 'chord':round(ch*(fixed_frac if has_ail else 1.0),3),
                  'area':round(ch*w*(fixed_frac if has_ail else 1.0),4), 'incidenceRad':round(inc,5),
                  'dihedralRad':round(math.radians(dihedral_deg),5), 'airfoil':airfoil,
                  'control':({'surface':'aileron','gain':round(-ail_gain*side*0.45,3)} if has_ail else None),
                  'sweepRad':round(sweep,5)}
            if slat is not None: st['slat']=slat
            if has_flap and flap is not None: st['flap']=flap
            fixed.append(st)
            if has_ail:
                # 0.8*(1-f)/0.75 is EXACTLY 0.8 at f=0.25, so every earlier type stays bit-identical
                ails.append({'pos':[round(x-0.8*(fixed_frac/0.75)*ch,3), round(yc,3), z], 'chord':round(ch*ail_chord_frac,3),
                    'area':round(ch*w*ail_chord_frac,4), 'incidenceRad':round(inc,5),
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
        'elevator':{'maxDeflRad':elev,'rateRadPerSec':3.0,'expo':0.1,'deadZone':0.03},   # owner 2026-10-03: 0.3 felt laggy in the flare
        'rudder':{'maxDeflRad':rud,'rateRadPerSec':4.0,'expo':0.2,'deadZone':0.03},
        'spoiler':{'maxDeflRad':0.0,'dragOnly':True,'axis':'throttleLever','axisMap':'aftOnly'}}

def prop(power_w, dia, sign=1, inertia=6.0, eff=0.80, tlz=0.0):
    return {'maxPowerW':power_w,'propDiameterM':dia,'idleRpm':700,'maxRpm':2700,'propInertia':inertia,
        'rotationSign':sign,'efficiency':eff,'thrustLineZ':tlz,'pFactorK':0.35,'slipstreamK':0.12}

def write(idname, disp, mass_kg, cg, inertia, surfaces, fuselage, propulsion, limits, ctl,
          extra_tables=None, vbf=1.0):
    tabs = dict(TABLES)
    if extra_tables: tabs.update(extra_tables)
    from reversed_flow import fix_table, needs_fix   # (2026-10-12) a wing sliding backwards isn't a barn door
    for t in tabs.values():
        if needs_fix(t): fix_table(t)
    import lowar_plate   # (2026-10-12) a low-AR tail plate broadside is CD ~1.18, not the 2-D 1.66 (Hoerner)
    lowar_plate.fix_tables(tabs)
    import poststall_plate   # (2026-10-12, owner) past the stall a wing is a flat plate: secondary CL peak ~45 deg, ~0 at 90 deg
    poststall_plate.fix_tables(tabs)
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


# ---------------------------------------------------------------------------------------------------------------
# Component mass breakdown -> mass, CG and inertias (NEW-AIRPLANE-PROTOCOL.md step 1c).
# Added 2026-10-20 for the Jenny. Before this, every type's CG and inertia tensor was typed into the builder as a
# finished number, so the breakdown behind it lived only in a document and could drift from the config. This takes
# the breakdown itself and does the arithmetic, so moving one component moves the CG and the inertias with it.
# ---------------------------------------------------------------------------------------------------------------

def item(name, lb, x, z=0.0, y=0.0, y_pair=None, shape=None):
    """One component of the empty weight or the useful load.

    x: station AFT of the datum (the propeller plane, for a single); z: DOWN from the datum (the propeller shaft);
    y: right of centreline, or y_pair=t to split the mass equally at +/-t (a pair of wheels, tip tanks, nacelles).
    shape: its own inertia about its own centre, one of
        ('rod_y', b)       spanwise rod of length b   -- a wing panel, a horizontal tail, a propeller
        ('rod_x', L)       lengthwise rod of length L -- a fuselage
        ('rod_z', h)       vertical rod of length h   -- a fin
        ('box', l, w, h)   a solid box                -- an engine, a fuel tank, a person
        None               a point mass
    """
    return {'name': name, 'lb': lb, 'x': x, 'z': z, 'y': y, 'y_pair': y_pair, 'shape': shape}


def _own_inertia(m, shape):
    """(ixx, iyy, izz) of a component about its OWN centre, in body axes (x fwd, y right, z down)."""
    if shape is None:
        return 0.0, 0.0, 0.0
    kind = shape[0]
    if kind == 'rod_y':
        b = shape[1]; i = m * b * b / 12.0
        return i, 0.0, i          # a rod along y resists roll and yaw, not pitch
    if kind == 'rod_x':
        L = shape[1]; i = m * L * L / 12.0
        return 0.0, i, i
    if kind == 'rod_z':
        h = shape[1]; i = m * h * h / 12.0
        return i, i, 0.0
    if kind == 'box':
        l, w, h = shape[1], shape[2], shape[3]
        return m * (w * w + h * h) / 12.0, m * (l * l + h * h) / 12.0, m * (l * l + w * w) / 12.0
    raise ValueError('unknown shape ' + kind)


def mass_properties(items, verbose=True):
    """Returns (mass_kg, cg_x_aft_of_datum, cg_z_below_datum, inertia_dict). The inertia dict is about the CG in
    config axes (x FORWARD, y right, z DOWN) and is what goes straight into B.write().

    Ixz is the product of inertia I_xz = sum(m x z) in those axes, which is the sign RigidBody6DOF's equations of
    motion expect (they carry +Ixz, with the tensor's off-diagonal being -Ixz)."""
    mass = sum(i['lb'] for i in items) * 0.45359237
    tot = sum(i['lb'] for i in items)
    cgx = sum(i['lb'] * i['x'] for i in items) / tot
    cgz = sum(i['lb'] * i['z'] for i in items) / tot
    ixx = iyy = izz = ixz = 0.0
    rows = []
    for it in items:
        m = it['lb'] * 0.45359237
        x = cgx - it['x']        # config x is FORWARD-positive
        z = it['z'] - cgz        # config z is DOWN-positive
        ys = [(m, it['y'])] if it['y_pair'] is None else [(m / 2, -it['y_pair']), (m / 2, it['y_pair'])]
        ox, oy, oz = _own_inertia(m, it['shape'])
        dxx = sum(mm * (yy * yy + z * z) for mm, yy in ys) + ox
        dyy = m * (x * x + z * z) + oy
        dzz = sum(mm * (x * x + yy * yy) for mm, yy in ys) + oz
        dxz = m * x * z
        ixx += dxx; iyy += dyy; izz += dzz; ixz += dxz
        rows.append((it['name'], it['lb'], m, x, z, dxx, dyy, dzz))
    if verbose:
        print(f"  {'component':<34}{'lb':>7}{'kg':>8}{'x':>8}{'z':>7}{'Ixx':>9}{'Iyy':>9}{'Izz':>9}")
        for n, lb, m, x, z, a, b, c in rows:
            print(f"  {n:<34}{lb:>7.0f}{m:>8.1f}{x:>8.3f}{z:>7.3f}{a:>9.0f}{b:>9.0f}{c:>9.0f}")
        print(f"  {'TOTAL':<34}{tot:>7.0f}{mass:>8.1f}  CG {cgx:.3f} m aft, {cgz:.3f} m below the datum")
    return mass, cgx, cgz, {'ixx': round(ixx), 'iyy': round(iyy), 'izz': round(izz), 'ixz': round(ixz)}


def radii_of_gyration(mass, inertia, span, length):
    """Protocol §1c sanity check: R = 2*sqrt(I/m)/reference. Bands Rx 0.25-0.30, Ry 0.35-0.40, Rz 0.38-0.45."""
    rx = 2 * math.sqrt(inertia['ixx'] / mass) / span
    ry = 2 * math.sqrt(inertia['iyy'] / mass) / length
    rz = 2 * math.sqrt(inertia['izz'] / mass) / ((span + length) / 2)
    return rx, ry, rz
