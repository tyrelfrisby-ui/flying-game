"""Small offline GLB editor for visual additions; preserves existing buffers and geometry.

Used by builders' --offline-update when Blender cannot start (e.g. Metal unavailable).
No triangulation/re-export of the existing airframe. Python stdlib, NumPy and Pillow.
"""
import io
import json
import struct
from pathlib import Path

import numpy as np


class Glb:
    def __init__(self, path=None):
        if path:
            raw = Path(path).read_bytes()
            assert raw[:4] == b'glTF' and struct.unpack_from('<I', raw, 4)[0] == 2
            n = struct.unpack_from('<I', raw, 12)[0]
            self.doc = json.loads(raw[20:20+n])
            size = struct.unpack_from('<I', raw, 20+n)[0]
            self.bin = bytearray(raw[28+n:28+n+size])
        else:
            self.bin = bytearray()
            self.doc = dict(asset=dict(version='2.0', generator='Aero Playground visual assets'),
                            scenes=[dict(nodes=[])], scene=0, nodes=[], meshes=[], materials=[],
                            buffers=[dict(byteLength=0)], bufferViews=[], accessors=[])

    def data(self, data, **fields):
        self.bin.extend(b'\0' * (-len(self.bin) % 4))
        view = dict(buffer=0, byteOffset=len(self.bin), byteLength=len(data), **fields)
        self.bin.extend(data)
        self.doc['bufferViews'].append(view)
        return len(self.doc['bufferViews']) - 1

    def accessor(self, data, kind, component=5126):
        a = np.asarray(data, dtype={5126:'<f4', 5125:'<u4', 5123:'<u2'}[component])
        v = self.data(a.tobytes())
        entry = dict(bufferView=v, componentType=component, count=len(a), type=kind)
        if kind == 'VEC3': entry.update(min=a.min(0).tolist(), max=a.max(0).tolist())
        self.doc['accessors'].append(entry)
        return len(self.doc['accessors']) - 1

    def array(self, index):
        a = self.doc['accessors'][index]; v = self.doc['bufferViews'][a['bufferView']]
        dtype = np.dtype({5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']])
        dim = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        return np.ndarray((a['count'],dim), dtype=dtype, buffer=self.bin,
                          offset=v.get('byteOffset',0)+a.get('byteOffset',0),
                          strides=(v.get('byteStride',dim*dtype.itemsize),dtype.itemsize)).copy()

    def world_matrices(self):
        """Active-scene node transforms, glTF column-vector convention."""
        result={}
        def visit(i,parent):
            node=self.doc['nodes'][i]
            if 'matrix' in node: local=np.array(node['matrix']).reshape(4,4).T
            else:
                x,y,z,w=node.get('rotation',[0,0,0,1])
                rot=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
                              [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
                              [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
                local=np.eye(4);local[:3,:3]=rot@np.diag(node.get('scale',[1,1,1]))
                local[:3,3]=node.get('translation',[0,0,0])
            result[i]=parent@local
            for child in node.get('children',[]): visit(child,result[i])
        for i in self.doc['scenes'][self.doc.get('scene',0)]['nodes']: visit(i,np.eye(4))
        return result

    def material(self, name, rgb=(1,1,1), png=None):
        m = dict(name=name, doubleSided=False,
                 pbrMetallicRoughness=dict(baseColorFactor=[*rgb,1],metallicFactor=0,roughnessFactor=0.9))
        if png:
            v = self.data(Path(png).read_bytes())
            images = self.doc.setdefault('images',[]); images.append(dict(name=Path(png).stem,bufferView=v,mimeType='image/png'))
            samplers = self.doc.setdefault('samplers',[])
            samplers.append(dict(magFilter=9729,minFilter=9987,wrapS=33071,wrapT=33071))
            textures = self.doc.setdefault('textures',[])
            textures.append(dict(source=len(images)-1,sampler=len(samplers)-1))
            m['pbrMetallicRoughness']['baseColorTexture'] = dict(index=len(textures)-1)
        existing=next((i for i,mat in enumerate(self.doc['materials']) if mat.get('name')==name),None)
        if existing is not None:
            self.doc['materials'][existing]=m
            return existing
        self.doc['materials'].append(m)
        return len(self.doc['materials'])-1

    def mesh(self, name, positions, triangles, material, uv=None):
        p = np.asarray(positions); triangles = np.asarray(triangles).reshape(-1,3)
        # Flat triangles, explicit normals; caller supplies UVs per original vertex.
        flat = p[triangles].reshape(-1,3)
        n = np.cross(p[triangles[:,1]]-p[triangles[:,0]],p[triangles[:,2]]-p[triangles[:,0]])
        n /= np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-10)
        attrs = dict(POSITION=self.accessor(flat,'VEC3'), NORMAL=self.accessor(np.repeat(n,3,axis=0),'VEC3'))
        if uv is not None: attrs['TEXCOORD_0'] = self.accessor(np.asarray(uv)[triangles].reshape(-1,2),'VEC2')
        self.doc['meshes'].append(dict(name=name,primitives=[dict(attributes=attrs,material=material,mode=4)]))
        existing=next((n for n in self.doc['nodes'] if n.get('name')==name),None)
        if existing is not None:
            self.doc['meshes'][existing['mesh']]=self.doc['meshes'].pop()
            return
        self.doc['nodes'].append(dict(name=name,mesh=len(self.doc['meshes'])-1))
        self.doc['scenes'][self.doc.get('scene',0)]['nodes'].append(len(self.doc['nodes'])-1)

    def save(self, path):
        from instrument_face_style import apply_glb
        apply_glb(self, cream=Path(path).parent.name.startswith(('j3-', 'pa18-')))
        self.doc['buffers'][0]['byteLength'] = len(self.bin)
        js = json.dumps(self.doc,separators=(',',':')).encode(); js += b' ' * (-len(js)%4)
        buf = bytes(self.bin); buf += b'\0' * (-len(buf)%4)
        raw = struct.pack('<4sII',b'glTF',2,28+len(js)+len(buf))+struct.pack('<I4s',len(js),b'JSON')+js+struct.pack('<I4s',len(buf),b'BIN\0')+buf
        Path(path).write_bytes(raw)

    def image(self, texture, mode="RGB"):
        from PIL import Image
        im = self.doc['images'][self.doc['textures'][texture]['source']]
        v = self.doc['bufferViews'][im['bufferView']]
        return Image.open(io.BytesIO(self.bin[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])).convert(mode)


def checker_update(path):
    from PIL import Image
    """Same projections as build_pitts_s2b.py; only replace previously plain red undersides."""
    from PIL import Image
    glb = Glb(path)
    if glb.doc.get('extras',{}).get('iacCheckerUndersides'): return
    q = 1.016 / 4
    import tempfile
    with tempfile.TemporaryDirectory(prefix='iac-checker-') as td:
        for name, yr, xr, w, h, parts in (
            ('pitts_uw_bot',(-3.15,3.15),(-0.75,0.72),4096,1024,('Wing_Upper','Aileron_UL','Aileron_UR')),
            ('pitts_stab_bot',(-1.25,1.25),(-3.85,-2.55),2048,1024,('Stab','Elevator_L','Elevator_R'))):
            y,x = np.meshgrid(np.linspace(*yr,w,endpoint=False)+(yr[1]-yr[0])/w/2,
                              np.linspace(*xr,h,endpoint=False)+(xr[1]-xr[0])/h/2)
            if name == 'pitts_uw_bot':
                d = np.clip((np.abs(y)-(3.048-0.508))/0.508,0,1)
                le = 0.664-0.0948*np.abs(y)-0.508*(1-np.sqrt(np.maximum(1-d*d,0.0004)))
            else: le = -3.30+0.621
            mask = (np.floor(np.abs(y)/q)+np.floor((le-x)/q))%2 == 1
            rgb = np.where(mask[:,:,None],(246,246,246),(200,24,36)).astype('uint8')
            png = Path(td)/(name+'.png'); Image.fromarray(rgb[::-1]).save(png)
            mat = glb.material('Paint_'+name,png=png)
            for node in glb.doc['nodes']:
                if node.get('name') not in parts: continue
                for prim in glb.doc['meshes'][node['mesh']]['primitives']:
                    if glb.doc['materials'][prim['material']]['name'] != 'Paint_red': continue
                    pos = glb.array(prim['attributes']['POSITION'])
                    # File axes (-builder x, builder z, builder y), glTF v points down.
                    uv = np.column_stack(((pos[:,2]-yr[0])/(yr[1]-yr[0]),1-(-pos[:,0]-xr[0])/(xr[1]-xr[0])))
                    prim['attributes']['TEXCOORD_0'] = glb.accessor(uv,'VEC2'); prim['material'] = mat
    glb.doc.setdefault('extras',{})['iacCheckerUndersides'] = True
    glb.save(path)
