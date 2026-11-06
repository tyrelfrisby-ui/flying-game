// Procedural water sheet fragments and fine mist: no imported texture or texture fetches.
Shader "FlyingGame/Spray"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Mist ("Fine mist", Range(0,1)) = 0
        _Foam ("Surface foam", Range(0,1)) = 0
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
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float clipDistance : TEXCOORD1; float4 sp : TEXCOORD2; float eyeDepth : TEXCOORD3; };
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            fixed4 _Color;
            float _Mist, _Foam, _ReflectionRendering, _WaterSceneDepthOff, _WaterDepthErrK;
            float4 _ReflectionClip;
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 q = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(q), hash(q+float2(1,0)), f.x), lerp(hash(q+float2(0,1)), hash(q+1), f.x), f.y);
            }
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; o.clipDistance = dot(mul(unity_ObjectToWorld,v.vertex), _ReflectionClip); o.sp = ComputeScreenPos(o.pos); o.eyeDepth = -UnityObjectToViewPos(v.vertex).z; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                if (_ReflectionRendering > .5) clip(i.clipDistance);
                float2 p = (i.uv - .5) * 2;
                float cloud = noise(i.uv * 9) * .65 + noise(i.uv * 23) * .35;
                float edge = saturate(1 - dot(p,p));
                // Perforated sheets with glistening beads; the stretched particle supplies the trajectory.
                float sheet = smoothstep(.38, .72, cloud) * edge * (.45 + .55 * noise(i.uv * float2(29, 6)));
                float beads = smoothstep(.76, .91, noise(i.uv * 53)) * edge;
                float alpha = lerp(saturate(sheet * 1.3 + beads), edge * edge * (.25 + cloud * .65), _Mist);
                // Surface foam is thin broken cell walls, not another spherical mist puff.
                float cells = noise(i.uv * 12 + noise(i.uv * 3) * 2);
                float lace = 1 - smoothstep(.025, .12, abs(cells - .52));
                alpha = lerp(alpha, lace * edge * (.4 + .6*cloud), _Foam);
                // Round droplet clouds, torn sheet edges and a soft intersection with hull/bank.
                // Fade only when this camera owns a useful depth texture; the mirror has none.
                float separation = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp))) - i.eyeDepth;
                float soft = saturate(separation / .22);
                if (_ReflectionRendering > .5 || _WaterSceneDepthOff > .5 || i.eyeDepth*i.eyeDepth*_WaterDepthErrK > .25) soft = 1;
                alpha *= soft * smoothstep(0, .24, edge);
                float light = .86 + .14 * cloud;
                return fixed4(i.color.rgb * light, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
