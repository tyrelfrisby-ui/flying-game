"""Convert Florian Blanc's CC BY-SA Twin III print plate with Blender headless.
Run: blender -b --factory-startup --python tools/own_models/build_grob103_blanc.py -- --python /path/to/python-with-numpy-pillow
The original STL is deliberately external. See the asset CREDITS.md.
"""
import argparse, hashlib, json, math, subprocess, sys, tempfile
from pathlib import Path
import bpy, bmesh, numpy as np
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
ID='glider-grob103-acro-like'
OUT=ROOT/'unity/Assets/_Project/Resources/Models'/ID
RENDER=ROOT/'renders/grob103/blanc-v1'

def mesh(name,v,f,material):
 m=bpy.data.meshes.new(name);m.from_pydata(v,[],f);m.update();o=bpy.data.objects.new(name,m);bpy.context.collection.objects.link(o);m.materials.append(material);return o

def mat(name,col,rough=.32):
 m=bpy.data.materials.new(name);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=col;p.inputs['Alpha'].default_value=col[3];p.inputs['Roughness'].default_value=rough;return m

def activate(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o

def split_by(o,fn,name):
 bm=bmesh.new();bm.from_mesh(o.data);bm.faces.ensure_lookup_table();idx=[f.index for f in bm.faces if fn(f.calc_center_median())]
 # Submesh via face indices preserves exact shared boundary positions.
 vv=[v.co[:] for v in bm.verts];faces=[tuple(v.index for v in bm.faces[i].verts) for i in idx]
 bmesh.ops.delete(bm,geom=[bm.faces[i] for i in idx],context='FACES');bm.to_mesh(o.data);bm.free()
 if not faces:return None
 n=mesh(name,vv,faces,o.data.materials[0]);bm=bmesh.new();bm.from_mesh(n.data);bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(n.data);bm.free();return n

def bisect(o,point,normal):
 bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=point,plane_no=normal,dist=1e-7);bm.to_mesh(o.data);bm.free()

def origin(o,p):
 p=Vector(p);delta=p-o.location
 for v in o.data.vertices:v.co-=delta
 o.location=p

def prepare_source(path,white):
 bpy.ops.wm.stl_import(filepath=str(path));plate=bpy.context.object
 activate(plate);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.separate(type='LOOSE');bpy.ops.object.mode_set(mode='OBJECT')
 parts=sorted([o for o in bpy.context.selected_objects if o.type=='MESH'],key=lambda o:len(o.data.polygons),reverse=True)
 assert [len(o.data.polygons) for o in parts]==[42282,42282,11496,11496]
 qs=[]
 for i,o in enumerate(parts):
  v=np.array([a.co[:] for a in o.data.vertices]);low=v[v[:,2]<3,:2];mu=low.mean(0);_,_,vt=np.linalg.svd(low-mu,full_matrices=False);d=vt[0]
  p=(v[:,:2]-mu)@np.array([d,[-d[1],d[0]]]).T
  if i==1:p[:,1]*=-1
  if i==3:p[:,0]*=-1
  qs.append(np.c_[p,v[:,2]])
 # PCA resolves the packing rotations. Rotate the measured boom axis level.
 c,s=math.cos(.1),math.sin(.1)
 def fus(q):return np.c_[q[:,0]*c-q[:,1]*s,q[:,2],q[:,0]*s+q[:,1]*c]
 a=fus(qs[0]);length_scale=8.18/np.ptp(a[:,0]);xmin=a[:,0].min();span_scale=9/(126.3701171875+126.31604766845703)
 zoff=-.51-np.percentile(a[(a[:,0]>-70)&(a[:,0]<-20)&(a[:,1]<10),2],1)*length_scale
 verts=[]
 for i,q in enumerate(qs):
  if i<2:
   v=fus(q);v[:,0]=(v[:,0]-xmin)*length_scale-2.5;v[:,2]=v[:,2]*length_scale+zoff;v[:,1]*=span_scale
  else:
   root=a[a[:,1]>126.3][:,[0,2]];qr=q[q[:,2]<.01,:2]
   def ends(pts):
    return pts[pts[:,0]<pts[:,0].min()+.02].mean(0),pts[pts[:,0]>pts[:,0].max()-.02].mean(0)
   lead,trail=ends(root);ol,ot=ends(qr)
   angle=math.atan2(*(trail-lead)[::-1])-math.atan2(*(ot-ol)[::-1]);cc,ss=math.cos(angle),math.sin(angle)
   xz=(q[:,:2]-ol)@np.array([[cc,ss],[-ss,cc]])+lead
   v=np.c_[(xz[:,0]-xmin)*length_scale-2.5,(q[:,2]+126.3701171875)*span_scale,xz[:,1]*length_scale+zoff]
  v[:,1]*=1 if i in [0,2] else -1;verts.append(v)
 # Level belly and fin crown are the specified 1.71 m envelope; do not use on-wheels height.
 v=verts[0];belly=v[(v[:,0]>-1.3)&(v[:,0]<-.8)&(v[:,1]<.2),2].min();fin=v[(v[:,0]>4.6)&(v[:,1]<.01),2].max();zscale=1.71/(fin-belly)
 for i,(o,v) in enumerate(zip(parts,verts)):
  v[:,2]=(v[:,2]-belly)*zscale-.51
  for p,co in zip(o.data.vertices,v):p.co=co
  # Remove print split/coupling caps; they are internal after assembly.
  bm=bmesh.new();bm.from_mesh(o.data)
  cap=(lambda f:all(abs(v.co.y)<.002 for v in f.verts) and abs(f.normal.y)>.9) if i<2 else (lambda f:all(abs(abs(v.co.y)-126.3701171875*span_scale)<1e-5 for v in f.verts))
  bmesh.ops.delete(bm,geom=[f for f in bm.faces if cap(f)],context='FACES')
  if i<2:
   for v in bm.verts:
    if abs(v.co.y)<.002:v.co.y=0
  bm.to_mesh(o.data);bm.free()
  o.data.materials.clear();o.data.materials.append(white)
 bpy.ops.object.select_all(action='DESELECT')
 for o in parts:o.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=bpy.context.object;o.name='Fuselage_Blanc'
 bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0003);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
 return o,dict(source_triangles=107556,length_scale=length_scale,span_scale=span_scale,height_scale=zscale,plate_axis_correction_radians=.1)

def run(args):
 RENDER.mkdir(parents=True,exist_ok=True)
 # The analytic asset supplies the existing rig, original cockpit and fixed gear only.
 with tempfile.TemporaryDirectory(prefix='grob-blanc-base-') as tmp:
  code="import sys;from pathlib import Path;sys.path.insert(0,'tools/own_models');import build_grob103 as b;b.OUT=Path(sys.argv[1]);b.DOC=b.OUT;b.make();[b.Builder(t).build() for t in [False,True]]"
  subprocess.run([args.python,'-c',code,tmp],cwd=ROOT,check=True)
  rig=json.loads((Path(tmp)/'grob103-own-rig.json').read_text())
  bpy.ops.wm.read_factory_settings(use_empty=True)
  bpy.ops.import_scene.gltf(filepath=str(Path(tmp)/(ID+'-own.glb')))
  # Retain cockpit, all instrument/lever/eye markers and fixed rounded gear.
  discard=('Fuselage','Wing_','Stabilizer','Fin','Rudder','Aileron','Spoiler','Airbrake_Cap','Canopy_Glass','Canopy_Bow','Canopy_Sill','Cheatline','Mark_','Pitot','Static_Port','Cockpit_Liner','Cockpit_Bulkhead')
  surfaces={r['node']:{k:(v.to_list() if hasattr(v,'to_list') else v) for k,v in bpy.data.objects[r['node']].items()} for r in rig['rig'] if r['node'] in ['Aileron_L','Aileron_R','Elevator_L','Elevator_R','Rudder','Spoiler_L','Spoiler_R']}
  for o in list(bpy.context.scene.objects):
   if o.name.endswith('_HingeAxis'):continue
   if o.name.startswith(discard) or o.name.startswith('Elevator'):bpy.data.objects.remove(o,do_unlink=True)
 # Proposed visual eyes fit the source canopy; application eyepoints are untouched.
 bpy.data.objects['Pilot_Eye_front'].location.z=.16
 bpy.data.objects['Pilot_Eye_rear'].location.z=.22
 # Fit the retained front panel and all of its dial layers lower in Blanc's narrower nose.
 panel_names=('Panel_F','Bezel_','Dial_','Needle_','Compass_Card')
 for o in bpy.context.scene.objects:
  if (o.name=='Panel_F' or o.name.startswith('Compass_Card') or (o.name.startswith(('Bezel_','Dial_','Needle_')) and ('_F' in o.name))):
   o.location.z-=.07
   for r in rig['rig']:
    if r['node']==o.name:r['pivot_gltf'][1]-=.07
 white=mat('Blanc white gelcoat',(.91,.93,.92,1),.24);glass=mat('Blanc canopy glass',(.46,.66,.74,.19),.12)
 glass.surface_render_method='BLENDED';glass.use_transparency_overlap=False;glass.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value=0
 frame=mat('Canopy white frame',(.86,.89,.88,1),.28);navy=mat('Registration navy',(.015,.035,.08,1),.35)
 body,measure=prepare_source(args.source,white)
 # First isolate flight surfaces by exact planar cuts, leaving no duplicate rest geometry.
 for x in [-1.95,.65]:bisect(body,(x,0,0),(1,0,0))
 slope=.165/2.6
 bisect(body,(-1.95,0,.025),(-slope,0,1))
 canopy=split_by(body,lambda p:abs(p.y)<.41 and -1.95001<p.x<.65001 and p.z>.025+slope*(p.x+1.95)+1e-6,'Canopy_Glass')
 assert canopy
 for x in [-1.925,-.672,-.628,.625]:bisect(canopy,(x,0,0),(1,0,0))
 bisect(canopy,(-1.95,0,.043),(-slope,0,1))
 framepart=split_by(canopy,lambda p:abs(p.x+.65)<.022 or p.x< -1.925 or p.x>.625 or p.z<.025+slope*(p.x+1.95)+.018,'Canopy_Frame')
 canopy.data.materials.clear();canopy.data.materials.append(glass)
 if framepart:framepart.data.materials.clear();framepart.data.materials.append(frame)
 # Hollow cockpit: canopy split plane caps would otherwise hide both seats.
 # Replace molded tires with rounded, dimensioned gear; close the cut at the local keel.
 wheel_cuts=[('Nose',-1.95,-1.25,-.0714286,-.599286,.25,-.48),('Main',0.,.95,0.,-.48,.30,-.48),('Tail',4.35,5.06,.034,-.3464,.12,-.19)]
 for label,x0,x1,slope_gear,intercept,width_limit,keel in wheel_cuts:
  bisect(body,(0,0,intercept),(-slope_gear,0,1))
  for x in [x0,x1]:bisect(body,(x,0,0),(1,0,0))
  bm=bmesh.new();bm.from_mesh(body.data)
  bmesh.ops.delete(bm,geom=[f for f in bm.faces if x0<f.calc_center_median().x<x1 and abs(f.calc_center_median().y)<width_limit and f.calc_center_median().z<slope_gear*f.calc_center_median().x+intercept-1e-6],context='FACES')
  edges=[e for e in bm.edges if e.is_boundary and all(x0-1e-5<v.co.x<x1+1e-5 and abs(v.co.z-slope_gear*v.co.x-intercept)<1e-5 for v in e.verts)]
  if edges:bmesh.ops.holes_fill(bm,edges=edges,sides=0)
  bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
  wheel=bpy.data.objects[label+'_Wheel'];exposure={'Nose':.10,'Main':.12,'Tail':.07}[label]
  old_keel=min((wheel.matrix_world@v.co).z for v in wheel.data.vertices)+exposure
  dz=keel-old_keel
  for suffix in ['_Wheel','_Hub','_Fairing']:bpy.data.objects[label+suffix].location.z+=dz
 # An inset copy of the actual cockpit skin supplies a fitted open liner.
 liner_copy=bpy.data.objects.new('Liner_work',body.data.copy());bpy.context.collection.objects.link(liner_copy)
 liner=split_by(liner_copy,lambda p:-1.90<p.x<.60 and abs(p.y)<.40,'Cockpit_Liner')
 bpy.data.objects.remove(liner_copy,do_unlink=True)
 if liner:
  liner.data.materials.clear();liner.data.materials.append(mat('Cockpit interior',(.24,.27,.29,1),.65))
  for v in liner.data.vertices:v.co.y*=.97;v.co.z=(v.co.z+.2)*.97-.2
  bm=bmesh.new();bm.from_mesh(liner.data);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(liner.data);bm.free()
 # Fin/rudder and T-tail are part of the two print halves.
 bisect(body,(5.23,0,0),(1,0,0));bisect(body,(0,.15,0),(0,1,0));bisect(body,(0,-.15,0),(0,1,0));rud=split_by(body,lambda p:p.x>5.23 and abs(p.y)<.15 and p.z>.02,'Rudder')
 bisect(body,(5.1,0,0),(1,0,0))
 elevators=[]
 for side,sg in [('L',-1),('R',1)]:
  el=split_by(body,lambda p,sg=sg:p.x>5.1 and p.y*sg>.06 and p.z>.9,'Elevator_'+side);elevators.append(el)
 # Wing trailing edges: spanwise sloped hinge follows actual Blanc taper.
 # Flight surface starts at 5.25 m span, with 22% chord aft of the hinge.
 for side,sg in [('L',-1),('R',1)]:
  bisect(body,(0,sg*5.25,0),(0,1,0))
  # measured outer panel TE 1.023 -> .58; hinge .80 -> .47.
  bisect(body,(.80,sg*4.501,0),(1,sg*.073,0))
  al=split_by(body,lambda p,sg=sg:p.y*sg>5.25 and p.x>.80-.073*(abs(p.y)-4.501),'Aileron_'+side)
  for o in [al]:assert o is not None
  # Separate flush spoiler panel from the source upper wing, with a real rest opening.
  for x in [.53,.64]:bisect(body,(x,0,0),(1,0,0))
  for y in [sg*2.275,sg*3.525]:bisect(body,(0,y,0),(0,1,0))
  sp=split_by(body,lambda p,sg=sg:2.275<p.y*sg<3.525 and .53<p.x<.64 and p.z>-.1,'Spoiler_'+side)
  assert sp is not None
 # Rest pivots and witnesses follow this geometry; parameter maps remain unchanged.
 for r in rig['rig']:
  name=r['node'];o=bpy.data.objects.get(name)
  if name in surfaces:
   assert o is not None,name
   if name.startswith('Aileron'):p=Vector((.80-.073*(5.25-4.501),-5.25 if name.endswith('L') else 5.25,.30));axis=Vector((.073,1 if name.endswith('L') else -1,-.055)).normalized()
   elif name.startswith('Elevator'):p=Vector((5.1,-.06 if name.endswith('L') else .06,1.18));axis=Vector((0,-1,0))
   elif name=='Rudder':p=Vector((5.23,0,.10));axis=Vector((0,0,1))
   else:p=Vector((.58,-2.9 if name.endswith('L') else 2.9,.25));axis=Vector((0,0,1))
   if name.startswith(('Aileron','Elevator')):
    verts=np.array([v.co[:] for v in o.data.vertices]);dist=np.linalg.norm(verts[:,:2]-np.array(p)[:2],axis=1);p.z=float(np.mean(verts[np.argsort(dist)[:4],2]))
   elif name.startswith('Spoiler'):
    verts=np.array([v.co[:] for v in o.data.vertices]);p.z=float((verts[:,2].max()+verts[:,2].min())/2)
   origin(o,p)
   for k,v in surfaces[name].items():o[k]=v
   key='rig_move_axis_gltf' if r['kind']=='move' else 'rig_axis_gltf';r['pivot_gltf']=[p.x,p.z,-p.y];r['axis_gltf']=[axis.x,axis.z,-axis.y];o[key]=r['axis_gltf']
   marker=bpy.data.objects[r['axis_marker']];marker.location=p+axis
 # Separate wing/fuselage names aid masks and mobile renderer culling.
 for side,sg in [('L',-1),('R',1)]:split_by(body,lambda p,sg=sg:p.y*sg>.42 and p.x<2,'Wing_Blanc_'+side)
 for side,sg in [('L',-1),('R',1)]:split_by(body,lambda p,sg=sg:p.y*sg>.15 and p.x>4.5 and p.z>.9,'Stabilizer_'+side)
 fin=split_by(body,lambda p:p.x>4.55 and p.z>.18 and abs(p.y)<.16,'Fin')
 # Registration decal geometry, synthetic example (not a claim of a real registration).
 for side,sg in [('L',-1),('R',1)]:
  bpy.ops.object.text_add(location=(2.65,sg*.115,-.145));t=bpy.context.object;t.name='Registration_'+side;t.data.body='D-103C';t.data.align_x='CENTER';t.data.size=.105;t.data.extrude=.0001;t.rotation_euler=(math.pi/2 if sg<0 else -math.pi/2,0,0 if sg<0 else math.pi);t.data.materials.append(navy);activate(t);bpy.ops.object.convert(target='MESH')
  bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
  for v in t.data.vertices:
   w=t.matrix_world@v.co;hit,loc,normal,idx=body.ray_cast(Vector((w.x,sg*2,w.z)),Vector((0,-sg,0)))
   if hit:v.co.y=loc.y+sg*.012-t.location.y
 # Thin red longitudinal trim follows the exterior rather than floating off it.
 trim=mat('Red registration trim',(.56,.012,.02,1),.3)
 for side,sg in [('L',-1),('R',1)]:
  v=[]
  shell_v=np.array([p.co[:] for p in body.data.vertices])
  for x in np.linspace(1.5,4.25,60):
   local=shell_v[(abs(shell_v[:,0]-x)<.08)&(abs(shell_v[:,1])<.23)]
   centre=float((local[:,2].max()+local[:,2].min())/2)
   for z in [centre+.065,centre+.082]:
    hits=[]
    for o in [body]:
     hit,loc,normal,idx=o.ray_cast(Vector((x,sg*2,z)),Vector((0,-sg,0)))
     if hit:hits.append(loc)
    p=hits[0] if hits else Vector((x,sg*.1,z));v.append((p.x,p.y+sg*.002,p.z))
  mesh('Trim_'+side,v,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(59)],trim)
 # Smooth, decimate source parts conservatively, unwrap every mesh without overwriting dial UVs.
 source_names={'Fuselage_Blanc','Canopy_Frame','Fin','Rudder','Canopy_Glass','Wing_Blanc_L','Wing_Blanc_R','Aileron_L','Aileron_R','Elevator_L','Elevator_R','Spoiler_L','Spoiler_R','Stabilizer_L','Stabilizer_R'}
 for o in list(bpy.context.scene.objects):
  if o.type!='MESH':continue
  activate(o)
  if o.name in source_names and len(o.data.polygons)>500:
   mod=o.modifiers.new('Mobile silhouette decimation','DECIMATE');mod.ratio=.42 if o.name!='Canopy_Glass' else .65;mod.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=mod.name)
  for f in o.data.polygons:f.use_smooth=True
  if not o.data.uv_layers:
   bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.015);bpy.ops.object.mode_set(mode='OBJECT')
 # Keep the exact fin crown after decimation's sub-mm numerical movement.
 fin=bpy.data.objects['Fin'];crown=max(v.co.z for v in fin.data.vertices)
 for v in fin.data.vertices:v.co.z*=1.2/crown
 # Blender exporter custom scene metadata; glTF extras are patched at document root after export.
 bpy.context.scene['sourceModel']='Florian Blanc G103c Twin III Acro';bpy.context.scene['license']='CC BY-SA';bpy.context.scene['visualApproval']='PENDING'
 report={'source_sha256':hashlib.sha256(args.source.read_bytes()).hexdigest(),'assembly':measure,'license':'CC BY-SA; see CREDITS.md','visual_approval':'PENDING','triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in bpy.context.scene.objects if o.type=='MESH')}
 for suffix in ['', '-tf']:
  path=OUT/(ID+'-own'+suffix+'.glb')
  bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',export_extras=True,export_yup=True,export_cameras=False,export_lights=False)
  # Root extras used by the existing checker; no geometry change.
  import struct
  data=path.read_bytes();jl=struct.unpack_from('<I',data,12)[0];doc=json.loads(data[20:20+jl]);doc['extras']={'sourceModel':'Florian Blanc G103c Twin III Acro','license':'CC BY-SA','visualApproval':'pending','cleanRoom':False}
  raw=json.dumps(doc,separators=(',',':')).encode();raw+=b' '*((-len(raw))%4);tail=data[20+jl:];path.write_bytes(struct.pack('<III',0x46546c67,2,20+len(raw)+len(tail))+struct.pack('<II',len(raw),0x4e4f534a)+raw+tail)
  report[path.name]={'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size}
 (ROOT/'docs/models/grob103-own-rig.json').write_text(json.dumps(rig,indent=2)+'\n')
 (RENDER/'build-manifest.json').write_text(json.dumps(report,indent=2)+'\n')
 bpy.ops.wm.save_as_mainfile(filepath=str(Path(tempfile.gettempdir())/'grob103-blanc-review.blend'))
 print('BLANC_REPORT',json.dumps(report))

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--source',type=Path,default=Path.home()/'Developer/agent-lanes/xfer/grob-blanc/grob-models/printables-twin3-acro-florianblanc/Twin3_plate.stl');p.add_argument('--python',default='python3');run(p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []))
