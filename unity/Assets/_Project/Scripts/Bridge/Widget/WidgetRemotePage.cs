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
</style></head><body>
<div class='row'>
 <button onclick=""cmd({cmd:'scenario',name:'flare'})"">Flare</button>
 <button onclick=""cmd({cmd:'scenario',name:'spin'})"">Spin</button>
 <select id='ac' onchange=""cmd({cmd:'scenario',aircraft:this.value})""></select>
 <select id='view' onchange=""cmd({cmd:'view',name:this.value})""><option>side</option><option>behind</option><option>front</option><option>top</option><option>chase</option><option value='locked'>locked (direction)</option></select>
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
<div id='stick'><div id='dot'></div></div>
<div class='row'><span class='lbl'>Rudder</span><input id='rud' type='range' min='-1' max='1' step='0.01' value='0'></div>
<div class='row'><span class='lbl'>Power&nbsp;</span><input id='thr' type='range' min='0' max='1' step='0.01' value='0'></div>
<div class='row'><span class='lbl'>Brake&nbsp;</span><input id='brk' type='range' min='0' max='1' step='0.01' value='0'></div>
<div id='st'></div>
<script>
function cmd(o){return fetch('/cmd',{method:'POST',body:JSON.stringify(o)}).then(r=>r.json()).catch(()=>{});}
fetch('/cmd',{method:'POST',body:JSON.stringify({cmd:'fleet'})}).then(r=>r.json()).then(j=>{const s=document.getElementById('ac');(j.fleet||[]).forEach(f=>{const o=document.createElement('option');o.value=f.id;o.textContent=f.name;s.appendChild(o);});});
const stick=document.getElementById('stick'),dot=document.getElementById('dot');let sx=0,sy=0,live=false;
function setFrom(t){const r=stick.getBoundingClientRect();sx=Math.max(-1,Math.min(1,((t.clientX-r.left)/r.width)*2-1));sy=Math.max(-1,Math.min(1,((t.clientY-r.top)/r.height)*2-1));dot.style.left=((sx+1)*50)+'%';dot.style.top=((sy+1)*50)+'%';}
stick.addEventListener('pointerdown',e=>{live=true;stick.setPointerCapture(e.pointerId);setFrom(e);});
stick.addEventListener('pointermove',e=>{if(live)setFrom(e);});
stick.addEventListener('pointerup',e=>{live=false;sx=0;sy=0;dot.style.left='50%';dot.style.top='50%';send();});
let rvOff=0,scrubbing=false;const scrub=document.getElementById('scrub');
scrub.addEventListener('input',()=>{scrubbing=true;cmd({cmd:'seek',offsetMs:+scrub.value});});
scrub.addEventListener('change',()=>{scrubbing=false;});
let touched=0;['rud','thr','brk'].forEach(id=>document.getElementById(id).addEventListener('input',()=>{touched=Date.now();}));
document.getElementById('rud').addEventListener('change',e=>{e.target.value=0;send();});
function send(){cmd({cmd:'controls',source:'remote',aileron:sx,elevator:-sy,rudder:+document.getElementById('rud').value,throttle:+document.getElementById('thr').value,brake:+document.getElementById('brk').value});}
setInterval(()=>{if(live||Date.now()-touched<300)send();},50);
setInterval(()=>{fetch('/state').then(r=>r.json()).then(s=>{document.getElementById('st').textContent=
 (()=>{const r=s.review||{};rvOff=r.offsetMs||0;scrub.min=-(r.historyMs||60000);if(!scrubbing)scrub.value=rvOff;
 document.getElementById('rv').textContent=r.active?`REVIEW ${(rvOff/1000).toFixed(1)} s${r.playing?' '+(r.direction=='reverse'?'◀':'▶')+'×'+r.rate:''}`:'live';return '';})()+
 `${s.scenario} ${s.aircraft} ${s.paused?'PAUSED':''} ×${(+s.timescale).toFixed(2)} [${s.source}] ${s.preset||''}\n`+
 `KIAS ${s.kias.toFixed(0)}  α ${s.alpha.toFixed(1)}°  β ${s.beta.toFixed(1)}°  pitch ${s.pitch.toFixed(0)}°  roll ${s.roll.toFixed(0)}°\n`+
 `yaw ${s.yawRate.toFixed(0)}°/s  L α ${s.leftWingAlpha.toFixed(0)}°${s.leftStalled?' STALL':''}  R α ${s.rightWingAlpha.toFixed(0)}°${s.rightStalled?' STALL':''}  ht ${s.heightFt.toFixed(0)} ft`;}).catch(()=>{});},300);
</script></body></html>";
    }
}
