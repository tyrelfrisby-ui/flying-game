Shader "FlyingGame/VR HUD Backing" {
    SubShader {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Pass { Cull Off ZTest Always ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        float4 vert(float4 p:POSITION):SV_POSITION { return UnityObjectToClipPos(p); }
        fixed4 frag():SV_Target { return fixed4(.015,.015,.015,.55); }
        ENDCG
    } }
}
