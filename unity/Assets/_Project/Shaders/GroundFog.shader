// Ground fog over a strip or a lake (lane A, 2026-11-05; docs/CLOUDS-WAKE-VISUALS.md "Ground fog"). One box per site
// (CloudSites.Fog); its back faces start a short march (12 steps) from the eye, or the box, to the box's far side or the
// scene (_CameraDepthTexture), whichever is nearer. Density: the wake-stirred patch (GroundFogAdvect) inside _FogPatch,
// the unstirred fog (GroundFogSeed.cginc) outside it, handed over across the patch's edge. Thin by design: about a
// third of the light is lost looking straight down through it, and no line of sight loses more than _FogMaxAlpha.
// Drawn after the water and before the clouds. Force-included via BuildScript.EnsureAlwaysIncludedShaders.
Shader "FlyingGame/GroundFog"
{
 Properties { _FogSigma("Extinction at density 1 (1/m)",Float)=0.1 _FogMaxAlpha("Most a line of sight loses",Float)=0.7 _FogShadow("Sun shadow per density-metre",Float)=0.3 }
 SubShader {
 Tags { "Queue"="Transparent+4" "RenderType"="Transparent" "IgnoreProjector"="True" }
 Blend One OneMinusSrcAlpha
 Cull Front ZWrite Off ZTest Always
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #pragma multi_compile_instancing
 #pragma target 3.0
 #include "UnityCG.cginc"
 #include "GroundFogSeed.cginc"
 UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
 sampler2D _FogA, _FogB;
 float4 _FogPatch;        // origin X/Z, size, box height H; size <= 0: no patch on this site
 float _FogSurface;       // the surface's height (m)
 float _FogSigma, _FogMaxAlpha, _FogShadow, _WaterSceneDepthOff;
 float4 _BubbleSunDir;
 struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float4 sp:TEXCOORD1; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
 v2f vert(appdata v)
 {
     v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
     o.pos = UnityObjectToClipPos(v.vertex);
     o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
     o.sp = ComputeScreenPos(o.pos);
     UNITY_TRANSFER_FOG(o, o.pos);
     return o;
 }
 // Density at p (x) and the fog above it in the same column (y, density x metres): the stirred patch inside _FogPatch,
 // the unstirred fog outside, handed over across the patch's edge. The column above is free inside the patch: the two
 // fetches already hold all eight slices.
 float2 Density(float3 p)
 {
     float h01 = (p.y - _FogSurface) / _FogPatch.w;
     float2 col = FogColumn(p.xz);
     float2 seed = float2(FogProfile(col.x, h01) * col.y, FogSeedAbove(col, h01) * _FogPatch.w);
     if (_FogPatch.z <= 0) return seed;
     float2 uv = (p.xz - _FogPatch.xy) / _FogPatch.z;
     float2 e = smoothstep(0, .08, uv) * smoothstep(0, .08, 1 - uv);
     float share = e.x * e.y;
     if (share <= 0) return seed;
     float4 a = tex2Dlod(_FogA, float4(uv, 0, 0)), b = tex2Dlod(_FogB, float4(uv, 0, 0));
     float s = h01 * 8 - .5;
     float4 wa = saturate(1 - abs(s - float4(0,1,2,3))), wb = saturate(1 - abs(s - float4(4,5,6,7)));
     float d = s < 0 ? a.x : dot(a, wa) + dot(b, wb);
     // Above: each slice's share of the column over s (a slice is H/8 thick; the one we are in counts by the part above).
     float4 ua = saturate(float4(0,1,2,3) + .5 - s), ub = saturate(float4(4,5,6,7) + .5 - s);
     float above = (dot(a, ua) + dot(b, ub)) * _FogPatch.w / 8;
     return lerp(seed, float2(d, above), share);
 }
 fixed4 frag(v2f i):SV_Target
 {
     UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
     float3 eye = _WorldSpaceCameraPos;
     float3 ray = i.world - eye;
     float far = length(ray);
     float3 dir = ray / far;
     // Entry into the box (object space slabs; the box is the unit cube).
     float3 oe = mul(unity_WorldToObject, float4(eye, 1)).xyz;
     float3 od = mul((float3x3)unity_WorldToObject, dir);
     float3 inv = 1 / (abs(od) > 1e-6 ? od : 1e-6);
     float3 t0 = (-.5 - oe) * inv, t1 = (.5 - oe) * inv;
     float3 tmin = min(t0, t1);
     float near = max(0, max(tmin.x, max(tmin.y, tmin.z)));
     // The scene in front of the far face (terrain, an aeroplane, a hangar) ends the march.
     if (_WaterSceneDepthOff < .5)
     {
         float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp)));
         float cosv = dot(dir, -UNITY_MATRIX_V[2].xyz);
         far = min(far, sceneZ / max(cosv, 1e-3));
     }
     if (far <= near) discard;
     const int N = 12;
     float stepLen = (far - near) / N;
     float jitter = WispHash(i.sp.xy / max(i.sp.w, 1e-4) * _ScreenParams.xy + _Time.y);
     float3 sun = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > .001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(.4, .8, -.4));
     float trans = 1;
     float3 col = 0;
     for (int k = 0; k < N; k++)
     {
         float3 p = eye + dir * (near + (k + jitter) * stepLen);
         float2 d = Density(p);
         if (d.x <= .002) continue;
         // Sunlit from above through the fog over this point (its own column: _FogShadow per density-metre), plus sky
         // light. So fog the vortices lifted into the clear air above shines, and the trench they cut reads as a
         // gap in it.
         float sunT = exp(-_FogShadow * d.y / max(sun.y, .25));
         float3 c = float3(.62, .67, .74) + float3(.40, .38, .34) * sunT * saturate(sun.y + .2);
         float a = 1 - exp(-_FogSigma * d.x * stepLen);
         col += trans * a * c;
         trans *= 1 - a;
     }
     float alpha = 1 - trans;
     if (alpha < .003) discard;
     float scale = min(1, _FogMaxAlpha / alpha);   // see-through, always
     fixed4 o = fixed4(col * scale, alpha * scale);
     // Scene fog toward its colour by the same distance, premultiplied.
     UNITY_APPLY_FOG_COLOR(i.fogCoord, o, fixed4(unity_FogColor.rgb * o.a, o.a));
     return o;
 }
 ENDCG
 }
 }
}
