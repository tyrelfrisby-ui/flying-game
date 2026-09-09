// Glass pane: a faint tint that brightens toward grazing angles (Fresnel) with a soft sky-ish reflection, so the
// aerobatic box reads as a glass box in the sky. Two-sided, transparent, no depth write — nothing collides.
Shader "FlyingGame/Glass"
{
    Properties
    {
        _Color ("Tint", Color) = (0.6, 0.8, 1.0, 0.12)
        _Reflect ("Reflectivity", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Reflect;
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wv : TEXCOORD1; };
            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wv = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wn); float3 v = normalize(i.wv);
                float f = pow(1.0 - abs(dot(n, v)), 3.0);              // Fresnel: grazing = brighter
                float3 r = reflect(-v, n);
                float3 sky = lerp(float3(0.55, 0.7, 0.9), float3(0.85, 0.92, 1.0), saturate(r.y));   // sky/horizon
                float3 col = lerp(_Color.rgb, sky, _Reflect * (0.4 + 0.6 * f));
                return fixed4(col, saturate(_Color.a + 0.45 * f));
            }
            ENDCG
        }
    }
}
