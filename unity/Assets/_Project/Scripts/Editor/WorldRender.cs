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
            (string name, Vector3 pos, Vector3 look)[] views =
            {
                ("city-roofs-detail", Outside(city.PadTower.Cx-450,city.PadTower.Cy-550,padH+180), WorldBuilder.U(city.PadTower.Cx-220,city.PadTower.Cy-350,padH-70)),
                ("city-roofs-night", Outside(city.PadTower.Cx-450,city.PadTower.Cy-550,padH+180), WorldBuilder.U(city.PadTower.Cx-220,city.PadTower.Cy-350,padH-70)),
                ("city-street-furniture", Street(438,525), Street(438,620)),
                ("city-facade-detail", Outside(city.PadTower.Cx-260,city.PadTower.Cy-240,padH-50), WorldBuilder.U(city.PadTower.Cx,city.PadTower.Cy,padH-80)),
                ("eiffel-detail", Outside(FlyingGame.Core.Landmarks.TowerX-360,FlyingGame.Core.Landmarks.TowerYAt(0)-420,terrain.HeightAt(FlyingGame.Core.Landmarks.TowerX,FlyingGame.Core.Landmarks.TowerYAt(0))+180), WorldBuilder.U(FlyingGame.Core.Landmarks.TowerX,FlyingGame.Core.Landmarks.TowerYAt(0),terrain.HeightAt(FlyingGame.Core.Landmarks.TowerX,FlyingGame.Core.Landmarks.TowerYAt(0))+130)),
                ("arch-detail", Outside(FlyingGame.Core.Landmarks.ArchCentreXAt(0)+300,FlyingGame.Core.Landmarks.ArchYAt(0)+450,FlyingGame.Core.Landmarks.ArchBaseUp(terrain,0)+180), WorldBuilder.U(FlyingGame.Core.Landmarks.ArchCentreXAt(0),FlyingGame.Core.Landmarks.ArchYAt(0),FlyingGame.Core.Landmarks.ArchBaseUp(terrain,0)+100)),
                ("avalon-detail", Outside(FlyingGame.Core.Island.AvalonX-300,FlyingGame.Core.Island.AvalonY-650,280), WorldBuilder.U(FlyingGame.Core.Island.AvalonX,FlyingGame.Core.Island.AvalonY+250,50)),
                ("valley-pad-detail", Outside(padX-180,padY-180,padH+130), WorldBuilder.U(padX,padY,padH)),
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
                ("alaska-glacier", new Vector3(-4600f, 2050f, 5400f), new Vector3(-6900f, 2100f, 5050f)),
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
                bool night = name.EndsWith("-night");
                bool dusk = name.EndsWith("-dusk");
                foreach(var tint in nightTints)
                    tint.material.SetColor(tint.property,night ? tint.colour*new Color(.18f,.22f,.30f,1) : tint.colour);
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
                if(night)
                {
                    cam.clearFlags=CameraClearFlags.Skybox;
                    cam.backgroundColor=new Color(.012f,.018f,.032f);
                    RenderSettings.fog=true; RenderSettings.fogMode=FogMode.Exponential;
                    RenderSettings.fogDensity=.00010f;
                    RenderSettings.fogColor=new Color(.055f,.055f,.065f);
                }
                cam.GetComponent<Skybox>().material = RenderSettings.skybox;
                cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
                // The forest is streamed round the camera in the game, and nothing streams in a batch render,
                // so build the timber round whatever this view is LOOKING AT and throw it away afterwards.
                // (Unity x is the sim's y and Unity z is the sim's x.)
                var forest = new GameObject("ForestView");
                ForestBuilder.BuildAround(FlyingGame.Core.WorldTerrain.Active, forest.transform, look.z, look.x, 2200, 700);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(forest);
            }
            Shader.SetGlobalFloat("_AirportDusk", 0);
            Shader.SetGlobalFloat("_CityNight",0);
            foreach(var tint in nightTints) tint.material.SetColor(tint.property,tint.colour);
            RenderSettings.fog=false;
            SunsetLighting.ApplyDay();
            RenderTexture.active = null;
            Debug.Log($"WorldRender: wrote {views.Length} images to {outDir}");
        }
    }
}
