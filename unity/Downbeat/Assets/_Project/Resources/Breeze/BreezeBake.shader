// Works out the gusts and the cloud shadows for a grid of ground points, once per frame (BreezeField.cs draws this into a small picture). The grass, the flowers, the ground and the painted far
// meadow read the result (MeadowBreezeField) instead of each working out the gusts again. Per texel: the gust as the foot, the middle and the tip of a blade meet it (red, green, blue: the tip
// meets a gust a little later than the foot) and the cloud shadow (alpha). A muted gust (--wind-mute gusts) is left out here.
Shader "Hidden/Downbeat/BreezeBake"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Bake"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/_Project/Shaders/MeadowCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 ground = _BreezeFieldRect.xy + input.uv / _BreezeFieldRect.zw;   // the middle of this texel, as a ground point
                float muteGusts = _MeadowWindMute2.x * _MeadowWindMute.x;
                return float4(MeadowGust(ground, 0.0) * muteGusts, MeadowGust(ground, 0.5) * muteGusts, MeadowGust(ground, 1.0) * muteGusts, MeadowCloudShadow(ground));
            }
            ENDHLSL
        }
    }
}
