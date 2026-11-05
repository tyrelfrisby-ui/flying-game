Shader "FlyingGame/CloudWispAdvect"
{
 Properties { _MainTex("Density",2D)="black" {} _WakeTex("Wake velocity",2D)="black" {} }
 SubShader { Cull Off ZWrite Off ZTest Always
 Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex,_WakeTex;
 float4 _WindStep; // wind X/Z m/s, dt seconds, inverse world tile size
 float4 _Origin; // new world X/Z, old-to-new UV offset
 float _Initialize;
 float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
 float noise(float2 p) { float2 a=floor(p), f=frac(p); f=f*f*(3-2*f); return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y); }
 float seed(float2 uv) {
 float2 world=(_Origin.xy+uv/_WindStep.w)*.016;
 float n=noise(world*float2(.45,3))+noise(world*float2(1.5,8))*.35;
 return smoothstep(.45,.92,n)*.58;
 }
 fixed4 frag(v2f_img i):SV_Target {
 float4 wake=tex2D(_WakeTex,i.uv);
 float2 back=i.uv+_Origin.zw-(_WindStep.xy+wake.rg)*_WindStep.z*_WindStep.w;
 float density=tex2D(_MainTex,back).r;
 float outside=step(back.x,0)+step(1,back.x)+step(back.y,0)+step(1,back.y);
 float fresh=seed(i.uv);
 density=lerp(density,fresh,saturate(outside+_Initialize));
 // Backtracing through the actual velocity curls the density; vertical entrainment clears the sheet.
 density*=exp(-min(8,abs(wake.a)*.12+wake.b*.035)*_WindStep.z);
 density=lerp(density,fresh,1-exp(-_WindStep.z/90));
 return float4(density,0,0,1);
 }
 ENDCG
 }
 }
}
