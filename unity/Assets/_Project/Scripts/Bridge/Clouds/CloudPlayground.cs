using System.Collections.Generic;
using Aero.Wake;
using FlyingGame.Bridge;
using FlyingGame.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Clouds
{
    /// <summary>Visual asset builder only (Codex, lane B; integrated by lane A, docs/CLOUDS-WAKE-VISUALS.md). Built by
    /// <see cref="Attach"/> from IslandWorldBuilder. No physics, colliders, camera overlays, or changes to thermal
    /// cumulus. The wisps read the real wake (Aero.Wake.WakeField, docs/WAKE-VORTEX.md).</summary>
    public sealed class CloudPlayground : MonoBehaviour
    {
        public Material billowMaterial, wispMaterial, advectionMaterial;
        public Vector2 ambientWind;
        public Camera viewCamera;
        const int Resolution = 256;
        const float TileSize = 512f, StepSec = 1f / 15f;
        readonly List<Object> owned = new List<Object>();
        readonly List<Renderer> billows = new List<Renderer>();
        readonly List<Vector3> centres = new List<Vector3>();
        // Sheets 0-2: the layers. Sheet 3: whichever low wisp patch (CloudSites.Low) the eye is at; its height and its
        // round mask change with the patch (lane A, 2026-11-05).
        const int Layers = 3, Sheets = 4;
        readonly float[] heights = { 650, 1550, 2450, 0 };
        readonly RenderTexture[] density = new RenderTexture[Sheets], scratch = new RenderTexture[Sheets];
        readonly Material[] sheets = new Material[Sheets];
        readonly Transform[] tiles = new Transform[Sheets];
        readonly Vector2[] origins = new Vector2[Sheets];
        readonly bool[] initialized = new bool[Sheets];
        readonly List<CloudSites.LowPatch> lowWisps = new List<CloudSites.LowPatch>();
        int activeWisp = -1;
        GroundFog fog;
        /// <summary>Low patches built: puffy clusters and wisp patches (CloudSites.Low).</summary>
        public int LowPuffyCount { get; private set; }
        public int LowWispCount => lowWisps.Count;
        public GroundFog Fog => fog;
        RenderTexture wake;
        Material advect;
        float nextStep;
        bool built;
        Vector3 island;

        readonly Material[] farSheets = new Material[3];
        Vector2 drift;

        /// <summary>Build the playground under <paramref name="parent"/> with the shared materials (Resources/Clouds).
        /// Island centre is the world origin; banks keep clear of the terrain under them.</summary>
        public static CloudPlayground Attach(Transform parent)
        {
            var go = new GameObject("CloudPlayground");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<CloudPlayground>();
            c.billowMaterial = Resources.Load<Material>("Clouds/CloudBillow");
            c.wispMaterial = Resources.Load<Material>("Clouds/CloudWisp");
            c.advectionMaterial = Resources.Load<Material>("Clouds/CloudAdvect");
            c.Build(Vector3.zero);
            c.fog = GroundFog.Attach(c.transform);
            return c;
        }

        /// <summary>Highest ground (Unity y, m) under a disc: sampled on a 5 × 5 grid.</summary>
        static float GroundUnder(Vector3 c, float radius)
        {
            float top = 0f;
            for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                float x = c.x + i * radius * .5f, z = c.z + j * radius * .5f;
                top = Mathf.Max(top, (float)WorldTerrain.GroundHeightAt(z, x));   // sim (north, east) = Unity (z, x)
            }
            return top;
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        RenderTexture Target(RenderTextureFormat format)
        {
            var rt = Own(new RenderTexture(Resolution, Resolution, 0, format) {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                useMipMap = false, autoGenerateMips = false, name = "Cloud density / wake" });
            rt.Create(); return rt;
        }
        GameObject MeshObject(string label, Mesh mesh, Material material)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Own(mesh);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }
        public void Build(Vector3 islandCentre)
        {
            if (built) return;
            if (!billowMaterial || !wispMaterial || !advectionMaterial)
            { Debug.LogError("CloudPlayground requires all three serialized cloud materials."); return; }
            built = true; island = islandCentre;
            var rng = new System.Random(1105);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // Three broken necklaces, an amphitheatre round the island (lane A, 2026-11-05): the high band over the
            // mountains, the middle over the foothills, the low band out over the coast and the sea. The island is ~30 km
            // across and 2.4 km high at the centre, so no bank may sit in the ground: a bank whose terrain comes within
            // 200 m of its base is lifted clear of it. Wide radial lanes separate banks; omitted sectors make canyons.
            float[] bases = { 2050, 1100, 450 };
            float[] radii = { 6000, 11000, 16500 };
            int[] sectors = { 12, 16, 20 };
            for (int band = 0; band < 3; band++)
            for (int sector = 0; sector < sectors[band]; sector++)
            {
                if (sector % 4 == band % 4) continue;
                float angle = (sector * 360f / sectors[band] + band * 11) * Mathf.Deg2Rad;
                float radius = radii[band] + R(-900, 900);
                Vector3 centre = new Vector3(island.x + Mathf.Cos(angle)*radius,
                    bases[band], island.z + Mathf.Sin(angle)*radius);
                centre.y = Mathf.Max(centre.y, GroundUnder(centre, 600) + 200);
                var vertices = new List<Vector3>(); var uv = new List<Vector2>();
                var sizes = new List<Vector2>(); var colors = new List<Color>(); var indices = new List<int>();
                // Horseshoe bank with a real 220+ m central passage, open at both ends.
                for (int lobe = 0; lobe < 20; lobe++)
                {
                    float a = lobe / 19f * Mathf.PI * 1.65f;
                    Vector3 p = new Vector3(Mathf.Cos(a)*400, R(90,210), Mathf.Sin(a)*400);
                    float size = R(95,155);
                    if (lobe >= 12) { p.y += R(120,240); size *= .75f; }
                    float verticalSize = size * R(.9f, 1.0f);
                    int start = vertices.Count;
                    for (int k = 0; k < 4; k++) {
                        vertices.Add(p); uv.Add(new Vector2(k % 2, k / 2));
                        sizes.Add(new Vector2(size, verticalSize));
                        colors.Add(new Color(1,1,1,lobe % 3 == 0 ? 1 : 0));
                    }
                    indices.AddRange(new[] {start,start+2,start+1,start+1,start+2,start+3});
                }
                var mesh = new Mesh {name="Billowing horseshoe impostors"};
                mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,sizes);
                mesh.SetColors(colors); mesh.SetTriangles(indices,0);
                mesh.bounds = new Bounds(new Vector3(0,240,0),new Vector3(1300,1000,1300));
                var go = MeshObject("Cloud bank",mesh,billowMaterial); go.transform.position=centre;
                billows.Add(go.GetComponent<Renderer>()); centres.Add(centre);
            }
            // Low patches "here and there" (Ty 08:39; CloudSites.Low): small puffy clusters 150-600 m above the ground
            // under them, the same impostor lobes as the banks, a few to a dozen in a heap a few hundred metres across.
            foreach (CloudSites.LowPatch lp in CloudSites.Low)
            {
                if (!lp.Puffy) { lowWisps.Add(lp); continue; }
                var lr = new System.Random(lp.PatchSeed);
                float LR(float a, float b) => a + (float)lr.NextDouble() * (b - a);
                var vertices = new List<Vector3>(); var uv = new List<Vector2>();
                var sizes = new List<Vector2>(); var colors = new List<Color>(); var indices = new List<int>();
                float rad = (float)lp.RadiusM;
                int lobes = 6 + lr.Next(7);
                for (int lobe = 0; lobe < lobes; lobe++)
                {
                    float a = LR(0, Mathf.PI * 2), d = rad * Mathf.Sqrt(LR(0, 1)) * .65f;
                    float size = LR(45, 95) * (1.15f - .5f * d / rad);
                    // Heaped: the middle lobes stand higher; every lobe's bottom near the patch's base.
                    Vector3 p = new Vector3(Mathf.Cos(a) * d, size * .7f + LR(0, 60) * (1 - d / rad), Mathf.Sin(a) * d);
                    int start = vertices.Count;
                    for (int k = 0; k < 4; k++) {
                        vertices.Add(p); uv.Add(new Vector2(k % 2, k / 2));
                        sizes.Add(new Vector2(size, size * LR(.8f, .95f)));
                        colors.Add(new Color(1, 1, 1, lobe % 3 == 0 ? 1 : 0));
                    }
                    indices.AddRange(new[] {start,start+2,start+1,start+1,start+2,start+3});
                }
                var mesh = new Mesh {name="Low puffy patch impostors"};
                mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,sizes);
                mesh.SetColors(colors); mesh.SetTriangles(indices,0);
                mesh.bounds = new Bounds(new Vector3(0,120,0),new Vector3(rad*2+250,400,rad*2+250));
                var go = MeshObject("Low cloud patch",mesh,billowMaterial);
                go.transform.position=new Vector3((float)lp.X,(float)lp.BaseM,(float)lp.Z);
                billows.Add(go.GetComponent<Renderer>()); centres.Add(go.transform.position);
                LowPuffyCount++;
            }
            advect = Own(new Material(advectionMaterial));
            wake = Target(RenderTextureFormat.ARGBHalf);
            for (int i=0;i<Sheets;i++) {
                density[i]=Target(RenderTextureFormat.R8); scratch[i]=Target(RenderTextureFormat.R8);
                sheets[i]=Own(new Material(wispMaterial));
                var mesh=new Mesh { name="Thin cloud sheet" };
                mesh.vertices=new[] {Vector3.zero,new Vector3(TileSize,0,0),new Vector3(0,0,TileSize),new Vector3(TileSize,0,TileSize)};
                mesh.uv=new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
                mesh.triangles=new[] {0,2,1,1,2,3}; mesh.RecalculateBounds();
                tiles[i]=MeshObject("Wake-reactive wisps",mesh,sheets[i]).transform;
                tiles[i].gameObject.SetActive(false);
                // The same layer beyond the patch (lane A): the patch's own seed, drifting with the wind, so the layer
                // is there to be seen and flown to from anywhere; it gives way to the wake-reactive patch inside it.
                Shader far = Shader.Find("FlyingGame/CloudWispFar");
                if (i >= Layers) continue;
                if (far != null)
                {
                    farSheets[i]=Own(new Material(far));
                    farSheets[i].SetFloat("_Layer",i);
                    var fm=new Mesh { name="Thin cloud sheet (far)" };
                    const float H=22000f;
                    fm.vertices=new[] {new Vector3(-H,0,-H),new Vector3(H,0,-H),new Vector3(-H,0,H),new Vector3(H,0,H)};
                    fm.uv=new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
                    fm.triangles=new[] {0,2,1,1,2,3}; fm.RecalculateBounds();
                    MeshObject("Thin cloud sheet",fm,farSheets[i]).transform.position=new Vector3(island.x,heights[i],island.z);
                }
                else Debug.LogError("[Clouds] FlyingGame/CloudWispFar missing from the build: the wispy layers show only close up");
            }
            // Each low wisp patch, seen from afar: one quad with the patch's own mask, handing over to sheet 3 when the
            // eye is at it (its _Layer is 3 + its index, which is what _WispPatch.w says then).
            Shader farPatch = Shader.Find("FlyingGame/CloudWispFar");
            for (int k = 0; farPatch != null && k < lowWisps.Count; k++)
            {
                CloudSites.LowPatch lp = lowWisps[k];
                var m = Own(new Material(farPatch));
                m.SetFloat("_Layer", Layers + k);
                m.SetVector("_WispMask", new Vector4((float)lp.X, (float)lp.Z, (float)lp.RadiusM, 1));
                float H = (float)lp.RadiusM * 1.2f;
                var fm=new Mesh { name="Low wisp patch (far)" };
                fm.vertices=new[] {new Vector3(-H,0,-H),new Vector3(H,0,-H),new Vector3(-H,0,H),new Vector3(H,0,H)};
                fm.uv=new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
                fm.triangles=new[] {0,2,1,1,2,3}; fm.RecalculateBounds();
                MeshObject("Low wisp patch",fm,m).transform.position=new Vector3((float)lp.X,(float)lp.BaseM,(float)lp.Z);
            }
            Debug.Log($"[Clouds] {centres.Count - LowPuffyCount} banks, {LowPuffyCount} low puffy patches, {lowWisps.Count} low wisp patches");
        }
        void LateUpdate()
        {
            if (!viewCamera) viewCamera=Camera.main;
            Step(viewCamera, Time.time, Time.deltaTime);
        }

        /// <summary>The frame, for an editor tool (LateUpdate does not run in edit mode): culls, and at most 15 times a
        /// second advances the nearest sheet's density through the wake. <paramref name="now"/> is the clock the 15 Hz
        /// gate runs on.</summary>
        public void StepForTesting(Camera cam, float now, float dt) { viewCamera = cam; Step(cam, now, dt); }
        /// <summary>The last wake sheet and a layer's density, for the render tool.</summary>
        public RenderTexture WakeTextureForTesting => wake;
        public RenderTexture DensityForTesting(int layer) => density[layer];
        /// <summary>The layers' large-scale coverage (CloudWispSeed.cginc WispCover, same expression): 0 clear sky, 1 a
        /// patch of wisps. <paramref name="world"/> = Unity X/Z minus the wind drift.</summary>
        public static float WispCover(Vector2 world)
        {
            float a = Mathf.Sin(world.x * .00061f + 1.3f * Mathf.Sin(world.y * .00043f)) * Mathf.Sin(world.y * .00057f + 1.1f * Mathf.Sin(world.x * .00037f + .7f));
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((a - .05f) / .4f));
        }
        public static float LayerHeight(int layer) => new float[] { 650, 1550, 2450 }[layer];

        void Step(Camera cam, float now, float dt)
        {
            if (!built || !cam) return;
            Vector3 eye=cam.transform.position;
            Vector3 w=CoordinateMap.ToUnity(Atmosphere.MeanWindAtPosition(CoordinateMap.ToSim(eye)));
            ambientWind=new Vector2(w.x,w.z);
            drift+=ambientWind*dt;
            Shader.SetGlobalVector("_WispDrift",new Vector4(drift.x,drift.y,0,0));
            // CPU culling at cluster granularity; shader fades detail lobes before disabling far banks.
            for(int i=0;i<billows.Count;i++) billows[i].enabled=(centres[i]-eye).sqrMagnitude < 19000f*19000f;
            if(now<nextStep) return;
            // At most one 256² simulation per tick, never catch up after a stalled frame.
            nextStep=now+StepSec;
            int nearest=0;
            for(int i=1;i<Layers;i++) if(Mathf.Abs(eye.y-heights[i])<Mathf.Abs(eye.y-heights[nearest])) nearest=i;
            bool nearby=new Vector2(eye.x-island.x,eye.z-island.z).sqrMagnitude<22000f*22000f;
            bool layerOn=nearby && Mathf.Abs(eye.y-heights[nearest])<220;
            // A low wisp patch the eye is at takes the reactive sheet if it is nearer in height than the layer.
            int patch=-1; float patchDy=220;
            for(int k=0;k<lowWisps.Count;k++)
            {
                CloudSites.LowPatch lp=lowWisps[k];
                float dy=Mathf.Abs(eye.y-(float)lp.BaseM);
                if(dy<patchDy && new Vector2(eye.x-(float)lp.X,eye.z-(float)lp.Z).magnitude<(float)lp.RadiusM+TileSize*.5f) { patch=k; patchDy=dy; }
            }
            int slot=nearest;
            if(patch>=0 && (!layerOn || patchDy<Mathf.Abs(eye.y-heights[nearest])))
            {
                slot=Layers; layerOn=true;
                if(patch!=activeWisp) { activeWisp=patch; initialized[Layers]=false; }
                CloudSites.LowPatch lp=lowWisps[patch];
                heights[Layers]=(float)lp.BaseM;
                advect.SetVector("_WispMask",new Vector4((float)lp.X,(float)lp.Z,(float)lp.RadiusM,1));
                sheets[Layers].SetVector("_WispMask",new Vector4((float)lp.X,(float)lp.Z,(float)lp.RadiusM,1));
            }
            else advect.SetVector("_WispMask",Vector4.zero);
            nearest=slot;
            for(int i=0;i<Sheets;i++) tiles[i].gameObject.SetActive(layerOn && i==nearest);
            if(!tiles[nearest].gameObject.activeSelf) { Shader.SetGlobalVector("_WispPatch",new Vector4(0,0,TileSize,-1)); return; }
            Vector2 origin=new Vector2(Mathf.Floor(eye.x/16)*16-TileSize/2,Mathf.Floor(eye.z/16)*16-TileSize/2);
            Vector2 shift=(origin-origins[nearest])/TileSize;
            WakeField.WriteToTexture(wake,new Bounds(new Vector3(origin.x+TileSize/2,heights[nearest],origin.y+TileSize/2),new Vector3(TileSize,40,TileSize)));
            advect.SetTexture("_WakeTex",wake);
            advect.SetVector("_WindStep",new Vector4(ambientWind.x,ambientWind.y,StepSec,1/TileSize));
            advect.SetVector("_Origin",new Vector4(origin.x,origin.y,shift.x,shift.y));
            advect.SetFloat("_Initialize",initialized[nearest]?0:1);
            Graphics.Blit(density[nearest],scratch[nearest],advect);
            var swap=density[nearest]; density[nearest]=scratch[nearest]; scratch[nearest]=swap;
            sheets[nearest].mainTexture=density[nearest];
            tiles[nearest].position=new Vector3(origin.x,heights[nearest],origin.y);
            origins[nearest]=origin; initialized[nearest]=true;
            Shader.SetGlobalVector("_WispPatch",new Vector4(origin.x,origin.y,TileSize,nearest<Layers?nearest:Layers+activeWisp));
        }
        void OnDestroy()
        {
            foreach(var item in owned) { if(item is RenderTexture rt) rt.Release(); if(item) { if(Application.isPlaying) Destroy(item); else DestroyImmediate(item); } }
            owned.Clear();
        }
    }
}
