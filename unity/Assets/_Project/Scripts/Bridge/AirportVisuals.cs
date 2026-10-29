using System;
using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    // Original procedural airport furniture, all collider-free. Pavement and sim surfaces stay fixed.
    public static partial class WorldBuilder
    {
        private static Material _airportLamps;
        private static void AirportVisualDressing(Transform parent, WorldTerrain.Airport a)
        {
            var b = new SeasideBuilder.Batch();
            var lamps = new SeasideBuilder.Batch();
            Color yellow = new Color(.93f,.69f,.12f), steel = new Color(.28f,.30f,.29f);
            Color white = new Color(.87f,.86f,.79f), blue = new Color(.06f,.25f,1f);
            double z = a.ElevationM, ax = a.X+WorldTerrain.ApronDx, ay = a.Y+WorldTerrain.ApronDy;
            Vector3 P(double x, double y, double h=0) => U(x,y,z+h);
            void Stroke(double x0,double y0,double x1,double y1,float width,Color color)
                => b.Beam(P(x0,y0,.105),P(x1,y1,.105),width,.018f,color);
            void Lamp(Vector3 p,Color color)
            {
                b.Box(p+Vector3.up*.15f,new Vector3(.26f,.3f,.26f),steel);
                lamps.Box(p+Vector3.up*.35f,new Vector3(.22f,.16f,.22f),color);
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
            AirportSign(b,parent,P(laneX-9,holdY+2),"18-36",new Color(.66f,.025f,.025f),Color.white,4.0f);
            AirportSign(b,parent,P(laneX+9,holdY+6),"A",new Color(.035f,.04f,.04f),yellow,1.3f);
            AirportSign(b,parent,P(laneX+9,holdY+9),"APRON >",yellow,new Color(.025f,.025f,.025f),4.6f);
            for(double py=ay-110;py<=ay+110;py+=22)
                foreach(int side in new[]{-1,1}) Lamp(P(laneX+side*8,py),blue);

            foreach(WorldTerrain.Strip s in WorldTerrain.AirportStrips)
            {
                double h=s.HeadingDeg*Math.PI/180, c=Math.Cos(h), sn=Math.Sin(h);
                Vector3 R(double along,double across,double up=0) => P(a.X+s.Dx+along*c-across*sn,a.Y+s.Dy+along*sn+across*c,up);
                bool paved=WorldTerrain.IsPaved(s.Kind);
                if(paved)
                {
                    // 60 m runway edge fixture spacing. Each end has separate inward green/outward red faces.
                    for(double d=-s.Length/2+30;d<s.Length/2;d+=60)
                    foreach(int side in new[]{-1,1})
                    {
                        // Omit fixtures in the crossing runway's pavement.
                        Vector3 p=R(d,side*(s.Width/2+1));
                        bool crossing=false;
                        foreach(WorldTerrain.Strip other in WorldTerrain.AirportStrips)
                            if(WorldTerrain.IsPaved(other.Kind)&&other.Kind!=s.Kind&&WorldTerrain.InStrip(a,other,p.z,p.x,1)) crossing=true;
                        if(!crossing) Lamp(p,white);
                    }
                    foreach(int end in new[]{-1,1})
                    {
                        Vector3 forward=R(end*2,0)-R(0,0);
                        forward.Normalize();
                        Vector3 right=R(0,1)-R(0,0); right.Normalize();
                        for(int j=0;j<8;j++)
                        {
                            Vector3 p=R(end*(s.Length/2+1),(j-3.5)*s.Width/8,.23);
                            b.Box(p,new Vector3(.4f,.25f,.4f),steel);
                            // vertical faces, directed to approach and runway respectively
                            Vector3 across=right*.16f, up=Vector3.up*.09f;
                            Vector3 outside=p+forward*.22f, inside=p-forward*.22f;
                            lamps.Quad(outside-across-up,outside+across-up,outside+across+up,outside-across+up,new Color(1,.035f,.015f));
                            lamps.Quad(inside-across-up,inside+across-up,inside+across+up,inside-across+up,new Color(.03f,1,.23f));
                        }
                        // Four PAPI boxes left of each approach. Emissive two-white/two-red inspection appearance.
                        for(int j=0;j<4;j++)
                        {
                            Vector3 p=R(end*(s.Length/2-300),end*(s.Width/2+16+j*9),.35);
                            b.Box(p,new Vector3(1.1f,.65f,1.0f),white);
                            lamps.Box(p+Vector3.up*.15f,new Vector3(.8f,.20f,.75f),j<2?white:new Color(1,.025f,.015f));
                        }
                    }
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
                        p.y=(float)WorldTerrain.Active.HeightAt(p.z,p.x)+.04f;
                        b.Cylinder(p,.38f,.48f,6,white,white);
                    }
                }
            }
            // Segmented circle on the rough infield, clear of every strip.
            Vector3 circle=P(a.X-650,a.Y+360);
            circle.y=(float)WorldTerrain.Active.HeightAt(circle.z,circle.x)+.06f;
            for(int j=0;j<16;j++)
            {
                float t=j*Mathf.PI/8;
                Vector3 v=new Vector3(Mathf.Cos(t),0,Mathf.Sin(t));
                b.Box(circle+v*12,new Vector3(2,.18f,.6f),Quaternion.Euler(0,-t*Mathf.Rad2Deg,0),white);
            }
            // Fuel equipment and small service apron, on the existing apron footprint.
            Vector3 fuel=P(ax+125,ay+25);
            for(int k=0;k<2;k++)
            {
                Vector3 tank=fuel+new Vector3(k*4,0,0);
                b.Cylinder(tank+Vector3.up*.4f,1.25f,3.8f,12,new Color(.68f,.70f,.67f),white);
                b.Box(tank+new Vector3(0,1.1f,-2.6f),new Vector3(.65f,2.2f,.65f),new Color(.61f,.12f,.08f));
                b.Tube(new[]{tank+new Vector3(0,1.8f,-2.9f),tank+new Vector3(.7f,.4f,-3.2f),tank+new Vector3(1,.2f,-2.6f)},.055f,4,steel);
            }
            // Three small gabled T-hangar bays along the apron perimeter. No new collision.
            for(int k=0;k<3;k++)
                AirportSmallHangar(b,P(ax+60-k*28,ay+108),12,9,6,k);
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
            }
            // Beacon tower: separated green/white heads; runtime rotation is purely cosmetic.
            Vector3 beacon=P(ax-140,ay+105);
            b.Box(beacon+Vector3.up*6,new Vector3(.35f,12,.35f),steel);
            b.Box(beacon+Vector3.up*12,new Vector3(2,.22f,2),steel);
            var head=new SeasideBuilder.Batch();
            head.Box(new Vector3(-.65f,0,0),new Vector3(.65f,.35f,.65f),new Color(.025f,1,.18f));
            head.Box(new Vector3(.65f,0,0),new Vector3(.65f,.35f,.65f),white);
            if(_airportLamps==null) { _airportLamps=Mat("FlyingGame/LitVC",Color.white); _airportLamps.SetFloat("_Emission",1); }
            GameObject beaconHead=head.Build("AirportBeacon",parent,_airportLamps);
            beaconHead.transform.position=beacon+Vector3.up*12.5f;
            beaconHead.AddComponent<AirportBeacon>();
            b.Build("AirportFurniture",parent,SeasideBuilder.VC);
            lamps.Build("AirportLenses",parent,_airportLamps);
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
                b.Tri(p+new Vector3(side*depth/2,height,width/2),p+new Vector3(0,height+rise,width/2),p+new Vector3(0,height,width/2),wall);
            }
            Vector3 face=p+Vector3.left*(depth/2+.025f);
            b.Box(face+Vector3.up*(height*.43f),new Vector3(.07f,height*.85f,width*.85f),new Color(.30f,.36f,.38f));
            for(float r=-width*.40f;r<width*.42f;r+=.45f)
                b.Box(face+new Vector3(-.05f,height*.43f,r),new Vector3(.05f,height*.85f,.045f),new Color(.44f,.48f,.47f));
            b.Box(face+Vector3.up*(height-.2f),new Vector3(.25f,.16f,width+1),roof);
        }

        private static void AirportSign(SeasideBuilder.Batch b,Transform parent,Vector3 p,string text,Color bg,Color ink,float width)
        {
            b.Box(p+Vector3.up*.7f,new Vector3(.18f,.9f,width),bg);
            foreach(int side in new[]{-1,1}) b.Box(p+new Vector3(0,.18f,side*(width/2-.2f)),new Vector3(.08f,.36f,.08f),new Color(.55f,.56f,.51f));
            foreach(int side in new[]{-1,1})
            {
                var go=new GameObject("AirportSignText"); go.transform.SetParent(parent,false);
                go.transform.position=p+new Vector3(side*.105f,.7f,0);
                go.transform.rotation=Quaternion.Euler(0,side>0?90:270,0);
                var tm=go.AddComponent<TextMesh>(); tm.text=text; tm.fontSize=48; tm.characterSize=.14f;
                tm.anchor=TextAnchor.MiddleCenter; tm.alignment=TextAlignment.Center; tm.color=ink;
                if(tm.font!=null) go.GetComponent<MeshRenderer>().sharedMaterial=DepthTestedText(tm.font);
            }
        }
    }

    public sealed class AirportBeacon : MonoBehaviour
    {
        private void Update() { transform.Rotate(0,120*Time.deltaTime,0,Space.Self); }
    }
}
