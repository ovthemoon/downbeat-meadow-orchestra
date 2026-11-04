// The ribbon of light that follows a hand: a TrailRenderer drawn additively. Along the trail the vertex colour fades from the bright head to the cool tail; across it (uv.y) it
// is soft in the middle and gone at the edges. _Fade (0..1, from HandWindView) lets the whole ribbon dissolve when the hand is gone. HDR above 1 for the camera's bloom.
Shader "Downbeat/Meadow/WindRibbon"
{
    Properties
    {
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" }

        Pass
        {
            Name "Ribbon"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float across = abs(input.uv.y * 2.0 - 1.0);
                float soft = pow(saturate(1.0 - across), 1.6);
                return half4(input.color.rgb, input.color.a * soft * _Fade);
            }
            ENDHLSL
        }
    }
}
