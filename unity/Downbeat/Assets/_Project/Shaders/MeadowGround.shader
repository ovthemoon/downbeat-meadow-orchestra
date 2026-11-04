// The bare ground under the grass. Dark and soft, lit by the low sun from behind and by the sky; patches of colour from noise. Far away it fades
// into the backdrop plate (MeadowApplyHaze).
Shader "Downbeat/Meadow/Ground"
{
    Properties
    {
        // linear RGB (set from the code as vectors, so that no colour-space conversion happens twice)
        _GroundDark ("Ground dark (linear)", Vector) = (0.011, 0.027, 0.006, 1)
        _GroundMid ("Ground mid (linear)", Vector) = (0.027, 0.07, 0.012, 1)
        _GroundLight ("Ground light (linear)", Vector) = (0.07, 0.125, 0.021, 1)
        _PatchScale ("Patch scale (m)", Float) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Ground"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GroundDark;
                half4 _GroundMid;
                half4 _GroundLight;
                float _PatchScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 clipXYW : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.clipXYW = float3(output.positionCS.xy, output.positionCS.w);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz;
                float patches = MeadowFbm(p / _PatchScale);
                float fine = MeadowFbm(p / (_PatchScale * 0.18) + 31.7);
                half3 baseColor = lerp(_GroundDark.rgb, _GroundMid.rgb, smoothstep(0.25, 0.65, patches));
                baseColor = lerp(baseColor, _GroundLight.rgb, smoothstep(0.55, 0.85, patches * 0.65 + fine * 0.35) * 0.85);

                float3 normal = normalize(input.normalWS);
                Light sun = GetMainLight();
                float facing = saturate(dot(normal, sun.direction));
                half3 lighting = _MeadowAmbientColor.rgb * (0.55 + 0.45 * normal.y) + sun.color * facing;
                half3 color = baseColor * lighting;

                // the sun is low and in front of the viewer: ground seen at a grazing angle catches its glow, more the farther it is
                float3 toViewer = _WorldSpaceCameraPos - input.positionWS;
                float viewDistance = length(toViewer);
                float graze = pow(1.0 - saturate(dot(normal, toViewer / viewDistance)), 3.0);
                color += sun.color * baseColor * graze * 1.1;

                color *= MeadowCloudLight(MeadowBreezeField(p));   // the shadow of a cloud deck sliding over the field
                color = MeadowApplyHaze(color, MeadowScreenUV(input.clipXYW));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
