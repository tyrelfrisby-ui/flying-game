"""The runtime rig for the own 172 (docs/models/c172-like-own-rig.json), binding Codex's panel nodes
(docs/models/c172-panel-rig.md) to the params FlyingGame.Core.Instruments writes (docs/C172-INSTRUMENTS.md).

Pivots are read from the GLB's own node translations, so a re-exported panel only needs this re-run:
    python3 tools/own_models/c172_instrument_rig.py \
        unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb docs/models/c172-like-own-rig.json

Dial calibrations follow tools/own_models/c172_cockpit.py. Maps are additive motion from the baked
mesh (not absolute angles); model records the parameter at the authored pose. VSI is drawn at its
left-pointing zero, so its additive map and model are both zero at 0 fpm. ASI/tach/ADF retain their
explicit baked-angle corrections. Regenerate after every export to read the new pivots.
"""
import json,struct,sys
glb=sys.argv[1]; out=sys.argv[2]
f=open(glb,'rb').read(); L=struct.unpack('<I',f[12:16])[0]; j=json.loads(f[20:20+L])
T={n.get('name'):n.get('translation',[0,0,0]) for n in j['nodes']}
X=[1,0,0]; Y=[0,1,0]; Z=[0,0,1]   # glTF: +X forward (this model), +Y up, +Z right
rows=[]
def row(node,param,kind,axis,mp,desc,parent="",wrap=0,extrap=0,model=0.0):
    pv=T[node] if not parent else [a+b for a,b in zip(T[parent],T[node])]
    extra={}
    if wrap: extra['rig_map_wrap']=wrap
    if extrap: extra['rig_map_extrapolate']=1
    rows.append(dict(node=node,parent=parent or None,param=param,kind=kind,axis_gltf=axis,pivot_gltf=[round(v,4) for v in pv],
                     map=mp,model=model,desc=desc,extra=extra))
CW="+deg = clockwise as seen from the seat"
# ---- the 172 instruments (FlyingGame.Core.Instruments) --------------------------------------------------------
row('C172_AI_roll','ai_bank_deg','rot',X,[[-180,180],[180,-180]],"AI horizon/roll assembly: rolls the OPPOSITE way to the indicated bank (right bank = horizon tilts anticlockwise); "+CW)
row('C172_AI_pitch','ai_pitch_deg','move',Y,[[-25,.020],[25,-.020]],"Flat horizon slide: 0.8 mm per degree; pitch up moves horizon DOWN, matching 5/10/15/20 degree ladder",parent='C172_AI_roll')
row('C172_DG_card','dg_card_deg','rot',X,[[0,0],[360,-360]],"DG card: drawn N at 12 o'clock, numbers clockwise; the reading sits under the top lubber, so the card turns anticlockwise by it; "+CW,wrap=360)
row('C172_DG_sync_adjust','dg_knob_deg','rot',X,[[0,0],[360,360]],"DG push-and-turn knob, 1:1 with the card change it made",wrap=360)
row('C172_TC_airplane','tc_needle','rot',X,[[-2,-40],[2,40]],"TC little aeroplane: 1 rate (standard rate) = 20 deg right wing down at the L/R standard-rate index; stops at 2 rates; "+CW)
row('C172_TC_slip_ball','slip_ball','move',Z,[[-1,-0.015],[1,0.015]],"inclinometer ball, +1 = right end of the tube (12 deg of apparent-gravity lean)")
for i in (1,2):
    row(f'C172_NAV{i}_CDI',f'nav{i}_cdi','move',Z,[[-1,-0.018],[1,0.018]],f"NAV{i} course needle: +1 = full scale right (course to the right); VOR 10 deg, localizer tailored")
    row(f'C172_NAV{i}_OBS_card',f'nav{i}_obs_deg','rot',X,[[0,0],[360,-360]],f"NAV{i} OBS card: the selected course under the top index, so the card turns anticlockwise by it",wrap=360)
    row(f'C172_NAV{i}_OBS',f'nav{i}_obs_deg','rot',X,[[0,0],[360,360]],f"NAV{i} OBS knob, 1:1",wrap=360)
    row(f'C172_NAV{i}_NAV_flag',f'nav{i}_flag','rot',X,[[0,90],[1,0]],f"NAV{i} warning flag: legend upright (as drawn) when the flag is IN view (1), turned 90 deg out of it when the receiver has a usable signal")
    row(f'C172_NAV{i}_TO_FROM_flag',f'nav{i}_tofrom','rot',X,[[-1,60],[0,90],[1,0]],f"NAV{i} TO/FROM: TO = legend upright, OFF = turned 90; FROM = 60 deg (PLACEHOLDER: the window has one 'TO_FROM' legend; it needs separate TO and FROM)")
row('C172_NAV1_GS','nav1_gs','move',Y,[[-1,-0.018],[1,0.018]],"NAV1 glideslope needle: +1 = full scale UP (fly up), 0.7 deg")
row('C172_NAV1_GS_flag','nav1_gs_flag','rot',X,[[0,90],[1,0]],"NAV1 GS flag: upright = in view (no usable glideslope)")
row('C172_ADF_bearing','adf_needle_deg','rot',X,[[0,35],[360,395]],"ADF needle: relative bearing clockwise from 12 o'clock; drawn at -35 deg, hence the 35 offset; "+CW,wrap=360,model=-35)
row('C172_ADF_card_adjust','adf_card_deg','rot',X,[[0,0],[360,360]],"ADF card knob, 1:1 with the card (the ADF face's card is drawn fixed; no rotating card node yet)",wrap=360)
row('C172_Compass_card','compass_card_deg','rot',Y,[[0,0],[360,360]],"whisky compass card about the vertical: relative to the case it turns anticlockwise-from-above by the reading (it holds north while the aeroplane turns); +deg = right-handed about up",wrap=360)
row('C172_Suction_needle','suction_inhg','rot',X,[[0,0],[10,270]],"suction gauge (1 in, yoke-v1): 0-10 inHg over 270 deg = 27 deg/inHg clockwise from 7:30; green 4.5-5.4 with red radials at both ends; drawn at 0 inHg",model=0)
# ---- the rest of the six-pack and the engine cluster, from the params the sim already supplies ----------------
row('C172_Airspeed_needle','airspeed_mph','rot',X,[[0,55],[200,415]],"ASI, MPH face: 1.8 deg/mph from 12 o'clock (labels every 20 mph at 36 deg, arcs at tau*v/200); drawn at -55 deg",model=-55/1.8)
row('C172_Altimeter_needle','altitude_ft','rot',X,[[0,0],[1000,360]],"altimeter long hand, 1 turn per 1,000 ft (unbounded); drawn at 0",extrap=1)
row('C172_Altimeter_1000','altitude_ft','rot',X,[[0,20],[10000,380]],"altimeter short hand, 1 turn per 10,000 ft; drawn at -20 deg",extrap=1)
row('C172_Altimeter_10000','altitude_ft','rot',X,[[0,5],[100000,365]],"altimeter 10,000 ft pointer, 1 turn per 100,000 ft; drawn at -5 deg",extrap=1)
row('C172_VSI_needle','vsi_fpm','rot',X,[[-2000,-170],[-1000,-90],[0,0],[1000,90],[2000,170]],"VSI: drawn zero at 9 o'clock (LEFT); +/-500 fpm at +/-45 clockwise degrees from zero; 1000 at top/bottom; 2000 near right",model=0)
row('C172_Tach_needle','rpm','rot',X,[[0,35],[3500,350]],"tachometer: 0-35 hundred rpm every 45 deg = 0.09 deg/rpm; drawn at -35 deg")
row('C172_Oil_press_needle','oil_press_psi','rot',X,[[0,35],[100,323]],"oil pressure: 0-100 psi, 72 deg per 25 psi; drawn at -35 deg")
row('C172_Oil_temp_needle','oil_temp_f','rot',X,[[75,35],[100,107],[150,179],[200,251],[245,323]],"oil temperature: labels 75/100/150/200/245 F every 72 deg (non-linear face); drawn at -35 deg")
for s in 'LR':
    row(f'C172_Fuel_{s}_needle','fuel_centre_frac','rot',X,[[0,35],[1,323]],f"fuel {s}: E..F over 288 deg (72 deg per quarter); the sim has one fuel fraction; drawn at -35 deg")
# Fixed-pitch trainer: throttle and mixture bind as plain plungers. ModelRig.TryGet has no carb-heat,
# ammeter or EGT param: keep those three authored nodes unbound rather than inventing signals.
for node,param in [('C172_Throttle','throttle'),('C172_Mixture','mixture')]:
    row(node,param,'move',X,[[0,0],[1,0.06]],'Plain push-pull plunger: +X pushes into the panel; no twist')
doc=dict(model='c172-like-own',livery='v4',
  source="Authored 172 v4 panel (v3 with the NAV heads and engine column swapped): tools/own_models/c172_cockpit.py, lane/claude-172-instruments; docs/models/c172-instruments-v4.md. Runtime maps target existing ModelRig inputs only. Pivots generated from the final GLB node translations by tools/own_models/c172_instrument_rig.py.",
  rig=rows)
open(out,'w').write(json.dumps(doc,indent=2)+'\n')
print(len(rows),'rows')
