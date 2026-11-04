// A test probe, never drawn in the meadow: for every pixel of a one-pixel-high picture it reads one sample (x, z, height up the blade, the blade's hash number) from _ProbeIn and writes
// what the GPU's steady wind says there, read from the baked field: (bend x, bend z, gust, cloud shadow). BreezeGpuParityPlayModeTests compares that with Core/Meadow/BreezeModel.cs.
Shader "Hidden/Downbeat/BreezeProbe"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Probe"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"

            TEXTURE2D(_ProbeIn);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 sample = LOAD_TEXTURE2D(_ProbeIn, int2((int)input.positionCS.x, 0));
                float4 field = MeadowBreezeField(sample.xy);
                float gust;
                float2 bend = MeadowBreezeBend(sample.xy, sample.z, sample.w, field, gust);
                return float4(bend, gust, field.w);
            }
            ENDHLSL
        }
    }
}
