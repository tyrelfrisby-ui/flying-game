"""Called under deploy-instrument-baseline.sh reservation; never overwrite working assets.

Clone current project, sources and Library using APFS copy-on-write; substitute only
13 audited baseline blobs in that isolated copy. Retain clone/logs as evidence.
"""
import hashlib
import json
import os
import subprocess
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
evidence = Path.home() / 'Developer/agent-lanes/renders/t932-instrument-fleet-flicker'
manifest = json.loads((evidence / 'baseline-assets-manifest.json').read_text())
assert len(manifest['assets']) == 13
for asset in manifest['assets']:
    assert 'cub' not in asset['path']
    assert hashlib.sha256((root / asset['path']).read_bytes()).hexdigest() == asset['currentSha256']
    assert hashlib.sha256((evidence / 'baseline-assets' / asset['path']).read_bytes()).hexdigest() == asset['baselineSha256']
# Engine-band migration followed the initial shared-only capture. Include its original
# live PNGs and Great Lakes model in future matched baseline runs as well.
engine_paths = ['unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb']
engine_paths += ['unity/Assets/_Project/Resources/CockpitDials/ea-dial-' + key + '.png' for key in ('tach', 'mp', 'oil')]
for path in engine_paths:
    original = subprocess.check_output(['git', 'show', '31ca88b2:' + path], cwd=root)
    saved = evidence / 'baseline-assets' / path
    saved.parent.mkdir(parents=True, exist_ok=True)
    saved.write_bytes(original)
    manifest['assets'].append(dict(path=path, baselineSha256=hashlib.sha256(original).hexdigest(),
                                  currentSha256=hashlib.sha256((root / path).read_bytes()).hexdigest()))
clone = Path(tempfile.mkdtemp(prefix='t932-baseline-', dir='/private/tmp'))
for name in ('unity', 'src', 'configs'):
    subprocess.run(['cp', '-cR', str(root / name), str(clone / name)], check=True)
# cp preserves the Library symlink; replace only the clone's link with a private COW cache.
library = clone / 'unity/Library'
if library.is_symlink():
    target = (root / 'unity/Library').resolve()
    library.unlink()
    subprocess.run(['cp', '-cR', str(target), str(library)], check=True)
# Unity's project lock is ephemeral. Remove it only from the newly-created isolated copy.
lock = clone / 'unity/Temp/UnityLockfile'
if lock.exists():
    lock.unlink()
for asset in manifest['assets']:
    (clone / asset['path']).write_bytes((evidence / 'baseline-assets' / asset['path']).read_bytes())
env = os.environ.copy()
env.update(COCKPIT_OUT=str(evidence / 'before-cockpit'), COCKPIT_TEMPORAL='0',
           COCKPIT_SHOTS='pitts-phone|pitts-s2b-like|rear|0|-15|72|sun||2160x1000;pitts-closeup|pitts-s2b-like|rear|0|-20|40|sun||2160x1000;great-lakes-phone|great-lakes-like|rear|0|-15|72|sun||2160x1000;great-lakes-closeup|great-lakes-like|rear|0|-20|40|sun||2160x1000;great-lakes-panel-phone|great-lakes-like|rear|0|-30|72|sun||2160x1000;great-lakes-panel-closeup|great-lakes-like|rear|0|-40|40|sun||2160x1000')
run = dict(clone=str(clone), sourceRoot=str(root), baselineRef=manifest['baselineRef'],
           assets=manifest['assets'], scope='Current source/config/camera; only thirteen baseline PNG/GLB blobs substituted in isolated project. No working assets overwritten.')
(evidence / 'baseline-capture-run.json').write_text(json.dumps(run, indent=2) + '\n')
result = subprocess.run(['/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity',
                         '-batchmode', '-quit', '-projectPath', str(clone / 'unity'), '-buildTarget', 'iOS',
                         '-executeMethod', 'FlyingGame.EditorTools.CockpitEyeShots.Run',
                         '-logFile', str(evidence / 'baseline-shots.log')], env=env)
run['unityExit'] = result.returncode
run['workingAssetHashesUnchanged'] = all(hashlib.sha256((root / a['path']).read_bytes()).hexdigest() == a['currentSha256'] for a in manifest['assets'])
(evidence / 'baseline-capture-run.json').write_text(json.dumps(run, indent=2) + '\n')
assert run['workingAssetHashesUnchanged']
raise SystemExit(result.returncode)
