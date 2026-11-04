using System.IO.Compression;
namespace IslandTerrainGen;

/// <summary>Minimal RGB PNG writer (review images only; standard library zlib).</summary>
public static class Png
{
    public static void Write(string path, int w, int h, Func<int, int, (byte r, byte g, byte b)> pixel)
    {
        var raw = new byte[(w * 3 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            int o = y * (w * 3 + 1); raw[o++] = 0;
            for (int x = 0; x < w; x++) { var (r, g, b) = pixel(x, y); raw[o++] = r; raw[o++] = g; raw[o++] = b; }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13]; BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", z.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }
    static void BE(byte[] a, int o, uint v) { a[o] = (byte)(v >> 24); a[o + 1] = (byte)(v >> 16); a[o + 2] = (byte)(v >> 8); a[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var td = new byte[4 + data.Length]; for (int i = 0; i < 4; i++) td[i] = (byte)type[i]; data.CopyTo(td, 4);
        s.Write(td); var crc = new byte[4]; BE(crc, 0, Crc(td)); s.Write(crc);
    }
    static uint[]? table;
    static uint Crc(byte[] d)
    {
        if (table == null) { table = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; } }
        uint crc = 0xFFFFFFFF; foreach (var b in d) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8); return crc ^ 0xFFFFFFFF;
    }
    /// <summary>Hypsometric tint + NW hillshade, the same ramp for every review image.</summary>
    public static (byte, byte, byte) Shade(double h, double dhdx, double dhdy, double cell)
    {
        if (h <= 0) return ((byte)(20), (byte)(50 + Math.Max(-30, h) * .5), (byte)(90 + Math.Max(-30, h)));
        // dhdx east, dhdy north (unitless). Sun from NW, 45 deg.
        double nx = -dhdx, ny = -dhdy, nz = 1, l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        double lx = -.5, ly = .5, lz = .7071;
        double shade = Math.Clamp((nx * lx + ny * ly + nz * lz) / l, 0, 1);
        (double r, double g, double b)[] ramp = { (70, 130, 70), (110, 150, 80), (190, 175, 120), (150, 120, 100), (230, 230, 230) };
        double[] at = { 0, 600, 1300, 2000, 2700 };
        int i = 0; while (i < at.Length - 2 && h > at[i + 1]) i++;
        double t = Math.Clamp((h - at[i]) / (at[i + 1] - at[i]), 0, 1);
        var c = (ramp[i].r + (ramp[i + 1].r - ramp[i].r) * t, ramp[i].g + (ramp[i + 1].g - ramp[i].g) * t, ramp[i].b + (ramp[i + 1].b - ramp[i].b) * t);
        double k = .35 + .9 * shade;
        return ((byte)Math.Clamp(c.Item1 * k, 0, 255), (byte)Math.Clamp(c.Item2 * k, 0, 255), (byte)Math.Clamp(c.Item3 * k, 0, 255));
    }
}
