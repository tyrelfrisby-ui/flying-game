"""Measure exported GLB triangle slices, compare B and baseline rig/paint.
PYTHONDONTWRITEBYTECODE=1 python check_ea300_wing_b.py --before /tmp/.../before.glb
Requires numpy, matplotlib, Pillow (no Blender). Outputs review evidence under renders/extra-wing-b.
"""
import argparse, json, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'aircraft_builder'))
import extra_sections as B
from glb_visuals import Glb
ROOT = Path(__file__).resolve().parents[2]
MODEL = ROOT/'unity/Assets/_Project/Resources/Models/extra-300-like'
OUT = ROOT/'renders/extra-wing-b'
R = json.loads((Path(__file__).with_name('ea300_ref_tables.json')).read_text())

def primitives(g, names):
    mats=g.world_matrices()
    for i,n in enumerate(g.doc['nodes']):
        if n.get('name') not in names or 'mesh' not in n: continue
        for p in g.doc['meshes'][n['mesh']]['primitives']:
            v=g.array(p['attributes']['POSITION']); v=(np.c_[v,np.ones(len(v))]@mats[i].T)[:,:3]
            # glTF (-build x, build z, build y) -> build frame.
            v=v[:,[0,2,1]]*[-1,1,1]
            yield p,v,g.array(p['indices']).reshape(-1,3)

SLICE_CACHE={}
def slice_mesh(g,y):
    key=id(g)
    if key not in SLICE_CACHE:
        wing=[v[i] for _,v,i in primitives(g, {'Wing'})]
        tris=np.concatenate([v[i] for _,v,i in primitives(g, {'Aileron_L'})])
        # Identify disconnected spade/weight/strut geometry by welded connectivity,
        # independent of B ordinates. The skin is the component spanning the aileron.
        _,inv=np.unique(np.round(tris.reshape(-1,3),6),axis=0,return_inverse=True)
        ids=inv.reshape(-1,3);parent=np.arange(inv.max()+1)
        def find(i):
            while parent[i]!=i:
                parent[i]=parent[parent[i]];i=parent[i]
            return i
        for aa,bb,cc in ids:
            root=find(aa);parent[find(bb)]=root;parent[find(cc)]=root
        groups=np.array([find(i) for i in ids[:,0]])
        keep=max(np.unique(groups),key=lambda k:np.ptp(tris[groups==k,:,1]))
        SLICE_CACHE[key]=np.concatenate(wing+[tris[groups==keep]])
    tri=SLICE_CACHE[key];mask=(tri[:,:,1].min(1)<=y)&(tri[:,:,1].max(1)>=y);seg=[]
    for t in tri[mask]:
        hits=[]
        for a,b in ((t[0],t[1]),(t[1],t[2]),(t[2],t[0])):
            if (a[1]-y)*(b[1]-y)<=0 and abs(b[1]-a[1])>1e-9:
                hits.append((a+(b-a)*(y-a[1])/(b[1]-a[1]))[[0,2]])
        if len(hits)==2:seg.append(hits)
    return np.array(seg)

def envelope(seg,x):
    lo=np.full(len(x),np.nan); hi=lo.copy()
    for k,q in enumerate(x):
        a,b=seg[:,0],seg[:,1]; valid=(np.minimum(a[:,0],b[:,0])<=q)&(np.maximum(a[:,0],b[:,0])>=q)&(abs(a[:,0]-b[:,0])>1e-10)
        z=a[valid,1]+(q-a[valid,0])/(b[valid,0]-a[valid,0])*(b[valid,1]-a[valid,1])
        if len(z):lo[k],hi[k]=min(z),max(z)
    return lo,hi

def run(before):
    OUT.mkdir(parents=True,exist_ok=True); old=Glb(before); results={}
    stations=[0.31,0.45,1.,1.875,2.523,3.508,3.99] # first exposed root; hidden centre is trimmed under cockpit
    xs=np.unique(np.r_[np.linspace(.0001,.9999,1800),.0125,B.XC])
    tab=sorted(R['wing'],key=lambda t:abs(t['s'])); sy=[abs(t['s']) for t in tab]
    for suffix in ('','-n300uy','-unmarked'):
        g=Glb(MODEL/('extra-300-like-own'+suffix+'.glb')); rows=[]
        for y in stations:
            le=np.interp(y,sy,[t['xle'] for t in tab]); te=np.interp(y,sy,[t['xte'] for t in tab]); chord=le-te
            lo,hi=envelope(slice_mesh(g,y),le-xs*chord)
            h=B.half(B.station(y)[2],'B'); ref=np.array([h(float(x)) for x in xs]); mid=np.nanmedian((hi+lo)/2)
            # Exclude the permitted half-round hinge nose, slot, and spade strut below skin.
            hs=[t for t in tab if t['hinge'] is not None]
            ss=np.array([abs(t['s']) for t in hs]); hh=np.array([t['hinge'] for t in hs]); fit=np.polyfit(ss,hh,1);rr=hh-np.polyval(fit,ss)
            keep=abs(rr)<2.5*np.median(abs(rr))+1e-3;fit=np.polyfit(ss[keep],hh[keep],1)
            hinge=(le-np.polyval(fit,y))/chord
            valid=np.isfinite(hi)&np.isfinite(lo)
            if y>R['aileron_span'][0]: valid &= abs(xs-hinge)>.10
            raw_upper=100*float(np.nanmax(abs((hi-mid)/chord-ref)))
            raw_lower=100*float(np.nanmax(abs((lo-mid)/chord+ref)))
            lower_skin=valid
            thickness=hi-lo; k=np.nanargmax(np.where(xs<.45,thickness,np.nan)); xm=xs[k]
            # Interpolate only across the permitted hinge slot for concavity; keep it out of skin error.
            hmesh=lambda x: float(np.interp(x,xs[valid],(hi[valid]-mid)/chord))
            conc=max((hi[k]-mid)/chord*(1-x)/(1-xm)+h(1)*(x-xm)/(1-xm)-hmesh(x) for x in B.XC if xm<x<.99)
            row=dict(y_m=y,chord_m=chord,B=B.geometry_w(B.station(y)[2],'B'),mesh_tc_pct=100*thickness[k]/chord,mesh_xmax_pct=100*xm,mesh_y125_pct=100*hmesh(.0125),mesh_concavity_pct=100*conc,max_upper_error_pct=100*float(np.max(abs((hi[valid]-mid)/chord-ref[valid]))),max_lower_error_pct=100*float(np.max(abs((lo[lower_skin]-mid)/chord+ref[lower_skin]))),raw_max_upper_error_pct=raw_upper,raw_max_lower_error_pct=raw_lower)
            rows.append(row)
        results[suffix or 'tf']=rows
    g=Glb(MODEL/'extra-300-like-own.glb')
    node=lambda q:{n['name']:n for n in q.doc['nodes']}
    a,b=node(old),node(g)
    checks={'node_names_equal':set(a)==set(b),'hinge_transforms_equal':all({k:v for k,v in a[n].items() if k!='mesh'}=={k:v for k,v in b[n].items() if k!='mesh'} for n in a if n.startswith('Aileron_') and n in b)}
    def imagebytes(q):
        return {im.get('name',str(i)):bytes(q.bin[q.doc['bufferViews'][im['bufferView']].get('byteOffset',0):q.doc['bufferViews'][im['bufferView']].get('byteOffset',0)+q.doc['bufferViews'][im['bufferView']]['byteLength']]) for i,im in enumerate(q.doc.get('images',[]))}
    ia,ib=imagebytes(old),imagebytes(g);checks['paint_images_equal']={k:ia[k]==ib.get(k) for k in ia if k.startswith('ea_')}
    for name in ('Wing','Aileron_L','Aileron_R'):
        va=np.concatenate([v for _,v,_ in primitives(old,{name})]);vb=np.concatenate([v for _,v,_ in primitives(g,{name})]);checks[name+'_xy_bounds_delta_m']=(np.array([vb[:,:2].min(0),vb[:,:2].max(0)])-np.array([va[:,:2].min(0),va[:,:2].max(0)])).tolist()
    checks['slice_LE_TE_delta_m']={}
    for y in stations:
        sa,sb=slice_mesh(old,y),slice_mesh(g,y)
        delta=np.array([sb[:,:,0].max()-sa[:,:,0].max(),sb[:,:,0].min()-sa[:,:,0].min()])
        checks['slice_LE_TE_delta_m'][str(y)]=delta.tolist()
        assert abs(delta).max()<1e-6
    # UVs are projected from x/y: compare mapping at all painted surface vertices to baseline affine projection.
    for label,q in [('before',old),('after',g)]:
        residual=[]
        for p,v,_ in primitives(q,{'Wing','Aileron_L','Aileron_R'}):
            mat=q.doc['materials'][p['material']]['name']
            if mat not in ('Paint_ea_top','Paint_ea_bot'):continue
            uv=q.array(p['attributes']['TEXCOORD_0']);X=np.c_[v[:,0],v[:,1],np.ones(len(v))]
            # Known unchanged projected map; baseline fit for each material.
            if label=='before':
                coef=np.linalg.lstsq(X,uv,rcond=None)[0]; UV_COEF[mat]=coef
            residual.append(float(abs(X@UV_COEF[mat]-uv).max()))
        checks[label+'_uv_projection_max_error']=max(residual)
    # Verify all non-wing primitive arrays are preserved exactly (including sequence-card UVs).
    def signature(q,name):
        n=next(n for n in q.doc['nodes'] if n['name']==name)
        if 'mesh' not in n:return None
        return [(tuple((k,q.array(v).tobytes()) for k,v in sorted(p['attributes'].items())),q.array(p['indices']).tobytes() if 'indices' in p else None) for p in q.doc['meshes'][n['mesh']]['primitives']]
    checks['non_wing_mesh_arrays_equal']=all(signature(old,n)==signature(g,n) for n in a if n not in {'Wing','Aileron_L','Aileron_R'})
    checks['triangles_before_after']={name:[sum(len(i) for _,_,i in primitives(q,{name})) for q in (old,g)] for name in ('Wing','Aileron_L','Aileron_R')}
    # Vertical ray intersections with the cockpit shell at the retained inner carry-through ledges.
    def zhits(tri,x,y):
        aa,bb,dd=tri[:,0],tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]
        det=bb[:,0]*dd[:,1]-bb[:,1]*dd[:,0]; ok=abs(det)>1e-10
        aa,bb,dd,det=aa[ok],bb[ok],dd[ok],det[ok];qq=np.array([x,y])-aa[:,:2]
        u=(qq[:,0]*dd[:,1]-qq[:,1]*dd[:,0])/det;v=(bb[:,0]*qq[:,1]-bb[:,1]*qq[:,0])/det
        ok=(u>=-1e-6)&(v>=-1e-6)&(u+v<=1+1e-6)
        return (aa[:,2]+u*bb[:,2]+v*dd[:,2])[ok]
    for label,q in [('before',old),('after',g)]:
        tub=np.concatenate([v[i] for _,v,i in primitives(q,{'Cockpit_Shell'})]);wing=np.concatenate([v[i] for _,v,i in primitives(q,{'Wing'})]);clear=[]
        for x in np.linspace(-1.3,-.4,25):
            for y in np.linspace(.305,.34,8):
                tw=zhits(wing,x,y);tt=zhits(tub,x,y);tw=tw[tw>0];tt=tt[(tt>0)&(tt<.3)]
                if len(tw) and len(tt):clear.append(float(tt.max()-tw.max()))
        checks[label+'_inner_ledge_clearance_m']={'samples':len(clear),'min':min(clear),'max':max(clear),'x_range':[-1.3,-.4],'y_range':[.305,.34]}
    # At the root junction the fuselage must overlap the hidden wing start at |y|=.30.
    fus=np.concatenate([v[i] for _,v,i in primitives(g,{'Fuselage_Painted'})])
    fus=fus[:,:,[0,2,1]] # intersect in x/z to obtain lateral y
    overlap=[]
    y=.31;le=np.interp(y,sy,[t['xle'] for t in tab]);te=np.interp(y,sy,[t['xte'] for t in tab]);seg=slice_mesh(g,y)
    for x in np.linspace(te+.02,le-.02,80):
        ll,uu=envelope(seg,np.array([x]))
        for z in (ll[0],uu[0]):
            yy=zhits(fus,x,z);yy=yy[yy>0]
            if len(yy):overlap.append(float(yy.max()-.30))
    checks['root_fuselage_overlap_m']={'samples':len(overlap),'min':min(overlap),'max':max(overlap),'note':'lateral skin half-width minus hidden trim half-width; sampled upper/lower root skin'}
    assert checks['node_names_equal'] and checks['hinge_transforms_equal'] and checks['non_wing_mesh_arrays_equal']
    assert all(checks['paint_images_equal'].values())
    assert checks['after_uv_projection_max_error']<1e-6
    assert all(row['max_upper_error_pct']<.16 and row['max_lower_error_pct']<.16 for liv in ('tf','-n300uy','-unmarked') for row in results[liv])
    results['checks']=checks
    (OUT/'measurements.json').write_text(json.dumps(results,indent=2)+'\n')
    import matplotlib;matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    fig,ax=plt.subplots(figsize=(12,4))
    y=.45; le=np.interp(y,sy,[t['xle'] for t in tab]);te=np.interp(y,sy,[t['xte'] for t in tab]);c=le-te
    for label,q in [('Before',old),('After',g)]:
        lo,hi=envelope(slice_mesh(q,y),le-xs*c);mid=np.nanmedian((lo+hi)/2)
        ax.plot(xs,(hi-mid)/c,label=label);ax.plot(xs,(lo-mid)/c,color=ax.lines[-1].get_color())
    h=B.half(B.station(y)[2],'B');ref=np.array([h(float(x)) for x in xs]);ax.plot(xs,ref,'k--',lw=1,label='B reference');ax.plot(xs,-ref,'k--',lw=1)
    ax.set_aspect('equal');ax.set(xlabel='x / chord (LE → TE)',ylabel='z / chord',title='Exposed root section at y = 0.45 m; same scale');ax.legend();ax.grid(alpha=.3);fig.tight_layout();fig.savefig(OUT/'section-overlay.png',dpi=140)
    print(json.dumps(results,indent=2))
UV_COEF={}
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--before',required=True);run(p.parse_args().before)
