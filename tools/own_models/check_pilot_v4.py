"""Validate the unshipped v4 GLB and render actual linear-blend skinning with matplotlib.
Run with a Python environment containing numpy, Pillow and matplotlib.
Hand remains a LowerArm child: review poses use LowerArm=0, twist=angle/2,
Hand=angle about elbow->wrist. No runtime posing code is changed.
"""
import argparse, copy, hashlib, json, math
from pathlib import Path
import numpy as np
from pilot_body import OUT, OUT_V2
from vr_cockpit import Glb

def ib(g):
    a=g.doc['accessors'][g.doc['skins'][0]['inverseBindMatrices']]
    v=g.doc['bufferViews'][a['bufferView']]
    return np.frombuffer(g.bin, '<f4', a['count']*16, v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(-1,4,4).transpose(0,2,1).copy()

def validate(g, old):
    nodes=g.doc['nodes']; parents={}; reached=set()
    for i,n in enumerate(nodes):
        for c in n.get('children',[]):
            assert 0<=c<len(nodes) and c not in parents
            parents[c]=i
        for key, table in [('mesh','meshes'),('skin','skins')]:
            if key in n: assert 0<=n[key]<len(g.doc[table])
    def visit(i):
        assert 0<=i<len(nodes) and i not in reached, 'cycle or duplicate root'
        reached.add(i)
        for c in nodes[i].get('children',[]): visit(c)
    for scene in g.doc['scenes']:
        for root in scene['nodes']:
            assert root not in parents
            visit(root)
    assert len(reached)==len(nodes), 'orphan nodes'
    skin=g.doc['skins'][0]; joints=skin['joints']; assert len(joints)==len(set(joints))==51
    assert all(0<=j<len(nodes) for j in joints)
    oldj=old.doc['skins'][0]['joints']; assert joints[:49]==oldj
    for j in oldj:
        a=copy.deepcopy(old.doc['nodes'][j]); b=copy.deepcopy(nodes[j])
        a.pop('children',None); b.pop('children',None); assert a==b
        oldparents={c:i for i,n in enumerate(old.doc['nodes']) for c in n.get('children',[])}
        assert parents.get(j)==oldparents.get(j)
    assert ib(old).tobytes()==ib(g)[:49].tobytes()
    world=g.world_matrices(); names={n['name']:i for i,n in enumerate(nodes)}
    for side in 'LR':
        lower=names[side+'_LowerArm']; hand=names[side+'_Hand']; twist=names[side+'_ForearmTwist']
        assert twist in joints and parents[twist]==lower
        assert np.allclose(world[twist][:3,3],(world[lower][:3,3]+world[hand][:3,3])/2)
        assert np.array_equal(world[twist][:3,:3],world[lower][:3,:3])
    tris=0; vertices=0; error=0
    for mesh in g.doc['meshes']:
        for p in mesh['primitives']:
            attrs=p['attributes']; pos=g.array(attrs['POSITION']); w=g.array(attrs['WEIGHTS_0']); j=g.array(attrs['JOINTS_0']); t=g.array(p['indices'])
            assert np.isfinite(pos).all() and np.isfinite(w).all() and (w>=0).all()
            assert j.min()>=0 and j.max()<51 and t.min()>=0 and t.max()<len(pos)
            assert np.allclose(w.sum(1),1,atol=1e-6)
            error=max(error,float(abs(w.sum(1)-1).max())); tris+=t.size//3; vertices+=len(pos)
    assert tris==14368 and tris<15000
    return dict(triangles=tris,joints=51,vertices=vertices,max_weight_sum_error=error,
                original_49_joints_rest_parents_and_inverse_binds_exact=True,node_graph='PASS: in range, single parents, no cycles/orphans',weights='PASS')

def rotation(axis, degrees):
    axis=axis/np.linalg.norm(axis); x,y,z=axis; a=math.radians(degrees); c=math.cos(a); s=math.sin(a)
    k=np.array([[0,-z,y],[z,0,-x],[-y,x,0]])
    return np.eye(3)*c+(1-c)*np.outer(axis,axis)+s*k

def render(g, directory):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    directory.mkdir(parents=True,exist_ok=True); paths=[]
    names={n['name']:i for i,n in enumerate(g.doc['nodes'])}; rest=g.world_matrices(); inverse=ib(g)
    for angle, bend, label in [(0,0,'twist-000'),(45,0,'twist-045'),(90,0,'twist-090'),(45,40,'flex-040'),(45,-40,'deviation-040')]:
        posed=copy.deepcopy(g)
        # All rest rotations are identity. Pose matrices in glTF frame, then traverse hierarchy.
        for side in 'LR':
            elbow=rest[names[side+'_LowerArm']][:3,3]; wrist=rest[names[side+'_Hand']][:3,3]; axis=wrist-elbow
            perpendicular=np.cross(axis,[0,1,0]); perpendicular/=np.linalg.norm(perpendicular)
            for suffix,deg in [('_ForearmTwist',angle/2),('_Hand',angle)]:
                i=names[side+suffix]; m=np.eye(4); m[:3,3]=g.doc['nodes'][i]['translation']; m[:3,:3]=rotation(axis,deg)
                if suffix=='_Hand' and bend:
                    bendaxis=perpendicular if bend>0 else np.cross(axis,perpendicular)
                    m[:3,:3]=m[:3,:3]@rotation(bendaxis,abs(bend))
                posed.doc['nodes'][i]['matrix']=m.T.reshape(-1).tolist()
        worlds=posed.world_matrices(); matrices=np.array([worlds[j]@inv for j,inv in zip(g.doc['skins'][0]['joints'],inverse)])
        fig=plt.figure(figsize=(13,6),facecolor='#ededed')
        for si,side in enumerate('LR'):
            ax=fig.add_subplot(1,2,si+1,projection='3d'); wrist=rest[names[side+'_Hand']][:3,3]; elbow=rest[names[side+'_LowerArm']][:3,3]
            axis=(wrist-elbow)/np.linalg.norm(wrist-elbow); up=np.array([0.,1.,0.]); up-=axis*np.dot(axis,up); up/=np.linalg.norm(up); cross=np.cross(axis,up); basis=np.stack([axis,cross,up],axis=1)
            for p in g.doc['meshes'][0]['primitives']:
                a=p['attributes']; pos=g.array(a['POSITION']); joints=g.array(a['JOINTS_0']); weights=g.array(a['WEIGHTS_0']); t=g.array(p['indices']).reshape(-1,3)
                hp=np.column_stack([pos,np.ones(len(pos))]); deformed=np.einsum('vk,vkij,vj->vi',weights,matrices[joints],hp)[:,:3]
                local=(deformed-wrist)@basis; original=(pos-wrist)@basis
                mask=(original[t].mean(1)[:,0]>-.24)&(original[t].mean(1)[:,0]<.15)&(np.linalg.norm(original[t].mean(1)[:,1:],axis=1)<.10)
                allowed=[k for k,j in enumerate(g.doc['skins'][0]['joints']) if g.doc['nodes'][j]['name'].startswith(side+'_') and not any(q in g.doc['nodes'][j]['name'] for q in ('Leg','Foot','Toes','Sole'))]
                belongs=np.sum(weights*np.isin(joints,allowed),axis=1)>.99
                mask &= belongs[t].all(1)
                faces=local[t[mask]]; color=g.doc['materials'][p['material']]['pbrMetallicRoughness']['baseColorFactor'][:3]
                color=np.clip(np.array(color)*2+.12,0,1)
                ax.add_collection3d(Poly3DCollection(faces,facecolors=color,edgecolors=(0,0,0,.13),linewidths=.25))
            ax.set(xlim=(-.24,.15),ylim=(-.09,.09),zlim=(-.09,.09),xlabel='forearm axis (m)',ylabel='width',zlabel='height',title=side+' forearm / wrist')
            ax.set_box_aspect((.39,.18,.18)); ax.view_init(elev=25,azim=-65)
        fig.suptitle(f'v4 CANDIDATE — {label} | Hand twist {angle}°, ForearmTwist {angle/2}° | CPU linear-blend skinning')
        path=directory/f'codex-v4-{label}.png'; fig.savefig(path,dpi=160); plt.close(fig); paths.append(str(path))
    return paths

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--render-dir',type=Path,default=Path('/tmp/quest-feedback-v4-renders')); args=parser.parse_args()
    g=Glb(OUT_V2.with_name('pilot-body-own-v4.glb')); result=validate(g,Glb(OUT)); result['renders']=render(g,args.render_dir)
    result['shipped_sha256']=hashlib.sha256(OUT.read_bytes()).hexdigest()
    print(json.dumps(result,indent=2))
