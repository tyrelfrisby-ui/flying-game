#!/usr/bin/env python3
"""Compare the compiled sim/render evaluator with the authoring source. Standard library only."""
import hashlib,json,random,shutil,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'docs/pending/tools'))
import generate_island as g
points=[[-20+i*.5,-20+j*.5] for i in range(81) for j in range(81)]
r=random.Random(20261030);points += [[r.uniform(-20,20),r.uniform(-20,20)] for _ in range(1000)]
for f in g.FIELDS:
 l,b=g.dimensions(f);u=g.vec(f['heading_true_deg']);v=[u[1],-u[0]]
 points += [[f['centre'][k]+a*l*u[k]+c*b*v[k] for k in [0,1]] for a in [-.5,0,.5] for c in [-.5,0,.5]]
for q in g.W.get('mesa_pads',[]):points += [[q['centre'][0]+e*q['width_m']/2000,q['centre'][1]+n*q['length_m']/2000] for e in [-1,0,1] for n in [-1,0,1]]
points += [[-.36225,4.45],[-.3622,4.45],[-.36,4.4486],[-.36,4.44865]]
dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
proc=subprocess.run([dotnet,str(ROOT/'tools/IslandCheck/bin/Debug/net8.0/IslandCheck.dll'),'heights'],input=''.join(json.dumps(p)+'\n' for p in points),text=True,capture_output=True,check=True)
rows=[json.loads(s) for s in proc.stdout.splitlines()];assert len(rows)==len(points)
worst=max((max(abs(v-g.height(*p)) for v in row),p) for p,row in zip(points,rows))
report=dict(points=len(points),max_error_m=worst[0],worst_position_km=worst[1],passes=worst[0]<1e-7,spec_sha256=hashlib.sha256((g.ROOT/'island-spec.json').read_bytes()).hexdigest())
(g.ROOT/'island-runtime-parity.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if not report['passes']:raise SystemExit(1)
