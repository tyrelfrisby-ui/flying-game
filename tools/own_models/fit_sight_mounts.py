"""Bake structure-only mount witnesses/collars into sights-spec.json (CPU, no Blender).

Run before sights.py when the base airframe changes. Anchors never use the eye.
Rays intersect the named current airframe mesh; the 24 mm wide collars follow its
actual sections with 0.25 mm engagement and a 3 mm metal wall. The small engagement
prevents a visible floating band; only fittings engage the airframe, never tubes.
"""
import json
import math
import numpy as np
from glb_visuals import Glb
from sights import spec, SPEC, MODELS, parts, body, ray_hits


def fit():
    data=spec()
    for aircraft,row in data['aircraft'].items():
        mount=row['mount'];g=Glb(MODELS/aircraft/row['glbs'][0])
        tris=np.concatenate([body(a[ids],row) for n,p,a,ids in parts(g) if n==mount['structure']])
        tris=tris[tris[:,:,1].mean(1)>0]
        for anchor in mount['anchors']:
            origin=np.array(anchor['probe']);direction=np.array(anchor['direction'],float)
            hits=ray_hits(tris,origin,direction);assert len(hits),(aircraft,anchor['id'])
            surface=origin+hits.min()*direction
            anchor['surface_body_m']=surface.tolist();anchor['outward_body']=(-direction).tolist()
            if 'section_centre' not in anchor:continue
            # Centre the collar inside the strut at this fixed structural station.
            far=origin+hits.max()*direction;centre=(surface+far)/2
            anchor['section_centre']=centre.tolist()
            axis=np.array(anchor['section_axis'],float);axis/=np.linalg.norm(axis)
            u=np.array([0,1,0.]);v=np.cross(axis,u);v/=np.linalg.norm(v)
            radii=[];count=32
            for end in [-1,1]:
                c=centre+end*axis*mount['collar_width_m']/2
                ring=[]
                for k in range(count):
                    radial=u*math.cos(k*2*math.pi/count)+v*math.sin(k*2*math.pi/count)
                    h=ray_hits(tris,c,radial);assert len(h),(aircraft,anchor['id'],k)
                    ring.append(float(h.min()))
                radii.append(ring)
            anchor['collar_radii_m']=radii
            anchor.pop('collar_vertices_body_m',None);anchor.pop('collar_triangles',None)
            print(aircraft,anchor['id'],'surface',surface)
    SPEC.write_text(json.dumps(data,indent=2)+'\n')


if __name__=='__main__':fit()
