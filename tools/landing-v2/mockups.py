#!/usr/bin/env python3
"""Dependency-free SVG design boards. Illustrations, never claimed as Unity captures."""
from pathlib import Path
from html import escape
import argparse

P = argparse.ArgumentParser()
P.add_argument('--output', type=Path, default=Path('docs/landing-v2/mockups'))
a = P.parse_args(); a.output.mkdir(parents=True, exist_ok=True)

def board(device, w, h, page, night=False, picker=None):
    compact = h < 600
    bg, surface, raised, ink, muted, border = ('#101d2b','#1c2c3d','#293d50','#f5f7fa','#bdcdd9','#54687b') if night else ('#f1f4f2','#ffffff','#e5ece9','#152d35','#4b626b','#a4b6b5')
    out=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">',f'<rect width="{w}" height="{h}" fill="{bg}"/>']
    def rect(x,y,ww,hh,fill=surface,stroke='none',r=12):
        out.append(f'<rect x="{x}" y="{y}" width="{ww}" height="{hh}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>')
    def text(x,y,s,size=16,color=ink,weight=400):
        out.append(f'<text x="{x}" y="{y}" font-family="Arial, sans-serif" font-size="{size}" font-weight="{weight}" fill="{color}">{escape(s)}</text>')
    def path(d,stroke=ink,fill='none',sw=2):
        out.append(f'<path d="{d}" stroke="{stroke}" fill="{fill}" stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round"/>')
    def icon(x,y,kind,color=ink):
        out.append(f'<g transform="translate({x} {y})" stroke="{color}" fill="none" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">')
        shapes={
          'plane':'<path d="M2 15 L28 15 L23 12 L16 12 L13 3 L10 3 L11 12 L4 12 Z M10 16 L8 24 L11 24 L16 16"/>',
          'wheel':'<circle cx="15" cy="15" r="10"/><circle cx="15" cy="15" r="3"/>',
          'ski':'<path d="M3 23 L23 23 Q29 23 29 16 M14 6 L14 23 M7 6 L21 6"/>',
          'float':'<path d="M2 19 L28 19 L22 25 L5 25 Z M10 6 L10 19 M20 6 L20 19"/>',
          'pin':'<path d="M15 29 Q-3 12 8 5 Q15 0 22 5 Q33 12 15 29 Z"/><circle cx="15" cy="12" r="4"/>',
          'seat':'<path d="M8 3 L8 20 L25 20 L25 28 M8 12 L21 12 L21 20 M5 28 L9 20"/>',
          'sun':'<circle cx="15" cy="15" r="6"/><path d="M15 1 V5 M15 25 V29 M1 15 H5 M25 15 H29 M5 5 L8 8 M22 22 L25 25 M5 25 L8 22 M22 8 L25 5"/>',
          'moon':'<path d="M23 24 A12 12 0 1 1 10 3 A13 13 0 0 0 23 24 Z"/>',
          'bag':'<rect x="4" y="9" width="23" height="19" rx="3"/><path d="M10 9 V3 H21 V9 M4 17 H27"/>',
          'lesson':'<path d="M2 7 L15 2 L28 7 L15 12 Z M7 10 V20 Q15 26 23 20 V10 M28 7 V20"/>',
          'flag':'<path d="M6 29 V3 Q13 -1 20 4 Q24 7 28 3 V17 Q22 21 17 17 Q11 13 6 17"/>',
          'cloud':'<path d="M7 24 A6 6 0 0 1 6 12 A10 10 0 0 1 25 13 A6 6 0 0 1 25 25 Z"/>',
          'shield':'<path d="M15 2 L27 7 V16 Q26 24 15 29 Q4 24 3 16 V7 Z M9 15 L14 20 L22 10"/>',
          'eye':'<path d="M1 15 Q15 -1 29 15 Q15 31 1 15 Z"/><circle cx="15" cy="15" r="5"/>'}
        out.append(shapes.get(kind,shapes['plane']));out.append('</g>')
    def button(x,y,ww,label,primary=False):
        rect(x,y,ww,64 if device=='quest-panel' else 48,'#ffd15c' if primary else raised)
        text(x+16,y+(40 if device=='quest-panel' else 30),label,16,'#152d35' if primary else ink,700)
    def landscape(x,y,ww,hh):
        rect(x,y,ww,hh,'#203750' if night else '#bbd8df',r=0)
        out.append(f'<svg x="{x}" y="{y}" width="{ww}" height="{hh}" viewBox="0 0 600 220" preserveAspectRatio="xMidYMid meet">')
        out.append(f'<rect width="600" height="220" fill="{"#203750" if night else "#bbd8df"}"/>')
        out.append(f'<circle cx="490" cy="40" r="18" fill="{"#e6e8d0" if night else "#fff1b2"}"/>')
        path('M0 150 L90 65 L170 133 L270 32 L370 128 L450 74 L600 162 V220 H0 Z', 'none', '#415d67' if night else '#759997')
        path('M0 180 Q150 110 310 166 Q450 118 600 175 V220 H0 Z','none','#243e48' if night else '#93b1a0')
        path('M200 220 L301 146 L316 146 L420 220 Z','none','#52616a' if night else '#d1d6cf')
        # Deliberately a yellow glider illustration, not a livery/model fallback.
        path('M170 153 L370 146 L409 151 L370 157 L170 160 Z','none','#ffd15c')
        path('M278 151 L213 109 L229 106 L330 150 L365 180 L348 181 Z','none','#ffd15c')
        path('M365 150 L380 126 L390 125 L390 150 Z','none','#ffd15c')
        out.append('<ellipse cx="300" cy="147" rx="17" ry="6" fill="#496775"/>')
        out.append('</svg>')
    m=24 if compact else 40; rail=174 if compact else 250; gap=16
    main=w-2*m-rail-gap; top=80 if compact else 128; bottom=h-76 if compact else h-106
    text(m,28 if compact else 42,'AERO / PLAYGROUND',14,muted,700)
    for i,label in enumerate(['Aircraft & place','People & load','Conditions']):
        xx=m+i*(163 if compact else 220)
        text(xx,61 if compact else 91,f'{i+1:02}  {label}',14,ink if i==page else muted,700 if i==page else 400)
        if i==page: rect(xx,68 if compact else 103,146 if compact else 198,3,'#d6a325',r=0)
    # Right-hand constrained activity entry points, separate from setup.
    rx=m+main+gap
    rect(rx,top,rail,bottom-top-12)
    text(rx+18,top+28,'YOUR FLIGHT',12,muted,700)
    text(rx+18,top+55,'Free play',20,ink,700)
    text(rx+18,top+77,'Choose your own setup',12,muted)
    yy=top+(94 if compact else 116)
    for title,kind in [('Challenges','flag'),('Lessons','lesson')]:
        rect(rx+12,yy,rail-24,64 if device=='quest-panel' else 48,raised);icon(rx+21,yy+9,kind);text(rx+59,yy+30,title,14,ink,700);yy+=76 if device=='quest-panel' else 60
    if not compact:
        text(rx+18,yy+28,'A guided flight opens',14,muted)
        text(rx+18,yy+49,'its own allowed choices.',14,muted)
    if page==0:
        if compact:
            landscape(m,top,main,82)
            rect(m+12,top+10,236,55,surface)
            text(m+24,top+33,'Schweizer SGS 2-33',19,ink,700)
            text(m+24,top+53,'Your aircraft  /  Change aircraft ›',12,muted)
            y=top+94; cw=(main-gap*2)/3
            for i,(kind,title,sub) in enumerate([('wheel','Configuration','Wheels'),('pin','Location','Choose a place'),('eye','Launch view','Front cockpit')]):
                xx=m+i*(cw+gap);rect(xx,y,cw,105,surface,border);icon(xx+12,y+10,kind)
                text(xx+12,y+56,title,14,ink,700);text(xx+12,y+78,sub,12,muted);text(xx+12,y+96,'Browse ›',12,muted)
        else:
            text(m,top+29,'Where will you fly?',32,ink,700)
            text(m,top+58,'Start with your aircraft. Make the flight yours.',16,muted)
            landscape(m,top+80,main,240)
            rect(m+18,top+98,300,72,surface)
            text(m+34,top+126,'Schweizer SGS 2-33',24,ink,700)
            text(m+34,top+151,'Your aircraft  ·  Change aircraft ›',14,muted)
            y=top+340
            for i,(kind,title,sub) in enumerate([('wheel','Configuration','Wheels'),('pin','Location','Choose a place'),('eye','Launch view','Front cockpit')]):
                xx=m+i*((main-gap*2)/3+gap);ww=(main-gap*2)/3
                rect(xx,y,ww,108,surface,border);icon(xx+16,y+14,kind);text(xx+16,y+65,title,17,ink,700);text(xx+16,y+90,sub+' ›',14,muted)
            text(m,y+143,'CONFIGURATION',12,muted,700)
            # This is a browse-sheet design example; options are supplied/filtered by Claude.
            text(m,y+167,'Browse sheet example · only compatible options appear in game',13,muted)
            for i,(kind,label) in enumerate([('wheel','Wheels'),('ski','Skis'),('ski','Wheel-skis'),('float','Floats')]):
                xx=m+i*(main/4);ww=main/4-10
                rect(xx,y+184,ww,80,'#fff2c9' if i==0 else surface,'#9b7410' if i==0 else border)
                icon(xx+14,y+195,kind,'#152d35' if i==0 else ink);text(xx+14,y+246,('Selected · ' if i==0 else '')+label,14,'#152d35' if i==0 else ink,700)
    elif page==1:
        text(m,top+23,'Bring your crew.',23 if compact else 32,ink,700)
        y=top+38 if compact else top+60
        ch=70 if compact else 112
        for i,(title,sub) in enumerate([('Front seat · Pilot','You · flight controls'),('Rear seat','Default person or invite a player')]):
            yy=y+i*(ch+12);rect(m,yy,main,ch);icon(m+14,yy+17,'seat');text(m+58,yy+26,title,16,ink,700)
            text(m+58,yy+48,sub,12 if compact else 15,muted)
            if not compact: button(m+58,yy+57,235,'Choose occupant ›')
        y+=2*(ch+12)
        if compact:
            text(m,y+15,'Baggage & balance ›',14,ink,700)
            text(m,y+34,'Fuel: not applicable to this glider',12,muted)
        else:
            rect(m,y,main,162);icon(m+16,y+18,'bag');text(m+60,y+30,'Baggage & balance',20,ink,700)
            text(m+60,y+59,'Use the aircraft’s actual stations and limits.',16,muted)
            button(m+60,y+81,250,'Open load sheet ›')
            text(m,y+195,'Fuel: not applicable to this glider',16,muted)
            text(m,y+234,'Invites open a seat role: Passenger or Dual controls.',15,muted)
            text(m,y+259,'Only offer dual controls where that seat supports them.',15,muted)
    else:
        text(m,top+24,'Set the mood. Go flying.',23 if compact else 32,ink,700)
        y=top+38 if compact else top+65
        hh=62 if compact else 196
        landscape(m,y,main,hh)
        rect(m+10,y+9,190,42,surface);icon(m+20,y+15,'moon' if night else 'sun');text(m+60,y+36,'Night' if night else 'Daylight',17,ink,700)
        y+=hh+12
        cw=(main-2*gap)/3; th=85 if compact else 156
        for i,(kind,title,sub) in enumerate([('sun','Time of day','Night' if night else 'Daylight'),('cloud','Weather','Choose conditions'),('shield','Reliability','Choose realism')]):
            xx=m+i*(cw+gap);rect(xx,y,cw,th,surface,border);icon(xx+12,y+10,kind)
            text(xx+12,y+55,title,14 if compact else 18,ink,700);text(xx+12,y+75,sub+' ›',11 if compact else 14,muted)
            if not compact: text(xx+12,y+126,'Adjust ›',15,ink,700)
        if not compact:
            rect(m,y+th+20,main,96)
            text(m+20,y+th+51,'Ready when you are.',20,ink,700)
            text(m+20,y+th+79,'Review aircraft, location and load before launch.',15,muted)
    button(m,h-64 if compact else h-88,106,'‹ Back')
    text(m+124,h-35 if compact else h-58,f'{page+1} / 3',14,muted)
    button(w-m-(208 if page==2 else 210),h-64 if compact else h-88,208,'Launch flight  >' if page==2 else ['People & load  →','Conditions  →'][page],True)
    text(m,h-7 if compact else h-16,f'DESIGN MOCKUP · {device} · illustrative scenery / not a Unity capture',10,muted)
    if picker:
        # A single open browse sheet replaces the busy underlying setup controls.
        rect(m,top,w-2*m,bottom-top+4,surface,border)
        text(m+16,top+28,'Choose aircraft' if picker=='aircraft' else 'Choose location',22,ink,700)
        text(w-m-80,top+28,'Close ×',14,muted)
        text(m+16,top+49,'Illustrative cards · runtime uses actual aircraft / launch-view captures',11,muted)
        yy=top+62; ww=300 if compact else 350; hh=bottom-yy-25 if compact else 235
        labels=['Schweizer SGS 2-33','Piper J-3 Cub','Great Lakes'] if picker=='aircraft' else ['Airfield','River bar','Mountain strip']
        # The third card is deliberately cropped on phone to reveal horizontal scrolling.
        out.append(f'<svg x="{m+12}" y="{yy}" width="{w-2*m-24}" height="{hh+4}" viewBox="0 0 {w-2*m-24} {hh+4}">')
        for i,label in enumerate(labels):
            xx=i*(ww+16);rect(xx,1,ww,hh,surface,'#9b7410' if i==0 else border)
            
            if picker=='location' or i==0: landscape(xx+4,5,ww-8,max(38,hh-58))
            else:
                rect(xx+4,5,ww-8,max(38,hh-58),raised,r=0)
                icon(xx+ww/2-15,12,'plane')
                text(xx+12,max(44,hh-70),'Capture pending',12,muted)
            text(xx+12,hh-25,label,16,ink,700)
            text(xx+12,hh-7,'Selected' if i==0 else ('View aircraft ›' if picker=='aircraft' else 'Preview launch view ›'),12,muted)
        out.append('</svg>')
        text(m+16,bottom-2,'‹ Previous    Scroll / swipe to browse    Next ›',12,muted)
    out.append('</svg>')
    name=f'{device}-{page+1:02}-'+['aircraft-place','people-load','conditions'][page]+('-night' if night else '')+('-browse-'+picker if picker else '')+'.svg'
    (a.output/name).write_text('\n'.join(out))
    return name

names=[]
for device,w,h in [('iphone-landscape',852,393),('ipad',1366,1024),('quest-panel',1440,1000),('mac',1440,900)]:
    for page in range(3): names.append(board(device,w,h,page,night=page==2))
    names.append(board(device,w,h,2,night=False))
    for picker in ['aircraft','location']: names.append(board(device,w,h,0,picker=picker))
(a.output/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Landing v2 visual review</title><style>body{background:#dce3e3;font:16px Arial;margin:32px}img{width:100%;max-width:1440px;display:block;margin:12px 0 40px}a{color:#152d35}</style><h1>Aero Playground · visual review</h1><p>Illustrative design boards, not captured gameplay. Runtime interface integration and device validation pending.</p>'+''.join(f'<h2>{escape(n)}</h2><a href="{n}"><img src="{n}" alt="{n}"></a>' for n in names))
print(f'Wrote {len(names)} SVG boards and index to {a.output}')
