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
            IslandTerrainMesh.Build(root.transform, WorldBuilder.Mat("FlyingGame/IslandTerrain", Color.white)); Water(root.transform); Runways(root.transform); StaticGeometry(root.transform); Cavities(root.transform); SurfaceDetails(root.transform);
            SeasideBuilder.BuildCarrier(root.transform);
            foreach (var z in IslandGeometry.Zones) Zone(z, root.transform);
            ForestStreamer.Attach(WorldTerrain.Active, root.transform);
            return root;
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
                for(int i=0;i<8;i++)
                {
                    var (lo,hi)=IslandWater.BraidSpanKm(i); int steps=(int)Math.Ceiling((hi-lo)/.01);
                    for(int j=0;j<steps;j++)
                    {
                        double e0=lo+(hi-lo)*j/steps,e1=lo+(hi-lo)*(j+1)/steps;
                        var a=c+new IslandPoint(e0,IslandWater.BraidOffsetKm(i,e0));var d=c+new IslandPoint(e1,IslandWater.BraidOffsetKm(i,e1));
                        if(Math.Abs((a-c).N)>.030) StreamRibbon(b,a,d,8);
                    }
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
            if (f.Shape.Kind < -.5)
            {
                Vector3 V(int a, int s) { var p = centre + f.U * (a * length / 2000) + f.V * (s * width / 2000); return P(p, z + (along + a * length / 2) * f.Slope / 100); }
                b.Quad(V(-1, -1), V(1, -1), V(1, 1), V(-1, 1), c);
                return;
            }
            // A shaped (grass/turf) strip: the paint lies on the ground's own surface, every 10 m along and every 6 m across,
            // at the same small offset above it as before.
            int na = Math.Max(1, (int)Math.Ceiling(length / 10)), nc = Math.Max(1, (int)Math.Ceiling(width / 6));
            Vector3 W(double fa, double fc) { var p = centre + f.U * (fa * length / 1000) + f.V * (fc * width / 1000); return P(p, f.SurfaceHeight(p) + (z - f.Elevation)); }
            for (int i = 0; i < na; i++) for (int k = 0; k < nc; k++)
            {
                double a0 = -.5 + (double)i / na, a1 = -.5 + (double)(i + 1) / na, c0 = -.5 + (double)k / nc, c1 = -.5 + (double)(k + 1) / nc;
                b.Quad(W(a0, c0), W(a1, c0), W(a1, c1), W(a0, c1), c);
            }
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
                var finish = StripFinish.Of(f.Surface);
                if (finish == StripFinish.Kind.Water)
                {
                    for (double a = -f.Length / 2; a <= f.Length / 2; a += 50) foreach (int s in new[] { -1, 1 }) b.Cylinder(P(f.Centre + f.U * (a / 1000) + f.V * (s * f.Width / 2000), f.Elevation), 1.3f, 1.2f, 8, Orange);
                    continue;
                }
                double length = f.Length, offset = 0; // Interior runway paint/deck stays beneath the opaque rock roof.
                // Shoulder planes are authored at the exact field datum, the pad's own flat shoulder beyond both edges
                // (60 m everywhere but the Frank Church canyon strips, whose 30-40 m shoulders end at the river bank:
                // a 60 m plane there hung in the air over the bank).
                foreach (int side in new[] { -1, 1 }) SurfaceQuad(b, f, length, f.Shoulder, f.Elevation + .025, new Color(.47f, .49f, .32f), offset, side * (f.Width / 2 + f.Shoulder / 2));
                if (f.Field == "AP3") for (int i = 0; i < 11; i++) SurfaceQuad(b, f, .7, 8, f.Elevation + .08, i == 0 ? Orange : White, -f.Length / 2 + 150 + i * 25);
                if (!StripFinish.Painted(f.Surface))
                {
                    // Dirt, turf, gravel and sand carry no runway paint at all (Ty 2026-11-03, the Frank Church strips):
                    // no centreline, threshold bars, numbers or painted ident -- just worn ground.
                    UnpavedSurface(b, f, finish);
                    continue;
                }
                SurfaceQuad(b, f, length, f.Width, f.Elevation + .04, new Color(.15f, .17f, .18f), offset);
                for (double a = -f.Length / 2 + 40; a < -f.Length / 2 + length - 20; a += 60) SurfaceQuad(b, f, 30, .9, f.Elevation + .06, White, a);
                string[] nums = f.Numbers.Split('/');
                for (int end = 0; end < (f.Field == "DRAKE" ? 1 : 2); end++)
                {
                    double a = (end == 0 ? -1 : 1) * (f.Length / 2 - 25); var p = f.Centre + f.U * (a / 1000);
                    Label(f.Id + ":number", nums[Math.Min(end, nums.Length - 1)], p, f.SurfaceHeight(p) + .08, parent, f.Heading + (end == 0 ? 0 : 180), (float)Math.Min(12, f.Width * .45));
                    SurfaceQuad(b, f, 3, f.Width - 3, f.Elevation + .07, White, (end == 0 ? -1 : 1) * (f.Length / 2 - 7));
                }
                var identAt = f.Centre + f.V * ((f.Width / 2 + 16) / 1000);
                Label(f.Id + ":ident", f.Field, identAt, Math.Max(f.Elevation, IslandTerrain.HeightKm(identAt)) + .10, parent, f.Heading, 8);
            }
            b.Build("Runway-surfaces-markings-buoys", parent, Lit);
        }
        /// <summary>An unpaved strip as worn ground: its own colour in 10 m patches along, a pair of darker wheel tracks
        /// (2.6 m apart, a main-gear track) down the middle where the traffic runs, edges fading into the shoulder grass,
        /// and on turf the lengthwise stripes a mower leaves. All low contrast and no white: nothing reads as paint. Laid on
        /// the strip's own surface (plane plus natural shape) at the same 4 cm as before, so it follows a shaped field.</summary>
        private static void UnpavedSurface(B b, IslandField f, StripFinish.Kind finish)
        {
            Color shoulder = new(.47f, .49f, .32f);
            (Color ground, Color track, float patch) = finish switch
            {
                StripFinish.Kind.Dirt => (new Color(.46f, .36f, .25f), new Color(.37f, .29f, .20f), .07f),
                StripFinish.Kind.Turf => (new Color(.40f, .49f, .25f), new Color(.45f, .46f, .27f), .025f),
                StripFinish.Kind.Sand => (Sand, Sand * .95f, .03f),
                _ => (new Color(.53f, .50f, .43f), new Color(.45f, .42f, .36f), .05f),
            };
            ground.a = track.a = 1;
            double half = f.Width / 2, edge = Math.Min(2.5, f.Width * .12);
            var cuts = new List<double> { -half, -half + edge, -1.75, -.85, .85, 1.75, half - edge, half };
            if (finish == StripFinish.Kind.Turf) for (double c = -half + edge + 4; c < half - edge - 1; c += 4) cuts.Add(c);
            cuts.Sort();
            int na = Math.Max(1, (int)Math.Ceiling(f.Length / 10));
            uint seed = 2166136261; foreach (char ch in f.Id) seed = unchecked((seed ^ ch) * 16777619);
            Vector3 W(double fa, double cm) { var p = f.Centre + f.U * (fa * f.Length / 1000) + f.V * (cm / 1000); return P(p, f.SurfaceHeight(p) + .04); }
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                double c0 = cuts[k], c1 = cuts[k + 1], cm = (c0 + c1) / 2, ac = Math.Abs(cm);
                if (c1 - c0 < .01) continue;
                Color band = ac > .85 && ac < 1.75 && finish != StripFinish.Kind.Sand ? track : ground;
                if (ac > half - edge) band = Color.Lerp(ground, shoulder, .5f);
                if (finish == StripFinish.Kind.Turf && ac < half - edge && (ac < .85 || ac > 1.75)) band *= ((int)Math.Floor((cm + half) / 4) & 1) == 0 ? .98f : 1.02f;
                for (int i = 0; i < na; i++)
                {
                    // Patchiness: a fixed hash of (strip, cell, band), so every build draws the same ground.
                    uint h = unchecked(seed ^ (uint)i * 19349663u ^ (uint)k * 83492791u); h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15;
                    float shade = 1 + patch * ((h & 0xffff) / 32767.5f - 1);
                    Color c = band * shade; c.a = 1;
                    double a0 = -.5 + (double)i / na, a1 = -.5 + (double)(i + 1) / na;
                    b.Quad(W(a0, c0), W(a1, c0), W(a1, c1), W(a0, c1), c);
                }
            }
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
            // Knik's bar (F13) is at the glacier's foot and its clean landing lane is NOT painted: the 520 x 52 m tan
            // stripe between the braids was the runway Ty saw on the glacier (2026-11-03). The lane is still there
            // for the gear — the braids keep out of it (IslandWater.BraidOffsetKm) — and reads as the bar itself.
            foreach (var id in new[] { "F14", "F15" })
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
