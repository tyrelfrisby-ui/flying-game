using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty 2026-11-05 ADDENDUM: "EXTRA: the front-seat view shows a GAP between the canopy and the airplane. Close it: the canopy
/// must seal to the fuselage/sill everywhere, verified from the front-seat eye point." (tools/own_models/extra_cockpit_v3.py;
/// the same two measurements in check_extra_canopy_seal.py.)
///
/// What it was: not a hole. The glass rim touched the sill everywhere, but the skin, sill and frame are double-sided with
/// the EXTERIOR livery, the dark liner stopped 6-13 cm below the sill, and the wing's skin ran straight through the front
/// cockpit, so the pilot saw white and red paint between the cockpit and the canopy. So this checks both:
///   RIM  every point of the canopy glass's free edge is within 3 mm of the sill, the frame or the skin;
///   VIEW from each seat's eye, no ray reaches exterior livery above the cockpit floor without passing through glass
///        first (through the canopy that paint is the outside of the aeroplane; without glass it is the inside of it).
/// Eyes (builder metres, x fwd, y left, z up) copied by hand from configs/aircraft/extra-300-like.json as placed
/// (check_eye_interiors.py): rear (-1.622, 0, 0.526), front (-0.703, 0, 0.537).
/// </summary>
public class ExtraCanopySealTests
{
    private readonly ITestOutputHelper _o;
    public ExtraCanopySealTests(ITestOutputHelper o) => _o = o;

    private static readonly string[] Liveries = { "extra-300-like-own", "extra-300-like-own-n300uy", "extra-300-like-own-unmarked" };
    private static readonly (string seat, P eye)[] Eyes = { ("rear", new P(-1.622, 0, 0.526)), ("front", new P(-0.703, 0, 0.537)) };
    private const double RimMaxM = 0.003, FloorZ = -0.15, StepDeg = 3.0;

    public static IEnumerable<object[]> Files() => Liveries.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Files))]
    public void The_canopy_glass_rim_sits_on_the_sill_everywhere(string file)
    {
        var tris = Load(file);
        var glass = tris.Where(t => t.node == "Canopy_Glass").ToList();
        var host = tris.Where(t => t.node is "Canopy_Sill" or "Canopy_Frame" or "Fuselage_Painted").ToList();
        Assert.True(glass.Count > 100 && host.Count > 1000, $"{file}: glass {glass.Count}, host {host.Count}");
        // The glass's free edge: edges used by one triangle only (vertices welded at 0.01 mm).
        string K(P p) => $"{Math.Round(p.x, 5)},{Math.Round(p.y, 5)},{Math.Round(p.z, 5)}";
        var use = new Dictionary<(string, string), (P, P, int)>();
        foreach (var t in glass)
            foreach (var (a, b) in new[] { (t.a, t.b), (t.b, t.c), (t.c, t.a) })
            {
                string ka = K(a), kb = K(b); var key = string.CompareOrdinal(ka, kb) < 0 ? (ka, kb) : (kb, ka);
                use[key] = use.TryGetValue(key, out var e) ? (e.Item1, e.Item2, e.Item3 + 1) : (a, b, 1);
            }
        var rim = use.Values.Where(e => e.Item3 == 1).SelectMany(e => new[] { 0.0, 0.5 }.Select(s => Lerp(e.Item1, e.Item2, s))).ToList();
        Assert.True(rim.Count > 100, $"{file}: only {rim.Count} rim samples");
        double worst = 0; P at = default;
        foreach (P p in rim)
        {
            double d = host.Where(t => Near(t, p, 0.05)).Select(t => PointTri(p, t)).DefaultIfEmpty(1.0).Min();
            if (d > worst) { worst = d; at = p; }
        }
        _o.WriteLine($"{file}: {rim.Count} rim samples, worst gap {worst * 1000:F1} mm at ({at.x:F3},{at.y:F3},{at.z:F3})");
        Assert.True(worst < RimMaxM, $"{file}: the canopy glass stands {worst * 1000:F1} mm off the sill at ({at.x:F3},{at.y:F3},{at.z:F3})");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void No_exterior_paint_is_seen_inside_either_cockpit(string file)
    {
        var tris = Load(file);
        foreach (var (seat, eye) in Eyes)
        {
            var near = tris.Where(t => Dist(t.a, eye) < 2.6 || Dist(t.b, eye) < 2.6 || Dist(t.c, eye) < 2.6).ToArray();
            var leaks = new List<string>();
            for (double el = -60; el <= 20; el += StepDeg)
                for (double az = -180; az < 180; az += StepDeg)
                {
                    double a = az * Math.PI / 180, b = el * Math.PI / 180;
                    var d = new P(Math.Cos(b) * Math.Cos(a), Math.Cos(b) * Math.Sin(a), Math.Sin(b));
                    double tSolid = double.MaxValue, tGlass = double.MaxValue; Tri hit = default;
                    foreach (var t in near)
                    {
                        double h = Ray(eye, d, t);
                        if (h <= 0) continue;
                        if (t.glass) tGlass = Math.Min(tGlass, h);
                        else if (h < tSolid) { tSolid = h; hit = t; }
                    }
                    if (tSolid == double.MaxValue || !hit.livery || tGlass < tSolid) continue;
                    var p = new P(eye.x + tSolid * d.x, eye.y + tSolid * d.y, eye.z + tSolid * d.z);
                    if (p.z > FloorZ) leaks.Add($"el {el:+0;-0} az {az:+0;-0}: {hit.node} / {hit.mat} at ({p.x:F3},{p.y:F3},{p.z:F3})");
                }
            _o.WriteLine($"{file} {seat}: {leaks.Count} rays reach exterior paint without glass");
            foreach (string l in leaks.Take(10)) _o.WriteLine("  " + l);
            Assert.True(leaks.Count == 0, $"{file} {seat} eye sees exterior paint inside the cockpit: {string.Join("; ", leaks.Take(5))}");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------

    private readonly record struct P(double x, double y, double z);
    private readonly record struct Tri(P a, P b, P c, string node, string mat, bool glass, bool livery);

    private static P Lerp(P a, P b, double s) => new(a.x + s * (b.x - a.x), a.y + s * (b.y - a.y), a.z + s * (b.z - a.z));
    private static P Sub(P a, P b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    private static double Dot(P a, P b) => a.x * b.x + a.y * b.y + a.z * b.z;
    private static P Cross(P a, P b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    private static double Dist(P a, P b) => Math.Sqrt(Dot(Sub(a, b), Sub(a, b)));

    private static bool Near(Tri t, P p, double r) =>
        Math.Min(t.a.x, Math.Min(t.b.x, t.c.x)) - r < p.x && Math.Max(t.a.x, Math.Max(t.b.x, t.c.x)) + r > p.x &&
        Math.Min(t.a.y, Math.Min(t.b.y, t.c.y)) - r < p.y && Math.Max(t.a.y, Math.Max(t.b.y, t.c.y)) + r > p.y &&
        Math.Min(t.a.z, Math.Min(t.b.z, t.c.z)) - r < p.z && Math.Max(t.a.z, Math.Max(t.b.z, t.c.z)) + r > p.z;

    /// <summary>Möller-Trumbore: the distance along d to t, or -1.</summary>
    private static double Ray(P o, P d, Tri t)
    {
        P e1 = Sub(t.b, t.a), e2 = Sub(t.c, t.a), h = Cross(d, e2);
        double det = Dot(e1, h);
        if (Math.Abs(det) < 1e-12) return -1;
        double inv = 1 / det; P s = Sub(o, t.a);
        double u = Dot(s, h) * inv; if (u < 0 || u > 1) return -1;
        P q = Cross(s, e1); double v = Dot(d, q) * inv; if (v < 0 || u + v > 1) return -1;
        double r = Dot(e2, q) * inv; return r > 1e-6 ? r : -1;
    }

    /// <summary>Point to triangle distance (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private static double PointTri(P p, Tri t)
    {
        P a = t.a, b = t.b, c = t.c, ab = Sub(b, a), ac = Sub(c, a), ap = Sub(p, a);
        double d1 = Dot(ab, ap), d2 = Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return Dist(p, a);
        P bp = Sub(p, b); double d3 = Dot(ab, bp), d4 = Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return Dist(p, b);
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return Dist(p, Lerp(a, b, d1 / (d1 - d3)));
        P cp = Sub(p, c); double d5 = Dot(ab, cp), d6 = Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return Dist(p, c);
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return Dist(p, Lerp(a, c, d2 / (d2 - d6)));
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return Dist(p, Lerp(b, c, (d4 - d3) / ((d4 - d3) + (d5 - d6))));
        double den = 1 / (va + vb + vc), v = vb * den, w = vc * den;
        return Dist(p, new P(a.x + ab.x * v + ac.x * w, a.y + ab.y * v + ac.y * w, a.z + ab.z * v + ac.z * w));
    }

    /// <summary>Every triangle near the cockpits, builder metres, with its node and material.</summary>
    private static List<Tri> Load(string file)
    {
        var (j, bin) = OwnPittsModelTests.ReadGlb(Path.Combine(OwnPittsModelTests.Models, "extra-300-like", file + ".glb"));
        JsonElement nodes = j.GetProperty("nodes"), meshes = j.GetProperty("meshes"), mats = j.GetProperty("materials");
        JsonElement acc = j.GetProperty("accessors"), views = j.GetProperty("bufferViews");
        int count = nodes.GetArrayLength(); var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = -1;
        for (int i = 0; i < count; i++)
            if (nodes[i].TryGetProperty("children", out JsonElement kids))
                foreach (JsonElement k in kids.EnumerateArray()) parent[k.GetInt32()] = i;
        var res = new List<Tri>();
        for (int i = 0; i < count; i++)
        {
            string name = nodes[i].GetProperty("name").GetString()!;
            if (!nodes[i].TryGetProperty("mesh", out JsonElement mi) || name.StartsWith("Legacy") || name.StartsWith("Sight")) continue;
            double[] m = OwnPittsModelTests.World(nodes, parent, i);
            bool glassNode = name is "Canopy_Glass" or "Window_Glass" or "Floor_Window_Glass";
            foreach (JsonElement prim in meshes[mi.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                string mat = prim.TryGetProperty("material", out JsonElement mm) ? mats[mm.GetInt32()].GetProperty("name").GetString()! : "";
                bool livery = mat.StartsWith("Paint_ea") || mat.StartsWith("Paint_white");
                var pos = Read(acc, views, bin, prim.GetProperty("attributes").GetProperty("POSITION").GetInt32())
                    .Select(v => new P(-(m[0] * v[0] + m[4] * v[1] + m[8] * v[2] + m[12]), m[2] * v[0] + m[6] * v[1] + m[10] * v[2] + m[14],
                                       m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13])).ToArray();
                int[] ids = prim.TryGetProperty("indices", out JsonElement ix)
                    ? Read(acc, views, bin, ix.GetInt32()).Select(v => (int)v[0]).ToArray() : Enumerable.Range(0, pos.Length).ToArray();
                for (int k = 0; k + 2 < ids.Length; k += 3)
                {
                    P a = pos[ids[k]], b = pos[ids[k + 1]], c = pos[ids[k + 2]];
                    double cx = (a.x + b.x + c.x) / 3, cy = (a.y + b.y + c.y) / 3, cz = (a.z + b.z + c.z) / 3;
                    if (Math.Abs(cy) > 0.9 || cx < -2.6 || cx > 1.2 || cz < -0.8) continue;
                    res.Add(new Tri(a, b, c, name, mat, glassNode, livery));
                }
            }
        }
        return res;
    }

    private static List<double[]> Read(JsonElement acc, JsonElement views, byte[] bin, int index)
    {
        JsonElement a = acc[index], bv = views[a.GetProperty("bufferView").GetInt32()];
        int comp = a.GetProperty("componentType").GetInt32();
        int dim = a.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, _ => 4 };
        int size = comp switch { 5126 or 5125 => 4, 5123 => 2, _ => 1 };
        int o = (bv.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        int stride = bv.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : dim * size;
        var res = new List<double[]>(a.GetProperty("count").GetInt32());
        for (int k = 0; k < a.GetProperty("count").GetInt32(); k++)
        {
            var v = new double[dim];
            for (int d = 0; d < dim; d++)
            {
                int at = o + k * stride + d * size;
                v[d] = comp switch { 5126 => BitConverter.ToSingle(bin, at), 5125 => BitConverter.ToUInt32(bin, at), 5123 => BitConverter.ToUInt16(bin, at), _ => bin[at] };
            }
            res.Add(v);
        }
        return res;
    }
}
