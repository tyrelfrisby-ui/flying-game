// Built-in, bounded fixture glow. Four vertices per light, no texture, bloom or realtime lights.
Shader "FlyingGame/AirportGlow"
{
    Properties { _HaloPixels ("Distant glow radius in pixels", Float) = 2 }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _AirportDusk, _ReflectionRendering, _HaloPixels, _SunsetOn, _CityNight;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float visible:TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                float3 towardEye=normalize(_WorldSpaceCameraPos-world);
                // Zero normal is omnidirectional. Threshold faces and PAPI point into their approach.
                float directional=dot(v.normal,v.normal);
                float visible=directional>.1?smoothstep(.02,.25,dot(v.normal,towardEye)):1;
                // Beacon alpha encodes opposed green/white rotating heads (120 degrees/s).
                if(v.color.a<.75)
                {
                    float phase=v.color.a<.375?0:UNITY_PI;
                    float beam=cos(atan2(towardEye.x,towardEye.z)-_Time.y*2.094395+phase);
                    visible*=.25+.75*smoothstep(.88,.99,beam);
                }
                float4 view=mul(UNITY_MATRIX_V,float4(world,1));
                float metresPerPixel=2*max(1,-view.z)/max(1,_ScreenParams.y*UNITY_MATRIX_P._m11);
                float radius=max(1.0,min(12.0,metresPerPixel*_HaloPixels));
                view.xy+=v.uv*radius;
                view.z+=.65; // Put the optical glow just in front of its opaque lens.
                o.pos=mul(UNITY_MATRIX_P,view); o.uv=v.uv; o.color=v.color;
                o.visible=visible; return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float strength=max(_AirportDusk,max(_SunsetOn*.78,_CityNight));
                clip(strength-.001);
                clip(.5-_ReflectionRendering); // No un-clipped halos in mirrored water views.
                float radius2=dot(i.uv,i.uv); clip(1-radius2);
                float halo=(exp(-radius2*5)*.35+exp(-radius2*70)*2.7)*(1-radius2);
                return float4(i.color.rgb*halo*strength*i.visible,0);
            }
            ENDCG
        }
    }
}
