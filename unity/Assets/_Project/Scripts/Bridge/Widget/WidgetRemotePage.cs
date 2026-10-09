namespace FlyingGame.Bridge.Widget
{
    /// <summary>The phone / iPad remote (owner 2026-10-07): open http://&lt;mac&gt;:47831/ in Safari — no app to install. A touch
    /// stick (aileron / elevator), rudder and power sliders, scenario / aircraft / view pickers, presets, pause and slow-mo.
    /// Sends {"cmd":"controls",…} 20× a second while touched.</summary>
    public static class WidgetRemotePage
    {
        public const string Html = @"<!doctype html><html><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no'>
<title>Aero Widget remote</title>
<style>
 body{margin:0;background:#111;color:#eee;font:15px -apple-system,Helvetica,sans-serif;touch-action:none;user-select:none;-webkit-user-select:none}
 .row{display:flex;gap:8px;padding:8px;flex-wrap:wrap;align-items:center}
 button,select{background:#2a2d33;color:#eee;border:1px solid #444;border-radius:8px;padding:10px 12px;font-size:15px}
 button.on{background:#2f7a3a}
 #stick{width:min(80vw,60vh);height:min(80vw,60vh);margin:8px auto;border-radius:16px;background:#1d2026;border:1px solid #444;position:relative}
 #dot{position:absolute;width:44px;height:44px;margin:-22px 0 0 -22px;border-radius:22px;background:#4fd1ff;left:50%;top:50%}
 .lbl{color:#aaa;font-size:13px} input[type=range]{width:60vw}
 #st{font:12px Menlo,monospace;color:#9f9;white-space:pre;padding:6px 10px}
 .cond{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;padding:8px}
 .cond button{font-size:19px;font-weight:600;padding:16px 4px}
 .cond button.on{background:#1f6fd1;border-color:#5aa2ff}
 select:disabled{opacity:.4}
 .ap{display:flex;gap:6px;padding:6px 8px;flex-wrap:wrap;align-items:center;background:#15181d;border-top:1px solid #333;border-bottom:1px solid #333}
 .ap button{padding:8px 9px;font-size:14px;font-weight:600}
 .ap button.on{background:#1f6f3a;border-color:#4fd18a}
 .ap button.off{opacity:.35}
 .chips{display:flex;flex-wrap:wrap;gap:6px;padding:6px 8px}
 .chips button{padding:7px 9px;font-size:13px;border-radius:14px}
 .chips button.on{background:#1f6fd1;border-color:#5aa2ff}
 .chips .grp{width:100%;color:#888;font-size:12px;margin-top:4px}
 #fma{font:600 13px Menlo,monospace;color:#7CFC9a;padding:4px 10px}
 #hold{position:absolute;top:8px;left:10px;font:600 13px -apple-system,sans-serif;color:#ffcf5a;display:none}
</style></head><body>
<div id='pf' class='row' style='display:none;background:#2a2410;color:#ffcf5a'><span id='pft'></span>
 <button id='req' onclick=""requestControls()"">Request controls</button></div>
<div id='banner' class='row' style='display:none;background:#14532d;color:#fff;font-weight:600'></div>
<div class='cond'>
 <button id='c-cruise' onclick=""cmd({cmd:'start',condition:'cruise',aircraft:document.getElementById('ac').value})"">Cruise</button>
 <button id='c-final' onclick=""cmd({cmd:'start',condition:'final',aircraft:document.getElementById('ac').value})"">Final</button>
 <button id='c-spin' onclick=""cmd({cmd:'start',condition:'spin',aircraft:document.getElementById('ac').value})"">Spin</button>
</div>
<div class='row'>
 <button id='d-ntsb' onclick=""cmd({cmd:'display',mode:'ntsb'})"">NTSB</button><button id='d-classic' onclick=""cmd({cmd:'display',mode:'classic'})"">Classic</button>
</div>
<div class='row'>
 <button onclick=""cmd({cmd:'scenario',name:'flare'})"">Flare</button>
 <button onclick=""cmd({cmd:'scenario',name:'spin'})"">Spin</button>
 <select id='ac' onchange=""cmd({cmd:'scenario',aircraft:this.value})""></select>
 <select id='view' onchange=""cmd({cmd:'view',name:this.value})""><option>side</option><option>behind</option><option>front</option><option>top</option><option>chase</option><option value='locked'>locked (direction)</option><option value='body'>body (airplane-fixed, left)</option></select>
</div>
<div class='row'>
 <button onclick=""cmd({cmd:'preset',name:'spin-entry'})"">Spin entry</button>
 <button onclick=""cmd({cmd:'preset',name:'spin-developed'})"">Developed</button>
 <button onclick=""cmd({cmd:'preset',name:'spin-recovery'})"">PARE recovery</button>
 <button onclick=""cmd({cmd:'preset',name:'flare-demo'})"">Flare demo</button>
 <button onclick=""cmd({cmd:'preset',name:'flare-hands-off'})"">Hands off</button>
</div>
<div class='row'>
 <button onclick=""cmd({cmd:'pause'})"">Pause</button><button onclick=""cmd({cmd:'resume'})"">Run</button>
 <button onclick=""cmd({cmd:'timescale',value:1})"">×1</button><button onclick=""cmd({cmd:'timescale',value:0.5})"">×½</button><button onclick=""cmd({cmd:'timescale',value:0.25})"">×¼</button>
 <button onclick=""cmd({cmd:'reset'})"">Reset</button>
</div>
<div class='row'>
 <button onclick=""cmd({cmd:'rewind',seconds:1})"">◀︎◀︎</button><button onclick=""cmd({cmd:'step',frames:-1})"">◀︎ frame</button>
 <button onclick=""cmd({cmd:'step',frames:1})"">frame ▶︎</button><button onclick=""cmd({cmd:'seek',offsetMs:Math.min(0,rvOff+1000)})"">▶︎▶︎</button>
 <button onclick=""cmd({cmd:'play',rate:0.25,direction:'reverse'})"">◀ ¼</button><button onclick=""cmd({cmd:'play',rate:0.25,direction:'forward'})"">▶ ¼</button>
 <button onclick=""cmd({cmd:'resume'})"">Fly from here</button><button onclick=""cmd({cmd:'resume',from:'live'})"">Back to live</button>
</div>
<div class='row'><span class='lbl'>History</span><input id='scrub' type='range' min='-60000' max='0' step='33' value='0'><span id='rv' class='lbl'></span></div>
<div id='fma'></div>
<div class='ap'>
 <button id='ap-on' onclick=""cmd({cmd:'autopilot',on:!(S.autopilot||{}).on})"">AP</button>
 <button id='ap-alt' onclick=""cmd({cmd:'autopilot',on:true,mode:'alt'})"">ALT</button>
 <button id='ap-vs' onclick=""cmd({cmd:'autopilot',on:true,mode:'vs',vsFpm:(S.autopilot||{}).vsFpm||500})"">VS</button>
 <button onclick=""cmd({cmd:'autopilot',vsFpm:((S.autopilot||{}).vsFpm||0)-100})"">VS▼</button>
 <button onclick=""cmd({cmd:'autopilot',vsFpm:((S.autopilot||{}).vsFpm||0)+100})"">VS▲</button>
 <button id='ap-flc' onclick=""cmd({cmd:'autopilot',on:true,mode:'flc'})"">FLC</button>
 <button onclick=""cmd({cmd:'autopilot',kias:((S.autopilot||{}).kias||90)-5})"">SPD−</button>
 <button onclick=""cmd({cmd:'autopilot',kias:((S.autopilot||{}).kias||90)+5})"">SPD+</button>
 <button onclick=""cmd({cmd:'autopilot',altitudeFt:((S.autopilot||{}).altitudeFt||3000)-500})"">ALT−</button>
 <button onclick=""cmd({cmd:'autopilot',altitudeFt:((S.autopilot||{}).altitudeFt||3000)+500})"">ALT+</button>
</div>
<div class='ap'>
 <button id='ap-hdg' onclick=""cmd({cmd:'autopilot',on:true,mode:'hdg'})"">HDG</button>
 <button onclick=""cmd({cmd:'autopilot',headingBump:-10})"">−10</button><button onclick=""cmd({cmd:'autopilot',headingBump:-1})"">−1</button>
 <span id='bug' style='font:600 15px Menlo;color:#ffcf5a;min-width:52px;text-align:center'>---</span>
 <button onclick=""cmd({cmd:'autopilot',headingBump:1})"">+1</button><button onclick=""cmd({cmd:'autopilot',headingBump:10})"">+10</button>
 <button onclick=""cmd({cmd:'autopilot',headingSync:true})"">SYNC</button>
 <button id='ap-yd' onclick=""cmd({cmd:'autopilot',yawDamper:!(S.autopilot||{}).yawDamper})"">YD</button>
 <button id='ap-at' onclick=""cmd({cmd:'autopilot',autothrottle:!(S.autopilot||{}).autothrottle})"">A/T</button>
 <button class='off' disabled title='a later build'>LNAV</button><button class='off' disabled title='a later build'>LOC</button><button class='off' disabled title='a later build'>APP</button>
</div>
<div class='row'><select id='lesson' onchange=""if(this.value)cmd({cmd:'lesson',id:this.value})""><option value=''>Lesson…</option></select>
 <button onclick=""cmd({cmd:'lesson',id:'stop'})"">Stop lesson</button></div>
<div class='row'><span class='lbl'>Weight</span><input id='wt' type='range' min='0.6' max='1.4' step='0.01' value='1'><span id='wtv' class='lbl'></span></div>
<div class='row'><span class='lbl'>CG %MAC</span><input id='cg' type='range' min='-15' max='25' step='0.5' value='0'><span id='cgv' class='lbl'></span>
 <button onclick=""cmd({cmd:'loading',reset:true})"">Reset W&amp;B</button></div>
<div class='lbl' style='padding:6px 10px 0'>ON SCREEN — tap to switch on / off</div>
<div class='chips' id='chips'></div>
<div id='stick'><div id='hold'>HOLD</div><div id='dot'></div></div>
<div class='row'><span class='lbl'>Rudder</span><input id='rud' type='range' min='-1' max='1' step='0.01' value='0'></div>
<div class='row'><span class='lbl'>Power&nbsp;</span><input id='thr' type='range' min='0' max='1' step='0.01' value='0'></div>
<div class='row'><span class='lbl'>Brake&nbsp;</span><input id='brk' type='range' min='0' max='1' step='0.01' value='0'></div>
<div id='st'></div>
<script>
// This remote's identity (protocol 4): a stable id and the pilot's name, kept on this device.
let myId=null,myName='';try{myId=localStorage.getItem('aeroRemoteId');myName=localStorage.getItem('aeroRemoteName')||'';}catch(e){}
if(!myId){myId='web-'+Math.random().toString(36).slice(2,10);try{localStorage.setItem('aeroRemoteId',myId);}catch(e){}}
function cmd(o){o.from=myId;o.name=myName;return fetch('/cmd',{method:'POST',body:JSON.stringify(o)}).then(r=>r.json()).catch(()=>{});}
function requestControls(){if(!myName){myName=(prompt('Your name (shown as the pilot flying)')||'').trim();if(!myName)return;try{localStorage.setItem('aeroRemoteName',myName);}catch(e){}}
 cmd({cmd:'requestControls'});document.getElementById('req').textContent='Requested…';}
let S={},FIT=null;
// Every vector, display element and inset as a switch (owner 2026-10-09: radio buttons for all the vectors and displays).
const NAMES={vectors:'All vectors',wingWind:'Wing relative winds',tailWind:'Tail relative winds (downwash)',inertial:'Inertial force',total:'Total aero force',tail:'Tail force',moments:'Pitch moments',
 lift:'Lift',drag:'Drag',weight:'Weight',thrust:'Thrust',wind:'Relative wind (CG)',strips:'Per-strip lift/drag',axis:'Rotation axis',wheels:'Wheel loads',labels:'Labels',
 readout:'Readout (classic)',review:'REVIEW tag',controlsDisplay:'Control panel (classic)',controlTraces:'Control traces',horizon:'Horizon + ground grid',cgnp:'CG / NP marks',
 liftComponents:'Lift components',wingDrag:'Each wing\'s drag',groundTrack:'Ground track + wind',aimPoint:'Aim point',propEffects:'Prop effects'};
const INSETS={clAlpha:'CL–α curve',liftDrag:'L/D curve',powerRequired:'Power required',ball:'Slip/skid ball',aoa:'AoA gauge',wb:'Weight & balance'};
const GROUPS=[['Forces & flow',['vectors','wingWind','tailWind','inertial','total','tail','moments','lift','drag','weight','thrust','wind','strips','wheels','axis','liftComponents','wingDrag','propEffects']],
 ['Picture',['labels','horizon','cgnp','groundTrack','aimPoint','readout','review','controlsDisplay','controlTraces']]];
function buildChips(){const c=document.getElementById('chips');c.innerHTML='';
 GROUPS.forEach(([g,keys])=>{const d=document.createElement('div');d.className='grp';d.textContent=g;c.appendChild(d);
  keys.forEach(k=>{const b=document.createElement('button');b.id='sh-'+k;b.textContent=NAMES[k]||k;b.onclick=()=>{const o={cmd:'show'};o[k]=!((S.show||{})[k]);cmd(o);};c.appendChild(b);});});
 const d=document.createElement('div');d.className='grp';d.textContent='Insets';c.appendChild(d);
 Object.keys(INSETS).forEach(k=>{const b=document.createElement('button');b.id='in-'+k;b.textContent=INSETS[k];b.onclick=()=>cmd({cmd:'inset',name:k,show:!(S.insets||[]).includes(k)});c.appendChild(b);});}
buildChips();
fetch('/cmd',{method:'POST',body:JSON.stringify({cmd:'hello'})}).then(r=>r.json()).then(h=>{const sel=document.getElementById('lesson');(h.lessons||[]).forEach(l=>{const o=document.createElement('option');o.value=l.id;o.textContent=l.name;sel.appendChild(o);});window.HELLO=h;});
document.getElementById('wt').addEventListener('input',e=>{const f=(HELLO.fleet||[]).find(x=>x.id===S.aircraft);if(f&&f.loading)cmd({cmd:'loading',grossWeightLb:Math.round(f.loading.defaultLb*e.target.value)});});
document.getElementById('cg').addEventListener('input',e=>{const f=(HELLO.fleet||[]).find(x=>x.id===S.aircraft);if(f&&f.loading)cmd({cmd:'loading',cgPercentMac:f.loading.defaultCgMac+ +e.target.value});});
let viewOnly=false,lastPilot=null,bannerUntil=0;
fetch('/cmd',{method:'POST',body:JSON.stringify({cmd:'fleet'})}).then(r=>r.json()).then(j=>{const s=document.getElementById('ac');(j.fleet||[]).forEach(f=>{const o=document.createElement('option');o.value=f.id;o.textContent=f.name;s.appendChild(o);});});
const stick=document.getElementById('stick'),dot=document.getElementById('dot');let sx=0,sy=0,live=false;
function setFrom(t){const r=stick.getBoundingClientRect();sx=Math.max(-1,Math.min(1,((t.clientX-r.left)/r.width)*2-1));sy=Math.max(-1,Math.min(1,((t.clientY-r.top)/r.height)*2-1));dot.style.left=((sx+1)*50)+'%';dot.style.top=((sy+1)*50)+'%';}
stick.addEventListener('pointerdown',e=>{live=true;stick.setPointerCapture(e.pointerId);setFrom(e);});
stick.addEventListener('pointermove',e=>{if(live)setFrom(e);});
let hold=false;
stick.addEventListener('pointerup',e=>{live=false;if(!hold){sx=0;sy=0;dot.style.left='50%';dot.style.top='50%';}send();});
let rvOff=0,scrubbing=false;const scrub=document.getElementById('scrub');
scrub.addEventListener('input',()=>{scrubbing=true;cmd({cmd:'seek',offsetMs:+scrub.value});});
scrub.addEventListener('change',()=>{scrubbing=false;});
let touched=0;['rud','thr','brk'].forEach(id=>document.getElementById(id).addEventListener('input',()=>{touched=Date.now();}));
document.getElementById('rud').addEventListener('change',e=>{if(!hold)e.target.value=0;send();});
function send(){cmd({cmd:'controls',source:'remote',aileron:sx,elevator:-sy,rudder:+document.getElementById('rud').value,throttle:+document.getElementById('thr').value,brake:+document.getElementById('brk').value});}
setInterval(()=>{if(!viewOnly&&(live||Date.now()-touched<300))send();},50);
setInterval(()=>{fetch('/state?from='+encodeURIComponent(myId)+'&name='+encodeURIComponent(myName)).then(r=>r.json()).then(s=>{
 S=s;Object.keys(s.show||{}).forEach(k=>{const b=document.getElementById('sh-'+k);if(b)b.classList.toggle('on',!!s.show[k]);});
 Object.keys(INSETS).forEach(k=>{const b=document.getElementById('in-'+k);if(b)b.classList.toggle('on',(s.insets||[]).includes(k));});
 const a=s.autopilot||{};document.getElementById('fma').textContent=(a.fma||'AP OFF')+(a.disc?'  · AP DISC':'');
 document.getElementById('ap-on').classList.toggle('on',!!a.on);document.getElementById('ap-alt').classList.toggle('on',a.on&&a.pitchMode==='alt');
 document.getElementById('ap-vs').classList.toggle('on',a.on&&a.pitchMode==='vs');document.getElementById('ap-flc').classList.toggle('on',a.on&&a.pitchMode==='flc');
 document.getElementById('ap-hdg').classList.toggle('on',a.on&&a.rollMode==='hdg');document.getElementById('ap-yd').classList.toggle('on',!!a.yawDamper);document.getElementById('ap-at').classList.toggle('on',!!a.autothrottle);
 document.getElementById('bug').textContent=(''+Math.round(a.headingBug||0)).padStart(3,'0')+'°';
 const L=s.loading||{};document.getElementById('wtv').textContent=(L.grossWeightLb||0)+' lb';document.getElementById('cgv').textContent=(L.cgPercentMac||0).toFixed(1)+'%'+(L.withinLimits===false?' OUT':'');
 const p=s.pilot||{};viewOnly=!!(p.managed&&p.id!==myId);
 // Protocol 5: the condition buttons, HOLD (the stick stays where it's put; it shows the held position), the view picker.
 hold=!!s.hold;const dm=(s.display||{}).mode;document.getElementById('d-ntsb').classList.toggle('on',dm==='ntsb');document.getElementById('d-classic').classList.toggle('on',dm==='classic');document.getElementById('hold').style.display=hold?'block':'none';
 ['cruise','final','spin'].forEach(c=>document.getElementById('c-'+c).classList.toggle('on',s.condition===c));
 const vs=document.getElementById('view');vs.disabled=!!s.viewFixed;
 [...vs.options].forEach(o=>o.disabled=s.condition==='spin'&&o.value!=='locked'&&o.value!=='body');
 if(document.activeElement!==vs)vs.value=s.view;
 if(hold&&!live&&Date.now()-touched>600){const c=s.controls||{};sx=c.aileron||0;sy=-(c.elevator||0);dot.style.left=((sx+1)*50)+'%';dot.style.top=((sy+1)*50)+'%';document.getElementById('rud').value=c.rudder||0;}
 document.getElementById('pf').style.display=viewOnly?'flex':'none';
 document.getElementById('pft').textContent=viewOnly?`${p.name} is flying — view only`:'';
 if(!viewOnly)document.getElementById('req').textContent='Request controls';
 if(p.id!==lastPilot){if(p.id&&p.id===myId)bannerUntil=Date.now()+2500;lastPilot=p.id;}
 const b=document.getElementById('banner');b.style.display=Date.now()<bannerUntil?'flex':'none';b.textContent=`YOU HAVE THE FLIGHT CONTROLS — ${p.name||''}`;
 document.getElementById('st').textContent=
 (()=>{const r=s.review||{};rvOff=r.offsetMs||0;scrub.min=-(r.historyMs||60000);if(!scrubbing)scrub.value=rvOff;
 document.getElementById('rv').textContent=r.active?`REVIEW ${(rvOff/1000).toFixed(1)} s${r.playing?' '+(r.direction=='reverse'?'◀':'▶')+'×'+r.rate:''}`:'live';return '';})()+
 `${s.scenario} ${s.aircraft} ${s.paused?'PAUSED':''} ×${(+s.timescale).toFixed(2)} [${s.source}] ${s.preset||''}\n`+
 `KIAS ${s.kias.toFixed(0)}  α ${s.alpha.toFixed(1)}°  β ${s.beta.toFixed(1)}°  pitch ${s.pitch.toFixed(0)}°  roll ${s.roll.toFixed(0)}°\n`+
 `yaw ${s.yawRate.toFixed(0)}°/s  L α ${s.leftWingAlpha.toFixed(0)}°${s.leftStalled?' STALL':''}  R α ${s.rightWingAlpha.toFixed(0)}°${s.rightStalled?' STALL':''}  ht ${s.heightFt.toFixed(0)} ft`;}).catch(()=>{});},300);
</script></body></html>";
    }
}
