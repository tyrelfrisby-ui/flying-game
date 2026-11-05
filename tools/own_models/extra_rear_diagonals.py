"""Retired pass 16: Ty 2026-11-05 superseded repositioning with complete removal.
Compatibility entry point; never generates tubes. Run after extra_cockpit_v3.py.
"""
from vr_cockpit import MODELS
from extra_remove_diagonals import update

if __name__ == '__main__':
    for p in sorted((MODELS / 'extra-300-like').glob('*own*.glb')):
        update(p)
