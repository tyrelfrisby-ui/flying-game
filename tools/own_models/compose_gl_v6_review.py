"""Compose the unaltered Ty photo and final v6 render with Pillow; no cropping.
Usage: python3 tools/own_models/compose_gl_v6_review.py PATH_TO_TY_PHOTO
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
out=Path('renders/greatlakes/v6')
photo=Image.open(sys.argv[1]).convert('RGB')
render=Image.open(out/'gl6_photo_comparison.png').convert('RGB')
h=1200
photo=photo.resize((round(photo.width*h/photo.height),h),Image.Resampling.LANCZOS)
render=render.resize((round(render.width*h/render.height),h),Image.Resampling.LANCZOS)
canvas=Image.new('RGB',(photo.width+render.width+60,h+110),(24,27,31))
canvas.paste(photo,(20,80));canvas.paste(render,(photo.width+40,80))
draw=ImageDraw.Draw(canvas)
try:font=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',28)
except OSError:font=ImageFont.load_default()
draw.text((20,24),"Ty's reference photo",font=font,fill='white')
draw.text((photo.width+40,24),'Great Lakes v6 — three round knobs / 2 x 3 controls',font=font,fill='white')
canvas.save(out/'gl6_side_by_side_ty.png')
