using System.Collections.Generic;
using UnityEngine;

namespace FlyingGame.Bridge
{
    // One opaque procedural facade draw per city. No textures or per-window objects.
    internal sealed class UrbanFacades
    {
        readonly List<Vector3> vertices = new();
        readonly List<Vector3> normals = new();
        readonly List<Vector2> uv = new(), data = new();
        readonly List<Color> colours = new();
        readonly List<int> indices = new();
        static Material material;
        public void Add(Vector3 bottom, Vector3 size, int style, float seed, Color wall, Quaternion? rotation = null)
        {
            for (int face = 0; face < 4; face++)
            {
                Quaternion q = (rotation ?? Quaternion.identity) * Quaternion.Euler(0, face * 90, 0);
                float w = face % 2 == 0 ? size.x : size.z;
                float d = face % 2 == 0 ? size.z : size.x;
                int start = vertices.Count;
                Vector2[] corners = { new(0,0), new(w,0), new(w,size.y), new(0,size.y) };
                foreach (var v in corners)
                {
                    vertices.Add(bottom + q * new Vector3(v.x-w*.5f,v.y,d*.5f+.045f));
                    normals.Add(q * Vector3.forward); uv.Add(v);
                    data.Add(new Vector2(style,seed+face*17)); colours.Add(wall);
                }
                indices.AddRange(new[] { start,start+1,start+2,start,start+2,start+3 });
            }
        }
        public void Build(Transform parent)
        {
            var mesh = new Mesh { name = "Urban facade atlas", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetUVs(1,data);
            mesh.SetColors(colours); mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
            var go = new GameObject("UrbanFacades"); go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (material == null) material = new Material(Shader.Find("FlyingGame/UrbanFacade"));
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
    internal static class UrbanDressing
    {
        static readonly Color Asphalt = new(.19f,.20f,.21f), Paint = new(.76f,.74f,.65f);
        static Color Emission(float r,float g,float b) => new Color(r,g,b,0);
        public static void Lot(SeasideBuilder.Batch b, UrbanFacades facades, Vector3 bottom, Vector3 size, int i, int j, int style, bool protectedLot)
        {
            // Original tower roofs remain clear and at their exact landing height.
            Vector3 roof = bottom+Vector3.up*size.y;
            if(size.y>230) b.Box(roof+new Vector3(size.x*.4f,-.10f,size.z*.4f),new Vector3(.65f,.2f,.65f),Emission(1,.025f,.015f));
            if(protectedLot) return;
            // Small mixed-use infill occupies the existing sidewalk lot, clear of the 30 m road.
            bool house = i==0 || i==6 || j==15;
            Vector3 shop=bottom+new Vector3(0,0,-52);
            float h=house?7:10+(i+j)%3*3;
            Vector3 shopSize=new Vector3(house?17:36,h,house?15:20);
            Color wall=house?new Color(.69f,.66f,.55f):new Color(.46f,.28f,.21f);
            b.Box(shop+Vector3.up*h*.5f,shopSize,wall);
            facades.Add(shop,shopSize,house?4:1,i*37+j*11,wall);
            if(house)
            {
                float x=shopSize.x*.53f,z=shopSize.z*.53f;
                Vector3 a=shop+new Vector3(-x,h,-z), c=shop+new Vector3(x,h,-z), d=shop+new Vector3(-x,h,z), e=shop+new Vector3(x,h,z);
                Vector3 r=shop+new Vector3(0,h+4,-z), t=shop+new Vector3(0,h+4,z);
                Color tile=(j%2==0)?new Color(.32f,.19f,.14f):new Color(.23f,.27f,.28f);
                b.Quad(a,r,t,d,tile); b.Quad(r,c,e,t,tile*.85f);
                b.Tri(a,c,r,wall); b.Tri(d,t,e,wall);
                b.Box(shop+new Vector3(5,h+2,3),new Vector3(1.1f,4,1.1f),new Color(.39f,.24f,.19f));
            }
            else
            {
                // Parapet, stairwell, paired HVAC on these non-landable decorative shops.
                b.Box(shop+Vector3.up*(h+.02f),new Vector3(35,.04f,19),new Color(.30f,.32f,.30f));
                for(int side=-1;side<=1;side+=2)
                {
                    b.Box(shop+new Vector3(side*17.7f,h+.35f,0),new Vector3(.5f,.7f,20),wall*.85f);
                    b.Box(shop+new Vector3(0,h+.35f,side*9.7f),new Vector3(36,.7f,.5f),wall*.85f);
                }
                b.Box(shop+new Vector3(-11,h+1,3),new Vector3(5,2,6),new Color(.38f,.37f,.33f));
                for(int k=0;k<2;k++) b.Box(shop+new Vector3(k*7,h+.8f,3),new Vector3(4,1.6f,4),new Color(.48f,.50f,.48f));
                if((i+j)%5==0)
                {
                    Vector3 tank=shop+new Vector3(10,h, -4);
                    for(int leg=-1;leg<=1;leg+=2) b.Box(tank+new Vector3(leg*1.3f,1,0),new Vector3(.2f,2,.2f),new Color(.23f,.23f,.21f));
                    b.Cylinder(tank+Vector3.up*2,2,3,8,new Color(.32f,.25f,.18f),new Color(.22f,.22f,.20f),.7f);
                }
                Color awning=(i%2==0)?new Color(.18f,.31f,.25f):new Color(.51f,.20f,.14f);
                b.Box(shop+new Vector3(0,4.8f,11),new Vector3(35,.3f,3),awning);
                b.Box(shop+new Vector3(0,4.2f,12.4f),new Vector3(35,1.0f,.2f),awning*.8f);
            }
            // Parked cars and bays, two per lot, no runtime objects/colliders.
            for(int k=0;k<2;k++)
            {
                Vector3 car=bottom+new Vector3(51,.3f,-18+k*12);
                b.Box(car+Vector3.up*.7f,new Vector3(2,1.4f,4.5f),i%3==0?new Color(.42f,.12f,.09f):new Color(.46f,.49f,.48f));
                b.Box(car+new Vector3(0,1.65f,0),new Vector3(1.7f,.65f,2.3f),new Color(.12f,.20f,.23f));
                b.Box(car+new Vector3(0,0,4),new Vector3(3,.025f,.15f),Paint);
            }
            // Broadleaf street trees: two crossed volumes in one static mesh, no alpha overdraw.
            for(int k=0;k<2;k++)
            {
                Vector3 tree=bottom+new Vector3(-59,0,-34+k*68);
                b.Cylinder(tree,.3f,5,5,new Color(.25f,.20f,.14f));
                b.Cylinder(tree+Vector3.up*3,3.8f,3.5f,7,new Color(.24f,.32f,.16f),new Color(.29f,.37f,.19f),2.5f);
            }
        }
        public static void Streets(FlyingGame.Core.CityGrid grid, double ground, SeasideBuilder.Batch b)
        {
            Vector3 P(double u,double v,double h)
            {
                var p=grid.Map(u,v); return WorldBuilder.U(p.x,p.y,ground+h);
            }
            // Map each road in grid coordinates; dimensions rotate with the city.
            void Mark(double u,double v,float w,float len,Color c)
            {
                Vector3 scale=grid.Rotated?new Vector3(w,.025f,len):new Vector3(len,.025f,w);
                Vector3 centre=P(u,v,.10), x=Vector3.right*scale.x*.5f, z=Vector3.forward*scale.z*.5f;
                b.Quad(centre-x-z,centre-x+z,centre+x+z,centre+x-z,c);
            }
            Color yellow=new(.70f,.55f,.22f);
            for(int axis=0;axis<2;axis++)
            {
                int count=axis==0?6:15; int steps=axis==0?16:7;
                for(int road=0;road<count;road++)
                for(int block=0;block<steps;block++)
                {
                    double u=road*170+85, v=block*170;
                    for(int dash=-3;dash<=3;dash++)
                    {
                        double a=u, z=v+dash*17;
                        if(axis==1) { a=z; z=u; }
                        // Swap mark orientation for the transverse streets.
                        if(axis==0) Mark(a,z,.20f,8,Paint); else Mark(a,z,8,.20f,Paint);
                    }
                    for(int sg=-1;sg<=1;sg+=2)
                    {
                        double a=u+sg*.4, z=v;
                        if(axis==1) { a=z; z=u+sg*.4; }
                        if(axis==0) Mark(a,z,.13f,130,yellow); else Mark(a,z,130,.13f,yellow);
                    }
                    // Crosswalks stop short of the junction centre.
                    for(int stripe=-4;stripe<=4;stripe++)
                    {
                        double a=u+stripe*2.5,z=v+64;
                        if(axis==1) { a=z; z=u+stripe*2.5; }
                        if(axis==0) Mark(a,z,1.2f,4,Paint); else Mark(a,z,4,1.2f,Paint);
                    }
                    // Two street lamps per block. Opaque emissive heads, no point lights or shadow maps.
                    for(int sg=-1;sg<=1;sg+=2)
                    {
                        double a=u+sg*18,z=v+sg*35;
                        if(axis==1) { a=z; z=u+sg*18; }
                        Vector3 foot=P(a,z,.22);
                        b.Box(foot+Vector3.up*4,new Vector3(.18f,8,.18f),new Color(.23f,.25f,.24f));
                        b.Box(foot+Vector3.up*8,new Vector3(1.4f,.22f,1.4f),Emission(1,road%3==0?.60f:.85f,road%3==0?.25f:.72f));
                        // Flush light pool: dark asphalt by day, softly warmer pavement at night.
                        b.Box(P(a,z,.082),new Vector3(5,.012f,7),new Color(.22f,.21f,.18f,.75f));
                    }
                }
            }
        }
    }

}
