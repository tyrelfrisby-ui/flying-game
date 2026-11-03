using FlyingGame.Core.MathTypes;
namespace FlyingGame.Core;

/// <summary>Shared visible/contact geometry, in sim metres. Each rendered obstacle has the same solid.
/// Event, scoring, AI, runway selection and weather remain outside this world-owned layer.</summary>
public static class IslandGeometry
{
    public sealed class Block : WorldSolids.IShape, WorldDecks.IDeck, WorldSolids.IBounded
    {
        public readonly string Id, Material;
        public readonly double X, Y, Bottom, Top, Length, Width, Heading;
        public readonly bool Landable;
        public Block(string id, string material, double x, double y, double bottom, double top, double length, double width, double heading = 0, bool landable = false)
        { Id = id; Material = material; X = x; Y = y; Bottom = bottom; Top = top; Length = length; Width = width; Heading = heading; Landable = landable; }
        private (double a, double c) Local(double x, double y)
        {
            double h = Heading * Math.PI / 180, dx = x - X, dy = y - Y;
            return (dx * Math.Cos(h) + dy * Math.Sin(h), -dx * Math.Sin(h) + dy * Math.Cos(h));
        }
        public bool TopAt(double x, double y, out double topM) { var (a, c) = Local(x, y); topM = Top; return Math.Abs(a) <= Length / 2 && Math.Abs(c) <= Width / 2; }
        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            normalNed = Vec3.Zero; depth = 0; var (a, c) = Local(x, y);
            if (Math.Abs(a) > Length / 2 || Math.Abs(c) > Width / 2 || up < Bottom || up > Top) return false;
            double da = Length / 2 - Math.Abs(a), dc = Width / 2 - Math.Abs(c), db = up - Bottom, dt = Top - up;
            depth = Math.Min(Math.Min(da, dc), Math.Min(db, dt)); double h = Heading * Math.PI / 180;
            normalNed = depth == dt ? new Vec3(0, 0, -1) : depth == db ? new Vec3(0, 0, 1) : depth == da ? new Vec3(Math.Sign(a) * Math.Cos(h), Math.Sign(a) * Math.Sin(h), 0) : new Vec3(-Math.Sign(c) * Math.Sin(h), Math.Sign(c) * Math.Cos(h), 0);
            return true;
        }
        public void Bounds(out double minX, out double minY, out double minUp, out double maxX, out double maxY, out double maxUp)
        {
            double h = Heading * Math.PI / 180, ex = Math.Abs(Math.Cos(h)) * Length / 2 + Math.Abs(Math.Sin(h)) * Width / 2, ey = Math.Abs(Math.Sin(h)) * Length / 2 + Math.Abs(Math.Cos(h)) * Width / 2;
            minX = X - ex - 0.01; maxX = X + ex + 0.01; minY = Y - ey - 0.01; maxY = Y + ey + 0.01; minUp = Bottom; maxUp = Top;
        }
    }
    public sealed class Slope : WorldDecks.IDeck, WorldSolids.IShape
    {
        public readonly string Id; public readonly IslandPoint Centre, U, V;
        public readonly double Length, Width, Heading, Elevation, Grade;
        public Slope(string id, IslandPoint c, double length, double width, double heading, double elevation, double grade)
        { Id=id; Centre=c; Length=length; Width=width; Heading=heading; Elevation=elevation; Grade=grade; U=IslandPoint.Heading(heading); V=new IslandPoint(U.N,-U.E); }
        public bool TopAt(double x,double y,out double topM)
        {
            var d=new IslandPoint(y/1000,x/1000)-Centre; double along=d.Dot(U)*1000;
            topM=Elevation+along*Grade; return Math.Abs(along)<=Length/2 && Math.Abs(d.Dot(V)*1000)<=Width/2;
        }
        public bool Penetrate(double x,double y,double up,out Vec3 normalNed,out double depth)
        {
            normalNed=new Vec3(-Grade*U.N,-Grade*U.E,-1).Normalized(); depth=0;
            if (!TopAt(x,y,out double top)||up>top||up<Elevation-Math.Abs(Grade)*Length/2) return false;
            depth=(top-up)/Math.Sqrt(1+Grade*Grade); return true;
        }
    }
    public sealed class Cable : WorldSolids.IShape, WorldSolids.IBounded
    {
        public readonly string Id, Material; public readonly Vec3 A, B; public readonly double Radius;
        public Cable(string id, string material, Vec3 a, Vec3 b, double radius) { Id = id; Material = material; A = a; B = b; Radius = radius; }
        public bool Penetrate(double x, double y, double up, out Vec3 normalNed, out double depth)
        {
            var p = new Vec3(x, y, -up); var ab = B - A; double t = Math.Clamp(Vec3.Dot(p - A, ab) / Math.Max(1e-12, Vec3.Dot(ab, ab)), 0, 1); var delta = p - (A + ab * t); double d = delta.Length;
            depth = Radius - d; normalNed = d > 1e-9 ? delta / d : new Vec3(0, 0, -1); return depth >= 0;
        }
        public void Bounds(out double minX, out double minY, out double minUp, out double maxX, out double maxY, out double maxUp)
        {
            minX = Math.Min(A.X, B.X) - Radius; maxX = Math.Max(A.X, B.X) + Radius; minY = Math.Min(A.Y, B.Y) - Radius; maxY = Math.Max(A.Y, B.Y) + Radius;
            minUp = Math.Min(-A.Z, -B.Z) - Radius; maxUp = Math.Max(-A.Z, -B.Z) + Radius;
        }
    }
    public readonly struct Ring
    {
        public readonly string Id; public readonly IslandPoint Centre; public readonly double Up, Radius, Heading; public readonly bool Spinning;
        public Ring(string id, IslandPoint c, double up, double radius, double heading, bool spinning = false) { Id = id; Centre = c; Up = up; Radius = radius; Heading = heading; Spinning = spinning; }
    }
    public static readonly List<Slope> Slopes = new();
    public static readonly List<Block> Blocks = new();
    public static readonly List<Cable> Cables = new();
    public static readonly List<NaturalArch> Arches = new();
    public static readonly List<Ring> Rings = new();
    public static readonly List<IslandCombatZone> Zones = new();
    public static readonly List<(string id, IslandPoint p, double up)> ObstructionLights = new();
    public static readonly List<(string id, IslandPoint p, double up)> MarkerBalls = new();
    private static IslandValue W => IslandTerrain.World;
    private static uint Hash(int i, int seed) { uint x = unchecked((uint)(i * 73856093) ^ (uint)seed); x ^= x >> 13; x *= 1274126177; return x ^ (x >> 16); }
    public static double Random(int i, string seed) => (Hash(i, (int)W["seeds"].N(seed)) & 0xffffff) / 16777215.0;
    private static double Ground(IslandPoint p) => IslandTerrain.HeightKm(p);
    public static void Add(string id, string mat, IslandPoint p, double bottom, double top, double length, double width, double heading = 0, bool deck = false)
    {
        if (id.StartsWith("CITY:"))
        {
            top = Math.Min(top, ClearanceCap(p, Math.Sqrt(length * length + width * width) / 2000));
            if (bottom > top - .5) bottom = top - Math.Min(8, top - Ground(p));
        }
        Blocks.Add(new Block(id, mat, p.X, p.Y, bottom, top, length, width, heading, deck));
    }
    public static double ClearanceCap(IslandPoint p, double radiusKm)
    {
        double cap = double.PositiveInfinity;
        foreach (var e in W["clearance_budgets"].Items)
        {
            if (e.S("kind") == "airway")
            {
                if (IslandTerrain.Segment(p, e["a"].Point, e["b"].Point).d <= 3.704 + radiusKm) cap = Math.Min(cap, e.N("cap_m") + 25 * IslandTerrain.Feet - 3);
                continue;
            }
            var u = e["u"].Point; var d = p - e["a"].Point; double along = -d.Dot(u), cross = Math.Abs(d.E * u.N - d.N * u.E);
            if (along + radiusKm < 0 || along - radiusKm > e.N("length") || cross - radiusKm > .15 + .05 * Math.Max(0, along + radiusKm)) continue;
            cap = Math.Min(cap, e.N("elevation_m") + Math.Max(0, along - radiusKm) * 1000 / e.N("ratio") - 3);
        }
        return cap;
    }
    public static void CableBetween(string id, string mat, IslandPoint a, double za, IslandPoint b, double zb, double radius)
        => Cables.Add(new Cable(id, mat, new Vec3(a.X, a.Y, -za), new Vec3(b.X, b.Y, -zb), radius));
    public static void Build()
    {
        Slopes.Clear(); Blocks.Clear(); Cables.Clear(); Arches.Clear(); Rings.Clear(); Zones.Clear(); ObstructionLights.Clear(); MarkerBalls.Clear();
        WorldSolids.Boxes.Clear(); WorldSolids.Shapes.Clear(); WorldDecks.All.Clear();
        BuildAirparks(); BuildFloatBases(); BuildHazards(); BuildBoxes(); BuildCombat(); BuildCavities(); BuildFeatures();
        foreach (var b in Blocks) { WorldSolids.Shapes.Add(b); if (b.Landable) WorldDecks.All.Add(b); }
        foreach (var slope in Slopes) { WorldDecks.All.Add(slope); WorldSolids.Shapes.Add(slope); }
        foreach (var c in Cables) WorldSolids.Shapes.Add(c);
        foreach (var a in Arches) WorldSolids.Shapes.Add(new CanyonLake.ArchSolid(a));
        foreach (var r in Rings) WorldSolids.Shapes.Add(new FlyCity.TorusSolid(r.Centre.X, r.Centre.Y, r.Up, r.Radius, 1.4, r.Spinning ? (double?)null : r.Heading * Math.PI / 180));
        WorldSolids.Shapes.Add(new IslandCavities.Roof()); Carrier.Register(); Forest.RegisterSolids(); WorldSolids.Version++;
    }
    private static void BuildAirparks()
    {
        foreach (var f in IslandTerrain.Fields)
        {
            if (f.Surface == "water" || f.Field == "DRAKE" || f.Field.StartsWith("B")) continue;
            var cfg = f.Definition["buildings"]; int hangars = (int)cfg.N("hangars", 1), houses = (int)cfg.N("houses", 0), parked = (int)cfg.N("parked_aircraft", 2);
            double setback = cfg.N("setback_m", f.Width / 2 + 85);
            for (int i = 0; i < hangars + houses; i++)
            {
                bool hangar = i < hangars; var p = f.Centre + f.U * ((i - (hangars + houses - 1) / 2.0) * .055) + f.V * (setback / 1000);
                double z = Ground(p); string id = f.Id + (hangar ? ":hangar:" : ":house:") + i;
                if (hangar)
                {
                    // Three walls and a roof: open front faces runway; no invisible box across the door.
                    Add(id + ":rear", "hangar", p + f.V * .012, z, z + 9, 30, 1, f.Heading);
                    foreach (int sign in new[] { -1, 1 }) Add(id + ":side", "hangar", p + f.U * (.015 * sign), z, z + 9, 1, 24, f.Heading);
                    Add(id + ":roof", "roof", p, z + 9, z + 10, 32, 26, f.Heading);
                }
                else Add(id, "house", p, z, z + 6 + Random(i, "buildings") * 2, 16, 12, f.Heading);
            }
            for (int i = 0; i < parked; i++)
            {
                var p = f.Centre + f.U * ((i - 1) * .027 + (Random(i + (int)f.Centre.X, "parked_aircraft") - .5) * .008) - f.V * ((f.Width / 2 + 30) / 1000); double z = f.SurfaceHeight(p) + 1;
                Add(f.Id + ":parked:" + i + ":body", "aircraft", p, z, z + 1.2, 7, .9, f.Heading + 12);
                Add(f.Id + ":parked:" + i + ":wing", "aircraft", p, z + 1.1, z + 1.3, 1.6, 10, f.Heading + 12);
                Add(f.Id + ":parked:" + i + ":tail", "aircraft", p - f.U * .0025, z + .7, z + .9, .8, 3.6, f.Heading + 12);
            }
            var sock = f.Centre + f.V * ((f.Width / 2 + 55) / 1000); double zg = f.SurfaceHeight(sock);
            Add(f.Id + ":windsock-pole", "white", sock, zg, zg + 6, .16, .16);
            // Fixed visual reference only. Claude owns wind animation.
            CableBetween(f.Id + ":windsock", "orange", sock, zg + 6, sock + IslandPoint.Heading(240) * .002, zg + 5.7, .35);
        }
    }
    private static void BuildFloatBases()
    {
        foreach (var lake in W["water"].Items.Where(q=>q.S("id")=="Lake Hood"||q.S("id")=="Lake Powell"))
        {
            var c=lake["centre"].Point; var u=IslandPoint.Heading(lake.N("long_axis_true_deg",90));var v=new IslandPoint(u.N,-u.E);
            double r=lake["radii_km"][1].Number(); var shore=c-v*r; var dry=shore-v*.060;
            double water=lake.N("elevation_ft")*IslandTerrain.Feet, top=Ground(dry)+.05;
            // Ramp60m from 1.5m below water to actual dry-bank height.
            var centre=shore-v*.015; double length=90, bottom=water-1.5;
            double heading=Math.Atan2(-v.E,-v.N)*180/Math.PI;
            Slopes.Add(new Slope(lake.S("id")+":seaplane-ramp",centre,length,24,heading,(top+bottom)/2,(top-bottom)/length));
            var dock=c-v*(r-.035); Add(lake.S("id")+":base-dock","wood",dock,water-.3,water+.5,60,5,heading+90,true);
            if(lake.S("id")=="Lake Hood")
            {
                var hp=dry-v*.035;double z=Ground(hp);
                Add("PALH:base-hangar-rear","hangar",hp-v*.015,z,z+11,35,1,heading+90);
                foreach(int sign in new[]{-1,1}) Add("PALH:base-hangar-side","hangar",hp+u*(sign*.018),z,z+11,1,30,heading+90);
                Add("PALH:base-hangar-roof","roof",hp,z+11,z+12,38,32,heading+90);
            }
        }
    }
    private static void BuildHazards()
    {
        foreach (var q in W["hazards"]["towers"].Items)
        {
            var c = q["centre"].Point; double z = Ground(c), height = q.N("height_agl_m"); string id = q.S("id");
            Add(id, "tower", c, z, z + height, 2, 2); ObstructionLights.Add((id, c, z + height));
            if (q["guyed"].Flag()) for (int i = 0; i < 3; i++)
            {
                var anchor = c + IslandPoint.Heading(i * 120) * ((q.N("horizontal_envelope_radius_m") - 2) / 1000);
                CableBetween(id + ":guy", "wire", c, z + height * .85, anchor, Ground(anchor), .06);
            }
        }
        foreach (var q in W["hazards"]["wires"].Items)
        {
            var a = q["endpoints"][0].Point; var b = q["endpoints"][1].Point; double span = (b - a).Length * 1000, sag = span * q.N("sag_fraction");
            double minBase = double.NegativeInfinity;
            // Terrain-following bound guarantees stated lowest AGL, not just at endpoints.
            for (int i = 0; i <= 100; i++) { double t = i / 100.0; minBase = Math.Max(minBase, Ground(a + (b - a) * t) + q.N("lowest_agl_m") + sag * 4 * t * (1 - t)); }
            for (int i = 0; i <= 2; i++)
            {
                var p = i == 0 ? a : b; if (i == 2) break;
                Add(q.S("id") + ":pole", "tower", p, Ground(p), minBase + 2, 2, 2); ObstructionLights.Add((q.S("id"), p, minBase + 2));
            }
            for (int i = 0; i < 40; i++)
            {
                double t0 = i / 40.0, t1 = (i + 1) / 40.0; var p0 = a + (b - a) * t0; var p1 = a + (b - a) * t1;
                CableBetween(q.S("id"), "wire", p0, minBase - sag * 4 * t0 * (1 - t0), p1, minBase - sag * 4 * t1 * (1 - t1), .08);
                if (i % 5 == 0) MarkerBalls.Add((q.S("id"), p0, minBase - sag * 4 * t0 * (1 - t0)));
            }
        }
    }
    public static void Marker(string id, string shape, IslandPoint p, double z, double eastSign = 1, double northSign = 1, bool raft = false)
    {
        double arm = shape == "cross" ? 40 : 30;
        bool sideEastWest = shape == "T" && eastSign != 0 && northSign == 0;
        double ex = shape == "cross" || shape == "T" && !sideEastWest ? 0 : eastSign * arm / 2000;
        double ny = shape == "cross" || sideEastWest ? 0 : northSign * arm / 2000;
        Add(id + ":EW", "white", p + new IslandPoint(ex, 0), z - (raft ? .5 : .03), z + .03, shape == "cross" ? 80 : arm, 4, 90, raft);
        Add(id + ":NS", "white", p + new IslandPoint(0, ny), z - (raft ? .5 : .03), z + .03, shape == "cross" ? 80 : arm, 4, 0, raft);
        if (raft) CableBetween(id + ":mooring", "wire", p, z - .5, p, -24, .04);
    }
    private static void BuildBoxes()
    {
        foreach (var b in W["aerobatic_boxes"].Items)
        {
            var c = b["centre"].Point;
            if (b.Has("rafts"))
            {
                foreach (var r in b["rafts"].Items) { var p = r["centre"].Point; Marker(r.S("id"), r.S("shape"), p, r.N("deck_msl_m"), Math.Sign(c.E - p.E), Math.Sign(c.N - p.N), true); }
                var j = b["judge_raft"]; Add(j.S("id"), "white", j["centre"].Point, 0, .5, j["size_m"][0].Number(), j["size_m"][1].Number(), 90, true);
                CableBetween(j.S("id") + ":mooring", "wire", j["centre"].Point, 0, j["centre"].Point, -24, .04);
                foreach (var q in b["corner_buoys"].Items) Add(b.S("id") + ":buoy", "orange", q["centre"].Point, -.5, 1.5, 2, 2);
                var shore = b["shore_reference_marker"]["centre"].Point; Marker(b.S("id") + ":judge-shore", "cross", shore, Ground(shore) + .05);
            }
            else
            {
                double z = b.N("centre_ground_ft") * IslandTerrain.Feet + .05; Marker(b.S("id") + ":cross", "cross", c, z);
                foreach (int e in new[] { -1, 1 }) foreach (int n in new[] { -1, 1 }) Marker(b.S("id") + ":L", "L", c + new IslandPoint(e * .5, n * .5), z, -e, -n);
                foreach (var d in new[] { new IslandPoint(.5, 0), new IslandPoint(-.5, 0), new IslandPoint(0, .5), new IslandPoint(0, -.5) }) Marker(b.S("id") + ":T", "T", c + d, z, -Math.Sign(d.E), -Math.Sign(d.N));
                var j = b["judges_line"]; var p = j["centre"].Point; if (p.Length < .01) p = c + (b.S("judges_side")=="east" ? new IslandPoint(.65,0) : b.S("judges_side")=="north" ? new IslandPoint(0,.65) : b.S("judges_side")=="south" ? new IslandPoint(0,-.65) : new IslandPoint(-.65,0));
                Add(b.S("id") + ":judges-line", "white", p, Ground(p) + .02, Ground(p) + .06, 1000, 4, j.N("orientation_true_deg"));
            }
        }
    }
    private static void BuildCombat()
    {
        foreach (var q in W["combat_zones"].Items)
        {
            Zones.Add(new IslandCombatZone(q, W["zone_geometry"][q.S("id")]));
            foreach (var f in q["combat_features"].Items)
            {
                var c = f["centre"].Point;
                if (f.S("type") == "bridge")
                {
                    double bottom = f.N("under_base_msl_ft") * IslandTerrain.Feet + f.N("under_clearance_m"), span = f.N("span_m"), head = f.N("heading_true_deg"); var u = IslandPoint.Heading(head);
                    Add(f.S("id"), "bridge", c, bottom, bottom + f.N("deck_thickness_m"), span, f.N("deck_width_m"), head, true);
                    foreach (int s in new[] { -1, 1 }) { var p = c + u * ((span / 2 - 8) / 1000); Add(f.S("id") + ":pier", "rock", p, Ground(p), bottom, 10, f.N("deck_width_m"), head); }
                }
                if (f.S("type") == "rock arch") Arch(c, f.N("opening_width_m"), f.N("opening_height_m"), f.N("base_msl_ft") * IslandTerrain.Feet, f.N("axis_true_deg"), 12);
            }
        }
    }
    public static void Arch(IslandPoint c, double width, double opening, double foot, double flightHeading, double thickness = 18)
    {
        var u = IslandPoint.Heading(flightHeading + 90); var a = c - u * (width / 2000); var b = c + u * (width / 2000);
        Arches.Add(new NaturalArch(a.X, a.Y, b.X, b.Y, foot, opening, thickness, 20));
    }
    private static void BuildCavities()
    {
        var d = IslandCavities.Drake; var c = d["chamber_centre"].Point; double floor = d.N("floor_msl_m"), ceiling = d.N("ceiling_msl_m");
        Add("Drake:floor", "floor", c, floor - 2, floor, 820, 420, 0, true);
        foreach (int s in new[] { -1, 1 }) Add("Drake:side-wall", "rock", c + new IslandPoint(s * .2125, 0), floor, ceiling + 60, 830, 5);
        Add("Drake:closed-south-wall", "rock", c - new IslandPoint(0, .4125), floor, ceiling + 60, 5, 430);
        var mouth = d["mouth"].Point;
        foreach (int s in new[] { -1, 1 }) Add("Drake:north-cliff", "rock", mouth + new IslandPoint(s * .12, .0025), floor, floor + 200, 5, 180);
        Add("Drake:portal-lintel", "rock", mouth, floor + 26, floor + 200, 5, 60);
        // Interior labs, west apron and east glider slope/pillowpool; no enrolment trigger.
        Add("Drake:west-apron", "floor", c + new IslandPoint(-.14, 0), floor, floor + .02, 240, 90, 0, true);
        for (int i = 0; i < 6; i++) Add("Drake:glass-lab", "glass", c + new IslandPoint(-.185, -.1 + i * .04), floor, floor + 7, 35, 1, 0);
        Add("Drake:pillow-pool", "pool", c + new IslandPoint(.15, -.20), floor, floor + 1.5, 70, 45);
        Slopes.Add(new Slope("Drake:glider-slope", c + new IslandPoint(.15, .025), 260, 80, 0, floor + 35, 70.0 / 260));
        var f = IslandTerrain.Feature("F2"); var ci = f["interior"]; var cc = ci["chamber_centre"].Point; double cf = ci.N("floor_msl_m");
        foreach (var pad in ci["pads"].Items) Add("Pirate:" + pad.S("id"), "floor", cc + pad["offset_m"].Point * .001, cf - .3, cf, pad["size_m"][1].Number(), pad["size_m"][0].Number(), 0, true);
        // Shell walls follow chamber circle; omit any segment inside the entry throat.
        for (int i = 0; i < 96; i++)
        {
            double a = (i + .5) * 360 / 96; var p = cc + IslandPoint.Heading(a) * .092;
            var inward = IslandPoint.Heading(f.N("mouth_true_deg") + 180); var dp = p - f["centre"].Point;
            if (dp.Dot(inward) < .2 && Math.Abs(dp.E * inward.N - dp.N * inward.E) < .03) continue;
            Add("Pirate:wall", "rock", p, cf, Math.Max(60, Ground(p)), 6.3, 4, a + 90);
        }
        var m = f["centre"].Point; var u = IslandPoint.Heading(f.N("mouth_true_deg") + 180); var v = new IslandPoint(u.N, -u.E);
        foreach (int side in new[] { -1, 1 }) Add("Pirate:throat-wall", "rock", m + u * .10 + v * (side * .032), cf, 35, 200, 4, f.N("mouth_true_deg"));
        var wreck = cc + ci["contents_bounds"][0]["offset_m"].Point * .001;
        Add("Pirate:wreck-hull", "wood", wreck, cf, 7, 14, 5, 45); CableBetween("Pirate:broken-mast", "wood", wreck, 7, wreck + new IslandPoint(.004, 0), 11, .25);
        for (int i = 0; i < 12; i++) { var p = cc + new IslandPoint(.060 + Random(i, "driftwood") * .012, (Random(i + 32, "driftwood") - .5) * .012); Add("Pirate:driftwood", "wood", p, cf, cf + .4, 3.5, .35, Random(i + 72, "driftwood") * 360); }
    }
    private static void BuildFeatures()
    {
        var city = IslandTerrain.Feature("CITY"); var cc = city["centre"].Point;
        for (int i = 0; i < 25; i++)
        {
            var p = cc + new IslandPoint(((i % 5) - 2) * .18, ((i / 5) - 2) * .46); double z = Ground(p), height = 18 + Random(i, "buildings") * 210;
            if (i == 12) height = 300;
            height = Math.Min(height, ClearanceCap(p, .05) - z);
            if (i == 12) // Hole tower: two columns, cap above the pass-through.
            {
                foreach (int s in new[] { -1, 1 }) Add("CITY:hole-tower-leg", "city", p + new IslandPoint(s * .035, 0), z, z + height, 70, 20, 0, true);
                Add("CITY:hole-tower-cap", "city", p, z + 170, z + height, 70, 50, 0, true);
            }
            else Add("CITY:tower:" + i, "city", p, z, z + height, 70, 55, 0, true);
            if (z + height + 32 + 23.4 < ClearanceCap(p, .034)) Rings.Add(new Ring("CITY:roof-ring:" + i, p, z + height + 32, 22, 0));
            if (i % 5 == 1)
            {
                var right = p + new IslandPoint(.18, 0); double rz = Ground(right); double deck = Math.Max(z, rz) + 70;
                Add("CITY:skybridge", "glass", p + new IslandPoint(.09, 0), deck, deck + 8, 140, 10, 90, true);
            }
        }
        var disc = cc + new IslandPoint(.38, -.75); double dg = Ground(disc);
        Add("CITY:raised-landing-pad", "floor", disc, dg + 120, dg + 123, 152.4, 152.4, 0, true);
        Add("CITY:pad-support", "city", disc, dg, dg + 120, 12, 12);
        Rings.Add(new Ring("CITY:spinning-ring", cc + new IslandPoint(.35, .75), Ground(cc + new IslandPoint(.35, .75)) + 100, 45, 0, true));
        var fountain = cc + new IslandPoint(0, -.23); double fg = Ground(fountain); Add("CITY:fountain-basin", "pool", fountain, fg, fg + .5, 34, 34);
        for (int i = 0; i < 20; i++) { var a = fountain + new IslandPoint(-.015 + i * .0015, 0); var b = fountain + new IslandPoint(-.0135 + i * .0015, 0); double za = fg + 10 * Math.Sin(i * Math.PI / 20), zb = fg + 10 * Math.Sin((i + 1) * Math.PI / 20); CableBetween("CITY:fountain-arc", "pool", a, za, b, zb, .12); }
        var race = IslandTerrain.Feature("RACE");
        foreach (var e in race["elements"].Items)
        {
            var p = e["centre"].Point; var u = IslandPoint.Heading(e.N("heading_true_deg") + 90);
            if (e.S("kind") == "Gate") foreach (int s in new[] { -1, 1 }) Add("RACE:gate:" + e.N("sequence"), "pylon", p + u * (.02286 * s), 0, 75, 3, 3);
            else Add("RACE:pylon:" + e.N("sequence"), "pylon", p, 0, 75, 3, 3);
            Add("RACE:moored-base", "white", p, -.6, .5, 8, 8);
        }
        var carson = IslandTerrain.Feature("F11")["centre"].Point;
        // Fences/structures kept off the600x300m crop field and its working flight line.
        foreach (var name in new[] { "F13", "F14", "F15" })
        {
            var c = IslandTerrain.Feature(name)["centre"].Point;
            for (int i = 0; i < 42; i++)
            {
                var p = c + new IslandPoint((Random(i, "driftwood") - .5) * .65, (Random(i + 90, "driftwood") - .5) * .20);
                if (Math.Abs(p.N - c.N) < .030) continue; double z = Ground(p);
                Add(name + ":seeded-driftwood", "wood", p, z, z + .6, 3 + Random(i + 142, "driftwood") * 4, .5, Random(i + 202, "driftwood") * 360);
            }
        }
        var avalon = IslandTerrain.Feature("F3")["centre"].Point; double az = Ground(avalon);
        Add("Avalon:casino", "casino", avalon, az, az + 42, 55, 55);
        for (int i = 0; i < 20; i++) { var p = avalon + new IslandPoint((i % 5) * .028, .08 + (i / 5) * .032); double z = Ground(p); Add("Avalon:house", "house", p, z, z + 7, 12, 10, 30); }
        foreach (var id in new[] { "F3", "F16", "F6" })
        {
            var c = id == "F6" ? W["water"].Items.First(q => q.S("id") == "Lake Powell")["centre"].Point - new IslandPoint(.18, 0) : IslandTerrain.Feature(id)["centre"].Point; double z = id == "F6" ? 3700 * IslandTerrain.Feet + .5 : 1.5;
            for (int i = 0; i < 5; i++) { var p = c + new IslandPoint(.12 + i * .025, 0); Add(id + ":floating-dock", "wood", p, z - .5, z, 60, 4, 0, true); Add(id + ":moored-boat", "white", p + new IslandPoint(.008, -.01), z - .3, z + 2, 8, 3, 0); }
        }
        var seaArch = IslandTerrain.Feature("F4")["centre"].Point; Arch(seaArch, 280, 150, 0, 90, 34);
        var gateway = IslandTerrain.Feature("GATEWAY")["centre"].Point; Arch(gateway, 150, 175, Ground(gateway), 90, 14);
        var powell = IslandTerrain.Feature("F6")["centre"].Point; double water = 3700 * IslandTerrain.Feet;
        Add("Powell:dam", "concrete", powell, water - 90, water + 7, 300, 22, 90, true);
        for (int i = 0; i < 5; i++) { var p = powell + new IslandPoint((i - 2) * .10, .20 + (i % 2) * .08); Add("Powell:spire", "sandstone", p, Ground(p), Math.Max(Ground(p) + 50, water + 80), 18, 18); }
        Arch(powell + new IslandPoint(.15, .10), 120, 65, water, 0, 14);
        foreach(var mesa in W["mesa_pads"].Items)
        {
            var c=mesa["centre"].Point;var u=IslandPoint.Heading(mesa.N("heading_true_deg"));var v=new IslandPoint(u.N,-u.E);
            foreach(int side in new[]{-1,1}) foreach(int end in new[]{-1,1})
            {
                var p=c+u*(end*(mesa.N("length_m")/2000+.005))+v*(side*.03048/2);double z=Ground(p);Add(mesa.S("id")+":threshold-cairn","rock",p,z,z+1.5,2,2);
            }
        }
        var slope = IslandTerrain.Feature("F18")["centre"].Point;
        Add("Mokuleia:launch-reference", "wood", slope, Ground(slope), Ground(slope) + .2, 15, 5, 60, true);
        // Tow geometry, contest marks and undulation lane have explicit anchor data for Claude's hook.
        var ap3 = IslandTerrain.Fields.First(f => f.Field == "AP3");
        Add("AP3:tow-hookup", "yellow", ap3.Centre - ap3.U * (ap3.Length / 2000 - .05), ap3.Elevation, ap3.Elevation + .03, 3, 3);
        for (int i = 0; i < 26; i++)
        {
            var p = ap3.Centre + ap3.V * .05 + ap3.U * ((i - 13) * .01); double z = ap3.SurfaceHeight(p);
            Add("AP3:undulation-lane", "grass", p, z - .1, z + .25 * (1 + Math.Sin(i * Math.PI / 6)), 10, 12, ap3.Heading, true);
        }
    }
    public static bool Timbered(double x, double y)
    {
        if (!WorldTerrain.OnBuiltGround(x, y) || IslandTerrain.SurfaceAt(x, y) != WorldTerrain.Surface.Rough) return false;
        var p = new IslandPoint(y / 1000, x / 1000); double h = Ground(p);
        if (h < 6 || p.E > 2 && p.N < -1 && p.N > -7) return false;
        if (IslandCavities.DrakeInside(p) || IslandCavities.PirateInside(p)) return false;
        foreach (var f in IslandTerrain.Fields) if (f.Contains(p, 130)) return false;
        foreach (var e in W["clearance_budgets"].Items)
        {
            if (e.S("kind") != "final") continue; var d = p - e["a"].Point; var u = e["u"].Point; double ad = -d.Dot(u), cr = Math.Abs(d.E * u.N - d.N * u.E);
            if (ad >= -.2 && ad <= e.N("length") && cr <= .20 + .05 * ad) return false;
        }
        foreach (var f in W["features"].Items)
        {
            var d = p - f["centre"].Point;
            if (f.S("id") == "CITY" && Math.Abs(d.E) < .60 && Math.Abs(d.N) < 1.4) return false;
            if (f.S("id") == "F11" && Math.Abs(d.E) < .35 && Math.Abs(d.N) < .20) return false;
        }
        return true;
    }
}

/// <summary>Chart/entry geometry and selectable state. Call ContainsActive with MSL altitude.
/// Claude owns entry detection, event/AI spawning, ATIS, scoring and menus.</summary>
public sealed class IslandCombatZone
{
    public readonly IslandValue Definition;
    public readonly string Id, Name;
    public readonly IslandPoint[][] ActivePolygons;
    private readonly (IslandPoint[] polygon, double floor)[] raised;
    public readonly double BaseFloorM, CeilingM;
    public bool Hot { get; set; } = true;
    public IslandCombatZone(IslandValue d, IslandValue geometry)
    {
        Definition = d; Id = d.S("id"); Name = d.S("name"); BaseFloorM = d.N("floor_msl_ft_target") * IslandTerrain.Feet; CeilingM = d.N("ceiling_msl_ft_target") * IslandTerrain.Feet;
        ActivePolygons = geometry["active_polygons"].Items.Select(p => p.Points).ToArray();
        raised = geometry["raised_floor_overlays"].Items.Select(v => (v["boundary"].Points, v.N("floor_ft") * IslandTerrain.Feet)).ToArray();
    }
    public double FloorAt(IslandPoint p) { double floor = BaseFloorM; foreach (var r in raised) if (IslandTerrain.Inside(p, r.polygon)) floor = Math.Max(floor, r.floor); return floor; }
    public bool ContainsActive(double x, double y, double up)
    {
        var p = new IslandPoint(y / 1000, x / 1000);
        return up >= FloorAt(p) && up <= CeilingM && ActivePolygons.Any(poly => IslandTerrain.Inside(p, poly));
    }
    public string ChartLabel => Name + " " + Math.Round(BaseFloorM / IslandTerrain.Feet) + "–" + Math.Round(CeilingM / IslandTerrain.Feet) + " MSL " + (Hot ? "HOT" : "COLD");
}
