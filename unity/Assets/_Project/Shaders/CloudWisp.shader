Shader "FlyingGame/CloudWisp"
{
 Properties { _MainTex("Density",2D)="black" {} }
 SubShader {
 Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
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
 struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float distance:TEXCOORD1; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
 sampler2D _MainTex;
 v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.distance=distance(mul(unity_ObjectToWorld,v.vertex).xyz,_WorldSpaceCameraPos); UNITY_TRANSFER_FOG(o,o.pos); return o; }
 fixed4 frag(v2f i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float2 edge=smoothstep(0,.18,i.uv)*smoothstep(0,.18,1-i.uv);
 float a=tex2D(_MainTex,i.uv).r*edge.x*edge.y*.42*saturate(i.distance/8);
 fixed4 c=fixed4(.88,.92,.98,a); UNITY_APPLY_FOG(i.fogCoord,c); return c;
 }
 ENDCG
 }
 }
}
