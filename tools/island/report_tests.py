#!/usr/bin/env python3
"""Record an unchanged-source xUnit baseline/world comparison; never edits test sources."""
import argparse,json
from collections import Counter
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def read(path):return json.loads(Path(path).read_text().splitlines()[-1])
p=argparse.ArgumentParser();p.add_argument('baseline');p.add_argument('world');a=p.parse_args();base=read(a.baseline);world=read(a.world)
known={f['name'] for f in base['Failures']};new=[f for f in world['Failures'] if f['name'] not in known];repeat=[f for f in world['Failures'] if f['name'] in known]
report={'method':'xUnit own in-process framework; unchanged test sources; sequential execution, normal fixtures/data rows/skips. Frozen repaired-height world binaries/config/docs. Final cavity surface-only changes separately covered by runtime probes.','baseline':{k:base[k] for k in ['Passed','Failed','Skipped']},'world':{k:world[k] for k in ['Passed','Failed','Skipped']},'baseline_notes':'Two baseline-only CREDITS fixture failures came from the temporary baseline lacking docs; actual-repository/final fixtures include docs. Eight LAN cases cannot bind in sandbox; other baseline aircraft/mirror failures are retained.','world_failures_absent_baseline':[{'name':f['name'],'messages':f['messages']} for f in new],'failures_also_in_baseline':[{'name':f['name'],'messages':f['messages']} for f in repeat],'all_world_failures':[{'name':f['name'],'messages':f['messages']} for f in world['Failures']]}
(ROOT/'docs/pending/island-test-results.json').write_text(json.dumps(report,indent=2)+'\n')
counts=Counter(f['name'].split('(')[0].rsplit('.',1)[0] for f in new)
lines=['# Existing tests broken by the world','','No test source was edited. These are observed failures after the world change, not claims that every failure is a stale assertion. Claude owns reconciliation of tests, spawn/arena/nav/event bindings and final integration.','',f"Baseline: {base['Passed']} passed, {base['Failed']} failed, {base['Skipped']} skipped. World: {world['Passed']} passed, {world['Failed']} failed, {world['Skipped']} skipped. {len(new)} failing cases are absent from baseline; {len(repeat)} also fail in baseline.",'','The baseline has two temporary CREDITS fixture artefacts. LAN failures reflect sandbox socket restrictions. Known aircraft handling/flat-spin and GreatLakes mirror failures are not changed here.','', 'The continuity repair removes the accidental airpark/lake pad-boundary step. The legacy full sweep still reports the deliberately vertical, approved Drake north cliff; its old allow-list has no Drake location. See island-continuity-check.json and the captured continuity output.','','## New failures by class','','| Class | Cases |','| --- | --- |']
lines += [f'| {name} | {count} |' for name,count in sorted(counts.items())]
for heading,values in [('Each failure absent from baseline',new),('Failures also in baseline',repeat)]:
 lines += ['', '## '+heading,'']
 for f in values:
  lines += ['- `'+f['name']+'`: '+(' / '.join(f['messages'])).replace('\n',' ').replace('|','\\|')]
(ROOT/'docs/pending/ISLAND-WORLD-BROKEN-TESTS.md').write_text('\n'.join(lines)+'\n')
print(json.dumps({'baseline':report['baseline'],'world':report['world'],'new_world_failures':len(new),'baseline_failures_retained':len(repeat)}))
