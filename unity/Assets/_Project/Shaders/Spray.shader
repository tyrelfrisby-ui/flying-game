// Procedural water sheet fragments and fine mist: no imported texture or texture fetches.
Shader "FlyingGame/Spray"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Mist ("Fine mist", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float clipDistance : TEXCOORD1; };
            fixed4 _Color;
            float _Mist, _ReflectionRendering;
            float4 _ReflectionClip;
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 q = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(q), hash(q+float2(1,0)), f.x), lerp(hash(q+float2(0,1)), hash(q+1), f.x), f.y);
            }
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; o.clipDistance = dot(mul(unity_ObjectToWorld,v.vertex), _ReflectionClip); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                float2 p = (i.uv - .5) * 2;
                float cloud = noise(i.uv * 9) * .65 + noise(i.uv * 23) * .35;
                float edge = saturate(1 - dot(p,p));
                // Perforated sheets with glistening beads; the stretched particle supplies the trajectory.
                float sheet = smoothstep(.28, .65, cloud) * edge;
                float beads = smoothstep(.67, .82, noise(i.uv * 37)) * edge;
                float alpha = lerp(saturate(sheet * 1.3 + beads), edge * edge * (.25 + cloud * .65), _Mist);
                float light = .78 + .22 * cloud;
                return fixed4(i.color.rgb * light, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
