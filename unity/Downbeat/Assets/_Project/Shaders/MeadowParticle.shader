// Soft round particles (seeds, petals, fireflies, pollen) drawn in front of the backdrop plate. The round gradient is a small texture made in code. Additive: the light of fireflies,
// seeds and pollen; the vertex colour carries the colour (HDR above 1, so the camera's bloom catches it) and the fade. Depth-tested against the grass (it can hide a particle) but writes no depth.
// The same haze as the grass: far particles dissolve into the plate. _DstBlend decides between light that is added (seeds, fireflies, pollen) and things that are blended (petals).
Shader "Downbeat/Meadow/Particle"
{
    Properties
    {
        _MainTex ("Gradient", 2D) = "white" {}
        // HDR gain of the vertex colour (which is 8 bit and cannot exceed 1): above 1 the middle of a firefly is brighter than the screen can show and the bloom catches it
        _Gain ("HDR gain", Float) = 1
        // how much brighter a particle is at the peak of its answer to a bell (a number per particle, from the custom vertex stream; fireflies only, 0 for the rest): colour * (1 + answer * _AnswerGain)
        _AnswerGain ("Answer gain", Float) = 0
        // the fewest pixels across a particle is drawn, however far away (0 = no floor): the corners of its quad are moved out from its middle (the Center vertex stream) when it is smaller
        _MinPixels ("Size floor in pixels", Float) = 0
        // blend factor of the destination: 1 (One) adds light, 10 (OneMinusSrcAlpha) blends an opaque-ish thing; set per material by ParticleDirector
        _DstBlend ("Destination blend", Float) = 1
        // how much of the haze applies (1 = all of it: the particle dissolves into the plate where the picture has handed over to it; 0 = none: the effects in the air, up in the sky, are not hazed away)
        _HazeAmount ("Haze amount", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Particle"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha [_DstBlend]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Gain;
            float _AnswerGain;
            float _MinPixels;
            float _HazeAmount;

            // The vertex streams (ParticleDirector.ConfigureStreams: Position, Color, UV, Custom1.x, Center) are packed by Unity tightly into TEXCOORDs, four numbers to each, and a stream may run over from one
            // to the next (measured with a probe particle: Center is TEXCOORD0.w, TEXCOORD1.xy): TEXCOORD0 holds u, v, the answer to a bell and the first number of the middle of the particle.
            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 t0 : TEXCOORD0;   // x, y: the UV, z: how strongly this particle answers a bell now (0..1, Custom1.x), w: the x of the middle of the particle
                float2 t1 : TEXCOORD1;   // y, z of the middle of the particle (in the space of the vertices)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 clipXYW : TEXCOORD1;
                float answer : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                // the size floor (Core/Meadow/ParticleSizeFloor.cs): a particle smaller than _MinPixels on the screen is scaled out from its middle to exactly that; a square's corner is size / sqrt(2) from its middle
                float3 corner = input.positionOS.xyz;
                float3 center = float3(input.t0.w, input.t1.x, input.t1.y);
                if (_MinPixels > 0.0)
                {
                    float3 offset = corner - center;
                    float viewDepth = max(-TransformWorldToView(TransformObjectToWorld(center)).z, 1e-3);
                    float pixels = length(offset) * 1.41421356 * unity_CameraProjection._m11 * 0.5 * _ScreenParams.y / viewDepth;
                    corner = center + offset * max(1.0, _MinPixels / max(pixels, 1e-4));
                }
                output.positionCS = TransformObjectToHClip(corner);
                output.color = input.color;
                output.uv = input.t0.xy;
                output.answer = input.t0.z;
                output.clipXYW = float3(output.positionCS.xy, output.positionCS.w);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half3 color = input.color.rgb * (_Gain * (1.0 + saturate(input.answer) * _AnswerGain));
                // the same haze as the grass: where the picture has handed over to the plate, particles are gone
                float2 screenUV = MeadowScreenUV(input.clipXYW);
                float row = 1.0 - screenUV.y;
                float fadeU = saturate((_MeadowHaze.x - row) / max(_MeadowHaze.x - _MeadowHaze.y, 1e-3)) * _HazeAmount;
                float visible = 1.0 - smoothstep(0.5, 1.0, fadeU);
                return half4(color * visible, mask * input.color.a);
            }
            ENDHLSL
        }
    }
}
