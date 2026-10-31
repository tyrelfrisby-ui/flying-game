Shader "FlyingGame/SunsetSky" {
 SubShader { Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
 Cull Off ZWrite Off
 Pass { CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "SunsetSky.cginc"
 struct v2f { float4 pos:SV_POSITION; float3 ray:TEXCOORD0; };
 v2f vert(float4 v:POSITION) { v2f o; o.pos=UnityObjectToClipPos(v); o.ray=v.xyz; return o; }
 float _CityNight;
 #include "UrbanGlow.cginc"
 float urbanDome(float3 ray,float4 city)
 {
  if(city.w<.5) return 0;
  float3 delta=city.xyz+float3(0,550,0)-_WorldSpaceCameraPos;
  float span=max(1,length(delta));
  float angularRadius=clamp(1850/span,.06,.55);
  return city.w*exp((dot(ray,delta/span)-1)/(angularRadius*angularRadius))*exp(-span/26000);
 }
 fixed4 frag(v2f i):SV_Target
 {
  if(_CityNight>.5)
  {
   float3 ray=normalize(i.ray);
   float glow=exp(-abs(ray.y)*9);
   if(_UrbanNightFill<.5) return fixed4(lerp(float3(.015,.022,.045),float3(.085,.068,.057),glow),1);
   float dome=urbanDome(ray,_UrbanGlow0)+urbanDome(ray,_UrbanGlow1)
    +urbanDome(ray,_UrbanGlow2)+urbanDome(ray,_UrbanGlow3)
    +urbanDome(ray,_UrbanGlow4)+urbanDome(ray,_UrbanGlow5)
    +urbanDome(ray,_UrbanGlow6)+urbanDome(ray,_UrbanGlow7);
   float3 moon=lerp(float3(.022,.032,.060),float3(.065,.077,.10),glow);
   return fixed4(urbanHaze(moon+float3(.07,.032,.009)*saturate(dome),_WorldSpaceCameraPos+ray*40000),1);
  }
  return fixed4(_SunsetOn > .5 ? SunsetSky(i.ray) : DaySky(i.ray),1);
 }
 ENDCG }
 }
}
