// Wake waves (owner 2026-10-07: "a V shaped wake from the hull or from each pontoon … make the waves from the wake last
// for 5 minutes and reflect off shorelines realistically"). One step of the 2-D wave equation on a height grid covering
// a lake's bounding box: h' = (2h − h_prev + C²·∇²h)·damp, plus the floats' / hull's forcing (Gaussian stamps). The
// lake is an ellipse filling the grid; outside it (the shore) the height is held at zero — a fixed boundary, so waves
// reflect off the shoreline (with the phase flip a wall gives). Height in metres in the red channel.
Shader "Hidden/FlyingGame/WaveSim"
{
    Properties { _MainTex ("Current", 2D) = "black" {} _Prev ("Previous", 2D) = "black" {} }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Prev;
            float4 _MainTex_TexelSize;
            float _C2, _Damp;
            int _SrcCount;
            float4 _Src[8];   // (u, v, radius in uv·x, amplitude m)
            float _Aspect;    // texel height / width in uv units (radius is measured in x-uv)

            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv, d = _MainTex_TexelSize.xy;
                float2 e = (uv - 0.5) / 0.5;
                if (dot(e, e) > 0.94) return 0;   // shore: held still (reflecting boundary)
                float h = tex2D(_MainTex, uv).r;
                float hp = tex2D(_Prev, uv).r;
                // 9-point (isotropic) Laplacian: the 5-point one combed the wake into grid-aligned streaks.
                float edge = tex2D(_MainTex, uv + float2(d.x, 0)).r + tex2D(_MainTex, uv - float2(d.x, 0)).r
                           + tex2D(_MainTex, uv + float2(0, d.y)).r + tex2D(_MainTex, uv - float2(0, d.y)).r;
                float corner = tex2D(_MainTex, uv + d).r + tex2D(_MainTex, uv - d).r
                             + tex2D(_MainTex, uv + float2(d.x, -d.y)).r + tex2D(_MainTex, uv + float2(-d.x, d.y)).r;
                float lap = (4.0 * edge + corner - 20.0 * h) / 6.0;
                float hn = (2.0 * h - hp + _C2 * lap) * _Damp;
                // A shoreline reflects only part of a wave (the shallows and the beach take the rest): extra loss in the
                // last few percent of the radius, so roughly half the height comes back off the shore.
                float r2 = dot(e, e);
                if (r2 > 0.80) hn *= lerp(1.0, 0.985, saturate((r2 - 0.80) / 0.14));
                for (int k = 0; k < 8; k++)
                {
                    if (k >= _SrcCount) break;
                    float2 q = (uv - _Src[k].xy) * float2(1.0, _Aspect);
                    hn += _Src[k].w * exp(-dot(q, q) / max(1e-8, _Src[k].z * _Src[k].z));
                }
                return float4(clamp(hn, -2.0, 2.0), 0, 0, 1);
            }
            ENDCG
        }
    }
}
