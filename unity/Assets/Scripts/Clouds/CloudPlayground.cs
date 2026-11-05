using System.Collections.Generic;
using Aero.Wake;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlyingGame.Clouds
{
    /// <summary>Visual asset builder only. Lane A calls Build with the island's Unity world centre.
    /// No auto-spawn, physics, colliders, camera overlays, or changes to thermal cumulus.</summary>
    public sealed class CloudPlayground : MonoBehaviour
    {
        public Material billowMaterial, wispMaterial, advectionMaterial;
        public Vector2 ambientWind;
        public Camera viewCamera;
        const int Resolution = 256;
        const float TileSize = 512f, Step = 1f / 15f;
        readonly List<Object> owned = new List<Object>();
        readonly List<Renderer> billows = new List<Renderer>();
        readonly List<Vector3> centres = new List<Vector3>();
        readonly float[] heights = { 650, 1550, 2450 };
        readonly RenderTexture[] density = new RenderTexture[3], scratch = new RenderTexture[3];
        readonly Material[] sheets = new Material[3];
        readonly Transform[] tiles = new Transform[3];
        readonly Vector2[] origins = new Vector2[3];
        readonly bool[] initialized = new bool[3];
        RenderTexture wake;
        Material advect;
        float nextStep;
        bool built;
        Vector3 island;

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
            // Three broken necklaces. Wide radial lanes separate banks; omitted sectors make cross-island canyons.
            float[] bases = { 350, 1100, 2050 };
            for (int band = 0; band < 3; band++)
            for (int sector = 0; sector < 12; sector++)
            {
                if (sector % 4 == band % 4) continue;
                float angle = (sector * 30 + band * 11) * Mathf.Deg2Rad;
                float radius = 2400 + band * 1450;
                Vector3 centre = new Vector3(island.x + Mathf.Cos(angle)*radius,
                    bases[band], island.z + Mathf.Sin(angle)*radius);
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
            advect = Own(new Material(advectionMaterial));
            wake = Target(RenderTextureFormat.ARGBHalf);
            for (int i=0;i<3;i++) {
                density[i]=Target(RenderTextureFormat.R8); scratch[i]=Target(RenderTextureFormat.R8);
                sheets[i]=Own(new Material(wispMaterial));
                var mesh=new Mesh { name="Thin cloud sheet" };
                mesh.vertices=new[] {Vector3.zero,new Vector3(TileSize,0,0),new Vector3(0,0,TileSize),new Vector3(TileSize,0,TileSize)};
                mesh.uv=new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
                mesh.triangles=new[] {0,2,1,1,2,3}; mesh.RecalculateBounds();
                tiles[i]=MeshObject("Wake-reactive wisps",mesh,sheets[i]).transform;
                tiles[i].gameObject.SetActive(false);
            }
        }
        void LateUpdate()
        {
            if (!built) return;
            if (!viewCamera) viewCamera=Camera.main;
            if (!viewCamera) return;
            Vector3 eye=viewCamera.transform.position;
            // CPU culling at cluster granularity; shader fades detail lobes before disabling far banks.
            for(int i=0;i<billows.Count;i++) billows[i].enabled=(centres[i]-eye).sqrMagnitude < 12500f*12500f;
            if(Time.time<nextStep) return;
            // At most one 256² simulation per tick, never catch up after a stalled frame.
            nextStep=Time.time+Step;
            int nearest=0;
            for(int i=1;i<3;i++) if(Mathf.Abs(eye.y-heights[i])<Mathf.Abs(eye.y-heights[nearest])) nearest=i;
            bool nearby=new Vector2(eye.x-island.x,eye.z-island.z).sqrMagnitude<10000f*10000f;
            for(int i=0;i<3;i++) tiles[i].gameObject.SetActive(nearby && i==nearest && Mathf.Abs(eye.y-heights[i])<220);
            if(!tiles[nearest].gameObject.activeSelf) return;
            Vector2 origin=new Vector2(Mathf.Floor(eye.x/16)*16-TileSize/2,Mathf.Floor(eye.z/16)*16-TileSize/2);
            Vector2 shift=(origin-origins[nearest])/TileSize;
            WakeField.WriteToTexture(wake,new Bounds(new Vector3(origin.x+TileSize/2,heights[nearest],origin.y+TileSize/2),new Vector3(TileSize,40,TileSize)));
            advect.SetTexture("_WakeTex",wake);
            advect.SetVector("_WindStep",new Vector4(ambientWind.x,ambientWind.y,Step,1/TileSize));
            advect.SetVector("_Origin",new Vector4(origin.x,origin.y,shift.x,shift.y));
            advect.SetFloat("_Initialize",initialized[nearest]?0:1);
            Graphics.Blit(density[nearest],scratch[nearest],advect);
            var swap=density[nearest]; density[nearest]=scratch[nearest]; scratch[nearest]=swap;
            sheets[nearest].mainTexture=density[nearest];
            tiles[nearest].position=new Vector3(origin.x,heights[nearest],origin.y);
            origins[nearest]=origin; initialized[nearest]=true;
        }
        void OnDestroy()
        {
            foreach(var item in owned) { if(item is RenderTexture rt) rt.Release(); if(item) Destroy(item); }
            owned.Clear();
        }
    }
}
