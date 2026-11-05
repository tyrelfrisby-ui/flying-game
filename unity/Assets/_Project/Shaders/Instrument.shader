// A cockpit instrument's face, markings and needles: lit like the panel, but never darker than a lit instrument.
//
// Ty 2026-11-04: "the instruments are dark and illegible most of the time they need to remain bright enough to be
// seen in all lighting conditions". The glTF PBR material a dial face imports with is lit by the sun and the sky
// alone, and a panel faces AFT: in any attitude that puts the sun behind the panel it is lit by ambient only, and in
// a canopy's shadow or at dusk that is a fraction of a marking's albedo.
//
// A real aerobatic panel stays readable because its markings are luminous (post lights / backlit faces) and the
// glass keeps glare off the paint. Here, per pixel:
//
//     colour = albedo * ( max(sun + sky, _Floor) + _Glow )
//
//   * _Floor: the least light the face ever gets. In full sun it does nothing (sun + sky > floor); in shadow,
//     backlit or at dusk it holds the face at what a cockpit in open shade reads at.
//   * _Glow: added on top whatever the light, as an emitter. It scales with the ALBEDO, so a white numeral or
//     needle glows and the black face around it stays black: contrast is kept in every light, and there is
//     nothing coloured or haloed about it (not neon — a marking is the colour it is painted).
//
// Alpha: the source material's blend state is copied into _SrcBlend/_DstBlend/_ZWrite (a cut-out PNG must stay
// cut out — see CLAUDE.md, "An image material with a transparent PNG is not transparent by itself"), and _Cutoff
// is the glTF alphaCutoff for a MASK material (-1 = no clip). Two-sided, back faces with the normal flipped, like
// FlyingGame/Lit. Honours the water reflection's clip plane like every airframe shader.
//
// Loaded through Resources/Materials/Instrument.mat (InstrumentLighting), which is what puts it in every player —
// iOS, macOS and the Quest — without a per-build always-included list.
Shader "FlyingGame/Instrument"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _MainTex ("Face art", 2D) = "white" {}
        _Floor ("Least light on the face", Range(0, 1)) = 0.32
        _Glow ("Marking glow", Range(0, 1)) = 0.30
        _Cutoff ("Alpha cutoff (-1 = none)", Float) = -1
        [HideInInspector] _SrcBlend ("Src blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Floor, _Glow, _Cutoff;
            float _ReflectionRendering;
            float4 _ReflectionClip;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 n : TEXCOORD1;
                float clipDistance : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.clipDistance = dot(mul(unity_ObjectToWorld, v.vertex), _ReflectionClip);
                return o;
            }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                if (_Cutoff >= 0) clip(albedo.a - _Cutoff);
                float3 n = normalize(i.n) * (facing >= 0 ? 1.0 : -1.0);
                // The directional sun (ForwardBase gives the brightest one here) and the sky's spherical harmonics.
                float3 light = _LightColor0.rgb * saturate(dot(n, normalize(_WorldSpaceLightPos0.xyz))) + ShadeSH9(float4(n, 1));
                float3 shade = max(light, _Floor.xxx) + _Glow;
                return fixed4(albedo.rgb * shade, albedo.a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
