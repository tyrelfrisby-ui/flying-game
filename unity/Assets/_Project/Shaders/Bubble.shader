// Soap-bubble look for BubbleField (owner request): a transparent sphere that is nearly clear in the
// middle and bright/glossy at the silhouette (fresnel rim), with a hard specular glint from the sun and
// a faint thin-film iridescence on the rim. Drawn per bubble via Graphics.DrawMesh with a
// MaterialPropertyBlock carrying _Color (temperature tint) and _Alpha (centre-of-frame fade), so
// nothing here depends on instancing (which failed to place instances on Metal).
//
// Perf notes (ARCHITECTURE.md #1 risk = transparent overdraw): bubbles are ~1 m spheres 12 m apart,
// so screen coverage stays low; ZWrite off + back-face cull keeps it a single cheap blend per pixel.
Shader "FlyingGame/Bubble"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _BodyAlpha ("Body alpha", Range(0, 1)) = 0.06
        _RimAlpha ("Rim alpha", Range(0, 1)) = 0.85
        _RimPower ("Rim power", Range(0.5, 8)) = 2.6
        _SpecPower ("Specular power", Range(4, 256)) = 48
        _SpecStrength ("Specular strength", Range(0, 2)) = 0.9
        _Iridescence ("Iridescence", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            fixed4 _Color;
            half _Alpha, _BodyAlpha, _RimAlpha, _RimPower, _SpecPower, _SpecStrength, _Iridescence;
            float4 _BubbleSunDir; // world-space direction TOWARD the sun, set by BubbleField

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = _WorldSpaceCameraPos - worldPos;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                float ndv = saturate(dot(n, v));

                // Fresnel rim: clear in the middle, bright glossy edge.
                float rim = pow(1.0 - ndv, _RimPower);

                // Sun glint (Blinn-Phong) — the "shiny" highlight that sells the sphere.
                float3 l = normalize(_BubbleSunDir.xyz);
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), _SpecPower) * _SpecStrength;

                // Thin-film iridescence: hue drifts around the rim with view angle.
                float phase = (1.0 - ndv) * 6.2831853;
                float3 film = 0.5 + 0.5 * float3(sin(phase), sin(phase + 2.094), sin(phase + 4.189));
                float3 rimColor = lerp(float3(1, 1, 1), film, _Iridescence);

                float3 col = _Color.rgb * (0.85 + 0.15 * rim);
                col = col * (1.0 - rim) + rimColor * _Color.rgb * rim;
                col += spec;

                float a = saturate(_BodyAlpha + rim * _RimAlpha + spec * 0.6) * _Alpha * _Color.a;
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
