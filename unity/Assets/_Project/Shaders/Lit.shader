// Cheap directional shading for the airframe (one dot product, no shadow maps): flat unlit colour made
// thin surfaces vanish edge-on (the glider's stab from the chase view). Two-sided: generated lofts have
// arbitrary winding, so back faces are drawn with the normal flipped. Sun direction comes from the
// _BubbleSunDir global BubbleField sets each frame (fallback if unset).
Shader "FlyingGame/Lit"
{
    Properties { _Color ("Color", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SunsetSky.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                float clipDistance : TEXCOORD1;
                float3 wp : TEXCOORD2;
            };

            fixed4 _Color;
            float4 _BubbleSunDir;
            float _ReflectionRendering;
            float4 _ReflectionClip;

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.clipDistance = dot(mul(unity_ObjectToWorld, v.vertex), _ReflectionClip);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                float3 n = normalize(i.n) * (facing >= 0 ? 1.0 : -1.0);
                float3 l = dot(_BubbleSunDir.xyz, _BubbleSunDir.xyz) > 0.001 ? normalize(_BubbleSunDir.xyz) : normalize(float3(0.4, 0.8, -0.4));
                float d = saturate(dot(n, l));
                float3 col = _Color.rgb * lerp(float3(.58,.58,.58) + .42*d, float3(.24,.27,.34) + SunsetEnvironment(n)*.60 + float3(.60,.37,.18)*d, _SunsetOn);
                if (_SunsetOn > .5)
                {
                    float3 v = normalize(_WorldSpaceCameraPos-i.wp);
                    float3 env = SunsetEnvironment(reflect(-v,n));
                    float rim = pow(1-saturate(dot(n,v)),3);
                    col += env * (.055 + .17 * rim) * saturate(dot(_Color.rgb,float3(.3,.4,.3))*3);
                }
                return fixed4(col,1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
