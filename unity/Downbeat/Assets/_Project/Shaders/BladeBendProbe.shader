// A test probe, never drawn in the meadow: for every pixel of a picture two rows high it reads one sample (bend x, bend z, t up the blade, the blade's height) from _BendIn and writes what MeadowBladeCurve
// (the bend of the grass and the flower stems, MeadowWind.hlsl) says there: row 0 the point relative to the foot (x, up, z), row 1 the direction the blade goes (a unit vector). The global _MeadowWindA.z holds
// the most a blade bends. BladeBendGpuParityPlayModeTests compares that with Core/Meadow/BladeBend.cs.
Shader "Hidden/Downbeat/BladeBendProbe"
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

            TEXTURE2D(_BendIn);

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
                int2 pixel = int2(input.positionCS.xy);
                float4 sample = LOAD_TEXTURE2D(_BendIn, int2(pixel.x, 0));
                float3 tangent;
                float3 onBlade = MeadowBladeCurve(sample.xy, sample.z, sample.w, tangent);
                return float4(pixel.y == 0 ? onBlade : tangent, 0.0);
            }
            ENDHLSL
        }
    }
}
