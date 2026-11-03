Shader "FlyingGame/FuelSlick"
{
    Properties { _SurfaceY ("Water surface height", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _SurfaceY;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 wp:TEXCOORD1; };
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 q=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(q),hash(q+float2(1,0)),f.x),lerp(hash(q+float2(0,1)),hash(q+1),f.x),f.y);
            }
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                o.wp=mul(unity_ObjectToWorld,v.vertex).xyz; o.uv=v.uv; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                // Per-camera rejection also covers egress and reflection cameras below this water plane.
                clip(_WorldSpaceCameraPos.y - _SurfaceY);
                float2 p=i.uv;
                // Irregular edge stays inside the physics radius; only the visual feather is warped.
                float edgeRadius=lerp(.88,1.0,noise(p*5+17));
                float edge=1-smoothstep(edgeRadius-.08,edgeRadius,length(p));
                clip(edge-.001);
                float d=lerp(.2,1.5,noise(p*4+noise(p*9)*.8)); // micrometres; spatial, no wall-clock animation
                float cosI=saturate(normalize(_WorldSpaceCameraPos-i.wp).y);
                const float n=1.42, waterN=1.33;
                float cosT=sqrt(1-(1-cosI*cosI)/(n*n)); // Snell air -> gasoline
                float cosW=sqrt(1-(1-cosI*cosI)/(waterN*waterN));
                // Two-beam Fresnel interference, RGB representative wavelengths in micrometres.
                // Air->gasoline reflection has ONE half-wave flip; gasoline->water has none.
                float rs01=(cosI-n*cosT)/(cosI+n*cosT);
                float rp01=(n*cosI-cosT)/(n*cosI+cosT);
                float rs12=(n*cosT-waterN*cosW)/(n*cosT+waterN*cosW);
                float rp12=(waterN*cosT-n*cosW)/(waterN*cosT+n*cosW);
                float3 phase=6.2831853*(2*n*d*cosT)/float3(.650,.510,.475);
                // Signed Fresnel amplitudes encode the phase flip (negative rs01, positive rs12).
                float3 reflectance=.5*(rs01*rs01+rs12*rs12+2*rs01*rs12*cos(phase)
                    +rp01*rp01+rp12*rp12+2*rp01*rp12*cos(phase));
                // A mostly transparent sheen; compress near-grazing reflection for readability over real water.
                float peak=max(max(reflectance.r,reflectance.g),reflectance.b);
                return float4(reflectance/max(peak,.0001), edge*min(peak,.28));
            }
            ENDCG
        }
    }
    Fallback Off
}
