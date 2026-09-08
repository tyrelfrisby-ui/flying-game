// Plain unlit translucent colour (canopy glass, propeller discs). Built-in transparent shaders are
// stripped from the iOS player, so this tiny one is force-included via BuildScript.
Shader "FlyingGame/UnlitTransparent"
{
    Properties { _Color ("Color", Color) = (0.2, 0.3, 0.4, 0.5) }
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
            fixed4 _Color;
            float4 vert (float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag () : SV_Target { return _Color; }
            ENDCG
        }
    }
}
