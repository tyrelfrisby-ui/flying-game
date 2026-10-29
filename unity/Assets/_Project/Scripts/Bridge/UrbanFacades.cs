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
        readonly List<Vector4> travel = new();
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
                    data.Add(new Vector2(style,seed)); colours.Add(wall); travel.Add(Vector4.zero);
                }
                indices.AddRange(new[] { start,start+1,start+2,start,start+2,start+3 });
            }
        }
        // Opaque road tile: analytic light falloff shares the facade material and draw.
        public void Pool(Vector3 centre, Vector3 size, Color light)
        {
            int start=vertices.Count;
            Vector2[] corners={new(-1,-1),new(-1,1),new(1,1),new(1,-1)};
            foreach(var v in corners)
            {
                vertices.Add(centre+new Vector3(v.x*size.x*.5f,0,v.y*size.z*.5f));
                normals.Add(Vector3.up); uv.Add(v); data.Add(new Vector2(5,0)); colours.Add(light); travel.Add(Vector4.zero);
            }
            indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        // Animated traffic is baked into this same opaque draw. Only vertex positions move;
        // no transforms, Update loop, colliders, allocations or per-car draw submissions.
        public void TrafficCar(Vector3 centre, Vector3 forward, int seed, float length)
        {
            Quaternion q=Quaternion.LookRotation(forward,Vector3.up);
            void Box(Vector3 offset,Vector3 size,Color colour)
            {
                Vector3 h=size*.5f;
                Vector3 P(float x,float y,float z)=>centre+q*(offset+Vector3.Scale(h,new Vector3(x,y,z)));
                void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
                {
                    int start=vertices.Count;
                    Vector3 n=Vector3.Cross(b-a,d-a).normalized;
                    foreach(var v in new[]{a,b,c,d})
                    {
                        vertices.Add(v); normals.Add(n); uv.Add(Vector2.zero);
                        data.Add(new Vector2(6,seed)); colours.Add(colour);
                        travel.Add(new Vector4(forward.x,0,forward.z,length));
                    }
                    indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
                Face(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
                Face(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));
                Face(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));
                Face(P(1,-1,1),P(1,1,1),P(-1,1,1),P(-1,-1,1));
                Face(P(-1,-1,1),P(-1,1,1),P(-1,1,-1),P(-1,-1,-1));
                Face(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
            }
            Box(new Vector3(0,.37f,0),new Vector3(1.9f,.30f,4.7f),new Color(.055f,.06f,.065f));
            Box(new Vector3(0,.85f,0),new Vector3(1.88f,.72f,4.6f),UrbanDressing.CarPaint(seed));
            Box(new Vector3(0,1.40f,-.15f),new Vector3(1.62f,.66f,2.35f),new Color(.12f,.23f,.29f));
            Box(new Vector3(0,1.76f,-.15f),new Vector3(1.65f,.12f,2.3f),UrbanDressing.CarPaint(seed));
            // Two dark axle boxes give four visible wheel ends, with half the box payload.
            foreach(int end in new[]{-1,1})
                Box(new Vector3(0,.40f,end*1.45f),new Vector3(2.07f,.68f,.65f),new Color(.035f,.038f,.04f));
            foreach(int side in new[]{-1,1})
            {
                foreach(int end in new[]{-1,1})
                {
                    int start=vertices.Count;
                    foreach(var corner in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)})
                    {
                        vertices.Add(centre+q*new Vector3(side*.65f+corner.x*.24f,.87f+corner.y*.12f,end*2.32f));
                        normals.Add(forward*end); uv.Add(Vector2.zero); data.Add(new Vector2(6,seed));
                        colours.Add(end==1?new Color(1,.91f,.72f,0):new Color(1,.018f,.008f,0));
                        travel.Add(new Vector4(forward.x,0,forward.z,length));
                    }
                    indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
            }
        }
        public void Build(Transform parent)
        {
            var mesh = new Mesh { name = "Urban facade atlas", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetUVs(1,data);
            mesh.SetUVs(2,travel);
            mesh.SetColors(colours); mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
            // Include the full shader travel envelope in CPU frustum culling.
            var bounds=mesh.bounds; bounds.Expand(new Vector3(120,0,120)); mesh.bounds=bounds;
            var go = new GameObject("UrbanFacades"); go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (material == null) material = new Material(Shader.Find("FlyingGame/UrbanFacade"));
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
    internal static class UrbanDressing
    {
        static readonly Color Paint = new(.76f,.74f,.65f);
        static Color Emission(float r,float g,float b) => new Color(r,g,b,0);
        public static void Lot(SeasideBuilder.Batch b, UrbanFacades facades, Vector3 bottom, Vector3 size, int i, int j, int style, bool protectedLot)
        {
            Vector3 roof = bottom+Vector3.up*size.y;
            // Challenge roofs/approach rows are left entirely clear. Everything here is visual only.
            if(!protectedLot)
            {
                Roof(b,roof,size,style,i*53+j*19);
                // Outward cladding tiers retain the original core and its roof elevation.
                // Limited to non-challenge lots, within the existing 140 m pavement block.
                if(size.y>120 && (i*7+j)%3!=1)
                {
                    int tiers=2;
                    for(int tier=0;tier<tiers;tier++)
                    {
                        float spread=tier==0?14:7;
                        Vector3 tierSize=new Vector3(size.x+spread,size.y*(tier==0?.40f:.73f),size.z+spread);
                        Color tierWall=style==0?new Color(.28f,.36f,.38f):style==1?new Color(.49f,.30f,.23f):style==2?new Color(.59f,.59f,.55f):new Color(.55f,.49f,.41f);
                        tierWall*=.92f+((i*13+j*7)%11)*.016f;
                        b.Box(bottom+Vector3.up*tierSize.y*.5f,tierSize,tierWall);
                        facades.Add(bottom,tierSize,style,i*53+j*19,tierWall);
                        // Only exposed terrace rim; service equipment belongs on the top roof.
                        Vector3 top=bottom+Vector3.up*(tierSize.y+.05f);
                        Color trim=new(.35f,.36f,.34f);
                        foreach(int side in new[]{-1,1})
                        {
                            b.Box(top+new Vector3(side*(tierSize.x*.5f-.2f),.4f,0),new Vector3(.4f,.8f,tierSize.z),trim);
                            b.Box(top+new Vector3(0,.4f,side*(tierSize.z*.5f-.2f)),new Vector3(tierSize.x,.8f,.4f),trim);
                        }
                    }
                }
                else if(size.y<125 && (i+j)%3==0)
                {
                    // Two perpendicular wings form an asymmetric L around the existing core.
                    Color stone=new(.50f,.44f,.37f);
                    Vector3 wingSize=new(size.x+18,Mathf.Min(32,size.y*.6f),size.z*.5f);
                    Vector3 wing=bottom+new Vector3(6,0,-size.z*.25f);
                    b.Box(wing+Vector3.up*wingSize.y*.5f,wingSize,stone);
                    facades.Add(wing,wingSize,1,i*53+j*19,stone);
                    Vector3 service=wing+new Vector3(size.x*.5f+3,wingSize.y+1,0);
                    b.Box(service,new Vector3(4,2,5),new Color(.63f,.66f,.65f));
                }
            }
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
                foreach(int front in new[]{-1,1})
                {
                    b.Box(shop+new Vector3(0,4.8f,front*11),new Vector3(35,.3f,3),awning);
                    b.Box(shop+new Vector3(0,4.2f,front*12.4f),new Vector3(35,1.0f,.2f),awning*.8f);
                    // Door surround and transom at pavement level, on both shop frontages.
                    b.Box(shop+new Vector3(-9,1.8f,front*10.08f),new Vector3(2.2f,3.2f,.16f),new Color(.16f,.20f,.20f));
                    b.Box(shop+new Vector3(-9,3.6f,front*10.18f),new Vector3(2.6f,.22f,.25f),new Color(.67f,.62f,.52f));
                }
            }
            // Six surface parking bays on the clear side of each non-challenge block.
            for(int k=0;k<(QualitySettings.GetQualityLevel()<=2?4:6);k++)
            {
                Vector3 car=bottom+new Vector3(53,.30f,-20+k*6);
                if((i+j+k)%4!=0) Car(b,car,Quaternion.Euler(0,90,0),i*29+j*7+k);
                b.Box(car+new Vector3(0,-.005f,3),new Vector3(6,.02f,.13f),Paint);
            }
        }

        static void Roof(SeasideBuilder.Batch b,Vector3 roof,Vector3 size,int style,int seed)
        {
            Color membrane=Color.Lerp(new Color(.13f,.15f,.16f),new Color(.66f,.62f,.52f),(seed%5)/4f);
            b.Box(roof+Vector3.up*.025f,new Vector3(size.x-.7f,.05f,size.z-.7f),membrane);
            Color rim=new(.49f,.48f,.43f);
            foreach(int side in new[]{-1,1})
            {
                b.Box(roof+new Vector3(side*(size.x*.5f-.25f),.5f,0),new Vector3(.65f,1.4f,size.z),rim);
                b.Box(roof+new Vector3(0,.5f,side*(size.z*.5f-.25f)),new Vector3(size.x,1.4f,.65f),rim);
            }
            b.Box(roof+new Vector3(-size.x*.25f,1.8f,-size.z*.22f),new Vector3(7,3.6f,8),rim*.8f);
            int units=QualitySettings.GetQualityLevel()<=2?2+seed%2:2+seed%5;
            for(int k=0;k<units;k++)
            {
                Vector3 hvac=roof+new Vector3((k%3-1)*8,1.6f,Mathf.Min(size.z*.5f-3,size.z*.16f+(k/3)*7));
                b.Box(hvac-Vector3.up*1.2f,new Vector3(6.5f,.8f,5.5f),new Color(.22f,.24f,.24f));
                b.Box(hvac,new Vector3(6,2.6f,5),new Color(.66f,.69f,.68f));
                for(int fan=0;fan<2;fan++)
                    b.Cylinder(hvac+new Vector3(fan*2.8f-1.4f,1.31f,0),1.15f,.16f,QualitySettings.GetQualityLevel()<=2?6:10,new Color(.16f,.20f,.21f));
                b.Box(hvac+new Vector3(0,-.3f,2.53f),new Vector3(4.8f,1.3f,.09f),new Color(.32f,.36f,.36f));
            }
            if(style==1 && (seed*37+seed/7)%10<3)
            {
                Vector3 tank=roof+new Vector3(size.x*.25f,0,-size.z*.24f);
                foreach(int x in new[]{-1,1}) foreach(int z in new[]{-1,1})
                    b.Box(tank+new Vector3(x*2.4f,2.2f,z*2.4f),new Vector3(.30f,4.4f,.30f),new Color(.23f,.24f,.23f));
                b.Cylinder(tank+Vector3.up*4.3f,3.8f,5.5f,10,new Color(.34f,.25f,.18f),new Color(.28f,.28f,.25f),1.8f);
                for(int band=0;band<3;band++)
                    b.Cylinder(tank+Vector3.up*(4.6f+band*2.35f),3.84f,.14f,10,new Color(.19f,.22f,.23f));
                foreach(int side in new[]{-1,1})
                    b.Beam(tank+new Vector3(-2.4f,.3f,side*2.4f),tank+new Vector3(2.4f,4.2f,side*2.4f),.16f,.16f,new Color(.23f,.24f,.23f));
            }
            if(size.y>220 && seed%3==0)
            {
                for(int k=0;k<3;k++)
                    b.Box(roof+Vector3.up*(2+k*3.5f),new Vector3(size.x*(.52f-k*.12f),4,size.z*(.52f-k*.12f)),rim*.8f);
                b.Cylinder(roof+Vector3.up*12,.3f,10,6,new Color(.43f,.46f,.46f));
            }
            if(size.y>230)
                b.Box(roof+new Vector3(size.x*.38f,1.2f,size.z*.38f),new Vector3(1.1f,.6f,1.1f),Emission(1,.035f,.015f));
        }
        static void Tree(SeasideBuilder.Batch b,Vector3 foot,int seed)
        {
            b.Box(foot+Vector3.up*2,new Vector3(.28f,4,.28f),new Color(.28f,.22f,.16f));
            // Six sided rounded crown: three latitude rings, opaque and combined.
            Color green=Color.Lerp(new Color(.18f,.28f,.13f),new Color(.32f,.40f,.19f),(seed%7)/6f);
            int sides=QualitySettings.GetQualityLevel()<=2?4:6;
            for(int k=0;k<sides;k++)
            {
                float a=k*Mathf.PI*2/sides, c=(k+1)*Mathf.PI*2/sides;
                Vector3 D(float angle,float radius,float y)=>foot+new Vector3(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);
                b.Tri(D(a,2.3f,4),foot+Vector3.up*3,D(c,2.3f,4),green*.85f);
                b.Quad(D(a,2.3f,4),D(a,3.3f,6),D(c,3.3f,6),D(c,2.3f,4),green);
                b.Tri(D(a,3.3f,6),foot+Vector3.up*8.6f,D(c,3.3f,6),green*1.08f);
            }
        }
        static readonly Color[] CarColours={new(.84f,.84f,.80f),new(.55f,.59f,.61f),new(.065f,.075f,.08f),new(.58f,.10f,.07f),new(.09f,.23f,.43f),new(.19f,.29f,.21f),new(.66f,.59f,.40f),new(.35f,.36f,.37f)};
        public static Color CarPaint(int seed)=>CarColours[Mathf.Abs(seed)%CarColours.Length];
        static void Car(SeasideBuilder.Batch b,Vector3 p,Quaternion q,int seed)
        {
            b.Box(p+Vector3.up*.62f,new Vector3(1.85f,.85f,4.5f),q,CarPaint(seed));
            b.Box(p+Vector3.up*1.22f,new Vector3(1.65f,.65f,2.2f),q,new Color(.13f,.22f,.25f));
            foreach(int end in new[]{-1,1})
                b.Box(p+q*new Vector3(0,.32f,end*1.35f),new Vector3(2.07f,.6f,.65f),q,new Color(.055f,.06f,.06f));
            foreach(int side in new[]{-1,1})
            {
                foreach(int end in new[]{-1,1})
                {
                    Vector3 P(float x,float y)=>p+q*new Vector3(side*.62f+x*.23f,.80f+y*.10f,end*2.27f);
                    b.Quad(P(-1,-1),P(1,-1),P(1,1),P(-1,1),end==1?Emission(1,.91f,.72f):Emission(1,.018f,.008f));
                }
            }
        }
        public static void Streets(FlyingGame.Core.CityGrid grid, double ground, SeasideBuilder.Batch b, UrbanFacades facades)
        {
            Vector3 P(double u,double v,double h) { var p=grid.Map(u,v); return WorldBuilder.U(p.x,p.y,ground+h); }
            // Longitudinal coordinates a (across street), z (along street) rotate through the public map.
            for(int axis=0;axis<2;axis++)
            {
                Vector3 P2(double a,double z,double h)=>axis==0?P(a,z,h):P(z,a,h);
                Vector3 S(float w,float h,float length)=>(grid.Rotated ^ (axis==1))?new Vector3(w,h,length):new Vector3(length,h,w);
                // Unity x corresponds to sim y; axis 0 runs along grid v.
                int count=axis==0?6:15, blocks=axis==0?16:7;
                for(int road=0;road<count;road++) for(int block=0;block<blocks;block++)
                {
                    double across=road*170+85, along=block*170;
                    var blockCentre=axis==0?grid.Map(across,along):grid.Map(along,across);
                    if(grid.InPlaza(blockCentre.x,blockCentre.y)) continue;
                    Vector3 forward=(P2(across,along+1,0)-P2(across,along,0)).normalized;
                    Vector3 right=Vector3.Cross(Vector3.up,forward);
                    int trafficStride=QualitySettings.GetQualityLevel()<=1?3:QualitySettings.GetQualityLevel()<=2?2:1;
                    if(block%trafficStride==0)
                    foreach(int direction in new[]{-1,1})
                    {
                        Vector3 centre=P2(across,along,.09)+right*(direction*4f);
                        facades.TrafficCar(centre,forward*direction,axis*2000+road*71+block*13+(direction+1)*5,114);
                    }
                    void Flat(Vector3 centre,Vector3 size,Color c)
                    {
                        Vector3 x=Vector3.right*size.x*.5f,z=Vector3.forward*size.z*.5f;
                        b.Quad(centre-x-z,centre-x+z,centre+x+z,centre+x-z,c);
                    }
                    void Mark(double a,double z,float w,float len,Color c)=>Flat(P2(a,z,.115),S(w,0,len),c);
                    foreach(int side in new[]{-1,1})
                    {
                        Mark(across+side*.32,along,.14f,130,new Color(.78f,.62f,.24f));
                        for(int dash=-4;dash<=4;dash++) Mark(across+side*7,along+dash*14,.18f,6,Paint);
                        // Raised five metre pedestrian strip, with continuous curb and expansion joints.
                        b.Box(P2(across+side*18,along,.19),S(5,.22f,140),new Color(.64f,.63f,.59f));
                        b.Box(P2(across+side*15.7,along,.18),S(.35f,.20f,140),new Color(.73f,.72f,.67f));
                        for(int joint=-9;joint<=9;joint++)
                            Flat(P2(across+side*18,along+joint*7,.307),S(4.7f,0,.045f),new Color(.44f,.44f,.41f));
                        // Tree rows: 14 m spacing, 28 m on Low. No alpha canopies or separate renderers.
                        int stride=QualitySettings.GetQualityLevel()<=1?2:1;
                        for(int k=-4;k<=4;k+=stride) Tree(b,P2(across+side*19,along+k*14,.31),road+block+k+10);
                        // Deterministic gaps along each curb, clear of crosswalks.
                        for(int parked=0;parked<(QualitySettings.GetQualityLevel()<=1?1:QualitySettings.GetQualityLevel()<=2?2:3);parked++)
                            if((road*7+block+parked)%3!=0)
                                Car(b,P2(across+side*12.5,along-36+parked*25,.09),Quaternion.LookRotation(forward*side,Vector3.up),road*17+block+parked);
                        // Four lamps on each side per block: road pools overlap gently to reveal the grid.
                        int lamps=QualitySettings.GetQualityLevel()<=2?2:4;
                        for(int k=0;k<lamps;k++)
                        {
                            double z=along-70+(k+.5)*140/lamps;
                            Vector3 foot=P2(across+side*17,z,.31), head=P2(across+side*13,z,8.7);
                            b.Box(foot+Vector3.up*4.1f,new Vector3(.18f,8.2f,.18f),new Color(.27f,.29f,.28f));
                            b.Beam(foot+Vector3.up*8.2f,head,.18f,.18f,new Color(.31f,.32f,.30f));
                            Color lamp=Emission(1,road%3==0?.69f:.87f,road%3==0?.38f:.73f);
                            b.Box(head,S(1.8f,.25f,.9f),lamp);
                            facades.Pool(P2(across+side*8,z,.09),S(14,0,140f/lamps-.5f),lamp);
                        }
                    }
                    // Both approaches of each junction receive zebra stripes and stop bars.
                    foreach(int end in new[]{-1,1})
                    {
                        for(int stripe=-5;stripe<=5;stripe++) Mark(across+stripe*2.4,along+end*65,1.25f,4,Paint);
                        Mark(across,along+end*60,25,.35f,Paint);
                    }
                }
            }
        }
    }
}
