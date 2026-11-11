#!/usr/bin/env python3
"""Offline QA rasterizer for these SVG boards (Pillow + fontTools).
Not a browser or Unity renderer. Handles only the primitives used by mockups.py.
"""
import sys, re
from pathlib import Path
from xml.etree import ElementTree as ET
from PIL import Image, ImageDraw, ImageFont
from fontTools.svgLib.path import parse_path
from fontTools.pens.basePen import BasePen
from fontTools.misc.transform import Transform

class Pen(BasePen):
    def __init__(self, transform):
        super().__init__(None); self.t=transform; self.paths=[]; self.points=[]
    def _moveTo(self,p):
        if self.points:self.paths.append(self.points)
        self.points=[self.t.transformPoint(p)]
    def _lineTo(self,p): self.points.append(self.t.transformPoint(p))
    def _curveToOne(self,p1,p2,p3):
        p0=self._getCurrentPoint()
        for i in range(1,25):
            t=i/24; u=1-t
            self.points.append(self.t.transformPoint(tuple(u**3*p0[j]+3*u*u*t*p1[j]+3*u*t*t*p2[j]+t**3*p3[j] for j in range(2))))
    def _closePath(self):
        if self.points:self.points.append(self.points[0]);self.paths.append(self.points);self.points=[]
    def _endPath(self):
        if self.points:self.paths.append(self.points);self.points=[]

def render(root, image, t=Transform(), inherited=None):
    inherited=inherited or {}; style={**inherited,**root.attrib}; d=ImageDraw.Draw(image)
    tag=root.tag.split('}')[-1]
    def n(k,default=0):return float(root.get(k,default))
    def point(x,y):return t.transformPoint((x,y))
    def box(x,y,w,h):return (*point(x,y),*point(x+w,y+h))
    def color(k,default=None):
        v=style.get(k,default);return None if v in [None,'none'] else v
    fill=color('fill','black');stroke=color('stroke');sw=max(1,round(float(style.get('stroke-width',1))*t.xx))
    if tag=='svg' and root.get('x') is not None:
        ww,hh=round(n('width')),round(n('height'));vb=list(map(float,root.get('viewBox').split()))
        scale=min(ww/vb[2],hh/vb[3]); layer=Image.new('RGBA',(ww,hh))
        nt=Transform(scale,0,0,scale,(ww-vb[2]*scale)/2,(hh-vb[3]*scale)/2)
        for child in root:render(child,layer,nt)
        image.paste(layer,tuple(round(v) for v in point(n('x'),n('y'))),layer);return
    if tag=='g':
        vals=re.findall(r'translate\(([^)]+)',root.get('transform',''))
        if vals:
            xy=list(map(float,vals[0].split()));t=t.translate(*xy)
    elif tag=='rect':
        d.rounded_rectangle(box(n('x'),n('y'),n('width'),n('height')),radius=n('rx')*t.xx,fill=fill,outline=stroke,width=sw)
    elif tag in ['circle','ellipse']:
        rx=n('r') if tag=='circle' else n('rx');ry=n('r') if tag=='circle' else n('ry')
        d.ellipse(box(n('cx')-rx,n('cy')-ry,rx*2,ry*2),fill=fill,outline=stroke,width=sw)
    elif tag=='path':
        pen=Pen(t);parse_path(root.get('d'),pen);pen._endPath()
        for points in pen.paths:
            if len(points)<2:continue
            if fill:d.polygon(points,fill=fill)
            if stroke:d.line(points,fill=stroke,width=sw,joint='curve')
    elif tag=='text':
        bold=int(root.get('font-weight','400'))>=600
        font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial'+(' Bold' if bold else '')+'.ttf',round(n('font-size',16)*t.xx))
        d.text(point(n('x'),n('y')),root.text or '',font=font,fill=fill,anchor='ls')
    if tag not in ['text','path','rect','circle','ellipse']:
        for child in root:render(child,image,t,style)

for filename in Path(sys.argv[1]).glob('*.svg'):
    root=ET.parse(filename).getroot(); image=Image.new('RGB',(int(root.get('width')),int(root.get('height'))),'white')
    render(root,image); image.save(filename.with_suffix('.png'))
print('Rasterized SVG boards for offline visual QA')
