using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05: "The panel is STILL not arranged for a centered IAC card. This is the tenth time I have asked for
/// this." The rear (PIC) panel of our own Great Lakes keeps a CENTRED clear space for a half-letter IAC card, 4.25 x 5.5 in
/// portrait, and the front panel carries a second card the front pilot can see. This reads the shipped GLB itself (node
/// bounds, every triangle) and fails if the card space is missing, too small, off centre, off the panel, or covered by
/// anything -- an instrument, a knob, a switch -- either on the panel around it or between it and the pilot's eye.
///
/// Every expected number is a literal: the card size from the IAC card (half of 8.5 x 11 in), the panel face stations and
/// eyes from the model's own record (docs/models/great-lakes-own-RIG.md: rear face glTF x 1.638, front 0.968), and the
/// eye points from configs/aircraft/great-lakes-like.json (Eye_Rear / Eye_Front, approved 2026-10-31).
/// glTF frame: +x aft, +y up, +z the pilot's LEFT.
/// </summary>
public class GreatLakesCardSpaceTests
{
    private readonly ITestOutputHelper _o;
    public GreatLakesCardSpaceTests(ITestOutputHelper o) { _o = o; }

    private const double CardW = 4.25 * 0.0254, CardH = 5.5 * 0.0254;   // 108.0 x 139.7 mm, portrait
    private const double RearFace = 1.638, FrontFace = 0.968;            // aft faces of the panels, glTF x
    private const double CentreTol = 0.005;                              // 5 mm either way of the panel's centre
    private const double Rim = 0.010;                                    // fingers + holder rim kept clear round the card
    private static readonly (double x, double y, double z) EyeRear = (1.95, 0.29, 0.0), EyeFront = (1.22, 0.27, 0.0);
    private static readonly string[] CardParts = { "SequenceCard_Face", "SequenceCard_Stock", "SequenceCard_Holder", "SequenceCard_Clip" };

    internal static string Glb => Path.Combine(OwnPittsModelTests.Models, "great-lakes-like", "great-lakes-like-own.glb");

    internal sealed record Tri(string Node, string Material, double[] A, double[] B, double[] C);

    /// <summary>Every triangle of the model in model space, with the node and material it belongs to.</summary>
    internal static List<Tri> Triangles(string path)
    {
        var (j, bin) = OwnPittsModelTests.ReadGlb(path);
        JsonElement nodes = j.GetProperty("nodes"), meshes = j.GetProperty("meshes"), mats = j.GetProperty("materials");
        int count = nodes.GetArrayLength();
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        var res = new List<Tri>();
        for (int i = 0; i < count; i++)
        {
            if (!nodes[i].TryGetProperty("mesh", out JsonElement mi)) continue;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            string name = nodes[i].GetProperty("name").GetString()!;
            foreach (JsonElement prim in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                string mat = prim.TryGetProperty("material", out JsonElement pm) ? mats[pm.GetInt32()].GetProperty("name").GetString()! : "";
                double[][] v = Accessor(j, bin, prim.GetProperty("attributes").GetProperty("POSITION").GetInt32(), 3)
                    .Select(p => new[] { m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12], m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13], m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14] }).ToArray();
                int[] idx = prim.TryGetProperty("indices", out JsonElement ii)
                    ? Accessor(j, bin, ii.GetInt32(), 1).Select(p => (int)p[0]).ToArray()
                    : Enumerable.Range(0, v.Length).ToArray();
                for (int t = 0; t + 2 < idx.Length; t += 3) res.Add(new Tri(name, mat, v[idx[t]], v[idx[t + 1]], v[idx[t + 2]]));
            }
        }
        return res;
    }

    private static double[][] Accessor(JsonElement j, byte[] bin, int index, int dim)
    {
        JsonElement a = j.GetProperty("accessors")[index];
        JsonElement bv = j.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
        int comp = a.GetProperty("componentType").GetInt32(), size = comp == 5126 || comp == 5125 ? 4 : comp == 5123 ? 2 : 1;
        int o = (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        int stride = bv.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : size * dim;
        int n = a.GetProperty("count").GetInt32();
        var res = new double[n][];
        for (int k = 0; k < n; k++)
        {
            res[k] = new double[dim];
            for (int c = 0; c < dim; c++)
            {
                int p = o + k * stride + c * size;
                res[k][c] = comp switch { 5126 => BitConverter.ToSingle(bin, p), 5125 => BitConverter.ToUInt32(bin, p), 5123 => BitConverter.ToUInt16(bin, p), _ => bin[p] };
            }
        }
        return res;
    }

    private static (double[] lo, double[] hi) Bounds(IEnumerable<Tri> tris)
    {
        var lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var hi = new[] { double.MinValue, double.MinValue, double.MinValue };
        foreach (Tri t in tris)
            foreach (double[] p in new[] { t.A, t.B, t.C })
                for (int c = 0; c < 3; c++) { lo[c] = Math.Min(lo[c], p[c]); hi[c] = Math.Max(hi[c], p[c]); }
        return (lo, hi);
    }

    /// <summary>Möller-Trumbore: the ray from o along d (unit) hits the triangle at a distance in (0, maxT).</summary>
    private static bool Hits(double[] o, double[] d, double maxT, Tri t)
    {
        double[] e1 = Sub(t.B, t.A), e2 = Sub(t.C, t.A), p = Cross(d, e2);
        double det = Dot(e1, p);
        if (Math.Abs(det) < 1e-14) return false;
        double inv = 1.0 / det;
        double[] s = Sub(o, t.A);
        double u = Dot(s, p) * inv;
        if (u < 0 || u > 1) return false;
        double[] q = Cross(s, e1);
        double v = Dot(d, q) * inv;
        if (v < 0 || u + v > 1) return false;
        double dist = Dot(e2, q) * inv;
        return dist > 1e-4 && dist < maxT;
    }
    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    /// <summary>Points on the card face that the eye cannot see: a ray from the point to the eye meets something that is
    /// not the card. 9 x 11 samples, the outermost 4 mm in from the card's edges.</summary>
    private static List<string> Hidden(List<Tri> all, (double[] lo, double[] hi) face, (double x, double y, double z) eye, ICollection<string> own)
    {
        var e = new[] { eye.x, eye.y, eye.z };
        double x = face.hi[0] + 0.0005;
        var lo = new[] { Math.Min(x, eye.x), Math.Min(face.lo[1], eye.y), Math.Min(face.lo[2], eye.z) };
        var hi = new[] { Math.Max(x, eye.x), Math.Max(face.hi[1], eye.y), Math.Max(face.hi[2], eye.z) };
        var near = all.Where(t => !own.Contains(t.Node) && new[] { t.A, t.B, t.C }.Any(_ => true)
                                  && Enumerable.Range(0, 3).All(c => Math.Max(t.A[c], Math.Max(t.B[c], t.C[c])) >= lo[c] - 0.01
                                                                   && Math.Min(t.A[c], Math.Min(t.B[c], t.C[c])) <= hi[c] + 0.01)).ToList();
        var hidden = new List<string>();
        for (int iy = 0; iy < 11; iy++)
            for (int iz = 0; iz < 9; iz++)
            {
                double y = face.lo[1] + 0.004 + (face.hi[1] - face.lo[1] - 0.008) * iy / 10.0;
                double z = face.lo[2] + 0.004 + (face.hi[2] - face.lo[2] - 0.008) * iz / 8.0;
                var o = new[] { x, y, z };
                double[] d = Sub(e, o);
                double len = Math.Sqrt(Dot(d, d));
                d = new[] { d[0] / len, d[1] / len, d[2] / len };
                Tri? hit = near.FirstOrDefault(t => Hits(o, d, len, t));
                if (hit != null) hidden.Add($"({y:F3},{z:F3}) behind {hit.Node}/{hit.Material}");
            }
        return hidden;
    }

    [Fact]
    public void The_rear_panel_keeps_a_centred_clear_half_letter_card_space()
    {
        List<Tri> all = Triangles(Glb);
        var face = all.Where(t => t.Node == "SequenceCard_Face").ToList();
        Assert.True(face.Count > 0, "no SequenceCard_Face node: the rear panel has no IAC card space");
        var (lo, hi) = Bounds(face);
        double w = hi[2] - lo[2], h = hi[1] - lo[1], zc = 0.5 * (lo[2] + hi[2]), yc = 0.5 * (lo[1] + hi[1]);
        _o.WriteLine($"rear card {w * 1000:F1} wide x {h * 1000:F1} tall mm, centre (y {yc:F3}, z {zc:F3}), face x {lo[0]:F4}..{hi[0]:F4}");

        // Big enough, the right way up.
        Assert.True(w >= CardW - 0.001 && h >= CardH - 0.001, $"card {w * 1000:F1} x {h * 1000:F1} mm is smaller than 108 x 139.7 mm");
        Assert.True(h > w, "the card is landscape: an IAC card is portrait");

        // On the rear panel, standing at most 30 mm proud of it.
        var panel = all.Where(t => t.Material == "Cockpit_panel" && Math.Abs(t.A[0] - RearFace) < 0.02).ToList();
        Assert.NotEmpty(panel);
        var (plo, phi) = Bounds(panel);
        double pc = 0.5 * (plo[2] + phi[2]);
        _o.WriteLine($"rear panel z {plo[2]:F3}..{phi[2]:F3} (centre {pc:F4}), y {plo[1]:F3}..{phi[1]:F3}");
        Assert.True(lo[0] >= RearFace - 0.001 && hi[0] <= RearFace + 0.030, $"card face x {lo[0]:F3}..{hi[0]:F3} is not on the panel face {RearFace}");
        Assert.True(lo[1] >= plo[1] && hi[1] <= phi[1], $"card y {lo[1]:F3}..{hi[1]:F3} runs off the panel (y {plo[1]:F3}..{phi[1]:F3})");

        // CENTRED: on the panel's centre and on the aeroplane's centreline.
        Assert.True(Math.Abs(zc - pc) <= CentreTol, $"card centre z {zc:F3} is {(zc - pc) * 1000:F0} mm off the panel centre {pc:F3}");
        Assert.True(Math.Abs(zc) <= CentreTol, $"card centre z {zc:F3} is off the centreline");

        // CLEAR: nothing but the card (and the panel plate behind it) inside the card's footprint grown by the rim,
        // from the panel face all the way back to the pilot.
        var own = new HashSet<string>(CardParts);
        var intruders = all.Where(t => !own.Contains(t.Node) && t.Material != "Cockpit_panel")
            .Where(t => new[] { t.A, t.B, t.C, new[] { (t.A[0] + t.B[0] + t.C[0]) / 3, (t.A[1] + t.B[1] + t.C[1]) / 3, (t.A[2] + t.B[2] + t.C[2]) / 3 } }
                .Any(p => p[0] > RearFace - 0.001 && p[0] < EyeRear.x && p[1] > lo[1] - Rim && p[1] < hi[1] + Rim && p[2] > lo[2] - Rim && p[2] < hi[2] + Rim))
            .Select(t => t.Node + "/" + t.Material).Distinct().ToList();
        foreach (string s in intruders.Take(20)) _o.WriteLine("  in the card space: " + s);
        Assert.Empty(intruders);

        // And every point of it can be seen from the rear seat's eye.
        List<string> hidden = Hidden(all, (lo, hi), EyeRear, own);
        foreach (string s in hidden.Take(20)) _o.WriteLine("  hidden from Eye_Rear: " + s);
        Assert.Empty(hidden);
    }

    [Fact]
    public void The_front_panel_carries_a_card_the_front_pilot_can_see()
    {
        List<Tri> all = Triangles(Glb);
        var face = all.Where(t => t.Node == "FrontPanel_SequenceCard_Face").ToList();
        Assert.True(face.Count > 0, "no FrontPanel_SequenceCard_Face node");
        var (lo, hi) = Bounds(face);
        double w = hi[2] - lo[2], h = hi[1] - lo[1];
        _o.WriteLine($"front card {w * 1000:F1} x {h * 1000:F1} mm, y {lo[1]:F3}..{hi[1]:F3}, z {lo[2]:F3}..{hi[2]:F3}, x {lo[0]:F4}");
        Assert.True(w >= CardW - 0.001 && h >= CardH - 0.001 && h > w, $"front card {w * 1000:F1} x {h * 1000:F1} mm is not a portrait half-letter card");
        Assert.True(Math.Abs(lo[0] - FrontFace) < 0.03, $"front card x {lo[0]:F3} is not at the front panel {FrontFace}");
        var own = new HashSet<string>(CardParts.Select(p => "FrontPanel_" + p));
        List<string> hidden = Hidden(all, (lo, hi), EyeFront, own);
        foreach (string s in hidden.Take(20)) _o.WriteLine("  hidden from Eye_Front: " + s);
        Assert.Empty(hidden);
    }

    /// <summary>The rear panel's instruments are real 3-1/8 and 2-1/4 in cases: no two dial faces may share space, and none
    /// may sit on the card.</summary>
    [Fact]
    public void Rear_instruments_do_not_share_space()
    {
        List<Tri> all = Triangles(Glb);
        var dials = all.Where(t => t.Material.StartsWith("Dial_") && Math.Abs(t.A[0] - RearFace) < 0.02)
            .GroupBy(t => t.Material).Select(g => (name: g.Key, b: Bounds(g))).ToList();
        Assert.True(dials.Count >= 6, $"only {dials.Count} rear dials");
        foreach (var d in dials) _o.WriteLine($"{d.name}: y {d.b.lo[1]:F3}..{d.b.hi[1]:F3} z {d.b.lo[2]:F3}..{d.b.hi[2]:F3}");
        for (int a = 0; a < dials.Count; a++)
            for (int b = a + 1; b < dials.Count; b++)
            {
                var (p, q) = (dials[a].b, dials[b].b);
                double ra = 0.5 * (p.hi[1] - p.lo[1]), rb = 0.5 * (q.hi[1] - q.lo[1]);
                double dy = 0.5 * (p.lo[1] + p.hi[1] - q.lo[1] - q.hi[1]), dz = 0.5 * (p.lo[2] + p.hi[2] - q.lo[2] - q.hi[2]);
                Assert.True(Math.Sqrt(dy * dy + dz * dz) > ra + rb + 0.004, $"{dials[a].name} and {dials[b].name} share space");
            }
    }
}
