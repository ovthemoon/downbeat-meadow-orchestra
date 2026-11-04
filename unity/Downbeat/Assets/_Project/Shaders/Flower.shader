// The flowers: each is an instance of a species mesh that holds two poses (the open flower and the closed bud, see FlowerMeshFactory); the bloom of the instance (0 bud .. 1 open,
// from a buffer the flower field writes every frame) blends them in the vertex shader. They sway with the same wind as the grass and shrink into the backdrop plate at the same picture
// rows. Lit with the grass's model: sky light tinted by the sky stop, a wrap of the low sun, the sun shining through petals and sepals towards the viewer (strongest in the sun's column of
// the screen, HDR on open petals so the bloom of the camera catches it), a soft rim, the same distance dimming and haze. Stems and sepals are green and lit like grass blades. The petals
// are darker at the base and lighter at the edge and have veins; the dandelion's filaments dissolve with a dither towards their ends; the poppy has a dark heart with a ring of stamens.
Shader "Downbeat/Meadow/Flower"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Flower"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"
            #include "MeadowWind.hlsl"

            struct Flower
            {
                float3 pos;
                float yaw;
                float scale;
                float tint;
                float phase;
                float lean;
                float height;
            };

            StructuredBuffer<Flower> _Flowers;
            StructuredBuffer<float> _Bloom;
            float4 _MeadowGrassFade;   // x fadeFromRow, y fadeToRow, z fadeMinHeight
            float4 _MeadowGrassDim;    // x farDim, y farDimFrom, z farDimTo, w nearDarkness (the grass's depth dimming, metres in front of the viewer)
            float4 _MeadowGrassDim2;   // x nearDarkStart, y nearDarkEnd, z sunColumn

            CBUFFER_START(UnityPerMaterial)
                float4 _BudColor;
                float4 _OpenColor;
                float4 _CenterColor;
                float4 _StemColor;
                float4 _FlowerParams;    // x stem height, y species (0 bellflower, 1 dandelion, 2 poppy), z bud glow, w open glow
                float4 _FlowerLight;     // x ambient mix, y sun body, z translucency
            CBUFFER_END

            static const float TwoPi = 6.2831853;

            struct Attributes
            {
                float4 positionOS : POSITION;   // open pose
                float4 color : COLOR;           // x gradient, y region, z occlusion, w alpha / across / angle
                float3 closedPos : TEXCOORD1;
                float3 openNormal : TEXCOORD2;
                float3 closedNormal : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 clipXYW : TEXCOORD2;
                float4 shade : TEXCOORD3;    // x gradient, y region, z occlusion, w bloom
                float3 inst : TEXCOORD4;     // x tint, y phase, z the vertex alpha channel
                float3 wind : TEXCOORD5;     // x wave strength, y glow, z cloud-shadow light factor
            };

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Flower f = _Flowers[instanceID];
                float bloom = saturate(_Bloom[instanceID]);

                // hand-over to the plate: by the picture row of the foot, like the grass
                float4 footCS = TransformWorldToHClip(f.pos);
                float rowUV = MeadowScreenUV(float3(footCS.xy, footCS.w)).y;
                float row = 1.0 - rowUV;
                float fadeU = saturate((_MeadowGrassFade.x - row) / max(_MeadowGrassFade.x - _MeadowGrassFade.y, 1e-3));
                float fade = 1.0 - fadeU * fadeU * (3.0 - 2.0 * fadeU);
                float size = f.scale * lerp(_MeadowGrassFade.z, 1.0, pow(fade, 1.4));

                float region = input.color.y;
                float3 pose = lerp(input.closedPos, input.positionOS.xyz, bloom);
                float3 normal = normalize(lerp(input.closedNormal, input.openNormal, bloom));
                // a shorter or taller stem: the stem scales, the head rides on its top
                float stemTop = _FlowerParams.x;
                if (region < 0.1) pose.y *= f.height; else pose.y += stemTop * (f.height - 1.0);
                float sy, cy;
                sincos(f.yaw, sy, cy);
                float3 local = pose * size;
                local = float3(local.x * cy - local.z * sy, local.y, local.x * sy + local.z * cy);
                normal = float3(normal.x * cy - normal.z * sy, normal.y, normal.x * sy + normal.z * cy);

                // sway: the stem leans with the wind, the head goes with the top of the stem
                float stemH = max(stemTop * f.height * size, 1e-3);
                float t = saturate(local.y / stemH);
                float4 field = MeadowBreezeField(f.pos.xz);
                float gust;
                float4 wind = MeadowWindAt(f.pos.xz, f.phase, t, field, gust);
                float2 bend = f.lean * float2(cos(f.phase * TwoPi), sin(f.phase * TwoPi)) + wind.xy * _MeadowWindA.y * 0.5;
                float3 stemTangent;
                float3 arc = MeadowBladeCurve(bend, t, stemH, stemTangent);   // the stem bends along an arc as long as it is, like a blade; the head rides on its tip
                local += arc - float3(0.0, stemH * t, 0.0);

                Varyings output;
                output.positionWS = f.pos + local;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = normal;
                output.clipXYW = float3(output.positionCS.xy, output.positionCS.w);
                output.shade = float4(input.color.xyz, bloom);
                output.inst = float3(f.tint, f.phase, input.color.w);
                output.wind = float3(wind.w, wind.z, MeadowCloudLight(field));
                return output;
            }

            float InterleavedNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float gradient = input.shade.x;
                float region = input.shade.y;
                float occlusion = input.shade.z;
                float bloom = input.shade.w;
                float tint = input.inst.x;
                float alpha = input.inst.z;
                float species = _FlowerParams.y;

                bool isStem = region < 0.1;
                bool isSepal = region >= 0.1 && region < 0.4;
                bool isPetal = region >= 0.4 && region < 0.75;
                bool isHeart = region >= 0.75;
                float openAmount = smoothstep(0.05, 0.85, bloom);

                // the dandelion's filaments dissolve towards their ends (dither); a closed bud of them is solid
                if (species > 0.5 && species < 1.5 && isPetal)
                {
                    float feather = lerp(1.0, alpha, openAmount);
                    clip(feather - InterleavedNoise(input.positionCS.xy) * 0.98);
                }

                float3 toViewer = _WorldSpaceCameraPos - input.positionWS;
                float viewDistance = length(toViewer);
                float3 V = toViewer / viewDistance;
                float3 N = normalize(input.normalWS);
                N = dot(N, V) < 0.0 ? -N : N;   // both sides of a petal are lit

                Light sun = GetMainLight();
                float3 L = sun.direction;
                float sunPower = max(max(sun.color.r, sun.color.g), sun.color.b);
                float depth = input.positionWS.z - _WorldSpaceCameraPos.z;

                // ---- colour ----
                float3 stemGreen = _StemColor.rgb;
                float3 albedo;
                float petalEdge = 0.0;
                if (isStem)
                {
                    albedo = lerp(stemGreen * 0.5, stemGreen * 1.1, gradient);
                }
                else if (isSepal)
                {
                    albedo = lerp(stemGreen * 0.7, stemGreen * 1.35, gradient);
                }
                else if (isHeart)
                {
                    float angle = alpha;
                    float ring = smoothstep(0.55, 0.72, gradient) * (1.0 - smoothstep(0.93, 1.0, gradient));
                    float dots = pow(saturate(cos(angle * TwoPi * 12.0) * 0.5 + 0.5), 5.0);
                    albedo = lerp(_CenterColor.rgb, float3(0.95, 0.75, 0.25) * 0.8, ring * dots);
                }
                else
                {
                    float3 petal = lerp(_BudColor.rgb, _OpenColor.rgb, openAmount);
                    float luma = dot(petal, float3(0.2126, 0.7152, 0.0722));
                    petal = lerp(luma.xxx, petal, 0.82);                                          // a little desaturated: no plastic
                    float baseDark = lerp(0.38, 1.0, smoothstep(0.0, 0.7, gradient));              // darker base, lighter tip
                    petal *= baseDark * lerp(0.86, 1.14, tint);
                    if (species < 0.5 || species > 1.5)
                    {
                        petalEdge = pow(abs(alpha * 2.0 - 1.0), 3.0);                              // across the petal: 1 at the two edges
                        float veins = 1.0 - 0.18 * pow(abs(sin(alpha * 3.14159 * (species > 1.5 ? 7.0 : 5.0))), 2.0) * (1.0 - 0.5 * gradient);
                        petal *= veins;
                        petal += petal * petalEdge * 0.22 * openAmount;                           // a lighter edge
                    }
                    if (species > 1.5) petal = lerp(_CenterColor.rgb * 1.5, petal, smoothstep(0.03, 0.4, gradient) * 0.85 + 0.15 * (1.0 - openAmount)); // the dark blotch at the base of a poppy petal
                    albedo = petal;
                }

                // ---- light ----
                float ambientLuma = dot(_MeadowAmbientColor.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 ambient = lerp(ambientLuma.xxx, _MeadowAmbientColor.rgb, 0.4) * (0.6 + 0.4 * N.y) * _FlowerLight.x;
                float wrap = saturate((dot(N, L) + 0.6) / 1.6);
                float3 bodySun = lerp(sunPower.xxx, sun.color, 0.5);
                float lightOcclusion = isStem ? lerp(0.12, 1.0, pow(saturate(gradient * 1.3), 1.4)) : (isSepal ? lerp(0.45, 1.0, gradient) : occlusion);
                float dim = lerp(1.0, _MeadowGrassDim.x, smoothstep(_MeadowGrassDim.y, _MeadowGrassDim.z, depth));
                dim *= lerp(_MeadowGrassDim.w, 1.0, smoothstep(_MeadowGrassDim2.x, _MeadowGrassDim2.y, depth));
                float3 color = albedo * lightOcclusion * (ambient + bodySun * (_FlowerLight.y * wrap)) * dim;

                // the sun shines through the petals, the sepals and the filaments towards the viewer: strongest in the sun's column of the screen; HDR on the open ones
                float towardSun = saturate(dot(-V, L));
                float column = pow(towardSun, max(_MeadowGrassDim2.z * 0.7, 1.0));
                float thin = (0.4 + 0.6 * saturate(dot(-N, L)));
                float3 through = sun.color * saturate(albedo * 1.2);
                float edgeMask = lerp(0.25, 1.0, smoothstep(0.25, 1.0, gradient)) + petalEdge * 0.8;
                if (isPetal) color += through * (column * thin * _FlowerLight.z * edgeMask * lerp(0.5, 1.6, openAmount) * dim);
                else if (isSepal) color += through * (column * thin * _FlowerLight.z * 0.35 * smoothstep(0.3, 1.0, gradient) * dim);
                else if (isStem) color += through * (column * 0.25 * smoothstep(0.55, 1.0, gradient) * dim);

                // a soft rim in the colour of the petal and the sun
                float rim = pow(1.0 - saturate(dot(N, V)), 2.5);
                if (isPetal) color += albedo * sun.color * (rim * 0.3 * dim);

                // a glow of their own: a hint in the quiet state, a little more when open
                if (isPetal) color += albedo * lerp(_FlowerParams.z, _FlowerParams.w, openAmount) * (0.35 + 0.65 * gradient) * (0.5 + 0.3 * sunPower) * dim;
                if (!isStem && !isSepal) color += albedo * saturate(input.wind.y) * 1.5;

                color *= input.wind.z;   // the shadow of a cloud deck sliding over the field
                color = MeadowApplyHaze(color, MeadowScreenUV(input.clipXYW));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
