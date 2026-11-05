"""Original generic Grob dial art, Pillow; no reference image pixels are used."""
from pathlib import Path
import math
from PIL import Image, ImageDraw, ImageFont
ART=Path(__file__).resolve().parent/'art'
def font(n):
    return ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',n)
def make():
    ART.mkdir(exist_ok=True)
    for name,unit,vals,angle in [('asi','KM/H',range(0,281,20),lambda v:20+v*320/280),('alt','100 FT',range(10),lambda v:v*36),('vario','KTS',range(-10,11,2),lambda v:-90+v*15),('compass','MAG',range(0,360,30),lambda v:v)]:
        im=Image.new('RGB',(512,512),(16,18,21));d=ImageDraw.Draw(im)
        def p(a,r):return (256+r*math.sin(math.radians(a)),256-r*math.cos(math.radians(a)))
        d.ellipse((7,7,505,505),outline=(90,95,100),width=5)
        if name=='asi':
            for a,b,col in [(80,170,'#38ad65'),(170,250,'#e5cb43')]:d.arc((32,32,480,480),angle(a)-90,angle(b)-90,fill=col,width=13)
            d.line([p(angle(250),196),p(angle(250),239)],fill='#eb4444',width=7)
        for v in vals:
            a=angle(v);d.line([p(a,207),p(a,239)],fill='white',width=5)
            s=({'0':'N','90':'E','180':'S','270':'W'}.get(str(v),str(v//10)) if name=='compass' else str(abs(v)))
            x,y=p(a,169);d.text((x,y),s,font=font(32),fill='white',anchor='mm')
        d.text((256,330),unit,font=font(27),fill='white',anchor='mm')
        if name=='alt':d.text((256,290),'ALT',font=font(24),fill='white',anchor='mm')
        im.save(ART/f'grob103-dial-{name}.png')
    for label in ['TALK FLYING','N103TF']:
        im=Image.new('RGB',(1024,256),(245,246,243));d=ImageDraw.Draw(im)
        d.text((512,128),label,font=font(108),fill=(23,39,65),anchor='mm')
        im.save(ART/('grob103-'+('wordmark' if label.startswith('TALK') else 'mark')+'.png'))
if __name__=='__main__':make()
