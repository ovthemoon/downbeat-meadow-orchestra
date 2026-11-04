// Real-time grass: every blade is an instance of a small ribbon mesh; its place, size, clump shade, lean and hue come from a structured buffer that
// GrassPlacement.compute filled once. Bending (breeze, wind waves, hands) is done in the vertex shader.
// Lighting is a backlit dusk: near-black roots, saturated green bodies lit by the sky and a little by the low sun, and warm tips that glow where the sun shines through
// them towards the viewer: strongest in the screen column of the sun, weaker to the sides, in the colour of the sun of the current sky stop (so cool and faint at blue hour,
// amber at dusk, bright enough for the bloom at the climax). The blade colours are only a tint of the light, they do not override it.
// The same haze as the ground: the far grass dissolves into the backdrop plate.
Shader "Downbeat/Meadow/Grass"
{
    Properties
    {
        _WidthScale ("Width scale of this band", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Grass"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "MeadowCommon.hlsl"
            #include "MeadowWind.hlsl"

            struct Blade
            {
                float3 pos;
                float yaw;
                float height;
                float width;
                float tint;
                float phase;
                float shade;
                float hue;
                float2 lean;
            };

            StructuredBuffer<Blade> _Blades;
            float _MeadowTipRim;   // meadow.grass.tipRim: the strength of the rim on the tips when the sun is low (0 = none; always none at a weak tier)

            CBUFFER_START(UnityPerMaterial)
                float _WidthScale;
                float _RingDark;
                float4 _RootColor;
                float4 _MidColor;
                float4 _TipColor;
                float4 _YellowColor;
                float4 _DriedColor;
                float4 _BacklightColor;
                float4 _GlowColor;
                float4 _GrassParams;   // x backlight, y rim, z sparkle, w tipGlow
                float4 _GrassParams2;  // x sunBody, y ambientMix, z sunColumn
                float4 _GrassParams3;  // x nearDarkness, y nearDarkEnd, z nearDarkStart (metres in front of the viewer)
                float4 _GrassParams4;  // x farDim, y farDimFrom, z farDimTo (metres in front of the viewer)
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // x: side (-1..1), y: height along the blade (0..1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 clipXYW : TEXCOORD2;
                float4 shade : TEXCOORD3;       // x: t along the blade, y: side, z: tint, w: phase
                float4 wind : TEXCOORD4;        // x: wave strength (brightening), y: glow, z: gust over the blade (brightens the tips), w: cloud-shadow light factor
                float2 ground : TEXCOORD5;      // x: how much the ground under the blade faces the sun, y: hue
                float3 widthAxis : TEXCOORD6;   // across the blade: its normal is tilted along it, so one edge of a blade catches the light
                float life : TEXCOORD7;         // the gusts and cloud shadows of the painted far meadow at this vertex's picture position (the haze fades the grass into it)
            };

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Blade b = _Blades[instanceID];
                float side = input.positionOS.x;
                float t = input.positionOS.y;

                float sy, cy;
                sincos(b.yaw, sy, cy);
                float3 widthAxis = float3(cy, 0, sy);
                // long and narrow: a little narrower at the foot, widest in the lower third, tapering to a soft tip
                float taper = (0.5 + 0.5 * smoothstep(0.0, 0.25, t)) * pow(saturate(1.0 - smoothstep(0.3, 1.0, t)), 0.8);
                float halfWidth = 0.5 * b.width * _WidthScale * taper;

                // a blade leans with its clump and bends with whatever blows
                float4 field = MeadowBreezeField(b.pos.xz);   // the gusts and the cloud shadow at the foot of this blade
                float gust;
                float4 wind = MeadowWindAt(b.pos.xz, b.phase, t, field, gust);   // the same for every blade near this one: only the blade's stiffness and its tip's phase are its own
                float2 bendPerHeight = b.lean + wind.xy * _MeadowWindA.y;   // tip displacement as a fraction of the height
                float3 tangent;
                float3 curve = MeadowBladeCurve(bendPerHeight, t, b.height, tangent);   // a circular arc as long as the blade is: it bends and lies down, it never stretches

                float3 positionWS = b.pos + curve + widthAxis * side * halfWidth;

                float3 normal = normalize(cross(tangent, widthAxis));

                Varyings output;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normal;
                output.clipXYW = float3(output.positionCS.xy, output.positionCS.w);
                output.shade = float4(t, side, b.tint, b.phase);
                output.wind = float4(wind.w, wind.z, gust, MeadowCloudLight(field));
                output.ground = float2(b.shade, b.hue);
                output.widthAxis = widthAxis;
                output.life = MeadowPlateLife(MeadowScreenUV(output.clipXYW));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = input.shade.x;
                float side = input.shade.y;
                float tint = input.shade.z;
                float phase = input.shade.w;
                float hue = input.ground.y;

                float3 toViewer = _WorldSpaceCameraPos - input.positionWS;
                float viewDistance = length(toViewer);
                float3 V = toViewer / viewDistance;
                float3 N = normalize(input.normalWS);
                N = dot(N, V) < 0.0 ? -N : N;   // both faces of a blade are the same blade: light the one the viewer sees
                N = normalize(N + input.widthAxis * side * 0.9);   // slightly rounded across the width: each blade has a lit and a shaded edge

                Light sun = GetMainLight();
                float3 L = sun.direction;
                float sunPower = max(max(sun.color.r, sun.color.g), sun.color.b);

                // colours: deep green clumps, yellow-green clumps and a few dried ones; only a tint of the light
                float yellowAmount = saturate(hue / 0.7) * step(hue, 0.95);
                float driedAmount = step(0.95, hue);
                float3 midColor = lerp(_MidColor.rgb, _YellowColor.rgb, yellowAmount);
                float3 tipAlbedo = lerp(_TipColor.rgb, _YellowColor.rgb * 1.25, yellowAmount);
                float3 albedo = lerp(_RootColor.rgb, midColor, smoothstep(0.0, 0.5, t));
                albedo = lerp(albedo, tipAlbedo, smoothstep(0.55, 1.0, t));
                albedo = lerp(albedo, _DriedColor.rgb, driedAmount * smoothstep(0.05, 0.5, t));
                albedo *= tint;
                tipAlbedo = lerp(tipAlbedo, _DriedColor.rgb, driedAmount) * tint;

                // the body: near-black roots, sky light, a little of the low sun wrapped round the blade; slopes that face the sun are brighter
                float occlusion = lerp(0.03, 1.0, pow(saturate(t * 1.3), 1.4));
                // a green blade lit by violet light turns grey: keep only a third of the hue of the sky light on the body, the stop still sets how bright and how cool it is
                float ambientLuma = dot(_MeadowAmbientColor.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 ambient = lerp(ambientLuma.xxx, _MeadowAmbientColor.rgb, 0.35) * (0.6 + 0.4 * N.y) * _GrassParams2.y;
                float wrap = saturate((dot(N, L) + 0.6) / 1.6);
                float3 bodySun = lerp(sunPower.xxx, sun.color, 0.25);   // the low sun is orange, but the body of a green blade stays green; the warmth is in the tips
                float relief = 1.0 + 0.8 * input.ground.x;
                float farness = saturate(1.0 - V.y * 2.2);
                float depth = input.positionWS.z - _WorldSpaceCameraPos.z;   // metres in front of the viewer, along the ground
                float nearDark = lerp(_GrassParams3.x, 1.0, smoothstep(_GrassParams3.z, _GrassParams3.y, depth));   // the foreground is the darkest part of the picture
                float dim = _RingDark * lerp(1.0, _GrassParams4.x, smoothstep(_GrassParams4.y, _GrassParams4.z, depth));   // the middle distance is hazed by the plate's own mist
                float3 color = albedo * occlusion * (ambient + bodySun * (_GrassParams2.x * wrap)) * (dim * relief * nearDark);

                // the sun is behind the field: it shines through the blade towards the viewer, most at the thin tip, strongest in the sun's column of the screen
                float towardSun = saturate(dot(-V, L));
                float column = pow(towardSun, _GrassParams2.z);
                // the light that comes through is the sun of the current sky stop, coloured by the leaf it passes (a green leaf lets less red through, but a low orange sun stays amber)
                float3 through = sun.color * _BacklightColor.rgb * saturate(tipAlbedo * 1.6 + 0.45);
                float shine = column * (0.55 + 0.45 * saturate(dot(-N, L)));
                float tipZone = smoothstep(0.62, 1.0, t);
                color += through * (shine * _GrassParams.x * tipZone * tipZone * lerp(0.9, 1.0, farness) * nearDark * dim);
                color += through * (shine * pow(t, 12.0) * _GrassParams.w * tint * nearDark * dim);   // the bright end of the tips (HDR: the bloom catches it when the sun is strong)

                // the thin edges of a blade glow where the sun shines through
                float edgeLine = pow(abs(side), 4.0);
                color += through * (edgeLine * column * _GrassParams.y * smoothstep(0.3, 0.95, t) * lerp(0.9, 1.0, farness) * tint * nearDark * dim);

                // a subtle rim on the tips when the sun is low and behind the field: the edge of a blade, seen at a grazing angle, catches it. It is a function of the blade's own angle to the sun and
                // to the viewer only: no noise, nothing that changes from frame to frame by itself. Within the bloom limits (it stays under the threshold: tested) and not at a weak tier.
                if (_MeadowSkyCheap < 0.5 && _MeadowTipRim > 0.0)
                {
                    float lowSun = 1.0 - smoothstep(0.08, 0.45, L.y);
                    float grazing = pow(1.0 - saturate(dot(N, V)), 3.0);
                    color += through * (grazing * saturate(0.25 + towardSun) * smoothstep(0.75, 1.0, t) * lowSun * _MeadowTipRim * nearDark * dim);
                }

                // a few tips catch the sun like drops of dew
                float sparkle = step(0.97, frac(phase * 37.7 + tint * 11.3)) * pow(t, 14.0);
                color += sun.color * (sparkle * 1.2 * column * _MeadowWindMute.w);

                // wind waves brighten the grass they bend, and a harp note makes tips glint
                color *= 1.0 + saturate(input.wind.x * 3.0) * 1.2 * t;
                color *= max(0.0, 1.0 + input.wind.z * _BreezeGustC.y * t);   // a gust lifts the tips into the light
                color *= input.wind.w;                                          // the shadow of a cloud deck sliding over the field
                color += _GlowColor.rgb * (min(input.wind.y, 1.5) * pow(t, 7.0) * 1.0);

                color = MeadowApplyHazeLife(color, MeadowScreenUV(input.clipXYW), input.life);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
