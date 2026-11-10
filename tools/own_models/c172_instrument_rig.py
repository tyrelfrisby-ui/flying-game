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
# The six-pack's moving parts (ASI, AI, altimeter, TC, DG, VSI) are the shared instrument library's
# (FlyingGame.Core.Instruments.InstrumentLibrary.C172, Bridge/SharedInstruments), driven from the same params; not rigged here.
row('C172_DG_sync_adjust','dg_knob_deg','rot',X,[[0,0],[360,360]],"DG push-and-turn knob, 1:1 with the card change it made",wrap=360)
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
