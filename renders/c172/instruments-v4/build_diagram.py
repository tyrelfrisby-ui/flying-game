"""Measured C172 panel layout schematic, read from the exported GLB (v3 build_review.py, diagram only).
Usage: python build_diagram.py <out_dir> <title>. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json,struct,sys
ROOT=Path(__file__).resolve().parents[3]; OUT=Path(sys.argv[1]); TITLE=sys.argv[2]
model=ROOT/'unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb'
raw=model.read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
cases=[json.loads(n['extras']['layout']) for n in g['nodes'] if n.get('name','').endswith('_case')]
fontpath='/System/Library/Fonts/Supplemental/Arial.ttf'
def font(n):return ImageFont.truetype(fontpath,n)
im=Image.new('RGB',(2800,1500),'#eee9dd');d=ImageDraw.Draw(im)
d.text((75,40),TITLE,font=font(48),fill='#202724')
d.text((75,105),'Design frame: right / forward / up (metres). Positions and mount sizes read from the exported GLB.',font=font(25),fill='#39413d')
scale=2050; ox=1120; oy=1120
def pt(x,z):return ox+x*scale,oy-z*scale
a=pt(-.52,.39);b=pt(.52,-.075);d.rounded_rectangle((a,b),radius=25,fill='#202827',outline='#65726c',width=4)
low0=pt(-.50,.0225);low1=pt(.50,-.0725);d.rectangle((low0,low1),fill='#d4c7a6')
names={'Airspeed':'AIRSPEED\nMPH','Attitude':'ATTITUDE','Altimeter':'ALTIMETER','Turn_coordinator':'TURN\nCOORDINATOR','DG':'DIRECTIONAL\nGYRO','VSI':'VERTICAL\nSPEED','Tach':'TACHOMETER','Suction':'SUCTION','Fuel_L':'FUEL QTY\nL','Fuel_R':'FUEL QTY\nR','Oil_press':'OIL\nPRESS','Oil_temp':'OIL\nTEMP','Ammeter':'AMMETER\nAMPS','EGT':'EGT','NAV1':'NAV 1','NAV2':'NAV 2','ADF':'ADF'}
# The magnetic compass sits above the glareshield, outside the main panel rectangle.
cx,cy=pt(0,.445);cw=.075*scale;ch=.055*scale
d.rectangle((cx-cw/2,cy-ch/2,cx+cw/2,cy+ch/2),fill='#202827',outline='#65726c',width=3)
d.multiline_text((cx,cy),'MAGNETIC\nCOMPASS',font=font(19),anchor='mm',align='center',fill='#fffce5')
d.line((cx,cy+ch/2,cx,pt(0,.39)[1]),fill='#65726c',width=4)
for n in cases:
 x,y=pt(n['x'],n['z']);r=(n['half_extent']-.005)*scale;half=n['half_extent']*scale
 d.rectangle((x-half,y-half,x+half,y+half),outline='#82958c',width=2)
 d.ellipse((x-r,y-r,x+r,y+r),fill='#0b100e',outline='#afbbad',width=3)
 for sx in [-1,1]:
  for sy in [-1,1]:d.ellipse((x+sx*(half-7)-3,y+sy*(half-7)-3,x+sx*(half-7)+3,y+sy*(half-7)+3),fill='#acb6b0')
 label=names[n['name']];fs=19 if r>65 else (17 if r>47 else 15)
 d.multiline_text((x,y),label,font=font(fs),fill='#fffce5',anchor='mm',align='center',spacing=3)
 # A short centre leader identifies the actual instrument centre beneath its name.
 d.line((x,y+22,x,y+r-9),fill='#b9c9be',width=2)
for n,z,h in [('AUDIO',.366,.028),('COM / NAV 1',.307,.075),('COM / NAV 2',.224,.075),('ADF RECEIVER',.151,.060),('DME',.094,.042),('TRANSPONDER',.042,.048)]:
 x,y=pt(.160,z);w=.177*scale;hh=h*scale;d.rectangle((x-w/2,y-hh/2,x+w/2,y+hh/2),fill='#111918',outline='#83958c',width=2);d.text((x,y),n,font=font(21),anchor='mm',fill='#f2eedb')
for i,t in enumerate(['BAT','ALT','BCN','LAND','TAXI','NAV','PITOT HT']):
 x,y=pt([-.460,-.421,-.382,-.343,-.267,-.224,-.174][i],-.019);d.rectangle((x-12,y-12,x+12,y+12),fill='#833527' if i<2 else '#313937');d.text((x,y+25),t,font=font(15),anchor='mm',fill='#101715')
for n,x,r,c in [('THROTTLE',-.040,.013,'#111816'),('MIXTURE',.035,.010,'#b62e20'),('CARB HEAT',.112,.008,'#303c37'),('CABIN HEAT',.255,.008,'#303c37'),('CABIN AIR',.335,.008,'#303c37')]:
 px,py=pt(x,-.030);d.ellipse((px-r*scale,py-r*scale,px+r*scale,py+r*scale),fill=c);d.text(pt(x,-.057),n,font=font(17),anchor='mm',fill='#111715')
d.text(pt(.400,.006),'FLAPS',font=font(16),anchor='mm',fill='#111715')
d.text(pt(.460,.006),'0 10 20 30 40',font=font(13),anchor='mm',fill='#111715')
for n,x,z in [('IGNITION',-.435,.035),('PRIMER',-.355,.035)]:
 px,py=pt(x,z);d.ellipse((px-12,py-12,px+12,py+12),fill='#9ea99f');d.text((px,py+24),n,font=font(15),anchor='mm',fill='#e9eddc')
# Callouts use the deliberately plain right panel area and the right page margin.
L={n['name']:n for n in cases}
def mid(a,b):return ((L[a]['x']+L[b]['x'])/2,(L[a]['z']+L[b]['z'])/2)
eng=sorted(['Oil_press','Oil_temp','Ammeter','EGT'],key=lambda n:L[n]['x']);nav=L['NAV1']['x']
order='engine column RIGHT of NAV heads' if L['Oil_press']['x']>nav else 'engine column LEFT of NAV heads'
callouts=[('Standard T six-pack',mid('Attitude','DG')),('Tachometer directly under VSI',(L['Tach']['x'],L['Tach']['z'])),('Suction directly under DG',(L['Suction']['x'],L['Suction']['z'])),('Separate left / right fuel quantity',mid('Fuel_L','Fuel_R')),('One engine column: oil press → oil temp → amps → EGT',mid('Oil_temp','Ammeter')),('NAV1 / NAV2 / ADF',(nav,L['NAV2']['z'])),('Intact radio stack',(.16,.224)),('Measured: '+order,(L['Oil_press']['x'],L['Oil_press']['z']+L['Oil_press']['half_extent']))]
# Keep the diagram itself unobscured; numbered pointers occupy its plain right panel.
for i,(label,point) in enumerate(callouts,1):
 y=250+(i-1)*90;d.text((2240,y),str(i),font=font(25),fill='#26372d')
 words=label.split();lines=['']
 for word in words:
  if len(lines[-1])+len(word)>30:lines.append(word)
  else:lines[-1]+=(' ' if lines[-1] else '')+word
 d.multiline_text((2290,y), '\n'.join(lines),font=font(23),fill='#26372d',spacing=3)
 px,py=pt(*point);d.ellipse((px-13,py-13,px+13,py+13),outline='#e5b34e',width=2)
 # Numeric index beside each target, joined by a short leader.
 d.line((px+13,py,px+35,py-18),fill='#e5b34e',width=2);d.text((px+40,py-26),str(i),font=font(18),fill='#e5b34e')
d.text((75,1330),'THROTTLE 13 mm  >  MIXTURE 10 mm  >  CARB HEAT 8 mm  •  plain translation-only plungers',font=font(26),fill='#26372d')
d.text((75,1380),'Carb heat, ammeter and EGT remain authored/unbound: ModelRig.cs supplies no matching runtime parameters.',font=font(24),fill='#26372d')
d.text((75,1425),'Schematic only; all photographic review PNGs are Unity 6000.3.23f1 / Metal captures of the final imported model and runtime rig.',font=font(22),fill='#526057')
im.save(OUT/'c172-panel-layout-diagram.png')
print(OUT/'c172-panel-layout-diagram.png',order)
