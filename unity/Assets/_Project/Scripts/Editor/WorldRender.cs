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
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            FlyingGame.Core.WorldTerrain.Active = new FlyingGame.Core.WorldTerrain();
            FlyingGame.Core.WorldClock.TimeS = 20;
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
                ("island-overview", new Vector3(18000, 18000, -21000), new Vector3(0, 500, 0)),
                ("island-topdown", new Vector3(0, 44000, 0), new Vector3(0, 0, 1)),
                ("mokuleia", new Vector3(7000, 2200, 9000), new Vector3(5000, 200, 5500)),
                ("mccall-crags", new Vector3(-3500, 4200, 6000), new Vector3(-600, 1700, 2400)),
                ("alaska-ruth", new Vector3(-8500, 3500, 8000), new Vector3(-3500, 900, 4700)),
                ("carson-drake", new Vector3(-5500, 1900, -3000), new Vector3(-4900, 1450, -3900)),
                ("drake-interior", new Vector3(-4900, 1450, -3900), new Vector3(-4900, 1460, -4350)),
                ("powell", new Vector3(6500, 2300, -6500), new Vector3(4475, 1150, -4525)),
                ("oc-city", new Vector3(6200, 1500, -9200), new Vector3(4800, 300, -6600)),
                ("catalina", new Vector3(4500, 1700, -17500), new Vector3(3500, 300, -14000)),
                ("pirate-mouth", new Vector3(2500, 35, -14720), new Vector3(2750, 15, -14570)),
                ("pirate-interior", new Vector3(2790, 18, -14550), new Vector3(2850, 10, -14510)),
                ("sitka-volcano", new Vector3(-17000, 1900, -500), new Vector3(-14000, 400, 2800)),
                ("airpark-payette", new Vector3(-2200, 1800, 3900), new Vector3(-1600, 1500, 4600)),
                ("ab1-rafts", new Vector3(6800, 1000, 6600), new Vector3(6900, 0, 7600)),
                ("ab2-rafts", new Vector3(-13700, 1500, -7500), new Vector3(-12200, 0, -6300)),
                ("ab3-mccall", new Vector3(-3100, 2400, 1700), new Vector3(-1800, 1524, 2800)),
                ("cz5-land", new Vector3(16900, 500, 2000), new Vector3(17300, 30, 2400)),
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
    }
}
