// Float water voices, rendered off the device (lane/claude-floats, Ty 2026-11-05 "some sort of hammering noise").
// Renders TyreBrakeSynth's splash / hiss / spray at a constant level and reports the envelope's strongest modulation
// between 1 and 20 Hz: a periodic voice shows a narrow peak (depth = peak / mean envelope), water should not.
// Usage: dotnet run -- [splash] [hiss] [spray] [seconds] [out.wav]
using System;
using System.IO;
using FlyingGame.Bridge;

float splash = args.Length > 0 ? float.Parse(args[0]) : 0.8f, hiss = args.Length > 1 ? float.Parse(args[1]) : 0f;
float spray = args.Length > 2 ? float.Parse(args[2]) : 0f, secs = args.Length > 3 ? float.Parse(args[3]) : 8f;
const int fs = 48000;
var s = new TyreBrakeSynth(fs);
float[] z2 = new float[2]; int[] p2 = new int[2];
int n = (int)(secs * fs);
var y = new float[n];
for (int i = 0; i < n; i++)
{
    if (i % 512 == 0) s.Prepare(z2, z2, z2, z2, z2, z2, z2, z2, z2, z2, z2, z2, splash, hiss, spray, 0, 0, z2, p2, z2, z2);
    s.Next(out float l, out float r);
    y[i] = 0.5f * (l + r);
}
// Envelope: RMS in 10 ms windows after the first second (the 30 ms smoothing has settled).
int w = fs / 100, start = fs, m = (n - start) / w;
var env = new double[m];
for (int k = 0; k < m; k++) { double a = 0; for (int j = 0; j < w; j++) { double v = y[start + k * w + j]; a += v * v; } env[k] = Math.Sqrt(a / w); }
double mean = 0; foreach (double e in env) mean += e; mean /= m;
double best = 0, bestHz = 0;
for (double hz = 1; hz <= 20; hz += 0.1)
{
    double re = 0, im = 0;
    for (int k = 0; k < m; k++) { double ph = 2 * Math.PI * hz * k / 100.0; re += (env[k] - mean) * Math.Cos(ph); im += (env[k] - mean) * Math.Sin(ph); }
    double amp = 2 * Math.Sqrt(re * re + im * im) / m;
    if (amp > best) { best = amp; bestHz = hz; }
}
double rms = 0; for (int i = start; i < n; i++) rms += y[i] * y[i]; rms = Math.Sqrt(rms / (n - start));
Console.WriteLine($"splash {splash} hiss {hiss} spray {spray}: rms {rms:F4}, strongest envelope line {bestHz:F1} Hz, depth {best / mean:P0} of the mean envelope");
if (args.Length > 4)
{
    using var f = new BinaryWriter(File.Create(args[4]));
    f.Write("RIFF"u8); f.Write(36 + n * 2); f.Write("WAVEfmt "u8); f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(fs); f.Write(fs * 2); f.Write((short)2); f.Write((short)16);
    f.Write("data"u8); f.Write(n * 2); foreach (float v in y) f.Write((short)Math.Clamp(v * 20000, -32767, 32767));
}
