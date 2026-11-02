// The Quest recorder (QuestRecorder): packs the recording camera's frame into I420 for Android's H.264 encoder,
// on the GPU, so the CPU never converts a pixel and the readback is 1.5 bytes a pixel instead of 4.
//
// Target: an R8 texture _PackSize.x wide and 1.5 x _PackSize.y tall, read back with AsyncGPUReadback, which
// returns texture row 0 (uv.y = 0) FIRST. So memory row m is uv.y = (m + 0.5) / rows, and the frame is written
// top row first, the way the encoder wants it: rows 0..h-1 luma, then U (w/2 x h/2, two chroma rows per
// memory row), then V. BT.601 limited range, the encoder's default. Layout pinned by QuestRecordingTests
// (QuestRecording.I420Layout).
// Force-included in the player via BuildScript.EnsureAlwaysIncludedShaders (it is only ever found by name).
Shader "Hidden/FlyingGame/QuestRecordPack"
{
    Properties { _MainTex ("Frame", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _PackSize;   // x = frame width, y = frame height

            float3 RgbAt(float2 uv)
            {
                float3 c = tex2D(_MainTex, uv).rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                c = LinearToGammaSpace(c);   // the encoder wants gamma-encoded values
            #endif
                return saturate(c);
            }

            float4 frag (v2f_img i) : SV_Target
            {
                float w = _PackSize.x, h = _PackSize.y;
                float x = floor(i.uv.x * w);
                float m = floor(i.uv.y * h * 1.5);
                if (m < h)
                {
                    float3 c = RgbAt(float2((x + 0.5) / w, 1.0 - (m + 0.5) / h));
                    return dot(c, float3(0.2568, 0.5041, 0.0979)) + 0.0627;
                }
                float r = m - h, quarter = h * 0.25;
                bool isV = r >= quarter;
                if (isV) r -= quarter;
                float idx = r * w + x, cw = w * 0.5;
                float cy = floor(idx / cw);
                float cx = idx - cy * cw;
                // The middle of the 2 x 2 block: bilinear sampling averages its four pixels.
                float3 c = RgbAt(float2((2.0 * cx + 1.0) / w, 1.0 - (2.0 * cy + 1.0) / h));
                float u = dot(c, float3(-0.1482, -0.2910, 0.4392)) + 0.5020;
                float v = dot(c, float3(0.4392, -0.3678, -0.0714)) + 0.5020;
                return isV ? v : u;
            }
            ENDCG
        }
    }
}
