Shader "FlyingGame/PowellSandstone"
{
    Properties { _Color ("Tint",Color)=(1,1,1,1) _Shelf ("Shelf fill",Float)=0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "PowellSandstone.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f { float4 pos:SV_POSITION; float3 wp:TEXCOORD0; float3 n:TEXCOORD1; UNITY_FOG_COORDS(2) };
            float4 _Color, _BubbleSunDir, _ReflectionClip;
            float _SunsetOn, _ReflectionRendering, _Shelf;
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                o.wp=mul(unity_ObjectToWorld,v.vertex).xyz; o.n=UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            fixed4 frag(v2f i,float facing:VFACE):SV_Target
            {
                if(_ReflectionRendering>.5) clip(dot(float4(i.wp,1),_ReflectionClip));
                float3 n=normalize(i.n)*(facing>=0?1:-1), rock;
                powellSurface(i.wp,n,rock);
                float3 l=dot(_BubbleSunDir.xyz,_BubbleSunDir.xyz)>.001?normalize(_BubbleSunDir.xyz):normalize(float3(.4,.8,-.4));
                float d=saturate(dot(n,l));
                float3 fill=lerp(float3(.28,.34,.43),float3(.43,.48,.54),n.y*.5+.5);
                // Warm reflected rock fill under the overhang: retain the physical
                // shelf, but avoid a uniform cold-grey beam across the curtain.
                fill=lerp(fill,max(fill,float3(.56,.47,.38)),_Shelf);
                fixed4 c=fixed4(rock*_Color.rgb*lerp(fill+float3(.68,.57,.43)*d,float3(.32,.30,.34)+float3(.81,.53,.29)*d,_SunsetOn),1);
                UNITY_APPLY_FOG(i.fogCoord,c); return c;
            }
            ENDCG
        }
    }
}
