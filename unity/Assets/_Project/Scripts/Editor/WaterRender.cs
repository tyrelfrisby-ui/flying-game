using System;
using System.IO;
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
        static void SpawnMesh(string name, Vector3[] vertices, int[] triangles, Material material, Transform parent) =>
            typeof(WorldBuilder).GetMethod("Spawn", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { name, vertices, triangles, material, parent, true });
        // A controlled visual stage, not replacement terrain or a new simulated lake.
        // Uses the real harbour's surface and real FloatHydro/wake signals.
        static void BuildSunsetShore()
        {
            var lake = WorldTerrain.Harbor;
            Vector3 origin = CoordinateMap.ToUnity(new Vec3(lake.Cx, lake.Cy, -lake.SurfaceM));
            var parent = new GameObject("Sunset comparison stage").transform;
            var water = WorldBuilder.Mat("FlyingGame/Water", new Color(.035f,.065f,.08f,1));
            SpawnMesh("Calm harbour", new[] { origin+new Vector3(-2400,0,-2000), origin+new Vector3(-2400,0,2400), origin+new Vector3(2400,0,2400), origin+new Vector3(2400,0,-2000) }, new[]{0,1,2,0,2,3}, water, parent);
            var bank = WorldBuilder.Mat("FlyingGame/Lit",new Color(.055f,.065f,.055f));
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
                for(int row=0;row<4;row++)
                {
                    float cluster=.5f+.5f*Mathf.Sin(i*.23f);
                    float h=7+cluster*9+(float)rng.NextDouble()*10;
                    float depth=row*48+(float)rng.NextDouble()*43;
                    Vector3 at=origin+new Vector3(x+(float)rng.NextDouble()*25,.3f*Mathf.Sin(i*.67f+row),z+6+depth);
                    for(int crown=0;crown<9;crown++)
                    {
                        float bottom=h*(.08f+crown*.09f), top=bottom+h*.28f;
                        float radius=h*(.28f-crown*.026f)*( .8f+(float)rng.NextDouble()*.4f);
                        int v=verts.Count; verts.Add(at+Vector3.up*top);
                        for(int j=0;j<9;j++) { float a=j*Mathf.PI*2/9; verts.Add(at+new Vector3(Mathf.Cos(a)*radius*(.65f+(float)rng.NextDouble()*.5f),bottom+h*((float)rng.NextDouble()-.5f)*.045f,Mathf.Sin(a)*radius)); }
                        for(int j=0;j<9;j++) tris.AddRange(new[]{v,v+1+j,v+1+(j+1)%9});
                    }
                }
            }
            SpawnMesh("Conifer shoreline",verts.ToArray(),tris.ToArray(),bank,parent);
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
                Debug.Log($"WaterRender fixture: speed={speed} m/s throttle={throttle} displacement={displacement}");
                float lift = float.Parse(Read("-lift", "0"), System.Globalization.CultureInfo.InvariantCulture);
                Directory.CreateDirectory(output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                WorldTerrain.Active = new WorldTerrain();
                Shader.SetGlobalFloat("_SunsetOn", 0); Shader.SetGlobalFloat("_WaterCalm", calm ? 1 : 0);
                if (sunset) BuildSunsetShore(); else WorldBuilder.BuildAll();
                SunsetLighting.ConfigureDayAtmosphere(Read("-skyQuality", "Balanced"),
                    float.Parse(Read("-hazeDensity", "0.000038"), System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(Read("-hazeClearRange", "850"), System.Globalization.CultureInfo.InvariantCulture));
                RenderSettings.fog = false;
                RenderSettings.ambientLight = new Color(.65f, .7f, .78f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                Shader.SetGlobalVector("_BubbleSunDir", new Vector3(.4f, .8f, -.4f));
                if (sunset) SunsetLighting.Apply(sun, calm);
                var cfg = UnityAircraftConfigLoader.LoadFromStreamingAssets(id);
                if (cfg.Floats == null) throw new ArgumentException("Water render requires a floatplane.");
                var lake = WorldTerrain.Harbor;
                var root = new GameObject("Water render " + id);
                var driver = root.AddComponent<FlightSimDriver>(); driver.enabled = false;
                var builder = new AirframeBuilder(); builder.Build(root.transform, cfg);
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = WaterReflection.AircraftLayer;
                var spray = root.AddComponent<FloatSplash>(); spray.Driver = driver;
                var wake = root.AddComponent<WakeWaves>(); wake.Driver = driver; Call(wake, "Start");
                Call(wake, "Allocate", WorldTerrain.Lakes.Length - 1);
                for (int frame = 0; frame < 960; frame++)
                {
                    double north = lake.Cx + (frame - 959) * speed / 60;
                    var attitude = new Quat(0, Math.Sin((displacement ? 0 : 3) * Math.PI / 360), 0, Math.Cos((displacement ? 0 : 3) * Math.PI / 360));
                    var state = new RigidBodyState(new Vec3(north, lake.Cy, -lake.SurfaceM - cfg.Floats.KeelZ + .13 - lift), attitude, new Vec3(speed, 0, 0), Vec3.Zero);
                    var ac = new Aircraft(cfg, state, ControlDeflections.Neutral);
                    ac.SetReplayPose(state, ControlDeflections.Neutral, throttle, 0, 1, 1);
                    driver.AdoptSim(new SimLoop(ac));
                    FloatHydro.Compute(cfg, state, 0);
                    Call(spray, "LateUpdate");
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
                Debug.Log($"WaterRender: wet={FloatHydro.Floats[0].Wet}, lift={FloatHydro.Floats[0].PlaningLiftN}, buoyancy={FloatHydro.Floats[0].BuoyancyN}, drag={FloatHydro.Floats[0].DragN}");
                // Publish the final ping-pong texture, as Update does in play mode.
                Shader.SetGlobalTexture("_WakeTex", (RenderTexture)typeof(WakeWaves).GetField("_curr", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(wake));
                var foamField = typeof(WakeWaves).GetField("_foam", BindingFlags.NonPublic | BindingFlags.Instance);
                Shader.SetGlobalTexture("_WakeFoamTex", foamField != null ? (Texture)foamField.GetValue(wake) : Texture2D.blackTexture);
                builder.SpinProps(_ => (float)driver.Sim.Aircraft.EngineRpm, .02f);
                var camera = new GameObject("Water camera").AddComponent<Camera>();
                var reflection = camera.gameObject.AddComponent<WaterReflection>();
                reflection.enabled = false; reflection.Driver = driver; Call(reflection, "Awake");
                reflection.Quality = (WaterReflection.ReflectionQuality)Enum.Parse(typeof(WaterReflection.ReflectionQuality), Read("-reflectionQuality", "High"), true);
                reflection.ReflectScenery = Read("-reflectionScenery", "on") != "off";
                reflection.TextureSize = int.Parse(Read("-reflectionSize", "0"));
                camera.clearFlags = CameraClearFlags.Skybox; camera.backgroundColor = new Color(.45f, .66f, .95f);
                camera.nearClipPlane = .15f; camera.farClipPlane = 12000; camera.fieldOfView = 48;
                var rt = new RenderTexture(1600, 1000, 24); camera.targetTexture = rt;
                Vector3 at = root.transform.position;
                var offsets = new[] { new Vector3(15, 5, 15), new Vector3(30, 40, -45), new Vector3(7, 2, 6) };
                if (displacement && !calm)
                {
                    offsets[0] = new Vector3(12, 3, 13);
                    offsets[1] = new Vector3(15, 18, -22);
                    offsets[2] = new Vector3(7, 2, 6);
                }
                var names = new[] { "spray", "wake", "bow" };
                for (int i = 0; i < offsets.Length; i++)
                {
                    camera.transform.position = at + offsets[i]; camera.transform.LookAt(at + new Vector3(0, -1, i == 1 ? (displacement ? -7 : -24) : 0));
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
                    // Additional low rear-quarter hero: fit both real and mirrored bounds.
                    // Fit projected corners, not just world X width, for wider types and near-wing perspective.
                    camera.fieldOfView = 64;
                    Vector3 heroCentre = aircraftBounds.center;
                    heroCentre.y = (float)lake.SurfaceM + .25f;
                    Vector3 rearQuarter = new Vector3(-.56f, 0, -.83f).normalized;
                    float heroDistance = Mathf.Max(6, aircraftBounds.size.x * .65f);
                    for (int fit = 0; fit < 60; fit++)
                    {
                        camera.transform.position = heroCentre + rearQuarter * heroDistance;
                        camera.transform.position = new Vector3(camera.transform.position.x, (float)lake.SurfaceM + 1, camera.transform.position.z);
                        camera.transform.LookAt(heroCentre);
                        bool fits = true;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            Vector3 point = aircraftBounds.center + Vector3.Scale(aircraftBounds.extents,
                                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                            for (int mirror = 0; mirror < 2; mirror++)
                            {
                                if (mirror == 1) point.y = 2 * (float)lake.SurfaceM - point.y;
                                Vector3 uv = camera.WorldToViewportPoint(point);
                                if (uv.z <= camera.nearClipPlane || uv.x < .055f || uv.x > .945f || uv.y < .055f || uv.y > .945f) fits = false;
                            }
                        }
                        if (fits) break;
                        heroDistance *= 1.035f;
                    }
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
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
