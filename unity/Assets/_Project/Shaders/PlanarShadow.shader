// Ground shadow for the airframe (GroundShadow.cs): the aircraft's meshes are re-drawn flattened onto
// the ground plane along the sun direction with this dark translucent material. The stencil test makes
// each pixel darken ONCE per frame even where the flattened wing/fuselage/tail overlap, so the shadow
// is a single flat silhouette. Depth offset keeps it from z-fighting the ground.
Shader "FlyingGame/PlanarShadow"
{
    Properties
    {
        _Color ("Shadow", Color) = (0, 0, 0, 0.45)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "IgnoreProjector" = "True" } // after the water surface so it shows on lakes
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1
        Stencil
        {
            Ref 7
            Comp NotEqual
            Pass Replace
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            float4 vert (float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag () : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
