"""Measure where each library instrument mounts in a model (tools/own_models/instruments): the dial centre (the old needle's
pivot, or the old face's centre), the face radius, and how far in front of the centre the nearest thing the library cannot
switch off reaches. The numbers are copied by hand into FlyingGame.Core.Instruments.InstrumentLibrary; InstrumentLibraryTests
holds the table against the GLBs.
CAUTION: this samples VERTICES. A face that is one big quad or a closed disc (the Great Lakes' Dial_* discs, 3 mm thick,
pilot face 3.0 mm proud) has none inside the radius and reads as "nothing in front": check the old dial's own
triangles before trusting frontVisible (2026-11-05: the first GL library faces sat 2.4 mm behind the old ones).    /usr/bin/python3 tools/own_models/instruments/measure_mounts.py
"""
import sys,json,numpy as np
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from glb_visuals import Glb
M=str(Path(__file__).resolve().parents[3]/'unity/Assets/_Project/Resources/Models')+'/'
# (glb, slot, pivot node, normal toward pilot, radius-from node or number, hide list)
MOUNTS=[
 ('extra-300-like/extra-300-like-own.glb','G_R','Needle_G_R',(1,0,0),'Face_P3b_G_R',['Bezel_P3b_G_R','Face_P3b_G_R','Needle_G_R','Needle_GMax_R','Needle_GMin_R']),
 ('extra-300-like/extra-300-like-own.glb','G_F','Needle_G_F',(1,0,0),'Face_P3b_G_F',['Bezel_P3b_G_F','Face_P3b_G_F','Needle_G_F','Needle_GMax_F','Needle_GMin_F']),
 ('great-lakes-like/great-lakes-like-own.glb','G_R','Needle_G_R',(1,0,0),0.036,['Needle_G_R','Needle_GMax_R','Needle_GMin_R']),
 ('pitts-s2b-like/pitts-s2b-like-own.glb','G_R','RearPanel_G_meter_Face',(1,0,0),'RearPanel_G_meter_Face',['RearPanel_G_meter_Face','RearPanel_G_meter_Bezel']),
 ('pitts-s2b-like/pitts-s2b-like-own.glb','G_F','FrontPanel_G_meter_Face',(1,0,0),'FrontPanel_G_meter_Face',['FrontPanel_G_meter_Face','FrontPanel_G_meter_Bezel']),
 ('c172-like/c172-like-own.glb','TC','C172_TC_airplane',(-1,0,0),'C172_Turn_coordinator_case',['C172_TC_airplane','C172_TC_rate_mark','C172_TC_rate_mark.001','C172_TC_slip_ball','C172_Turn_coordinator_case']),
 ('c172-like/c172-like-own.glb','ASI','C172_Airspeed_needle',(-1,0,0),'C172_Airspeed_case',['C172_Airspeed_needle','C172_Airspeed_case']),
 ('c172-like/c172-like-own.glb','ALT','C172_Altimeter_needle',(-1,0,0),'C172_Altimeter_case',['C172_Altimeter_needle','C172_Altimeter_1000','C172_Altimeter_10000','C172_Altimeter_case']),
 ('c172-like/c172-like-own.glb','VSI','C172_VSI_needle',(-1,0,0),'C172_VSI_case',['C172_VSI_needle','C172_VSI_case']),
 ('c172-like/c172-like-own.glb','DG','C172_DG_card',(-1,0,0),'C172_DG_case',['C172_DG_card','C172_DG_heading_bug','C172_DG_case']),
 ('c172-like/c172-like-own.glb','AI','C172_AI_pitch',(-1,0,0),'C172_Attitude_case',['C172_AI_roll','C172_AI_pitch','C172_Attitude_case']),
 ('extra-300-like/extra-300-like-own.glb','TC_R','Needle_TC_R',(1,0,0),'Face_P3b_TC_R',['Bezel_P3b_TC_R','Face_P3b_TC_R','Needle_TC_R','Ball_TC_R']),
 ('extra-300-like/extra-300-like-own.glb','ASI_R','Needle_ASI_R',(1,0,0),'Face_P3b_ASI_R',['Bezel_P3b_ASI_R','Face_P3b_ASI_R','Needle_ASI_R']),
 ('extra-300-like/extra-300-like-own.glb','ASI_F','Needle_ASI_F',(1,0,0),'Face_P3b_ASI_F',['Bezel_P3b_ASI_F','Face_P3b_ASI_F','Needle_ASI_F']),
 ('extra-300-like/extra-300-like-own.glb','ALT_R','Needle_Alt100_R',(1,0,0),'Face_P3b_Altimeter_R',['Bezel_P3b_Altimeter_R','Face_P3b_Altimeter_R','Needle_Alt100_R','Needle_Alt1000_R']),
 ('extra-300-like/extra-300-like-own.glb','ALT_F','Needle_Alt100_F',(1,0,0),'Face_P3b_Altimeter_F',['Bezel_P3b_Altimeter_F','Face_P3b_Altimeter_F','Needle_Alt100_F','Needle_Alt1000_F']),
 ('great-lakes-like/great-lakes-like-own.glb','TC_F','TC_Symbol_F',(1,0,0),0.02574,['TC_Symbol_F','TC_Ball_F']),
 ('great-lakes-like/great-lakes-like-own.glb','TC_R','TC_Symbol_R',(1,0,0),0.036,['TC_Symbol_R','TC_Ball_R']),
 ('great-lakes-like/great-lakes-like-own.glb','ASI_F','Needle_ASI_F',(1,0,0),0.03573,['Needle_ASI_F']),
 ('great-lakes-like/great-lakes-like-own.glb','ASI_R','Needle_ASI_R',(1,0,0),0.036,['Needle_ASI_R']),
 ('great-lakes-like/great-lakes-like-own.glb','ALT_F','Needle_Alt100_F',(1,0,0),0.03573,['Needle_Alt100_F','Needle_Alt1000_F']),
 ('great-lakes-like/great-lakes-like-own.glb','ALT_R','Needle_Alt100_R',(1,0,0),0.036,['Needle_Alt100_R','Needle_Alt1000_R']),
 ('pitts-s2b-like/pitts-s2b-like-own.glb','ASI_R','RearPanel_Airspeed_Face',(1,0,0),'RearPanel_Airspeed_Face',['RearPanel_Airspeed_Face','RearPanel_Airspeed_Bezel']),
 ('pitts-s2b-like/pitts-s2b-like-own.glb','ALT_R','RearPanel_Altimeter_Face',(1,0,0),'RearPanel_Altimeter_Face',['RearPanel_Altimeter_Face','RearPanel_Altimeter_Bezel']),
 ('pitts-s2b-like/pitts-s2b-like-own.glb','ASI_F','FrontPanel_Airspeed_Face',(1,0,0),'FrontPanel_Airspeed_Face',['FrontPanel_Airspeed_Face','FrontPanel_Airspeed_Bezel']),
 ('pitts-s2b-like/pitts-s2b-like-own.glb','ALT_F','FrontPanel_Altimeter_Face',(1,0,0),'FrontPanel_Altimeter_Face',['FrontPanel_Altimeter_Face','FrontPanel_Altimeter_Bezel']),
]
def wverts(g,w,i):
    n=g.doc['nodes'][i]; out=[]
    if 'mesh' not in n: return np.zeros((0,3))
    for p in g.doc['meshes'][n['mesh']]['primitives']:
        a=g.array(p['attributes']['POSITION']); out.append((w[i]@np.c_[a,np.ones(len(a))].T).T[:,:3])
    return np.vstack(out)
cache={}
for glb,slot,piv,nrm,rad,hide in MOUNTS:
    if glb not in cache:
        g=Glb(M+glb); cache[glb]=(g,g.world_matrices(),{n.get('name'):i for i,n in enumerate(g.doc['nodes'])})
    g,w,idx=cache[glb]; n=np.array(nrm,float)
    if piv not in idx: print(glb,slot,'NO NODE',piv); continue
    pv=wverts(g,w,idx[piv]) if piv.endswith('Face') else None
    c=w[idx[piv]][:3,3] if pv is None else (pv.min(0)+pv.max(0))/2
    if isinstance(rad,str):
        v=wverts(g,w,idx[rad]); d=v-c; lat=d-np.outer(d@n,n); R=np.linalg.norm(lat,axis=1).max()
    else: R=rad
    hidden={idx[h] for h in hide if h in idx}
    missing=[h for h in hide if h not in idx]
    # front-most visible static geometry over the face disc, and legacy face plane
    front=-1; facepl=None
    for i in w:
        if i in hidden or 'mesh' not in g.doc['nodes'][i]: continue
        v=wverts(g,w,i); d=v-c; ax=d@n; lat=np.linalg.norm(d-np.outer(ax,n),axis=1)
        k=(lat<R*0.9)&(ax>-0.02)&(ax<0.03)
        if k.any(): 
            m=ax[k].max()
            if m>front: front=m; who=g.doc['nodes'][i].get('name')
    hv=np.vstack([wverts(g,w,i) for i in hidden]) if hidden else np.zeros((0,3))
    hmax=((hv-c)@n).max() if len(hv) else None; hmin=((hv-c)@n).min() if len(hv) else None
    print(f"{glb.split('/')[0]:18s} {slot:6s} c=({c[0]:.4f},{c[1]:.4f},{c[2]:.4f}) R={R:.4f} frontVisible={front*1000:.1f}mm({who if front>-1 else '-'}) hidden ax[{(hmin or 0)*1000:.1f},{(hmax or 0)*1000:.1f}]mm missing={missing}")
