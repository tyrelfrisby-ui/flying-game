// Cosmetic history only. The wave equation and its forcing are left untouched.
Shader "Hidden/FlyingGame/WakeFoam"
{
    Properties { _MainTex ("Foam history", 2D) = "black" {} }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Height;
            float4 _Height_TexelSize;
            float _Cell;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 d = _Height_TexelSize.xy;
                float h = tex2D(_Height, i.uv).r;
                float left = tex2D(_Height, i.uv-float2(d.x,0)).r;
                float right = tex2D(_Height, i.uv+float2(d.x,0)).r;
                float back = tex2D(_Height, i.uv-float2(0,d.y)).r;
                float front = tex2D(_Height, i.uv+float2(0,d.y)).r;
                float slope = length(float2(right-left,front-back)) / (2 * _Cell);
                // Compression identifies positive crests, rather than whitening both sides of every ripple.
                float compression = max(0, 4*h-left-right-back-front) / _Cell;
                float breaking = smoothstep(.10, .38, slope) * smoothstep(.04, .18, compression);
                float old = tex2D(_MainTex, i.uv).r * .98758; // 4 s e-fold at the solver's 0.05 s step
                return float4(saturate(max(old, breaking * .85)),0,0,1);
            }
            ENDCG
        }
    }
}
