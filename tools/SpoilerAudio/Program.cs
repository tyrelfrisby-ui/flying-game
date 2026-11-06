// The spoiler voice, rendered off the device (lane/claude-sgs233-spoiler-brake, Ty 2026-11-05 16:06: "the speed break
// noise is a little too loud and it should have a bit more rumble to it on the 2-33"). Renders WindSynth twice from the
// same seeds, spoiler out and in, and takes the difference: that is the spoiler voice alone, sample for sample. Prints
// its RMS against the stock voice's (dB), how much of it is under 250 Hz, and the envelope's strongest line between 1
// and 20 Hz (a periodic voice shows a narrow, deep line; a wake should not).
// Usage: dotnet run -- [roarLevel] [rumble] [iasMs] [seconds] [out.wav]
using System;
using System.IO;
using FlyingGame.Bridge;

float roar = args.Length > 0 ? float.Parse(args[0]) : 1f, rumble = args.Length > 1 ? float.Parse(args[1]) : 0f;
float ias = args.Length > 2 ? float.Parse(args[2]) : 25f, secs = args.Length > 3 ? float.Parse(args[3]) : 8f;
const int fs = 48000, block = 512;

float[] Voice(float level, float rum)
{
    int n = (int)(secs * fs);
    var on = new WindSynth(fs); var off = new WindSynth(fs);
    on.SetSpoilerVoice(level, rum); off.SetSpoilerVoice(level, rum);
    var y = new float[n];
    for (int i = 0; i < n; i++)
    {
        if (i % block == 0) { on.Prepare(ias, 2f, 0f, 0f, 0f, 1f, false); off.Prepare(ias, 2f, 0f, 0f, 0f, 0f, false); }
        on.Next(out float l1, out _); off.Next(out float l0, out _);
        y[i] = l1 - l0;
    }
    return y;
}
double Rms(float[] y, int from) { double a = 0; for (int i = from; i < y.Length; i++) a += y[i] * y[i]; return Math.Sqrt(a / (y.Length - from)); }
double LowShare(float[] y, int from)
{
    // A fourth-order 250 Hz low-pass (two RBJ sections), the share of the voice's power below it.
    var a = new Biquad(); var b = new Biquad(); a.LowPass(fs, 250f, 0.7071f); b.LowPass(fs, 250f, 0.7071f);
    double lo = 0, all = 0;
    for (int i = 0; i < y.Length; i++) { float v = b.Process(a.Process(y[i])); if (i >= from) { lo += v * v; all += y[i] * y[i]; } }
    return lo / all;
}
(double hz, double depth) EnvelopeLine(float[] y, int from)
{
    int w = fs / 100, m = (y.Length - from) / w;
    var env = new double[m];
    for (int k = 0; k < m; k++) { double a = 0; for (int j = 0; j < w; j++) { double v = y[from + k * w + j]; a += v * v; } env[k] = Math.Sqrt(a / w); }
    double mean = 0; foreach (double e in env) mean += e; mean /= m;
    double best = 0, bestHz = 0;
    for (double f = 1; f <= 20; f += 0.1)
    {
        double re = 0, im = 0;
        for (int k = 0; k < m; k++) { double ph = 2 * Math.PI * f * k / 100.0; re += (env[k] - mean) * Math.Cos(ph); im += (env[k] - mean) * Math.Sin(ph); }
        double amp = 2 * Math.Sqrt(re * re + im * im) / m;
        if (amp > best) { best = amp; bestHz = f; }
    }
    return (bestHz, best / mean);
}

int start = fs;   // after the 30 ms smoothers and the random walk have settled
float[] stock = Voice(1f, 0f), mine = Voice(roar, rumble), rumbleOnly = Voice(0f, rumble);
double rs = Rms(stock, start), rm = Rms(mine, start), rr = Rms(rumbleOnly, start);
(double lineHz, double lineDepth) = EnvelopeLine(mine, start);
(double stockHz, double stockDepth) = EnvelopeLine(stock, start);
Console.WriteLine($"IAS {ias} m/s, spoiler full. stock voice: rms {rs:F4}, {LowShare(stock, start):P0} under 250 Hz, envelope line {stockHz:F1} Hz depth {stockDepth:P0}");
Console.WriteLine($"roar {roar} rumble {rumble}: rms {rm:F4} = {20 * Math.Log10(rm / rs):+0.0;-0.0} dB vs stock, {LowShare(mine, start):P0} under 250 Hz, envelope line {lineHz:F1} Hz depth {lineDepth:P0}; rumble alone {rr / rs:F2} of stock rms");
if (args.Length > 4)
{
    int n = mine.Length;
    using var f = new BinaryWriter(File.Create(args[4]));
    f.Write("RIFF"u8); f.Write(36 + n * 2); f.Write("WAVEfmt "u8); f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(fs); f.Write(fs * 2); f.Write((short)2); f.Write((short)16);
    f.Write("data"u8); f.Write(n * 2); foreach (float v in mine) f.Write((short)Math.Clamp(v * 20000, -32767, 32767));
}
