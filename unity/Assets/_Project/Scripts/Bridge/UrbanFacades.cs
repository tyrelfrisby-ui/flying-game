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
                    vertices.Add(bottom + q * new Vector3(v.x-w*.5f,v.y,d*.5f+.14f));
                    normals.Add(q * Vector3.forward); uv.Add(v);
                    data.Add(new Vector2(style,seed)); colours.Add(wall); travel.Add(Vector4.zero);
                }
                indices.AddRange(new[] { start,start+1,start+2,start,start+2,start+3 });
            }
        }
        // Opaque road tile: analytic light falloff shares the facade material and draw.
        public void Pool(Vector3 centre, Vector3 size, Color light, int style = 5)
        {
            int start=vertices.Count;
            Vector2[] corners={new(-1,-1),new(-1,1),new(1,1),new(1,-1)};
            foreach(var v in corners)
            {
                vertices.Add(centre+new Vector3(v.x*size.x*.5f,0,v.y*size.z*.5f));
                normals.Add(Vector3.up); uv.Add(v); data.Add(new Vector2(style,0)); colours.Add(light); travel.Add(Vector4.zero);
            }
            indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        // Animated traffic is baked into this same opaque draw. Only vertex positions move;
        // no transforms, Update loop, colliders, allocations or per-car draw submissions.
        public void TrafficCar(Vector3 centre, Vector3 forward, int seed, float length, float phaseSlot = -1)
        {
            Quaternion q=Quaternion.LookRotation(forward,Vector3.up);
            void Box(Vector3 offset,Vector3 size,Color colour)
            {
                Vector3 h=size*.5f;
                Vector3 P(float x,float y,float z)
                {
                    float taper=offset.y>1.1f && y>0?.86f:1;
                    return centre+q*(offset+Vector3.Scale(h,new Vector3(x*taper,y,z*taper)));
                }
                void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
                {
                    int start=vertices.Count;
                    Vector3 n=Vector3.Cross(b-a,d-a).normalized;
                    foreach(var v in new[]{a,b,c,d})
                    {
                        vertices.Add(v); normals.Add(n); uv.Add(Vector2.zero);
                        data.Add(new Vector2(6,seed)); colours.Add(colour);
                        travel.Add(new Vector4(forward.x,phaseSlot,forward.z,length));
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
                        travel.Add(new Vector4(forward.x,phaseSlot,forward.z,length));
                    }
                    indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                }
            }
        }
        // Ground-projected exposure streaks, baked into the traffic draw and travel envelope.
        public void TrafficStreak(Vector3 centre, Vector3 forward, int seed, float phaseSlot)
        {
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            foreach(int end in new[]{-1,1}) foreach(int side in new[]{-1,1})
            {
                int start=vertices.Count;
                foreach(var corner in new[]{new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,1),new Vector2(1,-1)})
                {
                    vertices.Add(centre+right*(side*.62f+corner.x*.18f)+forward*(end*5+corner.y*3));
                    normals.Add(Vector3.up); uv.Add(corner); data.Add(new Vector2(7,seed));
                    colours.Add(end==1?new Color(1,.83f,.52f):new Color(1,.025f,.009f));
                    travel.Add(new Vector4(forward.x,phaseSlot,forward.z,114));
                }
                indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
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
        static readonly Color[] Membranes={new(.13f,.15f,.16f),new(.59f,.56f,.48f),new(.30f,.36f,.25f),new(.38f,.43f,.45f),new(.49f,.43f,.34f)};
        static Color Emission(float r,float g,float b) => new Color(r,g,b,0);
        public static void Lot(SeasideBuilder.Batch b, UrbanFacades facades, Vector3 bottom, Vector3 size, int i, int j, int style, bool protectedLot)
        {
            // Original bodies are authored by PlaygroundBuilder and may carry sim challenges.
            // This additive shell never edits their dimensions, collision, pad or ring surfaces.
            if(!protectedLot) Massing(b,facades,bottom,size,i,j,style);
            if(protectedLot) return;
            // Small mixed-use infill occupies the existing sidewalk lot, clear of the 30 m road.
            float core=1-Mathf.Clamp01(Mathf.Max(Mathf.Abs(i-3)/3f,Mathf.Abs(j-7.5f)/7.5f));
            bool house = i==0 || i==6 || j==0 || j==15;
            Vector3 shop=bottom+new Vector3(0,0,-58);
            float h=house?7:12+core*32+(i+j)%3*3;
            Vector3 shopSize=new Vector3(house?17:Mathf.Lerp(42,104,core),h,house?15:20);
            Color wall=house?new Color(.69f,.66f,.55f):new Color(.46f,.28f,.21f);
            b.Box(shop+Vector3.up*h*.5f,shopSize,wall);
            facades.Add(shop,shopSize,house?4:1,i*37+j*11,wall);
            if(house)
            {
                PitchedRoof(b,shop+Vector3.up*h,shopSize,i*53+j*19,wall);
            }
            else
            {
                if(h<=18)
                    PitchedRoof(b,shop+Vector3.up*h,shopSize,i*53+j*19,wall);
                else
                {
                    // Flat roofs remain on taller mixed-use blocks.
                    b.Box(shop+Vector3.up*(h+.02f),new Vector3(shopSize.x-1,.04f,19),new Color(.30f,.32f,.30f));
                    for(int side=-1;side<=1;side+=2)
                    {
                        b.Box(shop+new Vector3(side*(shopSize.x*.5f-.3f),h+.35f,0),new Vector3(.5f,.7f,20),wall*.85f);
                        b.Box(shop+new Vector3(0,h+.35f,side*9.7f),new Vector3(shopSize.x,.7f,.5f),wall*.85f);
                    }
                    b.Box(shop+new Vector3(-11,h+1,3),new Vector3(5,2,6),new Color(.38f,.37f,.33f));
                    for(int k=0;k<2;k++) b.Box(shop+new Vector3(k*7,h+.8f,3),new Vector3(4,1.6f,4),new Color(.48f,.50f,.48f));
                    if((i+j)%5==0)
                    {
                        Vector3 tank=shop+new Vector3(10,h, -4);
                        for(int leg=-1;leg<=1;leg+=2) b.Box(tank+new Vector3(leg*1.3f,1,0),new Vector3(.2f,2,.2f),new Color(.23f,.23f,.21f));
                        b.Cylinder(tank+Vector3.up*2,2,3,8,new Color(.32f,.25f,.18f),new Color(.22f,.22f,.20f),.7f);
                    }
                }
                Color awning=(i%2==0)?new Color(.18f,.31f,.25f):new Color(.51f,.20f,.14f);
                foreach(int front in new[]{-1,1})
                {
                    b.Box(shop+new Vector3(0,4.8f,front*11),new Vector3(shopSize.x-1,.3f,3),awning);
                    b.Box(shop+new Vector3(0,4.2f,front*12.4f),new Vector3(shopSize.x-1,1.0f,.2f),awning*.8f);
                    // Door surround and transom at pavement level, on both shop frontages.
                    b.Box(shop+new Vector3(-9,1.8f,front*10.08f),new Vector3(2.2f,3.2f,.16f),new Color(.16f,.20f,.20f));
                    b.Box(shop+new Vector3(-9,3.6f,front*10.18f),new Vector3(2.6f,.22f,.25f),new Color(.67f,.62f,.52f));
                }
            }
            // Low street walls follow a density gradient without changing any tower or collider.
            // Their front/back strips stay within the existing 140 m block, clear of avenues.
            if(!house && core>.25f)
            {
                Vector3 rear=bottom+new Vector3(0,0,58);
                Vector3 rearSize=new Vector3(Mathf.Lerp(46,108,core),h*.72f,18);
                Color stone=(i+j)%2==0?new Color(.55f,.51f,.43f):new Color(.37f,.32f,.28f);
                b.Box(rear+Vector3.up*rearSize.y*.5f,rearSize,stone);
                facades.Add(rear,rearSize,(i+j)%2==0?3:1,i*53+j*19+401,stone);
                if(rearSize.y<=18)
                    PitchedRoof(b,rear+Vector3.up*rearSize.y,rearSize,i*53+j*19+401,stone);
                else Roof(b,rear+Vector3.up*rearSize.y,rearSize,3,i*53+j*19+401);
                if(core>.55f && size.x<94)
                {
                    Vector3 side=bottom+new Vector3(-58,0,0);
                    Vector3 sideSize=new Vector3(18,h*.60f,100);
                    b.Box(side+Vector3.up*sideSize.y*.5f,sideSize,stone*.90f);
                    facades.Add(side,sideSize,3,i*53+j*19+607,stone*.90f);
                    b.Box(side+Vector3.up*(sideSize.y+.10f),new Vector3(17,.16f,99),new Color(.26f,.32f,.23f));
                }
            }
            if(house)
            {
                // Repeated small houses, rather than another low slab at the perimeter.
                foreach(int side in new[]{-1,1})
                {
                    Vector3 foot=shop+new Vector3(side*36,0,0);
                    Vector3 hs=new Vector3(19,6+(i+j+side+2)%3,16);
                    Color siding=(i+j+side)%2==0?new Color(.58f,.61f,.56f):new Color(.64f,.53f,.41f);
                    b.Box(foot+Vector3.up*hs.y*.5f,hs,siding);
                    facades.Add(foot,hs,4,i*53+j*19+side*101,siding);
                    PitchedRoof(b,foot+Vector3.up*hs.y,hs,i*53+j*19+side*101,siding);
                }
            }
            // Forecourt parking stays between the shop strip and main shaft, clear of the
            // new side wings. Stepped podium lots have no surface bays under the building.
            bool podiumLot=(size.y>90 && (i*7+j*3)%5<2) || ((i==2 || i==4) && (j==6 || j==9));
            if(!podiumLot && size.z<62)
            for(int k=0;k<(QualitySettings.GetQualityLevel()<=2?4:6);k++)
            {
                Vector3 car=bottom+new Vector3(-30+k*12,.30f,-40);
                if((i+j+k)%4!=0) Car(b,car,Quaternion.Euler(0,90,0),i*29+j*7+k);
                b.Box(car+new Vector3(5,-.005f,0),new Vector3(.13f,.02f,5),Paint);
            }
        }

        // All architecture is baked into the two existing city batches. No per-lot renderer.
        static void Massing(SeasideBuilder.Batch b,UrbanFacades facades,Vector3 bottom,Vector3 size,int i,int j,int style)
        {
            int seed=i*53+j*19;
            float core=1-Mathf.Clamp01(Mathf.Max(Mathf.Abs(i-3)/3f,Mathf.Abs(j-7.5f)/7.5f));
            Color wall=style==0?new Color(.28f,.36f,.38f):style==1?new Color(.49f,.30f,.23f)
                :style==2?new Color(.59f,.59f,.55f):new Color(.55f,.49f,.41f);
            wall*=.92f+seed%11*.016f;
            void Block(Vector3 foot,Vector3 extent,int kind,Color paint,bool dress=true)
            {
                b.Box(foot+Vector3.up*extent.y*.5f,extent,paint);
                facades.Add(foot,extent,kind,seed,paint);
                if(dress)
                {
                    Vector3 roof=foot+Vector3.up*extent.y;
                    if(extent.y<=18)
                    {
                        PitchedRoof(b,roof,extent,seed,paint);
                        return;
                    }
                    Terrace(b,roof,extent,seed);
                    // Wing roofs have bounded service geometry; tanks remain on main roofs.
                    b.Box(roof+new Vector3(-extent.x*.25f,1.5f,-extent.z*.25f),new Vector3(4,3,5),paint*.82f);
                    int units=QualitySettings.GetQualityLevel()<=2?1:2;
                    for(int unit=0;unit<units;unit++)
                    {
                        Vector3 hvac=roof+new Vector3(0,1.1f,(unit-.5f)*7);
                        b.Box(hvac,new Vector3(5,2.2f,5),new Color(.60f,.63f,.61f));
                        b.Cylinder(hvac+Vector3.up*1.12f,1.6f,.16f,6,new Color(.16f,.20f,.21f));
                    }
                }
            }
            // Forty percent of the deterministic mid/high-rise families get genuine stacked
            // volumes. Their original cores remain inside; the upper extension creates the
            // central height profile rather than merely widening the old rectangular shaft.
            bool stepped=size.y>90 && (i*7+j*3)%5<2;
            bool pinnacle=(i==2 || i==4) && (j==6 || j==9);
            float height=Mathf.Max(size.y,70+Mathf.Pow(core,1.45f)*460);
            if(pinnacle) height=Mathf.Max(height,450+((i+j)%3)*25);
            if(stepped || pinnacle)
            {
                float podiumHeight=10+(seed%4)*3.3f;
                Vector3 podium=new Vector3(Mathf.Min(128,size.x+54),podiumHeight,Mathf.Min(128,size.z+54));
                Block(bottom,podium,3,Color.Lerp(wall,new Color(.58f,.52f,.43f),.55f),false);
                Terrace(b,bottom+Vector3.up*podiumHeight,podium,seed);
                float previous=podiumHeight;
                for(int tier=0;tier<3;tier++)
                {
                    float spread=36-tier*18;
                    float top=height*(tier==0?.42f:tier==1?.73f:1);
                    Vector3 extent=new Vector3(Mathf.Min(128,size.x+spread+.6f),top-previous,Mathf.Min(128,size.z+spread+.6f));
                    Vector3 foot=bottom+Vector3.up*previous;
                    Block(foot,extent,style,wall,false);
                    Terrace(b,bottom+Vector3.up*top,extent,seed+tier);
                    if(tier==2)
                    {
                        // Dress the actual top, not the roof buried inside the new shell.
                        Vector3 roofSize=new Vector3(extent.x,height,extent.z);
                        Roof(b,bottom+Vector3.up*top,roofSize,style,seed);
                        if(style!=1)
                            Crown(b,bottom+Vector3.up*(top+.3f)+new Vector3(extent.x*.12f,0,-extent.z*.16f),
                                extent*.80f,24+core*22,seed,pinnacle);
                        else if(pinnacle)
                        {
                            // Brick tank stays at the rear-right; mast occupies the left edge.
                            Vector3 mast=bottom+new Vector3(-extent.x*.42f,top+1,0);
                            b.Cylinder(mast,.65f,42,6,new Color(.54f,.55f,.52f));
                            b.Cylinder(mast+Vector3.up*42,.95f,1.2f,6,Emission(1,.035f,.015f));
                        }
                    }
                    previous=top;
                }
            }
            else
            {
                Vector3 roof=bottom+Vector3.up*size.y;
                if(size.y<=18)
                    PitchedRoof(b,roof,size,seed,wall);
                else Roof(b,roof,size,style,seed);
                if(size.y>18 && (core<.36f || size.y<100))
                {
                    // Preserve C7's taller peripheral cap and its clear service strip.
                    Vector3 cap=new Vector3(size.x*(style==1?.45f:.94f),0,size.z*.60f);
                    PitchedRoof(b,roof+new Vector3(style==1?-size.x*.23f:0,1,-size.z*.14f),cap,seed,wall);
                }
                else if(size.y>18 && style!=1)
                    Crown(b,roof+new Vector3(size.x*.12f,.3f,-size.z*.16f),size*.80f,18+core*22,seed,false);

                // L, U, enclosed courtyard and twin slab families around the retained shaft.
                // Low wings frame the shaft; new volumes stay inside the 140 m block.
                float wingHeight=Mathf.Min(size.y*.70f,18+core*86);
                float edge=Mathf.Max(size.x,size.z)*.5f;
                float room=67-edge;
                if(room>=16)
                {
                    int family=seed%4;
                    float depth=Mathf.Min(24,room-2);
                    float offset=67-depth*.5f;
                    Color masonry=Color.Lerp(wall,new Color(.51f,.43f,.34f),.55f);
                    Vector3 bar=new Vector3(depth,wingHeight,Mathf.Min(94,size.z+24));
                    Block(bottom+new Vector3(-offset,0,0),bar,style==0?2:1,masonry);
                    if(family==0)
                    {
                        // Join the long side slab to one corner of the shaft: a real L,
                        // rather than two disconnected rectangular buildings on the lot.
                        float inner=-67+depth;
                        float gap=-size.x*.5f-inner;
                        Vector3 link=new Vector3(gap+2,wingHeight,depth);
                        Vector3 foot=bottom+new Vector3((inner-size.x*.5f)*.5f,0,size.z*.5f-depth*.5f);
                        Block(foot,link,3,masonry,false);
                        Terrace(b,foot+Vector3.up*link.y,link,seed);
                    }
                    if(family!=0)
                        Block(bottom+new Vector3(offset,0,0),new Vector3(depth,wingHeight*(family==3?.83f:1),bar.z),3,masonry*.94f);
                    if(family==1 || family==2)
                    {
                        Vector3 link=new Vector3(offset*2-depth,wingHeight*.82f,depth);
                        Block(bottom+new Vector3(0,0,-offset),link,1,masonry);
                        if(family==2) Block(bottom+new Vector3(0,0,offset),link,3,masonry*.94f);
                    }
                }
            }
        }
        static void Terrace(SeasideBuilder.Batch b,Vector3 top,Vector3 size,int seed)
        {
            Color membrane=Membranes[Mathf.Abs(seed)%Membranes.Length];
            b.Box(top+Vector3.up*.12f,new Vector3(size.x-.3f,.18f,size.z-.3f),membrane);
            Color stone=new Color(.49f,.48f,.44f);
            foreach(int side in new[]{-1,1})
            {
                b.Box(top+new Vector3(side*(size.x*.5f-.2f),.5f,0),new Vector3(.4f,1,size.z),stone);
                b.Box(top+new Vector3(0,.5f,side*(size.z*.5f-.2f)),new Vector3(size.x,1,.4f),stone);
            }
        }
        static readonly Color[] PitchedColours={
            new(.19f,.22f,.25f), new(.39f,.20f,.14f), new(.31f,.25f,.19f),
            new(.24f,.34f,.29f), new(.53f,.56f,.55f)};
        static void PitchedRoof(SeasideBuilder.Batch b,Vector3 foot,Vector3 size,int seed,Color wall,bool district=false)
        {
            int variant=Mathf.Abs(seed);
            // Local z is always the long axis. Overhang is measured from the wall,
            // not a percentage of its width; broad warehouse roofs retain a real pitch.
            bool turn=size.x>size.z;
            Quaternion q=turn?Quaternion.Euler(0,90,0):Quaternion.identity;
            float halfWidth=Mathf.Min(size.x,size.z)*.5f;
            float halfLength=Mathf.Max(size.x,size.z)*.5f;
            float eave=district?(halfWidth<=4?.5f:1.05f):.3f+(variant%4)*.1f;
            float x=halfWidth+eave,z=halfLength+eave;
            float pitch=(district?36:25+variant%16)*Mathf.Deg2Rad;
            float rise=x*Mathf.Tan(pitch);
            // Put the eave just below the wall top: roof planes intersect the body
            // at the wall and leave no bright floating slit under the overhang.
            Vector3 P(float u,float y,float v)=>foot+q*new Vector3(u,y-eave*Mathf.Tan(pitch),v);
            Vector3 a=P(-x,0,-z),c=P(x,0,-z),d=P(-x,0,z),e=P(x,0,z);
            Color tile=PitchedColours[district?(variant%10<6?0:variant%10<9?2:1):variant%PitchedColours.Length];
            bool hip=!district && variant%3==0;
            float inset=hip?x:0;
            Vector3 r=P(0,rise,-z+inset),t=P(0,rise,z-inset);
            b.Quad(a,d,t,r,tile); b.Quad(r,t,e,c,tile);
            if(hip)
            {
                b.Tri(a,r,c,tile); b.Tri(d,e,t,tile);
            }
            else
            {
                // Wall-aligned gable ends stop at the roof slope, inside the eaves.
                float wallRise=halfWidth*Mathf.Tan(pitch);
                Vector3 A(float u,float y,float v)=>foot+q*new Vector3(u,y,v);
                b.Tri(A(-halfWidth,0,-halfLength),A(0,wallRise,-halfLength),A(halfWidth,0,-halfLength),wall);
                b.Tri(A(-halfWidth,0,halfLength),A(halfWidth,0,halfLength),A(0,wallRise,halfLength),wall);
            }
            // A narrow fascia makes the overhang readable without extra materials.
            foreach(int side in new[]{-1,1})
            {
                Vector3 centre=P(side*x,-.13f,0);
                b.Box(centre,new Vector3(.16f,.26f,2*z),q,tile*.8f);
            }
            if(size.y>0 && variant%2==0)
            {
                Vector3 chimney=foot+q*new Vector3(halfWidth*.45f,0,-halfLength*.3f);
                float top=halfWidth*.55f*Mathf.Tan(pitch)+1.6f;
                b.Box(chimney+Vector3.up*top*.5f,new Vector3(1.1f,top,1.1f),new Color(.38f,.25f,.20f));
            }
        }
        static void Crown(SeasideBuilder.Batch b,Vector3 foot,Vector3 size,float height,int seed,bool mast)
        {
            // Clipped rectangular footprint, four broad sloping faces and four chamfer faces.
            // Cyclic footprint is clockwise from above for outward wall normals.
            float x=size.x*.40f,z=size.z*.32f,clip=Mathf.Min(x,z)*.30f;
            Vector3[] ring={new(-x+clip,0,-z),new(-x,0,-z+clip),new(-x,0,z-clip),new(-x+clip,0,z),
                new(x-clip,0,z),new(x,0,z-clip),new(x,0,-z+clip),new(x-clip,0,-z)};
            Color metal=seed%3==0?new Color(.31f,.35f,.35f):seed%3==1?new Color(.46f,.43f,.36f):new Color(.22f,.31f,.31f);
            float inset=seed%2==0?.48f:.16f;
            for(int k=0;k<8;k++)
            {
                Vector3 a=ring[k],c=ring[(k+1)%8];
                Vector3 u=a*inset+Vector3.up*height,v=c*inset+Vector3.up*height;
                b.Quad(foot+a,foot+c,foot+v,foot+u,metal*(k%2==0?1:.88f));
                b.Tri(foot+Vector3.up*height,foot+u,foot+v,metal*.86f);
            }
            if(mast)
            {
                b.Box(foot+Vector3.up*(height+2),new Vector3(6,4,6),metal);
                b.Cylinder(foot+Vector3.up*(height+4),.65f,38,6,new Color(.54f,.55f,.52f));
                b.Cylinder(foot+Vector3.up*(height+42),.95f,1.2f,6,Emission(1,.035f,.015f));
            }
        }

        static void Roof(SeasideBuilder.Batch b,Vector3 roof,Vector3 size,int style,int seed)
        {
            Color membrane=Membranes[Mathf.Abs(seed)%Membranes.Length];
            b.Box(roof+Vector3.up*.16f,new Vector3(size.x-.7f,.16f,size.z-.7f),membrane);
            Color rim=new(.49f,.48f,.43f);
            foreach(int side in new[]{-1,1})
            {
                b.Box(roof+new Vector3(side*(size.x*.5f-.25f),.5f,0),new Vector3(.65f,1.4f,size.z),rim);
                b.Box(roof+new Vector3(0,.5f,side*(size.z*.5f-.25f)),new Vector3(size.x,1.4f,.65f),rim);
            }
            b.Box(roof+new Vector3(-size.x*.30f,2.5f,-size.z*.30f),new Vector3(8,5,9),rim*.8f);
            int units=Mathf.Min(size.z<28?3:QualitySettings.GetQualityLevel()<=2?3:4+seed%3,
                size.x<30?2:6);
            for(int k=0;k<units;k++)
            {
                float serviceX=size.x<30?0:(k%3-1)*Mathf.Min(10,(size.x-10)*.33f);
                float serviceZ=size.x<30?(k-.5f)*10:Mathf.Min(size.z*.5f-5,size.z*.30f+(k/3)*10);
                Vector3 hvac=roof+new Vector3(serviceX,2.3f,serviceZ);
                b.Box(hvac-Vector3.up*1.45f,new Vector3(9.5f,1.2f,8.5f),new Color(.22f,.24f,.24f));
                b.Box(hvac,new Vector3(8.5f,3.4f,7.5f),new Color(.66f,.69f,.68f));
                for(int fan=0;fan<2;fan++)
                    b.Cylinder(hvac+new Vector3(fan*4f-2f,1.71f,0),1.6f,.16f,QualitySettings.GetQualityLevel()<=2?6:10,new Color(.16f,.20f,.21f));
                b.Box(hvac+new Vector3(0,-.3f,3.78f),new Vector3(7,1.8f,.09f),new Color(.32f,.36f,.36f));
            }
            if(style==1 && seed%3!=1)
            {
                Vector3 tank=roof+new Vector3(size.x*.22f,.24f,-size.z*.28f);
                foreach(int x in new[]{-1,1}) foreach(int z in new[]{-1,1})
                    b.Box(tank+new Vector3(x*2.4f,2.2f,z*2.4f),new Vector3(.30f,4.4f,.30f),new Color(.23f,.24f,.23f));
                b.Cylinder(tank+Vector3.up*4.3f,4.8f,7.5f,10,new Color(.34f,.25f,.18f),new Color(.28f,.28f,.25f),2.4f);
                for(int band=0;band<3;band++)
                    b.Cylinder(tank+Vector3.up*(4.6f+band*3.25f),4.84f,.14f,10,new Color(.19f,.22f,.23f));
                foreach(int side in new[]{-1,1})
                    b.Beam(tank+new Vector3(-2.4f,.3f,side*2.4f),tank+new Vector3(2.4f,4.2f,side*2.4f),.16f,.16f,new Color(.23f,.24f,.23f));
            }
            if(size.y>230)
                b.Box(roof+new Vector3(size.x*.38f,.54f,size.z*.38f),new Vector3(1.1f,.6f,1.1f),Emission(1,.035f,.015f));
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
            b.Box(p+q*new Vector3(0,1.37f,-.25f),new Vector3(1.55f,.78f,2.15f),q,new Color(.13f,.22f,.25f));
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
        // Visual-only low-rise infill. Uses existing paved blocks/lawns, never the forest,
        // roads, plaza, or any challenge row. Bodies/facades/roofs share the two city batches.
        static void RoofDistrict(FlyingGame.Core.CityGrid grid,double ground,SeasideBuilder.Batch b,UrbanFacades facades)
        {
            var occupied=new Dictionary<(int,int),FlyingGame.Core.FlyCity.Tower>();
            foreach(var tower in grid.Towers()) occupied[(tower.I,tower.J)]=tower;
            var reservedRows=new HashSet<int>();
            if(grid.PadLot.HasValue) reservedRows.Add(grid.PadLot.Value.j);
            reservedRows.Add(grid.Tallest.J);
            foreach(var ring in grid.RooftopRings()) reservedRows.Add(ring.t.J);
            int count=0,emptyBlocks=0,bodyStart=b.Count;
            bool roofChimneys=QualitySettings.GetQualityLevel()>2;
            float area=0;
            void Building(Vector3 foot,Vector3 size,int seed,int kind,int roofSeed)
            {
                // Pale plaster, buff stone and muted brick: the roof carries the silhouette.
                Color wall=kind==1?new Color(.66f,.53f,.43f):kind==3?new Color(.72f,.67f,.57f)
                    :seed%3==0?new Color(.74f,.73f,.66f):seed%3==1?new Color(.73f,.66f,.55f):new Color(.66f,.68f,.65f);
                b.Box(foot+Vector3.up*size.y*.5f,size,wall);
                facades.Add(foot,size,kind,seed,wall);
                Vector3 roofSize=size;
                if(!roofChimneys) roofSize.y=0; // C8 builder omits optional chimneys at zero metadata height.
                PitchedRoof(b,foot+Vector3.up*size.y,roofSize,roofSeed,wall,true);
                if(kind==3)
                {
                    Color awning=seed%2==0?new Color(.19f,.34f,.27f):new Color(.49f,.22f,.16f);
                    b.Box(foot+new Vector3(0,3,-size.z*.5f-.7f),new Vector3(size.x-.5f,.22f,1.6f),awning);
                }
                count++; area+=size.x*size.z;
            }
            for(int i=0;i<7;i++) for(int j=0;j<16;j++)
            {
                if(reservedRows.Contains(j)) continue;
                var map=grid.Map(i*170,j*170);
                if(grid.InPlaza(map.x,map.y)) continue;
                Vector3 centre=WorldBuilder.U(map.x,map.y,ground+.33);
                if(!occupied.TryGetValue((i,j),out var tower))
                {
                    // Six garden rows of seven separate roofs fill each existing open block.
                    // 18 m frontage / 21 m depth leaves gaps, garden strips and rear access.
                    emptyBlocks++;
                    // Flush garden/path dressing uses the same opaque colour batch.
                    Vector3 garden=centre-Vector3.up*.18f;
                    b.Quad(garden+new Vector3(-64,0,-64),garden+new Vector3(-64,0,64),
                        garden+new Vector3(64,0,64),garden+new Vector3(64,0,-64),new Color(.33f,.39f,.28f));
                    for(int row=0;row<6;row++)
                    {
                        Vector3 path=garden+new Vector3(0,.025f,-43.5f+row*21);
                        b.Quad(path+new Vector3(-63,0,-1),path+new Vector3(-63,0,1),
                            path+new Vector3(63,0,1),path+new Vector3(63,0,-1),new Color(.57f,.55f,.49f));
                    }
                    for(int row=0;row<6;row++) for(int col=0;col<7;col++)
                    {
                        int seed=1009+i*997+j*71+row*13+col*17;
                        bool warehouse=row==4 && col==5 && (i+j)%3==0;
                        // A single warehouse replaces four house slots, with no overlapping roofs.
                        if((i+j)%3==0 && row>=4 && col>=5 && !warehouse) continue;
                        int kind=row==0 && col%3==0?3:col%3==1?1:4;
                        Vector3 size=warehouse?new Vector3(29,5,18)
                            :new Vector3(kind==1?8:10+seed%5,kind==3?9:3.1f*(1+seed%2),kind==3?11:14+seed%3);
                        Vector3 offset=new Vector3(-54+col*18,0,-52.5f+row*21);
                        if(warehouse) offset+=new Vector3(9,0,10.5f);
                        if(kind==1 && !warehouse)
                        {
                            // Paired narrow rowhouses read as a terrace with individual ridges.
                            foreach(int side in new[]{-1,1})
                                Building(centre+offset+new Vector3(side*4.6f,0,0),size,seed+side*103,kind,i*3+j+row);
                        }
                        else Building(centre+offset,size,seed,warehouse?2:kind,i*3+j+row);
                    }
                }
                else
                {
                    // Peripheral rear lawns only. Lot's shops are at world z=-58;
                    // its side wings end by z=47. Family 2 has a rear crossbar: exclude it.
                    float core=1-Mathf.Clamp01(Mathf.Max(Mathf.Abs(i-3)/3f,Mathf.Abs(j-7.5f)/7.5f));
                    int seed=i*53+j*19;
                    if(core>.25f || seed%4==2 || tower.HeightM>=330) continue;
                    for(int col=0;col<5;col++)
                        Building(centre+new Vector3(-36+col*18,0,57),
                            new Vector3(10+col%3,3.1f*(1+(col+i+j)%2),12),2003+seed*7+col*23,4,i+j);
                }
            }
            // The town camera is south of the pad row, behind the forest belt. Put the
            // visible suburb on the open eastern flank, beyond the pad/approach columns.
            // Do not hide registered forest: reject whole gardens containing a tree crown.
            // All positions are outside the original city blocks and protected challenge rows.
            int flankCount=0;
            var terrain=FlyingGame.Core.WorldTerrain.Active;
            if(grid.Rotated && terrain!=null)
            {
                for(int row=0;row<6;row++) for(int col=0;col<6;col++)
                {
                    double u=1190+col*76, v=-590+row*70;
                    var map=grid.Map(u,v);
                    bool terrace=(row+col)%3!=0;
                    Vector3 size=terrace?new Vector3(52,7,17):new Vector3(18,6.4f,16);
                    // Garden includes eaves and a crown buffer. Existing forest is untouched.
                    double hx=27,hy=size.x*.5+12;
                    bool blocked=false;
                    foreach(var tr in FlyingGame.Core.Forest.InRect(map.x-hx-12,map.y-hy-12,map.x+hx+12,map.y+hy+12))
                        if(System.Math.Abs(tr.X-map.x)<hx+tr.CrownRadiusM && System.Math.Abs(tr.Y-map.y)<hy+tr.CrownRadiusM)
                        { blocked=true; break; }
                    double g=terrain.HeightAt(map.x,map.y);
                    if(blocked || System.Math.Abs(g-ground)>3) continue;
                    // Keep gardens on genuinely flat lawn; no terrain cuts or raised pads.
                    foreach(int dx in new[]{-1,1}) foreach(int dy in new[]{-1,1})
                        if(System.Math.Abs(terrain.HeightAt(map.x+dx*hx,map.y+dy*hy)-g)>.3) blocked=true;
                    if(blocked) continue;
                    Vector3 foot=WorldBuilder.U(map.x,map.y,g+.34);
                    // Continuous 52 m terraces have one shared gable ridge; detached homes
                    // have 58 m clear frontage gaps. Both face the same garden frontage.
                    Vector3 garden=foot-Vector3.up*.17f;
                    float gx=size.x*.5f+8,gz=27;
                    b.Quad(garden+new Vector3(-gx,0,-gz),garden+new Vector3(-gx,0,gz),
                        garden+new Vector3(gx,0,gz),garden+new Vector3(gx,0,-gz),new Color(.31f,.40f,.25f));
                    Building(foot,size,7001+row*37+col*13,terrace?1:4,row+col/3);
                    // Short door paths, not paved parking lots.
                    int doors=terrace?6:1;
                    for(int door=0;door<doors;door++)
                    {
                        float x=(door-(doors-1)*.5f)*8;
                        b.Box(foot+new Vector3(x,.01f,-15),new Vector3(1.25f,.07f,12),new Color(.54f,.51f,.44f));
                    }
                    Tree(b,foot+new Vector3(gx-3,0,19),row*31+col);
                    flankCount++;
                }
            }
            // C11: C10's close southern flank is mostly OUTSIDE the town lens. Retain
            // every surviving C10 body, and extend along the eastern edge instead.
            // Map axes are sim (north,east), U swaps these into Unity (east,up,north).
            // This band is beyond the last tower/road column and every protected row.
            int extension=0, candidates=0, treesRejected=0, terrainRejected=0;
            int waterRejected=0, airportRejected=0, viewRejected=0, sightRejected=0;
            int extensionStart=b.Count,extensionEnd=b.Count;
            float extensionArea=0;
            int nearCount=0,nearTested=0,nearForest=0,nearGround=0,nearView=0,nearSight=0,nearOverlap=0;
            int laneSegments=0,laneRejected=0,streetTrees=0;
            var extensionRows=new Dictionary<int,double>();
            var nearLanes=new Dictionary<double,(double min,double max)>();
            if(grid.Rotated && terrain!=null)
            {
                // Exact town eye/look, translated with the city's plateau. Never move a camera.
                Vector3 eye=WorldBuilder.U(grid.X0-900,grid.Y0+780,ground+95.2);
                Vector3 aim=WorldBuilder.U(grid.X0+1050,grid.Y0+780,ground-244.8);
                Vector3 forward=(aim-eye).normalized;
                Vector3 right=Vector3.Cross(Vector3.up,forward).normalized;
                Vector3 up=Vector3.Cross(forward,right);
                const float tanHalfFov=.46630766f, aspect=1.6f;
                // Generate the sight corridor's registered trees once, not once per lot.
                // Temporary CPU list only; never added to the mesh or collision registry.
                var sightTrees=new List<FlyingGame.Core.Forest.Tree>();
                foreach(var tr in FlyingGame.Core.Forest.InRect(grid.X0-912,grid.Y0-40,
                    grid.X0+1352,grid.Y0+2084)) sightTrees.Add(tr);
                // Reject a lot whose roof cannot be seen above actual terrain/registered
                // forest. No visual clearing of trees that still have sim collision.
                bool ClearSight(Vector3 roof)
                {
                    Vector3 ray=roof-eye;
                    float length=ray.magnitude;
                    int steps=Mathf.CeilToInt(length/18);
                    for(int step=1;step<steps;step++)
                    {
                        Vector3 sample=eye+ray*((float)step/steps);
                        if(terrain.HeightAt(sample.z,sample.x)+1>sample.y) return false;
                    }
                    // Public forest query returns deterministic trees including crown radius.
                    foreach(var tr in sightTrees)
                    {
                        double dz=roof.z-eye.z,dx=roof.x-eye.x;
                        double t=((tr.X-eye.z)*dz+(tr.Y-eye.x)*dx)/(dz*dz+dx*dx);
                        if(t<=0 || t>=1) continue;
                        double z=eye.z+t*dz,x=eye.x+t*dx;
                        double radius=tr.CrownRadiusM+2;
                        if((tr.X-z)*(tr.X-z)+(tr.Y-x)*(tr.Y-x)<radius*radius
                            && tr.TopM+2>eye.y+t*(roof.y-eye.y)) return false;
                    }
                    return true;
                }
                // Up to 96 distinct ridges, each terrace containing four homes. 48 m
                // spacing leaves 14 m between eaves and 30 m between garden fronts.
                // Start on the camera side; a failed lot never shifts any existing body.
                for(int row=0;row<26 && extension<96;row++)
                for(int col=0;col<20 && extension<96;col++)
                {
                    candidates++;
                    double u=1140+col*48,v=140+row*48;
                    var map=grid.Map(u,v);
                    bool detached=(row+col)%4==0;
                    Vector3 size=detached?new Vector3(18,6.4f,16):new Vector3(32,7,16);
                    double hx=21,hy=size.x*.5+4;
                    bool blocked=false;
                    foreach(var a in FlyingGame.Core.WorldTerrain.Airports)
                        if(FlyingGame.Core.WorldTerrain.PadOutsideX(a,map.x-hx)<70
                            && System.Math.Abs(map.y-a.Y)<FlyingGame.Core.WorldTerrain.PadHalfY+hy+70)
                            blocked=true;
                    if(blocked) { airportRejected++; continue; }
                    foreach(int dx in new[]{-1,0,1}) foreach(int dy in new[]{-1,0,1})
                        if(terrain.WaterSurfaceAt(map.x+dx*hx,map.y+dy*hy).HasValue) blocked=true;
                    if(blocked) { waterRejected++; continue; }
                    double g=terrain.HeightAt(map.x,map.y);
                    // Hills begin at sim east=1650: C10's city-height gate excluded
                    // this open grass even though small houses can follow its gentle slope.
                    // Individual stone footings meet unchanged terrain; no estate-wide pad.
                    double low=g,high=g;
                    foreach(int dx in new[]{-1,1}) foreach(int dy in new[]{-1,1})
                    {
                        double corner=terrain.HeightAt(map.x+dx*size.z*.5,map.y+dy*size.x*.5);
                        low=System.Math.Min(low,corner); high=System.Math.Max(high,corner);
                    }
                    if(System.Math.Abs(g-ground)>100 || high-low>2.5)
                    { terrainRejected++; continue; }
                    foreach(var tr in FlyingGame.Core.Forest.InRect(map.x-hx-12,map.y-hy-12,map.x+hx+12,map.y+hy+12))
                        if(System.Math.Abs(tr.X-map.x)<hx+tr.CrownRadiusM
                            && System.Math.Abs(tr.Y-map.y)<hy+tr.CrownRadiusM) { blocked=true; break; }
                    if(blocked) { treesRejected++; continue; }
                    Vector3 foot=WorldBuilder.U(map.x,map.y,high+.16);
                    Vector3 roof=foot+Vector3.up*12;
                    Vector3 relative=roof-eye;
                    float depth=Vector3.Dot(relative,forward);
                    // Keep the whole roof inside the lens with an 8% horizontal margin.
                    if(depth<950 || depth>2800 || Mathf.Abs(Vector3.Dot(relative,right))+size.x*.5f>depth*tanHalfFov*aspect*.84f
                        || Mathf.Abs(Vector3.Dot(relative,up))+14>depth*tanHalfFov*.9f)
                    { viewRejected++; continue; }
                    if(!ClearSight(roof) || !ClearSight(foot+new Vector3(0,5,-8)))
                    { sightRejected++; continue; }
                    // Green garden, pale door paths, restrained C10 light walls/36-degree
                    // slate and brown roofs. No asphalt/paved lot or new road surface.
                    float gx=size.x*.5f+3,gz=20;
                    Vector3 Lawn(float x,float z)=>WorldBuilder.U(map.x+z,map.y+x,
                        terrain.HeightAt(map.x+z,map.y+x)+.08);
                    b.Quad(Lawn(-gx,-gz),Lawn(-gx,gz),Lawn(gx,gz),Lawn(gx,-gz),new Color(.32f,.39f,.27f));
                    float foundation=(float)(high-low)+.3f;
                    b.Box(new Vector3(foot.x,(float)(low-.14)+foundation*.5f,foot.z),
                        new Vector3(size.x,foundation,size.z),new Color(.46f,.45f,.41f));
                    Building(foot,size,11003+row*37+col*13,detached?4:1,row+col/4);
                    int doors=detached?1:4;
                    for(int door=0;door<doors;door++)
                    {
                        float x=(door-(doors-1)*.5f)*8;
                        b.Quad(Lawn(x-.55f,-19.5f),Lawn(x-.55f,-8.5f),
                            Lawn(x+.55f,-8.5f),Lawn(x+.55f,-19.5f),new Color(.54f,.51f,.44f));
                    }
                    extension++; extensionArea+=size.x*size.z;
                    extensionRows[row]=u;
                }
                extensionEnd=b.Count;
                // C12: a second, smaller-grain band at the town-facing edge. C11 bodies
                // remain exactly where they were. Small footprints fit real forest gaps;
                // registered forest is never cleared for the sake of the render.
                for(int candidate=0;candidate<72 && nearCount<72;candidate++)
                {
                    double u,v;
                    if(candidate<48) { u=1092+(candidate%8)*34; v=-196+(candidate/8)*48; }
                    else { u=1092; v=140+(candidate-48)*48; }
                    nearTested++;
                    // Keep the existing six-by-six flank and every C11 garden intact.
                    if(u>=1150 && v<=-202) { nearOverlap++; continue; }
                    var map=grid.Map(u,v);
                    bool blocked=false;
                    foreach(var a in FlyingGame.Core.WorldTerrain.Airports)
                        if(FlyingGame.Core.WorldTerrain.PadOutsideX(a,map.x-18)<70
                            && System.Math.Abs(map.y-a.Y)<FlyingGame.Core.WorldTerrain.PadHalfY+84) blocked=true;
                    double g=terrain.HeightAt(map.x,map.y),low=g,high=g;
                    foreach(int dx in new[]{-1,0,1}) foreach(int dy in new[]{-1,0,1})
                    {
                        double h=terrain.HeightAt(map.x+dx*9,map.y+dy*9);
                        low=System.Math.Min(low,h); high=System.Math.Max(high,h);
                        if(terrain.WaterSurfaceAt(map.x+dx*18,map.y+dy*14).HasValue) blocked=true;
                    }
                    if(blocked || System.Math.Abs(g-ground)>100 || high-low>2.5)
                    { nearGround++; continue; }
                    foreach(var tr in FlyingGame.Core.Forest.InRect(map.x-30,map.y-26,map.x+30,map.y+26))
                        if(System.Math.Abs(tr.X-map.x)<18+tr.CrownRadiusM
                            && System.Math.Abs(tr.Y-map.y)<14+tr.CrownRadiusM) { blocked=true; break; }
                    if(blocked) { nearForest++; continue; }
                    Vector3 foot=WorldBuilder.U(map.x,map.y,high+.16);
                    Vector3 size=new Vector3(16,9.3f,14); // three-storey town walk-up
                    Vector3 roof=foot+Vector3.up*14;
                    Vector3 relative=roof-eye;
                    float depth=Vector3.Dot(relative,forward);
                    if(depth<650 || depth>2050 || Mathf.Abs(Vector3.Dot(relative,right))+10>depth*tanHalfFov*aspect*.94f
                        || Mathf.Abs(Vector3.Dot(relative,up))+14>depth*tanHalfFov*.94f)
                    { nearView++; continue; }
                    // Roof AND upper facade must clear the forest. The former low 5 m
                    // test discarded houses with visible upper storeys behind the tree belt.
                    if(!ClearSight(roof) || !ClearSight(foot+new Vector3(0,9,-7)))
                    { nearSight++; continue; }
                    Vector3 Lawn(float x,float z)=>WorldBuilder.U(map.x+z,map.y+x,
                        terrain.HeightAt(map.x+z,map.y+x)+.08);
                    b.Quad(Lawn(-12,-18),Lawn(-12,18),Lawn(12,18),Lawn(12,-18),new Color(.34f,.41f,.28f));
                    float foundation=(float)(high-low)+.3f;
                    b.Box(new Vector3(foot.x,(float)(low-.14)+foundation*.5f,foot.z),
                        new Vector3(size.x,foundation,size.z),new Color(.46f,.45f,.41f));
                    Building(foot,size,15001+candidate*37,1,candidate/3);
                    b.Quad(Lawn(-.7f,-18),Lawn(-.7f,-7),Lawn(.7f,-7),Lawn(.7f,-18),new Color(.56f,.53f,.46f));
                    nearCount++;
                    double laneV=v-24;
                    if(nearLanes.TryGetValue(laneV,out var range))
                        nearLanes[laneV]=(System.Math.Min(range.min,u),System.Math.Max(range.max,u+12));
                    else nearLanes[laneV]=(System.Math.Min(1110,u),System.Math.Max(1110,u+12));
                }
                // C12: six-metre residential lanes along the C11 garden frontages,
                // joined by an edge collector. Entirely outside the old city road/lot
                // boundary (u=1090); unchanged city street ends meet the collector.
                // Eight-metre terrain-draped pieces avoid floating hillside asphalt.
                Vector3 Surface(double u,double v,float lift)
                {
                    var m=grid.Map(u,v);
                    return WorldBuilder.U(m.x,m.y,terrain.HeightAt(m.x,m.y)+lift);
                }
                void Lane(double u0,double v0,double u1,double v1)
                {
                    double length=System.Math.Sqrt((u1-u0)*(u1-u0)+(v1-v0)*(v1-v0));
                    int steps=Mathf.CeilToInt((float)length/8);
                    double du=(u1-u0)/steps,dv=(v1-v0)/steps;
                    double nx=-dv/ System.Math.Sqrt(du*du+dv*dv),nz=du/System.Math.Sqrt(du*du+dv*dv);
                    for(int k=0;k<steps;k++)
                    {
                        double u=u0+du*k,v=v0+dv*k;
                        var m=grid.Map(u+du*.5,v+dv*.5);
                        bool blocked=false;
                        foreach(var tr in FlyingGame.Core.Forest.InRect(m.x-12,m.y-12,m.x+12,m.y+12))
                            if(System.Math.Abs(tr.X-m.x)<6+tr.CrownRadiusM && System.Math.Abs(tr.Y-m.y)<6+tr.CrownRadiusM)
                            { blocked=true; break; }
                        foreach(var a in FlyingGame.Core.WorldTerrain.Airports)
                            if(FlyingGame.Core.WorldTerrain.PadOutsideX(a,m.x-8)<70
                                && System.Math.Abs(m.y-a.Y)<FlyingGame.Core.WorldTerrain.PadHalfY+78) blocked=true;
                        for(int corner=0;corner<4;corner++)
                        {
                            double cu=u+(corner/2)*du+(corner%2==0?-4:4)*nx;
                            double cv=v+(corner/2)*dv+(corner%2==0?-4:4)*nz;
                            var c=grid.Map(cu,cv);
                            if(terrain.WaterSurfaceAt(c.x,c.y).HasValue) blocked=true;
                        }
                        if(blocked || System.Math.Abs(Surface(u,v,0).y-Surface(u+du,v+dv,0).y)>2)
                        { laneRejected++; continue; }
                        void Strip(double a,double c,float lift,Color colour)
                        {
                            b.Quad(Surface(u+nx*a,v+nz*a,lift),Surface(u+nx*c,v+nz*c,lift),
                                Surface(u+du+nx*c,v+dv+nz*c,lift),Surface(u+du+nx*a,v+dv+nz*a,lift),colour);
                        }
                        Strip(-4,4,.09f,new Color(.56f,.55f,.50f));
                        Strip(-3,3,.105f,new Color(.26f,.27f,.26f));
                        if(k%3==0) Strip(-.07,.07,.12f,new Color(.72f,.68f,.52f));
                        laneSegments++;
                    }
                }
                int lastRow=-1;
                foreach(var row in extensionRows)
                {
                    double v=116+row.Key*48;
                    Lane(1110,v,row.Value+22,v);
                    lastRow=System.Math.Max(lastRow,row.Key);
                    // Occasional small deciduous garden trees, not a new forest belt.
                    double u=row.Value+21;
                    var m=grid.Map(u,v+11);
                    if(!terrain.WaterSurfaceAt(m.x,m.y).HasValue)
                    { Tree(b,Surface(u,v+11,.12f),row.Key*17); streetTrees++; }
                }
                double collectorStart=85,collectorEnd=lastRow>=0?116+lastRow*48:85;
                foreach(var row in nearLanes)
                {
                    Lane(row.Value.min,row.Key,row.Value.max,row.Key);
                    collectorStart=System.Math.Min(collectorStart,row.Key);
                    collectorEnd=System.Math.Max(collectorEnd,row.Key);
                }
                if(collectorEnd>collectorStart)
                {
                    Lane(1110,collectorStart,1110,collectorEnd);
                    Lane(1091,85,1110,85);
                }
            }
            Debug.Log($"Roof district C12 near band {grid.X0},{grid.Y0}: {nearCount}/72 placed from {nearTested}; rejects overlap={nearOverlap}, ground/water/airport={nearGround}, forest={nearForest}, frustum={nearView}, sightline={nearSight}; lanes={laneSegments} eight-metre-or-shorter pieces, rejected={laneRejected}, garden trees={streetTrees}. All C11 bodies retained; visible band and lane continuity require renders.");
            Debug.Log($"Roof district C11 eastern extension {grid.X0},{grid.Y0}: {extension}/96 placed from {candidates} tested; rejects airport={airportRejected}, water={waterRejected}, terrain={terrainRejected}, forest={treesRejected}, frustum={viewRejected}, sightline={sightRejected}; {extensionArea:F0} m2, {extensionEnd-extensionStart} body vertices + {extension*16} facade vertices. Existing C10 bodies retained. Town visibility remains subject to rendered verification.");
            Debug.Log($"Roof district camera-facing flank: {flankCount} new buildings; tree/terrain checked, original district retained.");
            // Count/footprint are emitted for Chief's render log; no collision registration.
            Debug.Log($"Roof district {grid.X0},{grid.Y0}: {count} visual-only buildings, {emptyBlocks} open blocks, {area:F0} m2 footprints, {b.Count-bodyStart} body/roof vertices + {count*16} facade vertices; no colliders.");
        }
        public static void Streets(FlyingGame.Core.CityGrid grid, double ground, SeasideBuilder.Batch b, UrbanFacades facades)
        {
            RoofDistrict(grid,ground,b,facades);
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
                    for(int lane=0;lane<(QualitySettings.GetQualityLevel()<=2?1:2);lane++)
                    for(int car=0;car<2;car++)
                    {
                        int seed=axis*2000+road*71+block*13+(direction+1)*5+lane*131;
                        float slot=(car+.17f+lane*.29f)/2f;
                        Vector3 centre=P2(across,along,.14)+right*(direction*(3.5f+lane*5));
                        facades.TrafficCar(centre,forward*direction,seed,114,slot);
                        facades.TrafficStreak(centre+Vector3.up*.012f,forward*direction,seed,slot);
                    }
                    // Continuous far-LOD road energy fills the gaps between lamp pools and junctions.
                    // Shader discards this at close range and for every day/sunset fragment.
                    facades.Pool(P2(across,along,.096),S(25,0,170),new Color(1,.55f,.22f),8);
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
                        for(int parked=0;parked<(QualitySettings.GetQualityLevel()<=1?3:QualitySettings.GetQualityLevel()<=2?6:9);parked++)
                            if((road*7+block+parked)%3!=0)
                                Car(b,P2(across+side*12.5,along-48+parked*12,.09),Quaternion.LookRotation(forward*side,Vector3.up),road*17+block+parked);
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
