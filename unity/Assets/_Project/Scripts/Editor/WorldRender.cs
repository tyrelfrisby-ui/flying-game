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
            File.WriteAllText(Path.Combine(outDir,"green-probe.txt"),"M18 terrain source probe; ORIGINAL materials, normal PNGs saved before diagnostics.\n");
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
        /// <summary>
        /// Island terrain review plates (2026-11-02, lane/claude-island-terrain): ONE fixed camera list in sim
        /// km east/north and metres MSL, so the shipping island and the rebuilt one are shot from exactly the same
        /// places. Writes build/island-terrain/&lt;tag&gt;/*.png plus mesh-stats.txt (terrain triangles, vertices,
        /// renderers, build time). Editor only; nothing here is reachable from the player.
        ///   Unity -batchmode -quit -projectPath unity -executeMethod FlyingGame.EditorTools.WorldRender.RenderIslandTerrain -terrainTag before
        /// </summary>
        public static void RenderIslandTerrain()
        {
            var args = System.Environment.GetCommandLineArgs();
            int tagAt = System.Array.IndexOf(args, "-terrainTag");
            string tag = tagAt >= 0 && tagAt + 1 < args.Length ? args[tagAt + 1] : "after";
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/island-terrain", tag));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Shader.SetGlobalVector("_BubbleSunDir", new Vector3(0.4f, 0.8f, -0.4f).normalized);
            FlyingGame.Core.WorldTerrain.Active = new FlyingGame.Core.WorldTerrain();
            FlyingGame.Core.WorldClock.TimeS = 20;
            // The device's level of detail: iOS runs the Medium quality level (lodBias 0.7) and nothing changes it.
            float savedLodBias = QualitySettings.lodBias; QualitySettings.lodBias = .7f; // restored below: never saved
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var world = WorldBuilder.BuildAll();
            double buildS = clock.Elapsed.TotalSeconds;
            SunsetLighting.ApplyDay();
            RenderSettings.fog = false;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool training = false;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                    if (t.name == "CombatZone" || t.name.StartsWith("AeroBox")) training = true;
                if (training || (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "FlyingGame/Glass")) renderer.enabled = false;
            }
            // Terrain mesh statistics: every renderer whose object name starts "Terrain".
            long tris = 0, verts = 0, meshBytes = 0; int terrainRenderers = 0, lodRenderers = 0;
            var shaders = new System.Collections.Generic.HashSet<string>();
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("Terrain") || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh; terrainRenderers++;
                bool lod = mf.GetComponentInParent<LODGroup>() != null && mf.transform.parent != null && mf.name.Contains("LOD") && !mf.name.EndsWith("LOD0");
                if (lod) lodRenderers++; else { tris += mesh.triangles.Length / 3; verts += mesh.vertexCount; }
                meshBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
                var r = mf.GetComponent<Renderer>(); if (r != null && r.sharedMaterial != null) shaders.Add(r.sharedMaterial.shader.name);
            }
            File.WriteAllText(Path.Combine(outDir, "mesh-stats.txt"),
                $"tag={tag}\nworld build (WorldBuilder.BuildAll) {buildS:F2} s in the editor\n" +
                $"terrain renderers {terrainRenderers} (of which coarser-LOD renderers {lodRenderers})\n" +
                $"finest-level terrain triangles {tris}, vertices {verts}\nterrain mesh memory (all LODs, editor, CPU+GPU copies) {meshBytes / 1048576.0:F1} MiB\n" +
                $"terrain shaders: {string.Join(", ", shaders)}\n" + IslandTerrainStats());
            var camGo = new GameObject("RenderCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; camGo.AddComponent<Skybox>().material = RenderSettings.skybox;
            cam.depthTextureMode = DepthTextureMode.Depth; cam.nearClipPlane = 1f; cam.farClipPlane = 80000f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            Vector3 S(double e, double n, double up) => WorldBuilder.U(n * 1000, e * 1000, up);
            const double ft = .3048;
            (string name, Vector3 eye, Vector3 look, float fov)[] views =
            {
                ("01-overview-from-southeast", S(16, -16, 9000), S(0, 0, 800), 50),
                ("02-topdown", S(0, -.5, 42000), S(0, -.49, 0), 40),
                ("03-oblique-from-north", S(0, 22, 3000), S(0, 0, 900), 50),
                ("04-oblique-from-east", S(22, 0, 3000), S(0, 0, 900), 50),
                ("05-oblique-from-south", S(0, -22, 3000), S(0, 0, 900), 50),
                ("06-oblique-from-west", S(-24, 0, 3000), S(0, 0, 900), 50),
                ("07-f86-cruise-25000ft", S(-18, -14, 25000 * ft), S(0, 0, 0), 60),
                ("08-f86-25000ft-looking-down", S(10, -12, 25000 * ft), S(0, 0, 500), 60),
                ("09-low-level-1000ft-agl-PANC", S(-12.5, -5.5, 151 * ft + 1000 * ft), S(-5, -1, 700), 60),
                // On the KMYL RNAV 17 final (course 179), 2 km before the north threshold, 1,000 ft above the field.
                ("10-low-level-1000ft-agl-KMYL", S(-0.88, 5.71, 5024 * ft + 1000 * ft), S(-0.88, 2.78, 5024 * ft), 60),
                ("11-low-level-1000ft-agl-PHDH", S(8.5, 4.6, 14 * ft + 1000 * ft), S(2, 6.5, 250), 60),
                ("12-low-level-1000ft-agl-middle-fork", S(3.5, -2.2, 6000 * ft), S(3.5, 1.8, 4600 * ft), 60),
                // On the PANC ILS 16 final (course 165), 2 km before the north threshold, 1,000 ft above the field.
                ("31-PANC-ILS16-final-1000ft", S(-11.19, 0.46, 151 * ft + 1000 * ft), S(-10.24, -3.07, 151 * ft), 60),
                ("13-frank-church-north-face", S(0.2, 4.6, 2700), S(0, 0.2, 2100), 50),
                ("14-frank-church-south-face", S(0.2, -4.6, 2700), S(0, -0.2, 2100), 50),
                ("15-sierra-north-face", S(-1.6, 3.4, 2500), S(-1.6, -0.2, 1900), 50),
                ("16-sierra-south-face", S(-1.6, -4.0, 2500), S(-1.6, -0.4, 1900), 50),
                ("17-knik-ski-zone", S(-8.6, 1.6, 900), S(-6.2, 0.1, 335), 50),
                ("18-knik-valley-overview", S(-9.5, -3.5, 2000), S(-5, -0.5, 700), 50),
                ("19-ruth-ski-zone", S(-6.2, 6.6, 1100), S(-3.9, 4.7, 400), 50),
                ("20-ruth-valley-overview", S(-7.0, 8.0, 2400), S(-2.5, 5.2, 1000), 50),
                ("21-central-massif-cirques", S(2.8, 5.8, 3200), S(0.2, 1.5, 2000), 50),
                ("22-denali-like", S(3.2, 10.5, 1500), S(0.5, 7.1, 700), 50),
                ("23-drainage-middle-fork-valley", S(7.0, 3.2, 2300), S(3.0, 0.3, 1300), 50),
                ("24-treeline-payette-mccall", S(-3.8, 6.4, 1900), S(-1.0, 3.6, 1550), 50),
                ("25-beach-oc-B1", S(4.6, -10.6, 250), S(2.2, -8.4, 15), 50),
                ("26-west-coast-cliffs", S(-14.5, -2.5, 450), S(-11.2, -0.8, 100), 50),
                ("27-page-desert-mesas", S(8.5, -7.5, 2200), S(5, -4, 1250), 50),
                ("28-catalina", S(3.5, -19, 1300), S(3.5, -14, 300), 50),
                ("29-drake-hill", S(-4.9, -1.2, 1900), S(-4.9, -4.4, 1500), 50),
                ("30-north-coast-low", S(-3.5, 10.5, 500), S(-1.0, 6.5, 200), 50),
                // Real-topography areas (tools/island/topo): one view each, as a pilot meets it.
                ("32-sitka-coast-from-the-sound", S(-14.5, 2.0, 700), S(-8.0, 2.8, 250), 50),
                ("33-fairweather-from-the-sea", S(-0.8, 12.5, 1100), S(-0.6, 6.0, 800), 50),
                ("34-fairweather-icefield-and-outlet", S(-6.5, 8.5, 2300), S(-1.6, 5.2, 900), 50),
                ("35-grand-canyon-gorge", S(9.0, -2.0, 1750), S(3.5, -2.6, 1050), 50),
                ("36-dillingham-under-the-ridge", S(6.5, 8.6, 250), S(4.8, 4.4, 300), 50),
                ("37-knik-valley-to-the-glacier-face", S(-8.6, 0.4, 500), S(-4.2, -0.9, 150), 50),
                ("38-johnson-creek-strip-valley", S(1.4, -3.6, 1850), S(1.85, -0.3, 1500), 55),
            };
            // Straight-down plates of each area's template window (e0, e1, n0, n1 km), north up, from 40 km so
            // terrain parallax stays under 4%: the island half of each imagery-vs-render comparison plate.
            (string name, double e0, double e1, double n0, double n1)[] areas =
            {
                ("fairweather", -4.4, 1.6, 4.0, 7.9), ("knik", -7.6, -2.6, -2.4, 1.4), ("sitka", -11.6, -5.4, 0.7, 4.7),
                ("grandcanyon", 3.0, 8.4, -4.9, -0.2), ("dillingham", 2.8, 8.6, 2.4, 6.9), ("johnsoncreek", -0.2, 3.1, -2.7, 1.3),
            };
            var viewStats = new System.Text.StringBuilder();
            foreach (var (name, eye, look, fov) in views)
            {
                cam.fieldOfView = fov;
                cam.transform.position = eye; cam.transform.LookAt(look, Vector3.up);
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(FlyingGame.Core.WorldTerrain.Active, forest.transform, look.z, look.x, 2600, 800);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex); Object.DestroyImmediate(forest);
                viewStats.AppendLine($"{name}\t{TerrainDrawn(world, cam)}");
            }
            var plates = new System.Text.StringBuilder("area\te0\te1\tn0\tn1\theight_m\tfov_v_deg\twidth_px\theight_px\n");
            foreach (var (name, e0, e1, n0, n1) in areas)
            {
                const double up = 40000; double ce = (e0 + e1) / 2, cn = (n0 + n1) / 2;
                double spanKm = System.Math.Max(n1 - n0, (e1 - e0) * rt.height / (double)rt.width) * 1.04;
                cam.fieldOfView = (float)(2 * System.Math.Atan(spanKm * 500 / up) * 180 / System.Math.PI);
                cam.transform.position = S(ce, cn, up);
                cam.transform.rotation = Quaternion.LookRotation(S(ce, cn, 0) - S(ce, cn, up), (S(ce, cn + 1, 0) - S(ce, cn, 0)).normalized);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "plate-" + name + "-topdown.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                plates.AppendLine($"{name}\t{e0}\t{e1}\t{n0}\t{n1}\t{up}\t{cam.fieldOfView:F4}\t{rt.width}\t{rt.height}");
            }
            File.WriteAllText(Path.Combine(outDir, "plates.txt"), plates.ToString());
            RenderTexture.active = null;
            File.WriteAllText(Path.Combine(outDir, "view-stats.txt"), "view\tterrain draw calls / triangles drawn (frustum-culled; LOD as LODGroup selects it at lodBias " + QualitySettings.lodBias + ")\n" + viewStats);
            var cams = new System.Text.StringBuilder();
            foreach (var (name, eye, look, fov) in views) cams.AppendLine($"{name}\teye(unity)={eye}\tlook={look}\tfov={fov}");
            File.WriteAllText(Path.Combine(outDir, "cameras.txt"), cams.ToString());
            QualitySettings.lodBias = savedLodBias;
            Debug.Log($"RenderIslandTerrain: wrote {views.Length} plates to {outDir}");
        }

        /// <summary>Terrain renderers this camera would draw and their triangles: frustum test on bounds, and for
        /// a LODGroup the level its screen-relative height selects (size / (2 d tan(fov/2)) x lodBias).</summary>
        static string TerrainDrawn(GameObject world, Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam); long tris = 0; int calls = 0;
            float k = 2 * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad);
            foreach (var group in world.GetComponentsInChildren<LODGroup>())
            {
                if (!group.name.StartsWith("Terrain")) continue;
                var lods = group.GetLODs(); Vector3 c = group.transform.TransformPoint(group.localReferencePoint);
                float rel = group.size / (k * Mathf.Max(1, Vector3.Distance(cam.transform.position, c))) * QualitySettings.lodBias;
                int chosen = -1; for (int l = 0; l < lods.Length; l++) if (rel >= lods[l].screenRelativeTransitionHeight) { chosen = l; break; }
                if (chosen < 0) continue;
                foreach (var r in lods[chosen].renderers)
                    if (r != null && GeometryUtility.TestPlanesAABB(planes, r.bounds)) { calls++; tris += r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3; }
            }
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.name.StartsWith("Terrain") || mf.GetComponentInParent<LODGroup>() != null || mf.sharedMesh == null) continue;
                if (GeometryUtility.TestPlanesAABB(planes, mf.GetComponent<Renderer>().bounds)) { calls++; tris += mf.sharedMesh.triangles.Length / 3; }
            }
            return $"{calls} draws, {tris} triangles";
        }
        static string IslandTerrainStats()
        {
            var t = IslandTerrainMesh.Last;
            return $"IslandTerrainMesh: {t.Chunks} chunks, {t.Renderers} LOD renderers, LOD0 triangles {t.Lod0Triangles}, all-level triangles {t.AllTriangles}, vertices {t.Vertices}, est. GPU {t.GpuBytes / 1048576.0:F1} MiB, sampling {t.SampleS:F2} s, total {t.BuildS:F2} s\n";
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
                report.AppendLine("GLOBAL "+uniform+"="+Shader.GetGlobalVector(uniform).ToString("F6"));
            report.AppendLine("GLOBAL _DayAtmosphere="+Shader.GetGlobalVector("_DayAtmosphere").ToString("F6"));
            for(int gi=0;gi<3;gi++) report.AppendLine("glacierAxis"+gi+"="+AlaskaBuilder.GlacierAxis(gi).ToString("F6"));
            report.AppendLine("Columns: pixelX,pixelY,plateRGB; modes are raw linear float4, NOT display colours.");
            string[] labels={"worldXYZ_valid","vertexRGBA","geologySnow_curvature_normalY_valid",
                "alpineDomain_alaska_alpineMeadow_mountain","snow_meadowFinish_canopy_forest",
                "glacier_skyView_corridorVegetation_glacialSetting","minCorridorDistance_colonySetting_greenToe_chroma",
                "meadowRGB","beforeGlacierRGB","afterGlacierRGB","beforeMeadowRGB","afterMeadowRGB",
                "afterCanopyAndToeRGB","corridorDistances012","beforeEarlyMeadowRGB","afterEarlyMeadowRGB",
                "earlyMeadowWeight_mountainRock_exposed_outsideAlpineCore",
                "originalMeadowRGB","headwallMeadowWeight_southHeads_outsideCore_sourceChroma",
                "macroNormalXYZ_aspect","broad_treeLine_redrock_headwallMeadow"};
            var pixels=new System.Collections.Generic.List<Vector2Int>();
            // Cover every Chief rectangle, including the small upper-left Colony patch,
            // and neighbouring brown/snow controls. 20 px spacing prevents one missed edge.
            if(shot=="alaska-colony")
            {
                for(int y=220;y<=340;y+=20) for(int x=1180;x<=1380;x+=20) pixels.Add(new Vector2Int(x,y));
                for(int y=240;y<=300;y+=20) for(int x=780;x<=1220;x+=20) pixels.Add(new Vector2Int(x,y));
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
            var materialStates=new System.Collections.Generic.Dictionary<Material,(string[] keywords,float mode)>();
            float savedProbeMode=Shader.GetGlobalFloat("_TerrainGreenProbeMode");
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
                    // Non-Properties uniforms/arrays set with Material.SetVector are not
                    // reliably retained by the M17 Material copy. Probe the very same
                    // materials, preserving renderer property blocks and every uniform.
                    report.AppendLine("renderer="+renderer.name+" propertyBlock="+renderer.HasPropertyBlock());
                    foreach(var material in originals)
                    {
                        if(materialStates.ContainsKey(material)) continue;
                        materialStates.Add(material,(material.shaderKeywords,material.GetFloat("_TerrainGreenProbeMode")));
                        report.AppendLine("MATERIAL "+material.name+" keywords="+string.Join(",",material.shaderKeywords));
                        foreach(string uniform in new[]{"_MountainAlpine","_MountainAlaska","_GlacierWidths","_DayAtmosphere"})
                            report.AppendLine("  material.GetVector("+uniform+")="+material.GetVector(uniform).ToString("F6"));
                        var axes=material.GetVectorArray("_GlacierAxes");
                        if(axes==null) report.AppendLine("  material.GetVectorArray(_GlacierAxes)=null");
                        else for(int gi=0;gi<axes.Length;gi++) report.AppendLine("  material.GetVectorArray(_GlacierAxes)["+gi+"]="+axes[gi].ToString("F6"));
                        material.EnableKeyword("TERRAIN_GREEN_PROBE");
                    }
                }
                cam.targetTexture=target; cam.clearFlags=CameraClearFlags.SolidColor;
                cam.backgroundColor=new Color(-99999,-99999,-99999,-99999);
                // First keep all scenery visible: a different value here proves that
                // another renderer occludes the terrain at the named plate pixel.
                Shader.SetGlobalFloat("_TerrainGreenProbeMode",1);
                foreach(var entry in materialStates) entry.Key.SetFloat("_TerrainGreenProbeMode",1);
                cam.Render(); RenderTexture.active=target;
                readback.ReadPixels(new Rect(0,0,plate.width,plate.height),0,0); readback.Apply();
                for(int pi=0;pi<pixels.Count;pi++)
                    visibleWorld[pi]=readback.GetPixel(pixels[pi].x,plate.height-1-pixels[pi].y);
                foreach(var state in states) if(!state.terrain) state.renderer.enabled=false;
                for(int mode=1;mode<=labels.Length;mode++)
                {
                    Shader.SetGlobalFloat("_TerrainGreenProbeMode",mode);
                    foreach(var entry in materialStates) entry.Key.SetFloat("_TerrainGreenProbeMode",mode);
                    cam.Render(); RenderTexture.active=target;
                    readback.ReadPixels(new Rect(0,0,plate.width,plate.height),0,0); readback.Apply();
                    for(int pi=0;pi<pixels.Count;pi++)
                        values[mode-1,pi]=readback.GetPixel(pixels[pi].x,plate.height-1-pixels[pi].y);
                }
                int alaskaControls=0,snowControls=0;
                for(int pi=0;pi<pixels.Count;pi++)
                {
                    if(values[3,pi].g>.5f) alaskaControls++;
                    if(values[3,pi].g>.5f && values[4,pi].r>.01f) snowControls++;
                }
                report.AppendLine("PROBE VALIDATION: alaska>0.5 samples="+alaskaControls+"; Alaska snow>0.01 samples="+snowControls
                    +(alaskaControls>0 && snowControls>0 ? "; PASS" : "; FAIL: do not use zero biome rows as attribution"));
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
                Shader.SetGlobalFloat("_TerrainGreenProbeMode",savedProbeMode);
                foreach(var entry in materialStates)
                { entry.Key.shaderKeywords=entry.Value.keywords; entry.Key.SetFloat("_TerrainGreenProbeMode",entry.Value.mode); }
                foreach(var state in states)
                { state.renderer.sharedMaterials=state.materials; state.renderer.enabled=state.enabled; }
                cam.targetTexture=savedTarget; cam.clearFlags=savedClear; cam.backgroundColor=savedBackground;
                RenderTexture.active=savedActive;
                Object.DestroyImmediate(readback); target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
