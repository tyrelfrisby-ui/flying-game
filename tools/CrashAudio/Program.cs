using System;
using System.IO;
using FlyingGame.Bridge;

static class Program
{
    const int Fs = 48000;
    static void Require(bool pass, string label) { if (!pass) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static float[] Render(Func<int, float> sample, int seconds = 10)
    {
        var data = new float[Fs * seconds];
        for (int i = 0; i < data.Length; i++) { data[i] = sample(i); if (!float.IsFinite(data[i])) throw new Exception("nonfinite sample"); }
        return data;
    }
    static void Wav(string path, float[] data)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + data.Length * 2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(Fs); writer.Write(Fs * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(data.Length * 2);
        foreach (float value in data) writer.Write((short)Math.Round(Math.Clamp(value, -1f, 1f) * 32767));
    }
    static void Main(string[] args)
    {
        string output = args[0];
        foreach (float severity in new[] { .3f, .7f, 1f })
        {
            var old = new OldAudio.FxVoice(Fs, 0x51A7Eu); old.Start(OldAudio.FxKind.Crash, 0, severity, 1);
            // Common fixed playback gain, no per-file normalisation; matches FxMix*pool headroom calibration.
            Wav(Path.Combine(output, $"crash-land-OLD-{severity:0.0}.wav"), Render(_ => old.Active ? OldAudio.Dsp.SoftClip(old.Next() * .52f) : 0));
            var current = new FxVoice(Fs, 0x51A7Eu); current.Start(FxKind.Crash, 0, severity, 1);
            Wav(Path.Combine(output, $"crash-land-NEW-{severity:0.0}.wav"), Render(_ => current.Active ? Dsp.SoftClip(current.Next() * .52f) : 0));
        }
        foreach (var scenario in new[] { ("small-piece", 8f, 7f), ("engine", 220f, 18f), ("intact-ditch", 1100f, 5f) })
        {
            var voice = new CrashSound(Fs, 0x51A7Eu); voice.Start(true, .7f, scenario.Item2, scenario.Item3);
            Wav(Path.Combine(output, $"splash-water-{scenario.Item1}.wav"), Render(_ => Dsp.SoftClip(voice.Next() * .52f)));
        }
        foreach (var scenario in new[] { ("fast", .012f, .08f), ("slow", .0006f, .008f) })
        {
            var voice = new FloodSound(Fs, 0x192Bu);
            Wav(Path.Combine(output, $"gurgle-flooding-{scenario.Item1}.wav"), Render(i => {
                if (i % 800 == 0) voice.Set(1, scenario.Item2, Math.Max(0, scenario.Item3 - scenario.Item2 * i / Fs), 1f + i / (float)Fs * .1f, 1f);
                return voice.Next() * .8f;
            }));
        }
        var legacy = new FxVoice(Fs, 77); legacy.Start(FxKind.Crash, 0, .7f, 1);
        var shared = new CrashSound(Fs, 77); shared.Start(false, .7f, 800, 10);
        Require(Render(_ => legacy.Active ? legacy.Next() : 0).AsSpan().SequenceEqual(Render(_ => shared.Next())),
            "legacy Crash API and shared land DSP match sample-for-sample");
        var silent = new FloodSound(Fs, 123);
        silent.Set(1, 0, 1, 0, 1);
        Require(Array.TrueForAll(Render(_ => silent.Next()), x => x == 0), "zero flow is exactly silent");
        silent.Set(1, .01f, 0, 0, 1);
        Require(Array.TrueForAll(Render(_ => silent.Next()), x => x == 0), "zero trapped air is exactly silent");
        var expired = new FloodSound(Fs, 123); expired.Set(1, .01f, 1, 0, 1);
        var stale = Render(_ => expired.Next());
        Require(Array.TrueForAll(stale[(Fs * 2)..], x => Math.Abs(x) < 1e-8), "missing flood updates expire");
        Require(Math.Abs(CrashScale.BubbleHz(.01f, 0) - 328.33f) < 1f, "10mm Minnaert bubble ~328Hz");
        float slowRadius = CrashScale.BubbleRadius(.0006f), fastRadius = CrashScale.BubbleRadius(.012f);
        float slowRate = .0006f / (256f * 4f * (float)Math.PI / 3f * slowRadius * slowRadius * slowRadius);
        float fastRate = .012f / (256f * 4f * (float)Math.PI / 3f * fastRadius * fastRadius * fastRadius);
        Require(fastRadius > slowRadius && fastRate > slowRate && fastRate < 80f, "increasing flow increases both bubble radius and packet rate before cap");
        var land = new CrashSound(Fs, 12); var water = new CrashSound(Fs, 12);
        land.Start(false, .7f, 800, 10); water.Start(true, .7f, 800, 10);
        var l = Render(_ => land.Next()); var w = Render(_ => water.Next());
        Require(!l.AsSpan().SequenceEqual(w), "surface selects different synthesis");
        var mixer = new CrashSoundMixer(Fs);
        for (int i = 0; i < 500; i++) { mixer.Impact(i % 2 == 0, 1, 1000, 30, 1, 0); mixer.Flood(i, .01f, 1, 0, 1); }
        mixer.Prepare(); var overload = Render(_ => mixer.Next());
        Require(Array.TrueForAll(overload, x => Math.Abs(x) <= 1), "bounded overload remains finite and limited");
        var delayed = new CrashSoundMixer(Fs); delayed.Impact(true, 1, 1000, 10, 1, 1); delayed.Prepare();
        var d = Render(_ => delayed.Next());
        Require(Array.TrueForAll(d[..(Fs - 10)], x => x == 0), "impact travel delay remains silent before arrival");
        var alloc = new CrashSoundMixer(Fs); alloc.Impact(true, 1, 800, 10, 1, 0); alloc.Flood(2, .01f, 1, 0, 1); alloc.Prepare();
        for (int i = 0; i < 10000; i++) alloc.Next();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Fs; i++) alloc.Next();
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "zero allocations during audio samples");
        Console.WriteLine("Rendered actual production C# DSP verbatim (no port or Unity stub needed). All WAVs: 48kHz mono, 10 seconds, PCM16.");
    }
}
