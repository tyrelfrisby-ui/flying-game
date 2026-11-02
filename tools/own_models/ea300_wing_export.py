"""Replace only wing meshes in an existing EA300 GLB; retain the approved cockpit/card/rig.
The procedural builder's --wing-only path uses this after its normal full export.
"""
from glb_visuals import Glb
NAMES={'Wing','Aileron_L','Aileron_R'}
def replace_wings(baseline, generated, output):
    old,new=Glb(baseline),Glb(generated)
    materials={m['name']:i for i,m in enumerate(old.doc['materials'])}
    nodes={n['name']:n for n in old.doc['nodes']}
    for n in new.doc['nodes']:
        if n.get('name') not in NAMES:continue
        dst=nodes[n['name']]
        for key in ('translation','rotation','scale','matrix'):
            assert dst.get(key)==n.get(key),(n['name'],key,'hinge transform changed')
        prims=[]
        for p in new.doc['meshes'][n['mesh']]['primitives']:
            attrs={k:old.accessor(new.array(v),new.doc['accessors'][v]['type'],new.doc['accessors'][v]['componentType']) for k,v in p['attributes'].items()}
            ix=p['indices'];acc=new.doc['accessors'][ix]
            prims.append(dict(attributes=attrs,indices=old.accessor(new.array(ix),acc['type'],acc['componentType']),material=materials[new.doc['materials'][p['material']]['name']]))
        old.doc['meshes'][dst['mesh']]['primitives']=prims
    old.save(output)
if __name__=='__main__':
    import sys
    replace_wings(*sys.argv[1:])
