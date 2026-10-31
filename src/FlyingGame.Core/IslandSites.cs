namespace FlyingGame.Core;

/// <summary>World-owned placement/metadata for Claude's spawn, chart and ATIS integration.</summary>
public static class IslandSites
{
    public readonly struct Start
    {
        public readonly string Id, Mode, Airport, Group;
        public readonly double X, Y, AltitudeM, HeadingTrueDeg;
        public Start(IslandValue d)
        {
            Id=d.S("id");Mode=d.S("mode");Airport=d.S("airport");Group=d.S("group");var p=d["centre"].Point;X=p.X;Y=p.Y;
            HeadingTrueDeg=d.N("heading_true_deg");
            AltitudeM=d.Has("altitude_ft_target")?d.N("altitude_ft_target")*IslandTerrain.Feet:IslandTerrain.HeightAt(X,Y)+d.N("altitude_agl_ft_target")*IslandTerrain.Feet;
        }
    }
    public static Start[] Starts => IslandTerrain.World["starts"].Items.Select(d=>new Start(d)).ToArray();
    public static IslandField[] Fields => IslandTerrain.Fields;
    public static IslandValue[] Boxes => IslandTerrain.World["aerobatic_boxes"].Items;
    public static IslandValue[] NavaidSitesOnly => IslandTerrain.World["outer_navaid_sites"].Items;
    public static List<Nav.ChartFeature> Obstructions()
    {
        var result=new List<Nav.ChartFeature>();
        foreach(var q in IslandTerrain.World["hazards"]["towers"].Items)
        {
            var p=q["centre"].Point;double ground=IslandTerrain.HeightKm(p),agl=q.N("height_agl_m");
            result.Add(new Nav.ChartFeature(Nav.ChartFeatureKind.Obstruction,q.S("id")+(q["guyed"].Flag()?" GUYED":""),p.X,p.Y,agl,ground+agl));
        }
        foreach(var q in IslandTerrain.World["hazards"]["wires"].Items)
        {
            var a=q["endpoints"][0].Point;var b=q["endpoints"][1].Point;var c=(a+b)*.5;
            double top=IslandGeometry.Cables.Where(s=>s.Id==q.S("id")).Select(s=>Math.Max(-s.A.Z,-s.B.Z)).DefaultIfEmpty(IslandTerrain.HeightKm(c)+q.N("lowest_agl_m")).Max();
            result.Add(new Nav.ChartFeature(Nav.ChartFeatureKind.Obstruction,q.S("id")+" WIRE",c.X,c.Y,q.N("lowest_agl_m"),top,Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)));
        }
        return result;
    }
}
