namespace Naqsh.Engine.Gpu;

/// <summary>HLSL used by the compositor. All mixing happens in 16-bit float RGBA.</summary>
internal static class Shaders
{
    public const string Source = """
        struct VSOut
        {
            float4 pos : SV_Position;
            float2 uv  : TEXCOORD0;
        };

        // Full-screen triangle from SV_VertexID: no vertex buffer, no input layout.
        VSOut VSMain(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.uv = uv;
            o.pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
            return o;
        }

        Texture2D texA : register(t0);
        Texture2D texB : register(t1);
        SamplerState samp : register(s0);

        cbuffer MixParams : register(b0)
        {
            float4 rectA;   // xy = scale, zw = offset of the source inside the frame (0..1)
            float4 rectB;
            float4 slateA;  // colour used when A has no picture
            float4 slateB;
            float  mixAmount;
            float  hasA;
            float  hasB;
            float  pad0;
        };

        float4 SampleFit(Texture2D t, float4 r, float2 uv)
        {
            float2 suv = (uv - r.zw) / r.xy;
            if (any(suv < 0.0) || any(suv > 1.0))
            {
                return float4(0.0, 0.0, 0.0, 1.0);
            }
            float4 c = t.SampleLevel(samp, suv, 0);
            // Straight alpha composited over black.
            return float4(c.rgb * c.a, 1.0);
        }

        float4 PSMix(VSOut i) : SV_Target
        {
            float4 a = hasA > 0.5 ? SampleFit(texA, rectA, i.uv) : float4(slateA.rgb, 1.0);
            float4 b = hasB > 0.5 ? SampleFit(texB, rectB, i.uv) : float4(slateB.rgb, 1.0);
            return lerp(a, b, mixAmount);
        }

        // Scale/convert an FP16 frame into an 8-bit target (monitors, encoder).
        float4 PSCopy(VSOut i) : SV_Target
        {
            float4 c = texA.SampleLevel(samp, i.uv, 0);
            return float4(saturate(c.rgb), 1.0);
        }
        """;
}
