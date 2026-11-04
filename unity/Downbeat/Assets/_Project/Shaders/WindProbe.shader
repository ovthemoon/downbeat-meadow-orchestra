// A test probe, never drawn in the meadow: for every pixel of a one-pixel-high picture it reads one sample (x, z, height up the blade, the blade's hash number) from _ProbeIn and writes what
// the whole wind of the grass (the breeze, the waves and the hands: MeadowWindAt) says there: (bend x, bend z, strength, glow). HandWindPlayModeTests compares a run with a hand and one without.
Shader "Hidden/Downbeat/WindProbe"
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"
            #include "MeadowWind.hlsl"

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
                float4 wind = MeadowWindAt(sample.xy, sample.w, sample.z, field, gust);
                return float4(wind.x, wind.y, wind.w, wind.z);
            }
            ENDHLSL
        }
    }
}
