Shader "FlyingGame/CloudPlayground"
{
 Properties { _SunDir("Toward sun",Vector)=(0.3,0.8,0.4,0) }
 SubShader {
 Tags { "Queue"="Transparent+5" "RenderType"="Transparent" }
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 #include "CloudWispSeed.cginc"
 struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 size:TEXCOORD1; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float fade:TEXCOORD1; float4 color:COLOR; float2 seed:TEXCOORD3; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
 float4 _SunDir;
 v2f vert(appdata v) {
 v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 float3 centre=mul(unity_ObjectToWorld,v.vertex).xyz;
 float distanceToEye=distance(centre,_WorldSpaceCameraPos);
 // Peripheral lobes disappear gradually at distance; main lobes retain the cluster silhouette.
 // Rings out to ~17 km (lane A): whole banks fade 15-18 km, secondary lobes 6-7.5 km.
 o.fade=saturate((18000-distanceToEye)/3000)*saturate((distanceToEye-v.size.x*.3)/max(12,v.size.x*.5));
 o.fade*=lerp(1,saturate((7500-distanceToEye)/1500),v.color.a);
 float3 right=UNITY_MATRIX_V[0].xyz, up=UNITY_MATRIX_V[1].xyz;
 float3 wp=centre+right*(v.uv.x*2-1)*v.size.x+up*(v.uv.y*2-1)*v.size.y;
 o.seed=frac(centre.xz*.0137)*19;
 o.pos=UnityWorldToClipPos(wp); o.uv=v.uv*2-1; o.color=v.color;
 UNITY_TRANSFER_FOG(o,o.pos); return o;
 }
 fixed4 frag(v2f i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 // Codex (lane B, 08:39): a broad, level underside and asymmetric shoulders, rather than a circular bubble.
 float2 q=i.uv;
 q.x+=(WispNoise(i.uv*2+i.seed)-.5)*.18*saturate(q.y+.7);
 float cap=WispNoise(float2(q.x*4.2,1.7)+i.seed);
 q.y=(q.y+.16)/lerp(.80,1.08,cap);
 float r2=dot(q,q);
 clip(1-r2);
 // Lane A + Codex: denser centres and a soft, eroded edge (per-lobe seeded), so overlapping lobes read as one cloud
 // rather than a stack of translucent bubbles.
 float wob=WispNoise(i.uv*6.5+i.seed)*.65+WispNoise(i.uv*13+i.seed)*.35;
 float edge=(1-r2)+(wob-.5)*.32;
 // The level underside, but ragged and soft: a hard cut drew a line under every lobe where lobes overlap.
 float base=smoothstep(-.95,-.45,i.uv.y+(wob-.5)*.35);
 float z=sqrt(saturate(1-r2));
 float3 n=normalize(float3(q*.65,z));
 float3 sunView=mul((float3x3)UNITY_MATRIX_V,_SunDir.xyz);
 float light=saturate(dot(n,normalize(sunView))*.5+.5);
 float3 col=lerp(float3(.57,.64,.74),float3(1,.98,.93),light);
 col=lerp(float3(.55,.61,.70),col,smoothstep(-1,.25,i.uv.y));
 col+=(1-z)*.04;
 fixed4 c=fixed4(col,smoothstep(0,.32,edge)*base*i.fade*.96);
 clip(c.a-.003); UNITY_APPLY_FOG(i.fogCoord,c); return c;
 }
 ENDCG
 }
 }
}
