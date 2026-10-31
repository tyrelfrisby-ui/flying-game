"""Round-3 Extra panels and Great Lakes eye review. CPU meshes only; no asset edits.
python3 tools/own_models/render_iac_round3.py [--extra | --great-lakes]
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from glb_visuals import Glb
from sequence_card import ROOT
import render_iac_visuals as cpu

OUT=ROOT/'renders/iac-pitts/v3'
MODELS=ROOT/'unity/Assets/_Project/Resources/Models'
REFS=Path.home()/'Developer/agent-lanes/aero-playground/refs'

def record(**row):
    path=OUT/'render-manifest.json'
    manifest=json.loads(path.read_text()) if path.exists() else {}
    manifest.update(row);path.write_text(json.dumps(manifest,indent=2)+'\n')

def extra():
    specs=json.loads((ROOT/'docs/models/sequence-card-spec.json').read_text())
    e=specs['extra-300-like']['rear_eye'];eye=np.array(e['gltf_m']);target=np.array([1.202,.350,0]);rows=[]
    only=lambda n:n=='Cockpit' or n.startswith(('RearPanel','SequenceCard'))
    clip=lambda name,a:name!='Cockpit' or (a[:,0].min()>=.997 and a[:,0].max()<=1.227 and a[:,1].min()>=-.01)
    for path in sorted((MODELS/'extra-300-like').glob('*own*.glb')):
        liv=path.stem.replace('extra-300-like-own','').lstrip('-') or 'talk-flying'
        straight=f'extra-{liv}-upper-panel-straight.png';perspective=f'extra-{liv}-rear-eyepoint.png'
        cpu.render(path,OUT/straight,(2.202,.288,0),(1.202,.288,0),.82,only,'Extra '+liv+' UPPER card; original recessed lower panel',triangle_filter=clip,image_size=(2400,1800))
        cpu.render(path,OUT/perspective,eye,target,86,label='Extra '+liv+' rear eye, upper panel centred',perspective=True)
        rows.append(dict(model=str(path.relative_to(ROOT)),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),eye_gltf=eye.tolist(),target_gltf=target.tolist(),horizontal_fov=86,renders=[straight,perspective]))
    with tempfile.TemporaryDirectory(prefix='iac-v3-before-') as td:
        path=Path(td)/'extra.glb';rel='unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own-unmarked.glb'
        path.write_bytes(subprocess.check_output(['git','show','1a6a4d7:'+rel],cwd=ROOT))
        cpu.render(path,OUT/'extra-rear-panels-before-v3.png',(2.202,.288,0),(1.202,.288,0),.82,only,'Extra BEFORE 1a6a4d7; same straight-on camera',triangle_filter=clip,image_size=(2400,1800))
    record(extra=rows,extra_before=dict(commit='1a6a4d7',render='extra-rear-panels-before-v3.png',orthographic_width_m=.82))

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--extra',action='store_true');ap.add_argument('--great-lakes',action='store_true');args=ap.parse_args()
    OUT.mkdir(parents=True,exist_ok=True)
    if args.extra or not args.great_lakes:extra()
    if args.great_lakes or not args.extra:great_lakes()

def parts(glb):
    for index,matrix in glb.world_matrices().items():
        node=glb.doc['nodes'][index]
        if 'mesh' not in node:continue
        for prim in glb.doc['meshes'][node['mesh']]['primitives']:
            pos=glb.array(prim['attributes']['POSITION'])@matrix[:3,:3].T+matrix[:3,3]
            ids=glb.array(prim['indices']).reshape(-1,3) if 'indices' in prim else np.arange(len(pos)).reshape(-1,3)
            yield node['name'],glb.doc['materials'][prim['material']].get('name',''),pos,ids

def project(camera,point):
    v=(np.array(point)-camera['eye'])@camera['basis']
    factor=camera['w']/camera['scale']
    if camera['perspective']:factor=camera['w']/(2*np.tan(np.radians(camera['scale'])/2))/v[2]
    return np.array([camera['w']/2+v[0]*factor,camera['h']/2-v[1]*factor])

def font(size):
    path=Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/pdfjs-dist/standard_fonts/LiberationSans-Regular.ttf'
    return ImageFont.truetype(str(path),size) if path.exists() else ImageFont.load_default(size=size)

COLORS=dict(eye='#ffdf00',picked='#ff36ab',sill='#00e7f2',alternative='#ff9b21',head='#39caff',panel='#86ff74')

def overlay(camera,points,vertices,metrics,transform,style='side',label_suffix=''):
    base=Image.open(camera['path']);w,h=base.size
    # Footer lives below the image, so it cannot hide the seat pan/fit evidence.
    im=Image.new('RGB',(w,h+185),'#142032');im.paste(base,(0,0));draw=ImageDraw.Draw(im)
    M,tr=transform;scale=abs(M[0,0]);eye=points['eye'];f=font(22)
    def text(at,label,color='white'):
        draw.text(tuple(at),label,font=f,fill=color,stroke_width=3,stroke_fill='#111b28')
    def line(a,b,color,width=3):draw.line([tuple(a),tuple(b)],fill=color,width=width)
    # Wire sphere is an explicit scale proxy, drawn through the cutaway (not an occupant).
    center=eye+np.linalg.solve(M,np.array([-.08,0,-.05]));r=.105/scale
    for axis in range(3):
        angles=np.linspace(0,2*np.pi,100);ring=[]
        for t in angles:
            v=np.zeros(3);v[(axis+1)%3]=r*np.cos(t);v[(axis+2)%3]=r*np.sin(t);ring.append(tuple(project(camera,center+v)))
        draw.line(ring,fill=COLORS['head'],width=2)
    for latitude in (-.6,0,.6):
        ring=[tuple(project(camera,center+[r*np.sqrt(1-latitude**2)*np.cos(t),r*latitude,r*np.sqrt(1-latitude**2)*np.sin(t)])) for t in np.linspace(0,2*np.pi,100)]
        draw.line(ring,fill=COLORS['head'],width=1)
    # Picked actual top-row vertices are distinct from the centre/midpoint reference.
    for name,row in vertices.items():
        color=COLORS['picked' if name=='picked' else 'alternative']
        for p in row:
            at=project(camera,p);draw.ellipse((at[0]-7,at[1]-7,at[0]+7,at[1]+7),outline=color,width=3)
        if len(row)>1:line(project(camera,row[0]),project(camera,row[-1]),color,4)
    # Dimension arrows, including extension to the panel PLANE above its physical top edge.
    panel_h=np.array([points['panel'][0],eye[1],eye[2]])
    line(project(camera,[points['panel'][0],points['panel'][1],0]),project(camera,panel_h),COLORS['panel'],2)
    dimensions=[('panel_h',eye,panel_h,'panel plane horizontal',COLORS['panel'],(60,-105)),
                ('panel_line',eye,points['panel'],'panel centre direct',COLORS['panel'],(25,15)),
                ('picked_line',eye,points['picked'],'picked back direct',COLORS['picked'],(-240,20)),
                ('sill_line',eye,points['sill'],'sill direct',COLORS['sill'],(150,-30))]
    if style=='top':dimensions=dimensions[:1]+dimensions[2:]
    for key,a,b,label,color,offset in dimensions:
        aa=project(camera,a);bb=project(camera,b);line(aa,bb,color)
        direction=bb-aa;length=np.linalg.norm(direction)
        if length>1:
            d=direction/length;normal=np.array([-d[1],d[0]])
            for endpoint,sign in ((aa,1),(bb,-1)):
                line(endpoint,endpoint+sign*d*12+normal*6,color);line(endpoint,endpoint+sign*d*12-normal*6,color)
        text((aa+bb)/2+offset,f'{label} {metrics[key]:.3f} m',color)
    offsets={'eye':(-180,-60),'picked':(-230,15),'sill':(15,15),'panel':(15,15),'alternative':(-270,-35)}
    for name,point in points.items():
        at=project(camera,point);color=COLORS.get(name,'white')
        draw.ellipse((at[0]-8,at[1]-8,at[0]+8,at[1]+8),fill=color,outline='black',width=2)
        label={'eye':'EYE (unchanged)','picked':'PICKED seat-back top','sill':'Windshield sill','panel':'Rear panel centre','alternative':'This lane actual back top'}[name]
        text(at+offsets[name],label,color)
    # Separate legend includes offsets and avoids implying a certified human/camera fit.
    draw.rectangle((12,h+5,w-12,h+177),fill='#142032')
    lines=[f'Great Lakes rear eye review {label_suffix} | all distances body metres, source span scale {scale:.6f}',
           'Head proxy: radius 0.105 m; centre 0.080 m aft + 0.050 m above EYE; wire is an x-ray scale aid.',
           'Pink: source-picked seat-shell top row. Orange: actual older lane seat-back top; alternative is NOTE ONLY.',
           f"Picked back: {metrics['picked_horizontal']:.3f} m aft / {metrics['picked_vertical']:.3f} m below eye. Sill: {metrics['sill_horizontal']:.3f} m ahead / {metrics['sill_vertical']:.3f} m below.",
           'Horizontal panel dimension ends on its EXTENDED plane at eye height; the actual panel is below the eye.']
    for i,label in enumerate(lines):draw.text((24,h+12+i*32),label,font=font(21),fill='white')
    im.save(camera['path'])

def vertical_pan_height(glb,x,source):
    hits=[]
    for name,material,pos,ids in parts(glb):
        if source:
            if name!='Cockpit_Interior' or material!='Cockpit_cushion':continue
        elif name!='Cockpit':continue
        for tri in pos[ids]:
            if not source:
                if not (tri[:,0].min()>1.73 and tri[:,0].max()<2.07 and tri[:,1].min()>-.65 and tri[:,1].max()<-.54):continue
                normal=np.cross(tri[1]-tri[0],tri[2]-tri[0]);normal/=max(np.linalg.norm(normal),1e-12)
                # Original rigid pan is pitched 6 degrees; exclude the flat crotch strap above it.
                if abs(abs(normal[0])-np.sin(np.radians(6)))>1e-4 or abs(normal[2])>1e-5:continue
            a=tri[0,[0,2]];b=tri[1,[0,2]];c=tri[2,[0,2]]
            matrix=np.column_stack((b-a,c-a))
            if abs(np.linalg.det(matrix))<1e-12:continue
            u,v=np.linalg.solve(matrix,np.array([x,0])-a)
            if u>=-1e-7 and v>=-1e-7 and u+v<=1+1e-7:
                y=tri[0,1]+u*(tri[1,1]-tri[0,1])+v*(tri[2,1]-tri[0,1])
                if y<-.3:hits.append(y)
    assert hits,'seat pan/cushion ray missed'
    return max(hits)

def great_lakes():
    entry=next(e for e in json.loads((REFS/'eyepoints-v2-great-lakes-full.json').read_text())['entries'] if e['seat_id']=='rear')
    tf=entry['transform_model_to_body'];M=np.array(tf['raw_model_native_to_body_matrix']);tr=np.array(tf['raw_model_to_body_translation_m']);scale=abs(M[0,0])
    body=lambda a:np.asarray(a)@M.T+tr
    native=lambda b:np.linalg.solve(M,np.array(b)-tr)
    eye=native(entry['position_body']);mi=entry['measured_inputs']
    picked=native(mi['seat_back_top']['position_body']);sill=native(mi['windshield_sill']['position_body'])
    lane=MODELS/'great-lakes-like/great-lakes-like-own.glb';allmetrics={};renders=[]
    with tempfile.TemporaryDirectory(prefix='iac-v3-gl-') as td:
        sourcepath=Path(td)/'source.glb';sourcepath.write_bytes(subprocess.check_output(['git','show','4a45232:'+str(lane.relative_to(ROOT))],cwd=ROOT))
        assert hashlib.sha256(sourcepath.read_bytes()).hexdigest()==entry['model_sha256']
        local=Glb(lane);reference=Glb(sourcepath)
        # Reproduce exactly the source's material/part/FRU selector; no headrest substitute.
        selected=[];box=np.array(mi['seat_back_top']['selection']['top_search_box_native_fru'])
        for name,material,p,ids in parts(reference):
            if name!=mi['seat_back_top']['part'] or material!=mi['seat_back_top']['material']:continue
            fru=np.column_stack((-p[:,0],-p[:,2],p[:,1]));mask=np.all((fru>=box[0])&(fru<=box[1]),axis=1);selected.extend(p[mask])
        selected=np.unique(selected,axis=0);top=selected[np.isclose(selected[:,1],selected[:,1].max(),atol=1e-6)]
        actual=top.mean(0);assert np.linalg.norm(actual-picked)<2e-5
        # Older lane has mixed material tags: identify actual old rear shell top geometrically.
        oldvertices=np.concatenate([p for n,m,p,i in parts(local) if n=='Cockpit'])
        region=oldvertices[(oldvertices[:,0]>2.0)&(oldvertices[:,0]<2.5)&(abs(oldvertices[:,2])<.221)&(oldvertices[:,1]>.08)&(oldvertices[:,1]<.15)]
        oldtop=np.unique(region[np.isclose(region[:,1],region[:,1].max(),atol=1e-6)],axis=0);oldcentre=oldtop.mean(0)
        alternative_body=np.array(entry['position_body']);alternative_body[0]=body(oldcentre)[0]+.254
        allmetrics['reference']=dict(raw_model_to_body_matrix=M.tolist(),raw_model_to_body_translation_m=tr.tolist(),eye_body=entry['position_body'],eye_gltf=eye.tolist(),picked_body=body(actual).tolist(),picked_gltf=actual.tolist(),picked_vertices_gltf=top.tolist(),picked_selector=mi['seat_back_top']['selection'],sill_body=mi['windshield_sill']['position_body'],sill_gltf=sill.tolist(),lane_actual_back_gltf=oldcentre.tolist(),lane_actual_back_body=body(oldcentre).tolist(),alternative_eye_body_NOTE_ONLY=alternative_body.tolist(),alternative_eye_gltf_NOTE_ONLY=native(alternative_body).tolist(),source_model_commit=entry['model_commit'],head_proxy_body_radius_m=.105,head_proxy_body_offset_m=[-.08,0,-.05],eye_changed=False)
        for source,path,g in [(False,lane,local),(True,sourcepath,reference)]:
            title='source-4a45232' if source else 'this-lane';prefix='great-lakes-source-4a45232' if source else 'great-lakes'
            panel_x=1.638 if source else 1.6395
            # Actual centreline top/bottom of each slab, not card-centre substitution.
            rows=[]
            for n,m,p,ids in parts(g):
                if n!=('Cockpit_Interior' if source else 'Cockpit'):continue
                if source and m!='Cockpit_panel':continue
                for tri in p[ids]:
                    if np.all(abs(tri[:,0]-panel_x)<2e-6):rows.extend(tri)
            rows=np.array(rows);panel=np.array([panel_x,(rows[:,1].min()+rows[:,1].max())/2,0])
            # Avoid unrelated coplanar box faces by reporting the measured slab bounds for audit.
            assert abs(panel[1]-(.051 if source else .005))<1e-5,(title,panel)
            norm=lambda a,b:float(np.linalg.norm(body(a)-body(b)))
            pan_y=vertical_pan_height(g,eye[0],source)
            wing=np.concatenate([p for n,m,p,i in parts(g) if n=='Wing_Upper']);near=wing[abs(wing[:,2])<.01];te=near[np.argmax(near[:,0])]
            metrics=dict(panel_h=float((eye[0]-panel_x)*scale),panel_line=norm(eye,panel),picked_line=norm(eye,picked),picked_horizontal=float((picked[0]-eye[0])*scale),picked_vertical=float((eye[1]-picked[1])*scale),sill_line=norm(eye,sill),sill_horizontal=float((eye[0]-sill[0])*scale),sill_vertical=float((eye[1]-sill[1])*scale),eye_above_pan=float((eye[1]-pan_y)*scale),pan_gltf_height=float(pan_y),panel_centre_gltf=panel.tolist(),panel_slab_height_native=float(rows[:,1].max()-rows[:,1].min()),eye_to_actual_lane_back=norm(eye,oldcentre),alternative_panel_h=float((native(alternative_body)[0]-panel_x)*scale),front_opening_aft_edge_horizontal=float((eye[0]-1.33)*scale),upper_wing_centreline_te_gltf=te.tolist(),upper_wing_te_horizontal=float((eye[0]-te[0])*scale),upper_wing_te_above=float((te[1]-eye[1])*scale))
            allmetrics[title]=metrics
            points=dict(eye=eye,picked=picked,sill=sill,panel=panel)
            vertices={'picked':top}
            if not source:points['alternative']=oldcentre;vertices['alternative']=oldtop
            # Highlight actual source-picked triangles, or actual older top, in a TEMP GLB only.
            highlighted=Glb(path)
            for n,m,p,ids in list(parts(g)):
                if n!=('Cockpit_Interior' if source else 'Cockpit'):continue
                targetrow=top if source else oldtop
                hit=np.any(np.linalg.norm(p[:,None,:]-targetrow[None,:,:],axis=2)<2e-6,axis=1)
                selected_tris=ids[np.any(hit[ids],axis=1)]
                if len(selected_tris):
                    mat=highlighted.material('Review_Highlight_'+m, (.95,.02,.30) if source else (1,.3,.01))
                    offset=p.copy();offset[:,2]-=.0005
                    highlighted.mesh('Review_Pick_'+m,offset,selected_tris,mat)
            reviewpath=Path(td)/(title+'.glb');highlighted.save(reviewpath)
            # Wings/struts omitted only in cutaways to expose cockpit. Their TE witness is measured above.
            include=lambda n:not n.startswith(('Wing','Aileron','Strut','Wire','Gear','Wheel','Oleo','Prop','Spinner','Engine','Cylinder','Review_Pick_unused'))
            views=[('side-cutaway',(1.98,.10,-4),(1.98,.10,0),2.15,(0,1,0),'side'),
                   ('3q-cutaway',(3.75,1.05,-3.4),(1.90,-.03,0),2.4,(0,1,0),'3q'),
                   ('top',(1.95,4.5,0),(1.95,0,0),2.15,(-1,0,0),'top')]
            for suffix,camera_eye,target,width,up,style in views:
                name=prefix+'-rear-eye-'+suffix+'.png'
                camera=cpu.render(reviewpath,OUT/name,camera_eye,target,width,include,title+' rear eye; skin cut at centreline',up=up,clip_skin=True,image_size=(2200,1500),view_kind='orthographic cutaway; +body Y/right skin removed')
                overlay(camera,points,vertices,metrics,(M,tr),style,title);renders.append(name)
            direction=np.linalg.solve(M,np.array([np.cos(np.radians(4)),0,np.sin(np.radians(4))]));direction/=np.linalg.norm(direction)
            name=prefix+'-rear-eye-view.png';cpu.render(path,OUT/name,eye,eye+direction,86,label=title+' unchanged rear eye; pitch -4 deg; hfov 86 deg',perspective=True);renders.append(name)
            if source:
                name='great-lakes-seatback-pick.png'
                camera=cpu.render(reviewpath,OUT/name,(3.6,.90,-3),(1.98,-.08,0),1.90,lambda n:n in ('Cockpit_Interior','Review_Pick_Cockpit_seat_shell') or n.startswith('Review_Pick_'),label='SOURCE 4a45232: picked seat-shell top, not pan/headrest',image_size=(2200,1500),view_kind='seat-shell isolation')
                overlay(camera,dict(eye=eye,picked=picked,sill=sill,panel=panel),{'picked':top},metrics,(M,tr),'3q','source picked surface isolation');renders.append(name)
        record(great_lakes=dict(cutaway_cameras=[dict(view=v[0],eye_gltf=v[1],target_gltf=v[2],orthographic_width_m=v[3],up=v[4],raw_skin_clip='Z>=0',original_image_size=[2200,1500],footer_extra_height_px=185) for v in views],pick_camera=dict(eye_gltf=[3.6,.9,-3],target_gltf=[1.98,-.08,0],orthographic_width_m=1.9),reference_file_sha256=hashlib.sha256((REFS/'eyepoints-v2-great-lakes-full.json').read_bytes()).hexdigest(),models=[dict(label='this-lane',path=str(lane.relative_to(ROOT)),sha256=hashlib.sha256(lane.read_bytes()).hexdigest()),dict(label='source-4a45232',commit=entry['model_commit'],sha256=entry['model_sha256'])],measurements=allmetrics,eye_pitch_deg=-4,horizontal_fov_deg=86,renders=renders,limitations=['Head sphere/dimensions are x-ray overlays; no occupant fit claim.','Cutaway skin clipped to raw Z>=0, cockpit retained; wings/gear omitted in cutaways only.','No measured real-aircraft eye-to-panel dimension found in repo; no generic range inferred.']))
        (OUT/'great-lakes-rear-eye-measurements.json').write_text(json.dumps(allmetrics,indent=2)+'\n')
        print(json.dumps(allmetrics,indent=2))

if __name__=='__main__': main()

