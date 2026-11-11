// The cloud playground's billowing lobe impostors (Codex lane B + lane A, docs/CLOUDS-WAKE-VISUALS.md): one
// camera-facing quad per lobe, shaded as a sphere and eroded by seeded noise so overlapping lobes read as one
// cloud. Since 2026-11-11 this also draws the THERMAL CUMULUS (Bridge/Cumulus.cs), on Ty's instruction to make
// the playground clouds double as them — which needed the one thing a bank does not have and a cumulus is
// defined by: a dead-flat base at the condensation level, shared by every lobe, in WORLD space. _Cumulus turns
// that on; the banks and the low patches leave it at 0 and are untouched.
Shader "FlyingGame/CloudPlayground"
{
 Properties {
  _SunDir("Toward sun",Vector)=(0.3,0.8,0.4,0)
  _Cumulus("Flat base + whole-cloud shading (0/1)",Float)=0
  _BaseY("Cloud base (world y)",Float)=0
  _TopY("Cloud top (world y)",Float)=1
 }
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
 struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float fade:TEXCOORD1; float4 color:COLOR; float2 seed:TEXCOORD3; float wy:TEXCOORD4; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
 float4 _SunDir;
 float _Cumulus,_BaseY,_TopY;
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
 o.wy=wp.y;                       // the fragment's own height: the flat base is cut in world space, not in uv
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
 // A CUMULUS base is not each lobe's own underside, it is the lifting condensation level: the one height at
 // which the rising air reaches its dew point, the same for every lobe and for every cloud of the day, which is
 // why a field of them has its bases in a plane. Cut there instead, in world metres, over a 26 m band with
 // +-9 m of the lobe's own noise in it so the line is a cloud's and not a razor's.
 if (_Cumulus>.5) base=smoothstep(0,26,i.wy-_BaseY+(wob-.5)*18);
 float z=sqrt(saturate(1-r2));
 float3 n=normalize(float3(q*.65,z));
 float3 sunView=mul((float3x3)UNITY_MATRIX_V,_SunDir.xyz);
 float light=saturate(dot(n,normalize(sunView))*.5+.5);
 float3 col=lerp(float3(.57,.64,.74),float3(1,.98,.93),light);
 // Grey underside brightening to sunlit white. For a bank that runs over each lobe (its own uv.y); for a
 // cumulus it has to run over the WHOLE cloud, base to crown, or a turret 900 m up is shaded like the lobe
 // sitting on the base beside it and the vertical development stops reading.
 float h=_Cumulus>.5 ? saturate((i.wy-_BaseY)/max(1,_TopY-_BaseY)) : smoothstep(-1,.25,i.uv.y);
 // A cumulus base is darker than a bank's underside and it stays dark further up: a kilometre of cloud stands
 // between it and the sun, which is why a pilot reads the lift from how black the base is.
 float3 shade=_Cumulus>.5 ? float3(.44,.49,.59) : float3(.55,.61,.70);
 col=lerp(shade,col,_Cumulus>.5 ? smoothstep(0,.38,h) : h);
 col+=(1-z)*.04;
 fixed4 c=fixed4(col,smoothstep(0,.32,edge)*base*i.fade*.96);
 clip(c.a-.003); UNITY_APPLY_FOG(i.fogCoord,c); return c;
 }
 ENDCG
 }
 }
}
