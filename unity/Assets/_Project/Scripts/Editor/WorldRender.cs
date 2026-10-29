using System.IO;
using FlyingGame.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Headless aerial renders of the generated world (terrain steps, water, airports) to build/world/*.png.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.WorldRender.Render  (no -nographics)</summary>
    public static class WorldRender
    {
        [MenuItem("FlyingGame/Render World")]
        public static void Render()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/world"));
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir,"green-probe.txt"),"M17 terrain source probe; normal PNGs saved before diagnostics.\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            FlyingGame.Core.WorldTerrain.Active = new FlyingGame.Core.WorldTerrain();
            WorldBuilder.BuildAll();
            SunsetLighting.ApplyDay();
            RenderSettings.fog = false;
            // Scenery plates omit translucent training volumes, preserving their frames
            // and all geometry/colliders. The game still shows its navigation aids.
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool training = false;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                    if (t.name == "CombatZone" || t.name.StartsWith("AeroBox")) training = true;
                if (training || (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "FlyingGame/Glass"))
                    renderer.enabled = false;
            }
            var args = System.Environment.GetCommandLineArgs();
            string ReadAtmosphere(string flag, string fallback)
            {
                int index = System.Array.IndexOf(args, flag);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
            }
            SunsetLighting.ConfigureDayAtmosphere(ReadAtmosphere("-skyQuality", "Balanced"),
                float.Parse(ReadAtmosphere("-hazeDensity", "0.000052"), System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(ReadAtmosphere("-hazeClearRange", "850"), System.Globalization.CultureInfo.InvariantCulture));

            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; cam.backgroundColor = new Color(0.45f, 0.66f, 0.95f);
            camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.depthTextureMode = DepthTextureMode.Depth;
            cam.fieldOfView = 50f; cam.nearClipPlane = 1f; cam.farClipPlane = 60000f;
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt;

            // Derive inspection cameras from today's terrain: the old fixed fall views are underground.
            var terrain = FlyingGame.Core.WorldTerrain.Active;
            // Exact read-only sim queries for Claude; emitted on every scenery render.
            // No assumptions that the old ice overlay matched the underlying terrain.
            var glacierReport = new System.Text.StringBuilder();
            glacierReport.AppendLine("name,centre_x,centre_y,centre_elevation_m,lz_x,lz_y,lz_elevation_m,along_deg,across_deg,heading_deg,max_along_deg,max_across_deg,max_30m_residual_m");
            Vector3 GlacierView(int index, double fraction, double across, double above)
            {
                Vector4 a=AlaskaBuilder.GlacierAxis(index);
                double dx=a.z-a.x, dy=a.w-a.y, len=System.Math.Sqrt(dx*dx+dy*dy);
                double x=a.x+dx*fraction-dy/len*across, y=a.y+dy*fraction+dx/len*across;
                return WorldBuilder.U(x,y,terrain.HeightAt(x,y)+above);
            }
            for(int gi=0;gi<3;gi++)
            {
                Vector4 a=AlaskaBuilder.GlacierAxis(gi);
                double dx=a.z-a.x,dy=a.w-a.y,len=System.Math.Sqrt(dx*dx+dy*dy);
                double ux=dx/len,uy=dy/len,rx=-uy,ry=ux;
                double cx=a.x+dx*.5,cy=a.y+dy*.5,lx=a.x+dx*.65,ly=a.y+dy*.65;
                double Height(double along,double across) => terrain.HeightAt(lx+ux*along+rx*across,ly+uy*along+ry*across);
                double Deg(double rise,double run) => System.Math.Atan2(rise,run)*180/System.Math.PI;
                double alongSlope=Deg(Height(15,0)-Height(-15,0),30);
                double crossSlope=Deg(Height(0,15)-Height(0,-15),30);
                double maxAlong=0,maxAcross=0,maxResidual=0;
                for(double along=-400;along<=400;along+=20)
                for(double across=-40;across<=40;across+=20)
                {
                    double h=Height(along,across);
                    maxAlong=System.Math.Max(maxAlong,System.Math.Abs(Deg(Height(along+15,across)-Height(along-15,across),30)));
                    maxAcross=System.Math.Max(maxAcross,System.Math.Abs(Deg(Height(along,across+15)-Height(along,across-15),30)));
                    maxResidual=System.Math.Max(maxResidual,System.Math.Abs(h-(Height(along-15,across)+Height(along+15,across))*.5));
                    maxResidual=System.Math.Max(maxResidual,System.Math.Abs(h-(Height(along,across-15)+Height(along,across+15))*.5));
                }
                var culture=System.Globalization.CultureInfo.InvariantCulture;
                glacierReport.AppendLine(string.Format(culture,"{0},{1:F2},{2:F2},{3:F3},{4:F2},{5:F2},{6:F3},{7:F3},{8:F3},{9:F2},{10:F3},{11:F3},{12:F3}",
                    AlaskaBuilder.GlacierName(gi),cx,cy,terrain.HeightAt(cx,cy),lx,ly,Height(0,0),alongSlope,crossSlope,
                    (System.Math.Atan2(uy,ux)*180/System.Math.PI+360)%360,maxAlong,maxAcross,maxResidual));
            }
            File.WriteAllText(Path.Combine(outDir,"glacier-sim-handoff.csv"),glacierReport.ToString());
            Debug.Log("Glacier sim handoff (read-only terrain samples):\n"+glacierReport);
            var fall = terrain.Waterfalls[0];
            double top = terrain.RiverSurfaceAt(fall.LipY-1);
            double pool = terrain.RiverSurfaceAt(fall.LipY+FlyingGame.Core.WorldTerrain.RiverTableStepPublic+2);
            double drop = System.Math.Max(10,top-pool);
            double hit = fall.LipY+FlyingGame.Core.WorldTerrain.FallLipSpeedMs*System.Math.Sqrt(2*drop/9.81);
            for (int solve = 0; solve < 3; solve++)
            {
                pool = terrain.WaterSurfaceAt(fall.X,hit) ?? pool;
                drop = System.Math.Max(10,top-pool);
                hit = fall.LipY+FlyingGame.Core.WorldTerrain.FallLipSpeedMs*System.Math.Sqrt(2*drop/9.81);
            }
            double reachY = fall.LipY + 350;
            double reachX = FlyingGame.Core.WorldTerrain.RiverCentreX(reachY);
            double reachH = terrain.WaterSurfaceAt(reachX,reachY) ?? pool;
            Vector3 Outside(double x, double y, double h) => WorldBuilder.U(x,y,System.Math.Max(h,terrain.HeightAt(x,y)+35));
            var lake = FlyingGame.Core.WorldTerrain.SeaplaneBaseLake;
            double lakeY = (FlyingGame.Core.CanyonLake.Y0+FlyingGame.Core.CanyonLake.DamY)*.5;
            double lakeX = FlyingGame.Core.CanyonLake.CentreX(lakeY);
            double bridgeY = FlyingGame.Core.WorldTerrain.BridgeY;
            double bridgeX = FlyingGame.Core.WorldTerrain.RiverCentreX(bridgeY);
            var city = FlyingGame.Core.ValleyCity.At(0);
            var (padX,padY) = city.PadCentre;
            double padH = terrain.HeightAt(city.PadTower.Cx,city.PadTower.Cy)+FlyingGame.Core.FlyCity.PadDeckM;
            var valley = FlyingGame.Core.WorldTerrain.Airports[0];
            var summit = FlyingGame.Core.WorldTerrain.Airports[3];
            var (baseX,baseY) = FlyingGame.Core.WorldTerrain.BaseShore;
            double inland = FlyingGame.Core.WorldTerrain.BaseInland;
            double baseTop = FlyingGame.Core.WorldTerrain.BasePadHeightM;
            // Street cameras stand on the city road slab, whose elevation follows the grid centre.
            // Sampling only the old world camera coordinate placed both cameras underneath that slab.
            Vector3 Street(double u, double v)
            {
                var p = city.Map(u,v); var centre = city.Centre;
                double road = System.Math.Max(terrain.HeightAt(p.x,p.y),terrain.HeightAt(centre.x,centre.y)+.08);
                return WorldBuilder.U(p.x,p.y,road+2);
            }
            FlyingGame.Core.WorldTerrain.Strip FindStrip(string kind)
            {
                foreach(var strip in FlyingGame.Core.WorldTerrain.AirportStrips)
                    if(strip.Kind==kind) return strip;
                throw new System.InvalidOperationException("Missing airport strip "+kind);
            }
            Vector3 StripView(string kind, double along, double across, double height)
            {
                var strip=FindStrip(kind);
                double angle=strip.HeadingDeg*System.Math.PI/180;
                double x=valley.X+strip.Dx+along*System.Math.Cos(angle)-across*System.Math.Sin(angle);
                double y=valley.Y+strip.Dy+along*System.Math.Sin(angle)+across*System.Math.Cos(angle);
                return WorldBuilder.U(x,y,terrain.HeightAt(x,y)+height);
            }
            // Approach height is relative to the RUNWAY, never terrain beneath final.
            // The old terrain-relative camera sat below field level among the trees.
            var papiStrip=FindStrip("paved");
            Vector3 papiEye=Vector3.zero, papiLook=Vector3.zero;
            float bestPapiHeight=float.PositiveInfinity;
            int papiEnd=-1;
            foreach(int end in new[] {-1,1})
            {
                Vector3 eye=StripView("paved",end*(papiStrip.Length/2+1400),0,0);
                Vector3 aim=StripView("paved",end*(papiStrip.Length/2-300),0,0);
                aim.y=(float)valley.ElevationM+.85f;
                eye.y=aim.y+1700*Mathf.Tan(3*Mathf.Deg2Rad);
                Vector3 path=eye-aim;
                float span=new Vector2(path.x,path.z).magnitude;
                // Clear the whole runway/PAPI sight corridor, without hiding any trees.
                for(float distance=40;distance<span;distance+=12)
                {
                    float t=distance/span;
                    Vector3 point=aim+path*t;
                    for(int side=-1;side<=1;side++)
                    {
                        Vector3 sample=point+Vector3.Cross(Vector3.up,path.normalized)*side*65;
                        float groundTop=(float)terrain.HeightAt(sample.z,sample.x)+3;
                        eye.y=Mathf.Max(eye.y,aim.y+(groundTop-aim.y)/t);
                    }
                }
                foreach(var tree in FlyingGame.Core.Forest.InRect(
                    System.Math.Min(eye.z,aim.z)-90,System.Math.Min(eye.x,aim.x)-90,
                    System.Math.Max(eye.z,aim.z)+90,System.Math.Max(eye.x,aim.x)+90))
                {
                    Vector3 delta=WorldBuilder.U(tree.X,tree.Y,0)-new Vector3(aim.x,0,aim.z);
                    float along=(delta.x*path.x+delta.z*path.z)/(span*span);
                    float lateral=Mathf.Abs(delta.x*path.z-delta.z*path.x)/span;
                    if(along>.025f && along<=1 && lateral<65+tree.CrownRadiusM)
                        eye.y=Mathf.Max(eye.y,aim.y+((float)tree.TopM+4-aim.y)/along);
                }
                if(eye.y<bestPapiHeight) { bestPapiHeight=eye.y; papiEye=eye; papiLook=aim; papiEnd=end; }
            }
            Debug.Log($"PAPI plates: 1400 m beyond threshold, eye {papiEye}, target {papiLook}, glide {Mathf.Atan2(papiEye.y-papiLook.y,1700)*Mathf.Rad2Deg:F2} degrees (terrain/tree clearance included)");
            // Close inspection: exactly 400 m from the PAPI station (100 m before
            // threshold), on the extended centreline. Do not reuse the distant
            // obstacle-clearance height: that would cease to be a 3-degree plate.
            Vector3 papiCloseEye=StripView("paved",papiEnd*(papiStrip.Length/2+100),0,0);
            Vector3 papiCloseLook=StripView("paved",papiEnd*(papiStrip.Length/2-300),0,0);
            papiCloseLook.y=(float)valley.ElevationM+.85f; // PAPI optical centre
            papiCloseEye.y=papiCloseLook.y+400*Mathf.Tan(3*Mathf.Deg2Rad);
            Debug.Log($"PAPI close plates: 100 m before threshold, 400 m to boxes, 3.00 degrees, 27 degree lens; eye {papiCloseEye}, target {papiCloseLook}, box spacing 9 m");
            var serviceTower=city.Towers()[0];
            var cityRingRoofs=city.RooftopRings();
            foreach(var candidate in city.Towers())
            {
                if(candidate.HeightM<120 || candidate.HeightM>220 || (city.PadLot.HasValue && candidate.J==city.PadLot.Value.j)) continue;
                bool protectedService=false;
                foreach(var ring in cityRingRoofs) if(ring.t.I==candidate.I && ring.t.J==candidate.J) protectedService=true;
                if(protectedService) continue;
                serviceTower=candidate; break;
            }
            Vector3 serviceRoof=WorldBuilder.U(serviceTower.Cx,serviceTower.Cy,terrain.HeightAt(serviceTower.Cx,serviceTower.Cy)+serviceTower.HeightM);
            var tankTower=serviceTower;
            bool foundTank=false;
            foreach(var candidate in city.Towers())
            {
                int style=(candidate.I*3+candidate.J*7)%4;
                if(candidate.HeightM>190 && candidate.Style%2==0) style=0;
                int seed=candidate.I*53+candidate.J*19;
                if(style!=1 || seed%3==1 || candidate.HeightM>=330 || (city.PadLot.HasValue && candidate.J==city.PadLot.Value.j)) continue;
                bool protectedTank=false;
                foreach(var ring in cityRingRoofs) if(ring.t.I==candidate.I && ring.t.J==candidate.J) protectedTank=true;
                if(protectedTank) continue;
                tankTower=candidate; foundTank=true; break;
            }
            if(!foundTank) Debug.LogWarning("City water-tank detail: no eligible brick roof; inspect eligibility before acceptance");
            Vector3 tankRoof=WorldBuilder.U(tankTower.Cx,tankTower.Cy,terrain.HeightAt(tankTower.Cx,tankTower.Cy)+tankTower.HeightM);
            (string name, Vector3 pos, Vector3 look)[] views =
            {
                ("city-water-tower-detail", tankRoof+new Vector3(65,38,-75), tankRoof+Vector3.up*5),
                ("city-water-tower-night", tankRoof+new Vector3(65,38,-75), tankRoof+Vector3.up*5),
                ("city-rooftop-equipment", serviceRoof+new Vector3(-65,45,-75), serviceRoof),
                ("city-rooftop-equipment-night", serviceRoof+new Vector3(-65,45,-75), serviceRoof),
                ("city-traffic-detail", Street(425,485), Street(425,555)),
                ("city-traffic-motion", Street(425,485), Street(425,555)),
                ("city-traffic-night", Street(425,485), Street(425,555)),
                ("city-roofs-detail", Outside(city.PadTower.Cx-450,city.PadTower.Cy-550,padH+180), WorldBuilder.U(city.PadTower.Cx-220,city.PadTower.Cy-350,padH-70)),
                ("city-roofs-night", Outside(city.PadTower.Cx-450,city.PadTower.Cy-550,padH+180), WorldBuilder.U(city.PadTower.Cx-220,city.PadTower.Cy-350,padH-70)),
                ("city-street-furniture", Street(438,525), Street(438,620)),
                ("city-facade-detail", Outside(city.PadTower.Cx-260,city.PadTower.Cy-240,padH-50), WorldBuilder.U(city.PadTower.Cx,city.PadTower.Cy,padH-80)),
                ("eiffel-detail", Outside(FlyingGame.Core.Landmarks.TowerX-360,FlyingGame.Core.Landmarks.TowerYAt(0)-420,terrain.HeightAt(FlyingGame.Core.Landmarks.TowerX,FlyingGame.Core.Landmarks.TowerYAt(0))+180), WorldBuilder.U(FlyingGame.Core.Landmarks.TowerX,FlyingGame.Core.Landmarks.TowerYAt(0),terrain.HeightAt(FlyingGame.Core.Landmarks.TowerX,FlyingGame.Core.Landmarks.TowerYAt(0))+130)),
                ("arch-detail", Outside(FlyingGame.Core.Landmarks.ArchCentreXAt(0)+300,FlyingGame.Core.Landmarks.ArchYAt(0)+450,FlyingGame.Core.Landmarks.ArchBaseUp(terrain,0)+180), WorldBuilder.U(FlyingGame.Core.Landmarks.ArchCentreXAt(0),FlyingGame.Core.Landmarks.ArchYAt(0),FlyingGame.Core.Landmarks.ArchBaseUp(terrain,0)+100)),
                ("avalon-detail", Outside(FlyingGame.Core.Island.AvalonX-300,FlyingGame.Core.Island.AvalonY-650,280), WorldBuilder.U(FlyingGame.Core.Island.AvalonX,FlyingGame.Core.Island.AvalonY+250,50)),
                ("valley-pad-detail", Outside(padX-180,padY-180,padH+130), WorldBuilder.U(padX,padY,padH)),
                ("airport-beacon-green-night", Outside(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-225,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy-40,valley.ElevationM+32), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-140,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+105,valley.ElevationM+12.5)),
                ("airport-beacon-white-night", Outside(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-225,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy-40,valley.ElevationM+32), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-140,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+105,valley.ElevationM+12.5)),
                ("runway-papi-night", papiEye, papiLook),
                ("runway-papi-dusk", papiEye, papiLook),
                ("runway-papi-close-night", papiCloseEye, papiCloseLook),
                ("runway-papi-close-dusk", papiCloseEye, papiCloseLook),
                ("valley-airport-grass-control-dusk", Outside(valley.X-950,valley.Y-650,valley.ElevationM+380), WorldBuilder.U(valley.X-350,valley.Y+60,valley.ElevationM)),
                ("valley-airport-normal-control-night", Outside(valley.X-950,valley.Y-650,valley.ElevationM+380), WorldBuilder.U(valley.X-350,valley.Y+60,valley.ElevationM)),
                ("valley-airport-night", Outside(valley.X-950,valley.Y-650,valley.ElevationM+380), WorldBuilder.U(valley.X-350,valley.Y+60,valley.ElevationM)),
                ("valley-airport-dusk", Outside(valley.X-950,valley.Y-650,valley.ElevationM+380), WorldBuilder.U(valley.X-350,valley.Y+60,valley.ElevationM)),
                ("airport-signs-detail", WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx+93,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy-71,valley.ElevationM+1.8), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx+93,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy-85,valley.ElevationM+.7)),
                ("valley-airport-detail", Outside(valley.X-950,valley.Y-650,valley.ElevationM+600), WorldBuilder.U(valley.X-350,valley.Y+60,valley.ElevationM)),
                ("runway-threshold-detail", Outside(valley.X+FlyingGame.Core.WorldTerrain.MainRunwayDx-FlyingGame.Core.WorldTerrain.RunwayLengthM/2-180,valley.Y-90,valley.ElevationM+95), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.MainRunwayDx-FlyingGame.Core.WorldTerrain.RunwayLengthM/2+180,valley.Y,valley.ElevationM)),
                ("airport-hangars-detail", Outside(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-80,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+15,valley.ElevationM+18), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx+10,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+105,valley.ElevationM+4)),
                ("airport-fbo-detail", WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-100,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+50,valley.ElevationM+3), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-85,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+85,valley.ElevationM+2)),
                ("airport-apron-detail", Outside(valley.X-650,valley.Y+430,valley.ElevationM+130), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy,valley.ElevationM+3)),
                ("summit-airport-detail", Outside(summit.X-950,summit.Y-650,summit.ElevationM+600), WorldBuilder.U(summit.X-350,summit.Y+60,summit.ElevationM)),
                ("stol-strip-detail", StripView("gravel",-FindStrip("gravel").Length/2-85,-28,45), StripView("gravel",-30,0,0)),
                ("grass-strip-detail", StripView("grass",-FindStrip("grass").Length/2-85,-30,48), StripView("grass",-50,0,0)),
                ("seaplane-base-detail", Outside(baseX+220,baseY-inland*190,baseTop+180), WorldBuilder.U(baseX,baseY+inland*55,baseTop)),
                ("seaplane-dock-detail", Outside(baseX+90,baseY-inland*100,lake.SurfaceM+32), WorldBuilder.U(baseX+40,baseY-inland*30,lake.SurfaceM+.4)),
                ("waterfall-front-detail", Outside(fall.X+drop*.25,hit+drop*1.65,pool+drop*.65), WorldBuilder.U(fall.X,hit-15,pool+drop*.48)),
                ("waterfall-pool-detail", Outside(fall.X+140,hit+230,pool+95), WorldBuilder.U(fall.X,hit+12,pool+12)),
                ("river-flow-detail", Outside(reachX+120,reachY+180,reachH+150), WorldBuilder.U(reachX,reachY,reachH)),
                ("lake-shore-detail", Outside(lake.Cx+lake.Rx*.65,lake.Cy,lake.SurfaceM+35), WorldBuilder.U(lake.Cx+lake.Rx*.95,lake.Cy,lake.SurfaceM)),
                ("waterfall-slot-detail", WorldBuilder.U(fall.X,fall.LipY-25,pool+45), WorldBuilder.U(fall.X,hit+20,pool+drop*.35)),
                ("waterfall-lip-detail", Outside(fall.X,fall.LipY-100,top+95), WorldBuilder.U(fall.X,hit,pool+drop*.65)),
                ("powell-canyon-low", WorldBuilder.U(FlyingGame.Core.CanyonLake.CentreX(250)+35,250,FlyingGame.Core.CanyonLake.SurfaceM+14), WorldBuilder.U(FlyingGame.Core.CanyonLake.CentreX(-350),-350,FlyingGame.Core.CanyonLake.SurfaceM+28)),
                ("powell-canyon-low-shelf-control", WorldBuilder.U(FlyingGame.Core.CanyonLake.CentreX(250)+35,250,FlyingGame.Core.CanyonLake.SurfaceM+14), WorldBuilder.U(FlyingGame.Core.CanyonLake.CentreX(-350),-350,FlyingGame.Core.CanyonLake.SurfaceM+28)),
                ("powell-mesa-aerial", WorldBuilder.U(lakeX+220,lakeY-300,FlyingGame.Core.CanyonLake.SurfaceM+1500), WorldBuilder.U(lakeX,lakeY,FlyingGame.Core.CanyonLake.SurfaceM)),
                ("powell-ring-detail", WorldBuilder.U(lakeX,lakeY,FlyingGame.Core.CanyonLake.SurfaceM+15), WorldBuilder.U(lakeX+FlyingGame.Core.CanyonLake.HalfWidthAt(lakeY),lakeY,FlyingGame.Core.CanyonLake.SurfaceM+25)),
                ("river-lake-detail", Outside(lakeX+450,lakeY-500,FlyingGame.Core.CanyonLake.SurfaceM+550), WorldBuilder.U(lakeX,lakeY,FlyingGame.Core.CanyonLake.SurfaceM)),
                ("gorge-flight-detail", Outside(lakeX,lakeY-350,FlyingGame.Core.CanyonLake.SurfaceM+85), WorldBuilder.U(lakeX,lakeY+300,FlyingGame.Core.CanyonLake.SurfaceM+15)),
                ("bridge-detail", Outside(bridgeX+250,bridgeY+350,FlyingGame.Core.WorldTerrain.DatumM+200), WorldBuilder.U(bridgeX,bridgeY,FlyingGame.Core.CanyonLake.SurfaceM)),
                ("seaplane-lane-detail", Outside(lake.Cx-100,lake.Cy,lake.SurfaceM+55), WorldBuilder.U(lake.Cx+200,lake.Cy,lake.SurfaceM)),
                // Atmosphere inspection at two elevations; all original scenery plates remain.
                ("sky-cumulus-detail", new Vector3(2900f, 500f, -1200f), new Vector3(2000f, 1400f, -1700f)),
                ("sky-horizon-detail", new Vector3(2900f, 500f, -1200f), new Vector3(2000f, 650f, -1700f)),
                ("overview", new Vector3(9000f, 9000f, -9000f), new Vector3(-3500f, 800f, 1500f)),
                ("valley-city", new Vector3(2400f, 750f, 200f), new Vector3(900f, 350f, -1500f)),
                ("valley-city-night", new Vector3(2400f,750f,200f),new Vector3(900f,350f,-1500f)),
                ("town-night", new Vector3(1160f,650f,-3700f),new Vector3(1160f,400f,-1750f)),
                ("overview-night",new Vector3(9000f,9000f,-9000f),new Vector3(-3500f,800f,1500f)),
                ("city-street-night",Street(425,510),Street(425,900)),
                ("valley-pad", new Vector3(1700f, 650f, -3300f), new Vector3(900f, 500f, -2850f)),
                ("topdown", new Vector3(-1500f, 17000f, 900f), new Vector3(-1500f, 0f, 910f)),
                ("topdown-valley", new Vector3(900f, 7500f, 600f), new Vector3(900f, 0f, 610f)),
                ("gorge-flight", new Vector3(-400f, 420f, 1650f), new Vector3(900f, 200f, 1700f)),
                ("hills", new Vector3(2900f, 500f, -1200f), new Vector3(2000f, 300f, -1700f)),
                ("valley-city-street", Street(425,510), Street(425,900)),
                ("valley-airport", Outside(valley.X-1000,valley.Y+650,valley.ElevationM+430), WorldBuilder.U(valley.X-350,valley.Y+60,valley.ElevationM)),
                ("first-wall", new Vector3(2500f, 1500f, -2500f), new Vector3(-1800f, 450f, 1000f)),
                ("summit-airport", new Vector3(-7000f, 3500f, -1800f), new Vector3(-8900f, 2700f, 400f)),
                ("stol-strip", StripView("gravel",-380,12,55), StripView("gravel",50,0,0)),
                ("river-lake", new Vector3(3500f, 1400f, -3200f), new Vector3(2400f, 850f, 1500f)),
                ("race-course", new Vector3(1950f, 520f, 250f), new Vector3(1950f, 40f, 1200f)),
                ("race-gate", new Vector3(1800f, 45f, 650f), new Vector3(1800f, 40f, 860f)),
                // The stretched course (2026-10-06): from the south, and a gate number up close (sim gate 1 at (45, 1800)).
                ("race-course2", new Vector3(1925f, 750f, -500f), new Vector3(1925f, 300f, 650f)),
                ("race-number", new Vector3(1850f, 400f, -20f), new Vector3(1800f, 396f, 45f)),
                ("race-number2", new Vector3(1760f, 395f, 100f), new Vector3(1800f, 396f, 45f)),
                ("aero-box", new Vector3(1000f, 700f, -900f), new Vector3(1000f, 300f, 400f)),
                ("bridge", new Vector3(-150f, 120f, 2200f), new Vector3(-150f, 0f, 1900f)),
                ("crop-field", new Vector3(2050f, 90f, -300f), new Vector3(1750f, 25f, 400f)),
                ("crop-wires", new Vector3(1450f, 25f, 100f), new Vector3(1750f, 30f, 191f)),
                ("grass-strip", StripView("grass",-460,15,55), StripView("grass",20,0,0)),
                ("arch", new Vector3(-1250f, 120f, 1300f), new Vector3(-900f, 120f, 1740f)),
                ("eiffel", new Vector3(-600f, 120f, -2500f), new Vector3(-1000f, 120f, -1750f)),
                ("town", new Vector3(1160f, 400f, -3700f), new Vector3(1160f, 60f, -1750f)),
                ("town-street", Street(85,340), Street(85,700)),
                // Plunge waterfall at the first wall (sim x 1849, lip y −2002): front, from inside the slot, and the lip.
                ("waterfall-front", new Vector3(-1350f, 520f, 2350f), new Vector3(-1985f, 380f, 1849f)),
                ("waterfall-slot", new Vector3(-2030f, 350f, 1480f), new Vector3(-2030f, 330f, 2200f)),
                // The carrier, posed at t = 20 s (northbound, sim (−2415, 3700)): the approach from astern, its port side, from the shore.
                ("carrier-approach", new Vector3(3712f, 75f, -3150f), new Vector3(3695f, 18f, -2440f)),
                ("carrier-side", new Vector3(3330f, 70f, -2330f), new Vector3(3705f, 25f, -2415f)),
                ("carrier-deck", new Vector3(3712f, 40f, -2560f), new Vector3(3690f, 18f, -2350f)),
                ("carrier-shore", new Vector3(2700f, 450f, -3600f), new Vector3(3700f, 0f, -2415f)),
                ("waterfall-lip", new Vector3(-1900f, 880f, 1700f), new Vector3(-2010f, 780f, 1849f)),
                // The Valley lake's seaplane lane and base (owner 2026-10-07): Unity (x = sim y, z = sim x), lake surface 302.8 m.
                ("runway-south-final", Outside(valley.X+FlyingGame.Core.WorldTerrain.MainRunwayDx-FlyingGame.Core.WorldTerrain.RunwayLengthM/2-600,valley.Y,valley.ElevationM+75), WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.MainRunwayDx-FlyingGame.Core.WorldTerrain.RunwayLengthM/2+250,valley.Y,valley.ElevationM)),
                ("seaplane-base", new Vector3(-980f, 345f, -2700f), new Vector3(-720f, 303f, -2600f)),
                ("seaplane-ramp", new Vector3(-685f, 309f, -2545f), new Vector3(-770f, 305f, -2600f)),
                ("seaplane-lane", new Vector3(-450f, 318f, -3120f), new Vector3(-450f, 302f, -2300f)),
                // The Reno high desert on the Mesa plateau (owner 2026-10-16). Unity x = sim y, z = sim x, so
                // the block (sim x -6900..-3600, y -7450..-4550) lies along Unity z at Unity x around -6000.
                ("desert-range", new Vector3(-6000f, 2200f, -1000f), new Vector3(-6000f, 1500f, -5000f)),
                ("desert-redrock", new Vector3(-4600f, 2050f, -4700f), new Vector3(-5280f, 1646f, -5600f)),
                ("desert-redrock-final", new Vector3(-5280f, 1760f, -4700f), new Vector3(-5280f, 1646f, -5600f)),
                ("desert-sandpass", new Vector3(-6000f, 1700f, -3700f), new Vector3(-6600f, 1480f, -4700f)),
                ("desert-hungryridge", new Vector3(-4810f, 1420f, -3350f), new Vector3(-4810f, 1340f, -4150f)),
                ("desert-hungryridge-high", new Vector3(-4810f, 1950f, -3350f), new Vector3(-4810f, 1500f, -4150f)),
                ("desert-playa", new Vector3(-7250f, 1420f, -4900f), new Vector3(-7250f, 1222f, -6100f)),
                ("desert-ruts", new Vector3(-7250f, 1232f, -6530f), new Vector3(-7250f, 1226f, -5900f)),
                ("desert-topdown", new Vector3(-6000f, 6000f, -5250f), new Vector3(-6000f, 1400f, -5240f)),
                // The desert's north edge, where its floor used to hang 610 m over the Bench and stop dead.
                ("desert-northedge", new Vector3(-3000f, 1500f, -4500f), new Vector3(-4700f, 1250f, -4500f)),
                // The mountain fronts (owner 2026-10-16: risers as mountain fronts, not flat shelves). The
                // fronts run along sim x, so in Unity they run along z and you look at them down Unity x.
                // First front: a summit to the west, the widest spur to the east, a saddle, and the bay the
                // Valley aerodrome sits in — where the front is still the old cliff.
                ("front-first-summit", new Vector3(1200f, 1150f, -4000f), new Vector3(-1500f, 800f, -4000f)),
                ("front-first-spur", new Vector3(1500f, 1250f, 6500f), new Vector3(-1400f, 800f, 6500f)),
                ("front-first-pass", new Vector3(-300f, 800f, -3000f), new Vector3(-2400f, 920f, -3000f)),
                ("front-first-bay", new Vector3(1100f, 1250f, 400f), new Vector3(-1400f, 800f, 400f)),
                // Along the first front from the south-east, high: spurs, bays and saddles all at once.
                ("front-first-oblique", new Vector3(2600f, 2700f, 2200f), new Vector3(-1300f, 800f, -4000f)),
                ("front-first-topdown", new Vector3(-1100f, 5200f, 1500f), new Vector3(-1150f, 500f, 1500f)),
                // Second front (Bench to Mesa, 3,000 to 5,000 ft) and third (Mesa to Summit, to 7,000 ft).
                ("front-second", new Vector3(-1600f, 2100f, 6000f), new Vector3(-5200f, 1250f, 6000f)),
                ("front-third", new Vector3(-6300f, 2600f, -4000f), new Vector3(-8100f, 2000f, -4000f)),
                ("front-third-east", new Vector3(-5000f, 3400f, 6500f), new Vector3(-7900f, 2300f, 6500f)),
                // The Alaska range on the Mesa plateau (owner 2026-10-16: "I didn't see any screenshots of
                // white snow covered mountains"). Nobody had ever rendered it: there were no views for it.
                // The High One is at sim (4200, −5900), 5,405 m — 17,700 ft — and the snow line is at 2,500 m.
                ("alaska-massif", new Vector3(-1600f, 4300f, 4200f), new Vector3(-5900f, 3000f, 4200f)),
                ("alaska-denali", new Vector3(-3400f, 3800f, 4300f), new Vector3(-5900f, 4300f, 4200f)),
                ("alaska-ridge", new Vector3(-2600f, 4800f, 900f), new Vector3(-6100f, 3400f, 4400f)),
                ("alaska-glacier", GlacierView(0,.92,330,330), GlacierView(0,.40,0,0)),
                ("alaska-ruth", GlacierView(0,.98,0,180), GlacierView(0,.12,0,350)),
                ("alaska-glacier-lz", GlacierView(0,.98,0,32), GlacierView(0,.46,0,4)),
                ("alaska-colony", GlacierView(1,.95,230,250), GlacierView(1,.38,0,0)),
                ("alaska-east-fork", GlacierView(2,.95,200,230), GlacierView(2,.38,0,0)),
                ("alaska-lake", new Vector3(-4850f, 1620f, 5395f), new Vector3(-5700f, 1560f, 5350f)),
                ("alaska-braided", new Vector3(-1200f, 1700f, 5800f), new Vector3(-4100f, 950f, 5800f)),
                // The alpine range and its stair of lakes, on the Summit plateau. Cathedral is 3,357 m.
                ("alpine-range", new Vector3(-6200f, 5000f, 5900f), new Vector3(-10600f, 2700f, 5900f)),
                ("alpine-lakes", new Vector3(-7600f, 3000f, 3200f), new Vector3(-8900f, 2430f, 3000f)),
                ("alpine-outlet", new Vector3(-7400f, 3050f, 2200f), new Vector3(-8900f, 2440f, 3000f)),
                ("alpine-larch", new Vector3(-11900f, 3250f, 5500f), new Vector3(-13600f, 2500f, 6600f)),
                ("alpine-cirque", new Vector3(-10100f, 3400f, 4700f), new Vector3(-11300f, 2520f, 4500f)),
                // The Frank Church canyon and its strips, which had no views at all until the timber arrived
                // and made it worth looking at the approaches. (Sim (x, y, up) → Unity (y, up, x).)
                ("church-canyon", new Vector3(-12800f, 2000f, -5000f), new Vector3(-12600f, 1500f, 1000f)),
                ("church-johnson", new Vector3(-12891f, 2108f, -2769f), new Vector3(-12600f, 1958f, -1400f)),
                ("church-soldier", new Vector3(-13054f, 1948f, -1592f), new Vector3(-13900f, 1788f, -1900f)),
                ("church-indian", new Vector3(-13501f, 1576f, -1295f), new Vector3(-12830f, 1476f, -300f)),
                // The timber itself: the line across the third front, and a low run over the valley stands.
                ("forest-line", new Vector3(-7800f, 2600f, 1500f), new Vector3(-7800f, 2200f, -3000f)),
                ("forest-low", new Vector3(-6000f, 1660f, 0f), new Vector3(-5200f, 1555f, 2500f)),
            };
            // Moving scenery doesn't Update in an editor render: pose the carrier for its views.
            var carrier = Object.FindFirstObjectByType<CarrierRuntime>();
            if (carrier != null)
            {
                var (cx, cy, ch, _) = FlyingGame.Core.Carrier.PoseAt(20);
                carrier.transform.SetPositionAndRotation(WorldBuilder.U(cx, cy, 0), Quaternion.Euler(0f, (float)(ch * Mathf.Rad2Deg), 0f));
            }
            // Fixture-only exposure for shaders that bake their own daylight rather than using Unity lights.
            // Restore exact shared material colours for every day plate; no terrain/airport source changes.
            var nightTints = new System.Collections.Generic.List<(Material material,string property,Color colour)>();
            var seenMaterials = new System.Collections.Generic.HashSet<Material>();
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            foreach(var material in renderer.sharedMaterials)
            {
                if(material==null || !seenMaterials.Add(material) || material.shader.name=="FlyingGame/LitVC") continue;
                foreach(string property in new[] { "_Color", "_SkyColor" })
                    if(material.HasProperty(property)) nightTints.Add((material,property,material.GetColor(property)));
            }
            string onlyViews = System.Environment.GetEnvironmentVariable("WORLD_VIEWS");
            foreach ((string name, Vector3 pos, Vector3 look) in views)
            {
                if (!string.IsNullOrEmpty(onlyViews) && !onlyViews.Contains(name)) continue;
                cam.fieldOfView=name.StartsWith("runway-papi-close-")?27f:name.StartsWith("runway-papi-")?35f:50f;
                Shader.SetGlobalFloat("_AirportGrassPreviewOff",name=="valley-airport-grass-control-dusk"?1:0);
                Shader.SetGlobalFloat("_CityTrafficPreview",1);
                Shader.SetGlobalFloat("_CityTrafficTime",name=="city-traffic-motion"?12:0);
                bool night = name.EndsWith("-night");
                bool dusk = name.EndsWith("-dusk");
                bool urbanNight=night && (name.StartsWith("city-") || name.StartsWith("valley-city") || name=="town-night" || name=="overview-night");
                foreach(var tint in nightTints)
                    tint.material.SetColor(tint.property,night ? tint.colour*(urbanNight?new Color(.30f,.38f,.53f,1):new Color(.18f,.22f,.30f,1)) : tint.colour);
                RenderSettings.fog = false;
                cam.clearFlags = CameraClearFlags.Skybox;
                if (dusk)
                {
                    var sun = new GameObject("DuskSun").AddComponent<Light>();
                    sun.type = LightType.Directional;
                    SunsetLighting.Apply(sun, false);
                    Shader.SetGlobalFloat("_AirportDusk", .78f);
                }
                else { SunsetLighting.ApplyDay(); Shader.SetGlobalFloat("_AirportDusk", 0); }
                Shader.SetGlobalFloat("_CityNight",night?1:0);
                Shader.SetGlobalFloat("_UrbanNightFill",urbanNight?1:0);
                if(night)
                {
                    cam.clearFlags=CameraClearFlags.Skybox;
                    cam.backgroundColor=new Color(.012f,.018f,.032f);
                    RenderSettings.fog=true; RenderSettings.fogMode=FogMode.Exponential;
                    RenderSettings.fogDensity=urbanNight?.000055f:.00010f;
                    RenderSettings.fogColor=urbanNight?new Color(.067f,.079f,.105f):new Color(.055f,.055f,.065f);
                }
                cam.GetComponent<Skybox>().material = RenderSettings.skybox;
                // Editor captures do not advance Update. Freeze opposed beacon beams at
                // known phases; the game continues using _Time and AirportBeacon.Update.
                bool beaconPlate=name.StartsWith("airport-beacon-");
                Shader.SetGlobalFloat("_AirportBeaconPreview",beaconPlate?1:0);
                if(beaconPlate)
                {
                    Vector3 beacon=WorldBuilder.U(valley.X+FlyingGame.Core.WorldTerrain.ApronDx-140,valley.Y+FlyingGame.Core.WorldTerrain.ApronDy+105,valley.ElevationM+12.5);
                    Vector3 eye=pos-beacon;
                    float phase=name.Contains("white")?Mathf.PI:0;
                    Shader.SetGlobalFloat("_AirportBeaconTime",(Mathf.Atan2(eye.x,eye.z)+phase)/2.094395f);
                }
                cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
                // The forest is streamed round the camera in the game, and nothing streams in a batch render,
                // so build the timber round whatever this view is LOOKING AT and throw it away afterwards.
                // (Unity x is the sim's y and Unity z is the sim's x.)
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(FlyingGame.Core.WorldTerrain.Active, forest.transform, look.z, look.x, 2200, 700);
                // A/B control for Chief: original triangle-averaged normals, identical
                // camera/material/exposure. Restore immediately after this one capture.
                Mesh normalControl=null; Vector3[] restoredNormals=null;
                if(name=="valley-airport-normal-control-night")
                {
                    var terrainObject=GameObject.Find("Terrain");
                    if(terrainObject!=null)
                    {
                        normalControl=terrainObject.GetComponent<MeshFilter>().sharedMesh;
                        restoredNormals=normalControl.normals; normalControl.RecalculateNormals();
                    }
                }
                var shelfControl=new System.Collections.Generic.List<Renderer>();
                if(name=="powell-canyon-low-shelf-control")
                    foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                        if(renderer.enabled && renderer.name=="Shelf")
                        { shelfControl.Add(renderer); renderer.enabled=false; }
                try { cam.Render(); }
                finally
                {
                    if(normalControl!=null) normalControl.normals=restoredNormals;
                    foreach(var shelfRenderer in shelfControl) shelfRenderer.enabled=true;
                }
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                if(name=="alaska-colony" || name=="alaska-east-fork")
                    WriteGreenProbe(cam,tex,name,outDir);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(forest);
            }
            Shader.SetGlobalFloat("_AirportGrassPreviewOff",0);
            Shader.SetGlobalFloat("_AirportDusk", 0);
            Shader.SetGlobalFloat("_AirportBeaconPreview",0);
            Shader.SetGlobalFloat("_CityNight",0);
            Shader.SetGlobalFloat("_UrbanNightFill",0);
            Shader.SetGlobalFloat("_CityTrafficPreview",0);
            foreach(var tint in nightTints) tint.material.SetColor(tint.property,tint.colour);
            RenderSettings.fog=false;
            SunsetLighting.ApplyDay();
            RenderTexture.active = null;
            Debug.Log($"WorldRender: wrote {views.Length} images to {outDir}");
        }
        // M17: sample the actual rasterized terrain and shader terms, using the exact
        // named-shot camera and pixel centres. No guessed mountain bounds or sim edits.
        // Runs AFTER saving the normal PNG. Probe variant is never enabled in play.
        static void WriteGreenProbe(Camera cam,Texture2D plate,string shot,string outDir)
        {
            var culture=System.Globalization.CultureInfo.InvariantCulture;
            var report=new System.Text.StringBuilder();
            report.AppendLine("\nSHOT "+shot+"; pixels use top-left origin; Unity XYZ=(sim Y,height,sim X)");
            report.AppendLine("camera="+cam.transform.position.ToString("F6")+" rotation="+cam.transform.rotation.ToString("F6")
                +" fov="+cam.fieldOfView.ToString(culture)+" aspect="+cam.aspect.ToString(culture));
            foreach(string uniform in new[]{"_MountainAlpine","_MountainAlaska","_GlacierWidths"})
                report.AppendLine(uniform+"="+Shader.GetGlobalVector(uniform).ToString("F6"));
            for(int gi=0;gi<3;gi++) report.AppendLine("glacierAxis"+gi+"="+AlaskaBuilder.GlacierAxis(gi).ToString("F6"));
            report.AppendLine("Columns: pixelX,pixelY,plateRGB; modes are raw linear float4, NOT display colours.");
            string[] labels={"worldXYZ_valid","vertexRGBA","geologySnow_curvature_normalY_valid",
                "alpineDomain_alaska_alpineMeadow_mountain","snow_meadowFinish_canopy_forest",
                "glacier_skyView_corridorVegetation_glacialSetting","minCorridorDistance_colonySetting_greenToe_chroma",
                "meadowRGB","beforeGlacierRGB","afterGlacierRGB","beforeMeadowRGB","afterMeadowRGB",
                "afterCanopyAndToeRGB","corridorDistances012","beforeEarlyMeadowRGB","afterEarlyMeadowRGB",
                "earlyMeadowWeight_mountainRock_exposed_outsideAlpineCore"};
            var pixels=new System.Collections.Generic.List<Vector2Int>();
            // Cover every Chief rectangle, including the small upper-left Colony patch,
            // and neighbouring brown/snow controls. 20 px spacing prevents one missed edge.
            if(shot=="alaska-colony")
            {
                for(int y=220;y<=340;y+=20) for(int x=1180;x<=1380;x+=20) pixels.Add(new Vector2Int(x,y));
                for(int y=260;y<=300;y+=20) for(int x=780;x<=980;x+=20) pixels.Add(new Vector2Int(x,y));
                for(int y=60;y<=180;y+=20) for(int x=220;x<=260;x+=20) pixels.Add(new Vector2Int(x,y));
            }
            else
            {
                for(int y=260;y<=380;y+=20) for(int x=540;x<=820;x+=20) pixels.Add(new Vector2Int(x,y));
                for(int y=300;y<=340;y+=20) for(int x=140;x<=340;x+=20) pixels.Add(new Vector2Int(x,y));
            }
            pixels.Add(new Vector2Int(800,500)); pixels.Add(new Vector2Int(1000,700));
            var values=new Color[labels.Length,pixels.Count];
            var states=new System.Collections.Generic.List<(Renderer renderer,bool enabled,Material[] materials,bool terrain)>();
            var visibleWorld=new Color[pixels.Count];
            var clones=new System.Collections.Generic.List<Material>();
            RenderTexture savedTarget=cam.targetTexture, savedActive=RenderTexture.active;
            CameraClearFlags savedClear=cam.clearFlags; Color savedBackground=cam.backgroundColor;
            var target=new RenderTexture(plate.width,plate.height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            target.antiAliasing=1;
            var readback=new Texture2D(plate.width,plate.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if(!renderer.enabled) continue;
                    Material[] originals=renderer.sharedMaterials;
                    bool isTerrain=originals.Length>0;
                    foreach(var material in originals)
                        isTerrain &= material!=null && material.shader.name=="FlyingGame/Terrain";
                    states.Add((renderer,renderer.enabled,originals,isTerrain));
                    if(!isTerrain) continue;
                    var probeMaterials=new Material[originals.Length];
                    for(int mi=0;mi<originals.Length;mi++)
                    {
                        var copy=new Material(originals[mi]); copy.EnableKeyword("TERRAIN_GREEN_PROBE");
                        clones.Add(copy); probeMaterials[mi]=copy;
                    }
                    renderer.sharedMaterials=probeMaterials;
                }
                cam.targetTexture=target; cam.clearFlags=CameraClearFlags.SolidColor;
                cam.backgroundColor=new Color(-99999,-99999,-99999,-99999);
                // First keep all scenery visible: a different value here proves that
                // another renderer occludes the terrain at the named plate pixel.
                Shader.SetGlobalFloat("_TerrainGreenProbeMode",1);
                cam.Render(); RenderTexture.active=target;
                readback.ReadPixels(new Rect(0,0,plate.width,plate.height),0,0); readback.Apply();
                for(int pi=0;pi<pixels.Count;pi++)
                    visibleWorld[pi]=readback.GetPixel(pixels[pi].x,plate.height-1-pixels[pi].y);
                foreach(var state in states) if(!state.terrain) state.renderer.enabled=false;
                for(int mode=1;mode<=labels.Length;mode++)
                {
                    Shader.SetGlobalFloat("_TerrainGreenProbeMode",mode);
                    cam.Render(); RenderTexture.active=target;
                    readback.ReadPixels(new Rect(0,0,plate.width,plate.height),0,0); readback.Apply();
                    for(int pi=0;pi<pixels.Count;pi++)
                        values[mode-1,pi]=readback.GetPixel(pixels[pi].x,plate.height-1-pixels[pi].y);
                }
                for(int pi=0;pi<pixels.Count;pi++)
                {
                    Vector2Int pixel=pixels[pi]; Color colour=plate.GetPixel(pixel.x,plate.height-1-pixel.y);
                    report.AppendLine(string.Format(culture,"pixel {0},{1}; plateRGB={2:F6},{3:F6},{4:F6}",pixel.x,pixel.y,colour.r,colour.g,colour.b));
                    for(int mode=0;mode<labels.Length;mode++)
                    {
                        Color value=values[mode,pi];
                        report.AppendLine(string.Format(culture,"  {0}={1:F6},{2:F6},{3:F6},{4:F6}",labels[mode],value.r,value.g,value.b,value.a));
                    }
                    Color world=values[0,pi], visible=visibleWorld[pi];
                    float worldDelta=Mathf.Abs(world.r-visible.r)+Mathf.Abs(world.g-visible.g)+Mathf.Abs(world.b-visible.b);
                    report.AppendLine("  visibleSource="+(world.a>.5f && world.r>-90000 && worldDelta<.05f
                        ? "Terrain.shader" : "NON_TERRAIN_OR_NO_HIT: do not attribute underlying terrain to visible green"));
                    if(world.a>.5f && world.r>-90000)
                    {
                        double simHeight=FlyingGame.Core.WorldTerrain.Active.HeightAt(world.b,world.r);
                        report.AppendLine(string.Format(culture,"  simX={0:F3} simY={1:F3} renderedHeight={2:F3} simHeight={3:F3} meshMinusSim={4:F3}",
                            world.b,world.r,world.g,simHeight,world.g-simHeight));
                    }
                }
                File.AppendAllText(Path.Combine(outDir,"green-probe.txt"),report.ToString());
                Debug.Log("WorldRender green probe: "+shot+" "+pixels.Count+" pixels; green-probe.txt");
            }
            finally
            {
                Shader.SetGlobalFloat("_TerrainGreenProbeMode",0);
                foreach(var state in states)
                { state.renderer.sharedMaterials=state.materials; state.renderer.enabled=state.enabled; }
                cam.targetTexture=savedTarget; cam.clearFlags=savedClear; cam.backgroundColor=savedBackground;
                RenderTexture.active=savedActive;
                foreach(var material in clones) Object.DestroyImmediate(material);
                Object.DestroyImmediate(readback); target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
