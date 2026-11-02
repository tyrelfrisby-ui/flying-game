"""Measurements of delivered PCM16, no normalisation. Requires numpy + matplotlib."""
import os
os.environ.setdefault('MPLCONFIGDIR', '/tmp/crash-audio-matplotlib')
import sys
import wave
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

root = Path(sys.argv[1])
def read(name):
    with wave.open(str(root / name)) as wav:
        assert wav.getframerate() == 48000 and wav.getnchannels() == 1
        return np.frombuffer(wav.readframes(wav.getnframes()), dtype='<i2').astype(float) / 32768

def metrics(x):
    power = abs(np.fft.rfft(x)) ** 2
    hz = np.fft.rfftfreq(len(x), 1 / 48000)
    cumulative = np.cumsum(x * x)
    end = int(np.searchsorted(cumulative, cumulative[-1] * .999)) + 1
    crest = 20 * np.log10(np.max(abs(x)) / np.sqrt(np.mean(x[:end] ** 2)))
    return (np.sum(hz * power) / sum(power), 100 * sum(power[hz < 150]) / sum(power), crest, end / 48000)

lines = ['| Severity | Version | Power centroid Hz | Energy <150 Hz % | Crest dB | T99.9 seconds |', '|---|---|---:|---:|---:|---:|']
for severity in ['0.3', '0.7', '1.0']:
    results = {}
    for version in ['OLD', 'NEW']:
        results[version] = metrics(read(f'crash-land-{version}-{severity}.wav'))
        lines.append(f'| {severity} | {version} | ' + ' | '.join(f'{v:.2f}' for v in results[version]) + ' |')
    assert results['NEW'][1] < results['OLD'][1], 'Land low band fraction must decrease'
body_lines = []
for severity in ['0.3', '0.7', '1.0']:
    bands = []
    for version in ['OLD', 'NEW']:
        x = read(f'crash-land-{version}-{severity}.wav')
        power = abs(np.fft.rfft(x)) ** 2
        hz = np.fft.rfftfreq(len(x), 1 / 48000)
        bands.append(100 * sum(power[(hz >= 200) & (hz < 450)]) / sum(power))
    assert bands[1] < bands[0]
    body_lines.append(f'Severity {severity}: 200–450 Hz body band {bands[0]:.2f}% OLD → {bands[1]:.2f}% NEW.')
print('\n'.join(lines + body_lines))
(root / 'measurements.md').write_text('\n'.join(lines + [''] + body_lines) + '\n\nCentroid weighted by FFT power; low band uses the complete 10 s PCM. T99.9 is time containing 99.9% cumulative signal energy; crest = peak/RMS through T99.9. All files have identical fixed gain, without individual normalisation.\n')

def spectrogram(ax, name, seconds=2):
    x = read(name)[:int(48000 * seconds)]
    n = 1024
    frames = np.lib.stride_tricks.sliding_window_view(x, n)[::256]
    spec = abs(np.fft.rfft(frames * np.hanning(n), axis=1)) / (n / 2)
    db = 20 * np.log10(np.maximum(spec, 1e-7))
    im = ax.imshow(db.T, origin='lower', aspect='auto', extent=[0, len(x)/48000, 0, 24000], cmap='magma', vmin=-95, vmax=-20)
    ax.set_ylim(30, 16000); ax.set_yscale('log'); ax.set_ylabel('Hz'); ax.set_xlabel('seconds'); ax.set_title(name)
    return im
fig, axes = plt.subplots(3, 2, figsize=(13, 10), constrained_layout=True)
for row, severity in enumerate(['0.3', '0.7', '1.0']):
    for col, version in enumerate(['OLD', 'NEW']):
        im = spectrogram(axes[row,col], f'crash-land-{version}-{severity}.wav')
fig.colorbar(im, ax=axes, label='dBFS, common scale')
fig.savefig(root / 'spectrogram-land-old-vs-new.png', dpi=150); plt.close(fig)
fig, axes = plt.subplots(3, 1, figsize=(11, 10), constrained_layout=True)
for ax, size in zip(axes, ['small-piece', 'engine', 'intact-ditch']):
    im = spectrogram(ax, f'splash-water-{size}.wav', 4)
fig.colorbar(im, ax=axes, label='dBFS, common scale')
fig.savefig(root / 'spectrogram-splash-water.png', dpi=150); plt.close(fig)
print('PASS all three NEW land low-band and body-band fractions < OLD; spectrograms written')
