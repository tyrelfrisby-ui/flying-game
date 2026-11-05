Shader "FlyingGame/CloudPlayground"
{
 Properties { _SunDir("Toward sun",Vector)=(0.3,0.8,0.4,0) }
 SubShader {
 Tags { "Queue"="Transparent-20" "RenderType"="Transparent" }
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 size:TEXCOORD1; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float fade:TEXCOORD1; float4 color:COLOR; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
 float4 _SunDir;
 v2f vert(appdata v) {
 v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 float3 centre=mul(unity_ObjectToWorld,v.vertex).xyz;
 float distanceToEye=distance(centre,_WorldSpaceCameraPos);
 // Peripheral lobes disappear gradually at distance; main lobes retain the cluster silhouette.
 o.fade=saturate((11000-distanceToEye)/1800)*saturate((distanceToEye-v.size.x*.3)/max(12,v.size.x*.5));
 o.fade*=lerp(1,saturate((4500-distanceToEye)/1200),v.color.a);
 float3 right=UNITY_MATRIX_V[0].xyz, up=UNITY_MATRIX_V[1].xyz;
 float3 wp=centre+right*(v.uv.x*2-1)*v.size.x+up*(v.uv.y*2-1)*v.size.y;
 o.pos=UnityWorldToClipPos(wp); o.uv=v.uv*2-1; o.color=v.color;
 UNITY_TRANSFER_FOG(o,o.pos); return o;
 }
 fixed4 frag(v2f i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float r2=dot(i.uv,i.uv); clip(1-r2);
 float z=sqrt(saturate(1-r2));
 float3 n=normalize(float3(i.uv,z));
 float3 sunView=mul((float3x3)UNITY_MATRIX_V,_SunDir.xyz);
 float light=saturate(dot(n,normalize(sunView))*.5+.5);
 float3 col=lerp(float3(.57,.64,.74),float3(1,.98,.93),light);
 col=lerp(float3(.53,.59,.68),col,smoothstep(-.85,.2,i.uv.y));
 col+=(1-z)*.09;
 fixed4 c=fixed4(col,smoothstep(0,.28,1-r2)*i.fade*.82);
 clip(c.a-.003); UNITY_APPLY_FOG(i.fogCoord,c); return c;
 }
 ENDCG
 }
 }
}
