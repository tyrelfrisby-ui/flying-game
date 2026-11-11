using System.Globalization;
using System.Text;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Opt-in (WHEEL_SKI_SHAPE=1): dumps one leg's hinged wheel-ski as numbers, so the shape can be LOOKED AT without
/// Unity. Ty asked on 2026-11-10 for the ski's front to curve instead of breaking at an angle, and the renders that
/// answer that come from <c>FlyingGame.EditorTools.WheelSkiShots</c> — a Unity batch run, which this agent does not
/// hold the slot for. This writes the same geometry <see cref="SkiGear.For"/> gives the physics and the drawing:
/// the ski's stations along its bottom, its plan width at each, the links, the slot and the tyre. A preview can then
/// be built from it by anything that can draw a box (tools/own_models runs Blender headless for exactly this kind of
/// thing). It is a preview, not a substitute: the Unity shots are what Ty signs off, and the numbers here are the
/// only thing the two have in common, which is the point — if they disagree, this file is right and the preview is
/// wrong, because this one is read straight out of the physics.
///
/// Output: build/wheel-ski/shape-&lt;id&gt;.json, one object per fitted leg.
/// </summary>
public class WheelSkiShapeProbe
{
    private readonly ITestOutputHelper _out;
    public WheelSkiShapeProbe(ITestOutputHelper o) { _out = o; }

    private static string N(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("pa18-cub-like")]
    [InlineData("j3-cub-like")]
    [InlineData("c172-like")]
    public void DumpTheSkiShape(string id)
    {
        if (Environment.GetEnvironmentVariable("WHEEL_SKI_SHAPE") != "1") return;
        AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "TestData", id + ".json"));
        cfg.Skis = true;
        var sb = new StringBuilder();
        sb.Append("{\n  \"id\": \"").Append(id).Append("\",\n  \"legs\": [\n");
        bool first = true;
        for (int i = 0; i < cfg.Gear.Count; i++)
        {
            GearConfig g = cfg.Gear[i];
            if (!SkiGear.Fitted(cfg, g)) continue;
            SkiGear.Geometry geo = SkiGear.For(cfg, g);
            if (!first) sb.Append(",\n");
            first = false;
            sb.Append("    {\n");
            sb.Append($"      \"leg\": {i}, \"tail\": {(g.IsTailwheel ? "true" : "false")},\n");
            sb.Append($"      \"lengthM\": {N(geo.LengthM)}, \"widthM\": {N(geo.WidthM)}, \"thicknessM\": {N(geo.ThicknessM)},\n");
            sb.Append($"      \"noseM\": {N(geo.FlatFwdM)}, \"heelM\": {N(geo.FlatAftM)},\n");
            sb.Append($"      \"noseSweepM\": {N(geo.NoseSweepM)}, \"tailSweepM\": {N(geo.TailSweepM)},\n");
            sb.Append($"      \"noseRiseM\": {N(geo.NoseRiseM)}, \"tailRiseM\": {N(geo.TailRiseM)},\n");
            sb.Append($"      \"slotFwdM\": {N(geo.SlotFwdM)}, \"slotAftM\": {N(geo.SlotAftM)}, \"slotWidthM\": {N(geo.SlotWidthM)},\n");
            sb.Append($"      \"tyreRadiusM\": {N(geo.TyreRadiusM)}, \"linkM\": {N(geo.LinkM)},\n");
            sb.Append($"      \"massKg\": {N(geo.MassKg)}, \"dragAreaM2\": {N(geo.DragAreaM2)},\n");
            // The bottom, walked end to end: 81 stations of (x forward of the pin, rise of the bottom above the flat).
            sb.Append("      \"bottom\": [");
            const int n = 80;
            for (int k = 0; k <= n; k++)
            {
                double x = geo.FlatAftM + (geo.FlatFwdM - geo.FlatAftM) * k / n;
                sb.Append(k == 0 ? "" : ", ").Append('[').Append(N(x)).Append(", ").Append(N(geo.BottomRiseAt(x))).Append(']');
            }
            sb.Append("],\n");
            // Where the ski bears when it is held nose-down against the ground, every 2° to its own nose slope: the
            // contact rolling forward along the sweep is the only place the shape reaches the forces (SkiGear.Probe).
            sb.Append("      \"contactVsNoseDown\": [");
            for (int d = 0; d <= 20; d += 2)
            {
                (double X, double Z) c = geo.NoseBottomAtSlope(d * Math.PI / 180.0);
                sb.Append(d == 0 ? "" : ", ").Append('[').Append(d).Append(", ").Append(N(c.X)).Append(", ").Append(N(geo.ThicknessM - c.Z)).Append(']');
            }
            sb.Append("]\n    }");
            _out.WriteLine($"{id} leg {i}{(g.IsTailwheel ? " (tail)" : "")}: {geo.LengthM:F3} x {geo.WidthM:F3} m, " +
                           $"nose {geo.NoseRiseM:F3} m up over {geo.FlatFwdM - geo.NoseSweepM:F3} m, tail {geo.TailRiseM:F3} m up, " +
                           $"{geo.MassKg:F1} kg, {geo.DragAreaM2:F4} m2");
        }
        sb.Append("\n  ]\n}\n");
        string dir = Path.Combine(FindRepoRoot(), "build", "wheel-ski");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"shape-{id}.json");
        File.WriteAllText(path, sb.ToString());
        _out.WriteLine("-> " + path);
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }
}
