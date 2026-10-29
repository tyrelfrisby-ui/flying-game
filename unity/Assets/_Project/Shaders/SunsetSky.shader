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
 fixed4 frag(v2f i):SV_Target
 {
  if(_CityNight>.5)
  {
   float3 ray=normalize(i.ray);
   float glow=exp(-abs(ray.y)*9);
   return fixed4(lerp(float3(.015,.022,.045),float3(.085,.068,.057),glow),1);
  }
  return fixed4(_SunsetOn > .5 ? SunsetSky(i.ray) : DaySky(i.ray),1);
 }
 ENDCG }
 }
}
