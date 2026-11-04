using FlyingGame.Core;
using System.Text.Json;
WorldTerrain.Active = new WorldTerrain();
var terrain = WorldTerrain.Active;
if (args.Length > 0 && args[0] == "heights")
{
    string? line;
    while ((line = Console.ReadLine()) != null)
    {
        var xy = JsonSerializer.Deserialize<double[]>(line)!;
        Console.WriteLine(JsonSerializer.Serialize(new[] { IslandTerrain.HeightAt(xy[1] * 1000, xy[0] * 1000), terrain.HeightAt(xy[1] * 1000, xy[0] * 1000) }));
    }
    return;
}
if (args.Length > 0 && args[0] == "geometry")
{
    IslandGeometry.Build();
    Console.WriteLine(JsonSerializer.Serialize(new {
        blocks = IslandGeometry.Blocks.Select(b => new { b.Id, b.Material, b.X, b.Y, b.Bottom, b.Top, b.Length, b.Width, b.Heading }),
        slopes = IslandGeometry.Slopes.Select(s => new { s.Id, x=s.Centre.X, y=s.Centre.Y, s.Length, s.Width, s.Heading, s.Elevation, s.Grade }),
        cables = IslandGeometry.Cables.Select(c => new { c.Id, ax=c.A.X, ay=c.A.Y, az=-c.A.Z, bx=c.B.X, by=c.B.Y, bz=-c.B.Z, c.Radius }),
        arches = IslandGeometry.Arches.Select(a => new { a.Ax, a.Ay, a.Bx, a.By, a.FootM, a.OpeningM, a.ThickM, a.WidthM }),
        rings = IslandGeometry.Rings.Select(r => new { r.Id, x=r.Centre.X, y=r.Centre.Y, r.Up, r.Radius })
    }));
    return;
}
if(args.Length>0&&args[0]=="continuity")
{
    int count=0,allowed=0;double worst=0;var failures=new List<object>();
    void Probe(IslandPoint p,IslandPoint outward,string source)
    {
        count++;double jump=Math.Abs(IslandTerrain.HeightKm(p+outward*.00005)-IslandTerrain.HeightKm(p-outward*.00005));
        var mouth=IslandCavities.Drake["mouth"].Point;
        if(Math.Abs(p.N-mouth.N)<.020&&Math.Abs(p.E-mouth.E)<.25){allowed++;return;}
        worst=Math.Max(worst,jump);if(jump>12)failures.Add(new{source,e=p.E,n=p.N,jump_m=jump});
    }
    foreach(var f in IslandTerrain.Fields)
    {
        double l=f.Length/2000+f.EndShoulder/1000,b=f.Width/2000+f.Shoulder/1000;
        foreach(double extra in new[]{0.0,f.Blend/1000})
        {
            int steps=Math.Max(4,(int)(f.Length/10));
            foreach(int sign in new[]{-1,1})
            {
                for(int i=0;i<=steps;i++) Probe(f.Centre+f.U*((i/(double)steps*2-1)*l)+f.V*(sign*(b+extra)),f.V*sign,f.Id+":side");
                for(int i=0;i<=40;i++) Probe(f.Centre+f.V*((i/40.0*2-1)*b)+f.U*(sign*(l+extra)),f.U*sign,f.Id+":end");
            }
        }
        foreach(int a in new[]{-1,1})foreach(int b0 in new[]{-1,1})for(int i=0;i<=90;i++)
        {
            var n=f.U*(a*Math.Cos(i*Math.PI/180))+f.V*(b0*Math.Sin(i*Math.PI/180));
            Probe(f.Centre+f.U*(a*l)+f.V*(b0*b)+n*(f.Blend/1000),n,f.Id+":rounded-blend");
        }
    }
    foreach(var q in IslandTerrain.World["height_field"]["peaks"].Items)foreach(double r in new[]{.12,.4})for(int i=0;i<360;i+=2)
    {var n=IslandPoint.Heading(i);Probe(q["centre"].Point+n*r,n,q.S("id")+":peak-release");}
    foreach(var q in IslandTerrain.World["water"].Items)foreach(double margin in new[]{0.0,10.0,40.0})for(int i=0;i<360;i+=2)
    {
        double a=i*Math.PI/180,rx=q["radii_km"][0].Number(),ry=q["radii_km"][1].Number(),rr=1+margin/1000/Math.Min(rx,ry);
        var u=IslandPoint.Heading(q.N("long_axis_true_deg",90));var v=new IslandPoint(u.N,-u.E);var n=u*(Math.Cos(a)/rx)+v*(Math.Sin(a)/ry);n=n*(1/n.Length);
        Probe(q["centre"].Point+u*(rx*rr*Math.Cos(a))+v*(ry*rr*Math.Sin(a)),n,q.S("id")+":waterbank");
    }
    Console.WriteLine(JsonSerializer.Serialize(new{status=failures.Count==0?"PASS":"FAIL",count,allowed_named_Drake_north_cliff=allowed,max_noncliff_step_in_10cm_m=worst,failures,scope="Dense authored pad/round-blend/peak-release/waterbank boundary probes; sampled, not a whole-domain interval proof."}));
    Environment.ExitCode=failures.Count>0?1:0;return;
}
int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
IslandGeometry.Build();
Check(WorldTerrain.Airports.Length == 8, "Exactly eight start airports");
Check(IslandTerrain.Fields.Length == 51, "Complete field inventory");
Check(IslandGeometry.Zones.Select(z => z.Id).Order().SequenceEqual(new[] { "CZ1", "CZ2", "CZ3", "CZ5" }), "Approved combat zones, CZ4 absent");
foreach (var f in IslandTerrain.Fields)
{
    if (f.Field == "DRAKE") continue;
    foreach (double a in new[] { -.5, 0, .5 }) foreach (double c in new[] { -.5, 0, .5 })
    {
        var p = f.Centre + f.U * (a * f.Length / 1000) + f.V * (c * f.Width / 1000);
        double expected = f.SurfaceHeight(p) - (f.Surface == "water" ? 3 : 0);   // its plane plus its natural lie (grass/turf)
        Check(Math.Abs(terrain.HeightAt(p.X, p.Y) - expected) < .05, "Pad plane " + f.Id);
    }
}
var drake = IslandCavities.Drake; var dc = drake["chamber_centre"].Point; double df = drake.N("floor_msl_m");
Check(Math.Abs(WorldTerrain.WheelGroundHeightAt(dc.X, dc.Y, df + 2) - df) < .001, "Drake floor underneath roof");
Check(terrain.HeightAt(dc.X, dc.Y) >= drake.N("ceiling_msl_m") + 60 - .01, "Drake cover");
Check(WorldSolids.Penetration(dc.X, dc.Y, df + 10) == null, "Clear Drake flight lane");
Check(WorldSolids.Penetration(dc.X, dc.Y, drake.N("ceiling_msl_m") + 2) != null, "Solid Drake ceiling");
var mouth = drake["mouth"].Point;
Check(WorldSolids.Penetration(mouth.X, mouth.Y, df + 10) == null, "Single Drake portal open");
Check(WorldSolids.Penetration(mouth.X, mouth.Y + 35, df + 10) != null, "Drake cliff beside portal solid");
Check(WorldSolids.Penetration(dc.X - 412, dc.Y, df + 10) != null, "Drake far wall sealed");
Check(WorldTerrain.SurfaceAt(dc.X,dc.Y,df+1)==WorldTerrain.Surface.Paved,"Drake interior paved surface");
var ci = IslandCavities.Pirate; var pc = ci["chamber_centre"].Point; double pf = ci.N("floor_msl_m");
Check(Math.Abs(WorldTerrain.WheelGroundHeightAt(pc.X, pc.Y, pf + 1) - pf) < .001, "Pirate floor");
Check(WorldSolids.Penetration(pc.X, pc.Y, 42) == null, "Open skylight");
Check(WorldSolids.Penetration(pc.X + 12, pc.Y, 42) != null, "Pirate roof solid beside skylight");
Check(WorldSolids.Penetration(pc.X, pc.Y, 10) == null, "Pirate flight lane unobstructed");
var pfDef=IslandTerrain.Feature("F2");var entrance=pfDef["centre"].Point+IslandPoint.Heading(pfDef.N("mouth_true_deg")+180)*.010;
Check(Math.Abs(WorldTerrain.WheelGroundHeightAt(entrance.X,entrance.Y,10)-pf)<.001,"Pirate throat floor above low coastal bed");
var landing=pc+ci["pads"][0]["offset_m"].Point*.001;
Check(WorldTerrain.SurfaceAt(landing.X,landing.Y,pf+1)==WorldTerrain.Surface.Paved,"Pirate landing pad paved surface");
foreach (var z in IslandGeometry.Zones)
{
    Check(z.Hot, "Default HOT"); var start = IslandTerrain.World["starts"].Items.First(s => s.S("id") == z.Id); var p = start["centre"].Point;
    Check(z.ContainsActive(p.X, p.Y, start.N("altitude_ft_target") * IslandTerrain.Feet), "Active start " + z.Id);
    z.Hot = false; Check(z.ChartLabel.EndsWith("COLD"), "COLD chart state"); z.Hot = true;
}
foreach (var z in IslandGeometry.Zones)
foreach (var o in z.Definition["airport_overlays"].Items)
{
    var f = IslandTerrain.Fields.First(f => f.Field == o.S("airport"));
    Check(!z.ContainsActive(f.Centre.X, f.Centre.Y, 3000 * IslandTerrain.Feet), "Traffic below MOA floor");
    Check(z.FloorAt(f.Centre) >= 5000 * IslandTerrain.Feet - .01, "5000ft overlay");
}
Check(FrankChurch.Strips.Length==9,"Nine retained Middle Fork strips; Johnson Creek is3U2");
Check(IslandSites.Starts.Length>=36,"All start geometry exposed");
Check(IslandSites.Obstructions().Count==11,"All6towers+5wires chartdata");
foreach(var q in IslandTerrain.World["mesa_pads"].Items)
{
    var c=q["centre"].Point;double elevation=q.N("elevation_ft")*IslandTerrain.Feet;
    foreach(int e in new[]{-1,0,1})foreach(int n in new[]{-1,0,1})
    {
        var p=c+new IslandPoint(e*q.N("width_m")/2000,n*q.N("length_m")/2000);
        Check(Math.Abs(terrain.HeightAt(p.X,p.Y)-elevation)<.05,"Flat mesa "+q.S("id"));
    }
}
foreach(var f in IslandTerrain.Fields.Where(f=>f.Surface!="water"&&f.Field!="DRAKE"))Check(!IslandWater.StreamAt(f.Centre),"Dry field "+f.Id);
var water = terrain.WaterSurfaceAt(-6300, -12200); Check(water == 0, "Cook Inlet box on ocean");
Check(terrain.HeightAt(22000, 0) == -30 && terrain.WaterSurfaceAt(22000, 0) == 0, "Open ocean beyond ring, no wall");
Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, fields = IslandTerrain.Fields.Length, blocks = IslandGeometry.Blocks.Count, cables = IslandGeometry.Cables.Count, arches = IslandGeometry.Arches.Count, zones = IslandGeometry.Zones.Count, spec = IslandData.SpecSha256 }));
