using System;
using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;
using B = FlyingGame.Bridge.SeasideBuilder.Batch;

namespace FlyingGame.Bridge
{
    /// <summary>Approved island visuals from the same world data and geometry used by contact.
    /// No weather, station, procedure, AI or scoring implementation belongs here.</summary>
    public static class IslandWorldBuilder
    {
        private static IslandValue W => IslandTerrain.World;
        private static Vector3 P(IslandPoint p, double z) => WorldBuilder.U(p.X, p.Y, z);
        private static Color Sand = new(.79f, .69f, .48f), Rock = new(.38f, .39f, .36f), White = new(.94f, .94f, .88f), Orange = new(1f, .35f, .04f);
        private static Material Lit => WorldBuilder.Mat("FlyingGame/LitVC", Color.white);
        public static GameObject Build()
        {
            var root = new GameObject("IslandWorld-1B");
            root.AddComponent<IslandSceneryClock>();
            IslandGeometry.Build();
            Terrain(root.transform); Water(root.transform); Runways(root.transform); StaticGeometry(root.transform); Cavities(root.transform); SurfaceDetails(root.transform);
            SeasideBuilder.BuildCarrier(root.transform);
            foreach (var z in IslandGeometry.Zones) Zone(z, root.transform);
            ForestStreamer.Attach(WorldTerrain.Active, root.transform);
            return root;
        }
        private static Color TerrainColour(double x, double y, double h)
        {
            if (h < 6) return Sand;
            var p = new IslandPoint(y / 1000, x / 1000);
            if (IslandTerrain.SurfaceAt(x, y) == WorldTerrain.Surface.Gravel) return new Color(.59f,.54f,.42f);
            if (IslandTerrain.SurfaceAt(x, y) == WorldTerrain.Surface.Snow) return new Color(.76f, .86f, .91f);
            if (p.E > 2 && p.N < -1 && p.N > -7) return Color.Lerp(new Color(.66f, .41f, .25f), Sand, .2f + .13f * Mathf.Sin((float)h / 9));
            if (p.E < -12) return Color.Lerp(new Color(.19f, .30f, .23f), Rock, Mathf.Clamp01((float)h / 700));
            float t = Mathf.Clamp01((float)(h - 2000) / 600);
            return Color.Lerp(Color.Lerp(new Color(.28f, .40f, .23f), Rock, Mathf.Clamp01((float)h / 2400)), new Color(.94f, .95f, .94f), t);
        }
        private static bool NearDetail(double n, double e)
        {
            var p = new IslandPoint(e / 1000, n / 1000);
            foreach (var f in IslandTerrain.Fields) if (f.Contains(p, 130)) return true;
            foreach(var q in W["mesa_pads"].Items) if((p-q["centre"].Point).Length<.35)return true;
            if ((p - IslandCavities.Drake["chamber_centre"].Point).Length < .65 || (p - IslandCavities.Pirate["chamber_centre"].Point).Length < .35) return true;
            return false;
        }
        private static bool PortalHole(IslandPoint p)
        {
            // A mesh aperture for the cave throat: physical roof/side shell supplied separately.
            var f = IslandTerrain.Feature("F2"); var dp = p - f["centre"].Point; var u = IslandPoint.Heading(f.N("mouth_true_deg") + 180);
            return dp.Dot(u) >= -.015 && dp.Dot(u) < .20 && Math.Abs(dp.E * u.N - dp.N * u.E) < .030
                &&(p-f["interior"]["chamber_centre"].Point).Length>.090 || IslandCavities.Skylight(p);
        }
        private static void Terrain(Transform parent)
        {
            // 2 km chunks keep index counts and culling bounded. Detail cells use10m vertices;
            // coarse neighbours add matching edge vertices where detail patches meet them.
            var mat = Lit;
            for (int north = -20000; north < 20000; north += 2000) for (int east = -20000; east < 20000; east += 2000)
            {
                var b = new B();
                for (int n = north; n < north + 2000; n += 100) for (int e = east; e < east + 2000; e += 100)
                {
                    double middle = IslandTerrain.HeightAt(n + 50, e + 50);
                    if (middle < -29.99) continue;
                    int step = NearDetail(n + 50, e + 50) ? 10 : 100;
                    for (int dn = 0; dn < 100; dn += step) for (int de = 0; de < 100; de += step)
                    {
                        double n0 = n + dn, e0 = e + de; var pc = new IslandPoint((e0 + step / 2.0) / 1000, (n0 + step / 2.0) / 1000);
                        if (PortalHole(pc)) continue;
                        Vector3 V(double nn, double ee) => WorldBuilder.U(nn, ee, IslandTerrain.HeightAt(nn, ee));
                        Color c = TerrainColour(n0, e0, IslandTerrain.HeightAt(n0, e0));
                        // SW–NE diagonal agrees with review bake; top faces wind clockwise in Unity.
                        var sw = V(n0, e0); var se = V(n0, e0 + step); var nw = V(n0 + step, e0); var ne = V(n0 + step, e0 + step);
                        bool stitch = step == 100 && (NearDetail(n + 50, e - 50) || NearDetail(n + 50, e + 150) || NearDetail(n - 50, e + 50) || NearDetail(n + 150, e + 50));
                        if (stitch)
                        {
                            var centre=V(n0+50,e0+50);
                            for(int k=0;k<10;k++)
                            {
                                double q=k*10,r=(k+1)*10;
                                b.Tri(centre,V(n0+q,e0),V(n0+r,e0),c);
                                b.Tri(centre,V(n0+100,e0+q),V(n0+100,e0+r),c);
                                b.Tri(centre,V(n0+100-q,e0+100),V(n0+100-r,e0+100),c);
                                b.Tri(centre,V(n0,e0+100-q),V(n0,e0+100-r),c);
                            }
                        }
                        else { b.Tri(sw, nw, ne, c); b.Tri(sw, ne, se, c); }
                    }
                }
                if (b.Count > 0) b.Build("Terrain-" + north + "-" + east, parent, mat);
            }
        }
        private static void Water(Transform parent)
        {
            var b = new B(); var blue = new Color(.075f, .30f, .41f);
            // Open ocean continues beyond the20km fade, with no ring collider or wall.
            b.Quad(new Vector3(-45000, 0, -45000), new Vector3(-45000, 0, 45000), new Vector3(45000, 0, 45000), new Vector3(45000, 0, -45000), blue);
            foreach (var lake in W["water"].Items)
            {
                var c = lake["centre"].Point; var u = IslandPoint.Heading(lake.N("long_axis_true_deg", 90)); var v = new IslandPoint(u.N, -u.E); double z = lake.N("elevation_ft") * IslandTerrain.Feet;
                IslandPoint Q(int i) { double a = i * Math.PI * 2 / 96; return c + u * (Math.Cos(a) * lake["radii_km"][0].Number()) + v * (Math.Sin(a) * lake["radii_km"][1].Number()); }
                for (int i = 0; i < 96; i++) b.Tri(P(c, z), P(Q(i), z), P(Q(i + 1), z), new Color(.10f, .40f, .48f));
            }
            foreach (var f in IslandTerrain.Fields) if (f.Surface == "water") SurfaceQuad(b, f, f.Length, f.Width, f.Elevation, blue);
            foreach (var canyon in W["canyons"].Items)
            {
                var path=canyon["path"].Items;
                for(int i=1;i<path.Length;i++) StreamRibbon(b,path[i-1].Point,path[i].Point,canyon.S("id")=="C3"?36:20);
            }
            foreach(string id in new[]{"F13","F14","F15"})
            {
                var c=IslandTerrain.Feature(id)["centre"].Point;
                for(int i=0;i<8;i++) for(int j=0;j<70;j++)
                {
                    double e0=-.35+j*.01,e1=e0+.01;
                    var a=c+new IslandPoint(e0,(i-3.5)*.025+.004*Math.Sin(e0*27+i));var d=c+new IslandPoint(e1,(i-3.5)*.025+.004*Math.Sin(e1*27+i));
                    if(Math.Abs((a-c).N)>.030) StreamRibbon(b,a,d,8);
                }
            }
            b.Build("Ocean-lakes-water-lanes", parent, WorldBuilder.Mat("FlyingGame/Water", new Color(.075f,.30f,.41f,.95f)));
        }
        private static void StreamRibbon(B b, IslandPoint a, IslandPoint c, double width)
        {
            var delta=c-a;var v=new IslandPoint(delta.N,-delta.E)*(width/2000/Math.Max(.00001,delta.Length));int steps=Math.Max(1,(int)(delta.Length*1000/5));
            Vector3 V(IslandPoint p)=>P(p,Math.Max(0,IslandTerrain.HeightKm(p)+.15));
            for(int i=0;i<steps;i++)
            {
                var p=a+delta*(i/(double)steps);var q=a+delta*((i+1)/(double)steps);
                if(!IslandWater.StreamAt((p+q)*.5)) continue;
                float grade=(float)Math.Abs(IslandTerrain.HeightKm(p)-IslandTerrain.HeightKm(q))/5;
                b.Quad(V(p-v),V(q-v),V(q+v),V(p+v),grade>.5f?new Color(.65f,.81f,.82f):new Color(.10f,.35f,.42f));
            }
        }
        private static void SurfaceQuad(B b, IslandField f, double length, double width, double z, Color c, double along = 0, double across = 0)
        {
            var centre = f.Centre + f.U * (along / 1000) + f.V * (across / 1000);
            Vector3 V(int a, int s) { var p = centre + f.U * (a * length / 2000) + f.V * (s * width / 2000); return P(p, z + (along + a * length / 2) * f.Slope / 100); }
            b.Quad(V(-1, -1), V(1, -1), V(1, 1), V(-1, 1), c);
        }
        private static void Label(string name, string text, IslandPoint p, double up, Transform parent, double heading = 0, float size = 12)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = P(p, up); go.transform.rotation = Quaternion.Euler(90, (float)heading, 0);
            var label = go.AddComponent<TextMesh>(); label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.fontSize = 64; label.characterSize = size / 6.4f; label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(label.font!=null) go.GetComponent<MeshRenderer>().sharedMaterial=WorldBuilder.DepthTestedText(label.font); label.color = White;
        }
        private static void Runways(Transform parent)
        {
            var b = new B();
            foreach (var f in IslandTerrain.Fields)
            {
                if (f.Surface == "water")
                {
                    for (double a = -f.Length / 2; a <= f.Length / 2; a += 50) foreach (int s in new[] { -1, 1 }) b.Cylinder(P(f.Centre + f.U * (a / 1000) + f.V * (s * f.Width / 2000), f.Elevation), 1.3f, 1.2f, 8, Orange);
                    continue;
                }
                double length = f.Length, offset = 0; // Interior runway paint/deck stays beneath the opaque rock roof.
                Color surface = f.Surface == "asphalt" || f.Surface == "paved" ? new Color(.15f, .17f, .18f) : f.Surface.Contains("grass") || f.Surface == "turf" ? new Color(.44f, .50f, .27f) : f.Surface == "hard sand" ? Sand : new Color(.58f, .54f, .45f);
                SurfaceQuad(b, f, length, f.Width, f.Elevation + .04, surface, offset);
                // Shoulder planes are authored at the exact field datum,60m beyond both edges.
                foreach (int side in new[] { -1, 1 }) SurfaceQuad(b, f, length, 60, f.Elevation + .025, new Color(.47f, .49f, .32f), offset, side * (f.Width / 2 + 30));
                for (double a = -f.Length / 2 + 40; a < -f.Length / 2 + length - 20; a += 60) SurfaceQuad(b, f, 30, .9, f.Elevation + .06, White, a);
                string[] nums = f.Numbers.Split('/');
                for (int end = 0; end < (f.Field == "DRAKE" ? 1 : 2); end++)
                {
                    double a = (end == 0 ? -1 : 1) * (f.Length / 2 - 25); var p = f.Centre + f.U * (a / 1000);
                    Label(f.Id + ":number", nums[Math.Min(end, nums.Length - 1)], p, f.Plane(p) + .08, parent, f.Heading + (end == 0 ? 0 : 180), (float)Math.Min(12, f.Width * .45));
                    SurfaceQuad(b, f, 3, f.Width - 3, f.Elevation + .07, White, (end == 0 ? -1 : 1) * (f.Length / 2 - 7));
                }
                Label(f.Id + ":ident", f.Field, f.Centre + f.V * ((f.Width / 2 + 16) / 1000), f.Elevation + .10, parent, f.Heading, 8);
                if (f.Field == "AP3") for (int i = 0; i < 11; i++) SurfaceQuad(b, f, .7, 8, f.Elevation + .08, i == 0 ? Orange : White, -f.Length / 2 + 150 + i * 25);
            }
            b.Build("Runway-surfaces-markings-buoys", parent, Lit);
        }
        private static Color Colour(string mat) => mat switch
        {
            "white" => White, "orange" => Orange, "yellow" => new Color(.96f, .79f, .20f), "wire" => new Color(.18f, .19f, .20f), "tower" => new Color(.70f, .20f, .12f),
            "rock" => Rock, "sandstone" => new Color(.73f, .40f, .24f), "grass" => new Color(.42f, .54f, .26f), "city" => new Color(.55f, .61f, .63f), "glass" => new Color(.27f, .53f, .61f),
            "wood" => new Color(.37f, .25f, .14f), "pool" => new Color(.12f, .60f, .68f), "pylon" => new Color(.88f, .13f, .10f), "roof" => new Color(.32f, .36f, .38f),
            "house" => new Color(.86f, .77f, .59f), "casino" => new Color(.92f, .86f, .69f), "bridge" => new Color(.51f, .46f, .35f), "aircraft" => new Color(.89f, .85f, .65f), _ => new Color(.60f, .61f, .56f)
        };
        private static void StaticGeometry(Transform parent)
        {
            // Regional batches retain culling, rather than one world-size scenery bound.
            var batches = new Dictionary<string, B>();
            B BatchAt(double x, double y) { string key = Math.Floor(x / 2000) + ":" + Math.Floor(y / 2000); if (!batches.TryGetValue(key, out var b)) { b = new B(); batches.Add(key, b); } return b; }
            foreach (var s in IslandGeometry.Blocks)
            {
                var b = BatchAt(s.X, s.Y); b.Box(WorldBuilder.U(s.X, s.Y, (s.Bottom + s.Top) / 2), new Vector3((float)s.Width, (float)Math.Max(.03, s.Top - s.Bottom), (float)s.Length), Quaternion.Euler(0, (float)s.Heading, 0), Colour(s.Material));
            }
            foreach (var s in IslandGeometry.Slopes)
            {
                Vector3 V(int a,int c) { var p=s.Centre+s.U*(a*s.Length/2000)+s.V*(c*s.Width/2000); return P(p,s.Elevation+a*s.Length/2*s.Grade); }
                BatchAt(s.Centre.X,s.Centre.Y).Quad(V(-1,-1),V(1,-1),V(1,1),V(-1,1),s.Id.Contains("ramp")?Colour("concrete"):Colour("grass"));
            }
            foreach (var s in IslandGeometry.Cables) BatchAt(s.A.X, s.A.Y).Beam(WorldBuilder.U(s.A.X, s.A.Y, -s.A.Z), WorldBuilder.U(s.B.X, s.B.Y, -s.B.Z), (float)(2 * s.Radius), (float)(2 * s.Radius), Colour(s.Material));
            foreach (var a in IslandGeometry.Arches)
            {
                var b = BatchAt(a.Ax, a.Ay); double h = a.HalfSpan;
                for (int i = 0; i < 48; i++)
                {
                    double s0 = -h + 2 * h * i / 48, s1 = -h + 2 * h * (i + 1) / 48;
                    double t0 = i / 48.0, t1 = (i + 1) / 48.0;
                    var p0 = WorldBuilder.U(a.Ax + (a.Bx - a.Ax) * t0, a.Ay + (a.By - a.Ay) * t0, a.InnerAt(s0)); var p1 = WorldBuilder.U(a.Ax + (a.Bx - a.Ax) * t1, a.Ay + (a.By - a.Ay) * t1, a.InnerAt(s1));
                    b.Beam(p0 + Vector3.up * (float)((a.OuterAt(s0) - a.InnerAt(s0)) / 2), p1 + Vector3.up * (float)((a.OuterAt(s1) - a.InnerAt(s1)) / 2), (float)a.WidthM, (float)(a.OuterAt((s0 + s1) / 2) - a.InnerAt((s0 + s1) / 2)), Rock);
                }
            }
            foreach (var r in IslandGeometry.Rings)
            {
                if (r.Spinning)
                {
                    var b = new B(); b.Torus(Vector3.zero, Vector3.forward, (float)r.Radius, 1.4f, 64, 8, i => i % 2 == 0 ? White : Orange);
                    var go = b.Build(r.Id, parent, Lit); go.transform.position = P(r.Centre, r.Up); go.AddComponent<IslandSpinningRing>();
                }
                else BatchAt(r.Centre.X, r.Centre.Y).Torus(P(r.Centre, r.Up), new Vector3((float)Math.Sin(r.Heading * Math.PI / 180), 0, (float)Math.Cos(r.Heading * Math.PI / 180)), (float)r.Radius, 1.4f, 48, 6, i => i % 2 == 0 ? White : Orange);
            }
            foreach (var p in IslandGeometry.MarkerBalls) BatchAt(p.p.X, p.p.Y).Cylinder(P(p.p, p.up - .45), .6f, .9f, 10, Orange);
            foreach (var p in IslandGeometry.ObstructionLights) BatchAt(p.p.X, p.p.Y).Cylinder(P(p.p, p.up), .6f, 1, 8, new Color(1, .04f, .01f));
            foreach(var p in IslandGeometry.ObstructionLights)
            {
                var go=new GameObject(p.id+"-obstruction-light");go.transform.SetParent(parent,false);go.transform.position=P(p.p,p.up+1);
                var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.02f,.01f);light.range=18;light.intensity=1.2f;light.shadows=LightShadows.None;
            }
            foreach (var b in batches) b.Value.Build("Scenery-" + b.Key, parent, Lit);
        }
        private static void Cavities(Transform parent)
        {
            var b = new B(); var d = IslandCavities.Drake; var c = d["chamber_centre"].Point; double floor = d.N("floor_msl_m"), ceiling = d.N("ceiling_msl_m");
            // Downward-facing chamber roof. The upper surface remains shared terrain.
            b.Quad(P(c + new IslandPoint(-.210, -.410), ceiling), P(c + new IslandPoint(.210, -.410), ceiling), P(c + new IslandPoint(.210, .410), ceiling), P(c + new IslandPoint(-.210, .410), ceiling), Rock);
            var f = IslandTerrain.Feature("F2"); var ci = f["interior"]; var cc = ci["chamber_centre"].Point; double cf = ci.N("floor_msl_m"), roof = ci.N("ceiling_msl_m");
            for (int i = 0; i < 96; i++)
            {
                var a = IslandPoint.Heading(i * 360.0 / 96); var z = IslandPoint.Heading((i + 1) * 360.0 / 96);
                // Annulus preserves the10m skylight hole in both interior and exterior meshes.
                b.Quad(P(cc + a * .005, roof), P(cc + z * .005, roof), P(cc + z * .09, roof), P(cc + a * .09, roof), Rock);
                b.Quad(P(cc + a * .005, roof), P(cc + z * .005, roof), P(cc + z * .005, Math.Max(60, IslandTerrain.HeightKm(cc + z * .005))), P(cc + a * .005, Math.Max(60, IslandTerrain.HeightKm(cc + a * .005))), Rock);
                b.Tri(P(cc, cf), P(cc + a * .09, cf), P(cc + z * .09, cf), Sand);
            }
            var mouth = f["centre"].Point; var u = IslandPoint.Heading(f.N("mouth_true_deg") + 180); var v = new IslandPoint(u.N, -u.E); var end = mouth + u * .20;
            b.Quad(P(mouth - v * .03, cf), P(end - v * .03, cf), P(end + v * .03, cf), P(mouth + v * .03, cf), Sand);
            // Union of throat and chamber air volumes: low throat roof stops at the curved
            // chamber boundary, so no spurious low slab or extra skylight crosses the junction.
            double tr=ci.N("throat_ceiling_msl_m");
            for(int along=0;along<200;along+=2)for(int across=-30;across<30;across+=2)
            {
                var pc=mouth+u*((along+1)/1000.0)+v*((across+1)/1000.0);if((pc-cc).Length<=.090)continue;
                var p0=mouth+u*(along/1000.0)+v*(across/1000.0);var p1=p0+u*.002;var p2=p1+v*.002;var p3=p0+v*.002;
                b.Quad(P(p3,tr),P(p2,tr),P(p1,tr),P(p0,tr),Rock);
                double top=ci.N("throat_roof_top_msl_m_min");b.Quad(P(p0,top),P(p1,top),P(p2,top),P(p3,top),Rock);
            }
            b.Build("Drake-pirate-interior-shells", parent, Lit);
            // Hologram is a translucent visual; no solid and no enrolment/trigger logic.
            var holo = new B(); var m = d["mouth"].Point;
            holo.Quad(P(m + new IslandPoint(-.03, 0), floor), P(m + new IslandPoint(-.03, 0), floor + 26), P(m + new IslandPoint(.03, 0), floor + 26), P(m + new IslandPoint(.03, 0), floor), new Color(.24f, .53f, .60f, .18f));
            holo.Build("Drake-single-north-hologram", parent, WorldBuilder.Mat("Sprites/Default", new Color(.35f, .66f, .76f, .18f)));
        }
        private static void SurfaceDetails(Transform parent)
        {
            var b = new B();
            foreach (var road in W["roads"].Items)
            {
                var path = road["path"].Items;
                for (int i = 1; i < path.Length; i++) Ribbon(b, path[i - 1].Point, path[i].Point, 6, new Color(.57f, .47f, .31f));
            }
            foreach (var line in W["practice_lines"].Items) Ribbon(b, line["path"][0].Point, line["path"][1].Point, 2, White);
            foreach(var mesa in W["mesa_pads"].Items)
            {
                var c=mesa["centre"].Point;var u=IslandPoint.Heading(mesa.N("heading_true_deg"));var v=new IslandPoint(u.N,-u.E);
                foreach(int s in new[]{-1,1}) Ribbon(b,c-u*(mesa.N("length_m")/2000)+v*(s*.0012),c+u*(mesa.N("length_m")/2000)+v*(s*.0012),.4,new Color(.38f,.32f,.23f));
            }
            var crop = IslandTerrain.Feature("F11")["centre"].Point;
            for (int i = 0; i < 30; i++) Ribbon(b, crop + new IslandPoint(-.30, -.15 + i * .01), crop + new IslandPoint(.30, -.15 + i * .01), 7, i % 2 == 0 ? new Color(.49f, .57f, .21f) : new Color(.35f, .42f, .14f));
            var city = IslandTerrain.Feature("CITY")["centre"].Point;
            for (int i = -3; i <= 3; i++) { Ribbon(b, city + new IslandPoint(i * .18, -1.3), city + new IslandPoint(i * .18, 1.3), 24, new Color(.17f, .18f, .18f)); Ribbon(b, city + new IslandPoint(-.55, i * .46), city + new IslandPoint(.55, i * .46), 24, new Color(.17f, .18f, .18f)); }
            foreach (var id in new[] { "F13", "F14", "F15" })
            {
                var c = IslandTerrain.Feature(id)["centre"].Point;

                Ribbon(b, c + new IslandPoint(-.26, 0), c + new IslandPoint(.26, 0), 52, new Color(.64f, .61f, .49f));
            }
            b.Build("Roads-practice-lines-hay-fields-bars", parent, Lit);
        }
        private static void Ribbon(B b, IslandPoint a, IslandPoint c, double width, Color colour)
        {
            var delta = c - a; var v = new IslandPoint(delta.N, -delta.E) * (width / 2000 / Math.Max(.00001, delta.Length));
            int steps = Math.Max(1, (int)(delta.Length * 1000 / 10));
            Vector3 V(IslandPoint p) => P(p, IslandTerrain.HeightKm(p) + .07);
            for (int j = 0; j < steps; j++) { var p = a + delta * (j / (double)steps); var q = a + delta * ((j + 1) / (double)steps); b.Quad(V(p - v), V(q - v), V(q + v), V(p + v), colour); }
        }
        private static void Zone(IslandCombatZone z, Transform parent)
        {
            var go = new GameObject(z.Id + "-" + z.Name); go.transform.SetParent(parent, false);
            var view = go.AddComponent<IslandZoneView>(); view.Zone = z;
            var contents = new GameObject("HOT-entry-content"); contents.transform.SetParent(go.transform, false); var b = new B();
            foreach (var t in z.Definition["gunnery_targets"].Items)
            {
                var c = t["centre"].Point; double h = IslandTerrain.HeightKm(c) + t.N("top_agl_m");
                b.Cylinder(P(c, h - .3), (float)t.N("radius_m"), .3f, 32, White);
                b.Cylinder(P(c, h), (float)t.N("radius_m") * .7f, .03f, 32, new Color(.8f, .08f, .05f));
                b.Cylinder(P(c, h + .04), (float)t.N("radius_m") * .35f, .03f, 32, White);
            }
            var orbit = z.Definition["drone_orbit"]; var c0 = orbit["centre"].Point;
            for (int i = 0; i < orbit.N("count"); i++)
            {
                var c = c0 + IslandPoint.Heading(i * 120) * (orbit.N("radius_m_target") / 1000); double h = orbit.N("altitude_ft_target") * IslandTerrain.Feet;
                b.Box(P(c, h), new Vector3(1, .7f, 5), Orange); b.Box(P(c, h + .3), new Vector3(7, .15f, 1.2f), White);
            }
            b.Build(z.Id + "-target-drone-geometry", contents.transform, Lit);
            contents.SetActive(false); view.Contents = contents;
            // Chart data is the exact polygon union and FloorAt(), consumed by Claude's chart/ATIS UI.
        }
    }
    /// <summary>World geometry adapter only. Claude's HOT-entry logic calls SetContentsVisible;
    /// COLD always hides content. No automatic engagement, AI or event implementation.</summary>
    public sealed class IslandZoneView : MonoBehaviour
    {
        public IslandCombatZone Zone;
        public GameObject Contents;
        public void SetHot(bool hot) { Zone.Hot = hot; if (!hot && Contents != null) Contents.SetActive(false); }
        public void SetContentsVisible(bool visible) { if (Contents != null) Contents.SetActive(visible && Zone.Hot); }
    }
    public sealed class IslandSceneryClock : MonoBehaviour
    {
        private void Update() { if (!SessionSettings.ReplayActive) WorldClock.TimeS=Time.timeSinceLevelLoad; }
    }
    public sealed class IslandSpinningRing : MonoBehaviour
    {
        private void Update() => transform.rotation = Quaternion.Euler(0, (float)(FlyCity.SpinYawRad(WorldClock.TimeS) * 180 / Math.PI), 0);
    }
}
