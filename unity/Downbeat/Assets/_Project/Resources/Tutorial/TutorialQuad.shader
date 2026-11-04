// The quads of the tutorial: a texture (the hand glyphs, the tick badge, the arrows) tinted by _Color, or a shape worked out in the shader (a rounded box with a fill and a border: the dim over the meadow, the cell
// outlines, the rings, the dots, the bars), alpha-blended over the picture, no lighting, no depth. One quad of size _Size is placed and scaled by its transform; the shape reaches to the edge of the quad.
// _Color is the colour (an sRGB colour, Unity makes it linear) and the alpha of everything that is drawn.
Shader "Downbeat/Tutorial/Quad"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        // 0: the texture times the colour. 1: a rounded box.
        _Shape ("Shape", Float) = 0
        // the size of the quad in the units of the scene (what the radius and the border width are in)
        _Size ("Size", Vector) = (1, 1, 0, 0)
        _Radius ("Corner radius", Float) = 0
        _Border ("Border width", Float) = 0
        _FillAlpha ("Fill alpha", Float) = 1
        _BorderAlpha ("Border alpha", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }

        Pass
        {
            Name "Quad"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // plain uniforms (no constant buffer): the view sets them per renderer with a property block, which the SRP batcher would not take anyway
            half4 _Color;
            float _Shape;
            float4 _Size;
            float _Radius;
            float _Border;
            float _FillAlpha;
            float _BorderAlpha;

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

            // How much of the pixel is inside an edge: 1 inside, 0 outside, a pixel wide ramp between (d is the distance to the edge, negative inside, in scene units).
            float Coverage(float d, float pixel)
            {
                return saturate(0.5 - d / pixel);
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_Shape < 0.5)
                {
                    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                }

                float2 halfSize = _Size.xy * 0.5;
                float2 p = (input.uv - 0.5) * _Size.xy;
                float radius = min(_Radius, min(halfSize.x, halfSize.y));
                float2 q = abs(p) - halfSize + radius;
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
                float pixel = max(fwidth(d), 1e-6);

                float outer = Coverage(d, pixel);
                float borderMask = (1.0 - Coverage(d + _Border, pixel)) * step(1e-6, _Border);   // 1 from the inner edge of the border outwards
                float alpha = outer * lerp(_FillAlpha, _BorderAlpha, borderMask);
                return half4(_Color.rgb, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
