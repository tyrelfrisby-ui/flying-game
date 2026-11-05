"""CPU fallback review renders from exported v2 GLB; Blender startup unavailable.
No Unity execution. Blender render_grob103_blanc.py remains the studio renderer.
"""
import hashlib,json,math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from glb_visuals import Glb
from grob103_render_cpu import render
from build_grob103 import ROOT,OUT,ID
D=ROOT/'renders/grob103/blanc-18m';PATH=OUT/(ID+'-own.glb')
VIEWS=[('side',(1,.25,22),(1,.25,0),9.4,(0,1,0)),('three-quarter',(-13,8,17),(1,.2,0),19.3,(0,1,0)),('front',(-22,1,0),(0,.2,0),19.3,(0,1,0)),('top',(1,24,0),(1,0,0),19.3,(-1,0,0)),('gear-main',(-.2,-.4,2.7),(.5397,-.43,0),1.4,(0,1,0)),('gear-nose',(-2.2,-.35,2),(-1.4595,-.43,0),.9,(0,1,0)),('gear-nose-steered',(-2.2,-.35,2),(-1.4595,-.43,0),.9,(0,1,0)),('gear-tail',(4.5,-.1,2),(4.8768,-.19,0),.85,(0,1,0))]
def run():
 D.mkdir(exist_ok=True,parents=True)
 for name,eye,target,scale,up in VIEWS:
  path=PATH
  if name=='gear-nose-steered':
   g=Glb(PATH);n=next(n for n in g.doc['nodes'] if n['name']=='Nose_Steering');a=math.radians(25)/2;n['rotation']=[0,math.sin(a),0,math.cos(a)];path=Path('/tmp/grob18-steered.glb');g.save(path)
  render(path,D/(name+'.png'),eye,target,scale,up=up,image_size=(1600,1000),render_glass=True,smooth_normals=True,view_kind='exterior',label=name+(' / +25 deg ESTIMATED' if 'steered' in name else '')+' / exported GLB / CPU fallback')
  if name=='top':
   im=Image.open(D/'top.png');draw=ImageDraw.Draw(im);x0=800-9*1600/19.3;x1=800+9*1600/19.3;y=100
   draw.line((x0,y,x1,y),fill=(20,30,45),width=3)
   for x in [x0,x1]:draw.line((x,y-12,x,y+12),fill=(20,30,45),width=3)
   draw.rectangle((685,70,915,95),fill='white');draw.text((720,75),'18.0 m measured span',fill='black');im.save(D/'top.png')
 manifest=dict(renderer='Project CPU rasterizer fallback; Blender 5.2.2 crashes in Metal capability detection before Python',views=VIEWS,model_sha256=hashlib.sha256(PATH.read_bytes()).hexdigest(),visual_approval='PENDING')
 (D/'render-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
def contact_sheet():
 names=[v[0] for v in VIEWS]+['side-poh-overlay'];sheet=Image.new('RGB',(1800,1230),'white');draw=ImageDraw.Draw(sheet)
 for i,name in enumerate(names):
  im=Image.open(D/(name+'.png')).convert('RGB');im.thumbnail((590,375));x=(i%3)*600;y=(i//3)*410;sheet.paste(im,(x+(600-im.width)//2,y+27));draw.text((x+12,y+8),name.replace('-',' ')+(' (25 deg estimated)' if 'steered' in name else ''),fill='black')
 sheet.save(D/'contact-sheet.png')
if __name__=='__main__':run()
