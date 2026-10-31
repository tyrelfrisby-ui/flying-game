namespace FlyingGame.Core;

/// <summary>Shallow streams over the approved canyon/outwash bed. Lakes and float lanes retain
/// their independently fixed MSL planes. Streams never inundate pads or cavity interiors.</summary>
public static class IslandWater
{
    private static IslandValue W => IslandTerrain.World;
    public static bool StreamAt(IslandPoint p)
    {
        bool wet = false;
        foreach (var c in W["canyons"].Items)
            if (IslandTerrain.Path(p, c["path"]).d < (c.S("id") == "C3" ? .018 : .010)) { wet = true; break; }
        if (!wet) foreach (string id in new[] { "F13", "F14", "F15" })
        {
            var c = IslandTerrain.Feature(id)["centre"].Point; var d = p - c;
            if (Math.Abs(d.E) > .35 || Math.Abs(d.N) > .11 || Math.Abs(d.N) < .030) continue;
            for (int i = 0; i < 8; i++) if (Math.Abs(d.N - ((i - 3.5) * .025 + .004 * Math.Sin(d.E * 27 + i))) < .004) { wet = true; break; }
        }
        if (!wet) return false;
        foreach (var f in IslandTerrain.Fields)
        {
            var d = p - f.Centre;
            if (Math.Abs(d.Dot(f.U)) < f.Length / 2000 + f.EndShoulder / 1000 && Math.Abs(d.Dot(f.V)) < f.Width / 2000 + f.Shoulder / 1000) return false;
        }
        return !IslandCavities.DrakeInside(p) && !IslandCavities.PirateInside(p);
    }
    public static bool GravelBar(IslandPoint p)
    {
        foreach (string id in new[] { "F13", "F14", "F15" })
        {
            var d = p - IslandTerrain.Feature(id)["centre"].Point;
            if (Math.Abs(d.E) <= .35 && Math.Abs(d.N) <= .12) return true;
        }
        return false;
    }
}
