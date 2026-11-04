// A test probe, never drawn in the meadow: for every pixel of a one-pixel-high picture it reads a screen position (x, y 0..1, y up) from _ProbeIn and writes what the drum of the hills does there:
// (the weight of the pulse, how near the plate is to the fog colour, the plate's luma). HillPulsePlayModeTests compares a flat band with one that follows the painted haze.
Shader "Hidden/Downbeat/HillPulseProbe"
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
                float3 plate = MeadowSamplePlate(sample.xy);
                float3 away = plate - _MeadowFogColor.rgb;
                return float4(MeadowHillPulseWeight(plate, 1.0 - sample.y), exp(-dot(away, away) * HillHazeSharpness), dot(plate, float3(0.2126, 0.7152, 0.0722)), 0);
            }
            ENDHLSL
        }
    }
}
