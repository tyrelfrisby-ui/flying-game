Shader "FlyingGame/CloudWispAdvect"
{
 Properties { _MainTex("Density",2D)="black" {} _WakeTex("Wake velocity",2D)="black" {}
 _RecoverySeconds("Density recovery seconds",Float)=90
 _Entrainment("Vertical clearing rate",Float)=0.12 }
 SubShader { Cull Off ZWrite Off ZTest Always
 Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex,_WakeTex;
 float4 _WindStep; // wind X/Z m/s, dt seconds, inverse world tile size
 float4 _Origin; // new world X/Z, old-to-new UV offset
 float _Initialize, _RecoverySeconds, _Entrainment;
 float4 _WispDrift; // the layer's wind drift so far (Unity X/Z m), shared with CloudWispFar
 float4 _WispMask;  // a low patch: centre X/Z, radius, 1 (w = 0: a layer, WispCover)
 #include "CloudWispSeed.cginc"
 float seed(float2 uv)
 {
   float2 world=_Origin.xy+uv/_WindStep.w;
   float2 w=world-_WispDrift.xy;
   return _WispMask.w>.5 ? WispDetail(w,1,1)*WispPatchCover(world,_WispMask.xyz) : WispSeed(w,1,1);
 }
 fixed4 frag(v2f_img i):SV_Target {
 float4 wake=tex2D(_WakeTex,i.uv);
 float2 back=i.uv+_Origin.zw-(_WindStep.xy+wake.rg)*_WindStep.z*_WindStep.w;
 float density=tex2D(_MainTex,back).r;
 float outside=step(back.x,0)+step(1,back.x)+step(back.y,0)+step(1,back.y);
 float fresh=seed(i.uv);
 density=lerp(density,fresh,saturate(outside+_Initialize));
 // Backtracing through the actual velocity curls the density; vertical entrainment clears the sheet.
 // Codex 08:39: a tenth of the earlier vorticity clearing, so the advected curls stay visible.
 density*=exp(-min(8,abs(wake.a)*_Entrainment+wake.b*.01)*_WindStep.z);
 density=lerp(density,fresh,1-exp(-_WindStep.z/max(1,_RecoverySeconds)));
 return float4(density,0,0,1);
 }
 ENDCG
 }
 }
}
