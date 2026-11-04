// The far picture: three same-composition plates (blue hour, playing dusk, sunset) cross-faded by the sky arc, drawn over the whole screen
// before anything else. The plate keeps its proportions and fills the screen (the longer side is cropped, like CSS "cover").
// Over the sky of the plates two layers of soft clouds drift in the wind direction (the near one lower, so it seems to move faster); their colours follow the sky arc like the plates.
// The strong tier works the cloud noise out for every pixel; the weak tier reads it from a baked tile (CloudTile.cs). Both draw the same clouds and both move.
Shader "Downbeat/Meadow/BackdropBlend"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"

            float4 _CloudNear;      // x altitude of the layer's plane (m), y size of a cloud cell (m), z coverage, w softness
            float4 _CloudFar;
            float4 _CloudShift;     // x, y: how far the near layer has drifted on its plane (m, wrapped), z, w: the far layer
            float4 _CloudParams;    // x opacity of the near layer, y of the far layer, z picture row above which clouds are fully there, w row at which they are gone (the hills)
            float4 _CloudLitTint;   // rgb: what the sky behind a cloud is multiplied by on the sunlit side (the hue of the stop's cloud colour times the brightness of that side), worked out on the CPU (CloudTint.cs)
            float4 _CloudShadeTint; // rgb: the same for the shaded side

            TEXTURE2D(_MeadowCloudTile);   // the weak tier: the cloud noise baked over one whole period (CloudTile.cs): red the noise, green the same noise a little towards the sun
            SAMPLER(sampler_MeadowCloudTile);

            static const float CLOUD_TILE_CELLS = 256.0;   // = CloudNoise.Period: the tile wraps where the noise repeats
            static const float CLOUD_SUN_OFFSET = 0.3;     // = CloudTile.SunOffsetCells: how far towards the sun the lit side is looked for, in cells

            // The colours of one layer where the cloud has the given density, and where the noise a little towards the sun has `towardSun`. A cloud only tints and lightens or darkens the sky behind it
            // (the plates are finished pictures: the clouds must not lift or darken their mood on average): the lit side is a little lighter and takes the hue of the stop's lit cloud colour, the shaded
            // side a little darker with the hue of its shaded colour (the tints, see CloudTint.cs).
            half3 CloudColour(half3 color, float density, float towardSun, float opacity, float fade)
            {
                float lit = saturate(0.55 + (density - towardSun) * 2.2);
                half3 bright = color * _CloudLitTint.rgb;
                half3 dark = color * _CloudShadeTint.rgb;
                return lerp(color, lerp(dark, bright, lit), density * opacity * fade);
            }

            // Where on its plane the view ray meets a layer, in cells of the noise (the drift is in `shift`).
            float2 CloudCell(float3 ray, float4 layer, float2 shift)
            {
                return (ray.xz / ray.y * layer.x - shift) / layer.y;
            }

            // One layer, the strong tier: the cloud shape is noise on the layer's plane, worked out for this pixel, moving with the wind. The edge that faces the sun (towards the horizon) is lit.
            half3 CloudLayer(half3 color, float3 ray, float4 layer, float2 shift, float opacity, float fade)
            {
                float2 uv = CloudCell(ray, layer, shift);
                float edge = 1.0 - layer.z;
                float density = smoothstep(edge - layer.w, edge + layer.w, MeadowCloudFbm(uv));
                if (density <= 0.001) return color;
                float towardSun = smoothstep(edge - layer.w, edge + layer.w, MeadowCloudFbm(uv + float2(0.0, CLOUD_SUN_OFFSET)));
                return CloudColour(color, density, towardSun, opacity, fade);
            }

            // The same layer, the weak tier: the noise is read from the baked tile (one read for the cloud and its lit side). It moves exactly like the strong one: the picture on the plane is still, only
            // `shift` changes. LOD 0, no mip chain: the clouds fade out well above the horizon, where a pixel is a fraction of a texel.
            half3 CloudLayerTile(half3 color, float3 ray, float4 layer, float2 shift, float opacity, float fade)
            {
                float2 uv = CloudCell(ray, layer, shift) * (1.0 / CLOUD_TILE_CELLS);
                float2 noise = SAMPLE_TEXTURE2D_LOD(_MeadowCloudTile, sampler_MeadowCloudTile, uv, 0).rg;
                float edge = 1.0 - layer.z;
                float density = smoothstep(edge - layer.w, edge + layer.w, noise.r);
                if (density <= 0.001) return color;
                float towardSun = smoothstep(edge - layer.w, edge + layer.w, noise.g);
                return CloudColour(color, density, towardSun, opacity, fade);
            }

            half3 MeadowSkyClouds(half3 color, float2 screenUV)
            {
                float fade = smoothstep(_CloudParams.w, _CloudParams.z, 1.0 - screenUV.y);
                if (fade <= 0.0) return color;
                float3 ray = MeadowViewRay(screenUV);
                if (ray.y < 0.02) return color;
                if (_MeadowSkyCheap > 0.5)
                {
                    color = CloudLayerTile(color, ray, _CloudFar, _CloudShift.zw, _CloudParams.y, fade);
                    return CloudLayerTile(color, ray, _CloudNear, _CloudShift.xy, _CloudParams.x, fade);
                }
                color = CloudLayer(color, ray, _CloudFar, _CloudShift.zw, _CloudParams.y, fade);
                return CloudLayer(color, ray, _CloudNear, _CloudShift.xy, _CloudParams.x, fade);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float2 ndc = input.positionOS.xy; // the quad mesh covers -1..1: it is placed in clip space directly, whatever the camera does
                output.positionCS = float4(ndc.x, ndc.y * _ProjectionParams.x, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
                output.screenUV = ndc * 0.5 + 0.5;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 plate = MeadowSamplePlate(input.screenUV) * MeadowPlateLife(input.screenUV);
                half3 color = MeadowSkyClouds(plate, input.screenUV);
                // the drum of the hills: the haze over the far hills lights up in the fog colour, where the painted hills are hazy (MeadowHillPulseWeight)
                color += _MeadowFogColor.rgb * (_MeadowHillPulse.x * MeadowHillPulseWeight(plate, 1.0 - input.screenUV.y));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
