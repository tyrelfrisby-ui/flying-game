using System;
using System.Collections.Generic;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    // Original procedural airport furniture, all collider-free. Pavement and sim surfaces stay fixed.
    public static partial class WorldBuilder
    {
        private static Material _airportLamps, _airportGlow;
        private static void AirportVisualDressing(Transform parent, WorldTerrain.Airport a)
        {
            var b = new SeasideBuilder.Batch();
            var lamps = new SeasideBuilder.Batch();
            var glowPositions=new List<Vector3>(); var glowRowSteps=new List<Vector3>(); var glowColours=new List<Color>(); var glowDirections=new List<Vector3>();
            int signCount=0;
            void Glow(Vector3 position,Color colour,Vector3 direction=default,Vector3 rowStep=default) { glowPositions.Add(position); glowColours.Add(colour); glowDirections.Add(direction); glowRowSteps.Add(rowStep); }
            void Sign(Vector3 position,string label,Color background,Color foreground,float width)
            {
                AirportSign(b,parent,position,label,background,foreground,width); signCount++;
            }
            Color yellow = new Color(.93f,.69f,.12f), steel = new Color(.28f,.30f,.29f);
            Color white = new Color(.87f,.86f,.79f), blue = new Color(.06f,.25f,1f);
            double z = a.ElevationM, ax = a.X+WorldTerrain.ApronDx, ay = a.Y+WorldTerrain.ApronDy;
            Vector3 P(double x, double y, double h=0) => U(x,y,z+h);
            void Stroke(double x0,double y0,double x1,double y1,float width,Color color)
                => b.Beam(P(x0,y0,.105),P(x1,y1,.105),width,.018f,color);
            void Lamp(Vector3 p,Color color,Vector3 rowStep=default)
            {
                b.Box(p+Vector3.up*.15f,new Vector3(.26f,.3f,.26f),steel);
                lamps.Box(p+Vector3.up*.35f,new Vector3(.46f,.22f,.46f),color);
                Glow(p+Vector3.up*.42f,color,rowStep:rowStep);
            }
            // Existing pavement only. Curved parking lead-ins and a marked apron taxi lane.
            double laneX=ax+105;
            for (int row=0;row<5;row++)
            {
                double py=ay-95+row*42;
                Vector3 prev=P(laneX,py+15,.11);
                for(int j=1;j<=12;j++)
                {
                    double t=j*Math.PI/24;
                    Vector3 next=P(laneX-15+15*Math.Cos(t),py+15-15*Math.Sin(t),.11);
                    b.Beam(prev,next,.30f,.018f,yellow); prev=next;
                }
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 pin=P(ax+47,py+side*5.4,.12);
                    b.Cylinder(pin,.17f,.035f,6,steel);
                    b.Beam(pin,P(ax+47,py+side*3.5,.55),.045f,.045f,steel);
                    b.Torus(pin+Vector3.up*.06f,Vector3.up,.15f,.025f,8,4,k=>white);
                }
            }
            // Double solid taxi lane edges on the apron (pavement continues outside them).
            foreach(int side in new[]{-1,1}) foreach(double off in new[]{6.0,6.45})
                Stroke(laneX+side*off,ay-110,laneX+side*off,ay+100,.15f,yellow);
            // Holding position: aircraft approaching the runway from the apron see solids first.
            // There is a 25 m unpaved gap; do not invent a drivable connector over it.
            double holdY=ay-87;
            for(int row=0;row<4;row++)
            {
                double hy=holdY-row*.6;
                if(row<2) Stroke(laneX-6,hy,laneX+6,hy,.30f,yellow);
                else for(double dx=-6;dx<6;dx+=1.8)
                    Stroke(laneX+dx,hy,laneX+Math.Min(6,dx+.9),hy,.30f,yellow);
            }
            int runwayHeading = (int)Math.Round(WorldTerrain.AirportStrips[0].HeadingDeg/10.0)%36;
            if(runwayHeading==0) runwayHeading=36;
            string holding = ((runwayHeading+17)%36+1).ToString("00")+"-"+runwayHeading.ToString("00");
            Sign(P(laneX-9,holdY+2),holding,new Color(.66f,.025f,.025f),Color.white,4.0f);
            Sign(P(laneX-11.8,holdY+2),"A",new Color(.035f,.04f,.04f),yellow,1.3f);
            Sign(P(laneX-14.9,holdY+2),"APRON \u2192",yellow,new Color(.025f,.025f,.025f),4.6f);
            // Matching signs on the far shoulder of the same holding position.
            Sign(P(laneX+10,holdY+2),holding,new Color(.66f,.025f,.025f),Color.white,4.0f);
            Sign(P(laneX+12.8,holdY+2),"A",new Color(.035f,.04f,.04f),yellow,1.3f);
            Sign(P(laneX+15.4,holdY+2),"\u2190 APRON",yellow,new Color(.025f,.025f,.025f),3.6f);
            for(double py=ay-110;py<=ay+110;py+=22)
                foreach(int side in new[]{-1,1}) Lamp(P(laneX+side*8,py),blue);

            foreach(WorldTerrain.Strip s in WorldTerrain.AirportStrips)
            {
                double h=s.HeadingDeg*Math.PI/180, c=Math.Cos(h), sn=Math.Sin(h);
                Vector3 R(double along,double across,double up=0) => P(a.X+s.Dx+along*c-across*sn,a.Y+s.Dy+along*sn+across*c,up);
                bool paved=WorldTerrain.IsPaved(s.Kind);
                if(paved)
                {
                    // Runway edge, threshold and glide path lights are the shared parts in AirportLighting / AirportLights
                    // (island world); this old-world builder no longer carries a copy of its own.
                    // Gravel-to-grass visual shoulder ribbon, sampled on existing terrain.
                    foreach(int side in new[]{-1,1})
                    for(double d=-s.Length/2;d<s.Length/2;d+=20)
                    {
                        double next=Math.Min(s.Length/2,d+20);
                        Vector3 i0=R(d,side*(s.Width/2+.05),.018), i1=R(next,side*(s.Width/2+.05),.018);
                        Vector3 o0=R(d,side*(s.Width/2+4),.018), o1=R(next,side*(s.Width/2+4),.018);
                        i0.y=(float)WorldTerrain.Active.HeightAt(i0.z,i0.x)+.022f;
                        i1.y=(float)WorldTerrain.Active.HeightAt(i1.z,i1.x)+.022f;
                        o0.y=(float)WorldTerrain.Active.HeightAt(o0.z,o0.x)+.022f;
                        o1.y=(float)WorldTerrain.Active.HeightAt(o1.z,o1.x)+.022f;
                        b.Quad(i0,o0,o1,i1,new Color(.38f,.40f,.29f));
                    }
                }
                else
                {
                    // Edge cones follow the undulating grass rather than bridging its hollows.
                    for(double d=-s.Length/2+10;d<s.Length/2;d+=45)
                    foreach(int side in new[]{-1,1})
                    {
                        Vector3 p=R(d,side*(s.Width/2+1.5));
                        p.y=AirportVisibleGround(p.z,p.x)+.04f;
                        b.Cylinder(p,.48f,.60f,6,white,white);
                        b.Box(p+Vector3.up*.08f,new Vector3(1.1f,.16f,1.1f),white);
                    }
                }
            }
            // Segmented circle on the rough infield, clear of every strip.
            Vector3 circle=P(a.X-650,a.Y+360);
            circle.y=AirportVisibleGround(circle.z,circle.x)+.06f;
            for(int j=0;j<16;j++)
            {
                float t=j*Mathf.PI/8;
                Vector3 v=new Vector3(Mathf.Cos(t),0,Mathf.Sin(t));
                b.Box(circle+v*12,new Vector3(2,.18f,.6f),Quaternion.Euler(0,-t*Mathf.Rad2Deg,0),white);
            }
            // Fuel equipment and small service apron, on the existing apron footprint.
            Vector3 fuel=P(ax+125,ay+25);
            b.Box(fuel+new Vector3(1.8f,.07f,-.5f),new Vector3(11,.08f,12),new Color(.57f,.57f,.53f));
            for(int k=0;k<2;k++)
            {
                Vector3 tank=fuel+new Vector3(k*4,0,0);
                b.Cylinder(tank+Vector3.up*.4f,1.25f,3.8f,12,new Color(.68f,.70f,.67f),white);
                b.Box(tank+new Vector3(0,1.1f,-2.6f),new Vector3(.65f,2.2f,.65f),new Color(.61f,.12f,.08f));
                b.Tube(new[]{tank+new Vector3(0,1.8f,-2.9f),tank+new Vector3(.7f,.4f,-3.2f),tank+new Vector3(1,.2f,-2.6f)},.055f,4,steel);
            }
            // Full-size visual hangar row along the apron perimeter, clear of lead-ins.
            AirportSmallHangar(b,P(ax+55,ay+105),22,30,7,0);
            AirportSmallHangar(b,P(ax+15,ay+105),26,32,8,1);
            AirportQuonset(b,P(ax-30,ay+105),24,36,8);
            // A linked T-hangar row; alternating door tones keep individual bays legible.
            for(int k=0;k<4;k++)
                AirportSmallHangar(b,P(ax-130+k*19,ay-105),18,22,6,k%3);
            // FBO entrance glazing, canopy and fascia on the existing office.
            Vector3 office=P(ax-85,ay+85);
            b.Box(office+new Vector3(-6.12f,2.2f,0),new Vector3(.12f,2.3f,14),new Color(.12f,.23f,.28f));
            for(int k=-3;k<=3;k++)
                b.Box(office+new Vector3(-6.22f,2.2f,k*2),new Vector3(.12f,2.4f,.09f),white);
            b.Box(office+new Vector3(-8,3.7f,0),new Vector3(4,.22f,17),new Color(.62f,.65f,.63f));
            foreach(int side in new[]{-1,1})
                b.Box(office+new Vector3(-9.5f,1.8f,side*7.5f),new Vector3(.12f,3.6f,.12f),steel);
            Sign(office+new Vector3(-6.5f,2.7f,0),a.Name.ToUpperInvariant()+" FBO",new Color(.12f,.20f,.23f),white,8);
            for(int k=-3;k<=3;k++)
                lamps.Box(office+new Vector3(-6.28f,2.2f,k*2),new Vector3(.035f,1.75f,1.8f),new Color(.78f,.58f,.29f));
            // Corrugated side walls on the original full-size hangar (doors remain in place).
            for(int side=-1;side<=1;side+=2)
            for(int j=-24;j<=24;j++)
                b.Box(P(a.X+WorldTerrain.HangarDx+j,a.Y+WorldTerrain.HangarDy+side*22.32,8),new Vector3(.10f,15.5f,.07f),new Color(.64f,.63f,.58f));
            // Low perimeter fence, with an opening on the runway side.
            for(int j=0;j<32;j++)
            {
                Vector3 p=P(ax-155+j*10,ay+128);
                b.Box(p+Vector3.up, new Vector3(.07f,2,.07f),steel);
                if(j<31) for(int rail=0;rail<3;rail++)
                    b.Beam(p+Vector3.up*(.4f+rail*.65f),p+new Vector3(0,.4f+rail*.65f,10),.035f,.035f,steel);
            }
            // FBO windows/parking alongside the existing office rather than more buildings across taxi routes.
            for(int j=0;j<5;j++)
            {
                Stroke(ax-135+j*4,ay+60,ax-135+j*4,ay+73,.12f,white);
                if(j<3)
                {
                    Vector3 car=P(ax-133+j*4,ay+67,.7);
                    b.Box(car,new Vector3(4.2f,1.1f,1.8f),j==0?new Color(.42f,.14f,.08f):new Color(.47f,.51f,.54f));
                    b.Box(car+Vector3.up*.6f,new Vector3(2.0f,.6f,1.6f),new Color(.15f,.22f,.25f));
                }
            }
            // Floodlight poles and warm, opaque ground pools; no realtime point lights on mobile.
            foreach(double dx in new[]{-135.0,135.0})
            {
                Vector3 pole=P(ax+dx,ay+110);
                b.Box(pole+Vector3.up*7,new Vector3(.2f,14,.2f),steel);
                b.Box(pole+new Vector3(-1,14,0),new Vector3(2,.25f,.6f),steel);
                lamps.Box(pole+new Vector3(-1,13.8f,0),new Vector3(1.8f,.10f,.5f),new Color(1,.83f,.51f));
                Glow(pole+new Vector3(-1,13.8f,0),new Color(1,.83f,.51f));
            }
            // Beacon tower: separated green/white heads; runtime rotation is purely cosmetic.
            Vector3 beacon=P(ax-140,ay+105);
            b.Box(beacon+Vector3.up*6,new Vector3(.35f,12,.35f),steel);
            for(int level=0;level<4;level++)
            {
                float lo=level*3, hi=lo+3;
                foreach(int side in new[]{-1,1})
                    b.Beam(beacon+new Vector3(side*.7f,lo,0),beacon+new Vector3(side*.7f,hi,0),.10f,.10f,steel);
                b.Beam(beacon+new Vector3(-.7f,lo,0),beacon+new Vector3(.7f,hi,0),.08f,.08f,steel);
            }
            Glow(beacon+new Vector3(-.65f,12.5f,0),new Color(.025f,1,.18f,.25f));
            Glow(beacon+new Vector3(.65f,12.5f,0),new Color(white.r,white.g,white.b,.5f));
            b.Box(beacon+Vector3.up*12,new Vector3(2,.22f,2),steel);
            var head=new SeasideBuilder.Batch();
            head.Box(new Vector3(-.65f,0,0),new Vector3(1.0f,.5f,1.0f),new Color(.025f,1,.18f));
            head.Box(new Vector3(.65f,0,0),new Vector3(1.0f,.5f,1.0f),white);
            if(_airportLamps==null) { _airportLamps=Mat("FlyingGame/LitVC",Color.white); _airportLamps.SetFloat("_Emission",1); }
            GameObject beaconHead=head.Build("AirportBeacon",parent,_airportLamps);
            beaconHead.transform.position=beacon+Vector3.up*12.5f;
            beaconHead.AddComponent<AirportBeacon>();
            b.Build("AirportFurniture",parent,SeasideBuilder.VC);
            lamps.Build("AirportLenses",parent,_airportLamps);
            AirportHalos(parent,glowPositions,glowColours,glowDirections,glowRowSteps);
            Debug.Log($"Airport visuals {a.Name}: {signCount} sign panels, 7 added hangar bays, 1 FBO, {glowPositions.Count} luminous fixtures; collider-free dressing.");
        }

        // Interpolate the existing coarse terrain triangles for furniture only. The grass
        // height field has small swells that the 60 m visible mesh cannot exactly reproduce.
        // This keeps edge markers on the visible ground without changing either surface.
        private static float AirportVisibleGround(double x,double y)
        {
            double x0=MinX+Math.Floor((x-MinX)/CoarseStep)*CoarseStep;
            double y0=MinY+Math.Floor((y-MinY)/CoarseStep)*CoarseStep;
            double u=(x-x0)/CoarseStep,v=(y-y0)/CoarseStep;
            var terrain=WorldTerrain.Active;
            double h10=terrain.HeightAt(x0+CoarseStep,y0),h01=terrain.HeightAt(x0,y0+CoarseStep);
            if(u+v<=1)
            {
                double h00=terrain.HeightAt(x0,y0);
                return (float)(h00+(h10-h00)*u+(h01-h00)*v);
            }
            double h11=terrain.HeightAt(x0+CoarseStep,y0+CoarseStep);
            return (float)(h11+(h01-h11)*(1-u)+(h10-h11)*(1-v));
        }

        private static void AirportHalos(Transform parent,List<Vector3> centres,List<Color> colours,List<Vector3> directions,List<Vector3> rowSteps)
        {
            int count=centres.Count;
            var vertices=new Vector3[count*4]; var normals=new Vector3[count*4];
            var spacing=new List<Vector3>(count*4);
            var uv=new Vector2[count*4]; var coloursOut=new Color[count*4]; var triangles=new int[count*6];
            Vector2[] corners={new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,1),new Vector2(1,-1)};
            for(int k=0;k<count;k++)
            {
                for(int j=0;j<4;j++) { int index=k*4+j; vertices[index]=centres[k]; normals[index]=directions[k]; uv[index]=corners[j]; coloursOut[index]=colours[k]; spacing.Add(rowSteps[k]); }
                int v=k*4,t=k*6;
                triangles[t]=v; triangles[t+1]=v+1; triangles[t+2]=v+2;
                triangles[t+3]=v; triangles[t+4]=v+2; triangles[t+5]=v+3;
            }
            var mesh=new Mesh { name="Airport fixture halos" };
            mesh.vertices=vertices; mesh.normals=normals; mesh.uv=uv; mesh.colors=coloursOut; mesh.triangles=triangles;
            mesh.SetUVs(1,spacing);
            mesh.RecalculateBounds(); var bounds=mesh.bounds; bounds.Expand(52); mesh.bounds=bounds;
            if(_airportGlow==null)
            {
                _airportGlow=new Material(Shader.Find("FlyingGame/AirportGlow"));
                string tier=QualitySettings.names[QualitySettings.GetQualityLevel()];
                bool low=tier.IndexOf("low",StringComparison.OrdinalIgnoreCase)>=0 || tier.IndexOf("fast",StringComparison.OrdinalIgnoreCase)>=0;
                _airportGlow.SetFloat("_HaloPixels",low?1.5f:Application.isMobilePlatform?2.0f:3.0f);
            }
            var go=new GameObject("AirportFixtureHalos"); go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=_airportGlow;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false;
        }

        private static void AirportSmallHangar(SeasideBuilder.Batch b,Vector3 p,float width,float depth,float height,int style)
        {
            Color wall=new Color(.59f+style*.05f,.61f+style*.03f,.58f),roof=new Color(.31f,.34f,.35f);
            b.Box(p+Vector3.up*(height/2),new Vector3(depth,height,width),wall);
            // Gable with two roof planes, ridge along Unity z.
            float rise=2.0f;
            foreach(int side in new[]{-1,1})
            {
                Vector3 eave=p+new Vector3(side*(depth/2+.5f),height,0);
                Vector3 ridge=p+new Vector3(0,height+rise,0);
                b.Quad(eave+Vector3.forward*(width/2+.5f),eave-Vector3.forward*(width/2+.5f),ridge-Vector3.forward*(width/2+.5f),ridge+Vector3.forward*(width/2+.5f),roof);
            }
            for(float k=-width/2;k<=width/2;k+=1.2f)
            foreach(int side in new[]{-1,1})
                b.Beam(p+new Vector3(side*(depth/2+.5f),height+.04f,k),p+new Vector3(0,height+rise+.04f,k),.055f,.055f,roof*.8f);
            foreach(int end in new[]{-1,1})
                b.Tri(p+new Vector3(-depth/2,height,end*width/2),p+new Vector3(depth/2,height,end*width/2),p+new Vector3(0,height+rise,end*width/2),wall);
            for(float k=-width/2;k<=width/2;k+=.6f)
                b.Box(p+new Vector3(depth/2+.035f,height/2,k),new Vector3(.06f,height,.055f),wall*.85f);
            Vector3 face=p+Vector3.left*(depth/2+.025f);
            b.Box(face+Vector3.up*(height*.43f),new Vector3(.07f,height*.85f,width*.85f),new Color(.30f,.36f,.38f));
            for(float r=-width*.40f;r<width*.42f;r+=.8f)
                b.Box(face+new Vector3(-.05f,height*.43f,r),new Vector3(.06f,height*.85f,.10f),new Color(.44f,.48f,.47f));
            for(int k=-2;k<=2;k++)
                b.Box(face+new Vector3(-.09f,height*.64f,k*width*.14f),new Vector3(.045f,.65f,width*.10f),new Color(.18f,.28f,.31f));
            b.Box(face+new Vector3(-.10f,1.05f,width*.44f),new Vector3(.08f,2.1f,.85f),new Color(.63f,.66f,.62f));
            b.Box(face+Vector3.up*(height-.2f),new Vector3(.25f,.16f,width+1),roof);
        }

        private static void AirportQuonset(SeasideBuilder.Batch b,Vector3 p,float width,float depth,float height)
        {
            Color sheet=new Color(.53f,.58f,.57f), trim=new Color(.29f,.34f,.35f);
            // Barrel axis along Unity x; front faces the apron centre.
            for(int k=0;k<16;k++)
            {
                float a=k*Mathf.PI/16, c=(k+1)*Mathf.PI/16;
                Vector3 v0=new Vector3(0,Mathf.Sin(a)*height,Mathf.Cos(a)*width/2);
                Vector3 v1=new Vector3(0,Mathf.Sin(c)*height,Mathf.Cos(c)*width/2);
                Color shade=k%2==0?sheet:sheet*.94f;
                b.Quad(p+v0+Vector3.right*depth/2,p+v1+Vector3.right*depth/2,p+v1-Vector3.right*depth/2,p+v0-Vector3.right*depth/2,shade);
                foreach(int end in new[]{-1,1})
                    b.Tri(p+Vector3.right*(end*depth/2),p+v0+Vector3.right*(end*depth/2),p+v1+Vector3.right*(end*depth/2),sheet);
            }
            Vector3 front=p+Vector3.left*(depth/2+.06f);
            b.Box(front+Vector3.up*2.8f,new Vector3(.15f,5.6f,width*.78f),trim);
            for(float k=-width*.38f;k<width*.39f;k+=.55f)
                b.Box(front+new Vector3(-.10f,2.8f,k),new Vector3(.08f,5.6f,.05f),sheet);
        }

        private static void AirportSign(SeasideBuilder.Batch b,Transform parent,Vector3 p,string text,Color bg,Color ink,float width)
        {
            b.Box(p+Vector3.up*.7f,new Vector3(.20f,1.0f,width+.12f),new Color(.22f,.24f,.22f));
            b.Box(p+Vector3.up*.7f,new Vector3(.24f,.88f,width),bg);
            // A location sign has a yellow border as well as yellow lettering.
            if(bg.r<.1f)
            foreach(int face in new[]{-1,1})
            {
                foreach(int edge in new[]{-1,1})
                {
                    b.Box(p+new Vector3(face*.126f,.7f+edge*.405f,0),new Vector3(.015f,.035f,width-.06f),ink);
                    b.Box(p+new Vector3(face*.126f,.7f,edge*(width/2-.03f)),new Vector3(.015f,.82f,.035f),ink);
                }
            }
            foreach(int side in new[]{-1,1}) b.Box(p+new Vector3(0,.18f,side*(width/2-.2f)),new Vector3(.08f,.36f,.08f),new Color(.55f,.56f,.51f));
            foreach(int side in new[]{-1,1})
            {
                var go=new GameObject("AirportSignText"); go.transform.SetParent(parent,false);
                go.transform.position=p+new Vector3(side*.13f,.7f,0);
                go.transform.rotation=Quaternion.Euler(0,side>0?270:90,0);
                var tm=go.AddComponent<TextMesh>(); tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); tm.text=text; tm.fontSize=48; tm.characterSize=1f;
                tm.anchor=TextAnchor.MiddleCenter; tm.alignment=TextAlignment.Center; tm.color=ink;
                var renderer=go.GetComponent<MeshRenderer>();
                if(tm.font!=null) renderer.sharedMaterial=DepthTestedText(tm.font);
                Vector3 extent=renderer.bounds.size;
                float fit=Mathf.Min(width*.86f/Mathf.Max(.01f,extent.z),.65f/Mathf.Max(.01f,extent.y));
                go.transform.localScale=Vector3.one*fit;
            }
        }
    }

    public sealed class AirportBeacon : MonoBehaviour
    {
        private void Update() { transform.Rotate(0,120*Time.deltaTime,0,Space.Self); }
    }
}
