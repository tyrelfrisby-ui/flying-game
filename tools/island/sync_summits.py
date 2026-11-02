#!/usr/bin/env python3
"""Set each authored summit's top_ft in island-spec.json to the ground the relief actually stands there (to 0.1 ft),
recording any change in the spec's change ledger and refreshing the ledger hash. Run after TerrainGen generate,
then tools/island/generate_runtime.py, then TerrainGen check (the summit table feeds back into the generator,
so repeat until nothing changes). Standard library only."""
import hashlib, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]; sys.path.insert(0, str(ROOT / 'docs/pending/tools'))
import generate_island as g
p = g.ROOT / 'island-spec.json'; s = json.loads(p.read_text()); w = s['world']; changed = 0
for q in w['height_field']['peaks']:
    z = g.height(*q['centre']) / g.FT
    if abs(z - q['top_ft']) < .09: continue
    old = q['top_ft']; q['top_ft'] = round(z, 1); changed += 1
    entry = next((c for c in s['changes'] if c['item'] == f"summit {q['id']}"), None)
    if entry: entry['new'] = f"{q['top_ft']} ft"
    else: s['changes'].append(dict(item=f"summit {q['id']}", old=f"{old} ft", new=f"{q['top_ft']} ft", reason="Claude 2026-11-02 island terrain rebuild: the highest ground that can physically stand here; docs/pending/ISLAND-TERRAIN-CLAUDE.md"))
    print(q['id'], old, '->', q['top_ft'])
s['revision_audit']['final_world_sha256'] = hashlib.sha256(json.dumps(w, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
p.write_text(json.dumps(s, indent=2, ensure_ascii=False) + '\n')
print(changed, 'summit(s) changed')
