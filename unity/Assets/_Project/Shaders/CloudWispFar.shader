// The thin wispy layers beyond the wake-reactive patch (lane A, 2026-11-05; docs/CLOUDS-WAKE-VISUALS.md): one large
// quad per layer with the patch's own seed (CloudWispAdvect's noise, drifting with the same wind), so the layer can be
// seen and flown to from anywhere. Inside the active patch it gives way with exactly the patch's edge fade, so the two
// add up to one layer. Fades with distance and edge-on. Force-included via BuildScript.EnsureAlwaysIncludedShaders.
Shader "FlyingGame/CloudWispFar"
{
 Properties { _Layer("Layer index",Float)=0 }
 SubShader {
 Tags { "Queue"="Transparent+6" "RenderType"="Transparent" }
 Blend SrcAlpha OneMinusSrcAlpha
 Cull Off ZWrite Off
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };
 float _Layer;
 float4 _WispPatch;   // patch origin X/Z, size, layer index (-1: none)
 float4 _WispDrift;
 #include "CloudWispSeed.cginc"
 v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; UNITY_TRANSFER_FOG(o,o.pos); return o; }
 fixed4 frag(v2f i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float2 world=i.world.xz-_WispDrift.xy;
 // Anti-aliasing: an octave whose cells are smaller than about two pixels here is replaced by its mean.
 float2 fw=fwidth(world*.016);
 float coarse01=saturate(1.5-max(fw.x*.45,fw.y*3)*2), fine01=saturate(1.5-max(fw.x*1.5,fw.y*8)*2);
 float density=WispSeed(world,coarse01,fine01);
 float3 toEye=i.world-_WorldSpaceCameraPos;
 float dist=length(toEye);
 float a=density*.42;
 a*=saturate((20000-dist)/5000);                                   // the far edge of the layer
 a*=saturate(abs(toEye.y)/8);                                       // edge-on, as thin as it is
 if (abs(_WispPatch.w-_Layer)<.5)
 {
   float2 uv=(i.world.xz-_WispPatch.xy)/_WispPatch.z;
   float2 inside=step(0,uv)*step(uv,1);
   float2 edge=smoothstep(0,.18,uv)*smoothstep(0,.18,1-uv);
   a*=1-inside.x*inside.y*edge.x*edge.y;                            // the patch's share, handed over
 }
 fixed4 c=fixed4(.88,.92,.98,a); UNITY_APPLY_FOG(i.fogCoord,c); return c;
 }
 ENDCG
 }
 }
}
