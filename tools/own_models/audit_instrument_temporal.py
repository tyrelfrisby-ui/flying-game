"""Measure CockpitEyeShots fixed-input bench sequences; never infer device stability.

Usage: python3 tools/own_models/audit_instrument_temporal.py <after-directory>
Writes temporal-audit.json beside the sequences. Requires Pillow and numpy.
"""
import csv
import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def audit(directory):
    with (directory / 'frames.tsv').open() as stream:
        frames = list(csv.DictReader(stream, delimiter='\t'))
    transforms = {}
    with (directory / 'transforms.tsv').open() as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            key = row.pop('frame')
            transforms.setdefault(key, []).append(row)
    reference = None
    previous = None
    results = []
    camera_fields = [k for k in frames[0] if k not in ('frame', 'wallSeconds')]
    for frame in frames:
        number = frame['frame']
        path = directory / f'{int(number):04d}.png'
        with Image.open(path) as image:
            pixels = np.array(image.convert('RGB'), dtype=np.int16)
        if reference is None:
            reference = pixels
            previous = pixels
        delta = np.abs(pixels - reference)
        step = np.abs(pixels - previous)
        results.append(dict(frame=int(number), wallSeconds=float(frame['wallSeconds']),
                            pngSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                            changedPixelsFromFirst=int(np.count_nonzero(np.any(delta, axis=2))),
                            changedPixelsFromPrevious=int(np.count_nonzero(np.any(step, axis=2))),
                            maxChannelDelta=int(delta.max()),
                            cameraMatchesFirst=all(frame[k] == frames[0][k] for k in camera_fields),
                            allLocalTransformsMatchFirst=transforms[number] == transforms[frames[0]['frame']]))
        previous = pixels
    assert len(results) == 101, f'{directory}: incomplete sequence'
    assert results[-1]['wallSeconds'] - results[0]['wallSeconds'] >= 9.9, 'sequence too short'
    return dict(directory=str(directory), scope=(directory / 'SCOPE.txt').read_text().strip(),
                frameCount=len(results), durationSeconds=results[-1]['wallSeconds'] - results[0]['wallSeconds'],
                allCameraSamplesIdentical=all(r['cameraMatchesFirst'] for r in results),
                allLocalTransformSamplesIdentical=all(r['allLocalTransformsMatchFirst'] for r in results),
                allPixelsIdentical=all(r['changedPixelsFromFirst'] == 0 for r in results), frames=results)


def main():
    root = Path(sys.argv[1])
    sequences = sorted(root.glob('*-fixed-input'))
    if not sequences:
        raise SystemExit('No temporal sequences found')
    report = dict(scope='Manual edit-mode bench evidence only; live flight and physical-device flicker remain untested.',
                  sequences=[audit(directory) for directory in sequences])
    target = root / 'temporal-audit.json'
    target.write_text(json.dumps(report, indent=2) + '\n')
    for sequence in report['sequences']:
        print(sequence['directory'], 'frames', sequence['frameCount'], 'identical pixels', sequence['allPixelsIdentical'],
              'identical transforms', sequence['allLocalTransformSamplesIdentical'])


if __name__ == '__main__':
    main()
