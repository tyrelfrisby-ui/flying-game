Shader "FlyingGame/UrbanFacade"
{
 SubShader
 {
  Tags { "RenderType"="Opaque" }
  Cull Off
  Offset -1, -1
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #pragma multi_compile_fog
   #include "UnityCG.cginc"
   #include "UrbanGlow.cginc"
   struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float2 data:TEXCOORD1; float4 color:COLOR; float4 travel:TEXCOORD2; };
   struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; float2 data:TEXCOORD3; float4 color:COLOR; UNITY_FOG_COORDS(4) };
   float4 _BubbleSunDir, _ReflectionClip, _DayAtmosphere;
   float _CityNight, _ReflectionRendering, _SunsetOn, _CityTrafficPreview, _CityTrafficTime;
   v2f vert(appdata v)
   {
    v2f o;
    // Time based decorative traffic, constrained to one clear avenue segment.
    float moving=step(5.5,v.data.x);
    float trafficTime=lerp(_Time.y,_CityTrafficTime,_CityTrafficPreview);
    float offset=v.travel.y<0?frac(v.data.y*.618034):v.travel.y;
    float phase=frac(trafficTime*(7.0+frac(v.data.y*.137)*5.0)/max(1.0,v.travel.w)+offset);
    v.vertex.xyz+=float3(v.travel.x,0,v.travel.z)*((phase-.5)*v.travel.w*moving);
    o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
    o.normal=UnityObjectToWorldNormal(v.normal); o.uv=v.uv; o.data=v.data; o.color=v.color;
    UNITY_TRANSFER_FOG(o,o.pos); return o;
   }
   float hash(float2 p)
   {
    float3 q=frac(float3(p.x,p.y,p.x)*.1031);
    q+=dot(q,q.yzx+33.33);
    return frac((q.x+q.y)*q.z);
   }
   float boxMask(float2 f,float2 halfSize,float2 aa)
   {
    float2 m=1-smoothstep(halfSize-aa,halfSize+aa,abs(f-.5)); return m.x*m.y;
   }
   float4 frag(v2f i):SV_Target
   {
    if (_ReflectionRendering>.5) clip(dot(float4(i.world,1),_ReflectionClip));
    float style=i.data.x;
    if(style>7.5)
    {
     clip(_CityNight-.5);
     float farWeight=smoothstep(1000,3200,distance(_WorldSpaceCameraPos,i.world));
     clip(farWeight-.001);
     float edge=1-smoothstep(.65,1,abs(i.uv.x));
     float4 result=float4(float3(1,.48,.14)*(.20+.65*edge)*farWeight,1);
     UNITY_APPLY_FOG(i.fogCoord,result); result.rgb=urbanHaze(result.rgb,i.world); return result;
    }
    if(style>6.5)
    {
     clip(_CityNight-.5); // no daytime road/marking overpaint from exposure streaks
     float sun=saturate(dot(float3(0,1,0),normalize(_BubbleSunDir.xyz)));
     float3 shade=lerp(float3(.58,.58,.58)+.42*sun,float3(.64,.65,.73)+float3(.65,.43,.24)*sun,_SunsetOn);
     float3 road=float3(.25,.26,.26)*shade;
     float taper=saturate(1-abs(i.uv.y));
     float width=1-smoothstep(.35,1,abs(i.uv.x));
     float4 result=float4(lerp(road,road*.25+i.color.rgb*width*taper*2.4,_CityNight),1);
     UNITY_APPLY_FOG(i.fogCoord,result); result.rgb=urbanHaze(result.rgb,i.world); return result;
    }
    if(style>5.5)
    {
     float sun=saturate(dot(normalize(i.normal),normalize(_BubbleSunDir.xyz)));
     float3 body=i.color.rgb*(.48+.52*sun);
     float lamp=1-step(.01,i.color.a);
     float3 night=lerp(body*.22,i.color.rgb*2.2,lamp);
     float4 result=float4(lerp(body,night,_CityNight),1);
     UNITY_APPLY_FOG(i.fogCoord,result); result.rgb=urbanHaze(result.rgb,i.world); return result;
    }
    if(style>4.5)
    {
     // Same top-face asphalt as the city slab by day; smooth analytic sodium/LED spill by night.
     float sun=saturate(dot(float3(0,1,0),normalize(_BubbleSunDir.xyz)));
     float3 dayShade=float3(.58,.58,.58)+.42*sun;
     float3 sunsetShade=lerp(float3(.53,.51,.54),float3(.64,.65,.73),1)+float3(.65,.43,.24)*sun;
     float3 road=float3(.25,.26,.26)*lerp(dayShade,sunsetShade,_SunsetOn);
     float edge=saturate(1-dot(i.uv,i.uv));
     float falloff=edge*edge;
     // Aerial sodium ribbon is the integrated unresolved lamp row. It retains
     // average energy after individual pools/dots fall below a pixel.
     float distant=smoothstep(450,2400,distance(_WorldSpaceCameraPos,i.world));
     float3 sodium=float3(1,.49,.16);
     float3 pool=road*float3(.26,.31,.42)+lerp(i.color.rgb*falloff*.65,sodium*(.65+.42*falloff),distant);
     float4 result=float4(lerp(road,pool,_CityNight),1);
     UNITY_APPLY_FOG(i.fogCoord,result); result.rgb=urbanHaze(result.rgb,i.world); return result;
    }
    float curtain=1-step(.5,abs(style-0));
    float ribbon=1-step(.5,abs(style-2));
    float brick=1-step(.5,abs(style-1));
    float house=step(3.5,style);
    float2 pitch=float2(lerp(3.8,2.2,curtain),lerp(3.6,3.9,brick));
    pitch=lerp(pitch,float2(4.8,3.9),brick);
    pitch=lerp(pitch,float2(3.4,3.1),house);
    float2 grid=i.uv/pitch, cell=floor(grid), f=frac(grid);
    float2 aa=max(fwidth(grid),.001);
    float rnd=hash(cell+float2(i.data.y,3.7)+i.normal.xz*17);
    float2 paneSize=float2(lerp(.31,.475,curtain),lerp(.32,.46,curtain));
    paneSize.x=lerp(paneSize.x,.49,ribbon);
    // Brick walk-ups use paired narrow sash windows rather than office panes.
    float2 paneCoord=lerp(f,float2(frac(f.x*2),f.y),brick);
    float2 paneAA=aa*float2(1+brick,1);
    paneSize=lerp(paneSize,float2(.29,.30),brick);
    float pane=boxMask(paneCoord,paneSize,paneAA);
    float frame=boxMask(paneCoord,max(.05,paneSize-float2(.025,.024)),paneAA);
    float storefront=1-smoothstep(4.3,4.5,i.uv.y);
    pane=lerp(pane,boxMask(frac(i.uv/float2(5,5)),float2(.46,.43),max(fwidth(i.uv/float2(5,5)),.001)),storefront);
    frame=lerp(frame,pane,storefront);
    float3 wall=i.color.rgb;
    // Restrained mortar, weather streaks and floor spandrels; fade before subpixel aliasing.
    float detail=(1-saturate(max(aa.x,aa.y)*8))*_DayAtmosphere.z;
    float mortar=step(.91,frac(i.uv.y*4))+step(.96,frac(i.uv.x*1.6+floor(i.uv.y*4)*.5));
    wall*=1-.10*brick*saturate(mortar)*detail;
    wall*=.94+.06*hash(float2(cell.x,i.data.y));
    float band=1-smoothstep(.03,.03+aa.y,abs(f.y-.04));
    wall=lerp(wall,wall*.72,band*.55);
    float3 n=normalize(i.normal), view=normalize(_WorldSpaceCameraPos-i.world);
    float3 reflected=reflect(-view,n);
    float sky=saturate(reflected.y*.7+.5);
    float3 glass=lerp(float3(.055,.10,.12),float3(.40,.57,.64),sky);
    glass*=lerp(lerp(.72,1.08,rnd),.96,curtain);
    float blind=step(.76,rnd)*(1-step(.60,f.y));
    glass=lerp(glass,float3(.48,.44,.35),blind*.65*(1-curtain));
    float mullion=1-boxMask(frac(i.uv/float2(pitch.x*.5,pitch.y)),float2(.478,.49),max(fwidth(i.uv/float2(pitch.x*.5,pitch.y)),.001));
    glass=lerp(glass,float3(.13,.17,.18),mullion*.7*detail);
    float sun=saturate(dot(n,normalize(_BubbleSunDir.xyz)));
    float3 base=lerp(wall,lerp(float3(.065,.075,.075),glass,frame),pane);
    base*=.58+.42*sun;
    float building=hash(float2(i.data.y,41.7));
    float occupancy=lerp(.30,.65,building);
    float lit=step(1-occupancy,rnd);
    float3 warm=lerp(float3(1,.77,.46),float3(.72,.86,1),step(.65,hash(cell+i.data.y+19)));
    // Analytic pixel-footprint average replaces subpixel hash/grid sampling at aerial distance.
    // A restrained unresolved contribution avoids solid glowing walls at aerial distance.
    float unresolved=smoothstep(.18,.85,max(aa.x,aa.y));
    float coverage=4*paneSize.x*paneSize.y;
    float roomLight=lerp(pane*frame*lit*(.7+.3*hash(cell+27)),coverage*occupancy*.95,unresolved);
    float3 averageLight=lerp(float3(1,.77,.46),float3(.72,.86,1),.35);
    float3 night=base*.20+roomLight*lerp(warm,averageLight,unresolved)*lerp(1.65,2.5,building);
    float4 result=float4(lerp(base,night,_CityNight),1);
    UNITY_APPLY_FOG(i.fogCoord,result); result.rgb=urbanHaze(result.rgb,i.world); return result;
   }
   ENDCG
  }
 }
}
