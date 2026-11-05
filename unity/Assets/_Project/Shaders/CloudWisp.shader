Shader "FlyingGame/CloudWisp"
{
 Properties { _MainTex("Density",2D)="black" {}
 _NearFadeHeight("Sheet intersection fade metres",Float)=8
 _Opacity("Density opacity",Range(0,1))=.42
 _Tint("Scattered light",Color)=(.88,.92,.98,1) }
 SubShader {
 Tags { "Queue"="Transparent+7" "RenderType"="Transparent" }
 Blend SrcAlpha OneMinusSrcAlpha
 Cull Off ZWrite Off
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float height:TEXCOORD1; float2 world:TEXCOORD3; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
 sampler2D _MainTex;
 float _Opacity, _NearFadeHeight; float4 _Tint;
 float4 _WispDrift, _WispMask;
 #include "CloudWispSeed.cginc"
 v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; float3 w=mul(unity_ObjectToWorld,v.vertex).xyz; o.world=w.xz; o.height=abs(w.y-_WorldSpaceCameraPos.y); UNITY_TRANSFER_FOG(o,o.pos); return o; }
 fixed4 frag(v2f i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float2 edge=smoothstep(0,.18,i.uv)*smoothstep(0,.18,1-i.uv);
 float weight=edge.x*edge.y;
 // Same vertical-distance fade as the far sheet (Codex 08:39).
 float d=tex2D(_MainTex,i.uv).r*_Opacity*saturate(i.height/max(.1,_NearFadeHeight));
 // Codex 08:39: the far sheet drew first with (1 - weight) of the undisturbed density; dividing by what it let through
 // makes the two compose to exactly that density where the patch is undisturbed, so the hand-over leaves no
 // darker or lighter rectangle. Lane A: a low patch's far quad uses its round mask, so the same expression here.
 float far=_WispMask.w>.5 ? WispDetail(i.world-_WispDrift.xy,1,1)*WispPatchCover(i.world,_WispMask.xyz) : WispSeed(i.world-_WispDrift.xy,1,1);
 float farAlpha=far*.42*saturate(i.height/8)*(1-weight);
 float a=d*weight/max(.001,1-farAlpha);
 fixed4 c=fixed4(_Tint.rgb,a); UNITY_APPLY_FOG(i.fogCoord,c); return c;
 }
 ENDCG
 }
 }
}
