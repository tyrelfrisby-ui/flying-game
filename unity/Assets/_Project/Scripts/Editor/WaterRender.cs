using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FlyingGame.Bridge;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    // Deterministic harbour tow fixture: prescribed pose, real FloatHydro samples and wave solver.
    // Does not claim to be a free-running takeoff or write any flight configuration.
    public static class WaterRender
    {
        static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, args);
        // Spawn is internal to the runtime assembly; keep fixture access out of its public API.
        static GameObject SpawnMesh(string name, Vector3[] vertices, int[] triangles, Material material, Transform parent) =>
            (GameObject)typeof(WorldBuilder).GetMethod("Spawn", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { name, vertices, triangles, material, parent, true });
        // A controlled visual stage, not replacement terrain or a new simulated lake.
        // Uses the real harbour's surface and real FloatHydro/wake signals.
        static WorldTerrain.Lake CaptureLake()
        {
            var lane = IslandSites.Fields.First(f => f.Surface == "water");
            double surface = WorldTerrain.Active.WaterSurfaceAt(lane.Centre.X, lane.Centre.Y) ?? lane.Elevation;
            return new WorldTerrain.Lake(lane.Centre.X, lane.Centre.Y, 600, 600, surface);
        }
        static void BuildSunsetShore()
        {
            var lake = CaptureLake();
            Vector3 origin = CoordinateMap.ToUnity(new Vec3(lake.Cx, lake.Cy, -lake.SurfaceM));
            var parent = new GameObject("Sunset comparison stage").transform;
            var water = WorldBuilder.Mat("FlyingGame/Water", new Color(.035f,.065f,.08f,1));
            SpawnMesh("Calm harbour", new[] { origin+new Vector3(-2400,0,-2000), origin+new Vector3(-2400,0,2400), origin+new Vector3(2400,0,2400), origin+new Vector3(2400,0,-2000) }, new[]{0,1,2,0,2,3}, water, parent);
            var bank = WorldBuilder.Mat("FlyingGame/LitVC",Color.white);
            var shoreColors = new System.Collections.Generic.List<Color>();
            var rng = new System.Random(743);
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            // Uneven, low far shore. Mesh bank and 3D conifer crowns all enter the same planar camera.
            int previousBank = -1;
            for (int i=0;i<180;i++)
            {
                float x=-1300+i*15;
                float z=340+55*Mathf.Sin(i*.08f)+27*Mathf.Sin(i*.31f);
                int n=verts.Count;
                verts.Add(origin+new Vector3(x,-.4f,z)); verts.Add(origin+new Vector3(x,1.2f+Mathf.Sin(i*.43f)*.8f,z+40));
                if(previousBank >= 0) tris.AddRange(new[]{previousBank,previousBank+1,n,n,previousBank+1,n+1});
                previousBank = n;
                shoreColors.Add(new Color(.045f,.055f,.048f));
                shoreColors.Add(new Color(.045f,.055f,.048f));
                for(int row=0;row<4;row++)
                {
                    float cluster=.5f+.5f*Mathf.Sin(i*.23f);
                    int treeStart = verts.Count;
                    float h=5+cluster*8+(float)rng.NextDouble()*17;
                    float depth=row*95+(float)rng.NextDouble()*65;
                    Vector3 at=origin+new Vector3(x+(float)rng.NextDouble()*25,.3f*Mathf.Sin(i*.67f+row),z+6+depth);
                    for(int crown=0;crown<9;crown++)
                    {
                        float bottom=h*(.08f+crown*.09f), top=bottom+h*.28f;
                        float radius=h*(.24f-crown*.022f)*( .8f+(float)rng.NextDouble()*.4f);
                        int v=verts.Count; verts.Add(at+Vector3.up*top);
                        for(int j=0;j<9;j++) { float a=j*Mathf.PI*2/9; verts.Add(at+new Vector3(Mathf.Cos(a)*radius*(.65f+(float)rng.NextDouble()*.5f),bottom+h*((float)rng.NextDouble()-.5f)*.045f,Mathf.Sin(a)*radius)); }
                        for(int j=0;j<9;j++) tris.AddRange(new[]{v,v+1+j,v+1+(j+1)%9});
                    }
                    Color layerColor = Color.Lerp(new Color(.035f,.047f,.040f),
                        new Color(.24f,.25f,.26f), row/3f*.62f);
                    for (int vertex=treeStart; vertex<verts.Count; vertex++) shoreColors.Add(layerColor);
                }
            }
            var shore = SpawnMesh("Conifer shoreline",verts.ToArray(),tris.ToArray(),bank,parent);
            shore.GetComponent<MeshFilter>().sharedMesh.colors = shoreColors.ToArray();
            // A distant softened ridge closes gaps between crowns without a ruler-flat opaque base.
            // One opaque mesh; no transparent fog cards or extra reflection target.
            verts.Clear(); tris.Clear();
            for(int i=0;i<180;i++)
            {
                float x=-1400+i*17;
                float crest=9+6*Mathf.Sin(i*.087f)+3*Mathf.Sin(i*.31f)+2*Mathf.Sin(i*1.73f);
                verts.Add(origin+new Vector3(x,-1,690));
                verts.Add(origin+new Vector3(x,crest,710));
                if(i>0) { int n=i*2; tris.AddRange(new[]{n-2,n-1,n,n,n-1,n+1}); }
            }
            var haze = WorldBuilder.Mat("FlyingGame/Lit",new Color(.35f,.32f,.29f));
            SpawnMesh("Distant shore haze",verts.ToArray(),tris.ToArray(),haze,parent);
        }

        static void WriteComparison(Texture2D render, string output, string filename = "reference-left-render-right.png")
        {
            string reference = Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/refs/target-look-sunset-floatplane.png"));
            if (!File.Exists(reference)) throw new FileNotFoundException("Sunset comparison reference missing",reference);
            var photo=new Texture2D(2,2,TextureFormat.RGB24,false);
            photo.LoadImage(File.ReadAllBytes(reference));
            // Preserve the reference exactly, including its UI; do not present a synthetic cleaned photo as evidence.
            const int width=750, height=1600;
            var board=new Texture2D(width*2,height,TextureFormat.RGB24,false);
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                float photoHeight = width * (float)photo.height / photo.width;
                float py = ((y-height*.5f)/photoHeight)+.5f;
                board.SetPixel(x,y,py>=0 && py<=1 ? photo.GetPixelBilinear((x+.5f)/width,py) : Color.black);
                // Letterbox the 1000:1600 capture into the right panel, preserving its aspect.
                float ry=((y-height*.5f)/(width*1.6f))+.5f;
                board.SetPixel(x+width,y,ry>=0 && ry<=1 ? render.GetPixelBilinear((x+.5f)/width,ry) : Color.black);
            }
            board.Apply(); File.WriteAllBytes(Path.Combine(output,filename),board.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(board); UnityEngine.Object.DestroyImmediate(photo);
        }

        public static void RenderFromArgs()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string Read(string flag, string fallback)
                {
                    int i = Array.IndexOf(args, flag);
                    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
                }
                string output = Path.GetFullPath(Read("-out", "build/renders/water"));
                bool sunset = Read("-preset", "day") == "sunset";
                bool calm = Array.IndexOf(args, "-calm") >= 0;
                string id = Read("-aircraft", "j3-cub-floats-like");
                float throttle = float.Parse(Read("-throttle", calm ? "0" : "1"), System.Globalization.CultureInfo.InvariantCulture);
                // Explicit tow speed wins; zero throttle alone represents slow displacement taxi.
                float speed = float.Parse(Read("-speed", calm ? "0" : throttle <= 0 ? "2.5" : "18"), System.Globalization.CultureInfo.InvariantCulture);
                bool displacement = speed < 4;
                bool touchdown = Array.IndexOf(args, "-touchdown") >= 0;
                double fixturePitch = double.Parse(Read("-pitch", displacement ? "0" : "3"), System.Globalization.CultureInfo.InvariantCulture);
                double fixtureImmersion = double.Parse(Read("-immersion", ".13"), System.Globalization.CultureInfo.InvariantCulture);
                Debug.Log($"WaterRender fixture: speed={speed} m/s throttle={throttle} displacement={displacement}");
                float lift = float.Parse(Read("-lift", "0"), System.Globalization.CultureInfo.InvariantCulture);
                Directory.CreateDirectory(output);
                string captureSource = Read("-sourceSha", "UNLABELLED");
                string captureLabel = $"PRESCRIBED TOW {speed:F4} m/s | {speed / .44704:F2} mph | {speed / .514444444:F2} kt | source {captureSource}";
                File.WriteAllText(Path.Combine(output, "capture.txt"), captureLabel + "\nStatic pose/tow fixture with real sampled hydro and stepped spray/wake. Not live flight; no measured ASI.\n");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                Shader.SetGlobalFloat("_SunsetOn", 0); Shader.SetGlobalFloat("_WaterCalm", calm ? 1 : 0);
                if (sunset) BuildSunsetShore(); else WorldBuilder.BuildAll();
                SunsetLighting.ConfigureDayAtmosphere(Read("-skyQuality", "Balanced"),
                    float.Parse(Read("-hazeDensity", "0.000052"), System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(Read("-hazeClearRange", "850"), System.Globalization.CultureInfo.InvariantCulture));
                RenderSettings.fog = false;
                RenderSettings.ambientLight = new Color(.65f, .7f, .78f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f));
                if (sunset) SunsetLighting.Apply(sun, calm);
                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                if (cfg.Floats == null) throw new ArgumentException("Water render requires a floatplane.");
                var lake = CaptureLake();
                var root = new GameObject("Water render " + id);
                var driver = root.AddComponent<FlightSimDriver>(); driver.enabled = false;
                var builder = new AirframeBuilder(); builder.Build(root.transform, cfg);
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = WaterReflection.AircraftLayer;
                var shadow = root.AddComponent<GroundShadow>(); Call(shadow, "Start");
                var spray = root.AddComponent<FloatSplash>(); spray.Driver = driver;
                var wake = root.AddComponent<WakeWaves>(); wake.Driver = driver; Call(wake, "Start");
                Call(wake, "AllocateLake", lake);
                var lane = IslandSites.Fields.First(f => f.Surface == "water");
                double heading = lane.Heading * Math.PI / 180;
                var yaw = new Quat(0,0,Math.Sin(heading/2),Math.Cos(heading/2));
                var cameraYaw = Quaternion.Euler(0, (float)lane.Heading, 0);
                for (int frame = 0; frame < 960; frame++)
                {
                    double travel = (frame - 959) * speed / 60;
                    double north = lake.Cx + travel * Math.Cos(heading), east = lake.Cy + travel * Math.Sin(heading);
                    var attitude = new Quat(0, Math.Sin(fixturePitch * Math.PI / 360), 0, Math.Cos(fixturePitch * Math.PI / 360));
                    attitude = Quat.Multiply(yaw, attitude);
                    // Optional prescribed 0.8 m/s arrival, not a free-running flight or a physics change.
                    double aboveContact = touchdown ? Math.Min(.8, (959 - frame) * .8 / 60) : 0;
                    Vec3 velocity = touchdown
                        ? attitude.Conjugate().Rotate(new Vec3(speed * Math.Cos(heading), speed * Math.Sin(heading), .8))
                        : attitude.Conjugate().Rotate(new Vec3(speed * Math.Cos(heading), speed * Math.Sin(heading), 0));
                    var state = new RigidBodyState(new Vec3(north, east, -lake.SurfaceM - cfg.Floats.KeelZ + fixtureImmersion - lift - aboveContact), attitude, velocity, Vec3.Zero);
                    var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
                    ac.SetReplayPose(state, ControlDeflections.Neutral, throttle, 0, 1, 1);
                    driver.AdoptSim(new SimLoop(ac));
                    FloatHydro.Compute(cfg, state, 0);
                    Call(spray, "Step", 1f / 60f);
                    var particles = new System.Collections.Generic.List<ParticleSystem>();
                    foreach (var field in typeof(FloatSplash).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                        if (field.FieldType == typeof(ParticleSystem[])) particles.AddRange((ParticleSystem[])field.GetValue(spray));
                    for (int i = 0; i < particles.Count; i++)
                    {
                        var ps = particles[i];
                        if (frame == 0) { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(1234 + i); ps.Play(); }
                        ps.Simulate(1f / 60, false, false, false);
                    }
                    if (frame % 3 == 0) Call(wake, "Step", (int)Call(wake, "Sources", ac));
                }
                if (!FloatHydro.Floats[0].Wet && lift <= 0) throw new Exception("Water fixture is dry; cannot certify a water capture.");
                Debug.Log($"WaterRender: wet={FloatHydro.Floats[0].Wet}, lift={FloatHydro.Floats[0].PlaningLiftN}, buoyancy={FloatHydro.Floats[0].BuoyancyN}, drag={FloatHydro.Floats[0].DragN}");
                var hydroReport = new System.Text.StringBuilder();
                bool allOnStep = true, allPlow = true;
                for (int i = 0; i < 2; i++)
                {
                    var f = FloatHydro.Floats[i];
                    bool step = FloatSprayVisual.IsOnStep(f);
                    bool plow = f.Wet && !step && f.SternDraftM >= .005;
                    allOnStep &= step; allPlow &= plow;
                    hydroReport.AppendLine($"float={i} wet={f.Wet} ON_STEP={step} heelDraggingPlow={plow} stepBlend={FloatSprayVisual.StepFraction(f):F4} sampledWaterRelativeSpeed={f.SpeedMs:F4}m/s ({f.SpeedMs/.44704:F2}mph, {f.SpeedMs/.514444444:F2}kt) lift={f.PlaningLiftN:F3}N buoyancy={f.BuoyancyN:F3}N ventilation={f.VentilatedFraction:F4} stepDraft={f.StepDraftM:F4}m sternDraft={f.SternDraftM:F4}m trim={f.TrimDeg:F3}deg lambda={f.Lambda:F3}");
                }
                string actualRegime = allOnStep ? "ON_STEP" : allPlow ? "PLOW" : "TRANSITION_OR_MIXED";
                string requiredRegime = Read("-requireRegime", "");
                if (requiredRegime.Length > 0 && requiredRegime != actualRegime)
                    throw new Exception($"Fixture regime {actualRegime}, expected {requiredRegime}: {hydroReport}");
                string regimeLabel = $"Verified regime={actualRegime}; fixture pitch={fixturePitch}deg immersionOffset={fixtureImmersion}m. Sampled hydro speed below comes from prescribed state, NOT live measured ASI.";
                File.WriteAllText(Path.Combine(output, "hydro-regime.txt"), regimeLabel + "\n" + hydroReport);
                File.AppendAllText(Path.Combine(output, "capture.txt"), regimeLabel + "\n" + hydroReport);
                Debug.Log(regimeLabel + "\n" + hydroReport);
                if (Array.IndexOf(args, "-requireFloatVs") >= 0)
                {
                    object GetSprayField(string name) => typeof(FloatSplash).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(spray);
                    var field = (SprayField)GetSprayField("_field");
                    var selected = new int[FloatSprayVisual.MaxArms];
                    FloatSprayVisual.SelectRoots(field.LastRoots, driver.Sim.Aircraft.State.Attitude, selected);
                    var mesh = (Mesh)GetSprayField("_sheets");
                    var vertices = mesh.vertices;
                    var report = new System.Text.StringBuilder();
                    for (int arm = 0; arm < 4; arm++)
                    {
                        if (selected[arm] < 0) throw new Exception($"Float V missing arm {arm}");
                        var contact = field.LastRoots[selected[arm]];
                        int vertex = arm * FloatSprayVisual.Samples * 2;
                        Vector3 origin = (vertices[vertex] + vertices[vertex + 1]) * .5f;
                        if (Vector3.Distance(origin, CoordinateMap.ToUnity(contact.Position)) > .002f)
                            throw new Exception($"Float V arm {arm} detached from its contact root");
                        report.AppendLine($"arm={arm} float={contact.Slot} contact={contact.Position} meshOrigin={origin}");
                    }
                    var ps = (ParticleSystem)GetSprayField("_spray");
                    var drops = new ParticleSystem.Particle[SprayField.MaxParcels];
                    int count = ps.GetParticles(drops);
                    if (count == 0) throw new Exception("No airborne spray in review fixture");
                    for (int i = 0; i < count; i++)
                    {
                        Color c = drops[i].startColor;
                        if (c.r < .99f || c.r != c.g || c.g != c.b) throw new Exception("Spray particle is not neutral white");
                    }
                    if (Array.IndexOf(args, "-requireFineSpray") >= 0)
                    {
                        if (ps.GetComponent<ParticleSystemRenderer>().renderMode != ParticleSystemRenderMode.Billboard)
                            throw new Exception("Airborne spray still stretches clouds into large droplets");
                        Vec3 forward = driver.Sim.Aircraft.State.Attitude.Rotate(new Vec3(1, 0, 0));
                        for (int i = 0; i < count; i++)
                        {
                            if (drops[i].startSize > .025f) throw new Exception("Oversized airborne spray footprint");
                            Vec3 point = CoordinateMap.ToSim(drops[i].position);
                            bool inside = actualRegime != "ON_STEP";
                            for (int f = 0; f < 2; f++)
                                inside |= FloatSprayVisual.InsideWake(point, FloatHydro.Floats[f].StepWorld, forward, cfg.Floats.BeamM, .011);
                            if (!inside) throw new Exception("Airborne spray outside both float wake envelopes");
                        }
                        report.AppendLine("Fine-spray bounds PASS: no velocity stretch, <=25 mm footprints, step parcels inside float wake envelopes (plow intentionally broad). Appearance still requires visual review.");
                    }
                    var material = (Material)GetSprayField("_mat");
                    if (material.color != Color.white) throw new Exception("Spray material tints white droplets");
                    foreach (var prop in (ParticleSystem[])GetSprayField("_prop"))
                        if (prop.emission.rateOverTime.constant > 0) throw new Exception("Central prop mist masks active float spray");
                    report.AppendLine($"PASS: four independent contact arms; {count} neutral-white particles; white material; central mist emission zero.");
                    File.WriteAllText(Path.Combine(output, "geometry-check.txt"), report.ToString());
                    Debug.Log(report.ToString());
                }
                // Publish the final ping-pong texture, as Update does in play mode.
                Shader.SetGlobalTexture("_WakeTex", (RenderTexture)typeof(WakeWaves).GetField("_curr", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(wake));
                var foamField = typeof(WakeWaves).GetField("_foam", BindingFlags.NonPublic | BindingFlags.Instance);
                Shader.SetGlobalTexture("_WakeFoamTex", foamField != null ? (Texture)foamField.GetValue(wake) : Texture2D.blackTexture);
                builder.SpinProps(_ => (float)driver.Sim.Aircraft.EngineRpm, .02f);
                var camera = new GameObject("Water camera").AddComponent<Camera>();
                var reflection = camera.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; Call(reflection, "Awake");
                // -reflectionQuality now names a tier on the ONE scale (Minimum/Low/Balanced/High), not the
                // reflection's own retired enum. An explicit -reflectionSize still wins, as it always did.
                var tier = (FlyingGame.Core.GraphicsQuality.Tier)Enum.Parse(
                    typeof(FlyingGame.Core.GraphicsQuality.Tier), Read("-reflectionQuality", "High"), true);
                var knobs = FlyingGame.Core.GraphicsQuality.Knobs.For(tier);
                reflection.ReflectScenery = knobs.ReflectScenery && Read("-reflectionScenery", "on") != "off";
                int sizeArg = int.Parse(Read("-reflectionSize", "0"));
                reflection.TextureSize = sizeArg > 0 ? sizeArg : knobs.ReflectionTextureSize;
                camera.clearFlags = CameraClearFlags.Skybox; camera.backgroundColor = new Color(.45f, .66f, .95f);
                camera.nearClipPlane = .15f; camera.farClipPlane = 12000; camera.fieldOfView = 48;
                var rt = new RenderTexture(1600, 1000, 24); camera.targetTexture = rt;
                Vector3 at = root.transform.position;
                var offsets = new[] { new Vector3(0, 5, -22), new Vector3(-12, 7, -24), new Vector3(7, 2, 6) };
                if (displacement && !calm)
                {
                    offsets[0] = new Vector3(0, 5, -22);
                    offsets[1] = new Vector3(-12, 7, -24);
                    offsets[2] = new Vector3(7, 2, 6);
                }
                var names = new[] { "tail", "chase", "bow" };
                if (Array.IndexOf(args, "-sprayReview") >= 0)
                {
                    offsets = new[] { new Vector3(0, 5, -14), new Vector3(0, 9, -3), new Vector3(5, 1.5f, 5) };
                    names = new[] { "water-run-tail", "dual-float-top", "spray-close" };
                }
                for (int i = 0; i < offsets.Length; i++)
                {
                    camera.transform.position = at + cameraYaw * offsets[i]; camera.transform.LookAt(at + new Vector3(0, -1, 0));
                    Call(shadow, "LateUpdate");
                    Call(reflection, "OnPreCull");
                    camera.Render(); RenderTexture.active = rt;
                    var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(output, names[i] + ".png"), tex.EncodeToPNG());
                    if (i == 0)
                    {
                        var reflectionTarget = (RenderTexture)typeof(WaterReflection).GetField("_rt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reflection);
                        RenderTexture.active = reflectionTarget;
                        var source = new Texture2D(reflectionTarget.width, reflectionTarget.height, TextureFormat.RGBA32, false);
                        source.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); source.Apply();
                        File.WriteAllBytes(Path.Combine(output, "reflection-source.png"), source.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(source);
                        var mirrorCamera = (Camera)typeof(WaterReflection).GetField("_refl", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reflection);
                        File.WriteAllText(Path.Combine(output, "reflection-diagnostics.txt"),
                            $"Fixture surface={lake.SurfaceM}; reflected surface={Shader.GetGlobalFloat("_ReflPlaneY")}; harbor={WorldTerrain.Harbor.SurfaceM}\n" +
                            $"Aircraft Unity={root.transform.position}; sim={CoordinateMap.ToSim(root.transform.position)}\n" +
                            $"Main eye={camera.transform.position}; mirrored eye={mirrorCamera.transform.position}\n" +
                            $"View determinant={mirrorCamera.worldToCameraMatrix.determinant}; near={mirrorCamera.nearClipPlane}; far={mirrorCamera.farClipPlane}\n" +
                            $"Projection (finite, no oblique clip):\n{mirrorCamera.projectionMatrix}\nView:\n{mirrorCamera.worldToCameraMatrix}\n");
                        // Same pose, same water: isolate the scenery contribution in every run.
                        bool scenery = reflection.ReflectScenery;
                        reflection.ReflectScenery = false;
                        Call(reflection, "OnPreCull"); camera.Render(); RenderTexture.active = rt;
                        var isolated = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                        isolated.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); isolated.Apply();
                        File.WriteAllBytes(Path.Combine(output, "reflection-scenery-off.png"), isolated.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(isolated);
                        reflection.ReflectScenery = scenery;
                        Call(reflection, "OnPreCull");
                        RenderTexture.active = rt;
                    }
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                if (sunset)
                {
                    // Rear view: whole aircraft, floats and their complete reflection, looking into low sun.
                    var portrait = new RenderTexture(1000, 1600, 24);
                    camera.targetTexture = portrait; camera.aspect = 1000f / 1600; camera.fieldOfView = 68;
                    var aircraftBounds = new Bounds(at, Vector3.zero);
                    foreach (var r in root.GetComponentsInChildren<Renderer>()) aircraftBounds.Encapsulate(r.bounds);
                    float distance = Mathf.Max(10, aircraftBounds.size.x / (2 * Mathf.Tan(34 * Mathf.Deg2Rad) * camera.aspect * .90f));
                    camera.transform.position = new Vector3(at.x - distance * .30f, (float)lake.SurfaceM + .85f, at.z - distance);
                    camera.transform.LookAt(new Vector3(at.x, (float)lake.SurfaceM + .55f, at.z));
                    Call(reflection, "OnPreCull"); camera.Render(); RenderTexture.active = portrait;
                    var photo = new Texture2D(1000, 1600, TextureFormat.RGB24, false);
                    photo.ReadPixels(new Rect(0,0,1000,1600),0,0); photo.Apply();
                    File.WriteAllBytes(Path.Combine(output,"sunset.png"),photo.EncodeToPNG());
                    WriteComparison(photo, output);
                    UnityEngine.Object.DestroyImmediate(photo);
                    // Low rear-quarter photo pose. Fit only airframe mesh renderers:
                    // particle bounds can otherwise force the camera far out of the scene.
                    var heroBounds = new Bounds(at, Vector3.zero);
                    var heroPoints = new System.Collections.Generic.List<Vector3>();
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                        heroBounds.Encapsulate(renderer.bounds);
                        // Fit the actual silhouette, not the empty corners of a large
                        // wing-to-tail bounding box. Editor-only; no runtime mesh readback.
                        foreach (var vertex in filter.sharedMesh.vertices)
                            heroPoints.Add(filter.transform.TransformPoint(vertex));
                    }
                    // A wider lens allows the float tails to sit closer to the low camera.
                    // Retain full silhouette/mirror fitting; increase lateral baseline
                    // to reveal the near float side instead of an almost axial rear view.
                    float span = Mathf.Max(1, heroBounds.size.x);
                    float waterY = (float)lake.SurfaceM;
                    Vector3 aim = new Vector3(heroBounds.center.x,
                        waterY + .65f, heroBounds.center.z);
                    camera.fieldOfView = 78;
                    float minimumAft = span * .35f;
                    float lowerAft = minimumAft, upperAft = span * 4;
                    Vector2 envelopeMin = Vector2.zero, envelopeMax = Vector2.one;
                    // Bisection is deterministic and avoids the 4.5% fit-step slack.
                    for (int fit = 0; fit < 24; fit++)
                    {
                        float aft = fit == 23 ? upperAft : (lowerAft + upperAft) * .5f;
                        camera.ResetProjectionMatrix();
                        float rearDistance = heroBounds.center.z - heroBounds.min.z + aft;
                        camera.transform.position = new Vector3(heroBounds.center.x - rearDistance * Mathf.Tan(25 * Mathf.Deg2Rad),
                            waterY + .65f, heroBounds.min.z - aft);
                        camera.transform.LookAt(aim);
                        envelopeMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                        envelopeMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                        bool inFront = true;
                        foreach (var silhouettePoint in heroPoints)
                        {
                            Vector3 point = silhouettePoint;
                            for (int mirror = 0; mirror < 2; mirror++)
                            {
                                Vector3 projected = camera.WorldToViewportPoint(point);
                                inFront &= projected.z > .2f;
                                envelopeMin = Vector2.Min(envelopeMin, new Vector2(projected.x, projected.y));
                                envelopeMax = Vector2.Max(envelopeMax, new Vector2(projected.x, projected.y));
                                point.y = 2 * waterY - point.y;
                            }
                        }
                        Vector2 extent = envelopeMax - envelopeMin;
                        if (inFront && extent.x <= .94f && envelopeMin.y >= -.02f && envelopeMax.y <= .92f) upperAft = aft;
                        else lowerAft = aft;
                    }
                    // Centre the silhouette horizontally; retain a fixed horizon vertically.
                    Vector2 envelopeCenter = (envelopeMin + envelopeMax) * .5f;
                    Matrix4x4 heroProjection = camera.projectionMatrix;
                    heroProjection.m02 += 2 * (envelopeCenter.x - .5f);
                    // Horizontal optical axis and fixed shift put the horizon 45% down.
                    heroProjection.m12 = -.10f;
                    camera.projectionMatrix = heroProjection;
                    File.WriteAllText(Path.Combine(output, "hero-diagnostics.txt"),
                        $"Bounds={heroBounds}; span={span}; camera={camera.transform.position}; aim={aim}\n" +
                        $"Tail aft={upperAft}; nominal aft={minimumAft}; yaw=25deg aft-left; height=.65; FOV={camera.fieldOfView}\n" +
                        $"Unshifted envelope={envelopeMin}..{envelopeMax}; projection shift={heroProjection.m02},{heroProjection.m12}\n");
                    Call(reflection, "OnPreCull"); camera.Render(); RenderTexture.active = portrait;
                    var hero = new Texture2D(1000, 1600, TextureFormat.RGB24, false);
                    hero.ReadPixels(new Rect(0,0,1000,1600),0,0); hero.Apply();
                    File.WriteAllBytes(Path.Combine(output,"sunset-hero.png"),hero.EncodeToPNG());
                    WriteComparison(hero, output, "reference-left-hero-right.png");
                    UnityEngine.Object.DestroyImmediate(hero);
                    camera.targetTexture = null; RenderTexture.active = null;
                    portrait.Release(); UnityEngine.Object.DestroyImmediate(portrait);
                }
                camera.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                RenderTexture.active = null;
                foreach (string name in new[] { "FlyingGame/Water", "FlyingGame/Spray", "Hidden/FlyingGame/WaveSim", "Hidden/FlyingGame/WakeFoam", "FlyingGame/SunsetSky", "FlyingGame/Airframe", "FlyingGame/Lit", "FlyingGame/LitVC", "FlyingGame/Terrain" })
                {
                    if (name == "Hidden/FlyingGame/WakeFoam" && foamField == null) continue;
                    var shader = Shader.Find(name);
                    if (shader == null || !shader.isSupported) throw new Exception("Missing/unsupported water shader: " + name);
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) throw new Exception(name + ": " + message.message);
                }
                Debug.Log("WaterRender: wrote " + output);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
